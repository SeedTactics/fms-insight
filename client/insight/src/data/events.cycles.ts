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
import * as api from "../network/api";
import { durationToMinutes } from "../util/parseISODuration";
import { HashMap, HashSet, Vector, Option, fieldsHashCode } from "prelude-ts";
import { LazySeq } from "../util/lazyseq";
import {
  activeMinutes,
  EstimatedCycleTimes,
  LogEntryWithSplitElapsed,
  splitElapsedLoadTime,
  StatisticalCycleTime,
} from "../cell-status/estimated-cycle-times";

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
  readonly MAD_aboveMinutes: number;
  readonly isLabor: boolean;
  readonly material: ReadonlyArray<Readonly<api.ILogMaterial>>;
  readonly operator: string;
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

export interface CycleState {
  readonly part_cycles: Vector<PartCycleData>;
  readonly by_pallet: HashMap<string, ReadonlyArray<PalletCycleData>>;
}

export const initial: CycleState = {
  part_cycles: Vector.empty(),
  by_pallet: HashMap.empty(),
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

export function process_events(
  expire: ExpireOldData,
  estimatedCycleTimes: EstimatedCycleTimes,
  newEvts: ReadonlyArray<Readonly<api.ILogEntry>>,
  st: CycleState
): CycleState {
  let allPartCycles = st.part_cycles;
  let pals = st.by_pallet;

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

  const newCycles: LazySeq<PartCycleData> = LazySeq.ofIterable(newEvts)
    .filter((c) => !c.startofcycle && (c.type === api.LogType.LoadUnloadCycle || c.type === api.LogType.MachineCycle))
    .map((cycle) => {
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
        isLabor: cycle.type === api.LogType.LoadUnloadCycle,
        stationGroup: cycle.loc,
        stationNumber: cycle.locnum,
        operation: cycle.type === api.LogType.LoadUnloadCycle ? cycle.result : cycle.program,
        operator: cycle.details ? cycle.details.operator || "" : "",
      };
    })
    .filter((c) => c.stationGroup !== "");
  allPartCycles = allPartCycles.appendAll(newCycles);

  const newPalCycles = LazySeq.ofIterable(newEvts)
    .filter((c) => !c.startofcycle && c.type === api.LogType.PalletCycle && c.pal !== "")
    .groupBy((c) => c.pal)
    .mapValues((cyclesForPal) =>
      LazySeq.ofIterable(cyclesForPal)
        .map((c) => ({
          x: c.endUTC,
          y: durationToMinutes(c.elapsed),
          active: durationToMinutes(c.active),
          completed: false,
        }))
        .toArray()
    );
  pals = pals.mergeWith(newPalCycles, (oldCs, newCs) => oldCs.concat(newCs));

  // clear invalidated cycles
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

  if (invalidatedCycles.length() > 0) {
    allPartCycles = allPartCycles.map((x) => {
      if (invalidatedCycles.contains(x.cntr)) {
        x = { ...x, activeMinutes: 0 };
      }
      return x;
    });
  }

  return {
    part_cycles: allPartCycles,
    by_pallet: pals,
  };
}

export function process_swap(swap: Readonly<api.IEditMaterialInLogEvents>, st: CycleState): CycleState {
  const changedByCntr = LazySeq.ofIterable(swap.editedEvents).toMap(
    (e) => [e.counter, e],
    (e, _) => e
  );

  const partCycles = st.part_cycles.map((cycle) => {
    const changed = changedByCntr.get(cycle.cntr).getOrNull();
    if (changed) {
      return { ...cycle, material: changed.material };
    } else {
      return cycle;
    }
  });

  return { ...st, part_cycles: partCycles };
}
