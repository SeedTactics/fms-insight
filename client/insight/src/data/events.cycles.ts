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
import { duration } from 'moment';

export interface CycleData {
  readonly x: Date;
  readonly y: number; // in minutes
}

export interface CycleState {
  readonly by_part_then_stat: im.Map<string, im.Map<string, ReadonlyArray<CycleData>>>;
  readonly by_pallet: im.Map<string, ReadonlyArray<CycleData>>;
}

export const initial: CycleState = {
  by_part_then_stat: im.Map(),
  by_pallet: im.Map(),
};

export enum ExpireOldDataType {
  ExpireEarlierThan,
  NoExpire
}

export type ExpireOldData =
  | {type: ExpireOldDataType.ExpireEarlierThan, d: Date }
  | {type: ExpireOldDataType.NoExpire }
  ;

function part_and_proc(m: api.ILogMaterial): string {
  return m.part + "-" + m.proc.toString();
}

function stat_name(e: api.ILogEntry): string {
  switch (e.type) {
    case api.LogType.LoadUnloadCycle:
    case api.LogType.MachineCycle:
    case api.LogType.Wash:
      return e.loc + ' #' + e.locnum;
    case api.LogType.InspectionResult:
      return e.loc + ' ' + e.program;
    default:
      return "";
    }
}

export function process_events(
  expire: ExpireOldData,
  newEvts: Iterable<api.ILogEntry>,
  st: CycleState): CycleState {

    let evtsSeq = im.Seq(newEvts);
    let parts = st.by_part_then_stat;
    let pals = st.by_pallet;

    switch (expire.type) {
      case ExpireOldDataType.ExpireEarlierThan:

        // check if nothing to expire and no new data
        const partEntries = parts
          .valueSeq()
          .flatMap(statMap => statMap.valueSeq())
          .flatMap(cs => cs);
        const palEntries = pals
          .valueSeq()
          .flatMap(cs => cs);
        const minEntry =
          palEntries.concat(partEntries)
          .minBy(e => e.x);

        if ((minEntry === undefined || minEntry.x >= expire.d) && evtsSeq.isEmpty()) {
            return st;
        }

        // filter old events
        parts = parts
          .map(statMap =>
            statMap.map(es => es.filter(e => e.x >= expire.d))
                   .filter(es => es.length > 0)
          )
          .filter(statMap => !statMap.isEmpty());

        pals = pals
          .map(es => es.filter(e => e.x >= expire.d))
          .filter(es => es.length > 0);

        break;

      case ExpireOldDataType.NoExpire:
        if (evtsSeq.isEmpty()) { return st; }
        break;
    }

    var newPartCycles = evtsSeq
      .filter(c => !c.startofcycle)
      .flatMap(c => c.material.map(m => ({cycle: c, mat: m})))
      .groupBy(e => part_and_proc(e.mat))
      .map(cyclesForPart =>
        cyclesForPart.toSeq()
          .groupBy(e => stat_name(e.cycle))
          .filter((cycles, stat) => stat !== "")
          .map(cycles =>
            cycles.map(c => ({
              x: c.cycle.endUTC,
              y: duration(c.cycle.elapsed).asMinutes()
            }))
            .valueSeq()
            .toArray()
          )
          .toMap()
      );

    parts = parts
      .mergeWith(
        (oldStatMap, newStatMap) =>
          oldStatMap.mergeWith(
            (oldCs, newCs) => oldCs.concat(newCs),
            newStatMap
          ),
        newPartCycles
      );

    var newPalCycles = evtsSeq
      .filter(c => !c.startofcycle && c.type === api.LogType.PalletCycle && c.pal !== "")
      .groupBy(c => c.pal)
      .map(cyclesForPal =>
        cyclesForPal.map(c => ({
          x: c.endUTC,
          y: duration(c.elapsed).asMinutes()
        }))
        .valueSeq()
        .toArray()
      );
    pals = pals
      .mergeWith(
        (oldCs, newCs) => oldCs.concat(newCs),
        newPalCycles
      );

    return {...st,
      by_part_then_stat: parts,
      by_pallet: pals,
    };
}