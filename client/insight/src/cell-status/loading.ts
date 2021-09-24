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
import { ICurrentStatus, IHistoricData, ILogEntry, IServerEvent } from "../network/api";
import { conduit } from "../util/recoil-util";
import { LazySeq } from "../util/lazyseq";

import * as simProd from "./sim-production";
import * as simUse from "./sim-station-use";
import * as schJobs from "./scheduled-jobs";
import * as buffers from "./buffers";
import * as currentSt from "./current-status";
import * as insp from "./inspections";
import * as names from "./names";
import * as estimated from "./estimated-cycle-times";
import * as tool from "./tool-usage";
import * as palCycles from "./pallet-cycles";
import * as statCycles from "./station-cycles";

export interface ServerEventAndTime {
  readonly evt: Readonly<IServerEvent>;
  readonly now: Date;
  readonly expire: boolean;
}

const lastEventCounterRW = atom<number | null>({
  key: "websocket-last-event-counter",
  default: null,
});
export const lastEventCounter: RecoilValueReadOnly<number | null> = lastEventCounterRW;

export const onServerEvent = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, evt: ServerEventAndTime) => {
    simProd.updateLast30JobProduction.transform(t, evt);
    simUse.updateLast30SimStatUse.transform(t, evt);
    schJobs.updateLast30Jobs.transform(t, evt);
    buffers.updateLast30Buffer.transform(t, evt);
    currentSt.updateCurrentStatus.transform(t, evt);
    insp.updateLast30Inspections.transform(t, evt);
    names.updateNames.transform(t, evt);
    tool.updateLast30ToolUse.transform(t, evt);
    palCycles.updateLast30PalletCycles.transform(t, evt);
    statCycles.updateLast30StationCycles.transform(t, evt);

    if (evt.evt.logEntry) {
      const newCntr = evt.evt.logEntry.counter;
      t.set(lastEventCounterRW, (old) => (old === null ? old : Math.max(old, newCntr)));
    }
  }
);

export const onLoadLast30Jobs = conduit<Readonly<IHistoricData>>(
  (t: TransactionInterface_UNSTABLE, historicData: Readonly<IHistoricData>) => {
    simUse.setLast30SimStatUse.transform(t, historicData);
    simProd.setLast30JobProduction.transform(t, historicData);
    schJobs.setLast30Jobs.transform(t, historicData);
    names.setNamesFromLast30Jobs.transform(t, historicData);
  }
);

export const onLoadLast30Log = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    estimated.setLast30EstimatedCycleTimes.transform(t, log);
    buffers.setLast30Buffer.transform(t, log);
    insp.setLast30Inspections.transform(t, log);
    names.setNamesFromLast30Evts.transform(t, log);
    tool.setLast30ToolUse.transform(t, log);
    palCycles.setLast30PalletCycles.transform(t, log);
    statCycles.setLast30StationCycles.transform(t, log);

    const newCntr = LazySeq.ofIterable(log)
      .maxOn((x) => x.counter)
      .map((x) => x.counter)
      .getOrNull();
    t.set(lastEventCounterRW, (oldCntr) => (oldCntr === null ? newCntr : Math.max(oldCntr, newCntr ?? -1)));
  }
);

export const onLoadCurrentSt = conduit<Readonly<ICurrentStatus>>(
  (t: TransactionInterface_UNSTABLE, curSt: Readonly<ICurrentStatus>) => {
    currentSt.setCurrentStatus.transform(t, curSt);
  }
);

export const onLoadSpecificMonthJobs = conduit<Readonly<IHistoricData>>(
  (t: TransactionInterface_UNSTABLE, historicData: Readonly<IHistoricData>) => {
    simUse.setSpecificMonthSimStatUse.transform(t, historicData);
    simProd.setSpecificMonthJobProduction.transform(t, historicData);
    schJobs.updateSpecificMonthJobs.transform(t, historicData);
  }
);

export const onLoadSpecificMonthLog = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    estimated.setSpecificMonthEstimatedCycleTimes.transform(t, log);
    buffers.setSpecificMonthBuffer.transform(t, log);
    insp.setSpecificMonthInspections.transform(t, log);
    palCycles.setSpecificMonthPalletCycles.transform(t, log);
    statCycles.setSpecificMonthStationCycles.transform(t, log);
  }
);
