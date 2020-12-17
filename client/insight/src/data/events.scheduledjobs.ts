/* Copyright (c) 2020, John Lenz

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
import { HashMap, HashSet } from "prelude-ts";
import { LazySeq } from "./lazyseq";
import { ExpireOldData, ExpireOldDataType } from "./events.cycles";

export interface ScheduledJobsState {
  readonly jobs: HashMap<string, Readonly<api.IHistoricJob>>;
  readonly matIdsForJob: HashMap<string, HashSet<number>>;
  readonly someJobHasCasting: boolean;
}

export const initial: ScheduledJobsState = {
  jobs: HashMap.empty(),
  matIdsForJob: HashMap.empty(),
  someJobHasCasting: false,
};

export function process_scheduled_jobs(
  expire: ExpireOldData,
  newHistory: Readonly<api.IHistoricData>,
  st: ScheduledJobsState
): ScheduledJobsState {
  let jobs = st.jobs;
  let matIds = st.matIdsForJob;
  let someJobHasCasting = st.someJobHasCasting;

  switch (expire.type) {
    case ExpireOldDataType.ExpireEarlierThan: {
      // check if nothing to expire and no new data
      const minStat = LazySeq.ofIterable(jobs).minOn(([, e]) => e.routeStartUTC.getTime());

      if (
        (minStat.isNone() || minStat.get()[1].routeStartUTC >= expire.d) &&
        Object.keys(newHistory.jobs).length === 0
      ) {
        return st;
      }

      // filter old events
      for (const [uniq, j] of jobs) {
        if (j.routeStartUTC < expire.d) {
          matIds = matIds.remove(uniq);
        }
      }
      jobs = jobs.filter((_, j) => j.routeStartUTC >= expire.d);

      break;
    }

    case ExpireOldDataType.NoExpire:
      if (Object.keys(newHistory.jobs).length === 0) {
        return st;
      }
      break;
  }

  for (const [newUniq, newJob] of LazySeq.ofObject(newHistory.jobs)) {
    if (!jobs.containsKey(newUniq)) {
      for (const p of newJob.procsAndPaths[0]?.paths ?? []) {
        if (p.casting !== null && p.casting !== undefined && p.casting !== "") {
          someJobHasCasting = true;
        }
      }
      jobs = jobs.put(newUniq, newJob);
    }
  }

  return { jobs, matIdsForJob: matIds, someJobHasCasting };
}

export function process_events(
  evts: Iterable<Readonly<api.ILogEntry>>,
  initial_load: boolean,
  st: ScheduledJobsState
): ScheduledJobsState {
  let matIdsForJob = st.matIdsForJob;

  for (const evt of evts) {
    for (const mat of evt.material) {
      const uniq = mat.uniq;
      if (uniq !== null && uniq !== undefined && uniq !== "") {
        if (initial_load || st.jobs.containsKey(uniq)) {
          let matIds = matIdsForJob.get(uniq).getOrNull();
          if (!matIds) {
            matIds = HashSet.empty<number>();
          }
          if (!matIds.contains(mat.id)) {
            matIds = matIds.add(mat.id);
            matIdsForJob = matIdsForJob.put(uniq, matIds);
          }
        }
      }
    }
  }

  if (matIdsForJob === st.matIdsForJob) {
    return st;
  } else {
    return {
      jobs: st.jobs,
      matIdsForJob,
      someJobHasCasting: st.someJobHasCasting,
    };
  }
}

export function set_job_comment(st: ScheduledJobsState, uniq: string, comment: string | null): ScheduledJobsState {
  const old = st.jobs.get(uniq);
  if (old.isSome()) {
    return {
      jobs: st.jobs.put(uniq, { ...old.get(), comment: comment ?? undefined }),
      matIdsForJob: st.matIdsForJob,
      someJobHasCasting: st.someJobHasCasting,
    };
  } else {
    return st;
  }
}
