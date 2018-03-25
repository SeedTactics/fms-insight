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

import { addDays, addHours } from 'date-fns';
import { duration } from 'moment';

import { PledgeStatus } from './pledge';
import * as events from './events';
import * as stationCycles from './events.cycles';
import { fakeCycle } from './events.fake';
import { ILogEntry } from './api';

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
      type: events.ActionType.LoadRecentLogEntries,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.loading_log_entries).toBe(true);
  expect(st.loading_job_history).toBe(false);
  expect(st.loading_error).toBeUndefined();
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it('responds to loading for jobs', () => {
  let st = events.reducer(
    {...events.initial, loading_error: new Error('hello')},
    {
      type: events.ActionType.LoadRecentJobHistory,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.loading_log_entries).toBe(false);
  expect(st.loading_job_history).toBe(true);
  expect(st.loading_error).toBeUndefined();
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it('responds to error', () => {
  let st = events.reducer(
    {...events.initial, loading_log_entries: true},
    {
      type: events.ActionType.LoadRecentLogEntries,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Error,
        error: new Error('hello')
      }
    }
  );
  expect(st.loading_log_entries).toBe(false);
  expect(st.loading_job_history).toBe(false);
  expect(st.loading_error).toEqual(new Error('hello'));
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it('responds to error for jobs', () => {
  let st = events.reducer(
    {...events.initial, loading_job_history: true},
    {
      type: events.ActionType.LoadRecentJobHistory,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Error,
        error: new Error('hello')
      }
    }
  );
  expect(st.loading_log_entries).toBe(false);
  expect(st.loading_job_history).toBe(false);
  expect(st.loading_error).toEqual(new Error('hello'));
  expect(st.last30).toBe(events.initial.last30);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

function procNewEvents(evtsToAction: (now: Date, newEvts: ReadonlyArray<ILogEntry>) => events.Action) {
  var now = new Date(2018, 1, 2, 3, 4, 5);

  // start with cycles from 27 days ago, 2 days ago, and today
  const todayCycle = fakeCycle(now, 30, "part111", 1, 'palbb');
  var twoDaysAgo = addDays(now, -2);
  const twoDaysAgoCycle = fakeCycle(twoDaysAgo, 24, "part222", 1, 'palbb');
  var twentySevenDaysAgo = addDays(now, -27);
  const twentySevenCycle = fakeCycle(twentySevenDaysAgo, 18, "part222", 2, 'palaa');
  let st = events.reducer(
    events.initial,
    {
      type: events.ActionType.LoadRecentLogEntries,
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
  const sixDaysCycle = fakeCycle(sixDays, 12, "part111", 1, 'palcc');
  st = events.reducer(
    st,
    evtsToAction(sixDays, sixDaysCycle)
  );

  // twentySevenDaysAgo should have been filtered out

  expect(st.last30).toMatchSnapshot('last30 with 2 days ago, today, and 6 days from now');

  // empty list should keep lists unchanged and the same object
  let newSt = events.reducer(
    st,
    {
      type: events.ActionType.LoadRecentLogEntries,
      now: sixDays,
      pledge: {
        status: PledgeStatus.Completed,
        result: []
      }
    }
  );
  expect(newSt.last30).toBe(st.last30);
}

it('processes new log events into last30', () => {
  procNewEvents((now, evts) =>
    ({
      type: events.ActionType.LoadRecentLogEntries,
      now,
      pledge: {
        status: PledgeStatus.Completed,
        result: evts
      }
    }));
});

it("refreshes new events into last30", () => {
  procNewEvents((now, evts) =>
    ({
      type: events.ActionType.ReceiveNewLogEntries,
      now,
      events: evts
    }));
});

it("starts loading a specific month for analysis", () => {
  let st = events.reducer(
    {...events.initial},
    {
      type: events.ActionType.LoadSpecificMonthLogEntries,
      month: new Date(2018, 2, 1),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.analysis_period).toBe(events.AnalysisPeriod.SpecificMonth);
  expect(st.analysis_period_month).toEqual(new Date(2018, 2, 1));
  expect(st.loading_analysis_month_log).toBe(true);
  expect(st.loading_analysis_month_jobs).toBe(false);
  expect(st.selected_month).toBe(events.initial.selected_month);
});

it("starts loading a specific month jobs for analysis", () => {
  let st = events.reducer(
    {...events.initial},
    {
      type: events.ActionType.LoadSpecificMonthJobHistory,
      month: new Date(2018, 2, 1),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.loading_analysis_month_log).toBe(false);
  expect(st.loading_analysis_month_jobs).toBe(true);
});

it("loads 30 days for analysis", () => {
  const cycles = stationCycles.process_events(
    {type: stationCycles.ExpireOldDataType.NoExpire},
    fakeCycle(new Date(), 3),
    stationCycles.initial);

  let st = events.reducer(
    {...events.initial,
      analysis_period: events.AnalysisPeriod.SpecificMonth,
      selected_month: {cycles}
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
  const todayCycle = fakeCycle(now, 30, "partAAA", 1, 'palss');
  var twoDaysAgo = addDays(now, -2);
  const twoDaysAgoCycle = fakeCycle(twoDaysAgo, 24, "partBBB", 2, 'paltt');
  var twentySevenDaysAgo = addDays(now, -27);
  const twentySevenCycle = fakeCycle(twentySevenDaysAgo, 18, "partCCC", 3, 'paltt');

  let st = events.reducer(
    {...events.initial,
      loading_analysis_month_log: true,
      analysis_period_month: new Date(2018, 1, 1),
      analysis_period: events.AnalysisPeriod.SpecificMonth
    },
    {
      type: events.ActionType.LoadSpecificMonthLogEntries,
      month: new Date(2018, 1, 1),
      pledge: {
        status: PledgeStatus.Completed,
        result: twentySevenCycle.concat(twoDaysAgoCycle, todayCycle)
      }
    }
  );
  expect(st.analysis_period).toBe(events.AnalysisPeriod.SpecificMonth);
  expect(st.loading_analysis_month_log).toBe(false);
  expect(st.analysis_period_month).toEqual(new Date(2018, 1, 1));
  expect(st.selected_month).toMatchSnapshot('selected month with 27 days ago, 2 days ago, and today');
});

it("bins actual cycles by day", () => {
  const now = new Date(2018, 2, 5);

  const evts = ([] as ILogEntry[])
    .concat(
      fakeCycle(now, 30),
      fakeCycle(addHours(now, -3), 20),
      fakeCycle(addHours(now, -15), 15),
    );
  const st = events.reducer(
    events.initial,
    {
      type: events.ActionType.LoadRecentLogEntries,
      now: addDays(now, 1),
      pledge: {
        status: PledgeStatus.Completed,
        result: evts
      }
    });

  const byDay = events.binCyclesByDay(
    st.last30.cycles.by_part_then_stat,
    c => duration(c.active).asMinutes()
  );

  expect(byDay).toMatchSnapshot("cycles binned by day and station");
});