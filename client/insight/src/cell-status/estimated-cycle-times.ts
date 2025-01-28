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
import { differenceInSeconds } from "date-fns";
import { ILogEntry, LogType } from "../network/api.js";
import { LazySeq, HashMap, hashValues, OrderedMapKey } from "@seedtactics/immutable-collections";
import { durationToMinutes } from "../util/parseISODuration.js";
import type { PartCycleData } from "./station-cycles.js";
import { Atom, atom } from "jotai";

export interface StatisticalCycleTime {
  readonly medianMinutesForSingleMat: number;
  readonly MAD_belowMinutes: number; // MAD of points below the median
  readonly MAD_aboveMinutes: number; // MAD of points above the median
  readonly expectedCycleMinutesForSingleMat: number;
}

export class PartAndStationOperation {
  public constructor(
    public readonly part: string,
    public readonly statGroup: string,
    public readonly operation: string,
  ) {}
  public static ofLogCycle(c: Readonly<ILogEntry>): PartAndStationOperation {
    return new PartAndStationOperation(
      c.material[0].part,
      c.loc,
      c.type === LogType.LoadUnloadCycle ? c.result + "-" + c.material[0].proc.toString() : c.program,
    );
  }
  public static ofPartCycle(c: Readonly<PartCycleData>): PartAndStationOperation {
    return new PartAndStationOperation(
      c.part,
      c.stationGroup,
      c.isLabor ? c.operation + "-" + c.material[0].proc.toString() : c.operation,
    );
  }

  compare(other: PartAndStationOperation): number {
    let cmp = this.part.localeCompare(other.part);
    if (cmp !== 0) return cmp;
    cmp = this.statGroup.localeCompare(other.statGroup);
    if (cmp !== 0) return cmp;
    return this.operation.localeCompare(other.operation);
  }
  hash(): number {
    return hashValues(this.part, this.statGroup, this.operation);
  }
  toString(): string {
    return `{part: ${this.part}}, statGroup: ${this.statGroup}, operation: ${this.operation}}`;
  }
}

export type EstimatedCycleTimes = HashMap<PartAndStationOperation, StatisticalCycleTime>;

const last30EstimatedTimesRW = atom<EstimatedCycleTimes>(
  HashMap.empty<PartAndStationOperation, StatisticalCycleTime>(),
);
export const last30EstimatedCycleTimes: Atom<EstimatedCycleTimes> = last30EstimatedTimesRW;

const specificMonthEstimatedTimesRW = atom<EstimatedCycleTimes>(
  HashMap.empty<PartAndStationOperation, StatisticalCycleTime>(),
);
export const specificMonthEstimatedCycleTimes: Atom<EstimatedCycleTimes> = specificMonthEstimatedTimesRW;

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

export function isOutlierAbove(s: StatisticalCycleTime, mins: number): boolean {
  if (s.medianMinutesForSingleMat === 0) {
    return false;
  }
  if (mins < s.medianMinutesForSingleMat) {
    return false;
  } else {
    return (mins - s.medianMinutesForSingleMat) / s.MAD_aboveMinutes > 2;
  }
}

function median(vals: LazySeq<number>): number {
  const sorted = vals.toMutableArray().sort((a, b) => a - b);
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

function estimateCycleTimes(cycles: Iterable<number>): StatisticalCycleTime {
  // compute median
  const medianMinutes = median(LazySeq.of(cycles));

  // absolute deviation from median, but use different values for below and above
  // median.  Below is assumed to be from fake cycles and above is from interrupted programs.
  // since we assume gaussian, use consistantcy constant of 1.4826

  let madBelowMinutes =
    1.4826 *
    median(
      LazySeq.of(cycles)
        .filter((x) => x <= medianMinutes)
        .map((x) => medianMinutes - x),
    );
  // clamp at 15 seconds
  if (madBelowMinutes < 0.25) {
    madBelowMinutes = 0.25;
  }

  let madAboveMinutes =
    1.4826 *
    median(
      LazySeq.of(cycles)
        .filter((x) => x >= medianMinutes)
        .map((x) => x - medianMinutes),
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
  const inliers = LazySeq.of(cycles)
    .filter((x) => !isOutlier(statCycleTime, x))
    .toRArray();
  // compute average of inliers
  const expectedCycleMinutesForSingleMat = inliers.reduce((sum, x) => sum + x, 0) / inliers.length;

  return { ...statCycleTime, expectedCycleMinutesForSingleMat };
}

export function chunkCyclesWithSimilarEndTime<T, K>(
  allCycles: LazySeq<T>,
  getKey: (t: T) => K & OrderedMapKey,
  getTime: (c: T) => Date,
): LazySeq<[K, ReadonlyArray<ReadonlyArray<T>>]> {
  return allCycles
    .toLookupOrderedMap(
      getKey,
      getTime,
      (c) => [c],
      (cs, ds) => cs.concat(ds),
    )
    .toAscLazySeq()
    .map(([k, cycles]) => {
      const ret: Array<ReadonlyArray<T>> = [];
      let chunk: Array<T> = [];
      for (const c of cycles.valuesToAscLazySeq().flatMap((cs) => cs)) {
        if (chunk.length === 0) {
          chunk = [c];
        } else if (differenceInSeconds(getTime(c), getTime(chunk[chunk.length - 1])) < 10) {
          chunk.push(c);
        } else {
          ret.push(chunk);
          chunk = [c];
        }
      }
      if (chunk.length > 0) {
        ret.push(chunk);
      }
      return [k, ret];
    });
}

export interface LogEntryWithSplitElapsed<T> {
  readonly cycle: T;
  readonly elapsedForSingleMaterialMinutes: number;
}

export function splitElapsedTimeAmongChunk<T extends { material: ReadonlyArray<unknown> }>(
  chunk: ReadonlyArray<T>,
  getElapsedMins: (c: T) => number,
  getActiveMins: (c: T) => number,
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

// In older versions of FMS Insight, load cycles had a shared elapsed time among all events
// for the cycle and each event had the total elapsed time for the whole combination of operations.
// An update to the server changed this so that the server splits the elapsed time among the events.
// But, for backwards compatibility, detect if we need to split the elpased time in the client too.
// To detect new vs old, the new version also started adding the material IDs to pallet begin and end events,
// so if the material appears in a begin/end event that means it does not need to be split.
export function calcElapsedForCycles(
  eventLog: ReadonlyArray<Readonly<ILogEntry>>,
): LazySeq<LogEntryWithSplitElapsed<Readonly<ILogEntry>>> {
  const matsInPalEvts = new Set<number>();
  for (const e of eventLog) {
    if (e.type === LogType.PalletCycle && e.material.length > 0) {
      for (const m of e.material) {
        matsInPalEvts.add(m.id);
      }
    }
  }

  return chunkCyclesWithSimilarEndTime(
    LazySeq.of(eventLog).filter(
      (e) =>
        (e.type === LogType.LoadUnloadCycle || e.type === LogType.MachineCycle) &&
        !e.startofcycle &&
        e.loc !== "",
    ),
    (c) => c.loc + " #" + c.locnum.toString(),
    (c) => c.endUTC,
  )
    .flatMap(([_lul, chunks]) => chunks)
    .flatMap((chunk) => {
      // Check if need to split the elapsed
      const chunk0Elapsed = durationToMinutes(chunk[0].elapsed);
      const shouldSplit =
        chunk.length >= 2 &&
        chunk.every(
          (c) =>
            durationToMinutes(c.elapsed) === chunk0Elapsed &&
            c.material.every((m) => !matsInPalEvts.has(m.id)),
        );

      if (shouldSplit) {
        return splitElapsedTimeAmongChunk(
          chunk,
          (c) => durationToMinutes(c.elapsed),
          (c) => (c.active === "" ? -1 : durationToMinutes(c.active)),
        );
      } else {
        return chunk.map((c) => ({
          cycle: c,
          elapsedForSingleMaterialMinutes:
            c.material.length > 0 ? durationToMinutes(c.elapsed) / c.material.length : 0,
        }));
      }
    });
}

function estimateCycleTimesOfParts(cycles: ReadonlyArray<Readonly<ILogEntry>>): EstimatedCycleTimes {
  return calcElapsedForCycles(cycles)
    .toLookup(
      (c) => PartAndStationOperation.ofLogCycle(c.cycle),
      (c) => c.elapsedForSingleMaterialMinutes,
    )
    .mapValues(estimateCycleTimes);
}

export const setLast30EstimatedCycleTimes = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(last30EstimatedTimesRW, (old) => (old.size === 0 ? estimateCycleTimesOfParts(log) : old));
});

export const setSpecificMonthEstimatedCycleTimes = atom(
  null,
  (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    set(specificMonthEstimatedTimesRW, estimateCycleTimesOfParts(log));
  },
);
