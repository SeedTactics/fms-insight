import { it, expect } from "vitest";
import { fakeLoadOrUnload, fakeMachineCycle } from "../../test/events.fake.js";
import { createStore } from "jotai";
import {
  isOutlier,
  last30EstimatedCycleTimes,
  PartAndStationOperation,
  setLast30EstimatedCycleTimes,
} from "./estimated-cycle-times.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { addMinutes } from "date-fns";

it("calcs expected times", () => {
  const evts = [
    ...fakeMachineCycle({
      counter: 100,
      numMats: 3,
      part: "part111",
      proc: 4,
      pal: 2,
      program: "prog1",
      mcName: "mmm",
      mcNum: 44,
      time: new Date(2024, 12, 14, 9, 4, 5),
      elapsedMin: 45, // 15 per mat
      activeMin: 30,
    }),
    ...fakeMachineCycle({
      counter: 200,
      numMats: 3,
      part: "part111",
      proc: 4,
      pal: 2,
      program: "prog1",
      mcName: "mmm",
      mcNum: 21,
      time: new Date(2024, 12, 14, 10, 4, 5),
      elapsedMin: 63, // 21 per mat.  Expected should then be 18
      activeMin: 30,
    }),

    // load and unload cycles with same time and same elapsed time, should have the elapsed time split

    // use an elapsed time of 15 mins for 4 parts
    // but the load has 8 active mins and unload 4, so time is split
    // so the load gets 8/12 * 15 = 10 mins / 2 mats and the unload 5 mins / 2 mats
    ...fakeLoadOrUnload({
      counter: 300,
      part: "part111",
      numMats: 2,
      proc: 4,
      pal: 2,
      isLoad: true,
      time: new Date(2024, 12, 14, 11, 4, 5),
      elapsedMin: 15,
      activeMin: 8,
    }),
    ...fakeLoadOrUnload({
      counter: 400,
      part: "part111",
      numMats: 2,
      proc: 4,
      pal: 2,
      isLoad: false,
      time: new Date(2024, 12, 14, 11, 4, 5), // same time
      elapsedMin: 15, // same elapsed
      activeMin: 4, // half the active time
    }),

    // now a load and unload with the same time but different elapsed times
    ...fakeLoadOrUnload({
      counter: 500,
      part: "part111",
      numMats: 2,
      proc: 1,
      pal: 2,
      isLoad: true,
      time: new Date(2024, 12, 14, 12, 4, 5),
      elapsedMin: 12,
      activeMin: 8,
    }),
    ...fakeLoadOrUnload({
      counter: 600,
      part: "part111",
      numMats: 2,
      proc: 1,
      pal: 2,
      isLoad: false,
      time: new Date(2024, 12, 14, 12, 4, 5), // same time
      elapsedMin: 6, // different elapsed
      activeMin: 4, // half the active time, but active is ignored
    }),
  ];

  const store = createStore();
  store.set(setLast30EstimatedCycleTimes, evts);

  const estimated = store.get(last30EstimatedCycleTimes);

  expect(estimated.size).toBe(5);

  // first the machine cycle, average of 15 and 21
  expect(estimated.get(new PartAndStationOperation("part111", "mmm", "prog1"))).toEqual({
    MAD_aboveMinutes: 4.4478,
    MAD_belowMinutes: 4.4478,
    expectedCycleMinutesForSingleMat: 18,
    medianMinutesForSingleMat: 18,
  });

  // next the proc4 load and unload which should split
  expect(estimated.get(new PartAndStationOperation("part111", "L/U", "LOAD-4"))).toEqual({
    MAD_aboveMinutes: 0.25,
    MAD_belowMinutes: 0.25,
    expectedCycleMinutesForSingleMat: 5, // 8/12 * 15 = 10 mins / 2 mats
    medianMinutesForSingleMat: 5,
  });
  expect(estimated.get(new PartAndStationOperation("part111", "L/U", "UNLOAD-4"))).toEqual({
    MAD_aboveMinutes: 0.25,
    MAD_belowMinutes: 0.25,
    expectedCycleMinutesForSingleMat: 2.5, // 4/12 * 15 = 5 mins / 2 mats
    medianMinutesForSingleMat: 2.5,
  });

  // now the cycles which have already been split
  expect(estimated.get(new PartAndStationOperation("part111", "L/U", "LOAD-1"))).toEqual({
    MAD_aboveMinutes: 0.25,
    MAD_belowMinutes: 0.25,
    expectedCycleMinutesForSingleMat: 6,
    medianMinutesForSingleMat: 6,
  });
  expect(estimated.get(new PartAndStationOperation("part111", "L/U", "UNLOAD-1"))).toEqual({
    MAD_aboveMinutes: 0.25,
    MAD_belowMinutes: 0.25,
    expectedCycleMinutesForSingleMat: 3,
    medianMinutesForSingleMat: 3,
  });
});

//

it("calculates median and MAD", () => {
  const evts = LazySeq.ofRange(1, 100)
    .flatMap((i) =>
      fakeMachineCycle({
        counter: 2 * i,
        numMats: 2,
        part: "part111",
        proc: 4,
        pal: 2,
        program: "prog1",
        mcName: "mmm",
        mcNum: 44,
        time: addMinutes(new Date(2024, 12, 14, 9, 4, 5), i),
        activeMin: 30,
        // elapsed is random between 30 and 40
        elapsedMin: Math.random() * 10 + 30,
      }),
    )
    .concat(
      // add an outlier
      fakeMachineCycle({
        counter: 200,
        numMats: 2,
        part: "part111",
        proc: 4,
        pal: 2,
        program: "prog1",
        mcName: "mmm",
        mcNum: 21,
        time: new Date(2024, 12, 20, 10, 4, 5),
        elapsedMin: 100,
        activeMin: 30,
      }),
    )
    .toRArray();

  const store = createStore();
  store.set(setLast30EstimatedCycleTimes, evts);

  const estimated = store.get(last30EstimatedCycleTimes);

  expect(estimated.size).toBe(1);

  const es = estimated.get(new PartAndStationOperation("part111", "mmm", "prog1"));

  // median should be between 30 and 40
  expect(es?.medianMinutesForSingleMat).toBeGreaterThan(30 / 2);
  expect(es?.medianMinutesForSingleMat).toBeLessThan(40 / 2);

  expect(es?.expectedCycleMinutesForSingleMat).toBeGreaterThan(30 / 2);
  expect(es?.expectedCycleMinutesForSingleMat).toBeLessThan(40 / 2);

  expect(es?.MAD_aboveMinutes).toBeGreaterThan(0);
  expect(es?.MAD_aboveMinutes).toBeLessThan(2.6);
  expect(es?.MAD_belowMinutes).toBeGreaterThan(0);
  expect(es?.MAD_belowMinutes).toBeLessThan(3);

  expect(isOutlier(es!, 31 / 2)).toBe(false);
  expect(isOutlier(es!, 38 / 2)).toBe(false);
  expect(isOutlier(es!, 100 / 2)).toBe(true);
});
