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
import * as faker from 'faker';
import * as moment from 'moment';
import { ILogMaterial, ILogEntry, LogType } from './api';
import { PledgeStatus } from './pledge';

// tslint:disable no-any

it('creates initial state', () => {
  let s = events.reducer(undefined as any, undefined as any);
  expect(s).toBe(events.initial);
});

it('sets the system hours', () => {
  let s = events.reducer(events.initial, {
    type: events.ActionType.SetSystemHours,
    hours: 5
  });
  expect(s.system_active_hours_per_week).toBe(5);
});

function fakeMaterial(): ILogMaterial {
  return {
    id: faker.random.number(),
    uniq: 'uniq' + faker.random.alphaNumeric(),
    part: 'part' + faker.random.alphaNumeric(),
    proc: faker.random.number({max: 4}),
    numproc: faker.random.number({max: 4}),
    face: 'face' + faker.random.alphaNumeric()
  };
}

function addStartAndEnd(es: ILogEntry[], e: ILogEntry): void {
  let elapsed = moment.duration(e.elapsed);
  let startTime = moment.utc(e.endUTC).subtract(elapsed).toDate();
  let start = {...e,
    counter: e.counter - 1,
    startofcycle: false,
    endUTC: startTime,
    result: '',
    program: ''
  };
  es.push(start);
  es.push(e);
}

function fakeCycle(): ReadonlyArray<ILogEntry> {
  const pal = 'pal' + faker.random.alphaNumeric();
  const material = [fakeMaterial()];

  let counter = 1;
  let time = moment.utc();

  let es: ILogEntry[] = [];

  addStartAndEnd(
    es,
    {counter, material, pal,
      type: LogType.LoadUnloadCycle,
      startofcycle: false,
      endUTC: time.toDate(),
      loc: 'L/U',
      locnum: 1,
      result: 'LOAD',
      program: 'LOAD',
      endofroute: false,
      elapsed: '00:05:00',
      active: '00:05:00'
    }
  );

  counter += 2;
  time.add(30, 'minutes');

  addStartAndEnd(
    es,
    {
      counter, material, pal,
      type: LogType.MachineCycle,
      startofcycle: false,
      endUTC: time.toDate(),
      loc: 'MC',
      locnum: 1,
      result: '',
      program: 'prog' + faker.random.alphaNumeric(),
      endofroute: false,
      elapsed: '00:22:00',
      active: '00:22:00'
    }
  );

  counter += 2;
  time.add(10, 'minutes');

  addStartAndEnd(
    es,
    {counter, material, pal,
      type: LogType.LoadUnloadCycle,
      startofcycle: false,
      endUTC: time.toDate(),
      loc: 'L/U',
      locnum: 1,
      result: 'UNLOAD',
      program: 'UNLOAD',
      endofroute: true,
      elapsed: '00:05:00',
      active: '00:05:00'
    }
  );

  return es;
}

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

it('adds new log entries', () => {
  const entries = fakeCycle();
  let st = events.reducer(
    events.initial,
    {
      type: events.ActionType.RequestLastWeek,
      now: new Date(),
      pledge: {
        status: PledgeStatus.Completed,
        result: entries
      }
    });

  expect(st.last_week_of_events).toEqual(entries);
  // TODO: test hours calc
});