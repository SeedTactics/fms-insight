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
import { PledgeStatus } from './middleware';
// import * as api from './api';
import {
  fakeCycle,
  fakeInspComplete,
  fakeWashComplete,
  fakeSerial,
  fakeInspSignal,
  fakeWorkorderAssign
} from './events.fake';

it('creates initial state', () => {
  // tslint:disable no-any
  let s = mat.reducer(undefined as any, undefined as any);
  // tslint:enable no-any
  expect(s).toBe(mat.initial);
});

const m: Readonly<mat.MaterialDetail> = {
  materialID: 105,
  partName: "aaa",
  jobUnique: "uniq",
  serial: "abc",
  workorderId: "asd",
  signaledInspections: ["a", "b"],
  completedInspections: ["a"],
  loading_events: true,
  updating_material: false,
  events: [],
  loading_workorders: false,
  workorders: [],
};

it('starts an open', () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    initial: m,
    pledge: { status: PledgeStatus.Starting }
  };
  let s = mat.reducer(mat.initial, action);
  expect(s.material).toEqual(m);
});

it('finishes material open', () => {
  const evts = fakeCycle(new Date(), 20);
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    initial: m,
    pledge: { status: PledgeStatus.Completed, result: evts }
  };
  const initialSt = {
    material: {...m, loading_events: true}
  };
  let s = mat.reducer(initialSt, action);
  expect(s.material).toEqual({...m, loading_events: false, events: evts});
});

it('handles material error', () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    initial: m,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("aaaa")
    }
  };
  const initialSt = {
    material: {...m, loading_events: true}
  };
  let s = mat.reducer(initialSt, action);
  expect(s.material).toEqual({...m, loading_events: false, events: []});
});

it('clears the material', () => {
  const action: mat.Action = {
    type: mat.ActionType.CloseMaterialDialog,
  };
  const fullSt: mat.State = {
    material: m
  };
  const st = mat.reducer(fullSt, action);
  expect(st.material).toEqual(null);
});

it("starts to update", () => {
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    pledge: {
      status: PledgeStatus.Starting
    }
  };
  const st = mat.reducer({material: m}, action);
  expect(st.material).toEqual({...m, updating_material: true});
});

it("errors during update", () => {
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("the error")
    }
  };
  const st = mat.reducer({material: {...m, updating_material: true}}, action);
  expect(st.material).toEqual(m);
});

it("starts to load workorders", () => {
  const action: mat.Action = {
    type: mat.ActionType.LoadWorkorders,
    pledge: {
      status: PledgeStatus.Starting
    }
  };
  const st = mat.reducer({material: m}, action);
  expect(st.material).toEqual({...m, loading_workorders: true});
});

it("errors during loading workorders", () => {
  const action: mat.Action = {
    type: mat.ActionType.LoadWorkorders,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("the error")
    }
  };
  const st = mat.reducer({material: {...m, loading_workorders: true}}, action);
  expect(st.material).toEqual(m);
});

it("successfully loads workorders", () => {
  const work: mat.WorkorderPlanAndSummary = {
    plan: {
      workorderId: "work1",
      part: "aaa",
      quantity: 5,
      dueDate: new Date(),
      priority: 100,
    },
  };
  const action: mat.Action = {
    type: mat.ActionType.LoadWorkorders,
    pledge: {
      status: PledgeStatus.Completed,
      result: [work]
    }
  };
  const st = mat.reducer({material: {...m, loading_workorders: true}}, action);
  expect(st.material).toEqual({...m, workorders: [work]});
});

it("succeeds for an completed inspection cycle", () => {
  const evt = fakeInspComplete();
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    newInspType: "abc",
    pledge: {
      status: PledgeStatus.Completed,
      result: evt,
    }
  };
  const initialSt = {
    material: {...m, updating_material: true}
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({...m,
      events: [evt],
      completedInspections: [...m.completedInspections, "abc"]
  });
});

it("succeeds for a wash complete cycle", () => {
  const evt = fakeWashComplete();
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    pledge: {
      status: PledgeStatus.Completed,
      result: evt,
    }
  };
  const initialSt = {
    material: {...m, updating_material: true}
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({...m,
      events: [evt],
  });
});

it("succeeds for a workorder set", () => {
  const evt = fakeWashComplete();
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    newWorkorder: "work1234",
    pledge: {
      status: PledgeStatus.Completed,
      result: evt,
    }
  };
  const initialSt = {
    material: {...m, updating_material: true}
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({...m,
      events: [evt],
      workorderId: "work1234",
  });
});

it("successfully processes events", () => {
  const cycle = fakeCycle(new Date(), 55);
  const logmat = cycle[0].material[0];
  const evts = [...cycle,
    fakeInspComplete(logmat, "compinsp"),
    fakeInspSignal(logmat, "signalinsp"),
    fakeSerial(logmat, "theserial"),
    fakeWorkorderAssign(logmat, "work1234"),
  ];

  const initial = {
    materialID: -1,
    partName: "",
    jobUnique: "",
    serial: "",
    workorderId: "",
    signaledInspections: [],
    completedInspections: [],
    loading_events: true,
    updating_material: false,
    events: [],
    loading_workorders: false,
    saving_workorder: false,
    workorders: [],
  };
  const after = {...initial,
    materialID: logmat.id,
    partName: logmat.part,
    jobUnique: logmat.uniq,
    signaledInspections: ["signalinsp"],
    completedInspections: ["compinsp"],
    serial: "theserial",
    workorderId: "work1234",
    events: evts,
    loading_events: false,
  };

  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    initial,
    pledge: { status: PledgeStatus.Completed, result: evts }
  };
  const initialSt = {
    material: {...m, loading_events: true}
  };
  let s = mat.reducer(initialSt, action);
  expect(s.material).toEqual(after);
});