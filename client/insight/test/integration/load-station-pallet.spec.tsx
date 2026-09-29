import { describe, expect, test } from "vitest";

import LoadStation from "../../src/components/station-monitor/LoadStation.js";
import * as api from "../../src/network/api.js";
import { renderInsightPage } from "./framework.js";
import {
  basketRegionTestId,
  basketsColumnTestId,
  completedRegionTestId,
  createBasket,
  createCompletedUnloadLog,
  createCurrentStatus,
  createMaterial,
  createPallet,
  palletFaceRegionTestId,
  queueRegionTestId,
  region,
} from "./load-station-testkit.js";

describe("load station with active pallet", () => {
  test("places basket-to-pallet loading and pallet-to-basket unloading in the correct regions", async () => {
    const currentStatus = createCurrentStatus({
      pallets: [
        createPallet({
          palletNum: 1,
          currentPalletLocation: {
            loc: api.PalletLocationEnum.LoadUnload,
            group: "L/U",
            num: 1,
          },
          numFaces: 2,
          faceNames: ["Face 1", "Face 2"],
        }),
      ],
      baskets: [
        createBasket({
          basketId: 9,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadStationStaging,
            locationNum: 1,
            zone: 1,
          }),
        }),
        createBasket({
          basketId: 10,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadStationStaging,
            locationNum: 1,
            zone: 2,
          }),
        }),
      ],
      material: [
        createMaterial({
          materialID: 111,
          jobUnique: "",
          partName: "Basket Load",
          process: 0,
          path: 1,
          serial: "BL-1",
          location: {
            type: api.LocType.InBasket,
            basketId: 9,
            basketSlot: 1,
          },
          action: {
            type: api.ActionType.Loading,
            loadFromBasketId: 9,
            loadOntoPalletNum: 1,
            loadOntoFace: 1,
            processAfterLoad: 1,
          },
        }),
        createMaterial({
          materialID: 112,
          jobUnique: "",
          partName: "Basket Unload",
          process: 1,
          path: 1,
          serial: "BU-1",
          location: {
            type: api.LocType.OnPallet,
            palletNum: 1,
            face: 2,
          },
          action: {
            type: api.ActionType.UnloadToInProcess,
            unloadToBasketId: 10,
            unloadToBasketSlot: 2,
          },
        }),
        createMaterial({
          materialID: 113,
          jobUnique: "JOB-113",
          partName: "Target Basket Existing",
          process: 1,
          path: 1,
          serial: "TB-1",
          location: {
            type: api.LocType.InBasket,
            basketId: 10,
            basketSlot: 1,
          },
          action: {
            type: api.ActionType.Waiting,
          },
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed={false} />,
      {
        currentStatus,
      },
    );

    const face1 = region(screen, palletFaceRegionTestId(1));
    const face2 = region(screen, palletFaceRegionTestId(2));
    const basketsColumn = region(screen, basketsColumnTestId);
    const sourceBasket = region(screen, basketRegionTestId(9));
    const targetBasket = region(screen, basketRegionTestId(10));

    await expect.element(face1).not.toMatchTextContent("Basket Load");
    await expect.element(face2).toMatchTextContent("Basket Unload");
    await expect.element(face2).toMatchTextContent("Unload to Basket 10 slot B");
    await expect.element(face2).not.toMatchTextContent("Basket Load");

    await expect.element(basketsColumn).toMatchTextContent("Baskets");
    await expect.element(sourceBasket).toMatchTextContent("Basket 9");
    await expect.element(sourceBasket).toMatchTextContent("Basket Load");
    await expect.element(sourceBasket).toMatchTextContent("Load from Basket 9 to Face 1");
    await expect.element(sourceBasket).not.toMatchTextContent("Basket Unload");

    await expect.element(targetBasket).toMatchTextContent("Basket 10");
    await expect.element(targetBasket).toMatchTextContent("Target Basket Existing");
    await expect.element(targetBasket).not.toMatchTextContent("Basket Unload");
  });

  test("places queued, pallet-face, and explicit queue material in the correct regions", async () => {
    const currentStatus = createCurrentStatus({
      pallets: [
        createPallet({
          palletNum: 1,
          currentPalletLocation: {
            loc: api.PalletLocationEnum.LoadUnload,
            group: "L/U",
            num: 1,
          },
          numFaces: 2,
          faceNames: ["Face 1", "Face 2"],
        }),
      ],
      material: [
        createMaterial({
          materialID: 101,
          jobUnique: "",
          partName: "Queue Load",
          process: 0,
          path: 1,
          serial: "QL-1",
          location: {
            type: api.LocType.InQueue,
            currentQueue: "Queue A",
            queuePosition: 0,
          },
          action: {
            type: api.ActionType.Loading,
            loadOntoPalletNum: 1,
            loadOntoFace: 1,
            processAfterLoad: 1,
          },
        }),
        createMaterial({
          materialID: 102,
          jobUnique: "",
          partName: "Face Transfer",
          process: 1,
          path: 1,
          serial: "FT-1",
          location: {
            type: api.LocType.OnPallet,
            palletNum: 1,
            face: 1,
          },
          action: {
            type: api.ActionType.Loading,
            loadOntoPalletNum: 1,
            loadOntoFace: 2,
          },
        }),
        createMaterial({
          materialID: 103,
          jobUnique: "",
          partName: "Unload Queue",
          process: 2,
          path: 1,
          serial: "UQ-1",
          location: {
            type: api.LocType.OnPallet,
            palletNum: 1,
            face: 2,
          },
          action: {
            type: api.ActionType.UnloadToInProcess,
            unloadIntoQueue: "Queue B",
          },
        }),
        createMaterial({
          materialID: 104,
          jobUnique: "JOB-104",
          partName: "Queued Existing",
          process: 1,
          path: 1,
          serial: "QE-1",
          location: {
            type: api.LocType.InQueue,
            currentQueue: "Queue B",
            queuePosition: 0,
          },
          action: {
            type: api.ActionType.Waiting,
          },
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={["Queue C"]} completed={false} />,
      {
        currentStatus,
      },
    );

    const face1 = region(screen, palletFaceRegionTestId(1));
    const face2 = region(screen, palletFaceRegionTestId(2));
    const queueA = region(screen, queueRegionTestId("Queue A"));
    const queueB = region(screen, queueRegionTestId("Queue B"));
    const queueC = region(screen, queueRegionTestId("Queue C"));

    await expect.element(face1).toMatchTextContent("Face Transfer");
    await expect.element(face1).toMatchTextContent("Transfer to Face 2");
    await expect.element(face1).not.toMatchTextContent("Queue Load");
    await expect.element(face1).not.toMatchTextContent("Unload Queue");

    await expect.element(face2).toMatchTextContent("Unload Queue");
    await expect.element(face2).toMatchTextContent("Unload into queue Queue B");
    await expect.element(face2).not.toMatchTextContent("Face Transfer");
    await expect.element(face2).not.toMatchTextContent("Queued Existing");

    await expect.element(queueA).toMatchTextContent("Queue A");
    await expect.element(queueA).toMatchTextContent("Queue Load");
    await expect.element(queueA).toMatchTextContent("Load to Face 1");
    await expect.element(queueA).not.toMatchTextContent("Face Transfer");

    await expect.element(queueB).toMatchTextContent("Queue B");
    await expect.element(queueB).toMatchTextContent("Queued Existing");
    await expect.element(queueB).not.toMatchTextContent("Unload Queue");

    await expect.element(queueC).toMatchTextContent("Queue C");
    await expect.element(queueC).not.toMatchTextContent("Queue Load");
    await expect.element(queueC).not.toMatchTextContent("Queued Existing");
  });

  test("keeps completed material hidden when the completed column is collapsed", async () => {
    const currentStatus = createCurrentStatus({
      pallets: [
        createPallet({
          palletNum: 1,
          currentPalletLocation: {
            loc: api.PalletLocationEnum.LoadUnload,
            group: "L/U",
            num: 1,
          },
          numFaces: 1,
          faceNames: ["Face 1"],
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed={false} />,
      {
        currentStatus,
        last30Log: [
          createCompletedUnloadLog({
            counter: 1,
            materialId: 201,
            partName: "Completed Hidden",
            serial: "CH-1",
            process: 2,
            numProcesses: 2,
          }),
        ],
      },
    );

    const completed = region(screen, completedRegionTestId);

    await expect.element(completed).toMatchTextContent("Completed");
    await expect.element(completed).not.toMatchTextContent("Completed Hidden");
  });

  test("shows recently completed material when the completed column is expanded", async () => {
    const currentStatus = createCurrentStatus({
      pallets: [
        createPallet({
          palletNum: 1,
          currentPalletLocation: {
            loc: api.PalletLocationEnum.LoadUnload,
            group: "L/U",
            num: 1,
          },
          numFaces: 1,
          faceNames: ["Face 1"],
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed={true} />,
      {
        currentStatus,
        last30Log: [
          createCompletedUnloadLog({
            counter: 2,
            materialId: 202,
            partName: "Completed Visible",
            serial: "CV-1",
            process: 2,
            numProcesses: 2,
          }),
        ],
      },
    );

    const completed = region(screen, completedRegionTestId);

    await expect.element(completed).toMatchTextContent("Completed");
    await expect.element(completed).toMatchTextContent("Completed Visible");
    await expect.element(completed).not.toMatchTextContent("Queue Load");
    await expect.element(completed).not.toMatchTextContent("Face Transfer");
  });
});
