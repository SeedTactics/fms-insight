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
import { addHours, addMinutes } from "date-fns";

import { fakeCycle, fakeLoadOrUnload, fakeMachineCycle } from "../../test/events.fake.js";
import { ILogEntry } from "../network/api.js";
import {
  filterStationCycles,
  buildCycleTable,
  buildLogEntriesTable,
  recentCycles,
} from "./results.cycles.js";
import { onLoadLast30Log } from "../cell-status/loading.js";
import { last30StationCycles } from "../cell-status/station-cycles.js";
import { last30MaterialSummary } from "../cell-status/material-summary.js";
import { it, expect } from "vitest";
import { LazySeq } from "@seedtactics/immutable-collections";
import { createStore } from "jotai";

it("creates cycles clipboard table", () => {
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle({ time: now, machineTime: 30, counter: 100 }),
    fakeCycle({ time: addHours(now, -3), machineTime: 20, counter: 200 }),
    fakeCycle({ time: addHours(now, -15), machineTime: 15, counter: 300 }),
  );
  const snapshot = createStore();
  snapshot.set(onLoadLast30Log, evts);
  const cycles = snapshot.get(last30StationCycles);
  const matSummary = snapshot.get(last30MaterialSummary);

  const data = filterStationCycles(cycles.valuesToLazySeq(), {});

  const table = document.createElement("div");
  table.innerHTML = buildCycleTable(data, matSummary.matsById, undefined, undefined);
  expect(table).toMatchSnapshot("cycle clipboard table");

  table.innerHTML = buildCycleTable(data, matSummary.matsById, addHours(now, -3), now);
  expect(table).toMatchSnapshot("cycle filtered clipboard table");
});

it("creates log entries clipboard table", () => {
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle({ time: now, machineTime: 30, counter: 100 }),
    fakeCycle({ time: addHours(now, -3), machineTime: 20, counter: 200 }),
    fakeCycle({ time: addHours(now, -15), machineTime: 15, counter: 300 }),
  );
  const table = document.createElement("div");
  table.innerHTML = buildLogEntriesTable(evts);
  expect(table).toMatchSnapshot("events clipboard table");
});

it("calculates recent cycles", () => {
  const now = new Date(Date.UTC(2018, 2, 5, 7, 0, 0));
  const tenDaysAgo = addHours(now, -24 * 10);

  const evts =
    // use cycles 10 days ago to establish the expected cycle times for outliers
    LazySeq.ofRange(1, 300)
      .flatMap((i) =>
        fakeMachineCycle({
          counter: 2 * i,
          numMats: 3,
          part: "part1",
          proc: 1,
          program: "abcd",
          time: addMinutes(tenDaysAgo, i),
          // elapsed is random between 25 and 35
          elapsedMin: Math.random() * 10 + 25,
        }),
      )
      .concat(
        LazySeq.ofRange(1, 300).flatMap((i) =>
          fakeLoadOrUnload({
            counter: 600 + 2 * i,
            numMats: 2,
            part: "part3",
            proc: 1,
            isLoad: true,
            time: addMinutes(tenDaysAgo, 400 + i),
            // elapsed is random between 10 and 15
            elapsedMin: Math.random() * 5 + 10,
          }),
        ),
      )
      .concat([
        // one good machine cycle
        ...fakeMachineCycle({
          time: now,
          part: "part1",
          proc: 1,
          program: "abcd",
          numMats: 3,
          activeMin: 28,
          elapsedMin: 29,
          counter: 2000,
        }),

        // one outlier machine cycle
        ...fakeMachineCycle({
          time: addHours(now, 1),
          part: "part1",
          proc: 1,
          program: "abcd",
          numMats: 3,
          activeMin: 28,
          elapsedMin: 45,
          counter: 2010,
        }),

        // one load and one unload at the same time
        ...fakeLoadOrUnload({
          time: addHours(now, 2),
          part: "part3",
          proc: 1,
          lulNum: 2,
          numMats: 2,
          isLoad: true,
          elapsedMin: 12,
          activeMin: 8,
          counter: 3000,
        }),
        ...fakeLoadOrUnload({
          time: addHours(now, 2),
          part: "part4",
          proc: 2,
          lulNum: 2,
          numMats: 2,
          isLoad: false,
          elapsedMin: 21,
          activeMin: 12,
          counter: 4000,
        }),

        // one load and one unload at the same time but is outlier
        ...fakeLoadOrUnload({
          time: addHours(now, 3),
          part: "part3",
          proc: 1,
          isLoad: true,
          elapsedMin: 25, // outside range of 10 to 15
          numMats: 2,
          activeMin: 8,
          counter: 5000,
        }),
        ...fakeLoadOrUnload({
          time: addHours(now, 3),
          part: "part4",
          proc: 2,
          isLoad: false,
          elapsedMin: 28,
          numMats: 2,
          activeMin: 12,
          counter: 6000,
        }),
      ])
      .toRArray();

  const snapshot = createStore();
  snapshot.set(onLoadLast30Log, evts);

  const cycles = snapshot.get(last30StationCycles);
  expect(recentCycles(cycles.valuesToLazySeq().filter((e) => e.endTime >= now))).toMatchSnapshot(
    "recent cycles",
  );
});
