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

import { BufferEntry } from "./events.buffering";
import { Vector, fieldsHashCode, HashSet } from "prelude-ts";

export interface BufferChartPoint {
  readonly x: Date;
  readonly y: number;
}

export interface BufferChartSeries {
  readonly label: string;
  readonly points: ReadonlyArray<BufferChartPoint>;
}

type BufferSeriesType =
  | { readonly type: "Rotary"; readonly machineGroup: string; readonly machineNum: number }
  | { readonly type: "StockerWaitForMC" }
  | { readonly type: "StockerWaitForUnload" }
  | { readonly type: "Queue"; readonly queue: string };

class BufferSeriesKey {
  public constructor(public readonly ty: BufferSeriesType) {}
  equals(other: BufferSeriesKey): boolean {
    switch (this.ty.type) {
      case "Queue":
        return other.ty.type === "Queue" && this.ty.queue === other.ty.queue;
      case "Rotary":
        return (
          other.ty.type === "Rotary" &&
          this.ty.machineGroup === other.ty.machineGroup &&
          this.ty.machineNum === other.ty.machineNum
        );
      case "StockerWaitForMC":
        return other.ty.type === "StockerWaitForMC";
      case "StockerWaitForUnload":
        return other.ty.type === "StockerWaitForUnload";
    }
  }
  hashCode(): number {
    switch (this.ty.type) {
      case "Queue":
        return fieldsHashCode("Queue", this.ty.queue, 0);
      case "Rotary":
        return fieldsHashCode("Rotary", this.ty.machineGroup, this.ty.machineNum);
      case "StockerWaitForMC":
        return fieldsHashCode("StockerWaitForMC", "", 0);
      case "StockerWaitForUnload":
        return fieldsHashCode("StockerWaitForUnload", "", 0);
    }
  }
  toString(): string {
    switch (this.ty.type) {
      case "Queue":
        return this.ty.queue;
      case "Rotary":
        return "Rotary " + this.ty.machineGroup + " #" + this.ty.machineNum.toString();
      case "StockerWaitForMC":
        return "Stocker[Waiting For Machining]";
      case "StockerWaitForUnload":
        return "Stocker[Waiting For Unload]";
    }
  }
}

const numPoints: number = 30 * 5;

function addEntryToPoint(
  movingAverageDistanceInMilliseconds: number,
  point: { x: Date; y: number },
  entry: BufferEntry
) {
  const startT = Math.max(
    point.x.getTime() - movingAverageDistanceInMilliseconds,
    entry.endTime.getTime() - entry.elapsedSeconds * 1000
  );
  const endT = Math.min(point.x.getTime() + movingAverageDistanceInMilliseconds, entry.endTime.getTime());

  point.y += ((endT - startT) / (2 * movingAverageDistanceInMilliseconds)) * entry.numMaterial;
}

function calcPoints(
  absoluteStart: Date,
  absoluteEnd: Date,
  movingAverageDistanceInMilliseconds: number,
  entries: Iterable<BufferEntry>
): ReadonlyArray<BufferChartPoint> {
  // the actual start and end is inward from the start and end by the moving average distance
  const start = new Date(absoluteStart.getTime() + movingAverageDistanceInMilliseconds);
  const end = new Date(absoluteEnd.getTime() - movingAverageDistanceInMilliseconds);
  const gap = (end.getTime() - start.getTime()) / (numPoints - 1);

  const points: { x: Date; y: number }[] = [];

  // initialize all points to 0
  for (let i = 0; i < numPoints - 1; i++) {
    points.push({
      x: new Date(start.getTime() + i * gap),
      y: 0,
    });
  }
  points.push({ x: end, y: 0 });

  // add times
  for (const e of entries) {
    const startIdx = Math.max(
      0,
      Math.ceil(
        (e.endTime.getTime() - e.elapsedSeconds * 1000 - movingAverageDistanceInMilliseconds - start.getTime()) / gap
      )
    );
    const endIdx = Math.min(
      numPoints - 1,
      Math.floor((e.endTime.getTime() + movingAverageDistanceInMilliseconds - start.getTime()) / gap)
    );
    for (let i = startIdx; i <= endIdx; i++) {
      addEntryToPoint(movingAverageDistanceInMilliseconds, points[i], e);
    }
  }

  return points;
}

export function buildBufferChart(
  start: Date,
  end: Date,
  movingAverageDistanceInHours: number,
  rawMatQueues: HashSet<string>,
  entries: Vector<BufferEntry>
): ReadonlyArray<BufferChartSeries> {
  const movingAverageDistanceInMilliseconds = movingAverageDistanceInHours * 60 * 60 * 1000;
  return entries
    .groupBy((v) => new BufferSeriesKey(v.buffer))
    .mapValues((es) => calcPoints(start, end, movingAverageDistanceInMilliseconds, es))
    .toArray()
    .filter(([k]) => k.ty.type !== "Queue" || !rawMatQueues.contains(k.ty.queue))
    .map(([k, points]) => ({
      label: k.toString(),
      points,
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
}
