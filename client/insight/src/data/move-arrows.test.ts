import { HashMap } from "@seedtactics/immutable-collections";
import { expect, test } from "vitest";

import * as api from "../network/api.js";
import {
  type AllMoveMaterialNodes,
  computeArrows,
  type MoveMaterialElemRect,
  type MoveMaterialNodeKind,
  MoveMaterialNodeKindType,
  uniqueIdForNodeKind,
} from "./move-arrows.js";

test("points basket loads to the exact destination slot", () => {
  const material = new api.InProcessMaterial({
    materialID: 42,
    jobUnique: "job",
    partName: "part",
    process: 1,
    path: 1,
    signaledInspections: [],
    location: new api.InProcessMaterialLocation({
      type: api.LocType.InQueue,
      currentQueue: "transfer",
      queuePosition: 0,
    }),
    action: new api.InProcessMaterialAction({
      type: api.ActionType.LoadingToBasket,
      loadToBasketId: 7,
      loadToBasketSlot: 3,
    }),
  });
  const materialKind: MoveMaterialNodeKind = {
    type: MoveMaterialNodeKindType.Material,
    material,
  };
  const completedKind: MoveMaterialNodeKind = {
    type: MoveMaterialNodeKindType.CompletedCollapsedMaterialZone,
  };
  const basketKind: MoveMaterialNodeKind = {
    type: MoveMaterialNodeKindType.BasketZone,
    basketId: 7,
  };
  const slotKind: MoveMaterialNodeKind = {
    type: MoveMaterialNodeKindType.BasketSlotZone,
    basketId: 7,
    slot: 3,
  };
  const nodes: AllMoveMaterialNodes<MoveMaterialElemRect> = HashMap.empty<
    string,
    MoveMaterialNodeKind & { readonly elem: MoveMaterialElemRect }
  >()
    .set(uniqueIdForNodeKind(materialKind), { ...materialKind, elem: rect(10, 100, 100, 80) })
    .set(uniqueIdForNodeKind(completedKind), { ...completedKind, elem: rect(900, 0, 100, 500) })
    .set(uniqueIdForNodeKind(basketKind), { ...basketKind, elem: rect(300, 20, 500, 400) })
    .set(uniqueIdForNodeKind(slotKind), { ...slotKind, elem: rect(550, 220, 200, 150) });

  expect(computeArrows(rect(0, 0, 1_000, 500), nodes)).toEqual([
    {
      fromX: 110,
      fromY: 140,
      toX: 570,
      toY: 270,
      curveDirection: -1,
    },
  ]);
});

function rect(left: number, top: number, width: number, height: number): MoveMaterialElemRect {
  return { left, top, width, height, right: left + width, bottom: top + height };
}
