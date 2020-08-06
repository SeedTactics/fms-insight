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

import { ActionType } from "./api";
import { LazySeq } from "./lazyseq";
import { Vector, HashMap } from "prelude-ts";
import { duration } from "moment";
import {
  ToolUsage,
  ProgramToolUseInSingleCycle,
  StatisticalCycleTime,
  PartAndStationOperation,
  StationOperation,
} from "./events.cycles";
import { Store } from "../store/store";
import * as redux from "redux";
import { MachineBackend } from "./backend";

function averageToolUse(
  usage: ToolUsage,
  sort: boolean
): HashMap<PartAndStationOperation, ProgramToolUseInSingleCycle> {
  return usage.mapValues((cycles) => {
    const tools = LazySeq.ofIterable(cycles)
      .flatMap((c) => c.tools)
      .filter((c) => c.toolChanged !== true)
      .groupBy((t) => t.toolName)
      .toArray()
      .map(([toolName, usageInCycles]) => ({
        toolName: toolName,
        cycleUsageMinutes: usageInCycles.sumOn((c) => c.cycleUsageMinutes) / usageInCycles.length(),
        toolChanged: false,
      }));
    if (sort) {
      tools.sort((a, b) => a.toolName.localeCompare(b.toolName));
    }
    return { tools };
  });
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

export async function calcToolSummary(
  store: redux.Store<Store>
): Promise<{ readonly report: Vector<ToolReport>; readonly time: Date }> {
  const currentSt = store.getState().Current.current_status;
  const usage = store.getState().Events.last30.cycles.tool_usage;
  let partPlannedQtys = HashMap.empty<PartAndStationOperation, number>();
  for (const [uniq, job] of LazySeq.ofObject(currentSt.jobs)) {
    const planQty = LazySeq.ofIterable(job.cyclesOnFirstProcess).sumOn((x) => x);
    for (let procIdx = 0; procIdx < job.procsAndPaths.length; procIdx++) {
      let completed = 0;
      let programsToAfterInProc = HashMap.empty<StationOperation, number>();
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
            programsToAfterInProc = programsToAfterInProc.putWithMerge(
              new StationOperation(stop.stationGroup, stop.program),
              inProcAfter,
              (a, b) => a + b
            );
          }
        }
      }
      for (const [op, afterInProc] of programsToAfterInProc) {
        partPlannedQtys = partPlannedQtys.putWithMerge(
          new PartAndStationOperation(job.partName, procIdx + 1, op.statGroup, op.operation),
          planQty - completed - afterInProc,
          (a, b) => a + b
        );
      }
    }
  }

  const parts = LazySeq.ofIterable(averageToolUse(usage, false))
    .flatMap(([partAndProg, tools]) => {
      const qty = partPlannedQtys.get(partAndProg);
      if (qty.isSome() && qty.get() > 0) {
        return tools.tools.map((tool) => ({
          toolName: tool.toolName,
          part: {
            partName: partAndProg.part,
            process: partAndProg.proc,
            program: partAndProg.operation,
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

  const toolsInMach = await MachineBackend.getToolsInMachines();
  return {
    time: new Date(),
    report: LazySeq.ofIterable(toolsInMach)
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
      }),
  };
}

export interface CellControllerProgram {
  readonly programName: string;
  readonly cellControllerProgramName: string;
  readonly comment: string | null;
  readonly revision: number | null;
  readonly partName: string | null;
  readonly process: number | null;
  readonly statisticalCycleTime: StatisticalCycleTime | null;
  readonly toolUse: ProgramToolUseInSingleCycle | null;
}
export async function calcProgramSummary(
  store: redux.Store<Store>
): Promise<{ readonly programs: Vector<CellControllerProgram>; readonly time: Date }> {
  const tools = averageToolUse(store.getState().Events.last30.cycles.tool_usage, true);
  const cycleTimes = store.getState().Events.last30.cycles.estimatedCycleTimes;

  const progToPart = LazySeq.ofIterable(store.getState().Events.last30.cycles.active_cycle_times)
    .flatMap(([partAndProc, ops]) =>
      LazySeq.ofIterable(ops.keySet()).map((op) => ({
        op: op.operation,
        part: new PartAndStationOperation(partAndProc.part, partAndProc.proc, op.statGroup, op.operation),
      }))
    )
    .toMap(
      (x) => [x.op, x.part],
      (_, x) => x
    );

  return {
    time: new Date(),
    programs: LazySeq.ofIterable(await MachineBackend.getProgramsInCellController())
      .map((prog) => {
        const part = progToPart.get(prog.programName).getOrNull();
        return {
          programName: prog.programName,
          cellControllerProgramName: prog.cellControllerProgramName,
          comment: prog.comment ?? null,
          revision: prog.revision ?? null,
          partName: part?.part ?? null,
          process: part?.proc ?? null,
          statisticalCycleTime: part ? cycleTimes.get(part).getOrNull() : null,
          toolUse: part ? tools.get(part).getOrNull() : null,
        };
      })
      .toVector(),
  };
}

export type Action =
  | { readonly type: "Tools_SetToolReport"; readonly report: Vector<ToolReport>; readonly time: Date }
  | { readonly type: "Programs_SetProgramData"; readonly programs: Vector<CellControllerProgram>; readonly time: Date };

export interface State {
  readonly tool_report: Vector<ToolReport> | null;
  readonly tool_report_time: Date | null;
  readonly programs: Vector<CellControllerProgram> | null;
  readonly program_time: Date | null;
}

export const initial: State = {
  tool_report: null,
  tool_report_time: null,
  programs: null,
  program_time: null,
};

export function reducer(s: State, a: Action): State {
  if (s === undefined) return initial;

  switch (a.type) {
    case "Tools_SetToolReport":
      return { ...s, tool_report: a.report, tool_report_time: a.time };
    case "Programs_SetProgramData":
      return { ...s, programs: a.programs, program_time: a.time };
    default:
      return s;
  }
}
