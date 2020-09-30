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
import { LazySeq } from "./lazyseq";

export type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

export enum MaterialBinType {
  LoadStations = "Bin_LoadStations",
  Pallets = "Bin_Pallets",
  ActiveQueues = "Bin_ActiveQueues",
  QuarantineQueues = "Bin_Quarantine",
}

export type MaterialBin =
  | {
      readonly type: MaterialBinType.LoadStations;
      readonly binId: MaterialBinId;
      readonly byLul: HashMap<number, MaterialList>;
    }
  | {
      readonly type: MaterialBinType.Pallets;
      readonly binId: MaterialBinId;
      readonly byPallet: HashMap<string, MaterialList>;
    }
  | {
      readonly type: MaterialBinType.ActiveQueues;
      readonly binId: MaterialBinId;
      readonly byQueue: HashMap<string, MaterialList>;
    }
  | {
      readonly type: MaterialBinType.QuarantineQueues;
      readonly binId: MaterialBinId;
      readonly queueName: string;
      readonly material: MaterialList;
    };

export enum MaterialBinActionType {
  Move = "MaterialBin_Move",
}

export type MaterialBinId = string;
export const LoadStationBinId: MaterialBinId = "__FMS_INSIGHT_LOAD_STATION_BIN__";
export const PalletsBinId: MaterialBinId = "__FMS_INSIGHT_PALLETS_BIN__";
export const ActiveQueuesBinId: MaterialBinId = "__FMS_INSIGHT_ACTIVE_QUEUE_BIN__";

export type MaterialBinAction = {
  readonly type: MaterialBinActionType.Move;
  readonly newBinOrder: ReadonlyArray<MaterialBinId>;
};

export interface MaterialBinState {
  readonly curBinOrder: ReadonlyArray<MaterialBinId>;
}

export const initial: MaterialBinState = {
  curBinOrder: JSON.parse(localStorage.getItem("material-bins") || "[]"),
};

export function moveMaterialBin(
  curBinOrder: ReadonlyArray<MaterialBinId>,
  oldIdx: number,
  newIdx: number
): MaterialBinAction {
  const newBinOrder = Array.from(curBinOrder);
  const [removed] = newBinOrder.splice(oldIdx, 1);
  newBinOrder.splice(newIdx, 0, removed);
  return { type: MaterialBinActionType.Move, newBinOrder };
}

function addToMap<T>(m: Map<T, Array<Readonly<api.IInProcessMaterial>>>, k: T, mat: Readonly<api.IInProcessMaterial>) {
  const mats = m.get(k);
  if (mats) {
    mats.push(mat);
  } else {
    m.set(k, [mat]);
  }
}

export function selectAllMaterialIntoBins(
  curSt: Readonly<api.ICurrentStatus>,
  curBinOrder: ReadonlyArray<MaterialBinId>
): ReadonlyArray<MaterialBin> {
  const loadStations = new Map<number, Array<Readonly<api.IInProcessMaterial>>>();
  const pallets = new Map<string, Array<Readonly<api.IInProcessMaterial>>>();
  const queues = new Map<string, Array<Readonly<api.IInProcessMaterial>>>();

  const palLoc = HashMap.ofObjectDictionary(curSt.pallets).mapValues(
    (st) => " (" + st.currentPalletLocation.group + " #" + st.currentPalletLocation.num.toString() + ")"
  );

  for (const mat of curSt.material) {
    switch (mat.location.type) {
      case api.LocType.InQueue:
        if (mat.action.type === api.ActionType.Loading) {
          const pal = curSt.pallets[mat.action.loadOntoPallet ?? ""];
          if (pal && pal.currentPalletLocation.loc === api.PalletLocationEnum.LoadUnload) {
            const lul = pal.currentPalletLocation.num;
            addToMap(loadStations, lul, mat);
          } else {
            addToMap(queues, mat.location.currentQueue || "Queue", mat);
          }
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

  const activeQueues = LazySeq.ofObject(curSt.jobs)
    .flatMap(([_, job]) => job.procsAndPaths)
    .flatMap((proc) => proc.paths)
    .flatMap((path) => {
      const q: string[] = [];
      if (path.inputQueue !== undefined) q.push(path.inputQueue);
      if (path.outputQueue !== undefined) q.push(path.outputQueue);
      return q;
    })
    .toSet((x) => x);
  const quarantineQueues = LazySeq.ofObject(curSt.queues)
    .filter(([qname, _]) => !activeQueues.contains(qname))
    .toSet(([qname, _]) => qname);

  const bins = curBinOrder.filter(
    (b) => b === LoadStationBinId || b === PalletsBinId || b === ActiveQueuesBinId || quarantineQueues.contains(b)
  );
  if (bins.indexOf(ActiveQueuesBinId) < 0) {
    bins.unshift(ActiveQueuesBinId);
  }
  if (bins.indexOf(PalletsBinId) < 0) {
    bins.unshift(PalletsBinId);
  }
  if (bins.indexOf(LoadStationBinId) < 0) {
    bins.unshift(LoadStationBinId);
  }
  for (const queue of quarantineQueues.toArray({ sortOn: (x) => x })) {
    if (bins.indexOf(queue) < 0) {
      bins.push(queue);
    }
  }

  return bins.map((binId) => {
    if (binId === LoadStationBinId) {
      return { type: MaterialBinType.LoadStations, binId: LoadStationBinId, byLul: HashMap.ofIterable(loadStations) };
    } else if (binId === PalletsBinId) {
      return { type: MaterialBinType.Pallets, binId: PalletsBinId, byPallet: HashMap.ofIterable(pallets) };
    } else if (binId === ActiveQueuesBinId) {
      return {
        type: MaterialBinType.ActiveQueues,
        binId: ActiveQueuesBinId,
        byQueue: LazySeq.ofIterable(activeQueues)
          .map((queueName) => {
            const mat = queues.get(queueName) ?? [];
            mat.sort((m1, m2) => (m1.location.queuePosition ?? 0) - (m2.location.queuePosition ?? 0));
            return [queueName, mat] as [string, MaterialList];
          })
          .toMap(
            ([queueName, mat]) => [queueName, mat],
            (ms1, ms2) => ms1.concat(ms2)
          ),
      };
    } else {
      const queueName = binId;
      const mat = queues.get(queueName) ?? [];
      mat.sort((m1, m2) => (m1.location.queuePosition ?? 0) - (m2.location.queuePosition ?? 0));
      return {
        type: MaterialBinType.QuarantineQueues as const,
        binId: queueName as MaterialBinId,
        queueName,
        material: mat,
      };
    }
  });
}

export function createOnStateChange(): (s: MaterialBinState) => void {
  let lastBins = initial.curBinOrder;
  return (s) => {
    if (s.curBinOrder !== lastBins) {
      lastBins = s.curBinOrder;
      localStorage.setItem("material-bins", JSON.stringify(s.curBinOrder));
    }
  };
}

export function reducer(s: MaterialBinState | undefined, a: MaterialBinAction): MaterialBinState {
  if (!s) {
    return initial;
  }

  switch (a.type) {
    case MaterialBinActionType.Move:
      return { curBinOrder: a.newBinOrder };
    default:
      return s;
  }
}
