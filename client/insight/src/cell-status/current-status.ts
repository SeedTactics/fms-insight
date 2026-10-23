/* Copyright (c) 2021, John Lenz

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
import { JobsBackend, LogBackend } from "../network/backend.js";
import {
  InProcessMaterial,
  ICurrentStatus,
  IInProcessMaterial,
  IInProcessMaterialLocation,
  ILogEntry,
  LogType,
  LocType,
  ActiveJob,
  WorkorderComment,
  ActiveWorkorder,
  IActiveWorkorder,
  InProcessMaterialLocation,
} from "../network/api.js";
import { last30JobComment } from "./scheduled-jobs.js";
import type { ServerEventAndTime } from "./loading.js";
import { Atom, atom } from "jotai";
import { HashSet } from "@seedtactics/immutable-collections";
import { currentOperator } from "../data/operators.js";

const currentStatusRW = atom<Readonly<ICurrentStatus>>({
  revision: 0,
  timeOfCurrentStatusUTC: new Date(),
  jobs: {},
  pallets: {},
  material: [],
  alarms: [],
  queues: {},
});
export const currentStatus: Atom<ICurrentStatus> = currentStatusRW;

export const secondsSinceEpochAtom = atom(Math.floor(Date.now() / 1000));
secondsSinceEpochAtom.onMount = (setSelf) => {
  const interval = setInterval(() => {
    setSelf(Math.floor(Date.now() / 1000));
  }, 1000);
  return () => clearInterval(interval);
};

// Minute precision keeps report time windows current without redrawing charts every second.
export const minutesSinceEpochAtom = atom((get) => Math.floor(get(secondsSinceEpochAtom) / 60));

export const setJobComment = atom(null, (_, set, uniq: string, newVal: string | null) => {
  const newComment = newVal === null ? "" : newVal;

  set(currentStatusRW, (st) => {
    const oldJob = st.jobs[uniq];
    if (oldJob) {
      const newJob = new ActiveJob(oldJob);
      newJob.comment = newComment;
      return { ...st, jobs: { ...st.jobs, [uniq]: newJob } };
    } else {
      return st;
    }
  });

  set(last30JobComment(uniq), newComment);

  void JobsBackend.setJobComment(uniq, newComment);
});

export const addWorkorderComment = atom(
  null,
  (get, set, { workorder, comment }: { workorder: string; comment: string }) => {
    const operator = get(currentOperator);
    set(currentStatusRW, (st) => {
      if (!st.workorders) return st;
      return {
        ...st,
        workorders: st.workorders.map((w) =>
          w.workorderId === workorder
            ? new ActiveWorkorder({
                ...(w as IActiveWorkorder),
                comments: [
                  ...(w.comments ?? []),
                  new WorkorderComment({ comment, timeUTC: new Date() }),
                ],
              })
            : w,
        ),
      };
    });

    void LogBackend.recordWorkorderComment(workorder, operator, comment);
  },
);

export const setCurrentStatus = atom(null, (_, set, st: Readonly<ICurrentStatus>) =>
  set(currentStatusRW, st),
);

export const updateCurrentStatus = atom(null, (_, set, { evt }: ServerEventAndTime) => {
  if (evt.logEntry) {
    set(currentStatusRW, processEventsIntoCurrentStatus(evt.logEntry));
  } else if (evt.newCurrentStatus) {
    set(currentStatusRW, evt.newCurrentStatus);
  }
});

function processEventsIntoCurrentStatus(
  entry: Readonly<ILogEntry>,
): (curSt: Readonly<ICurrentStatus>) => Readonly<ICurrentStatus> {
  return (curSt) => {
    if (
      entry.type !== LogType.PartMark &&
      entry.type !== LogType.OrderAssignment &&
      entry.type !== LogType.Inspection &&
      entry.type !== LogType.InspectionForce
    )
      return curSt;
    const materialIds = HashSet.from(entry.material.map((m) => m.id));
    // Anonymous raw material can repeat ID -1, including several pieces in the same slot. A log
    // updates identified material in place; it must never rebuild these targets through an ID map.
    const material = curSt.material.map((mat) => {
      if (mat.materialID < 0 || !materialIds.has(mat.materialID)) return mat;
      const data: Readonly<IInProcessMaterial> = mat;
      switch (entry.type) {
        case LogType.PartMark:
          return mat.serial === entry.result
            ? mat
            : new InProcessMaterial({ ...data, serial: entry.result });

        case LogType.OrderAssignment:
          return mat.workorderId === entry.result
            ? mat
            : new InProcessMaterial({ ...data, workorderId: entry.result });

        case LogType.Inspection:
        case LogType.InspectionForce:
          if (entry.result.toLowerCase() === "true" || entry.result === "1") {
            let inspType: string;
            if (entry.type === LogType.InspectionForce) {
              inspType = entry.program;
            } else {
              inspType = (entry.details || {}).InspectionType;
            }
            if (inspType && !mat.signaledInspections.includes(inspType))
              return new InProcessMaterial({
                ...data,
                signaledInspections: [...mat.signaledInspections, inspType],
              });
          }
          break;
      }
      return mat;
    });
    return material.every((mat, idx) => mat === curSt.material[idx])
      ? curSt
      : { ...curSt, material };
  };
}

export interface QueueReordering {
  readonly queue: string;
  readonly matId: number;
  readonly newIdx: number;
}

export const reorderQueuedMatInCurrentStatus = atom(
  null,
  (_, set, { queue, matId, newIdx }: QueueReordering) => {
    set(currentStatusRW, (curSt) => {
      const oldMat = curSt.material.find((i) => i.materialID === matId);
      if (!oldMat || oldMat.location.type !== LocType.InQueue) {
        return curSt;
      }
      if (oldMat.location.currentQueue === queue && oldMat.location.queuePosition === newIdx) {
        return curSt;
      }

      const oldQueue = oldMat.location.currentQueue;
      const oldIdx = oldMat.location.queuePosition;
      if (oldIdx === undefined || oldQueue === undefined) {
        return curSt;
      }

      const newMats = curSt.material.map((m) => {
        if (
          m.location.type !== LocType.InQueue ||
          m.location.queuePosition === undefined ||
          m.location.currentQueue === undefined
        ) {
          return m;
        }

        if (m.materialID === matId) {
          return new InProcessMaterial({
            ...(m as IInProcessMaterial),
            location: new InProcessMaterialLocation({
              ...(m.location as IInProcessMaterialLocation),
              type: LocType.InQueue,
              currentQueue: queue,
              queuePosition: newIdx,
            }),
          });
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
            ...(m as IInProcessMaterial),
            location: new InProcessMaterialLocation({
              ...(m.location as IInProcessMaterialLocation),
              queuePosition: idx,
            }),
          });
        } else {
          return m;
        }
      });

      return { ...curSt, material: newMats };
    });
  },
);
