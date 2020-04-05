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

import * as api from "./api";
import { LazySeq } from "./lazyseq";
import { Vector } from "prelude-ts";

export interface JobAndGroups {
  readonly job: Readonly<api.IInProcessJob>;
  readonly castings: ReadonlyArray<{ readonly casting: string; readonly details?: string }>;
  readonly rawMatPartDetails?: string;
  readonly machinedProcs: ReadonlyArray<{
    readonly lastProc: number;
    readonly pathGroup: number;
    readonly details?: string;
  }>;
}

function describePath(path: Readonly<api.IProcPathInfo>): string {
  return `${
    path.pallets.length > 1 ? "Pallets " + path.pallets.join(",") : "Pallet " + path.pallets[0]
  }; ${path.stops.map(s => s.stationGroup + "#" + (s.stationNums ?? []).join(",")).join("->")}`;
}

interface PathOneDetails {
  readonly planned: number;
  readonly completed: number;
  readonly path: string;
}

function pathOneDetails(job: Readonly<api.IInProcessJob>, pathIdx: number): PathOneDetails {
  return {
    planned: job.cyclesOnFirstProcess[pathIdx] || 0,
    completed: job.completed?.[0]?.[pathIdx] || 0,
    path: describePath(job.procsAndPaths[0].paths[pathIdx])
  };
}

function joinPathOneDetails(details: ReadonlyArray<PathOneDetails>): string {
  const planned = LazySeq.ofIterable(details).sumOn(d => d.planned);
  const completed = LazySeq.ofIterable(details).sumOn(d => d.completed);
  const path = LazySeq.ofIterable(details).foldLeft("", (x, details) => x + " | " + details.path);
  return `Machined ${completed}/${planned} ${path}`;
}

interface PathDetails {
  readonly completed: number;
  readonly path: string;
}

function pathDetails(job: Readonly<api.IInProcessJob>, procIdx: number, pathIdx: number): PathDetails {
  return {
    completed: job.completed?.[procIdx]?.[pathIdx] || 0,
    path: describePath(job.procsAndPaths[procIdx].paths[pathIdx])
  };
}

function joinDetails(job: Readonly<api.IInProcessJob>, pathGroup: number, details: ReadonlyArray<PathDetails>): string {
  let planned = 0;
  for (let pathIdx = 0; pathIdx < job.procsAndPaths[0].paths.length; pathIdx++) {
    if (job.procsAndPaths[0].paths[pathIdx].pathGroup === pathGroup) {
      planned += job.cyclesOnFirstProcess[pathIdx] || 0;
    }
  }
  const completed = LazySeq.ofIterable(details).sumOn(d => d.completed);
  const path = LazySeq.ofIterable(details).foldLeft("", (x, details) => x + " | " + details.path);
  return `Machined ${completed}/${planned} ${path}`;
}

export function extractJobGroups(job: Readonly<api.IInProcessJob>): JobAndGroups {
  const castings = new Map<string, PathOneDetails[]>();
  const rawMat: PathOneDetails[] = [];
  for (let pathIdx = 0; pathIdx < job.procsAndPaths[0].paths.length; pathIdx++) {
    const path = job.procsAndPaths[0].paths[pathIdx];
    if (path.casting) {
      const castingDetails = castings.get(path.casting);
      if (castingDetails) {
        castingDetails.push(pathOneDetails(job, pathIdx));
      } else {
        castings.set(path.casting, [pathOneDetails(job, pathIdx)]);
      }
    } else {
      rawMat.push(pathOneDetails(job, pathIdx));
    }
  }

  const machinedProcs: {
    readonly lastProc: number;
    readonly pathGroup: number;
    readonly details?: string;
  }[] = [];
  for (let procIdx = 0; procIdx < job.procsAndPaths.length - 1; procIdx++) {
    const groups = new Map<number, PathDetails[]>();
    for (let pathIdx = 0; pathIdx < job.procsAndPaths[procIdx].paths.length; pathIdx++) {
      const pathGroup = job.procsAndPaths[procIdx].paths[pathIdx].pathGroup;
      const group = groups.get(pathGroup);
      if (group) {
        group.push(pathDetails(job, 0, pathIdx));
      } else {
        groups.set(pathGroup, [pathDetails(job, 0, pathIdx)]);
      }
    }

    for (const [group, paths] of groups.entries()) {
      machinedProcs.push({ lastProc: procIdx + 1, pathGroup: group, details: joinDetails(job, group, paths) });
    }
  }

  return {
    job,
    castings:
      castings.size === 1 && rawMat.length === 0
        ? [
            {
              casting: LazySeq.ofIterable(castings.entries())
                .head()
                .getOrThrow()[0]
            }
          ]
        : Vector.ofIterable(castings.entries())
            .map(([casting, details]) => ({ casting: casting, details: joinPathOneDetails(details) }))
            .sortOn(c => c.casting)
            .toArray(),

    rawMatPartDetails: rawMat.length === 0 ? undefined : castings.size === 0 ? "" : joinPathOneDetails(rawMat),
    machinedProcs
  };
}
