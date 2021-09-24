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
import {
  RecoilState,
  Snapshot,
  TransactionInterface_UNSTABLE,
  useRecoilState,
  useRecoilTransaction_UNSTABLE,
  useSetRecoilState,
} from "recoil";
import produce, { Draft } from "immer";

export function useRecoilStateDraft<T>(recoilState: RecoilState<T>): [T, (f: (d: Draft<T>) => void) => void] {
  const [st, setState] = useRecoilState(recoilState);
  const setDraft = React.useCallback(
    function setDraft(f: (d: Draft<T>) => void) {
      const mapper = produce((d) => void f(d));
      setState(mapper);
    },
    [setState]
  );
  return [st, setDraft];
}

export function useSetRecoilStateDraft<T>(recoilState: RecoilState<T>): (f: (d: Draft<T>) => void) => void {
  const setState = useSetRecoilState(recoilState);
  const setDraft = React.useCallback(
    function setDraft(f: (d: Draft<T>) => void) {
      const mapper = produce((d) => void f(d));
      setState(mapper);
    },
    [setState]
  );
  return setDraft;
}

export interface RecoilConduit<T> {
  readonly transform: (trans: TransactionInterface_UNSTABLE, val: T) => void;
}

export function useRecoilConduit<T>({ transform }: RecoilConduit<T>): (val: T) => void {
  return useRecoilTransaction_UNSTABLE((trans) => (val: T) => transform(trans, val));
}

export function conduit<T>(transform: (trans: TransactionInterface_UNSTABLE, val: T) => void): RecoilConduit<T> {
  return { transform };
}

type Destructor = () => void;

export interface SourceInterface {
  readonly send: <T>(conduit: RecoilConduit<T>, val: T) => void;
}

export type RecoilSource =
  | { effect: (interf: SourceInterface) => void | Destructor }
  | { effectWithRef: (interf: SourceInterface, ref: React.MutableRefObject<unknown>) => void | Destructor };

export function source(effect: (interf: SourceInterface) => void | Destructor): RecoilSource {
  return { effect };
}

export function sourceWithRef<Ref>(
  effect: (interf: SourceInterface, ref: React.MutableRefObject<Ref | undefined>) => void | Destructor
): RecoilSource {
  // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-explicit-any
  return { effectWithRef: effect as any };
}

export function useRecoilSource(source: RecoilSource): void {
  const ref = React.useRef(undefined);
  const send = useRecoilTransaction_UNSTABLE(
    (t) =>
      <T>(conduit: RecoilConduit<T>, val: T) =>
        conduit.transform(t, val)
  );
  React.useEffect(() => {
    if ("effect" in source) {
      return source.effect({
        send,
      });
    } else {
      return source.effectWithRef(
        {
          send,
        },
        ref
      );
    }
  }, []);
}

export function applyConduitToSnapshot<T>(snapshot: Snapshot, conduit: RecoilConduit<T>, val: T): Snapshot {
  return snapshot.map((ms) =>
    conduit.transform(
      {
        get: (s) => ms.getLoadable(s).valueOrThrow(),
        set: ms.set,
        reset: ms.reset,
      },
      val
    )
  );
}
