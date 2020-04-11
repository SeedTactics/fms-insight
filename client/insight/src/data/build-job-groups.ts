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
import { HashSet } from "prelude-ts";

export interface JobAndGroups {
  readonly job: Readonly<api.IInProcessJob>;
  readonly machinedProcs: ReadonlyArray<{
    readonly lastProc: number;
    readonly pathGroup: number;
    readonly details?: string;
    readonly queues: HashSet<string>;
  }>;
}

function describePath(path: Readonly<api.IProcPathInfo>): string {
  return `${
    path.pallets.length > 1 ? "Pallets " + path.pallets.join(",") : "Pallet " + path.pallets[0]
  }; ${path.stops.map((s) => s.stationGroup + "#" + (s.stationNums ?? []).join(",")).join("->")}`;
}

interface RawMatDetails {
  readonly planned: number;
  readonly path: string;
  readonly queues: HashSet<string>;
}

function rawMatDetails(job: Readonly<api.IInProcessJob>, pathIdx: number): RawMatDetails {
  const queue = job.procsAndPaths[0].paths[pathIdx].inputQueue;
  return {
    planned: job.cyclesOnFirstProcess[pathIdx] || 0,
    path: describePath(job.procsAndPaths[0].paths[pathIdx]),
    queues: queue !== undefined && queue !== "" ? HashSet.of(queue) : HashSet.empty(),
  };
}

function joinRawMatDetails(details: ReadonlyArray<RawMatDetails>): string {
  const planned = LazySeq.ofIterable(details).sumOn((d) => d.planned);
  const path = LazySeq.ofIterable(details).foldLeft("", (x, details) => x + " | " + details.path);
  return `Plan Qty ${planned} ${path}`;
}

interface PathDetails {
  readonly path: string;
  readonly queues: HashSet<string>;
}

function pathDetails(job: Readonly<api.IInProcessJob>, procIdx: number, pathIdx: number): PathDetails {
  const queue = job.procsAndPaths[0].paths[pathIdx].outputQueue;
  return {
    path: describePath(job.procsAndPaths[procIdx].paths[pathIdx]),
    queues: queue !== undefined && queue !== "" ? HashSet.of(queue) : HashSet.empty(),
  };
}

function joinDetails(details: ReadonlyArray<PathDetails>): string {
  return LazySeq.ofIterable(details).foldLeft("", (x, details) => (x === "" ? details.path : x + " | " + details.path));
}

export function extractJobGroups(job: Readonly<api.IInProcessJob>): JobAndGroups {
  const rawMatGroups = new Map<number, RawMatDetails[]>();
  const machinedProcs: {
    readonly lastProc: number;
    readonly pathGroup: number;
    readonly details?: string;
    readonly queues: HashSet<string>;
  }[] = [];

  for (let pathIdx = 0; pathIdx < job.procsAndPaths[0].paths.length; pathIdx++) {
    const path = job.procsAndPaths[0].paths[pathIdx];
    const group = rawMatGroups.get(path.pathGroup);
    if (group) {
      group.push(rawMatDetails(job, pathIdx));
    } else {
      rawMatGroups.set(path.pathGroup, [rawMatDetails(job, pathIdx)]);
    }
  }

  for (const [group, paths] of rawMatGroups.entries()) {
    machinedProcs.push({
      lastProc: 0,
      pathGroup: group,
      details: joinRawMatDetails(paths),
      queues: LazySeq.ofIterable(paths).foldLeft(HashSet.empty(), (s, path) => s.addAll(path.queues)),
    });
  }

  for (let procIdx = 0; procIdx < job.procsAndPaths.length - 1; procIdx++) {
    const groups = new Map<number, PathDetails[]>();
    for (let pathIdx = 0; pathIdx < job.procsAndPaths[procIdx].paths.length; pathIdx++) {
      const pathGroup = job.procsAndPaths[procIdx].paths[pathIdx].pathGroup;
      const group = groups.get(pathGroup);
      if (group) {
        group.push(pathDetails(job, procIdx, pathIdx));
      } else {
        groups.set(pathGroup, [pathDetails(job, procIdx, pathIdx)]);
      }
    }

    for (const [group, paths] of groups.entries()) {
      machinedProcs.push({
        lastProc: procIdx + 1,
        pathGroup: group,
        details: joinDetails(paths),
        queues: LazySeq.ofIterable(paths).foldLeft(HashSet.empty(), (s, path) => s.addAll(path.queues)),
      });
    }
  }

  return {
    job,
    machinedProcs,
  };
}

export interface JobRawMaterialData {
  readonly job: Readonly<api.IInProcessJob>;
  readonly proc1Path: number;
  readonly path: Readonly<api.IProcPathInfo>;
  readonly pathDetails: null | string;
  readonly rawMatName: string;
  readonly plannedQty: number;
  readonly startedQty: number;
  readonly assignedRaw: number;
  readonly availableUnassigned: number;
}

export function extractJobRawMaterial(
  queue: string,
  jobs: {
    [key: string]: Readonly<api.IInProcessJob>;
  },
  mats: Iterable<Readonly<api.IInProcessMaterial>>
): ReadonlyArray<JobRawMaterialData> {
  return LazySeq.ofObject(jobs)
    .flatMap(([, j]) =>
      j.procsAndPaths[0].paths
        .filter((p) => p.inputQueue == queue)
        .map((path, idx) => {
          const rawMatName = path.casting && path.casting !== "" ? path.casting : j.partName;
          return {
            job: j,
            proc1Path: idx,
            path: path,
            rawMatName: rawMatName,
            pathDetails: j.procsAndPaths[0].paths.length === 1 ? null : describePath(path),
            plannedQty: j.cyclesOnFirstProcess[idx],
            startedQty:
              (j.completed?.[0]?.[idx] || 0) +
              LazySeq.ofIterable(mats)
                .filter(
                  (m) =>
                    (m.location.type !== api.LocType.InQueue ||
                      (m.location.type === api.LocType.InQueue && m.location.currentQueue !== queue)) &&
                    m.jobUnique === j.unique &&
                    m.process === 1 &&
                    m.path === idx + 1
                )
                .length(),
            assignedRaw: LazySeq.ofIterable(mats)
              .filter(
                (m) =>
                  m.location.type === api.LocType.InQueue &&
                  m.location.currentQueue === queue &&
                  m.jobUnique === j.unique &&
                  m.process === 0 &&
                  m.path === idx + 1
              )
              .length(),
            availableUnassigned: LazySeq.ofIterable(mats)
              .filter(
                (m) =>
                  m.location.type === api.LocType.InQueue &&
                  m.location.currentQueue === queue &&
                  (!m.jobUnique || m.jobUnique === "") &&
                  m.process === 0 &&
                  m.partName === rawMatName
              )
              .length(),
          };
        })
    )
    .toArray();
}
