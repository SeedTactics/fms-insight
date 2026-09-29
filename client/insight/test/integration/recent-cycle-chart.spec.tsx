import { act } from "react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { RecentCycleChart } from "../../src/components/operations/RecentCycleChart.js";
import { secondsSinceEpochAtom } from "../../src/cell-status/current-status.js";
import { onLoadCurrentSt, onServerEvent } from "../../src/cell-status/loading.js";
import { recentCycles } from "../../src/data/results.cycles.js";
import {
  ActionType,
  LocType,
  LogEntry,
  PalletLocationEnum,
  type ICurrentStatus,
} from "../../src/network/api.js";
import { renderInsightPage } from "./framework.js";
import { createCurrentStatus, createMaterial, createPallet } from "./load-station-testkit.js";
import { fakeMachineCycle } from "../events.fake.js";

vi.mock("../../src/data/results.cycles.js", { spy: true });

beforeEach(() => {
  vi.setSystemTime(new Date("2030-01-01T12:00:45Z"));
  vi.clearAllMocks();
});

afterEach(() => vi.useRealTimers());

test("a cycle starting after the five-minute tick has visible elapsed and remaining bars", async () => {
  const statusTime = new Date("2030-01-01T12:00:45Z");
  const status: Readonly<ICurrentStatus> = createCurrentStatus({
    pallets: [
      createPallet({
        palletNum: 1,
        numFaces: 1,
        currentPalletLocation: { loc: PalletLocationEnum.Machine, group: "MC", num: 1 },
      }),
    ],
    material: [
      createMaterial({
        materialID: 1,
        jobUnique: "job",
        partName: "part",
        process: 1,
        path: 1,
        location: { type: LocType.OnPallet, palletNum: 1 },
        action: {
          type: ActionType.Machining,
          program: "program",
          elapsedMachiningTime: "PT15S",
          expectedRemainingMachiningTime: "PT5M",
        },
      }),
    ],
  });
  const screen = await renderInsightPage(<RecentCycleChart width={1000} height={300} />, {
    currentStatus: { ...status, timeOfCurrentStatusUTC: statusTime },
    seedStore: (store) => store.set(secondsSinceEpochAtom, statusTime.getTime() / 1000),
  });

  await expect
    .poll(() =>
      Array.from(screen.container.querySelectorAll("svg rect[fill]")).map(
        (rect) => Number(rect.getAttribute("width")) > 0,
      ),
    )
    .toEqual([true, true]);
});

test("status updates and minute ticks keep history cached until the five-minute tick", async () => {
  const status: Readonly<ICurrentStatus> = createCurrentStatus({});
  const screen = await renderInsightPage(<RecentCycleChart width={1000} height={300} />, {
    currentStatus: { ...status, timeOfCurrentStatusUTC: new Date("2030-01-01T12:00:45Z") },
    seedStore: (store) =>
      store.set(secondsSinceEpochAtom, new Date("2030-01-01T12:00:45Z").getTime() / 1000),
  });
  expect(recentCycles).toHaveBeenCalled();
  const initialCalculations = vi.mocked(recentCycles).mock.calls.length;

  for (const time of ["12:00:46", "12:01:00", "12:04:59"]) {
    const timestamp = new Date(`2030-01-01T${time}Z`);
    vi.setSystemTime(timestamp);
    await act(async () => {
      screen.store.set(secondsSinceEpochAtom, timestamp.getTime() / 1000);
      screen.store.set(onLoadCurrentSt, { ...status, timeOfCurrentStatusUTC: timestamp });
    });
    expect(recentCycles).toHaveBeenCalledTimes(initialCalculations);
  }

  const eventTime = new Date("2030-01-01T12:04:59Z");
  await act(async () => {
    for (const logEntry of fakeMachineCycle({
      counter: 1,
      part: "part",
      proc: 1,
      pal: 1,
      program: "program",
      time: eventTime,
      elapsedMin: 30,
      activeMin: 30,
    })) {
      screen.store.set(onServerEvent, {
        evt: { logEntry: new LogEntry(logEntry) },
        now: eventTime,
        expire: false,
      });
    }
  });
  const calculationsAfterEvent = vi.mocked(recentCycles).mock.calls.length;
  expect(calculationsAfterEvent).toBeGreaterThan(initialCalculations);
  await expect.poll(() => screen.container.querySelectorAll("svg rect[fill]").length).toBe(1);

  const nextTick = new Date("2030-01-01T12:05:00Z");
  vi.setSystemTime(nextTick);
  await act(async () => {
    screen.store.set(secondsSinceEpochAtom, nextTick.getTime() / 1000);
  });
  expect(vi.mocked(recentCycles).mock.calls.length).toBeGreaterThan(calculationsAfterEvent);
});
