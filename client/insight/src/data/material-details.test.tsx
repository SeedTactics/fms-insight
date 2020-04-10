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

import * as mat from "./material-details";
import { PledgeStatus } from "../store/middleware";
// import * as api from './api';
import {
  fakeCycle,
  fakeInspComplete,
  fakeWashComplete,
  fakeSerial,
  fakeInspSignal,
  fakeWorkorderAssign,
} from "./events.fake";
import { Vector } from "prelude-ts";

it("creates initial state", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const s = mat.reducer(undefined as any, undefined as any);
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
  events: Vector.empty(),
  loading_workorders: false,
  workorders: Vector.empty(),
  openedViaBarcodeScanner: false,
};

it("starts an open", () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    initial: m,
    pledge: { status: PledgeStatus.Starting },
  };
  const s = mat.reducer({ ...mat.initial, load_error: new Error("a") }, action);
  expect(s.material).toEqual(m);
  expect(s.load_error).toBeUndefined();
});

it("finishes material open", () => {
  const evts = fakeCycle(new Date(), 20, undefined, undefined, undefined, true);
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    initial: m,
    pledge: { status: PledgeStatus.Completed, result: evts },
  };
  const initialSt = {
    material: { ...m, loading_events: true },
    add_mat_in_progress: false,
  };
  const s = mat.reducer(initialSt, action);
  expect(s.material).toEqual({ ...m, loading_events: false, events: Vector.ofIterable(evts) });
});

it("handles material error", () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    initial: m,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("aaaa"),
    },
  };
  const initialSt = {
    material: { ...m, loading_events: true },
    add_mat_in_progress: false,
  };
  const s = mat.reducer(initialSt, action);
  expect(s.material).toEqual({ ...m, loading_events: false, events: Vector.empty() });
  expect(s.load_error).toEqual(new Error("aaaa"));
});

it("opens without loading", () => {
  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialogWithoutLoad,
    mat: m,
  };
  const initialSt = {
    material: null,
    add_mat_in_progress: false,
  };
  const s = mat.reducer(initialSt, action);
  expect(s.material).toEqual(m);
});

it("clears the material", () => {
  const action: mat.Action = {
    type: mat.ActionType.CloseMaterialDialog,
  };
  const fullSt: mat.State = {
    material: m,
    add_mat_in_progress: false,
  };
  const st = mat.reducer(fullSt, action);
  expect(st.material).toEqual(null);
});

it("starts to update", () => {
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    pledge: {
      status: PledgeStatus.Starting,
    },
  };
  const st = mat.reducer({ material: m, add_mat_in_progress: false, update_error: new Error("a") }, action);
  expect(st.material).toEqual({ ...m, updating_material: true });
  expect(st.update_error).toBeUndefined();
});

it("errors during update", () => {
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("the error"),
    },
  };
  const st = mat.reducer({ material: { ...m, updating_material: true }, add_mat_in_progress: false }, action);
  expect(st.material).toEqual(m);
  expect(st.update_error).toEqual(new Error("the error"));
});

it("starts to load workorders", () => {
  const action: mat.Action = {
    type: mat.ActionType.LoadWorkorders,
    pledge: {
      status: PledgeStatus.Starting,
    },
  };
  const st = mat.reducer(
    {
      material: m,
      add_mat_in_progress: false,
      load_workorders_error: new Error("a"),
    },
    action
  );
  expect(st.material).toEqual({ ...m, loading_workorders: true });
  expect(st.load_workorders_error).toBeUndefined();
});

it("errors during loading workorders", () => {
  const action: mat.Action = {
    type: mat.ActionType.LoadWorkorders,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("the error"),
    },
  };
  const st = mat.reducer(
    {
      material: { ...m, loading_workorders: true },
      add_mat_in_progress: false,
    },
    action
  );
  expect(st.material).toEqual(m);
  expect(st.load_workorders_error).toEqual(new Error("the error"));
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
      result: Vector.of(work),
    },
  };
  const st = mat.reducer(
    {
      material: { ...m, loading_workorders: true },
      add_mat_in_progress: false,
    },
    action
  );
  expect(st.material).toEqual({ ...m, workorders: Vector.of(work) });
});

it("succeeds for an completed inspection cycle", () => {
  const evt = fakeInspComplete();
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    newCompletedInspection: "abc",
    pledge: {
      status: PledgeStatus.Completed,
      result: evt,
    },
  };
  const initialSt = {
    material: { ...m, updating_material: true },
    add_mat_in_progress: false,
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({
    ...m,
    events: Vector.of(evt),
    completedInspections: [...m.completedInspections, "abc"],
  });
});

it("succeeds for a signaled inspection cycle", () => {
  const evt = fakeInspSignal();
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    newSignaledInspection: "xxx",
    pledge: {
      status: PledgeStatus.Completed,
      result: evt,
    },
  };
  const initialSt = {
    material: { ...m, updating_material: true },
    add_mat_in_progress: false,
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({
    ...m,
    events: Vector.of(evt),
    signaledInspections: [...m.signaledInspections, "xxx"],
  });
});

it("succeeds for a wash complete cycle", () => {
  const evt = fakeWashComplete();
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    pledge: {
      status: PledgeStatus.Completed,
      result: evt,
    },
  };
  const initialSt = {
    material: { ...m, updating_material: true },
    add_mat_in_progress: false,
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({
    ...m,
    events: Vector.of(evt),
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
    },
  };
  const initialSt = {
    material: { ...m, updating_material: true },
    add_mat_in_progress: false,
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({
    ...m,
    events: Vector.of(evt),
    workorderId: "work1234",
  });
});

it("succeeds for a serial set", () => {
  const evt = fakeWashComplete();
  const action: mat.Action = {
    type: mat.ActionType.UpdateMaterial,
    newSerial: "serial1524",
    pledge: {
      status: PledgeStatus.Completed,
      result: evt,
    },
  };
  const initialSt = {
    material: { ...m, updating_material: true },
    add_mat_in_progress: false,
  };
  const st = mat.reducer(initialSt, action);
  expect(st.material).toEqual({
    ...m,
    events: Vector.of(evt),
    serial: "serial1524",
  });
});

it("successfully processes events", () => {
  const cycle = fakeCycle(new Date(), 55, undefined, undefined, undefined, true);
  const logmat = cycle[0].material[0];
  const evts = [
    ...cycle,
    fakeInspComplete(logmat, "compinsp"),
    fakeInspSignal(logmat, "signalinsp"),
    fakeSerial(logmat, "theserial"),
    fakeWorkorderAssign(logmat, "work1234"),
  ];
  const sortedEvts = Vector.ofIterable(evts).sortOn(
    (e) => e.endUTC.getTime(),
    (e) => e.counter
  );

  const initial: mat.MaterialDetail = {
    materialID: -1,
    partName: "",
    jobUnique: "",
    serial: "",
    workorderId: "",
    signaledInspections: [],
    completedInspections: [],
    loading_events: true,
    updating_material: false,
    events: Vector.empty(),
    loading_workorders: false,
    workorders: Vector.empty(),
    openedViaBarcodeScanner: false,
  };
  const after = {
    ...initial,
    materialID: logmat.id,
    partName: logmat.part,
    jobUnique: logmat.uniq,
    signaledInspections: ["signalinsp"],
    completedInspections: ["compinsp"],
    serial: "theserial",
    workorderId: "work1234",
    events: sortedEvts,
    loading_events: false,
  };

  const action: mat.Action = {
    type: mat.ActionType.OpenMaterialDialog,
    initial,
    pledge: { status: PledgeStatus.Completed, result: evts },
  };
  const initialSt = {
    material: { ...initial, loading_events: true },
    add_mat_in_progress: false,
  };
  const s = mat.reducer(initialSt, action);
  expect(s.material).toEqual(after);
});

it("starts to add new material", () => {
  const action: mat.Action = {
    type: mat.ActionType.AddNewMaterialToQueue,
    pledge: {
      status: PledgeStatus.Starting,
    },
  };
  const st = mat.reducer({ material: m, add_mat_in_progress: false, add_mat_error: new Error("a") }, action);
  expect(st.material).toEqual(m);
  expect(st.add_mat_in_progress).toBe(true);
  expect(st.add_mat_error).toBeUndefined();
});

it("completes to add new material", () => {
  const action: mat.Action = {
    type: mat.ActionType.AddNewMaterialToQueue,
    pledge: {
      status: PledgeStatus.Completed,
      result: undefined,
    },
  };
  const st = mat.reducer({ material: m, add_mat_in_progress: true }, action);
  expect(st.material).toEqual(m);
  expect(st.add_mat_in_progress).toBe(false);
  expect(st.add_mat_error).toBeUndefined();
});

it("errors during add new material", () => {
  const action: mat.Action = {
    type: mat.ActionType.AddNewMaterialToQueue,
    pledge: {
      status: PledgeStatus.Error,
      error: new Error("an error"),
    },
  };
  const st = mat.reducer({ material: m, add_mat_in_progress: true }, action);
  expect(st.material).toEqual(m);
  expect(st.add_mat_in_progress).toBe(false);
  expect(st.add_mat_error).toEqual(new Error("an error"));
});

it("adds extra logs", () => {
  const evts1 = fakeCycle(new Date(), 55);
  const evts2 = fakeCycle(new Date(), 12);

  const initial: mat.MaterialDetail = {
    materialID: -1,
    partName: "adouh",
    jobUnique: "sdfoudj",
    serial: "eiwewg",
    workorderId: "aeawef",
    signaledInspections: [],
    completedInspections: [],
    loading_events: true,
    updating_material: false,
    events: Vector.ofIterable(evts1),
    loading_workorders: false,
    workorders: Vector.empty(),
    openedViaBarcodeScanner: false,
  };
  const after = {
    ...initial,
    events: Vector.ofIterable(evts1)
      .appendAll(evts2)
      .sortOn(
        (e) => e.endUTC.getTime(),
        (e) => e.counter
      ),
  };

  const action: mat.Action = {
    type: mat.ActionType.LoadLogFromOtherServer,
    pledge: { status: PledgeStatus.Completed, result: evts2 },
  };
  const initialSt = {
    material: initial,
    add_mat_in_progress: false,
  };
  const s = mat.reducer(initialSt, action);
  expect(s.material).toEqual(after);
});
