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
import { startOfDay, addMinutes, differenceInMinutes } from "date-fns";

export interface CycleData {
  readonly x: Date;
  readonly y: number; // cycle time in minutes
  readonly active: number; // active time in minutes
}

export interface PartCycleData extends CycleData {
  readonly part: string;
  readonly process: number;
  readonly stationGroup: string;
  readonly stationNumber: number;
  readonly isLabor: boolean;
  readonly completed: boolean; // did this cycle result in a completed part
}

export interface StatisticalCycleTime {
  readonly medianMinutes: number;
  readonly MAD_belowMinutes: number; // MAD of points below the median
  readonly MAD_aboveMinutes: number; // MAD of points below the median
  readonly expectedCycleMinutes: number;
}

export interface CycleState {
  readonly by_part_then_stat: im.Map<string, im.Map<string, ReadonlyArray<PartCycleData>>>;
  readonly by_pallet: im.Map<string, ReadonlyArray<CycleData>>;
  readonly station_groups: im.Set<string>;
  readonly estimatedCycleTimes: im.Map<string, im.Map<string, StatisticalCycleTime>>;
}

export const initial: CycleState = {
  by_part_then_stat: im.Map(),
  by_pallet: im.Map(),
  station_groups: im.Set(),
  estimatedCycleTimes: im.Map()
};

export enum ExpireOldDataType {
  ExpireEarlierThan,
  NoExpire
}

export type ExpireOldData =
  | { type: ExpireOldDataType.ExpireEarlierThan; d: Date }
  | { type: ExpireOldDataType.NoExpire };

function part_and_proc(m: api.ILogMaterial): string {
  return m.part + "-" + m.proc.toString();
}

function stat_group(e: Readonly<api.ILogEntry>): string {
  switch (e.type) {
    case api.LogType.LoadUnloadCycle:
    case api.LogType.MachineCycle:
    case api.LogType.Wash:
      return e.loc;
    case api.LogType.InspectionResult:
      return "Inspect " + e.program;
    default:
      return "";
  }
}

function stat_group_load_machine_only(e: Readonly<api.ILogEntry>): string {
  switch (e.type) {
    case api.LogType.LoadUnloadCycle:
    case api.LogType.MachineCycle:
      return e.loc;
    default:
      return "";
  }
}

function stat_name_and_num(e: PartCycleData): string {
  if (e.stationGroup.startsWith("Inspect")) {
    return e.stationGroup;
  } else {
    return e.stationGroup + " #" + e.stationNumber.toString();
  }
}

// Assume: samples come from two distributions:
//  - the program runs without interruption, giving a guassian iid around the cycle time.
//  - the program is interrupted or stopped, which adds a random amount to the program
//    and results in an outlier.
//  - the program doesn't run at all, which results in a random short cycle time.
// We use median absolute deviation to detect outliers, remove the outliers,
// then compute average to find cycle time.

function isOutlier(s: StatisticalCycleTime, mins: number): boolean {
  if (s.medianMinutes === 0) {
    return false;
  }
  if (mins < s.medianMinutes) {
    return (s.medianMinutes - mins) / s.MAD_belowMinutes > 2;
  } else {
    return (mins - s.medianMinutes) / s.MAD_aboveMinutes > 2;
  }
}

function median(vals: im.Seq.Indexed<number>): number {
  const sorted = vals.sort().toArray();
  const cnt = sorted.length;
  if (cnt === 0) {
    return 0;
  }
  const half = Math.floor(sorted.length / 2);
  if (sorted.length % 2 === 0) {
    // average two middle
    return (sorted[half - 1] + sorted[half]) / 2;
  } else {
    // return middle
    return sorted[half];
  }
}

function estimateCycleTimes(cycles: im.Seq.Indexed<number>): StatisticalCycleTime {
  // compute median
  const medianMinutes = median(cycles);

  // absolute deviation from median, but use different values for below and above
  // median.  Below is assumed to be from fake cycles and above is from interrupted programs.
  // since we assume gaussian, use consistantcy constant of 1.4826

  let madBelowMinutes = 1.4826 * median(cycles.filter(x => x <= medianMinutes).map(x => medianMinutes - x));
  if (madBelowMinutes < 0.01) {
    madBelowMinutes = 0.01;
  }

  let madAboveMinutes = 1.4826 * median(cycles.filter(x => x >= medianMinutes).map(x => x - medianMinutes));
  if (madAboveMinutes < 0.01) {
    madAboveMinutes = 0.01;
  }

  const statCycleTime = {
    medianMinutes,
    MAD_belowMinutes: madBelowMinutes,
    MAD_aboveMinutes: madAboveMinutes,
    expectedCycleMinutes: 0
  };

  // filter to only inliers
  var inliers = cycles.filter(x => !isOutlier(statCycleTime, x)).toArray();
  // compute average of inliers
  const expectedCycleMinutes = inliers.reduce((sum, x) => sum + x, 0) / inliers.length;

  return { ...statCycleTime, expectedCycleMinutes };
}

function estimateCycleTimesOfParts(
  cycles: Iterable<api.ILogEntry>
): im.Map<string, im.Map<string, StatisticalCycleTime>> {
  const ret = im
    .Seq(cycles)
    .filter(c => c.type === api.LogType.MachineCycle && !c.startofcycle)
    .flatMap(c => c.material.map(m => ({ cycle: c, mat: m })))
    .groupBy(e => part_and_proc(e.mat))
    .map(cyclesForPart =>
      cyclesForPart
        .groupBy(c => c.cycle.loc)
        .map(cyclesForPartAndStat =>
          estimateCycleTimes(
            cyclesForPartAndStat
              .map(p => duration(p.cycle.elapsed).asMinutes())
              .toIndexedSeq()
              .cacheResult()
          )
        )
        .toMap()
    )
    .toMap();
  return ret;
}

function activeMinutes(
  partAndProc: string,
  cycle: Readonly<api.ILogEntry>,
  estimated: im.Map<string, im.Map<string, StatisticalCycleTime>>
) {
  const cMins = duration(cycle.active).asMinutes();
  if (cycle.type === api.LogType.MachineCycle && (cycle.active === "" || cMins <= 0)) {
    const byStat = estimated.get(partAndProc);
    if (byStat) {
      const e = byStat.get(cycle.loc);
      if (e) {
        return e.expectedCycleMinutes;
      } else {
        return 0;
      }
    } else {
      return 0;
    }
  } else {
    return cMins;
  }
}

export function process_events(
  expire: ExpireOldData,
  newEvts: Iterable<api.ILogEntry>,
  initialLoad: boolean,
  st: CycleState
): CycleState {
  let evtsSeq = im.Seq(newEvts);
  let parts = st.by_part_then_stat;
  let pals = st.by_pallet;
  let statGroups = st.station_groups;

  let estimatedCycleTimes: im.Map<string, im.Map<string, StatisticalCycleTime>>;
  if (initialLoad) {
    estimatedCycleTimes = estimateCycleTimesOfParts(newEvts);
  } else {
    estimatedCycleTimes = st.estimatedCycleTimes;
  }

  switch (expire.type) {
    case ExpireOldDataType.ExpireEarlierThan:
      // check if nothing to expire and no new data
      const partEntries = parts
        .valueSeq()
        .flatMap(statMap => statMap.valueSeq())
        .flatMap(cs => cs);
      const palEntries = pals.valueSeq().flatMap(cs => cs);
      const minEntry = palEntries.concat(partEntries).minBy(e => e.x);

      if ((minEntry === undefined || minEntry.x >= expire.d) && evtsSeq.isEmpty()) {
        return st;
      }

      // filter old events
      parts = parts
        .map(statMap => statMap.map(es => es.filter(e => e.x >= expire.d)).filter(es => es.length > 0))
        .filter(statMap => !statMap.isEmpty());

      pals = pals.map(es => es.filter(e => e.x >= expire.d)).filter(es => es.length > 0);

      break;

    case ExpireOldDataType.NoExpire:
      if (evtsSeq.isEmpty()) {
        return st;
      }
      break;
  }

  var newPartCycles: im.Seq.Keyed<string, im.Map<string, ReadonlyArray<PartCycleData>>> = evtsSeq
    .filter(c => !c.startofcycle && (c.type === api.LogType.LoadUnloadCycle || c.type === api.LogType.MachineCycle))
    .flatMap(c => c.material.map(m => ({ cycle: c, mat: m })))
    .groupBy(e => part_and_proc(e.mat))
    .map((cyclesForPart, partAndProc) =>
      cyclesForPart
        .toSeq()
        .map(e => ({
          x: e.cycle.endUTC,
          y: duration(e.cycle.elapsed).asMinutes(),
          active: activeMinutes(partAndProc, e.cycle, estimatedCycleTimes),
          completed: e.cycle.type === api.LogType.LoadUnloadCycle && e.cycle.result === "UNLOAD",
          part: e.mat.part,
          process: e.mat.proc,
          isLabor: e.cycle.type === api.LogType.LoadUnloadCycle,
          stationGroup: stat_group(e.cycle),
          stationNumber: e.cycle.locnum
        }))
        .filter(c => c.stationGroup !== "")
        .groupBy(e => stat_name_and_num(e))
        .map(cycles => cycles.valueSeq().toArray())
        .toMap()
    );

  parts = parts.mergeWith(
    (oldStatMap, newStatMap) => oldStatMap.mergeWith((oldCs, newCs) => oldCs.concat(newCs), newStatMap),
    newPartCycles
  );

  var newPalCycles = evtsSeq
    .filter(c => !c.startofcycle && c.type === api.LogType.PalletCycle && c.pal !== "")
    .groupBy(c => c.pal)
    .map(cyclesForPal =>
      cyclesForPal
        .map(c => ({
          x: c.endUTC,
          y: duration(c.elapsed).asMinutes(),
          active: duration(c.active).asMinutes(),
          completed: false
        }))
        .valueSeq()
        .toArray()
    );
  pals = pals.mergeWith((oldCs, newCs) => oldCs.concat(newCs), newPalCycles);

  var newStatGroups = evtsSeq
    .map(stat_group_load_machine_only)
    .filter(s => s !== "" && !statGroups.has(s))
    .toSet();
  if (newStatGroups.size > 0) {
    statGroups = statGroups.union(newStatGroups);
  }

  const newSt = {
    ...st,
    by_part_then_stat: parts,
    by_pallet: pals,
    station_groups: statGroups
  };

  if (initialLoad) {
    return {
      ...newSt,
      estimatedCycleTimes: estimatedCycleTimes
    };
  } else {
    return newSt;
  }
}

function splitPartCycleToDays(cycle: CycleData, totalVal: number): Array<{ day: Date; value: number }> {
  const startDay = startOfDay(cycle.x);
  const endTime = addMinutes(cycle.x, totalVal);
  const endDay = startOfDay(endTime);
  if (startDay.getTime() === endDay.getTime()) {
    return [
      {
        day: startDay,
        value: totalVal
      }
    ];
  } else {
    const startDayPct = differenceInMinutes(endDay, cycle.x) / totalVal;
    return [
      {
        day: startDay,
        value: totalVal * startDayPct
      },
      {
        day: endDay,
        value: totalVal * (1 - startDayPct)
      }
    ];
  }
}

type DayAndStation = im.Record<{ day: Date; station: string }>;
const mkDayAndStation = im.Record({ day: new Date(), station: "" });

export function binCyclesByDayAndStat(
  byPartThenStat: im.Map<string, im.Map<string, ReadonlyArray<CycleData>>>,
  extractValue: (c: CycleData) => number
): im.Map<DayAndStation, number> {
  return byPartThenStat
    .valueSeq()
    .flatMap(byStation =>
      byStation
        .toSeq()
        .map((points, station) =>
          im.Seq(points).flatMap(point =>
            splitPartCycleToDays(point, extractValue(point)).map(x => ({
              ...x,
              station
            }))
          )
        )
        .valueSeq()
        .flatMap(x => x)
    )
    .groupBy(p => new mkDayAndStation({ day: p.day, station: p.station }))
    .map((points, group) => points.reduce((sum, p) => sum + p.value, 0))
    .toMap();
}

type DayAndPart = im.Record<{ day: Date; part: string }>;
const mkDayAndPart = im.Record({ day: new Date(), part: "" });

export function binCyclesByDayAndPart(
  byPartThenStat: im.Map<string, im.Map<string, ReadonlyArray<PartCycleData>>>,
  extractValue: (c: PartCycleData) => number
): im.Map<DayAndPart, number> {
  return byPartThenStat
    .toSeq()
    .map((byStation, part) =>
      byStation.valueSeq().flatMap(points =>
        im.Seq(points).map(point => ({
          day: startOfDay(point.x),
          part: part,
          value: extractValue(point)
        }))
      )
    )
    .valueSeq()
    .flatMap(x => x)
    .groupBy(p => new mkDayAndPart({ day: p.day, part: p.part }))
    .map((points, group) => points.reduce((sum, p) => sum + p.value, 0))
    .toMap();
}

export function stationMinutes(
  byPartThenStat: im.Map<string, im.Map<string, ReadonlyArray<PartCycleData>>>,
  cutoff: Date
): im.Map<string, number> {
  return byPartThenStat
    .valueSeq()
    .flatMap((byStation, part) =>
      byStation
        .toSeq()
        .map((points, station) =>
          im
            .Seq(points)
            .filter(p => p.x >= cutoff)
            .map(p => ({
              station,
              active: p.active
            }))
        )
        .valueSeq()
        .flatMap(x => x)
    )
    .groupBy(x => x.station)
    .map(points => points.reduce((sum, p) => sum + p.active, 0))
    .toMap();
}
