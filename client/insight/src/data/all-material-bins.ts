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
import { HashMap } from "prelude-ts";
import { LazySeq } from "./lazyseq";

export type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

export type AllMaterialBins = HashMap<string, MaterialList>;

export function selectAllMaterialIntoBins(curSt: Readonly<api.ICurrentStatus>): AllMaterialBins {
  const palLoc = HashMap.ofObjectDictionary(curSt.pallets).mapValues(
    st => " (" + st.currentPalletLocation.group + " #" + st.currentPalletLocation.num.toString() + ")"
  );

  return LazySeq.ofIterable(curSt.material)
    .map(mat => {
      switch (mat.location.type) {
        case api.LocType.InQueue:
          return { region: mat.location.currentQueue || "Queue", mat };
        case api.LocType.OnPallet:
          let region = "Pallet";
          if (mat.location.pallet) {
            region = "Pallet " + mat.location.pallet.toString() + palLoc.get(mat.location.pallet).getOrElse("");
          }
          return { region, mat };
        case api.LocType.Free:
        default:
          switch (mat.action.type) {
            case api.ActionType.Loading:
              return { region: "Raw Material", mat };
            default:
              return { region: "Free Material", mat };
          }
      }
    })
    .groupBy(x => x.region)
    .mapValues(group =>
      group
        .map(x => x.mat)
        .sortOn(mat => {
          switch (mat.location.type) {
            case api.LocType.OnPallet:
              return mat.location.face || 1;
            case api.LocType.InQueue:
            case api.LocType.Free:
            default:
              return mat.location.queuePosition || 1;
          }
        })
        .toArray()
    );
}
