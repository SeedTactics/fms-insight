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
import * as api from "../network/api";
import { HashMap, HashSet, Vector } from "prelude-ts";
import { LazySeq } from "../util/lazyseq";
import { ExpireOldData, ExpireOldDataType } from "./events.cycles";

export interface MaterialSummary {
  readonly materialID: number;
  readonly jobUnique: string;
  readonly partName: string;
  readonly startedProcess1: boolean;

  readonly serial?: string;
  readonly workorderId?: string;
  readonly signaledInspections: ReadonlyArray<string>;
}

export interface MaterialSummaryAndCompletedData extends MaterialSummary {
  readonly numProcesses?: number;
  readonly unloaded_processes?: { [process: number]: Date };
  readonly last_unload_time?: Date;
  readonly completed_last_proc_machining?: boolean;
  readonly completed_inspect_time?: Date;
  readonly wash_completed?: Date;
  readonly completedInspections?: { [key: string]: { time: Date; success: boolean } };
}

interface MaterialSummaryFromEvents extends MaterialSummaryAndCompletedData {
  readonly last_event: Date;
}

export interface MatSummaryState {
  readonly matsById: HashMap<number, MaterialSummaryFromEvents>;
  readonly matIdsForJob: HashMap<string, HashSet<number>>;
  readonly inspTypes: HashSet<string>;
}

export const initial: MatSummaryState = {
  matsById: HashMap.empty(),
  matIdsForJob: HashMap.empty(),
  inspTypes: HashSet.empty(),
};

export function inproc_mat_to_summary(mat: Readonly<api.IInProcessMaterial>): MaterialSummary {
  return {
    materialID: mat.materialID,
    jobUnique: mat.jobUnique,
    partName: mat.partName,
    startedProcess1: mat.process > 0,
    serial: mat.serial,
    workorderId: mat.workorderId,
    signaledInspections: mat.signaledInspections,
  };
}

export function process_events(
  expire: ExpireOldData,
  newEvts: ReadonlyArray<api.ILogEntry>,
  st: MatSummaryState
): MatSummaryState {
  let mats = st.matsById;
  let jobs = st.matIdsForJob;

  if (expire.type === ExpireOldDataType.ExpireEarlierThan) {
    // check if no changes needed: no new events and nothing to filter out
    const minEntry = LazySeq.ofIterable(mats.valueIterable()).minBy(
      (m1, m2) => m1.last_event.getTime() - m2.last_event.getTime()
    );
    if ((minEntry.isNone() || minEntry.get().last_event >= expire.d) && newEvts.length === 0) {
      return st;
    }

    const matsToRemove = LazySeq.ofIterable(mats.valueIterable())
      .filter((e) => e.last_event < expire.d)
      .map((e) => e.materialID)
      .toRSet((x) => x);

    if (matsToRemove.size > 0) {
      jobs = HashMap.ofIterable(
        LazySeq.ofIterable(jobs)
          .map(([uniq, ids]) => [uniq, ids.removeAll(matsToRemove)] as [string, HashSet<number>])
          .filter(([_, ids]) => !ids.isEmpty())
      );
    }

    mats = mats.filter((_, e) => e.last_event >= expire.d);
  }

  const inspTypes = new Map<string, boolean>();

  newEvts.forEach((e) => {
    if (e.startofcycle || e.material.length === 0) {
      return;
    }
    for (const logMat of e.material) {
      if (logMat.uniq && logMat.uniq !== "") {
        const forJob = jobs.get(logMat.uniq).getOrNull();
        if (forJob === null) {
          jobs = jobs.put(logMat.uniq, HashSet.of(logMat.id));
        } else {
          if (!forJob.contains(logMat.id)) {
            jobs = jobs.put(logMat.uniq, forJob.add(logMat.id));
          }
        }
      }

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
          startedProcess1: false,
          numProcesses: logMat.numproc,
          unloaded_processes: {},
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

        case api.LogType.InspectionResult: {
          const success = e.result.toLowerCase() == "true" || e.result === "1";
          mat = {
            ...mat,
            completedInspections: {
              ...mat.completedInspections,
              [e.program]: { time: e.endUTC, success },
            },
            completed_inspect_time: e.endUTC,
          };
          inspTypes.set(e.program, true);
          break;
        }

        case api.LogType.LoadUnloadCycle:
          if (e.result === "UNLOAD") {
            mat = {
              ...mat,
              unloaded_processes: { ...mat.unloaded_processes, [logMat.proc]: e.endUTC },
            };
            if (logMat.proc === logMat.numproc) {
              mat = {
                ...mat,
                last_unload_time: e.endUTC,
                completed_last_proc_machining: true,
              };
            } else {
              mat = {
                ...mat,
                last_unload_time: e.endUTC,
              };
            }
          } else if (e.result === "LOAD") {
            mat = {
              ...mat,
              startedProcess1: true,
            };
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

  return { ...st, matsById: mats, matIdsForJob: jobs, inspTypes: inspTypesSet };
}

export function process_swap(swap: Readonly<api.IEditMaterialInLogEvents>, st: MatSummaryState): MatSummaryState {
  let jobs = st.matIdsForJob;
  const oldMatFromState = st.matsById.get(swap.oldMaterialID).getOrNull();
  const newMatFromState = st.matsById.get(swap.newMaterialID).getOrNull();

  if (oldMatFromState === null) return st;
  let oldMat = oldMatFromState;

  let newMat: MaterialSummaryFromEvents;
  if (newMatFromState === null) {
    newMat = {
      materialID: swap.newMaterialID,
      jobUnique: oldMat.jobUnique,
      partName: oldMat.partName,
      last_event: oldMat.last_event,
      numProcesses: oldMat.numProcesses,
      startedProcess1: true,
      unloaded_processes: {},
      signaledInspections: [],
      completedInspections: {},
    };
  } else {
    newMat = newMatFromState;
  }

  if (oldMat.jobUnique && oldMat.jobUnique !== "" && (!newMat.jobUnique || newMat.jobUnique === "")) {
    // Swap newMat from raw material
    const forJob = jobs.get(oldMat.jobUnique).getOrNull();
    if (forJob !== null) {
      jobs = jobs.put(oldMat.jobUnique, forJob.remove(oldMat.materialID).add(newMat.materialID));
    }
    newMat = { ...newMat, jobUnique: oldMat.jobUnique };
    oldMat = { ...oldMat, jobUnique: "" };
  }

  const oldMatUnloads = oldMat.unloaded_processes;
  oldMat = { ...oldMat, unloaded_processes: newMat.unloaded_processes };
  newMat = { ...newMat, unloaded_processes: oldMatUnloads };

  for (const evt of swap.editedEvents) {
    const newMatFromEvt = evt.material.find((m) => m.id === swap.newMaterialID);
    if (newMatFromEvt) {
      newMat = {
        ...newMat,
        serial: newMatFromEvt.serial ?? newMat.serial,
        workorderId: newMatFromEvt.workorder ?? newMat.workorderId,
      };
    }

    switch (evt.type) {
      case api.LogType.Inspection:
      case api.LogType.InspectionForce: {
        const inspType = evt.program;
        let inspect: boolean;
        if (evt.result.toLowerCase() === "true" || evt.result === "1") {
          inspect = true;
        } else {
          inspect = false;
        }
        if (inspect) {
          // remove from oldMat, add to newMat
          oldMat = {
            ...oldMat,
            signaledInspections: LazySeq.ofIterable(oldMat.signaledInspections)
              .filter((i) => i !== inspType)
              .toArray(),
          };
          newMat = {
            ...newMat,
            signaledInspections: LazySeq.ofIterable([...newMat.signaledInspections, inspType])
              .toSet((x) => x)
              .toArray(),
          };
        }
      }
    }
  }

  return {
    matsById: st.matsById.put(oldMat.materialID, oldMat).put(newMat.materialID, newMat),
    matIdsForJob: jobs,
    inspTypes: st.inspTypes,
  };
}
