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
import { LazySeq } from "../data/lazyseq";
import { atom, DefaultValue, RecoilValueReadOnly, selectorFamily, TransactionInterface_UNSTABLE } from "recoil";
import { addDays } from "date-fns";
import { HashMap } from "prelude-ts";
import { conduit } from "../store/recoil-util";
import { ServerEventAndTime } from "../store/websocket";
import { IHistoricData, IHistoricJob } from "../data/api";

const last30JobsRW = atom<HashMap<string, Readonly<IHistoricJob>>>({
  key: "last30Jobs",
  default: HashMap.empty(),
});
export const last30Jobs: RecoilValueReadOnly<HashMap<string, Readonly<IHistoricJob>>> = last30JobsRW;

const specificMonthJobsRW = atom<HashMap<string, Readonly<IHistoricJob>>>({
  key: "specificMonthJobs",
  default: HashMap.empty(),
});
export const specificMonthJobs: RecoilValueReadOnly<HashMap<string, Readonly<IHistoricJob>>> = specificMonthJobsRW;

export const setLast30Jobs = conduit((t: TransactionInterface_UNSTABLE, history: Readonly<IHistoricData>) => {
  t.set(last30JobsRW, (oldJobs) => LazySeq.ofObject(history.jobs).foldLeft(oldJobs, (m, [_, j]) => m.put(j.unique, j)));
});

export const updateLast30Jobs = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt, now, expire }: ServerEventAndTime) => {
    if (evt.newJobs) {
      const newJobs = LazySeq.ofIterable(evt.newJobs.jobs);
      t.set(last30JobsRW, (oldJobs) => {
        if (expire) {
          const expire = addDays(now, -30);

          const minStat = LazySeq.ofIterable(oldJobs).minOn(([, e]) => e.routeStartUTC.getTime());

          if ((minStat.isNone() || minStat.get()[1].routeStartUTC >= expire) && newJobs.isEmpty()) {
            return oldJobs;
          }

          oldJobs = oldJobs.filter((_, j) => j.routeStartUTC >= expire);
        }

        return newJobs.foldLeft(oldJobs, (m, j) => m.put(j.unique, { ...j, copiedToSystem: true }));
      });
    }
  }
);

export const updateSpecificMonthJobs = conduit((t: TransactionInterface_UNSTABLE, history: Readonly<IHistoricData>) => {
  t.set(specificMonthJobsRW, HashMap.ofObjectDictionary(history.jobs));
});

export const last30JobComment = selectorFamily<string | null, string>({
  key: "last-30-job-comment",
  get:
    (uniq) =>
    ({ get }) =>
      get(last30Jobs).get(uniq).getOrNull()?.comment ?? null,
  set:
    (uniq) =>
    ({ set }, newVal) => {
      const newComment = newVal instanceof DefaultValue || newVal === null ? "" : newVal;

      set(last30JobsRW, (oldJobs) => {
        const old = oldJobs.get(uniq);
        if (old.isSome()) {
          return oldJobs.put(uniq, { ...old.get(), comment: newComment ?? undefined });
        } else {
          return oldJobs;
        }
      });
    },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});
