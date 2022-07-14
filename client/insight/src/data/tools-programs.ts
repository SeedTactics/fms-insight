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

import { ActionType, ICurrentStatus, IProgramInCellController, IToolInMachine } from "../network/api";
import { LazySeq } from "../util/lazyseq";
import { durationToMinutes } from "../util/parseISODuration";
import { atom, selector, useRecoilCallback } from "recoil";
import { MachineBackend } from "../network/backend";
import { currentStatus } from "../cell-status/current-status";
import copy from "copy-to-clipboard";
import { last30ToolUse, ProgramToolUseInSingleCycle, ToolUsage } from "../cell-status/tool-usage";
import {
  EstimatedCycleTimes,
  last30EstimatedCycleTimes,
  PartAndStationOperation,
  StatisticalCycleTime,
} from "../cell-status/estimated-cycle-times";
import { stat_name_and_num } from "../cell-status/station-cycles";
import { emptyIMap, HashMap } from "../util/imap";

function averageToolUse(
  usage: ToolUsage,
  sort: boolean
): HashMap<PartAndStationOperation, ProgramToolUseInSingleCycle> {
  return usage.mapValues((cycles) => {
    const tools = LazySeq.ofIterable(cycles)
      .flatMap((c) => c.tools)
      .filter((c) => c.toolChanged !== true)
      .groupBy((t) => t.toolName)
      .map(([toolName, usageInCycles]) => ({
        toolName: toolName,
        cycleUsageMinutes: LazySeq.ofIterable(usageInCycles).sumOn((c) => c.cycleUsageMinutes) / usageInCycles.length,
        toolChanged: false,
      }))
      .toMutableArray();
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
  readonly machines: ReadonlyArray<ToolInMachine>;
  readonly minRemainingMinutes: number;
  readonly minRemainingMachine: string;
  readonly parts: ReadonlyArray<PartToolUsage>;
}

class StationOperation {
  public constructor(public readonly statGroup: string, public readonly operation: string) {}
  equals(other: PartAndStationOperation): boolean {
    return this.statGroup === other.statGroup && this.operation === other.operation;
  }
  hashPrimitives(): readonly [string, string] {
    return [this.statGroup, this.operation];
  }
  toString(): string {
    return `{statGroup: ${this.statGroup}, operation: ${this.operation}}`;
  }
}

export function calcToolReport(
  currentSt: Readonly<ICurrentStatus>,
  toolsInMach: ReadonlyArray<Readonly<IToolInMachine>>,
  usage: ToolUsage,
  machineFilter: string | null
): ReadonlyArray<ToolReport> {
  let partPlannedQtys = emptyIMap<PartAndStationOperation, number>();
  for (const [uniq, job] of LazySeq.ofObject(currentSt.jobs)) {
    const planQty = job.cycles ?? 0;
    for (let procIdx = 0; procIdx < job.procsAndPaths.length; procIdx++) {
      let completed = 0;
      let programsToAfterInProc = emptyIMap<StationOperation, number>();
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
            programsToAfterInProc = programsToAfterInProc.modify(
              new StationOperation(stop.stationGroup, stop.program),
              (old) => (old ?? 0) + inProcAfter
            );
          }
        }
      }
      for (const [op, afterInProc] of programsToAfterInProc) {
        partPlannedQtys = partPlannedQtys.modify(
          new PartAndStationOperation(job.partName, procIdx + 1, op.statGroup, op.operation),
          (old) => (old ?? 0) + planQty - completed - afterInProc
        );
      }
    }
  }

  const parts = LazySeq.ofIterable(averageToolUse(usage, false))
    .flatMap(([partAndProg, tools]) => {
      const qty = partPlannedQtys.get(partAndProg);
      if (qty !== undefined && qty > 0) {
        return tools.tools.map((tool) => ({
          toolName: tool.toolName,
          part: {
            partName: partAndProg.part,
            process: partAndProg.proc,
            program: partAndProg.operation,
            quantity: qty,
            scheduledUseMinutes: tool.cycleUsageMinutes,
          },
        }));
      } else {
        return [];
      }
    })
    .sortBy(
      (p) => p.part.partName,
      (p) => p.part.process
    )
    .toLookup(
      (t) => t.toolName,
      (t) => t.part
    );

  return LazySeq.ofIterable(toolsInMach)
    .filter((t) => machineFilter === null || machineFilter === stat_name_and_num(t.machineGroupName, t.machineNum))
    .groupBy((t) => t.toolName)
    .map(([toolName, tools]) => {
      const toolsInMachine = tools
        .toLazySeq()
        .sortBy(
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
        })
        .toRArray();

      const minMachine = LazySeq.ofIterable(toolsInMachine)
        .groupBy((m) => m.machineName)
        .map(([machineName, toolsForMachine]) => ({
          machineName,
          remaining: LazySeq.ofIterable(toolsForMachine).sumOn((m) => m.remainingMinutes),
        }))
        .minOn((m) => m.remaining);

      return {
        toolName,
        machines: toolsInMachine,
        minRemainingMachine: minMachine?.machineName ?? "",
        minRemainingMinutes: minMachine?.remaining ?? 0,
        parts: parts.get(toolName) ?? [],
      };
    })
    .toRArray();
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

export const currentToolReport = selector<L.List<ToolReport> | null>({
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

export const machinesWithTools = selector<ReadonlyArray<string>>({
  key: "machines-with-tools",
  get: ({ get }) => {
    const toolsInMach = get(toolsInMachine);
    if (toolsInMach === null) return [];
    const names = new Set<string>();
    for (const t of toolsInMach) {
      names.add(stat_name_and_num(t.machineGroupName, t.machineNum));
    }
    return Array.from(names).sort((a, b) => a.localeCompare(b));
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export const currentToolReport = selector<ReadonlyArray<ToolReport> | null>({
  key: "tool-report",
  get: ({ get }) => {
    const toolsInMach = get(toolsInMachine);
    if (toolsInMach === null) return null;
    const machineFilter = get(toolReportMachineFilter);
    const currentSt = get(currentStatus);
    const usage = get(last30ToolUse);
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

export function buildToolReportHTML(tools: Iterable<ToolReport>, singleMachine: boolean): string {
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

    const schUse = LazySeq.ofIterable(tool.parts).sumOn((p) => p.scheduledUseMinutes * p.quantity);
    table += "<td>" + schUse.toFixed(1) + "</td>";

    const totalLife = LazySeq.ofIterable(tool.machines).sumOn((m) => m.remainingMinutes);
    table += "<td>" + totalLife.toFixed(1) + "</td>";

    if (singleMachine) {
      const pockets = tool.machines.map((m) => m.pocket.toString());
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

export function copyToolReportToClipboard(tools: Iterable<ToolReport>, singleMachine: boolean): void {
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
  readonly programs: ReadonlyArray<CellControllerProgram>;
  readonly hasRevisions: boolean;
  readonly cellNameDifferentFromProgName: boolean;
}

export function calcProgramReport(
  usage: ToolUsage,
  cycleTimes: EstimatedCycleTimes,
  progsInCellCtrl: ReadonlyArray<Readonly<IProgramInCellController>>,
  progsToShow: ReadonlySet<string> | null
): ProgramReport {
  const tools = averageToolUse(usage, true);

  const progToPart = usage.keysToLazySeq().toRMap(
    (op) => [op.operation, op],
    (_, x) => x
  );

  let allPrograms = LazySeq.ofIterable(progsInCellCtrl);

  if (progsToShow != null) {
    allPrograms = allPrograms.filter((p) => progsToShow.has(p.programName));
  }

  const programs = allPrograms
    .map((prog) => {
      const part = progToPart.get(prog.programName);
      return {
        programName: prog.programName,
        cellControllerProgramName: prog.cellControllerProgramName,
        comment: prog.comment ?? null,
        revision: prog.revision ?? null,
        partName: part?.part ?? null,
        process: part?.proc ?? null,
        statisticalCycleTime: part ? cycleTimes.get(part) ?? null : null,
        toolUse: part ? tools.get(part) ?? null : null,
      };
    })
    .toRArray();

  return {
    time: new Date(),
    programs,
    hasRevisions: programs.some((p) => p.revision !== null),
    cellNameDifferentFromProgName: programs.some((p) => p.cellControllerProgramName !== p.programName),
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
    if (time === null) return null;

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

    return calcProgramReport(get(last30ToolUse), get(last30EstimatedCycleTimes), progsInCell, progsToShow);
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
