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
import { atom, RecoilValueReadOnly, TransactionInterface_UNSTABLE } from "recoil";
import { ICurrentStatus, IHistoricData, IJob, ILogEntry, LogType } from "../network/api.js";
import { conduit } from "../util/recoil-util.js";
import type { ServerEventAndTime } from "./loading.js";

const rawMaterialQueuesRW = atom<ReadonlySet<string>>({
  key: "rawMaterialQueueNames",
  default: new Set(),
});
export const rawMaterialQueues: RecoilValueReadOnly<ReadonlySet<string>> = rawMaterialQueuesRW;

const castingNamesRW = atom<ReadonlySet<string>>({
  key: "castingNames",
  default: new Set(),
});
export const castingNames: RecoilValueReadOnly<ReadonlySet<string>> = castingNamesRW;

const inspectionTypesRW = atom<ReadonlySet<string>>({
  key: "last30-inspectionTypes",
  default: new Set(),
});
export const last30InspectionTypes: RecoilValueReadOnly<ReadonlySet<string>> = inspectionTypesRW;

export const updateNames = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt }: ServerEventAndTime) => {
    if (evt.newJobs) {
      onNewJobs(t, evt.newJobs.jobs);
    } else if (evt.logEntry) {
      onLog(t, [evt.logEntry]);
    } else if (evt.newCurrentStatus) {
      onCurrentStatus(t, evt.newCurrentStatus);
    }
  }
);

export const setNamesFromLast30Jobs = conduit<Readonly<IHistoricData>>(
  (t: TransactionInterface_UNSTABLE, history: Readonly<IHistoricData>) => {
    onNewJobs(t, Object.values(history.jobs));
  }
);

export const setNamesFromLast30Evts = conduit<ReadonlyArray<Readonly<ILogEntry>>>(onLog);

export const setNamesFromCurrentStatus = conduit<Readonly<ICurrentStatus>>(onCurrentStatus);

function onNewJobs(t: TransactionInterface_UNSTABLE, newJobs: ReadonlyArray<Readonly<IJob>>): void {
  t.set(rawMaterialQueuesRW, (queues) => {
    const newQ = new Set<string>();
    for (const j of newJobs) {
      for (const path of j.procsAndPaths[0].paths) {
        if (path.inputQueue && path.inputQueue !== "" && !queues.has(path.inputQueue)) {
          newQ.add(path.inputQueue);
        }
      }
    }
    if (newQ.size === 0) {
      return queues;
    } else {
      return new Set([...queues, ...newQ]);
    }
  });

  t.set(castingNamesRW, (names) => {
    const newC = new Set<string>();
    for (const j of newJobs) {
      for (const path of j.procsAndPaths[0].paths) {
        if (path.casting && path.casting !== "" && !names.has(path.casting)) {
          newC.add(path.casting);
        }
      }
    }
    if (newC.size === 0) {
      return names;
    } else {
      return new Set([...names, ...newC]);
    }
  });
}

function onCurrentStatus(t: TransactionInterface_UNSTABLE, st: Readonly<ICurrentStatus>): void {
  onNewJobs(t, Object.values(st.jobs));
}

function onLog(t: TransactionInterface_UNSTABLE, evts: Iterable<Readonly<ILogEntry>>): void {
  t.set(inspectionTypesRW, (inspTypes) => {
    const newC = new Set<string>();
    for (const evt of evts) {
      switch (evt.type) {
        case LogType.Inspection:
          {
            const inspType = (evt.details || {}).InspectionType;
            if (inspType && !inspTypes.has(inspType)) {
              newC.add(inspType);
            }
          }
          break;

        case LogType.InspectionForce:
        case LogType.InspectionResult:
          {
            const inspType = evt.program;
            if (!inspTypes.has(inspType)) {
              newC.add(inspType);
            }
          }
          break;
      }
    }
    if (newC.size === 0) {
      return inspTypes;
    } else {
      return new Set([...inspTypes, ...newC]);
    }
  });
}
