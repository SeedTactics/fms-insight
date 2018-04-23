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

import * as im from 'immutable';

import * as api from './api';
import { Pledge, PledgeStatus, ActionBeforeMiddleware } from '../store/middleware';
import { MaterialSummary } from './events';

export enum ActionType {
  OpenMaterialDialog = 'MaterialDetails_Open',
  CloseMaterialDialog = 'MaterialDetails_Close',
  UpdateMaterial = 'MaterialDetails_UpdateMaterial',
  LoadWorkorders = 'OrderAssign_LoadWorkorders',
  AddNewMaterialToQueue = 'MaterialDetails_AddNewMaterialToQueue',
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

  readonly updating_material: boolean;

  readonly loading_events: boolean;
  readonly events: ReadonlyArray<Readonly<api.ILogEntry>>;

  readonly loading_workorders: boolean;
  readonly workorders: ReadonlyArray<WorkorderPlanAndSummary>;
}

export type Action =
  | {
      type: ActionType.CloseMaterialDialog,
    }
  | {
      type: ActionType.OpenMaterialDialog,
      initial: MaterialDetail,
      pledge: Pledge<ReadonlyArray<Readonly<api.ILogEntry>>>
    }
  | {
      type: ActionType.UpdateMaterial,
      newInspType?: string,
      newWorkorder?: string,
      pledge: Pledge<Readonly<api.ILogEntry> | undefined>,
    }
  | {
      type: ActionType.LoadWorkorders,
      pledge: Pledge<ReadonlyArray<WorkorderPlanAndSummary>>,
    }
  | {
      type: ActionType.AddNewMaterialToQueue,
      pledge: Pledge<void>,
    }
  ;

type ABF = ActionBeforeMiddleware<Action>;

export function openMaterialDialog(mat: Readonly<MaterialSummary>):  ABF {
  const client = new api.LogClient();
  return {
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
      events: [],
      loading_workorders: false,
      saving_workorder: false,
      workorders: [],
    } as MaterialDetail,
    pledge: client.logForMaterial(mat.materialID),
  };
}

export function openMaterialBySerial(serial: string): ABF {
  const client = new api.LogClient();
  return {
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
      events: [],
      loading_workorders: false,
      saving_workorder: false,
      workorders: [],
    } as MaterialDetail,
    pledge: client.logForSerial(serial),
  };
}

export interface CompleteInspectionData {
  readonly mat: MaterialDetail;
  readonly inspType: string;
  readonly success: boolean;
  readonly operator?: string;
}

export function completeInspection({mat, inspType, success, operator}: CompleteInspectionData): ABF {
  const client = new api.LogClient();
  return {
    type: ActionType.UpdateMaterial,
    newInspType: inspType,
    pledge: client.recordInspectionCompleted(new api.NewInspectionCompleted({
      material: new api.LogMaterial({
        id: mat.materialID,
        uniq: mat.jobUnique,
        part: mat.partName,
        proc: 1,
        numproc: 1,
        face: "1",
      }),
      inspectionLocationNum: 1,
      inspectionType: inspType,
      success,
      active: 'PT0S',
      elapsed: 'PT0S',
      extraData: operator ? {operator} : undefined
    }))
  };
}

export interface CompleteWashData {
  readonly mat: MaterialDetail;
  readonly operator?: string;
}

export function completeWash(d: CompleteWashData): ABF {
  const client = new api.LogClient();
  return {
    type: ActionType.UpdateMaterial,
    pledge: client.recordWashCompleted(new api.NewWash({
      material: new api.LogMaterial({
        id: d.mat.materialID,
        uniq: d.mat.jobUnique,
        part: d.mat.partName,
        proc: 1,
        numproc: 1,
        face: "1",
      }),
      washLocationNum: 1,
      active: 'PT0S',
      elapsed: 'PT0S',
      extraData: d.operator ? {operator: d.operator} : undefined
    }))
  };
}

export function removeFromQueue(mat: MaterialDetail): ABF {
  const client = new api.JobsClient();
  return {
    type: ActionType.UpdateMaterial,
    pledge: client.removeMaterialFromAllQueues(mat.materialID).then(() => undefined)
  };
}

export interface AssignWorkorderData {
  readonly mat: MaterialDetail;
  readonly workorder: string;
}

export function assignWorkorder({mat, workorder}: AssignWorkorderData): ABF {
  const client = new api.LogClient();
  return {
    type: ActionType.UpdateMaterial,
    newWorkorder: workorder,
    pledge: client.setWorkorder(
      workorder,
      new api.LogMaterial({
        id: mat.materialID,
        uniq: mat.jobUnique,
        part: mat.partName,
        proc: 1,
        numproc: 1,
        face: "1",
      })
    )
  };
}

export function computeWorkorders(
    partName: string,
    workorders: ReadonlyArray<api.PartWorkorder>,
    summaries: ReadonlyArray<api.WorkorderSummary>): ReadonlyArray<WorkorderPlanAndSummary> {

  const workMap = new Map<string, WorkorderPlanAndSummary>();
  for (const w of workorders) {
    workMap.set(w.workorderId, {plan: w});
  }
  for (const s of summaries) {
    for (const w of s.parts) {
      if (w.name === partName) {
        const planAndS = workMap.get(s.id);
        if (planAndS) {
          workMap.set(s.id, {...planAndS, summary: w});
        }
      }
    }
  }
  return im.Seq.Keyed(workMap)
    .valueSeq()
    .sortBy(w => [w.plan.dueDate, -w.plan.priority])
    .toArray();
}

export function loadWorkorders(mat: MaterialDetail): ABF {
  const logClient = new api.LogClient();
  const jobClient = new api.JobsClient();

  return {
    type: ActionType.LoadWorkorders,
    pledge:
      jobClient.mostRecentUnfilledWorkordersForPart(mat.partName)
      .then(workorders => {
        return logClient.getWorkorders(workorders.map(w => w.workorderId))
          .then(summaries => {
            return computeWorkorders(mat.partName, workorders, summaries);
          });
      })
  };
}

export interface AddExistingMaterialToQueueData {
  readonly materialId: number;
  readonly queue: string;
  readonly queuePosition: number;
}

export function addExistingMaterialToQueue(d: AddExistingMaterialToQueueData): ABF {
  const client = new api.JobsClient();
  return {
    type: ActionType.AddNewMaterialToQueue,
    pledge: client.setMaterialInQueue(d.materialId, new api.QueuePosition({
      queue: d.queue,
      position: d.queuePosition
    })),
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
  const client = new api.JobsClient();
  return {
    type: ActionType.AddNewMaterialToQueue,
    pledge: client.addUnprocessedMaterialToQueue(
      d.jobUnique, d.lastCompletedProcess || -1, d.queue, d.queuePosition, d.serial || ""
    )
  };
}

export interface State {
  readonly material: MaterialDetail | null;
  readonly load_error?: Error;
  readonly update_error?: Error;
  readonly add_mat_in_progress: boolean;
  readonly add_mat_error?: Error;
}

export const initial: State = {
  material: null,
  add_mat_in_progress: false,
};

function processEvents(evts: ReadonlyArray<Readonly<api.ILogEntry>>, mat: MaterialDetail): MaterialDetail {
  let inspTypes = im.Set(mat.signaledInspections);
  let completedTypes = im.Set(mat.completedInspections);

  evts.forEach(e => {
    e.material.forEach(m => {
      if (mat.materialID < 0) {
        mat = {...mat, materialID: m.id};
      }
      if (mat.partName === "") {
        mat = {...mat, partName: m.part};
      }
      if (mat.jobUnique === "") {
        mat = {...mat, jobUnique: m.uniq};
      }
    });

    switch (e.type) {
      case api.LogType.PartMark:
        mat = {...mat, serial: e.result};
        break;

      case api.LogType.OrderAssignment:
        mat = {...mat, workorderId: e.result};
        break;

      case api.LogType.Inspection:
        if (e.result.toLowerCase() === "true" || e.result === "1") {
          const entries = e.program.split(",");
          if (entries.length >= 2) {
            inspTypes = inspTypes.add(entries[1]);
          }
        }
        break;

      case api.LogType.InspectionResult:
        completedTypes = completedTypes.add(e.program);
        break;

    }
  });

  return {...mat,
    signaledInspections: inspTypes.toSeq().sort().toArray(),
    completedInspections: completedTypes.toSeq().sort().toArray(),
    loading_events: false,
    events: evts,
  };
}

export function reducer(s: State, a: Action): State {
  if (s === undefined) { return initial; }
  switch (a.type) {
    case ActionType.OpenMaterialDialog:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {...s, material: a.initial, load_error: undefined};

        case PledgeStatus.Completed:
          return {...s, material: processEvents(a.pledge.result, a.initial)};

        case PledgeStatus.Error:
          return {...s,
            material: {...a.initial,
              loading_events: false,
              events: [],
            },
            load_error: a.pledge.error,
          };

        default:
          return s;
      }

    case ActionType.CloseMaterialDialog:
      return {...s, material: null};

    case ActionType.UpdateMaterial:
      if (!s.material) { return s; }
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {...s,
            material: {...s.material,
              updating_material: true
            },
            update_error: undefined,
          };
        case PledgeStatus.Completed:
          const oldMatEnd = s.material;
          return {...s, material: {...oldMatEnd,
              completedInspections:
                a.newInspType ? [...oldMatEnd.completedInspections, a.newInspType] : oldMatEnd.completedInspections,
              workorderId: a.newWorkorder || oldMatEnd.workorderId,
              events: a.pledge.result ? [...oldMatEnd.events, a.pledge.result] : oldMatEnd.events,
              updating_material: false,
            },
          };

        case PledgeStatus.Error:
          return {...s, material:
            {...s.material,
              updating_material: false
            },
            update_error: a.pledge.error,
          };

        default: return s;
      }

    case ActionType.LoadWorkorders:
      if (!s.material) { return s; }
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {...s, material: {...s.material,
            loading_workorders: true
          }};

        case PledgeStatus.Completed:
          return {...s, material: {...s.material,
            loading_workorders: false,
            workorders: a.pledge.result,
          }};

        case PledgeStatus.Error:
          return {...s, material: {...s.material,
            loading_workorders: false,
            workorders: [],
          }};

        default:
          return s;
      }

    case ActionType.AddNewMaterialToQueue:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {...s, add_mat_in_progress: true, add_mat_error: undefined};
        case PledgeStatus.Completed:
          return {...s, add_mat_in_progress: false };
        case PledgeStatus.Error:
          return {...s, add_mat_in_progress: false, add_mat_error: a.pledge.error };
        default: return s;
      }

    default:
      return s;
  }
}