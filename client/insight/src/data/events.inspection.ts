/* Copyright (c) 2018, John Lenz

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
import * as api from './api';
import * as im from 'immutable'; // consider collectable.js at some point?

export enum InspectionLogResultType {
  Triggered,
  Succeeded,
  Failed,
}

export interface InspectionCounter {
  readonly part: string;
  readonly inspType: string;
  readonly pallets: ReadonlyArray<string>;
  readonly stations: ReadonlyArray<string>;
}

export type InspectionLogResult =
  | { readonly type: InspectionLogResultType.Triggered, readonly counter: InspectionCounter }
  | { readonly type: InspectionLogResultType.Succeeded }
  | { readonly type: InspectionLogResultType.Failed }
  ;

export interface InspectionLogEntry {
  readonly time: Date;
  readonly materialID: number;
  readonly result: InspectionLogResult;
}

export type PartAndInspType = im.Record<{part: string, inspType: string}>;
export const mkPartAndInspType = im.Record({part: "", inspType: ""});

export interface InspectionState {
  readonly by_part: im.Map<PartAndInspType, ReadonlyArray<InspectionLogEntry>>;
}

export const initial: InspectionState = {
  by_part: im.Map(),
};

export enum ExpireOldDataType {
  ExpireEarlierThan,
  NoExpire
}

export type ExpireOldData =
  | {type: ExpireOldDataType.ExpireEarlierThan, d: Date }
  | {type: ExpireOldDataType.NoExpire }
  ;

export function parseInspectionCounter(counter: string): InspectionCounter {
  const s = counter.split(",");
  const pals: string[] = [];
  const stats: string[] = [];
  for (var i = 2; i < s.length; i++) {
    if (s[i].startsWith("P")) {
      pals.push(s[i].substr(1));
    } else {
      stats.push(s[i].substr(1));
    }
  }
  return {
    part: s.length >= 1 ? s[0] : "",
    inspType: s.length >= 2 ? s[1] : "",
    pallets: pals,
    stations: stats,
  };
}

export function process_events(
  expire: ExpireOldData,
  newEvts: Iterable<api.ILogEntry>,
  st: InspectionState): InspectionState {

    let evtsSeq = im.Seq(newEvts);
    let parts = st.by_part;

    switch (expire.type) {
      case ExpireOldDataType.ExpireEarlierThan:

        let eventsFiltered = false;
        parts = parts
          .map(entries => {
            // check if expire is needed
            if (entries.length === 0 || entries[0].time >= expire.d) {
              return entries;
            } else {
              eventsFiltered = true;
              return im.Seq(entries).skipWhile(e => e.time < expire.d).toArray();
            }
          });
        if (!eventsFiltered && evtsSeq.isEmpty()) { return st; }

        break;

      case ExpireOldDataType.NoExpire:
        if (evtsSeq.isEmpty()) { return st; }
        break;
    }

    var newPartCycles = evtsSeq
      .filter(c =>
             c.type === api.LogType.Inspection
          || c.type === api.LogType.InspectionResult
      )
      .flatMap(c => c.material.map(m => {
        if (c.type === api.LogType.Inspection) {
          const counter = parseInspectionCounter(c.program);
          return {
            key: mkPartAndInspType({part: m.part, inspType: counter.inspType}),
            entry: {
              time: c.endUTC,
              materialID: m.id,
              result: {type: InspectionLogResultType.Triggered, counter }
            } as InspectionLogEntry
          };
        } else { // api.LogType.InspectionResult
          let ty: InspectionLogResultType;
          if (c.result.toLowerCase() === "true" || c.result === "1") {
            ty = InspectionLogResultType.Succeeded;
          } else {
            ty = InspectionLogResultType.Failed;
          }
          return {
            key: mkPartAndInspType({part: m.part, inspType: c.program}),
            entry: {
              time: c.endUTC,
              materialID: m.id,
              result: {type: ty, counter: c.program}
            } as InspectionLogEntry
          };
        }
      }))
      .groupBy(e => e.key)
      .map(es => es.map(e => e.entry).valueSeq().toArray())
      ;

    parts = parts
      .mergeWith(
        (oldEntries, newEntries) =>
          im.Seq(oldEntries)
          .concat(newEntries)
          .sortBy(e => e.time)
          .toArray(),
        newPartCycles
      );

    return {...st,
      by_part: parts,
    };
}