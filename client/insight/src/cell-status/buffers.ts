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
import type { ServerEventAndTime } from "./loading.js";
import { ILogEntry, LogType } from "../network/api.js";
import { durationToSeconds } from "../util/parseISODuration.js";
import { LazySeq, HashMap } from "@seedtactics/immutable-collections";
import { Atom, atom } from "jotai";

export type BufferType =
  | { readonly type: "Rotary"; readonly machineGroup: string; readonly machineNum: number }
  | { readonly type: "StockerWaitForMC"; readonly stockerNum: number }
  | { readonly type: "StockerWaitForUnload"; readonly stockerNum: number }
  | { readonly type: "Queue"; readonly queue: string };

export interface BufferEntry {
  readonly buffer: BufferType;
  readonly endTime: Date;
  readonly elapsedSeconds: number;
  readonly numMaterial: number;
}

export type BufferEntryByCntr = HashMap<number, BufferEntry>;

const last30BufferEntriesRW = atom<BufferEntryByCntr>(HashMap.empty<number, BufferEntry>());
export const last30BufferEntries: Atom<BufferEntryByCntr> = last30BufferEntriesRW;

const specificMonthBufferEntriesRW = atom<BufferEntryByCntr>(HashMap.empty<number, BufferEntry>());
export const specificMonthBufferEntries: Atom<BufferEntryByCntr> = specificMonthBufferEntriesRW;

function convertEntry(e: Readonly<ILogEntry>): [number, BufferEntry] | null {
  if (e.elapsed === "") return null;
  const elapsedSeconds = durationToSeconds(e.elapsed);
  if (elapsedSeconds <= 0) return null;

  switch (e.type) {
    case LogType.RemoveFromQueue:
      return [
        e.counter,
        {
          buffer: { type: "Queue", queue: e.loc },
          endTime: e.endUTC,
          elapsedSeconds,
          numMaterial: e.material.length,
        },
      ];
    case LogType.PalletInStocker:
      if (!e.startofcycle && e.result === "WaitForMachine") {
        return [
          e.counter,
          {
            buffer: { type: "StockerWaitForMC", stockerNum: e.locnum },
            endTime: e.endUTC,
            elapsedSeconds,
            numMaterial: e.material.length,
          },
        ];
      } else if (!e.startofcycle) {
        return [
          e.counter,
          {
            buffer: { type: "StockerWaitForUnload", stockerNum: e.locnum },
            endTime: e.endUTC,
            elapsedSeconds,
            numMaterial: e.material.length,
          },
        ];
      } else {
        return null;
      }
    case LogType.PalletOnRotaryInbound:
      if (!e.startofcycle) {
        return [
          e.counter,
          {
            buffer: { type: "Rotary", machineGroup: e.loc, machineNum: e.locnum },
            endTime: e.endUTC,
            elapsedSeconds,
            numMaterial: e.material.length,
          },
        ];
      } else {
        return null;
      }

    default:
      return null;
  }
}

export const setLast30Buffer = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(last30BufferEntriesRW, (oldEntries) =>
    oldEntries.union(
      LazySeq.of(log)
        .collect(convertEntry)
        .toHashMap((x) => x)
    )
  );
});

export const updateLast30Buffer = atom(null, (_, set, { evt, now, expire }: ServerEventAndTime) => {
  if (!evt.logEntry) return;
  const log = convertEntry(evt.logEntry);
  if (!log) return;

  set(last30BufferEntriesRW, (entries) => {
    if (expire) {
      const expireD = addDays(now, -30);
      entries = entries.filter((e) => e.endTime >= expireD);
    }

    return entries.set(log[0], log[1]);
  });
});

export const setSpecificMonthBuffer = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(
    specificMonthBufferEntriesRW,
    LazySeq.of(log)
      .collect(convertEntry)
      .toHashMap((x) => x)
  );
});
