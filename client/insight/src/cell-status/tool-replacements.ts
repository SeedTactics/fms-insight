/* Copyright (c) 2022, John Lenz

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
import { addDays } from "date-fns";
import { ILogEntry, LogType, ToolUse } from "../network/api.js";
import { LazySeq, HashMap, OrderedMap, HashableObj, hashValues } from "@seedtactics/immutable-collections";
import { durationToMinutes } from "../util/parseISODuration.js";
import type { ServerEventAndTime } from "./loading.js";
import { atom } from "jotai";

export type ToolReplacement =
  | {
      readonly tool: string;
      readonly pocket: number;
      readonly type: "ReplaceBeforeCycleStart";
      readonly useAtReplacement: number | null;
      readonly cntAtReplacement: number | null;
    }
  | {
      readonly tool: string;
      readonly pocket: number;
      readonly type: "ReplaceInCycle";

      // when a replacement happens in the middle of the cycle, we don't know exactly how much usage there was.
      // We instead estimate it by recording the total use at the beginning and end of the cycle and estimate using
      // the calculated average usage (from tool-usage.ts)

      // Thus, useAtReplacement estimate = totalUseAtBeginningOfCycle + (averageUse - totalUseAtEndOfCycle)
      readonly totalUseAtBeginningOfCycle: number | null;
      readonly totalUseAtEndOfCycle: number | null;
      readonly totalCntAtBeginningOfCycle: number | null;
      readonly totalCntAtEndOfCycle: number | null;
    };

export type ToolReplacements = {
  readonly time: Date;
  readonly replacements: ReadonlyArray<ToolReplacement>;
};

export class StationGroupAndNum implements HashableObj {
  readonly group: string;
  readonly num: number;
  constructor(group: string, num: number) {
    this.group = group;
    this.num = num;
  }
  static ofLogCycle(e: Readonly<ILogEntry>): StationGroupAndNum {
    return new StationGroupAndNum(e.loc, e.locnum);
  }
  hash(): number {
    return hashValues(this.group, this.num);
  }
  compare(other: StationGroupAndNum): number {
    return this.group === other.group ? this.num - other.num : this.group.localeCompare(other.group);
  }
  toString(): string {
    return this.group + " #" + this.num.toString();
  }
}

export type ToolReplacementsByCntr = OrderedMap<number, ToolReplacements>;
export type ToolReplacementsByStation = OrderedMap<StationGroupAndNum, ToolReplacementsByCntr>;
type MostRecentUseByStation = HashMap<StationGroupAndNum, ReadonlyArray<ToolUse>>;

type ReplacementsAndMostRecentUse = {
  readonly replacements: ToolReplacementsByStation;
  readonly recentUse: MostRecentUseByStation;
};

const emptyReplacementsAndUse: ReplacementsAndMostRecentUse = {
  replacements: OrderedMap.empty(),
  recentUse: HashMap.empty(),
};

const last30ToolReplacementsRW = atom<ReplacementsAndMostRecentUse>(emptyReplacementsAndUse);
export const last30ToolReplacements = atom<ToolReplacementsByStation>(
  (get) => get(last30ToolReplacementsRW).replacements
);

const specificMonthToolReplacementsRW = atom<ReplacementsAndMostRecentUse>(emptyReplacementsAndUse);
export const specificMonthToolReplacements = atom<ToolReplacementsByStation>(
  (get) => get(specificMonthToolReplacementsRW).replacements
);

function addReplacementsFromLog(
  old: ReplacementsAndMostRecentUse,
  e: Readonly<ILogEntry>
): ReplacementsAndMostRecentUse {
  const key = StationGroupAndNum.ofLogCycle(e);
  const newReplacements = old.replacements.alter(key, (cycles) => {
    const replacements: Array<ToolReplacement> = [];
    for (const use of e.tooluse ?? []) {
      // see server/lib/BlackMaple.MachineFramework/backend/ToolSnapshotDiff.cs for the original calculations
      const useDuring =
        use.toolUseDuringCycle !== null && use.toolUseDuringCycle !== undefined
          ? durationToMinutes(use.toolUseDuringCycle)
          : null;
      const totalUseAtEnd =
        use.totalToolUseAtEndOfCycle !== null && use.totalToolUseAtEndOfCycle !== undefined
          ? durationToMinutes(use.totalToolUseAtEndOfCycle)
          : null;
      const cntDuring = use.toolUseCountDuringCycle ?? null;
      const totalCntAtEnd = use.totalToolUseCountAtEndOfCycle ?? null;

      if ((useDuring || cntDuring) && useDuring === totalUseAtEnd && cntDuring === totalCntAtEnd) {
        // replace before cycle start
        const last = old.recentUse.get(key)?.find((e) => e.tool === use.tool && e.pocket === use.pocket);
        if (last) {
          const lastTotalUse = last.totalToolUseAtEndOfCycle
            ? durationToMinutes(last.totalToolUseAtEndOfCycle)
            : null;
          if (lastTotalUse || last.totalToolUseCountAtEndOfCycle) {
            replacements.push({
              tool: use.tool,
              pocket: use.pocket,
              type: "ReplaceBeforeCycleStart",
              useAtReplacement: lastTotalUse,
              cntAtReplacement: last.totalToolUseCountAtEndOfCycle ?? null,
            });
          }
        }
      } else if (
        use.toolChangeOccurred &&
        ((useDuring && use.configuredToolLife) || (cntDuring && use.configuredToolLifeCount))
      ) {
        // replace in the middle of the cycle

        // the server calculates  useDuring = lifetime - useAtStartOfCycle + useAtEndOfCycle
        // so solving for useAtStart = lifetime - useDuring + useAtEndOfCycle
        const lifetime =
          use.configuredToolLife !== null && use.configuredToolLife !== undefined
            ? durationToMinutes(use.configuredToolLife)
            : null;
        const lifeCnt = use.configuredToolLifeCount ?? null;
        replacements.push({
          tool: use.tool,
          pocket: use.pocket,
          type: "ReplaceInCycle",
          totalUseAtBeginningOfCycle:
            lifetime !== null && useDuring !== null ? lifetime - useDuring + (totalUseAtEnd ?? 0) : null,
          totalUseAtEndOfCycle: totalUseAtEnd,
          totalCntAtBeginningOfCycle:
            lifeCnt !== null && cntDuring !== null ? lifeCnt - cntDuring + (totalCntAtEnd ?? 0) : null,
          totalCntAtEndOfCycle: totalCntAtEnd,
        });
      }
    }
    if (replacements.length > 0) {
      return (cycles ?? OrderedMap.empty()).set(e.counter, { time: e.endUTC, replacements });
    } else {
      return cycles;
    }
  });

  const newRecent = e.tooluse ? old.recentUse.set(key, e.tooluse) : old.recentUse;

  return { replacements: newReplacements, recentUse: newRecent };
}

export const setLast30ToolReplacements = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(last30ToolReplacementsRW, (oldCycles) =>
    LazySeq.of(log)
      .filter(
        (e) => e.type === LogType.MachineCycle && !e.startofcycle && !!e.tooluse && e.tooluse.length > 0
      )
      .foldLeft(oldCycles, addReplacementsFromLog)
  );
});

export const updateLastToolReplacements = atom(null, (_, set, { evt, now, expire }: ServerEventAndTime) => {
  if (
    evt.logEntry &&
    !evt.logEntry.startofcycle &&
    evt.logEntry.type === LogType.MachineCycle &&
    evt.logEntry.tooluse &&
    evt.logEntry.tooluse.length > 0
  ) {
    const log = evt.logEntry;

    set(last30ToolReplacementsRW, (oldCycles) => {
      if (expire) {
        const thirtyDaysAgo = addDays(now, -30);
        const newReplace = oldCycles.replacements.collectValues((es) => {
          const newEs = es.filter((e) => e.time >= thirtyDaysAgo);
          return newEs.size > 0 ? newEs : null;
        });
        oldCycles = { replacements: newReplace, recentUse: oldCycles.recentUse };
      }

      return addReplacementsFromLog(oldCycles, log);
    });
  }
});

export const setSpecificMonthToolReplacements = atom(
  null,
  (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    set(
      specificMonthToolReplacementsRW,
      LazySeq.of(log)
        .filter(
          (e) => e.type === LogType.MachineCycle && !e.startofcycle && !!e.tooluse && e.tooluse.length > 0
        )
        .foldLeft(emptyReplacementsAndUse, addReplacementsFromLog)
    );
  }
);
