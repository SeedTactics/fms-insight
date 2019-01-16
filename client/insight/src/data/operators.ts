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

import { HashSet } from "prelude-ts";

export enum ActionType {
  SetOperator = "Operators_SetOperator",
  RemoveOperator = "Operators_Remove"
}

export type Action =
  | { type: ActionType.SetOperator; operator: string }
  | { type: ActionType.RemoveOperator; operator: string };

export interface State {
  readonly operators: HashSet<string>;
  readonly current?: string;
}

export const initial = {
  operators: HashSet.ofIterable<string>(JSON.parse(localStorage.getItem("operators") || "[]")),
  current: localStorage.getItem("current-operator") || undefined
};

export function createOnStateChange(): (s: State) => void {
  let lastOpers = initial.operators;
  let lastCurrent = initial.current;
  return s => {
    if (s.operators !== lastOpers) {
      lastOpers = s.operators;
      localStorage.setItem("operators", JSON.stringify(s.operators.toArray()));
    }
    if (s.current !== lastCurrent) {
      lastCurrent = s.current;
      if (s.current) {
        localStorage.setItem("current-operator", s.current);
      } else {
        localStorage.removeItem("current-operator");
      }
    }
  };
}

export function reducer(s: State, a: Action): State {
  if (s === undefined) {
    return initial;
  }

  switch (a.type) {
    case ActionType.SetOperator:
      return {
        operators: s.operators.contains(a.operator) ? s.operators : s.operators.add(a.operator),
        current: a.operator
      };
    case ActionType.RemoveOperator:
      if (s.operators.contains(a.operator)) {
        return {
          operators: s.operators.remove(a.operator),
          current: s.current === a.operator ? undefined : s.current
        };
      } else {
        return s;
      }
    default:
      return s;
  }
}
