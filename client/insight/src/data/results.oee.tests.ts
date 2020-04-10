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

import { addDays, addHours, differenceInMinutes, addMinutes } from "date-fns";
import { duration } from "moment";

import { PledgeStatus } from "../store/middleware";
import * as events from "./events";
import { fakeCycle } from "./events.fake";
import { ILogEntry } from "./api";
import { LazySeq } from "./lazyseq";
import { binCyclesByDayAndStat, buildOeeHeatmapTable } from "./results.oee";

it("bins actual cycles by day", () => {
  const now = new Date(2018, 2, 5); // midnight in local time
  const nowChicago = new Date(Date.UTC(2018, 2, 5, 6, 0, 0)); // America/Chicago time
  const minOffset = differenceInMinutes(nowChicago, now);

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
      result: evts,
    },
  });

  let byDayAndStat = binCyclesByDayAndStat(st.last30.cycles.part_cycles, (c) => duration(c.activeMinutes).asMinutes());

  // update day to be in Chicago timezone
  // This is because the snapshot formats the day as a UTC time in Chicago timezone
  // Note this is after cycles are binned, which is correct since cycles are generated using
  // now in local time and then binned in local time.  Just need to update the date before
  // comparing with the snapshot
  byDayAndStat = byDayAndStat.map((dayAndStat, val) => [dayAndStat.adjustDay((d) => addMinutes(d, minOffset)), val]);

  expect(byDayAndStat).toMatchSnapshot("cycles binned by day and station");
});

it("creates points clipboard table", () => {
  // table formats columns in local time, so no need to convert to a specific timezone
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
      result: evts,
    },
  });

  const byDayAndStat = binCyclesByDayAndStat(st.last30.cycles.part_cycles, (c) =>
    duration(c.activeMinutes).asMinutes()
  );

  const points = LazySeq.ofIterable(byDayAndStat)
    .map(([dayAndStat, val]) => ({
      x: dayAndStat.day,
      y: dayAndStat.station,
      label: val.toString(),
    }))
    .toArray();

  const table = document.createElement("div");
  table.innerHTML = buildOeeHeatmapTable("Station", points);
  expect(table).toMatchSnapshot("clipboard table");
});

it.skip("bins simulated station use");

it.skip("calculates combined oee series and creates table");
