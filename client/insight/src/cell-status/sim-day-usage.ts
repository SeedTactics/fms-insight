/* Copyright (c) 2023, John Lenz

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

import { Atom, Getter, Setter, atom } from "jotai";
import { IRecentHistoricData, ISimulatedDayUsage } from "../network/api";
import { ServerEventAndTime } from "./loading";

export type LatestSimDayUsage = {
  readonly simId: string;
  readonly usage: ReadonlyArray<Readonly<ISimulatedDayUsage>>;
};

const latestUsageRW = atom<LatestSimDayUsage | null>(null);
export const latestSimDayUsage: Atom<LatestSimDayUsage | null> = latestUsageRW;

function update(get: Getter, set: Setter, schId: string, usage: ReadonlyArray<Readonly<ISimulatedDayUsage>>) {
  const old = get(latestSimDayUsage);
  if (old === null || old.simId < schId) {
    set(latestUsageRW, { simId: schId, usage });
  }
}

export const setLatestSimDayUsage = atom(null, (get, set, history: Readonly<IRecentHistoricData>) => {
  if (
    !history.mostRecentSimulationId ||
    !history.mostRecentSimDayUsage ||
    history.mostRecentSimDayUsage.length === 0
  ) {
    return;
  }
  update(get, set, history.mostRecentSimulationId, history.mostRecentSimDayUsage);
});

export const updateLatestSimDayUsage = atom(null, (get, set, { evt }: ServerEventAndTime) => {
  if (evt.newJobs && evt.newJobs.simDayUsage && evt.newJobs.simDayUsage.length > 0) {
    update(get, set, evt.newJobs.scheduleId, evt.newJobs.simDayUsage);
  }
});
