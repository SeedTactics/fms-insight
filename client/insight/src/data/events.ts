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
import * as moment from 'moment';
import { PledgeStatus, ConsumingPledge } from './pledge';
import * as im from 'immutable';

export interface State {
    readonly loading_events: boolean;
    readonly loading_error: Error | null;
    readonly last_week_of_events: ReadonlyArray<Readonly<api.ILogEntry>>; // TODO: deep readonly in typescript 2.8
    readonly station_active_hours_past_week: im.Map<string, number>;
    readonly system_active_hours_per_week: number;
}

const initial: State = {
    loading_events: false,
    loading_error: null,
    last_week_of_events: [],
    station_active_hours_past_week: im.Map(),
    system_active_hours_per_week: 7 * 24
};

export enum ActionType {
    RequestLastWeek = 'Events_RequestLastWeek',
    SetSystemHours = 'Events_SetSystemHours',
    Other = 'Other',
}

// TODO: use Plege when typescript 2.8 shows up
export type Action =
  | {type: ActionType.RequestLastWeek, now: Date, pledge: ConsumingPledge<ReadonlyArray<Readonly<api.ILogEntry>>>}
  | {type: ActionType.SetSystemHours, hours: number}
  | {type: ActionType.Other}
  ;

export function requestLastWeek() /*: Action<ActionUse.CreatingAction> */ {
    var client = new api.Client();
    var now = moment.utc();
    var nowDate = now.toDate();
    var oneWeekAgo = now.add(-1, 'w');
    return {
        type: ActionType.RequestLastWeek,
        now: nowDate,
        pledge: client.apiV1LogEventsAllGet(oneWeekAgo.toDate(), nowDate)
    };
}

function stat_name(e: api.ILogEntry): string | null {
    switch (e.type) {
        case api.LogEntryType.LoadUnloadCycle:
        case api.LogEntryType.MachineCycle:
            return e.loc + ' #' + e.locnum;
        default:
            return null;
    }
}

function remove_old_entries(now: Date, st: State): State {
    let hours = st.station_active_hours_past_week;
    let oneWeekAgo = moment(now).add(-1, 'w').toDate();
    let newEvts = st.last_week_of_events.filter(e => {
        if (e.endUTC === undefined || e.endUTC < oneWeekAgo) {
            if (e.startofcycle) { return false; }
            let name = stat_name(e);
            if (!name) { return false; }
            let activeHrs = moment.duration(e.active).asHours();
            if (activeHrs > 0) {
                // TODO: estimate cycle times
                hours = hours.update(name, 0, old => old - activeHrs);
            }
            return false;
        } else {
            return true;
        }
    });
    return {...st,
        station_active_hours_past_week: hours,
        last_week_of_events: newEvts
    };
}

function add_new_entries(evts: ReadonlyArray<api.ILogEntry>, st: State): State {
    let hours = st.station_active_hours_past_week;
    evts.forEach(e => {
        if (e.startofcycle) { return; }
        let name = stat_name(e);
        if (!name) { return; }
        let activeHrs = moment.duration(e.active).asHours();
        if (activeHrs > 0) {
            // TODO: estimate cycle times
            hours = hours.update(name, 0, old => old + activeHrs);
        }
    });
    return {...st,
        station_active_hours_past_week: hours,
        last_week_of_events: st.last_week_of_events.concat(evts)
    };
}

export function reducer(s: State, a: Action): State {
    if (s === undefined) { return initial; }
    switch (a.type) {
        case ActionType.RequestLastWeek:
            switch (a.pledge.status) {
                case PledgeStatus.Starting:
                    return {...s, loading_events: true, loading_error: null};
                case PledgeStatus.Completed:
                    let s2 = remove_old_entries(a.now, s);
                    let s3 = add_new_entries(a.pledge.result, s2);
                    return {...s3, loading_events: false};
                case PledgeStatus.Error:
                    return {...s, loading_events: false, loading_error: a.pledge.error};
                default: return s;
            }

        case ActionType.SetSystemHours:
            return {...s, system_active_hours_per_week: a.hours};

        default: return s;
    }
}