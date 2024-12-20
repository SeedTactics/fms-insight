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

import { startOfDay, addSeconds, addDays, max, min, differenceInMinutes } from "date-fns";
import copy from "copy-to-clipboard";
import { SimStationUse } from "../cell-status/sim-station-use.js";
import { chunkCyclesWithSimilarEndTime } from "../cell-status/estimated-cycle-times.js";
import { PartCycleData, stat_name_and_num } from "../cell-status/station-cycles.js";
import { HashMap, hashValues, LazySeq } from "@seedtactics/immutable-collections";

// --------------------------------------------------------------------------------
// Actual
// --------------------------------------------------------------------------------

export class DayAndStation {
  constructor(
    public readonly day: Date,
    public readonly station: string,
  ) {}
  compare(other: DayAndStation): number {
    const cmp = this.day.getTime() - other.day.getTime();
    if (cmp !== 0) return cmp;
    return this.station.localeCompare(other.station);
  }
  hash(): number {
    return hashValues(this.day, this.station);
  }
  toString(): string {
    return `{day: ${this.day.toISOString()}}, station: ${this.station}}`;
  }
  adjustDay(f: (d: Date) => Date): DayAndStation {
    return new DayAndStation(f(this.day), this.station);
  }
}

function splitTimeToDays(
  startTime: Date,
  endTime: Date,
  mins: number,
): ReadonlyArray<{ day: Date; value: number }> {
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
    const vals: Array<{ day: Date; value: number }> = [];
    const totalRange = endTime.getTime() - startTime.getTime();
    for (let day = startDay; day <= endDay; day = addDays(day, 1)) {
      const s = max([day, startTime]);
      const e = min([addDays(day, 1), endTime]);
      const pct = (e.getTime() - s.getTime()) / totalRange;

      vals.push({
        day,
        value: mins * pct,
      });
    }
    return vals;
  }
}

function binCycles(
  cycles: Iterable<PartCycleData>,
  f: (c: PartCycleData) => number,
): HashMap<DayAndStation, number> {
  return chunkCyclesWithSimilarEndTime(
    LazySeq.of(cycles),
    (c) => stat_name_and_num(c.stationGroup, c.stationNumber),
    (c) => c.endTime,
  )
    .flatMap(([statNameAndNum, cyclesForStat]) =>
      cyclesForStat
        .flatMap((chunk) => {
          // sum elapsed time for chunk and subtract from end time to get start time
          const elapForChunkMins = LazySeq.of(chunk).sumBy(
            (c) => c.elapsedMinsPerMaterial * c.material.length,
          );
          const startTime = addSeconds(chunk[0].endTime, -elapForChunkMins * 60);

          // sum value for chunk
          const valForChunk = LazySeq.of(chunk).sumBy((c) => f(c));

          return splitTimeToDays(startTime, chunk[0].endTime, valForChunk);
        })
        .map((s) => ({
          ...s,
          statNameAndNum,
        })),
    )
    .buildHashMap<DayAndStation, number>(
      (p) => new DayAndStation(p.day, p.statNameAndNum),
      (old, p) => p.value + (old ?? 0),
    );
}

export function binActiveCyclesByDayAndStat(cycles: Iterable<PartCycleData>): HashMap<DayAndStation, number> {
  return binCycles(cycles, (c) => c.activeMinutes);
}

export function binOccupiedCyclesByDayAndStat(
  cycles: Iterable<PartCycleData>,
): HashMap<DayAndStation, number> {
  return binCycles(cycles, (c) => c.elapsedMinsPerMaterial * c.material.length);
}

// --------------------------------------------------------------------------------
// Planned
// --------------------------------------------------------------------------------

function mergeSortedIntervals(
  intervals: Iterable<{ start: Date; end: Date }>,
): Array<{ start: Date; end: Date }> {
  const merged: Array<{ start: Date; end: Date }> = [];
  for (const i of intervals) {
    if (merged.length === 0) {
      merged.push(i);
    } else {
      const last = merged[merged.length - 1];
      if (i.start <= last.end) {
        merged[merged.length - 1] = {
          start: last.start,
          end: max([last.end, i.end]),
        };
      } else {
        merged.push(i);
      }
    }
  }
  return merged;
}

export function binDowntimeToDayAndStat(simUses: Iterable<SimStationUse>): HashMap<DayAndStation, number> {
  return LazySeq.of(simUses)
    .filter((simUse) => simUse.plannedDown)
    .toLookupOrderedMap(
      (simUse) => simUse.station,
      (simUse) => simUse.start,
    )
    .toAscLazySeq()
    .flatMap(([station, uses]) =>
      mergeSortedIntervals(uses.valuesToAscLazySeq()).flatMap((simUse) =>
        splitTimeToDays(simUse.start, simUse.end, differenceInMinutes(simUse.end, simUse.start)).map((x) => ({
          ...x,
          station: station,
        })),
      ),
    )
    .toHashMap(
      (s) => [new DayAndStation(s.day, s.station), s.value] as [DayAndStation, number],
      (v1, v2) => v1 + v2,
    );
}

export function binSimStationUseByDayAndStat(
  simUses: Iterable<SimStationUse>,
): HashMap<DayAndStation, number> {
  const downtimes = LazySeq.of(simUses)
    .filter((simUse) => simUse.plannedDown)
    .toLookupOrderedMap(
      (simUse) => simUse.station,
      (simUse) => simUse.start,
    )
    .mapValues((uses) => mergeSortedIntervals(uses.valuesToAscLazySeq()));

  return LazySeq.of(simUses)
    .filter((simUse) => !simUse.plannedDown)
    .toLookupOrderedMap(
      (simUse) => simUse.station,
      (simUse) => simUse.start,
    )
    .toAscLazySeq()
    .flatMap(([station, uses]) => {
      const sorted = mergeSortedIntervals(uses.valuesToAscLazySeq());
      const down = downtimes.get(station) ?? [];

      for (let downIdx = 0, sortedIdx = 0; sortedIdx < sorted.length; ) {
        if (downIdx < down.length && down[downIdx].end <= sorted[sortedIdx].start) {
          downIdx++;
        } else if (downIdx < down.length && down[downIdx].start < sorted[sortedIdx].end) {
          // Downtime overlaps the interval, need to split
          // Several cases, depending on the start and end of the intervals
          const simStart = sorted[sortedIdx].start;
          const simEnd = sorted[sortedIdx].end;
          const downStart = down[downIdx].start;
          const downEnd = down[downIdx].end;

          if (downStart <= simStart && downEnd >= simEnd) {
            // down: |-----------|
            // sim:     |-----|
            // downtime removes the whole chunk
            sorted.splice(sortedIdx, 1);
            // don't increment sortedIdx, since we removed it
            // don't increment downIdx since a future sim might overlap
          } else if (downStart <= simStart) {
            // down: |-----------|
            // sim:     |--------------|
            sorted[sortedIdx] = {
              start: downEnd,
              end: simEnd,
            };
            downIdx += 1;
            // don't increment simIdx, since a future downtime might overlap and so still need to check
          } else if (downEnd >= simEnd) {
            // down:    |-----------|
            // sim:  |----------|
            sorted[sortedIdx] = {
              start: simStart,
              end: downStart,
            };
            sortedIdx += 1;
          } else {
            // down:    |-----------|
            // sim:  |---------------------|
            sorted[sortedIdx] = {
              start: simStart,
              end: downStart,
            };
            sorted.splice(sortedIdx + 1, 0, { start: downEnd, end: simEnd });
            downIdx++; // increment downIdx since we used this downtime

            // increment simIdx only by 1 so that we check the remaining end of the sim time
            // we just split
            sortedIdx++;
          }
        } else {
          // No downtimes overlapping, don't need to edit
          sortedIdx++;
        }
      }

      return sorted.flatMap((simUse) =>
        splitTimeToDays(simUse.start, simUse.end, differenceInMinutes(simUse.end, simUse.start)).map((x) => ({
          ...x,
          station: station,
        })),
      );
    })
    .toHashMap(
      (s) => [new DayAndStation(s.day, s.station), s.value] as const,
      (v1, v2) => v1 + v2,
    );
}

// --------------------------------------------------------------------------------
// Combined
// --------------------------------------------------------------------------------

export interface OEEBarPoint {
  readonly x: string;
  readonly y: number;
  readonly plannedHours: number;
  readonly plannedOee: number;
  readonly actualOee: number;
  readonly station: string;
  readonly day: Date;
}

export interface OEEBarSeries {
  readonly station: string;
  readonly points: ReadonlyArray<OEEBarPoint>;
}

export type OEEType = "labor" | "machine";

export function buildOeeSeries(
  start: Date,
  end: Date,
  ty: OEEType,
  cycles: Iterable<PartCycleData>,
  statUse: Iterable<SimStationUse>,
): ReadonlyArray<OEEBarSeries> {
  const filteredCycles = LazySeq.of(cycles).filter(
    (e) => (ty === "labor") === e.isLabor && e.endTime >= start && e.endTime <= end,
  );
  const actualBins = binActiveCyclesByDayAndStat(filteredCycles);
  const filteredStatUse = LazySeq.of(statUse).filter(
    (e) => (ty === "labor") === e.station.startsWith("L/U") && e.end >= start && e.start <= end,
  );
  const downtimes = binDowntimeToDayAndStat(filteredStatUse);
  const plannedBins = binSimStationUseByDayAndStat(filteredStatUse);

  const series: Array<OEEBarSeries> = [];
  const statNames = actualBins
    .keysToLazySeq()
    .concat(plannedBins.keysToLazySeq())
    .map((e) => e.station)
    .distinct()
    .toSortedArray((x) => x);

  for (const stat of statNames) {
    const points: Array<OEEBarPoint> = [];
    for (let d = start; d < end; d = addDays(d, 1)) {
      const dAndStat = new DayAndStation(d, stat);
      const actual = actualBins.get(dAndStat);
      const planned = plannedBins.get(dAndStat);
      const down = downtimes.get(dAndStat) ?? 0;
      points.push({
        x: d.toLocaleDateString(),
        y: (actual ?? 0) / 60,
        plannedHours: (planned ?? 0) / 60,
        actualOee: down < 24 * 60 ? (actual ?? 0) / (24 * 60 - down) : 0,
        plannedOee: down < 24 * 60 ? (planned ?? 0) / (24 * 60 - down) : 0,
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
  public constructor(
    public readonly x: number,
    public readonly y: string,
  ) {}
  compare(other: HeatmapClipboardCell): number {
    const cmp = this.x - other.x;
    if (cmp !== 0) return cmp;
    return this.y.localeCompare(other.y);
  }
  hash(): number {
    return hashValues(this.x, this.y);
  }
  toString(): string {
    return `{x: ${new Date(this.x).toISOString()}, y: ${this.y}}`;
  }
}

export function buildOeeHeatmapTable(yTitle: string, points: ReadonlyArray<HeatmapClipboardPoint>): string {
  const cells = LazySeq.of(points).toHashMap(
    (p) => [new HeatmapClipboardCell(p.x.getTime(), p.y), p],
    (_, c) => c, // cells should be unique, but just in case take the second
  );
  const days = LazySeq.of(points)
    .map((p) => p.x.getTime())
    .distinct()
    .toSortedArray((x) => x);
  const rows = LazySeq.of(points)
    .map((p) => p.y)
    .distinct()
    .toSortedArray((x) => x);

  let table = "<table>\n<thead><tr><th>" + yTitle + "</th>";
  for (const x of days) {
    table += "<th>" + new Date(x).toDateString() + "</th>";
  }
  table += "</tr></thead>\n<tbody>\n";
  for (const y of rows) {
    table += "<tr><th>" + y + "</th>";
    for (const x of days) {
      const cell = cells.get(new HeatmapClipboardCell(x, y));
      if (cell !== undefined) {
        table += "<td>" + cell.label + "</td>";
      } else {
        table += "<td></td>";
      }
    }
    table += "</tr>\n";
  }
  table += "</tbody>\n</table>";
  return table;
}

export function copyOeeHeatmapToClipboard(
  yTitle: string,
  points: ReadonlyArray<HeatmapClipboardPoint>,
): void {
  copy(buildOeeHeatmapTable(yTitle, points));
}

export function buildOeeTable(series: Iterable<OEEBarSeries>): string {
  let table = "<table>\n<thead><tr>";
  table += "<th>Day</th><th>Station</th><th>Actual Hours</th><th>Planned Hours</th>";
  table += "</tr></thead>\n<tbody>\n";

  for (const s of series) {
    for (const pt of s.points) {
      table += "<tr>";
      table += "<td>" + pt.day.toLocaleDateString() + "</td>";
      table += "<td>" + pt.station + "</td>";
      table += "<td>" + pt.y.toFixed(1) + "</td>";
      table += "<td>" + pt.plannedHours.toFixed(1) + "</td>";
      table += "</tr>\n";
    }
  }

  table += "</tbody>\n</table>";
  return table;
}

export function copyOeeToClipboard(series: Iterable<OEEBarSeries>): void {
  copy(buildOeeTable(series));
}
