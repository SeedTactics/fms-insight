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

import {
  ActionType,
  ICurrentStatus,
  IHistoricJob,
  IProgramInCellController,
  IToolInMachine,
} from "../network/api.js";
import { durationToMinutes } from "../util/parseISODuration.js";
import { MachineBackend } from "../network/backend.js";
import { currentStatus } from "../cell-status/current-status.js";
import copy from "copy-to-clipboard";
import { last30ToolUse, ProgramToolUseInSingleCycle, ToolUsage } from "../cell-status/tool-usage.js";
import {
  EstimatedCycleTimes,
  last30EstimatedCycleTimes,
  PartAndStationOperation,
  StatisticalCycleTime,
} from "../cell-status/estimated-cycle-times.js";
import { stat_name_and_num } from "../cell-status/station-cycles.js";
import { LazySeq, HashMap, hashValues } from "@seedtactics/immutable-collections";
import { atom, useStore } from "jotai";
import { useCallback } from "react";
import { last30Jobs } from "../cell-status/scheduled-jobs.js";

function averageToolUse(
  usage: ToolUsage,
  sort: boolean,
): HashMap<PartAndStationOperation, ProgramToolUseInSingleCycle> {
  return usage.mapValues((cycles) => {
    const tools = LazySeq.of(cycles)
      .flatMap((c) => c.tools)
      .filter((c) => !c.toolChangedDuringMiddleOfCycle)
      .groupBy((t) => t.toolName)
      .map(([toolName, usageInCycles]) => ({
        toolName: toolName,
        cycleUsageMinutes: LazySeq.of(usageInCycles).sumBy((c) => c.cycleUsageMinutes) / usageInCycles.length,
        cycleUsageCnt: LazySeq.of(usageInCycles).sumBy((c) => c.cycleUsageCnt) / usageInCycles.length,
        toolChangedDuringMiddleOfCycle: false,
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
  readonly serial?: string;
  readonly currentUseMinutes: number | null;
  readonly lifetimeMinutes: number | null;
  readonly remainingMinutes: number | null;
  readonly currentUseCnt: number | null;
  readonly lifetimeCnt: number | null;
  readonly remainingCnt: number | null;
}

export interface PartToolUsage {
  readonly partName: string;
  readonly program: string;
  readonly quantity: number;
  readonly scheduledUseMinutes: number;
  readonly scheduledUseCnt: number;
}

export interface ToolReport {
  readonly toolName: string;
  readonly machines: ReadonlyArray<ToolInMachine>;
  readonly minRemainingMinutes: number | null;
  readonly minRemainingCnt: number | null;
  readonly parts: ReadonlyArray<PartToolUsage>;
}

class StationOperation {
  public constructor(
    public readonly statGroup: string,
    public readonly operation: string,
  ) {}
  compare(other: PartAndStationOperation): number {
    const c = this.statGroup.localeCompare(other.statGroup);
    if (c !== 0) return c;
    return this.operation.localeCompare(other.operation);
  }
  hash(): number {
    return hashValues(this.statGroup, this.operation);
  }
  toString(): string {
    return `{statGroup: ${this.statGroup}, operation: ${this.operation}}`;
  }
}

export function calcToolReport(
  currentSt: Readonly<ICurrentStatus>,
  toolsInMach: ReadonlyArray<Readonly<IToolInMachine>>,
  usage: ToolUsage,
  machineFilter: string | null,
): ReadonlyArray<ToolReport> {
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
          const inProcAfter = LazySeq.of(currentSt.material)
            .filter(
              (m) =>
                m.jobUnique === uniq &&
                m.process == procIdx + 1 &&
                m.path === pathIdx + 1 &&
                (m.action.type === ActionType.UnloadToCompletedMaterial ||
                  m.action.type === ActionType.UnloadToInProcess ||
                  (m.lastCompletedMachiningRouteStopIndex !== undefined &&
                    m.lastCompletedMachiningRouteStopIndex > stopIdx)),
            )
            .length();
          if (stop.program !== undefined && stop.program !== "") {
            programsToAfterInProc = programsToAfterInProc.modify(
              new StationOperation(stop.stationGroup, stop.program),
              (old) => (old ?? 0) + inProcAfter,
            );
          }
        }
      }
      for (const [op, afterInProc] of programsToAfterInProc) {
        partPlannedQtys = partPlannedQtys.modify(
          new PartAndStationOperation(job.partName, op.statGroup, op.operation),
          (old) => (old ?? 0) + planQty - completed - afterInProc,
        );
      }
    }
  }

  const parts = LazySeq.of(averageToolUse(usage, false))
    .flatMap(([partAndProg, tools]) => {
      const qty = partPlannedQtys.get(partAndProg);
      if (qty !== undefined && qty > 0) {
        return tools.tools.map((tool) => ({
          toolName: tool.toolName,
          part: {
            partName: partAndProg.part,
            program: partAndProg.operation,
            quantity: qty,
            scheduledUseMinutes: tool.cycleUsageMinutes,
            scheduledUseCnt: tool.cycleUsageCnt,
          },
        }));
      } else {
        return [];
      }
    })
    .sortBy(
      (p) => p.part.partName,
      (p) => p.part.program,
    )
    .toLookup(
      (t) => t.toolName,
      (t) => t.part,
    );

  return LazySeq.of(toolsInMach)
    .filter(
      (t) => machineFilter === null || machineFilter === stat_name_and_num(t.machineGroupName, t.machineNum),
    )
    .groupBy((t) => t.toolName)
    .map(([toolName, tools]) => {
      const toolsInMachine: ReadonlyArray<ToolInMachine> = LazySeq.of(tools)
        .sortBy(
          (t) => t.machineGroupName,
          (t) => t.machineNum,
          (t) => t.pocket,
        )
        .map((m) => {
          const currentUseMinutes =
            m.currentUse !== null && m.currentUse !== undefined && m.currentUse !== ""
              ? durationToMinutes(m.currentUse)
              : null;
          const lifetimeMinutes =
            m.totalLifeTime !== null && m.totalLifeTime !== undefined && m.totalLifeTime !== ""
              ? durationToMinutes(m.totalLifeTime)
              : null;
          return {
            machineName: m.machineGroupName + " #" + m.machineNum.toString(),
            pocket: m.pocket,
            serial: m.serial,
            currentUseMinutes,
            lifetimeMinutes,
            remainingMinutes:
              currentUseMinutes !== null && lifetimeMinutes !== null
                ? Math.max(0, lifetimeMinutes - currentUseMinutes)
                : null,
            currentUseCnt: m.currentUseCount ?? null,
            lifetimeCnt: m.totalLifeCount ?? null,
            remainingCnt:
              m.currentUseCount !== null &&
              m.currentUseCount !== undefined &&
              m.totalLifeCount !== null &&
              m.totalLifeCount !== undefined
                ? Math.max(0, m.totalLifeCount - m.currentUseCount)
                : null,
          };
        })
        .toRArray();

      const machGroups = LazySeq.of(toolsInMachine).groupBy((m) => m.machineName);

      const minMachMins = machGroups
        .map(([_, toolsForMachine]) =>
          LazySeq.of(toolsForMachine)
            .collect((m) => m.remainingMinutes)
            .sumBy((m) => m),
        )
        .minBy((m) => m);

      const minMachCnt = machGroups
        .map(([_, toolsForMachine]) =>
          LazySeq.of(toolsForMachine)
            .collect((m) => m.remainingCnt)
            .sumBy((m) => m),
        )
        .minBy((m) => m);

      return {
        toolName,
        machines: toolsInMachine,
        minRemainingMinutes: minMachMins ?? null,
        minRemainingCnt: minMachCnt ?? null,
        parts: parts.get(toolName) ?? [],
      };
    })
    .toRArray();
}

export const toolReportMachineFilter = atom<string | null>(null);

type ToolsInMachine = {
  readonly tools: ReadonlyArray<IToolInMachine>;
  readonly time: Date;
};

const toolsInMachine = atom<ToolsInMachine | null>(null);

export const toolReportRefreshTime = atom<Date | null, [Date], Promise<void>>(
  (get) => get(toolsInMachine)?.time ?? null,
  async (_, set, time) => {
    set(toolsInMachine, {
      tools: await MachineBackend.getToolsInMachines(),
      time,
    });
  },
);

export const machinesWithTools = atom<ReadonlyArray<string>>((get) => {
  const toolsInMach = get(toolsInMachine);
  if (toolsInMach === null) return [];
  const names = new Set<string>();
  for (const t of toolsInMach.tools) {
    names.add(stat_name_and_num(t.machineGroupName, t.machineNum));
  }
  return Array.from(names).sort((a, b) => a.localeCompare(b));
});

export const currentToolReport = atom<ReadonlyArray<ToolReport> | null>((get) => {
  const toolsInMach = get(toolsInMachine);
  if (toolsInMach === null) return null;
  const machineFilter = get(toolReportMachineFilter);
  const currentSt = get(currentStatus);
  const usage = get(last30ToolUse);
  return calcToolReport(currentSt, toolsInMach.tools, usage, machineFilter);
});

export const toolReportHasSerial = atom<boolean>((get) => {
  const report = get(currentToolReport);
  if (!report) return false;
  return LazySeq.of(report)
    .flatMap((r) => r.machines)
    .some((t) => t.serial != null && t.serial !== "");
});

export const toolReportHasTimeUsage = atom<boolean>((get) => {
  const report = get(currentToolReport);
  if (!report) return false;
  return LazySeq.of(report)
    .flatMap((r) => r.machines)
    .some((t) => t.currentUseMinutes != null);
});

export const toolReportHasCntUsage = atom<boolean>((get) => {
  const report = get(currentToolReport);
  if (!report) return false;
  return LazySeq.of(report)
    .flatMap((r) => r.machines)
    .some((t) => t.currentUseCnt != null);
});

function buildToolReportHTML(
  tools: Iterable<ToolReport>,
  { showTime, showCnts }: { showTime: boolean; showCnts: boolean },
): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Tool</th><th>Machine</th><th>Pocket</th>";
  if (showTime) {
    table +=
      "<th>Scheduled Use (min)</th><th>Current Use (min)</th><th>Lifetime (min)</th><th>Remaining Life (min)</th>";
  }
  if (showCnts) {
    table +=
      "<th>Scheduled Use (cnt)</th><th>Current Use (cnt)</th><th>Lifetime (cnt)</th><th>Remaining Life (cnt)</th>";
  }
  table += "</tr></thead>\n<tbody>\n";

  for (const tool of tools) {
    for (const mach of tool.machines) {
      table += `<tr><td>${tool.toolName}</td><td>${mach.machineName}</td><td>${mach.pocket}</td>`;

      if (showTime) {
        const schUse = LazySeq.of(tool.parts).sumBy((p) => p.scheduledUseMinutes * p.quantity);
        table += "<td>" + schUse.toFixed(1) + "</td>";

        table += "<td>" + (mach.currentUseMinutes ? mach.currentUseMinutes.toFixed(1) : "") + "</td>";
        table += "<td>" + (mach.lifetimeMinutes ? mach.lifetimeMinutes.toFixed(1) : "") + "</td>";
        table += "<td>" + (mach.remainingMinutes ? mach.remainingMinutes.toFixed(1) : "") + "</td>";
      }

      if (showCnts) {
        const schUse = LazySeq.of(tool.parts).sumBy((p) => p.scheduledUseCnt * p.quantity);
        table += "<td>" + schUse.toFixed(1) + "</td>";

        table += "<td>" + (mach.currentUseCnt ? mach.currentUseCnt.toFixed(1) : "") + "</td>";
        table += "<td>" + (mach.lifetimeCnt ? mach.lifetimeCnt.toFixed(1) : "") + "</td>";
        table += "<td>" + (mach.remainingCnt ? mach.remainingCnt.toFixed(1) : "") + "</td>";
      }

      table += "</tr>\n";
    }
  }
  table += "</tbody></table>";

  return table;
}

export function useCopyToolReportToClipboard(): () => void {
  const store = useStore();
  return useCallback(() => {
    const tools = store.get(currentToolReport);
    if (!tools) return;
    copy(
      buildToolReportHTML(tools, {
        showTime: store.get(toolReportHasTimeUsage),
        showCnts: store.get(toolReportHasCntUsage),
      }),
    );
  }, []);
}

export interface CellControllerProgram {
  readonly programName: string;
  readonly cellControllerProgramName: string;
  readonly comment: string | null;
  readonly revision: number | null;
  readonly partName: string | null;
  readonly statisticalCycleTime: StatisticalCycleTime | null;
  readonly toolUse: ProgramToolUseInSingleCycle | null;
  readonly plannedMins: number | null;
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
  jobs: HashMap<string, Readonly<IHistoricJob>> | null,
  progsInCellCtrl: ReadonlyArray<Readonly<IProgramInCellController>>,
  progsToShow: ReadonlySet<string> | null,
): ProgramReport {
  const tools = averageToolUse(usage, true);

  const progToPart = usage.keysToLazySeq().toRMap(
    (op) => [op.operation, op],
    (_, x) => x,
  );

  let allPrograms = LazySeq.of(progsInCellCtrl);

  if (progsToShow != null) {
    allPrograms = allPrograms.filter((p) => progsToShow.has(p.programName));
  }

  const plannedTimes = jobs
    ?.valuesToLazySeq()
    .flatMap((j) =>
      LazySeq.of(j.procsAndPaths)
        .flatMap((p) => p.paths)
        .flatMap((path) =>
          LazySeq.of(path.stops).collect((stop) => {
            const mins = durationToMinutes(stop.expectedCycleTime);
            if (stop.program && mins > 0) {
              return {
                program: stop.program,
                plannedMins: mins / path.partsPerPallet,
                start: j.routeStartUTC,
              };
            } else {
              return null;
            }
          }),
        ),
    )
    .toOrderedMap(
      (p) => [p.program, p],
      (v1, v2) => (v1.start > v2.start ? v1 : v2),
    );

  const programs = allPrograms
    .map((prog) => {
      const part = progToPart.get(prog.programName);
      return {
        programName: prog.programName,
        cellControllerProgramName: prog.cellControllerProgramName,
        comment: prog.comment ?? null,
        revision: prog.revision ?? null,
        partName: part?.part ?? null,
        statisticalCycleTime: part ? cycleTimes.get(part) ?? null : null,
        toolUse: part ? tools.get(part) ?? null : null,
        plannedMins: plannedTimes?.get(prog.programName)?.plannedMins ?? null,
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

export const programFilter = atom<"AllPrograms" | "ActivePrograms">("AllPrograms");

type ProgsInCellCtrl = {
  readonly progs: ReadonlyArray<Readonly<IProgramInCellController>>;
  readonly time: Date;
};

const programsInCellCtrl = atom<ProgsInCellCtrl | null>(null);

export const programReportRefreshTime = atom<Date | null, [Date], Promise<void>>(
  (get) => get(programsInCellCtrl)?.time ?? null,
  async (_, set, time) => {
    set(programsInCellCtrl, {
      progs: await MachineBackend.getProgramsInCellController(),
      time,
    });
  },
);

export const currentProgramReport = atom<ProgramReport | null>((get) => {
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
        .map((s) => s.program ?? ""),
    );
  }

  const progsInCell = get(programsInCellCtrl);
  if (progsInCell === null) return null;

  return calcProgramReport(
    get(last30ToolUse),
    get(last30EstimatedCycleTimes),
    get(last30Jobs),
    progsInCell.progs,
    progsToShow,
  );
});

export type ProgramNameAndRevision = {
  readonly programName: string;
  readonly revision: number | null;
  readonly partName: string | null;
};

export const programToShowContent = atom<ProgramNameAndRevision | null>(null);

export const programContent = atom<Promise<string>>(async (get) => {
  const prog = get(programToShowContent);
  if (prog === null) {
    return "";
  } else if (prog.revision === null) {
    return await MachineBackend.getLatestProgramRevisionContent(prog.programName);
  } else {
    return await MachineBackend.getProgramRevisionContent(prog.programName, prog.revision);
  }
});

export type ProgramHistoryRequest = {
  readonly programName: string;
  readonly partName: string | null;
};

export const programToShowHistory = atom<ProgramHistoryRequest | null>(null);
