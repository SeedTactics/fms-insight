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
  CompletedCollapsedMaterialZone,
  CompletedExpandedMaterialZone,
  PalletFaceZone,
  QueueZone,
}

export type MoveMaterialNodeKind =
  | {
      readonly type: MoveMaterialNodeKindType.Material;
      readonly material: Readonly<api.IInProcessMaterial> | null;
    }
  | { readonly type: MoveMaterialNodeKindType.FreeMaterialZone }
  | { readonly type: MoveMaterialNodeKindType.CompletedCollapsedMaterialZone }
  | { readonly type: MoveMaterialNodeKindType.CompletedExpandedMaterialZone }
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
    case MoveMaterialNodeKindType.CompletedCollapsedMaterialZone:
      return "CompletedCollapsedMaterialZone";
    case MoveMaterialNodeKindType.CompletedExpandedMaterialZone:
      return "CompletedExpandedMaterialZone";
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
    case MoveMaterialNodeKindType.CompletedCollapsedMaterialZone:
    case MoveMaterialNodeKindType.CompletedExpandedMaterialZone:
      return [kind.type, null];
    case MoveMaterialNodeKindType.PalletFaceZone:
      return [kind.type, kind.face];
    case MoveMaterialNodeKindType.QueueZone:
      return [kind.type, kind.queue];
  }
}

export type AllMoveMaterialNodes<T> = HashMap<
  MoveMaterialIdentifier,
  MoveMaterialNodeKind & { readonly elem: T }
>;

export type MoveMaterialArrow = {
  readonly fromX: number;
  readonly fromY: number;
  readonly toX: number;
  readonly toY: number;
  readonly curveDirection: number; // 1 or -1
};

export type MoveMaterialElemRect = {
  readonly left: number;
  readonly top: number;
  readonly width: number;
  readonly height: number;
  readonly bottom: number;
  readonly right: number;
};

type NodeRectsGroupedByKind = {
  readonly freeMaterial?: MoveMaterialElemRect;
  readonly completedMaterial?: MoveMaterialElemRect;
  readonly faces: ReadonlyMap<number, MoveMaterialElemRect>;
  readonly queues: ReadonlyMap<string, MoveMaterialElemRect>;

  readonly material: ReadonlyArray<[MoveMaterialElemRect, Readonly<api.IInProcessMaterial>]>;
};

function groupMatByKind(allNodes: AllMoveMaterialNodes<MoveMaterialElemRect>): NodeRectsGroupedByKind {
  let freeMaterial: MoveMaterialElemRect | undefined;
  let completedMaterial: MoveMaterialElemRect | undefined;
  const faces = new Map<number, MoveMaterialElemRect>();
  const queues = new Map<string, MoveMaterialElemRect>();
  const material = new Array<[MoveMaterialElemRect, Readonly<api.IInProcessMaterial>]>();

  for (const node of allNodes.values()) {
    switch (node.type) {
      case MoveMaterialNodeKindType.FreeMaterialZone:
        freeMaterial = node.elem;
        break;
      case MoveMaterialNodeKindType.CompletedCollapsedMaterialZone:
      case MoveMaterialNodeKindType.CompletedExpandedMaterialZone:
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
  container: MoveMaterialElemRect | null | undefined,
  allNodes: AllMoveMaterialNodes<MoveMaterialElemRect>
): ReadonlyArray<MoveMaterialArrow> {
  if (!container) {
    return [];
  }
  const byKind = groupMatByKind(allNodes);
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
      case api.ActionType.UnloadToInProcess:
        if (
          mat.action.type === api.ActionType.UnloadToCompletedMaterial &&
          (!mat.action.unloadIntoQueue || mat.action.unloadIntoQueue === "")
        ) {
          arrows.push({
            fromX: rect.right,
            fromY: rect.top + rect.height / 2,
            toY: rect.top + rect.height / 2,
            toX: byKind.completedMaterial ? byKind.completedMaterial.left + 2 : container.right - 10,
            curveDirection: 1,
          });
        } else {
          let dest: MoveMaterialElemRect | undefined;
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
            fromX: rect.left,
            fromY: rect.top + rect.height / 2,
            toX: dest !== undefined ? dest.right - 5 : container.left + 2,
            toY: dest !== undefined ? dest.top + 20 * (lastSlotUsed + 1) : rect.top + rect.height / 2,
            curveDirection: 1,
          });
        }
        break;
      case api.ActionType.Loading:
        if (mat.action.loadOntoFace) {
          if (mat.location.type === api.LocType.OnPallet) {
            if (
              mat.location.pallet === mat.action.loadOntoPallet &&
              mat.location.face === mat.action.loadOntoFace
            ) {
              // reclamp
              arrows.push({
                fromX: rect.right,
                fromY: rect.top + (rect.height * 3) / 4,
                toX: rect.left + (rect.width * 7) / 8,
                toY: rect.bottom,
                curveDirection: -1,
              });
            } else {
              // move to different face
              const face = byKind.faces.get(mat.action.loadOntoFace);
              if (face) {
                arrows.push({
                  fromX: rect.left,
                  fromY: rect.top + rect.height / 2,
                  toX: rect.left,
                  toY: face.top - 20,
                  curveDirection: 1,
                });
              }
            }
          } else {
            // loading from queues or free Material
            const face = byKind.faces.get(mat.action.loadOntoFace);
            if (face !== undefined) {
              const faceSpotsUsed = faceDestUsed.get(mat.action.loadOntoFace) ?? 0;
              faceDestUsed.set(mat.action.loadOntoFace, faceSpotsUsed + 1);
              arrows.push({
                fromX: rect.right,
                fromY: rect.top + rect.height / 2,
                toX: face.left + 20,
                toY: face.top + 50 + 20 * faceSpotsUsed,
                curveDirection: -1,
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
