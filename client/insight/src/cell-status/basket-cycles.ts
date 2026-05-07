/* Copyright (c) 2026, John Lenz

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
import { ILogEntry, LogType } from "../network/api.js";
import { HashMap, LazySeq } from "@seedtactics/immutable-collections";
import { durationToMinutes } from "../util/parseISODuration.js";
import type { ServerEventAndTime } from "./loading.js";
import { Atom, atom } from "jotai";
import type { PalletCycleData } from "./pallet-cycles.js";

export type BasketCycleData = PalletCycleData;
export type BasketCyclesByCntr = HashMap<number, BasketCycleData>;
export type BasketCyclesByBasket = HashMap<number, BasketCyclesByCntr>;

const last30BasketCyclesRW = atom<BasketCyclesByBasket>(HashMap.empty<number, BasketCyclesByCntr>());
export const last30BasketCycles: Atom<BasketCyclesByBasket> = last30BasketCyclesRW;
export const last30HasBasketCycleData: Atom<boolean> = atom((get) => get(last30BasketCyclesRW).size > 0);

const specificMonthBasketCyclesRW = atom<BasketCyclesByBasket>(HashMap.empty<number, BasketCyclesByCntr>());
export const specificMonthBasketCycles: Atom<BasketCyclesByBasket> = specificMonthBasketCyclesRW;
export const specificMonthHasBasketCycleData: Atom<boolean> = atom(
  (get) => get(specificMonthBasketCyclesRW).size > 0,
);

function logToBasketCycle(c: Readonly<ILogEntry>): BasketCycleData {
  return {
    cntr: c.counter,
    x: c.endUTC,
    y: durationToMinutes(c.elapsed),
    active: durationToMinutes(c.active),
    mats: c.material,
  };
}

export const setLast30BasketCycles = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(last30BasketCyclesRW, (oldCycles) =>
    oldCycles.union(
      LazySeq.of(log)
        .filter((c) => !c.startofcycle && c.type === LogType.BasketCycle && c.pal !== 0)
        .toLookupMap(
          (c) => c.pal,
          (c) => c.counter,
          logToBasketCycle,
        ),
      (e1, e2) => e1.union(e2),
    ),
  );
});

export const updateLast30BasketCycles = atom(null, (_, set, { evt, now, expire }: ServerEventAndTime) => {
  if (
    evt.logEntry &&
    !evt.logEntry.startofcycle &&
    evt.logEntry.type === LogType.BasketCycle &&
    evt.logEntry.pal !== 0
  ) {
    const log = evt.logEntry;

    set(last30BasketCyclesRW, (oldCycles) => {
      if (expire) {
        const thirtyDaysAgo = addDays(now, -30);
        oldCycles = oldCycles.collectValues((es) => {
          const newEs = es.filter((e) => e.x >= thirtyDaysAgo);
          return newEs.size > 0 ? newEs : null;
        });
      }

      return oldCycles.modify(log.pal, (old) =>
        (old ?? HashMap.empty()).set(log.counter, logToBasketCycle(log)),
      );
    });
  }
});

export const setSpecificMonthBasketCycles = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(
    specificMonthBasketCyclesRW,
    LazySeq.of(log)
      .filter((c) => !c.startofcycle && c.type === LogType.BasketCycle && c.pal !== 0)
      .toLookupMap(
        (c) => c.pal,
        (c) => c.counter,
        logToBasketCycle,
      ),
  );
});
