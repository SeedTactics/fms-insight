/* Copyright (c) 2022, John Lenz

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

import { JobsBackend, LogBackend, OtherLogBackends, FmsServerBackend } from "../network/backend.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { MaterialSummary } from "./material-summary.js";
import {
  atom,
  noWait,
  RecoilValueReadOnly,
  selector,
  useRecoilTransaction_UNSTABLE,
  useSetRecoilState,
  waitForNone,
} from "recoil";
import {
  IInProcessMaterial,
  ILogEntry,
  ILogMaterial,
  IMaterialDetails,
  LogType,
  NewInspectionCompleted,
  NewCloseout,
  QueuePosition,
  IActiveWorkorder,
} from "../network/api.js";
import { useCallback, useState } from "react";
import { currentStatus } from "./current-status.js";

export type MaterialToShow =
  | { readonly type: "InProcMat"; readonly inproc: Readonly<IInProcessMaterial> }
  | { readonly type: "MatSummary"; readonly summary: Readonly<MaterialSummary> }
  | { readonly type: "MatDetails"; readonly details: Readonly<IMaterialDetails> }
  | { readonly type: "LogMat"; readonly logMat: Readonly<ILogMaterial> }
  | { readonly type: "Barcode"; readonly barcode: string }
  | { readonly type: "ManuallyEnteredSerial"; readonly serial: string }
  | { readonly type: "AddMatWithEnteredSerial"; readonly serial: string; readonly toQueue: string };

const matToShow = atom<MaterialToShow | null>({
  key: "mat-to-show-in-dialog",
  default: null,
});

export function useSetMaterialToShowInDialog(): (mat: MaterialToShow) => void {
  return useSetRecoilState(matToShow);
}

export const materialDialogOpen: RecoilValueReadOnly<MaterialToShow | null> = matToShow;

export function useCloseMaterialDialog(): () => void {
  return useRecoilTransaction_UNSTABLE(
    ({ set }) =>
      () => {
        set(matToShow, null);
        set(extraLogEventsFromUpdates, []);
      },
    []
  );
}

//--------------------------------------------------------------------------------
// Material Details
//--------------------------------------------------------------------------------

const barcodeMaterialDetail = selector<Readonly<IMaterialDetails> | null>({
  key: "barcode-material-detail",
  get: async ({ get }) => {
    const toShow = get(matToShow);
    if (toShow && toShow.type === "Barcode") {
      return await FmsServerBackend.parseBarcode(toShow.barcode);
    } else {
      return null;
    }
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export interface MaterialToShowInfo {
  readonly materialID: number;
  readonly jobUnique: string;
  readonly partName: string;
  readonly serial?: string;
  readonly workorderId?: string;
}

export const materialInDialogInfo = selector<MaterialToShowInfo | null>({
  key: "material-detail",
  get: async ({ get }) => {
    const curMat = get(matToShow);
    if (curMat === null) return null;
    switch (curMat.type) {
      case "InProcMat":
        return curMat.inproc;
      case "MatSummary":
        return curMat.summary;
      case "MatDetails":
        return {
          ...curMat.details,
          jobUnique: curMat.details.jobUnique ?? "",
        };
      case "LogMat":
        return {
          materialID: curMat.logMat.id,
          jobUnique: curMat.logMat.uniq,
          partName: curMat.logMat.part,
          serial: curMat.logMat.serial,
          workorderId: curMat.logMat.workorder,
        };
      case "Barcode": {
        const mat = get(barcodeMaterialDetail);
        if (mat && mat.materialID >= 0) {
          return { ...mat, jobUnique: mat.jobUnique ?? "" };
        } else {
          return null;
        }
      }
      case "ManuallyEnteredSerial":
      case "AddMatWithEnteredSerial": {
        const mat = (await LogBackend.materialForSerial(curMat.serial))?.[0] ?? null;
        return mat ? { ...mat, jobUnique: mat.jobUnique ?? "" } : null;
      }
    }
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export const inProcessMaterialInDialog = selector<IInProcessMaterial | null>({
  key: "material-inproc-mat-in-dialog",
  get: ({ get }) => {
    const status = get(currentStatus);
    const toShow = get(matToShow);
    if (toShow === null) return null;
    if (toShow.type === "InProcMat") return toShow.inproc;
    const matId = get(materialInDialogInfo)?.materialID ?? null;
    return matId !== null && matId >= 0 ? status.material.find((m) => m.materialID === matId) ?? null : null;
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export const serialInMaterialDialog = selector<string | null>({
  key: "serial-in-material-dialog",
  get: ({ get }) => {
    const toShow = get(matToShow);
    if (toShow === null) return null;
    switch (toShow.type) {
      case "InProcMat":
        return toShow.inproc.serial ?? null;
      case "MatSummary":
        return toShow.summary.serial ?? null;
      case "MatDetails":
        return toShow.details.serial ?? null;
      case "LogMat":
        return toShow.logMat.serial ?? null;
      case "Barcode":
        return get(barcodeMaterialDetail)?.serial ?? null;
      case "ManuallyEnteredSerial":
      case "AddMatWithEnteredSerial":
        return toShow.serial;
    }
  },
});

//--------------------------------------------------------------------------------
// Events
//--------------------------------------------------------------------------------

const extraLogEventsFromUpdates = atom<ReadonlyArray<ILogEntry>>({
  key: "extra-mat-events",
  default: [],
});

const localMatEvents = selector<ReadonlyArray<Readonly<ILogEntry>>>({
  key: "mat-to-show-local-evts",
  get: async ({ get }) => {
    const mat = get(materialInDialogInfo);
    if (mat === null) {
      return [];
    } else if (mat.materialID >= 0) {
      return await LogBackend.logForMaterial(mat.materialID);
    } else if (mat.serial && mat.serial !== "") {
      return await LogBackend.logForSerial(mat.serial);
    } else {
      return [];
    }
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

const otherMatEvents = selector<ReadonlyArray<Readonly<ILogEntry>>>({
  key: "mat-to-show-other-evts",
  get: async ({ get }) => {
    const serial = get(serialInMaterialDialog);
    if (serial === null || serial === "") return [];

    const evts: Array<Readonly<ILogEntry>> = [];

    for (const b of OtherLogBackends) {
      evts.push.apply(await b.logForSerial(serial));
    }

    return evts;
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export const materialInDialogEvents = selector<ReadonlyArray<Readonly<ILogEntry>>>({
  key: "material-in-dialog-events",
  get: ({ get }) => {
    const [localEvts, otherEvts] = get(waitForNone([localMatEvents, otherMatEvents]));
    const evtsFromUpdate = get(extraLogEventsFromUpdates);
    return LazySeq.of(localEvts.state === "hasValue" ? localEvts.valueOrThrow() : [])
      .concat(otherEvts.state === "hasValue" ? otherEvts.valueOrThrow() : [])
      .sortBy(
        (e) => e.endUTC.getTime(),
        (e) => e.counter
      )
      .concat(evtsFromUpdate)
      .toRArray();
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

//--------------------------------------------------------------------------------
// Inspections
//--------------------------------------------------------------------------------

export interface MaterialToShowInspections {
  readonly signaledInspections: ReadonlyArray<string>;
  readonly completedInspections: ReadonlyArray<string>;
}

export const materialInDialogInspections = selector<MaterialToShowInspections>({
  key: "material-in-dialog-inspections",
  get: ({ get }) => {
    const curMat = get(matToShow);
    const evtsLoadable = get(noWait(materialInDialogEvents));
    const evts = evtsLoadable.valueMaybe() ?? [];

    if (curMat === null) {
      return { signaledInspections: [], completedInspections: [] };
    }

    const inspTypes = new Set<string>(
      curMat.type === "MatSummary"
        ? curMat.summary.signaledInspections
        : curMat.type === "InProcMat"
        ? curMat.inproc.signaledInspections
        : []
    );
    const completedTypes = new Set<string>();

    evts.forEach((e) => {
      switch (e.type) {
        case LogType.Inspection:
          if (e.result.toLowerCase() === "true" || e.result === "1") {
            const itype = (e.details || {}).InspectionType;
            if (itype) {
              inspTypes.add(itype);
            }
          }
          break;

        case LogType.InspectionForce:
          if (e.result.toLowerCase() === "true" || e.result === "1") {
            inspTypes.add(e.program);
          }
          break;

        case LogType.InspectionResult:
          completedTypes.add(e.program);
          break;
      }
    });

    return {
      signaledInspections: LazySeq.of(inspTypes).toSortedArray((x) => x),
      completedInspections: LazySeq.of(completedTypes).toSortedArray((x) => x),
    };
  },
});

//--------------------------------------------------------------------------------
// Workorders
//--------------------------------------------------------------------------------

export const possibleWorkordersForMaterialInDialog = selector<ReadonlyArray<IActiveWorkorder>>({
  key: "possible-workorders-for-mat-in-dialog",
  get: async ({ get }) => {
    const mat = get(materialInDialogInfo);
    if (mat === null || mat.partName === "") return [];

    const works = await JobsBackend.mostRecentUnfilledWorkordersForPart(mat.partName);

    return LazySeq.of(works).toSortedArray(
      (w) => (w.finalizedTimeUTC ? 1 : 0),
      (w) => w.dueDate.getTime(),
      (w) => -w.priority
    );
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

//--------------------------------------------------------------------------------
// Updates
//--------------------------------------------------------------------------------

export interface ForceInspectionData {
  readonly mat: MaterialToShowInfo;
  readonly inspType: string;
  readonly inspect: boolean;
}

export function useForceInspection(): [(data: ForceInspectionData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const setExtraLogEvts = useSetRecoilState(extraLogEventsFromUpdates);
  const callback = useCallback((data: ForceInspectionData) => {
    setUpdating(true);
    LogBackend.setInspectionDecision(
      data.mat.materialID,
      data.inspType,
      1,
      data.inspect,
      data.mat.jobUnique,
      data.mat.partName
    )
      .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface CompleteInspectionData {
  readonly mat: MaterialToShowInfo;
  readonly inspType: string;
  readonly success: boolean;
  readonly operator: string | null;
}

export function useCompleteInspection(): [(data: CompleteInspectionData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((data: CompleteInspectionData) => {
    setUpdating(true);
    LogBackend.recordInspectionCompleted(
      new NewInspectionCompleted({
        materialID: data.mat.materialID,
        process: 1,
        inspectionLocationNum: 1,
        inspectionType: data.inspType,
        success: data.success,
        active: "PT0S",
        elapsed: "PT0S",
        extraData: data.operator ? { operator: data.operator } : undefined,
      }),
      data.mat.jobUnique,
      data.mat.partName
    ).finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface CompleteCloseoutData {
  readonly mat: MaterialToShowInfo;
  readonly operator: string | null;
}

export function useCompleteCloseout(): [(d: CompleteCloseoutData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((d: CompleteCloseoutData) => {
    setUpdating(true);
    LogBackend.recordCloseoutCompleted(
      new NewCloseout({
        materialID: d.mat.materialID,
        process: 1,
        locationNum: 1,
        closeoutType: "",
        active: "PT0S",
        elapsed: "PT0S",
        extraData: d.operator ? { operator: d.operator } : undefined,
      }),
      d.mat.jobUnique,
      d.mat.partName
    ).finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export function useAssignWorkorder(): [(mat: MaterialToShowInfo, workorder: string) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const setExtraLogEvts = useSetRecoilState(extraLogEventsFromUpdates);
  const callback = useCallback((mat: MaterialToShowInfo, workorder: string) => {
    setUpdating(true);
    LogBackend.setWorkorder(mat.materialID, 1, workorder, mat.jobUnique, mat.partName)
      .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface AddNoteData {
  readonly matId: number;
  readonly process: number;
  readonly operator: string | null;
  readonly notes: string;
}

export function useAddNote(): [(data: AddNoteData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const setExtraLogEvts = useSetRecoilState(extraLogEventsFromUpdates);
  const callback = useCallback((data: AddNoteData) => {
    setUpdating(true);
    LogBackend.recordOperatorNotes(data.matId, data.process, data.operator, data.notes)
      .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface PrintLabelData {
  readonly materialId: number;
  readonly proc: number;
}

export function usePrintLabel(): [(data: PrintLabelData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((d: PrintLabelData) => {
    setUpdating(true);
    FmsServerBackend.printLabel(d.materialId, d.proc).finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export function useRemoveFromQueue(): [(matId: number, operator: string | null) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((matId: number, operator: string | null) => {
    setUpdating(true);
    JobsBackend.removeMaterialFromAllQueues(matId, operator ?? undefined).finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export function useSignalForQuarantine(): [
  (matId: number, operator: string | null, reason: string) => void,
  boolean
] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((matId: number, operator: string | null, reason: string) => {
    setUpdating(true);
    JobsBackend.signalMaterialForQuarantine(matId, operator, reason === "" ? undefined : reason).finally(() =>
      setUpdating(false)
    );
  }, []);

  return [callback, updating];
}

export interface AddExistingMaterialToQueueData {
  readonly materialId: number;
  readonly queue: string;
  readonly queuePosition: number;
  readonly operator: string | null;
}

export function useAddExistingMaterialToQueue(): [(d: AddExistingMaterialToQueueData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((d: AddExistingMaterialToQueueData) => {
    setUpdating(true);
    JobsBackend.setMaterialInQueue(
      d.materialId,
      d.operator,
      new QueuePosition({
        queue: d.queue,
        position: d.queuePosition,
      })
    ).finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface AddNewMaterialToQueueData {
  readonly jobUnique: string;
  readonly lastCompletedProcess: number;
  readonly queue: string;
  readonly queuePosition: number;
  readonly serial?: string;
  readonly operator: string | null;
  readonly onNewMaterial?: (mat: Readonly<IInProcessMaterial>) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  readonly onError?: (reason: any) => void;
}

export function useAddNewMaterialToQueue(): [(d: AddNewMaterialToQueueData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((d: AddNewMaterialToQueueData) => {
    setUpdating(true);

    JobsBackend.addUnprocessedMaterialToQueue(
      d.jobUnique,
      d.lastCompletedProcess,
      d.queue,
      d.queuePosition,
      d.operator,
      d.serial || ""
    )
      .then((m) => {
        if (d.onNewMaterial && m) {
          d.onNewMaterial(m);
        } else if (d.onError) {
          d.onError("No material returned");
        }
      }, d.onError)
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface AddNewCastingToQueueData {
  readonly casting: string;
  readonly quantity: number;
  readonly queue: string;
  readonly serials?: ReadonlyArray<string>;
  readonly operator: string | null;
  readonly onNewMaterial?: (mats: ReadonlyArray<Readonly<IInProcessMaterial>>) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  readonly onError?: (reason: any) => void;
}

export function useAddNewCastingToQueue(): [(d: AddNewCastingToQueueData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((d: AddNewCastingToQueueData) => {
    setUpdating(true);

    JobsBackend.addUnallocatedCastingToQueue(d.casting, d.queue, d.quantity, d.operator, [
      ...(d.serials || []),
    ])
      .then((ms) => {
        if (d.onNewMaterial) d.onNewMaterial(ms);
      }, d.onError)
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}
