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
import { atom, selector, TransactionInterface_UNSTABLE } from "recoil";
import { ILogEntry, LogType, ToolUse } from "../network/api.js";
import { LazySeq, HashMap, OrderedMap, HashableObj, hashValues } from "@seedtactics/immutable-collections";
import { durationToMinutes } from "../util/parseISODuration.js";
import { conduit } from "../util/recoil-util.js";
import type { ServerEventAndTime } from "./loading.js";

export type ToolReplacement =
  | {
      tool: string;
      type: "ReplaceBeforeCycleStart";
      useAtReplacement: number;
    }
  | {
      tool: string;
      type: "ReplaceInCycle";

      // when a replacement happens in the middle of the cycle, we don't know exactly how much usage there was.
      // We instead estimate it by recording the total use at the beginning and end of the cycle and estimate using
      // the calculated average usage (from tool-usage.ts)

      // Thus, useAtReplacement estimate = totalUseAtBeginningOfCycle + (averageUse - totalUseAtEndOfCycle)
      totalUseAtBeginningOfCycle: number;
      totalUseAtEndOfCycle: number;
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
}

export type ToolReplacementsByCntr = OrderedMap<number, ToolReplacements>;
export type ToolReplacementsByStation = HashMap<StationGroupAndNum, ToolReplacementsByCntr>;
type MostRecentUseByStation = HashMap<StationGroupAndNum, { [key: string]: ToolUse }>;

type ReplacementsAndMostRecentUse = {
  readonly replacements: ToolReplacementsByStation;
  readonly recentUse: MostRecentUseByStation;
};

const emptyReplacementsAndUse: ReplacementsAndMostRecentUse = {
  replacements: HashMap.empty(),
  recentUse: HashMap.empty(),
};

const last30ToolReplacementsRW = atom<ReplacementsAndMostRecentUse>({
  key: "last30ToolReplacementsAndUse",
  default: emptyReplacementsAndUse,
});
export const last30ToolReplacements = selector<ToolReplacementsByStation>({
  key: "last30ToolReplacements",
  get: ({ get }) => get(last30ToolReplacementsRW).replacements,
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

const specificMonthToolReplacementsRW = atom<ReplacementsAndMostRecentUse>({
  key: "specificMonthToolReplacementsAndUse",
  default: emptyReplacementsAndUse,
});
export const specificMonthToolReplacements = selector<ToolReplacementsByStation>({
  key: "specificMonthToolReplacements",
  get: ({ get }) => get(specificMonthToolReplacementsRW).replacements,
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

function addReplacementsFromLog(
  old: ReplacementsAndMostRecentUse,
  e: Readonly<ILogEntry>
): ReplacementsAndMostRecentUse {
  const key = StationGroupAndNum.ofLogCycle(e);
  const newReplacements = old.replacements.alter(key, (cycles) => {
    const replacements: Array<ToolReplacement> = [];
    for (const [tool, use] of LazySeq.ofObject(e.tools ?? {})) {
      // see server/lib/BlackMaple.MachineFramework/backend/ToolSnapshotDiff.cs for the original calculations
      const useDuring = durationToMinutes(use.toolUseDuringCycle);
      const totalUseAtEnd = durationToMinutes(use.totalToolUseAtEndOfCycle);

      if (use.toolChangeOccurred && useDuring === totalUseAtEnd && useDuring > 0) {
        // replace before cycle start
        const last = old.recentUse.get(key)?.[tool];
        if (last) {
          const lastTotalUse = durationToMinutes(use.totalToolUseAtEndOfCycle);
          if (lastTotalUse > 0) {
            replacements.push({
              tool,
              type: "ReplaceBeforeCycleStart",
              useAtReplacement: lastTotalUse,
            });
          }
        }
      } else if (use.toolChangeOccurred && useDuring > 0 && totalUseAtEnd > 0 && use.configuredToolLife) {
        // replace in the middle of the cycle

        // the server calculates  useDuring = lifetime - useAtStartOfCycle + useAtEndOfCycle
        // so solving for useAtStart = lifetime - useDuring + useAtEndOfCycle
        const lifetime = durationToMinutes(use.configuredToolLife);
        replacements.push({
          tool,
          type: "ReplaceInCycle",
          totalUseAtBeginningOfCycle: lifetime - useDuring + totalUseAtEnd,
          totalUseAtEndOfCycle: totalUseAtEnd,
        });
      }
    }
    if (replacements.length > 0) {
      return (cycles ?? OrderedMap.empty()).set(e.counter, { time: e.endUTC, replacements });
    } else {
      return cycles;
    }
  });

  const newRecent = e.tools ? old.recentUse.set(key, e.tools) : old.recentUse;

  return { replacements: newReplacements, recentUse: newRecent };
}

export const setLast30ToolReplacements = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(last30ToolReplacementsRW, (oldCycles) =>
      LazySeq.of(log)
        .filter((e) => e.type === LogType.MachineCycle && !e.startofcycle && !!e.tools)
        .foldLeft(oldCycles, addReplacementsFromLog)
    );
  }
);

export const updateLastToolReplacements = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt, now, expire }: ServerEventAndTime) => {
    if (
      evt.logEntry &&
      !evt.logEntry.startofcycle &&
      evt.logEntry.type === LogType.MachineCycle &&
      evt.logEntry.tools
    ) {
      const log = evt.logEntry;

      t.set(last30ToolReplacementsRW, (oldCycles) => {
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
  }
);

export const setSpecificMonthToolReplacements = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(
      specificMonthToolReplacementsRW,
      LazySeq.of(log)
        .filter((e) => e.type === LogType.MachineCycle && !e.startofcycle && !!e.tools)
        .foldLeft(emptyReplacementsAndUse, addReplacementsFromLog)
    );
  }
);