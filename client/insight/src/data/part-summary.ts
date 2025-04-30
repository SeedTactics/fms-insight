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

import { LazySeq, OrderedMap, OrderedSet } from "@seedtactics/immutable-collections";
import { last30MaterialSummary, MaterialSummaryAndCompletedData } from "../cell-status/material-summary";
import { atom } from "jotai";
import { addDays, addMinutes, startOfToday, startOfWeek } from "date-fns";
import { last30StationCycles } from "../cell-status/station-cycles";
import { splitTimeToDays } from "./results.oee";

export type PartSummary = {
  readonly part: string;
  readonly completedQty: number;
  readonly abnormalQty: number;
  readonly stationMins: OrderedMap<string, { readonly active: number; readonly elapsed: number }>;
  readonly mats: ReadonlyArray<MaterialSummaryAndCompletedData>;
  readonly workorders: OrderedSet<string>;
};

function isAbnormal(m: MaterialSummaryAndCompletedData): boolean {
  if (m.closeout_completed === undefined) {
    // no closeout has been done, so fall back to checking inspections and quarantined
    if (LazySeq.ofObject(m.completedInspections ?? {}).some(([, insp]) => insp.success === false)) {
      return true;
    }

    // TODO: quarantined;
    return false;
  } else {
    return m.closeout_failed !== false;
  }
}

export const last30PartSummaryRange = atom<"Today" | "PastTwoDays" | "ThisWeek" | "LastTwoWeeks">("ThisWeek");

export function last30PartSummaryRangeStart(
  range: "Today" | "PastTwoDays" | "ThisWeek" | "LastTwoWeeks",
): Date {
  return range === "Today"
    ? startOfToday()
    : range === "PastTwoDays"
      ? addDays(startOfToday(), -1)
      : range === "ThisWeek"
        ? startOfWeek(new Date())
        : addDays(startOfWeek(new Date()), -7);
}

export const last30PartSummary = atom<ReadonlyArray<PartSummary>>((get) => {
  const mats = get(last30MaterialSummary);
  const cycles = get(last30StationCycles);

  const range = get(last30PartSummaryRange);
  const start = last30PartSummaryRangeStart(range);

  const stationTimes = cycles
    .valuesToLazySeq()
    .filter((c) => c.endTime >= start)
    .toLookupOrderedMap(
      (c) => c.part,
      (c) => c.stationGroup,
      (c) => {
        const elapTime = c.elapsedMinsPerMaterial * c.material.length;
        const startTime = addMinutes(c.endTime, -elapTime);
        const elapDays = splitTimeToDays(startTime, c.endTime, elapTime);
        const activeDays = splitTimeToDays(startTime, c.endTime, c.activeMinutes);
        // only the final day, since the range is >= start
        return {
          elapsed: elapDays[elapDays.length - 1].value,
          active: activeDays[activeDays.length - 1].value,
        };
      },
      (a, b) => ({ elapsed: a.elapsed + b.elapsed, active: a.active + b.active }),
    );

  return mats.matsById
    .valuesToLazySeq()
    .filter((m) =>
      Boolean(
        m.numProcesses &&
          m.unloaded_processes?.[m.numProcesses] &&
          m.unloaded_processes[m.numProcesses] >= start,
      ),
    )
    .toOrderedLookup((m) => m.partName)
    .mapValues(
      (mats, partName) =>
        ({
          part: partName,
          completedQty: mats.length,
          abnormalQty: LazySeq.of(mats).sumBy((m) => (isAbnormal(m) ? 1 : 0)),
          mats: mats,
          stationMins: OrderedMap.empty(),
          workorders: LazySeq.of(mats)
            .toOrderedSet((m) => m.workorderId ?? "")
            .delete(""),
        }) satisfies PartSummary,
    )
    .adjust(stationTimes, (summary, stationTimes, partName) => {
      if (summary) {
        return { ...summary, stationMins: stationTimes };
      } else {
        return {
          part: partName,
          completedQty: 0,
          abnormalQty: 0,
          mats: [],
          stationMins: stationTimes,
          workorders: OrderedSet.empty(),
        };
      }
    })
    .valuesToAscLazySeq()
    .toRArray();
});
