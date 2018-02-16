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

import * as React from 'react';
import { shallow } from 'enzyme';

import { jobsToPoints, CurrentJobs } from './CurrentJobs';
import * as api from '../data/api';

it('renders the current jobs', () => {
  const completedData = [
    {x: 10, y: 1, part: 'abc'},
    {x: 11, y: 2, part: 'def'},
    {x: 16, y: 3, part: 'zzz'}
  ];
  const planData = [
    {x: 16, y: 1},
    {x: 17, y: 2},
    {x: 22, y: 3}
  ];
  const val = shallow(
    <CurrentJobs completedData={completedData} planData={planData}/>);
  expect(val).toMatchSnapshot('current job graph');
});

it('converts events to points', () => {
  const dummyHold: api.IJobHoldPattern = {
    userHold: false,
    reasonForUserHold: '',
    holdUnholdPattern: [],
    holdUnholdPatternStartUTC: new Date(),
    holdUnholdPatternRepeats: false,
  };
  const jobs: api.IInProcessJob[] = [
    {
      routeStartUTC: new Date(),
      routeEndUTC: new Date(),
      archived: false,
      copiedToSystem: true,
      partName: "part1",
      unique: "uniq1",
      priority: 10,
      manuallyCreated: false,
      createMarkingData: true,
      holdEntireJob: new api.JobHoldPattern(dummyHold),
      cyclesOnFirstProcess: [40, 42],
      completed: [[20, 21], [22]],
      procsAndPaths: [
        new api.ProcessInfo({
          paths: [
            new api.ProcPathInfo({
              pathGroup: 1,
              pallets: ["pal1"],
              load: [1],
              unload: [2],
              stops: [
                new api.JobMachiningStop({
                  stations: { "1": "progabc" },
                  tools: {},
                  stationGroup: "MC",
                  expectedCycleTime: "01:15:00",
                })
              ],
              simulatedStartingUTC: new Date(),
              simulatedAverageFlowTime: "",
              holdMachining: new api.JobHoldPattern(dummyHold),
              holdLoadUnload: new api.JobHoldPattern(dummyHold),
              partsPerPallet: 1,
            }),
            new api.ProcPathInfo({
              pathGroup: 1,
              pallets: ["pal2"],
              load: [1],
              unload: [2],
              stops: [
                new api.JobMachiningStop({
                  stations: { "1": "progabc" },
                  tools: {},
                  stationGroup: "MC",
                  expectedCycleTime: "01:15:00",
                })
              ],
              simulatedStartingUTC: new Date(),
              simulatedAverageFlowTime: "",
              holdMachining: new api.JobHoldPattern(dummyHold),
              holdLoadUnload: new api.JobHoldPattern(dummyHold),
              partsPerPallet: 1,
            })
          ]
        }),
        new api.ProcessInfo({
          paths: [
            new api.ProcPathInfo({
              pathGroup: 1,
              pallets: ["pal1"],
              stops: [
                new api.JobMachiningStop({
                  stations: { "1": "progabc" },
                  tools: {},
                  stationGroup: "MC",
                  expectedCycleTime: "00:45:00",
                })
              ],
              load: [1],
              unload: [2],
              simulatedStartingUTC: new Date(),
              simulatedAverageFlowTime: "",
              holdMachining: new api.JobHoldPattern(dummyHold),
              holdLoadUnload: new api.JobHoldPattern(dummyHold),
              partsPerPallet: 1,
            })
          ]
        })
      ]
    }
  ];
  const points = jobsToPoints(jobs);
  expect(points).toMatchSnapshot('points for sample jobs');
});