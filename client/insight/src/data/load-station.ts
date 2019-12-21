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

import * as api from "./api";
import { HashMap, Vector } from "prelude-ts";

export interface PalletData {
  pallet: api.IPalletStatus;
  material: api.IInProcessMaterial[];
}

export function buildPallets(
  st: Readonly<api.ICurrentStatus>
): HashMap<string, { pal?: PalletData; queued?: PalletData }> {
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
            material: matByPallet.get(pal.pallet) || []
          }
        });
        break;
      }

      case api.PalletLocationEnum.MachineQueue: {
        const stat2 = pal.currentPalletLocation.group + " #" + pal.currentPalletLocation.num.toString();
        m.set(stat2, {
          ...(m.get(stat2) || {}),
          queued: {
            pallet: pal,
            material: matByPallet.get(pal.pallet) || []
          }
        });
        break;
      }

      // TODO: buffer and cart
    }
  }

  return HashMap.ofIterable(m);
}

export type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

export interface LoadStationAndQueueData {
  readonly loadNum: number;
  readonly pallet?: Readonly<api.IPalletStatus>;
  readonly face: HashMap<number, MaterialList>;
  readonly stationStatus?: HashMap<string, { pal?: PalletData; queued?: PalletData }>;
  readonly castings: MaterialList;
  readonly free?: MaterialList;
  readonly queues: HashMap<string, MaterialList>;
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

  let byFace: HashMap<number, api.IInProcessMaterial[]> = HashMap.empty();
  let palName: string | undefined;
  let castings: ReadonlyArray<Readonly<api.IInProcessMaterial>> = [];
  let stationStatus: HashMap<string, { pal?: PalletData; queued?: PalletData }> | undefined;

  // first free and queued material
  let free: MaterialList | undefined;
  if (displayFree) {
    free = curSt.material.filter(
      m => m.action.processAfterLoad && m.action.processAfterLoad > 1 && m.location.type === api.LocType.Free
    );
  }

  const queueNames = HashMap.ofIterable<string, api.IInProcessMaterial[]>(
    queues.map(q => [q, []] as [string, api.IInProcessMaterial[]])
  );
  const queueMat = Vector.ofIterable(curSt.material)
    .filter(m => {
      if (m.location.type !== api.LocType.InQueue) {
        return false;
      }
      if (queueNames.containsKey(m.location.currentQueue || "")) {
        return true;
      } else if (pal && m.action.type === api.ActionType.Loading && m.action.loadOntoPallet === pal.pallet) {
        return true; // display even if queue not selected
      }
      return false;
    })
    .groupBy(m => m.location.currentQueue || "")
    .mapValues(ms => ms.sortOn(m => m.location.queuePosition || 0).toArray());

  // load pallet material
  if (pal !== undefined) {
    palName = pal.pallet;

    byFace = Vector.ofIterable(curSt.material)
      .filter(m => m.location.type === api.LocType.OnPallet && m.location.pallet === palName)
      .groupBy(m => m.location.face || 0)
      .mapValues(ms => ms.toArray());

    castings = curSt.material.filter(
      m =>
        m.action.type === api.ActionType.Loading &&
        m.action.loadOntoPallet === palName &&
        m.action.processAfterLoad === 1 &&
        m.location.type === api.LocType.Free
    );

    // make sure all face desinations exist
    queueMat.forEach(([_, mats]) =>
      mats.forEach(m => {
        if (m.action.type === api.ActionType.Loading && m.action.loadOntoPallet === palName) {
          const face = m.action.loadOntoFace;
          if (face !== undefined && !byFace.containsKey(face)) {
            byFace = byFace.put(face, []);
          }
        }
      })
    );
    [...castings, ...(free || [])].forEach(m => {
      if (m.action.type === api.ActionType.Loading && m.action.loadOntoPallet === palName) {
        const face = m.action.loadOntoFace;
        if (face !== undefined && !byFace.containsKey(face)) {
          byFace = byFace.put(face, []);
        }
      }
    });
  } else {
    stationStatus = buildPallets(curSt);
    if (displayFree) {
      castings = curSt.material.filter(m => m.action.processAfterLoad === 1 && m.location.type === api.LocType.Free);
    }
  }

  return {
    loadNum,
    pallet: pal,
    face: byFace,
    stationStatus: stationStatus,
    castings: castings,
    free: free,
    queues: queueNames.mergeWith(queueMat, (m1, m2) => [...m1, ...m2])
  };
}
