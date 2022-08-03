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

import * as api from "../network/api.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { LogBackend } from "../network/backend.js";
import { differenceInSeconds } from "date-fns";

export interface JobAndGroups {
  readonly job: Readonly<api.IActiveJob>;
  readonly machinedProcs: ReadonlyArray<{
    readonly lastProc: number;
    readonly details?: string;
    readonly queues: ReadonlySet<string>;
  }>;
}

function describePath(path: Readonly<api.IProcPathInfo>): string {
  return `${
    path.pallets.length > 1 ? "Pallets " + path.pallets.join(",") : "Pallet " + path.pallets[0]
  }; ${path.stops.map((s) => s.stationGroup + "#" + (s.stationNums ?? []).join(",")).join("->")}`;
}

interface RawMatDetails {
  readonly path: string;
  readonly queue: string | null;
}

function rawMatDetails(job: Readonly<api.IActiveJob>, pathIdx: number): RawMatDetails {
  const queue = job.procsAndPaths[0].paths[pathIdx].inputQueue;
  return {
    path: describePath(job.procsAndPaths[0].paths[pathIdx]),
    queue: queue !== undefined && queue !== "" ? queue : null,
  };
}

function joinRawMatDetails(details: ReadonlyArray<RawMatDetails>): string {
  return LazySeq.of(details).foldLeft("", (x, details) => x + " | " + details.path);
}

interface PathDetails {
  readonly path: string;
  readonly queue: string | null;
}

function pathDetails(job: Readonly<api.IActiveJob>, procIdx: number, pathIdx: number): PathDetails {
  const queue = job.procsAndPaths[0].paths[pathIdx].outputQueue;
  return {
    path: describePath(job.procsAndPaths[procIdx].paths[pathIdx]),
    queue: queue !== undefined && queue !== "" ? queue : null,
  };
}

function joinDetails(details: ReadonlyArray<PathDetails>): string {
  return LazySeq.of(details).foldLeft("", (x, details) =>
    x === "" ? details.path : x + " | " + details.path
  );
}

export function extractJobGroups(job: Readonly<api.IActiveJob>): JobAndGroups {
  const machinedProcs: {
    readonly lastProc: number;
    readonly details?: string;
    readonly queues: ReadonlySet<string>;
  }[] = [];

  // Raw material
  const rawMatPaths = job.procsAndPaths[0].paths.map((_, pathIdx) => rawMatDetails(job, pathIdx));
  machinedProcs.push({
    lastProc: 0,
    details: joinRawMatDetails(rawMatPaths),
    queues: LazySeq.of(rawMatPaths)
      .collect((p) => p.queue)
      .toRSet((p) => p),
  });

  // paths besides the final path
  for (let procIdx = 0; procIdx < job.procsAndPaths.length - 1; procIdx++) {
    const paths = job.procsAndPaths[procIdx].paths.map((_, pathIdx) => pathDetails(job, procIdx, pathIdx));
    machinedProcs.push({
      lastProc: procIdx + 1,
      details: joinDetails(paths),
      queues: LazySeq.of(paths)
        .collect((p) => p.queue)
        .toRSet((p) => p),
    });
  }

  return {
    job,
    machinedProcs,
  };
}

export interface JobRawMaterialData {
  readonly job: Readonly<api.IActiveJob>;
  readonly proc1Path: number;
  readonly path: Readonly<api.IProcPathInfo>;
  readonly pathDetails: null | string;
  readonly rawMatName: string;
  readonly plannedQty: number;
  readonly startedQty: number;
  readonly assignedRaw: number;
  readonly availableUnassigned: number;
}

function isMatDuringProc1(unique: string, proc1path: number, m: Readonly<api.IInProcessMaterial>): boolean {
  if (m.jobUnique !== unique) return false;

  if (m.location.type === api.LocType.OnPallet) {
    return m.process === 1 && m.path === proc1path;
  } else {
    return (
      m.action.type === api.ActionType.Loading &&
      m.action.processAfterLoad === 1 &&
      m.action.pathAfterLoad === proc1path
    );
  }
}

function isMatAssigned(unique: string, proc1path: number, m: Readonly<api.IInProcessMaterial>): boolean {
  return (
    m.jobUnique === unique &&
    m.location.type === api.LocType.InQueue &&
    m.action.type !== api.ActionType.Loading &&
    m.process === 0 &&
    m.path === proc1path
  );
}

export function extractJobRawMaterial(
  queue: string,
  jobs: {
    [key: string]: Readonly<api.IActiveJob>;
  },
  mats: Iterable<Readonly<api.IInProcessMaterial>>
): ReadonlyArray<JobRawMaterialData> {
  return LazySeq.ofObject(jobs)
    .filter(
      ([, j]) => LazySeq.of(j.completed?.[j.procsAndPaths.length - 1] ?? []).sumBy((x) => x) < (j.cycles ?? 0)
    )
    .flatMap(([, j]) =>
      j.procsAndPaths[0].paths
        .map((path, idx) => ({ path, pathNum: idx + 1 }))
        .filter((p) => p.path.inputQueue == queue)
        .map(({ path, pathNum }) => {
          const rawMatName = path.casting && path.casting !== "" ? path.casting : j.partName;
          return {
            job: j,
            // Eventually, once everyone has updated to a version which removes path groups and all existing jobs
            // no longer use path groups, change this to merge quantites from all paths so each job is just one row
            proc1Path: pathNum,
            path: path,
            rawMatName: rawMatName,
            pathDetails: j.procsAndPaths[0].paths.length === 1 ? null : describePath(path),
            plannedQty: j.cycles ?? 0,
            startedQty:
              (j.completed?.[0]?.[pathNum - 1] || 0) +
              LazySeq.of(mats)
                .filter((m) => isMatDuringProc1(j.unique, pathNum, m))
                .length(),
            assignedRaw: LazySeq.of(mats)
              .filter((m) => isMatAssigned(j.unique, pathNum, m))
              .length(),
            availableUnassigned: LazySeq.of(mats)
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
    .toSortedArray((x) => {
      const prec = x.job.precedence?.[0]?.[x.proc1Path - 1];
      if (!prec || prec < 0) return Number.MAX_SAFE_INTEGER;
      return prec;
    });
}

export type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

export interface QueueRawMaterialGroup {
  readonly partOrCasting: string;
  readonly assignedJobUnique: string | null;
  readonly material: MaterialList;
}

export interface QueueData {
  readonly label: string;
  readonly free: boolean;
  readonly rawMaterialQueue: boolean;
  readonly material: MaterialList;
  readonly groupedRawMat?: ReadonlyArray<QueueRawMaterialGroup>;
}

function compareByQueuePos(
  m1: Readonly<api.IInProcessMaterial>,
  m2: Readonly<api.IInProcessMaterial>
): number {
  return (m1.location.queuePosition ?? 10000000000) - (m2.location.queuePosition ?? 10000000000);
}

export function selectQueueData(
  displayFree: boolean,
  queuesToCheck: ReadonlyArray<string>,
  curSt: Readonly<api.ICurrentStatus>,
  initialRawMatQueues: ReadonlySet<string>
): ReadonlyArray<QueueData> {
  const queues: QueueData[] = [];

  const rawMatQueues = new Set(initialRawMatQueues);
  for (const [, j] of LazySeq.ofObject(curSt.jobs)) {
    for (const path of j.procsAndPaths[0].paths) {
      if (path.inputQueue && path.inputQueue !== "" && !rawMatQueues.has(path.inputQueue)) {
        rawMatQueues.add(path.inputQueue);
      }
    }
  }

  // first free and queued material
  if (displayFree) {
    queues.push({
      label: "Loading Material",
      free: true,
      rawMaterialQueue: false,
      material: curSt.material.filter(
        (m) => m.action.processAfterLoad === 1 && m.location.type === api.LocType.Free
      ),
    });
    queues.push({
      label: "In Process Material",
      free: true,
      rawMaterialQueue: false,
      material: curSt.material.filter(
        (m) =>
          m.action.processAfterLoad && m.action.processAfterLoad > 1 && m.location.type === api.LocType.Free
      ),
    });
  }

  const queueNames = [...queuesToCheck];
  queueNames.sort((a, b) => a.localeCompare(b));
  for (const queueName of queueNames) {
    const isRawMat = rawMatQueues.has(queueName);

    if (isRawMat) {
      const material: Readonly<api.IInProcessMaterial>[] = [];
      const matByPartThenUniq = new Map<string, Map<string | null, Readonly<api.IInProcessMaterial>[]>>();

      for (const m of curSt.material) {
        if (m.location.type === api.LocType.InQueue && m.location.currentQueue === queueName) {
          if ((m.serial && m.serial !== "") || m.action.type !== api.ActionType.Waiting) {
            material.push(m);
          } else {
            let matsForPart = matByPartThenUniq.get(m.partName);
            if (!matsForPart) {
              matsForPart = new Map<string | null, Readonly<api.IInProcessMaterial>[]>();
              matByPartThenUniq.set(m.partName, matsForPart);
            }

            const uniq = m.jobUnique || null;
            const matsForJob = matsForPart.get(uniq);
            if (matsForJob) {
              matsForJob.push(m);
            } else {
              matsForPart.set(uniq, [m]);
            }
          }
        }
      }

      const matGroups: QueueRawMaterialGroup[] = [];
      for (const [partName, matsForPart] of matByPartThenUniq) {
        for (const [uniq, mats] of matsForPart) {
          matGroups.push({
            partOrCasting: partName,
            assignedJobUnique: uniq,
            material: mats,
          });
        }
      }
      matGroups.sort((x, y) => {
        const c1 = x.partOrCasting.localeCompare(y.partOrCasting);
        if (c1 === 0) {
          return (x.assignedJobUnique || "").localeCompare(y.assignedJobUnique || "");
        } else {
          return c1;
        }
      });

      queues.push({
        label: queueName,
        free: false,
        rawMaterialQueue: true,
        material: material.sort(compareByQueuePos),
        groupedRawMat: matGroups,
      });
    } else {
      queues.push({
        label: queueName,
        free: false,
        rawMaterialQueue: false,
        material: curSt.material
          .filter((m) => m.location.type === api.LocType.InQueue && m.location.currentQueue === queueName)
          .sort(compareByQueuePos),
      });
    }
  }

  return queues;
}

export async function loadRawMaterialEvents(
  material: ReadonlyArray<Readonly<api.IInProcessMaterial>>
): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
  const events: Array<Readonly<api.ILogEntry>> = [];
  for (const chunk of LazySeq.of(material).chunk(15)) {
    events.push(...(await LogBackend.logForMaterials(chunk.map((m) => m.materialID))));
  }
  events.sort((a, b) => a.endUTC.getTime() - b.endUTC.getTime());

  const groupedEvents: Array<Readonly<api.ILogEntry>> = [];
  for (let i = 0; i < events.length; i++) {
    const evt = events[i];
    if (evt.type === api.LogType.AddToQueue || evt.type === api.LogType.RemoveFromQueue) {
      const material: Array<api.LogMaterial> = [...evt.material];
      while (
        i + 1 < events.length &&
        events[i + 1].type === evt.type &&
        differenceInSeconds(events[i + 1].endUTC, evt.endUTC) < 10
      ) {
        material.push(...events[i + 1].material);
        i += 1;
      }
      if (material.length === evt.material.length) {
        groupedEvents.push(evt);
      } else {
        groupedEvents.push({ ...evt, material: material });
      }
    } else {
      groupedEvents.push(events[i]);
    }
  }

  return groupedEvents;
}
