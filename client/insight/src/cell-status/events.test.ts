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

import { fakeCycle } from "../../test/events.fake.js";
import { lastEventCounter, onLoadLast30Log, onLoadSpecificMonthLog, onServerEvent } from "./loading.js";
import { addDays } from "date-fns";
import { last30BufferEntries, specificMonthBufferEntries } from "./buffers.js";
import { last30EstimatedCycleTimes, specificMonthEstimatedCycleTimes } from "./estimated-cycle-times.js";
import { last30Inspections, specificMonthInspections } from "./inspections.js";
import { last30MaterialSummary, specificMonthMaterialSummary } from "./material-summary.js";
import { last30PalletCycles, specificMonthPalletCycles } from "./pallet-cycles.js";
import { last30StationCycles, specificMonthStationCycles } from "./station-cycles.js";
import { last30ToolUse } from "./tool-usage.js";
import { LogEntry } from "../network/api.js";
import { it, expect } from "vitest";

import { toRawJs } from "../../test/to-raw-js.js";
import { createStore } from "jotai";

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
