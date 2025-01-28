/* Copyright (c) 2021, John Lenz

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

import { fakeCycle, fakeLoadOrUnload, fakeMaterial, fakePalletBegin } from "../../test/events.fake.js";
import { lastEventCounter, onLoadLast30Log, onLoadSpecificMonthLog, onServerEvent } from "./loading.js";
import { addDays, addMinutes } from "date-fns";
import { last30BufferEntries, specificMonthBufferEntries } from "./buffers.js";
import { last30EstimatedCycleTimes, specificMonthEstimatedCycleTimes } from "./estimated-cycle-times.js";
import { last30Inspections, specificMonthInspections } from "./inspections.js";
import { last30MaterialSummary, specificMonthMaterialSummary } from "./material-summary.js";
import { last30PalletCycles, specificMonthPalletCycles } from "./pallet-cycles.js";
import { last30StationCycles, specificMonthStationCycles } from "./station-cycles.js";
import { last30ToolUse } from "./tool-usage.js";
import { LogEntry, LogMaterial } from "../network/api.js";
import { it, expect } from "vitest";

import { toRawJs } from "../../test/to-raw-js.js";
import { createStore } from "jotai";
import { LazySeq } from "@seedtactics/immutable-collections";

function checkLast30(snapshot: ReturnType<typeof createStore>, msg: string) {
  expect(toRawJs(snapshot.get(last30BufferEntries))).toMatchSnapshot(msg + " - buffers");
  expect(toRawJs(snapshot.get(last30EstimatedCycleTimes))).toMatchSnapshot(msg + " - estimated cycle times");
  expect(toRawJs(snapshot.get(last30Inspections))).toMatchSnapshot(msg + " - inspections");
  expect(toRawJs(snapshot.get(last30MaterialSummary))).toMatchSnapshot(msg + " - material summary");
  expect(toRawJs(snapshot.get(last30PalletCycles))).toMatchSnapshot(msg + " - pallet cycles");
  expect(toRawJs(snapshot.get(last30StationCycles))).toMatchSnapshot(msg + " - station cycles");
  expect(toRawJs(snapshot.get(last30ToolUse))).toMatchSnapshot(msg + " - tool use");
  expect(toRawJs(snapshot.get(lastEventCounter))).toMatchSnapshot(msg + " - event counter");
}

function twentySevenTwoAndTodayCycles(now: Date) {
  const twoDaysAgo = addDays(now, -2);
  const twentySevenDaysAgo = addDays(now, -27);

  return {
    todayCycle: fakeCycle({
      counter: 100,
      time: now,
      machineTime: 30,
      part: "part111",
      proc: 1,
      pallet: 222,
      includeTools: true,
    }),
    twoDaysAgoCycle: fakeCycle({
      counter: 200,
      time: twoDaysAgo,
      machineTime: 24,
      part: "part222",
      proc: 1,
      pallet: 222,
      includeTools: true,
    }),
    twentySevenCycle: fakeCycle({
      counter: 300,
      time: twentySevenDaysAgo,
      machineTime: 18,
      part: "part222",
      proc: 2,
      pallet: 111,
      includeTools: true,
    }),
  };
}

it("processes last 30 events", () => {
  const now = new Date(Date.UTC(2018, 1, 2, 9, 4, 5));

  // start with cycles from 27 days ago, 2 days ago, and today
  const { twentySevenCycle, twoDaysAgoCycle, todayCycle } = twentySevenTwoAndTodayCycles(now);

  const snapshot = createStore();
  snapshot.set(onLoadLast30Log, [...twentySevenCycle, ...twoDaysAgoCycle, ...todayCycle]);

  checkLast30(snapshot, "initial 27 days ago, two days ago, and today");

  // Now add again 6 days from now so that twenty seven is removed from processing 30 days
  const sixDays = addDays(now, 6);
  const sixDaysCycle = fakeCycle({
    time: sixDays,
    machineTime: 12,
    part: "part111",
    proc: 1,
    pallet: 333,
    includeTools: true,
    counter: 400,
  });

  for (const c of sixDaysCycle) {
    snapshot.set(onServerEvent, {
      now: sixDays,
      expire: true,
      evt: {
        logEntry: new LogEntry(c),
      },
    });
  }

  // twentySevenDaysAgo should have been filtered out
  checkLast30(snapshot, "after filter");
});

it("processes events in a specific month", () => {
  const now = new Date(Date.UTC(2018, 1, 2, 9, 4, 5));

  const { twentySevenCycle, twoDaysAgoCycle, todayCycle } = twentySevenTwoAndTodayCycles(now);

  const snapshot = createStore();
  snapshot.set(onLoadSpecificMonthLog, [...twentySevenCycle, ...twoDaysAgoCycle, ...todayCycle]);

  expect(toRawJs(snapshot.get(specificMonthBufferEntries))).toMatchSnapshot("buffers");
  expect(toRawJs(snapshot.get(specificMonthEstimatedCycleTimes))).toMatchSnapshot("estimated cycle times");
  expect(toRawJs(snapshot.get(specificMonthInspections))).toMatchSnapshot("inspections");
  expect(toRawJs(snapshot.get(specificMonthMaterialSummary))).toMatchSnapshot("material summary");
  expect(toRawJs(snapshot.get(specificMonthPalletCycles))).toMatchSnapshot("pallet cycles");
  expect(toRawJs(snapshot.get(specificMonthStationCycles))).toMatchSnapshot("station cycles");
});

it("splits load elapsed times", () => {
  const evts = [
    // load and unload cycles with same time and same elapsed time, should have the elapsed time split

    // use an elapsed time of 30 mins for 4 parts
    // but the load has 16 active mins and unload 8, so time is split
    // so the load gets 16/24 * 30 = 20 mins / 2 mats and the unload 10 mins / 2 mats
    ...fakeLoadOrUnload({
      counter: 300,
      part: "part111",
      numMats: 2,
      proc: 2,
      pal: 8,
      isLoad: true,
      time: new Date(2024, 12, 14, 11, 4, 5),
      elapsedMin: 30,
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
      elapsedMin: 30, // same elapsed
      activeMin: 4, // half the active time
    }),
  ];

  const store = createStore();
  store.set(onLoadLast30Log, evts);

  const cycles = store.get(last30StationCycles);

  expect(cycles.size).toBe(2);

  expect(cycles.get(300)?.elapsedMinsPerMaterial).toBe((30 * 16) / 24 / 2);
  expect(cycles.get(400)?.elapsedMinsPerMaterial).toBe((30 * 8) / 24 / 2);
});

it("doesn't split load elapsed times if they differ", () => {
  const evts = [
    // load and unload cycles with same time and different elapsed time (already split by the server)
    // should not be split again

    ...fakeLoadOrUnload({
      counter: 300,
      part: "part111",
      numMats: 2,
      proc: 1,
      pal: 9,
      isLoad: true,
      time: new Date(2024, 12, 16, 11, 4, 5),
      elapsedMin: 22,
      activeMin: 8,
    }),
    ...fakeLoadOrUnload({
      counter: 400,
      part: "part111",
      numMats: 2,
      proc: 2,
      pal: 7,
      isLoad: false,
      time: new Date(2024, 12, 16, 11, 4, 5), // same time
      elapsedMin: 6,
      activeMin: 4,
    }),
  ];

  const store = createStore();
  store.set(onLoadLast30Log, evts);

  const cycles = store.get(last30StationCycles);

  expect(cycles.size).toBe(2);

  expect(cycles.get(300)?.elapsedMinsPerMaterial).toBe(22 / 2);
  expect(cycles.get(400)?.elapsedMinsPerMaterial).toBe(6 / 2);
});

it("doesn't split load elapsed times if they are the same", () => {
  // new versions add the material to begin pallet cycle events, which is used to determine
  // the need to split even when the elapsed times are equal.

  const mats1 = [fakeMaterial("part444", 1), fakeMaterial("part444", 1)];
  const mats2 = mats1.map((m) => new LogMaterial({ ...m, proc: 2 }));

  const evts = [
    // load and unload cycles with same time and elapsed time but in a pallet cycle event
    // are already split by the server, should not be split again

    fakePalletBegin({
      counter: 100,
      time: new Date(2024, 1, 28, 11, 4, 5),
      pal: 6,
      material: mats1,
    }),

    ...fakeLoadOrUnload({
      counter: 300,
      part: "part444",
      material: mats1,
      proc: 1,
      pal: 6,
      isLoad: true,
      time: new Date(2025, 1, 28, 11, 4, 5),
      elapsedMin: 14,
      activeMin: 8,
    }),
    ...fakeLoadOrUnload({
      counter: 400,
      part: "part444",
      material: mats2,
      proc: 2,
      pal: 6,
      isLoad: false,
      time: new Date(2025, 1, 28, 11, 4, 5), // same time
      elapsedMin: 14,
      activeMin: 4,
    }),
  ];

  const store = createStore();
  store.set(onLoadLast30Log, evts);

  const cycles = store.get(last30StationCycles);

  expect(cycles.size).toBe(2);

  expect(cycles.get(300)?.elapsedMinsPerMaterial).toBe(14 / 2);
  expect(cycles.get(400)?.elapsedMinsPerMaterial).toBe(14 / 2);
});

it("detects outliers", () => {
  const evts = LazySeq.ofRange(1, 100)
    .flatMap((i) =>
      fakeLoadOrUnload({
        counter: 2 * i,
        numMats: 2,
        part: "part111",
        proc: 4,
        pal: 2,
        isLoad: true,
        time: addMinutes(new Date(2024, 12, 14, 9, 4, 5), i),
        activeMin: 30,
        // elapsed is random between 30 and 40
        elapsedMin: Math.random() * 10 + 30,
      }),
    )
    .concat(
      // add an outlier
      fakeLoadOrUnload({
        counter: 300,
        numMats: 2,
        part: "part111",
        proc: 4,
        pal: 2,
        isLoad: true,
        time: new Date(2024, 12, 20, 10, 4, 5),
        elapsedMin: 100,
        activeMin: 30,
      }),
    )
    .toRArray();

  const store = createStore();
  store.set(onLoadLast30Log, evts);

  expect(store.get(last30StationCycles).size).toBe(100);
  for (let i = 1; i < 100; i++) {
    expect(store.get(last30StationCycles).get(2 * i)?.isOutlier).toBe(false);
  }
  expect(store.get(last30StationCycles).get(300)?.isOutlier).toBe(true);
});
