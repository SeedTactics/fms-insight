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
import { PledgeStatus, ConsumingPledge } from './pledge';
import { addDays, addMonths, startOfMonth } from 'date-fns';
import * as im from 'immutable'; // consider collectable.js at some point?

export interface StationInUse {
    readonly date: Date;
    readonly station: string;
    readonly hours: number;
}

export enum AnalysisPeriod {
    Last30Days = 'Last_30_Days',
    SpecificMonth = 'Specific_Month'
}

export interface State {
    readonly loading_events: boolean;
    readonly loading_error?: Error;

    // list of entries sorted by endUTC
    readonly last_30_days_of_events: im.List<Readonly<api.ILogEntry>>; // TODO: deep readonly in typescript 2.8

    // list of station use, sorted by date
    readonly last_week_of_hours: im.List<StationInUse>;

    readonly system_active_hours_per_week: number;

    readonly analysis_period: AnalysisPeriod;
    readonly analysis_period_month: Date;
    readonly loading_analysis_period: boolean;
    readonly analysis_period_month_events: im.List<Readonly<api.ILogEntry>>;
}

export const initial: State = {
    loading_events: false,
    last_30_days_of_events: im.List(),
    last_week_of_hours: im.List(),
    system_active_hours_per_week: 7 * 24,
    analysis_period: AnalysisPeriod.Last30Days,
    analysis_period_month: startOfMonth(new Date()),
    loading_analysis_period: false,
    analysis_period_month_events: im.List()
};

export enum ActionType {
    RequestLastWeek = 'Events_RequestLastWeek',
    SetSystemHours = 'Events_SetSystemHours',
    LoadAnalysisLast30Days = 'Events_LoadAnalysisLast30Days',
    LoadAnalysisSpecificMonth = 'Events_LoadAnalysisMonth',
    SetAnalysisMonth = 'Events_SetAnalysisMonth',
    Other = 'Other',
}

// TODO: use Plege when typescript 2.8 shows up
export type Action =
  | {type: ActionType.RequestLastWeek, now: Date, pledge: ConsumingPledge<ReadonlyArray<Readonly<api.ILogEntry>>>}
  | {type: ActionType.SetSystemHours, hours: number}
  | {type: ActionType.LoadAnalysisLast30Days }
  | {
      type: ActionType.LoadAnalysisSpecificMonth,
      month: Date,
      pledge: ConsumingPledge<ReadonlyArray<Readonly<api.ILogEntry>>>
    }
  | {type: ActionType.SetAnalysisMonth, month: Date }
  | {type: ActionType.Other}
  ;

export function requestLastMonth() /*: Action<ActionUse.CreatingAction> */ {
    var client = new api.LogClient();
    var now = new Date();
    var oneWeekAgo = addDays(now, -30);
    return {
        type: ActionType.RequestLastWeek,
        now: now,
        pledge: client.get(oneWeekAgo, now)
    };
}

export function analyzeLast30Days() {
    return {type: ActionType.LoadAnalysisLast30Days};
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

function stat_name(e: api.ILogEntry): string | null {
    switch (e.type) {
        case api.LogType.LoadUnloadCycle:
        case api.LogType.MachineCycle:
            return e.loc + ' #' + e.locnum;
        default:
            return null;
    }
}

function refresh_weekly_hours(now: Date, newEvts: ReadonlyArray<api.ILogEntry>, st: State): State {
    let hours = st.last_week_of_hours;
    const oneWeekAgo = addDays(now, -7);

    // check if no changes needed: no new events and nothing to filter out
    const minEntry = hours.first();
    if ((minEntry === undefined || minEntry.date >= oneWeekAgo) && newEvts.length === 0) {
        return st;
    }

    // new entries
    const newEntries =
        im.Seq(newEvts)
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
        case ActionType.RequestLastWeek:
            switch (a.pledge.status) {
                case PledgeStatus.Starting:
                    return {...s, loading_events: true, loading_error: undefined};
                case PledgeStatus.Completed:
                    let s2 = refresh_weekly_hours(a.now, a.pledge.result, s);
                    let s3 = refresh_events(a.now, a.pledge.result, s2);
                    return {...s3, loading_events: false};
                case PledgeStatus.Error:
                    return {...s, loading_events: false, loading_error: a.pledge.error};
                default: return s;
            }

        case ActionType.SetSystemHours:
            return {...s, system_active_hours_per_week: a.hours};

        case ActionType.LoadAnalysisLast30Days:
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