/* Copyright (c) 2020, John Lenz

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
import { HashMap, HashSet, Vector, Option, fieldsHashCode } from "prelude-ts";
import { LazySeq } from "./lazyseq";
import { differenceInSeconds } from "date-fns";

export interface CycleData {
  readonly x: Date;
  readonly y: number; // cycle time in minutes
}

export interface PalletCycleData extends CycleData {
  readonly active: number;
}

export interface PartCycleData extends CycleData {
  readonly cntr: number;
  readonly part: string;
  readonly process: number;
  readonly stationGroup: string;
  readonly stationNumber: number;
  readonly operation: string;
  readonly pallet: string;
  readonly activeMinutes: number; // active time in minutes
  readonly medianCycleMinutes: number;
  readonly activeTotalMachineMinutesForSingleMat: number; // active time for all machines in entire route
  readonly MAD_aboveMinutes: number;
  readonly isLabor: boolean;
  readonly material: ReadonlyArray<Readonly<api.ILogMaterial>>;
  readonly completed: boolean; // did this cycle result in a completed part
  readonly signaledInspections: HashSet<string>;
  readonly completedInspections: HashMap<string, boolean>; // boolean is if successful or failed
  readonly operator: string;
}

export interface StatisticalCycleTime {
  readonly medianMinutesForSingleMat: number;
  readonly MAD_belowMinutes: number; // MAD of points below the median
  readonly MAD_aboveMinutes: number; // MAD of points above the median
  readonly expectedCycleMinutesForSingleMat: number;
}

export class PartAndProcess {
  public constructor(public readonly part: string, public readonly proc: number) {}
  public static ofPartCycle(cy: PartCycleData): PartAndProcess {
    return new PartAndProcess(cy.part, cy.process);
  }
  public static ofLogCycle(c: Readonly<api.ILogEntry>): PartAndProcess {
    return new PartAndProcess(c.material[0].part, c.material[0].proc);
  }
  equals(other: PartAndProcess): boolean {
    return this.part === other.part && this.proc === other.proc;
  }
  hashCode(): number {
    return fieldsHashCode(this.part, this.proc);
  }
  toString(): string {
    return this.part + "-" + this.proc.toString();
  }
}

export class PartAndStationOperation {
  public constructor(
    public readonly part: string,
    public readonly proc: number,
    public readonly statGroup: string,
    public readonly operation: string
  ) {}
  public static ofPartCycle(cy: PartCycleData): PartAndStationOperation {
    return new PartAndStationOperation(cy.part, cy.process, cy.stationGroup, cy.operation);
  }
  public static ofLogCycle(c: Readonly<api.ILogEntry>): PartAndStationOperation {
    return new PartAndStationOperation(
      c.material[0].part,
      c.material[0].proc,
      c.loc,
      c.type === api.LogType.LoadUnloadCycle ? c.result : c.program
    );
  }
  equals(other: PartAndStationOperation): boolean {
    return (
      this.part === other.part &&
      this.proc === other.proc &&
      this.statGroup === other.statGroup &&
      this.operation === other.operation
    );
  }
  hashCode(): number {
    return fieldsHashCode(this.part, this.proc, this.statGroup, this.operation);
  }
  toString(): string {
    return `{part: ${this.part}}, proc: ${this.proc}, statGroup: ${this.statGroup}, operation: ${this.operation}}`;
  }
}

export class StationOperation {
  public constructor(public readonly statGroup: string, public readonly operation: string) {}
  public static ofLogCycle(c: Readonly<api.ILogEntry>): StationOperation {
    return new StationOperation(c.loc, c.type === api.LogType.LoadUnloadCycle ? c.result : c.program);
  }
  equals(other: PartAndStationOperation): boolean {
    return this.statGroup === other.statGroup && this.operation === other.operation;
  }
  hashCode(): number {
    return fieldsHashCode(this.statGroup, this.operation);
  }
  toString(): string {
    return `{statGroup: ${this.statGroup}, operation: ${this.operation}}`;
  }
}

export type EstimatedCycleTimes = HashMap<PartAndStationOperation, StatisticalCycleTime>;
export type ActiveCycleTime = HashMap<
  PartAndProcess,
  HashMap<StationOperation, { readonly activeMinsForSingleMat: number }>
>;

export interface ProgramToolUseInSingleCycle {
  readonly tools: ReadonlyArray<{
    readonly toolName: string;
    readonly cycleUsageMinutes: number;
    readonly toolChanged: boolean;
  }>;
}

export type ToolUsage = HashMap<PartAndStationOperation, Vector<ProgramToolUseInSingleCycle>>;

export interface CycleState {
  readonly part_cycles: Vector<PartCycleData>;
  readonly by_pallet: HashMap<string, ReadonlyArray<PalletCycleData>>;

  readonly part_and_proc_names: HashSet<PartAndProcess>;
  readonly machine_groups: HashSet<string>;
  readonly machine_names: HashSet<string>;
  readonly loadstation_names: HashSet<string>;
  readonly pallet_names: HashSet<string>;
  readonly estimatedCycleTimes: EstimatedCycleTimes;
  readonly active_cycle_times: ActiveCycleTime;
  readonly tool_usage: ToolUsage;
}

export const initial: CycleState = {
  part_cycles: Vector.empty(),
  part_and_proc_names: HashSet.empty(),
  by_pallet: HashMap.empty(),
  machine_groups: HashSet.empty(),
  machine_names: HashSet.empty(),
  loadstation_names: HashSet.empty(),
  pallet_names: HashSet.empty(),
  estimatedCycleTimes: HashMap.empty(),
  active_cycle_times: HashMap.empty(),
  tool_usage: HashMap.empty(),
};

export enum ExpireOldDataType {
  ExpireEarlierThan,
  NoExpire,
}

export type ExpireOldData =
  | { type: ExpireOldDataType.ExpireEarlierThan; d: Date }
  | { type: ExpireOldDataType.NoExpire };

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

export function isOutlier(s: StatisticalCycleTime, mins: number): boolean {
  if (s.medianMinutesForSingleMat === 0) {
    return false;
  }
  if (mins < s.medianMinutesForSingleMat) {
    return (s.medianMinutesForSingleMat - mins) / s.MAD_belowMinutes > 2;
  } else {
    return (mins - s.medianMinutesForSingleMat) / s.MAD_aboveMinutes > 2;
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
        .filter((x) => x <= medianMinutes)
        .map((x) => medianMinutes - x)
    );
  // clamp at 15 seconds
  if (madBelowMinutes < 0.25) {
    madBelowMinutes = 0.25;
  }

  let madAboveMinutes =
    1.4826 *
    median(
      LazySeq.ofIterable(cycles)
        .filter((x) => x >= medianMinutes)
        .map((x) => x - medianMinutes)
    );
  // clamp at 15 seconds
  if (madAboveMinutes < 0.25) {
    madAboveMinutes = 0.25;
  }

  const statCycleTime = {
    medianMinutesForSingleMat: medianMinutes,
    MAD_belowMinutes: madBelowMinutes,
    MAD_aboveMinutes: madAboveMinutes,
    expectedCycleMinutesForSingleMat: 0,
  };

  // filter to only inliers
  const inliers = cycles.filter((x) => !isOutlier(statCycleTime, x)).toArray();
  // compute average of inliers
  const expectedCycleMinutesForSingleMat = inliers.reduce((sum, x) => sum + x, 0) / inliers.length;

  return { ...statCycleTime, expectedCycleMinutesForSingleMat };
}

export function chunkCyclesWithSimilarEndTime<T>(cycles: Vector<T>, getTime: (c: T) => Date): Vector<ReadonlyArray<T>> {
  const sorted = cycles.sortOn((c) => getTime(c).getTime());
  return Vector.ofIterable(
    LazySeq.ofIterator(function* () {
      let chunk: Array<T> = [];
      for (const c of sorted) {
        if (chunk.length === 0) {
          chunk = [c];
        } else if (differenceInSeconds(getTime(c), getTime(chunk[chunk.length - 1])) < 10) {
          chunk.push(c);
        } else {
          yield chunk;
          chunk = [c];
        }
      }
      if (chunk.length > 0) {
        yield chunk;
      }
    })
  );
}

interface LogEntryWithSplitElapsed<T> {
  readonly cycle: T;
  readonly elapsedForSingleMaterialMinutes: number;
}

export function splitElapsedTimeAmongChunk<T extends { material: ReadonlyArray<unknown> }>(
  chunk: ReadonlyArray<T>,
  getElapsedMins: (c: T) => number,
  getActiveMins: (c: T) => number
): ReadonlyArray<LogEntryWithSplitElapsed<T>> {
  let totalActiveMins = 0;
  let totalMatCount = 0;
  let allEventsHaveActive = true;
  for (const cycle of chunk) {
    if (getActiveMins(cycle) < 0) {
      allEventsHaveActive = false;
    }
    totalMatCount += cycle.material.length;
    totalActiveMins += getActiveMins(cycle);
  }

  if (allEventsHaveActive && totalActiveMins > 0) {
    //split by active.  First multiply by (active/totalActive) ratio to get fraction of elapsed
    //for this cycle, then by material count to get per-material
    return chunk.map((cycle) => ({
      cycle,
      elapsedForSingleMaterialMinutes:
        (getElapsedMins(cycle) * getActiveMins(cycle)) / totalActiveMins / cycle.material.length,
    }));
  }

  // split equally among all material
  if (totalMatCount > 0) {
    return chunk.map((cycle) => ({
      cycle,
      elapsedForSingleMaterialMinutes: getElapsedMins(cycle) / totalMatCount,
    }));
  }

  // only when no events have material, which should never happen
  return chunk.map((cycle) => ({
    cycle,
    elapsedForSingleMaterialMinutes: getElapsedMins(cycle),
  }));
}

function splitElapsedLoadTime<T extends { material: ReadonlyArray<unknown> }>(
  cycles: LazySeq<T>,
  getLuL: (c: T) => number,
  getTime: (c: T) => Date,
  getElapsedMins: (c: T) => number,
  getActiveMins: (c: T) => number
): LazySeq<LogEntryWithSplitElapsed<T>> {
  const loadEventsByLUL = cycles.groupBy(getLuL).mapValues((cs) => chunkCyclesWithSimilarEndTime(cs, getTime));

  return LazySeq.ofIterable(loadEventsByLUL.valueIterable())
    .flatMap((cycles) => cycles)
    .map((cs) => splitElapsedTimeAmongChunk(cs, getElapsedMins, getActiveMins))
    .flatMap((chunk) => chunk);
}

export function splitElapsedLoadTimeAmongCycles(
  cycles: LazySeq<PartCycleData>
): LazySeq<LogEntryWithSplitElapsed<PartCycleData>> {
  return splitElapsedLoadTime(
    cycles,
    (c) => c.stationNumber,
    (c) => c.x,
    (c) => c.y,
    (c) => c.activeMinutes
  );
}

function estimateCycleTimesOfParts(cycles: Iterable<Readonly<api.ILogEntry>>): EstimatedCycleTimes {
  const machines = LazySeq.ofIterable(cycles)
    .filter((c) => c.type === api.LogType.MachineCycle && !c.startofcycle && c.material.length > 0)
    .groupBy((c) => PartAndStationOperation.ofLogCycle(c))
    .mapValues((cyclesForPartAndStat) =>
      estimateCycleTimes(
        cyclesForPartAndStat.map((cycle) => duration(cycle.elapsed).asMinutes() / cycle.material.length)
      )
    );

  const loads = splitElapsedLoadTime(
    LazySeq.ofIterable(cycles).filter(
      (c) => c.type === api.LogType.LoadUnloadCycle && !c.startofcycle && c.material.length > 0
    ),
    (c) => c.locnum,
    (c) => c.endUTC,
    (c) => duration(c.elapsed).asMinutes(),
    (c) => (c.active === "" ? -1 : duration(c.active).asMinutes())
  )
    .groupBy((c) => PartAndStationOperation.ofLogCycle(c.cycle))
    .mapValues((cyclesForPartAndStat) =>
      estimateCycleTimes(cyclesForPartAndStat.map((c) => c.elapsedForSingleMaterialMinutes))
    );

  return machines.mergeWith(loads, (s1, _) => s1);
}

function activeMinutes(
  cycle: Readonly<api.ILogEntry>,
  stats: Option<StatisticalCycleTime>,
  activeTimes: ActiveCycleTime
): [number, ActiveCycleTime] {
  const aMins = duration(cycle.active).asMinutes();
  if (cycle.active === "" || aMins <= 0 || cycle.material.length === 0) {
    return [stats.map((s) => s.expectedCycleMinutesForSingleMat).getOrElse(0) * cycle.material.length, activeTimes];
  } else {
    const activeMinsForSingleMat = aMins / cycle.material.length;
    const partKey = PartAndProcess.ofLogCycle(cycle);
    const oldActive = activeTimes.get(partKey);
    const statKey = StationOperation.ofLogCycle(cycle);
    if (oldActive.isSome()) {
      const oldOp = oldActive.get().get(statKey);
      if (oldOp.isSome()) {
        if (activeMinsForSingleMat !== oldOp.get().activeMinsForSingleMat) {
          // adjust to new time
          activeTimes = activeTimes.put(partKey, oldActive.get().put(statKey, { activeMinsForSingleMat }));
        } else {
          // no adjustment, cMins equals current value
        }
      } else {
        // add operation
        activeTimes = activeTimes.put(partKey, oldActive.get().put(statKey, { activeMinsForSingleMat }));
      }
    } else {
      activeTimes = activeTimes.put(partKey, HashMap.of([statKey, { activeMinsForSingleMat }]));
    }
    return [aMins, activeTimes];
  }
}

function activeTotalMachineMinutesForSingleMat(
  part: string,
  process: number,
  machineGroups: HashSet<string>,
  activeTimes: ActiveCycleTime,
  estimated: EstimatedCycleTimes
): number {
  const active = activeTimes.get(new PartAndProcess(part, process));
  if (active.isSome()) {
    return LazySeq.ofIterable(active.get()).sumOn(([k, time]) =>
      machineGroups.contains(k.statGroup) ? time.activeMinsForSingleMat : 0
    );
  } else {
    // fall back to using estimated times
    return LazySeq.ofIterable(estimated).sumOn(([k, times]) =>
      k.part === part && k.proc === process && machineGroups.contains(k.statGroup)
        ? times.expectedCycleMinutesForSingleMat
        : 0
    );
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
      case api.LogType.Inspection: {
        const inspName = (evt.details || {}).InspectionType;
        const inspected = evt.result.toLowerCase() === "true" || evt.result === "1";
        if (inspected && inspName) {
          for (const m of evt.material) {
            signaled[m.id] = (signaled[m.id] || HashSet.empty()).add(inspName);
          }
        }
        break;
      }

      case api.LogType.InspectionForce: {
        const forceInspName = evt.program;
        const forced = evt.result.toLowerCase() === "true" || evt.result === "1";
        if (forceInspName && forced) {
          for (const m of evt.material) {
            signaled[m.id] = (signaled[m.id] || HashSet.empty()).add(forceInspName);
          }
        }
        break;
      }

      case api.LogType.InspectionResult: {
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
  }
  return { signaled, result };
}

function process_tools(cycle: Readonly<api.ILogEntry>, toolUsage: ToolUsage): ToolUsage {
  if (cycle.tools === undefined || cycle.type !== api.LogType.MachineCycle) {
    return toolUsage;
  }

  const toolsUsedInCycle = LazySeq.ofObject(cycle.tools)
    .map(([toolName, use]) => ({
      toolName,
      cycleUsageMinutes: use.toolUseDuringCycle === "" ? 0 : duration(use.toolUseDuringCycle).asMinutes(),
      toolChanged: use.toolChangeOccurred === true,
    }))
    .toArray();

  if (toolsUsedInCycle.length === 0) {
    return toolUsage;
  }

  const key = PartAndStationOperation.ofLogCycle(cycle);
  return toolUsage.putWithMerge(key, Vector.of({ tools: toolsUsedInCycle }), (oldV, newV) =>
    oldV.drop(Math.max(0, oldV.length() - 4)).appendAll(newV)
  );
}

export function process_events(
  expire: ExpireOldData,
  newEvts: ReadonlyArray<Readonly<api.ILogEntry>>,
  initialLoad: boolean,
  st: CycleState
): CycleState {
  let allPartCycles = st.part_cycles;
  let pals = st.by_pallet;

  let estimatedCycleTimes: EstimatedCycleTimes;
  if (initialLoad) {
    estimatedCycleTimes = estimateCycleTimesOfParts(newEvts);
  } else {
    estimatedCycleTimes = st.estimatedCycleTimes;
  }
  let activeCycleTimes = st.active_cycle_times;
  let toolUsage = st.tool_usage;

  const invalidateOldCyclesOnEvent = expire.type === ExpireOldDataType.ExpireEarlierThan;

  switch (expire.type) {
    case ExpireOldDataType.ExpireEarlierThan: {
      // check if nothing to expire and no new data
      const partEntries = LazySeq.ofIterable(allPartCycles);
      const palEntries = LazySeq.ofIterable(pals.valueIterable()).flatMap((cs) => cs);
      const minEntry = (palEntries as LazySeq<CycleData>).appendAll(partEntries).minOn((e) => e.x.getTime());

      if ((minEntry.isNone() || minEntry.get().x >= expire.d) && newEvts.length === 0) {
        return st;
      }

      // filter old events
      allPartCycles = allPartCycles.filter((e) => e.x >= expire.d);
      pals = pals.mapValues((es) => es.filter((e) => e.x >= expire.d)).filter((es) => es.length > 0);

      break;
    }

    case ExpireOldDataType.NoExpire:
      if (newEvts.length === 0) {
        return st;
      }
      break;
  }

  let partNames = st.part_and_proc_names;
  let machNames = st.machine_names;
  let machineGroups = st.machine_groups;
  let lulNames = st.loadstation_names;
  let palNames = st.pallet_names;
  for (const e of newEvts) {
    for (const m of e.material) {
      const p = new PartAndProcess(m.part, m.proc);
      if (!partNames.contains(p)) {
        partNames = partNames.add(p);
      }
    }
    if (e.pal !== "") {
      if (!palNames.contains(e.pal)) {
        palNames = palNames.add(e.pal);
      }
    }
    if (e.type === api.LogType.MachineCycle) {
      if (!machineGroups.contains(e.loc)) {
        machineGroups = machineGroups.add(e.loc);
      }
      const machName = stat_name_and_num(e.loc, e.locnum);
      if (!machNames.contains(machName)) {
        machNames = machNames.add(machName);
      }
    }
    if (e.type === api.LogType.LoadUnloadCycle) {
      const n = stat_name_and_num(e.loc, e.locnum);
      if (!lulNames.contains(n)) {
        lulNames = lulNames.add(n);
      }
    }
  }

  const newCycles: LazySeq<PartCycleData> = LazySeq.ofIterable(newEvts)
    .filter((c) => !c.startofcycle && (c.type === api.LogType.LoadUnloadCycle || c.type === api.LogType.MachineCycle))
    .map((cycle) => {
      const part = cycle.material.length > 0 ? cycle.material[0].part : "";
      const proc = cycle.material.length > 0 ? cycle.material[0].proc : 1;
      const stats =
        cycle.material.length > 0
          ? estimatedCycleTimes.get(PartAndStationOperation.ofLogCycle(cycle))
          : Option.none<StatisticalCycleTime>();
      let activeMins;
      [activeMins, activeCycleTimes] = activeMinutes(cycle, stats, activeCycleTimes);
      const elapsed = duration(cycle.elapsed).asMinutes();
      if (stats.isSome() && !isOutlier(stats.get(), elapsed)) {
        toolUsage = process_tools(cycle, toolUsage);
      }
      return {
        x: cycle.endUTC,
        y: elapsed,
        cntr: cycle.counter,
        activeMinutes: activeMins,
        medianCycleMinutes: stats.map((s) => s.medianMinutesForSingleMat).getOrElse(0) * cycle.material.length,
        MAD_aboveMinutes: stats.map((s) => s.MAD_aboveMinutes).getOrElse(0),
        activeTotalMachineMinutesForSingleMat: activeTotalMachineMinutesForSingleMat(
          part,
          proc,
          machineGroups,
          activeCycleTimes,
          estimatedCycleTimes
        ),
        completed: cycle.type === api.LogType.LoadUnloadCycle && cycle.result === "UNLOAD",
        part: part,
        process: proc,
        pallet: cycle.pal,
        material: cycle.material,
        isLabor: cycle.type === api.LogType.LoadUnloadCycle,
        stationGroup: cycle.loc,
        stationNumber: cycle.locnum,
        operation: cycle.type === api.LogType.LoadUnloadCycle ? cycle.result : cycle.program,
        signaledInspections: HashSet.empty<string>(),
        completedInspections: HashMap.empty<string, boolean>(),
        operator: cycle.details ? cycle.details.operator || "" : "",
      };
    })
    .filter((c) => c.stationGroup !== "");

  const newPalCycles = LazySeq.ofIterable(newEvts)
    .filter((c) => !c.startofcycle && c.type === api.LogType.PalletCycle && c.pal !== "")
    .groupBy((c) => c.pal)
    .mapValues((cyclesForPal) =>
      LazySeq.ofIterable(cyclesForPal)
        .map((c) => ({
          x: c.endUTC,
          y: duration(c.elapsed).asMinutes(),
          active: duration(c.active).asMinutes(),
          completed: false,
        }))
        .toArray()
    );
  pals = pals.mergeWith(newPalCycles, (oldCs, newCs) => oldCs.concat(newCs));

  // merge inspections and clear invalidated cycles
  const inspResult = newInspectionData(newEvts);
  const invalidatedCycles = invalidateOldCyclesOnEvent
    ? LazySeq.ofIterable(newEvts)
        .filter((e) => e.type === api.LogType.InvalidateCycle)
        .flatMap((e) => {
          const cntrs = e.details?.["EditedCounters"];
          if (cntrs) {
            return cntrs.split(",").map((i) => parseInt(i));
          } else {
            return [];
          }
        })
        .toSet((x) => x)
    : HashSet.empty<number>();

  allPartCycles = allPartCycles.appendAll(newCycles).map((x) => {
    for (const mat of x.material) {
      const signaled = inspResult.signaled[mat.id];
      if (signaled) {
        x = { ...x, signaledInspections: x.signaledInspections.addAll(signaled) };
      }
      const result = inspResult.result[mat.id];
      if (result) {
        x = { ...x, completedInspections: x.completedInspections.mergeWith(result, (_a, b) => b) };
      }
    }
    if (invalidatedCycles.contains(x.cntr)) {
      x = { ...x, activeMinutes: 0 };
    }
    return x;
  });

  const newSt: CycleState = {
    ...st,
    part_cycles: allPartCycles,
    part_and_proc_names: partNames,
    by_pallet: pals,
    machine_groups: machineGroups,
    machine_names: machNames,
    loadstation_names: lulNames,
    pallet_names: palNames,
    active_cycle_times: activeCycleTimes,
    tool_usage: toolUsage,
  };

  if (initialLoad) {
    return {
      ...newSt,
      estimatedCycleTimes: estimatedCycleTimes,
    };
  } else {
    return newSt;
  }
}

export function process_swap(swap: Readonly<api.IEditMaterialInLogEvents>, st: CycleState): CycleState {
  const changedByCntr = LazySeq.ofIterable(swap.editedEvents).toMap(
    (e) => [e.counter, e],
    (e, _) => e
  );

  let partCycles = st.part_cycles.map((cycle) => {
    const changed = changedByCntr.get(cycle.cntr).getOrNull();
    if (changed) {
      return { ...cycle, material: changed.material };
    } else {
      return cycle;
    }
  });

  return { ...st, part_cycles: partCycles };
}
