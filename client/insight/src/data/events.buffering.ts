/* Copyright (c) 2020, John Lenz

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
import { ExpireOldData, ExpireOldDataType } from "./events.cycles";
import { LazySeq } from "./lazyseq";
import { Vector } from "prelude-ts";
import { durationToSeconds } from "./parseISODuration";

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

export interface BufferingState {
  readonly entries: Vector<BufferEntry>;
}

export const initial: BufferingState = {
  entries: Vector.empty(),
};

export function process_events(
  expire: ExpireOldData,
  allNewEvts: ReadonlyArray<Readonly<api.ILogEntry>>,
  st: BufferingState
): BufferingState {
  let entries = st.entries;

  const newEvts = allNewEvts.filter(
    (e) =>
      e.type === api.LogType.RemoveFromQueue ||
      (e.type === api.LogType.PalletInStocker && !e.startofcycle) ||
      (e.type === api.LogType.PalletOnRotaryInbound && !e.startofcycle)
  );

  switch (expire.type) {
    case ExpireOldDataType.ExpireEarlierThan: {
      const minEntry = LazySeq.ofIterable(entries).minBy((m1, m2) => m1.endTime.getTime() - m2.endTime.getTime());
      if ((minEntry.isNone() || minEntry.get().endTime >= expire.d) && newEvts.length === 0) {
        return st;
      }

      entries = entries.filter((e) => e.endTime >= expire.d);

      break;
    }

    case ExpireOldDataType.NoExpire:
      if (newEvts.length === 0) {
        return st;
      }
      break;
  }

  for (const e of newEvts) {
    if (e.elapsed === "") continue;
    const elapsedSeconds = durationToSeconds(e.elapsed);
    if (elapsedSeconds <= 0) continue;

    switch (e.type) {
      case api.LogType.RemoveFromQueue:
        entries = entries.append({
          buffer: { type: "Queue", queue: e.loc },
          endTime: e.endUTC,
          elapsedSeconds,
          numMaterial: e.material.length,
        });
        break;
      case api.LogType.PalletInStocker:
        if (!e.startofcycle && e.result === "WaitForMachine") {
          entries = entries.append({
            buffer: { type: "StockerWaitForMC", stockerNum: e.locnum },
            endTime: e.endUTC,
            elapsedSeconds,
            numMaterial: e.material.length,
          });
        } else if (!e.startofcycle) {
          entries = entries.append({
            buffer: { type: "StockerWaitForUnload", stockerNum: e.locnum },
            endTime: e.endUTC,
            elapsedSeconds,
            numMaterial: e.material.length,
          });
        }
        break;
      case api.LogType.PalletOnRotaryInbound:
        if (!e.startofcycle) {
          entries = entries.append({
            buffer: { type: "Rotary", machineGroup: e.loc, machineNum: e.locnum },
            endTime: e.endUTC,
            elapsedSeconds,
            numMaterial: e.material.length,
          });
        }
        break;
    }
  }

  return { entries };
}
