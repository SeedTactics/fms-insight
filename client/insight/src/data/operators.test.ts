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

localStorage.setItem("operators", '["initial1"]');
localStorage.setItem("current-operator", "initialoper");
import * as operators from './operators';
import * as im from 'immutable';

it("creates the initial state", () => {
  // tslint:disable no-any
  let s = operators.reducer(undefined as any, undefined as any);
  // tslint:enable no-any
  expect(s).toBe(operators.initial);
  expect(operators.initial.operators.toArray()).toEqual(
    ["initial1"]
  );
  expect(operators.initial.current).toEqual("initialoper");
});

it('adds an operator', () => {
  let s = operators.reducer(operators.initial, {
    type: operators.ActionType.SetOperator,
    operator: "op1"
  });

  expect(s).toEqual({
    operators: im.Set(["initial1", "op1"]),
    current: "op1",
  });

  s = operators.reducer(s, {
    type: operators.ActionType.SetOperator,
    operator: "op2"
  });

  expect(s).toEqual({
    operators: im.Set(["initial1", "op1", "op2"]),
    current: "op2",
  });
});

it('removes an operator', () => {
  let s: operators.State = {
    operators: im.Set(["op1", "op2", "aaaa"]),
    current: "aaaa"
  };
  s = operators.reducer(s, {
    type: operators.ActionType.RemoveOperator,
    operator: "op1"
  });

  expect(s).toEqual({
    operators: im.Set(["op2", "aaaa"]),
    current: "aaaa",
  });

  s = operators.reducer(s, {
    type: operators.ActionType.RemoveOperator,
    operator: "aaaa"
  });

  expect(s).toEqual({
    operators: im.Set(["op2"]),
    current: undefined,
  });
});

it("sets local storage", () => {
  const onChange = operators.createOnStateChange();
  onChange({
    operators: im.Set(["op1", "op2"]),
    current: "op2"
  });
  const opers = JSON.parse(localStorage.getItem("operators") || "[]");
  expect(opers).toEqual(["op1", "op2"]);
  expect(localStorage.getItem("current-operator")).toEqual("op2");
});