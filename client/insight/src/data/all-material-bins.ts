/* Copyright (c) 2019, John Lenz

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
import { HashMap } from "prelude-ts";

export type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

export interface MaterialBins {
  readonly loadStations: HashMap<number, MaterialList>;
  readonly pallets: HashMap<string, MaterialList>;
  readonly queues: HashMap<string, MaterialList>;
}

function addToMap<T>(m: Map<T, Array<Readonly<api.IInProcessMaterial>>>, k: T, mat: Readonly<api.IInProcessMaterial>) {
  const mats = m.get(k);
  if (mats) {
    mats.push(mat);
  } else {
    m.set(k, [mat]);
  }
}

export function selectAllMaterialIntoBins(curSt: Readonly<api.ICurrentStatus>): MaterialBins {
  const loadStations = new Map<number, Array<Readonly<api.IInProcessMaterial>>>();
  const pallets = new Map<string, Array<Readonly<api.IInProcessMaterial>>>();
  const queues = new Map<string, Array<Readonly<api.IInProcessMaterial>>>();

  const palLoc = HashMap.ofObjectDictionary(curSt.pallets).mapValues(
    st => " (" + st.currentPalletLocation.group + " #" + st.currentPalletLocation.num.toString() + ")"
  );

  for (const mat of curSt.material) {
    switch (mat.location.type) {
      case api.LocType.InQueue:
        if (mat.action.type === api.ActionType.Loading) {
          const lul = curSt.pallets[mat.action.loadOntoPallet ?? ""]?.currentPalletLocation.num;
          addToMap(loadStations, lul, mat);
        } else {
          addToMap(queues, mat.location.currentQueue || "Queue", mat);
        }
        break;

      case api.LocType.OnPallet:
        if (mat.location.pallet) {
          const palSt = curSt.pallets[mat.location.pallet];
          if (palSt.currentPalletLocation.loc === api.PalletLocationEnum.LoadUnload) {
            addToMap(loadStations, palSt.currentPalletLocation.num, mat);
          } else {
            const loc = palLoc.get(mat.location.pallet).getOrElse("");
            addToMap(pallets, mat.location.pallet + loc, mat);
          }
        } else {
          addToMap(pallets, "Pallet", mat);
        }
        break;

      case api.LocType.Free:
        switch (mat.action.type) {
          case api.ActionType.Loading:
            const lul = curSt.pallets[mat.action.loadOntoPallet ?? ""]?.currentPalletLocation.num;
            addToMap(loadStations, lul, mat);
            break;
          default:
            addToMap(queues, "Free Material", mat);
            break;
        }
        break;
    }
  }

  return {
    loadStations: HashMap.ofIterable(loadStations),
    pallets: HashMap.ofIterable(pallets),
    queues: HashMap.ofIterable(queues).mapValues(arr =>
      arr.sort((m1, m2) => (m1.location.queuePosition ?? 0) - (m2.location.queuePosition ?? 0))
    )
  };
}
