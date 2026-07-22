import { describe, expect, test } from "vitest";

import LoadStation from "../../src/components/station-monitor/LoadStation.js";
import { AllMaterial } from "../../src/components/operations/AllMaterial.js";
import { SystemOverviewPage } from "../../src/components/station-monitor/SystemOverview.js";
import { secondsSinceEpochAtom } from "../../src/cell-status/current-status.js";
import * as api from "../../src/network/api.js";
import { renderInsightPage } from "./framework.js";
import { createMixedBasketFixture, createPalletOnlyFixture } from "./basket-fixtures.js";
import { createBasket, createCurrentStatus, createMaterial } from "./load-station-testkit.js";

describe("basket shop-floor screens", () => {
  test("system overview shows tray-specific active, floating, and storage sections", async () => {
    const fixture = createMixedBasketFixture();
    const screen = await renderInsightPage(
      <SystemOverviewPage ignoreOperator whiteBackground />,
      fixture.data,
    );

    await expect.element(screen.getByText("Prep Cell")).toBeVisible();
    await expect.element(screen.getByText("Tray Cell")).toBeVisible();
    await expect.element(screen.locator).toHaveTextContent(/Tray\s*21/);
    await expect.element(screen.locator).toHaveTextContent(/Tray\s*22/);
    await expect.element(screen.getByRole("heading", { name: "Tray 25" })).toBeVisible();
    await expect.element(screen.getByText("Tray Storage")).toBeVisible();
    await expect.element(screen.getByText("Empty: 1")).toBeVisible();
    await expect.element(screen.getByText("Filled: 1")).toBeVisible();
  });

  test("system overview stays pallet-only when no basket data exists", async () => {
    const fixture = createPalletOnlyFixture();
    const screen = await renderInsightPage(
      <SystemOverviewPage ignoreOperator whiteBackground />,
      fixture.data,
    );

    await expect.element(screen.getByText("Prep Cell")).toBeVisible();
    await expect.element(screen.locator).not.toHaveTextContent("Basket Storage");
    await expect.element(screen.locator).not.toHaveTextContent("Tray Storage");
    await expect.element(screen.locator).not.toHaveTextContent("Basket ");
  });

  test("system overview shows elapsed time for basket loading", async () => {
    const currentStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
          emptySlots: [1],
        }),
      ],
      material: [
        createMaterial({
          materialID: -1,
          jobUnique: "JOB",
          partName: "Part",
          process: 0,
          path: 1,
          location: { type: api.LocType.Free },
          action: {
            type: api.ActionType.LoadingToBasket,
            workId: "load-work",
            loadToBasketId: 7,
            loadToBasketSlot: 1,
            processAfterLoad: 1,
            elapsedLoadUnloadTime: "PT5M",
          },
        }),
      ],
    });
    const statusSeconds = Math.floor(currentStatus.timeOfCurrentStatusUTC.getTime() / 1000);

    const screen = await renderInsightPage(<SystemOverviewPage ignoreOperator whiteBackground />, {
      currentStatus,
      fmsInfo: { loadStationNames: { "1": "Basket Cell" } },
      seedStore: (store) => store.set(secondsSinceEpochAtom, statusSeconds),
    });

    await expect.element(screen.getByText("Basket Cell")).toBeVisible();
    await expect.element(screen.getByText("5:00")).toBeVisible();
  });

  test("all material renders tray bins alongside queues and pallets", async () => {
    const fixture = createMixedBasketFixture();
    const screen = await renderInsightPage(<AllMaterial displaySystemBins />, fixture.data);

    await expect.element(screen.getByText("Prep Cell")).toBeVisible();
    await expect.element(screen.getByText("Trays")).toBeVisible();
    await expect.element(screen.getByText("Tray 21 (LoadUnload 2)")).toBeVisible();
    await expect.element(screen.getByText("Load into Tray 21")).toBeVisible();
    await expect.element(screen.getByText("Unload into queue Queue Beta")).toBeVisible();
    await expect.element(screen.getByText("In Tray 22")).toBeVisible();
    await expect.element(screen.getByText("In Tray 25")).toBeVisible();
    await expect.element(screen.getByText("Pallets")).toBeVisible();
    await expect.element(screen.getByText("Queues")).toBeVisible();
  });

  test("all material hides tray bins on pallet-only sites", async () => {
    const fixture = createPalletOnlyFixture();
    const screen = await renderInsightPage(<AllMaterial displaySystemBins />, fixture.data);

    await expect.element(screen.getByText("Pallets")).toBeVisible();
    await expect.element(screen.getByText("Queues")).toBeVisible();
    await expect.element(screen.locator).not.toHaveTextContent("Baskets");
    await expect.element(screen.locator).not.toHaveTextContent("Trays");
  });

  test("load station does not render basket headings on pallet-only sites", async () => {
    const fixture = createPalletOnlyFixture();
    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={["Queue Alpha", "Queue Beta"]} completed={false} />,
      fixture.data,
    );

    await expect.element(screen.getByText("Pallet 11")).toBeVisible();
    await expect.element(screen.getByText("Queue Alpha")).toBeVisible();
    await expect.element(screen.locator).not.toHaveTextContent("Baskets");
    await expect.element(screen.locator).not.toHaveTextContent("Trays");
  });
});
