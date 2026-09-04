import { expect, it } from "vitest";
import { createStore } from "jotai";
import { EditMaterialInLogEvents, LogEntry, LogMaterial, LogType } from "../network/api.js";
import {
  last30MaterialSummary,
  setLast30MatSummary,
  updateLast30MatSummary,
} from "./material-summary.js";
import { last30PartSummary } from "../data/part-summary.js";

function unload(counter: number, type: LogType, completed: string | undefined): LogEntry {
  return new LogEntry({
    counter,
    type,
    startofcycle: false,
    endUTC: new Date(Date.UTC(2026, 8, 4, 12, counter)),
    loc: "L/U",
    locnum: 1,
    pal: 4,
    program: "UNLOAD",
    result: "UNLOAD",
    elapsed: "PT1M",
    active: "PT1M",
    material: [new LogMaterial({ id: 1, uniq: "job", part: "part", proc: 2, numproc: 2, face: 1 })],
    details: completed === undefined ? undefined : { "MaterialCompleted:1": completed },
  });
}

it("counts a basket part only at terminal exit, while preserving machining/unload history", () => {
  const store = createStore();
  store.set(setLast30MatSummary, [unload(1, LogType.BasketLoadUnload, "False")]);
  expect(store.get(last30MaterialSummary).matsById.get(1)?.completed_time).toBeUndefined();
  const returned = unload(2, LogType.LoadUnloadCycle, "False");
  store.set(setLast30MatSummary, [returned]);
  expect(store.get(last30MaterialSummary).matsById.get(1)?.completed_time).toBeUndefined();
  expect(store.get(last30MaterialSummary).matsById.get(1)?.completed_last_proc_machining).toBe(
    true,
  );
  expect(store.get(last30PartSummary).find((part) => part.part === "part")?.completedQty ?? 0).toBe(
    0,
  );
  const terminal = unload(3, LogType.BasketLoadUnload, "True");
  store.set(setLast30MatSummary, [terminal]);
  expect(store.get(last30MaterialSummary).matsById.get(1)?.completed_time).toEqual(terminal.endUTC);
  expect(store.get(last30MaterialSummary).matsById.get(1)?.unloaded_processes?.[2]).toEqual(
    returned.endUTC,
  );
  expect(store.get(last30PartSummary).find((part) => part.part === "part")?.completedQty).toBe(1);
});

it.each([undefined, "True"])("preserves conventional terminal pallet completion (%s)", (fact) => {
  const store = createStore();
  const terminal = unload(1, LogType.LoadUnloadCycle, fact);
  store.set(setLast30MatSummary, [terminal]);
  expect(store.get(last30MaterialSummary).matsById.get(1)?.completed_time).toEqual(terminal.endUTC);
});

it("does not guess completion from an unmarked historical basket unload", () => {
  const store = createStore();
  store.set(setLast30MatSummary, [unload(1, LogType.BasketLoadUnload, undefined)]);
  expect(store.get(last30MaterialSummary).matsById.get(1)?.completed_time).toBeUndefined();
});

it("requires an ended manufacturing unload event", () => {
  const store = createStore();
  store.set(setLast30MatSummary, [
    unload(1, LogType.GeneralMessage, "True"),
    new LogEntry({ ...unload(2, LogType.BasketLoadUnload, "True"), startofcycle: true }),
  ]);
  expect(store.get(last30MaterialSummary).matsById.get(1)?.completed_time).toBeUndefined();
});

it("moves a corrected terminal completion to the corrected event material", () => {
  const store = createStore();
  const terminal = unload(1, LogType.LoadUnloadCycle, "True");
  store.set(setLast30MatSummary, [terminal]);
  store.set(updateLast30MatSummary, {
    now: terminal.endUTC,
    expire: false,
    evt: {
      editMaterialInLog: new EditMaterialInLogEvents({
        oldMaterialID: 1,
        newMaterialID: 2,
        editedEvents: [
          new LogEntry({
            ...terminal,
            material: [new LogMaterial({ ...terminal.material[0], id: 2 })],
            details: { "MaterialCompleted:2": "True" },
          }),
        ],
      }),
    },
  });
  expect(store.get(last30MaterialSummary).matsById.get(1)?.completed_time).toBeUndefined();
  expect(store.get(last30MaterialSummary).matsById.get(2)?.completed_time).toEqual(terminal.endUTC);
});
