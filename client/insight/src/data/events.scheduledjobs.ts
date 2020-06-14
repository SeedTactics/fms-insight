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
import { HashMap, Option } from "prelude-ts";
import { LazySeq } from "./lazyseq";
import { ExpireOldData, ExpireOldDataType } from "./events.cycles";

export interface ScheduledJob {
  readonly unique: string;
  readonly scheduleId: string | null;
  readonly partName: string;
  readonly startingTime: Date;
  readonly casting: string | null;
  readonly comment: string | null;
  readonly scheduledQty: number;
  readonly decrementedQty: number;
}

export interface ScheduledJobsState {
  readonly jobs: HashMap<string, ScheduledJob>;
  readonly someJobHasCasting: boolean;
}

export const initial: ScheduledJobsState = {
  jobs: HashMap.empty(),
  someJobHasCasting: false,
};

export function process_scheduled_jobs(
  expire: ExpireOldData,
  newHistory: Readonly<api.IHistoricData>,
  st: ScheduledJobsState
): ScheduledJobsState {
  let jobs = st.jobs;
  let someJobHasCasting = st.someJobHasCasting;

  switch (expire.type) {
    case ExpireOldDataType.ExpireEarlierThan: {
      // check if nothing to expire and no new data
      const minStat = LazySeq.ofIterable(jobs).minOn(([, e]) => e.startingTime.getTime());

      if (
        (minStat.isNone() || minStat.get()[1].startingTime >= expire.d) &&
        Object.keys(newHistory.jobs).length === 0
      ) {
        return st;
      }

      // filter old events
      jobs = jobs.filter((_, j) => j.startingTime >= expire.d);

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
      const casting = LazySeq.ofIterable(newJob.procsAndPaths[0]?.paths ?? [])
        .mapOption((p) =>
          p.casting !== null && p.casting !== undefined && p.casting !== ""
            ? Option.some<string>(p.casting)
            : Option.none<string>()
        )
        .head()
        .getOrNull();
      if (casting !== null) {
        someJobHasCasting = true;
      }
      jobs = jobs.put(newUniq, {
        unique: newJob.unique,
        partName: newJob.partName,
        scheduleId: newJob.scheduleId ?? null,
        startingTime: newJob.routeStartUTC,
        casting: casting,
        comment: newJob.comment || null,
        scheduledQty: LazySeq.ofIterable(newJob.cyclesOnFirstProcess).sumOn((c) => c),
        decrementedQty: LazySeq.ofIterable(newJob.decrements || []).sumOn((d) => d.quantity),
      });
    }
  }

  return { jobs, someJobHasCasting };
}

export function set_job_comment(st: ScheduledJobsState, uniq: string, comment: string | null): ScheduledJobsState {
  const old = st.jobs.get(uniq);
  if (old.isSome()) {
    return { jobs: st.jobs.put(uniq, { ...old.get(), comment: comment }), someJobHasCasting: st.someJobHasCasting };
  } else {
    return st;
  }
}
