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
import { onLoadSpecificMonthJobs, onLoadSpecificMonthLog } from "../cell-status/loading.js";
import { JobsBackend, LogBackend } from "./backend.js";
import { Atom, Setter, atom, useSetAtom } from "jotai";

const selectType = atom<"Last30" | "SpecificMonth">("Last30");

const selectMonth = atom<Date>(startOfMonth(new Date()));
export const selectedMonth = atom(
  (get) => get(selectMonth),
  (get, set, m: Date) => {
    set(selectType, "SpecificMonth");
    set(selectMonth, m);
    if (get(loadedMonth) !== m && !get(loadingSpecificMonthRW)) {
      loadMonth(m, set);
    }
  }
);

export type SelectedAnalysisPeriod = { type: "Last30" } | { type: "SpecificMonth"; month: Date };
export const selectedAnalysisPeriod = atom<SelectedAnalysisPeriod>((get) => {
  const ty = get(selectType);
  if (ty === "Last30") {
    return { type: "Last30" };
  } else {
    return { type: "SpecificMonth", month: get(selectMonth) };
  }
});

const loadedMonth = atom<Date | null>(null);

const loadingSpecificMonthRW = atom<boolean>(false);
export const loadingSpecificMonthData: Atom<boolean> = loadingSpecificMonthRW;

const errorLoadingSpecificMonthRW = atom<string | null>(null);
export const errorLoadingSpecificMonthData: Atom<string | null> = errorLoadingSpecificMonthRW;

function loadMonth(month: Date, set: Setter): void {
  set(loadingSpecificMonthRW, true);
  set(errorLoadingSpecificMonthRW, null);

  const startOfNextMonth = addMonths(month, 1);

  const jobsProm = JobsBackend.history(month, startOfNextMonth).then((j) => set(onLoadSpecificMonthJobs, j));
  const logProm = LogBackend.get(month, startOfNextMonth).then((log) => set(onLoadSpecificMonthLog, log));

  Promise.all([jobsProm, logProm])
    .then(() => set(loadedMonth, month))
    .catch((e: Record<string, string | undefined>) =>
      set(errorLoadingSpecificMonthRW, e.message ?? e.toString())
    )
    .finally(() => set(loadingSpecificMonthRW, false));
}

export function useSetLast30(): () => void {
  const setLast30 = useSetAtom(selectType);
  return () => setLast30("Last30");
}

export function useSetSpecificMonthWithoutLoading(): (m: Date) => void {
  return useSetAtom(selectMonth);
}
