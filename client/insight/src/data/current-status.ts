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
import { HashMap } from "prelude-ts";
import { Pledge, PledgeStatus, ActionBeforeMiddleware } from "../store/middleware";
import { JobsBackend } from "./backend";
import { InProcessMaterial } from "./api";

export interface State {
  readonly loading: boolean;
  readonly loading_error?: Error;
  readonly current_status: Readonly<api.ICurrentStatus>; // TODO: DeepReadonly
}

export const initial: State = {
  loading: false,
  current_status: {
    timeOfCurrentStatusUTC: new Date(),
    jobs: {},
    pallets: {},
    material: [],
    alarms: [],
    queues: {},
  },
};

export enum ActionType {
  LoadCurrentStatus = "CurStatus_LoadCurrentStatus",
  SetCurrentStatus = "CurStatus_SetCurrentStatus",
  ReceiveNewLogEntry = "Events_NewLogEntry",
  ReorderQueuedMaterial = "CurStatus_ReorderQueuedMaterial",
  SetJobComment = "CurStatus_SetJobComment",
  SetJobPlannedQty = "CurStatus_SetJobPlannedQty",
}

export type Action =
  | {
      type: ActionType.LoadCurrentStatus;
      pledge: Pledge<Readonly<api.ICurrentStatus>>;
    }
  | {
      type: ActionType.SetCurrentStatus;
      st: Readonly<api.ICurrentStatus>;
    }
  | { type: ActionType.ReceiveNewLogEntry; entry: Readonly<api.ILogEntry> }
  | {
      type: ActionType.ReorderQueuedMaterial;
      materialId: number;
      queue: string;
      newIdx: number;
    }
  | { type: ActionType.SetJobComment; uniq: string; comment: string; pledge: Pledge<void> }
  | { type: ActionType.SetJobPlannedQty; uniq: string; proc1path: number; qty: number };

type ABF = ActionBeforeMiddleware<Action>;

export function loadCurrentStatus(): ABF {
  return {
    type: ActionType.LoadCurrentStatus,
    pledge: JobsBackend.currentStatus(),
  };
}

export function setCurrentStatus(st: Readonly<api.ICurrentStatus>): ABF {
  return {
    type: ActionType.SetCurrentStatus,
    st,
  };
}

export function receiveNewLogEntry(entry: Readonly<api.ILogEntry>): ABF {
  return {
    type: ActionType.ReceiveNewLogEntry,
    entry,
  };
}

export function setJobComment(uniq: string, comment: string): ABF {
  return {
    type: ActionType.SetJobComment,
    uniq,
    comment,
    pledge: JobsBackend.setJobComment(uniq, comment),
  };
}

function process_new_events(entry: Readonly<api.ILogEntry>, s: State): State {
  let mats: HashMap<number, Readonly<api.InProcessMaterial>> | undefined;
  function adjustMat(id: number, f: (mat: Readonly<api.IInProcessMaterial>) => Readonly<api.IInProcessMaterial>) {
    if (mats === undefined) {
      mats = s.current_status.material.reduce(
        (map, mat) => map.put(mat.materialID, mat),
        HashMap.empty<number, Readonly<api.InProcessMaterial>>()
      );
    }
    const oldMat = mats.get(id);
    if (oldMat.isSome()) {
      mats = mats.put(id, new api.InProcessMaterial(f(oldMat.get())));
    }
  }

  switch (entry.type) {
    case api.LogType.PartMark:
      for (const m of entry.material) {
        adjustMat(m.id, (inmat) => ({ ...inmat, serial: entry.result }));
      }
      break;

    case api.LogType.OrderAssignment:
      for (const m of entry.material) {
        adjustMat(m.id, (inmat) => ({ ...inmat, workorderId: entry.result }));
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
          for (const m of entry.material) {
            adjustMat(m.id, (inmat) => ({
              ...inmat,
              signaledInspections: [...inmat.signaledInspections, inspType],
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
        material: Array.from(mats.valueIterable()),
      },
    };
  }
}

function reorder_queued_mat(
  queue: string,
  matId: number,
  newIdx: number,
  oldMats: InProcessMaterial[]
): InProcessMaterial[] {
  const oldMat = oldMats.find((i) => i.materialID === matId);
  if (!oldMat || oldMat.location.type !== api.LocType.InQueue) {
    return oldMats;
  }
  if (oldMat.location.currentQueue === queue && oldMat.location.queuePosition === newIdx) {
    return oldMats;
  }

  const oldQueue = oldMat.location.currentQueue;
  const oldIdx = oldMat.location.queuePosition;
  if (oldIdx === undefined || oldQueue === undefined) {
    return oldMats;
  }

  return oldMats.map((m) => {
    if (
      m.location.type !== api.LocType.InQueue ||
      m.location.queuePosition === undefined ||
      m.location.currentQueue === undefined
    ) {
      return m;
    }

    if (m.materialID === matId) {
      return new InProcessMaterial({
        ...m,
        location: { type: api.LocType.InQueue, currentQueue: queue, queuePosition: newIdx },
      } as api.IInProcessMaterial);
    }

    let idx = m.location.queuePosition;
    // old queue material is moved down
    if (m.location.currentQueue === oldQueue && m.location.queuePosition > oldIdx) {
      idx -= 1;
    }
    // new queue material is moved up to make room
    if (m.location.currentQueue === queue && idx >= newIdx) {
      idx += 1;
    }

    if (idx !== m.location.queuePosition) {
      return new InProcessMaterial({
        ...m,
        location: { ...m.location, queuePosition: idx },
      } as api.IInProcessMaterial);
    } else {
      return m;
    }
  });
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
          };
        case PledgeStatus.Error:
          return { ...s, loading_error: a.pledge.error };

        default:
          return s;
      }
    case ActionType.SetCurrentStatus:
      return { ...s, current_status: a.st };

    case ActionType.ReceiveNewLogEntry:
      return process_new_events(a.entry, s);

    case ActionType.ReorderQueuedMaterial:
      return {
        ...s,
        current_status: {
          ...s.current_status,
          material: reorder_queued_mat(a.queue, a.materialId, a.newIdx, s.current_status.material),
        },
      };

    case ActionType.SetJobComment:
      switch (a.pledge.status) {
        case PledgeStatus.Starting:
          let newSt = s.current_status;
          const oldJob = s.current_status.jobs[a.uniq];
          if (oldJob) {
            var newJob = new api.InProcessJob(oldJob);
            newJob.comment = a.comment;
            newSt = { ...newSt, jobs: { ...newSt.jobs, [a.uniq]: newJob } };
          }
          return { ...s, loading: true, loading_error: undefined, current_status: newSt };
        case PledgeStatus.Error:
          return { ...s, loading: false, loading_error: a.pledge.error };
        case PledgeStatus.Completed:
          return { ...s, loading: false };
      }

    case ActionType.SetJobPlannedQty: {
      const oldJob = s.current_status.jobs[a.uniq];
      if (oldJob) {
        var newJob = new api.InProcessJob(oldJob);
        newJob.cyclesOnFirstProcess[a.proc1path - 1] = a.qty;
        return { ...s, current_status: { ...s.current_status, jobs: { ...s.current_status.jobs, [a.uniq]: newJob } } };
      } else {
        return s;
      }
    }

    default:
      return s;
  }
}
