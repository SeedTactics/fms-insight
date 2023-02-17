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
import { durationToMinutes } from "../util/parseISODuration.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { atom, RecoilValueReadOnly, TransactionInterface_UNSTABLE } from "recoil";
import { addDays } from "date-fns";
import { conduit } from "../util/recoil-util.js";
import type { ServerEventAndTime } from "./loading.js";
import { IHistoricData, IJob } from "../network/api.js";

export interface SimPartCompleted {
  readonly partName: string;
  readonly process: number;
  readonly finalProcess: boolean;
  readonly completeTime: Date;
  readonly quantity: number;
  readonly expectedMachineMins: number; // expected machine minutes to entirely produce quantity
}

const last30SimProductionRW = atom<ReadonlyArray<SimPartCompleted>>({
  key: "last30SimProduction",
  default: [], // TODO: switch to persistent list
});
export const last30SimProduction: RecoilValueReadOnly<ReadonlyArray<SimPartCompleted>> =
  last30SimProductionRW;

const specificMonthSimProductionRW = atom<ReadonlyArray<SimPartCompleted>>({
  key: "specificMonthSimProduction",
  default: [],
});
export const specificMonthSimProduction: RecoilValueReadOnly<ReadonlyArray<SimPartCompleted>> =
  specificMonthSimProductionRW;

function* jobToPartCompleted(jobs: Iterable<Readonly<IJob>>): Iterable<SimPartCompleted> {
  for (const j of jobs) {
    for (let proc = 0; proc < j.procsAndPaths.length; proc++) {
      const procInfo = j.procsAndPaths[proc];
      for (let path = 0; path < procInfo.paths.length; path++) {
        const pathInfo = procInfo.paths[path];
        let machTime = 0;
        for (const stop of pathInfo.stops) {
          machTime += durationToMinutes(stop.expectedCycleTime);
        }
        let prevQty = 0;
        for (const prod of pathInfo.simulatedProduction || []) {
          yield {
            partName: j.partName,
            process: proc + 1,
            finalProcess: proc === j.procsAndPaths.length - 1,
            completeTime: prod.timeUTC,
            quantity: prod.quantity - prevQty,
            expectedMachineMins: machTime,
          };
          prevQty = prod.quantity;
        }
      }
    }
  }
}

export const setLast30JobProduction = conduit<Readonly<IHistoricData>>(
  (t: TransactionInterface_UNSTABLE, history: Readonly<IHistoricData>) => {
    t.set(last30SimProductionRW, (oldProd) => [
      ...oldProd,
      ...jobToPartCompleted(Object.values(history.jobs)),
    ]);
  }
);

export const updateLast30JobProduction = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt, now, expire }: ServerEventAndTime) => {
    if (evt.newJobs) {
      const apiNewJobs = evt.newJobs.jobs;
      t.set(last30SimProductionRW, (simProd) => {
        if (expire) {
          const expire = addDays(now, -30);
          // check if nothing to expire and no new data
          const minProd = LazySeq.of(simProd).minBy((e) => e.completeTime.getTime());
          if ((minProd === undefined || minProd.completeTime >= expire) && apiNewJobs.length === 0) {
            return simProd;
          }

          simProd = simProd.filter((e) => e.completeTime >= expire);
        }

        return [...simProd, ...jobToPartCompleted(apiNewJobs)];
      });
    }
  }
);

export const setSpecificMonthJobProduction = conduit<Readonly<IHistoricData>>(
  (t: TransactionInterface_UNSTABLE, history: Readonly<IHistoricData>) => {
    t.set(specificMonthSimProductionRW, Array.from(jobToPartCompleted(Object.values(history.jobs))));
  }
);
