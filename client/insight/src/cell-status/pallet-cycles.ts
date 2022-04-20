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
import { HashMap } from "prelude-ts";
import { atom, RecoilValueReadOnly, TransactionInterface_UNSTABLE } from "recoil";
import { ILogEntry, LogType } from "../network/api";
import { LazySeq } from "../util/lazyseq";
import { durationToMinutes } from "../util/parseISODuration";
import { conduit } from "../util/recoil-util";
import * as L from "list/methods";
import type { ServerEventAndTime } from "./loading";

export interface PalletCycleData {
  readonly cntr: number;
  readonly x: Date;
  readonly y: number; // cycle time in minutes
  readonly active: number;
}

export type PalletCyclesByPallet = HashMap<string, L.List<PalletCycleData>>;

const last30PalletCyclesRW = atom<PalletCyclesByPallet>({
  key: "last30PalletCycles",
  default: HashMap.empty(),
});
export const last30PalletCycles: RecoilValueReadOnly<PalletCyclesByPallet> = last30PalletCyclesRW;

const specificMonthPalletCyclesRW = atom<PalletCyclesByPallet>({
  key: "specificMonthPalletCycles",
  default: HashMap.empty(),
});
export const specificMonthPalletCycles: RecoilValueReadOnly<PalletCyclesByPallet> = specificMonthPalletCyclesRW;

function logToPalletCycle(c: Readonly<ILogEntry>): PalletCycleData {
  return {
    cntr: c.counter,
    x: c.endUTC,
    y: durationToMinutes(c.elapsed),
    active: durationToMinutes(c.active),
  };
}

export const setLast30PalletCycles = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(last30PalletCyclesRW, (oldCycles) => {
      const newPalCycles = LazySeq.ofIterable(log)
        .filter((c) => !c.startofcycle && c.type === LogType.PalletCycle && c.pal !== "")
        .groupBy((c) => c.pal)
        .mapValues((cyclesForPal) => L.from(LazySeq.ofIterable(cyclesForPal).map(logToPalletCycle)));
      return oldCycles.mergeWith(newPalCycles, (oldCs, newCs) => oldCs.concat(newCs));
    });
  }
);

export const updateLast30PalletCycles = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt, now, expire }: ServerEventAndTime) => {
    if (
      evt.logEntry &&
      !evt.logEntry.startofcycle &&
      evt.logEntry.type === LogType.PalletCycle &&
      evt.logEntry.pal !== ""
    ) {
      const log = evt.logEntry;

      t.set(last30PalletCyclesRW, (oldCycles) => {
        if (expire) {
          const thirtyDaysAgo = addDays(now, -30);
          oldCycles = oldCycles.mapValues((es) => es.filter((e) => e.x >= thirtyDaysAgo)).filter((es) => es.length > 0);
        }

        return oldCycles.putWithMerge(log.pal, L.of(logToPalletCycle(log)), (a, b) => a.concat(b));
      });
    }
  }
);

export const setSpecificMonthPalletCycles = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(
      specificMonthPalletCyclesRW,
      LazySeq.ofIterable(log)
        .filter((c) => !c.startofcycle && c.type === LogType.PalletCycle && c.pal !== "")
        .groupBy((c) => c.pal)
        .mapValues((cyclesForPal) => L.from(LazySeq.ofIterable(cyclesForPal).map(logToPalletCycle)))
    );
  }
);
