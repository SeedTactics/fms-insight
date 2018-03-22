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

import * as mat from './material-details';
import { PledgeStatus } from './pledge';
import { StationMonitorType } from './routes';
// import * as api from './api';
import { fakeCycle } from './events.fake';

it('creates initial state', () => {
  // tslint:disable no-any
  let s = mat.reducer(undefined as any, undefined as any);
  // tslint:enable no-any
  expect(s).toBe(mat.initial);
});

const m: Readonly<mat.MaterialDetail> = {
  partName: "aaa",
  serial: "abc",
  workorderId: "asd",
  signaledInspections: ["a", "b"],
  completedInspections: ["a"],
  loading_events: true,
  events: [],
};

it('starts an open for load station', () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    station: StationMonitorType.LoadUnload,
    initial: m,
    pledge: { status: PledgeStatus.Starting }
  };
  let s = mat.reducer(mat.initial, action);
  expect(s.loadstation_display_material).toBe(m);
  expect(s.inspection_display_material).toBeUndefined();
  expect(s.wash_display_material).toBeUndefined();
});

it('starts an open for wash station', () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    station: StationMonitorType.Wash,
    initial: m,
    pledge: { status: PledgeStatus.Starting }
  };
  let s = mat.reducer(mat.initial, action);
  expect(s.loadstation_display_material).toBeUndefined();
  expect(s.inspection_display_material).toBeUndefined();
  expect(s.wash_display_material).toBe(m);
});

it('starts an open for inspection station', () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    station: StationMonitorType.Inspection,
    initial: m,
    pledge: { status: PledgeStatus.Starting }
  };
  let s = mat.reducer(mat.initial, action);
  expect(s.loadstation_display_material).toBeUndefined();
  expect(s.inspection_display_material).toBe(m);
  expect(s.wash_display_material).toBeUndefined();
});

it('finishes material open for load station', () => {
  const evts = fakeCycle(new Date(), 20);
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    station: StationMonitorType.LoadUnload,
    initial: m,
    pledge: { status: PledgeStatus.Completed, result: evts }
  };
  let s = mat.reducer({...mat.initial, loadstation_display_material: m}, action);
  expect(s.loadstation_display_material).toBeDefined();
  var mats = s.loadstation_display_material || (() => { throw "undefined"; })();
  expect(mats.loading_events).toBe(false);
  expect(mats.events).toEqual(evts);

  expect(s.inspection_display_material).toBeUndefined();
  expect(s.wash_display_material).toBeUndefined();
});

it('finishes material open for wash station', () => {
  const evts = fakeCycle(new Date(), 20);
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    station: StationMonitorType.Wash,
    initial: m,
    pledge: { status: PledgeStatus.Completed, result: evts }
  };
  let s = mat.reducer({...mat.initial, wash_display_material: m}, action);
  expect(s.wash_display_material).toBeDefined();
  var mats = s.wash_display_material || (() => { throw "undefined"; })();
  expect(mats.loading_events).toBe(false);
  expect(mats.events).toEqual(evts);

  expect(s.inspection_display_material).toBeUndefined();
  expect(s.loadstation_display_material).toBeUndefined();
});

it('finishes material open for inspection station', () => {
  const evts = fakeCycle(new Date(), 20);
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    station: StationMonitorType.Inspection,
    initial: m,
    pledge: { status: PledgeStatus.Completed, result: evts }
  };
  let s = mat.reducer({...mat.initial, inspection_display_material: m}, action);
  expect(s.inspection_display_material).toBeDefined();
  var mats = s.inspection_display_material || (() => { throw "undefined"; })();
  expect(mats.loading_events).toBe(false);
  expect(mats.events).toEqual(evts);

  expect(s.wash_display_material).toBeUndefined();
  expect(s.loadstation_display_material).toBeUndefined();
});

it('handles material error for load station', () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    station: StationMonitorType.LoadUnload,
    initial: m,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("aaaa")
    }
  };
  let s = mat.reducer({...mat.initial, loadstation_display_material: m}, action);

  expect(s.loadstation_display_material).toBeDefined();
  var mats = s.loadstation_display_material || (() => { throw "undefined"; })();
  expect(mats.loading_events).toBe(false);
  expect(mats.events).toEqual([]);

  expect(s.inspection_display_material).toBeUndefined();
  expect(s.wash_display_material).toBeUndefined();
});

it('handles material error for wash', () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    station: StationMonitorType.Wash,
    initial: m,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("aaaa")
    }
  };
  let s = mat.reducer({...mat.initial, wash_display_material: m}, action);

  expect(s.wash_display_material).toBeDefined();
  var mats = s.wash_display_material || (() => { throw "undefined"; })();
  expect(mats.loading_events).toBe(false);
  expect(mats.events).toEqual([]);

  expect(s.inspection_display_material).toBeUndefined();
  expect(s.loadstation_display_material).toBeUndefined();
});

it('handles material error for inspection', () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    station: StationMonitorType.Inspection,
    initial: m,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("aaaa")
    }
  };
  let s = mat.reducer({...mat.initial, inspection_display_material: m}, action);

  expect(s.inspection_display_material).toBeDefined();
  var mats = s.inspection_display_material || (() => { throw "undefined"; })();
  expect(mats.loading_events).toBe(false);
  expect(mats.events).toEqual([]);

  expect(s.wash_display_material).toBeUndefined();
  expect(s.loadstation_display_material).toBeUndefined();
});

let fullSt: mat.State = {
  loadstation_display_material: m,
  wash_display_material: m,
  inspection_display_material: m,
};

it('clears the load station material', () => {
  const action: mat.Action = {
    type: mat.ActionType.CloseMaterialDialog,
    station: StationMonitorType.LoadUnload,
  };
  const st = mat.reducer(fullSt, action);
  expect(st.loadstation_display_material).toBeUndefined();
  expect(st.wash_display_material).toBe(m);
  expect(st.inspection_display_material).toBe(m);
});

it('clears the wash material', () => {
  const action: mat.Action = {
    type: mat.ActionType.CloseMaterialDialog,
    station: StationMonitorType.Wash,
  };
  const st = mat.reducer(fullSt, action);
  expect(st.wash_display_material).toBeUndefined();
  expect(st.loadstation_display_material).toBe(m);
  expect(st.inspection_display_material).toBe(m);
});

it('clears the inspection material', () => {
  const action: mat.Action = {
    type: mat.ActionType.CloseMaterialDialog,
    station: StationMonitorType.Inspection,
  };
  const st = mat.reducer(fullSt, action);
  expect(st.inspection_display_material).toBeUndefined();
  expect(st.loadstation_display_material).toBe(m);
  expect(st.wash_display_material).toBe(m);
});