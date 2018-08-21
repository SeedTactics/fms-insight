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
import * as im from "immutable"; // consider collectable.js at some point?
import { duration } from "moment";
import { startOfDay } from "date-fns";

export interface SimStationUse {
  readonly station: string;
  readonly start: Date;
  readonly end: Date;
  readonly utilizationTime: number;
  readonly plannedDownTime: number;
}

export interface SimProduction {
  readonly part: string;
  readonly start: Date;
  readonly quantity: number;
}

export interface SimUseState {
  readonly station_use: im.List<SimStationUse>;
  readonly production: im.List<SimProduction>;
}

export const initial: SimUseState = {
  station_use: im.List(),
  production: im.List()
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
  let evtsSeq = im.Seq(newHistory.stationUse);
  let stations = st.station_use;
  let production = st.production;

  let newProd = im
    .Seq(newHistory.jobs)
    .valueSeq()
    .map(j => ({
      part: j.partName,
      start: j.routeStartUTC,
      quantity: j.cyclesOnFirstProcess.reduce((sum, p) => sum + p, 0)
    }));

  switch (expire.type) {
    case ExpireOldDataType.ExpireEarlierThan:
      // check if nothing to expire and no new data
      const minStat = stations.minBy(e => e.end);
      const minProd = production.minBy(e => e.start);

      if (
        (minStat === undefined || minStat.start >= expire.d) &&
        (minProd === undefined || minProd.start >= expire.d) &&
        evtsSeq.isEmpty() &&
        newProd.isEmpty()
      ) {
        return st;
      }

      // filter old events
      stations = stations.filter(e => e.start >= expire.d);
      production = production.filter(x => x.start >= expire.d);

      break;

    case ExpireOldDataType.NoExpire:
      if (evtsSeq.isEmpty() && newProd.isEmpty()) {
        return st;
      }
      break;
  }

  var newStationUse = evtsSeq.map(simUse => ({
    station: stat_name(simUse),
    start: simUse.startUTC,
    end: simUse.endUTC,
    utilizationTime: duration(simUse.utilizationTime).asMinutes(),
    plannedDownTime: duration(simUse.plannedDownTime).asMinutes()
  }));

  return {
    ...st,
    station_use: stations.concat(newStationUse),
    production: production.concat(newProd)
  };
}

type DayAndStation = im.Record<{ day: Date; station: string }>;
const mkDayAndStation = im.Record({ day: new Date(), station: "" });

interface DayStatAndVal {
  day: Date;
  station: string;
  value: number;
}

function splitElapsedToDays(simUse: SimStationUse, extractValue: (c: SimStationUse) => number): DayStatAndVal[] {
  const startDay = startOfDay(simUse.start);
  const endDay = startOfDay(simUse.end);

  if (startDay.getTime() === endDay.getTime()) {
    return [
      {
        day: startDay,
        station: simUse.station,
        value: extractValue(simUse)
      }
    ];
  } else {
    const totalVal = extractValue(simUse);
    const endDayPercentage =
      (simUse.end.getTime() - endDay.getTime()) / (simUse.end.getTime() - simUse.start.getTime());
    return [
      {
        day: startDay,
        station: simUse.station,
        value: totalVal * (1 - endDayPercentage)
      },
      {
        day: endDay,
        station: simUse.station,
        value: totalVal * endDayPercentage
      }
    ];
  }
}

export function binSimStationUseByDayAndStat(
  simUses: im.Seq.Indexed<SimStationUse>,
  extractValue: (c: SimStationUse) => number
): im.Map<DayAndStation, number> {
  return simUses
    .flatMap(s => splitElapsedToDays(s, extractValue))
    .groupBy(s => new mkDayAndStation({ day: s.day, station: s.station }))
    .map((points, group) => points.reduce((sum, p) => sum + p.value, 0))
    .toMap();
}

type DayAndPart = im.Record<{ day: Date; part: string }>;
const mkDayAndPart = im.Record({ day: new Date(), part: "" });

export function binSimProductionByDayAndPart(prod: im.Seq.Indexed<SimProduction>): im.Map<DayAndPart, number> {
  return (
    prod
      // TODO: split across day boundary?
      .groupBy(p => new mkDayAndPart({ day: startOfDay(p.start), part: p.part }))
      .map((points, group) => points.reduce((sum, p) => sum + p.quantity, 0))
      .toMap()
  );
}
