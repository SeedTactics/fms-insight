/* Copyright (c) 2018, John Lenz

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
import { duration } from "moment";
import { Vector, Option } from "prelude-ts";

export interface DataPoint {
  readonly part: string;
  readonly completed: number;
  readonly completedCount: number;
  readonly totalPlan: number;
  readonly totalCount: number;
}

function displayJob(job: api.IInProcessJob, proc: number): DataPoint {
  const totalPlan = job.cyclesOnFirstProcess.reduce((a, b) => a + b, 0);
  const completed = job.completed ? job.completed[proc].reduce((a, b) => a + b, 0) : 0;

  const stops = job.procsAndPaths[proc].paths[0].stops;
  let cycleTime = duration();
  for (let i = 0; i < stops.length; i++) {
    const x = duration(stops[i].expectedCycleTime);
    cycleTime = cycleTime.add(x);
  }
  const cycleTimeHours = cycleTime.asHours();
  return {
    part: job.partName + "-" + (proc + 1).toString(),
    completed: cycleTimeHours * completed,
    completedCount: completed,
    totalPlan: cycleTimeHours * totalPlan,
    totalCount: totalPlan,
  };
}

export interface CompletedDataPoint extends DataPoint {
  readonly x: number;
  readonly y: number;
}

export interface DataPoints {
  readonly completedData: ReadonlyArray<CompletedDataPoint>;
  readonly planData: ReadonlyArray<{ x: number; y: number }>;
  readonly longestPartName: number;
}

function vectorRange(start: number, count: number): Vector<number> {
  return Vector.unfoldRight(start, (x) =>
    x - start < count ? Option.of([x, x + 1] as [number, number]) : Option.none<[number, number]>()
  );
}

export function jobsToPoints(jobs: ReadonlyArray<Readonly<api.IInProcessJob>>): DataPoints {
  const points = Vector.ofIterable(jobs)
    .flatMap((j) => vectorRange(0, j.procsAndPaths.length).map((proc) => displayJob(j, proc)))
    .sortOn((pt) => pt.part)
    .reverse();
  const completedData = points
    .zipWithIndex()
    .map(([pt, i]) => ({ ...pt, x: pt.completed, y: i }))
    .toArray();
  const planData = points
    .zipWithIndex()
    .map(([pt, i]) => ({ x: pt.totalPlan, y: i }))
    .toArray();
  const longestPartName = points
    .maxOn((p) => p.part.length)
    .map((p) => p.part.length)
    .getOrElse(1);
  return { completedData, planData, longestPartName };
}
