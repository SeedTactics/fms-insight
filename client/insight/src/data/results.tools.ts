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

import { IToolInMachine } from "./api";
import { LazySeq } from "./lazyseq";
import { Vector } from "prelude-ts";
import { duration } from "moment";

export interface ToolInMachine {
  readonly machineName: string;
  readonly pocket: number;
  readonly currentUseMinutes: number;
  readonly lifetimeMinutes: number;
  readonly remainingMinutes: number;
}

export interface ToolReport {
  readonly toolName: string;
  readonly machines: Vector<ToolInMachine>;
  readonly minRemainingMinutes: number;
  readonly minRemainingMachine: string;
}

export function calcToolSummary(allTools: Iterable<Readonly<IToolInMachine>>): Vector<ToolReport> {
  return LazySeq.ofIterable(allTools)
    .groupBy((t) => t.toolName)
    .toVector()
    .map(([toolName, tools]) => {
      const toolsInMachine = tools
        .sortOn(
          (t) => t.machineGroupName,
          (t) => t.machineNum,
          (t) => t.pocket
        )
        .map((m) => {
          const currentUseMinutes = m.currentUse !== "" ? duration(m.currentUse).asMinutes() : 0;
          const lifetimeMinutes = m.totalLifeTime !== "" ? duration(m.totalLifeTime).asMinutes() : 0;
          return {
            machineName: m.machineGroupName + " #" + m.machineNum,
            pocket: m.pocket,
            currentUseMinutes,
            lifetimeMinutes,
            remainingMinutes: Math.max(0, lifetimeMinutes - currentUseMinutes),
          };
        });

      const minMachine = toolsInMachine
        .groupBy((m) => m.machineName)
        .toVector()
        .map(([machineName, toolsForMachine]) => ({
          machineName,
          remaining: toolsForMachine.sumOn((m) => m.remainingMinutes),
        }))
        .minOn((m) => m.remaining);

      return {
        toolName,
        machines: toolsInMachine,
        minRemainingMachine: minMachine.map((m) => m.machineName).getOrElse(""),
        minRemainingMinutes: minMachine.map((m) => m.remaining).getOrElse(0),
      };
    });
}
