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
import { HashMap, LazySeq } from "@seedtactics/immutable-collections";
import { LogBackend } from "../network/backend.js";
import { differenceInSeconds } from "date-fns";
import { useAtomValue } from "jotai";
import { currentStatus } from "../cell-status/current-status.js";
import { rawMaterialQueues } from "../cell-status/names.js";
import { barcodePotentialNewMaterial } from "../cell-status/material-details.js";
import { useMemo } from "react";
import { last30Jobs } from "../cell-status/scheduled-jobs.js";
import { atomWithStorage } from "jotai/utils";

export type SelectableJob = {
  readonly jobUnique: string;
  readonly job: Readonly<api.IJob> | null;
  readonly workorder: string | null;
  readonly machinedProcs: ReadonlyArray<{
    readonly lastProc: number;
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

export const hideNonLoadingMaterialOnLoadStation = atomWithStorage(
  "fmsinsight.hide-nonloading-loadstation",
  false,
);

function describePath(path: Readonly<api.IProcPathInfo>): string {
  return `${
    path.palletNums && path.palletNums.length > 1
      ? "Pallets " + path.palletNums.map((p) => p.toString()).join(",")
      : path.palletNums && path.palletNums.length == 1
        ? "Pallet " + path.palletNums[0].toString()
        : "Pallet"
  }; ${path.stops.map((s) => s.stationGroup + "#" + (s.stationNums ?? []).join(",")).join("->")}`;
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
  barcode: Readonly<api.IScannedPotentialNewMaterial> | null,
  toQueue: string,
): ReadonlyArray<SelectableCasting> {
  const workorder = barcode?.workorder;
  return LazySeq.of(barcode?.possibleCastingsByQueue?.[toQueue] ?? [])
    .map((c) => ({
      casting: c,
      message: workorder ? workorderDetailForCasting(currentSt, workorder, c) : null,
    }))
    .toSortedArray((c) => c.casting);
}

function possibleJobs(
  currentSt: Readonly<api.ICurrentStatus>,
  historicJobs: HashMap<string, Readonly<api.IHistoricJob>>,
  barcode: Readonly<api.IScannedPotentialNewMaterial> | null,
  toQueue: string,
): ReadonlyArray<SelectableJob> {
  return LazySeq.of(barcode?.possibleJobsByQueue?.[toQueue] ?? [])
    .groupBy((j) => j.jobUnique)
    .collect(([uniq, procs]) => {
      const job = currentSt.jobs[uniq] ?? historicJobs.get(uniq);
      return {
        jobUnique: uniq,
        job: job ?? null,
        workorder:
          LazySeq.of(procs)
            .collect((p) => p.workorder)
            .head() ?? null,
        machinedProcs: procs.map((proc) => ({
          lastProc: proc.lastCompletedProcess,
          details:
            job?.procsAndPaths[proc.lastCompletedProcess].paths.map(describePath).join(" | ") ?? "",
        })),
      };
    })
    .toSortedArray((j) => j.job?.partName ?? j.jobUnique);
}

export function usePossibleNewMaterialTypes(toQueue: string | null): SelectableMaterialType {
  const historicJobs = useAtomValue(last30Jobs);
  const currentSt = useAtomValue(currentStatus);
  const barcode = useAtomValue(barcodePotentialNewMaterial);
  const rawMatQueues = useAtomValue(rawMaterialQueues);

  return useMemo(() => {
    if (barcode === null || toQueue === null) {
      return { castings: [], jobs: [] };
    } else {
      return {
        castings: rawMatQueues.has(toQueue) ? possibleCastings(currentSt, barcode, toQueue) : [],
        jobs: possibleJobs(currentSt, historicJobs, barcode, toQueue),
      };
    }
  }, [currentSt, historicJobs, toQueue, barcode, rawMatQueues]);
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
    m.action.type !== api.ActionType.LoadingToBasket &&
    m.process === 0
  );
}

function isMatUnassignedRaw(rawMatName: string, m: Readonly<api.IInProcessMaterial>): boolean {
  return (
    m.partName === rawMatName &&
    m.location.type === api.LocType.InQueue &&
    m.action.type !== api.ActionType.Loading &&
    m.action.type !== api.ActionType.LoadingToBasket &&
    m.process === 0 &&
    !m.jobUnique
  );
}

export function extractJobRawMaterial(
  jobs: {
    [key: string]: Readonly<api.IActiveJob>;
  },
  mats: Iterable<Readonly<api.IInProcessMaterial>>,
  queue: string,
): ReadonlyArray<JobRawMaterialData> {
  return LazySeq.ofObject(jobs)
    .filter(([, j]) => j.remainingToStart === undefined || j.remainingToStart > 0)
    .collect(([, j]) => {
      const paths = (j.procsAndPaths?.[0]?.paths ?? []).filter((p) => p.inputQueue === queue);
      if (paths.length === 0) {
        return null;
      }

      const rawMatName: string =
        LazySeq.of(paths)
          .collect((path) => (path.casting && path.casting !== "" ? path.casting : undefined))
          .head() ?? j.partName;
      return {
        job: j,
        startingTime: LazySeq.of(paths)
          .map((p) => p.simulatedStartingUTC)
          .minBy((d) => d),
        rawMatName: rawMatName,
        remainingToStart: j.remainingToStart ?? 0,
        assignedRaw: LazySeq.of(mats)
          .filter((m) => isMatAssignedRaw(j.unique, m))
          .length(),
        availableUnassigned: LazySeq.of(mats)
          .filter((m) => isMatUnassignedRaw(rawMatName, m))
          .length(),
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

function hasSerial(m: Readonly<api.IInProcessMaterial>): boolean {
  return m.serial !== undefined && m.serial !== null && m.serial !== "";
}

function interchangeableKey(m: Readonly<api.IInProcessMaterial>): string {
  return [
    m.partName,
    m.jobUnique,
    m.process,
    m.action.type,
    m.action.loadOntoPalletNum,
    m.action.loadOntoFace,
    m.action.loadToBasketId,
    m.action.loadToBasketSlot,
    m.action.processAfterLoad,
    m.action.pathAfterLoad,
  ].join("|");
}

// Material without a serial is interchangeable with others of the same part, job, process, and
// destination, so it is displayed as one card with a count.  Groups the material selected by
// canGroup in order of first appearance; other material is returned individually.
export function groupInterchangeableMaterial(
  material: MaterialList,
  canGroup: (m: Readonly<api.IInProcessMaterial>) => boolean,
): ReadonlyArray<MaterialList> {
  const groupKey = (m: Readonly<api.IInProcessMaterial>): string | null =>
    canGroup(m) && !hasSerial(m) ? interchangeableKey(m) : null;
  const groups = LazySeq.of(material)
    .collect((m) => {
      const key = groupKey(m);
      return key === null ? null : ([key, m] as const);
    })
    .groupBy(([key]) => key)
    .toHashMap(([key, entries]) => [key, entries.map(([, m]) => m)]);
  const seen = new Set<string>();
  return material.flatMap((m) => {
    const key = groupKey(m);
    if (key === null) return [[m]];
    if (seen.has(key)) return [];
    seen.add(key);
    return [groups.get(key) ?? [m]];
  });
}

export interface QueueRawMaterialGroup {
  readonly partOrCasting: string;
  readonly assignedJobUnique: string | null;
  readonly material: MaterialList;
}

export interface QueueData {
  readonly label: string;
  readonly free: boolean;
  readonly rawMaterialQueue: boolean;
  readonly inProcQueue: boolean;
  readonly material: MaterialList;
  readonly groupedRawMat?: ReadonlyArray<QueueRawMaterialGroup>;
}

export function selectQueueData(
  queuesToCheck: ReadonlyArray<string>,
  curSt: Readonly<api.ICurrentStatus>,
  rawMatQueues: ReadonlySet<string>,
  inProcQueues: ReadonlySet<string>,
): ReadonlyArray<QueueData> {
  const queues: QueueData[] = [];

  const queueNames = [...queuesToCheck];
  queueNames.sort((a, b) => a.localeCompare(b));
  for (const queueName of queueNames) {
    const isRawMat = rawMatQueues.has(queueName);

    if (isRawMat) {
      const inQueue = LazySeq.of(curSt.material)
        .filter(
          (m) =>
            m.location.type === api.LocType.InQueue &&
            m.location.currentQueue === queueName &&
            m.location.queuePosition !== undefined,
        )
        .toSortedArray((m) => m.location.queuePosition ?? 0);
      // material with a serial or an action keeps its own card and queue position
      const waitingWithoutSerial = (m: Readonly<api.IInProcessMaterial>) =>
        !hasSerial(m) && m.action.type === api.ActionType.Waiting;

      const matGroups = LazySeq.of(
        groupInterchangeableMaterial(inQueue.filter(waitingWithoutSerial), () => true),
      ).toSortedArray(
        (g) => g[0].partName,
        (g) => g[0].jobUnique ?? "",
      );

      queues.push({
        label: queueName,
        free: false,
        rawMaterialQueue: true,
        inProcQueue: false,
        material: inQueue.filter((m) => !waitingWithoutSerial(m)),
        groupedRawMat: matGroups.map((material) => ({
          partOrCasting: material[0].partName,
          assignedJobUnique: material[0].jobUnique || null,
          material,
        })),
      });
    } else {
      queues.push({
        label: queueName,
        free: false,
        rawMaterialQueue: false,
        inProcQueue: inProcQueues.has(queueName),
        material: LazySeq.of(curSt.material)
          .filter(
            (m) =>
              m.location.type === api.LocType.InQueue &&
              m.location.currentQueue === queueName &&
              m.location.queuePosition !== undefined,
          )
          .toSortedArray((m) => m.location.queuePosition ?? 0),
      });
    }
  }

  return queues;
}

export async function loadRawMaterialEvents(
  materials: ReadonlyArray<Readonly<api.IInProcessMaterial>>,
): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
  const events: Array<Readonly<api.ILogEntry>> = [];
  for (const chunk of LazySeq.of(materials).chunk(15)) {
    events.push(...(await LogBackend.logForMaterials(chunk.map((m) => m.materialID))));
  }
  events.sort((a, b) => a.endUTC.getTime() - b.endUTC.getTime());

  const groupedEvents: Array<Readonly<api.ILogEntry>> = [];
  for (let i = 0; i < events.length; i++) {
    const evt = events[i];
    if (evt.type === api.LogType.AddToQueue || evt.type === api.LogType.RemoveFromQueue) {
      const groupedMaterial: Array<api.LogMaterial> = [...evt.material];
      while (
        i + 1 < events.length &&
        events[i + 1].type === evt.type &&
        differenceInSeconds(events[i + 1].endUTC, evt.endUTC) < 10
      ) {
        groupedMaterial.push(...events[i + 1].material);
        i += 1;
      }
      if (groupedMaterial.length === evt.material.length) {
        groupedEvents.push(evt);
      } else {
        groupedEvents.push({ ...evt, material: groupedMaterial });
      }
    } else {
      groupedEvents.push(events[i]);
    }
  }

  return groupedEvents;
}
