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

import * as routes from './routes';

it("has the initial state", () => {
  // tslint:disable-next-line:no-any
  let s = routes.reducer(undefined as any, undefined as any);
  expect(s).toBe(routes.initial);
});

it("changes to the station page", () => {
  let st = routes.reducer(routes.initial, {
    type: routes.RouteLocation.WashMonitor,
  });

  expect(st).toEqual({
    current: routes.RouteLocation.WashMonitor,
    station_monitor: routes.StationMonitorType.Wash,
    selected_load_id: 1,
    station_queues: [],
    station_free_material: false,
  });

  // now one with queues
  st = routes.reducer(st, {
    type: routes.RouteLocation.LoadMonitor,
    payload: {
      num: 4,
    },
    meta: {
      query: {
        queue: ["z", "y", "x"]
      }
    },
  });

  expect(st).toEqual({
    current: routes.RouteLocation.LoadMonitor,
    station_monitor: routes.StationMonitorType.LoadUnload,
    selected_load_id: 4,
    station_queues: ["z", "y", "x"],
    station_free_material: false,
  });

  // now with free material
  st = routes.reducer(st, {
    type: routes.RouteLocation.LoadMonitor,
    payload: {
      num: 6,
    },
    meta: {
      query: {
        queue: ["w"],
        free: null
      }
    },
  });

  expect(st).toEqual({
    current: routes.RouteLocation.LoadMonitor,
    station_monitor: routes.StationMonitorType.LoadUnload,
    selected_load_id: 6,
    station_queues: ["w"],
    station_free_material: true,
  });

});

it("transitions to the cost/piece, dashboard, and efficiency pages", () => {
  const pages = [
    routes.RouteLocation.CostPerPiece,
    routes.RouteLocation.Dashboard,
    routes.RouteLocation.Efficiency
  ];
  const initialSt = {
    current: routes.RouteLocation.LoadMonitor,
    station_monitor: routes.StationMonitorType.LoadUnload,
    selected_load_id: 2,
    station_queues: ["a", "b"],
    station_free_material: false,
  };
  for (var page of pages) {
    // tslint:disable-next-line:no-any
    let st = routes.reducer(initialSt, { type: page as any });
    expect(st).toEqual({...initialSt, current: page});
  }
});