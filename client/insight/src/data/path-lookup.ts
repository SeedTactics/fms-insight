/* Copyright (c) 2019, John Lenz

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

import { LogBackend, OtherLogBackends } from "../network/backend";
import { HashMap } from "prelude-ts";
import { addDays } from "date-fns";
import { atom, selector, waitForAny } from "recoil";
import { convertLogToInspections, PartAndInspType, InspectionLogEntry } from "../cell-status/inspections";
import { LazySeq } from "../util/lazyseq";

export type PathLookupLogEntries = HashMap<PartAndInspType, ReadonlyArray<InspectionLogEntry>>;

export interface PathLookupRange {
  readonly part: string;
  readonly curStart: Date;
  readonly curEnd: Date;
}

export const pathLookupRange = atom<PathLookupRange | null>({
  key: "path-lookup-range",
  default: null,
});

const localLogEntries = selector<PathLookupLogEntries>({
  key: "path-lookup-local",
  get: async ({ get }) => {
    const range = get(pathLookupRange);
    if (range == null) return HashMap.empty();

    const events = await LogBackend.get(range.curStart, range.curEnd);
    return HashMap.ofIterable(
      LazySeq.ofIterable(events)
        .flatMap(convertLogToInspections)
        .filter((e) => e.key.part === range.part)
        .groupBy((e) => e.key)
        .map((k, es) => [k, es.map((e) => e.entry).toArray()])
    );
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

const otherLogEntries = selector<PathLookupLogEntries>({
  key: "path-lookup-other",
  get: async ({ get }) => {
    const range = get(pathLookupRange);
    if (range == null) return HashMap.empty();

    let parts: PathLookupLogEntries = HashMap.empty();

    for (const b of OtherLogBackends) {
      const events = await b.get(range.curStart, range.curEnd);

      parts = events
        .flatMap(convertLogToInspections)
        .reduce(
          (m, e) =>
            m.putWithMerge(e.key, [e.entry], (a, b) =>
              a.concat(b).sort((e1, e2) => e1.time.getTime() - e2.time.getTime())
            ),
          parts
        );
    }

    return parts;
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export const inspectionLogEntries = selector<PathLookupLogEntries>({
  key: "path-lookup-logs",
  get: ({ get }) => {
    const entries = get(waitForAny([localLogEntries, otherLogEntries]));

    const vals = entries
      .filter((e) => e.state === "hasValue")
      .map((e) => e.valueOrThrow())
      .filter((e) => !e.isEmpty());
    if (vals.length === 0) {
      return HashMap.empty();
    } else if (vals.length === 1) {
      return vals[0];
    } else {
      let m = vals[0];
      for (let i = 1; i < vals.length; i++) {
        m = m.mergeWith(vals[i], (oldEntries, newEntries) =>
          oldEntries.concat(newEntries).sort((e1, e2) => e1.time.getTime() - e2.time.getTime())
        );
      }
      return m;
    }
  },
  cachePolicy_UNSTABLE: { eviction: "lru", maxSize: 1 },
});

export function extendRange(numDays: number): (range: PathLookupRange | null) => PathLookupRange | null {
  return (range) => {
    if (range === null) return null;
    if (numDays < 0) {
      return {
        curStart: addDays(range.curStart, numDays),
        curEnd: range.curEnd,
        part: range.part,
      };
    } else {
      return {
        curStart: range.curStart,
        curEnd: addDays(range.curEnd, numDays),
        part: range.part,
      };
    }
  };
}
