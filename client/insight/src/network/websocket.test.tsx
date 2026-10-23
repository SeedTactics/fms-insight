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
  refreshCurrentStatus,
} from "./websocket.js";
import {
  registerBackend,
  LogBackend,
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
  ActionType,
  LocType,
  ToolUse,
  NewJobs,
  LogEntry,
  LogClient,
  LogMaterial,
  LogType,
  ServerEvent,
  type ICurrentStatus,
  type IInProcessMaterial,
  type ILogEntry,
} from "./api.js";
import { fmsInformation } from "./server-settings.js";
import { currentStatus } from "../cell-status/current-status.js";
import { moveQueuedMatInCurrentStatus } from "../cell-status/loading.js";
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

let nextStatusRevision = 0;

function status(label = "status", revision = ++nextStatusRevision): ICurrentStatus {
  return new CurrentStatus({
    revision,
    timeOfCurrentStatusUTC: new Date(),
    jobs: {},
    pallets: {},
    material: [],
    alarms: [label],
    queues: {},
    customState: { label },
  });
}

function statusMessage(label: string, revision?: number) {
  const evt = new ServerEvent({ newCurrentStatus: new CurrentStatus(status(label, revision)) });
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

function statusWithMaterial(
  workorderId: string,
  location = new InProcessMaterialLocation(),
): ICurrentStatus {
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
        location,
        action: new InProcessMaterialAction(),
      }),
    ],
  };
}

function queuedStatus(label: string, revision: number): ICurrentStatus {
  return {
    ...status(label, revision),
    material: statusWithMaterial(
      label,
      new InProcessMaterialLocation({
        type: LocType.InQueue,
        currentQueue: "Queue A",
        queuePosition: 0,
      }),
    ).material,
  };
}

function rejectedQueueMove(store: ReturnType<typeof createStore>) {
  return store
    .set(moveQueuedMatInCurrentStatus, {
      queue: "Queue B",
      matId: 100,
      newIdx: 0,
      operator: null,
    })
    .catch((error: unknown) => error);
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
  const backend = new Proxy(calls, {
    get: (target, prop) =>
      prop in target
        ? (target as Record<string | symbol, unknown>)[prop]
        : () => {
            throw new Error(`Unexpected backend call ${String(prop)}`);
          },
  });
  // oxlint-disable-next-line typescript/no-unsafe-type-assertion
  return backend as T;
}

let statusLoads: Deferred<Readonly<ICurrentStatus>>[];
let logLoads: Array<
  Deferred<ReadonlyArray<Readonly<ILogEntry>>> & { readonly signal?: AbortSignal }
>;
let recentFrom: number[];
let root: Root;
let mounted: boolean;

beforeEach(() => {
  nextStatusRevision = 0;
  vi.useFakeTimers();
  vi.spyOn(AbortSignal, "timeout").mockImplementation((milliseconds) => {
    const controller = new AbortController();
    setTimeout(
      () => controller.abort(new DOMException("The operation timed out.", "TimeoutError")),
      milliseconds,
    );
    return controller.signal;
  });
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  FakeSocket.sockets.length = 0;
  vi.stubGlobal("WebSocket", FakeSocket);
  statusLoads = [];
  logLoads = [];
  recentFrom = [];
  const logLoad = (signal?: AbortSignal) => {
    const load = { ...deferred<ReadonlyArray<Readonly<ILogEntry>>>(), signal };
    logLoads.push(load);
    signal?.addEventListener("abort", () => load.reject(signal.reason), { once: true });
    return load.promise;
  };
  registerBackend(
    only<LogAPI>({
      get: (_start, _end, signal) => logLoad(signal),
      recent: (from: number, _expected: Date | null | undefined, signal?: AbortSignal) => {
        recentFrom.push(from);
        return logLoad(signal);
      },
    }),
    only<JobAPI>({
      setMaterialInQueue: () => Promise.reject(new Error("queue rejected")),
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
  vi.restoreAllMocks();
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

test("a failed bootstrap status retries without a websocket status event", async () => {
  const { live, installed, store } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => {
    statusLoads[0]?.reject(new Error("temporary status failure"));
    logLoads[0]?.resolve([]);
  });
  expect(live().live).toBe(false);
  await settle(() => vi.advanceTimersByTime(1000));
  expect(statusLoads).toHaveLength(2);
  await settle(() => statusLoads[1]?.resolve(status("recovered")));
  expect(installed().alarms).toEqual(["recovered"]);
  expect(live().live).toBe(true);
  expect(store.get(errorLoadingLast30)).toBeNull();
});

test.each(["close", "dispose", "socket status"] as const)(
  "bootstrap retries stop after %s",
  async (ending) => {
    const { unmount } = await connect();
    await settle(() => FakeSocket.sockets[0]?.emit("open"));
    await settle(() => statusLoads[0]?.reject(new Error("temporary failure")));
    if (ending === "dispose") await unmount();
    else
      await settle(() => {
        if (ending === "close") FakeSocket.sockets[0]?.close();
        else FakeSocket.sockets[0]?.emit("message", statusMessage("recovered"));
      });
    await settle(() => vi.advanceTimersByTime(1000));
    expect(statusLoads).toHaveLength(1);
  },
);

test("explicit refresh works after a previous websocket status", async () => {
  const { store, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("before control")));
  const refreshed = store.set(refreshCurrentStatus);
  await settle(() => statusLoads[1]?.resolve(status("after control")));
  expect(await refreshed).toBe(true);
  expect(installed().alarms).toEqual(["after control"]);
});

test("server revisions let a newer HTTP snapshot replace an intervening socket snapshot", async () => {
  const { store, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("socket", 41)));
  await settle(() => statusLoads[0]?.resolve(status("newer HTTP", 42)));
  expect(installed()).toEqual({ alarms: ["newer HTTP"], custom: { label: "newer HTTP" } });
  expect(store.get(currentStatus).revision).toBe(42);
  expect(statusLoads).toHaveLength(1);
});

test("older HTTP responses and delayed or duplicate socket snapshots cannot replace newer state", async () => {
  const { store, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => statusLoads[0]?.resolve(status("initial", 40)));
  const refreshed = store.set(refreshCurrentStatus);
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("newer", 42)));
  await settle(() => statusLoads[1]?.resolve(status("old HTTP", 41)));
  expect(await refreshed).toBe(true);
  await settle(() => {
    FakeSocket.sockets[0]?.emit("message", statusMessage("old socket", 41));
    FakeSocket.sockets[0]?.emit("message", statusMessage("duplicate socket", 42));
  });
  expect(installed()).toEqual({ alarms: ["newer"], custom: { label: "newer" } });
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("latest", 43)));
  expect(installed().alarms).toEqual(["latest"]);
});

test("queue rejection recovery orders subsequent socket snapshots against the recovered revision", async () => {
  const { store, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => statusLoads[0]?.resolve(queuedStatus("initial", 40)));
  const recovery = rejectedQueueMove(store);
  await settle();
  expect(statusLoads).toHaveLength(2);
  await settle(() => statusLoads[1]?.resolve(queuedStatus("recovered", 42)));
  expect(await recovery).toMatchObject({ message: "queue rejected" });
  expect(store.get(currentStatus).revision).toBe(42);

  await settle(() => {
    FakeSocket.sockets[0]?.emit("message", statusMessage("delayed", 41));
    FakeSocket.sockets[0]?.emit("message", statusMessage("duplicate", 42));
  });
  expect(store.get(currentStatus).revision).toBe(42);
  expect(installed().custom).toEqual({ label: "recovered" });
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("latest", 43)));
  expect(installed().custom).toEqual({ label: "latest" });
});

test.each(["success", "failure"] as const)(
  "an old queue recovery %s cannot install or roll back status in a new connection",
  async (outcome) => {
    const { store, live } = await connect();
    await settle(() => FakeSocket.sockets[0]?.emit("open"));
    await settle(() => statusLoads[0]?.resolve(queuedStatus("initial", 40)));
    const recovery = rejectedQueueMove(store);
    await settle();
    const optimistic = store.get(currentStatus);
    expect(optimistic.material[0]?.location.currentQueue).toBe("Queue B");
    await reconnect(1);
    await settle(() => {
      if (outcome === "success") statusLoads[1]?.resolve(queuedStatus("old session", 42));
      else statusLoads[1]?.reject(new Error("offline"));
    });
    expect(await recovery).toMatchObject({ message: "queue rejected" });
    expect(store.get(currentStatus)).toBe(optimistic);
    expect(live().live).toBe(false);
    await settle(() => statusLoads[2]?.resolve(queuedStatus("new session", 1)));
    expect(store.get(currentStatus).revision).toBe(1);
    expect(live().live).toBe(true);
  },
);

test.each([true, false])(
  "concurrent HTTP reads follow capture revisions (newer response first: %s)",
  async (newerFirst) => {
    const { store, installed } = await connect();
    await settle(() => FakeSocket.sockets[0]?.emit("open"));
    await settle(() => statusLoads[0]?.resolve(status("initial", 40)));
    const first = store.set(refreshCurrentStatus);
    const second = store.set(refreshCurrentStatus);
    const newer = () => statusLoads[1]?.resolve(status("newer", 42));
    const older = () => statusLoads[2]?.resolve(status("older", 41));
    await settle(newerFirst ? newer : older);
    await settle(newerFirst ? older : newer);
    expect(await first).toBe(true);
    expect(await second).toBe(true);
    expect(installed().alarms).toEqual(["newer"]);
    expect(statusLoads).toHaveLength(3);
  },
);

test("reconnect resets server revision comparison and rejects a late read from the old session", async () => {
  const { store, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => statusLoads[0]?.resolve(status("before restart", 100)));
  const oldRead = store.set(refreshCurrentStatus);
  await reconnect(1);
  await settle(() => statusLoads[2]?.resolve(status("restarted", 1)));
  await settle(() => statusLoads[1]?.resolve(status("old process", 101)));
  expect(await oldRead).toBe(false);
  expect(installed().alarms).toEqual(["restarted"]);
  expect(store.get(currentStatus).revision).toBe(1);
});

test.each([true, false])(
  "a higher snapshot revision preserves log edits (intervening full snapshot: %s)",
  async (interveningSnapshot) => {
    const { store } = await connect();
    await settle(() => FakeSocket.sockets[0]?.emit("open"));
    await settle(() => statusLoads[0]?.resolve({ ...statusWithMaterial("initial"), revision: 40 }));
    const refreshed = store.set(refreshCurrentStatus);
    if (interveningSnapshot)
      await settle(() =>
        FakeSocket.sockets[0]?.emit("message", {
          data: JSON.stringify(
            new ServerEvent({
              newCurrentStatus: new CurrentStatus({
                ...statusWithMaterial("socket"),
                revision: 41,
              }),
            }).toJSON(),
          ),
        }),
      );
    await settle(() => FakeSocket.sockets[0]?.emit("message", logMessage(assigned(120))));
    await settle(() => statusLoads[1]?.resolve({ ...statusWithMaterial("old"), revision: 42 }));
    expect(store.get(currentStatus).material[0]?.workorderId).toBe("order-120");
    expect(statusLoads).toHaveLength(3);
    await settle(() =>
      statusLoads[2]?.resolve({ ...statusWithMaterial("order-120"), revision: 43 }),
    );
    expect(await refreshed).toBe(true);
    expect(store.get(currentStatus).revision).toBe(43);
  },
);

test("a cancelled refresh cannot install a late result", async () => {
  const { store, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => statusLoads[0]?.resolve(status("initial")));
  const lifetime = new AbortController();
  const refreshed = store.set(refreshCurrentStatus, lifetime.signal);
  const rejected = expect(refreshed).rejects.toMatchObject({ name: "AbortError" });
  lifetime.abort();
  await settle(() => statusLoads[1]?.resolve(status("cancelled")));
  await rejected;
  expect(installed().alarms).toEqual(["initial"]);
});

test("a post-control refresh accepts its newer capture after an older bootstrap completes", async () => {
  const { store, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  const refreshed = store.set(refreshCurrentStatus);
  await settle(() => statusLoads[0]?.resolve(status("before control", 1)));
  expect(installed().alarms).toEqual(["before control"]);
  await settle(() => statusLoads[1]?.resolve(status("after control", 2)));
  expect(await refreshed).toBe(true);
  expect(statusLoads).toHaveLength(2);
  expect(installed().alarms).toEqual(["after control"]);
});

test("an explicit refresh yields to a newer socket status and ends with its session", async () => {
  const { store, live, installed } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => statusLoads[0]?.resolve(status("initial")));
  const refreshed = store.set(refreshCurrentStatus);
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("newer", 3)));
  await settle(() => statusLoads[1]?.resolve(status("late read", 2)));
  expect(await refreshed).toBe(true);
  expect(installed().alarms).toEqual(["newer"]);
  const ended = store.set(refreshCurrentStatus);
  await settle(() => FakeSocket.sockets[0]?.close());
  await settle(() => statusLoads[2]?.resolve(status("closed session")));
  expect(await ended).toBe(false);
  expect(installed().alarms).toEqual(["newer"]);
  expect(live().live).toBe(false);
});

test("refresh preserves intervening log-derived state by reading again", async () => {
  const { store } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => statusLoads[0]?.resolve(statusWithMaterial("initial")));
  const refreshed = store.set(refreshCurrentStatus);
  await settle(() => FakeSocket.sockets[0]?.emit("message", logMessage(assigned(120))));
  await settle(() => statusLoads[1]?.resolve(statusWithMaterial("old")));
  expect(store.get(currentStatus).material[0]?.workorderId).toBe("order-120");
  expect(statusLoads).toHaveLength(3);
  await settle(() => statusLoads[2]?.resolve(statusWithMaterial("order-120")));
  expect(await refreshed).toBe(true);
});

test("live logs already reflected in status preserve anonymous targets and inspection uniqueness", async () => {
  const { store } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  const base = statusWithMaterial("order");
  const named: Readonly<IInProcessMaterial> | undefined = base.material[0];
  if (!named) throw new Error("Expected identified material");
  const anonymous = [1, 1, 2].map(
    (slot) =>
      new InProcessMaterial({
        ...named,
        materialID: -1,
        action: new InProcessMaterialAction({
          type: ActionType.LoadingToBasket,
          loadToBasketSlot: slot,
        }),
      }),
  );
  const snapshot = {
    ...base,
    material: [...anonymous, new InProcessMaterial({ ...named, signaledInspections: ["check"] })],
  };
  const inspection = {
    ...machined(100),
    counter: 120,
    type: LogType.InspectionForce,
    result: "true",
    program: "check",
  };
  await settle(() => {
    statusLoads[0]?.resolve(snapshot);
    logLoads[0]?.resolve([inspection]);
  });
  await settle(() => FakeSocket.sockets[0]?.emit("message", logMessage(inspection)));
  await settle(() =>
    FakeSocket.sockets[0]?.emit(
      "message",
      logMessage({ ...machined(999), type: LogType.PartMark, result: "unrelated serial" }),
    ),
  );
  expect(store.get(currentStatus)).toBe(snapshot);
  expect(store.get(currentStatus).material).toHaveLength(4);
  expect(store.get(currentStatus).material[3]?.signaledInspections).toEqual(["check"]);
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
  await settle(() => FakeSocket.sockets[0]?.emit("message", statusMessage("pushed", 2)));
  await settle(() => {
    statusLoads[0]?.resolve(status("bootstrap", 1));
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
  test(`${loading} history times out a stalled request and retries without advancing the applied cursor`, async () => {
    const { store, live } = await connect();
    await settle(() => FakeSocket.sockets[0]?.emit("open"));
    const socket = loading === "initial" ? 0 : 1;
    if (socket === 1) {
      await settle(() => logLoads[0]?.resolve([assigned(100)]));
      await reconnect(1);
    }
    await settle(() => statusLoads[socket]?.resolve(statusWithMaterial("before")));
    await settle(() => {
      FakeSocket.sockets[socket]?.emit("message", logMessage(toolCycle(120)));
      FakeSocket.sockets[socket]?.emit("message", logMessage(assigned(130)));
    });
    await settle(() => vi.advanceTimersByTime(119999));
    expect(logLoads).toHaveLength(socket + 1);
    expect(store.get(errorLoadingLast30)).toBeNull();
    await settle(() => vi.advanceTimersByTime(1));
    expect(logLoads[socket]?.signal?.aborted).toBe(true);
    expect(store.get(errorLoadingLast30)).toContain("timed out");
    expect(store.get(lastEventCounter)).toBe(socket === 0 ? null : 100);
    expect(store.get(currentStatus).material[0]?.workorderId).toBe("order-130");
    expect(live().live).toBe(true);

    await settle(() => vi.advanceTimersByTime(1000));
    expect(logLoads).toHaveLength(socket + 2);
    expect(recentFrom).toEqual(socket === 0 ? [] : [100, 100]);
    expect(logLoads[socket + 1]?.signal).not.toBe(logLoads[socket]?.signal);
    expect(logLoads[socket + 1]?.signal?.aborted).toBe(false);
    await settle(() => FakeSocket.sockets[socket]?.emit("message", logMessage(assigned(140))));
    await settle(() =>
      logLoads[socket + 1]?.resolve([toolCycle(110), toolCycle(120), assigned(125)]),
    );
    expect(store.get(lastEventCounter)).toBe(140);
    expect(store.get(last30MaterialSummary).matsById.get(100)?.workorderId).toBe("order-140");
    expect(store.get(currentStatus).material[0]?.workorderId).toBe("order-140");
    expect(store.get(errorLoadingLast30)).toBeNull();
    expect(
      store
        .get(last30ToolUse)
        .get(PartAndStationOperation.ofLogCycle(toolCycle(110)))
        ?.recentCycles.map((cycle) => cycle.tools[0]?.cycleUsageCnt),
    ).toEqual([110, 120]);
  });

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

test("a stalled history response body times out through the generated HTTP client", async () => {
  let bodies: ReadonlyArray<{
    readonly signal: AbortSignal | null | undefined;
    readonly controller: ReadableStreamDefaultController<Uint8Array>;
  }> = [];
  const client = new LogClient(undefined, {
    fetch: async (_input, init) =>
      new Response(
        new ReadableStream<Uint8Array>({
          start(controller) {
            const signal = init?.signal;
            bodies = [...bodies, { signal, controller }];
            signal?.addEventListener("abort", () => controller.error(signal.reason), {
              once: true,
            });
          },
        }),
      ),
  });
  vi.spyOn(LogBackend, "get").mockImplementation((start, end, signal) =>
    client.get(start, end, signal),
  );
  const { store, live } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => {
    statusLoads[0]?.resolve(statusWithMaterial("before"));
    FakeSocket.sockets[0]?.emit("message", logMessage(assigned(120)));
  });
  expect(bodies).toHaveLength(1);
  await settle(() => vi.advanceTimersByTime(120000));
  expect(bodies[0]?.signal?.aborted).toBe(true);
  expect(store.get(lastEventCounter)).toBeNull();
  expect(store.get(errorLoadingLast30)).toContain("timed out");
  expect(live().live).toBe(true);
  await settle(() => vi.advanceTimersByTime(1000));
  expect(bodies).toHaveLength(2);
  await settle(() => {
    bodies[1]?.controller.enqueue(
      new TextEncoder().encode(JSON.stringify([new LogEntry(assigned(110)).toJSON()])),
    );
    bodies[1]?.controller.close();
  });
  expect(store.get(lastEventCounter)).toBe(120);
  expect(store.get(last30MaterialSummary).matsById.get(100)?.workorderId).toBe("order-120");
  expect(store.get(errorLoadingLast30)).toBeNull();
});

test.each(["close", "dispose"] as const)(
  "a pending history timeout does not retry after %s",
  async (ending) => {
    const { store, unmount } = await connect();
    await settle(() => FakeSocket.sockets[0]?.emit("open"));
    if (ending === "close") await settle(() => FakeSocket.sockets[0]?.close());
    else await unmount();
    await settle(() => vi.advanceTimersByTime(150000));
    expect(logLoads[0]?.signal?.aborted).toBe(true);
    expect(logLoads).toHaveLength(1);
    expect(store.get(lastEventCounter)).toBeNull();
    expect(store.get(errorLoadingLast30)).toBeNull();
  },
);

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
