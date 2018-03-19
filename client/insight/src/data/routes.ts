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
  CostPerPiece = 'ROUTE_CostPerPiece',
  Efficiency = 'ROUTE_Efficiency'
}

export const routeMap = {
  [RouteLocation.Dashboard]: '/',
  [RouteLocation.LoadMonitor]: '/station/loadunload/:num',
  [RouteLocation.InspectionMonitor]: '/station/inspection',
  [RouteLocation.WashMonitor]: '/station/wash',
  [RouteLocation.CostPerPiece]: '/cost',
  [RouteLocation.Efficiency]: '/efficiency',
};

export type Action =
  | { type: RouteLocation.Dashboard }
  | {
      type: RouteLocation.LoadMonitor,
      payload: { num: number },
      meta?: {
        query?: {
          queue?: ReadonlyArray<string>,
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
  | { type: RouteLocation.CostPerPiece }
  | { type: RouteLocation.Efficiency }
  | { type: typeof NOT_FOUND }
  ;

export enum StationMonitorType {
  LoadUnload = 'StationType_LoadUnload',
  Inspection = 'StationType_Insp',
  Wash = 'StationType_Wash',
}

export interface State {
  readonly current: RouteLocation;
  readonly station_monitor: StationMonitorType;
  readonly selected_load_id: number;
  readonly selected_insp_type?: string;
  readonly station_queues: ReadonlyArray<string>;
  readonly station_free_material: boolean;
}

export const initial: State = {
  current: RouteLocation.Dashboard,
  station_monitor: StationMonitorType.LoadUnload,
  selected_load_id: 1,
  selected_insp_type: undefined,
  station_queues: [],
  station_free_material: false,
};

export function switchToStationMonitorPage(curSt: State): Action {
  switch (curSt.station_monitor) {
    case StationMonitorType.LoadUnload:
      return {
        type: RouteLocation.LoadMonitor,
        payload: { num: curSt.selected_load_id },
        meta: { query: { queue: curSt.station_queues, free: curSt.station_free_material ? null : undefined }},
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

export function reducer(s: State, a: Action): State {
  if ( s === undefined) { return initial; }
  switch (a.type) {
    case RouteLocation.LoadMonitor:
      var query = (a.meta || {}).query || {};
      return {...s,
        current: RouteLocation.LoadMonitor,
        station_monitor: StationMonitorType.LoadUnload,
        selected_load_id: a.payload.num,
        station_queues: Seq(query.queue || []).take(3).toArray(),
        station_free_material: query.free === null ? true : false
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
    case RouteLocation.CostPerPiece:
      return {...s, current: RouteLocation.CostPerPiece };
    case RouteLocation.Efficiency:
      return {...s, current: RouteLocation.Efficiency };
    case RouteLocation.Dashboard:
    case NOT_FOUND:
      return {...s, current: RouteLocation.Dashboard };
    default:
      return s;
  }
}