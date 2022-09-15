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

import * as api from "../network/api.js";
import { LazySeq } from "@seedtactics/immutable-collections";
import { atom } from "recoil";

export type MaterialList = ReadonlyArray<Readonly<api.IInProcessMaterial>>;

export type MaterialBinId = string;
export const LoadStationBinId: MaterialBinId = "__FMS_INSIGHT_LOAD_STATION_BIN__";
export const PalletsBinId: MaterialBinId = "__FMS_INSIGHT_PALLETS_BIN__";
export const ActiveQueuesBinId: MaterialBinId = "__FMS_INSIGHT_ACTIVE_QUEUE_BIN__";

export interface MaterialBinState {
  readonly curBinOrder: ReadonlyArray<MaterialBinId>;
}

export const currentMaterialBinOrder = atom<ReadonlyArray<MaterialBinId>>({
  key: "current-material-bin-order",
  default: JSON.parse(localStorage.getItem("material-bins") || "[]") as ReadonlyArray<MaterialBinId>,
  effects: [
    ({ onSet }) => {
      onSet((newBins) => {
        localStorage.setItem("material-bins", JSON.stringify(newBins));
      });
    },
  ],
});

export function moveMaterialBin(
  curBinOrder: ReadonlyArray<MaterialBinId>,
  oldIdx: number,
  newIdx: number
): ReadonlyArray<MaterialBinId> {
  const newBinOrder = Array.from(curBinOrder);
  const [removed] = newBinOrder.splice(oldIdx, 1);
  newBinOrder.splice(newIdx, 0, removed);
  return newBinOrder;
}

function addToMap<T>(
  m: Map<T, Array<Readonly<api.IInProcessMaterial>>>,
  k: T,
  mat: Readonly<api.IInProcessMaterial>
) {
  const mats = m.get(k);
  if (mats) {
    mats.push(mat);
  } else {
    m.set(k, [mat]);
  }
}

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
      readonly byLul: ReadonlyMap<number, MaterialList>;
    }
  | {
      readonly type: MaterialBinType.Pallets;
      readonly binId: MaterialBinId;
      readonly byPallet: ReadonlyMap<string, MaterialList>;
    }
  | {
      readonly type: MaterialBinType.ActiveQueues;
      readonly binId: MaterialBinId;
      readonly byQueue: ReadonlyMap<string, MaterialList>;
    }
  | {
      readonly type: MaterialBinType.QuarantineQueues;
      readonly binId: MaterialBinId;
      readonly queueName: string;
      readonly material: MaterialList;
    };

// TODO: switch to recoil selector
export function selectAllMaterialIntoBins(
  curSt: Readonly<api.ICurrentStatus>,
  curBinOrder: ReadonlyArray<MaterialBinId>
): ReadonlyArray<MaterialBin> {
  const loadStations = new Map<number, Array<Readonly<api.IInProcessMaterial>>>();
  const pallets = new Map<string, Array<Readonly<api.IInProcessMaterial>>>();
  const queues = new Map<string, Array<Readonly<api.IInProcessMaterial>>>();

  const palLoc = LazySeq.ofObject(curSt.pallets).toRMap(([k, st]) => [
    k,
    " (" + st.currentPalletLocation.group + " #" + st.currentPalletLocation.num.toString() + ")",
  ]);

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
            const loc = palLoc.get(mat.location.pallet) ?? "";
            addToMap(pallets, mat.location.pallet + loc, mat);
          }
        } else {
          addToMap(pallets, "Pallet", mat);
        }
        break;

      case api.LocType.Free:
        switch (mat.action.type) {
          case api.ActionType.Loading: {
            const lul = curSt.pallets[mat.action.loadOntoPallet ?? ""]?.currentPalletLocation.num;
            addToMap(loadStations, lul, mat);
            break;
          }
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
    .toRSet((x) => x);
  const quarantineQueues = LazySeq.ofObject(curSt.queues)
    .filter(([qname, _]) => !activeQueues.has(qname))
    .toRSet(([qname, _]) => qname);

  const bins = curBinOrder.filter(
    (b) => b === LoadStationBinId || b === PalletsBinId || b === ActiveQueuesBinId || quarantineQueues.has(b)
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
  for (const queue of LazySeq.of(quarantineQueues).sortBy((x) => x)) {
    if (bins.indexOf(queue) < 0) {
      bins.push(queue);
    }
  }

  return bins.map((binId) => {
    if (binId === LoadStationBinId) {
      return { type: MaterialBinType.LoadStations, binId: LoadStationBinId, byLul: loadStations };
    } else if (binId === PalletsBinId) {
      return { type: MaterialBinType.Pallets, binId: PalletsBinId, byPallet: pallets };
    } else if (binId === ActiveQueuesBinId) {
      return {
        type: MaterialBinType.ActiveQueues,
        binId: ActiveQueuesBinId,
        byQueue: LazySeq.of(activeQueues).toRMap(
          (queueName) => {
            const mat = queues.get(queueName) ?? [];
            mat.sort((m1, m2) => (m1.location.queuePosition ?? 0) - (m2.location.queuePosition ?? 0));
            return [queueName, mat] as const;
          },
          (ms1, ms2) => ms1.concat(ms2) // TODO: rework to use toLookup
        ),
      };
    } else {
      const queueName = binId;
      const mat = queues.get(queueName) ?? [];
      mat.sort((m1, m2) => (m1.location.queuePosition ?? 0) - (m2.location.queuePosition ?? 0));
      return {
        type: MaterialBinType.QuarantineQueues as const,
        binId: queueName,
        queueName,
        material: mat,
      };
    }
  });
}

export function moveMaterialInBin(
  bins: ReadonlyArray<MaterialBin>,
  mat: Readonly<api.IInProcessMaterial>,
  queue: string,
  queuePosition: number
): ReadonlyArray<MaterialBin> {
  return bins.map((bin) => {
    switch (bin.type) {
      case MaterialBinType.QuarantineQueues:
        if (bin.queueName === queue) {
          const mats = bin.material.filter((m) => m.materialID !== mat.materialID);
          mats.splice(queuePosition, 0, mat);
          return { ...bin, material: mats };
        } else {
          return { ...bin, material: bin.material.filter((m) => m.materialID !== mat.materialID) };
        }
      default:
        return bin;
    }
  });
}

export interface QuarantineBinAndIndex {
  readonly bin: Extract<MaterialBin, { type: MaterialBinType.QuarantineQueues }>;
  readonly idx: number;
}

export function findMaterialInQuarantineQueues(
  matId: number,
  bins: ReadonlyArray<MaterialBin>
): QuarantineBinAndIndex | null {
  for (const bin of bins) {
    switch (bin.type) {
      case MaterialBinType.QuarantineQueues:
        for (let i = 0; i < bin.material.length; i++) {
          if (bin.material[i].materialID === matId) {
            return { bin, idx: i };
          }
        }
        break;
      default:
        break;
    }
  }
  return null;
}

export function findQueueuInQuarantineQueues(
  queue: string,
  bins: ReadonlyArray<MaterialBin>
): QuarantineBinAndIndex | null {
  for (const bin of bins) {
    if (bin.type === MaterialBinType.QuarantineQueues && bin.queueName === queue) {
      return { bin, idx: 0 };
    }
  }
  return null;
}
