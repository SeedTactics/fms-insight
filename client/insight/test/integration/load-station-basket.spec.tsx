import { describe, expect, test } from "vitest";

import LoadStation from "../../src/components/station-monitor/LoadStation.js";
import * as api from "../../src/network/api.js";
import { renderInsightPage } from "./framework.js";
import {
  activeBasketRegionTestId,
  basketRegionTestId,
  basketsColumnTestId,
  createBasket,
  createCurrentStatus,
  createMaterial,
  queueRegionTestId,
  region,
} from "./load-station-testkit.js";

describe("load station with active basket", () => {
  test("places basket-only loading, queued material, and staging baskets in the correct regions", async () => {
    const currentStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
        }),
        createBasket({
          basketId: 8,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadStationStaging,
            locationNum: 1,
            zone: 1,
          }),
        }),
      ],
      material: [
        createMaterial({
          materialID: 301,
          jobUnique: "",
          partName: "Queue To Basket",
          process: 0,
          path: 1,
          serial: "QB-1",
          location: {
            type: api.LocType.InQueue,
            currentQueue: "Queue A",
            queuePosition: 0,
          },
          action: {
            type: api.ActionType.Loading,
            loadFromBasketId: 7,
            processAfterLoad: 1,
          },
        }),
        createMaterial({
          materialID: 302,
          jobUnique: "",
          partName: "Basket To Queue",
          process: 1,
          path: 1,
          serial: "BQ-1",
          location: {
            type: api.LocType.InBasket,
            basketId: 7,
            basketSlot: 0,
          },
          action: {
            type: api.ActionType.UnloadToInProcess,
            unloadIntoQueue: "Queue B",
          },
        }),
        createMaterial({
          materialID: 303,
          jobUnique: "JOB-303",
          partName: "Queue Existing",
          process: 1,
          path: 1,
          serial: "QE-3",
          location: {
            type: api.LocType.InQueue,
            currentQueue: "Queue B",
            queuePosition: 0,
          },
          action: {
            type: api.ActionType.Waiting,
          },
        }),
        createMaterial({
          materialID: 304,
          jobUnique: "",
          partName: "Staging Basket",
          process: 1,
          path: 1,
          serial: "SB-1",
          location: {
            type: api.LocType.InBasket,
            basketId: 8,
            basketSlot: 0,
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

    const activeBasket = region(screen, activeBasketRegionTestId);
    const queueA = region(screen, queueRegionTestId("Queue A"));
    const queueB = region(screen, queueRegionTestId("Queue B"));
    const queueC = region(screen, queueRegionTestId("Queue C"));
    const basketsColumn = region(screen, basketsColumnTestId);
    const stagingBasket = region(screen, basketRegionTestId(8));

    await expect.element(activeBasket).toHaveTextContent("Basket 7");
    await expect.element(activeBasket).toHaveTextContent("Basket To Queue");
    await expect.element(activeBasket).toHaveTextContent("Unload into queue Queue B");
    await expect.element(activeBasket).not.toHaveTextContent("Queue To Basket");

    await expect.element(queueA).toHaveTextContent("Queue A");
    await expect.element(queueA).toHaveTextContent("Queue To Basket");
    await expect.element(queueA).toHaveTextContent("Load into Basket 7");
    await expect.element(queueA).not.toHaveTextContent("Basket To Queue");

    await expect.element(queueB).toHaveTextContent("Queue B");
    await expect.element(queueB).toHaveTextContent("Queue Existing");
    await expect.element(queueB).not.toHaveTextContent("Basket To Queue");

    await expect.element(queueC).toHaveTextContent("Queue C");
    await expect.element(queueC).not.toHaveTextContent("Queue Existing");
    await expect.element(queueC).not.toHaveTextContent("Queue To Basket");

    await expect.element(basketsColumn).toHaveTextContent("Baskets");
    await expect.element(stagingBasket).toHaveTextContent("Basket 8");
    await expect.element(stagingBasket).toHaveTextContent("Staging Basket");
    await expect.element(stagingBasket).not.toHaveTextContent("Basket To Queue");
  });
});
