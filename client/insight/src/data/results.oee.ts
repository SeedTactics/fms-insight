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

import { SimStationUse, SimProduction } from "./events.simuse";
import { startOfDay, addMinutes, differenceInMinutes } from "date-fns";
import { HashMap, fieldsHashCode, Vector } from "prelude-ts";
import { LazySeq } from "./lazyseq";
import { CycleData } from "./events.cycles";
import { PartCycleData, stat_name_and_num, part_and_proc } from "./events.cycles";
const copy = require("copy-to-clipboard");

// --------------------------------------------------------------------------------
// Actual
// --------------------------------------------------------------------------------

export class DayAndStation {
  constructor(public readonly day: Date, public readonly station: string) {}
  equals(other: DayAndStation): boolean {
    return this.day.getTime() === other.day.getTime() && this.station === other.station;
  }
  hashCode(): number {
    return fieldsHashCode(this.day.getTime(), this.station);
  }
  toString(): string {
    return `{day: ${this.day.toISOString()}}, station: ${this.station}}`;
  }
  adjustDay(f: (d: Date) => Date): DayAndStation {
    return new DayAndStation(f(this.day), this.station);
  }
}

function splitPartCycleToDays(cycle: CycleData, totalVal: number): Array<{ day: Date; value: number }> {
  const startDay = startOfDay(cycle.x);
  const endTime = addMinutes(cycle.x, totalVal);
  const endDay = startOfDay(endTime);
  if (startDay.getTime() === endDay.getTime()) {
    return [
      {
        day: startDay,
        value: totalVal
      }
    ];
  } else {
    const startDayPct = differenceInMinutes(endDay, cycle.x) / totalVal;
    return [
      {
        day: startDay,
        value: totalVal * startDayPct
      },
      {
        day: endDay,
        value: totalVal * (1 - startDayPct)
      }
    ];
  }
}

export function binCyclesByDayAndStat(
  cycles: Iterable<PartCycleData>,
  extractValue: (c: PartCycleData) => number
): HashMap<DayAndStation, number> {
  return LazySeq.ofIterable(cycles)
    .flatMap(point =>
      splitPartCycleToDays(point, extractValue(point)).map(x => ({
        ...x,
        station: stat_name_and_num(point.stationGroup, point.stationNumber),
        isLabor: point.isLabor
      }))
    )
    .toMap(p => [new DayAndStation(p.day, p.station), p.value] as [DayAndStation, number], (v1, v2) => v1 + v2);
}

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

export function binCyclesByDayAndPart(
  cycles: Vector<PartCycleData>,
  extractValue: (c: PartCycleData) => number
): HashMap<DayAndPart, number> {
  return LazySeq.ofIterable(cycles)
    .map(point => ({
      day: startOfDay(point.x),
      part: part_and_proc(point.part, point.process),
      value: extractValue(point)
    }))
    .toMap(p => [new DayAndPart(p.day, p.part), p.value] as [DayAndPart, number], (v1, v2) => v1 + v2);
}

// --------------------------------------------------------------------------------
// Planned
// --------------------------------------------------------------------------------

interface DayStatAndVal {
  day: Date;
  station: string;
  value: number;
}

function splitElapsedToDays(simUse: SimStationUse, extractValue: (c: SimStationUse) => number): DayStatAndVal[] {
  const startDay = startOfDay(simUse.start);
  const endDay = startOfDay(simUse.end);

  if (startDay.getTime() === endDay.getTime()) {
    return [
      {
        day: startDay,
        station: simUse.station,
        value: extractValue(simUse)
      }
    ];
  } else {
    const totalVal = extractValue(simUse);
    const endDayPercentage =
      (simUse.end.getTime() - endDay.getTime()) / (simUse.end.getTime() - simUse.start.getTime());
    return [
      {
        day: startDay,
        station: simUse.station,
        value: totalVal * (1 - endDayPercentage)
      },
      {
        day: endDay,
        station: simUse.station,
        value: totalVal * endDayPercentage
      }
    ];
  }
}

export function binSimStationUseByDayAndStat(
  simUses: Iterable<SimStationUse>,
  extractValue: (c: SimStationUse) => number
): HashMap<DayAndStation, number> {
  return LazySeq.ofIterable(simUses)
    .flatMap(s => splitElapsedToDays(s, extractValue))
    .toMap(s => [new DayAndStation(s.day, s.station), s.value] as [DayAndStation, number], (v1, v2) => v1 + v2);
}

export function binSimProductionByDayAndPart(prod: Iterable<SimProduction>): HashMap<DayAndPart, number> {
  return LazySeq.ofIterable(prod).toMap(
    p => [new DayAndPart(startOfDay(p.start), p.part), p.quantity] as [DayAndPart, number],
    (q1, q2) => q1 + q2
  );
}

// --------------------------------------------------------------------------------
// Clipboard
// --------------------------------------------------------------------------------

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
