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

export type MoveMaterialIdentifier = Symbol;

export enum MoveMaterialNodeKindType {
  Material,
  FreeMaterialZone,
  CompletedMaterialZone,
  PalletFaceZone,
  QueueZone
}

export type MoveMaterialNodeKind =
  | { readonly type: MoveMaterialNodeKindType.Material, readonly action: Readonly<api.IInProcessMaterialAction> | null }
  | { readonly type: MoveMaterialNodeKindType.FreeMaterialZone }
  | { readonly type: MoveMaterialNodeKindType.CompletedMaterialZone }
  | { readonly type: MoveMaterialNodeKindType.PalletFaceZone, readonly face: number }
  | { readonly type: MoveMaterialNodeKindType.QueueZone, readonly queue: string }
  ;

export interface MoveMaterialArrowData<T> {
  readonly container: T | null;
  readonly nodes: im.Map<MoveMaterialIdentifier, T>;
  readonly node_type: im.Map<MoveMaterialIdentifier, MoveMaterialNodeKind>;
}

export interface MoveMaterialArrow {
  readonly fromX: number;
  readonly fromY: number;
  readonly toX: number;
  readonly toY: number;
  readonly curveDirection: number; // 1 or -1
}

interface MoveMaterialByKind {
  readonly freeMaterial?: ClientRect;
  readonly completedMaterial?: ClientRect;
  readonly faces: im.Map<number, ClientRect>;
  readonly queues: im.Map<string, ClientRect>;

  readonly material: im.List<[ClientRect, Readonly<api.IInProcessMaterialAction>]>;
}

function buildMatByKind(data: MoveMaterialArrowData<ClientRect>): MoveMaterialByKind {
  return data.node_type.reduce(
    (acc, kind, key) => {
      var node = data.nodes.get(key);
      if (!node) { return acc; }
      switch (kind.type) {
        case MoveMaterialNodeKindType.FreeMaterialZone:
          return {...acc, freeMaterial: node};
        case MoveMaterialNodeKindType.CompletedMaterialZone:
          return {...acc, completedMaterial: node};
        case MoveMaterialNodeKindType.PalletFaceZone:
          return {...acc, faces: acc.faces.set(kind.face, node) };
        case MoveMaterialNodeKindType.QueueZone:
          return {...acc, queues: acc.queues.set(kind.queue, node) };
        case MoveMaterialNodeKindType.Material:
          if (kind.action) {
            return {...acc, material: acc.material.push([node, kind.action])};
          } else {
            return acc;
          }
      }
    },
    {
      faces: im.Map<number, ClientRect>(),
      queues: im.Map<string, ClientRect>(),
      material: im.List<[ClientRect, Readonly<api.IInProcessMaterialAction>]>(),
    } as MoveMaterialByKind
  );
}

export function computeArrows(data: MoveMaterialArrowData<ClientRect>): ReadonlyArray<MoveMaterialArrow> {
  const container = data.container;
  if (!container) { return []; }
  const byKind = buildMatByKind(data);
  if (!byKind.completedMaterial) { return []; }

  return byKind.material.toSeq().map(
    value => {
      const rect = value[0];
      const action = value[1];
      switch (action.type) {
        case api.ActionType.UnloadToCompletedMaterial:
          return {
            fromX: rect.left,
            fromY: rect.top + rect.height / 2,
            toX: rect.left,
            toY: byKind.completedMaterial
              ? byKind.completedMaterial.top + byKind.completedMaterial.height / 2
              : container.bottom - 10,
            curveDirection: 1,
          } as MoveMaterialArrow;
        case api.ActionType.UnloadToInProcess:
          let dest: ClientRect | undefined;
          if (action.unloadIntoQueue) {
            dest = byKind.queues.get(action.unloadIntoQueue);
          } else {
            dest = byKind.freeMaterial;
          }
          return {
            fromX: rect.right,
            fromY: rect.top + rect.height / 2,
            toX: dest ? dest.left - 5 : container.right - 2,
            toY: dest ? dest.top + dest.height / 2 : rect.top + rect.height / 2,
            curveDirection: 1,
          } as MoveMaterialArrow;
        case api.ActionType.Loading:
          if (action.loadOntoFace) {
            const face = byKind.faces.get(action.loadOntoFace);
            if (face) {
              const fromQueue = rect.left > face.right;
              return {
                fromX: rect.left,
                fromY: rect.top + rect.height / 2,
                toX: fromQueue ? face.right - 10 : rect.left,
                toY: face.top + 10,
                curveDirection: 1,
              } as MoveMaterialArrow;
            }
          }
          break;
      }
      return null;
    }
  )
  .filter((e): e is MoveMaterialArrow => e !== null)
  .map(arr => ({
    fromX: arr.fromX - container.left,
    fromY: arr.fromY - container.top,
    toX: arr.toX - container.left,
    toY: arr.toY - container.top,
    curveDirection: arr.curveDirection,
  }))
  .toArray();
}
