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
import * as api from "./api";
import { fieldsHashCode, HashMap } from "prelude-ts";
import { LazySeq } from "./lazyseq";

export enum InspectionLogResultType {
  Triggered,
  Forced,
  Completed
}

export type InspectionLogResult =
  | {
      readonly type: InspectionLogResultType.Triggered;
      readonly actualPath: ReadonlyArray<Readonly<api.IMaterialProcessActualPath>>;
      readonly toInspect: boolean;
    }
  | {
      readonly type: InspectionLogResultType.Forced;
      readonly toInspect: boolean;
    }
  | {
      readonly type: InspectionLogResultType.Completed;
      readonly success: boolean;
    };

export interface InspectionLogEntry {
  readonly time: Date;
  readonly materialID: number;
  readonly serial?: string;
  readonly workorder?: string;
  readonly result: InspectionLogResult;
  readonly part: string;
  readonly inspType: string;
}

export class PartAndInspType {
  public constructor(public readonly part: string, public readonly inspType: string) {}
  equals(other: PartAndInspType): boolean {
    return this.part === other.part && this.inspType === other.inspType;
  }
  hashCode(): number {
    return fieldsHashCode(this.part, this.inspType);
  }
  toString(): string {
    return `{part: ${this.part}}, inspType: ${this.inspType}}`;
  }
}

export interface InspectionState {
  readonly by_part: HashMap<PartAndInspType, ReadonlyArray<InspectionLogEntry>>;
}

export const initial: InspectionState = {
  by_part: HashMap.empty()
};

export enum ExpireOldDataType {
  ExpireEarlierThan,
  NoExpire
}

export type ExpireOldData =
  | { type: ExpireOldDataType.ExpireEarlierThan; d: Date }
  | { type: ExpireOldDataType.NoExpire };

export function process_events(
  expire: ExpireOldData,
  newEvts: ReadonlyArray<Readonly<api.ILogEntry>>,
  filterPart: string | undefined,
  st: InspectionState
): InspectionState {
  let parts = st.by_part;

  switch (expire.type) {
    case ExpireOldDataType.ExpireEarlierThan: {
      let eventsFiltered = false;
      parts = parts.mapValues(entries => {
        // check if expire is needed
        if (entries.length === 0 || entries[0].time >= expire.d) {
          return entries;
        } else {
          eventsFiltered = true;
          return entries.filter(e => e.time >= expire.d);
        }
      });
      if (!eventsFiltered && newEvts.length === 0) {
        return st;
      }

      break;
    }

    case ExpireOldDataType.NoExpire:
      if (newEvts.length === 0) {
        return st;
      }
      break;
  }

  const newPartCycles = LazySeq.ofIterable(newEvts)
    .filter(
      c =>
        c.type === api.LogType.Inspection ||
        c.type === api.LogType.InspectionForce ||
        c.type === api.LogType.InspectionResult
    )
    .flatMap(c =>
      c.material.map(m => {
        if (c.type === api.LogType.Inspection) {
          const pathsJson: ReadonlyArray<object> = JSON.parse((c.details || {}).ActualPath || "[]");
          const paths: Array<Readonly<api.IMaterialProcessActualPath>> = [];
          for (const pathJson of pathsJson) {
            paths.push(api.MaterialProcessActualPath.fromJS(pathJson));
          }
          const inspType = (c.details || {}).InspectionType || "";

          let toInspect: boolean;
          if (c.result.toLowerCase() === "true" || c.result === "1") {
            toInspect = true;
          } else {
            toInspect = false;
          }
          return {
            key: new PartAndInspType(m.part, inspType),
            entry: {
              time: c.endUTC,
              materialID: m.id,
              serial: m.serial,
              workorder: m.workorder,
              result: {
                type: InspectionLogResultType.Triggered,
                actualPath: paths,
                toInspect
              },
              part: m.part,
              inspType: inspType
            } as InspectionLogEntry
          };
        } else if (c.type === api.LogType.InspectionForce) {
          let forceInspect: boolean;
          if (c.result.toLowerCase() === "true" || c.result === "1") {
            forceInspect = true;
          } else {
            forceInspect = false;
          }
          return {
            key: new PartAndInspType(m.part, c.program),
            entry: {
              time: c.endUTC,
              materialID: m.id,
              serial: m.serial,
              workorder: m.workorder,
              result: {
                type: InspectionLogResultType.Forced,
                toInspect: forceInspect
              },
              part: m.part,
              inspType: c.program
            } as InspectionLogEntry
          };
        } else {
          // api.LogType.InspectionResult
          let success: boolean;
          if (c.result.toLowerCase() === "true" || c.result === "1") {
            success = true;
          } else {
            success = false;
          }
          return {
            key: new PartAndInspType(m.part, c.program),
            entry: {
              time: c.endUTC,
              materialID: m.id,
              serial: m.serial,
              workorder: m.workorder,
              result: { type: InspectionLogResultType.Completed, success },
              part: m.part,
              inspType: c.program
            } as InspectionLogEntry
          };
        }
      })
    )
    .filter(e => filterPart === undefined || e.entry.part === filterPart)
    .groupBy(e => e.key)
    .mapValues(es => es.map(e => e.entry).toArray());

  parts = parts.mergeWith(newPartCycles, (oldEntries, newEntries) =>
    oldEntries.concat(newEntries).sort((e1, e2) => e1.time.getTime() - e2.time.getTime())
  );

  return {
    ...st,
    by_part: parts
  };
}
