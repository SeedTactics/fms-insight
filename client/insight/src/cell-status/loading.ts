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

import {
  ICurrentStatus,
  IHistoricData,
  ILogEntry,
  IRecentHistoricData,
  IServerEvent,
} from "../network/api.js";
import { LazySeq } from "@seedtactics/immutable-collections";

import * as simProd from "./sim-production.js";
import * as simUse from "./sim-station-use.js";
import * as schJobs from "./scheduled-jobs.js";
import * as buffers from "./buffers.js";
import * as currentSt from "./current-status.js";
import * as insp from "./inspections.js";
import * as mats from "./material-summary.js";
import * as names from "./names.js";
import * as estimated from "./estimated-cycle-times.js";
import * as tool from "./tool-usage.js";
import * as palCycles from "./pallet-cycles.js";
import * as statCycles from "./station-cycles.js";
import * as toolReplace from "./tool-replacements.js";
import * as simDayUsage from "./sim-day-usage.js";
import * as rebookings from "./rebookings.js";
import { Atom, atom } from "jotai";

export interface ServerEventAndTime {
  readonly evt: Readonly<IServerEvent>;
  readonly now: Date;
  readonly expire: boolean;
}

const lastEventCounterRW = atom<number | null>(null);
export const lastEventCounter: Atom<number | null> = lastEventCounterRW;

export const onServerEvent = atom(null, (_, set, evt: ServerEventAndTime) => {
  set(simProd.updateLast30JobProduction, evt);
  set(simUse.updateLast30SimStatUse, evt);
  set(schJobs.updateLast30Jobs, evt);
  set(buffers.updateLast30Buffer, evt);
  set(currentSt.updateCurrentStatus, evt);
  set(mats.updateLast30MatSummary, evt);
  set(insp.updateLast30Inspections, evt);
  set(names.updateNames, evt);
  set(tool.updateLast30ToolUse, evt);
  set(palCycles.updateLast30PalletCycles, evt);
  set(toolReplace.updateLastToolReplacements, evt);
  set(statCycles.updateLast30StationCycles, evt);
  set(rebookings.updateLast30Rebookings, evt);
  set(simDayUsage.updateLatestSimDayUsage, evt);

  if (evt.evt.logEntry) {
    const newCntr = evt.evt.logEntry.counter;
    set(lastEventCounterRW, (old) => (old === null ? old : Math.max(old, newCntr)));
  }
});

export const onLoadLast30Jobs = atom(null, (get, set, historicData: Readonly<IRecentHistoricData>) => {
  const filtered = schJobs.filterExistingJobs(get(schJobs.last30SchIds), historicData);
  set(simUse.setLast30SimStatUse, filtered);
  set(simProd.setLast30JobProduction, filtered);
  set(schJobs.setLast30Jobs, filtered);
  set(names.setNamesFromLast30Jobs, filtered);
  set(simDayUsage.setLatestSimDayUsage, filtered);
  set(rebookings.setLast30RebookingJobs, filtered);
});

export const onLoadLast30Log = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(estimated.setLast30EstimatedCycleTimes, log);
  set(buffers.setLast30Buffer, log);
  set(insp.setLast30Inspections, log);
  set(mats.setLast30MatSummary, log);
  set(names.setNamesFromLast30Evts, log);
  set(tool.setLast30ToolUse, log);
  set(palCycles.setLast30PalletCycles, log);
  set(toolReplace.setLast30ToolReplacements, log);
  set(statCycles.setLast30StationCycles, log);
  set(rebookings.setLast30Rebookings, log);

  const newCntr = LazySeq.of(log).maxBy((x) => x.counter)?.counter ?? null;
  set(lastEventCounterRW, (oldCntr) => (oldCntr === null ? newCntr : Math.max(oldCntr, newCntr ?? -1)));
});

export const onLoadCurrentSt = atom(null, (_, set, curSt: Readonly<ICurrentStatus>) => {
  set(currentSt.setCurrentStatus, curSt);
  set(names.setNamesFromCurrentStatus, curSt);
});

export const onLoadSpecificMonthJobs = atom(null, (_, set, historicData: Readonly<IHistoricData>) => {
  set(simUse.setSpecificMonthSimStatUse, historicData);
  set(simProd.setSpecificMonthJobProduction, historicData);
  set(schJobs.updateSpecificMonthJobs, historicData);
});

export const onLoadSpecificMonthLog = atom(null, (_, set, log: ReadonlyArray<Readonly<ILogEntry>>) => {
  set(estimated.setSpecificMonthEstimatedCycleTimes, log);
  set(buffers.setSpecificMonthBuffer, log);
  set(insp.setSpecificMonthInspections, log);
  set(mats.setSpecificMonthMatSummary, log);
  set(palCycles.setSpecificMonthPalletCycles, log);
  set(toolReplace.setSpecificMonthToolReplacements, log);
  set(statCycles.setSpecificMonthStationCycles, log);
});
