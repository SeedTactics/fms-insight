import { expect, test } from "vitest";

import * as api from "../network/api.js";
import { groupInterchangeableMaterial } from "./queue-material.js";

function loadingToSlot(
  materialID: number,
  slot: number,
  { serial, process = 0 }: { readonly serial?: string; readonly process?: number } = {},
): api.InProcessMaterial {
  return new api.InProcessMaterial({
    materialID,
    jobUnique: "job",
    partName: "part",
    process,
    path: 1,
    serial,
    signaledInspections: [],
    location: new api.InProcessMaterialLocation({ type: api.LocType.Free }),
    action: new api.InProcessMaterialAction({
      type: api.ActionType.LoadingToBasket,
      loadToBasketId: 7,
      loadToBasketSlot: slot,
      processAfterLoad: 1,
    }),
  });
}

test("groups unserialed raw material by destination and keeps other material separate", () => {
  const toA = [loadingToSlot(-1, 1), loadingToSlot(-1, 1), loadingToSlot(-1, 1)];
  const toB = [loadingToSlot(-1, 2), loadingToSlot(-1, 2)];
  const inProcess = loadingToSlot(5, 1, { process: 1 });
  const serialized = loadingToSlot(-1, 1, { serial: "SER-1" });

  const groups = groupInterchangeableMaterial(
    [toA[0], inProcess, toB[0], toA[1], toB[1], toA[2], serialized],
    (m) => m.process === 0,
  );

  expect(groups).toEqual([toA, [inProcess], toB, [serialized]]);
});
