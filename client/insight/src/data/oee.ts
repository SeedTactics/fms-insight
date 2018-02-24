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
import { duration } from 'moment';
import { addDays } from 'date-fns';
import * as im from 'immutable'; // consider collectable.js at some point?

export interface StationInUse {
    readonly date: Date;
    readonly station: string;
    readonly hours: number;
}

export interface OeeState {
    // list of station use, sorted by date
    readonly last_week_of_hours: im.List<StationInUse>;
    readonly system_active_hours_per_week: number;
}

export const initial: OeeState = {
    last_week_of_hours: im.List(),
    system_active_hours_per_week: 7 * 24,
};

function stat_name(e: api.ILogEntry): string | null {
    switch (e.type) {
        case api.LogType.LoadUnloadCycle:
        case api.LogType.MachineCycle:
            return e.loc + ' #' + e.locnum;
        default:
            return null;
    }
}

export function process_events(now: Date, newEvts: Iterable<api.ILogEntry>, st: OeeState): OeeState {
    let hours = st.last_week_of_hours;
    const oneWeekAgo = addDays(now, -7);

    const evtsSeq = im.Seq(newEvts);

    // check if no changes needed: no new events and nothing to filter out
    const minEntry = hours.first();
    if ((minEntry === undefined || minEntry.date >= oneWeekAgo) && evtsSeq.isEmpty()) {
        return st;
    }

    // new entries
    const newEntries =
        evtsSeq
        .filter(e => {
            if (e.endUTC < oneWeekAgo) { return false; }
            if (e.startofcycle) { return false; }
            let name = stat_name(e);
            if (!name) { return false; }
            let activeHrs = duration(e.active).asHours();
            if (activeHrs <=  0) { return false; }
            return true;
        })
        .map(e => ({
            date: e.endUTC,
            station: stat_name(e) || '',
            // TODO: calculate estimated active time
            hours: duration(e.active).asHours()
        }));

    hours = hours.toSeq()
        .filter(e => e.date >= oneWeekAgo)
        .concat(newEntries)
        .sortBy(e => e.date)
        .toList();

    return {...st, last_week_of_hours: hours};
}