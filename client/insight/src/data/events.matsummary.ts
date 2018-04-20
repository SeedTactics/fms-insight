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
import { addDays } from 'date-fns';
import * as im from 'immutable'; // consider collectable.js at some point?

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
  readonly completed_time?: Date;
  readonly wash_completed?: Date;
  readonly completedInspections?: {[key: string]: Date};
}

interface MaterialSummaryFromEvents extends MaterialSummaryAndCompletedData {
  readonly last_event: Date;
}

export interface MatSummaryState {
  readonly matsById: im.Map<number, MaterialSummaryFromEvents>;
  readonly inspTypes: im.Set<string>;
}

export const initial: MatSummaryState = {
  matsById: im.Map(),
  inspTypes: im.Set(),
};

export function inproc_mat_to_summary(mat: Readonly<api.IInProcessMaterial>): MaterialSummary {
  return {
    materialID: mat.materialID,
    jobUnique: mat.jobUnique,
    partName: mat.partName,
    completed_procs: im.Range(1, mat.process, 1).toArray(),
    serial: mat.serial,
    workorderId: mat.workorderId,
    signaledInspections: mat.signaledInspections,
  };
}

export function process_events(now: Date, newEvts: Iterable<api.ILogEntry>, st: MatSummaryState): MatSummaryState {
  const oneWeekAgo = addDays(now, -7);

  const evtsSeq = im.Seq(newEvts);

  // check if no changes needed: no new events and nothing to filter out
  const minEntry = st.matsById.valueSeq().minBy(m => m.last_event);
  if ((minEntry === undefined || minEntry.last_event >= oneWeekAgo) && evtsSeq.isEmpty()) {
      return st;
  }

  let mats = st.matsById.filter(e => e.last_event >= oneWeekAgo);

  let inspTypes = new Map<string, boolean>();

  evtsSeq
    .filter(e => !e.startofcycle && e.material.length > 0)
    .forEach(e => {
    for (let logMat of e.material) {
      let mat = mats.get(logMat.id);
      if (mat) {
        mat = {...mat, last_event: e.endUTC};
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
          mat = {...mat, serial: e.result};
          break;

        case api.LogType.OrderAssignment:
          mat = {...mat, workorderId: e.result};
          break;

        case api.LogType.Inspection:
          if (e.result.toLowerCase() === "true" || e.result === "1") {
            const entries = e.program.split(",");
            if (entries.length >= 2) {
              mat = {...mat, signaledInspections: [...mat.signaledInspections, entries[1]]};
              inspTypes.set(entries[1], true);
            }
          }
          break;

        case api.LogType.InspectionResult:
          mat = {...mat, completedInspections: {...mat.completedInspections,
            [e.program]: e.endUTC}};
          inspTypes.set(e.program, true);
          break;

        case api.LogType.LoadUnloadCycle:
          if (e.result === "UNLOAD") {
            if (logMat.proc === logMat.numproc) {
              mat = {...mat,
                completed_procs: [...mat.completed_procs, logMat.proc],
                completed_time: e.endUTC
              };
            } else {
              mat = {...mat,
                completed_procs: [...mat.completed_procs, logMat.proc],
              };
            }
          }
          break;

        case api.LogType.Wash:
          mat = {...mat, wash_completed: e.endUTC};
          break;
      }

      mats = mats.set(logMat.id, mat);
    }
  });

  var inspTypesSet = st.inspTypes;
  var newInspTypes = im.Seq.Keyed(inspTypes)
    .keySeq()
    .filter(i => !inspTypesSet.contains(i));

  if (!newInspTypes.isEmpty()) {
    inspTypesSet = inspTypesSet.union(newInspTypes);
  }

  return {...st, matsById: mats, inspTypes: inspTypesSet};
}