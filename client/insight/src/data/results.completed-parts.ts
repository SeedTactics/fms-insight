/* Copyright (c) 2021, John Lenz

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

import { startOfDay } from "date-fns";
import { MaterialSummaryAndCompletedData } from "../cell-status/material-summary.js";
import copy from "copy-to-clipboard";
import { SimPartCompleted } from "../cell-status/sim-production.js";
import { PartCycleData } from "../cell-status/station-cycles.js";
import { HashMap, hashValues, LazySeq } from "@seedtactics/immutable-collections";

// --------------------------------------------------------------------------------
// Actual
// --------------------------------------------------------------------------------

class DayAndPart {
  constructor(
    public day: Date,
    public part: string,
  ) {}
  compare(other: DayAndPart): number {
    const cmp = this.day.getTime() - other.day.getTime();
    if (cmp === 0) {
      return this.part.localeCompare(other.part);
    } else {
      return cmp;
    }
  }
  hash(): number {
    return hashValues(this.day, this.part);
  }
  toString(): string {
    return `{day: ${this.day.toISOString()}}, part: ${this.part}}`;
  }
  adjustDay(f: (d: Date) => Date): DayAndPart {
    return new DayAndPart(f(this.day), this.part);
  }
}

export interface PartsCompletedSummary {
  readonly count: number;
  readonly activeMachineMins: number;
}

class MatIdAndProcess {
  public constructor(
    public readonly matId: number,
    public readonly proc: number,
  ) {}
  compare(other: MatIdAndProcess): number {
    const cmp = this.matId - other.matId;
    if (cmp === 0) {
      return this.proc - other.proc;
    } else {
      return cmp;
    }
  }
  hash(): number {
    return hashValues(this.matId, this.proc);
  }
  toString(): string {
    return this.matId.toString() + "-" + this.proc.toString();
  }
}

export function binCyclesByDayAndPart(
  cycles: Iterable<PartCycleData>,
  matsById: HashMap<number, MaterialSummaryAndCompletedData>,
  start: Date,
  end: Date,
): HashMap<DayAndPart, PartsCompletedSummary> {
  const activeTimeByMatId = LazySeq.of(cycles)
    .filter(
      (cycle) =>
        cycle.endTime >= start &&
        cycle.endTime <= end &&
        !cycle.isLabor &&
        cycle.activeMinutes > 0 &&
        cycle.material.length > 0,
    )
    .flatMap((cycle) =>
      cycle.material.map((mat) => ({
        matId: mat.id,
        proc: mat.proc,
        active: cycle.activeMinutes / cycle.material.length,
      })),
    )
    .toHashMap(
      (p) => [new MatIdAndProcess(p.matId, p.proc), p.active],
      (a1, a2) => a1 + a2,
    );

  return LazySeq.of(matsById)
    .flatMap(([matId, details]) =>
      LazySeq.ofObject(details.unloaded_processes ?? {})
        .filter(([_, unloadTime]) => unloadTime >= start && unloadTime <= end)
        .map(([proc, unloadTime]) => ({
          day: startOfDay(unloadTime),
          part: details.partName + "-" + proc.toString(),
          value: {
            count: 1,
            activeMachineMins: activeTimeByMatId.get(new MatIdAndProcess(matId, parseInt(proc))) ?? 0,
          },
        })),
    )
    .toHashMap(
      (p) => [new DayAndPart(p.day, p.part), p.value] as [DayAndPart, PartsCompletedSummary],
      (v1, v2) => ({
        count: v1.count + v2.count,
        activeMachineMins: v1.activeMachineMins + v2.activeMachineMins,
      }),
    );
}

// --------------------------------------------------------------------------------
// Planned
// --------------------------------------------------------------------------------

export function binSimProductionByDayAndPart(
  prod: Iterable<SimPartCompleted>,
): HashMap<DayAndPart, PartsCompletedSummary> {
  return LazySeq.of(prod).toHashMap(
    (p) =>
      [
        new DayAndPart(startOfDay(p.completeTime), p.partName + "-" + p.process.toString()),
        { count: p.quantity, activeMachineMins: p.expectedMachineMins },
      ] as [DayAndPart, PartsCompletedSummary],
    (v1, v2) => ({
      count: v1.count + v2.count,
      activeMachineMins: v1.activeMachineMins + v2.activeMachineMins,
    }),
  );
}

// --------------------------------------------------------------------------------
// Clipboard
// --------------------------------------------------------------------------------

interface HeatmapClipboardPoint {
  readonly x: Date;
  readonly y: string;
}

class HeatmapClipboardCell {
  public constructor(
    public readonly x: number,
    public readonly y: string,
  ) {}
  compare(other: HeatmapClipboardCell): number {
    const cmp = this.x - other.x;
    if (cmp === 0) {
      return this.y.localeCompare(other.y);
    } else {
      return cmp;
    }
  }
  hash(): number {
    return hashValues(this.x, this.y);
  }
  toString(): string {
    return `{x: ${new Date(this.x).toISOString()}, y: ${this.y}}`;
  }
}

export function buildCompletedPartsHeatmapTable(
  points: ReadonlyArray<HeatmapClipboardPoint & PartsCompletedSummary>,
): string {
  const cells = LazySeq.of(points).toHashMap(
    (p) => [new HeatmapClipboardCell(p.x.getTime(), p.y), p],
    (_, c) => c, // cells should be unique, but just in case take the second
  );
  const days = LazySeq.of(points)
    .map((p) => p.x.getTime())
    .distinct()
    .toSortedArray((x) => x);
  const rows = LazySeq.of(points)
    .aggregate(
      (p) => p.y,
      (p) => p.activeMachineMins / p.count,
      (first, snd) => (isNaN(first) ? snd : first),
    )
    .sortBy(([name, _cycleMins]) => name);

  let table = "<table>\n<thead><tr><th>Part</th><th>Expected Cycle Mins</th>";
  for (const x of days) {
    table += "<th>" + new Date(x).toDateString() + " Completed</th>";
    table += "<th>" + new Date(x).toDateString() + " Std. Hours</th>";
  }
  table += "</tr></thead>\n<tbody>\n";
  for (const [partName, cycleMins] of rows) {
    table += "<tr><th>" + partName + "</th><th>" + cycleMins.toFixed(1) + "</th>";
    for (const x of days) {
      const cell = cells.get(new HeatmapClipboardCell(x, partName));
      if (cell !== undefined) {
        table += "<td>" + cell.count.toFixed(0) + "</td>";
        table += "<td>" + (cell.activeMachineMins / 60).toFixed(1) + "</td>";
      } else {
        table += "<td></td>";
        table += "<td></td>";
      }
    }
    table += "</tr>\n";
  }
  table += "</tbody>\n</table>";
  return table;
}

export function copyCompletedPartsHeatmapToClipboard(
  points: ReadonlyArray<HeatmapClipboardPoint & PartsCompletedSummary>,
): void {
  copy(buildCompletedPartsHeatmapTable(points));
}
