import { expect, it } from "vitest";
import { createStore } from "jotai";

import { fakeBasketCycle } from "../../test/events.fake.js";
import { onLoadLast30Log } from "./loading.js";
import { last30BasketCycles } from "./basket-cycles.js";

it("aggregates basket cycles from the event log", () => {
  const store = createStore();
  store.set(onLoadLast30Log, [
    ...fakeBasketCycle({
      counter: 8100,
      basket: 77,
      part: "tray",
      proc: 1,
      numMats: 2,
      time: new Date(Date.UTC(2026, 0, 10, 12, 0, 0)),
      elapsedMin: 18,
      activeMin: 14,
    }),
  ]);

  const baskets = store.get(last30BasketCycles);
  expect(baskets.get(77)?.get(8100)).toMatchObject({
    cntr: 8100,
    y: 18,
    active: 14,
  });
});
