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

import { NOT_FOUND } from 'redux-first-router';
import { Seq } from 'immutable';

export enum RouteLocation {
  Dashboard = 'ROUTE_Dashboard',
  LoadMonitor = 'ROUTE_LoadMonitor',
  InspectionMonitor = 'ROUTE_Inspection',
  WashMonitor = 'ROUTE_Wash',
  Queues = 'ROUTE_Queues',
  AllMaterial = 'ROUTE_AllMaterial',
  CostPerPiece = 'ROUTE_CostPerPiece',
  Efficiency = 'ROUTE_Efficiency',
  DataExport = 'ROUTE_DataExport',
}

export const routeMap = {
  [RouteLocation.Dashboard]: '/',
  [RouteLocation.LoadMonitor]: '/station/loadunload/:num',
  [RouteLocation.InspectionMonitor]: '/station/inspection',
  [RouteLocation.WashMonitor]: '/station/wash',
  [RouteLocation.Queues]: '/station/queues',
  [RouteLocation.AllMaterial]: '/station/all-material',
  [RouteLocation.CostPerPiece]: '/cost',
  [RouteLocation.Efficiency]: '/efficiency',
  [RouteLocation.DataExport]: '/data-export',
};

export type Action =
  | { type: RouteLocation.Dashboard }
  | {
      type: RouteLocation.LoadMonitor,
      payload: { num: number },
      meta?: {
        query?: {
          queue?: string | ReadonlyArray<string>,
          free?: null,
        }
      },
    }
  | {
      type: RouteLocation.InspectionMonitor,
      meta?: {
        query?: {
         type?: string
        }
      },
    }
  | {
      type: RouteLocation.WashMonitor,
    }
  | {
      type: RouteLocation.Queues,
      meta?: {
        query?: {
          queue?: string | ReadonlyArray<string>,
          free?: null,
        }
      },
    }
  | { type: RouteLocation.AllMaterial }
  | { type: RouteLocation.CostPerPiece }
  | { type: RouteLocation.Efficiency }
  | { type: RouteLocation.DataExport }
  | { type: typeof NOT_FOUND }
  ;

export enum StationMonitorType {
  LoadUnload = 'StationType_LoadUnload',
  Inspection = 'StationType_Insp',
  Wash = 'StationType_Wash',
  Queues = 'StationType_Queues',
  AllMaterial = 'StationType_AllMaterial',
}

export interface State {
  readonly current: RouteLocation;
  readonly station_monitor: StationMonitorType;
  readonly selected_load_id: number;
  readonly selected_insp_type?: string;
  readonly load_queues: ReadonlyArray<string>;
  readonly load_free_material: boolean;
  readonly standalone_queues: ReadonlyArray<string>;
  readonly standalone_free_material: boolean;
}

export const initial: State = {
  current: RouteLocation.Dashboard,
  station_monitor: StationMonitorType.LoadUnload,
  selected_load_id: 1,
  selected_insp_type: undefined,
  load_queues: [],
  load_free_material: false,
  standalone_queues: [],
  standalone_free_material: false,
};

export function switchToStationMonitorPage(curSt: State): Action {
  switch (curSt.station_monitor) {
    case StationMonitorType.LoadUnload:
      return {
        type: RouteLocation.LoadMonitor,
        payload: { num: curSt.selected_load_id },
        meta: { query: { queue: curSt.load_queues, free: curSt.load_free_material ? null : undefined }},
      };

    case StationMonitorType.Inspection:
      return {
        type: RouteLocation.InspectionMonitor,
        meta: { query: {type: curSt.selected_insp_type } },
      };

    case StationMonitorType.Wash:
      return {
        type: RouteLocation.WashMonitor,
      };

    case StationMonitorType.Queues:
      return {
        type: RouteLocation.Queues,
        meta: { query: { queue: curSt.load_queues, free: curSt.load_free_material ? null : undefined }},
      };

    case StationMonitorType.AllMaterial:
      return {
        type: RouteLocation.AllMaterial,
      };
  }
}

export function displayLoadStation(
    num: number, queues: ReadonlyArray<string>, freeMaterial: boolean
  ): Action {
  return {
    type: RouteLocation.LoadMonitor,
    payload: { num },
    meta: { query: {
      queue: queues.length === 0 ? undefined : queues,
      free: freeMaterial ? null : undefined,
    } }
  };
}

export function displayInspectionType(type: string | undefined): Action {
  return {
    type: RouteLocation.InspectionMonitor,
    meta: {query: { type }},
  };
}

export function displayWash(): Action {
  return {
    type: RouteLocation.WashMonitor,
  };
}

export function displayQueues(
    queues: ReadonlyArray<string>, freeMaterial: boolean
  ): Action {
  return {
    type: RouteLocation.Queues,
    meta: { query: {
      queue: queues.length === 0 ? undefined : queues,
      free: freeMaterial ? null : undefined,
    } }
  };
}

export function displayAllMaterial(): Action {
  return { type: RouteLocation.AllMaterial };
}

export function reducer(s: State, a: Action): State {
  if ( s === undefined) { return initial; }
  switch (a.type) {
    case RouteLocation.LoadMonitor:
      const query = (a.meta || {}).query || {};
      let loadqueues: ReadonlyArray<string> = [];
      if (query.queue) {
        if (typeof query.queue === "string") {
          loadqueues = [query.queue];
        } else {
          loadqueues = query.queue;
        }
      }
      return {...s,
        current: RouteLocation.LoadMonitor,
        station_monitor: StationMonitorType.LoadUnload,
        selected_load_id: a.payload.num,
        load_queues: Seq(loadqueues).take(3).toArray(),
        load_free_material: query.free === null ? true : false
      };
    case RouteLocation.InspectionMonitor:
      var iquery = (a.meta || {}).query || {};
      return {...s,
        current: RouteLocation.InspectionMonitor,
        station_monitor: StationMonitorType.Inspection,
        selected_insp_type: iquery.type,
      };
    case RouteLocation.WashMonitor:
      return {...s,
        current: RouteLocation.WashMonitor,
        station_monitor: StationMonitorType.Wash,
      };
    case RouteLocation.Queues:
      const standalonequery = (a.meta || {}).query || {};
      let queues: ReadonlyArray<string> = [];
      if (standalonequery.queue) {
        if (typeof standalonequery.queue === "string") {
          queues = [standalonequery.queue];
        } else {
          queues = standalonequery.queue;
        }
      }
      return {...s,
        current: RouteLocation.Queues,
        station_monitor: StationMonitorType.Queues,
        standalone_queues: queues,
        standalone_free_material: standalonequery.free === null ? true : false
      };
    case RouteLocation.AllMaterial:
      return {...s,
        current: RouteLocation.AllMaterial,
        station_monitor: StationMonitorType.AllMaterial,
      };
    case RouteLocation.CostPerPiece:
      return {...s, current: RouteLocation.CostPerPiece };
    case RouteLocation.Efficiency:
      return {...s, current: RouteLocation.Efficiency };
    case RouteLocation.DataExport:
      return {...s, current: RouteLocation.DataExport };
    case RouteLocation.Dashboard:
    case NOT_FOUND:
      return {...s, current: RouteLocation.Dashboard };
    default:
      return s;
  }
}