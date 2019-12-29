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

import * as routes from "./routes";

it("has the initial state", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const s = routes.reducer(undefined as any, undefined as any);
  expect(s).toBe(routes.initial);
});

it("changes to the station page", () => {
  let st = routes.reducer(routes.initial, {
    type: routes.RouteLocation.Station_WashMonitor
  });

  expect(st).toEqual({
    current: routes.RouteLocation.Station_WashMonitor,
    selected_load_id: 1,
    load_queues: [],
    load_free_material: false,
    standalone_queues: [],
    standalone_free_material: false
  });

  // now one with load queues
  st = routes.reducer(st, {
    type: routes.RouteLocation.Station_LoadMonitor,
    payload: {
      num: 4
    },
    meta: {
      query: {
        queue: ["z", "y", "x"]
      }
    }
  });

  expect(st).toEqual({
    current: routes.RouteLocation.Station_LoadMonitor,
    selected_load_id: 4,
    load_queues: ["z", "y", "x"],
    load_free_material: false,
    standalone_queues: [],
    standalone_free_material: false
  });

  // now with free material
  st = routes.reducer(st, {
    type: routes.RouteLocation.Station_LoadMonitor,
    payload: {
      num: 6
    },
    meta: {
      query: {
        queue: ["w"],
        free: null
      }
    }
  });

  expect(st).toEqual({
    current: routes.RouteLocation.Station_LoadMonitor,
    selected_load_id: 6,
    load_queues: ["w"],
    load_free_material: true,
    standalone_queues: [],
    standalone_free_material: false
  });

  // now with standalone queues
  st = routes.reducer(st, {
    type: routes.RouteLocation.Station_Queues,
    meta: {
      query: {
        queue: ["a", "b", "c"]
      }
    }
  });

  expect(st).toEqual({
    current: routes.RouteLocation.Station_Queues,
    selected_load_id: 6,
    load_queues: ["w"],
    load_free_material: true,
    standalone_queues: ["a", "b", "c"],
    standalone_free_material: false
  });

  // now with free material
  st = routes.reducer(st, {
    type: routes.RouteLocation.Station_Queues,
    meta: {
      query: {
        queue: ["d"],
        free: null
      }
    }
  });

  expect(st).toEqual({
    current: routes.RouteLocation.Station_Queues,
    selected_load_id: 6,
    load_queues: ["w"],
    load_free_material: true,
    standalone_queues: ["d"],
    standalone_free_material: true
  });
});

it("transitions to the cost/piece, dashboard, and efficiency pages", () => {
  const pages = [
    routes.RouteLocation.Operations_Dashboard,
    routes.RouteLocation.Operations_LoadStation,
    routes.RouteLocation.Operations_Machines,
    routes.RouteLocation.Operations_AllMaterial,
    routes.RouteLocation.Operations_CompletedParts,

    routes.RouteLocation.Engineering,

    routes.RouteLocation.Quality_Dashboard,
    routes.RouteLocation.Quality_Serials,
    routes.RouteLocation.Quality_Paths,
    routes.RouteLocation.Quality_Quarantine,

    routes.RouteLocation.Tools_Dashboard,

    routes.RouteLocation.Analysis_Efficiency,
    routes.RouteLocation.Analysis_CostPerPiece,
    routes.RouteLocation.Analysis_DataExport
  ];
  const initialSt = {
    current: routes.RouteLocation.Station_LoadMonitor,
    selected_load_id: 2,
    load_queues: ["a", "b"],
    load_free_material: false,
    standalone_queues: [],
    standalone_free_material: false
  };
  for (const page of pages) {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const st = routes.reducer(initialSt, { type: page as any });
    expect(st).toEqual({ ...initialSt, current: page });
  }
});
