/* Copyright (c) 2020, John Lenz

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

import * as React from "react";
import { RecoilState, useRecoilState, useSetRecoilState } from "recoil";
import produce, { Draft, Immutable } from "immer";

export function useRecoilStateDraft<T>(recoilState: RecoilState<T>): [T, (f: (d: Draft<T>) => void) => void] {
  const [st, setState] = useRecoilState(recoilState);
  const setDraft = React.useCallback(
    function setDraft(f: (d: Draft<T>) => void) {
      const mapper: (t: Immutable<Draft<T>>) => Immutable<Draft<T>> = produce((d) => void f(d));
      // convert Immutable<Draft<T>> to T
      setState((mapper as unknown) as (t: T) => T);
    },
    [setState]
  );
  return [st, setDraft];
}

export function useSetRecoilStateDraft<T>(recoilState: RecoilState<T>): (f: (d: Draft<T>) => void) => void {
  const setState = useSetRecoilState(recoilState);
  const setDraft = React.useCallback(
    function setDraft(f: (d: Draft<T>) => void) {
      const mapper: (t: Immutable<Draft<T>>) => Immutable<Draft<T>> = produce((d) => void f(d));
      // convert Immutable<Draft<T>> to T
      setState((mapper as unknown) as (t: T) => T);
    },
    [setState]
  );
  return setDraft;
}
