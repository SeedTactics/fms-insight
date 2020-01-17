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

import { PledgeStatus } from "../store/middleware";
import * as events from "./events";
import { fakeCycle } from "./events.fake";
import { ILogEntry } from "./api";
import {
  stationMinutes,
  filterStationCycles,
  buildCycleTable,
  buildLogEntriesTable,
  outlierLoadCycles,
  outlierMachineCycles
} from "./results.cycles";

it("creates cycles clipboard table", () => {
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle(now, 30),
    fakeCycle(addHours(now, -3), 20),
    fakeCycle(addHours(now, -15), 15)
  );
  const st = events.reducer(events.initial, {
    type: events.ActionType.LoadRecentLogEntries,
    now: addDays(now, 1),
    pledge: {
      status: PledgeStatus.Completed,
      result: evts
    }
  });
  const data = filterStationCycles(st.last30.cycles.part_cycles, undefined, undefined, undefined);

  const table = document.createElement("div");
  table.innerHTML = buildCycleTable(data, true, undefined, undefined);
  expect(table).toMatchSnapshot("cycle clipboard table");

  table.innerHTML = buildCycleTable(data, true, addHours(now, -3), now);
  expect(table).toMatchSnapshot("cycle filtered clipboard table");
});

it("loads outlier cycles", () => {
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle(now, 30),
    fakeCycle(addHours(now, -3), 20),
    fakeCycle(addHours(now, -15), 15)
  );
  const st = events.reducer(events.initial, {
    type: events.ActionType.LoadRecentLogEntries,
    now: addDays(now, 1),
    pledge: {
      status: PledgeStatus.Completed,
      result: evts
    }
  });

  const loadOutliers = outlierLoadCycles(
    st.last30.cycles.part_cycles,
    new Date(2018, 0, 1),
    new Date(2018, 11, 1),
    st.last30.cycles.estimatedCycleTimes
  );
  expect(loadOutliers.data.length()).toBe(0);

  const machineOutliers = outlierMachineCycles(
    st.last30.cycles.part_cycles,
    new Date(2018, 0, 1),
    new Date(2018, 11, 1),
    st.last30.cycles.estimatedCycleTimes
  );
  expect(machineOutliers.data.length()).toBe(0);
});

it("computes station oee", () => {
  const now = new Date(2018, 2, 5);

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle(now, 30),
    fakeCycle(addDays(now, -3), 20),
    fakeCycle(addDays(now, -15), 15)
  );
  const st = events.reducer(events.initial, {
    type: events.ActionType.LoadRecentLogEntries,
    now: addDays(now, 1),
    pledge: {
      status: PledgeStatus.Completed,
      result: evts
    }
  });

  const statMins = stationMinutes(st.last30.cycles.part_cycles, addDays(now, -7));
  expect(statMins).toMatchSnapshot("station minutes for last week");
});

it("creates log entries clipboard table", () => {
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle(now, 30),
    fakeCycle(addHours(now, -3), 20),
    fakeCycle(addHours(now, -15), 15)
  );
  const table = document.createElement("div");
  table.innerHTML = buildLogEntriesTable(evts);
  expect(table).toMatchSnapshot("events clipboard table");
});
