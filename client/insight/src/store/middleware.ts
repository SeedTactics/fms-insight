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
export enum PledgeStatus {
  Starting = "Pledge_Starting",
  Completed = "Pledge_Completed",
  Error = "Pledge_Error"
}

export type Pledge<T> =
  | { status: PledgeStatus.Starting }
  | { status: PledgeStatus.Completed; result: T }
  | { status: PledgeStatus.Error; error: Error };

export type PledgeToPromise<AP> = {
  [P in keyof AP]: "pledge" extends P ? (AP[P] extends Pledge<infer R> ? Promise<R> : AP[P]) : AP[P]
};

interface WithPromise<R> {
  pledge: Promise<R>;
}
// tslint:disable-next-line:no-any
function hasPledge<R>(obj: any): obj is WithPromise<R> {
  return "pledge" in obj && obj.pledge && obj.pledge instanceof Promise;
}

function pledgeMiddleware<A>(dispatch: (a: A) => void): (action: PledgeToPromise<A>) => void {
  return action => {
    if (hasPledge(action)) {
      // tslint:disable-next-line:no-any
      const anyAction: any = action;
      dispatch({ ...anyAction, pledge: { status: PledgeStatus.Starting } });
      action.pledge
        .then(r => {
          dispatch({
            ...anyAction,
            pledge: { status: PledgeStatus.Completed, result: r }
          });
        })
        .catch((e: Error) => {
          dispatch({
            ...anyAction,
            pledge: { status: PledgeStatus.Error, error: e }
          });
        });
    } else {
      // tslint:disable-next-line:no-any
      return dispatch(action as any);
    }
  };
}

function arrayMiddleware<A>(dispatch: (a: A) => void): (action: A | ReadonlyArray<A>) => void {
  return action => {
    if (action instanceof Array) {
      action.map(dispatch);
    } else {
      return dispatch(action as A);
    }
  };
}

export type ActionBeforeMiddleware<A> = PledgeToPromise<A> | ReadonlyArray<PledgeToPromise<A>>;

export function middleware<A>(dispatch: (a: A) => void): (action: ActionBeforeMiddleware<A>) => void {
  return arrayMiddleware(pledgeMiddleware(dispatch));
}
