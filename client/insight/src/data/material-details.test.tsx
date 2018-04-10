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
import { StationMonitorType } from './routes';
// import * as api from './api';
import { fakeCycle, fakeInspComplete, fakeWashComplete } from './events.fake';

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

const allNullMats = {...mat.initial.material};
const allStations = [
  StationMonitorType.Inspection,
  StationMonitorType.LoadUnload,
  StationMonitorType.Wash,
  StationMonitorType.Queues
];

for (const station of allStations) {
  it('starts an open for ' + station, () => {
    const action: mat.Action = {
      type: mat.ActionType.OpenMaterialDialog,
      station,
      initial: m,
      pledge: { status: PledgeStatus.Starting }
    };
    let s = mat.reducer(mat.initial, action);
    expect(s.material).toEqual({...allNullMats, [station]: m});
  });

  it('finishes material open for ' + station, () => {
    const evts = fakeCycle(new Date(), 20);
    const action: mat.Action = {
      type: mat.ActionType.OpenMaterialDialog,
      station,
      initial: m,
      pledge: { status: PledgeStatus.Completed, result: evts }
    };
    const initialSt = {
      material: {...allNullMats,
        [station]: {...m, loading_events: true}}
    };
    let s = mat.reducer(initialSt, action);
    expect(s.material).toEqual({...allNullMats,
      [station]: {...m, loading_events: false, events: evts}
    });
  });

  it('handles material error for ' + station, () => {
    const action: mat.Action = {
      type: mat.ActionType.OpenMaterialDialog,
      station,
      initial: m,
      pledge: {
        status: PledgeStatus.Error,
        error: new Error("aaaa")
      }
    };
    const initialSt = {
      material: {...allNullMats,
        [station]: {...m, loading_events: true}}
    };
    let s = mat.reducer(initialSt, action);
    expect(s.material).toEqual({...allNullMats,
      [station]: {...m, loading_events: false, events: []}
    });
  });

  it('clears the material from ' + station, () => {
    const action: mat.Action = {
      type: mat.ActionType.CloseMaterialDialog,
      station,
    };
    const fullSt: mat.State = {
      material: {
        [StationMonitorType.Inspection]: m,
        [StationMonitorType.Wash]: m,
        [StationMonitorType.LoadUnload]: m,
        [StationMonitorType.Queues]: m,
      }
    };
    const st = mat.reducer(fullSt, action);
    expect(st.material).toEqual({...fullSt.material,
      [station]: null});
  });

  it("starts to update for " + station, () => {
    const action: mat.Action = {
      type: mat.ActionType.UpdateMaterial,
      station,
      pledge: {
        status: PledgeStatus.Starting
      }
    };
    const st = mat.reducer({material: {...allNullMats, [station]: m}}, action);
    expect(st.material).toEqual({...allNullMats,
      [station]: {...m, updating_material: true}
    });
  });

  it("errors during update for a " + station, () => {
    const action: mat.Action = {
      type: mat.ActionType.UpdateMaterial,
      station,
      pledge: {
        status: PledgeStatus.Error,
        error: new Error("the error")
      }
    };
    const st = mat.reducer({material: {...allNullMats, [station]: {...m, updating_material: true}}}, action);
    expect(st.material).toEqual({...allNullMats,
      [station]: m
    });
  });

  it("starts to load workorders for " + station, () => {
    const action: mat.Action = {
      type: mat.ActionType.LoadWorkorders,
      station,
      pledge: {
        status: PledgeStatus.Starting
      }
    };
    const st = mat.reducer({material: {...allNullMats, [station]: m}}, action);
    expect(st.material).toEqual({...allNullMats,
      [station]: {...m, loading_workorders: true}
    });
  });

  it("errors during loading workorders for a " + station, () => {
    const action: mat.Action = {
      type: mat.ActionType.LoadWorkorders,
      station,
      pledge: {
        status: PledgeStatus.Error,
        error: new Error("the error")
      }
    };
    const st = mat.reducer({material: {...allNullMats, [station]: {...m, loading_workorders: true}}}, action);
    expect(st.material).toEqual({...allNullMats,
      [station]: m
    });
  });

  it("successfully loads workorders for a " + station, () => {
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
      station,
      pledge: {
        status: PledgeStatus.Completed,
        result: [work]
      }
    };
    const st = mat.reducer({material: {...allNullMats, [station]: {...m, loading_workorders: true}}}, action);
    expect(st.material).toEqual({...allNullMats,
      [station]: {...m, workorders: [work]}
    });
  });
}

it("succeeds for an completed inspection cycle", () => {
  const evt = fakeInspComplete();
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    station: StationMonitorType.Inspection,
    newInspType: "abc",
    pledge: {
      status: PledgeStatus.Completed,
      result: evt,
    }
  };
  const initialSt = {
    material: {...allNullMats, [StationMonitorType.Inspection]: {...m, updating_material: true}}
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({...allNullMats,
    [StationMonitorType.Inspection]: {...m,
      events: [evt],
      completedInspections: [...m.completedInspections, "abc"]
    }
  });
});

it("succeeds for a wash complete cycle", () => {
  const evt = fakeWashComplete();
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    station: StationMonitorType.Wash,
    pledge: {
      status: PledgeStatus.Completed,
      result: evt,
    }
  };
  const initialSt = {
    material: {...allNullMats, [StationMonitorType.Wash]: {...m, updating_material: true}}
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({...allNullMats,
    [StationMonitorType.Wash]: {...m,
      events: [evt],
    }
  });
});

it("succeeds for a workorder set", () => {
  const evt = fakeWashComplete();
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    station: StationMonitorType.LoadUnload,
    newWorkorder: "work1234",
    pledge: {
      status: PledgeStatus.Completed,
      result: evt,
    }
  };
  const initialSt = {
    material: {...allNullMats, [StationMonitorType.LoadUnload]: {...m, updating_material: true}}
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({...allNullMats,
    [StationMonitorType.LoadUnload]: {...m,
      events: [evt],
      workorderId: "work1234",
    }
  });
});