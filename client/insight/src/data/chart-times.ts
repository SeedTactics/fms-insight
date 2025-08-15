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
import { atom } from "jotai";
import { atomWithStorage } from "jotai/utils";

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

// TODO: deep equality?
export const last30ChartStartTimes = atom<Record<Last30ChartStart, Date | null>>((get) => {
  const weekStart = get(last30WeekdayStartIdx);
  const weekStartMins = get(last30WeekdayStartMinuteOffset);
  const mins = get(minsSinceEpochAtom);
  const now = new Date(mins * 60_000);
  const startOfT = startOfDay(now);
  const startOfW = addMinutes(startOfWeek(now, { weekStartsOn: weekStart }), weekStartMins);

  return {
    StartOfToday: startOfT,
    StartOfYesterday: addDays(startOfT, -1),
    StartOfWeek: startOfW,
    StartOfLastWeek: addDays(startOfW, -7),
    Last30: null,
  };
});

export const last30ChartEndTimes = atom<Record<Last30ChartEnd, Date | null>>((get) => {
  const weekStart = get(last30WeekdayStartIdx);
  const weekStartMins = get(last30WeekdayStartMinuteOffset);
  const mins = get(minsSinceEpochAtom);
  const now = new Date(mins * 60_000);

  return {
    Now: null,
    EndOfYesterday: startOfDay(now),
    EndOfLastWeek: addMinutes(startOfWeek(now, { weekStartsOn: weekStart }), weekStartMins),
  };
});
