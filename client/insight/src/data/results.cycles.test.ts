/* Copyright (c) 2019, John Lenz

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
import { addHours } from "date-fns";

import { fakeCycle, fakeLoadOrUnload, fakeMachineCycle } from "../../test/events.fake.js";
import { ILogEntry } from "../network/api.js";
import {
  filterStationCycles,
  buildCycleTable,
  buildLogEntriesTable,
  outlierLoadCycles,
  outlierMachineCycles,
  recentCycles,
} from "./results.cycles.js";
import { applyConduitToSnapshot } from "../util/recoil-util.js";
import { snapshot_UNSTABLE } from "recoil";
import { onLoadLast30Log } from "../cell-status/loading.js";
import { last30StationCycles } from "../cell-status/station-cycles.js";
import { last30MaterialSummary } from "../cell-status/material-summary.js";
import {
  last30EstimatedCycleTimes,
  PartAndStationOperation,
  StatisticalCycleTime,
} from "../cell-status/estimated-cycle-times.js";
import { it, expect } from "vitest";
import { HashMap } from "@seedtactics/immutable-collections";

it("creates cycles clipboard table", () => {
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle({ time: now, machineTime: 30, counter: 100 }),
    fakeCycle({ time: addHours(now, -3), machineTime: 20, counter: 200 }),
    fakeCycle({ time: addHours(now, -15), machineTime: 15, counter: 300 })
  );
  const snapshot = applyConduitToSnapshot(snapshot_UNSTABLE(), onLoadLast30Log, evts);
  const cycles = snapshot.getLoadable(last30StationCycles).valueOrThrow();
  const matSummary = snapshot.getLoadable(last30MaterialSummary).valueOrThrow();

  const data = filterStationCycles(cycles.valuesToLazySeq(), {});

  const table = document.createElement("div");
  table.innerHTML = buildCycleTable(data, matSummary.matsById, undefined, undefined);
  expect(table).toMatchSnapshot("cycle clipboard table");

  table.innerHTML = buildCycleTable(data, matSummary.matsById, addHours(now, -3), now);
  expect(table).toMatchSnapshot("cycle filtered clipboard table");
});

it("loads outlier cycles", () => {
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle({ time: now, machineTime: 30, counter: 100 }),
    fakeCycle({ time: addHours(now, -3), machineTime: 20, counter: 200 }),
    fakeCycle({ time: addHours(now, -15), machineTime: 15, counter: 300 })
  );
  const snapshot = applyConduitToSnapshot(snapshot_UNSTABLE(), onLoadLast30Log, evts);
  const cycles = snapshot.getLoadable(last30StationCycles).valueOrThrow();
  const estimatedCycleTimes = snapshot.getLoadable(last30EstimatedCycleTimes).valueOrThrow();

  const loadOutliers = outlierLoadCycles(
    cycles.valuesToLazySeq(),
    new Date(2018, 0, 1),
    new Date(2018, 11, 1),
    estimatedCycleTimes
  );
  expect(loadOutliers.data.size).toBe(0);

  const machineOutliers = outlierMachineCycles(
    cycles.valuesToLazySeq(),
    new Date(2018, 0, 1),
    new Date(2018, 11, 1),
    estimatedCycleTimes
  );
  expect(machineOutliers.data.size).toBe(0);
});

it("creates log entries clipboard table", () => {
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle({ time: now, machineTime: 30, counter: 100 }),
    fakeCycle({ time: addHours(now, -3), machineTime: 20, counter: 200 }),
    fakeCycle({ time: addHours(now, -15), machineTime: 15, counter: 300 })
  );
  const table = document.createElement("div");
  table.innerHTML = buildLogEntriesTable(evts);
  expect(table).toMatchSnapshot("events clipboard table");
});

it("calculates recent cycles", () => {
  const now = new Date(Date.UTC(2018, 2, 5, 7, 0, 0));

  const evts = ([] as ILogEntry[]).concat([
    // one good machine cycle
    ...fakeMachineCycle({
      time: now,
      part: "part1",
      proc: 1,
      program: "abcd",
      numMats: 3,
      activeMin: 28,
      elapsedMin: 29,
      counter: 100,
    }),

    // one outlier machine cycle
    ...fakeMachineCycle({
      time: addHours(now, 1),
      part: "part1",
      proc: 1,
      program: "abcd",
      numMats: 3,
      activeMin: 28,
      elapsedMin: 40,
      counter: 200,
    }),

    // one load and one unload at the same time
    ...fakeLoadOrUnload({
      time: addHours(now, 2),
      part: "part3",
      proc: 1,
      isLoad: true,
      elapsedMin: 21,
      numMats: 2,
      activeMin: 8,
      counter: 300,
    }),
    ...fakeLoadOrUnload({
      time: addHours(now, 2),
      part: "part4",
      proc: 2,
      isLoad: false,
      elapsedMin: 21,
      numMats: 2,
      activeMin: 12,
      counter: 400,
    }),

    // one load and one unload at the same time but is outlier
    ...fakeLoadOrUnload({
      time: addHours(now, 3),
      part: "part3",
      proc: 1,
      isLoad: true,
      elapsedMin: 28,
      numMats: 2,
      activeMin: 8,
      counter: 500,
    }),
    ...fakeLoadOrUnload({
      time: addHours(now, 3),
      part: "part4",
      proc: 2,
      isLoad: false,
      elapsedMin: 28,
      numMats: 2,
      activeMin: 12,
      counter: 600,
    }),
  ]);

  const expected = HashMap.from<PartAndStationOperation, StatisticalCycleTime>([
    [
      new PartAndStationOperation("part1", "MC", "abcd"),
      {
        medianMinutesForSingleMat: 28 / 3, // 3 parts per face
        MAD_aboveMinutes: 1.2,
        MAD_belowMinutes: 1.2,
        expectedCycleMinutesForSingleMat: 100000, // not used
      },
    ],
    [
      new PartAndStationOperation("part3", "L/U", "LOAD-1"),
      {
        medianMinutesForSingleMat: 8 / 2, // 2 parts per face
        MAD_aboveMinutes: 1,
        MAD_belowMinutes: 1,
        expectedCycleMinutesForSingleMat: 100000, // not used
      },
    ],
    [
      new PartAndStationOperation("part4", "L/U", "UNLOAD-2"),
      {
        medianMinutesForSingleMat: 11 / 2, // two parts per face
        MAD_aboveMinutes: 1,
        MAD_belowMinutes: 1,
        expectedCycleMinutesForSingleMat: 100000, // not used
      },
    ],
  ]);

  const snapshot = applyConduitToSnapshot(snapshot_UNSTABLE(), onLoadLast30Log, evts);
  const cycles = snapshot.getLoadable(last30StationCycles).valueOrThrow();
  expect(recentCycles(cycles.valuesToLazySeq(), expected)).toMatchSnapshot("recent cycles");
});
