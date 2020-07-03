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

import { HashMap, Vector } from "prelude-ts";
import {
  PartCycleData,
  stat_name_and_num,
  part_and_proc,
  isOutlier,
  EstimatedCycleTimes,
  splitElapsedLoadTimeAmongCycles,
  PartAndStationOperation,
} from "./events.cycles";
import { LazySeq } from "./lazyseq";
import * as api from "./api";
import { format, differenceInSeconds } from "date-fns";
import { duration } from "moment";
// eslint-disable-next-line @typescript-eslint/no-var-requires
const copy = require("copy-to-clipboard");

export interface FilteredStationCycles {
  readonly seriesLabel: string;
  readonly data: HashMap<string, ReadonlyArray<PartCycleData>>;
}

export const FilterAnyMachineKey = "@@@_FMSInsight_FilterAnyMachineKey_@@@";
export const FilterAnyLoadKey = "@@@_FMSInsigt_FilterAnyLoadKey_@@@";

export function filterStationCycles(
  allCycles: Vector<PartCycleData>,
  zoom: { start: Date; end: Date } | undefined,
  partAndProc?: string,
  pallet?: string,
  station?: string,
  operation?: PartAndStationOperation
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
        if (partAndProc && part_and_proc(e.part, e.process) !== partAndProc) {
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
          return part_and_proc(e.part, e.process);
        } else {
          return stat_name_and_num(e.stationGroup, e.stationNumber);
        }
      })
      .mapValues((e) => e.toArray()),
  };
}

export function outlierMachineCycles(
  allCycles: Vector<PartCycleData>,
  start: Date,
  end: Date,
  estimated: EstimatedCycleTimes
) {
  return {
    seriesLabel: "Part",
    data: LazySeq.ofIterable(allCycles)
      .filter((e) => !e.isLabor && e.x >= start && e.x <= end)
      .filter((cycle) => {
        if (cycle.material.length === 0) return false;
        const stats = estimated.get(PartAndStationOperation.ofPartCycle(cycle));
        return stats.isSome() && isOutlier(stats.get(), cycle.y / cycle.material.length);
      })
      .groupBy((e) => part_and_proc(e.part, e.process))
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
      .groupBy((c) => part_and_proc(c.part, c.process))
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

// --------------------------------------------------------------------------------
// Clipboard
// --------------------------------------------------------------------------------

export function format_cycle_inspection(c: PartCycleData): string {
  const ret = [];
  const names = c.signaledInspections.addAll(c.completedInspections.keySet());
  for (const name of names.toArray({ sortOn: (x) => x })) {
    const completed = c.completedInspections.get(name);
    if (completed.isSome()) {
      const success = completed.get();
      ret.push(name + "[" + (success ? "success" : "failed") + "]");
    } else {
      ret.push(name);
    }
  }
  return ret.join(", ");
}

export function buildCycleTable(
  cycles: FilteredStationCycles,
  startD: Date | undefined,
  endD: Date | undefined
): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Date</th><th>Part</th><th>Station</th><th>Pallet</th>";
  table += "<th>Serial</th><th>Workorder</th><th>Inspection</th>";
  table += "<th>Elapsed Min</th><th>Target Min</th>";
  table += "<th>Median Elapsed Min</th><th>Median Deviation</th>";
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
    table += "<td>" + format_cycle_inspection(cycle) + "</td>";
    table += "<td>" + cycle.y.toFixed(1) + "</td>";
    table += "<td>" + cycle.activeMinutes.toFixed(1) + "</td>";
    table += "<td>" + cycle.medianCycleMinutes.toFixed(1) + "</td>";
    table += "<td>" + cycle.MAD_aboveMinutes.toFixed(1) + "</td>";
    table += "</tr>\n";
  }
  table += "</tbody>\n</table>";
  return table;
}

export function copyCyclesToClipboard(
  cycles: FilteredStationCycles,
  zoom: { start: Date; end: Date } | undefined
): void {
  copy(buildCycleTable(cycles, zoom ? zoom.start : undefined, zoom ? zoom.end : undefined));
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
