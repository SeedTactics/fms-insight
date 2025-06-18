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
  IScannedMaterial,
  IScannedPotentialNewMaterial,
} from "../network/api.js";
import { useCallback, useState } from "react";
import { currentStatus } from "./current-status.js";
import { atom, useSetAtom } from "jotai";
import { loadable } from "jotai/utils";
import { isLogEntryInvalidated } from "../components/LogEntry.js";
import { currentRoute, RouteLocation } from "../components/routes.js";

export type MaterialToShowInfo = {
  readonly materialID: number;
  readonly partName: string;
  readonly serial?: string;
  readonly workorderId?: string;
};

export type MaterialToShow =
  | { readonly type: "InProcMat"; readonly inproc: Readonly<IInProcessMaterial> }
  | { readonly type: "MatSummary"; readonly summary: MaterialToShowInfo }
  | { readonly type: "MatDetails"; readonly details: Readonly<IMaterialDetails> }
  | { readonly type: "LogMat"; readonly logMat: Readonly<ILogMaterial> }
  | { readonly type: "Barcode"; readonly barcode: string; readonly toQueue: string | null }
  | { readonly type: "ManuallyEnteredSerial"; readonly serial: string }
  | { readonly type: "AddMatWithEnteredSerial"; readonly serial: string; readonly toQueue: string };

const matToShow = atom<MaterialToShow | null>(null);

export const materialDialogOpen = atom(
  (get) => get(matToShow),
  (_, set, mat: MaterialToShow | null) => {
    if (mat === null) {
      set(matToShow, null);
      set(extraLogEventsFromUpdates, []);
    } else {
      set(matToShow, mat);
    }
  },
);

//--------------------------------------------------------------------------------
// Material Details
//--------------------------------------------------------------------------------

const barcodeMaterialDetail = atom<Promise<Readonly<IScannedMaterial> | null>>(async (get) => {
  const toShow = get(matToShow);
  if (toShow && toShow.type === "Barcode") {
    const route = get(currentRoute);
    const queues = toShow.toQueue
      ? [toShow.toQueue]
      : route.route === RouteLocation.Station_LoadMonitor
        ? route.queues
        : route.route === RouteLocation.Station_Queues
          ? route.queues
          : null;
    return await FmsServerBackend.parseBarcode(toShow.barcode, queues);
  } else {
    return null;
  }
});

export const barcodePotentialNewMaterial = atom<Promise<Readonly<IScannedPotentialNewMaterial> | null>>(
  async (get) => {
    return (await get(barcodeMaterialDetail))?.potentialNewMaterial ?? null;
  },
);

export const materialInDialogInfo = atom<Promise<MaterialToShowInfo | null>>(async (get) => {
  const curMat = get(matToShow);
  if (curMat === null) return null;
  switch (curMat.type) {
    case "InProcMat":
      return curMat.inproc;
    case "MatSummary":
      return curMat.summary;
    case "MatDetails":
      return curMat.details;
    case "LogMat":
      return {
        materialID: curMat.logMat.id,
        partName: curMat.logMat.part,
        serial: curMat.logMat.serial,
        workorderId: curMat.logMat.workorder,
      };
    case "Barcode": {
      const mat = await get(barcodeMaterialDetail);
      if (mat?.existingMaterial && mat.existingMaterial.materialID >= 0) {
        return mat.existingMaterial;
      } else {
        return null;
      }
    }
    case "ManuallyEnteredSerial":
    case "AddMatWithEnteredSerial": {
      return (await LogBackend.materialForSerial(curMat.serial))?.[0] ?? null;
    }
  }
});

export const inProcessMaterialInDialog = atom<Promise<IInProcessMaterial | null>>(async (get) => {
  const status = get(currentStatus);
  const toShow = get(matToShow);
  if (toShow === null) return null;
  if (toShow.type === "InProcMat") return toShow.inproc;
  const matId = (await get(materialInDialogInfo))?.materialID ?? null;
  return matId !== null && matId >= 0 ? (status.material.find((m) => m.materialID === matId) ?? null) : null;
});

export const serialInMaterialDialog = atom<Promise<string | null>>(async (get) => {
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
    case "Barcode": {
      const barcodeMat = await get(barcodeMaterialDetail);
      return barcodeMat?.existingMaterial?.serial ?? barcodeMat?.potentialNewMaterial?.serial ?? null;
    }
    case "ManuallyEnteredSerial":
    case "AddMatWithEnteredSerial":
      return toShow.serial;
  }
});

export const workorderInMaterialDialog = atom<Promise<string | null>>(async (get) => {
  const toShow = get(matToShow);
  if (toShow === null) return null;
  switch (toShow.type) {
    case "InProcMat":
      return toShow.inproc.workorderId ?? null;
    case "MatSummary":
      return toShow.summary.workorderId ?? null;
    case "MatDetails":
      return toShow.details.workorder ?? null;
    case "LogMat":
      return toShow.logMat.workorder ?? null;
    case "Barcode": {
      const barcodeMat = await get(barcodeMaterialDetail);
      return barcodeMat?.existingMaterial?.workorder ?? barcodeMat?.potentialNewMaterial?.workorder ?? null;
    }
    case "ManuallyEnteredSerial":
    case "AddMatWithEnteredSerial":
      return null;
  }
});

//--------------------------------------------------------------------------------
// Events
//--------------------------------------------------------------------------------

const extraLogEventsFromUpdates = atom<ReadonlyArray<ILogEntry>>([]);

const localMatEvents = atom<Promise<ReadonlyArray<Readonly<ILogEntry>>>>(async (get, { signal }) => {
  const mat = await get(materialInDialogInfo);
  if (mat === null) {
    return [];
  } else if (mat.materialID >= 0) {
    return await LogBackend.logForMaterial(mat.materialID, signal);
  } else if (mat.serial && mat.serial !== "") {
    return await LogBackend.logForSerial(mat.serial);
  } else {
    return [];
  }
});
const localMatEventsLoadable = loadable(localMatEvents);

const otherMatEvents = atom<Promise<ReadonlyArray<Readonly<ILogEntry>>>>(async (get, { signal }) => {
  const serial = await get(serialInMaterialDialog);
  if (serial === null || serial === "") return [];

  const evts: Array<Readonly<ILogEntry>> = [];

  for (const b of OtherLogBackends) {
    evts.push.apply(await b.logForSerial(serial, signal));
  }

  return evts;
});

const otherMatEventsLoadable = loadable(otherMatEvents);

export const materialInDialogEvents = atom<ReadonlyArray<Readonly<ILogEntry>>>((get) => {
  const localEvts = get(localMatEventsLoadable);
  const otherEvts = get(otherMatEventsLoadable);
  const evtsFromUpdate = get(extraLogEventsFromUpdates);
  return LazySeq.of(localEvts.state === "hasData" ? localEvts.data : [])
    .concat(otherEvts.state === "hasData" ? otherEvts.data : [])
    .sortBy(
      (e) => e.endUTC.getTime(),
      (e) => e.counter,
    )
    .concat(evtsFromUpdate)
    .toRArray();
});

export type LargestUsedProces = {
  readonly process: number;
  readonly totalNumProcesses: number;
};

export const materialInDialogLargestUsedProcess = atom<Promise<LargestUsedProces | null>>(async (get) => {
  const info = await get(materialInDialogInfo);
  if (info === null) return null;
  const evts = get(materialInDialogEvents);
  const maxEvt = LazySeq.of(evts)
    .filter(
      (e) =>
        !isLogEntryInvalidated(e) &&
        (e.type === LogType.LoadUnloadCycle ||
          e.type === LogType.MachineCycle ||
          e.type === LogType.AddToQueue),
    )
    .flatMap((e) => e.material)
    .filter((e) => e.id === info.materialID)
    .maxBy((e) => e.proc);
  if (maxEvt) {
    return { process: maxEvt.proc, totalNumProcesses: maxEvt.numproc };
  } else {
    return null;
  }
});

//--------------------------------------------------------------------------------
// Inspections
//--------------------------------------------------------------------------------

export interface MaterialToShowInspections {
  readonly signaledInspections: ReadonlyArray<string>;
  readonly completedInspections: ReadonlyArray<string>;
}

export const materialInDialogInspections = atom<MaterialToShowInspections>((get) => {
  const curMat = get(matToShow);
  const evts = get(materialInDialogEvents);

  if (curMat === null) {
    return { signaledInspections: [], completedInspections: [] };
  }

  const inspTypes = new Set<string>(curMat.type === "InProcMat" ? curMat.inproc.signaledInspections : []);
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
});

//--------------------------------------------------------------------------------
// Workorders
//--------------------------------------------------------------------------------

export const possibleWorkordersForMaterialInDialog = atom<Promise<ReadonlyArray<IActiveWorkorder>>>(
  async (get) => {
    const mat = await get(materialInDialogInfo);
    if (mat === null || mat.partName === "") return [];

    const works = get(currentStatus)?.workorders ?? [];

    return LazySeq.of(works)
      .filter((w) => w.part === mat.partName)
      .toSortedArray(
        (w) => w.dueDate.getTime(),
        (w) => -w.priority,
      );
  },
);

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
  const setExtraLogEvts = useSetAtom(extraLogEventsFromUpdates);
  const callback = useCallback(
    (data: ForceInspectionData) => {
      setUpdating(true);
      LogBackend.setInspectionDecision(data.mat.materialID, data.inspType, 1, data.inspect)
        .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
        .catch(console.log)
        .finally(() => setUpdating(false));
    },
    [setExtraLogEvts],
  );

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
    )
      .catch(console.log)
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface CompleteCloseoutData {
  readonly mat: MaterialToShowInfo;
  readonly operator: string | null;
  readonly failed: boolean;
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
        failed: d.failed,
      }),
    )
      .catch(console.log)
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export function useAssignWorkorder(): [(mat: MaterialToShowInfo, workorder: string) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const setExtraLogEvts = useSetAtom(extraLogEventsFromUpdates);
  const callback = useCallback(
    (mat: MaterialToShowInfo, workorder: string) => {
      setUpdating(true);
      LogBackend.setWorkorder(mat.materialID, 1, workorder)
        .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
        .catch(console.log)
        .finally(() => setUpdating(false));
    },
    [setExtraLogEvts],
  );

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
  const setExtraLogEvts = useSetAtom(extraLogEventsFromUpdates);
  const callback = useCallback(
    (data: AddNoteData) => {
      setUpdating(true);
      LogBackend.recordOperatorNotes(data.matId, data.process, data.operator, data.notes)
        .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
        .catch(console.log)
        .finally(() => setUpdating(false));
    },
    [setExtraLogEvts],
  );

  return [callback, updating];
}

export interface PrintLabelData {
  readonly materialId: number;
  readonly proc: number;
}

export function usePrintLabel(): [(data: PrintLabelData) => void, boolean, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const [printed, setPrinted] = useState<boolean>(false);
  const callback = useCallback((d: PrintLabelData) => {
    setUpdating(true);
    FmsServerBackend.printLabel(d.materialId, d.proc)
      .catch(console.log)
      .finally(() => {
        setUpdating(false);
        setPrinted(true);
      });
  }, []);

  return [callback, updating, printed];
}

export function useRemoveFromQueue(): [(matId: number, operator: string | null) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((matId: number, operator: string | null) => {
    setUpdating(true);
    JobsBackend.removeMaterialFromAllQueues(matId, operator ?? undefined)
      .catch(console.log)
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export function useSignalForQuarantine(): [
  (matId: number, operator: string | null, reason: string) => void,
  boolean,
] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((matId: number, operator: string | null, reason: string) => {
    setUpdating(true);
    JobsBackend.signalMaterialForQuarantine(matId, operator, reason === "" ? undefined : reason)
      .catch(console.log)
      .finally(() => setUpdating(false));
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
      }),
    )
      .catch(console.log)
      .finally(() => setUpdating(false));
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
  readonly workorder: string | null;
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
      d.workorder,
      d.serial || "",
    )
      .then((m) => {
        if (d.onNewMaterial && m) {
          d.onNewMaterial(m);
        } else if (d.onError) {
          d.onError("No material returned");
        }
      }, d.onError)
      .catch(console.log)
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface AddNewCastingToQueueData {
  readonly casting: string;
  readonly quantity: number;
  readonly queue: string;
  readonly serials?: ReadonlyArray<string>;
  readonly workorder: string | null;
  readonly operator: string | null;
  readonly onNewMaterial?: (mats: ReadonlyArray<Readonly<IInProcessMaterial>>) => void;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  readonly onError?: (reason: any) => void;
}

export function useAddNewCastingToQueue(): [(d: AddNewCastingToQueueData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((d: AddNewCastingToQueueData) => {
    setUpdating(true);

    JobsBackend.addUnallocatedCastingToQueue(d.casting, d.queue, d.quantity, d.operator, d.workorder, [
      ...(d.serials || []),
    ])
      .then((ms) => {
        if (d.onNewMaterial) d.onNewMaterial(ms);
      }, d.onError)
      .catch(console.log)
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}
