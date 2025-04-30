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
import { IEditMaterialInLogEvents, IInProcessMaterial, ILogEntry, LogType } from "../network/api.js";
import type { ServerEventAndTime } from "./loading.js";
import { addDays } from "date-fns";
import { HashMap, LazySeq, HashSet } from "@seedtactics/immutable-collections";
import { Atom, atom } from "jotai";

export interface MaterialSummary {
  readonly materialID: number;
  readonly jobUnique: string;
  readonly partName: string;
  readonly startedProcess1: boolean;

  readonly serial?: string;
  readonly workorderId?: string;
  readonly signaledInspections: ReadonlyArray<string>;
  readonly quarantineAfterUnload: boolean | null | undefined;
}

export interface MaterialSummaryAndCompletedData extends MaterialSummary {
  readonly numProcesses?: number;
  readonly unloaded_processes?: { [process: number]: Date };
  readonly last_unload_time?: Date;
  readonly completed_last_proc_machining?: boolean;
  readonly completed_inspect_time?: Date;
  readonly closeout_completed?: Date;
  readonly closeout_failed?: boolean;
  readonly completedInspections?: { [key: string]: { time: Date; success: boolean } };
  readonly currently_quarantined?: boolean;
}

interface MaterialSummaryFromEvents extends MaterialSummaryAndCompletedData {
  readonly last_event: Date;
}

export interface MatSummaryState {
  readonly matsById: HashMap<number, MaterialSummaryFromEvents>;
  readonly matIdsForJob: HashMap<string, HashSet<number>>;
}

const last30MaterialSummaryRW = atom({
  matsById: HashMap.empty(),
  matIdsForJob: HashMap.empty(),
} satisfies MatSummaryState);
export const last30MaterialSummary: Atom<MatSummaryState> = last30MaterialSummaryRW;

const specificMonthMaterialSummaryRW = atom<MatSummaryState>({
  matsById: HashMap.empty(),
  matIdsForJob: HashMap.empty(),
} satisfies MatSummaryState);
export const specificMonthMaterialSummary: Atom<MatSummaryState> = specificMonthMaterialSummaryRW;

export function inproc_mat_to_summary(mat: Readonly<IInProcessMaterial>): MaterialSummary {
  return {
    materialID: mat.materialID,
    jobUnique: mat.jobUnique,
    partName: mat.partName,
    startedProcess1: mat.process > 0,
    serial: mat.serial,
    workorderId: mat.workorderId,
    signaledInspections: mat.signaledInspections,
    quarantineAfterUnload: mat.quarantineAfterUnload,
  };
}

function process_event(st: MatSummaryState, e: Readonly<ILogEntry>): MatSummaryState {
  let mats = st.matsById;
  let jobs = st.matIdsForJob;

  if (e.startofcycle || e.material.length === 0) {
    return st;
  }
  for (const logMat of e.material) {
    if (logMat.uniq && logMat.uniq !== "") {
      const forJob = jobs.get(logMat.uniq);
      if (forJob === undefined) {
        jobs = jobs.set(logMat.uniq, HashSet.empty<number>().add(logMat.id));
      } else {
        if (!forJob.has(logMat.id)) {
          jobs = jobs.set(logMat.uniq, forJob.add(logMat.id));
        }
      }
    }

    const oldMat = mats.get(logMat.id);
    let mat: MaterialSummaryFromEvents;
    if (oldMat !== undefined) {
      mat = { ...oldMat, last_event: e.endUTC };
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
        quarantineAfterUnload: null,
        completedInspections: {},
      };
    }

    switch (e.type) {
      case LogType.PartMark:
        mat = { ...mat, serial: e.result };
        break;

      case LogType.OrderAssignment:
        mat = { ...mat, workorderId: e.result };
        break;

      case LogType.Inspection:
        if (e.result.toLowerCase() === "true" || e.result === "1") {
          const inspType = (e.details || {}).InspectionType;
          if (inspType) {
            mat = {
              ...mat,
              signaledInspections: [...mat.signaledInspections, inspType],
            };
          }
        }
        break;

      case LogType.InspectionForce:
        if (e.result.toLowerCase() === "true" || e.result === "1") {
          const inspType = e.program;
          mat = {
            ...mat,
            signaledInspections: [...mat.signaledInspections, inspType],
          };
        }
        break;

      case LogType.InspectionResult: {
        const success = e.result.toLowerCase() == "true" || e.result === "1";
        mat = {
          ...mat,
          completedInspections: {
            ...mat.completedInspections,
            [e.program]: { time: e.endUTC, success },
          },
          completed_inspect_time: e.endUTC,
        };
        break;
      }

      case LogType.LoadUnloadCycle:
        if (e.result === "UNLOAD") {
          mat = {
            ...mat,
            unloaded_processes: { ...mat.unloaded_processes, [logMat.proc]: e.endUTC },
            quarantineAfterUnload: false,
            currently_quarantined: false, // a load cycle disables quarantine
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
            quarantineAfterUnload: false,
            currently_quarantined: false, // a load cycle disables quarantine
          };
        }

        break;

      case LogType.MachineCycle:
        // a machine cycle disables quarantine
        if (mat.currently_quarantined) {
          mat = { ...mat, currently_quarantined: false };
        }
        break;

      case LogType.AddToQueue:
        if (e.program === "Quarantine") {
          mat = { ...mat, currently_quarantined: true };
        }
        break;

      case LogType.CloseOut:
        mat = { ...mat, closeout_completed: e.endUTC, closeout_failed: e.result === "Failed" };
        break;

      case LogType.SignalQuarantine:
        mat = { ...mat, quarantineAfterUnload: true };
    }

    mats = mats.set(logMat.id, mat);
  }

  return { matsById: mats, matIdsForJob: jobs };
}

function filter_old(expire: Date, { matIdsForJob, matsById }: MatSummaryState): MatSummaryState {
  const matsToRemove = matsById
    .valuesToLazySeq()
    .filter((e) => e.last_event < expire)
    .toRSet((e) => e.materialID);

  if (matsToRemove.size > 0) {
    matIdsForJob = matIdsForJob.collectValues((ids) => {
      const newIds = LazySeq.of(matsToRemove).fold(ids, (i, c) => i.delete(c));
      if (newIds.size > 0) {
        return newIds;
      } else {
        return null;
      }
    });
  }

  matsById = matsById.filter((e) => e.last_event >= expire);

  return { matIdsForJob, matsById };
}

function process_swap(swap: Readonly<IEditMaterialInLogEvents>, st: MatSummaryState): MatSummaryState {
  let jobs = st.matIdsForJob;
  const oldMatFromState = st.matsById.get(swap.oldMaterialID) ?? null;
  const newMatFromState = st.matsById.get(swap.newMaterialID) ?? null;

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
      quarantineAfterUnload: null,
      completedInspections: {},
    };
  } else {
    newMat = newMatFromState;
  }

  if (oldMat.jobUnique && oldMat.jobUnique !== "" && (!newMat.jobUnique || newMat.jobUnique === "")) {
    // Swap newMat from raw material
    const forJob = jobs.get(oldMat.jobUnique);
    if (forJob !== undefined) {
      jobs = jobs.set(oldMat.jobUnique, forJob.delete(oldMat.materialID).add(newMat.materialID));
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
      case LogType.Inspection:
      case LogType.InspectionForce: {
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
            signaledInspections: LazySeq.of(oldMat.signaledInspections)
              .filter((i) => i !== inspType)
              .toRArray(),
          };
          newMat = {
            ...newMat,
            signaledInspections: LazySeq.of([...newMat.signaledInspections, inspType])
              .distinct()
              .toSortedArray((x) => x),
          };
        }
      }
    }
  }

  return {
    matsById: st.matsById.set(oldMat.materialID, oldMat).set(newMat.materialID, newMat),
    matIdsForJob: jobs,
  };
}

export const setLast30MatSummary = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(last30MaterialSummaryRW, (st) => log.reduce(process_event, st));
});

export const updateLast30MatSummary = atom(null, (_, set, { evt, now, expire }: ServerEventAndTime) => {
  if (evt.logEntry) {
    const log = evt.logEntry;
    set(last30MaterialSummaryRW, (st) => {
      const newSt = process_event(st, log);
      if (newSt === st || !expire) {
        return st;
      } else {
        return filter_old(addDays(now, -30), newSt);
      }
    });
  } else if (evt.editMaterialInLog) {
    const edit = evt.editMaterialInLog;
    set(last30MaterialSummaryRW, (st) => process_swap(edit, st));
  }
});

export const setSpecificMonthMatSummary = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(
    specificMonthMaterialSummaryRW,
    log.reduce<MatSummaryState>(process_event, {
      matsById: HashMap.empty(),
      matIdsForJob: HashMap.empty(),
    }),
  );
});
