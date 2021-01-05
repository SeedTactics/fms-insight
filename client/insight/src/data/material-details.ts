/* Copyright (c) 2021, John Lenz

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

import { JobsBackend, LogBackend, OtherLogBackends, FmsServerBackend } from "./backend";
import { Vector, HashSet } from "prelude-ts";
import { LazySeq } from "./lazyseq";
import { MaterialSummary } from "./events.matsummary";
import { atom, DefaultValue, selector, useSetRecoilState, waitForNone } from "recoil";
import {
  IInProcessMaterial,
  ILogEntry,
  ILogMaterial,
  IPartWorkorder,
  IWorkorderPartSummary,
  IWorkorderSummary,
  LogType,
  NewInspectionCompleted,
  NewWash,
  QueuePosition,
} from "./api";
import { useCallback, useState } from "react";

export type MaterialToShow =
  | { type: "MatSummary"; summary: Readonly<MaterialSummary> }
  | { type: "LogMat"; logMat: Readonly<ILogMaterial> }
  | { type: "Serial"; serial: string | null; addToQueue?: string };

const matToShow = atom<MaterialToShow | null>({
  key: "mat-to-show-in-dialog",
  default: null,
});

export const loadWorkordersForMaterialInDialog = atom<boolean>({
  key: "load-workorders-for-mat-in-dialog",
  default: false,
});

const extraLogEventsFromUpdates = atom<ReadonlyArray<ILogEntry>>({
  key: "extra-mat-events",
  default: [],
});

export const materialToShowInDialog = selector<MaterialToShow | null>({
  key: "mat-to-show-in-dialog-sel",
  get: ({ get }) => get(matToShow),
  set: ({ set }, newVal) => {
    if (newVal instanceof DefaultValue || newVal === null) {
      set(matToShow, null);
      set(loadWorkordersForMaterialInDialog, false);
      set(extraLogEventsFromUpdates, []);
    } else {
      set(matToShow, newVal);
    }
  },
});

//--------------------------------------------------------------------------------
// Material Details
//--------------------------------------------------------------------------------

const localMatEvents = selector<ReadonlyArray<Readonly<ILogEntry>>>({
  key: "mat-to-show-local-evts",
  get: async ({ get }) => {
    const mat = get(matToShow);
    if (mat === null) return [];

    switch (mat.type) {
      case "MatSummary":
        return await LogBackend.logForMaterial(mat.summary.materialID);
      case "LogMat":
        return await LogBackend.logForMaterial(mat.logMat.id);
      case "Serial":
        if (mat.serial && mat.serial !== "") {
          return await LogBackend.logForSerial(mat.serial);
        } else {
          return [];
        }
    }
  },
});

const otherMatEvents = selector<ReadonlyArray<Readonly<ILogEntry>>>({
  key: "mat-to-show-other-evts",
  get: async ({ get }) => {
    const mat = get(matToShow);
    if (mat === null) return [];

    const evts: Array<Readonly<ILogEntry>> = [];

    for (const b of OtherLogBackends) {
      switch (mat.type) {
        case "MatSummary":
          evts.push.apply(await b.logForMaterial(mat.summary.materialID));
          break;
        case "LogMat":
          evts.push.apply(await b.logForMaterial(mat.logMat.id));
          break;
        case "Serial":
          if (mat.serial && mat.serial !== "") {
            evts.push.apply(await b.logForSerial(mat.serial));
          }
          break;
      }
    }

    return evts;
  },
});

export interface MaterialDetail {
  readonly materialID: number;
  readonly jobUnique: string;
  readonly partName: string;
  readonly startedProcess1: boolean;

  readonly serial?: string;
  readonly workorderId?: string;

  readonly signaledInspections: ReadonlyArray<string>;
  readonly completedInspections: ReadonlyArray<string>;

  readonly loading_events: boolean;
  readonly events: Vector<Readonly<ILogEntry>>;
}

export const materialDetail = selector<MaterialDetail | null>({
  key: "material-detail",
  get: ({ get }) => {
    const curMat = get(matToShow);
    if (curMat === null) return null;

    const [localEvts, otherEvts] = get(waitForNone([localMatEvents, otherMatEvents]));
    const evtsFromUpdate = get(extraLogEventsFromUpdates);
    const loading = localEvts.state === "loading" || otherEvts.state === "loading";
    const allEvents = Vector.ofIterable(localEvts.state === "hasValue" ? localEvts.valueOrThrow() : [])
      .appendAll(otherEvts.state === "hasValue" ? otherEvts.valueOrThrow() : [])
      .sortOn(
        (e) => e.endUTC.getTime(),
        (e) => e.counter
      )
      .appendAll(evtsFromUpdate);

    let mat: MaterialDetail;
    switch (curMat.type) {
      case "MatSummary":
        mat = {
          ...curMat.summary,
          completedInspections: [],
          loading_events: loading,
          events: Vector.empty(),
        };
        break;
      case "LogMat":
        mat = {
          materialID: curMat.logMat.id,
          jobUnique: curMat.logMat.uniq,
          partName: curMat.logMat.part,
          startedProcess1: curMat.logMat.proc > 0,
          serial: curMat.logMat.serial,
          workorderId: curMat.logMat.workorder,
          signaledInspections: [],
          completedInspections: [],
          loading_events: loading,
          events: Vector.empty(),
        };
        break;
      case "Serial":
        mat = {
          materialID: -1,
          jobUnique: "",
          partName: "",
          startedProcess1: false,
          serial: curMat.serial ?? undefined,
          workorderId: undefined,
          signaledInspections: [],
          completedInspections: [],
          loading_events: loading,
          events: Vector.empty(),
        };
        break;
    }

    let inspTypes = HashSet.ofIterable(mat.signaledInspections);
    let completedTypes = HashSet.ofIterable(mat.completedInspections);

    allEvents.forEach((e) => {
      e.material.forEach((m) => {
        if (mat.materialID < 0 && mat.serial === m.serial) {
          mat = { ...mat, materialID: m.id };
        }
        if (mat.partName === "") {
          mat = { ...mat, partName: m.part };
        }
        if (mat.jobUnique === "") {
          mat = { ...mat, jobUnique: m.uniq };
        }
      });

      switch (e.type) {
        case LogType.PartMark:
          mat = { ...mat, serial: e.result };
          break;

        case LogType.OrderAssignment:
          mat = { ...mat, workorderId: e.result };
          break;

        case LogType.Inspection:
          if (e.result.toLowerCase() === "true" || e.result === "1") {
            const itype = (e.details || {}).InspectionType;
            if (itype) {
              inspTypes = inspTypes.add(itype);
            }
          }
          break;

        case LogType.InspectionForce:
          if (e.result.toLowerCase() === "true" || e.result === "1") {
            inspTypes = inspTypes.add(e.program);
          }
          break;

        case LogType.InspectionResult:
          completedTypes = completedTypes.add(e.program);
          break;
      }
    });

    return {
      ...mat,
      signaledInspections: inspTypes.toArray({ sortOn: (x) => x }),
      completedInspections: completedTypes.toArray({ sortOn: (x) => x }),
      events: allEvents,
    };
  },
});

//--------------------------------------------------------------------------------
// Workorders
//--------------------------------------------------------------------------------

export interface WorkorderPlanAndSummary {
  readonly plan: Readonly<IPartWorkorder>;
  readonly summary?: Readonly<IWorkorderPartSummary>;
}

export const possibleWorkordersForMaterialInDialog = selector<Vector<WorkorderPlanAndSummary>>({
  key: "possible-workorders-for-mat-in-dialog",
  get: async ({ get }) => {
    const mat = get(materialDetail);
    const load = get(loadWorkordersForMaterialInDialog);
    if (mat === null || mat.partName === "" || load === false) return Vector.empty();

    const works = await JobsBackend.mostRecentUnfilledWorkordersForPart(mat.partName);
    const summaries: IWorkorderSummary[] = [];
    for (const ws of LazySeq.ofIterable(works).chunk(16)) {
      summaries.push(...(await LogBackend.getWorkorders(ws.map((w) => w.workorderId))));
    }

    const workMap = new Map<string, WorkorderPlanAndSummary>();
    for (const w of works) {
      workMap.set(w.workorderId, { plan: w });
    }
    for (const s of summaries) {
      for (const w of s.parts) {
        if (w.name === mat.partName) {
          const planAndS = workMap.get(s.id);
          if (planAndS) {
            workMap.set(s.id, { ...planAndS, summary: w });
          }
        }
      }
    }
    return Vector.ofIterable(workMap.values()).sortOn(
      (w) => w.plan.dueDate.getTime(),
      (w) => -w.plan.priority
    );
  },
});

//--------------------------------------------------------------------------------
// Updates
//--------------------------------------------------------------------------------

export interface ForceInspectionData {
  readonly mat: MaterialDetail;
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
      data.inspect,
      1,
      data.mat.jobUnique,
      data.mat.partName
    )
      .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface CompleteInspectionData {
  readonly mat: MaterialDetail;
  readonly inspType: string;
  readonly success: boolean;
  readonly operator: string | null;
}

export function useCompleteInspection(): [(data: CompleteInspectionData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const setExtraLogEvts = useSetRecoilState(extraLogEventsFromUpdates);
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
    )
      .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface CompleteWashData {
  readonly mat: MaterialDetail;
  readonly operator: string | null;
}

export function useCompleteWash(): [(d: CompleteWashData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const setExtraLogEvts = useSetRecoilState(extraLogEventsFromUpdates);
  const callback = useCallback((d: CompleteWashData) => {
    setUpdating(true);
    LogBackend.recordWashCompleted(
      new NewWash({
        materialID: d.mat.materialID,
        process: 1,
        washLocationNum: 1,
        active: "PT0S",
        elapsed: "PT0S",
        extraData: d.operator ? { operator: d.operator } : undefined,
      }),
      d.mat.jobUnique,
      d.mat.partName
    )
      .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export function useAssignWorkorder(): [(mat: MaterialDetail, workorder: string) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const setExtraLogEvts = useSetRecoilState(extraLogEventsFromUpdates);
  const callback = useCallback((mat: MaterialDetail, workorder: string) => {
    setUpdating(true);
    LogBackend.setWorkorder(mat.materialID, workorder, 1, mat.jobUnique, mat.partName)
      .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export function useAssignSerial(): [(mat: MaterialDetail, serial: string) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const setExtraLogEvts = useSetRecoilState(extraLogEventsFromUpdates);
  const callback = useCallback((mat: MaterialDetail, serial: string) => {
    setUpdating(true);
    LogBackend.setSerial(mat.materialID, serial, 1, mat.jobUnique, mat.partName)
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
    LogBackend.recordOperatorNotes(data.matId, data.notes, data.process, data.operator ?? undefined)
      .then((evt) => setExtraLogEvts((evts) => [...evts, evt]))
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface PrintLabelData {
  readonly materialId: number;
  readonly proc: number;
  readonly loadStation: number | null;
  readonly queue: string | null;
}

export function usePrintLabel(): [(data: PrintLabelData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((d: PrintLabelData) => {
    setUpdating(true);
    FmsServerBackend.printLabel(d.materialId, d.proc, d.loadStation ?? undefined, d.queue ?? undefined).finally(() =>
      setUpdating(false)
    );
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

export function useSignalForQuarantine(): [(matId: number, queue: string, operator: string | null) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((matId: number, queue: string, operator: string | null) => {
    setUpdating(true);
    JobsBackend.signalMaterialForQuarantine(matId, queue, operator ?? undefined).finally(() => setUpdating(false));
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
      new QueuePosition({
        queue: d.queue,
        position: d.queuePosition,
      }),
      d.operator ?? undefined
    ).finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}

export interface AddNewMaterialToQueueData {
  readonly jobUnique: string;
  readonly lastCompletedProcess: number;
  readonly pathGroup: number;
  readonly queue: string;
  readonly queuePosition: number;
  readonly serial?: string;
  readonly operator: string | null;
  readonly onNewMaterial?: (mat: Readonly<IInProcessMaterial>) => void;
  readonly onError?: (reason: any) => void;
}

export function useAddNewMaterialToQueue(): [(d: AddNewMaterialToQueueData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((d: AddNewMaterialToQueueData) => {
    setUpdating(true);

    JobsBackend.addUnprocessedMaterialToQueue(
      d.jobUnique,
      d.lastCompletedProcess,
      d.pathGroup,
      d.queue,
      d.queuePosition,
      d.serial || "",
      d.operator || undefined
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
  readonly queuePosition: number;
  readonly serials?: ReadonlyArray<string>;
  readonly operator: string | null;
  readonly onNewMaterial?: (mats: ReadonlyArray<Readonly<IInProcessMaterial>>) => void;
  readonly onError?: (reason: any) => void;
}

export function useAddNewCastingToQueue(): [(d: AddNewCastingToQueueData) => void, boolean] {
  const [updating, setUpdating] = useState<boolean>(false);
  const callback = useCallback((d: AddNewCastingToQueueData) => {
    setUpdating(true);

    JobsBackend.addUnallocatedCastingToQueue(
      d.casting,
      d.queue,
      d.queuePosition,
      [...(d.serials || [])],
      d.quantity,
      d.operator || undefined
    )
      .then((ms) => {
        if (d.onNewMaterial) d.onNewMaterial(ms);
      }, d.onError)
      .finally(() => setUpdating(false));
  }, []);

  return [callback, updating];
}
