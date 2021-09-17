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
import { conduit } from "../store/recoil-util";
import { ServerEventAndTime } from "../store/websocket";
import { ILogEntry, LogType } from "../data/api";
import * as L from "list/methods";
import { durationToSeconds } from "../data/parseISODuration";
import { LazySeq } from "../data/lazyseq";

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

const last30BufferEntriesRW = atom<L.List<BufferEntry>>({
  key: "last30Buffers",
  default: L.empty(),
});
export const last30BufferEntries: RecoilValueReadOnly<L.List<BufferEntry>> = last30BufferEntriesRW;

const specificMonthBufferEntriesRW = atom<L.List<BufferEntry>>({
  key: "specificMonthBuffers",
  default: L.empty(),
});
export const specificMonthBufferEntries: RecoilValueReadOnly<L.List<BufferEntry>> = specificMonthBufferEntriesRW;

function convertEntry(e: Readonly<ILogEntry>): BufferEntry | null {
  if (e.elapsed === "") return null;
  const elapsedSeconds = durationToSeconds(e.elapsed);
  if (elapsedSeconds <= 0) return null;

  switch (e.type) {
    case LogType.RemoveFromQueue:
      return {
        buffer: { type: "Queue", queue: e.loc },
        endTime: e.endUTC,
        elapsedSeconds,
        numMaterial: e.material.length,
      };
    case LogType.PalletInStocker:
      if (!e.startofcycle && e.result === "WaitForMachine") {
        return {
          buffer: { type: "StockerWaitForMC", stockerNum: e.locnum },
          endTime: e.endUTC,
          elapsedSeconds,
          numMaterial: e.material.length,
        };
      } else if (!e.startofcycle) {
        return {
          buffer: { type: "StockerWaitForUnload", stockerNum: e.locnum },
          endTime: e.endUTC,
          elapsedSeconds,
          numMaterial: e.material.length,
        };
      } else {
        return null;
      }
    case LogType.PalletOnRotaryInbound:
      if (!e.startofcycle) {
        return {
          buffer: { type: "Rotary", machineGroup: e.loc, machineNum: e.locnum },
          endTime: e.endUTC,
          elapsedSeconds,
          numMaterial: e.material.length,
        };
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
      LazySeq.ofIterable(log)
        .collect(convertEntry)
        .foldLeft(oldEntries, (lst, e) => lst.append(e))
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

      return entries.append(log);
    });
  }
);

export const updateSpecificMonthBuffer = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(specificMonthBufferEntriesRW, L.from(LazySeq.ofIterable(log).collect(convertEntry)));
  }
);
