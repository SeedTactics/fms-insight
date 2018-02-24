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

import { addDays } from 'date-fns';

import { PledgeStatus } from './pledge';
import * as events from './events';
import * as stationCycles from './station-cycles';
import { fakeCycle } from './events.fake';

it('creates initial state', () => {
  // tslint:disable no-any
  let s = events.reducer(undefined as any, undefined as any);
  // tslint:enable no-any
  expect(s).toBe(events.initial);
});

it('sets the system hours', () => {
  let s = events.reducer(events.initial, {
    type: events.ActionType.SetSystemHours,
    hours: 5
  });
  expect(s.last30.oee.system_active_hours_per_week).toBe(5);
});

it('responds to loading', () => {
  let st = events.reducer(
    {...events.initial, loading_error: new Error('hello')},
    {
      type: events.ActionType.LoadLast30Days,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.loading_events).toBe(true);
  expect(st.loading_error).toBeUndefined();
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it('responds to error', () => {
  let st = events.reducer(
    {...events.initial, loading_events: true},
    {
      type: events.ActionType.LoadLast30Days,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Error,
        error: new Error('hello')
      }
    }
  );
  expect(st.loading_events).toBe(false);
  expect(st.loading_error).toEqual(new Error('hello'));
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it('processes new log events into last30', () => {
  var now = new Date(2018, 1, 2, 3, 4, 5);

  // start with cycles from 27 days ago, 2 days ago, and today
  const todayCycle = fakeCycle(now, 30);
  var twoDaysAgo = addDays(now, -2);
  const twoDaysAgoCycle = fakeCycle(twoDaysAgo, 24);
  var twentySevenDaysAgo = addDays(now, -27);
  const twentySevenCycle = fakeCycle(twentySevenDaysAgo, 18);
  let st = events.reducer(
    events.initial,
    {
      type: events.ActionType.LoadLast30Days,
      now: now,
      pledge: {
        status: PledgeStatus.Completed,
        result: twentySevenCycle.concat(twoDaysAgoCycle, todayCycle)
      }
    });
  expect(st.last30).toMatchSnapshot('last30 with 27 days ago, 2 days ago, and today');

  // Now add again 6 days from now, so that the twoDaysAgo cycle is removed from hours (which processes only 7 days)
  // and twenty seven is removed from processing 30 days
  var sixDays = addDays(now, 6);
  const sixDaysCycle = fakeCycle(sixDays, 12);
  st = events.reducer(
    st,
    {
      type: events.ActionType.LoadLast30Days,
      now: sixDays,
      pledge: {
        status: PledgeStatus.Completed,
        result: sixDaysCycle
      }
    });

  // twentySevenDaysAgo should have been filtered out

  expect(st.last30).toMatchSnapshot('last30 with 2 days ago, today, and 6 days from now');

  // empty list should keep lists unchanged and the same object
  let newSt = events.reducer(
    st,
    {
      type: events.ActionType.LoadLast30Days,
      now: sixDays,
      pledge: {
        status: PledgeStatus.Completed,
        result: []
      }
    }
  );
  expect(newSt.last30).toBe(st.last30);
});

it("starts loading a specific month for analysis", () => {
  let st = events.reducer(
    {...events.initial},
    {
      type: events.ActionType.LoadAnalysisSpecificMonth,
      month: new Date(2018, 2, 1),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.analysis_period).toBe(events.AnalysisPeriod.SpecificMonth);
  expect(st.analysis_period_month).toEqual(new Date(2018, 2, 1));
  expect(st.loading_analysis_month).toBe(true);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it("loads 30 days for analysis", () => {
  const cycles = stationCycles.process_events(
    {type: stationCycles.ExpireOldDataType.NoExpire},
    fakeCycle(new Date(), 3),
    stationCycles.initial);

  let st = events.reducer(
    {...events.initial,
      analysis_period: events.AnalysisPeriod.SpecificMonth,
      selected_month: {station_cycles: cycles}
    },
    {
      type: events.ActionType.SetAnalysisLast30Days
    }
  );
  expect(st.analysis_period).toBe(events.AnalysisPeriod.Last30Days);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it("loads a specific month for analysis", () => {
  var now = new Date(2018, 1, 2, 3, 4, 5);

  // start with cycles from 27 days ago, 2 days ago, and today
  const todayCycle = fakeCycle(now, 30);
  var twoDaysAgo = addDays(now, -2);
  const twoDaysAgoCycle = fakeCycle(twoDaysAgo, 24);
  var twentySevenDaysAgo = addDays(now, -27);
  const twentySevenCycle = fakeCycle(twentySevenDaysAgo, 18);

  let st = events.reducer(
    {...events.initial,
      loading_analysis_month: true,
      analysis_period_month: new Date(2018, 1, 1),
      analysis_period: events.AnalysisPeriod.SpecificMonth
    },
    {
      type: events.ActionType.LoadAnalysisSpecificMonth,
      month: new Date(2018, 1, 1),
      pledge: {
        status: PledgeStatus.Completed,
        result: twentySevenCycle.concat(twoDaysAgoCycle, todayCycle)
      }
    }
  );
  expect(st.analysis_period).toBe(events.AnalysisPeriod.SpecificMonth);
  expect(st.loading_analysis_month).toBe(false);
  expect(st.analysis_period_month).toEqual(new Date(2018, 1, 1));
  expect(st.selected_month).toMatchSnapshot('selected month with 27 days ago, 2 days ago, and today');
});