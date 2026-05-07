import { addHours, subDays } from "date-fns";
import { describe, expect, test, vi } from "vitest";

vi.mock("../../src/components/analysis/CycleChart.js", () => ({
  CycleChart: () => <div>mock cycle chart</div>,
}));

import * as api from "../../src/network/api.js";
import App from "../../src/components/App.js";
import { fakeBasketCycle, fakeBasketLoadOrUnload } from "../events.fake.js";
import { RouteLocation } from "../../src/components/routes.js";
import { renderInsightPage } from "./framework.js";
import { BasketCycleChart } from "../../src/components/analysis/BasketCycleCards.js";
import { PartLoadStationCycleChart } from "../../src/components/analysis/PartCycleCards.js";
import { RecentStationCycleChart } from "../../src/components/operations/RecentStationCycles.js";
import { OutlierCycles } from "../../src/components/operations/Outliers.js";
import { createMixedBasketFixture, createPalletOnlyFixture } from "./basket-fixtures.js";

function outlierBasketLogs(): ReadonlyArray<Readonly<api.ILogEntry>> {
  const baseTime = addHours(subDays(new Date(), 1), 9);
  const logs: Array<Readonly<api.ILogEntry>> = [];
  for (let idx = 0; idx < 6; idx += 1) {
    logs.push(
      ...fakeBasketLoadOrUnload({
        counter: 9800 + idx * 10,
        basket: 21,
        part: "tray-part",
        proc: 1,
        material: [
          new api.LogMaterial({
            id: 2000 + idx,
            uniq: "JOB-OUTLIER",
            part: "tray-part",
            proc: 1,
            path: 1,
            numproc: 2,
            face: 1,
            serial: `INLIER-${idx + 1}`,
            workorder: "WO-OUTLIER",
          }),
        ],
        isLoad: true,
        lulNum: 2,
        time: addHours(baseTime, idx),
        elapsedMin: 4,
        activeMin: 4,
      }),
    );
  }

  logs.push(
    ...fakeBasketLoadOrUnload({
      counter: 9900,
      basket: 21,
      part: "tray-part",
      proc: 1,
      material: [
        new api.LogMaterial({
          id: 2999,
          uniq: "JOB-OUTLIER",
          part: "tray-part",
          proc: 1,
          path: 1,
          numproc: 2,
          face: 1,
          serial: "OUTLIER-1",
          workorder: "WO-OUTLIER",
        }),
      ],
      isLoad: true,
      lulNum: 2,
      time: addHours(baseTime, 9),
      elapsedMin: 15,
      activeMin: 15,
    }),
  );

  return logs;
}

describe("basket reports", () => {
  test("renders basket cycle analysis with custom basket naming", async () => {
    const screen = await renderInsightPage(<BasketCycleChart />, {
      fmsInfo: { basketName: "Tray" },
      last30Log: [
        ...fakeBasketCycle({
          counter: 9001,
          basket: 42,
          part: "tray-part",
          proc: 1,
          numMats: 2,
          time: new Date(Date.UTC(2026, 0, 10, 12, 0, 0)),
          elapsedMin: 20,
          activeMin: 15,
        }),
      ],
    });

    await expect.element(screen.getByText("Tray Cycles")).toBeVisible();
    await expect.element(screen.getByText("Any Tray")).toBeVisible();
    await expect.element(screen.locator).not.toHaveTextContent("Pallet Cycles");
  });

  test("shows the carrier filter on load cycle reports when basket events exist", async () => {
    const screen = await renderInsightPage(<PartLoadStationCycleChart />, {
      fmsInfo: { basketName: "Tray" },
      last30Log: [
        ...fakeBasketLoadOrUnload({
          counter: 9100,
          basket: 42,
          part: "tray-part",
          proc: 1,
          numMats: 1,
          isLoad: true,
          time: new Date(Date.UTC(2026, 0, 10, 14, 0, 0)),
          elapsedMin: 4,
          activeMin: 3,
        }),
      ],
    });

    await expect.element(screen.getByText("Any Carrier")).toBeVisible();
  });

  test("hides the basket cycles navigation item when no basket cycles exist", async () => {
    const fixture = createPalletOnlyFixture();
    const screen = await renderInsightPage(<App />, {
      ...fixture.data,
      route: { route: RouteLocation.Analysis_LoadCycles },
    });

    await screen.getByRole("combobox").first().click();
    await expect.element(screen.locator).not.toHaveTextContent("Basket Cycles");
  });

  test("shows the basket cycles navigation item with fallback basket naming", async () => {
    const fixture = createMixedBasketFixture();
    const screen = await renderInsightPage(<App />, {
      ...fixture.data,
      last30Log: [
        ...fakeBasketCycle({
          counter: 9200,
          basket: 42,
          part: "tray-part",
          proc: 1,
          numMats: 1,
          time: new Date(Date.UTC(2026, 0, 10, 15, 0, 0)),
          elapsedMin: 18,
          activeMin: 12,
        }),
      ],
      fmsInfo: {
        ...fixture.data.fmsInfo,
        basketName: "",
      },
      route: { route: RouteLocation.Analysis_LoadCycles },
    });

    await screen.getByRole("combobox").first().click();
    await expect.element(screen.getByRole("option", { name: "Basket Cycles" })).toBeVisible();
  });

  test("shows the basket cycles navigation item when basket naming is present", async () => {
    const screen = await renderInsightPage(<App />, {
      fmsInfo: { basketName: "Tray" },
      last30Log: [
        ...fakeBasketCycle({
          counter: 9201,
          basket: 42,
          part: "tray-part",
          proc: 1,
          numMats: 1,
          time: new Date(Date.UTC(2026, 0, 10, 16, 0, 0)),
          elapsedMin: 16,
          activeMin: 11,
        }),
      ],
      route: { route: RouteLocation.Analysis_LoadCycles },
    });

    await screen.getByRole("combobox").first().click();
    await expect.element(screen.getByRole("option", { name: "Tray Cycles" })).toBeVisible();
  });

  test("hides the carrier filter on recent load cycles when no basket cycles exist", async () => {
    const fixture = createPalletOnlyFixture();
    const screen = await renderInsightPage(<RecentStationCycleChart ty="labor" />, fixture.data);

    await expect.element(screen.locator).not.toHaveTextContent("Any Carrier");
  });

  test("filters recent load cycles by basket carrier and uses display names", async () => {
    const fixture = createMixedBasketFixture();
    const screen = await renderInsightPage(<RecentStationCycleChart ty="labor" />, fixture.data);

    await expect.element(screen.getByText("Any Carrier")).toBeVisible();
    await screen.getByRole("combobox").first().click();
    await screen.getByRole("option", { name: "Table" }).click();
    await screen.getByRole("combobox").nth(2).click();
    await screen.getByRole("option", { name: "Tray" }).click();

    await expect.element(screen.locator).toHaveTextContent("Tray Cell");
    await expect.element(screen.locator).toHaveTextContent("Tray 21");
    await expect.element(screen.locator).not.toHaveTextContent("Pallet 1");
  });

  test("shows only the basket outlier in the outlier report", async () => {
    const screen = await renderInsightPage(<OutlierCycles outlierTy="labor" />, {
      fmsInfo: {
        basketName: "Tray",
        loadStationNames: { "2": "Tray Cell" },
      },
      last30Log: outlierBasketLogs(),
    });

    await expect.element(screen.locator).toHaveTextContent("OUTLIER-1");
    await expect.element(screen.locator).toHaveTextContent("Tray Cell");
    await expect.element(screen.locator).not.toHaveTextContent("INLIER-1");
  });
});
