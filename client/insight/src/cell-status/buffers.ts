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
import { atom, RecoilValueReadOnly, TransactionInterface_UNSTABLE } from "recoil";
import { addDays } from "date-fns";
import { conduit } from "../util/recoil-util";
import type { ServerEventAndTime } from "./loading";
import { ILogEntry, LogType } from "../network/api";
import { durationToSeconds } from "../util/parseISODuration";
import { LazySeq } from "../util/lazyseq";
import { HashMap, emptyIMap } from "../util/imap";

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

const last30BufferEntriesRW = atom<BufferEntryByCntr>({
  key: "last30Buffers",
  default: emptyIMap(),
});
export const last30BufferEntries: RecoilValueReadOnly<BufferEntryByCntr> = last30BufferEntriesRW;

const specificMonthBufferEntriesRW = atom<BufferEntryByCntr>({
  key: "specificMonthBuffers",
  default: emptyIMap(),
});
export const specificMonthBufferEntries: RecoilValueReadOnly<BufferEntryByCntr> = specificMonthBufferEntriesRW;

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

export const setLast30Buffer = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(last30BufferEntriesRW, (oldEntries) =>
      oldEntries.union(
        LazySeq.ofIterable(log)
          .collect(convertEntry)
          .toHashMap((x) => x)
      )
    );
  }
);

export const updateLast30Buffer = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt, now, expire }: ServerEventAndTime) => {
    if (!evt.logEntry) return;
    const log = convertEntry(evt.logEntry);
    if (!log) return;

    t.set(last30BufferEntriesRW, (entries) => {
      if (expire) {
        const expireD = addDays(now, -30);
        entries = entries.filter((e) => e.endTime >= expireD);
      }

      return entries.set(log[0], log[1]);
    });
  }
);

export const setSpecificMonthBuffer = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(
      specificMonthBufferEntriesRW,
      LazySeq.ofIterable(log)
        .collect(convertEntry)
        .toHashMap((x) => x)
    );
  }
);
