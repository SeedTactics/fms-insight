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

import * as events from './events';
import { fakeCycle } from './events.fake';
import { PledgeStatus } from './pledge';
import { addDays } from 'date-fns';

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
  expect(s.system_active_hours_per_week).toBe(5);
});

it('responds to loading', () => {
  let st = events.reducer(
    events.initial,
    {
      type: events.ActionType.RequestLastWeek,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.loading_events).toBe(true);
  expect(st.last_week_of_events).toEqual([]);
});

it('responds to error', () => {
  let st = events.reducer(
    {...events.initial, loading_events: true},
    {
      type: events.ActionType.RequestLastWeek,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Error,
        error: new Error('hello')
      }
    }
  );
  expect(st.loading_events).toBe(false);
  expect(st.loading_error).toEqual(new Error('hello'));
  expect(st.last_week_of_events).toEqual([]);
});

it('adds new log entries', () => {
  var now = new Date();
  var twoDaysAgo = addDays(now, -2);
  const twoDaysAgoCycle = fakeCycle(twoDaysAgo, 24);
  const todayCycle = fakeCycle(now, 30);
  let st = events.reducer(
    events.initial,
    {
      type: events.ActionType.RequestLastWeek,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Completed,
        result: twoDaysAgoCycle.concat(todayCycle)
      }
    });

  expect(st.last_week_of_events).toEqual(twoDaysAgoCycle.concat(todayCycle));

  // 6 minutes on L/U #1 for each cycle, so total of 12
  // 24 + 30 minutes on MC #1
  // 3 minutes on L/U #2 for each cycle, so total of 6
  expect(st.station_active_hours_past_week.toJS()).toEqual({
    'L/U #1': 12 / 60,
    'MC #1': 54 / 60,
    'L/U #2': 6 / 60
  });

  // Now add again 6 days from now, so that the twoDaysAgo cycle is removed
  var sixDays = addDays(now, 6);
  const sixDaysCycle = fakeCycle(sixDays, 12);
  st = events.reducer(
    st,
    {
      type: events.ActionType.RequestLastWeek,
      now: sixDays,
      pledge: {
        status: PledgeStatus.Completed,
        result: sixDaysCycle
      }
    });

  // twoDaysAgo should have been filtered out
  expect(st.last_week_of_events).toEqual(todayCycle.concat(sixDaysCycle));

  // 6 minutes on L/U #1 for each cycle, so total of 12
  // 30 + 12 minutes on MC #1
  // 3 minutes on L/U #2 for each cycle, so total of 6
  expect(st.station_active_hours_past_week.toJS()).toEqual({
    'L/U #1': 12 / 60,
    'MC #1': 42 / 60,
    'L/U #2': 6 / 60
  });
});