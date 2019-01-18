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
import { Pledge, PledgeStatus, PledgeToPromise } from "../store/middleware";
import { MaterialSummary } from "./events";
import { JobsBackend, LogBackend, OtherLogBackends } from "./backend";
import { chunk } from "lodash";
import { Vector, HashSet } from "prelude-ts";

export enum ActionType {
  OpenMaterialDialog = "MaterialDetails_Open",
  OpenMaterialDialogWithoutLoad = "MaterialDetails_OpenWithoutLoad",
  LoadLogFromOtherServer = "MaterialDetails_LoadLogFromOtherServer",
  CloseMaterialDialog = "MaterialDetails_Close",
  UpdateMaterial = "MaterialDetails_UpdateMaterial",
  LoadWorkorders = "OrderAssign_LoadWorkorders",
  AddNewMaterialToQueue = "MaterialDetails_AddNewMaterialToQueue"
}

export interface WorkorderPlanAndSummary {
  readonly plan: Readonly<api.IPartWorkorder>;
  readonly summary?: Readonly<api.IWorkorderPartSummary>;
}

export interface MaterialDetail {
  readonly materialID: number;
  readonly partName: string;
  readonly jobUnique: string;
  readonly serial?: string;
  readonly workorderId?: string;
  readonly signaledInspections: ReadonlyArray<string>;
  readonly completedInspections: ReadonlyArray<string>;
  readonly openedViaBarcodeScanner: boolean;

  readonly updating_material: boolean;

  readonly loading_events: boolean;
  readonly events: Vector<Readonly<api.ILogEntry>>;

  readonly loading_workorders: boolean;
  readonly workorders: Vector<WorkorderPlanAndSummary>;
}

export type Action =
  | {
      type: ActionType.CloseMaterialDialog;
    }
  | {
      type: ActionType.OpenMaterialDialog;
      initial: MaterialDetail;
      pledge: Pledge<ReadonlyArray<Readonly<api.ILogEntry>>>;
    }
  | {
      type: ActionType.LoadLogFromOtherServer;
      pledge: Pledge<ReadonlyArray<Readonly<api.ILogEntry>>>;
    }
  | {
      type: ActionType.OpenMaterialDialogWithoutLoad;
      mat: MaterialDetail;
    }
  | {
      type: ActionType.UpdateMaterial;
      newCompletedInspection?: string;
      newWorkorder?: string;
      newSerial?: string;
      newSignaledInspection?: string;
      pledge: Pledge<Readonly<api.ILogEntry> | undefined>;
    }
  | {
      type: ActionType.LoadWorkorders;
      pledge: Pledge<Vector<WorkorderPlanAndSummary>>;
    }
  | {
      type: ActionType.AddNewMaterialToQueue;
      pledge: Pledge<void>;
    };

export function openMaterialDialog(mat: Readonly<MaterialSummary>): ReadonlyArray<PledgeToPromise<Action>> {
  const mainLoad = {
    type: ActionType.OpenMaterialDialog,
    initial: {
      materialID: mat.materialID,
      partName: mat.partName,
      jobUnique: mat.jobUnique,
      serial: mat.serial,
      workorderId: mat.workorderId,
      signaledInspections: mat.signaledInspections,
      completedInspections: [],
      loading_events: true,
      updating_material: false,
      events: Vector.empty(),
      loading_workorders: false,
      saving_workorder: false,
      workorders: Vector.empty(),
      openedViaBarcodeScanner: false
    } as MaterialDetail,
    pledge: LogBackend.logForMaterial(mat.materialID)
  } as PledgeToPromise<Action>;

  let extra: ReadonlyArray<PledgeToPromise<Action>> = [];
  if (mat.serial && mat.serial !== "") {
    const serial = mat.serial;
    extra = OtherLogBackends.map(
      b =>
        ({
          type: ActionType.LoadLogFromOtherServer,
          pledge: b.logForSerial(serial)
        } as PledgeToPromise<Action>)
    );
  }
  return [mainLoad].concat(extra);
}

export function openMaterialDialogWithEmptyMat(): PledgeToPromise<Action> {
  return {
    type: ActionType.OpenMaterialDialogWithoutLoad,
    mat: {
      materialID: -1,
      partName: "",
      jobUnique: "",
      serial: "",
      workorderId: "",
      signaledInspections: [],
      completedInspections: [],
      loading_events: false,
      updating_material: false,
      events: Vector.empty(),
      loading_workorders: false,
      saving_workorder: false,
      workorders: Vector.empty(),
      openedViaBarcodeScanner: false
    } as MaterialDetail
  };
}

export function openMaterialBySerial(serial: string, openedByBarcode: boolean): ReadonlyArray<PledgeToPromise<Action>> {
  const mainLoad = {
    type: ActionType.OpenMaterialDialog,
    initial: {
      materialID: -1,
      partName: "",
      jobUnique: "",
      serial: serial,
      workorderId: "",
      signaledInspections: [],
      completedInspections: [],
      loading_events: true,
      updating_material: false,
      events: Vector.empty(),
      loading_workorders: false,
      saving_workorder: false,
      workorders: Vector.empty(),
      openedViaBarcodeScanner: openedByBarcode
    } as MaterialDetail,
    pledge: LogBackend.logForSerial(serial)
  } as PledgeToPromise<Action>;
  let extra: ReadonlyArray<PledgeToPromise<Action>> = [];
  if (serial !== "") {
    extra = OtherLogBackends.map(
      b =>
        ({
          type: ActionType.LoadLogFromOtherServer,
          pledge: b.logForSerial(serial)
        } as PledgeToPromise<Action>)
    );
  }
  return [mainLoad].concat(extra);
}

export interface ForceInspectionData {
  readonly mat: MaterialDetail;
  readonly inspType: string;
  readonly inspect: boolean;
}

export function forceInspection({ mat, inspType, inspect }: ForceInspectionData): PledgeToPromise<Action> {
  const logMat = new api.LogMaterial({
    id: mat.materialID,
    uniq: mat.jobUnique,
    part: mat.partName,
    proc: 1,
    numproc: 1,
    face: "1"
  });
  return {
    type: ActionType.UpdateMaterial,
    newSignaledInspection: inspType,
    pledge: LogBackend.setInspectionDecision(inspType, logMat, inspect)
  };
}

export interface CompleteInspectionData {
  readonly mat: MaterialDetail;
  readonly inspType: string;
  readonly success: boolean;
  readonly operator?: string;
}

export function completeInspection({
  mat,
  inspType,
  success,
  operator
}: CompleteInspectionData): PledgeToPromise<Action> {
  return {
    type: ActionType.UpdateMaterial,
    newCompletedInspection: inspType,
    pledge: LogBackend.recordInspectionCompleted(
      new api.NewInspectionCompleted({
        material: new api.LogMaterial({
          id: mat.materialID,
          uniq: mat.jobUnique,
          part: mat.partName,
          proc: 1,
          numproc: 1,
          face: "1"
        }),
        inspectionLocationNum: 1,
        inspectionType: inspType,
        success,
        active: "PT0S",
        elapsed: "PT0S",
        extraData: operator ? { operator } : undefined
      })
    )
  };
}

export interface CompleteWashData {
  readonly mat: MaterialDetail;
  readonly operator?: string;
}

export function completeWash(d: CompleteWashData): PledgeToPromise<Action> {
  return {
    type: ActionType.UpdateMaterial,
    pledge: LogBackend.recordWashCompleted(
      new api.NewWash({
        material: new api.LogMaterial({
          id: d.mat.materialID,
          uniq: d.mat.jobUnique,
          part: d.mat.partName,
          proc: 1,
          numproc: 1,
          face: "1"
        }),
        washLocationNum: 1,
        active: "PT0S",
        elapsed: "PT0S",
        extraData: d.operator ? { operator: d.operator } : undefined
      })
    )
  };
}

export function removeFromQueue(mat: MaterialDetail): PledgeToPromise<Action> {
  return {
    type: ActionType.UpdateMaterial,
    pledge: JobsBackend.removeMaterialFromAllQueues(mat.materialID).then(() => undefined)
  };
}

export interface AssignWorkorderData {
  readonly mat: MaterialDetail;
  readonly workorder: string;
}

export function assignWorkorder({ mat, workorder }: AssignWorkorderData): PledgeToPromise<Action> {
  return {
    type: ActionType.UpdateMaterial,
    newWorkorder: workorder,
    pledge: LogBackend.setWorkorder(
      workorder,
      new api.LogMaterial({
        id: mat.materialID,
        uniq: mat.jobUnique,
        part: mat.partName,
        proc: 1,
        numproc: 1,
        face: "1"
      })
    )
  };
}

export interface AssignSerialData {
  readonly mat: MaterialDetail;
  readonly serial: string;
}

export function assignSerial({ mat, serial }: AssignSerialData): PledgeToPromise<Action> {
  return {
    type: ActionType.UpdateMaterial,
    newSerial: serial,
    pledge: LogBackend.setSerial(
      serial,
      new api.LogMaterial({
        id: mat.materialID,
        uniq: mat.jobUnique,
        part: mat.partName,
        proc: 1,
        numproc: 1,
        face: "1"
      })
    )
  };
}

export function computeWorkorders(
  partName: string,
  workorders: ReadonlyArray<api.IPartWorkorder>,
  summaries: ReadonlyArray<api.IWorkorderSummary>
): Vector<WorkorderPlanAndSummary> {
  const workMap = new Map<string, WorkorderPlanAndSummary>();
  for (const w of workorders) {
    workMap.set(w.workorderId, { plan: w });
  }
  for (const s of summaries) {
    for (const w of s.parts) {
      if (w.name === partName) {
        const planAndS = workMap.get(s.id);
        if (planAndS) {
          workMap.set(s.id, { ...planAndS, summary: w });
        }
      }
    }
  }
  return Vector.ofIterable(workMap.values()).sortOn(w => w.plan.dueDate.getTime(), w => -w.plan.priority);
}

async function loadWorkordersForPart(part: string): Promise<Vector<WorkorderPlanAndSummary>> {
  const works = await JobsBackend.mostRecentUnfilledWorkordersForPart(part);
  const summaries: api.IWorkorderSummary[] = [];
  for (let ws of chunk(works, 16)) {
    summaries.push(...(await LogBackend.getWorkorders(ws.map(w => w.workorderId))));
  }
  return computeWorkorders(part, works, summaries);
}

export function loadWorkorders(mat: MaterialDetail): PledgeToPromise<Action> {
  return {
    type: ActionType.LoadWorkorders,
    pledge: loadWorkordersForPart(mat.partName)
  };
}

export interface AddExistingMaterialToQueueData {
  readonly materialId: number;
  readonly queue: string;
  readonly queuePosition: number;
}

export function addExistingMaterialToQueue(d: AddExistingMaterialToQueueData): PledgeToPromise<Action> {
  return {
    type: ActionType.AddNewMaterialToQueue,
    pledge: JobsBackend.setMaterialInQueue(
      d.materialId,
      new api.QueuePosition({
        queue: d.queue,
        position: d.queuePosition
      })
    )
  };
}

export interface AddNewMaterialToQueueData {
  readonly jobUnique: string;
  readonly lastCompletedProcess?: number;
  readonly queue: string;
  readonly queuePosition: number;
  readonly serial?: string;
}

export function addNewMaterialToQueue(d: AddNewMaterialToQueueData) {
  return {
    type: ActionType.AddNewMaterialToQueue,
    pledge: JobsBackend.addUnprocessedMaterialToQueue(
      d.jobUnique,
      d.lastCompletedProcess || -1,
      d.queue,
      d.queuePosition,
      d.serial || ""
    )
  };
}

export interface State {
  readonly material: MaterialDetail | null;
  readonly load_error?: Error;
  readonly update_error?: Error;
  readonly load_workorders_error?: Error;
  readonly add_mat_in_progress: boolean;
  readonly add_mat_error?: Error;
}

export const initial: State = {
  material: null,
  add_mat_in_progress: false
};

function processEvents(evts: ReadonlyArray<Readonly<api.ILogEntry>>, mat: MaterialDetail): MaterialDetail {
  let inspTypes = HashSet.ofIterable(mat.signaledInspections);
  let completedTypes = HashSet.ofIterable(mat.completedInspections);

  evts.forEach(e => {
    e.material.forEach(m => {
      if (mat.materialID < 0) {
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
      case api.LogType.PartMark:
        mat = { ...mat, serial: e.result };
        break;

      case api.LogType.OrderAssignment:
        mat = { ...mat, workorderId: e.result };
        break;

      case api.LogType.Inspection:
        if (e.result.toLowerCase() === "true" || e.result === "1") {
          const itype = (e.details || {}).InspectionType;
          if (itype) {
            inspTypes = inspTypes.add(itype);
          }
        }
        break;

      case api.LogType.InspectionForce:
        if (e.result.toLowerCase() === "true" || e.result === "1") {
          inspTypes = inspTypes.add(e.program);
        }
        break;

      case api.LogType.InspectionResult:
        completedTypes = completedTypes.add(e.program);
        break;
    }
  });

  var allEvents = mat.events.appendAll(evts).sortOn(e => e.endUTC.getTime());

  return {
    ...mat,
    signaledInspections: inspTypes.toArray({ sortOn: x => x }),
    completedInspections: completedTypes.toArray({ sortOn: x => x }),
    loading_events: false,
    events: allEvents
  };
}

export function reducer(s: State, a: Action): State {
  if (s === undefined) {
    return initial;
  }
  switch (a.type) {
    case ActionType.OpenMaterialDialog:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return { ...s, material: a.initial, load_error: undefined };

        case PledgeStatus.Completed:
          return {
            ...s,
            material: processEvents(a.pledge.result, s.material || a.initial)
          };

        case PledgeStatus.Error:
          return {
            ...s,
            material: {
              ...a.initial,
              loading_events: false,
              events: Vector.empty()
            },
            load_error: a.pledge.error
          };

        default:
          return s;
      }

    case ActionType.LoadLogFromOtherServer:
      if (a.pledge.status === PledgeStatus.Completed) {
        if (s.material) {
          return {
            ...s,
            material: {
              ...s.material,
              events: s.material.events.appendAll(a.pledge.result).sortOn(e => e.endUTC.getTime())
            }
          };
        } else {
          return s; // happens if the dialog is closed before the response arrives
        }
      } else {
        return s; // ignore starting and errors for other servers
      }

    case ActionType.OpenMaterialDialogWithoutLoad:
      return { ...s, material: a.mat };

    case ActionType.CloseMaterialDialog:
      return { ...s, material: null };

    case ActionType.UpdateMaterial:
      if (!s.material) {
        return s;
      }
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {
            ...s,
            material: {
              ...s.material,
              updating_material: true
            },
            update_error: undefined
          };
        case PledgeStatus.Completed:
          const oldMatEnd = s.material;
          return {
            ...s,
            material: {
              ...oldMatEnd,
              completedInspections: a.newCompletedInspection
                ? [...oldMatEnd.completedInspections, a.newCompletedInspection]
                : oldMatEnd.completedInspections,
              signaledInspections: a.newSignaledInspection
                ? [...oldMatEnd.signaledInspections, a.newSignaledInspection]
                : oldMatEnd.signaledInspections,
              workorderId: a.newWorkorder || oldMatEnd.workorderId,
              serial: a.newSerial || oldMatEnd.serial,
              events: a.pledge.result ? oldMatEnd.events.append(a.pledge.result) : oldMatEnd.events,
              updating_material: false
            }
          };

        case PledgeStatus.Error:
          return {
            ...s,
            material: {
              ...s.material,
              updating_material: false
            },
            update_error: a.pledge.error
          };

        default:
          return s;
      }

    case ActionType.LoadWorkorders:
      if (!s.material) {
        return s;
      }
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {
            ...s,
            material: {
              ...s.material,
              loading_workorders: true
            },
            load_workorders_error: undefined
          };

        case PledgeStatus.Completed:
          return {
            ...s,
            material: {
              ...s.material,
              loading_workorders: false,
              workorders: a.pledge.result
            }
          };

        case PledgeStatus.Error:
          return {
            ...s,
            material: {
              ...s.material,
              loading_workorders: false,
              workorders: Vector.empty()
            },
            load_workorders_error: a.pledge.error
          };

        default:
          return s;
      }

    case ActionType.AddNewMaterialToQueue:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return { ...s, add_mat_in_progress: true, add_mat_error: undefined };
        case PledgeStatus.Completed:
          return { ...s, add_mat_in_progress: false };
        case PledgeStatus.Error:
          return {
            ...s,
            add_mat_in_progress: false,
            add_mat_error: a.pledge.error
          };
        default:
          return s;
      }

    default:
      return s;
  }
}
