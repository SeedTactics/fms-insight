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

import { ActionType, ICurrentStatus, IProgramInCellController, IToolInMachine } from "./api";
import { LazySeq } from "./lazyseq";
import { Vector, HashMap, HashSet } from "prelude-ts";
import { durationToMinutes } from "./parseISODuration";
import { atom, selector, useRecoilCallback } from "recoil";
import {
  ToolUsage,
  ProgramToolUseInSingleCycle,
  StatisticalCycleTime,
  PartAndStationOperation,
  StationOperation,
  EstimatedCycleTimes,
  stat_name_and_num,
} from "./events.cycles";
import { reduxStore } from "../store/store";
import { MachineBackend } from "./backend";
import { currentStatus } from "./current-status";
import copy from "copy-to-clipboard";

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

export function calcToolReport(
  currentSt: Readonly<ICurrentStatus>,
  toolsInMach: ReadonlyArray<Readonly<IToolInMachine>>,
  usage: ToolUsage,
  machineFilter: string | null
): Vector<ToolReport> {
  let partPlannedQtys = HashMap.empty<PartAndStationOperation, number>();
  for (const [uniq, job] of LazySeq.ofObject(currentSt.jobs)) {
    const planQty = job.cycles ?? 0;
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

  return LazySeq.ofIterable(toolsInMach)
    .filter((t) => machineFilter === null || machineFilter === stat_name_and_num(t.machineGroupName, t.machineNum))
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
          const currentUseMinutes = m.currentUse !== "" ? durationToMinutes(m.currentUse) : 0;
          const lifetimeMinutes = m.totalLifeTime && m.totalLifeTime !== "" ? durationToMinutes(m.totalLifeTime) : 0;
          return {
            machineName: m.machineGroupName + " #" + m.machineNum.toString(),
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

export const toolReportMachineFilter = atom<string | null>({
  key: "tool-report-machine-filter",
  default: null,
});

export const toolReportRefreshTime = atom<Date | null>({
  key: "tool-report-refresh-time",
  default: null,
});

/*
This code is the way when we can use react concurrent mode and useTransition,
otherwise the table flashes each time the currentStatus changes.

export const currentToolReport = selector<Vector<ToolReport> | null>({
  key: "tool-report",
  get: async ({ get }) => {
    const t = get(toolReportRefreshTime);
    if (t === null) {
      return null;
    } else {
      if (reduxStore === null) return null;
      const machineFilter = get(toolReportMachineFilter);
      const currentSt = get(currentStatus);
      const toolsInMach = await MachineBackend.getToolsInMachines();
      const usage = reduxStore.getState().Events.last30.cycles.tool_usage;
      return calcToolReport(currentSt, usage, machineFilter);
    }
  },
});
*/

const toolsInMachine = atom<ReadonlyArray<Readonly<IToolInMachine>> | null>({
  key: "tools-in-machines",
  default: null,
});

export const currentToolReport = selector<Vector<ToolReport> | null>({
  key: "tool-report",
  get: ({ get }) => {
    if (reduxStore === null) return null;
    const toolsInMach = get(toolsInMachine);
    if (toolsInMach === null) return null;
    const machineFilter = get(toolReportMachineFilter);
    const currentSt = get(currentStatus);
    const usage = reduxStore.getState().Events.last30.cycles.tool_usage;
    return calcToolReport(currentSt, toolsInMach, usage, machineFilter);
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export function useRefreshToolReport(): () => Promise<void> {
  return useRecoilCallback(({ set }) => async () => {
    set(toolsInMachine, await MachineBackend.getToolsInMachines());
    set(toolReportRefreshTime, new Date());
  });
}

export function buildToolReportHTML(tools: Vector<ToolReport>, singleMachine: boolean): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Tool</th>";
  table += "<th>Scheduled Use (min)</th>";
  table += "<th>Total Remaining Life (min)</th>";
  if (singleMachine) {
    table += "<th>Pockets</th>";
  } else {
    table += "<th>Smallest Remaining Life (min)</th>";
    table += "<th>Machine With Smallest Remaining Life</th>";
  }
  table += "</tr></thead>\n<tbody>\n";

  for (const tool of tools) {
    table += "<tr><td>" + tool.toolName + "</td>";

    const schUse = tool.parts.sumOn((p) => p.scheduledUseMinutes * p.quantity);
    table += "<td>" + schUse.toFixed(1) + "</td>";

    const totalLife = tool.machines.sumOn((m) => m.remainingMinutes);
    table += "<td>" + totalLife.toFixed(1) + "</td>";

    if (singleMachine) {
      const pockets = tool.machines.map((m) => m.pocket.toString()).toArray();
      table += "<td>" + pockets.join(", ") + "</td>";
    } else {
      table += "<td>" + tool.minRemainingMinutes.toFixed(1) + "</td>";
      table += "<td>" + tool.minRemainingMachine + "</td>";
    }
    table += "</tr>\n";
  }
  table += "</tbody></table>";

  return table;
}

export function copyToolReportToClipboard(tools: Vector<ToolReport>, singleMachine: boolean): void {
  // eslint-disable-next-line @typescript-eslint/no-unsafe-call
  copy(buildToolReportHTML(tools, singleMachine));
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

export interface ProgramReport {
  readonly time: Date;
  readonly programs: Vector<CellControllerProgram>;
  readonly hasRevisions: boolean;
  readonly cellNameDifferentFromProgName: boolean;
}

export function calcProgramReport(
  usage: ToolUsage,
  cycleTimes: EstimatedCycleTimes,
  partOperations: HashSet<PartAndStationOperation>,
  progsInCellCtrl: ReadonlyArray<Readonly<IProgramInCellController>>,
  progsToShow: ReadonlySet<string> | null
): ProgramReport {
  const tools = averageToolUse(usage, true);

  const progToPart = LazySeq.ofIterable(partOperations)
    .map((op) => ({
      program: op.operation,
      part: op,
    }))
    .toMap(
      (x) => [x.program, x.part],
      (_, x) => x
    );

  let allPrograms = LazySeq.ofIterable(progsInCellCtrl);

  if (progsToShow != null) {
    allPrograms = allPrograms.filter((p) => progsToShow.has(p.programName));
  }

  const programs = allPrograms
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
    .toVector();

  return {
    time: new Date(),
    programs,
    hasRevisions: programs.find((p) => p.revision !== null).isSome(),
    cellNameDifferentFromProgName: programs.find((p) => p.cellControllerProgramName !== p.programName).isSome(),
  };
}

export const programReportRefreshTime = atom<Date | null>({
  key: "program-report-refresh-time",
  default: null,
});

export const programFilter = atom<"AllPrograms" | "ActivePrograms">({
  key: "program-report-filter",
  default: "AllPrograms",
});

const programsInCellCtrl = atom<ReadonlyArray<Readonly<IProgramInCellController>> | null>({
  key: "programs-in-cell-controller",
  default: null,
});

export const currentProgramReport = selector<ProgramReport | null>({
  key: "cell-controller-programs",
  get: ({ get }) => {
    const time = get(programReportRefreshTime);
    if (time === null || reduxStore === null) return null;

    const filter = get(programFilter);
    let progsToShow: ReadonlySet<string> | null = null;
    if (filter === "ActivePrograms") {
      const status = get(currentStatus);
      progsToShow = new Set(
        LazySeq.ofObject(status.jobs)
          .flatMap(([, job]) => job.procsAndPaths)
          .flatMap((p) => p.paths)
          .flatMap((p) => p.stops)
          .filter((s) => s.program !== null && s.program !== undefined && s.program !== "")
          .map((s) => s.program ?? "")
      );
    }

    /*
    This code is the way when we can use react concurrent mode and useTransition,
    otherwise the table flashes each time the currentStatus changes.

    const progsInCell = await MachineBackend.getProgramsInCellController();
    */
    const progsInCell = get(programsInCellCtrl);
    if (progsInCell === null) return null;

    return calcProgramReport(
      reduxStore.getState().Events.last30.cycles.tool_usage,
      reduxStore.getState().Events.last30.cycles.estimatedCycleTimes,
      reduxStore.getState().Events.last30.cycles.part_and_operation_names,
      progsInCell,
      progsToShow
    );
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export function useRefreshProgramReport(): () => Promise<void> {
  return useRecoilCallback(({ set }) => async () => {
    set(programsInCellCtrl, await MachineBackend.getProgramsInCellController());
    set(programReportRefreshTime, new Date());
  });
}

export interface ProgramNameAndRevision {
  readonly programName: string;
  readonly revision: number | null;
  readonly partName: string | null;
  readonly process: number | null;
}

export const programToShowContent = atom<ProgramNameAndRevision | null>({
  key: "program-to-show-content",
  default: null,
});

export const programContent = selector<string>({
  key: "program-content",
  get: async ({ get }) => {
    const prog = get(programToShowContent);
    if (prog === null) {
      return "";
    } else if (prog.revision === null) {
      return await MachineBackend.getLatestProgramRevisionContent(prog.programName);
    } else {
      return await MachineBackend.getProgramRevisionContent(prog.programName, prog.revision);
    }
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export interface ProgramHistoryRequest {
  readonly programName: string;
  readonly partName: string | null;
  readonly process: number | null;
}

export const programToShowHistory = atom<ProgramHistoryRequest | null>({
  key: "program-to-show-history",
  default: null,
});
