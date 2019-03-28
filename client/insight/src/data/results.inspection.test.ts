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

import { addDays, differenceInSeconds } from "date-fns";

import * as inspection from "./events.inspection";
import * as inspEvts from "./events.inspection";
import { loadMockData } from "../mock-data/load";
import { groupInspectionsByPath, buildInspectionTable } from "./results.inspection";

it("groups inspections by path", async () => {
  const jan18 = new Date(Date.UTC(2018, 0, 1, 0, 0, 0));
  const offsetSeconds = differenceInSeconds(addDays(new Date(Date.UTC(2018, 7, 6, 15, 39, 0)), -28), jan18);
  const data = loadMockData(offsetSeconds);
  const evts = await data.events;
  const inspState = inspEvts.process_events(
    { type: inspEvts.ExpireOldDataType.NoExpire },
    evts,
    undefined,
    inspEvts.initial
  );

  const entries = inspState.by_part.get(new inspection.PartAndInspType("aaa", "CMM")).getOrThrow();
  const range = { start: new Date(Date.UTC(2018, 7, 1)), end: new Date(Date.UTC(2018, 7, 4)) };
  const groups = groupInspectionsByPath(entries, range, e => e.serial || "");
  expect(groups).toMatchSnapshot("grouped inspections");
});

it("copies inspections by path to clipboard", async () => {
  const jan18 = new Date(Date.UTC(2018, 0, 1, 0, 0, 0));
  // use local time zone for offset since clipboard also uses local time
  const offsetSeconds = differenceInSeconds(addDays(new Date(2018, 7, 6, 15, 39, 0), -28), jan18);
  const data = loadMockData(offsetSeconds);
  const evts = await data.events;
  const inspState = inspEvts.process_events(
    { type: inspEvts.ExpireOldDataType.NoExpire },
    evts,
    undefined,
    inspEvts.initial
  );

  const entries = inspState.by_part.get(new inspection.PartAndInspType("aaa", "CMM")).getOrThrow();
  const table = document.createElement("div");
  table.innerHTML = buildInspectionTable("aaa", "CMM", entries);
  expect(table).toMatchSnapshot("inspection clipboard table");
});

it.skip("extracts failed inspections", () => undefined);
