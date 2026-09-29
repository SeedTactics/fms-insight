/* Copyright (c) 2026, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

import { act } from "react";
import { Provider, createStore } from "jotai";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, test, vi } from "vitest";

import {
  currentStatusIsLive,
  websocketReconnecting,
  WebsocketConnection,
  errorLoadingLast30,
} from "./websocket.js";
import {
  registerBackend,
  type FmsAPI,
  type JobAPI,
  type LogAPI,
  type MachineAPI,
} from "./backend.js";
import {
  CurrentStatus,
  InProcessMaterial,
  InProcessMaterialLocation,
  InProcessMaterialAction,
  ToolUse,
  NewJobs,
  LogEntry,
  LogMaterial,
  LogType,
  ServerEvent,
  type ICurrentStatus,
  type ILogEntry,
} from "./api.js";
import { fmsInformation } from "./server-settings.js";
import { currentStatus } from "../cell-status/current-status.js";
import { customState } from "../cell-status/custom-state.js";
import { lastEventCounter } from "../cell-status/loading.js";
import { last30ToolUse } from "../cell-status/tool-usage.js";
import { PartAndStationOperation } from "../cell-status/estimated-cycle-times.js";
import { last30MaterialSummary } from "../cell-status/material-summary.js";
import { last30SchIds } from "../cell-status/scheduled-jobs.js";

class FakeSocket {
  static readonly sockets: FakeSocket[] = [];
  private readonly listeners = new Map<string, Array<(evt: unknown) => void>>();
  constructor(readonly url: string) {
    FakeSocket.sockets.push(this);
  }
  addEventListener(type: string, listener: (evt: unknown) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }
  close() {
    this.emit("close");
  }
  emit(type: string, evt: unknown = {}) {
    for (const listener of this.listeners.get(type) ?? []) listener(evt);
  }
}

type Deferred<T> = { readonly promise: Promise<T>; resolve(v: T): void; reject(e: Error): void };
function deferred<T>(): Deferred<T> {
  let resolve!: (v: T) => void;
  let reject!: (e: Error) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

function status(label = "status"): ICurrentStatus {
  return new CurrentStatus({
    timeOfCurrentStatusUTC: new Date(),
    jobs: {},
    pallets: {},
    material: [],
    alarms: [label],
    queues: {},
    customState: { label },
  });
}

function statusMessage(label: string) {
  const evt = new ServerEvent({ newCurrentStatus: new CurrentStatus(status(label)) });
  return { data: JSON.stringify(evt.toJSON()) };
}

function machined(materialID: number): ILogEntry {
  return new LogEntry({
    counter: materialID,
    material: [
      new LogMaterial({ id: materialID, uniq: "u", part: "p", proc: 1, numproc: 1, face: 1 }),
    ],
    type: LogType.MachineCycle,
    startofcycle: false,
    endUTC: new Date(),
    loc: "MC",
    locnum: 1,
    pal: 1,
    program: "prog",
    result: "",
    elapsed: "00:10:00",
    active: "00:10:00",
  });
}

function logMessage(entry: ILogEntry) {
  return { data: JSON.stringify(new ServerEvent({ logEntry: new LogEntry(entry) }).toJSON()) };
}

function assigned(counter: number): ILogEntry {
  return { ...machined(100), counter, type: LogType.OrderAssignment, result: `order-${counter}` };
}

function toolCycle(counter: number): ILogEntry {
  return {
    ...machined(counter),
    tooluse: [new ToolUse({ tool: "drill", pocket: 1, toolUseCountDuringCycle: counter })],
  };
}

function statusWithMaterial(workorderId: string): ICurrentStatus {
  return {
    ...status(),
    material: [
      new InProcessMaterial({
        materialID: 100,
        jobUnique: "u",
        partName: "p",
        process: 1,
        path: 1,
        workorderId,
        signaledInspections: [],
        location: new InProcessMaterialLocation(),
        action: new InProcessMaterialAction(),
      }),
    ],
  };
}

async function reconnect(socket: number) {
  await settle(() => FakeSocket.sockets[socket - 1]?.close());
  await settle(() => vi.advanceTimersByTime(1000));
  expect(FakeSocket.sockets).toHaveLength(socket + 1);
  await settle(() => FakeSocket.sockets[socket]?.emit("open"));
}

// Backend calls not listed fail the test.
function only<T extends object>(calls: Partial<T>): T {
  // The proxy supplies a throwing implementation for every omitted backend method.
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return new Proxy(calls, {
    get: (target, prop) =>
      prop in target
        ? (target as Record<string | symbol, unknown>)[prop]
        : () => {
            throw new Error(`Unexpected backend call ${String(prop)}`);
          },
  }) as T;
}

let statusLoads: Deferred<Readonly<ICurrentStatus>>[];
let logLoads: Deferred<ReadonlyArray<Readonly<ILogEntry>>>[];
let recentFrom: number[];
let root: Root;
let mounted: boolean;

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  FakeSocket.sockets.length = 0;
  vi.stubGlobal("WebSocket", FakeSocket);
  statusLoads = [];
  logLoads = [];
  recentFrom = [];
  const logLoad = () => {
    const load = deferred<ReadonlyArray<Readonly<ILogEntry>>>();
    logLoads.push(load);
    return load.promise;
  };
  registerBackend(
    only<LogAPI>({
      get: logLoad,
      recent: (from: number) => {
        recentFrom.push(from);
        return logLoad();
      },
    }),
    only<JobAPI>({
      currentStatus: () => {
        const load = deferred<Readonly<ICurrentStatus>>();
        statusLoads.push(load);
        return load.promise;
      },
      recent: () => Promise.resolve({ jobs: {}, stationUse: [] }),
    }),
    only<FmsAPI>({}),
    only<MachineAPI>({}),
  );
});

afterEach(async () => {
  if (mounted) await act(async () => root.unmount());
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

async function connect() {
  const store = createStore();
  root = createRoot(document.createElement("div"));
  mounted = true;
  await act(async () => {
    root.render(
      <Provider store={store}>
        <WebsocketConnection />
      </Provider>,
    );
  });
  const live = () => ({
    live: store.get(currentStatusIsLive),
    reconnecting: store.get(websocketReconnecting),
  });
  const installed = () => ({
    alarms: store.get(currentStatus).alarms,
    custom: store.get(customState),
  });
  const unmount = () =>
    act(async () => {
      root.unmount();
      mounted = false;
    });
  return { store, live, installed, unmount };
}

async function settle(action: () => void = () => {}) {
  await act(async () => {
    action();
    await vi.advanceTimersByTimeAsync(0);
  });
}

test("a status load from a closed connection does not mark the retained status live", async () => {
  const { live } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => FakeSocket.sockets[0]?.close());
  await settle(() => {
    statusLoads[0]?.resolve(status());
    logLoads[0]?.resolve([]);
  });
  expect(live()).toEqual({ live: false, reconnecting: true });
});

test("a failed status load does not certify the retained status", async () => {
  const { live } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => {
    statusLoads[0]?.reject(new Error("status unavailable"));
    logLoads[0]?.resolve([]);
  });
  expect(live()).toEqual({ live: false, reconnecting: false });
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("pushed")));
  expect(live().live).toBe(true);
});

test("a failed history load does not affect a live status", async () => {
  const { live } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => {
    statusLoads[0]?.resolve(status());
    logLoads[0]?.reject(new Error("log unavailable"));
  });
  expect(live()).toEqual({ live: true, reconnecting: false });
});

test("a bootstrap status response does not replace a newer status from the socket", async () => {
  const { live, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("pushed")));
  await settle(() => {
    statusLoads[0]?.resolve(status("bootstrap"));
    logLoads[0]?.resolve([]);
  });
  expect(installed()).toEqual({ alarms: ["pushed"], custom: { label: "pushed" } });
  expect(live().live).toBe(true);
});

test("a failed bootstrap status after a socket status keeps it live", async () => {
  const { live, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("pushed")));
  await settle(() => {
    statusLoads[0]?.reject(new Error("status unavailable"));
    logLoads[0]?.resolve([]);
  });
  expect(installed()).toEqual({ alarms: ["pushed"], custom: { label: "pushed" } });
  expect(live().live).toBe(true);
});

test("history from an earlier connection cannot replace a later connection's", async () => {
  const { store } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => FakeSocket.sockets[0]?.close());
  await settle(() => vi.advanceTimersByTime(1000));
  await settle(() => FakeSocket.sockets[1]?.emit("open"));
  await settle(() => logLoads[1]?.resolve([machined(1)]));
  await settle(() => logLoads[0]?.resolve([machined(99)]));
  const matsById = store.get(last30MaterialSummary).matsById;
  expect(matsById.has(1)).toBe(true);
  expect(matsById.has(99)).toBe(false);
});

test("disposing the connection ends its session", async () => {
  const { live, installed, unmount } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("pushed")));
  expect(live().live).toBe(true);
  await unmount();
  expect(live().live).toBe(false);
  await settle(() => statusLoads[0]?.resolve(status("disposed")));
  expect(live().live).toBe(false);
  expect(installed().alarms).toEqual(["pushed"]);
});

test("a replaced connection cannot change the new connection's state", async () => {
  const { store, live, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => store.set(fmsInformation, { name: "FMS Insight", version: "replaced" }));
  await settle(() => FakeSocket.sockets[1]?.emit("open"));
  await settle(() => statusLoads[1]?.resolve(status("current")));
  expect(installed().alarms).toEqual(["current"]);
  expect(live().live).toBe(true);
  await settle(() => {
    FakeSocket.sockets[0]?.emit("message", statusMessage("stale"));
    statusLoads[0]?.resolve(status("stale"));
    FakeSocket.sockets[0]?.emit("close");
  });
  expect(installed()).toEqual({ alarms: ["current"], custom: { label: "current" } });
  expect(live().live).toBe(true);
});

test("an interrupted history catch-up resumes from where it started", async () => {
  const { store } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => logLoads[0]?.resolve([machined(100)]));
  await reconnect(1);
  // A live event arrives while the catch-up from 100 is still pending.
  const live = new ServerEvent({ logEntry: new LogEntry(machined(120)) });
  await settle(() =>
    FakeSocket.sockets[1]?.emit("message", { data: JSON.stringify(live.toJSON()) }),
  );
  expect(store.get(lastEventCounter)).toBe(100);
  expect(store.get(last30MaterialSummary).matsById.has(120)).toBe(false);
  await reconnect(2);
  expect(recentFrom).toEqual([100, 100]);
  await settle(() => logLoads[1]?.resolve([machined(110), machined(120)]));
  await settle(() => logLoads[2]?.resolve([machined(110), machined(120)]));
  expect(store.get(last30MaterialSummary).matsById.has(110)).toBe(true);
  await reconnect(3);
  expect(recentFrom).toEqual([100, 100, 120]);
});

for (const loading of ["initial", "reconnect"] as const) {
  test(`${loading} history is applied before buffered live assignments without replaying current status`, async () => {
    const { store, live } = await connect();
    await settle(() => FakeSocket.sockets[0]?.emit("open"));
    const socket = loading === "initial" ? 0 : 1;
    if (socket === 1) {
      await settle(() => logLoads[0]?.resolve([assigned(100)]));
      await reconnect(1);
    }
    await settle(() => statusLoads[socket]?.resolve(statusWithMaterial("before")));
    await settle(() => FakeSocket.sockets[socket]?.emit("message", logMessage(assigned(120))));
    expect(store.get(currentStatus).material[0]?.workorderId).toBe("order-120");
    expect(store.get(last30MaterialSummary).matsById.get(100)?.workorderId).toBe(
      socket === 0 ? undefined : "order-100",
    );
    expect(store.get(lastEventCounter)).toBe(socket === 0 ? null : 100);
    expect(live().live).toBe(true);

    const newerStatus = new ServerEvent({
      newCurrentStatus: new CurrentStatus(statusWithMaterial("newer snapshot")),
    });
    await settle(() =>
      FakeSocket.sockets[socket]?.emit("message", { data: JSON.stringify(newerStatus.toJSON()) }),
    );
    await settle(() => logLoads[socket]?.resolve([assigned(110)]));
    expect(store.get(last30MaterialSummary).matsById.get(100)?.workorderId).toBe("order-120");
    expect(store.get(lastEventCounter)).toBe(120);
    expect(store.get(currentStatus).material[0]?.workorderId).toBe("newer snapshot");

    await settle(() => FakeSocket.sockets[socket]?.emit("message", logMessage(assigned(130))));
    expect(store.get(last30MaterialSummary).matsById.get(100)?.workorderId).toBe("order-130");
  });

  test(`${loading} history deduplicates tool samples including socket delivery after HTTP completes`, async () => {
    const { store } = await connect();
    await settle(() => FakeSocket.sockets[0]?.emit("open"));
    const socket = loading === "initial" ? 0 : 1;
    if (socket === 1) {
      await settle(() => logLoads[0]?.resolve([machined(100)]));
      await reconnect(1);
    }
    const samples = () =>
      store
        .get(last30ToolUse)
        .get(PartAndStationOperation.ofLogCycle(toolCycle(110)))
        ?.recentCycles.map((cycle) => cycle.tools[0]?.cycleUsageCnt);
    await settle(() => FakeSocket.sockets[socket]?.emit("message", logMessage(toolCycle(120))));
    expect(samples()).toBeUndefined();
    await settle(() => logLoads[socket]?.resolve([toolCycle(110), toolCycle(120)]));
    expect(samples()).toEqual([110, 120]);
    await settle(() => FakeSocket.sockets[socket]?.emit("message", logMessage(toolCycle(110))));
    expect(samples()).toEqual([110, 120]);
    // A lower counter absent from the HTTP response must still be accepted.
    await settle(() => FakeSocket.sockets[socket]?.emit("message", logMessage(toolCycle(115))));
    expect(samples()).toEqual([110, 120, 115]);
  });

  test(`${loading} history retries a failed request from the same boundary while status stays live`, async () => {
    const { store, live } = await connect();
    await settle(() => FakeSocket.sockets[0]?.emit("open"));
    const socket = loading === "initial" ? 0 : 1;
    if (socket === 1) {
      await settle(() => logLoads[0]?.resolve([machined(100)]));
      await reconnect(1);
    }
    await settle(() => {
      statusLoads[socket]?.resolve(status());
      FakeSocket.sockets[socket]?.emit("message", logMessage(toolCycle(120)));
      logLoads[socket]?.reject(new Error("log unavailable"));
    });
    expect(live().live).toBe(true);
    expect(store.get(errorLoadingLast30)).toBe("log unavailable");
    expect(store.get(lastEventCounter)).toBe(socket === 0 ? null : 100);
    await settle(() => vi.advanceTimersByTime(1000));
    expect(logLoads).toHaveLength(socket + 2);
    expect(recentFrom).toEqual(socket === 0 ? [] : [100, 100]);
    await settle(() => logLoads[socket + 1]?.resolve([toolCycle(110), toolCycle(120)]));
    expect(store.get(errorLoadingLast30)).toBeNull();
    expect(store.get(lastEventCounter)).toBe(120);
    expect(
      store
        .get(last30ToolUse)
        .get(PartAndStationOperation.ofLogCycle(toolCycle(110)))
        ?.recentCycles.map((cycle) => cycle.tools[0]?.cycleUsageCnt),
    ).toEqual([110, 120]);
  });
}

test("an interrupted initial load discards unapplied samples and reloads initial history", async () => {
  const { store } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => FakeSocket.sockets[0]?.emit("message", logMessage(toolCycle(120))));
  await reconnect(1);
  expect(recentFrom).toEqual([]);
  await settle(() => logLoads[0]?.resolve([toolCycle(110), toolCycle(120)]));
  expect(store.get(lastEventCounter)).toBeNull();
  await settle(() => logLoads[1]?.resolve([toolCycle(110), toolCycle(120)]));
  expect(
    store
      .get(last30ToolUse)
      .get(PartAndStationOperation.ofLogCycle(toolCycle(110)))
      ?.recentCycles.map((cycle) => cycle.tools[0]?.cycleUsageCnt),
  ).toEqual([110, 120]);
});

test("a failed history load does not retry after its session closes", async () => {
  const { unmount } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => logLoads[0]?.reject(new Error("log unavailable")));
  await unmount();
  await settle(() => vi.advanceTimersByTime(30000));
  expect(logLoads).toHaveLength(1);
});

test("live history advances the reconnect cursor after an empty initial load", async () => {
  const { store } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => logLoads[0]?.resolve([]));
  await settle(() => FakeSocket.sockets[0]?.emit("message", logMessage(assigned(120))));
  expect(store.get(lastEventCounter)).toBe(120);
  await reconnect(1);
  expect(recentFrom).toEqual([120]);
});

test("new jobs arrive immediately while log history is pending", async () => {
  const { store } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  const jobs = new ServerEvent({ newJobs: new NewJobs({ scheduleId: "new schedule", jobs: [] }) });
  await settle(() =>
    FakeSocket.sockets[0]?.emit("message", { data: JSON.stringify(jobs.toJSON()) }),
  );
  expect(store.get(last30SchIds).has("new schedule")).toBe(true);
  expect(store.get(lastEventCounter)).toBeNull();
});

test("a successful log retry does not clear an independent status error", async () => {
  const { store, live } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => {
    statusLoads[0]?.reject(new Error("status unavailable"));
    logLoads[0]?.reject(new Error("log unavailable"));
  });
  await settle(() => vi.advanceTimersByTime(1000));
  await settle(() => logLoads[1]?.resolve([machined(100)]));
  expect(store.get(errorLoadingLast30)).toBe("status unavailable");
  expect(live().live).toBe(false);
});
