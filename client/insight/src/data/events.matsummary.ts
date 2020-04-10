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
import { addDays } from "date-fns";
import { HashMap, HashSet, Vector } from "prelude-ts";
import { LazySeq } from "./lazyseq";

export interface MaterialSummary {
  readonly materialID: number;
  readonly jobUnique: string;
  readonly partName: string;
  readonly completed_procs: ReadonlyArray<number>;

  readonly serial?: string;
  readonly workorderId?: string;
  readonly signaledInspections: ReadonlyArray<string>;
}

export interface MaterialSummaryAndCompletedData extends MaterialSummary {
  readonly completed_machining?: boolean;
  readonly last_unload_time?: Date;
  readonly completed_inspect_time?: Date;
  readonly wash_completed?: Date;
  readonly completedInspections?: { [key: string]: Date };
}

interface MaterialSummaryFromEvents extends MaterialSummaryAndCompletedData {
  readonly last_event: Date;
}

export interface MatSummaryState {
  readonly matsById: HashMap<number, MaterialSummaryFromEvents>;
  readonly inspTypes: HashSet<string>;
}

export const initial: MatSummaryState = {
  matsById: HashMap.empty(),
  inspTypes: HashSet.empty(),
};

export function inproc_mat_to_summary(mat: Readonly<api.IInProcessMaterial>): MaterialSummary {
  return {
    materialID: mat.materialID,
    jobUnique: mat.jobUnique,
    partName: mat.partName,
    completed_procs: LazySeq.ofRange(1, mat.process, 1).toArray(),
    serial: mat.serial,
    workorderId: mat.workorderId,
    signaledInspections: mat.signaledInspections,
  };
}

export function process_events(now: Date, newEvts: ReadonlyArray<api.ILogEntry>, st: MatSummaryState): MatSummaryState {
  const oneWeekAgo = addDays(now, -7);

  // check if no changes needed: no new events and nothing to filter out
  const minEntry = LazySeq.ofIterable(st.matsById.valueIterable()).minBy(
    (m1, m2) => m1.last_event.getTime() - m2.last_event.getTime()
  );
  if ((minEntry.isNone() || minEntry.get().last_event >= oneWeekAgo) && newEvts.length === 0) {
    return st;
  }

  let mats = st.matsById.filter((_, e) => e.last_event >= oneWeekAgo);

  const inspTypes = new Map<string, boolean>();

  newEvts.forEach((e) => {
    if (e.startofcycle || e.material.length === 0) {
      return;
    }
    for (const logMat of e.material) {
      const oldMat = mats.get(logMat.id);
      let mat: MaterialSummaryFromEvents;
      if (oldMat.isSome()) {
        mat = { ...oldMat.get(), last_event: e.endUTC };
      } else {
        mat = {
          materialID: logMat.id,
          jobUnique: logMat.uniq,
          partName: logMat.part,
          last_event: e.endUTC,
          completed_procs: [],
          signaledInspections: [],
          completedInspections: {},
        };
      }

      switch (e.type) {
        case api.LogType.PartMark:
          mat = { ...mat, serial: e.result };
          break;

        case api.LogType.OrderAssignment:
          mat = { ...mat, workorderId: e.result };
          break;

        case api.LogType.Inspection:
          if (e.result.toLowerCase() === "true" || e.result === "1") {
            const inspType = (e.details || {}).InspectionType;
            if (inspType) {
              mat = {
                ...mat,
                signaledInspections: [...mat.signaledInspections, inspType],
              };
              inspTypes.set(inspType, true);
            }
          }
          break;

        case api.LogType.InspectionForce:
          if (e.result.toLowerCase() === "true" || e.result === "1") {
            const inspType = e.program;
            mat = {
              ...mat,
              signaledInspections: [...mat.signaledInspections, inspType],
            };
            inspTypes.set(inspType, true);
          }
          break;

        case api.LogType.InspectionResult:
          mat = {
            ...mat,
            completedInspections: {
              ...mat.completedInspections,
              [e.program]: e.endUTC,
            },
            completed_inspect_time: e.endUTC,
          };
          inspTypes.set(e.program, true);
          break;

        case api.LogType.LoadUnloadCycle:
          if (e.result === "UNLOAD") {
            if (logMat.proc === logMat.numproc) {
              mat = {
                ...mat,
                completed_procs: [...mat.completed_procs, logMat.proc],
                last_unload_time: e.endUTC,
                completed_machining: true,
              };
            } else {
              mat = {
                ...mat,
                completed_procs: [...mat.completed_procs, logMat.proc],
                last_unload_time: e.endUTC,
              };
            }
          }
          break;

        case api.LogType.Wash:
          mat = { ...mat, wash_completed: e.endUTC };
          break;
      }

      mats = mats.put(logMat.id, mat);
    }
  });

  let inspTypesSet = st.inspTypes;
  const newInspTypes = Vector.ofIterable(inspTypes.keys()).filter((i) => !inspTypesSet.contains(i));

  if (!newInspTypes.isEmpty()) {
    inspTypesSet = inspTypesSet.addAll(newInspTypes);
  }

  return { ...st, matsById: mats, inspTypes: inspTypesSet };
}
