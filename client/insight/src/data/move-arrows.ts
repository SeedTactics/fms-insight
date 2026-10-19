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
  BasketZone,
  BasketSlotZone,
}

export type MoveMaterialNodeKind =
  | {
      readonly type: MoveMaterialNodeKindType.Material;
      readonly material: Readonly<api.IInProcessMaterial> | null;
      readonly identifier?: string;
    }
  | { readonly type: MoveMaterialNodeKindType.FreeMaterialZone }
  | { readonly type: MoveMaterialNodeKindType.CompletedCollapsedMaterialZone }
  | { readonly type: MoveMaterialNodeKindType.CompletedExpandedMaterialZone }
  | {
      readonly type: MoveMaterialNodeKindType.PalletFaceZone;
      readonly palletNum: number;
      readonly face: number;
    }
  | {
      readonly type: MoveMaterialNodeKindType.QueueZone;
      readonly queue: string;
    }
  | {
      readonly type: MoveMaterialNodeKindType.BasketZone;
      readonly basketId: number;
    }
  | {
      readonly type: MoveMaterialNodeKindType.BasketSlotZone;
      readonly basketId: number;
      readonly slot: number;
    };

export type MoveMaterialIdentifier = string;

export function uniqueIdForNodeKind(kind: MoveMaterialNodeKind): MoveMaterialIdentifier {
  switch (kind.type) {
    case MoveMaterialNodeKindType.Material:
      return "Material-" + (kind.identifier ?? (kind.material?.materialID ?? -1).toString());
    case MoveMaterialNodeKindType.FreeMaterialZone:
      return "FreeMaterialZone";
    case MoveMaterialNodeKindType.CompletedCollapsedMaterialZone:
      return "CompletedCollapsedMaterialZone";
    case MoveMaterialNodeKindType.CompletedExpandedMaterialZone:
      return "CompletedExpandedMaterialZone";
    case MoveMaterialNodeKindType.PalletFaceZone:
      return `PalletFaceZone-${kind.palletNum}-${kind.face}`;
    case MoveMaterialNodeKindType.QueueZone:
      return "QueueZone-" + kind.queue;
    case MoveMaterialNodeKindType.BasketZone:
      return "BasketZone-" + kind.basketId.toString();
    case MoveMaterialNodeKindType.BasketSlotZone:
      return `BasketSlotZone-${kind.basketId}-${kind.slot}`;
  }
}

export function memoPropsForNodeKind(kind: MoveMaterialNodeKind): ReadonlyArray<unknown> {
  switch (kind.type) {
    case MoveMaterialNodeKindType.Material:
      return [kind.type, kind.material, kind.identifier];
    case MoveMaterialNodeKindType.FreeMaterialZone:
    case MoveMaterialNodeKindType.CompletedCollapsedMaterialZone:
    case MoveMaterialNodeKindType.CompletedExpandedMaterialZone:
      return [kind.type, null];
    case MoveMaterialNodeKindType.PalletFaceZone:
      return [kind.type, kind.palletNum, kind.face];
    case MoveMaterialNodeKindType.QueueZone:
      return [kind.type, kind.queue];
    case MoveMaterialNodeKindType.BasketZone:
      return [kind.type, kind.basketId];
    case MoveMaterialNodeKindType.BasketSlotZone:
      return [kind.type, kind.basketId, kind.slot];
  }
}

export type AllMoveMaterialNodes<T> = HashMap<
  MoveMaterialIdentifier,
  MoveMaterialNodeKind & { readonly elem: T }
>;

export type MoveMaterialArrow =
  | {
      readonly type: "curve";
      readonly fromX: number;
      readonly fromY: number;
      readonly toX: number;
      readonly toY: number;
      readonly curveDirection: number; // 1 or -1
    }
  | {
      // material staying where it is: a small circle pointing back at itself
      readonly type: "loop";
      readonly centerX: number;
      readonly centerY: number;
      readonly radius: number;
    };

type CurveArrow = Omit<Extract<MoveMaterialArrow, { type: "curve" }>, "type">;

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
  readonly faces: ReadonlyMap<string, MoveMaterialElemRect>;
  readonly queues: ReadonlyMap<string, MoveMaterialElemRect>;
  readonly baskets: ReadonlyMap<number, MoveMaterialElemRect>;
  readonly basketSlots: ReadonlyMap<string, MoveMaterialElemRect>;

  readonly material: ReadonlyArray<[MoveMaterialElemRect, Readonly<api.IInProcessMaterial>]>;
};

function faceKey(palletNum: number | undefined, face: number): string {
  return `${palletNum ?? ""}:${face}`;
}

function groupMatByKind(
  allNodes: AllMoveMaterialNodes<MoveMaterialElemRect>,
): NodeRectsGroupedByKind {
  let freeMaterial: MoveMaterialElemRect | undefined;
  let completedMaterial: MoveMaterialElemRect | undefined;
  const faces = new Map<string, MoveMaterialElemRect>();
  const queues = new Map<string, MoveMaterialElemRect>();
  const baskets = new Map<number, MoveMaterialElemRect>();
  const basketSlots = new Map<string, MoveMaterialElemRect>();
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
        faces.set(faceKey(node.palletNum, node.face), node.elem);
        break;
      case MoveMaterialNodeKindType.QueueZone:
        queues.set(node.queue, node.elem);
        break;
      case MoveMaterialNodeKindType.BasketZone:
        baskets.set(node.basketId, node.elem);
        break;
      case MoveMaterialNodeKindType.BasketSlotZone:
        basketSlots.set(`${node.basketId}:${node.slot}`, node.elem);
        break;
      case MoveMaterialNodeKindType.Material:
        if (node.material) {
          material.push([node.elem, node.material]);
        }
        break;
    }
  }

  return { freeMaterial, completedMaterial, faces, queues, baskets, basketSlots, material };
}

// All material in a basket slot moves together, so material leaving or staying in a slot draws a
// single arrow from the combined rectangle of the slot's material.  Material arriving from
// elsewhere keeps an arrow per piece.
function basketSlotGroup(mat: Readonly<api.IInProcessMaterial>): string | null {
  if (
    mat.location.type === api.LocType.InBasket &&
    mat.location.basketId !== undefined &&
    mat.location.basketSlot !== undefined
  ) {
    return `${mat.location.basketId}:${mat.location.basketSlot}`;
  }
  return null;
}

function unionRect(a: MoveMaterialElemRect, b: MoveMaterialElemRect): MoveMaterialElemRect {
  const left = Math.min(a.left, b.left);
  const top = Math.min(a.top, b.top);
  const right = Math.max(a.right, b.right);
  const bottom = Math.max(a.bottom, b.bottom);
  return { left, top, right, bottom, width: right - left, height: bottom - top };
}

function arrowSources(
  material: ReadonlyArray<[MoveMaterialElemRect, Readonly<api.IInProcessMaterial>]>,
): ReadonlyArray<readonly [MoveMaterialElemRect, Readonly<api.IInProcessMaterial>]> {
  const sorted = LazySeq.of(material)
    .sortBy(
      ([box]) => box.left,
      ([box]) => box.top,
    )
    .toRArray();
  const slotRects = LazySeq.of(sorted)
    .collect(([rect, mat]) => {
      const group = basketSlotGroup(mat);
      return group === null ? null : ([group, rect] as const);
    })
    .buildHashMap(
      ([group]) => group,
      (prev: MoveMaterialElemRect | undefined, [, rect]) => (prev ? unionRect(prev, rect) : rect),
    );
  const seen = new Set<string>();
  return sorted.flatMap(([rect, mat]) => {
    const group = basketSlotGroup(mat);
    if (group === null) return [[rect, mat] as const];
    if (seen.has(group)) return [];
    seen.add(group);
    return [[slotRects.get(group) ?? rect, mat] as const];
  });
}

export function computeArrows(
  container: MoveMaterialElemRect | null | undefined,
  allNodes: AllMoveMaterialNodes<MoveMaterialElemRect>,
): ReadonlyArray<MoveMaterialArrow> {
  if (!container) {
    return [];
  }
  const byKind = groupMatByKind(allNodes);
  if (!byKind.completedMaterial) {
    return [];
  }

  const arrows: Array<CurveArrow> = [];
  const loops: Array<Extract<MoveMaterialArrow, { type: "loop" }>> = [];

  const faceDestUsed = new Map<string, number>();
  const queueDestUsed = new Map<string, number>();
  const basketDestUsed = new Map<number, number>();
  const slotDestUsed = new Map<string, number>();
  let lastFreeUsed = 0;

  for (const [rect, mat] of arrowSources(byKind.material)) {
    switch (mat.action.type) {
      case api.ActionType.UnloadToCompletedMaterial:
      case api.ActionType.UnloadToInProcess:
        if (mat.action.unloadToBasketId) {
          const dest = byKind.baskets.get(mat.action.unloadToBasketId);
          const lastSlotUsed = basketDestUsed.get(mat.action.unloadToBasketId) ?? 0;
          basketDestUsed.set(mat.action.unloadToBasketId, lastSlotUsed + 1);
          arrows.push({
            fromX: rect.left,
            fromY: rect.top + rect.height / 2,
            toX: dest !== undefined ? dest.right - 5 : container.left + 2,
            toY:
              dest !== undefined ? dest.top + 20 * (lastSlotUsed + 1) : rect.top + rect.height / 2,
            curveDirection: 1,
          });
        } else if (
          mat.action.type === api.ActionType.UnloadToCompletedMaterial &&
          (!mat.action.unloadIntoQueue || mat.action.unloadIntoQueue === "")
        ) {
          arrows.push({
            fromX: rect.right,
            fromY: rect.top + rect.height / 2,
            toY: rect.top + rect.height / 2,
            toX: byKind.completedMaterial
              ? byKind.completedMaterial.left + 2
              : container.right - 10,
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
            toY:
              dest !== undefined ? dest.top + 20 * (lastSlotUsed + 1) : rect.top + rect.height / 2,
            curveDirection: 1,
          });
        }
        break;
      case api.ActionType.Loading:
        if (mat.action.loadFromBasketId && mat.action.loadOntoFace) {
          // loading from basket onto pallet face
          const key = faceKey(mat.action.loadOntoPalletNum, mat.action.loadOntoFace);
          const face = byKind.faces.get(key);
          if (face !== undefined) {
            const faceSpotsUsed = faceDestUsed.get(key) ?? 0;
            faceDestUsed.set(key, faceSpotsUsed + 1);
            arrows.push({
              fromX: rect.right,
              fromY: rect.top + rect.height / 2,
              toX: face.left + 20,
              toY: face.top + 50 + 20 * faceSpotsUsed,
              curveDirection: -1,
            });
          }
        } else if (mat.action.loadFromBasketId) {
          // basket-only load from a queue into the active basket
          const dest = byKind.baskets.get(mat.action.loadFromBasketId);
          const lastSlotUsed = basketDestUsed.get(mat.action.loadFromBasketId) ?? 0;
          basketDestUsed.set(mat.action.loadFromBasketId, lastSlotUsed + 1);
          arrows.push({
            fromX: rect.right,
            fromY: rect.top + rect.height / 2,
            toX: dest !== undefined ? dest.left + 20 : container.right - 10,
            toY:
              dest !== undefined ? dest.top + 50 + 20 * lastSlotUsed : rect.top + rect.height / 2,
            curveDirection: -1,
          });
        } else if (mat.action.loadOntoFace) {
          if (mat.location.type === api.LocType.OnPallet) {
            if (
              mat.location.palletNum === mat.action.loadOntoPalletNum &&
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
              const face = byKind.faces.get(
                faceKey(mat.action.loadOntoPalletNum, mat.action.loadOntoFace),
              );
              if (face) {
                if (mat.action.loadOntoFace < (mat.location.face ?? 0)) {
                  arrows.push({
                    fromX: rect.left,
                    fromY: rect.top + rect.height / 2,
                    toX: rect.left,
                    toY: face.bottom + 20,
                    curveDirection: -1,
                  });
                } else {
                  arrows.push({
                    fromX: rect.left,
                    fromY: rect.top + rect.height / 2,
                    toX: rect.left,
                    toY: face.top - 20,
                    curveDirection: 1,
                  });
                }
              }
            }
          } else {
            // loading from queues or free Material
            const key = faceKey(mat.action.loadOntoPalletNum, mat.action.loadOntoFace);
            const face = byKind.faces.get(key);
            if (face !== undefined) {
              const faceSpotsUsed = faceDestUsed.get(key) ?? 0;
              faceDestUsed.set(key, faceSpotsUsed + 1);
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
      case api.ActionType.LoadingToBasket: {
        const basketId = mat.action.loadToBasketId;
        if (basketId === undefined) break;
        if (
          mat.location.type === api.LocType.InBasket &&
          mat.location.basketId === basketId &&
          mat.location.basketSlot === mat.action.loadToBasketSlot
        ) {
          loops.push({
            type: "loop",
            centerX: rect.left - 20 - container.left,
            centerY: rect.top + rect.height / 2 - container.top,
            radius: 12,
          });
          break;
        }
        const slotKey = `${basketId}:${mat.action.loadToBasketSlot}`;
        const slotDest =
          mat.action.loadToBasketSlot === undefined ? undefined : byKind.basketSlots.get(slotKey);
        if (slotDest !== undefined) {
          // a slot is a row, so arrows into it are spread along the row
          const spotsUsed = slotDestUsed.get(slotKey) ?? 0;
          slotDestUsed.set(slotKey, spotsUsed + 1);
          arrows.push({
            fromX: rect.right,
            fromY: rect.top + rect.height / 2,
            toX: slotDest.left + 20 + 40 * spotsUsed,
            toY: slotDest.top + slotDest.height / 2,
            curveDirection: -1,
          });
          break;
        }
        const dest = byKind.baskets.get(basketId);
        if (dest === undefined) break;
        const lastSlotUsed = basketDestUsed.get(basketId) ?? 0;
        basketDestUsed.set(basketId, lastSlotUsed + 1);
        arrows.push({
          fromX: rect.right,
          fromY: rect.top + rect.height / 2,
          toX: dest.left + 20,
          toY: dest.top + 50 + 20 * lastSlotUsed,
          curveDirection: -1,
        });
        break;
      }
    }
  }

  return [
    ...arrows.map((arr): MoveMaterialArrow => ({
      type: "curve",
      fromX: arr.fromX - container.left,
      fromY: arr.fromY - container.top,
      toX: arr.toX - container.left,
      toY: arr.toY - container.top,
      curveDirection: arr.curveDirection,
    })),
    ...loops,
  ];
}
