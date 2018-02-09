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
    {...events.initial, loading_error: new Error('hello')},
    {
      type: events.ActionType.RequestLastWeek,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Starting
      }
    }
  );
  expect(st.loading_events).toBe(true);
  expect(st.loading_error).toBeUndefined();
  expect(st.last_week_of_hours.isEmpty()).toBe(true);
  expect(st.last_30_days_of_events.isEmpty()).toBe(true);
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
  expect(st.last_week_of_hours.isEmpty()).toBe(true);
  expect(st.last_30_days_of_events.isEmpty()).toBe(true);
});

it('adds new log entries', () => {
  var now = new Date();
  const todayCycle = fakeCycle(now, 30);

  var twoDaysAgo = addDays(now, -2);
  const twoDaysAgoCycle = fakeCycle(twoDaysAgo, 24);

  var twentySevenDaysAgo = addDays(now, -27);
  const twentySevenCycle = fakeCycle(twentySevenDaysAgo, 18);

  let st = events.reducer(
    events.initial,
    {
      type: events.ActionType.RequestLastWeek,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Completed,
        result: twentySevenCycle.concat(twoDaysAgoCycle, todayCycle)
      }
    });

  expect(st.last_30_days_of_events.toArray()).toEqual(
    twentySevenCycle.concat(twoDaysAgoCycle, todayCycle)
  );

  expect(st.last_week_of_hours
            .map(e => ({station: e.station, hours: e.hours})) // need to filter date for the snapshot
            .toArray()
        ).toMatchSnapshot('hours with two days ago and today');

  // Now add again 6 days from now, so that the twoDaysAgo cycle is removed from hours and twenty seven
  // is removed from all events
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
  expect(st.last_30_days_of_events.toArray()).toEqual(
    twoDaysAgoCycle.concat(todayCycle, sixDaysCycle)
  );

  expect(st.last_week_of_hours
            .map(e => ({station: e.station, hours: e.hours})) // need to filter date for the snapshot
            .toArray()
        ).toMatchSnapshot('hours with today and 6 days from now');

  // empty list should keep lists unchanged and the same object
  let newSt = events.reducer(
    st,
    {
      type: events.ActionType.RequestLastWeek,
      now: sixDays,
      pledge: {
        status: PledgeStatus.Completed,
        result: []
      }
    }
  );
  expect(newSt.last_30_days_of_events).toBe(st.last_30_days_of_events);
  expect(newSt.last_week_of_hours).toBe(st.last_week_of_hours);
});