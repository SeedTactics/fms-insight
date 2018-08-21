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
  fakeInspForce
} from "./events.fake";

it("creates initial state", () => {
  // tslint:disable no-any
  let s = cs.reducer(undefined as any, undefined as any);
  // tslint:enable no-any
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
        action: new api.InProcessMaterialAction()
      }),
      new api.InProcessMaterial({
        materialID: 20,
        jobUnique: "uuuuu",
        partName: "pppp",
        process: 2,
        path: 3,
        signaledInspections: ["aaa"],
        location: new api.InProcessMaterialLocation(),
        action: new api.InProcessMaterialAction()
      })
    ]
  }
};

it("sets the serial", () => {
  const mat = new api.LogMaterial({ ...fakeMaterial(), id: 10 });
  const st = cs.reducer(statusWithMat, {
    type: cs.ActionType.ReceiveNewLogEntry,
    entry: fakeSerial(mat, "serial12345")
  });

  const actualInProcMat = st.current_status.material.filter(m => m.materialID === mat.id)[0];
  expect(actualInProcMat.serial).toEqual("serial12345");
});

it("sets a workorder", () => {
  const mat = new api.LogMaterial({ ...fakeMaterial(), id: 20 });
  const st = cs.reducer(statusWithMat, {
    type: cs.ActionType.ReceiveNewLogEntry,
    entry: fakeWorkorderAssign(mat, "work7777")
  });

  const actualInProcMat = st.current_status.material.filter(m => m.materialID === mat.id)[0];
  expect(actualInProcMat.workorderId).toEqual("work7777");
});

it("sets an inspection", () => {
  const mat = new api.LogMaterial({ ...fakeMaterial(), id: 20 });
  const st = cs.reducer(statusWithMat, {
    type: cs.ActionType.ReceiveNewLogEntry,
    entry: fakeInspSignal(mat, "insp11")
  });

  const actualInProcMat = st.current_status.material.filter(m => m.materialID === mat.id)[0];
  expect(actualInProcMat.signaledInspections).toEqual(["aaa", "insp11"]);
});

it("sets a forced inspection", () => {
  const mat = new api.LogMaterial({ ...fakeMaterial(), id: 20 });
  const st = cs.reducer(statusWithMat, {
    type: cs.ActionType.ReceiveNewLogEntry,
    entry: fakeInspForce(mat, "insp55")
  });

  const actualInProcMat = st.current_status.material.filter(m => m.materialID === mat.id)[0];
  expect(actualInProcMat.signaledInspections).toEqual(["aaa", "insp55"]);
});

it("ignores other cycles", () => {
  const mat = new api.LogMaterial({ ...fakeMaterial(), id: 10 });
  const st = cs.reducer(statusWithMat, {
    type: cs.ActionType.ReceiveNewLogEntry,
    entry: fakeInspComplete(mat)
  });

  expect(st).toBe(statusWithMat);
});
