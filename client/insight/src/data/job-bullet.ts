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

import * as api from "../network/api";
import { durationToSeconds } from "../util/parseISODuration";
import { LazySeq } from "../util/lazyseq";

export interface DataPoint {
  readonly part: string;
  readonly completed: number;
  readonly completedCount: number;
  readonly totalPlan: number;
  readonly totalCount: number;
  readonly workorders: string;
}

function displayJob(job: api.IActiveJob, proc: number): DataPoint {
  const totalPlan = job.cycles ?? 0;
  const completed = job.completed ? job.completed[proc].reduce((a, b) => a + b, 0) : 0;

  const stops = job.procsAndPaths[proc].paths[0].stops;
  let cycleTimeSeconds = 0;
  for (let i = 0; i < stops.length; i++) {
    cycleTimeSeconds += durationToSeconds(stops[i].expectedCycleTime);
  }
  const cycleTimeHours = cycleTimeSeconds / (60 * 60) / job.procsAndPaths[proc].paths[0].partsPerPallet;
  return {
    part: job.partName + "-" + (proc + 1).toString(),
    completed: cycleTimeHours * completed,
    completedCount: completed,
    totalPlan: cycleTimeHours * totalPlan,
    totalCount: totalPlan,
    workorders: (job.assignedWorkorders ?? []).join(", "),
  };
}

export interface IndexedDataPoint extends DataPoint {
  readonly idx: number;
}

export interface DataPoints {
  readonly jobs: ReadonlyArray<IndexedDataPoint>;
  readonly longestPartName: number;
}

export function jobsToPoints(jobs: ReadonlyArray<Readonly<api.IActiveJob>>): DataPoints {
  const points = jobs
    .toLazySeq()
    .sort(
      (j) => j.routeStartUTC.getTime(),
      (j) => j.scheduleId ?? "",
      (j) => j.partName
    )
    .flatMap((j) => LazySeq.ofRange(0, j.procsAndPaths.length).map((proc) => displayJob(j, proc)))
    .toRArray();
  const jobPoints = points.map((pt, i) => ({ ...pt, idx: points.length - i - 1 }));
  let longestPartName = 1;
  for (const p of jobPoints) {
    if (p.part.length > longestPartName) {
      longestPartName = p.part.length;
    }
  }
  return { jobs: jobPoints, longestPartName };
}
