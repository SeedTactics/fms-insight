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

import * as im from 'immutable';
import * as api from './api';

export type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

export interface LoadStationAndQueueData {
  readonly loadNum: number;
  readonly pallet?: Readonly<api.IPalletStatus>;
  readonly face: im.Map<number, MaterialList>;
  readonly castings: MaterialList;
  readonly free?: MaterialList;
  readonly queues: im.Map<string, MaterialList>;
}

export function selectLoadStationAndQueueProps(
    loadNum: number,
    queues: ReadonlyArray<string>,
    displayFree: boolean,
    curSt: Readonly<api.ICurrentStatus>
  ): LoadStationAndQueueData {

  let pal: Readonly<api.IPalletStatus> | undefined;
  if (loadNum >= 0) {
    for (let p of Object.values(curSt.pallets)) {
      if (p.currentPalletLocation.loc === api.PalletLocationEnum.LoadUnload
          && p.currentPalletLocation.num === loadNum) {
        pal = p;
        break;
      }
    }
  }

  let byFace: im.Map<number, api.IInProcessMaterial[]> = im.Map();
  let palName: string | undefined;
  let castings: im.Seq.Indexed<api.IInProcessMaterial> = im.Seq.Indexed();

  // load pallet material
  if (pal !== undefined) {
    palName = pal.pallet;

    byFace = im.Seq(curSt.material)
      .filter(m => m.location.type === api.LocType.OnPallet
                && m.location.pallet === palName
      )
      .groupBy(m => m.location.face || 0)
      .map(ms => ms.valueSeq().toArray())
      .toMap();

    // add missing blank faces
    const maxFace = pal.numFaces >= 1 ? pal.numFaces : 1;
    for (let face = 1; face <= maxFace; face++) {
      if (!byFace.has(face)) {
        byFace = byFace.set(face, []);
      }
    }

    castings = im.Seq(curSt.material)
      .filter(m => m.action.type === api.ActionType.Loading
                && m.action.loadOntoPallet === palName
                && m.action.processAfterLoad === 1
                && m.location.type === api.LocType.Free);
  } else if (displayFree) {
    castings = im.Seq(curSt.material)
      .filter(m => m.action.processAfterLoad === 1
                && m.location.type === api.LocType.Free);
  }

  // now free and queued material
  let free: MaterialList | undefined;
  if (displayFree) {
    free = im.Seq(curSt.material)
      .filter(m => m.action.processAfterLoad &&
                   m.action.processAfterLoad > 1
                && m.location.type === api.LocType.Free)
      .toArray();
  }

  const queueNames = im.Map<string, api.IInProcessMaterial[]>(
    queues.map(q => [q, []] as [string, api.IInProcessMaterial[]]));
  const queueMat = im.Seq(curSt.material)
    .filter(m => m.location.type === api.LocType.InQueue
              && queueNames.has(m.location.currentQueue || "")
    )
    .groupBy(m => m.location.currentQueue || "")
    .map(ms => ms.valueSeq().toArray())
    .toMap();

  return {
    loadNum,
    pallet: pal,
    face: byFace,
    castings: castings.toArray(),
    free: free,
    queues: queueNames.merge(queueMat),
  };
}