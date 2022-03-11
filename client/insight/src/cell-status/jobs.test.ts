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

import { Snapshot, snapshot_UNSTABLE } from "recoil";
import { applyConduitToSnapshot } from "../util/recoil-util";
import { onLoadLast30Jobs, onLoadSpecificMonthJobs, onServerEvent } from "./loading";
import { addDays } from "date-fns";
import { HistoricJob, IHistoricData, NewJobs } from "../network/api";
import { last30SimProduction, specificMonthSimProduction } from "./sim-production";
import { last30SimStationUse, specificMonthSimStationUse } from "./sim-station-use";
import { last30Jobs, specificMonthJobs } from "./scheduled-jobs";
import newJobsJson from "../../test/newjobs.json";
import { LazySeq } from "../util/lazyseq";
import { it, expect } from "vitest";
import { ptsToJs } from "../../test/prelude-ts-snapshots";
const newJobs = newJobsJson.map((j) => NewJobs.fromJS(j));

function checkLast30(snapshot: Snapshot, msg: string) {
  expect(ptsToJs(snapshot.getLoadable(last30SimProduction).valueOrThrow())).toMatchSnapshot(msg + " - sim production");
  expect(ptsToJs(snapshot.getLoadable(last30SimStationUse).valueOrThrow())).toMatchSnapshot(msg + " - sim stations");
  expect(ptsToJs(snapshot.getLoadable(last30Jobs).valueOrThrow())).toMatchSnapshot(msg + " - jobs");
}

function jobsToHistory(newJs: Iterable<NewJobs>): IHistoricData {
  return {
    jobs: LazySeq.ofIterable(newJs)
      .flatMap((s) => s.jobs)
      .toMap(
        (j) => [j.unique, new HistoricJob({ ...j, copiedToSystem: true })],
        (a, _) => a
      )
      .toObjectDictionary((k) => k),
    stationUse: LazySeq.ofIterable(newJs)
      .flatMap((s) => s.stationUse ?? [])
      .toArray(),
  };
}

it("processes last 30 jobs", () => {
  const firstHalf = newJobs.slice(0, 15);

  // start with cycles from 27 days ago, 2 days ago, and today

  let snapshot = snapshot_UNSTABLE();
  snapshot = applyConduitToSnapshot(snapshot, onLoadLast30Jobs, jobsToHistory(firstHalf));

  checkLast30(snapshot, "first half");

  // Now add again 6 days from now so that twenty seven is removed from processing 30 days
  const secondHalf = newJobs.slice(16);
  const now = addDays(secondHalf[secondHalf.length - 1].jobs[0].routeEndUTC, 10);

  for (const nj of secondHalf) {
    snapshot = applyConduitToSnapshot(snapshot, onServerEvent, {
      now,
      expire: true,
      evt: {
        newJobs: nj,
      },
    });
  }

  checkLast30(snapshot, "after second half");
});

it("processes jobs in a specific month", () => {
  let snapshot = snapshot_UNSTABLE();
  // only the first 10 just to keep the size of the snapshots down
  snapshot = applyConduitToSnapshot(snapshot, onLoadSpecificMonthJobs, jobsToHistory(newJobs.slice(0, 10)));

  expect(ptsToJs(snapshot.getLoadable(specificMonthSimProduction).valueOrThrow())).toMatchSnapshot("sim production");
  expect(ptsToJs(snapshot.getLoadable(specificMonthSimStationUse).valueOrThrow())).toMatchSnapshot("sim stations");
  expect(ptsToJs(snapshot.getLoadable(specificMonthJobs).valueOrThrow())).toMatchSnapshot("jobs");
});
