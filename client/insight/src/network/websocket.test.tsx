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

import { currentStatusIsLive, websocketReconnecting, WebsocketConnection } from "./websocket.js";
import {
  registerBackend,
  type FmsAPI,
  type JobAPI,
  type LogAPI,
  type MachineAPI,
} from "./backend.js";
import { CurrentStatus, ServerEvent, type ICurrentStatus } from "./api.js";

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

function status(): ICurrentStatus {
  return new CurrentStatus({
    timeOfCurrentStatusUTC: new Date(),
    jobs: {},
    pallets: {},
    material: [],
    alarms: [],
    queues: {},
  });
}

// Backend calls not listed fail the test.
function only<T extends object>(calls: Partial<T>): T {
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
let logLoad: Deferred<[]>;
let root: Root;

beforeEach(() => {
  vi.useFakeTimers();
  FakeSocket.sockets.length = 0;
  vi.stubGlobal("WebSocket", FakeSocket);
  statusLoads = [];
  logLoad = deferred();
  registerBackend(
    only<LogAPI>({ get: () => logLoad.promise, recent: () => logLoad.promise }),
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
  await act(async () => root.unmount());
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

async function connect() {
  const store = createStore();
  root = createRoot(document.createElement("div"));
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
  return { live };
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
    logLoad.resolve([]);
  });
  expect(live()).toEqual({ live: false, reconnecting: true });
});

test("a failed status load does not certify the retained status", async () => {
  const { live } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => {
    statusLoads[0]?.reject(new Error("status unavailable"));
    logLoad.resolve([]);
  });
  expect(live()).toEqual({ live: false, reconnecting: false });
  const evt = new ServerEvent({ newCurrentStatus: new CurrentStatus(status()) });
  await settle(() =>
    FakeSocket.sockets[0]?.emit("message", { data: JSON.stringify(evt.toJSON()) }),
  );
  expect(live().live).toBe(true);
});

test("a failed history load does not affect a live status", async () => {
  const { live } = await connect();
  await settle(() => FakeSocket.sockets[0]?.emit("open"));
  await settle(() => {
    statusLoads[0]?.resolve(status());
    logLoad.reject(new Error("log unavailable"));
  });
  expect(live()).toEqual({ live: true, reconnecting: false });
});
