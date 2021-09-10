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
import * as api from "../data/api";
import { LazySeq } from "../data/lazyseq";
import { atom, RecoilValueReadOnly, SetRecoilState, TransactionInterface_UNSTABLE } from "recoil";
import { addDays } from "date-fns";
import { HashMap } from "prelude-ts";

const last30JobsRW = atom<HashMap<string, Readonly<api.IHistoricJob>>>({
  key: "last30Jobs",
  default: HashMap.empty(),
});
export const last30Jobs: RecoilValueReadOnly<HashMap<string, Readonly<api.IHistoricJob>>> = last30JobsRW;

const specificMonthJobsRW = atom<HashMap<string, Readonly<api.IHistoricJob>>>({
  key: "specificMonthJobs",
  default: HashMap.empty(),
});
export const specificMonthJobs: RecoilValueReadOnly<HashMap<string, Readonly<api.IHistoricJob>>> = specificMonthJobsRW;

export function onNewJobs(t: TransactionInterface_UNSTABLE, apiNewJobs: Iterable<api.IHistoricJob>, now?: Date): void {
  const newJobs = LazySeq.ofIterable(apiNewJobs);
  t.set(last30JobsRW, (oldJobs) => {
    if (now) {
      const expire = addDays(now, -30);

      const minStat = LazySeq.ofIterable(oldJobs).minOn(([, e]) => e.routeStartUTC.getTime());

      if ((minStat.isNone() || minStat.get()[1].routeStartUTC >= expire) && newJobs.isEmpty()) {
        return oldJobs;
      }

      oldJobs = oldJobs.filter((_, j) => j.routeStartUTC >= expire);
    }

    return newJobs.foldLeft(oldJobs, (m, j) => m.put(j.unique, j));
  });
}

export function onSpecificMonthJobs(
  t: TransactionInterface_UNSTABLE,
  apiNewJobs: { readonly [key: string]: api.IHistoricJob }
): void {
  t.set(specificMonthJobsRW, HashMap.ofObjectDictionary(apiNewJobs));
}

export function updateJobComment(set: SetRecoilState, uniq: string, comment: string | null): void {
  set(last30JobsRW, (oldJobs) => {
    const old = oldJobs.get(uniq);
    if (old.isSome()) {
      return oldJobs.put(uniq, { ...old.get(), comment: comment ?? undefined });
    } else {
      return oldJobs;
    }
  });
}
