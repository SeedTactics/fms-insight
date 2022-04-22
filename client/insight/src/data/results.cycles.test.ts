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
import { addDays, addHours } from "date-fns";

import { fakeCycle } from "../../test/events.fake";
import { ILogEntry } from "../network/api";
import {
  stationMinutes,
  filterStationCycles,
  buildCycleTable,
  buildLogEntriesTable,
  outlierLoadCycles,
  outlierMachineCycles,
} from "./results.cycles";
import { applyConduitToSnapshot } from "../util/recoil-util";
import { snapshot_UNSTABLE } from "recoil";
import { onLoadLast30Log } from "../cell-status/loading";
import { last30StationCycles } from "../cell-status/station-cycles";
import { last30MaterialSummary } from "../cell-status/material-summary";
import { last30EstimatedCycleTimes } from "../cell-status/estimated-cycle-times";
import { it, expect } from "vitest";
import { toRawJs } from "../../test/to-raw-js";

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
  expect(data).toMatchSnapshot("filtered cycles");

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

it("computes station oee", () => {
  const now = new Date(2018, 2, 5);

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle({ time: now, machineTime: 30, counter: 100 }),
    fakeCycle({ time: addDays(now, -3), machineTime: 20, counter: 200 }),
    fakeCycle({ time: addDays(now, -15), machineTime: 15, counter: 300 })
  );
  const snapshot = applyConduitToSnapshot(snapshot_UNSTABLE(), onLoadLast30Log, evts);
  const cycles = snapshot.getLoadable(last30StationCycles).valueOrThrow();

  const statMins = stationMinutes(cycles.valuesToLazySeq(), addDays(now, -7));
  expect(toRawJs(statMins)).toMatchSnapshot("station minutes for last week");
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
