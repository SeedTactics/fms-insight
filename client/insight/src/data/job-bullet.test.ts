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

import { jobsToPoints } from "./job-bullet.js";
import * as api from "../network/api.js";
import { it, expect } from "vitest";

it("converts events to points", () => {
  const dummyHold: api.IHoldPattern = {
    userHold: false,
    reasonForUserHold: "",
    holdUnholdPattern: [],
    holdUnholdPatternStartUTC: new Date(),
    holdUnholdPatternRepeats: false,
  };
  const jobs: api.IActiveJob[] = [
    {
      routeStartUTC: new Date(),
      routeEndUTC: new Date(),
      archived: false,
      copiedToSystem: true,
      partName: "part1",
      unique: "uniq1",
      manuallyCreated: false,
      holdEntireJob: new api.HoldPattern(dummyHold),
      cycles: 82,
      completed: [[20, 21], [22]],
      procsAndPaths: [
        new api.ProcessInfo({
          paths: [
            new api.ProcPathInfo({
              pallets: ["pal1"],
              load: [1],
              unload: [2],
              stops: [
                new api.MachiningStop({
                  stationNums: [1],
                  program: "progabc",
                  tools: {},
                  stationGroup: "MC",
                  expectedCycleTime: "PT1H15M",
                }),
              ],
              expectedLoadTime: "PT5M",
              expectedUnloadTime: "PT4M",
              simulatedStartingUTC: new Date(),
              simulatedAverageFlowTime: "",
              holdMachining: new api.HoldPattern(dummyHold),
              holdLoadUnload: new api.HoldPattern(dummyHold),
              partsPerPallet: 1,
            }),
            new api.ProcPathInfo({
              pallets: ["pal2"],
              load: [1],
              unload: [2],
              stops: [
                new api.MachiningStop({
                  stationNums: [1],
                  program: "progabc",
                  tools: {},
                  stationGroup: "MC",
                  expectedCycleTime: "PT1H15M",
                }),
              ],
              expectedLoadTime: "PT5M",
              expectedUnloadTime: "PT4M",
              simulatedStartingUTC: new Date(),
              simulatedAverageFlowTime: "",
              holdMachining: new api.HoldPattern(dummyHold),
              holdLoadUnload: new api.HoldPattern(dummyHold),
              partsPerPallet: 1,
            }),
          ],
        }),
        new api.ProcessInfo({
          paths: [
            new api.ProcPathInfo({
              pallets: ["pal1"],
              stops: [
                new api.MachiningStop({
                  stationNums: [1],
                  program: "progabc",
                  tools: {},
                  stationGroup: "MC",
                  expectedCycleTime: "PT45M",
                }),
              ],
              expectedLoadTime: "PT5M",
              expectedUnloadTime: "PT4M",
              load: [1],
              unload: [2],
              simulatedStartingUTC: new Date(),
              simulatedAverageFlowTime: "",
              holdMachining: new api.HoldPattern(dummyHold),
              holdLoadUnload: new api.HoldPattern(dummyHold),
              partsPerPallet: 1,
            }),
          ],
        }),
      ],
    },
  ];
  const points = jobsToPoints(jobs);
  expect(points).toMatchSnapshot("points for sample jobs");
});
