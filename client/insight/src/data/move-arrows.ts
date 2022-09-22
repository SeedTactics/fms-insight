/* Copyright (c) 2022, John Lenz

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
import { HashMap, LazySeq } from "@seedtactics/immutable-collections";

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
      readonly material: Readonly<api.IInProcessMaterial> | null;
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

export type MoveMaterialIdentifier = string;

export function uniqueIdForNodeKind(kind: MoveMaterialNodeKind): MoveMaterialIdentifier {
  switch (kind.type) {
    case MoveMaterialNodeKindType.Material:
      return "Material-" + (kind.material?.materialID ?? -1).toString();
    case MoveMaterialNodeKindType.FreeMaterialZone:
      return "FreeMaterialZone";
    case MoveMaterialNodeKindType.CompletedMaterialZone:
      return "CompletedMaterialZone";
    case MoveMaterialNodeKindType.PalletFaceZone:
      return "PalletFaceZone-" + kind.face.toString();
    case MoveMaterialNodeKindType.QueueZone:
      return "QueueZone-" + kind.queue;
  }
}

export function memoPropsForNodeKind(kind: MoveMaterialNodeKind): ReadonlyArray<unknown> {
  switch (kind.type) {
    case MoveMaterialNodeKindType.Material:
      return [kind.type, kind.material];
    case MoveMaterialNodeKindType.FreeMaterialZone:
      return [kind.type];
    case MoveMaterialNodeKindType.CompletedMaterialZone:
      return [kind.type];
    case MoveMaterialNodeKindType.PalletFaceZone:
      return [kind.type, kind.face];
    case MoveMaterialNodeKindType.QueueZone:
      return [kind.type, kind.queue];
  }
}

export type AllMoveMaterialArrows<T> = HashMap<
  MoveMaterialIdentifier,
  MoveMaterialNodeKind & { readonly elem: T }
>;

export interface MoveMaterialArrow {
  readonly fromX: number;
  readonly fromY: number;
  readonly toX: number;
  readonly toY: number;
  readonly curveDirection: number; // 1 or -1
}

export interface MoveArrowElemRect {
  readonly left: number;
  readonly top: number;
  readonly width: number;
  readonly height: number;
  readonly bottom: number;
  readonly right: number;
}

interface MoveMaterialByKind {
  readonly freeMaterial?: MoveArrowElemRect;
  readonly completedMaterial?: MoveArrowElemRect;
  readonly faces: ReadonlyMap<number, MoveArrowElemRect>;
  readonly queues: ReadonlyMap<string, MoveArrowElemRect>;

  readonly material: ReadonlyArray<[MoveArrowElemRect, Readonly<api.IInProcessMaterial>]>;
}

function buildMatByKind(allArrows: AllMoveMaterialArrows<MoveArrowElemRect>): MoveMaterialByKind {
  let freeMaterial: MoveArrowElemRect | undefined;
  let completedMaterial: MoveArrowElemRect | undefined;
  const faces = new Map<number, MoveArrowElemRect>();
  const queues = new Map<string, MoveArrowElemRect>();
  const material = new Array<[MoveArrowElemRect, Readonly<api.IInProcessMaterial>]>();

  for (const node of allArrows.values()) {
    switch (node.type) {
      case MoveMaterialNodeKindType.FreeMaterialZone:
        freeMaterial = node.elem;
        break;
      case MoveMaterialNodeKindType.CompletedMaterialZone:
        completedMaterial = node.elem;
        break;
      case MoveMaterialNodeKindType.PalletFaceZone:
        faces.set(node.face, node.elem);
        break;
      case MoveMaterialNodeKindType.QueueZone:
        queues.set(node.queue, node.elem);
        break;
      case MoveMaterialNodeKindType.Material:
        if (node.material) {
          material.push([node.elem, node.material]);
        }
        break;
    }
  }

  return { freeMaterial, completedMaterial, faces, queues, material };
}

export function computeArrows(
  container: MoveArrowElemRect | null | undefined,
  allArrows: AllMoveMaterialArrows<MoveArrowElemRect>
): ReadonlyArray<MoveMaterialArrow> {
  if (!container) {
    return [];
  }
  const byKind = buildMatByKind(allArrows);
  if (!byKind.completedMaterial) {
    return [];
  }

  const arrows: Array<MoveMaterialArrow> = [];

  const faceDestUsed = new Map<number, number>();
  const queueDestUsed = new Map<string, number>();
  let lastFreeUsed = 0;

  for (const [rect, mat] of LazySeq.of(byKind.material).sortBy(
    ([rect]) => rect.left,
    ([rect]) => rect.top
  )) {
    switch (mat.action.type) {
      case api.ActionType.UnloadToCompletedMaterial:
        arrows.push({
          fromX: rect.left,
          fromY: rect.top + rect.height / 2,
          toX: rect.left,
          toY: byKind.completedMaterial
            ? byKind.completedMaterial.top + byKind.completedMaterial.height / 2
            : container.bottom - 10,
          curveDirection: 1,
        });
        break;
      case api.ActionType.UnloadToInProcess: {
        let dest: MoveArrowElemRect | undefined;
        let lastSlotUsed: number;
        if (mat.action.unloadIntoQueue) {
          dest = byKind.queues.get(mat.action.unloadIntoQueue);
          lastSlotUsed = queueDestUsed.get(mat.action.unloadIntoQueue) ?? 0;
          queueDestUsed.set(mat.action.unloadIntoQueue, lastSlotUsed + 1);
        } else {
          dest = byKind.freeMaterial;
          lastSlotUsed = lastFreeUsed;
          lastFreeUsed += 1;
        }
        arrows.push({
          fromX: rect.right,
          fromY: rect.top + rect.height / 2,
          toX: dest !== undefined ? dest.left - 5 : container.right - 2,
          toY: dest !== undefined ? dest.top + 20 * (lastSlotUsed + 1) : rect.top + rect.height / 2,
          curveDirection: 1,
        });
        break;
      }
      case api.ActionType.Loading:
        if (mat.action.loadOntoFace) {
          const face = byKind.faces.get(mat.action.loadOntoFace);
          if (face !== undefined) {
            const fromQueue = rect.left > face.right;
            if (fromQueue) {
              const faceSpotsUsed = faceDestUsed.get(mat.action.loadOntoFace) ?? 0;
              faceDestUsed.set(mat.action.loadOntoFace, faceSpotsUsed + 1);
              arrows.push({
                fromX: rect.left,
                fromY: rect.top + rect.height / 2,
                toX: face.right - 10,
                toY: face.top + 20 * (faceSpotsUsed + 1),
                curveDirection: 1,
              });
            } else {
              arrows.push({
                fromX: rect.left,
                fromY: rect.top + rect.height / 2,
                toX: rect.left,
                toY: face.top + 10,
                curveDirection: 1,
              });
            }
          }
        }
        break;
    }
  }

  return arrows.map((arr) => ({
    fromX: arr.fromX - container.left,
    fromY: arr.fromY - container.top,
    toX: arr.toX - container.left,
    toY: arr.toY - container.top,
    curveDirection: arr.curveDirection,
  }));
}
