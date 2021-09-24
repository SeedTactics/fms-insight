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
import * as L from "list/methods";
import type { ServerEventAndTime } from "./loading";
import { LazySeq } from "../util/lazyseq";
import { Option } from "prelude-ts";
import {
  activeMinutes,
  EstimatedCycleTimes,
  last30EstimatedCycleTimes,
  LogEntryWithSplitElapsed,
  PartAndStationOperation,
  splitElapsedLoadTime,
  StatisticalCycleTime,
} from "./estimated-cycle-times";
import { durationToMinutes } from "../util/parseISODuration";
import { addDays } from "date-fns";

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

const last30StationCyclesRW = atom<L.List<PartCycleData>>({
  key: "last30StationCycles",
  default: L.empty(),
});
export const last30StationCycles: RecoilValueReadOnly<L.List<PartCycleData>> = last30StationCyclesRW;

const specificMonthStationCyclesRW = atom<L.List<PartCycleData>>({
  key: "specificMonthStationCycles",
  default: L.empty(),
});
export const specificMonthStationCycles: RecoilValueReadOnly<L.List<PartCycleData>> = specificMonthStationCyclesRW;

function convertLogToCycle(estimatedCycleTimes: EstimatedCycleTimes, cycle: ILogEntry): PartCycleData | null {
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
    cycle.material.length > 0
      ? estimatedCycleTimes.get(PartAndStationOperation.ofLogCycle(cycle))
      : Option.none<StatisticalCycleTime>();
  const elapsed = durationToMinutes(cycle.elapsed);
  return {
    x: cycle.endUTC,
    y: elapsed,
    cntr: cycle.counter,
    activeMinutes: activeMinutes(cycle, stats.getOrNull()),
    medianCycleMinutes: stats.map((s) => s.medianMinutesForSingleMat).getOrElse(0) * cycle.material.length,
    MAD_aboveMinutes: stats.map((s) => s.MAD_aboveMinutes).getOrElse(0),
    part: part,
    process: proc,
    pallet: cycle.pal,
    material: cycle.material,
    isLabor: cycle.type === LogType.LoadUnloadCycle,
    stationGroup: cycle.loc,
    stationNumber: cycle.locnum,
    operation: cycle.type === LogType.LoadUnloadCycle ? cycle.result : cycle.program,
    operator: cycle.details ? cycle.details.operator || "" : "",
  };
}

function process_swap(
  swap: Readonly<IEditMaterialInLogEvents>,
  partCycles: L.List<PartCycleData>
): L.List<PartCycleData> {
  const changedByCntr = LazySeq.ofIterable(swap.editedEvents).toMap(
    (e) => [e.counter, e],
    (e, _) => e
  );

  return partCycles.map((cycle) => {
    const changed = changedByCntr.get(cycle.cntr).getOrNull();
    if (changed) {
      return { ...cycle, material: changed.material };
    } else {
      return cycle;
    }
  });
}

export const setLast30StationCycles = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    const estimatedCycleTimes = t.get(last30EstimatedCycleTimes);
    t.set(last30StationCyclesRW, (oldCycles) =>
      oldCycles.concat(L.from(LazySeq.ofIterable(log).collect((c) => convertLogToCycle(estimatedCycleTimes, c))))
    );
  }
);

export const updateLast30StationCycles = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt, now, expire }: ServerEventAndTime) => {
    if (evt.logEntry && evt.logEntry.type === LogType.InvalidateCycle) {
      const cntrs = evt.logEntry.details?.["EditedCounters"];
      const invalidatedCycles = cntrs ? new Set(cntrs.split(",").map((i) => parseInt(i))) : new Set();

      if (invalidatedCycles.size > 0) {
        t.set(last30StationCyclesRW, (cycles) =>
          cycles.map((x) => {
            if (invalidatedCycles.has(x.cntr)) {
              x = { ...x, activeMinutes: 0 };
            }
            return x;
          })
        );
      }
    } else if (evt.logEntry) {
      const estimatedCycleTimes = t.get(last30EstimatedCycleTimes);
      const converted = convertLogToCycle(estimatedCycleTimes, evt.logEntry);
      if (!converted) return;

      t.set(last30StationCyclesRW, (cycles) => {
        if (expire) {
          const thirtyDaysAgo = addDays(now, -30);
          cycles = cycles.filter((e) => e.x >= thirtyDaysAgo);
        }

        cycles = cycles.append(converted);

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
    const estimatedCycleTimes = t.get(last30EstimatedCycleTimes);
    t.set(
      last30StationCyclesRW,
      L.from(LazySeq.ofIterable(log).collect((c) => convertLogToCycle(estimatedCycleTimes, c)))
    );
  }
);
