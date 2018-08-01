import { initMockBackend } from "./backend";

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
import * as im from 'immutable';
import { addSeconds, differenceInSeconds } from 'date-fns';
import * as events from './events';
import * as current from './current-status';

function offsetJob(j: api.JobPlan, offsetSeconds: number) {
  j.routeStartUTC = addSeconds(j.routeStartUTC, offsetSeconds);
  j.routeEndUTC = addSeconds(j.routeEndUTC, offsetSeconds);
  for (const proc of j.procsAndPaths) {
    for (const path of proc.paths) {
      path.simulatedStartingUTC = addSeconds(path.simulatedStartingUTC, offsetSeconds);
      for (const prod of path.simulatedProduction || []) {
        prod.timeUTC = addSeconds(prod.timeUTC, offsetSeconds);
      }
    }
  }
}

function parseEvts(raw: string, offsetSeconds: number): im.Seq.Indexed<api.LogEntry> {
  return im.Seq(raw.split('\n'))
    .map(line => {
      if (line !== "") {
        const e = api.LogEntry.fromJS(JSON.parse(line));
        e.endUTC = addSeconds(e.endUTC, offsetSeconds);
        return e;
      } else {
        return null;
      }
    })
    .filter((e): e is api.LogEntry => e !== null);
}

// tslint:disable-next-line:no-any
export function initMockData(d: (a: any) => void) {
  if (process.env.REACT_APP_MOCK_DATA) {
    const jan18 = new Date(Date.UTC(2018, 1, 1, 0, 0, 0));
    const offsetSeconds = differenceInSeconds(new Date(), jan18);

    const statusJson = require("../sample-data/status-mock.json");
    const status = api.CurrentStatus.fromJS(statusJson);
    for (const j of Object.values(status.jobs)) {
      offsetJob(j, offsetSeconds);
    }
    for (const m of status.material) {
      // for some reason, status-mock.json uses numbers instead of strings for pallets
      // and strings instead of numbers for face
      if (m.location.pallet) {
        m.location.pallet = m.location.pallet.toString();
      }
      if (m.location.face) {
        // tslint:disable-next-line:no-any
        m.location.face = parseInt(m.location.face as any, 10);
      }
    }

    const jobsJson = require("../sample-data/newjobs.json");
    const jobs = api.NewJobs.fromJS(jobsJson);
    for (const j of jobs.jobs) {
      offsetJob(j, offsetSeconds);
    }
    for (const s of jobs.stationUse || []) {
      s.startUTC = addSeconds(s.startUTC, offsetSeconds);
      s.endUTC = addSeconds(s.endUTC, offsetSeconds);
    }
    for (const w of jobs.currentUnfilledWorkorders || []) {
      w.dueDate = addSeconds(w.dueDate, offsetSeconds);
    }
    const historic: api.IHistoricData = {
      jobs: jobs.jobs.reduce(
        (acc, j) => { acc[j.unique] = j; return acc; },
        {} as {[key: string]: api.JobPlan}),
      stationUse: jobs.stationUse || []
    };

    const evts =
      parseEvts(
        require("raw-loader!../sample-data/events-sim.json"),
        offsetSeconds)
      .concat(
        parseEvts(
          require("raw-loader!../sample-data/events-status-mock.json"),
          offsetSeconds)
      )
      .concat(
        parseEvts(
          require("raw-loader!../sample-data/events-insp-results.json"),
          offsetSeconds)
      )
      .toArray();

    initMockBackend(
      status,
      historic,
      new Map<string, ReadonlyArray<Readonly<api.IPartWorkorder>>>(),
      evts);

    d(events.loadLast30Days());
    d(current.loadCurrentStatus());
  }
}