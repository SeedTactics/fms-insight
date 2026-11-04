import { createStore } from "jotai";
import { expect, test } from "vitest";
import { LogEntry, LogType } from "../network/api.js";
import {
  last30MaterialSummary,
  setLast30MatSummary,
  updateLast30MatSummary,
} from "./material-summary.js";

function signal(counter: number, process: number, note?: string) {
  return LogEntry.fromJS({
    counter,
    type: LogType.SignalQuarantine,
    endUTC: new Date().toISOString(),
    material: [{ id: 101, proc: process, part: "Part", numproc: 2, face: 1 }],
    details: note === undefined ? undefined : { note },
  });
}

test.each([false, true])(
  "quarantine reasons follow the latest signal per process, expire %s",
  (expire) => {
    const store = createStore();
    store.set(setLast30MatSummary, [
      signal(10, 1, "Previous process"),
      signal(20, 2, "Initial reason"),
    ]);
    const signals = () => store.get(last30MaterialSummary).matsById.get(101)?.quarantineSignals;
    expect(signals()?.get(2)?.note).toBe("Initial reason");

    for (const event of [signal(30, 2, " Updated reason "), signal(15, 2, "Older replay")]) {
      store.set(updateLast30MatSummary, { evt: { logEntry: event }, now: new Date(), expire });
    }
    expect(signals()?.get(1)?.note).toBe("Previous process");
    expect(signals()?.get(2)?.note).toBe("Updated reason");

    store.set(updateLast30MatSummary, {
      evt: { logEntry: signal(40, 2) },
      now: new Date(),
      expire,
    });
    expect(signals()?.get(2)).toEqual({ counter: 40, note: undefined });
  },
);
