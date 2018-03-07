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
import { ConsumingPledge, PledgeStatus } from './pledge';

export enum ActionType {
  OpenMaterialDialog = 'MaterialDetails_Open',
  CloseMaterialDialog = 'MaterialDetails_Close',
}

export type Action =
  |
    {
      type: ActionType.OpenMaterialDialog,
      material: Readonly<api.IInProcessMaterial>,
      pledge: ConsumingPledge<ReadonlyArray<Readonly<api.ILogEntry>>>
    }
  | { type: ActionType.CloseMaterialDialog }
  ;

export function openMaterialDialog(mat: Readonly<api.IInProcessMaterial>) {
  var client = new api.LogClient();
  return {
    type: ActionType.OpenMaterialDialog,
    material: mat,
    pledge: client.logForMaterial(mat.materialID),
  };
}

export interface State {
  readonly display_material?: Readonly<api.IInProcessMaterial>;
  readonly loading_events: boolean;
  readonly events: ReadonlyArray<Readonly<api.ILogEntry>>;
}

export const initial: State = {
  display_material: undefined,
  loading_events: false,
  events: [],
};

export function reducer(s: State, a: Action): State {
  if (s === undefined) { return initial; }
  switch (a.type) {
    case ActionType.OpenMaterialDialog:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return {...s,
            display_material: a.material,
            loading_events: true,
            events: []
          };

        case PledgeStatus.Completed:
          return {...s,
            loading_events: false,
            events: a.pledge.result
          };

        case PledgeStatus.Error:
          return {...s,
            loading_events: false,
          };
        default:
          return s;
      }

    case ActionType.CloseMaterialDialog:
      return {
        display_material: undefined,
        loading_events: false,
        events: []
      };

    default:
      return s;
  }
}