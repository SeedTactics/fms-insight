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

import { IToolInMachine, ICurrentStatus, ActionType } from "./api";
import { LazySeq } from "./lazyseq";
import { Vector, HashMap } from "prelude-ts";
import { duration } from "moment";
import { ToolUsage, ProgramToolUseInSingleCycle, PartAndProgram } from "./events.cycles";

export function averageToolUse(usage: ToolUsage): HashMap<PartAndProgram, ProgramToolUseInSingleCycle> {
  return usage.mapValues((cycles) => ({
    tools: LazySeq.ofIterable(cycles)
      .flatMap((c) => c.tools)
      .filter((c) => c.toolChanged !== true)
      .groupBy((t) => t.toolName)
      .toArray()
      .map(([toolName, usageInCycles]) => ({
        toolName: toolName,
        cycleUsageMinutes: usageInCycles.sumOn((c) => c.cycleUsageMinutes) / usageInCycles.length(),
        toolChanged: false,
      })),
  }));
}

export interface ToolInMachine {
  readonly machineName: string;
  readonly pocket: number;
  readonly currentUseMinutes: number;
  readonly lifetimeMinutes: number;
  readonly remainingMinutes: number;
}

export interface PartToolUsage {
  readonly partName: string;
  readonly process: number;
  readonly program: string;
  readonly quantity: number;
  readonly scheduledUseMinutes: number;
}

export interface ToolReport {
  readonly toolName: string;
  readonly machines: Vector<ToolInMachine>;
  readonly minRemainingMinutes: number;
  readonly minRemainingMachine: string;
  readonly parts: Vector<PartToolUsage>;
}

export function calcToolSummary(
  allTools: Iterable<Readonly<IToolInMachine>>,
  usage: ToolUsage,
  currentSt: Readonly<ICurrentStatus>
): Vector<ToolReport> {
  let partPlannedQtys = HashMap.empty<PartAndProgram, number>();
  for (const [uniq, job] of LazySeq.ofObject(currentSt.jobs)) {
    const planQty = LazySeq.ofIterable(job.cyclesOnFirstProcess).sumOn((x) => x);
    for (let procIdx = 0; procIdx < job.procsAndPaths.length; procIdx++) {
      let completed = 0;
      let programsToAfterInProc = HashMap.empty<string, number>();
      for (let pathIdx = 0; pathIdx < job.procsAndPaths[procIdx].paths.length; pathIdx++) {
        completed += job.completed?.[procIdx]?.[pathIdx] ?? 0;
        const path = job.procsAndPaths[procIdx].paths[pathIdx];
        for (let stopIdx = 0; stopIdx < path.stops.length; stopIdx += 1) {
          const stop = path.stops[stopIdx];
          const inProcAfter = LazySeq.ofIterable(currentSt.material)
            .filter(
              (m) =>
                m.jobUnique === uniq &&
                m.process == procIdx + 1 &&
                m.path === pathIdx + 1 &&
                (m.action.type === ActionType.UnloadToCompletedMaterial ||
                  m.action.type === ActionType.UnloadToInProcess ||
                  (m.lastCompletedMachiningRouteStopIndex !== undefined &&
                    m.lastCompletedMachiningRouteStopIndex > stopIdx))
            )
            .length();
          if (stop.program !== undefined && stop.program !== "") {
            programsToAfterInProc = programsToAfterInProc.putWithMerge(stop.program, inProcAfter, (a, b) => a + b);
          }
        }
      }
      for (const [program, afterInProc] of programsToAfterInProc) {
        partPlannedQtys = partPlannedQtys.putWithMerge(
          new PartAndProgram(job.partName, procIdx + 1, program),
          planQty - completed - afterInProc,
          (a, b) => a + b
        );
      }
    }
  }

  const parts = LazySeq.ofIterable(averageToolUse(usage))
    .flatMap(([partAndProg, tools]) => {
      const qty = partPlannedQtys.get(partAndProg);
      if (qty.isSome() && qty.get() > 0) {
        return tools.tools.map((tool) => ({
          toolName: tool.toolName,
          part: {
            partName: partAndProg.part,
            process: partAndProg.proc,
            program: partAndProg.program,
            quantity: qty.get(),
            scheduledUseMinutes: tool.cycleUsageMinutes,
          },
        }));
      } else {
        return [];
      }
    })
    .groupBy((t) => t.toolName)
    .mapValues((v) =>
      v
        .map((t) => t.part)
        .sortOn(
          (p) => p.partName,
          (p) => p.process
        )
    );

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
        parts: parts.get(toolName).getOrElse(Vector.empty()),
      };
    });
}
