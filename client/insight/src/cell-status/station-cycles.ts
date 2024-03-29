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
import { IEditMaterialInLogEvents, ILogEntry, ILogMaterial, LogType } from "../network/api.js";
import type { ServerEventAndTime } from "./loading.js";
import {
  activeMinutes,
  EstimatedCycleTimes,
  last30EstimatedCycleTimes,
  LogEntryWithSplitElapsed,
  PartAndStationOperation,
  specificMonthEstimatedCycleTimes,
  splitElapsedLoadTime,
} from "./estimated-cycle-times.js";
import { durationToMinutes } from "../util/parseISODuration.js";
import { addDays } from "date-fns";
import { LazySeq, HashMap } from "@seedtactics/immutable-collections";
import { Atom, atom } from "jotai";

export interface PartCycleData {
  readonly x: Date;
  readonly y: number; // cycle time in minutes
  readonly cntr: number;
  readonly part: string;
  readonly stationGroup: string;
  readonly stationNumber: number;
  readonly operation: string;
  readonly pallet: number;
  readonly activeMinutes: number; // active time in minutes
  readonly medianCycleMinutes: number;
  readonly MAD_aboveMinutes: number;
  readonly isLabor: boolean;
  readonly material: ReadonlyArray<Readonly<ILogMaterial>>;
  readonly operator: string;
}

export type StationCyclesByCntr = HashMap<number, PartCycleData>;

export function stat_name_and_num(stationGroup: string, stationNumber: number): string {
  if (stationGroup.startsWith("Inspect")) {
    return stationGroup;
  } else {
    return stationGroup + " #" + stationNumber.toString();
  }
}

export function splitElapsedLoadTimeAmongCycles(
  cycles: LazySeq<PartCycleData>,
): LazySeq<LogEntryWithSplitElapsed<PartCycleData>> {
  return splitElapsedLoadTime(
    cycles,
    (c) => c.stationNumber,
    (c) => c.x,
    (c) => c.y,
    (c) => c.activeMinutes,
  );
}

const last30StationCyclesRW = atom<StationCyclesByCntr>(HashMap.empty<number, PartCycleData>());
export const last30StationCycles: Atom<StationCyclesByCntr> = last30StationCyclesRW;

const specificMonthStationCyclesRW = atom<StationCyclesByCntr>(HashMap.empty<number, PartCycleData>());
export const specificMonthStationCycles: Atom<StationCyclesByCntr> = specificMonthStationCyclesRW;

function convertLogToCycle(
  estimatedCycleTimes: EstimatedCycleTimes,
  cycle: ILogEntry,
): [number, PartCycleData] | null {
  if (
    cycle.startofcycle ||
    (cycle.type !== LogType.LoadUnloadCycle && cycle.type !== LogType.MachineCycle) ||
    cycle.loc === ""
  ) {
    return null;
  }
  const part = cycle.material.length > 0 ? cycle.material[0].part : "";
  const stats =
    cycle.material.length > 0
      ? estimatedCycleTimes.get(PartAndStationOperation.ofLogCycle(cycle))
      : undefined;
  const elapsed = durationToMinutes(cycle.elapsed);
  return [
    cycle.counter,
    {
      x: cycle.endUTC,
      y: elapsed,
      cntr: cycle.counter,
      activeMinutes: activeMinutes(cycle, stats),
      medianCycleMinutes: (stats?.medianMinutesForSingleMat ?? 0) * cycle.material.length,
      MAD_aboveMinutes: stats?.MAD_aboveMinutes ?? 0,
      part: part,
      pallet: cycle.pal,
      material: cycle.material,
      isLabor: cycle.type === LogType.LoadUnloadCycle,
      stationGroup: cycle.loc,
      stationNumber: cycle.locnum,
      operation: cycle.type === LogType.LoadUnloadCycle ? cycle.result : cycle.program,
      operator: cycle.details ? cycle.details.operator || "" : "",
    },
  ];
}

function process_swap(
  swap: Readonly<IEditMaterialInLogEvents>,
  partCycles: StationCyclesByCntr,
): StationCyclesByCntr {
  for (const changed of swap.editedEvents) {
    const c = partCycles.get(changed.counter);
    if (c !== undefined) {
      const newC = { ...c, material: changed.material };
      partCycles = partCycles.set(changed.counter, newC);
    }
  }
  return partCycles;
}

export const setLast30StationCycles = atom(null, (get, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  const estimatedCycleTimes = get(last30EstimatedCycleTimes);
  set(last30StationCyclesRW, (oldCycles) =>
    oldCycles.union(
      LazySeq.of(log)
        .collect((c) => convertLogToCycle(estimatedCycleTimes, c))
        .toHashMap((x) => x),
    ),
  );
});

export const updateLast30StationCycles = atom(null, (get, set, { evt, now, expire }: ServerEventAndTime) => {
  if (evt.logEntry && evt.logEntry.type === LogType.InvalidateCycle) {
    const cntrs = evt.logEntry.details?.["EditedCounters"];
    const invalidatedCycles = cntrs ? new Set(cntrs.split(",").map((i) => parseInt(i))) : new Set<number>();

    if (invalidatedCycles.size > 0) {
      set(last30StationCyclesRW, (cycles) => {
        for (const invalid of invalidatedCycles) {
          const c = cycles.get(invalid);
          if (c !== undefined) {
            cycles = cycles.set(invalid, { ...c, activeMinutes: 0 });
          }
        }
        return cycles;
      });
    }
  } else if (evt.logEntry) {
    const estimatedCycleTimes = get(last30EstimatedCycleTimes);
    const converted = convertLogToCycle(estimatedCycleTimes, evt.logEntry);
    if (!converted) return;

    set(last30StationCyclesRW, (cycles) => {
      if (expire) {
        const thirtyDaysAgo = addDays(now, -30);
        cycles = cycles.filter((e) => e.x >= thirtyDaysAgo);
      }

      cycles = cycles.set(converted[0], converted[1]);

      return cycles;
    });
  } else if (evt.editMaterialInLog) {
    const edit = evt.editMaterialInLog;
    set(last30StationCyclesRW, (oldCycles) => process_swap(edit, oldCycles));
  }
});

export const setSpecificMonthStationCycles = atom(
  null,
  (get, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    const estimatedCycleTimes = get(specificMonthEstimatedCycleTimes);
    set(
      specificMonthStationCyclesRW,
      LazySeq.of(log)
        .collect((c) => convertLogToCycle(estimatedCycleTimes, c))
        .toHashMap((x) => x),
    );
  },
);
