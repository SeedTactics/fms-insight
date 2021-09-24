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
import { HashMap, Option } from "prelude-ts";
import { atom, RecoilValueReadOnly, TransactionInterface_UNSTABLE } from "recoil";
import { PartAndStationOperation } from "../data/events.cycles";
import { ILogEntry, LogType } from "../network/api";
import { LazySeq } from "../util/lazyseq";
import { conduit } from "../util/recoil-util";
import type { ServerEventAndTime } from "./loading";
import * as L from "list/methods";
import { durationToMinutes } from "../util/parseISODuration";
import {
  EstimatedCycleTimes,
  isOutlier,
  last30EstimatedCycleTimes,
  StatisticalCycleTime,
} from "./estimated-cycle-times";

export interface ProgramToolUseInSingleCycle {
  readonly tools: ReadonlyArray<{
    readonly toolName: string;
    readonly cycleUsageMinutes: number;
    readonly toolChanged: boolean;
  }>;
}

export type ToolUsage = HashMap<PartAndStationOperation, L.List<ProgramToolUseInSingleCycle>>;

const last30ToolUseRW = atom<ToolUsage>({
  key: "last30ToolUse",
  default: HashMap.empty(),
});
export const last30ToolUse: RecoilValueReadOnly<ToolUsage> = last30ToolUseRW;

function process_tools(
  cycle: Readonly<ILogEntry>,
  estimatedCycleTimes: EstimatedCycleTimes,
  toolUsage: ToolUsage
): ToolUsage {
  if (cycle.tools === undefined || cycle.type !== LogType.MachineCycle) {
    return toolUsage;
  }

  const stats =
    cycle.material.length > 0
      ? estimatedCycleTimes.get(PartAndStationOperation.ofLogCycle(cycle))
      : Option.none<StatisticalCycleTime>();
  const elapsed = durationToMinutes(cycle.elapsed);
  if (stats.isNone() || isOutlier(stats.get(), elapsed)) {
    return toolUsage;
  }

  const key = PartAndStationOperation.ofLogCycle(cycle);
  const toolsUsedInCycle = LazySeq.ofObject(cycle.tools)
    .map(([toolName, use]) => ({
      toolName,
      cycleUsageMinutes: use.toolUseDuringCycle === "" ? 0 : durationToMinutes(use.toolUseDuringCycle),
      toolChanged: use.toolChangeOccurred === true,
    }))
    .toArray();

  if (toolsUsedInCycle.length === 0 && toolUsage.containsKey(key)) {
    return toolUsage;
  }

  return toolUsage.putWithMerge(
    key,
    toolsUsedInCycle.length === 0 ? L.empty() : L.of({ tools: toolsUsedInCycle }),
    (oldV, newV) => oldV.drop(Math.max(0, oldV.length - 4)).concat(newV)
  );
}

export const setLast30ToolUse = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    const estimated = t.get(last30EstimatedCycleTimes);
    t.set(last30ToolUseRW, (oldUsage) => log.reduce((usage, log) => process_tools(log, estimated, usage), oldUsage));
  }
);

export const updateLast30ToolUse = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt }: ServerEventAndTime) => {
    if (evt.logEntry) {
      const log = evt.logEntry;
      const estimated = t.get(last30EstimatedCycleTimes);
      t.set(last30ToolUseRW, (oldUsage) => process_tools(log, estimated, oldUsage));
    }
  }
);
