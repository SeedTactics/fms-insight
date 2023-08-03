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
import { Atom, Getter, Setter, atom } from "jotai";
import { ICurrentStatus, IHistoricData, IJob, ILogEntry, LogType, QueueRole } from "../network/api.js";
import type { ServerEventAndTime } from "./loading.js";
import { LazySeq } from "@seedtactics/immutable-collections";

const rawMaterialQueuesRW = atom<ReadonlySet<string>>(new Set<string>());
export const rawMaterialQueues: Atom<ReadonlySet<string>> = rawMaterialQueuesRW;

const castingNamesRW = atom<ReadonlySet<string>>(new Set<string>());
export const castingNames: Atom<ReadonlySet<string>> = castingNamesRW;

const inspectionTypesRW = atom<ReadonlySet<string>>(new Set<string>());
export const last30InspectionTypes: Atom<ReadonlySet<string>> = inspectionTypesRW;

export const updateNames = atom(null, (get, set, { evt }: ServerEventAndTime) => {
  if (evt.newJobs) {
    onNewJobs(set, evt.newJobs.jobs);
  } else if (evt.logEntry) {
    onLog(set, [evt.logEntry]);
  } else if (evt.newCurrentStatus) {
    onCurrentStatus(get, set, evt.newCurrentStatus);
  }
});

export const setNamesFromLast30Jobs = atom(null, (_, set, history: Readonly<IHistoricData>) => {
  onNewJobs(set, Object.values(history.jobs));
});

export const setNamesFromLast30Evts = atom(null, (_, set, logs: ReadonlyArray<Readonly<ILogEntry>>) =>
  onLog(set, logs),
);

export const setNamesFromCurrentStatus = atom(null, onCurrentStatus);

function onNewJobs(set: Setter, newJobs: ReadonlyArray<Readonly<IJob>>): void {
  set(rawMaterialQueuesRW, (queues) => {
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

  set(castingNamesRW, (names) => {
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

function onCurrentStatus(get: Getter, set: Setter, st: Readonly<ICurrentStatus>): void {
  onNewJobs(set, Object.values(st.jobs));

  const rawMatQueues = get(rawMaterialQueuesRW);

  const newQ = new Set<string>();
  for (const [queue, info] of LazySeq.ofObject(st.queues)) {
    if (info.role === QueueRole.RawMaterial) {
      if (!rawMatQueues.has(queue)) {
        newQ.add(queue);
      }
    }
  }
  if (newQ.size > 0) {
    set(rawMaterialQueuesRW, new Set([...rawMatQueues, ...newQ]));
  }
}

function onLog(set: Setter, evts: Iterable<Readonly<ILogEntry>>): void {
  set(inspectionTypesRW, (inspTypes) => {
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
