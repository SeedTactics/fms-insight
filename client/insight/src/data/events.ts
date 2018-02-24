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
import { PledgeStatus, ConsumingPledge } from './pledge';
import * as im from 'immutable';

import * as api from './api';
import * as oee from './oee';
import * as stationCycles from './station-cycles';

export { OeeState, StationInUse } from './oee';
export { StationCycleState, StationCycle } from './station-cycles';

export enum AnalysisPeriod {
    Last30Days = 'Last_30_Days',
    SpecificMonth = 'Specific_Month'
}

export interface Last30Days {
    readonly latest_event?: Date;
    readonly latest_counter?: number;
    readonly oee: oee.OeeState;
    readonly station_cycles: stationCycles.StationCycleState;
}

export interface AnalysisMonth {
    readonly station_cycles: stationCycles.StationCycleState;
}

const emptyAnalysisMonth = {
    station_cycles: stationCycles.initial
};

export interface State {
    readonly loading_events: boolean;
    readonly loading_error?: Error;

    readonly analysis_period: AnalysisPeriod;
    readonly analysis_period_month: Date;
    readonly loading_analysis_month: boolean;

    readonly last30: Last30Days;
    readonly selected_month: AnalysisMonth;
}

export const initial: State = {
    loading_events: false,
    analysis_period: AnalysisPeriod.Last30Days,
    analysis_period_month: startOfMonth(new Date()),
    loading_analysis_month: false,

    last30: {
        oee: oee.initial,
        station_cycles: stationCycles.initial
    },

    selected_month: emptyAnalysisMonth,
};

export enum ActionType {
    SetAnalysisLast30Days = 'Events_SetAnalysisLast30Days',
    SetAnalysisMonth = 'Events_SetAnalysisMonth',
    LoadLast30Days = 'Events_LoadLast30Days',
    LoadAnalysisSpecificMonth = 'Events_LoadAnalysisMonth',
    SetSystemHours = 'Events_SetSystemHours',
    Other = 'Other',
}

// TODO: use Plege when typescript 2.8 shows up
export type Action =
  | {type: ActionType.SetAnalysisLast30Days }
  | {type: ActionType.SetAnalysisMonth, month: Date }
  | {type: ActionType.LoadLast30Days, now: Date, pledge: ConsumingPledge<ReadonlyArray<Readonly<api.ILogEntry>>>}
  | {
      type: ActionType.LoadAnalysisSpecificMonth,
      month: Date,
      pledge: ConsumingPledge<ReadonlyArray<Readonly<api.ILogEntry>>>
    }
  | {type: ActionType.SetSystemHours, hours: number}
  | {type: ActionType.Other}
  ;

export function loadLast30Days() /*: Action<ActionUse.CreatingAction> */ {
    var client = new api.LogClient();
    var now = new Date();
    var oneWeekAgo = addDays(now, -30);
    return {
        type: ActionType.LoadLast30Days,
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

function safeAssign<T, R extends T>(o: T, n: R): T {
    let allMatch: boolean = true;
    for (let k of Object.keys(n)) {
        // tslint:disable-next-line:no-any
        if ((o as any)[k] !== (n as any)[k]) {
            allMatch = false;
            break;
        }
    }
    if (allMatch) {
        return o;
    } else {
        return Object.assign(o, n);
    }
}

function processRecentEvents(now: Date, evts: Iterable<api.ILogEntry>, s: Last30Days): Last30Days {
    let lastCounter = s.latest_counter;
    let lastDate = s.latest_event;
    let lastNewEvent = im.Seq(evts).maxBy(e => e.counter);
    if (lastNewEvent !== undefined) {
        if (lastCounter === undefined || lastCounter < lastNewEvent.counter) {
            lastCounter = lastNewEvent.counter;
            lastDate = lastNewEvent.endUTC;
        }
    }
    return safeAssign(
        s,
        {
            latest_counter: lastCounter,
            latest_event: lastDate,
            oee: oee.process_events(now, evts, s.oee),
            station_cycles: stationCycles.process_events(
                {type: stationCycles.ExpireOldDataType.ExpireEarlierThan, d: now},
                evts,
                s.station_cycles),
        });
}

function processSpecificMonth(evts: Iterable<api.ILogEntry>, s: AnalysisMonth): AnalysisMonth {
    return safeAssign(
        s,
        {
            station_cycles: stationCycles.process_events(
                {type: stationCycles.ExpireOldDataType.NoExpire},
                evts,
                s.station_cycles)
        }
    );
}

export function reducer(s: State, a: Action): State {
    if (s === undefined) { return initial; }
    switch (a.type) {
        case ActionType.LoadLast30Days:
            switch (a.pledge.status) {
                case PledgeStatus.Starting:
                    return {...s, loading_events: true, loading_error: undefined};
                case PledgeStatus.Completed:
                    return {...s,
                        last30: processRecentEvents(a.now, a.pledge.result, s.last30),
                        loading_events: false
                    };
                case PledgeStatus.Error:
                    return {...s, loading_events: false, loading_error: a.pledge.error};
                default: return s;
            }

        case ActionType.SetSystemHours:
            return {...s,
                last30: {...s.last30,
                    oee: {...s.last30.oee, system_active_hours_per_week: a.hours}
                },
            };

        case ActionType.SetAnalysisLast30Days:
            return {...s,
                analysis_period: AnalysisPeriod.Last30Days,
                selected_month: emptyAnalysisMonth,
            };

        case ActionType.LoadAnalysisSpecificMonth:
            switch (a.pledge.status) {
                case PledgeStatus.Starting:
                    return {...s,
                        analysis_period: AnalysisPeriod.SpecificMonth,
                        analysis_period_month: a.month,
                        loading_error: undefined,
                        loading_analysis_month: true,
                        selected_month: emptyAnalysisMonth,
                    };

                case PledgeStatus.Completed:
                    return {...s,
                        loading_analysis_month: false,
                        selected_month: processSpecificMonth(a.pledge.result, s.selected_month)
                    };

                case PledgeStatus.Error:
                    return {...s,
                        loading_analysis_month: false,
                        loading_error: a.pledge.error
                    };

                default: return s;
            }

        case ActionType.SetAnalysisMonth:
            return {...s, analysis_period_month: a.month};

        default: return s;
    }
}