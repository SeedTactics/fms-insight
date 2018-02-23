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
import { addDays, addMonths, startOfMonth } from 'date-fns';
import * as im from 'immutable'; // consider collectable.js at some point?
import { PledgeStatus, ConsumingPledge } from './pledge';

import * as api from './api';
import * as oee from './oee';

export { OeeState, StationInUse } from './oee';

export enum AnalysisPeriod {
    Last30Days = 'Last_30_Days',
    SpecificMonth = 'Specific_Month'
}

export interface State {
    readonly loading_events: boolean;
    readonly loading_error?: Error;

    readonly analysis_period: AnalysisPeriod;
    readonly analysis_period_month: Date;
    readonly loading_analysis_period: boolean;

    // list of entries sorted by endUTC
    readonly last_30_days_of_events: im.List<Readonly<api.ILogEntry>>; // TODO: deep readonly in typescript 2.8

    readonly oee: oee.OeeState;

    readonly analysis_period_month_events: im.List<Readonly<api.ILogEntry>>;
}

export const initial: State = {
    loading_events: false,
    analysis_period: AnalysisPeriod.Last30Days,
    analysis_period_month: startOfMonth(new Date()),
    loading_analysis_period: false,

    last_30_days_of_events: im.List(),
    oee: oee.initial,
    analysis_period_month_events: im.List()
};

export enum ActionType {
    RequestLast30Days = 'Events_RequestLast30Days',
    SetSystemHours = 'Events_SetSystemHours',
    SetAnalysisLast30Days = 'Events_SetAnalysisLast30Days',
    LoadAnalysisSpecificMonth = 'Events_LoadAnalysisMonth',
    SetAnalysisMonth = 'Events_SetAnalysisMonth',
    Other = 'Other',
}

// TODO: use Plege when typescript 2.8 shows up
export type Action =
  | {type: ActionType.RequestLast30Days, now: Date, pledge: ConsumingPledge<ReadonlyArray<Readonly<api.ILogEntry>>>}
  | {type: ActionType.SetSystemHours, hours: number}
  | {type: ActionType.SetAnalysisLast30Days }
  | {type: ActionType.SetAnalysisMonth, month: Date }
  | {
      type: ActionType.LoadAnalysisSpecificMonth,
      month: Date,
      pledge: ConsumingPledge<ReadonlyArray<Readonly<api.ILogEntry>>>
    }
  | {type: ActionType.Other}
  ;

export function requestLastMonth() /*: Action<ActionUse.CreatingAction> */ {
    var client = new api.LogClient();
    var now = new Date();
    var oneWeekAgo = addDays(now, -30);
    return {
        type: ActionType.RequestLast30Days,
        now: now,
        pledge: client.get(oneWeekAgo, now)
    };
}

export function analyzeLast30Days() {
    return {type: ActionType.SetAnalysisLast30Days};
}

export function analyzeSpecificMonth(month: Date) {
    var client = new api.LogClient();
    var startOfNextMonth = addMonths(month, 1);
    return {
        type: ActionType.LoadAnalysisSpecificMonth,
        month: month,
        pledge: client.get(month, startOfNextMonth)
    };
}

export function setAnalysisMonth(month: Date) {
    return {
        type: ActionType.SetAnalysisMonth,
        month: month
    };
}

function refresh_events(now: Date, newEvts: ReadonlyArray<api.ILogEntry>, st: State): State {
    let evts = st.last_30_days_of_events;
    let thirtyDaysAgo = addDays(now, -30);

    // check if no changes needed: no new events and nothing to filter out
    const minEntry = evts.first();
    if ((minEntry === undefined || minEntry.endUTC >= thirtyDaysAgo) && newEvts.length === 0) {
        return st;
    }

    evts = evts.toSeq()
        .concat(newEvts)
        .filter(e => e.endUTC >= thirtyDaysAgo)
        .sortBy(e => e.endUTC)
        .toList();

    return {...st, last_30_days_of_events: evts};
}

export function reducer(s: State, a: Action): State {
    if (s === undefined) { return initial; }
    switch (a.type) {
        case ActionType.RequestLast30Days:
            switch (a.pledge.status) {
                case PledgeStatus.Starting:
                    return {...s, loading_events: true, loading_error: undefined};
                case PledgeStatus.Completed:
                    let s2 = refresh_events(a.now, a.pledge.result, s);
                    return {...s2,
                        oee: oee.process_events(a.now, a.pledge.result, s2.oee),
                        loading_events: false
                    };
                case PledgeStatus.Error:
                    return {...s, loading_events: false, loading_error: a.pledge.error};
                default: return s;
            }

        case ActionType.SetSystemHours:
            return {...s,
                oee: {...s.oee, system_active_hours_per_week: a.hours}
            };

        case ActionType.SetAnalysisLast30Days:
            return {...s,
                analysis_period: AnalysisPeriod.Last30Days,
                analysis_period_month_events: im.List()
            };

        case ActionType.LoadAnalysisSpecificMonth:
            switch (a.pledge.status) {
                case PledgeStatus.Starting:
                    return {...s,
                        analysis_period: AnalysisPeriod.SpecificMonth,
                        analysis_period_month: a.month,
                        loading_error: undefined,
                        loading_analysis_period: true,
                        analysis_period_month_events: im.List()
                    };

                case PledgeStatus.Completed:
                    return {...s,
                        loading_analysis_period: false,
                        analysis_period_month_events:
                            im.List(a.pledge.result).sortBy(e => e.endUTC)
                    };

                case PledgeStatus.Error:
                    return {...s,
                        loading_analysis_period: false,
                        loading_error: a.pledge.error
                    };

                default: return s;
            }

        case ActionType.SetAnalysisMonth:
            return {...s, analysis_period_month: a.month};

        default: return s;
    }
}