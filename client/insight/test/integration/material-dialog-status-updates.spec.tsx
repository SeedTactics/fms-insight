import { describe, expect, test } from "vitest";

import { MaterialDialog } from "../../src/components/station-monitor/Material.js";
import { materialDialogOpen, type MaterialToShow } from "../../src/cell-status/material-details.js";
import { onServerEvent } from "../../src/cell-status/loading.js";
import { CurrentStatus, ServerEvent } from "../../src/network/api.js";
import { renderInsightPage } from "./framework.js";
import { createMixedBasketFixture } from "./basket-fixtures.js";

// Some cells publish a complete current status every second. The open material dialog must keep its
// content on screen instead of dropping back to its loading fallback on every status event.
describe("material dialog during current status updates", () => {
  const fixture = createMixedBasketFixture();
  const mat = fixture.materials.queueToTray;
  const opened: ReadonlyArray<readonly [string, MaterialToShow]> = [
    ["material summary", { type: "MatSummary", summary: mat }],
    ["in-process material", { type: "InProcMat", inproc: mat }],
  ];

  test.each(opened)("opened as %s keeps its content", async (_, toShow) => {
    const screen = await renderInsightPage(<MaterialDialog />, fixture.data);
    screen.store.set(materialDialogOpen, toShow);

    const dialog = screen.getByRole("dialog");
    await expect.element(dialog).toMatchTextContent("Workorder: WO-TRAY");

    let fallbacks = 0;
    const observer = new MutationObserver(() => {
      if (dialog.element().textContent?.includes("Loading material")) fallbacks++;
    });
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    try {
      for (let i = 0; i < 3; i++) {
        const status = new CurrentStatus({
          ...fixture.data.currentStatus!,
          timeOfCurrentStatusUTC: new Date(),
        });
        screen.store.set(onServerEvent, {
          evt: new ServerEvent({ newCurrentStatus: status }),
          now: new Date(),
          expire: true,
        });
        await new Promise((resolve) => setTimeout(resolve, 100));
      }
    } finally {
      observer.disconnect();
    }

    expect(fallbacks).toBe(0);
    await expect.element(dialog).toMatchTextContent("Workorder: WO-TRAY");
  });
});
