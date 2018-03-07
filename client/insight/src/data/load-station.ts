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

export interface LoadStationData {
  readonly pallet?: Readonly<api.IPalletStatus>;
  readonly face: im.Map<number, MaterialList>;
  readonly castings: MaterialList;
  readonly free: MaterialList;
  readonly queues: im.Map<string, MaterialList>;
}

export function selectLoadStationProps(
    loadNum: number,
    queues: ReadonlyArray<string>,
    curSt: Readonly<api.ICurrentStatus>
  ): LoadStationData {

  let pal: Readonly<api.IPalletStatus> | undefined;
  for (let p of Object.values(curSt.pallets)) {
    if (p.currentPalletLocation.loc === api.PalletLocationEnum.LoadUnload && p.currentPalletLocation.num === loadNum) {
      pal = p;
      break;
    }
  }
  if (pal === undefined) {
    return {
      pallet: undefined,
      face: im.Map(),
      castings: [],
      free: [],
      queues: im.Map(),
    };
  }
  let palName: string = pal.pallet;

  const onPal = im.Seq(curSt.material)
    .filter(m => m.location.type === api.LocType.OnPallet
              && m.location.pallet === palName
    );

  let byFace = onPal
    .groupBy(m => m.location.face)
    .map(ms => ms.valueSeq().toArray())
    .toMap();

  // add missing blank faces
  const maxFace = onPal
    .map(m => m.location.face)
    .max()
    || 0;
  for (let face = 1; face <= maxFace; face++) {
    if (!byFace.has(face)) {
      byFace = byFace.set(face, []);
    }
  }

  /*const byFace = new Map<number, Readonly<api.InProcessMaterial>[]>();
  for (let mat of curSt.material) {
    if (mat.location.type === api.LocType.OnPallet && mat.location.pallet === palName) {
      if (byFace.has(mat.location.face)) {
        (byFace.get(mat.location.face) || []).push(mat);
      } else {
        byFace.set(mat.location.face, [mat]);
      }
    }
  }*/

  const castings = im.Seq(curSt.material)
    .filter(m => m.action.type === api.ActionType.Loading
              && m.action.loadOntoPallet === palName
              && m.action.processAfterLoad === 1
              && m.location.type === api.LocType.Free);

  const free = im.Seq(curSt.material)
    .filter(m => m.action.type === api.ActionType.Loading
              && m.action.loadOntoPallet === palName
              && m.action.processAfterLoad > 1
              && m.location.type === api.LocType.Free);

  const queueNames = im.Set(queues);
  const queueMat = im.Seq(curSt.material)
    .filter(m => m.action.type === api.ActionType.Loading
              && m.action.loadOntoPallet === palName
              && m.location.type === api.LocType.InQueue
              && queueNames.contains(m.location.currentQueue || "")
    )
    .groupBy(m => m.location.currentQueue || "")
    .map(ms => ms.valueSeq().toArray())
    .toMap();

  return {
    pallet: pal,
    face: byFace,
    castings: castings.toArray(),
    free: free.toArray(),
    queues: queueMat,
  };
}