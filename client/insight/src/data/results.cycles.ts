/* Copyright (c) 2019, John Lenz

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
import { format, differenceInSeconds } from "date-fns";
import { durationToMinutes } from "../util/parseISODuration.js";
import { MaterialSummaryAndCompletedData } from "../cell-status/material-summary.js";
import copy from "copy-to-clipboard";
import {
  chunkCyclesWithSimilarEndTime,
  EstimatedCycleTimes,
  isOutlier,
  PartAndStationOperation,
  splitElapsedTimeAmongChunk,
} from "../cell-status/estimated-cycle-times.js";
import {
  PartCycleData,
  splitElapsedLoadTimeAmongCycles,
  stat_name_and_num,
} from "../cell-status/station-cycles.js";
import { PalletCyclesByPallet } from "../cell-status/pallet-cycles.js";
import { HashSet, HashMap, LazySeq } from "@seedtactics/immutable-collections";

export interface PartAndProcess {
  readonly part: string;
  readonly proc: number;
}

function cycleToPartAndOp(cycle: PartCycleData): PartAndStationOperation {
  return new PartAndStationOperation(cycle.part, cycle.process, cycle.stationGroup, cycle.operation);
}

export interface CycleFilterOptions {
  readonly allPartAndProcNames: ReadonlyArray<PartAndProcess>;
  readonly allPalletNames: ReadonlyArray<string>;
  readonly allLoadStationNames: ReadonlyArray<string>;
  readonly allMachineNames: ReadonlyArray<string>;
  readonly allMachineOperations: ReadonlyArray<PartAndStationOperation>;
}

function extractFilterOptions(
  cycles: Iterable<PartCycleData>,
  selectedPart?: PartAndProcess
): CycleFilterOptions {
  const palNames = new Set<string>();
  const lulNames = new Set<string>();
  const mcNames = new Set<string>();
  let partNames = HashMap.empty<string, HashSet<number>>();
  let oper = HashSet.empty<PartAndStationOperation>();

  for (const c of cycles) {
    palNames.add(c.pallet);

    if (c.isLabor) {
      lulNames.add(stat_name_and_num(c.stationGroup, c.stationNumber));
    } else {
      mcNames.add(stat_name_and_num(c.stationGroup, c.stationNumber));

      if (selectedPart && c.part == selectedPart.part && c.process == selectedPart.proc) {
        oper = oper.add(new PartAndStationOperation(c.part, c.process, c.stationGroup, c.operation));
      }
    }

    for (const m of c.material) {
      partNames = partNames.modify(m.part, (old) => (old ?? HashSet.empty()).add(m.proc));
    }
  }

  return {
    allPalletNames: Array.from(palNames).sort((a, b) => a.localeCompare(b)),
    allLoadStationNames: Array.from(lulNames).sort((a, b) => a.localeCompare(b)),
    allMachineNames: Array.from(mcNames).sort((a, b) => a.localeCompare(b)),
    allPartAndProcNames: LazySeq.of(partNames)
      .sortBy(([p, _]) => p)
      .flatMap(([part, procs]) => procs.toLazySeq().map((proc) => ({ part: part, proc: proc })))
      .toSortedArray(
        (n) => n.part,
        (n) => n.proc
      ),
    allMachineOperations: oper.toLazySeq().toSortedArray(
      (p) => p.statGroup,
      (p) => p.operation
    ),
  };
}

export interface FilteredStationCycles {
  readonly seriesLabel: string;
  readonly data: ReadonlyMap<string, ReadonlyArray<PartCycleData>>;
}

export const FilterAnyMachineKey = "@@@_FMSInsight_FilterAnyMachineKey_@@@";
export const FilterAnyLoadKey = "@@@_FMSInsigt_FilterAnyLoadKey_@@@";

export interface StationCycleFilter {
  readonly zoom?: { start: Date; end: Date };
  readonly partAndProc?: PartAndProcess;
  readonly pallet?: string;
  readonly station?: string;
  readonly operation?: PartAndStationOperation;
}

export function emptyStationCycles(
  allCycles: Iterable<PartCycleData>
): FilteredStationCycles & CycleFilterOptions {
  return {
    ...extractFilterOptions(allCycles),
    seriesLabel: "Station",
    data: new Map<string, ReadonlyArray<PartCycleData>>(),
  };
}

export function filterStationCycles(
  allCycles: Iterable<PartCycleData>,
  { zoom, partAndProc, pallet, station, operation }: StationCycleFilter
): FilteredStationCycles & CycleFilterOptions {
  const groupByPal =
    partAndProc && station && station !== FilterAnyMachineKey && station !== FilterAnyLoadKey;
  const groupByPart = pallet && station && station !== FilterAnyMachineKey && station !== FilterAnyLoadKey;

  return {
    ...extractFilterOptions(allCycles, partAndProc),
    seriesLabel: groupByPal ? "Pallet" : groupByPart ? "Part" : "Station",
    data: LazySeq.of(allCycles)
      .filter((e) => {
        if (zoom && (e.x < zoom.start || e.x > zoom.end)) {
          return false;
        }
        if (partAndProc && (e.part !== partAndProc.part || e.process !== partAndProc.proc)) {
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

        if (operation && operation.compare(cycleToPartAndOp(e)) !== 0) {
          return false;
        }

        return true;
      })
      .toRLookup((e) => {
        if (groupByPal) {
          return e.pallet;
        } else if (groupByPart) {
          return e.part + "-" + e.process.toString();
        } else {
          return stat_name_and_num(e.stationGroup, e.stationNumber);
        }
      }),
  };
}

export interface LoadCycleData extends PartCycleData {
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
  readonly pallet?: string;
  readonly station?: string;
}

export function loadOccupancyCycles(
  allCycles: Iterable<PartCycleData>,
  { zoom, partAndProc, pallet, station }: LoadCycleFilter
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
      (e) => stat_name_and_num(e.stationGroup, e.stationNumber),
      (c) => c.x
    )
      .map(
        ([statNameAndNum, cyclesForStat]) =>
          [
            statNameAndNum,
            LazySeq.of(cyclesForStat)
              .collect((chunk) => {
                const cycle = chunk.find((e) => {
                  if (zoom && (e.x < zoom.start || e.x > zoom.end)) {
                    return false;
                  }
                  if (partAndProc && (e.part !== partAndProc.part || e.process !== partAndProc.proc)) {
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
                    operations: LazySeq.of(chunk)
                      .flatMap((e) =>
                        e.material.map((mat) => ({
                          mat,
                          operation: e.operation,
                        }))
                      )
                      .toRArray(),
                  };
                } else {
                  return null;
                }
              })
              .toRArray(),
          ] as const
      )
      .filter(([, e]) => e.length > 0)
      .toRMap((x) => x),
  };
}

export interface LoadOpFilters {
  readonly operation: PartAndStationOperation;
  readonly pallet?: string;
  readonly station?: string;
  readonly zoom?: { readonly start: Date; readonly end: Date };
}

export function estimateLulOperations(
  allCycles: Iterable<PartCycleData>,
  { operation, pallet, zoom, station }: LoadOpFilters
): FilteredLoadCycles & CycleFilterOptions {
  const filteredCycles = LazySeq.of(allCycles).filter((e) => {
    if (!station || station === FilterAnyLoadKey) {
      return e.isLabor;
    } else {
      return stat_name_and_num(e.stationGroup, e.stationNumber) === station;
    }
  });
  return {
    ...extractFilterOptions(allCycles),
    seriesLabel: "Station",
    data: chunkCyclesWithSimilarEndTime(
      filteredCycles,
      (e) => stat_name_and_num(e.stationGroup, e.stationNumber),
      (c) => c.x
    )
      .map(
        ([statNameAndNum, cyclesForStat]) =>
          [
            statNameAndNum,
            LazySeq.of(cyclesForStat)
              .collect((chunk) => {
                const split = splitElapsedTimeAmongChunk(
                  chunk,
                  (c) => c.y,
                  (c) => c.activeMinutes
                );
                const splitCycle = split.find((e) => {
                  if (zoom && (e.cycle.x < zoom.start || e.cycle.x > zoom.end)) {
                    return false;
                  }
                  if (operation.compare(cycleToPartAndOp(e.cycle)) !== 0) {
                    return false;
                  }
                  if (pallet && e.cycle.pallet !== pallet) {
                    return false;
                  }
                  return true;
                });

                if (splitCycle) {
                  return {
                    ...splitCycle.cycle,
                    y: splitCycle.elapsedForSingleMaterialMinutes,
                    operations: LazySeq.of(chunk)
                      .flatMap((e) =>
                        e.material.map((mat) => ({
                          mat,
                          operation: e.operation + (e === splitCycle.cycle ? "*" : ""),
                        }))
                      )
                      .toRArray(),
                  };
                } else {
                  return null;
                }
              })
              .toRArray(),
          ] as const
      )
      .filter(([_, e]) => e.length > 0)
      .toRMap((x) => x),
  };
}

export function outlierMachineCycles(
  allCycles: Iterable<PartCycleData>,
  start: Date,
  end: Date,
  estimated: EstimatedCycleTimes
): FilteredStationCycles {
  return {
    seriesLabel: "Part",
    data: LazySeq.of(allCycles)
      .filter((e) => !e.isLabor && e.x >= start && e.x <= end)
      .filter((cycle) => {
        if (cycle.material.length === 0) return false;
        const stats = estimated.get(cycleToPartAndOp(cycle));
        return stats !== undefined && isOutlier(stats, cycle.y / cycle.material.length);
      })
      .toRLookup((e) => e.part + "-" + e.process.toString()),
  };
}

export function outlierLoadCycles(
  allCycles: Iterable<PartCycleData>,
  start: Date,
  end: Date,
  estimated: EstimatedCycleTimes
): FilteredStationCycles {
  const loadCycles = LazySeq.of(allCycles).filter((e) => e.isLabor && e.x >= start && e.x <= end);
  const now = new Date();
  return {
    seriesLabel: "Part",
    data: splitElapsedLoadTimeAmongCycles(loadCycles)
      .filter((e) => {
        if (e.cycle.material.length === 0) return false;
        // if it is too close to the start (or end) we might have cut off and only seen half of the events
        if (differenceInSeconds(e.cycle.x, start) < 20 || differenceInSeconds(end, e.cycle.x) < 20)
          return false;

        // if the cycle is within 15 seconds of now, don't display it yet.  We might only have received some
        // of the events for the load and the others are in-flight about to be received.
        // Technically, using now is wrong since the result of outlierLoadCycles is cached, but as soon as
        // another cycle arrives it will be recaluclated.  Thus showing stale data isn't a huge problem.
        if (Math.abs(differenceInSeconds(now, e.cycle.x)) < 15) return false;

        const stats = estimated.get(cycleToPartAndOp(e.cycle));
        return stats !== undefined && isOutlier(stats, e.elapsedForSingleMaterialMinutes);
      })
      .toRLookup(
        (e) => e.cycle.part + "-" + e.cycle.process.toString(),
        (e) => e.cycle
      ),
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

// --------------------------------------------------------------------------------
// Clipboard
// --------------------------------------------------------------------------------

export function format_cycle_inspection(
  c: PartCycleData,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>
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

export function buildCycleTable(
  cycles: FilteredStationCycles,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
  startD: Date | undefined,
  endD: Date | undefined,
  hideMedian?: boolean
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
    .filter((p) => (!startD || p.x >= startD) && (!endD || p.x < endD))
    .toSortedArray((a) => a.x.getTime());
  for (const cycle of filteredCycles) {
    table += "<tr>";
    table += "<td>" + format(cycle.x, "MMM d, yyyy, h:mm aa") + "</td>";
    table += "<td>" + cycle.part + "-" + cycle.process.toString() + "</td>";
    table += "<td>" + stat_name_and_num(cycle.stationGroup, cycle.stationNumber) + "</td>";
    table += "<td>" + cycle.pallet + "</td>";
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
    table += "<td>" + cycle.y.toFixed(1) + "</td>";
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
  cycles: FilteredStationCycles,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
  zoom: { start: Date; end: Date } | undefined,
  hideMedian?: boolean
): void {
  copy(
    buildCycleTable(cycles, matsById, zoom ? zoom.start : undefined, zoom ? zoom.end : undefined, hideMedian)
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
      table += "<td>" + pal + "</td>";
      table += "<td>" + format(cycle.x, "MMM d, yyyy, h:mm aa") + "</td>";
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
    case api.LogType.Wash:
      return "Wash";
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
      table += "<td>" + format(cycle.endUTC, "MMM d, yyyy, h:mm aa") + "</td>";
      table += "<td>" + mat.part + "-" + mat.proc.toString() + "</td>";
      table += "<td>" + stat_name(cycle) + "</td>";
      table += "<td>" + cycle.pal + "</td>";
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
