/* Copyright (c) 2019, John Lenz

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

import { NOT_FOUND } from "redux-first-router";

export enum RouteLocation {
  ChooseMode = "ROUTE_ChooseMode",

  Station_LoadMonitor = "ROUTE_Station_LoadMonitor",
  Station_InspectionMonitor = "ROUTE_Station_Inspection",
  Station_WashMonitor = "ROUTE_Station_Wash",
  Station_Queues = "ROUTE_Station_Queues",

  Operations_Dashboard = "ROUTE_Operations_Dashboard",
  Operations_LoadStation = "ROUTE_Operations_LoadStation",
  Operations_Machines = "ROUTE_Operations_Machines",
  Operations_AllMaterial = "ROUTE_Operations_AllMaterial",
  Operations_CompletedParts = "ROUTE_Operations_CompletedParts",

  Engineering = "ROUTE_Engineering",

  Quality_Dashboard = "ROUTE_Quality_Dashboard",
  Quality_Serials = "ROUTE_Quality_Serials",
  Quality_Paths = "ROUTE_Quality_Paths",
  Quality_Quarantine = "ROUTE_Quality_Quarantine",

  Tools_Dashboard = "ROUTE_Tools_Dashboard",

  Analysis_Efficiency = "ROUTE_Analysis_Efficiency",
  Analysis_CostPerPiece = "ROUTE_Analysis_CostPerPiece",
  Analysis_DataExport = "ROUTE_Analysis_DataExport",
}

export const routeMap = {
  [RouteLocation.ChooseMode]: "/",

  [RouteLocation.Station_LoadMonitor]: "/station/loadunload/:num",
  [RouteLocation.Station_InspectionMonitor]: "/station/inspection",
  [RouteLocation.Station_WashMonitor]: "/station/wash",
  [RouteLocation.Station_Queues]: "/station/queues",

  [RouteLocation.Operations_Dashboard]: "/operations",
  [RouteLocation.Operations_LoadStation]: "/operations/loadunload",
  [RouteLocation.Operations_Machines]: "/operations/machines",
  [RouteLocation.Operations_AllMaterial]: "/operations/material",
  [RouteLocation.Operations_CompletedParts]: "/operations/completed",

  [RouteLocation.Engineering]: "/engineering",

  [RouteLocation.Quality_Dashboard]: "/quality",
  [RouteLocation.Quality_Serials]: "/quality/serials",
  [RouteLocation.Quality_Paths]: "/quality/paths",
  [RouteLocation.Quality_Quarantine]: "/quality/quarantine",

  [RouteLocation.Tools_Dashboard]: "/tools",

  [RouteLocation.Analysis_Efficiency]: "/analysis/efficiency",
  [RouteLocation.Analysis_CostPerPiece]: "/analysis/cost",
  [RouteLocation.Analysis_DataExport]: "/analysis/data-export",
};

export type Action =
  | { type: RouteLocation.ChooseMode }
  | {
      type: RouteLocation.Station_LoadMonitor;
      payload: { num: number | string };
      meta?: {
        query?: {
          queue?: string | ReadonlyArray<string>;
          free?: null;
        };
      };
    }
  | {
      type: RouteLocation.Station_InspectionMonitor;
      meta?: {
        query?: {
          type?: string;
        };
      };
    }
  | {
      type: RouteLocation.Station_WashMonitor;
    }
  | {
      type: RouteLocation.Station_Queues;
      meta?: {
        query?: {
          queue?: string | ReadonlyArray<string>;
          free?: null;
        };
      };
    }
  | { type: RouteLocation.Operations_Dashboard }
  | { type: RouteLocation.Operations_LoadStation }
  | { type: RouteLocation.Operations_Machines }
  | { type: RouteLocation.Operations_AllMaterial }
  | { type: RouteLocation.Operations_CompletedParts }
  | { type: RouteLocation.Engineering }
  | { type: RouteLocation.Quality_Dashboard }
  | { type: RouteLocation.Quality_Serials }
  | { type: RouteLocation.Quality_Paths }
  | { type: RouteLocation.Quality_Quarantine }
  | { type: RouteLocation.Tools_Dashboard }
  | { type: RouteLocation.Analysis_Efficiency }
  | { type: RouteLocation.Analysis_CostPerPiece }
  | { type: RouteLocation.Analysis_DataExport }
  | { type: typeof NOT_FOUND };

export interface State {
  readonly current: RouteLocation;
  readonly selected_load_id: number;
  readonly selected_insp_type?: string;
  readonly load_queues: ReadonlyArray<string>;
  readonly load_free_material: boolean;
  readonly standalone_queues: ReadonlyArray<string>;
  readonly standalone_free_material: boolean;
}

export const initial: State = {
  current: RouteLocation.ChooseMode,
  selected_load_id: 1,
  selected_insp_type: undefined,
  load_queues: [],
  load_free_material: false,
  standalone_queues: [],
  standalone_free_material: false,
};

export function displayLoadStation(num: number, queues: ReadonlyArray<string>, freeMaterial: boolean): Action {
  return {
    type: RouteLocation.Station_LoadMonitor,
    payload: { num },
    meta: {
      query: {
        queue: queues.length === 0 ? undefined : queues,
        free: freeMaterial ? null : undefined,
      },
    },
  };
}

export function displayInspectionType(type: string | undefined): Action {
  return {
    type: RouteLocation.Station_InspectionMonitor,
    meta: { query: { type } },
  };
}

export function displayWash(): Action {
  return {
    type: RouteLocation.Station_WashMonitor,
  };
}

export function displayQueues(queues: ReadonlyArray<string>, freeMaterial: boolean): Action {
  return {
    type: RouteLocation.Station_Queues,
    meta: {
      query: {
        queue: queues.length === 0 ? undefined : queues,
        free: freeMaterial ? null : undefined,
      },
    },
  };
}

export function displayPage(ty: RouteLocation, oldSt: State): Action {
  switch (ty) {
    case RouteLocation.Station_LoadMonitor:
      return displayLoadStation(oldSt.selected_load_id, oldSt.load_queues, oldSt.load_free_material);
    case RouteLocation.ChooseMode:
      return { type: NOT_FOUND };
    case RouteLocation.Station_InspectionMonitor:
      return displayInspectionType(oldSt.selected_insp_type);
    case RouteLocation.Station_Queues:
      return displayQueues(oldSt.standalone_queues, oldSt.standalone_free_material);
    default:
      return { type: ty } as Action;
  }
}

export function reducer(s: State, a: Action): State {
  if (s === undefined) {
    return initial;
  }
  switch (a.type) {
    case RouteLocation.Station_LoadMonitor: {
      const query = (a.meta || {}).query || {};
      let loadqueues: ReadonlyArray<string> = [];
      if (query.queue) {
        if (typeof query.queue === "string") {
          loadqueues = [query.queue];
        } else {
          loadqueues = query.queue;
        }
      }
      return {
        ...s,
        current: RouteLocation.Station_LoadMonitor,
        selected_load_id: typeof a.payload.num === "string" ? parseInt(a.payload.num, 10) : a.payload.num,
        load_queues: loadqueues.slice(0, 3),
        load_free_material: query.free === null ? true : false,
      };
    }
    case RouteLocation.Station_InspectionMonitor: {
      const iquery = (a.meta || {}).query || {};
      return {
        ...s,
        current: RouteLocation.Station_InspectionMonitor,
        selected_insp_type: iquery.type,
      };
    }
    case RouteLocation.Station_WashMonitor:
      return {
        ...s,
        current: RouteLocation.Station_WashMonitor,
      };
    case RouteLocation.Station_Queues: {
      const standalonequery = (a.meta || {}).query || {};
      let queues: ReadonlyArray<string> = [];
      if (standalonequery.queue) {
        if (typeof standalonequery.queue === "string") {
          queues = [standalonequery.queue];
        } else {
          queues = standalonequery.queue;
        }
      }
      return {
        ...s,
        current: RouteLocation.Station_Queues,
        standalone_queues: queues,
        standalone_free_material: standalonequery.free === null ? true : false,
      };
    }

    case RouteLocation.Operations_Dashboard:
    case RouteLocation.Operations_AllMaterial:
    case RouteLocation.Operations_LoadStation:
    case RouteLocation.Operations_Machines:
    case RouteLocation.Operations_CompletedParts:
    case RouteLocation.Engineering:
    case RouteLocation.Quality_Dashboard:
    case RouteLocation.Quality_Serials:
    case RouteLocation.Quality_Paths:
    case RouteLocation.Quality_Quarantine:
    case RouteLocation.Tools_Dashboard:
    case RouteLocation.Analysis_CostPerPiece:
    case RouteLocation.Analysis_Efficiency:
    case RouteLocation.Analysis_DataExport:
      return {
        ...s,
        current: a.type,
      };

    case RouteLocation.ChooseMode:
    case NOT_FOUND:
      return { ...s, current: RouteLocation.ChooseMode };
    default:
      return s;
  }
}
