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
import { Pledge, PledgeStatus, PledgeToPromise } from './pledge';
import { MaterialSummary } from './events';
import { StationMonitorType } from './routes';

export enum ActionType {
  OpenMaterialDialog = 'MaterialDetails_Open',
  CloseMaterialDialog = 'MaterialDetails_Close',
  UpdateMaterial = 'MaterialDetails_UpdateMaterial',
  LoadWorkorders = 'OrderAssign_LoadWorkorders',
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
      station: StationMonitorType
    }
  | {
      type: ActionType.OpenMaterialDialog,
      station: StationMonitorType,
      initial: MaterialDetail,
      pledge: Pledge<ReadonlyArray<Readonly<api.ILogEntry>>>
    }
  | {
      type: ActionType.UpdateMaterial,
      station: StationMonitorType,
      newInspType?: string,
      newWorkorder?: string,
      pledge: Pledge<Readonly<api.ILogEntry>>,
    }
  | {
      type: ActionType.LoadWorkorders,
      station: StationMonitorType,
      pledge: Pledge<ReadonlyArray<WorkorderPlanAndSummary>>,
    }
  ;

type ActionToDispatch = PledgeToPromise<Action>;

export function openLoadunloadMaterialDialog(mat: Readonly<api.IInProcessMaterial>): ActionToDispatch {
  const client = new api.LogClient();
  return {
    type: ActionType.OpenMaterialDialog,
    station: StationMonitorType.LoadUnload,
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

export function openInspectionMaterial(mat: MaterialSummary): ActionToDispatch {
  const client = new api.LogClient();
  return {
    type: ActionType.OpenMaterialDialog,
    station: StationMonitorType.Inspection,
    initial: {
      materialID: mat.materialID,
      partName: mat.partName,
      jobUnique: mat.jobUnique,
      serial: mat.serial,
      workorderId: mat.workorderId,
      signaledInspections: mat.signaledInspections,
      completedInspections: mat.completedInspections,
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

export function openWashMaterial(mat: MaterialSummary): ActionToDispatch {
  const client = new api.LogClient();
  return {
    type: ActionType.OpenMaterialDialog,
    station: StationMonitorType.Wash,
    initial: {
      materialID: mat.materialID,
      partName: mat.partName,
      jobUnique: mat.jobUnique,
      serial: mat.serial,
      workorderId: mat.workorderId,
      signaledInspections: mat.signaledInspections,
      completedInspections: mat.completedInspections,
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

export function completeInspection(mat: MaterialDetail, inspType: string, success: boolean): ActionToDispatch {
  const client = new api.LogClient();
  return {
    type: ActionType.UpdateMaterial,
    station: StationMonitorType.Inspection,
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
      active: '0:0:0',
      elapsed: '0:0:0',
    }))
  };
}

export function completeWash(mat: MaterialDetail): ActionToDispatch {
  const client = new api.LogClient();
  return {
    type: ActionType.UpdateMaterial,
    station: StationMonitorType.Wash,
    pledge: client.recordWashCompleted(new api.NewWash({
      material: new api.LogMaterial({
        id: mat.materialID,
        uniq: mat.jobUnique,
        part: mat.partName,
        proc: 1,
        numproc: 1,
        face: "1",
      }),
      washLocationNum: 1,
      active: '0:0:0',
      elapsed: '0:0:0',
    }))
  };
}

export function assignWorkorder(mat: MaterialDetail, station: StationMonitorType, workorder: string): ActionToDispatch {
  const client = new api.LogClient();
  return {
    type: ActionType.UpdateMaterial,
    station,
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

export function loadWorkorders(mat: MaterialDetail, station: StationMonitorType) {
  const logClient = new api.LogClient();
  const jobClient = new api.JobsClient();

  return {
    type: ActionType.LoadWorkorders,
    station,
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

export interface State {
  readonly material: {[key in StationMonitorType]: MaterialDetail | null };
}

export const initial: State = {
  material: {
    StationType_Insp: null,
    StationType_LoadUnload: null,
    StationType_Wash: null,
  }
};

export function reducer(s: State, a: Action): State {
  if (s === undefined) { return initial; }
  switch (a.type) {
    case ActionType.OpenMaterialDialog:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {...s, material: {...s.material, [a.station]: a.initial}};

        case PledgeStatus.Completed:
          return {...s, material: {...s.material, [a.station]: {...a.initial,
            loading_events: false,
            events: a.pledge.result
          }}};

        case PledgeStatus.Error:
          return {...s, material: {...s.material, [a.station]: {...a.initial,
            loading_events: false,
            events: [],
          }}};

        default:
          return s;
      }

    case ActionType.CloseMaterialDialog:
      return {...s, material: {...s.material, [a.station]: null}};

    case ActionType.UpdateMaterial:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          const oldMatStart = s.material[a.station];
          if (oldMatStart === null) {
            return s;
          } else {
            return {...s, material: {...s.material, [a.station]: {...oldMatStart,
              updating_material: true}
            }};
          }
        case PledgeStatus.Completed:
          const oldMatEnd = s.material[a.station];
          if (oldMatEnd === null) {
            return s;
          } else {
            return {...s, material: {...s.material, [a.station]: {...oldMatEnd,
                completedInspections:
                  a.newInspType ? [...oldMatEnd.completedInspections, a.newInspType] : oldMatEnd.completedInspections,
                workorderId: a.newWorkorder || oldMatEnd.workorderId,
                events: [...oldMatEnd.events, a.pledge.result],
                updating_material: false,
              },
            }};
          }

        case PledgeStatus.Error:
          const oldMatErr = s.material[a.station];
          if (oldMatErr === undefined) {
            return s;
          } else {
            return {...s, material: {...s.material, [a.station]: {...oldMatErr,
              updating_material: false}
            }};
          }

        default: return s;
      }

    case ActionType.LoadWorkorders:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {...s, material: {...s.material, [a.station]: {...s.material[a.station],
            loading_workorders: true
          }}};

        case PledgeStatus.Completed:
          return {...s, material: {...s.material, [a.station]: {...s.material[a.station],
            loading_workorders: false,
            workorders: a.pledge.result,
          }}};

        case PledgeStatus.Error:
          return {...s, material: {...s.material, [a.station]: {...s.material[a.station],
            loading_workorders: false,
            workorders: [],
          }}};

        default:
          return s;
      }

    default:
      return s;
  }
}