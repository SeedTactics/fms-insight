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

import { SimProduction } from "./events.simuse";
import { startOfDay, addDays } from "date-fns";
import { HashMap, fieldsHashCode } from "prelude-ts";
import { LazySeq } from "./lazyseq";
import { PartCycleData, part_and_proc } from "./events.cycles";
const copy = require("copy-to-clipboard");

// --------------------------------------------------------------------------------
// Actual
// --------------------------------------------------------------------------------

class DayAndPart {
  constructor(public day: Date, public part: string) {}
  equals(other: DayAndPart): boolean {
    return this.day.getTime() === other.day.getTime() && this.part === other.part;
  }
  hashCode(): number {
    return fieldsHashCode(this.day.getTime(), this.part);
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

export function binCyclesByDayAndPart(cycles: Iterable<PartCycleData>): HashMap<DayAndPart, PartsCompletedSummary> {
  return LazySeq.ofIterable(cycles)
    .map(point => ({
      day: startOfDay(point.x),
      part: part_and_proc(point.part, point.process),
      value: {
        count: point.completed ? 1 : 0,
        activeMachineMins: point.completed ? point.activeTotalMachineMinutesForSingleMat : 0
      }
    }))
    .toMap(
      p => [new DayAndPart(p.day, p.part), p.value] as [DayAndPart, PartsCompletedSummary],
      (v1, v2) => ({
        count: v1.count + v2.count,
        activeMachineMins: v1.activeMachineMins + v2.activeMachineMins
      })
    );
}

// --------------------------------------------------------------------------------
// Planned
// --------------------------------------------------------------------------------

export function binSimProductionByDayAndPart(prod: Iterable<SimProduction>): HashMap<DayAndPart, number> {
  return LazySeq.ofIterable(prod).toMap(
    p => [new DayAndPart(startOfDay(p.start), p.part), p.quantity] as [DayAndPart, number],
    (q1, q2) => q1 + q2
  );
}

// --------------------------------------------------------------------------------
// Combined
// --------------------------------------------------------------------------------

export interface CompletedPartEntry {
  readonly day: Date;
  readonly actual: number;
  readonly planned: number;
}

export interface CompletedPartSeries {
  readonly part: string;
  readonly days: ReadonlyArray<CompletedPartEntry>;
}

export function buildCompletedPartSeries(
  start: Date,
  end: Date,
  cycles: Iterable<PartCycleData>,
  sim: Iterable<SimProduction>
): ReadonlyArray<CompletedPartSeries> {
  const filteredCycles = LazySeq.ofIterable(cycles).filter(e => e.x >= start && e.x <= end);
  const actualBins = binCyclesByDayAndPart(filteredCycles);
  const filteredStatUse = LazySeq.ofIterable(sim).filter(e => e.start >= start && e.start <= end);
  const plannedBins = binSimProductionByDayAndPart(filteredStatUse);

  const series: Array<CompletedPartSeries> = [];
  const partNames = actualBins
    .keySet()
    .addAll(plannedBins.keySet())
    .map(e => e.part)
    .toArray({ sortOn: x => x });

  for (let part of partNames) {
    const days: Array<CompletedPartEntry> = [];
    for (let d = start; d < end; d = addDays(d, 1)) {
      const dAndPart = new DayAndPart(d, part);
      const actual = actualBins.get(dAndPart);
      const planned = plannedBins.get(dAndPart);
      days.push({
        actual: actual.map(v => v.count).getOrElse(0),
        planned: planned.getOrElse(0),
        day: d
      });
    }
    series.push({
      part: part,
      days: days
    });
  }
  return series;
}

// --------------------------------------------------------------------------------
// Clipboard
// --------------------------------------------------------------------------------

export function buildCompletedPartsTable(series: ReadonlyArray<CompletedPartSeries>) {
  const days = series.length > 0 ? series[0].days.map(p => p.day) : [];

  let table = "<table>\n<thead><tr>";
  table += "<th>Part</th>";
  for (let d of days) {
    table += "<th>" + d.toLocaleDateString() + " Actual</th>";
    table += "<th>" + d.toLocaleDateString() + " Planned</th>";
  }
  table += "</tr></thead>\n<tbody>\n";

  for (let s of series) {
    table += "<tr><td>" + s.part + "</td>";
    for (let day of s.days) {
      table += "<td>" + day.actual.toFixed(0) + "</td>";
      table += "<td>" + day.planned.toFixed(0) + "</td>";
    }
    table += "</tr>\n";
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyCompletedPartsToClipboard(series: ReadonlyArray<CompletedPartSeries>): void {
  copy(buildCompletedPartsTable(series));
}
