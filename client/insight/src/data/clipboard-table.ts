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
import { FilteredStationCycles, stat_name_and_num } from "./events.cycles";
import { format } from "date-fns";
const copy = require("copy-to-clipboard");

export interface ClipboardTablePoint {
  readonly x: Date;
  readonly y: string;
  readonly label: string;
}

class PointsTableCell {
  public constructor(public readonly x: number, public readonly y: string) {}
  equals(other: PointsTableCell): boolean {
    return this.x === other.x && this.y === other.y;
  }
  hashCode(): number {
    return fieldsHashCode(this.x, this.y);
  }
  toString(): string {
    return `{x: ${new Date(this.x).toISOString()}, y: ${this.y}}`;
  }
}

export function buildPointsTable(yTitle: string, points: ReadonlyArray<ClipboardTablePoint>): string {
  const cells = LazySeq.ofIterable(points).toMap(
    p => [new PointsTableCell(p.x.getTime(), p.y), p],
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
      const cell = cells.get(new PointsTableCell(x, y));
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

export function copyPointsToClipboard(yTitle: string, points: ReadonlyArray<ClipboardTablePoint>): void {
  copy(buildPointsTable(yTitle, points));
}

export function buildCycleTable(cycles: FilteredStationCycles): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Date</th><th>Part</th><th>Station</th><th>Pallet</th>";
  table += "<th>Serial</th><th>Workorder</th><td>Elapsed Min</td><td>Active Min</td>";
  table += "</tr></thead>\n<tbody>\n";
  for (let cycle of LazySeq.ofIterable(cycles.data).flatMap(([_, c]) => c)) {
    table += "<tr>";
    table += "<td>" + format(cycle.x, "MMM D, YYYY, H:mm a") + "</td>";
    table += "<td>" + cycle.part + "-" + cycle.process.toString() + "</td>";
    table += "<td>" + stat_name_and_num(cycle.stationGroup, cycle.stationNumber) + "</td>";
    table += "<td>" + cycle.pallet + "</td>";
    table += "<td>" + (cycle.serial || "") + "</td>";
    table += "<td>" + (cycle.workorder || "") + "</td>";
    table += "<td>" + cycle.y.toFixed(1) + "</td>";
    table += "<td>" + cycle.active.toFixed(1) + "</td>";
    table += "</tr>\n";
  }
  table += "</tbody>\n</table>";
  return table;
}

export function copyCyclesToClipboard(cycles: FilteredStationCycles): void {
  copy(buildCycleTable(cycles));
}
