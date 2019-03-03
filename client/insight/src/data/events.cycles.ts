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
import { startOfDay, addMinutes, differenceInMinutes } from "date-fns";
import { HashMap, HashSet, Vector, fieldsHashCode } from "prelude-ts";
import { LazySeq } from "./lazyseq";

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
  readonly pallet: string;
  readonly isLabor: boolean;
  readonly matId: number;
  readonly serial?: string;
  readonly workorder?: string;
  readonly completed: boolean; // did this cycle result in a completed part
  readonly signaledInspections: HashSet<string>;
  readonly completedInspections: HashMap<string, boolean>; // boolean is if successful or failed
  readonly operator: string;
}

export interface StatisticalCycleTime {
  readonly medianMinutes: number;
  readonly MAD_belowMinutes: number; // MAD of points below the median
  readonly MAD_aboveMinutes: number; // MAD of points below the median
  readonly expectedCycleMinutes: number;
}

export interface CycleState {
  readonly part_cycles: Vector<PartCycleData>;
  readonly by_pallet: HashMap<string, ReadonlyArray<CycleData>>;

  readonly part_and_proc_names: HashSet<string>;
  readonly station_groups: HashSet<string>;
  readonly station_names: HashSet<string>;
  readonly pallet_names: HashSet<string>;
  readonly estimatedCycleTimes: HashMap<string, HashMap<string, StatisticalCycleTime>>;
}

export const initial: CycleState = {
  part_cycles: Vector.empty(),
  part_and_proc_names: HashSet.empty(),
  by_pallet: HashMap.empty(),
  station_groups: HashSet.empty(),
  station_names: HashSet.empty(),
  pallet_names: HashSet.empty(),
  estimatedCycleTimes: HashMap.empty()
};

export enum ExpireOldDataType {
  ExpireEarlierThan,
  NoExpire
}

export type ExpireOldData =
  | { type: ExpireOldDataType.ExpireEarlierThan; d: Date }
  | { type: ExpireOldDataType.NoExpire };

function part_and_proc(part: string, proc: number): string {
  return part + "-" + proc.toString();
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

export function stat_name_and_num(stationGroup: string, stationNumber: number): string {
  if (stationGroup.startsWith("Inspect")) {
    return stationGroup;
  } else {
    return stationGroup + " #" + stationNumber.toString();
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

function median(vals: LazySeq<number>): number {
  const sorted = vals.toArray().sort();
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

function estimateCycleTimes(cycles: Vector<number>): StatisticalCycleTime {
  // compute median
  const medianMinutes = median(LazySeq.ofIterable(cycles));

  // absolute deviation from median, but use different values for below and above
  // median.  Below is assumed to be from fake cycles and above is from interrupted programs.
  // since we assume gaussian, use consistantcy constant of 1.4826

  let madBelowMinutes =
    1.4826 *
    median(
      LazySeq.ofIterable(cycles)
        .filter(x => x <= medianMinutes)
        .map(x => medianMinutes - x)
    );
  if (madBelowMinutes < 0.01) {
    madBelowMinutes = 0.01;
  }

  let madAboveMinutes =
    1.4826 *
    median(
      LazySeq.ofIterable(cycles)
        .filter(x => x >= medianMinutes)
        .map(x => x - medianMinutes)
    );
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
): HashMap<string, HashMap<string, StatisticalCycleTime>> {
  const ret = LazySeq.ofIterable(cycles)
    .filter(c => c.type === api.LogType.MachineCycle && !c.startofcycle)
    .flatMap(c => c.material.map(m => ({ cycle: c, mat: m })))
    .groupBy(e => part_and_proc(e.mat.part, e.mat.proc))
    .mapValues(cyclesForPart =>
      LazySeq.ofIterable(cyclesForPart)
        .groupBy(c => c.cycle.loc)
        .mapValues(cyclesForPartAndStat =>
          estimateCycleTimes(cyclesForPartAndStat.map(p => duration(p.cycle.elapsed).asMinutes()))
        )
    );
  return ret;
}

function activeMinutes(
  partAndProc: string,
  cycle: Readonly<api.ILogEntry>,
  estimated: HashMap<string, HashMap<string, StatisticalCycleTime>>
) {
  const cMins = duration(cycle.active).asMinutes();
  if (cycle.type === api.LogType.MachineCycle && (cycle.active === "" || cMins <= 0)) {
    const byStat = estimated.get(partAndProc);
    if (byStat.isSome()) {
      const e = byStat.get().get(cycle.loc);
      if (e.isSome()) {
        return e.get().expectedCycleMinutes;
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

interface InspectionData {
  readonly signaled: { [materialId: number]: HashSet<string> };
  readonly result: { [materialId: number]: HashMap<string, boolean> };
}

function newInspectionData(newEvts: ReadonlyArray<Readonly<api.ILogEntry>>): InspectionData {
  const signaled: { [materialId: number]: HashSet<string> } = {};
  const result: { [materialId: number]: HashMap<string, boolean> } = {};
  for (const evt of newEvts) {
    switch (evt.type) {
      case api.LogType.Inspection:
        const inspName = (evt.details || {}).InspectionType;
        const inspected = evt.result.toLowerCase() === "true" || evt.result === "1";
        if (inspected && inspName) {
          for (const m of evt.material) {
            signaled[m.id] = (signaled[m.id] || HashSet.empty()).add(inspName);
          }
        }
        break;

      case api.LogType.InspectionForce:
        const forceInspName = evt.program;
        let forced = evt.result.toLowerCase() === "true" || evt.result === "1";
        if (forceInspName && forced) {
          for (const m of evt.material) {
            signaled[m.id] = (signaled[m.id] || HashSet.empty()).add(forceInspName);
          }
        }
        break;

      case api.LogType.InspectionResult:
        const resultInspName = evt.program;
        const succeeded = evt.result.toLowerCase() !== "false";
        if (resultInspName) {
          for (const m of evt.material) {
            result[m.id] = (result[m.id] || HashMap.empty()).put(resultInspName, succeeded);
          }
        }
        break;
    }
  }
  return { signaled, result };
}

export function process_events(
  expire: ExpireOldData,
  newEvts: ReadonlyArray<Readonly<api.ILogEntry>>,
  initialLoad: boolean,
  st: CycleState
): CycleState {
  let allPartCycles = st.part_cycles;
  let pals = st.by_pallet;

  let estimatedCycleTimes: HashMap<string, HashMap<string, StatisticalCycleTime>>;
  if (initialLoad) {
    estimatedCycleTimes = estimateCycleTimesOfParts(newEvts);
  } else {
    estimatedCycleTimes = st.estimatedCycleTimes;
  }

  switch (expire.type) {
    case ExpireOldDataType.ExpireEarlierThan:
      // check if nothing to expire and no new data
      const partEntries = LazySeq.ofIterable(allPartCycles);
      const palEntries = LazySeq.ofIterable(pals.valueIterable()).flatMap(cs => cs);
      const minEntry = palEntries.appendAll(partEntries).minOn(e => e.x.getTime());

      if ((minEntry.isNone() || minEntry.get().x >= expire.d) && newEvts.length === 0) {
        return st;
      }

      // filter old events
      allPartCycles = allPartCycles.filter(e => e.x >= expire.d);
      pals = pals.mapValues(es => es.filter(e => e.x >= expire.d)).filter(es => es.length > 0);

      break;

    case ExpireOldDataType.NoExpire:
      if (newEvts.length === 0) {
        return st;
      }
      break;
  }

  var newCycles: LazySeq<PartCycleData> = LazySeq.ofIterable(newEvts)
    .filter(c => !c.startofcycle && (c.type === api.LogType.LoadUnloadCycle || c.type === api.LogType.MachineCycle))
    .flatMap(c => c.material.map(m => ({ cycle: c, mat: m })))
    .map(e => ({
      x: e.cycle.endUTC,
      y: duration(e.cycle.elapsed).asMinutes(),
      active: activeMinutes(part_and_proc(e.mat.part, e.mat.proc), e.cycle, estimatedCycleTimes),
      completed: e.cycle.type === api.LogType.LoadUnloadCycle && e.cycle.result === "UNLOAD",
      part: e.mat.part,
      process: e.mat.proc,
      pallet: e.cycle.pal,
      matId: e.mat.id,
      isLabor: e.cycle.type === api.LogType.LoadUnloadCycle,
      serial: e.mat.serial,
      workorder: e.mat.workorder,
      stationGroup: stat_group(e.cycle),
      stationNumber: e.cycle.locnum,
      signaledInspections: HashSet.empty<string>(),
      completedInspections: HashMap.empty<string, boolean>(),
      operator: e.cycle.details ? e.cycle.details.operator || "" : ""
    }))
    .filter(c => c.stationGroup !== "");

  let partNames = st.part_and_proc_names;
  let statNames = st.station_names;
  let statGroups = st.station_groups;
  let palNames = st.pallet_names;
  for (let e of newEvts) {
    for (let m of e.material) {
      const p = part_and_proc(m.part, m.proc);
      if (!partNames.contains(p)) {
        partNames = partNames.add(p);
      }
    }
    if (e.pal !== "") {
      if (!palNames.contains(e.pal)) {
        palNames = palNames.add(e.pal);
      }
    }
    const statGroup = stat_group_load_machine_only(e);
    if (statGroup !== "") {
      if (!statGroups.contains(statGroup)) {
        statGroups = statGroups.add(statGroup);
      }
      const statName = stat_name_and_num(statGroup, e.locnum);
      if (!statNames.contains(statName)) {
        statNames = statNames.add(statName);
      }
    }
  }

  var newPalCycles = LazySeq.ofIterable(newEvts)
    .filter(c => !c.startofcycle && c.type === api.LogType.PalletCycle && c.pal !== "")
    .groupBy(c => c.pal)
    .mapValues(cyclesForPal =>
      LazySeq.ofIterable(cyclesForPal)
        .map(c => ({
          x: c.endUTC,
          y: duration(c.elapsed).asMinutes(),
          active: duration(c.active).asMinutes(),
          completed: false
        }))
        .toArray()
    );
  pals = pals.mergeWith(newPalCycles, (oldCs, newCs) => oldCs.concat(newCs));

  // merge inspections
  const inspResult = newInspectionData(newEvts);
  allPartCycles = allPartCycles.appendAll(newCycles).map(x => {
    const signaled = inspResult.signaled[x.matId];
    if (signaled) {
      x = { ...x, signaledInspections: x.signaledInspections.addAll(signaled) };
    }
    const result = inspResult.result[x.matId];
    if (result) {
      x = { ...x, completedInspections: x.completedInspections.mergeWith(result, (_a, b) => b) };
    }
    return x;
  });

  const newSt = {
    ...st,
    part_cycles: allPartCycles,
    part_and_proc_names: partNames,
    by_pallet: pals,
    station_groups: statGroups,
    station_names: statNames,
    pallet_names: palNames
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

export interface FilteredStationCycles {
  readonly seriesLabel: string;
  readonly data: HashMap<string, ReadonlyArray<PartCycleData>>;
}

export function filterStationCycles(
  allCycles: Vector<PartCycleData>,
  partAndProc?: string,
  pallet?: string,
  station?: string
): FilteredStationCycles {
  return {
    seriesLabel: partAndProc && station ? "Pallet" : station && pallet ? "Part" : "Station",
    data: LazySeq.ofIterable(allCycles)
      .filter(e => {
        if (partAndProc && part_and_proc(e.part, e.process) !== partAndProc) {
          return false;
        }
        if (pallet && e.pallet !== pallet) {
          return false;
        }
        if (station && stat_name_and_num(e.stationGroup, e.stationNumber) !== station) {
          return false;
        }
        return true;
      })
      .groupBy(e => {
        if (partAndProc && station) {
          return e.pallet;
        } else if (station && pallet) {
          return part_and_proc(e.part, e.process);
        } else {
          return stat_name_and_num(e.stationGroup, e.stationNumber);
        }
      })
      .mapValues(e => e.toArray())
  };
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

class DayAndStation {
  constructor(public day: Date, public station: string) {}
  equals(other: DayAndStation): boolean {
    return this.day.getTime() === other.day.getTime() && this.station === other.station;
  }
  hashCode(): number {
    return fieldsHashCode(this.day.getTime(), this.station);
  }
  toString(): string {
    return `{day: ${this.day.toISOString()}}, station: ${this.station}}`;
  }
  adjustDay(f: (d: Date) => Date): DayAndStation {
    return new DayAndStation(f(this.day), this.station);
  }
}

export function binCyclesByDayAndStat(
  cycles: Vector<PartCycleData>,
  extractValue: (c: PartCycleData) => number
): HashMap<DayAndStation, number> {
  return LazySeq.ofIterable(cycles)
    .flatMap(point =>
      splitPartCycleToDays(point, extractValue(point)).map(x => ({
        ...x,
        station: stat_name_and_num(point.stationGroup, point.stationNumber)
      }))
    )
    .toMap(p => [new DayAndStation(p.day, p.station), p.value] as [DayAndStation, number], (v1, v2) => v1 + v2);
}

class DayAndPart {
  constructor(public day: Date, public part: string) {}
  equals(other: DayAndPart): boolean {
    return this.day.getTime() === other.day.getTime() && this.part === other.part;
  }
  hashCode(): number {
    return fieldsHashCode(this.day.getTime(), this.part);
  }
  toString(): string {
    return `{day: ${this.day.toISOString()}}, part: ${this.part}}`;
  }
  adjustDay(f: (d: Date) => Date): DayAndPart {
    return new DayAndPart(f(this.day), this.part);
  }
}

export function binCyclesByDayAndPart(
  cycles: Vector<PartCycleData>,
  extractValue: (c: PartCycleData) => number
): HashMap<DayAndPart, number> {
  return LazySeq.ofIterable(cycles)
    .map(point => ({
      day: startOfDay(point.x),
      part: part_and_proc(point.part, point.process),
      value: extractValue(point)
    }))
    .toMap(p => [new DayAndPart(p.day, p.part), p.value] as [DayAndPart, number], (v1, v2) => v1 + v2);
}

export function stationMinutes(partCycles: Vector<PartCycleData>, cutoff: Date): HashMap<string, number> {
  return LazySeq.ofIterable(partCycles)
    .filter(p => p.x >= cutoff)
    .map(p => ({
      station: stat_name_and_num(p.stationGroup, p.stationNumber),
      active: p.active
    }))
    .toMap(x => [x.station, x.active] as [string, number], (v1, v2) => v1 + v2);
}
