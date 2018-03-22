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

import * as api from './api';
import { ConsumingPledge, PledgeStatus } from './pledge';
import { MaterialSummary } from './events';
import { StationMonitorType } from './routes';

export enum ActionType {
  OpenMaterialDialog = 'MaterialDetails_Open',
  CloseMaterialDialog = 'MaterialDetails_Close',
}

export interface MaterialDetail {
  readonly partName: string;
  readonly serial?: string;
  readonly workorderId?: string;
  readonly signaledInspections: ReadonlyArray<string>;
  readonly completedInspections: ReadonlyArray<string>;

  readonly loading_events: boolean;
  readonly events: ReadonlyArray<Readonly<api.ILogEntry>>;
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
      pledge: ConsumingPledge<ReadonlyArray<Readonly<api.ILogEntry>>>
    }
  ;

export function openLoadunloadMaterialDialog(mat: Readonly<api.IInProcessMaterial>) {
  var client = new api.LogClient();
  return {
    type: ActionType.OpenMaterialDialog,
    station: StationMonitorType.LoadUnload,
    initial: {
      partName: mat.partName,
      serial: mat.serial,
      workorderId: mat.workorderId,
      signaledInspections: mat.signaledInspections,
      completedInspections: [],
      loading_events: true,
      events: [],
    } as MaterialDetail,
    pledge: client.logForMaterial(mat.materialID),
  };
}

export function openInspectionMaterial(mat: MaterialSummary) {
  var client = new api.LogClient();
  return {
    type: ActionType.OpenMaterialDialog,
    station: StationMonitorType.Inspection,
    initial: {
      partName: mat.partName,
      serial: mat.serial,
      workorderId: mat.workorderId,
      signaledInspections: mat.signaledInspections,
      completedInspections: mat.completedInspections,
      loading_events: true,
      events: [],
    } as MaterialDetail,
    pledge: client.logForMaterial(mat.materialID),
  };
}

export function openWashMaterial(mat: MaterialSummary) {
  var client = new api.LogClient();
  return {
    type: ActionType.OpenMaterialDialog,
    station: StationMonitorType.Wash,
    initial: {
      partName: mat.partName,
      serial: mat.serial,
      workorderId: mat.workorderId,
      signaledInspections: mat.signaledInspections,
      completedInspections: mat.completedInspections,
      loading_events: true,
      events: [],
    } as MaterialDetail,
    pledge: client.logForMaterial(mat.materialID),
  };
}

export interface State {
  readonly loadstation_display_material?: MaterialDetail;
  readonly inspection_display_material?: MaterialDetail;
  readonly wash_display_material?: MaterialDetail;
}

export const initial: State = {};

function setEvents(e: ReadonlyArray<Readonly<api.ILogEntry>>, d: MaterialDetail): MaterialDetail {
  return {...d, loading_events: false, events: e};
}

export function reducer(s: State, a: Action): State {
  if (s === undefined) { return initial; }
  switch (a.type) {
    case ActionType.OpenMaterialDialog:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:

          switch (a.station) {
            case StationMonitorType.LoadUnload:
              return {...s, loadstation_display_material: a.initial};
            case StationMonitorType.Inspection:
              return {...s, inspection_display_material: a.initial};
            case StationMonitorType.Wash:
              return {...s, wash_display_material: a.initial};
            default: return s;
          }

        case PledgeStatus.Completed:
          switch (a.station) {
            case StationMonitorType.LoadUnload:
              return {...s, loadstation_display_material: setEvents(a.pledge.result, a.initial)};
            case StationMonitorType.Inspection:
              return {...s, inspection_display_material: setEvents(a.pledge.result, a.initial)};
            case StationMonitorType.Wash:
              return {...s, wash_display_material: setEvents(a.pledge.result, a.initial)};
            default: return s;
          }

        case PledgeStatus.Error:
          switch (a.station) {
            case StationMonitorType.LoadUnload:
              return {...s, loadstation_display_material: setEvents([], a.initial)};
            case StationMonitorType.Inspection:
              return {...s, inspection_display_material: setEvents([], a.initial)};
            case StationMonitorType.Wash:
              return {...s, wash_display_material: setEvents([], a.initial)};
            default: return s;
          }

        default:
          return s;
      }

    case ActionType.CloseMaterialDialog:
      switch (a.station) {
        case StationMonitorType.LoadUnload:
          return {...s, loadstation_display_material: undefined};
        case StationMonitorType.Inspection:
          return {...s, inspection_display_material: undefined};
        case StationMonitorType.Wash:
          return {...s, wash_display_material: undefined};
        default: return s;
      }

    default:
      return s;
  }
}