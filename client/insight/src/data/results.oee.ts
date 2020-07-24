/* Copyright (c) 2020, John Lenz

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
import { startOfDay, addSeconds, addDays } from "date-fns";
import { HashMap, fieldsHashCode, Vector } from "prelude-ts";
import { LazySeq } from "./lazyseq";
import { chunkCyclesWithSimilarEndTime } from "./events.cycles";
import { PartCycleData, stat_name_and_num } from "./events.cycles";
// eslint-disable-next-line @typescript-eslint/no-var-requires
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

function splitPartCycleToDays(startTime: Date, endTime: Date, mins: number): Array<{ day: Date; value: number }> {
  const startDay = startOfDay(startTime);
  const endDay = startOfDay(endTime);
  if (startDay.getTime() === endDay.getTime()) {
    return [
      {
        day: startDay,
        value: mins,
      },
    ];
  } else {
    const endDayPercentage = (endTime.getTime() - endDay.getTime()) / (endTime.getTime() - startTime.getTime());
    const endDayMins = mins * Math.max(0, Math.min(endDayPercentage, 1));
    return [
      {
        day: startDay,
        value: mins - endDayMins,
      },
      {
        day: endDay,
        value: endDayMins,
      },
    ];
  }
}

export function binActiveCyclesByDayAndStat(cycles: Iterable<PartCycleData>): HashMap<DayAndStation, number> {
  return LazySeq.ofIterable(cycles)
    .flatMap((point) =>
      // point.x is the end time, point.y is elapsed in minutes, so point.x - point.y is start time
      // also, use addSeconds since addMinutes from date-fns rounds to nearest minute
      splitPartCycleToDays(addSeconds(point.x, -point.y * 60), point.x, point.activeMinutes).map((x) => ({
        ...x,
        station: stat_name_and_num(point.stationGroup, point.stationNumber),
        isLabor: point.isLabor,
      }))
    )
    .toMap(
      (p) => [new DayAndStation(p.day, p.station), p.value] as [DayAndStation, number],
      (v1, v2) => v1 + v2
    );
}

export function binOccupiedCyclesByDayAndStat(cycles: Vector<PartCycleData>): HashMap<DayAndStation, number> {
  return cycles
    .groupBy((point) => stat_name_and_num(point.stationGroup, point.stationNumber))
    .transform(LazySeq.ofIterable)
    .flatMap(([station, cyclesForStat]) =>
      chunkCyclesWithSimilarEndTime(cyclesForStat, (c) => c.x)
        .transform(LazySeq.ofIterable)
        .flatMap((chunk) =>
          // point.x is the end time, point.y is elapsed in minutes, so point.x - point.y is start time
          splitPartCycleToDays(addSeconds(chunk[0].x, -chunk[0].y * 60), chunk[0].x, chunk[0].y).map((split) => ({
            ...split,
            station: station,
            isLabor: chunk[0].isLabor,
          }))
        )
    )
    .toMap(
      (p) => [new DayAndStation(p.day, p.station), p.value] as [DayAndStation, number],
      (v1, v2) => v1 + v2
    );
}

// --------------------------------------------------------------------------------
// Planned
// --------------------------------------------------------------------------------

interface DayStatAndVal {
  day: Date;
  station: string;
  value: number;
}

function splitElapsedToDays(simUse: SimStationUse): DayStatAndVal[] {
  const startDay = startOfDay(simUse.start);
  const endDay = startOfDay(simUse.end);
  const mins = simUse.utilizationTime - simUse.plannedDownTime;

  if (startDay.getTime() === endDay.getTime()) {
    return [
      {
        day: startDay,
        station: simUse.station,
        value: mins,
      },
    ];
  } else {
    const endDayPercentage =
      (simUse.end.getTime() - endDay.getTime()) / (simUse.end.getTime() - simUse.start.getTime());
    const endDayMins = mins * Math.max(0, Math.min(endDayPercentage, 1));
    return [
      {
        day: startDay,
        station: simUse.station,
        value: mins - endDayMins,
      },
      {
        day: endDay,
        station: simUse.station,
        value: endDayMins,
      },
    ];
  }
}

export function binSimStationUseByDayAndStat(simUses: Iterable<SimStationUse>): HashMap<DayAndStation, number> {
  return LazySeq.ofIterable(simUses)
    .flatMap((s) => splitElapsedToDays(s))
    .toMap(
      (s) => [new DayAndStation(s.day, s.station), s.value] as [DayAndStation, number],
      (v1, v2) => v1 + v2
    );
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
  const filteredCycles = LazySeq.ofIterable(cycles).filter((e) => isLabor === e.isLabor && e.x >= start && e.x <= end);
  const actualBins = binActiveCyclesByDayAndStat(filteredCycles);
  const filteredStatUse = LazySeq.ofIterable(statUse).filter(
    (e) => isLabor === e.station.startsWith("L/U") && e.end >= start && e.start <= end
  );
  const plannedBins = binSimStationUseByDayAndStat(filteredStatUse);

  const series: Array<OEEBarSeries> = [];
  const statNames = actualBins
    .keySet()
    .addAll(plannedBins.keySet())
    .map((e) => e.station)
    .toArray({ sortOn: (x) => x });

  for (const stat of statNames) {
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
        day: d,
      });
    }
    series.push({
      station: stat,
      points: points,
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

export function buildOeeHeatmapTable(yTitle: string, points: ReadonlyArray<HeatmapClipboardPoint>): string {
  const cells = LazySeq.ofIterable(points).toMap(
    (p) => [new HeatmapClipboardCell(p.x.getTime(), p.y), p],
    (_, c) => c // cells should be unique, but just in case take the second
  );
  const days = LazySeq.ofIterable(points)
    .toSet((p) => p.x.getTime())
    .toArray({ sortOn: (x) => x });
  const rows = LazySeq.ofIterable(points)
    .toSet((p) => p.y)
    .toArray({ sortOn: (x) => x });

  let table = "<table>\n<thead><tr><th>" + yTitle + "</th>";
  for (const x of days) {
    table += "<th>" + new Date(x).toDateString() + "</th>";
  }
  table += "</tr></thead>\n<tbody>\n";
  for (const y of rows) {
    table += "<tr><th>" + y + "</th>";
    for (const x of days) {
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

export function copyOeeHeatmapToClipboard(yTitle: string, points: ReadonlyArray<HeatmapClipboardPoint>): void {
  copy(buildOeeHeatmapTable(yTitle, points));
}

export function buildOeeTable(series: Iterable<OEEBarSeries>) {
  let table = "<table>\n<thead><tr>";
  table += "<th>Day</th><th>Station</th><th>Actual Hours</th><th>Planned Hours</th>";
  table += "</tr></thead>\n<tbody>\n";

  for (const s of series) {
    for (const pt of s.points) {
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
