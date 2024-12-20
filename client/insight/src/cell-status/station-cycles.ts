/* Copyright (c) 2024, John Lenz

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
  calcElapsedForCycles,
  EstimatedCycleTimes,
  isOutlier,
  last30EstimatedCycleTimes,
  PartAndStationOperation,
  specificMonthEstimatedCycleTimes,
} from "./estimated-cycle-times.js";
import { durationToMinutes } from "../util/parseISODuration.js";
import { addDays } from "date-fns";
import { HashMap, LazySeq } from "@seedtactics/immutable-collections";
import { Atom, atom } from "jotai";

export interface PartCycleData {
  readonly endTime: Date;
  readonly elapsedMinsPerMaterial: number;
  readonly cntr: number;
  readonly part: string;
  readonly stationGroup: string;
  readonly stationNumber: number;
  readonly operation: string;
  readonly pallet: number;
  readonly activeMinutes: number; // active time in minutes
  readonly isLabor: boolean;
  readonly material: ReadonlyArray<Readonly<ILogMaterial>>;
  readonly operator: string;

  readonly medianCycleMinutes: number;
  readonly MAD_aboveMinutes: number;
  readonly isOutlier: boolean;
}

export type StationCyclesByCntr = HashMap<number, PartCycleData>;

export function stat_name_and_num(stationGroup: string, stationNumber: number): string {
  if (stationGroup.startsWith("Inspect")) {
    return stationGroup;
  } else {
    return stationGroup + " #" + stationNumber.toString();
  }
}

const last30StationCyclesRW = atom<StationCyclesByCntr>(HashMap.empty<number, PartCycleData>());
export const last30StationCycles: Atom<StationCyclesByCntr> = last30StationCyclesRW;

const specificMonthStationCyclesRW = atom<StationCyclesByCntr>(HashMap.empty<number, PartCycleData>());
export const specificMonthStationCycles: Atom<StationCyclesByCntr> = specificMonthStationCyclesRW;

function convertLogToCycle(
  estimatedCycleTimes: EstimatedCycleTimes,
  cycle: ILogEntry,
  elapsedPerMat: number,
): PartCycleData | null {
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

  let activeMins = durationToMinutes(cycle.active);
  if (cycle.active === "" || activeMins <= 0 || cycle.material.length == 0) {
    activeMins = (stats?.expectedCycleMinutesForSingleMat ?? 0) * cycle.material.length;
  }

  return {
    endTime: cycle.endUTC,
    elapsedMinsPerMaterial: elapsedPerMat,
    cntr: cycle.counter,
    activeMinutes: activeMins,
    medianCycleMinutes: (stats?.medianMinutesForSingleMat ?? 0) * cycle.material.length,
    MAD_aboveMinutes: stats?.MAD_aboveMinutes ?? 0,
    part: part,
    pallet: cycle.pal,
    material: cycle.material,
    isLabor: cycle.type === LogType.LoadUnloadCycle,
    isOutlier: stats ? isOutlier(stats, elapsedPerMat) : false,
    stationGroup: cycle.loc,
    stationNumber: cycle.locnum,
    operation: cycle.type === LogType.LoadUnloadCycle ? cycle.result : cycle.program,
    operator: cycle.details ? cycle.details.operator || "" : "",
  };
}

function convertOldLogsToCycles(
  estimateCycleTimes: EstimatedCycleTimes,
  log: ReadonlyArray<Readonly<ILogEntry>>,
): StationCyclesByCntr {
  return calcElapsedForCycles(
    LazySeq.of(log).filter(
      (e) =>
        (e.type === LogType.LoadUnloadCycle || e.type === LogType.MachineCycle) &&
        !e.startofcycle &&
        e.loc !== "",
    ),
  )
    .collect((c) => convertLogToCycle(estimateCycleTimes, c.cycle, c.elapsedForSingleMaterialMinutes))
    .buildHashMap((c) => c.cntr);
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
    oldCycles.union(convertOldLogsToCycles(estimatedCycleTimes, log)),
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

    // new events arriving over websocket are garuanteed to use the new method of calculating elapsed time
    // where the server already splits the elapsed time among a chunk, so no need to chunk here
    const elapsedPerMat =
      evt.logEntry.material.length > 0
        ? durationToMinutes(evt.logEntry.elapsed) / evt.logEntry.material.length
        : 0;

    const converted = convertLogToCycle(estimatedCycleTimes, evt.logEntry, elapsedPerMat);
    if (!converted) return;

    set(last30StationCyclesRW, (cycles) => {
      if (expire) {
        const thirtyDaysAgo = addDays(now, -30);
        cycles = cycles.filter((e) => e.endTime >= thirtyDaysAgo);
      }

      cycles = cycles.set(converted.cntr, converted);

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
    set(specificMonthStationCyclesRW, convertOldLogsToCycles(estimatedCycleTimes, log));
  },
);
