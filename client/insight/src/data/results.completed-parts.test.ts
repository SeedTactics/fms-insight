/* Copyright (c) 2020, John Lenz

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

import { PledgeStatus } from "../store/middleware";
import * as events from "./events";
import { fakeCycle } from "./events.fake";
import { ILogEntry } from "./api";
import { binCyclesByDayAndPart, buildCompletedPartsHeatmapTable } from "./results.completed-parts";
import { LazySeq } from "./lazyseq";

it("bins actual cycles by day", () => {
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

  let byDayAndPart = binCyclesByDayAndPart(
    st.last30.cycles.part_cycles,
    st.last30.mat_summary.matsById,
    addDays(now, -30),
    now
  );

  const points = LazySeq.ofIterable(byDayAndPart)
    .map(([dayAndPart, val]) => ({
      x: dayAndPart.day,
      y: dayAndPart.part,
      label: "Unused",
      count: val.count,
      activeMachineMins: val.activeMachineMins,
    }))
    .toArray();

  const heattable = document.createElement("div");
  heattable.innerHTML = buildCompletedPartsHeatmapTable(points);
  expect(heattable).toMatchSnapshot("heatmap clipboard table");

  // convert to chicago time because snapshot includes date and time in UTC when formatting the byDayAndPart snapshot
  const nowChicago = new Date(Date.UTC(2018, 2, 5, 6, 0, 0)); // America/Chicago time
  const minOffset = differenceInMinutes(nowChicago, now);
  byDayAndPart = byDayAndPart.map((dayAndPart, val) => [dayAndPart.adjustDay((d) => addMinutes(d, minOffset)), val]);

  expect(byDayAndPart).toMatchSnapshot("cycles binned by day and part");
});
