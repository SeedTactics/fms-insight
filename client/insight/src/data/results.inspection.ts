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
import * as api from "./api";
import { Vector, ToOrderable, HashMap, Option } from "prelude-ts";
import { InspectionLogEntry, InspectionLogResultType } from "./events.inspection";
import { LazySeq } from "./lazyseq";
import { format } from "date-fns";
import { MaterialDetail } from "./material-details";
const copy = require("copy-to-clipboard");

export interface TriggeredInspectionEntry {
  readonly time: Date;
  readonly materialID: number;
  readonly serial?: string;
  readonly workorder?: string;
  readonly toInspect: boolean;
  readonly path: string;
  readonly failed: boolean;
}

export function buildPathString(procs: ReadonlyArray<Readonly<api.IMaterialProcessActualPath>>) {
  const pathStrs = [];
  for (let proc of procs) {
    for (let stop of proc.stops) {
      pathStrs.push("P" + proc.pallet.toString() + "," + "M" + stop.stationNum.toString());
    }
  }
  return pathStrs.join(" -> ");
}

export interface InspectionsForPath {
  readonly material: Vector<TriggeredInspectionEntry>;
  readonly failedCnt: number;
}

export function groupInspectionsByPath(
  entries: ReadonlyArray<InspectionLogEntry>,
  dateRange: { start: Date; end: Date } | undefined,
  sortOn: ToOrderable<TriggeredInspectionEntry> | { desc: ToOrderable<TriggeredInspectionEntry> }
): HashMap<string, InspectionsForPath> {
  const failed = LazySeq.ofIterable(entries)
    .mapOption(e => {
      if (e.result.type === InspectionLogResultType.Completed && !e.result.success) {
        return Option.some(e.materialID);
      } else {
        return Option.none<number>();
      }
    })
    .toSet(e => e);

  return LazySeq.ofIterable(entries)
    .mapOption(e => {
      if (dateRange && (e.time < dateRange.start || e.time > dateRange.end)) {
        return Option.none<TriggeredInspectionEntry>();
      }
      switch (e.result.type) {
        case InspectionLogResultType.Triggered:
          return Option.some({
            time: e.time,
            materialID: e.materialID,
            serial: e.serial,
            workorder: e.workorder,
            toInspect: e.result.toInspect,
            path: buildPathString(e.result.actualPath),
            failed: failed.contains(e.materialID)
          });
        default:
          return Option.none<TriggeredInspectionEntry>();
      }
    })
    .groupBy(e => e.path)
    .mapValues(mats => ({
      material: mats.sortOn(sortOn, e => e.time.getTime()),
      failedCnt: mats.sumOn(e => (e.failed ? 1 : 0))
    }));
}

export interface FailedInspectionEntry {
  readonly time: Date;
  readonly materialID: number;
  readonly serial?: string;
  readonly workorder?: string;
  readonly inspType: string;
  readonly part: string;
}

export function extractFailedInspections(
  entries: Iterable<InspectionLogEntry>,
  start: Date,
  end: Date
): Vector<FailedInspectionEntry> {
  return LazySeq.ofIterable(entries)
    .mapOption(e => {
      if (e.time < start || e.time > end) {
        return Option.none<FailedInspectionEntry>();
      }
      if (e.result.type === InspectionLogResultType.Completed && !e.result.success) {
        return Option.some({
          time: e.time,
          materialID: e.materialID,
          serial: e.serial,
          workorder: e.workorder,
          inspType: e.inspType,
          part: e.part
        });
      } else {
        return Option.none<FailedInspectionEntry>();
      }
    })
    .toVector();
}

// --------------------------------------------------------------------------------
// Failed Lookup
// --------------------------------------------------------------------------------

export function extractPath(mat: MaterialDetail): ReadonlyArray<Readonly<api.IMaterialProcessActualPath>> {
  for (let e of mat.events) {
    if (e.type === api.LogType.Inspection) {
      const pathsJson: ReadonlyArray<object> = JSON.parse((e.details || {}).ActualPath || "[]");
      const paths: Array<Readonly<api.IMaterialProcessActualPath>> = [];
      for (const pathJson of pathsJson) {
        paths.push(api.MaterialProcessActualPath.fromJS(pathJson));
      }
      if (paths.length > 0) {
        return paths;
      }
    }
  }
  return [];
}

// --------------------------------------------------------------------------------
// Clipboard
// --------------------------------------------------------------------------------

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

export function buildFailedInspTable(entries: Vector<FailedInspectionEntry>): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Date</th><th>Part</th><th>Inspection</th><th>Serial</th><th>Workorder</th>";
  table += "</tr></thead>\n<tbody>\n";

  for (const e of entries.sortOn({ desc: x => x.time.getTime() })) {
    table += "<tr>";
    table += "<td>" + e.time.toLocaleString() + "</td>";
    table += "<td>" + e.part + "</td>";
    table += "<td>" + e.inspType + "</td>";
    table += "<td>" + (e.serial || "") + "</td>";
    table += "<td>" + (e.workorder || "") + "</td>";
    table += "</tr>\n";
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyFailedInspectionsToClipboard(entries: Vector<FailedInspectionEntry>) {
  copy(buildFailedInspTable(entries));
}
