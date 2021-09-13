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
import { HashMap } from "prelude-ts";
import { JobsBackend } from "../data/backend";
import {
  InProcessMaterial,
  ICurrentStatus,
  IInProcessMaterial,
  ILogEntry,
  LogType,
  LocType,
  ActiveJob,
} from "../data/api";
import { atom, DefaultValue, selectorFamily } from "recoil";
import { updateJobComment } from "./scheduled-jobs";

export const currentStatus = atom<Readonly<ICurrentStatus>>({
  key: "current-status",
  default: {
    timeOfCurrentStatusUTC: new Date(),
    jobs: {},
    pallets: {},
    material: [],
    alarms: [],
    queues: {},
  },
});

export const currentStatusJobComment = selectorFamily<string | null, string>({
  key: "current-status-job-comment",
  get:
    (uniq) =>
    ({ get }) =>
      get(currentStatus).jobs[uniq]?.comment ?? null,
  set:
    (uniq) =>
    async ({ set }, newVal) => {
      const newComment = newVal instanceof DefaultValue || newVal === null ? "" : newVal;

      set(currentStatus, (st) => {
        const oldJob = st.jobs[uniq];
        if (oldJob) {
          const newJob = new ActiveJob(oldJob);
          newJob.comment = newComment;
          return { ...st, jobs: { ...st.jobs, [uniq]: newJob } };
        } else {
          return st;
        }
      });

      updateJobComment(set, uniq, newComment);

      await JobsBackend.setJobComment(uniq, newComment);
    },
  //cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export function processEventsIntoCurrentStatus(
  entry: Readonly<ILogEntry>
): (curSt: Readonly<ICurrentStatus>) => Readonly<ICurrentStatus> {
  return (curSt) => {
    let mats: HashMap<number, Readonly<InProcessMaterial>> | undefined;
    function adjustMat(id: number, f: (mat: Readonly<IInProcessMaterial>) => Readonly<IInProcessMaterial>) {
      if (mats === undefined) {
        mats = curSt.material.reduce(
          (map, mat) => map.put(mat.materialID, mat),
          HashMap.empty<number, Readonly<InProcessMaterial>>()
        );
      }
      const oldMat = mats.get(id);
      if (oldMat.isSome()) {
        mats = mats.put(id, new InProcessMaterial(f(oldMat.get())));
      }
    }

    switch (entry.type) {
      case LogType.PartMark:
        for (const m of entry.material) {
          adjustMat(m.id, (inmat) => ({ ...inmat, serial: entry.result }));
        }
        break;

      case LogType.OrderAssignment:
        for (const m of entry.material) {
          adjustMat(m.id, (inmat) => ({ ...inmat, workorderId: entry.result }));
        }
        break;

      case LogType.Inspection:
      case LogType.InspectionForce:
        if (entry.result.toLowerCase() === "true" || entry.result === "1") {
          let inspType: string;
          if (entry.type === LogType.InspectionForce) {
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
      return curSt;
    } else {
      return {
        ...curSt,
        material: Array.from(mats.valueIterable()),
      };
    }
  };
}

export function reorder_queued_mat(
  queue: string,
  matId: number,
  newIdx: number
): (curSt: Readonly<ICurrentStatus>) => Readonly<ICurrentStatus> {
  return (curSt) => {
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
          ...m,
          location: { type: LocType.InQueue, currentQueue: queue, queuePosition: newIdx },
        } as IInProcessMaterial);
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
        } as IInProcessMaterial);
      } else {
        return m;
      }
    });

    return { ...curSt, material: newMats };
  };
}
