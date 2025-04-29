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

import { LazySeq, OrderedMap } from "@seedtactics/immutable-collections";
import { MaterialSummaryAndCompletedData } from "../cell-status/material-summary";
import { atom } from "jotai";

export type PartSummary = {
  readonly part: string;
  readonly plannedQty: number;
  readonly completedQty: number;
  readonly abnormalQty: number;
  readonly stationMins: OrderedMap<string, { readonly active: number; readonly elapsed: number }>;
  readonly mats: ReadonlyArray<MaterialSummaryAndCompletedData>;
  readonly workorders: OrderedMap<string, number>; // key is workorder, value is count of parts
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

export const specificMonthPartSummary = atom<ReadonlyArray<PartSummary>>((get) => {
  const allParts = get(last30MaterialSummary);
  return [] as ReadonlyArray<PartSummary>;
});
