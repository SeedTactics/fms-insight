import { createStore } from "jotai";
import { expect, test } from "vitest";
import { CurrentStatus, type ICurrentStatus } from "./api.js";
import {
  acceptCurrentStatusSnapshot,
  applyCurrentStatusResponse,
  currentStatusIsLiveRW,
  currentStatusRequest,
  currentStatusSessionAtom,
} from "./current-status-session.js";
import { currentStatus, reorderQueuedMatInCurrentStatus } from "../cell-status/current-status.js";
import { customState } from "../cell-status/custom-state.js";

function status(revision: number, label: string): Readonly<ICurrentStatus> {
  return CurrentStatus.fromJS({
    Revision: revision,
    TimeOfCurrentStatusUTC: "2026-10-26T12:00:00Z",
    Jobs: {},
    Pallets: {},
    Queues: {},
    Alarms: [],
    CustomState: { label },
    Material: [1, 2].map((id) => ({
      MaterialID: id,
      JobUnique: "job",
      PartName: "Part",
      Process: 1,
      Path: 1,
      SignaledInspections: [],
      Action: { Type: "Waiting" },
      Location: { Type: "InQueue", CurrentQueue: "stock", QueuePosition: id - 1 },
    })),
  });
}

function connected() {
  const store = createStore();
  store.set(currentStatusSessionAtom, { id: 1, open: true, snapshot: null });
  store.set(acceptCurrentStatusSnapshot, 1, status(100, "initial"), "socket");
  return store;
}

test("an acknowledged server snapshot replaces optimistic edits using the websocket ordering rule", () => {
  const store = connected();
  const request = store.get(currentStatusRequest);
  store.set(reorderQueuedMatInCurrentStatus, { queue: "stock", matId: 1, newIdx: 1 });
  expect(store.get(currentStatus).material.map((m) => m.location.queuePosition)).toEqual([1, 0]);
  const result = status(102, "action acknowledged");
  expect(store.set(applyCurrentStatusResponse, request, result)).toBe(true);
  expect(store.get(currentStatus)).toBe(result);
  expect(store.get(currentStatus).material.map((m) => m.location.queuePosition)).toEqual([0, 1]);
  expect(store.get(customState)).toEqual({ label: "action acknowledged" });
  expect(store.get(currentStatusSessionAtom).snapshot?.status).toBe(result);
  expect(store.get(currentStatusIsLiveRW)).toBe(true);
  expect(
    store.set(acceptCurrentStatusSnapshot, 1, status(101, "queued before action"), "socket"),
  ).toBe(false);
  expect(store.get(currentStatus)).toBe(result);
});

test("an already newer snapshot wins without requiring an HTTP read", () => {
  const store = connected();
  const request = store.get(currentStatusRequest);
  const newer = status(103, "newer socket");
  store.set(acceptCurrentStatusSnapshot, 1, newer, "socket");
  expect(store.set(applyCurrentStatusResponse, request, status(102, "older response"))).toBe(true);
  expect(store.get(currentStatus)).toBe(newer);
  expect(store.get(currentStatusIsLiveRW)).toBe(true);
  const latest = status(104, "next action");
  expect(store.set(applyCurrentStatusResponse, store.get(currentStatusRequest), latest)).toBe(true);
  expect(store.get(currentStatus)).toBe(latest);
});

test("an action response from an ended session cannot replace the reconnected server view", () => {
  const store = connected();
  const request = store.get(currentStatusRequest);
  store.set(currentStatusSessionAtom, { id: 2, open: true, snapshot: null });
  store.set(currentStatusIsLiveRW, false);
  const newerSession = status(1, "restarted server");
  store.set(acceptCurrentStatusSnapshot, 2, newerSession, "socket");
  expect(store.set(applyCurrentStatusResponse, request, status(1000, "old session"))).toBe(false);
  expect(store.get(currentStatus)).toBe(newerSession);
  expect(store.get(currentStatusIsLiveRW)).toBe(true);
});
