import { createStore } from "jotai";
import { afterEach, expect, test, vi } from "vitest";

import * as api from "../network/api.js";
import { registerNetworkBackend } from "../network/backend.js";
import { currentStatus, reorderQueuedMatInCurrentStatus } from "./current-status.js";
import { customState } from "./custom-state.js";
import { moveQueuedMatInCurrentStatus, onLiveServerEvent, onLoadCurrentSt } from "./loading.js";

afterEach(() => vi.restoreAllMocks());

function status(label: string): api.CurrentStatus {
  return new api.CurrentStatus({
    timeOfCurrentStatusUTC: new Date("2026-09-30T12:00:00Z"),
    jobs: {},
    pallets: {},
    alarms: [],
    queues: {},
    customState: { label },
    material: [1, 2].map(
      (id) =>
        new api.InProcessMaterial({
          materialID: id,
          jobUnique: "JOB",
          partName: "Part",
          process: 1,
          path: 1,
          serial: `${label}-${id}`,
          signaledInspections: [],
          location: new api.InProcessMaterialLocation({
            type: api.LocType.InQueue,
            currentQueue: "Queue A",
            queuePosition: id - 1,
          }),
          action: new api.InProcessMaterialAction({ type: api.ActionType.Waiting }),
        }),
    ),
  });
}

function rejectedMoveWithPendingReload() {
  let respond!: (response: Response) => void;
  const reload = new Promise<Response>((resolve) => {
    respond = resolve;
  });
  const fetch = vi
    .spyOn(window, "fetch")
    .mockImplementation((input) =>
      (input instanceof Request ? input.url : input.toString()).endsWith("/status")
        ? reload
        : Promise.resolve(new Response("Rejected", { status: 409 })),
    );
  registerNetworkBackend();
  return { fetch, respond };
}

function move(store: ReturnType<typeof createStore>) {
  return store
    .set(moveQueuedMatInCurrentStatus, { queue: "Queue A", matId: 1, newIdx: 1, operator: null })
    .catch((error: unknown) => error);
}

test("undoes the optimistic drop when rejection recovery cannot reach the server", async () => {
  const fetch = vi
    .spyOn(window, "fetch")
    .mockImplementation((input) =>
      (input instanceof Request ? input.url : input.toString()).endsWith("/status")
        ? Promise.reject(new Error("Offline"))
        : Promise.resolve(new Response("Rejected", { status: 409 })),
    );
  registerNetworkBackend();
  const store = createStore();
  const before = status("before");
  store.set(onLoadCurrentSt, before);
  const request = move(store);
  expect(store.get(currentStatus).material.map((m) => m.location.queuePosition)).toEqual([1, 0]);

  expect(await request).toMatchObject({ status: 409 });
  expect(fetch).toHaveBeenCalledTimes(2);
  expect(store.get(currentStatus)).toBe(before);
  expect(store.get(customState)).toEqual(before.customState);
});

test.each([200, 503])(
  "preserves live status arriving during a recovery returning HTTP %s",
  async (responseStatus) => {
    const { fetch, respond } = rejectedMoveWithPendingReload();
    const store = createStore();
    store.set(onLoadCurrentSt, status("before"));
    const request = move(store);
    await expect.poll(() => fetch.mock.calls.length).toBe(2);
    const newer = status("live");
    store.set(onLiveServerEvent, {
      evt: { newCurrentStatus: newer },
      expire: false,
      now: new Date(),
    });

    respond(
      new Response(responseStatus === 200 ? JSON.stringify(status("older").toJSON()) : "Offline", {
        status: responseStatus,
      }),
    );
    expect(await request).toMatchObject({ status: 409 });
    expect(store.get(currentStatus)).toBe(newer);
    expect(store.get(customState)).toEqual(newer.customState);
  },
);

test("loads custom status along with queue state during successful rejection recovery", async () => {
  const { fetch, respond } = rejectedMoveWithPendingReload();
  const store = createStore();
  store.set(onLoadCurrentSt, status("before"));
  const request = move(store);
  await expect.poll(() => fetch.mock.calls.length).toBe(2);
  const refreshed = status("server");
  respond(new Response(JSON.stringify(refreshed.toJSON())));

  expect(await request).toMatchObject({ status: 409 });
  expect(store.get(currentStatus)).toEqual(refreshed);
  expect(store.get(customState)).toEqual(refreshed.customState);
});

test("does not replace an intervening optimistic drag with an older recovery response", async () => {
  const { fetch, respond } = rejectedMoveWithPendingReload();
  const store = createStore();
  store.set(onLoadCurrentSt, status("before"));
  const request = move(store);
  await expect.poll(() => fetch.mock.calls.length).toBe(2);
  store.set(reorderQueuedMatInCurrentStatus, { queue: "Queue B", matId: 2, newIdx: 0 });
  const newer = store.get(currentStatus);
  respond(new Response(JSON.stringify(status("older").toJSON())));

  expect(await request).toMatchObject({ status: 409 });
  expect(store.get(currentStatus)).toBe(newer);
});
