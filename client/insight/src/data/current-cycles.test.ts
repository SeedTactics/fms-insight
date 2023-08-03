/* Copyright (c) 2022, John Lenz

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

import { PartAndStationOperation, StatisticalCycleTime } from "../cell-status/estimated-cycle-times.js";
import { it, expect } from "vitest";
import { HashMap } from "@seedtactics/immutable-collections";
import { currentCycles } from "./current-cycles.js";
import {
  ActionType,
  ActiveJob,
  ICurrentStatus,
  InProcessMaterial,
  InProcessMaterialAction,
  InProcessMaterialLocation,
  LocType,
  PalletLocation,
  PalletLocationEnum,
  PalletStatus,
  ProcessInfo,
  ProcPathInfo,
} from "../network/api.js";

function fakePalAtMachine(pal: number, num: number): PalletStatus {
  return new PalletStatus({
    palletNum: pal,
    fixtureOnPallet: "",
    onHold: false,
    currentPalletLocation: new PalletLocation({
      loc: PalletLocationEnum.Machine,
      group: "MC",
      num,
    }),
    numFaces: 1,
  });
}

function fakePalAtLoad(pal: number, num: number): PalletStatus {
  return new PalletStatus({
    palletNum: pal,
    fixtureOnPallet: "",
    onHold: false,
    currentPalletLocation: new PalletLocation({
      loc: PalletLocationEnum.LoadUnload,
      group: "L/U",
      num,
    }),
    numFaces: 1,
  });
}

function fakeMC({
  part,
  proc,
  path,
  pal,
  prog,
  elapsedMin,
  remainMin,
}: {
  part: string;
  proc: number;
  path?: number;
  pal: number;
  prog: string;
  elapsedMin: number;
  remainMin: number;
}): InProcessMaterial {
  return new InProcessMaterial({
    materialID: 100,
    jobUnique: "unused-job-unique",
    partName: part,
    process: proc,
    path: path ?? 1,
    signaledInspections: [],
    action: new InProcessMaterialAction({
      type: ActionType.Machining,
      program: prog,
      elapsedMachiningTime: `PT${elapsedMin}M`,
      expectedRemainingMachiningTime: `PT${remainMin}M`,
    }),
    location: new InProcessMaterialLocation({
      type: LocType.OnPallet,
      palletNum: pal,
    }),
  });
}

function fakeLoad({
  uniq,
  part,
  proc,
  path,
  ty,
  pal,
  elapsedMin,
}: {
  uniq: string;
  part: string;
  proc: number;
  path?: number;
  ty: "load" | "unload" | "completed";
  pal: number;
  elapsedMin: number;
}): InProcessMaterial {
  return new InProcessMaterial({
    materialID: 100,
    jobUnique: uniq,
    partName: part,
    process: ty === "load" ? 123094345 : proc,
    path: ty === "load" ? 1239829345 : path ?? 1,
    signaledInspections: [],
    action: new InProcessMaterialAction({
      type:
        ty === "load"
          ? ActionType.Loading
          : ty === "unload"
          ? ActionType.UnloadToInProcess
          : ActionType.UnloadToCompletedMaterial,
      elapsedLoadUnloadTime: `PT${elapsedMin}M`,
      loadOntoPalletNum: ty === "load" ? pal : undefined,
      processAfterLoad: ty === "load" ? proc : undefined,
      pathAfterLoad: ty === "load" ? path ?? 1 : undefined,
    }),
    location:
      ty === "load"
        ? new InProcessMaterialLocation({
            type: LocType.Free,
          })
        : new InProcessMaterialLocation({
            type: LocType.OnPallet,
            palletNum: pal,
          }),
  });
}

it("calculates current cycles", () => {
  const now = new Date(Date.UTC(2018, 2, 5, 7, 0, 0));

  const curSt: ICurrentStatus = {
    timeOfCurrentStatusUTC: now,
    jobs: {},
    pallets: {
      1: fakePalAtLoad(1, 10),
      2: fakePalAtLoad(2, 20),
      3: fakePalAtMachine(3, 6),
      4: fakePalAtMachine(4, 2),
      5: fakePalAtMachine(5, 1),
      6: fakePalAtLoad(6, 30),
    },
    material: [
      // one cycle just getting started
      fakeMC({ part: "part1", proc: 1, pal: 3, prog: "abcd", elapsedMin: 10, remainMin: 18 }),
      fakeMC({ part: "part1", proc: 1, pal: 3, prog: "abcd", elapsedMin: 10, remainMin: 18 }),
      fakeMC({ part: "part1", proc: 1, pal: 3, prog: "abcd", elapsedMin: 10, remainMin: 18 }),

      // one cycle just passed completed
      fakeMC({ part: "part1", proc: 1, pal: 4, prog: "abcd", elapsedMin: 30, remainMin: -2 }),
      fakeMC({ part: "part1", proc: 1, pal: 4, prog: "abcd", elapsedMin: 30, remainMin: -2 }),
      fakeMC({ part: "part1", proc: 1, pal: 4, prog: "abcd", elapsedMin: 30, remainMin: -2 }),

      // one cycle long passed completed
      fakeMC({ part: "part1", proc: 1, pal: 5, prog: "abcd", elapsedMin: 50, remainMin: -22 }),
      fakeMC({ part: "part1", proc: 1, pal: 5, prog: "abcd", elapsedMin: 50, remainMin: -22 }),
      fakeMC({ part: "part1", proc: 1, pal: 5, prog: "abcd", elapsedMin: 50, remainMin: -22 }),

      // loading but very short
      fakeLoad({ uniq: "uniq3", part: "part3", proc: 1, pal: 1, ty: "load", elapsedMin: 10 }),
      fakeLoad({ uniq: "uniq3", part: "part3", proc: 1, pal: 1, ty: "load", elapsedMin: 10 }),
      fakeLoad({ uniq: "uniq4", part: "part4", proc: 2, pal: 1, ty: "unload", elapsedMin: 10 }),
      fakeLoad({ uniq: "uniq4", part: "part4", proc: 2, pal: 1, ty: "completed", elapsedMin: 10 }),

      // loading but at the expected time
      fakeLoad({ uniq: "uniq3", part: "part3", proc: 1, pal: 2, ty: "load", elapsedMin: 20 }),
      fakeLoad({ uniq: "uniq3", part: "part3", proc: 1, pal: 2, ty: "load", elapsedMin: 20 }),
      fakeLoad({ uniq: "uniq4", part: "part4", proc: 2, pal: 2, ty: "unload", elapsedMin: 20 }),
      fakeLoad({ uniq: "uniq4", part: "part4", proc: 2, pal: 2, ty: "completed", elapsedMin: 20 }),

      // loading far past the expected time
      fakeLoad({ uniq: "uniq3", part: "part3", proc: 1, pal: 6, ty: "load", elapsedMin: 50 }),
      fakeLoad({ uniq: "uniq3", part: "part3", proc: 1, pal: 6, ty: "load", elapsedMin: 50 }),
      fakeLoad({ uniq: "uniq4", part: "part4", proc: 2, pal: 6, ty: "unload", elapsedMin: 50 }),
      fakeLoad({ uniq: "uniq4", part: "part4", proc: 2, pal: 6, ty: "completed", elapsedMin: 50 }),
    ],
    alarms: [],
    queues: {},
  };

  expect(currentCycles(curSt, HashMap.empty())).toMatchSnapshot("current cycles without jobs or expected");

  const expected = HashMap.from<PartAndStationOperation, StatisticalCycleTime>([
    [
      new PartAndStationOperation("part1", "MC", "abcd"),
      {
        medianMinutesForSingleMat: 28 / 3, // 3 parts per face
        MAD_aboveMinutes: 1.2,
        MAD_belowMinutes: 1.2,
        expectedCycleMinutesForSingleMat: 28,
      },
    ],
    [
      new PartAndStationOperation("part3", "L/U", "LOAD-1"),
      {
        medianMinutesForSingleMat: 8 / 2, // 2 parts per face
        MAD_aboveMinutes: 1,
        MAD_belowMinutes: 1,
        expectedCycleMinutesForSingleMat: 8 / 2,
      },
    ],
    [
      new PartAndStationOperation("part4", "L/U", "UNLOAD-2"),
      {
        medianMinutesForSingleMat: 11 / 2, // two parts per face
        MAD_aboveMinutes: 1,
        MAD_belowMinutes: 1,
        expectedCycleMinutesForSingleMat: 11 / 2,
      },
    ],
  ]);

  expect(currentCycles(curSt, expected)).toMatchSnapshot("current cycles without jobs, just expected");

  const jobs = {
    // uniq3 only has loading on proc1
    uniq3: new ActiveJob({
      unique: "uniq3",
      partName: "part3",
      routeStartUTC: new Date(),
      routeEndUTC: new Date(),
      archived: false,
      copiedToSystem: true,
      procsAndPaths: [new ProcessInfo({ paths: [{ expectedLoadTime: "PT4.5M" } as ProcPathInfo] })],
    }),
    // uniq4 is unloading on proc2
    uniq4: new ActiveJob({
      unique: "uniq4",
      partName: "part4",
      routeStartUTC: new Date(),
      routeEndUTC: new Date(),
      archived: false,
      copiedToSystem: true,
      procsAndPaths: [
        {} as ProcessInfo,
        new ProcessInfo({ paths: [{ expectedUnloadTime: "PT6M" } as ProcPathInfo] }),
      ],
    }),
  };

  expect(currentCycles({ ...curSt, jobs }, expected)).toMatchSnapshot(
    "current cycles with jobs and expected",
  );
});
