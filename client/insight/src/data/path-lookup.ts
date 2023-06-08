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

import { LogBackend, OtherLogBackends } from "../network/backend.js";
import { addDays } from "date-fns";
import {
  convertLogToInspections,
  PartAndInspType,
  InspectionLogsByCntr,
} from "../cell-status/inspections.js";
import { ILogEntry } from "../network/api.js";
import { HashMap, LazySeq } from "@seedtactics/immutable-collections";
import { atom } from "jotai";
import { loadable } from "jotai/utils";

export type PathLookupLogEntries = HashMap<PartAndInspType, InspectionLogsByCntr>;

export interface PathLookupRange {
  readonly part: string;
  readonly curStart: Date;
  readonly curEnd: Date;
}

export const pathLookupRange = atom<PathLookupRange | null>(null);

const localLogEntries = atom<Promise<PathLookupLogEntries>>(async (get) => {
  const range = get(pathLookupRange);
  if (range == null) return HashMap.empty();

  const events = await LogBackend.get(range.curStart, range.curEnd);
  return LazySeq.of(events)
    .flatMap(convertLogToInspections)
    .filter((e) => e.key.part === range.part)
    .toLookupMap(
      (e) => e.key,
      (e) => e.entry.cntr,
      (e) => e.entry
    );
});

const localLogLoadable = loadable(localLogEntries);

const otherLogEntries = atom<Promise<PathLookupLogEntries>>(async (get) => {
  const range = get(pathLookupRange);
  if (range == null) return HashMap.empty();

  const allEvts: Array<ReadonlyArray<Readonly<ILogEntry>>> = [];

  for (const b of OtherLogBackends) {
    allEvts.push(await b.get(range.curStart, range.curEnd));
  }

  return LazySeq.of(allEvts)
    .flatMap((es) => es)
    .flatMap(convertLogToInspections)
    .filter((e) => e.key.part === range.part)
    .toLookupMap(
      (e) => e.key,
      (e) => e.entry.cntr,
      (e) => e.entry
    );
});

const otherLogLoadable = loadable(otherLogEntries);

export const inspectionLogEntries = atom<PathLookupLogEntries>((get) => {
  const localEvts = get(localLogLoadable);
  const otherEvts = get(otherLogLoadable);
  const localData = localEvts.state === "hasData" ? localEvts.data : null;
  const otherData = otherEvts.state === "hasData" ? otherEvts.data : null;

  if (localData) {
    if (!otherData) {
      return localData;
    } else {
      return HashMap.union(
        (inspsByCntr1: InspectionLogsByCntr, inspsByCntr2: InspectionLogsByCntr) =>
          inspsByCntr1.union(inspsByCntr2),
        localData,
        otherData
      );
    }
  } else {
    if (otherData) {
      return otherData;
    } else {
      return HashMap.empty();
    }
  }
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
