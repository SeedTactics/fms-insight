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
import { hashValues } from "@seedtactics/immutable-collections";
import { addDays } from "date-fns";
import { ILogEntry, IMaterialProcessActualPath, LogType, MaterialProcessActualPath } from "../network/api.js";
import { LazySeq, HashMap } from "@seedtactics/immutable-collections";
import type { ServerEventAndTime } from "./loading.js";
import { Atom, atom } from "jotai";

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

export type InspectionLogsByCntr = HashMap<number, InspectionLogEntry>;

export class PartAndInspType {
  public constructor(
    public readonly part: string,
    public readonly inspType: string,
  ) {}
  compare(other: PartAndInspType): number {
    const cmp = this.part.localeCompare(other.part);
    if (cmp !== 0) {
      return cmp;
    } else {
      return this.inspType.localeCompare(other.inspType);
    }
  }
  hash(): number {
    return hashValues(this.part, this.inspType);
  }
  toString(): string {
    return `{part: ${this.part}}, inspType: ${this.inspType}}`;
  }
}

export type InspectionsByPartAndType = HashMap<PartAndInspType, InspectionLogsByCntr>;

const last30InspectionsRW = atom<InspectionsByPartAndType>(
  HashMap.empty<PartAndInspType, InspectionLogsByCntr>(),
);
export const last30Inspections: Atom<InspectionsByPartAndType> = last30InspectionsRW;

const specificMonthInspectionsRW = atom<InspectionsByPartAndType>(
  HashMap.empty<PartAndInspType, InspectionLogsByCntr>(),
);
export const specificMonthInspections: Atom<InspectionsByPartAndType> = specificMonthInspectionsRW;

export function convertLogToInspections(
  c: Readonly<ILogEntry>,
): ReadonlyArray<{ key: PartAndInspType; entry: InspectionLogEntry }> {
  if (
    c.type !== LogType.Inspection &&
    c.type !== LogType.InspectionForce &&
    c.type !== LogType.InspectionResult
  ) {
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

export const setLast30Inspections = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(last30InspectionsRW, (oldEntries) =>
    oldEntries.union(
      LazySeq.of(log)
        .flatMap(convertLogToInspections)
        .toLookupMap(
          (e) => e.key,
          (e) => e.entry.cntr,
          (e) => e.entry,
        ),
      (e1, e2) => e1.union(e2),
    ),
  );
});

export const updateLast30Inspections = atom(null, (_, set, { evt, now, expire }: ServerEventAndTime) => {
  if (evt.logEntry) {
    const log = convertLogToInspections(evt.logEntry);
    if (log.length === 0) return;

    set(last30InspectionsRW, (parts) => {
      if (expire) {
        const expireD = addDays(now, -30);
        parts = parts.collectValues((entries) => {
          const newEntries = entries.filter((e) => e.time >= expireD);
          if (newEntries.size === 0) {
            return null;
          } else {
            return newEntries;
          }
        });
      }

      return parts.union(
        LazySeq.of(log).toLookupMap(
          (e) => e.key,
          (e) => e.entry.cntr,
          (e) => e.entry,
        ),
        (e1, e2) => e1.union(e2),
      );
    });
  } else if (evt.editMaterialInLog) {
    const changedByCntr = evt.editMaterialInLog.editedEvents;

    set(last30InspectionsRW, (parts) =>
      parts.collectValues((entries) => {
        for (const changed of changedByCntr) {
          // inspection logs have only a single material
          const mat = changed?.material[0];
          const old = entries.get(changed.counter);
          if (old !== undefined && mat) {
            const newEntry = {
              ...old,
              materialID: mat.id,
              serial: mat.serial,
              workorder: mat.workorder,
            };
            entries = entries.set(changed.counter, newEntry);
          }
        }
        return entries;
      }),
    );
  }
});

export const setSpecificMonthInspections = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(
    specificMonthInspectionsRW,
    LazySeq.of(log)
      .flatMap(convertLogToInspections)
      .toLookupMap(
        (e) => e.key,
        (e) => e.entry.cntr,
        (e) => e.entry,
      ),
  );
});
