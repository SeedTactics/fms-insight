/* Copyright (c) 2024, John Lenz

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

import * as api from "../network/api.js";
import { addMinutes, addSeconds } from "date-fns";
import { durationToMinutes } from "../util/parseISODuration.js";
import { MaterialSummaryAndCompletedData } from "../cell-status/material-summary.js";
import copy from "copy-to-clipboard";
import {
  chunkCyclesWithSimilarEndTime,
  PartAndStationOperation,
} from "../cell-status/estimated-cycle-times.js";
import { PartCycleData, stat_name_and_num } from "../cell-status/station-cycles.js";
import { PalletCyclesByPallet } from "../cell-status/pallet-cycles.js";
import { HashMap, LazySeq, OrderedSet, OrderedMap } from "@seedtactics/immutable-collections";

export interface PartAndProcess {
  readonly part: string;
  readonly proc: number;
}

export interface CycleFilterOptions {
  readonly allPartAndProcNames: ReadonlyArray<PartAndProcess>;
  readonly allPalletNames: ReadonlyArray<number>;
  readonly allLoadStationNames: ReadonlyArray<string>;
  readonly allMachineNames: ReadonlyArray<string>;
  readonly allMachineOperations: ReadonlyArray<PartAndStationOperation>;
}

function extractFilterOptions(
  cycles: Iterable<PartCycleData>,
  selectedPart?: PartAndProcess,
): CycleFilterOptions {
  let palNames = OrderedSet.empty<number>();
  let lulNames = OrderedSet.empty<string>();
  let mcNames = OrderedSet.empty<string>();
  let partNames = OrderedMap.empty<string, OrderedSet<number>>();
  let oper = OrderedSet.empty<PartAndStationOperation>();

  for (const c of cycles) {
    palNames = palNames.add(c.pallet);

    if (c.isLabor) {
      lulNames = lulNames.add(stat_name_and_num(c.stationGroup, c.stationNumber));
    } else {
      mcNames = mcNames.add(stat_name_and_num(c.stationGroup, c.stationNumber));

      if (
        selectedPart &&
        c.part == selectedPart.part &&
        c.material.some((m) => m.proc === selectedPart.proc)
      ) {
        oper = oper.add(PartAndStationOperation.ofPartCycle(c));
      }
    }

    for (const m of c.material) {
      partNames = partNames.alter(m.part, (old) => (old ?? OrderedSet.empty()).add(m.proc));
    }
  }

  return {
    allPalletNames: palNames.toAscLazySeq().toRArray(),
    allLoadStationNames: lulNames.toAscLazySeq().toRArray(),
    allMachineNames: mcNames.toAscLazySeq().toRArray(),
    allPartAndProcNames: partNames
      .toAscLazySeq()
      .flatMap(([part, procs]) => procs.toAscLazySeq().map((proc) => ({ part: part, proc: proc })))
      .toRArray(),
    allMachineOperations: oper.toAscLazySeq().toRArray(),
  };
}

export type PartCycleChartData = PartCycleData & { readonly x: Date; readonly y: number };

export interface FilteredStationCycles {
  readonly seriesLabel: string;
  readonly data: ReadonlyMap<string, ReadonlyArray<PartCycleChartData>>;
}

export const FilterAnyMachineKey = "@@@_FMSInsight_FilterAnyMachineKey_@@@";
export const FilterAnyLoadKey = "@@@_FMSInsigt_FilterAnyLoadKey_@@@";

export interface StationCycleFilter {
  readonly zoom?: { start: Date; end: Date };
  readonly partAndProc?: PartAndProcess;
  readonly pallet?: number;
  readonly station?: string;
  readonly operation?: PartAndStationOperation;
}

export function emptyStationCycles(
  allCycles: Iterable<PartCycleData>,
): FilteredStationCycles & CycleFilterOptions {
  return {
    ...extractFilterOptions(allCycles),
    seriesLabel: "Station",
    data: new Map<string, ReadonlyArray<PartCycleChartData>>(),
  };
}

export function filterStationCycles(
  allCycles: Iterable<PartCycleData>,
  { zoom, partAndProc, pallet, station, operation }: StationCycleFilter,
): FilteredStationCycles & CycleFilterOptions {
  const groupByPal =
    partAndProc && station && station !== FilterAnyMachineKey && station !== FilterAnyLoadKey;
  const groupByPart =
    pallet && !operation && station && station !== FilterAnyMachineKey && station !== FilterAnyLoadKey;

  return {
    ...extractFilterOptions(allCycles, partAndProc),
    seriesLabel: groupByPal ? "Pallet" : groupByPart ? "Part" : "Station",
    data: LazySeq.of(allCycles)
      .filter((e) => {
        if (zoom && (e.endTime < zoom.start || e.endTime > zoom.end)) {
          return false;
        }
        if (
          partAndProc &&
          (e.part !== partAndProc.part || !e.material.some((m) => m.proc === partAndProc.proc))
        ) {
          return false;
        }
        if (pallet && e.pallet !== pallet) {
          return false;
        }

        if (station === FilterAnyMachineKey) {
          if (e.isLabor) {
            return false;
          }
        } else if (station === FilterAnyLoadKey) {
          if (!e.isLabor) {
            return false;
          }
        } else if (station && stat_name_and_num(e.stationGroup, e.stationNumber) !== station) {
          return false;
        }

        if (operation && operation.compare(PartAndStationOperation.ofPartCycle(e)) !== 0) {
          return false;
        }

        return true;
      })
      .toRLookup(
        (e) => {
          if (groupByPal) {
            return e.pallet.toString();
          } else if (groupByPart) {
            return (
              e.part +
              "-" +
              LazySeq.of(e.material)
                .map((m) => m.proc)
                .distinctAndSortBy((p) => p)
                .toRArray()
                .join(":")
            );
          } else {
            return stat_name_and_num(e.stationGroup, e.stationNumber);
          }
        },
        (e) => ({
          ...e,
          x: e.endTime,
          y: e.elapsedMinsPerMaterial * e.material.length,
        }),
      ),
  };
}

export interface LoadCycleData extends PartCycleChartData {
  readonly operations?: ReadonlyArray<{
    readonly mat: Readonly<api.ILogMaterial>;
    readonly operation: string;
  }>;
}

export interface FilteredLoadCycles {
  readonly seriesLabel: string;
  readonly data: ReadonlyMap<string, ReadonlyArray<LoadCycleData>>;
}

export interface LoadCycleFilter {
  readonly zoom?: { start: Date; end: Date };
  readonly partAndProc?: PartAndProcess;
  readonly pallet?: number;
  readonly station?: string;
}

export function loadOccupancyCycles(
  allCycles: Iterable<PartCycleData>,
  { zoom, partAndProc, pallet, station }: LoadCycleFilter,
): FilteredLoadCycles & CycleFilterOptions {
  const filteredCycles = LazySeq.of(allCycles).filter((e) => {
    if (!station || station === FilterAnyLoadKey) {
      return e.isLabor;
    } else {
      return stat_name_and_num(e.stationGroup, e.stationNumber) === station;
    }
  });

  return {
    ...extractFilterOptions(allCycles, partAndProc),
    seriesLabel: "Station",
    data: chunkCyclesWithSimilarEndTime(
      filteredCycles,
      (c) => stat_name_and_num(c.stationGroup, c.stationNumber),
      (c) => c.endTime,
    )
      .map(
        ([statNameAndNum, cyclesForStat]) =>
          [
            statNameAndNum,
            LazySeq.of(cyclesForStat)
              .collect((chunk) => {
                const cycle = chunk.find((e) => {
                  if (zoom && (e.endTime < zoom.start || e.endTime > zoom.end)) {
                    return false;
                  }
                  if (
                    partAndProc &&
                    (e.part !== partAndProc.part || !e.material.some((m) => m.proc === partAndProc.proc))
                  ) {
                    return false;
                  }
                  if (pallet && e.pallet !== pallet) {
                    return false;
                  }
                  return true;
                });

                if (cycle) {
                  return {
                    ...cycle,
                    x: cycle.endTime,
                    y: chunk.reduce((acc, e) => acc + e.elapsedMinsPerMaterial * e.material.length, 0),
                    operations: LazySeq.of(chunk)
                      .flatMap((e) =>
                        e.material.map((mat) => ({
                          mat,
                          operation: e.operation,
                        })),
                      )
                      .toRArray(),
                  };
                } else {
                  return null;
                }
              })
              .toRArray(),
          ] as const,
      )
      .filter(([, e]) => e.length > 0)
      .toRMap((x) => x),
  };
}

export function plannedOperationMinutes(s: FilteredStationCycles, forSingleMat: boolean): number | undefined {
  let planned: { time: Date; mins: number } | null = null;

  for (const [, cycles] of s.data) {
    for (const pt of cycles) {
      if (pt.material.length > 0) {
        if (planned === null || planned.time < pt.x) {
          const mins = forSingleMat ? pt.activeMinutes / pt.material.length : pt.activeMinutes;
          planned = { time: pt.x, mins };
        }
      }
    }
  }
  return planned?.mins;
}

export interface RecentCycle {
  readonly station: string;
  readonly startTime: Date;
  readonly endOccupied: Date;
  readonly endActive?: Date;
  readonly outlier: boolean;
  readonly parts: ReadonlyArray<{ part: string; oper: string }>;
}

export function recentCycles(allCycles: LazySeq<PartCycleData>): ReadonlyArray<RecentCycle> {
  return chunkCyclesWithSimilarEndTime(
    allCycles,
    (c) => stat_name_and_num(c.stationGroup, c.stationNumber),
    (c) => c.endTime,
  )
    .flatMap(function* procChunk([station, chunks]) {
      for (const chunk of chunks) {
        // sum elapsed time for chunk and subtract from end time to get start time
        const elapForChunkMins = LazySeq.of(chunk).sumBy((c) => c.elapsedMinsPerMaterial * c.material.length);
        const startTime = addSeconds(chunk[0].endTime, -elapForChunkMins * 60);

        const activeMins = LazySeq.of(chunk).sumBy((c) => c.activeMinutes);

        yield {
          station,
          startTime,
          endActive: activeMins > 0 ? addMinutes(startTime, activeMins) : undefined,
          endOccupied: chunk[0].endTime,
          outlier: chunk.some((c) => c.isOutlier),
          parts: LazySeq.of(chunk)
            .flatMap((c) =>
              LazySeq.of(c.material)
                .distinctBy((m) => m.proc)
                .map((m) => ({ part: c.part, proc: m.proc, oper: c.operation })),
            )
            .distinctAndSortBy(
              (c) => c.part,
              (c) => c.proc,
              (c) => c.oper,
            )
            .map((c) => ({
              part: c.part + "-" + c.proc.toString(),
              oper: c.oper,
            }))
            .toRArray(),
        };
      }
    })
    .toRArray();
}

// --------------------------------------------------------------------------------
// Clipboard
// --------------------------------------------------------------------------------

export function format_cycle_inspection(
  c: PartCycleData,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
): string {
  const ret = [];
  const signaled = new Set<string>();
  const completed = new Set<string>();
  const success = new Set<string>();

  for (const mat of c.material) {
    const summary = matsById.get(mat.id);
    if (summary !== undefined) {
      for (const s of summary.signaledInspections) {
        signaled.add(s);
      }
      for (const [compInsp, details] of LazySeq.ofObject(summary.completedInspections ?? {})) {
        signaled.add(compInsp);
        completed.add(compInsp);
        if (details.success) {
          success.add(compInsp);
        }
      }
    }
  }

  for (const name of LazySeq.of(signaled).toSortedArray((x) => x)) {
    if (completed.has(name)) {
      ret.push(name + "[" + (success.has(name) ? "success" : "failed") + "]");
    } else {
      ret.push(name);
    }
  }
  return ret.join(", ");
}

export type ClipboardStationCycles = {
  readonly seriesLabel: string;
  readonly data: ReadonlyMap<string, ReadonlyArray<PartCycleData>>;
};

export function buildCycleTable(
  cycles: ClipboardStationCycles,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
  startD: Date | undefined,
  endD: Date | undefined,
  hideMedian?: boolean,
): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Date</th><th>Part</th><th>Station</th><th>Pallet</th>";
  table += "<th>Serial</th><th>Workorder</th><th>Inspection</th>";
  table += "<th>Elapsed Min</th><th>Target Min</th>";
  if (!hideMedian) {
    table += "<th>Median Elapsed Min</th><th>Median Deviation</th>";
  }
  table += "</tr></thead>\n<tbody>\n";

  const filteredCycles = LazySeq.of(cycles.data)
    .flatMap(([_, c]) => c)
    .filter((p) => (!startD || p.endTime >= startD) && (!endD || p.endTime < endD))
    .toSortedArray((a) => a.endTime.getTime());
  for (const cycle of filteredCycles) {
    table += "<tr>";
    table +=
      "<td>" +
      cycle.endTime.toLocaleString(undefined, {
        month: "short",
        day: "numeric",
        year: "numeric",
        hour: "numeric",
        minute: "2-digit",
      }) +
      "</td>";
    table +=
      "<td>" +
      cycle.part +
      "-" +
      LazySeq.of(cycle.material)
        .map((m) => m.proc)
        .distinctAndSortBy((p) => p)
        .toRArray()
        .join(":") +
      "</td>";
    table += "<td>" + stat_name_and_num(cycle.stationGroup, cycle.stationNumber) + "</td>";
    table += "<td>" + cycle.pallet.toString() + "</td>";
    table +=
      "<td>" +
      cycle.material
        .filter((m) => m.serial)
        .map((m) => m.serial)
        .join(",") +
      "</td>";
    table +=
      "<td>" +
      cycle.material
        .filter((m) => m.workorder)
        .map((m) => m.workorder)
        .join(",") +
      "</td>";
    table += "<td>" + format_cycle_inspection(cycle, matsById) + "</td>";
    table += "<td>" + (cycle.elapsedMinsPerMaterial * cycle.material.length).toFixed(1) + "</td>";
    table += "<td>" + cycle.activeMinutes.toFixed(1) + "</td>";
    if (!hideMedian) {
      table += "<td>" + cycle.medianCycleMinutes.toFixed(1) + "</td>";
      table += "<td>" + cycle.MAD_aboveMinutes.toFixed(1) + "</td>";
    }
    table += "</tr>\n";
  }
  table += "</tbody>\n</table>";
  return table;
}

export function copyCyclesToClipboard(
  cycles: ClipboardStationCycles,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
  zoom: { start: Date; end: Date } | undefined,
  hideMedian?: boolean,
): void {
  copy(
    buildCycleTable(cycles, matsById, zoom ? zoom.start : undefined, zoom ? zoom.end : undefined, hideMedian),
  );
}

export function buildPalletCycleTable(points: PalletCyclesByPallet): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Pallet</th><th>Date</th><th>Elapsed (min)</th>";
  table += "</tr></thead>\n<tbody>\n";

  const pals = points.keysToLazySeq().toSortedArray((x) => x);

  for (const pal of pals) {
    for (const cycle of points.get(pal)?.valuesToLazySeq() ?? []) {
      table += "<tr>";
      table += "<td>" + pal.toString() + "</td>";
      table +=
        "<td>" +
        cycle.x.toLocaleString(undefined, {
          month: "short",
          day: "numeric",
          year: "numeric",
          hour: "numeric",
          minute: "2-digit",
        }) +
        "</td>";
      table += "<td>" + cycle.y.toFixed(1) + "</td>";
      table += "</tr>\n";
    }
  }
  table += "</tbody>\n</table>";
  return table;
}

export function copyPalletCyclesToClipboard(points: PalletCyclesByPallet): void {
  copy(buildPalletCycleTable(points));
}

function stat_name(e: Readonly<api.ILogEntry>): string {
  switch (e.type) {
    case api.LogType.LoadUnloadCycle:
    case api.LogType.MachineCycle:
      return e.loc + " #" + e.locnum.toString();
    case api.LogType.AddToQueue:
    case api.LogType.RemoveFromQueue:
      return e.loc;
    case api.LogType.PartMark:
      return "Mark";
    case api.LogType.OrderAssignment:
      return "Workorder";
    case api.LogType.CloseOut:
      return "CloseOut";
    case api.LogType.Inspection: {
      const inspName = (e.details || {}).InspectionType || "";
      return "Signal " + inspName;
    }
    case api.LogType.InspectionForce:
      return "Signal " + e.program;
    case api.LogType.InspectionResult:
      return "Inspect " + e.program;
    default:
      return e.loc;
  }
}

function result(e: Readonly<api.ILogEntry>): string {
  switch (e.type) {
    case api.LogType.Inspection:
    case api.LogType.InspectionForce:
    case api.LogType.LoadUnloadCycle:
    case api.LogType.PartMark:
    case api.LogType.OrderAssignment:
      return e.result;
    case api.LogType.AddToQueue:
      return "Add";
    case api.LogType.RemoveFromQueue:
      return "Remove";
    case api.LogType.MachineCycle:
      return e.program;
    case api.LogType.InspectionResult:
      if (e.result.toLowerCase() === "false") {
        return "Failed";
      } else {
        return "Succeeded";
      }
    default:
      return "";
  }
}

export function buildLogEntriesTable(cycles: Iterable<Readonly<api.ILogEntry>>): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Date</th><th>Part</th><th>Station</th><th>Pallet</th>";
  table += "<th>Serial</th><th>Workorder</th><th>Result</th><th>Elapsed Min</th><th>Active Min</th>";
  table += "</tr></thead>\n<tbody>\n";
  for (const cycle of cycles) {
    if (cycle.startofcycle) {
      continue;
    }
    for (const mat of cycle.material) {
      table += "<tr>";
      table +=
        "<td>" +
        cycle.endUTC.toLocaleString(undefined, {
          month: "short",
          day: "numeric",
          year: "numeric",
          hour: "numeric",
          minute: "2-digit",
        }) +
        "</td>";
      table += "<td>" + mat.part + "-" + mat.proc.toString() + "</td>";
      table += "<td>" + stat_name(cycle) + "</td>";
      table += "<td>" + cycle.pal.toString() + "</td>";
      table += "<td>" + (mat.serial || "") + "</td>";
      table += "<td>" + (mat.workorder || "") + "</td>";
      table += "<td>" + result(cycle) + "</td>";
      table += "<td>" + durationToMinutes(cycle.elapsed).toFixed(1) + "</td>";
      table += "<td>" + durationToMinutes(cycle.active).toFixed(1) + "</td>";
      table += "</tr>\n";
    }
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyLogEntriesToClipboard(cycles: Iterable<Readonly<api.ILogEntry>>): void {
  copy(buildLogEntriesTable(cycles));
}
