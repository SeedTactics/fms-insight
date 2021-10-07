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
import { addDays } from "date-fns";
import { fieldsHashCode, HashMap } from "prelude-ts";
import { atom, RecoilValueReadOnly, TransactionInterface_UNSTABLE } from "recoil";
import { ILogEntry, IMaterialProcessActualPath, LogType, MaterialProcessActualPath } from "../network/api";
import { LazySeq } from "../util/lazyseq";
import { conduit } from "../util/recoil-util";
import type { ServerEventAndTime } from "./loading";

export enum InspectionLogResultType {
  Triggered,
  Forced,
  Completed,
}

export type InspectionLogResult =
  | {
      readonly type: InspectionLogResultType.Triggered;
      readonly actualPath: ReadonlyArray<Readonly<IMaterialProcessActualPath>>;
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
  readonly cntr: number;
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

const last30InspectionsRW = atom<HashMap<PartAndInspType, ReadonlyArray<InspectionLogEntry>>>({
  key: "last30Inspections",
  default: HashMap.empty(),
});
export const last30Inspections: RecoilValueReadOnly<HashMap<PartAndInspType, ReadonlyArray<InspectionLogEntry>>> =
  last30InspectionsRW;

const specificMonthInspectionsRW = atom<HashMap<PartAndInspType, ReadonlyArray<InspectionLogEntry>>>({
  key: "specificMonthInspections",
  default: HashMap.empty(),
});
export const specificMonthInspections: RecoilValueReadOnly<
  HashMap<PartAndInspType, ReadonlyArray<InspectionLogEntry>>
> = specificMonthInspectionsRW;

export function convertLogToInspections(
  c: Readonly<ILogEntry>
): ReadonlyArray<{ key: PartAndInspType; entry: InspectionLogEntry }> {
  if (c.type !== LogType.Inspection && c.type !== LogType.InspectionForce && c.type !== LogType.InspectionResult) {
    return [];
  }

  return c.material.map((m) => {
    if (c.type === LogType.Inspection) {
      // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
      const pathsJson: ReadonlyArray<unknown> = JSON.parse((c.details || {}).ActualPath || "[]");
      const paths: Array<Readonly<IMaterialProcessActualPath>> = [];
      for (const pathJson of pathsJson) {
        paths.push(MaterialProcessActualPath.fromJS(pathJson));
      }
      const inspType = (c.details || {}).InspectionType || "";

      let toInspect: boolean;
      if (c.result.toLowerCase() === "true" || c.result === "1") {
        toInspect = true;
      } else {
        toInspect = false;
      }
      const r: { key: PartAndInspType; entry: InspectionLogEntry } = {
        key: new PartAndInspType(m.part, inspType),
        entry: {
          cntr: c.counter,
          time: c.endUTC,
          materialID: m.id,
          serial: m.serial,
          workorder: m.workorder,
          result: {
            type: InspectionLogResultType.Triggered,
            actualPath: paths,
            toInspect,
          },
          part: m.part,
          inspType: inspType,
        },
      };
      return r;
    } else if (c.type === LogType.InspectionForce) {
      let forceInspect: boolean;
      if (c.result.toLowerCase() === "true" || c.result === "1") {
        forceInspect = true;
      } else {
        forceInspect = false;
      }
      const r: { key: PartAndInspType; entry: InspectionLogEntry } = {
        key: new PartAndInspType(m.part, c.program),
        entry: {
          cntr: c.counter,
          time: c.endUTC,
          materialID: m.id,
          serial: m.serial,
          workorder: m.workorder,
          result: {
            type: InspectionLogResultType.Forced,
            toInspect: forceInspect,
          },
          part: m.part,
          inspType: c.program,
        },
      };
      return r;
    } else {
      // api.LogType.InspectionResult
      let success: boolean;
      if (c.result.toLowerCase() === "true" || c.result === "1") {
        success = true;
      } else {
        success = false;
      }
      const r: { key: PartAndInspType; entry: InspectionLogEntry } = {
        key: new PartAndInspType(m.part, c.program),
        entry: {
          cntr: c.counter,
          time: c.endUTC,
          materialID: m.id,
          serial: m.serial,
          workorder: m.workorder,
          result: { type: InspectionLogResultType.Completed, success },
          part: m.part,
          inspType: c.program,
        },
      };
      return r;
    }
  });
}

export const setLast30Inspections = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(last30InspectionsRW, (oldEntries) =>
      log
        .flatMap(convertLogToInspections)
        .reduce(
          (m, e) =>
            m.putWithMerge(e.key, [e.entry], (a, b) =>
              a.concat(b).sort((e1, e2) => e1.time.getTime() - e2.time.getTime())
            ),
          oldEntries
        )
    );
  }
);

export const updateLast30Inspections = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt, now, expire }: ServerEventAndTime) => {
    if (evt.logEntry) {
      const log = convertLogToInspections(evt.logEntry);
      if (log.length === 0) return;

      t.set(last30InspectionsRW, (parts) => {
        if (expire) {
          const expireD = addDays(now, -30);
          parts = parts.mapValues((entries) => {
            // check if expire is needed
            if (entries.length === 0 || entries[0].time >= expireD) {
              return entries;
            } else {
              return entries.filter((e) => e.time >= expireD);
            }
          });
        }

        return log.reduce(
          (m, e) =>
            m.putWithMerge(e.key, [e.entry], (a, b) =>
              a.concat(b).sort((e1, e2) => e1.time.getTime() - e2.time.getTime())
            ),
          parts
        );
      });
    } else if (evt.editMaterialInLog) {
      const changedByCntr = LazySeq.ofIterable(evt.editMaterialInLog.editedEvents).toMap(
        (e) => [e.counter, e],
        (e, _) => e
      );

      t.set(last30InspectionsRW, (parts) =>
        parts.mapValues((entries) =>
          entries.map((entry) => {
            const changed = changedByCntr.get(entry.cntr).getOrNull();
            // inspection logs have only a single material
            const mat = changed?.material[0];
            if (changed && mat) {
              return { ...entry, materialID: mat.id, serial: mat.serial, workorder: mat.workorder };
            } else {
              return entry;
            }
          })
        )
      );
    }
  }
);

export const setSpecificMonthInspections = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(
      specificMonthInspectionsRW,
      HashMap.ofIterable(
        LazySeq.ofIterable(log)
          .flatMap(convertLogToInspections)
          .groupBy((e) => e.key)
          .map((k, es) => [k, es.map((e) => e.entry).toArray()])
      )
    );
  }
);
