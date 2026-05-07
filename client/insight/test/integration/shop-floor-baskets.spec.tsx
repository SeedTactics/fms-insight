import { describe, expect, test } from "vitest";

import LoadStation from "../../src/components/station-monitor/LoadStation.js";
import { AllMaterial } from "../../src/components/operations/AllMaterial.js";
import { SystemOverviewPage } from "../../src/components/station-monitor/SystemOverview.js";
import { renderInsightPage } from "./framework.js";
import { createMixedBasketFixture, createPalletOnlyFixture } from "./basket-fixtures.js";

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
