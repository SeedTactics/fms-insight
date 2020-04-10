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
import { HashMap, fieldsHashCode, Vector, Option } from "prelude-ts";
import { LazySeq } from "./lazyseq";

export class MoveMaterialIdentifier {
  public static allocateNodeId: () => MoveMaterialIdentifier = (function () {
    let cntr = 0;
    return function () {
      cntr += 1;
      return new MoveMaterialIdentifier(cntr);
    };
  })();

  equals(other: MoveMaterialIdentifier): boolean {
    return this.cntr === other.cntr;
  }
  hashCode(): number {
    return fieldsHashCode(this.cntr);
  }
  toString(): string {
    return "MoveMaterialId-" + this.cntr.toString();
  }
  private constructor(private readonly cntr: number) {}
}

export enum MoveMaterialNodeKindType {
  Material,
  FreeMaterialZone,
  CompletedMaterialZone,
  PalletFaceZone,
  QueueZone,
}

export type MoveMaterialNodeKind =
  | {
      readonly type: MoveMaterialNodeKindType.Material;
      readonly action: Readonly<api.IInProcessMaterialAction> | null;
    }
  | { readonly type: MoveMaterialNodeKindType.FreeMaterialZone }
  | { readonly type: MoveMaterialNodeKindType.CompletedMaterialZone }
  | {
      readonly type: MoveMaterialNodeKindType.PalletFaceZone;
      readonly face: number;
    }
  | {
      readonly type: MoveMaterialNodeKindType.QueueZone;
      readonly queue: string;
    };

export interface MoveMaterialArrowData<T> {
  readonly container: T | null;
  readonly nodes: HashMap<MoveMaterialIdentifier, T>;
  readonly node_type: HashMap<MoveMaterialIdentifier, MoveMaterialNodeKind>;
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
  readonly faces: HashMap<number, ClientRect>;
  readonly queues: HashMap<string, ClientRect>;

  readonly material: Vector<[ClientRect, Readonly<api.IInProcessMaterialAction>]>;
}

function buildMatByKind(data: MoveMaterialArrowData<ClientRect>): MoveMaterialByKind {
  return data.node_type.foldLeft(
    {
      faces: HashMap.empty<number, ClientRect>(),
      queues: HashMap.empty<string, ClientRect>(),
      material: Vector.empty<[ClientRect, Readonly<api.IInProcessMaterialAction>]>(),
    } as MoveMaterialByKind,
    (acc, [key, kind]) => {
      const nodeM = data.nodes.get(key);
      if (nodeM.isNone()) {
        return acc;
      }
      const node = nodeM.get();
      switch (kind.type) {
        case MoveMaterialNodeKindType.FreeMaterialZone:
          return { ...acc, freeMaterial: node };
        case MoveMaterialNodeKindType.CompletedMaterialZone:
          return { ...acc, completedMaterial: node };
        case MoveMaterialNodeKindType.PalletFaceZone:
          return { ...acc, faces: acc.faces.put(kind.face, node) };
        case MoveMaterialNodeKindType.QueueZone:
          return { ...acc, queues: acc.queues.put(kind.queue, node) };
        case MoveMaterialNodeKindType.Material:
          if (kind.action) {
            return { ...acc, material: acc.material.append([node, kind.action]) };
          } else {
            return acc;
          }
      }
    }
  );
}

export function computeArrows(data: MoveMaterialArrowData<ClientRect>): ReadonlyArray<MoveMaterialArrow> {
  const container = data.container;
  if (!container) {
    return [];
  }
  const byKind = buildMatByKind(data);
  if (!byKind.completedMaterial) {
    return [];
  }

  return byKind.material
    .transform(LazySeq.ofIterable)
    .map((value) => {
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
        case api.ActionType.UnloadToInProcess: {
          let dest: Option<ClientRect>;
          if (action.unloadIntoQueue) {
            dest = byKind.queues.get(action.unloadIntoQueue);
          } else {
            dest = Option.ofNullable(byKind.freeMaterial);
          }
          return {
            fromX: rect.right,
            fromY: rect.top + rect.height / 2,
            toX: dest.isSome() ? dest.get().left - 5 : container.right - 2,
            toY: dest.isSome() ? dest.get().top + dest.get().height / 2 : rect.top + rect.height / 2,
            curveDirection: 1,
          } as MoveMaterialArrow;
        }
        case api.ActionType.Loading:
          if (action.loadOntoFace) {
            const face = byKind.faces.get(action.loadOntoFace);
            if (face.isSome()) {
              const fromQueue = rect.left > face.get().right;
              return {
                fromX: rect.left,
                fromY: rect.top + rect.height / 2,
                toX: fromQueue ? face.get().right - 10 : rect.left,
                toY: face.get().top + 10,
                curveDirection: 1,
              } as MoveMaterialArrow;
            }
          }
          break;
      }
      return null;
    })
    .mapOption((arr) => {
      if (arr) {
        return Option.some({
          fromX: arr.fromX - container.left,
          fromY: arr.fromY - container.top,
          toX: arr.toX - container.left,
          toY: arr.toY - container.top,
          curveDirection: arr.curveDirection,
        });
      } else {
        return Option.none<MoveMaterialArrow>();
      }
    })
    .toArray();
}
