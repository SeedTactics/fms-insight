/* Copyright (c) 2025, John Lenz

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

import { addDays, addMinutes, startOfDay, startOfWeek } from "date-fns";
import { Atom, atom, WritableAtom } from "jotai";
import { atomWithStorage } from "jotai/utils";
import { atomFamily } from "jotai-family";

export type Last30ChartStart =
  | "StartOfToday"
  | "StartOfYesterday"
  | "StartOfWeek"
  | "StartOfLastWeek"
  | "Last30";

export type Last30ChartEnd = "Now" | "EndOfYesterday" | "EndOfLastWeek";

const minsSinceEpochAtom = atom(Math.floor(Date.now() / 60_000));
minsSinceEpochAtom.onMount = (setSelf) => {
  const interval = setInterval(() => {
    setSelf(Math.floor(Date.now() / 60_000));
  }, 60_000);
  return () => clearInterval(interval);
};

export const last30WeekdayStartIdx = atomWithStorage<0 | 1 | 2 | 3 | 4 | 5 | 6>(
  "charts_last30WeekdayStartIdx",
  0,
);

export const last30WeekdayStartMinuteOffset = atomWithStorage<number>(
  "charts_last30WeekdayStartMinuteOffset",
  6 * 60,
);

export const last30ChartStartTimes = atomFamily<Last30ChartStart, Atom<Date>>((start) =>
  atom((get) => {
    const weekStart = get(last30WeekdayStartIdx);
    const weekStartMins = get(last30WeekdayStartMinuteOffset);
    const now = new Date(get(minsSinceEpochAtom) * 60_000);
    const startOfT = addMinutes(startOfDay(now), weekStartMins);
    const startOfW = addMinutes(startOfWeek(now, { weekStartsOn: weekStart }), weekStartMins);

    switch (start) {
      case "StartOfToday":
        return startOfT;
      case "StartOfYesterday":
        return addDays(startOfT, -1);
      case "StartOfWeek":
        return startOfW;
      case "StartOfLastWeek":
        return addDays(startOfW, -7);
      case "Last30":
        return addDays(now, -30);
    }
  }),
);

export const last30ChartEndTimes = atomFamily<Last30ChartEnd, Atom<Date | null>>((cEnd) =>
  atom((get) => {
    if (cEnd === "Now") {
      return null;
    }

    const weekStart = get(last30WeekdayStartIdx);
    const weekStartMins = get(last30WeekdayStartMinuteOffset);
    const now = new Date(get(minsSinceEpochAtom) * 60_000);

    switch (cEnd) {
      case "EndOfYesterday":
        return addMinutes(startOfDay(now), weekStartMins);
      case "EndOfLastWeek":
        return addMinutes(startOfWeek(now, { weekStartsOn: weekStart }), weekStartMins);
    }
  }),
);

export type Last30ChartRangeAtom = WritableAtom<
  {
    readonly startType: Last30ChartStart | Date;
    readonly endType: Last30ChartEnd | Date;
    readonly startDate: Date | null;
    readonly endDate: Date | null;
  },
  [{ start: Last30ChartStart | Date } | { end: Last30ChartEnd | Date }],
  void
>;

export function chartRangeAtom(chartName: string): Last30ChartRangeAtom {
  const startA = atomWithStorage<Last30ChartStart | Date>(`charts_${chartName}_start`, "StartOfWeek");

  const endA = atomWithStorage<Last30ChartEnd | Date>(`charts_${chartName}_end`, "Now");

  return atom(
    (get) => {
      const start = get(startA);
      const end = get(endA);
      return {
        startType: start,
        endType: end,
        startDate: start instanceof Date ? start : get(last30ChartStartTimes(start)),
        endDate: end instanceof Date ? end : get(last30ChartEndTimes(end)),
      };
    },
    (_, set, update) => {
      if ("start" in update) {
        set(startA, update.start);
      }
      if ("end" in update) {
        set(endA, update.end);
      }
    },
  );
}
