import { it, expect } from "vitest";
import { createStore } from "jotai";
import { fakeBasketLoadOrUnload } from "../../test/events.fake.js";
import { onLoadLast30Log, onLoadSpecificMonthLog } from "./loading.js";
import { fmsInformation } from "../network/server-settings.js";
import {
  basketDisplayName,
  last30HasBasketCycles,
  last30StationCycles,
  loadStationDisplayName,
  specificMonthHasBasketCycles,
  specificMonthStationCycles,
} from "./station-cycles.js";

it("loadStationDisplayName falls back to default when key absent", () => {
  expect(loadStationDisplayName(2, { "1": "Line 1" })).toBe("L/U #2");
  expect(loadStationDisplayName(1, undefined)).toBe("L/U #1");
  expect(loadStationDisplayName(4, { "4": "" })).toBe("L/U #4");
  expect(loadStationDisplayName(3, { "3": "Assembly" })).toBe("Assembly");
});

it("basketDisplayName falls back when missing or empty", () => {
  expect(basketDisplayName(undefined)).toBe("Basket");
  expect(basketDisplayName("")).toBe("Basket");
  expect(basketDisplayName("  ")).toBe("Basket");
  expect(basketDisplayName("Tray")).toBe("Tray");
});

it("derives station labels and basket-cycle presence from current settings and cycles", () => {
  const store = createStore();
  const now = new Date(Date.UTC(2026, 0, 10, 12, 0, 0));

  store.set(fmsInformation, {
    name: "FMS Insight",
    version: "",
    loadStationNames: { "2": "Tray Cell" },
  });
  store.set(
    onLoadLast30Log,
    fakeBasketLoadOrUnload({
      counter: 300,
      part: "part1",
      proc: 1,
      basket: 7,
      lulNum: 2,
      isLoad: true,
      time: now,
      elapsedMin: 4,
      activeMin: 4,
    }),
  );

  expect(store.get(last30HasBasketCycles)).toBe(true);
  expect(store.get(last30StationCycles).get(300)?.stationLabel).toBe("Tray Cell");
});

it("derives basket-cycle presence for the specific month store", () => {
  const store = createStore();
  const now = new Date(Date.UTC(2026, 0, 10, 12, 0, 0));

  store.set(fmsInformation, {
    name: "FMS Insight",
    version: "",
    loadStationNames: { "2": "Tray Cell" },
  });
  store.set(
    onLoadSpecificMonthLog,
    fakeBasketLoadOrUnload({
      counter: 400,
      part: "part1",
      proc: 1,
      basket: 8,
      lulNum: 2,
      isLoad: false,
      time: now,
      elapsedMin: 5,
      activeMin: 5,
    }),
  );

  expect(store.get(specificMonthHasBasketCycles)).toBe(true);
  expect(store.get(specificMonthStationCycles).get(400)?.stationLabel).toBe("Tray Cell");
});

it("drops zero-elapsed basket companion events from station cycles", () => {
  const store = createStore();
  const now = new Date(Date.UTC(2026, 0, 10, 12, 0, 0));

  store.set(
    onLoadLast30Log,
    fakeBasketLoadOrUnload({
      counter: 500,
      part: "part1",
      proc: 1,
      basket: 9,
      lulNum: 1,
      isLoad: true,
      time: now,
      elapsedMin: 0,
      activeMin: 0,
    }),
  );

  expect(store.get(last30HasBasketCycles)).toBe(false);
  expect(store.get(last30StationCycles).has(500)).toBe(false);
});
