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

import { fakeCycle } from "../../test/events.fake";
import { Snapshot, snapshot_UNSTABLE } from "recoil";
import { applyConduitToSnapshot } from "../util/recoil-util";
import { onLoadLast30Log, onServerEvent } from "./loading";
import { addDays } from "date-fns";
import { last30BufferEntries } from "./buffers";
import { last30EstimatedCycleTimes } from "./estimated-cycle-times";
import { last30Inspections } from "./inspections";
import { last30MaterialSummary } from "./material-summary";
import { last30PalletCycles } from "./pallet-cycles";
import { last30StationCycles } from "./station-cycles";
import { last30ToolUse } from "./tool-usage";
import { LogEntry } from "../network/api";

function checkLast30(snapshot: Snapshot, msg: string) {
  expect(snapshot.getLoadable(last30BufferEntries).valueOrThrow()).toMatchSnapshot(msg + " - buffers");
  expect(snapshot.getLoadable(last30EstimatedCycleTimes).valueOrThrow()).toMatchSnapshot(
    msg + " - estimated cycle times"
  );
  expect(snapshot.getLoadable(last30Inspections).valueOrThrow()).toMatchSnapshot(msg + " - inspections");
  expect(snapshot.getLoadable(last30MaterialSummary).valueOrThrow()).toMatchSnapshot(msg + " - material summary");
  expect(snapshot.getLoadable(last30PalletCycles).valueOrThrow()).toMatchSnapshot(msg + " - pallet cycles");
  expect(snapshot.getLoadable(last30StationCycles).valueOrThrow()).toMatchSnapshot(msg + " - station cycles");
  expect(snapshot.getLoadable(last30ToolUse).valueOrThrow()).toMatchSnapshot(msg + " - tool use");
}

it("processes last 30 events", () => {
  const now = new Date(Date.UTC(2018, 1, 2, 9, 4, 5));

  // start with cycles from 27 days ago, 2 days ago, and today
  const todayCycle = fakeCycle({
    time: now,
    machineTime: 30,
    part: "part111",
    proc: 1,
    pallet: "palbb",
    includeTools: true,
  });
  const twoDaysAgo = addDays(now, -2);
  const twoDaysAgoCycle = fakeCycle({
    time: twoDaysAgo,
    machineTime: 24,
    part: "part222",
    proc: 1,
    pallet: "palbb",
    includeTools: true,
  });
  const twentySevenDaysAgo = addDays(now, -27);
  const twentySevenCycle = fakeCycle({
    time: twentySevenDaysAgo,
    machineTime: 18,
    part: "part222",
    proc: 2,
    pallet: "palaa",
    includeTools: true,
  });

  let snapshot = snapshot_UNSTABLE();
  snapshot = applyConduitToSnapshot(snapshot, onLoadLast30Log, [
    ...twentySevenCycle,
    ...twoDaysAgoCycle,
    ...todayCycle,
  ]);

  checkLast30(snapshot, "initial 27 days ago, two days ago, and today");

  // Now add again 6 days from now so that twenty seven is removed from processing 30 days
  const sixDays = addDays(now, 6);
  const sixDaysCycle = fakeCycle({
    time: sixDays,
    machineTime: 12,
    part: "part111",
    proc: 1,
    pallet: "palcc",
    includeTools: true,
  });

  for (const c of sixDaysCycle) {
    snapshot = applyConduitToSnapshot(snapshot, onServerEvent, {
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

// TODO: load specific month
