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

import { SimStationUse } from "./events.simuse";
import { startOfDay, addMinutes, differenceInMinutes, addDays } from "date-fns";
import { HashMap, fieldsHashCode } from "prelude-ts";
import { LazySeq } from "./lazyseq";
import { CycleData } from "./events.cycles";
import { PartCycleData, stat_name_and_num } from "./events.cycles";
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

// --------------------------------------------------------------------------------
// Combined
// --------------------------------------------------------------------------------

export interface OEEBarPoint {
  readonly x: string;
  readonly y: number;
  readonly planned: number;
  readonly station: string;
  readonly day: Date;
}

export interface OEEBarSeries {
  readonly station: string;
  readonly points: ReadonlyArray<OEEBarPoint>;
}

export function buildOeeSeries(
  start: Date,
  end: Date,
  isLabor: boolean,
  cycles: Iterable<PartCycleData>,
  statUse: Iterable<SimStationUse>
): ReadonlyArray<OEEBarSeries> {
  const filteredCycles = LazySeq.ofIterable(cycles).filter(e => isLabor === e.isLabor && e.x >= start && e.x <= end);
  const actualBins = binCyclesByDayAndStat(filteredCycles, c => c.activeMinsForSingleMat);
  const filteredStatUse = LazySeq.ofIterable(statUse).filter(
    e => isLabor === e.station.startsWith("L/U") && e.end >= start && e.start <= end
  );
  const plannedBins = binSimStationUseByDayAndStat(filteredStatUse, c => c.utilizationTime - c.plannedDownTime);

  const series: Array<OEEBarSeries> = [];
  const statNames = actualBins
    .keySet()
    .addAll(plannedBins.keySet())
    .map(e => e.station)
    .toArray({ sortOn: x => x });

  for (let stat of statNames) {
    const points: Array<OEEBarPoint> = [];
    for (let d = start; d < end; d = addDays(d, 1)) {
      const dAndStat = new DayAndStation(d, stat);
      const actual = actualBins.get(dAndStat);
      const planned = plannedBins.get(dAndStat);
      points.push({
        x: d.toLocaleDateString(),
        y: actual.getOrElse(0) / 60,
        planned: planned.getOrElse(0) / 60,
        station: stat,
        day: d
      });
    }
    series.push({
      station: stat,
      points: points
    });
  }
  return series;
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

export function buildOeeTable(series: Iterable<OEEBarSeries>) {
  let table = "<table>\n<thead><tr>";
  table += "<th>Day</th><th>Station</th><th>Actual Hours</th><th>Planned Hours</th>";
  table += "</tr></thead>\n<tbody>\n";

  for (let s of series) {
    for (let pt of s.points) {
      table += "<tr>";
      table += "<td>" + pt.day.toLocaleDateString() + "</td>";
      table += "<td>" + pt.station + "</td>";
      table += "<td>" + pt.y.toFixed(1) + "</td>";
      table += "<td>" + pt.planned.toFixed(1) + "</td>";
      table += "</tr>\n";
    }
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyOeeToClipboard(series: Iterable<OEEBarSeries>): void {
  copy(buildOeeTable(series));
}
