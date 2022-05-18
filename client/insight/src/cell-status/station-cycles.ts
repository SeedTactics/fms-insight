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
import { atom, RecoilValueReadOnly, TransactionInterface_UNSTABLE } from "recoil";
import { IEditMaterialInLogEvents, ILogEntry, ILogMaterial, LogType } from "../network/api";
import { conduit } from "../util/recoil-util";
import type { ServerEventAndTime } from "./loading";
import { LazySeq } from "../util/lazyseq";
import {
  activeMinutes,
  EstimatedCycleTimes,
  last30EstimatedCycleTimes,
  LogEntryWithSplitElapsed,
  PartAndStationOperation,
  specificMonthEstimatedCycleTimes,
  splitElapsedLoadTime,
} from "./estimated-cycle-times";
import { durationToMinutes } from "../util/parseISODuration";
import { addDays } from "date-fns";
import { emptyIMap, IMap } from "../util/imap";

export interface PartCycleData {
  readonly x: Date;
  readonly y: number; // cycle time in minutes
  readonly cntr: number;
  readonly part: string;
  readonly process: number;
  readonly stationGroup: string;
  readonly stationNumber: number;
  readonly operation: string;
  readonly pallet: string;
  readonly activeMinutes: number; // active time in minutes
  readonly medianCycleMinutes: number;
  readonly MAD_aboveMinutes: number;
  readonly isLabor: boolean;
  readonly material: ReadonlyArray<Readonly<ILogMaterial>>;
  readonly operator: string;
}

export type StationCyclesByCntr = IMap<number, PartCycleData>;

export function stat_name_and_num(stationGroup: string, stationNumber: number): string {
  if (stationGroup.startsWith("Inspect")) {
    return stationGroup;
  } else {
    return stationGroup + " #" + stationNumber.toString();
  }
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

const last30StationCyclesRW = atom<StationCyclesByCntr>({
  key: "last30StationCycles",
  default: emptyIMap(),
});
export const last30StationCycles: RecoilValueReadOnly<StationCyclesByCntr> = last30StationCyclesRW;

const specificMonthStationCyclesRW = atom<StationCyclesByCntr>({
  key: "specificMonthStationCycles",
  default: emptyIMap(),
});
export const specificMonthStationCycles: RecoilValueReadOnly<StationCyclesByCntr> = specificMonthStationCyclesRW;

function convertLogToCycle(estimatedCycleTimes: EstimatedCycleTimes, cycle: ILogEntry): [number, PartCycleData] | null {
  if (
    cycle.startofcycle ||
    (cycle.type !== LogType.LoadUnloadCycle && cycle.type !== LogType.MachineCycle) ||
    cycle.loc === ""
  ) {
    return null;
  }
  const part = cycle.material.length > 0 ? cycle.material[0].part : "";
  const proc = cycle.material.length > 0 ? cycle.material[0].proc : 1;
  const stats =
    cycle.material.length > 0 ? estimatedCycleTimes.get(PartAndStationOperation.ofLogCycle(cycle)) : undefined;
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
      process: proc,
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

function process_swap(swap: Readonly<IEditMaterialInLogEvents>, partCycles: StationCyclesByCntr): StationCyclesByCntr {
  for (const changed of swap.editedEvents) {
    const c = partCycles.get(changed.counter);
    if (c !== undefined) {
      const newC = { ...c, material: changed.material };
      partCycles = partCycles.set(changed.counter, newC);
    }
  }
  return partCycles;
}

export const setLast30StationCycles = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    const estimatedCycleTimes = t.get(last30EstimatedCycleTimes);
    t.set(last30StationCyclesRW, (oldCycles) =>
      oldCycles.size === 0
        ? LazySeq.ofIterable(log)
            .collect((c) => convertLogToCycle(estimatedCycleTimes, c))
            .toIMap((x) => x)
        : oldCycles.append(
            LazySeq.ofIterable(log).collect((c) => convertLogToCycle(estimatedCycleTimes, c)),
            (_, b) => b
          )
    );
  }
);

export const updateLast30StationCycles = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt, now, expire }: ServerEventAndTime) => {
    if (evt.logEntry && evt.logEntry.type === LogType.InvalidateCycle) {
      const cntrs = evt.logEntry.details?.["EditedCounters"];
      const invalidatedCycles = cntrs ? new Set(cntrs.split(",").map((i) => parseInt(i))) : new Set<number>();

      if (invalidatedCycles.size > 0) {
        t.set(last30StationCyclesRW, (cycles) => {
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
      const estimatedCycleTimes = t.get(last30EstimatedCycleTimes);
      const converted = convertLogToCycle(estimatedCycleTimes, evt.logEntry);
      if (!converted) return;

      t.set(last30StationCyclesRW, (cycles) => {
        if (expire) {
          const thirtyDaysAgo = addDays(now, -30);
          cycles = cycles.bulkDelete((_, e) => e.x < thirtyDaysAgo);
        }

        cycles = cycles.set(converted[0], converted[1]);

        return cycles;
      });
    } else if (evt.editMaterialInLog) {
      const edit = evt.editMaterialInLog;
      t.set(last30StationCyclesRW, (oldCycles) => process_swap(edit, oldCycles));
    }
  }
);

export const setSpecificMonthStationCycles = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    const estimatedCycleTimes = t.get(specificMonthEstimatedCycleTimes);
    t.set(
      specificMonthStationCyclesRW,
      LazySeq.ofIterable(log)
        .collect((c) => convertLogToCycle(estimatedCycleTimes, c))
        .toIMap((x) => x)
    );
  }
);
