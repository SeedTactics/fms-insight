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

import { addMonths, startOfMonth } from "date-fns";
import { atom, RecoilState, RecoilValueReadOnly, selector, useRecoilCallback, useSetRecoilState } from "recoil";
import { onLoadSpecificMonthJobs, onLoadSpecificMonthLog } from "../cell-status/loading";
import { JobsBackend, LogBackend } from "./backend";
import { RecoilConduit } from "../util/recoil-util";

const selectType = atom<"Last30" | "SpecificMonth">({
  key: "analysisSelectType",
  default: "Last30",
});

const selectMonth = atom<Date>({ key: "analysisSelectMonth", default: startOfMonth(new Date()) });
export const selectedMonth: RecoilValueReadOnly<Date> = selectMonth;

export type SelectedAnalysisPeriod = { type: "Last30" } | { type: "SpecificMonth"; month: Date };
export const selectedAnalysisPeriod = selector<SelectedAnalysisPeriod>({
  key: "selectedAnalysisPeriod",
  get: ({ get }) => {
    const ty = get(selectType);
    if (ty === "Last30") {
      return { type: "Last30" };
    } else {
      return { type: "SpecificMonth", month: get(selectMonth) };
    }
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

const loadedMonth = atom<Date | null>({ key: "specific-month-loaded", default: null });

const loadingSpecificMonthRW = atom<boolean>({ key: "loading-specific-month-data", default: false });
export const loadingSpecificMonthData: RecoilValueReadOnly<boolean> = loadingSpecificMonthRW;

const errorLoadingSpecificMonthRW = atom<string | null>({ key: "error-loading-specific-moneth-data", default: null });
export const errorLoadingSpecificMonthData: RecoilValueReadOnly<string | null> = errorLoadingSpecificMonthRW;

function loadMonth(
  month: Date,
  set: <T>(s: RecoilState<T>, t: T) => void,
  push: <T>(c: RecoilConduit<T>) => (t: T) => void
): void {
  set(loadingSpecificMonthRW, true);
  set(errorLoadingSpecificMonthRW, null);

  const startOfNextMonth = addMonths(month, 1);

  const jobsProm = JobsBackend.history(month, startOfNextMonth).then(push(onLoadSpecificMonthJobs));
  const logProm = LogBackend.get(month, startOfNextMonth).then(push(onLoadSpecificMonthLog));

  Promise.all([jobsProm, logProm])
    .then(() => set(loadedMonth, month))
    .catch((e: Record<string, string | undefined>) => set(errorLoadingSpecificMonthRW, e.message ?? e.toString()))
    .finally(() => set(loadingSpecificMonthRW, false));
}

export function useLoadSpecificMonth(): (m: Date) => void {
  return useRecoilCallback(
    ({ set, snapshot, transact_UNSTABLE }) =>
      (m: Date) => {
        function push<T>(c: RecoilConduit<T>): (t: T) => void {
          return (t) => transact_UNSTABLE((trans) => c.transform(trans, t));
        }

        set(selectType, "SpecificMonth");
        set(selectMonth, m);
        if (
          snapshot.getLoadable(loadedMonth).valueMaybe() !== m &&
          !snapshot.getLoadable(loadingSpecificMonthRW).valueMaybe()
        ) {
          loadMonth(m, set, push);
        }
      },
    []
  );
}

export function useSetLast30(): () => void {
  const setLast30 = useSetRecoilState(selectType);
  return () => setLast30("Last30");
}

export function useSetSpecificMonthWithoutLoading(): (m: Date) => void {
  return useSetRecoilState(selectMonth);
}
