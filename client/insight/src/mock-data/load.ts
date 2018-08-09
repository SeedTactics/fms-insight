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

import * as api from '../data/api';
import * as im from 'immutable';
import { addSeconds } from 'date-fns';

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

async function loadEventsJson(offsetSeconds: number): Promise<Readonly<api.ILogEntry>[]> {
  const evtJson = require("./events-json.txt");
  let evtsSeq: im.Seq.Indexed<object>;
  if (typeof(evtJson) === "string") {
    // parcel provides the url to the file
    const req = await fetch(evtJson);
    const rawEvts = await req.json();
    evtsSeq = im.Seq.Indexed(rawEvts);
  } else {
    // jest loads the json and parses it already into an array
    evtsSeq = im.Seq.Indexed(evtJson);
  }

  return evtsSeq
    .map((evt: object) => {
        const e = api.LogEntry.fromJS(evt);
        e.endUTC = addSeconds(e.endUTC, offsetSeconds);
        return e;
    })
    .sortBy(e => e.endUTC)
    .filter(e => {
      if (e.type === api.LogType.InspectionResult) {
        // filter out a couple inspection results so they are uninspected
        // and display as uninspected on the station monitor screen
        var mid = e.material[0].id;
        if (mid === 2993 || mid === 2974) {
          return false;
        } else {
          return true;
        }
      } else {
        return true;
      }
    })
    .toArray();
}

export interface MockData {
  readonly curSt: Readonly<api.ICurrentStatus>;
  readonly jobs: Readonly<api.IHistoricData>;
  readonly workorders: Map<string, ReadonlyArray<Readonly<api.IPartWorkorder>>>;
  readonly events: Promise<Readonly<api.ILogEntry>[]>;
}

export function loadMockData(offsetSeconds: number): MockData {

  const status = api.CurrentStatus.fromJS(require("./status-mock.json"));
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

  const jobs = api.NewJobs.fromJS(require("./newjobs.json"));
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

  return {
    curSt: status,
    jobs: historic,
    workorders: new Map<string, ReadonlyArray<Readonly<api.IPartWorkorder>>>(),
    events: loadEventsJson(offsetSeconds)
  };
}