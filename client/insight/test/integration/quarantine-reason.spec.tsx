import { afterEach, expect, test, vi } from "vitest";
import { page, userEvent } from "vitest/browser";
import LoadStation from "../../src/components/station-monitor/LoadStation.js";
import { InProcMaterial } from "../../src/components/station-monitor/Material.js";
import { onServerEvent } from "../../src/cell-status/loading.js";
import { registerNetworkBackend, LogBackend } from "../../src/network/backend.js";
import * as api from "../../src/network/api.js";
import { renderInsightPage } from "./framework.js";
import { createBasket, createCurrentStatus, createMaterial } from "./load-station-testkit.js";

afterEach(() => vi.restoreAllMocks());

function signal(counter: number, process: number, note?: string) {
  return api.LogEntry.fromJS({
    counter,
    type: api.LogType.SignalQuarantine,
    endUTC: new Date().toISOString(),
    material: [{ id: 101, proc: process, part: "Part", numproc: 2, face: 1, serial: "TEST-1" }],
    details: note === undefined ? undefined : { note },
  });
}

test("the load station tooltip follows live quarantine reasons while the open dialog keeps its history", async () => {
  registerNetworkBackend();
  vi.spyOn(window, "fetch").mockResolvedValue(new Response("[]"));
  vi.spyOn(LogBackend, "logForMaterial").mockResolvedValue([
    signal(20, 1, "No completed machining record for returned set"),
  ]);
  const material = createMaterial({
    materialID: 101,
    jobUnique: "JOB-1",
    partName: "Part",
    process: 1,
    path: 1,
    serial: "TEST-1",
    quarantineAfterUnload: true,
    signaledInspections: ["Visual"],
    location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 1 },
    action: { type: api.ActionType.UnloadToInProcess },
  });
  const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
    currentStatus: createCurrentStatus({
      material: [material],
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
        }),
      ],
    }),
  });
  const warning = screen.getByRole("img", { name: "Material warnings" });
  await userEvent.hover(warning);
  await expect
    .element(page.getByRole("tooltip"))
    .toHaveTextContent("Inspect: Visual; Material will quarantine after unload");
  screen.store.set(onServerEvent, {
    evt: { logEntry: signal(20, 1, "No completed machining record for returned set") },
    now: new Date(),
    expire: true,
  });
  await expect
    .element(page.getByRole("tooltip"))
    .toHaveTextContent(
      "Inspect: Visual; Material will quarantine after unload: No completed machining record for returned set",
    );

  await screen.getByText("Serial: TEST-1").click();
  const dialog = screen.getByRole("dialog");
  const notice = dialog.getByText(
    "Quarantine requested: No completed machining record for returned set",
    { exact: true },
  );
  await expect.element(notice).toBeVisible();
  screen.store.set(onServerEvent, {
    evt: { logEntry: signal(30, 1, "Updated quality concern") },
    now: new Date(),
    expire: true,
  });
  await expect.element(notice).toBeVisible();
  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  await userEvent.hover(warning);
  await expect
    .element(page.getByRole("tooltip"))
    .toHaveTextContent(
      "Inspect: Visual; Material will quarantine after unload: Updated quality concern",
    );
  screen.store.set(onServerEvent, {
    evt: { logEntry: signal(40, 1) },
    now: new Date(),
    expire: true,
  });
  await expect
    .element(page.getByRole("tooltip"))
    .toHaveTextContent("Inspect: Visual; Material will quarantine after unload");
});

test.each([api.ActionType.Loading, api.ActionType.LoadingToBasket])(
  "the material card uses the quarantine destination process during automated %s",
  async (type) => {
    const material = createMaterial({
      materialID: 101,
      jobUnique: "JOB-1",
      partName: "Part",
      process: 1,
      path: 1,
      quarantineAfterUnload: true,
      location: { type: api.LocType.InQueue, currentQueue: "Queue", queuePosition: 0 },
      action: { type, automatedTransfer: true, processAfterLoad: 2, pathAfterLoad: 1 },
    });
    const screen = await renderInsightPage(<InProcMaterial mat={material} />, {
      currentStatus: createCurrentStatus({ material: [material] }),
      last30Log: [signal(10, 2, "Destination reason"), signal(20, 1, "Other process reason")],
    });
    await userEvent.hover(screen.getByRole("img", { name: "Material warnings" }));
    await expect
      .element(page.getByRole("tooltip"))
      .toHaveTextContent("Material will quarantine after unload: Destination reason");
  },
);
