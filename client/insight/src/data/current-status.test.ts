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

import * as cs from "./current-status";
import * as api from "./api";
import {
  fakeMaterial,
  fakeSerial,
  fakeWorkorderAssign,
  fakeInspSignal,
  fakeInspComplete,
  fakeInspForce,
  fakeInProcMaterial,
} from "./events.fake";

it("creates initial state", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const s = cs.reducer(undefined as any, undefined as any);
  expect(s).toBe(cs.initial);
});

const statusWithMat: cs.State = {
  ...cs.initial,
  current_status: {
    ...cs.initial.current_status,
    material: [
      new api.InProcessMaterial({
        materialID: 10,
        jobUnique: "uniq",
        partName: "part",
        process: 1,
        path: 1,
        signaledInspections: [],
        location: new api.InProcessMaterialLocation(),
        action: new api.InProcessMaterialAction(),
      }),
      new api.InProcessMaterial({
        materialID: 20,
        jobUnique: "uuuuu",
        partName: "pppp",
        process: 2,
        path: 3,
        signaledInspections: ["aaa"],
        location: new api.InProcessMaterialLocation(),
        action: new api.InProcessMaterialAction(),
      }),
    ],
  },
};

it("sets the serial", () => {
  const mat = new api.LogMaterial({ ...fakeMaterial(), id: 10 });
  const st = cs.reducer(statusWithMat, {
    type: cs.ActionType.ReceiveNewLogEntry,
    entry: fakeSerial(mat, "serial12345"),
  });

  const actualInProcMat = st.current_status.material.filter((m) => m.materialID === mat.id)[0];
  expect(actualInProcMat.serial).toEqual("serial12345");
});

it("sets a workorder", () => {
  const mat = new api.LogMaterial({ ...fakeMaterial(), id: 20 });
  const st = cs.reducer(statusWithMat, {
    type: cs.ActionType.ReceiveNewLogEntry,
    entry: fakeWorkorderAssign(mat, "work7777"),
  });

  const actualInProcMat = st.current_status.material.filter((m) => m.materialID === mat.id)[0];
  expect(actualInProcMat.workorderId).toEqual("work7777");
});

it("sets an inspection", () => {
  const mat = new api.LogMaterial({ ...fakeMaterial(), id: 20 });
  const st = cs.reducer(statusWithMat, {
    type: cs.ActionType.ReceiveNewLogEntry,
    entry: fakeInspSignal(mat, "insp11"),
  });

  const actualInProcMat = st.current_status.material.filter((m) => m.materialID === mat.id)[0];
  expect(actualInProcMat.signaledInspections).toEqual(["aaa", "insp11"]);
});

it("sets a forced inspection", () => {
  const mat = new api.LogMaterial({ ...fakeMaterial(), id: 20 });
  const st = cs.reducer(statusWithMat, {
    type: cs.ActionType.ReceiveNewLogEntry,
    entry: fakeInspForce(mat, "insp55"),
  });

  const actualInProcMat = st.current_status.material.filter((m) => m.materialID === mat.id)[0];
  expect(actualInProcMat.signaledInspections).toEqual(["aaa", "insp55"]);
});

it("ignores other cycles", () => {
  const mat = new api.LogMaterial({ ...fakeMaterial(), id: 10 });
  const st = cs.reducer(statusWithMat, {
    type: cs.ActionType.ReceiveNewLogEntry,
    entry: fakeInspComplete(mat),
  });

  expect(st).toBe(statusWithMat);
});

function adjPos(m: api.InProcessMaterial, newPos: number, newQueue?: string): api.InProcessMaterial {
  return new api.InProcessMaterial({
    ...m,
    location: {
      ...m.location,
      currentQueue: newQueue || m.location.currentQueue,
      queuePosition: newPos,
    },
  } as api.IInProcessMaterial);
}

it("reorders in-process material backwards", () => {
  const mats = [
    fakeInProcMaterial(0, "abc", 0),
    fakeInProcMaterial(1, "abc", 1),
    fakeInProcMaterial(2, "abc", 2),
    fakeInProcMaterial(3, "abc", 3),
    fakeInProcMaterial(4, "abc", 4),
    fakeInProcMaterial(5, "other", 0),
    fakeInProcMaterial(6),
    fakeInProcMaterial(7),
  ];
  const initialSt = { ...cs.initial, current_status: { ...cs.initial.current_status, material: mats } };
  const st = cs.reducer(initialSt, {
    type: cs.ActionType.ReorderQueuedMaterial,
    queue: "abc",
    materialId: 1,
    newIdx: 3,
  });

  expect(st.current_status.material).toEqual([
    mats[0],
    adjPos(mats[1], 3),
    adjPos(mats[2], 1),
    adjPos(mats[3], 2),
    mats[4],
    mats[5],
    mats[6],
    mats[7],
  ]);
});

it("reorders in-process material forwards", () => {
  const mats = [
    fakeInProcMaterial(0, "abc", 0),
    fakeInProcMaterial(1, "abc", 1),
    fakeInProcMaterial(2, "abc", 2),
    fakeInProcMaterial(3, "abc", 3),
    fakeInProcMaterial(4, "abc", 4),
    fakeInProcMaterial(5, "other", 0),
    fakeInProcMaterial(6),
    fakeInProcMaterial(7),
  ];
  const initialSt = { ...cs.initial, current_status: { ...cs.initial.current_status, material: mats } };
  const st = cs.reducer(initialSt, {
    type: cs.ActionType.ReorderQueuedMaterial,
    queue: "abc",
    materialId: 3,
    newIdx: 1,
  });

  expect(st.current_status.material).toEqual([
    mats[0],
    adjPos(mats[1], 2),
    adjPos(mats[2], 3),
    adjPos(mats[3], 1),
    mats[4],
    mats[5],
    mats[6],
    mats[7],
  ]);
});

it("moves between queue", () => {
  const mats = [
    fakeInProcMaterial(0, "abc", 0),
    fakeInProcMaterial(1, "abc", 1),
    fakeInProcMaterial(2, "abc", 2),
    fakeInProcMaterial(3, "abc", 3),
    fakeInProcMaterial(4, "abc", 4),
    fakeInProcMaterial(5, "other", 0),
    fakeInProcMaterial(6, "other", 1),
    fakeInProcMaterial(7, "other", 2),
    fakeInProcMaterial(8),
    fakeInProcMaterial(9),
  ];
  const initialSt = { ...cs.initial, current_status: { ...cs.initial.current_status, material: mats } };
  const st = cs.reducer(initialSt, {
    type: cs.ActionType.ReorderQueuedMaterial,
    queue: "abc",
    materialId: 6,
    newIdx: 1,
  });

  expect(st.current_status.material).toEqual([
    mats[0],
    adjPos(mats[1], 2), // 1 -> 2
    adjPos(mats[2], 3), // 2 -> 3
    adjPos(mats[3], 4), // 3 -> 4
    adjPos(mats[4], 5), // 4 -> 5
    mats[5],
    adjPos(mats[6], 1, "abc"), // change queue
    adjPos(mats[7], 1), // 2 -> 1,
    mats[8],
    mats[9],
  ]);
});
