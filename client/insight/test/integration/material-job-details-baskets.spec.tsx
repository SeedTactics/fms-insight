import { Fragment } from "react";
import { describe, expect, test } from "vitest";

import { LogEntries } from "../../src/components/LogEntry.js";
import { JobDetails } from "../../src/components/station-monitor/JobDetails.js";
import { InProcMaterial, MaterialDialog } from "../../src/components/station-monitor/Material.js";
import { renderInsightPage } from "./framework.js";
import { createMixedBasketFixture, createPalletOnlyFixture } from "./basket-fixtures.js";
import { LogType } from "../../src/network/api.js";

describe("basket material and job details", () => {
  test("material cards show basket-related action text and fallback naming", async () => {
    const fixture = createMixedBasketFixture();
    const screen = await renderInsightPage(
      <Fragment>
        <InProcMaterial mat={fixture.materials.queueToTray} />
        <InProcMaterial mat={fixture.materials.trayToQueue} />
        <InProcMaterial mat={fixture.materials.trayToPallet} />
        <InProcMaterial mat={fixture.materials.palletToTray} />
      </Fragment>,
      fixture.data,
    );

    await expect.element(screen.getByText("Load into Tray 21")).toBeVisible();
    await expect.element(screen.getByText("Unload into queue Queue Beta")).toBeVisible();
    await expect.element(screen.getByText("Load from Tray 22 to pal 1")).toBeVisible();
    await expect.element(screen.getByText("Unload to Tray 22 position 2")).toBeVisible();

    const fallback = await renderInsightPage(<InProcMaterial mat={fixture.materials.palletToTray} />, {
      ...fixture.data,
      fmsInfo: { loadStationNames: fixture.data.fmsInfo?.loadStationNames },
    });
    await expect.element(fallback.getByText("Unload to basket 22 position 2")).toBeVisible();
  });

  test("material dialog opens for basket-backed material cards", async () => {
    const fixture = createMixedBasketFixture();
    const screen = await renderInsightPage(
      <Fragment>
        <InProcMaterial mat={fixture.materials.trayToQueue} />
        <MaterialDialog />
      </Fragment>,
      fixture.data,
    );

    await screen.getByText("Serial: TQ-1").click();

    const dialog = screen.getByRole("dialog");
    await expect.element(dialog).toBeVisible();
    await expect.element(dialog).toHaveTextContent("Tray Part - TQ-1");
    await expect.element(dialog).toHaveTextContent("Workorder: WO-TRAY");
  });

  test("log entries render basket cycle and location history text clearly", async () => {
    const fixture = createMixedBasketFixture();
    const screen = await renderInsightPage(
      <LogEntries
        entries={(fixture.data.last30Log ?? []).filter(
          (entry) => entry.type === LogType.BasketCycle || entry.type === LogType.BasketInLocation,
        )}
      />,
      fixture.data,
    );

    await expect.element(screen.locator).toHaveTextContent("Tray 21 completed cycle");
    await expect.element(screen.locator).toHaveTextContent("Tray Cycle");
    await expect.element(screen.locator).toHaveTextContent("Arrive");
    await expect.element(screen.locator).toHaveTextContent("Depart");
    await expect
      .element(screen.locator)
      .toHaveTextContent("Tray 21 arrived at Load Station Staging position 2");
    await expect
      .element(screen.locator)
      .toHaveTextContent("Tray 21 departed from Load Station Staging position 2");
  });

  test("job details shows basket load and unload sections with load station display names", async () => {
    const fixture = createMixedBasketFixture();
    const screen = await renderInsightPage(
      <JobDetails job={fixture.job} checkAnalysisMonth={false} />,
      fixture.data,
    );

    await expect.element(screen.getByText("Basket Load Stations: Tray Cell | 4.0 mins")).toBeVisible();
    await expect.element(screen.getByText("Load Stations: Prep Cell | 6.0 mins")).toBeVisible();
    await expect.element(screen.getByText("Unload Stations: Tray Cell | 5.0 mins")).toBeVisible();
    await expect.element(screen.getByText("Basket Unload Stations: Tray Cell | 3.0 mins")).toBeVisible();
  });

  test("job details omits basket sections when a job has no basket metadata", async () => {
    const fixture = createPalletOnlyFixture();
    const screen = await renderInsightPage(
      <JobDetails job={fixture.job} checkAnalysisMonth={false} />,
      fixture.data,
    );

    await expect.element(screen.getByText("Load Stations: Prep Cell | 5.0 mins")).toBeVisible();
    await expect.element(screen.locator).not.toHaveTextContent("Basket Load Stations");
    await expect.element(screen.locator).not.toHaveTextContent("Basket Unload Stations");
  });
});
