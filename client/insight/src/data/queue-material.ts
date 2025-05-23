/* Copyright (c) 2023, John Lenz

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
import { HashMap, LazySeq, OrderedSet } from "@seedtactics/immutable-collections";
import { LogBackend } from "../network/backend.js";
import { differenceInSeconds } from "date-fns";
import { useAtomValue } from "jotai";
import { currentStatus } from "../cell-status/current-status.js";
import { fmsInformation } from "../network/server-settings.js";
import { castingNames, rawMaterialQueues } from "../cell-status/names.js";
import { barcodeMaterialDetail } from "../cell-status/material-details.js";
import { useMemo } from "react";
import { last30Jobs } from "../cell-status/scheduled-jobs.js";

export type SelectableJob = {
  readonly job: Readonly<api.IJob>;
  readonly machinedProcs: ReadonlyArray<{
    readonly lastProc: number;
    readonly disabledMsg: string | null;
    readonly details?: string;
  }>;
};

export type SelectableCasting = {
  readonly casting: string;
  readonly message: string | null;
};

export type SelectableMaterialType = {
  readonly castings: ReadonlyArray<SelectableCasting>;
  readonly jobs: ReadonlyArray<SelectableJob>;
};

function describePath(path: Readonly<api.IProcPathInfo>): string {
  return `${
    path.palletNums && path.palletNums.length > 1
      ? "Pallets " + path.palletNums.map((p) => p.toString()).join(",")
      : path.palletNums && path.palletNums.length == 1
        ? "Pallet " + path.palletNums[0].toString()
        : "Pallet"
  }; ${path.stops.map((s) => s.stationGroup + "#" + (s.stationNums ?? []).join(",")).join("->")}`;
}

function extractJobGroups(
  job: Readonly<api.IActiveJob>,
  fmsInfo: Readonly<api.IFMSInfo>,
  toQueue: string,
): SelectableJob | null {
  const machinedProcs: {
    readonly lastProc: number;
    readonly disabledMsg: string | null;
    readonly details?: string;
  }[] = [];

  const matchesRawMatQueue = LazySeq.of(job.procsAndPaths?.[0].paths ?? []).some(
    (p) => p.inputQueue === toQueue,
  );

  if (matchesRawMatQueue && fmsInfo.addRawMaterial === api.AddRawMaterialType.AddAndSpecifyJob) {
    // Raw material
    machinedProcs.push({
      lastProc: 0,
      details: job.procsAndPaths[0].paths.map(describePath).join(" | "),
      disabledMsg: null,
    });
  }

  if (fmsInfo.addInProcessMaterial === api.AddInProcessMaterialType.AddAndSpecifyJob) {
    // paths besides the final path
    for (let procIdx = 0; procIdx < job.procsAndPaths.length - 1; procIdx++) {
      const matchingQueue = LazySeq.of(job.procsAndPaths[procIdx + 1].paths).some(
        (p) => p.inputQueue === toQueue,
      );
      machinedProcs.push({
        lastProc: procIdx + 1,
        details: job.procsAndPaths[procIdx].paths.map(describePath).join(" | "),
        disabledMsg: !matchingQueue ? "Material for this process is not loaded to " + toQueue : null,
      });
    }
  }

  const anyGoodPath = LazySeq.of(machinedProcs).some((p) => p.disabledMsg === null);

  if (anyGoodPath) {
    return {
      job,
      machinedProcs,
    };
  } else {
    return null;
  }
}

function workorderDetailForCasting(
  currentSt: Readonly<api.ICurrentStatus>,
  workorderId: string,
  casting: string,
): string | null {
  const partNames = LazySeq.ofObject(currentSt.jobs)
    .filter(([, j]) => j.procsAndPaths[0].paths.some((p) => p.casting === casting))
    .toHashSet(([, j]) => j.partName);

  const workorder = currentSt.workorders?.find(
    (w) => w.workorderId === workorderId && (w.part === casting || partNames.has(w.part)),
  );

  if (!workorder) return null;

  return `Started: ${workorder.material?.length ?? 0}; Planned: ${workorder.plannedQuantity}`;
}

function possibleCastings(
  currentSt: Readonly<api.ICurrentStatus>,
  historicCastingNames: ReadonlySet<string>,
  barcode: Readonly<api.IScannedMaterial> | null,
  fmsInfo: Readonly<api.IFMSInfo>,
): ReadonlyArray<SelectableCasting> {
  if (barcode?.casting?.possibleCastings && barcode.casting.possibleCastings.length > 0) {
    const workorder = barcode.casting.workorder;
    return LazySeq.of(barcode.casting.possibleCastings)
      .map((c) => ({
        casting: c,
        message: workorder ? workorderDetailForCasting(currentSt, workorder, c) : null,
      }))
      .toSortedArray((c) => c.casting);
  }

  switch (fmsInfo.addRawMaterial) {
    case api.AddRawMaterialType.AddAndSpecifyJob:
    case api.AddRawMaterialType.RequireBarcodeScan:
    case api.AddRawMaterialType.RequireExistingMaterial:
    case undefined:
      return [];

    case api.AddRawMaterialType.AddAsUnassigned:
    case api.AddRawMaterialType.AddAsUnassignedWithSerial:
      return LazySeq.ofObject(currentSt.jobs)
        .flatMap(([, j]) => j.procsAndPaths[0].paths)
        .collect((p) => (p.casting === "" ? null : p.casting))
        .concat(historicCastingNames)
        .map((c) => ({ casting: c, message: null }))
        .distinctAndSortBy((c) => c.casting)
        .toRArray();
  }
}

function possibleJobs(
  currentSt: Readonly<api.ICurrentStatus>,
  historicJobs: HashMap<string, Readonly<api.IHistoricJob>>,
  fmsInfo: Readonly<api.IFMSInfo>,
  toQueue: string,
  barcode: Readonly<api.IScannedMaterial> | null,
): ReadonlyArray<SelectableJob> {
  if (barcode?.casting?.possibleJobs && barcode.casting.possibleJobs.length > 0) {
    const possible = OrderedSet.from(barcode.casting.possibleJobs);
    return LazySeq.ofObject<Readonly<api.IJob>>(currentSt.jobs)
      .map(([, j]) => j)
      .concat(historicJobs.valuesToLazySeq())
      .filter((j) => possible.has(j.unique))
      .distinctBy((j) => j.unique)
      .map((j) => ({
        job: j,
        machinedProcs: [
          {
            lastProc: 0,
            details: j.procsAndPaths[0].paths.map(describePath).join(" | "),
            disabledMsg: null,
          },
        ],
      }))
      .toSortedArray((j) => j.job.partName);
  } else {
    return LazySeq.ofObject(currentSt.jobs)
      .collect(([, j]) => extractJobGroups(j, fmsInfo, toQueue))
      .toSortedArray((j) => j.job.partName);
  }
}

export function usePossibleNewMaterialTypes(toQueue: string | null): SelectableMaterialType {
  const historicJobs = useAtomValue(last30Jobs);
  const currentSt = useAtomValue(currentStatus);
  const fmsInfo = useAtomValue(fmsInformation);
  const castingsFromHistoric = useAtomValue(castingNames);
  const barcode = useAtomValue(barcodeMaterialDetail);
  const rawMatQueues = useAtomValue(rawMaterialQueues);

  return useMemo(() => {
    if (toQueue === null) {
      return { castings: [], jobs: [] };
    } else {
      return {
        castings: rawMatQueues.has(toQueue)
          ? possibleCastings(currentSt, castingsFromHistoric, barcode, fmsInfo)
          : [],
        jobs: possibleJobs(currentSt, historicJobs, fmsInfo, toQueue, barcode),
      };
    }
  }, [currentSt, historicJobs, fmsInfo, toQueue, castingsFromHistoric, barcode, rawMatQueues]);
}

export interface JobRawMaterialData {
  readonly job: Readonly<api.IActiveJob>;
  readonly startingTime: Date | undefined;
  readonly rawMatName: string;
  readonly remainingToStart: number;
  readonly assignedRaw: number;
  readonly availableUnassigned: number;
}

function isMatAssignedRaw(unique: string, m: Readonly<api.IInProcessMaterial>): boolean {
  return (
    m.jobUnique === unique &&
    m.location.type === api.LocType.InQueue &&
    m.action.type !== api.ActionType.Loading &&
    m.process === 0
  );
}

export function extractJobRawMaterial(
  jobs: {
    [key: string]: Readonly<api.IActiveJob>;
  },
  mats: Iterable<Readonly<api.IInProcessMaterial>>,
): ReadonlyArray<JobRawMaterialData> {
  return LazySeq.ofObject(jobs)
    .filter(([, j]) => j.remainingToStart === undefined || j.remainingToStart > 0)
    .map(([, j]) => {
      const rawMatName: string =
        LazySeq.of(j.procsAndPaths?.[0]?.paths ?? [])
          .collect((path) => (path.casting && path.casting !== "" ? path.casting : undefined))
          .head() ?? j.partName;
      return {
        job: j,
        startingTime: LazySeq.of(j.procsAndPaths?.[0]?.paths ?? [])
          .map((p) => p.simulatedStartingUTC)
          .minBy((d) => d),
        rawMatName: rawMatName,
        remainingToStart: j.remainingToStart ?? 0,
        assignedRaw: LazySeq.of(mats)
          .filter((m) => isMatAssignedRaw(j.unique, m))
          .length(),
        availableUnassigned: LazySeq.of(mats).length(),
      };
      // })
    })
    .toSortedArray((x) => {
      const prec = LazySeq.of(x.job.precedence?.[0] ?? []).minBy((p) => p);
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
  m2: Readonly<api.IInProcessMaterial>,
): number {
  return (m1.location.queuePosition ?? 10000000000) - (m2.location.queuePosition ?? 10000000000);
}

export function selectQueueData(
  queuesToCheck: ReadonlyArray<string>,
  curSt: Readonly<api.ICurrentStatus>,
  rawMatQueues: ReadonlySet<string>,
): ReadonlyArray<QueueData> {
  const queues: QueueData[] = [];

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
  material: ReadonlyArray<Readonly<api.IInProcessMaterial>>,
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
