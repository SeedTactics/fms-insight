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

import { LazySeq } from "./lazyseq";
import { fieldsHashCode } from "prelude-ts";
import { FilteredStationCycles, stat_name_and_num, format_cycle_inspection } from "./events.cycles";
import { format } from "date-fns";
import * as api from "../data/api";
import { duration } from "moment";
import { InspectionLogEntry } from "./events";
import { groupInspectionsByPath } from "./events.inspection";
const copy = require("copy-to-clipboard");

export interface HeatmapClipboardPoint {
  readonly x: Date;
  readonly y: string;
  readonly label: string;
}

class HeatmapClipboardCell {
  public constructor(public readonly x: number, public readonly y: string) {}
  equals(other: HeatmapClipboardCell): boolean {
    return this.x === other.x && this.y === other.y;
  }
  hashCode(): number {
    return fieldsHashCode(this.x, this.y);
  }
  toString(): string {
    return `{x: ${new Date(this.x).toISOString()}, y: ${this.y}}`;
  }
}

export function buildHeatmapTable(yTitle: string, points: ReadonlyArray<HeatmapClipboardPoint>): string {
  const cells = LazySeq.ofIterable(points).toMap(
    p => [new HeatmapClipboardCell(p.x.getTime(), p.y), p],
    (_, c) => c // cells should be unique, but just in case take the second
  );
  const days = LazySeq.ofIterable(points)
    .toSet(p => p.x.getTime())
    .toArray({ sortOn: x => x });
  const rows = LazySeq.ofIterable(points)
    .toSet(p => p.y)
    .toArray({ sortOn: x => x });

  let table = "<table>\n<thead><tr><th>" + yTitle + "</th>";
  for (let x of days) {
    table += "<th>" + new Date(x).toDateString() + "</th>";
  }
  table += "</tr></thead>\n<tbody>\n";
  for (let y of rows) {
    table += "<tr><th>" + y + "</th>";
    for (let x of days) {
      const cell = cells.get(new HeatmapClipboardCell(x, y));
      if (cell.isSome()) {
        table += "<td>" + cell.get().label + "</td>";
      } else {
        table += "<td></td>";
      }
    }
    table += "</tr>\n";
  }
  table += "</tbody>\n</table>";
  return table;
}

export function copyHeatmapToClipboard(yTitle: string, points: ReadonlyArray<HeatmapClipboardPoint>): void {
  copy(buildHeatmapTable(yTitle, points));
}

export function buildCycleTable(
  cycles: FilteredStationCycles,
  includeStats: boolean,
  startD: Date | undefined,
  endD: Date | undefined
): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Date</th><th>Part</th><th>Station</th><th>Pallet</th>";
  table += "<th>Serial</th><th>Workorder</th><th>Inspection</th>";
  table += "<th>Elapsed Min</th><th>Active Min</th>";
  if (includeStats) {
    table += "<th>Median Elapsed Min</th><th>Median Deviation</th>";
  }
  table += "</tr></thead>\n<tbody>\n";

  let filteredCycles = LazySeq.ofIterable(cycles.data)
    .flatMap(([_, c]) => c)
    .filter(p => (!startD || p.x >= startD) && (!endD || p.x < endD))
    .toArray()
    .sort((a, b) => a.x.getTime() - b.x.getTime());
  for (let cycle of filteredCycles) {
    table += "<tr>";
    table += "<td>" + format(cycle.x, "MMM D, YYYY, H:mm a") + "</td>";
    table += "<td>" + cycle.part + "-" + cycle.process.toString() + "</td>";
    table += "<td>" + stat_name_and_num(cycle.stationGroup, cycle.stationNumber) + "</td>";
    table += "<td>" + cycle.pallet + "</td>";
    table += "<td>" + (cycle.serial || "") + "</td>";
    table += "<td>" + (cycle.workorder || "") + "</td>";
    table += "<td>" + format_cycle_inspection(cycle) + "</td>";
    table += "<td>" + cycle.y.toFixed(1) + "</td>";
    table += "<td>" + cycle.active.toFixed(1) + "</td>";
    if (includeStats) {
      table += "<td>" + cycle.medianElapsed.toFixed(1) + "</td>";
      table += "<td>" + cycle.MAD_aboveMinutes.toFixed(1) + "</td>";
    }
    table += "</tr>\n";
  }
  table += "</tbody>\n</table>";
  return table;
}

export function copyCyclesToClipboard(
  cycles: FilteredStationCycles,
  includeStats: boolean,
  zoom: { start: Date; end: Date } | undefined
): void {
  copy(buildCycleTable(cycles, includeStats, zoom ? zoom.start : undefined, zoom ? zoom.end : undefined));
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
    case api.LogType.Inspection:
      const inspName = (e.details || {}).InspectionType || "";
      return "Signal " + inspName;
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
  for (let cycle of cycles) {
    if (cycle.startofcycle) {
      continue;
    }
    for (let mat of cycle.material) {
      table += "<tr>";
      table += "<td>" + format(cycle.endUTC, "MMM D, YYYY, H:mm a") + "</td>";
      table += "<td>" + mat.part + "-" + mat.proc.toString() + "</td>";
      table += "<td>" + stat_name(cycle) + "</td>";
      table += "<td>" + cycle.pal + "</td>";
      table += "<td>" + (mat.serial || "") + "</td>";
      table += "<td>" + (mat.workorder || "") + "</td>";
      table += "<td>" + result(cycle) + "</td>";
      table +=
        "<td>" +
        duration(cycle.elapsed)
          .asMinutes()
          .toFixed(1) +
        "</td>";
      table +=
        "<td>" +
        duration(cycle.active)
          .asMinutes()
          .toFixed(1) +
        "</td>";
      table += "</tr>\n";
    }
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyLogEntriesToClipboard(cycles: Iterable<Readonly<api.ILogEntry>>): void {
  copy(buildLogEntriesTable(cycles));
}

export function buildInspectionTable(
  part: string,
  inspType: string,
  entries: ReadonlyArray<InspectionLogEntry>
): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Path</th><th>Date</th><th>Part</th><th>Inspection</th>";
  table += "<th>Serial</th><th>Workorder</th><th>Inspected</th><th>Failed</th>";
  table += "</tr></thead>\n<tbody>\n";

  const groups = groupInspectionsByPath(entries, undefined, e => e.time.getTime());
  const paths = groups.keySet().toArray({ sortOn: x => x });

  for (let path of paths) {
    const data = groups.get(path).getOrThrow();
    for (let mat of data.material) {
      table += "<tr>";
      table += "<td>" + path + "</td>";
      table += "<td>" + format(mat.time, "MMM D, YYYY, H:mm a") + "</td>";
      table += "<td>" + part + "</td>";
      table += "<td>" + inspType + "</td>";
      table += "<td>" + (mat.serial || "") + "</td>";
      table += "<td>" + (mat.workorder || "") + "</td>";
      table += "<td>" + (mat.toInspect ? "inspected" : "") + "</td>";
      table += "<td>" + (mat.failed ? "failed" : "") + "</td>";
      table += "</tr>\n";
    }
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyInspectionEntriesToClipboard(
  part: string,
  inspType: string,
  entries: ReadonlyArray<InspectionLogEntry>
): void {
  copy(buildInspectionTable(part, inspType, entries));
}

export interface OEEClipboardPoint {
  readonly day: Date;
  readonly y: number;
  readonly planned: number;
  readonly station: string;
}

export function buildOeeTable(points: LazySeq<OEEClipboardPoint>) {
  let table = "<table>\n<thead><tr>";
  table += "<th>Day</th><th>Station</th><th>Actual Hours</th><th>Planned Hours</th>";
  table += "</tr></thead>\n<tbody>\n";

  for (let pt of points) {
    table += "<tr>";
    table += "<td>" + pt.day.toLocaleDateString() + "</td>";
    table += "<td>" + pt.station + "</td>";
    table += "<td>" + pt.y.toFixed(1) + "</td>";
    table += "<td>" + pt.planned.toFixed(1) + "</td>";
    table += "</tr>\n";
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyOeeToClipboard(points: LazySeq<OEEClipboardPoint>): void {
  copy(buildOeeTable(points));
}
