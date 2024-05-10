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
import { ILogEntry, LogType } from "../network/api.js";
import type { ServerEventAndTime } from "./loading.js";
import { durationToMinutes } from "../util/parseISODuration.js";
import {
  EstimatedCycleTimes,
  isOutlier,
  last30EstimatedCycleTimes,
  PartAndStationOperation,
} from "./estimated-cycle-times.js";
import { LazySeq, HashMap } from "@seedtactics/immutable-collections";
import { Atom, atom } from "jotai";

export interface ProgramToolUseInSingleCycle {
  readonly tools: ReadonlyArray<{
    readonly toolName: string;
    readonly cycleUsageMinutes: number;
    readonly cycleUsageCnt: number;
    readonly toolChangedDuringMiddleOfCycle: boolean;
  }>;
}

export type ToolUsage = HashMap<PartAndStationOperation, ReadonlyArray<ProgramToolUseInSingleCycle>>;

const last30ToolUseRW = atom<ToolUsage>(
  HashMap.empty<PartAndStationOperation, ReadonlyArray<ProgramToolUseInSingleCycle>>(),
);
export const last30ToolUse: Atom<ToolUsage> = last30ToolUseRW;

function process_tools(
  cycle: Readonly<ILogEntry>,
  estimatedCycleTimes: EstimatedCycleTimes,
  toolUsage: ToolUsage,
): ToolUsage {
  if (cycle.tooluse === undefined || cycle.tooluse.length === 0 || cycle.type !== LogType.MachineCycle) {
    return toolUsage;
  }

  const stats =
    cycle.material.length > 0
      ? estimatedCycleTimes.get(PartAndStationOperation.ofLogCycle(cycle))
      : undefined;
  const elapsed = durationToMinutes(cycle.elapsed);
  if (stats === undefined || isOutlier(stats, elapsed)) {
    return toolUsage;
  }

  const key = PartAndStationOperation.ofLogCycle(cycle);
  const toolsUsedInCycle = LazySeq.of(cycle.tooluse)
    .groupBy((u) => u.tool)
    .map(([toolName, uses]) => {
      const useDuring = LazySeq.of(uses).sumBy((use) =>
        use.toolUseDuringCycle && use.toolUseDuringCycle !== ""
          ? durationToMinutes(use.toolUseDuringCycle)
          : 0,
      );
      const cntDuring = LazySeq.of(uses).sumBy((use) => use.toolUseCountDuringCycle ?? 0);
      return {
        toolName,
        cycleUsageMinutes: useDuring,
        cycleUsageCnt: cntDuring,
        toolChangedDuringMiddleOfCycle: LazySeq.of(uses).some((use) => use.toolChangeOccurred ?? false),
      };
    })
    .toRArray();

  if (toolsUsedInCycle.length === 0 && toolUsage.has(key)) {
    return toolUsage;
  }

  return toolUsage.modify(key, (old) => {
    if (old) {
      const n = old.slice(-4);
      n.push({ tools: toolsUsedInCycle });
      return n;
    } else {
      return [{ tools: toolsUsedInCycle }];
    }
  });
}

export const setLast30ToolUse = atom(null, (get, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  const estimated = get(last30EstimatedCycleTimes);
  set(last30ToolUseRW, (oldUsage) =>
    log.reduce((usage, log) => process_tools(log, estimated, usage), oldUsage),
  );
});

export const updateLast30ToolUse = atom(null, (get, set, { evt }: ServerEventAndTime) => {
  if (evt.logEntry) {
    const log = evt.logEntry;
    const estimated = get(last30EstimatedCycleTimes);
    set(last30ToolUseRW, (oldUsage) => process_tools(log, estimated, oldUsage));
  }
});
