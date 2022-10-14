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
import {
  atom,
  DefaultValue,
  RecoilValueReadOnly,
  selectorFamily,
  TransactionInterface_UNSTABLE,
} from "recoil";
import { addDays } from "date-fns";
import { conduit } from "../util/recoil-util.js";
import type { ServerEventAndTime } from "./loading.js";
import { IHistoricData, IHistoricJob } from "../network/api.js";
import { HashMap, HashSet, LazySeq } from "@seedtactics/immutable-collections";

const last30JobsRW = atom<HashMap<string, Readonly<IHistoricJob>>>({
  key: "last30Jobs",
  default: HashMap.empty(),
});
export const last30Jobs: RecoilValueReadOnly<HashMap<string, Readonly<IHistoricJob>>> = last30JobsRW;

const last30SchIdsRW = atom<HashSet<string>>({
  key: "last30SchIds",
  default: HashSet.empty(),
});
export const last30SchIds: RecoilValueReadOnly<HashSet<string>> = last30SchIdsRW;

const specificMonthJobsRW = atom<HashMap<string, Readonly<IHistoricJob>>>({
  key: "specificMonthJobs",
  default: HashMap.empty(),
});
export const specificMonthJobs: RecoilValueReadOnly<HashMap<string, Readonly<IHistoricJob>>> =
  specificMonthJobsRW;

export function filterExistingJobs(
  schIds: HashSet<string>,
  history: Readonly<IHistoricData>
): Readonly<IHistoricData> {
  const newHistory = {
    jobs: { ...history.jobs },
    stationUse: history.stationUse.filter((s) => !schIds.has(s.scheduleId)),
  };
  for (const [u, j] of Object.entries(history.jobs)) {
    if (j.scheduleId && schIds.has(j.scheduleId)) {
      delete newHistory.jobs[u];
    }
  }
  return newHistory;
}

export const setLast30Jobs = conduit((t: TransactionInterface_UNSTABLE, history: Readonly<IHistoricData>) => {
  t.set(last30JobsRW, (oldJobs) => oldJobs.union(LazySeq.ofObject(history.jobs).toHashMap((x) => x)));
  t.set(last30SchIdsRW, (oldIds) =>
    oldIds.union(
      LazySeq.ofObject(history.jobs)
        .collect(([, x]) => x.scheduleId)
        .toHashSet((s) => s)
    )
  );
});

export const updateLast30Jobs = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt, now, expire }: ServerEventAndTime) => {
    if (evt.newJobs) {
      const schId = evt.newJobs.scheduleId;
      const newJobs = LazySeq.of(evt.newJobs.jobs);
      t.set(last30JobsRW, (oldJobs) => {
        if (expire) {
          const expire = addDays(now, -30);
          oldJobs = oldJobs.filter((j) => j.routeStartUTC >= expire);
        }

        return oldJobs.union(newJobs.toHashMap((j) => [j.unique, { ...j, copiedToSystem: true }]));
      });
      if (schId) {
        t.set(last30SchIdsRW, (oldIds) => oldIds.add(schId));
      }
    }
  }
);

export const updateSpecificMonthJobs = conduit(
  (t: TransactionInterface_UNSTABLE, history: Readonly<IHistoricData>) => {
    t.set(
      specificMonthJobsRW,
      LazySeq.ofObject(history.jobs).toHashMap((x) => x)
    );
  }
);

export const last30JobComment = selectorFamily<string | null, string>({
  key: "last-30-job-comment",
  get:
    (uniq) =>
    ({ get }) =>
      get(last30Jobs).get(uniq)?.comment ?? null,
  set:
    (uniq) =>
    ({ set }, newVal) => {
      const newComment = newVal instanceof DefaultValue || newVal === null ? "" : newVal;

      set(last30JobsRW, (oldJobs) => {
        const old = oldJobs.get(uniq);
        if (old !== undefined) {
          return oldJobs.set(uniq, { ...old, comment: newComment ?? undefined });
        } else {
          return oldJobs;
        }
      });
    },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});
