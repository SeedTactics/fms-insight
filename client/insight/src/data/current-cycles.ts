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

import { LazySeq } from "@seedtactics/immutable-collections";
import { addSeconds } from "date-fns";
import {
  EstimatedCycleTimes,
  isOutlierAbove,
  PartAndStationOperation,
  splitElapsedTimeAmongChunk,
} from "../cell-status/estimated-cycle-times.js";
import { stat_name_and_num } from "../cell-status/station-cycles.js";
import { ActionType, ICurrentStatus, PalletLocation } from "../network/api.js";
import { durationToMinutes, durationToSeconds } from "../util/parseISODuration.js";

export type CurrentCycle = {
  readonly station: string;
  readonly start: Date;
  readonly expectedEnd: Date;
  readonly isOutlier: boolean;
  readonly parts: ReadonlyArray<{ readonly part: string; readonly oper: string }>;
};

function machiningCurrentCycles(
  currentSt: ICurrentStatus,
  estimated: EstimatedCycleTimes,
  palToLoc: ReadonlyMap<string, PalletLocation>
): LazySeq<CurrentCycle> {
  return LazySeq.of(currentSt.material)
    .collect((m) => {
      if (m.action.type !== ActionType.Machining) return null;
      if (!m.location.pallet) return null;
      const loc = palToLoc.get(m.location.pallet);
      if (!loc) return null;
      return { mat: m, loc };
    })
    .groupBy(
      ({ loc }) => loc.group,
      ({ loc }) => loc.num
    )
    .map(([[statGroup, statNum], mats]) => {
      // all mats currently machining at the same station should all have the same part and program
      const stats = estimated.get(
        new PartAndStationOperation(
          mats[0].mat.partName,
          mats[0].mat.process,
          statGroup,
          mats[0].mat.action.program ?? ""
        )
      );
      const elapsedSec = durationToSeconds(mats[0].mat.action.elapsedMachiningTime ?? "PT0S");
      const remainingSec = durationToSeconds(mats[0].mat.action.expectedRemainingMachiningTime ?? "PT0S");
      return {
        station: stat_name_and_num(statGroup, statNum),
        start: addSeconds(currentSt.timeOfCurrentStatusUTC, -elapsedSec),
        expectedEnd: addSeconds(currentSt.timeOfCurrentStatusUTC, remainingSec),
        isOutlier: stats ? isOutlierAbove(stats, elapsedSec / 60 / mats.length) : false,
        parts: [{ part: mats[0].mat.partName, oper: mats[0].mat.action.program ?? "" }],
      };
    });
}

function loadCurrentCycles(
  currentSt: ICurrentStatus,
  estimated: EstimatedCycleTimes,
  palToLoc: ReadonlyMap<string, PalletLocation>
): LazySeq<CurrentCycle> {
  return LazySeq.of(currentSt.material)
    .collect((m) => {
      if (
        m.action.type === ActionType.UnloadToCompletedMaterial ||
        m.action.type === ActionType.UnloadToInProcess
      ) {
        if (!m.location.pallet) return null;
        const loc = palToLoc.get(m.location.pallet);
        if (!loc) return null;
        return { mat: m, material: [m], proc: m.process, path: m.path, loc };
      } else if (m.action.type === ActionType.Loading) {
        if (!m.action.loadOntoPallet) return null;
        const loc = palToLoc.get(m.action.loadOntoPallet);
        if (!loc) return null;
        return {
          mat: m,
          material: [m],
          proc: m.action.processAfterLoad ?? m.process,
          path: m.action.pathAfterLoad ?? m.path,
          loc,
        };
      }
      return null;
    })
    .map((m) => {
      const job = currentSt.jobs[m.mat.jobUnique];
      const pathData = job?.procsAndPaths?.[m.proc - 1]?.paths?.[m.path - 1];
      if (m.mat.action.type === ActionType.Loading) {
        return { ...m, expectedLoadSecs: durationToSeconds(pathData?.expectedLoadTime ?? "PT0S") };
      } else {
        return { ...m, expectedLoadSecs: durationToSeconds(pathData?.expectedUnloadTime ?? "PT0S") };
      }
    })
    .groupBy(
      ({ loc }) => loc.group,
      ({ loc }) => loc.num
    )
    .map(([[statGroup, statNum], mats]) => {
      const matsWithTime = splitElapsedTimeAmongChunk(
        mats,
        (m) => durationToMinutes(m.mat.action.elapsedLoadUnloadTime ?? "PT0S"),
        (m) => m.expectedLoadSecs / 60
      );
      let outlier = false;
      let expectedSecs = 0;
      for (const { cycle: m, elapsedForSingleMaterialMinutes } of matsWithTime) {
        const stats = estimated.get(
          new PartAndStationOperation(
            m.mat.partName,
            m.proc,
            statGroup,
            m.mat.action.type === ActionType.Loading ? "LOAD" : "UNLOAD"
          )
        );
        if (stats && isOutlierAbove(stats, elapsedForSingleMaterialMinutes)) {
          outlier = true;
        }
        if (m.expectedLoadSecs > 0) {
          expectedSecs += m.expectedLoadSecs;
        } else if (stats?.expectedCycleMinutesForSingleMat) {
          expectedSecs += stats.expectedCycleMinutesForSingleMat * 60;
        }
      }
      const elapsedSec = durationToSeconds(mats[0].mat.action.elapsedLoadUnloadTime ?? "PT0S");
      return {
        station: stat_name_and_num(statGroup, statNum),
        start: addSeconds(currentSt.timeOfCurrentStatusUTC, -elapsedSec),
        expectedEnd: addSeconds(currentSt.timeOfCurrentStatusUTC, -elapsedSec + expectedSecs),
        isOutlier: outlier,
        parts: LazySeq.of(mats)
          .distinctAndSortBy(
            (m) => m.mat.partName,
            (m) => m.proc,
            (m) => (m.mat.action.type === ActionType.Loading ? "LOAD" : "UNLOAD")
          )
          .map((m) => ({
            part: m.mat.partName + "-" + m.proc.toString(),
            oper: m.mat.action.type === ActionType.Loading ? "LOAD" : "UNLOAD",
          }))
          .toRArray(),
      };
    });
}

export function currentCycles(
  currentSt: ICurrentStatus,
  estimated: EstimatedCycleTimes
): ReadonlyArray<CurrentCycle> {
  const palToLoc = LazySeq.ofObject(currentSt.pallets).buildHashMap<string, PalletLocation>(
    ([p]) => p,
    (_old, [, pal]) => pal.currentPalletLocation
  );

  return machiningCurrentCycles(currentSt, estimated, palToLoc)
    .concat(loadCurrentCycles(currentSt, estimated, palToLoc))
    .toRArray();
}
