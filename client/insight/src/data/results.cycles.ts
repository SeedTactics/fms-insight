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

import { HashMap, Vector, Option, HashSet } from "prelude-ts";
import {
  PartCycleData,
  stat_name_and_num,
  PartAndProcess,
  isOutlier,
  EstimatedCycleTimes,
  splitElapsedLoadTimeAmongCycles,
  PartAndStationOperation,
  chunkCyclesWithSimilarEndTime,
  splitElapsedTimeAmongChunk,
} from "./events.cycles";
import { LazySeq } from "./lazyseq";
import * as api from "./api";
import { format, differenceInSeconds } from "date-fns";
import { duration } from "moment";
import { MaterialSummaryAndCompletedData } from "./events.matsummary";
import copy from "copy-to-clipboard";

export interface FilteredStationCycles {
  readonly seriesLabel: string;
  readonly data: HashMap<string, ReadonlyArray<PartCycleData>>;
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

export function filterStationCycles(
  allCycles: Vector<PartCycleData>,
  { zoom, partAndProc, pallet, station, operation }: StationCycleFilter
): FilteredStationCycles {
  const groupByPal = partAndProc && station && station !== FilterAnyMachineKey && station !== FilterAnyLoadKey;
  const groupByPart = pallet && station && station !== FilterAnyMachineKey && station !== FilterAnyLoadKey;

  return {
    seriesLabel: groupByPal ? "Pallet" : groupByPart ? "Part" : "Station",
    data: LazySeq.ofIterable(allCycles)
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

        if (operation && !operation.equals(PartAndStationOperation.ofPartCycle(e))) {
          return false;
        }

        return true;
      })
      .groupBy((e) => {
        if (groupByPal) {
          return e.pallet;
        } else if (groupByPart) {
          return e.part + "-" + e.process.toString();
        } else {
          return stat_name_and_num(e.stationGroup, e.stationNumber);
        }
      })
      .mapValues((e) => e.toArray()),
  };
}

export interface LoadCycleData extends PartCycleData {
  readonly operations?: ReadonlyArray<{ readonly mat: Readonly<api.ILogMaterial>; readonly operation: string }>;
}

export interface FilteredLoadCycles {
  readonly seriesLabel: string;
  readonly data: HashMap<string, ReadonlyArray<LoadCycleData>>;
}

export interface LoadCycleFilter {
  readonly zoom?: { start: Date; end: Date };
  readonly partAndProc?: PartAndProcess;
  readonly pallet?: string;
}

export function loadOccupancyCycles(
  allCycles: Vector<PartCycleData>,
  { zoom, partAndProc, pallet }: LoadCycleFilter
): FilteredLoadCycles {
  return {
    seriesLabel: "Station",
    data: LazySeq.ofIterable(allCycles)
      .filter((e) => e.isLabor)
      .groupBy((e) => stat_name_and_num(e.stationGroup, e.stationNumber))
      .mapValues((cyclesForStat) =>
        chunkCyclesWithSimilarEndTime(cyclesForStat, (c) => c.x)
          .transform(LazySeq.ofIterable)
          .mapOption((chunk) => {
            var cycle = chunk.find((e) => {
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
              return Option.some({
                ...cycle,
                operations: LazySeq.ofIterable(chunk)
                  .flatMap((e) =>
                    e.material.map((mat) => ({
                      mat,
                      operation: e.operation,
                    }))
                  )
                  .toArray(),
              });
            } else {
              return Option.none<LoadCycleData>();
            }
          })
          .toArray()
      ),
  };
}

export interface LoadOpFilters {
  readonly operation: PartAndStationOperation;
  readonly pallet?: string;
  readonly zoom?: { readonly start: Date; readonly end: Date };
}

export function estimateLulOperations(
  allCycles: Vector<PartCycleData>,
  { operation, pallet, zoom }: LoadOpFilters
): FilteredLoadCycles {
  return {
    seriesLabel: "Station",
    data: LazySeq.ofIterable(allCycles)
      .filter((e) => e.isLabor)
      .groupBy((e) => stat_name_and_num(e.stationGroup, e.stationNumber))
      .mapValues((cyclesForStat) =>
        chunkCyclesWithSimilarEndTime(cyclesForStat, (c) => c.x)
          .transform(LazySeq.ofIterable)
          .mapOption((chunk) => {
            const split = splitElapsedTimeAmongChunk(
              chunk,
              (c) => c.y,
              (c) => c.activeMinutes
            );
            const splitCycle = split.find((e) => {
              if (zoom && (e.cycle.x < zoom.start || e.cycle.x > zoom.end)) {
                return false;
              }
              if (!operation.equals(PartAndStationOperation.ofPartCycle(e.cycle))) {
                return false;
              }
              if (pallet && e.cycle.pallet !== pallet) {
                return false;
              }
              return true;
            });

            if (splitCycle) {
              return Option.some({
                ...splitCycle.cycle,
                y: splitCycle.elapsedForSingleMaterialMinutes,
                operations: LazySeq.ofIterable(chunk)
                  .flatMap((e) =>
                    e.material.map((mat) => ({
                      mat,
                      operation: e.operation + (e === splitCycle.cycle ? "*" : ""),
                    }))
                  )
                  .toArray(),
              });
            } else {
              return Option.none<LoadCycleData>();
            }
          })
          .toArray()
      ),
  };
}

export function outlierMachineCycles(
  allCycles: Vector<PartCycleData>,
  start: Date,
  end: Date,
  estimated: EstimatedCycleTimes
): FilteredStationCycles {
  return {
    seriesLabel: "Part",
    data: LazySeq.ofIterable(allCycles)
      .filter((e) => !e.isLabor && e.x >= start && e.x <= end)
      .filter((cycle) => {
        if (cycle.material.length === 0) return false;
        const stats = estimated.get(PartAndStationOperation.ofPartCycle(cycle));
        return stats.isSome() && isOutlier(stats.get(), cycle.y / cycle.material.length);
      })
      .groupBy((e) => e.part + "-" + e.process.toString())
      .mapValues((e) => e.toArray()),
  };
}

export function outlierLoadCycles(
  allCycles: Vector<PartCycleData>,
  start: Date,
  end: Date,
  estimated: EstimatedCycleTimes
): FilteredStationCycles {
  const loadCycles = LazySeq.ofIterable(allCycles).filter((e) => e.isLabor && e.x >= start && e.x <= end);
  const now = new Date();
  return {
    seriesLabel: "Part",
    data: splitElapsedLoadTimeAmongCycles(loadCycles)
      .filter((e) => {
        if (e.cycle.material.length === 0) return false;
        // if it is too close to the start (or end) we might have cut off and only seen half of the events
        if (differenceInSeconds(e.cycle.x, start) < 20 || differenceInSeconds(end, e.cycle.x) < 20) return false;

        // if the cycle is within 15 seconds of now, don't display it yet.  We might only have received some
        // of the events for the load and the others are in-flight about to be received.
        // Technically, using now is wrong since the result of outlierLoadCycles is cached, but as soon as
        // another cycle arrives it will be recaluclated.  Thus showing stale data isn't a huge problem.
        if (Math.abs(differenceInSeconds(now, e.cycle.x)) < 15) return false;

        const stats = estimated.get(PartAndStationOperation.ofPartCycle(e.cycle));
        return stats.isSome() && isOutlier(stats.get(), e.elapsedForSingleMaterialMinutes);
      })
      .map((e) => e.cycle)
      .groupBy((c) => c.part + "-" + c.process.toString())
      .mapValues((e) => e.toArray()),
  };
}

export function stationMinutes(partCycles: Vector<PartCycleData>, cutoff: Date): HashMap<string, number> {
  return LazySeq.ofIterable(partCycles)
    .filter((p) => p.x >= cutoff)
    .map((p) => ({
      station: stat_name_and_num(p.stationGroup, p.stationNumber),
      active: p.activeMinutes,
    }))
    .toMap(
      (x) => [x.station, x.active] as [string, number],
      (v1, v2) => v1 + v2
    );
}

export function plannedOperationSeries(
  s: FilteredStationCycles,
  forSingleMat: boolean
): ReadonlyArray<{ readonly x: Date; readonly y: number }> {
  const arr = LazySeq.ofIterable(s.data)
    .flatMap(([, d]) => d)
    .filter((c) => c.material.length > 0)
    .map((c) => ({ x: c.x, y: forSingleMat ? c.activeMinutes / c.material.length : c.activeMinutes }))
    .toArray();

  arr.sort((a, b) => a.x.getTime() - b.x.getTime());

  const toRemove = new Set<number>();
  for (let i = 1; i < arr.length - 1; i++) {
    if (arr[i - 1].y === arr[i].y && arr[i].y === arr[i + 1].y) {
      toRemove.add(i);
    }
  }

  return arr.filter((_, idx) => !toRemove.has(idx));
}

// --------------------------------------------------------------------------------
// Clipboard
// --------------------------------------------------------------------------------

export function format_cycle_inspection(
  c: PartCycleData,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>
): string {
  const ret = [];
  let signaled = HashSet.empty<string>();
  let completed = HashSet.empty<string>();
  let success = HashSet.empty<string>();

  for (const mat of c.material) {
    const summary = matsById.get(mat.id).getOrNull();
    if (summary) {
      signaled = signaled.addAll(summary.signaledInspections);
      for (const [compInsp, details] of LazySeq.ofObject(summary.completedInspections ?? {})) {
        signaled = signaled.add(compInsp);
        completed = completed.add(compInsp);
        if (details.success) {
          success = success.add(compInsp);
        }
      }
    }
  }

  for (const name of signaled.toArray({ sortOn: (x) => x })) {
    if (completed.contains(name)) {
      ret.push(name + "[" + (success.contains(name) ? "success" : "failed") + "]");
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

  const filteredCycles = LazySeq.ofIterable(cycles.data)
    .flatMap(([_, c]) => c)
    .filter((p) => (!startD || p.x >= startD) && (!endD || p.x < endD))
    .toArray()
    .sort((a, b) => a.x.getTime() - b.x.getTime());
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
  copy(buildCycleTable(cycles, matsById, zoom ? zoom.start : undefined, zoom ? zoom.end : undefined, hideMedian));
}

export function buildPalletCycleTable(
  points: HashMap<string, ReadonlyArray<{ readonly x: Date; readonly y: number }>>
) {
  let table = "<table>\n<thead><tr>";
  table += "<th>Pallet</th><th>Date</th><th>Elapsed (min)</th>";
  table += "</tr></thead>\n<tbody>\n";

  const pals = points.keySet().toArray({ sortOn: (x) => x });

  for (const pal of pals) {
    for (const cycle of points.get(pal).getOrElse([])) {
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

export function copyPalletCyclesToClipboard(
  points: HashMap<string, ReadonlyArray<{ readonly x: Date; readonly y: number }>>
) {
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
      table += "<td>" + duration(cycle.elapsed).asMinutes().toFixed(1) + "</td>";
      table += "<td>" + duration(cycle.active).asMinutes().toFixed(1) + "</td>";
      table += "</tr>\n";
    }
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyLogEntriesToClipboard(cycles: Iterable<Readonly<api.ILogEntry>>): void {
  copy(buildLogEntriesTable(cycles));
}
