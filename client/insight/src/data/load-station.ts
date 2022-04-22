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

import * as api from "../network/api";
import { LazySeq, sortByProp } from "../util/lazyseq";

export interface PalletData {
  pallet: api.IPalletStatus;
  material: api.IInProcessMaterial[];
}

export function buildPallets(
  st: Readonly<api.ICurrentStatus>
): ReadonlyMap<string, { pal?: PalletData; queued?: PalletData }> {
  const matByPallet = new Map<string, api.IInProcessMaterial[]>();
  for (const mat of st.material) {
    if (mat.location.type === api.LocType.OnPallet && mat.location.pallet !== undefined) {
      const mats = matByPallet.get(mat.location.pallet) || [];
      mats.push(mat);
      matByPallet.set(mat.location.pallet, mats);
    }
  }

  const m = new Map<string, { pal?: PalletData; queued?: PalletData }>();
  for (const pal of Object.values(st.pallets)) {
    switch (pal.currentPalletLocation.loc) {
      case api.PalletLocationEnum.LoadUnload:
      case api.PalletLocationEnum.Machine: {
        const stat = pal.currentPalletLocation.group + " #" + pal.currentPalletLocation.num.toString();
        m.set(stat, {
          ...(m.get(stat) || {}),
          pal: {
            pallet: pal,
            material: matByPallet.get(pal.pallet) || [],
          },
        });
        break;
      }

      case api.PalletLocationEnum.MachineQueue: {
        const stat2 = pal.currentPalletLocation.group + " #" + pal.currentPalletLocation.num.toString();
        m.set(stat2, {
          ...(m.get(stat2) || {}),
          queued: {
            pallet: pal,
            material: matByPallet.get(pal.pallet) || [],
          },
        });
        break;
      }

      // TODO: buffer and cart
    }
  }

  return m;
}

export type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

export interface LoadStationAndQueueData {
  readonly loadNum: number;
  readonly pallet?: Readonly<api.IPalletStatus>;
  readonly face: ReadonlyMap<number, MaterialList>;
  readonly stationStatus?: ReadonlyMap<string, { pal?: PalletData; queued?: PalletData }>;
  readonly freeLoadingMaterial: MaterialList;
  readonly free?: MaterialList;
  readonly queues: ReadonlyMap<string, MaterialList>;
  readonly allJobsHaveRawMatQueue: boolean;
}

export function selectLoadStationAndQueueProps(
  loadNum: number,
  queues: ReadonlyArray<string>,
  displayFree: boolean,
  curSt: Readonly<api.ICurrentStatus>
): LoadStationAndQueueData {
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

  const byFace = new Map<number, Array<api.IInProcessMaterial>>();
  let palName: string | undefined;
  let freeLoading: ReadonlyArray<Readonly<api.IInProcessMaterial>> = [];
  let stationStatus: ReadonlyMap<string, { pal?: PalletData; queued?: PalletData }> | undefined;

  // first free and queued material
  let free: MaterialList | undefined;
  if (displayFree) {
    free = curSt.material.filter(
      (m) => m.action.processAfterLoad && m.action.processAfterLoad > 1 && m.location.type === api.LocType.Free
    );
  }

  const queueMat = new Map<string, Array<api.IInProcessMaterial>>(queues.map((q) => [q, []]));
  for (const m of curSt.material) {
    if (m.location.type !== api.LocType.InQueue) {
      continue;
    }
    const queue = m.location.currentQueue ?? "";
    if (
      queueMat.has(queue) ||
      (pal !== undefined && m.action.type === api.ActionType.Loading && m.action.loadOntoPallet === pal.pallet) // show even if not listed as a queue
    ) {
      const old = queueMat.get(queue);
      if (old === undefined) {
        queueMat.set(queue, [m]);
      } else {
        old.push(m);
      }
    }
  }

  // load pallet material
  if (pal !== undefined) {
    palName = pal.pallet;

    for (const m of curSt.material) {
      if (m.location.type === api.LocType.OnPallet && m.location.pallet === palName) {
        const face = byFace.get(m.location.face ?? 0);
        if (face !== undefined) {
          face.push(m);
        } else {
          byFace.set(m.location.face ?? 0, [m]);
        }
      }
    }

    freeLoading = curSt.material.filter(
      (m) =>
        m.action.type === api.ActionType.Loading &&
        m.action.loadOntoPallet === palName &&
        m.action.processAfterLoad === 1 &&
        m.location.type === api.LocType.Free
    );

    // make sure all face desinations exist
    queueMat.forEach((mats) =>
      mats.forEach((m) => {
        if (m.action.type === api.ActionType.Loading && m.action.loadOntoPallet === palName) {
          const face = m.action.loadOntoFace;
          if (face !== undefined && !byFace.has(face)) {
            byFace.set(face, []);
          }
        }
      })
    );
    [...freeLoading, ...(free || [])].forEach((m) => {
      if (m.action.type === api.ActionType.Loading && m.action.loadOntoPallet === palName) {
        const face = m.action.loadOntoFace;
        if (face !== undefined && !byFace.has(face)) {
          byFace.set(face, []);
        }
      }
    });
  } else {
    stationStatus = buildPallets(curSt);
    if (displayFree) {
      freeLoading = curSt.material.filter(
        (m) => m.action.processAfterLoad === 1 && m.location.type === api.LocType.Free
      );
    }
  }

  const allJobsHaveRawMatQueue = LazySeq.ofObject(curSt.jobs ?? {})
    .flatMap(([_, j]) => j.procsAndPaths[0]?.paths ?? [])
    .allMatch((p) => !!p.inputQueue && p.inputQueue !== "");

  byFace.forEach((face) => face.sort(sortByProp((m) => m.location.queuePosition ?? 0)));
  queueMat.forEach((mats) => mats.sort(sortByProp((m) => m.location.queuePosition ?? 0)));

  return {
    loadNum,
    pallet: pal,
    face: byFace,
    stationStatus: stationStatus,
    freeLoadingMaterial: freeLoading,
    free: free,
    queues: queueMat,
    allJobsHaveRawMatQueue,
  };
}
