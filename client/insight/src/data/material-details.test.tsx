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
import * as api from './api';
import { fakeCycle } from './events.fake';

it('creates initial state', () => {
  // tslint:disable no-any
  let s = mat.reducer(undefined as any, undefined as any);
  // tslint:enable no-any
  expect(s).toBe(mat.initial);
});

const m: Readonly<api.IInProcessMaterial> = {
  materialID: 10,
  jobUnique: "aaa",
  partName: "aaa",
  process: 2,
  path: 1,
  signaledInspections: ["a", "b"],
  location: new api.InProcessMaterialLocation({
    type: api.LocType.OnPallet,
    pallet: "7",
    face: 1,
    queuePosition: 0,
  }),
  action: new api.InProcessMaterialAction({
    type: api.ActionType.Loading,
    loadOntoFace: 1,
    processAfterLoad: 2,
    pathAfterLoad: 1,
  }),
};

it('starts a material open', () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    material: m,
    pledge: {
      status: PledgeStatus.Starting
    }
  };
  let s = mat.reducer(mat.initial, action);
  expect(s.display_material).toBe(m);
  expect(s.loading_events).toBe(true);
  expect(s.events).toEqual([]);
});

it('finishes material open', () => {
  const evts = fakeCycle(new Date(), 20);
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    material: m,
    pledge: {
      status: PledgeStatus.Completed,
      result: evts
    }
  };
  let s = mat.reducer({...mat.initial, loading_events: true}, action);
  expect(s.loading_events).toBe(false);
  expect(s.events).toEqual(evts);
});

it('handles material error', () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    material: m,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("aaaa")
    }
  };
  let s = mat.reducer({...mat.initial, loading_events: true}, action);
  expect(s.loading_events).toBe(false);
});

it('closes the dialog', () => {
  let st: mat.State = {
    display_material: m,
    loading_events: false,
    events: fakeCycle(new Date(), 20),
  };
  const action: mat.Action = {
    type: mat.ActionType.CloseMaterialDialog,
  };
  st = mat.reducer(st, action);
  expect(st.display_material).toBeUndefined();
  expect(st.loading_events).toBe(false);
  expect(st.events).toEqual([]);
});