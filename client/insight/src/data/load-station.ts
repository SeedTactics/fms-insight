/* Copyright (c) 2023, John Lenz

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

import * as api from "../network/api.js";
import { LazySeq, mkCompareByProperties } from "@seedtactics/immutable-collections";

export type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

export interface LoadStationData {
  readonly pallet?: Readonly<api.IPalletStatus>;
  readonly face: ReadonlyMap<number, MaterialList>;
  readonly freeLoadingMaterial: MaterialList;
  readonly queues: ReadonlyMap<string, MaterialList>;
  readonly elapsedLoadingTime: string | null;
}

export function selectLoadStationAndQueueProps(
  loadNum: number,
  queues: ReadonlyArray<string>,
  curSt: Readonly<api.ICurrentStatus>
): LoadStationData {
  // search for pallet
  let pal: Readonly<api.IPalletStatus> | undefined;
  if (loadNum >= 0) {
    for (const p of Object.values(curSt.pallets)) {
      if (
        p.currentPalletLocation.loc === api.PalletLocationEnum.LoadUnload &&
        p.currentPalletLocation.num === loadNum
      ) {
        pal = p;
        break;
      }
    }
  }

  // calculate queues to show, which is all configured queues and any queues with
  // material loading onto the pallet
  const queuesToShow = new Set(queues);
  if (pal !== undefined) {
    for (const m of curSt.material) {
      if (
        m.action.type === api.ActionType.Loading &&
        m.action.loadOntoPallet === pal.pallet &&
        m.location.type === api.LocType.InQueue &&
        m.location.currentQueue
      ) {
        queuesToShow.add(m.location.currentQueue);
      }
    }
  }

  const queueMat = new Map<string, Array<api.IInProcessMaterial>>(
    LazySeq.of(queuesToShow).map((q) => [q, []])
  );
  const freeLoading: Array<Readonly<api.IInProcessMaterial>> = [];
  const byFace = new Map<number, Array<api.IInProcessMaterial>>();
  let elapsedLoadingTime: string | null = null;

  // ensure all faces
  if (pal) {
    for (let i = 0; i <= pal.numFaces; i++) {
      byFace.set(i, []);
    }
  }

  for (const m of curSt.material) {
    if (pal) {
      // if loading onto pallet, set elapsed load time, ensure face exists, and set free loading
      if (m.action.type === api.ActionType.Loading && m.action.loadOntoPallet === pal.pallet) {
        if (m.action.elapsedLoadUnloadTime) {
          elapsedLoadingTime = m.action.elapsedLoadUnloadTime;
        }

        if (m.action.loadOntoFace && !byFace.has(m.action.loadOntoFace)) {
          byFace.set(m.action.loadOntoFace, []);
        }

        if (m.location.type === api.LocType.Free) {
          freeLoading.push(m);
        }
      }

      // if currently on the pallet, set elapsed load time and add it to the pallet face
      if (m.location.type === api.LocType.OnPallet && m.location.pallet === pal.pallet) {
        if (
          (m.action.type === api.ActionType.UnloadToCompletedMaterial ||
            m.action.type === api.ActionType.UnloadToInProcess) &&
          m.action.elapsedLoadUnloadTime
        ) {
          elapsedLoadingTime = m.action.elapsedLoadUnloadTime;
        }

        const face = byFace.get(m.location.face ?? 0);
        if (face !== undefined) {
          face.push(m);
        } else {
          byFace.set(m.location.face ?? 0, [m]);
        }
      }
    }

    // add all material in the configured queues
    if (
      m.location.type === api.LocType.InQueue &&
      m.location.currentQueue &&
      queueMat.has(m.location.currentQueue)
    ) {
      const old = queueMat.get(m.location.currentQueue);
      if (old === undefined) {
        queueMat.set(m.location.currentQueue, [m]);
      } else {
        old.push(m);
      }
    }
  }

  byFace.forEach((face) => face.sort(mkCompareByProperties((m) => m.location.queuePosition ?? 0)));
  queueMat.forEach((mats) => mats.sort(mkCompareByProperties((m) => m.location.queuePosition ?? 0)));

  return {
    pallet: pal,
    face: byFace,
    freeLoadingMaterial: freeLoading,
    queues: queueMat,
    elapsedLoadingTime,
  };
}
