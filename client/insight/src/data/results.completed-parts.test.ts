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

import { fakeCycle } from "../../test/events.fake.js";
import { ILogEntry } from "../network/api.js";
import { binCyclesByDayAndPart, buildCompletedPartsHeatmapTable } from "./results.completed-parts.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { applyConduitToSnapshot } from "../util/recoil-util.js";
import { snapshot_UNSTABLE } from "recoil";
import { onLoadLast30Log } from "../cell-status/loading.js";
import { last30StationCycles } from "../cell-status/station-cycles.js";
import { last30MaterialSummary } from "../cell-status/material-summary.js";
import { it, expect } from "vitest";
import { toRawJs } from "../../test/to-raw-js.js";

it("bins actual cycles by day", () => {
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle({ time: now, machineTime: 30, counter: 100 }),
    fakeCycle({ time: addHours(now, -3), machineTime: 20, counter: 200 }),
    fakeCycle({ time: addHours(now, -15), machineTime: 15, counter: 300 })
  );
  const snapshot = applyConduitToSnapshot(snapshot_UNSTABLE(), onLoadLast30Log, evts);
  const cycles = snapshot.getLoadable(last30StationCycles).valueOrThrow();
  const matSummary = snapshot.getLoadable(last30MaterialSummary).valueOrThrow();

  let byDayAndPart = binCyclesByDayAndPart(
    cycles.valuesToLazySeq(),
    matSummary.matsById,
    addDays(now, -30),
    now
  );

  const points = LazySeq.of(byDayAndPart)
    .map(([dayAndPart, val]) => ({
      x: dayAndPart.day,
      y: dayAndPart.part,
      label: "Unused",
      count: val.count,
      activeMachineMins: val.activeMachineMins,
    }))
    .toRArray();

  const heattable = document.createElement("div");
  heattable.innerHTML = buildCompletedPartsHeatmapTable(points);
  expect(heattable).toMatchSnapshot("heatmap clipboard table");

  // convert to chicago time because snapshot includes date and time in UTC when formatting the byDayAndPart snapshot
  const nowChicago = new Date(Date.UTC(2018, 2, 5, 6, 0, 0)); // America/Chicago time
  const minOffset = differenceInMinutes(nowChicago, now);
  byDayAndPart = byDayAndPart
    .toLazySeq()
    .toHashMap(([dayAndPart, val]) => [dayAndPart.adjustDay((d) => addMinutes(d, minOffset)), val]);

  expect(toRawJs(byDayAndPart)).toMatchSnapshot("cycles binned by day and part");
});
