import { Suspense } from "react";
import { afterEach, describe, expect, test, vi } from "vitest";

import { CancelLoadButton } from "../../src/components/station-monitor/CancelLoadButton.js";
import { onLoadCurrentSt } from "../../src/cell-status/loading.js";
import { materialDialogOpen } from "../../src/cell-status/material-details.js";
import { registerNetworkBackend } from "../../src/network/backend.js";
import * as api from "../../src/network/api.js";
import { renderInsightPage } from "./framework.js";
import { createCurrentStatus, createMaterial } from "./load-station-testkit.js";

afterEach(() => vi.restoreAllMocks());

function loadingMaterial({
  materialId,
  serial,
  cancellationId,
}: {
  readonly materialId: number;
  readonly serial?: string;
  readonly cancellationId?: string;
}): api.InProcessMaterial {
  return createMaterial({
    materialID: materialId,
    jobUnique: "JOB-1",
    partName: "Part",
    process: 1,
    path: 1,
    serial,
    location: { type: api.LocType.InQueue, currentQueue: "Queue A", queuePosition: materialId },
    action: {
      type: api.ActionType.Loading,
      loadCancellationId: cancellationId,
      loadOntoPalletNum: 1,
      loadOntoFace: 1,
      processAfterLoad: 2,
    },
  });
}

async function renderCancelLoadButton(
  selected: api.InProcessMaterial,
  material: ReadonlyArray<api.InProcessMaterial>,
) {
  return await renderInsightPage(
    <Suspense fallback={<div>Loading</div>}>
      <CancelLoadButton />
    </Suspense>,
    {
      currentStatus: createCurrentStatus({ material }),
      operator: "Operator A",
      seedStore: (store) => store.set(materialDialogOpen, { type: "InProcMat", inproc: selected }),
    },
  );
}

describe("cancel load", () => {
  test("is only available for an instruction with a cancellation ID", async () => {
    const material = loadingMaterial({ materialId: 101, serial: "SERIAL-101" });
    const screen = await renderCancelLoadButton(material, [material]);

    await expect
      .element(screen.getByRole("button", { name: "Cancel the displayed load instruction" }))
      .not.toBeInTheDocument();
  });

  test("shows the cancellation group and submits its displayed token", async () => {
    const selected = loadingMaterial({
      materialId: 101,
      serial: "SERIAL-101",
      cancellationId: "load-instruction-1",
    });
    const peerWithoutSerial = loadingMaterial({
      materialId: 102,
      cancellationId: "load-instruction-1",
    });
    const otherLoad = loadingMaterial({
      materialId: 103,
      serial: "SERIAL-103",
      cancellationId: "other-instruction",
    });
    const fetch = vi.spyOn(window, "fetch").mockResolvedValue(new Response(null, { status: 204 }));
    registerNetworkBackend();

    const screen = await renderCancelLoadButton(selected, [selected, peerWithoutSerial, otherLoad]);

    await screen.getByRole("button", { name: "Cancel the displayed load instruction" }).click();
    const dialog = screen.getByRole("dialog");
    await expect.element(dialog).toHaveTextContent("SERIAL-101");
    await expect.element(dialog).toHaveTextContent("Material ID 102");
    await expect.element(dialog).not.toHaveTextContent("SERIAL-103");
    await dialog.getByRole("textbox", { name: "Reason (optional)" }).fill("Changed schedule");
    await dialog.getByRole("button", { name: "Cancel Load" }).click();

    await expect.element(dialog).not.toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/jobs/material/101/cancel-load?operName=Operator%20A",
      expect.objectContaining({
        method: "PUT",
        body: '{"ExpectedLoadCancellationId":"load-instruction-1","Reason":"Changed schedule"}',
      }),
    );
  });

  test("keeps the dialog open and reports a rejected cancellation", async () => {
    const material = loadingMaterial({
      materialId: 101,
      serial: "SERIAL-101",
      cancellationId: "load-instruction-1",
    });
    vi.spyOn(window, "fetch").mockResolvedValue(
      new Response("Stale load instruction", { status: 409 }),
    );
    registerNetworkBackend();

    const screen = await renderCancelLoadButton(material, [material]);
    await screen.getByRole("button", { name: "Cancel the displayed load instruction" }).click();
    const dialog = screen.getByRole("dialog");
    await dialog.getByRole("button", { name: "Cancel Load" }).click();

    await expect.element(dialog).toBeVisible();
    await expect.element(dialog.getByRole("alert")).toHaveTextContent("Stale load instruction");
  });

  test("uses the selected material, token, and group that were current when confirmation opened", async () => {
    const selected = loadingMaterial({
      materialId: 101,
      serial: "SERIAL-101",
      cancellationId: "initial-instruction",
    });
    const peer = loadingMaterial({ materialId: 102, cancellationId: "initial-instruction" });
    const fetch = vi.spyOn(window, "fetch").mockResolvedValue(new Response(null, { status: 204 }));
    registerNetworkBackend();

    const screen = await renderCancelLoadButton(selected, [selected, peer]);
    await screen.getByRole("button", { name: "Cancel the displayed load instruction" }).click();
    const dialog = screen.getByRole("dialog");
    await expect.element(dialog).toHaveTextContent("Material ID 102");

    screen.store.set(onLoadCurrentSt, createCurrentStatus());

    await expect.element(dialog).toHaveTextContent("Material ID 102");
    await dialog.getByRole("button", { name: "Cancel Load" }).click();
    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/jobs/material/101/cancel-load?operName=Operator%20A",
      expect.objectContaining({
        body: '{"ExpectedLoadCancellationId":"initial-instruction"}',
      }),
    );
  });
});
