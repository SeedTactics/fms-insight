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
import * as api from "./api";
import * as im from "immutable";
import {
  Pledge,
  PledgeStatus,
  ActionBeforeMiddleware
} from "../store/middleware";
import { JobsBackend } from "./backend";

export interface State {
  readonly loading: boolean;
  readonly loading_error?: Error;
  readonly current_status: Readonly<api.ICurrentStatus>; // TODO: DeepReadonly
  readonly date_of_current_status: Date | undefined;
}

export const initial: State = {
  loading: false,
  current_status: {
    jobs: {},
    pallets: {},
    material: [],
    alarms: [],
    queues: {}
  },
  date_of_current_status: undefined
};

export enum ActionType {
  LoadCurrentStatus = "CurStatus_LoadCurrentStatus",
  SetCurrentStatus = "CurStatus_SetCurrentStatus",
  ReceiveNewLogEntry = "Events_NewLogEntry"
}

export type Action =
  | {
      type: ActionType.LoadCurrentStatus;
      now: Date;
      pledge: Pledge<Readonly<api.ICurrentStatus>>;
    }
  | {
      type: ActionType.SetCurrentStatus;
      now: Date;
      st: Readonly<api.ICurrentStatus>;
    }
  | { type: ActionType.ReceiveNewLogEntry; entry: Readonly<api.ILogEntry> };

type ABF = ActionBeforeMiddleware<Action>;

export function loadCurrentStatus(): ABF {
  return {
    type: ActionType.LoadCurrentStatus,
    now: new Date(),
    pledge: JobsBackend.currentStatus()
  };
}

export function setCurrentStatus(st: Readonly<api.ICurrentStatus>): ABF {
  return {
    type: ActionType.SetCurrentStatus,
    now: new Date(),
    st
  };
}

export function receiveNewLogEntry(entry: Readonly<api.ILogEntry>): ABF {
  return {
    type: ActionType.ReceiveNewLogEntry,
    entry
  };
}

function process_new_events(entry: Readonly<api.ILogEntry>, s: State): State {
  let mats: im.Map<number, Readonly<api.InProcessMaterial>> | undefined;
  function adjustMat(
    id: number,
    f: (
      mat: Readonly<api.IInProcessMaterial>
    ) => Readonly<api.IInProcessMaterial>
  ) {
    if (mats === undefined) {
      mats = s.current_status.material.reduce(
        (map, mat) => map.set(mat.materialID, mat),
        im.Map<number, Readonly<api.InProcessMaterial>>()
      );
    }
    if (mats.has(id)) {
      mats = mats.update(id, mat => new api.InProcessMaterial(f(mat)));
    }
  }

  switch (entry.type) {
    case api.LogType.PartMark:
      for (let m of entry.material) {
        adjustMat(m.id, inmat => ({ ...inmat, serial: entry.result }));
      }
      break;

    case api.LogType.OrderAssignment:
      for (let m of entry.material) {
        adjustMat(m.id, inmat => ({ ...inmat, workorderId: entry.result }));
      }
      break;

    case api.LogType.Inspection:
    case api.LogType.InspectionForce:
      if (entry.result.toLowerCase() === "true" || entry.result === "1") {
        let inspType: string;
        if (entry.type === api.LogType.InspectionForce) {
          inspType = entry.program;
        } else {
          inspType = (entry.details || {}).InspectionType;
        }
        if (inspType) {
          for (let m of entry.material) {
            adjustMat(m.id, inmat => ({
              ...inmat,
              signaledInspections: [...inmat.signaledInspections, inspType]
            }));
          }
        }
      }
      break;
  }

  if (mats === undefined) {
    return s;
  } else {
    return {
      ...s,
      current_status: {
        ...s.current_status,
        material: mats.valueSeq().toArray()
      }
    };
  }
}

export function reducer(s: State, a: Action): State {
  if (s === undefined) {
    return initial;
  }
  switch (a.type) {
    case ActionType.LoadCurrentStatus:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          return { ...s, loading: true, loading_error: undefined };
        case PledgeStatus.Completed:
          return {
            ...s,
            loading: false,
            current_status: a.pledge.result,
            date_of_current_status: a.now
          };
        case PledgeStatus.Error:
          return { ...s, loading_error: a.pledge.error };

        default:
          return s;
      }
    case ActionType.SetCurrentStatus:
      return { ...s, current_status: a.st, date_of_current_status: a.now };

    case ActionType.ReceiveNewLogEntry:
      return process_new_events(a.entry, s);

    default:
      return s;
  }
}
