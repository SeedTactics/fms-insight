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

import * as api from './api';
import { Pledge, PledgeStatus, ActionBeforeMiddleware } from '../store/middleware';

export enum ActionType {
  Load = 'ServerSettings_Load'
}

interface LoadReturn {
  readonly workType: api.WorkorderAssignmentType;
  readonly fmsInfo: Readonly<api.IFMSInfo>;
}

export type Action =
  | {type: ActionType.Load, pledge: Pledge<LoadReturn>}
  ;

export interface State {
  readonly workorderAssignmentType: api.WorkorderAssignmentType;
  readonly fmsInfo?: Readonly<api.IFMSInfo>;
  readonly loadError?: Error;
}

export const initial: State = {
  workorderAssignmentType: api.WorkorderAssignmentType.AssignWorkorderAtWash,
  fmsInfo: undefined,
};

export function loadServerSettings(): ActionBeforeMiddleware<Action> {
  const client = new api.ServerClient();
  const p: Promise<LoadReturn> = client.workorderAssignmentType()
  .then(workType => {
    return client.fMSInformation()
    .then(fmsInfo => {
      return {workType, fmsInfo};
    });
  });

  return {
    type: ActionType.Load,
    pledge: p,
  };
}

export function reducer(s: State, a: Action): State {
  if (s === undefined) { return initial; }
  switch (a.type) {
    case ActionType.Load:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return s; // do nothing
        case PledgeStatus.Completed:
          return {
            workorderAssignmentType: a.pledge.result.workType,
            fmsInfo: a.pledge.result.fmsInfo,
          };
        case PledgeStatus.Error:
          return {...s, loadError: a.pledge.error};
        default: return s;
      }

    default: return s;
  }
}