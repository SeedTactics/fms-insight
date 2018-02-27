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

import { LogMaterial, ILogEntry, LogType } from './api';
import * as faker from 'faker';
import { duration } from 'moment';
import { addSeconds, addMinutes } from 'date-fns';

export function fakeMaterial(part?: string, proc?: number): LogMaterial {
  return new LogMaterial({
    id: faker.random.number(),
    uniq: 'uniq' + faker.random.alphaNumeric(),
    part: part || 'part' + faker.random.alphaNumeric(),
    proc: proc || faker.random.number({max: 4}),
    numproc: faker.random.number({max: 4}),
    face: 'face' + faker.random.alphaNumeric()
  });
}

function addStartAndEnd(es: ILogEntry[], e: ILogEntry): void {
  let elapsed = duration(e.elapsed);
  let startTime = addSeconds(e.endUTC, -elapsed.asSeconds());
  let start = {...e,
    counter: e.counter - 1,
    startofcycle: true,
    endUTC: startTime,
    result: '',
    program: ''
  };
  es.push(start);
  es.push(e);
}

export function fakeCycle(
    time: Date, machineTime: number, part?: string, proc?: number, pallet?: string
  ): ReadonlyArray<ILogEntry> {
    const pal = pallet || 'pal' + faker.random.alphaNumeric();
    const material = [fakeMaterial(part, proc)];

    let counter = 1;
    time = addMinutes(time, 5);

    let es: ILogEntry[] = [];

    addStartAndEnd(
      es,
      {counter, material, pal,
        type: LogType.LoadUnloadCycle,
        startofcycle: false,
        endUTC: time,
        loc: 'L/U',
        locnum: 1,
        result: 'LOAD',
        program: 'LOAD',
        endofroute: false,
        elapsed: '00:06:00',
        active: '00:06:00'
      }
    );

    counter += 2;
    time = addMinutes(time, machineTime + 3);

    const elapsed = '00:' + machineTime.toString() + ':00';
    addStartAndEnd(
      es,
      {
        counter, material, pal,
        type: LogType.MachineCycle,
        startofcycle: false,
        endUTC: time,
        loc: 'MC',
        locnum: 1,
        result: '',
        program: 'prog' + faker.random.alphaNumeric(),
        endofroute: false,
        elapsed: elapsed,
        active: elapsed
      }
    );

    counter += 2;
    time = addMinutes(time, 10);

    addStartAndEnd(
      es,
      {counter, material, pal,
        type: LogType.LoadUnloadCycle,
        startofcycle: false,
        endUTC: time,
        loc: 'L/U',
        locnum: 2,
        result: 'UNLOAD',
        program: 'UNLOAD',
        endofroute: true,
        elapsed: '00:03:00',
        active: '00:03:00'
      }
    );

    es.push(
      {
        counter: counter + 1,
        pal,
        material: [],
        type: LogType.PalletCycle,
        startofcycle: false,
        endUTC: time,
        loc: 'L/U',
        locnum: 2,
        result: 'PalletCycle',
        program: '',
        endofroute: false,
        elapsed: '00:44:00',
        active: '-00:01:00',
      }
    );

  return es;
}