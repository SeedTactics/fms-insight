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
      type: "curve",
      fromX: 110,
      fromY: 140,
      toX: 570,
      toY: 295,
      curveDirection: -1,
    },
  ]);
});

test.each([true, false])(
  "draws basket loads only when their destination is displayed (%s)",
  (destinationShown) => {
    const materialKind: MoveMaterialNodeKind = {
      type: MoveMaterialNodeKindType.Material,
      material: basketMaterial(
        42,
        new api.InProcessMaterialLocation({
          type: api.LocType.InQueue,
          currentQueue: "transfer",
          queuePosition: 0,
        }),
        toSlot(3),
      ),
    };
    const basketKind: MoveMaterialNodeKind = {
      type: MoveMaterialNodeKindType.BasketZone,
      basketId: destinationShown ? 7 : 4,
    };
    const completedKind: MoveMaterialNodeKind = {
      type: MoveMaterialNodeKindType.CompletedCollapsedMaterialZone,
    };
    const nodes: AllMoveMaterialNodes<MoveMaterialElemRect> = HashMap.empty<
      string,
      MoveMaterialNodeKind & { readonly elem: MoveMaterialElemRect }
    >()
      .set(uniqueIdForNodeKind(materialKind), { ...materialKind, elem: rect(10, 100, 100, 80) })
      .set(uniqueIdForNodeKind(completedKind), { ...completedKind, elem: rect(900, 0, 100, 500) })
      .set(uniqueIdForNodeKind(basketKind), { ...basketKind, elem: rect(300, 20, 500, 400) });

    expect(computeArrows(rect(0, 0, 1_000, 500), nodes)).toEqual(
      destinationShown
        ? [{ type: "curve", fromX: 110, fromY: 140, toX: 320, toY: 70, curveDirection: -1 }]
        : [],
    );
  },
);

test("points queue loads only to the face of their destination pallet", () => {
  const thisPalletKind: MoveMaterialNodeKind = {
    type: MoveMaterialNodeKindType.Material,
    material: loadingOnto(1, 1),
  };
  const otherPalletKind: MoveMaterialNodeKind = {
    type: MoveMaterialNodeKindType.Material,
    material: loadingOnto(2, 5),
  };
  const completedKind: MoveMaterialNodeKind = {
    type: MoveMaterialNodeKindType.CompletedCollapsedMaterialZone,
  };
  const faceKind: MoveMaterialNodeKind = {
    type: MoveMaterialNodeKindType.PalletFaceZone,
    palletNum: 1,
    face: 1,
  };
  const nodes: AllMoveMaterialNodes<MoveMaterialElemRect> = HashMap.empty<
    string,
    MoveMaterialNodeKind & { readonly elem: MoveMaterialElemRect }
  >()
    .set(uniqueIdForNodeKind(thisPalletKind), { ...thisPalletKind, elem: rect(10, 100, 100, 80) })
    .set(uniqueIdForNodeKind(otherPalletKind), {
      ...otherPalletKind,
      elem: rect(10, 200, 100, 80),
    })
    .set(uniqueIdForNodeKind(completedKind), { ...completedKind, elem: rect(900, 0, 100, 500) })
    .set(uniqueIdForNodeKind(faceKind), { ...faceKind, elem: rect(300, 20, 500, 400) });

  expect(computeArrows(rect(0, 0, 1_000, 500), nodes)).toEqual([
    {
      type: "curve",
      fromX: 110,
      fromY: 140,
      toX: 320,
      toY: 70,
      curveDirection: -1,
    },
  ]);
});

test("arrows into a basket slot spread along it while a slot leaves or stays with one arrow", () => {
  const queued = [1, 2, 3].map((id) =>
    basketMaterial(
      id,
      new api.InProcessMaterialLocation({
        type: api.LocType.InQueue,
        currentQueue: "Q",
        queuePosition: id,
      }),
      toSlot(1),
    ),
  );
  const unloading = [4, 5].map((id) =>
    inBasket(
      id,
      2,
      new api.InProcessMaterialAction({
        type: api.ActionType.UnloadToInProcess,
        unloadIntoQueue: "Q",
      }),
    ),
  );
  const staying = [6, 7].map((id) => inBasket(id, 3, toSlot(3)));
  const materialRects: ReadonlyArray<[api.InProcessMaterial, MoveMaterialElemRect]> = [
    [queued[0], rect(10, 100, 100, 80)],
    [queued[1], rect(10, 200, 100, 80)],
    [queued[2], rect(10, 300, 100, 80)],
    [unloading[0], rect(300, 150, 100, 80)],
    [unloading[1], rect(420, 150, 100, 80)],
    [staying[0], rect(300, 260, 100, 80)],
    [staying[1], rect(420, 260, 100, 80)],
  ];
  const zones: ReadonlyArray<[MoveMaterialNodeKind, MoveMaterialElemRect]> = [
    [{ type: MoveMaterialNodeKindType.CompletedCollapsedMaterialZone }, rect(900, 0, 100, 500)],
    [{ type: MoveMaterialNodeKindType.QueueZone, queue: "Q" }, rect(150, 0, 100, 500)],
    [
      { type: MoveMaterialNodeKindType.BasketSlotZone, basketId: 7, slot: 1 },
      rect(300, 20, 500, 100),
    ],
  ];
  const nodes = materialRects
    .map(([material, elem]): [MoveMaterialNodeKind, MoveMaterialElemRect] => [
      { type: MoveMaterialNodeKindType.Material, material },
      elem,
    ])
    .concat(zones)
    .reduce<AllMoveMaterialNodes<MoveMaterialElemRect>>(
      (acc, [kind, elem]) => acc.set(uniqueIdForNodeKind(kind), { ...kind, elem }),
      HashMap.empty(),
    );

  expect(computeArrows(rect(0, 0, 1_000, 500), nodes)).toEqual([
    { type: "curve", fromX: 110, fromY: 140, toX: 320, toY: 70, curveDirection: -1 },
    { type: "curve", fromX: 110, fromY: 240, toX: 360, toY: 70, curveDirection: -1 },
    { type: "curve", fromX: 110, fromY: 340, toX: 400, toY: 70, curveDirection: -1 },
    { type: "curve", fromX: 300, fromY: 190, toX: 245, toY: 20, curveDirection: 1 },
    { type: "loop", centerX: 280, centerY: 300, radius: 12 },
  ]);
});

function inBasket(
  materialID: number,
  slot: number,
  action: api.IInProcessMaterialAction,
): api.InProcessMaterial {
  return basketMaterial(
    materialID,
    new api.InProcessMaterialLocation({
      type: api.LocType.InBasket,
      basketId: 7,
      basketSlot: slot,
    }),
    action,
  );
}

function toSlot(slot: number): api.InProcessMaterialAction {
  return new api.InProcessMaterialAction({
    type: api.ActionType.LoadingToBasket,
    loadToBasketId: 7,
    loadToBasketSlot: slot,
  });
}

function basketMaterial(
  materialID: number,
  location: api.InProcessMaterialLocation,
  action: api.IInProcessMaterialAction,
): api.InProcessMaterial {
  return new api.InProcessMaterial({
    materialID,
    jobUnique: "job",
    partName: "part",
    process: 1,
    path: 1,
    signaledInspections: [],
    location,
    action: new api.InProcessMaterialAction(action),
  });
}

function loadingOnto(materialID: number, palletNum: number): api.InProcessMaterial {
  return new api.InProcessMaterial({
    materialID,
    jobUnique: "job",
    partName: "part",
    process: 1,
    path: 1,
    signaledInspections: [],
    location: new api.InProcessMaterialLocation({
      type: api.LocType.InQueue,
      currentQueue: "Queue1",
      queuePosition: materialID,
    }),
    action: new api.InProcessMaterialAction({
      type: api.ActionType.Loading,
      loadOntoPalletNum: palletNum,
      loadOntoFace: 1,
      processAfterLoad: 1,
      pathAfterLoad: 1,
    }),
  });
}

function rect(left: number, top: number, width: number, height: number): MoveMaterialElemRect {
  return { left, top, width, height, right: left + width, bottom: top + height };
}
