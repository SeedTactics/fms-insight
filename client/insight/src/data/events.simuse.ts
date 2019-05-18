/* Copyright (c) 2018, John Lenz

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
import * as api from "./api";
import { duration } from "moment";
import { Vector } from "prelude-ts";
import { LazySeq } from "./lazyseq";
import { part_and_proc } from "./events.cycles";

export interface SimStationUse {
  readonly station: string;
  readonly start: Date;
  readonly end: Date;
  readonly utilizationTime: number;
  readonly plannedDownTime: number;
}

export interface SimPartCompleted {
  readonly part: string;
  readonly completeTime: Date;
  readonly quantity: number;
  readonly expectedMachineMins: number; // expected machine minutes to entirely produce quantity
}

export interface SimUseState {
  readonly station_use: Vector<SimStationUse>;
  readonly production: Vector<SimPartCompleted>;
}

export const initial: SimUseState = {
  station_use: Vector.empty(),
  production: Vector.empty()
};

export enum ExpireOldDataType {
  ExpireEarlierThan,
  NoExpire
}

export type ExpireOldData =
  | { type: ExpireOldDataType.ExpireEarlierThan; d: Date }
  | { type: ExpireOldDataType.NoExpire };

function stat_name(e: api.ISimulatedStationUtilization): string {
  return e.stationGroup + " #" + e.stationNum;
}

export function process_sim_use(
  expire: ExpireOldData,
  newHistory: Readonly<api.IHistoricData>,
  st: SimUseState
): SimUseState {
  let stations = st.station_use;
  let production = st.production;

  switch (expire.type) {
    case ExpireOldDataType.ExpireEarlierThan:
      // check if nothing to expire and no new data
      const minStat = stations.minOn(e => e.end.getTime());
      const minProd = production.minOn(e => e.completeTime.getTime());

      if (
        (minStat.isNone() || minStat.get().start >= expire.d) &&
        (minProd.isNone() || minProd.get().completeTime >= expire.d) &&
        newHistory.stationUse.length === 0 &&
        Object.keys(newHistory.jobs).length === 0
      ) {
        return st;
      }

      // filter old events
      stations = stations.filter(e => e.start >= expire.d);
      production = production.filter(x => x.completeTime >= expire.d);

      break;

    case ExpireOldDataType.NoExpire:
      if (newHistory.stationUse.length === 0 && Object.keys(newHistory.jobs).length === 0) {
        return st;
      }
      break;
  }

  var newStationUse = LazySeq.ofIterable(newHistory.stationUse).map(simUse => ({
    station: stat_name(simUse),
    start: simUse.startUTC,
    end: simUse.endUTC,
    utilizationTime: duration(simUse.utilizationTime).asMinutes(),
    plannedDownTime: duration(simUse.plannedDownTime).asMinutes()
  }));

  let newProd = LazySeq.ofObject(newHistory.jobs).flatMap(function*([_, jParam]: [string, api.JobPlan]) {
    const j = jParam;
    for (let proc = 0; proc < j.procsAndPaths.length; proc++) {
      const procInfo = j.procsAndPaths[proc];
      for (let path = 0; path < procInfo.paths.length; path++) {
        const pathInfo = procInfo.paths[path];
        let machTime = 0;
        for (let stop of pathInfo.stops) {
          machTime += duration(stop.expectedCycleTime).asMinutes();
        }
        let prevQty = 0;
        for (let prod of pathInfo.simulatedProduction || []) {
          yield {
            part: part_and_proc(j.partName, proc + 1),
            completeTime: prod.timeUTC,
            quantity: prod.quantity - prevQty,
            expectedMachineMins: machTime
          };
          prevQty = prod.quantity;
        }
      }
    }
  });

  return {
    ...st,
    station_use: stations.appendAll(newStationUse),
    production: production.appendAll(newProd)
  };
}
