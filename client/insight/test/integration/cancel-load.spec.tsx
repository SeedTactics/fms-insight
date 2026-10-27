import { Suspense, useState } from "react";
import { page, userEvent } from "vitest/browser";
import { afterEach, describe, expect, test, vi } from "vitest";

import { CancelLoadButton } from "../../src/components/station-monitor/CancelLoadButton.js";
import { QuarantineMatButton } from "../../src/components/station-monitor/QuarantineButton.js";
import { MaterialDialog } from "../../src/components/station-monitor/Material.js";
import { AddToQueueButton } from "../../src/components/station-monitor/QueuesAddMaterial.js";
import { MultiMaterialDialog } from "../../src/components/station-monitor/BulkRawMaterial.js";
import {
  InvalidateCycleDialogButton,
  type InvalidateCycleState,
} from "../../src/components/station-monitor/InvalidateCycle.js";
import { onLoadCurrentSt } from "../../src/cell-status/loading.js";
import { materialDialogOpen } from "../../src/cell-status/material-details.js";
import { LogBackend, registerNetworkBackend } from "../../src/network/backend.js";
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

function MutatingMaterialActions() {
  const [invalidateState, setInvalidateState] = useState<InvalidateCycleState | null>(null);

  return (
    <MaterialDialog
      buttons={
        <>
          <QuarantineMatButton />
          <CancelLoadButton />
          <AddToQueueButton
            st={{ toQueue: null, enteredOperator: null, newMaterialTy: null }}
            queueNames={["Queue B"]}
            onClose={() => {}}
          />
          <InvalidateCycleDialogButton
            st={invalidateState}
            setState={setInvalidateState}
            onClose={() => {}}
          />
        </>
      }
    />
  );
}

const raw = (slot: number) =>
  createMaterial({
    materialID: -1,
    jobUnique: "JOB-1",
    partName: "Raw Part",
    process: 0,
    path: 1,
    location: { type: api.LocType.Free },
    action: {
      type: api.ActionType.LoadingToBasket,
      workId: "station-work",
      loadToBasketId: 4,
      loadToBasketSlot: slot,
      processAfterLoad: 1,
      loadCancellationId: `raw-slot-${slot}`,
    },
  });

describe("cancel load", () => {
  test.each([false, true])(
    "a blocking alarm retains quarantine facts without offering operation instructions: %s",
    async (quarantineAfterUnload) => {
      const base: Readonly<api.IInProcessMaterial> = loadingMaterial({ materialId: 101 });
      const material = new api.InProcessMaterial({ ...base, quarantineAfterUnload });
      const status: Readonly<api.ICurrentStatus> = createCurrentStatus({ material: [material] });
      const alarm = new api.CurrentStatusAlarm({
        code: "status-refresh-failed",
        message: "Showing retained status.",
        impact: api.AlarmImpact.SuspendChanges,
      });
      vi.spyOn(window, "fetch").mockImplementation(async () => new Response("[]"));
      registerNetworkBackend();
      const screen = await renderInsightPage(<MaterialDialog />, {
        currentStatus: { ...status, alarmDetails: [alarm] },
        seedStore: (store) =>
          store.set(materialDialogOpen, { type: "InProcMat", inproc: material }),
      });
      await expect
        .element(screen.getByText(alarm.message, { exact: true }))
        .not.toBeInTheDocument();
      const quarantineNotice = screen.getByText("Quarantine requested", { exact: true });
      const operationNotice = screen.getByText(
        /This material is part of the current load\/unload operation/,
      );
      if (quarantineAfterUnload) await expect.element(quarantineNotice).toBeVisible();
      else await expect.element(quarantineNotice).not.toBeInTheDocument();
      await expect.element(operationNotice).not.toBeInTheDocument();

      screen.store.set(onLoadCurrentSt, status);
      await expect.element(screen.getByRole("alert")).not.toBeInTheDocument();
      if (quarantineAfterUnload) {
        await expect.element(quarantineNotice).toBeVisible();
        await expect.element(operationNotice).not.toBeInTheDocument();
      } else await expect.element(operationNotice).toBeVisible();
    },
  );

  test("an open quarantine dialog checks the captured material's latest basket", async () => {
    const material = createMaterial({
      materialID: 101,
      jobUnique: "JOB-1",
      partName: "Part",
      process: 1,
      path: 1,
      location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 1 },
      action: { type: api.ActionType.Waiting },
    });
    const status: Readonly<api.ICurrentStatus> = createCurrentStatus({
      material: [material],
      jobs: { "JOB-1": api.ActiveJob.fromJS({ ProcsAndPaths: [{ paths: [{}] }] }) },
    });
    registerNetworkBackend();
    const fetch = vi.spyOn(window, "fetch").mockResolvedValue(new Response(null, { status: 204 }));
    const screen = await renderInsightPage(<QuarantineMatButton />, {
      currentStatus: status,
      fmsInfo: { quarantineQueue: "Quarantine" },
      seedStore: (store) => store.set(materialDialogOpen, { type: "InProcMat", inproc: material }),
    });
    await screen.getByRole("button", { name: "Signal for quarantine", exact: true }).click();
    const dialog = screen.getByRole("dialog");
    const confirm = dialog.getByRole("button", { name: "Signal for quarantine", exact: true });
    await expect.element(confirm).toBeEnabled();
    const captured: Readonly<api.IInProcessMaterial> = material;
    const moved = new api.InProcessMaterial({
      ...captured,
      location: new api.InProcessMaterialLocation({
        type: api.LocType.InBasket,
        basketId: 8,
        basketSlot: 1,
      }),
    });
    const alarm = new api.CurrentStatusAlarm({
      code: "basket-observation",
      message: "Basket 8 observation unavailable.",
      impact: api.AlarmImpact.SuspendChanges,
      basketId: 8,
    });
    screen.store.set(onLoadCurrentSt, { ...status, material: [moved], alarmDetails: [alarm] });
    await expect.element(confirm).toBeDisabled();
    await userEvent.hover(confirm, { force: true });
    await expect.element(page.getByRole("tooltip")).toHaveTextContent("System alarm");
    await expect.element(dialog.getByText(alarm.message, { exact: true })).not.toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
    screen.store.set(onLoadCurrentSt, { ...status, material: [moved] });
    await expect.element(confirm).toBeEnabled();
    await confirm.click();
    await expect.element(dialog).not.toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining("/material/101/signal-quarantine"),
      expect.objectContaining({ method: "PUT" }),
    );
  });

  test("bulk removal checks the latest scope while keeping the captured material IDs", async () => {
    const materialBase: Readonly<api.IInProcessMaterial> = loadingMaterial({ materialId: 101 });
    const material = new api.InProcessMaterial({
      ...materialBase,
      action: new api.InProcessMaterialAction({ type: api.ActionType.Waiting }),
    });
    const status: Readonly<api.ICurrentStatus> = createCurrentStatus({ material: [material] });
    registerNetworkBackend();
    vi.spyOn(LogBackend, "logForMaterials").mockResolvedValue([]);
    const fetch = vi.spyOn(window, "fetch").mockResolvedValue(new Response(null, { status: 204 }));
    const close = vi.fn();
    const screen = await renderInsightPage(
      <MultiMaterialDialog material={[material]} closeDialog={close} operator="Operator A" />,
      { currentStatus: status },
    );
    const dialog = screen.getByRole("dialog");
    await dialog.getByRole("button", { name: "Remove Material", exact: true }).click();
    await dialog.getByRole("spinbutton", { name: "Quantity to Remove" }).fill("1");
    const confirm = dialog.getByRole("button", { name: "Remove 1 material", exact: true });
    const captured: Readonly<api.IInProcessMaterial> = material;
    const moved = new api.InProcessMaterial({
      ...captured,
      location: new api.InProcessMaterialLocation({ type: api.LocType.InBasket, basketId: 8 }),
    });
    const alarm = new api.CurrentStatusAlarm({
      code: "basket-observation",
      message: "Basket 8 observation unavailable.",
      impact: api.AlarmImpact.SuspendChanges,
      basketId: 8,
    });
    screen.store.set(onLoadCurrentSt, { ...status, material: [moved], alarmDetails: [alarm] });
    await expect.element(confirm).toBeDisabled();
    await userEvent.hover(confirm, { force: true });
    await expect.element(page.getByRole("tooltip")).toHaveTextContent("System alarm");
    expect(fetch).not.toHaveBeenCalled();
    screen.store.set(onLoadCurrentSt, { ...status, material: [moved] });
    await expect.element(confirm).toBeEnabled();
    await confirm.click();
    await expect.poll(() => close.mock.calls.length).toBe(1);
    expect(fetch).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({ body: "[101]" }),
    );
  });

  test("an alarm arriving in an open cancellation dialog disables submission until recovery", async () => {
    const material = loadingMaterial({ materialId: 101, cancellationId: "saved-load" });
    const fetch = vi.spyOn(window, "fetch").mockResolvedValue(new Response(null, { status: 204 }));
    registerNetworkBackend();
    const screen = await renderCancelLoadButton(material, [material]);
    await screen.getByRole("button", { name: "Cancel Load" }).click();
    const dialog = screen.getByRole("dialog");
    const confirm = dialog.getByRole("button", { name: "Cancel Load", exact: true });
    await expect.element(confirm).toBeEnabled();
    const status: Readonly<api.ICurrentStatus> = createCurrentStatus({ material: [material] });
    screen.store.set(onLoadCurrentSt, {
      ...status,
      alarmDetails: [
        new api.CurrentStatusAlarm({
          code: "status-refresh-failed",
          message: "Showing retained status.",
          impact: api.AlarmImpact.SuspendChanges,
        }),
      ],
    });
    await expect
      .element(dialog.getByText("Showing retained status.", { exact: true }))
      .not.toBeInTheDocument();
    await expect.element(confirm).toBeDisabled();
    await userEvent.hover(confirm, { force: true });
    await expect.element(page.getByRole("tooltip")).toHaveTextContent("System alarm");
    expect(fetch).not.toHaveBeenCalled();
    // Read-only inspection and dismissing the dialog remain available.
    await expect.element(dialog.getByRole("textbox", { name: "Reason (optional)" })).toBeEnabled();
    await expect.element(dialog.getByRole("button", { name: "Keep Load" })).toBeEnabled();
    screen.store.set(onLoadCurrentSt, status);
    await expect.element(confirm).toBeEnabled();
    await confirm.click();
    await expect.element(dialog).not.toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({
        method: "PUT",
        body: '{"ExpectedLoadCancellationId":"saved-load"}',
      }),
    );
  });

  test("scoped cancellation checks every member of the captured atomic group", async () => {
    const selected = raw(1);
    const peerBase: Readonly<api.IInProcessMaterial> = raw(2);
    const peerAction: Readonly<api.IInProcessMaterialAction> = peerBase.action;
    const peer = new api.InProcessMaterial({
      ...peerBase,
      action: new api.InProcessMaterialAction({
        ...peerAction,
        loadToBasketId: 8,
        loadCancellationId: selected.action.loadCancellationId,
      }),
    });
    const screen = await renderCancelLoadButton(selected, [selected, peer]);
    await screen.getByRole("button", { name: "Cancel Load" }).click();
    const dialog = screen.getByRole("dialog");
    const confirm = dialog.getByRole("button", { name: "Cancel Load", exact: true });
    const status: Readonly<api.ICurrentStatus> = createCurrentStatus({
      material: [selected, peer],
    });
    const alarm = new api.CurrentStatusAlarm({
      code: "basket-observation",
      message: "Basket 8 observation unavailable.",
      impact: api.AlarmImpact.SuspendChanges,
      activity: api.AlarmActivity.BasketLoadStation,
      basketId: 8,
    });
    screen.store.set(onLoadCurrentSt, { ...status, alarmDetails: [alarm] });
    await expect.element(confirm).toBeDisabled();
    await userEvent.hover(confirm, { force: true });
    await expect.element(page.getByRole("tooltip")).toHaveTextContent("System alarm");
    await expect.element(dialog.getByText(alarm.message, { exact: true })).not.toBeInTheDocument();
    // The dialog still represents the original group if the live material view changes.
    const empty: Readonly<api.ICurrentStatus> = createCurrentStatus();
    screen.store.set(onLoadCurrentSt, { ...empty, alarmDetails: [alarm] });
    await expect.element(confirm).toBeDisabled();
    await userEvent.hover(confirm, { force: true });
    await expect.element(page.getByRole("tooltip")).toHaveTextContent("System alarm");
    await expect.element(dialog.getByRole("list")).toBeVisible();
    const alarmData: Readonly<api.ICurrentStatusAlarm> = alarm;
    screen.store.set(onLoadCurrentSt, {
      ...status,
      alarmDetails: [new api.CurrentStatusAlarm({ ...alarmData, basketId: 9 })],
    });
    await expect.element(confirm).toBeEnabled();
  });

  test("an open quarantine dialog uses the same blocking rule and retains its material", async () => {
    const materialBase: Readonly<api.IInProcessMaterial> = loadingMaterial({ materialId: 101 });
    const material = new api.InProcessMaterial({
      ...materialBase,
      action: new api.InProcessMaterialAction({ type: api.ActionType.Waiting }),
    });
    const statusBase: Readonly<api.ICurrentStatus> = createCurrentStatus({ material: [material] });
    const status: Readonly<api.ICurrentStatus> = new api.CurrentStatus({
      ...statusBase,
      queues: { "Queue A": new api.QueueInfo({ role: api.QueueRole.RawMaterial }) },
    });
    const screen = await renderInsightPage(
      <Suspense fallback={<div>Loading</div>}>
        <QuarantineMatButton />
      </Suspense>,
      {
        currentStatus: status,
        fmsInfo: { quarantineQueue: "Quarantine" },
        seedStore: (store) =>
          store.set(materialDialogOpen, { type: "InProcMat", inproc: material }),
      },
    );
    await screen.getByRole("button", { name: "Quarantine" }).click();
    const dialog = screen.getByRole("dialog");
    const confirm = dialog.getByRole("button", { name: "Quarantine", exact: true });
    await expect.element(confirm).toBeEnabled();
    screen.store.set(onLoadCurrentSt, {
      ...status,
      alarmDetails: [
        new api.CurrentStatusAlarm({
          code: "status-refresh-failed",
          message: "Showing retained status.",
          impact: api.AlarmImpact.SuspendChanges,
        }),
      ],
    });
    await expect.element(confirm).toBeDisabled();
    await userEvent.hover(confirm, { force: true });
    await expect.element(page.getByRole("tooltip")).toHaveTextContent("System alarm");
    await expect
      .element(dialog.getByText("Showing retained status.", { exact: true }))
      .not.toBeInTheDocument();
    await expect.element(dialog.getByRole("button", { name: "Cancel", exact: true })).toBeEnabled();
    screen.store.set(onLoadCurrentSt, status);
    await expect.element(confirm).toBeEnabled();
  });

  test("is only available for an instruction with a cancellation ID", async () => {
    const material = loadingMaterial({ materialId: 101, serial: "SERIAL-101" });
    const screen = await renderCancelLoadButton(material, [material]);

    await expect
      .element(screen.getByRole("button", { name: "Cancel Load" }))
      .not.toBeInTheDocument();
  });

  test("blocks competing actions and explains machine-control cancellation", async () => {
    const material = loadingMaterial({ materialId: 101, serial: "SERIAL-101" });
    vi.spyOn(window, "fetch").mockImplementation(async () => new Response("[]", { status: 200 }));
    registerNetworkBackend();

    const screen = await renderInsightPage(<MutatingMaterialActions />, {
      currentStatus: createCurrentStatus({ material: [material] }),
      fmsInfo: {
        allowInvalidateMaterialOnQueuesPage: true,
      },
      seedStore: (store) => store.set(materialDialogOpen, { type: "InProcMat", inproc: material }),
    });

    await expect
      .element(
        screen.getByText(
          "This material is part of the current load/unload operation. Complete the operation or cancel it at the machine control before making other changes.",
        ),
      )
      .toBeVisible();
    await expect
      .element(screen.getByRole("button", { name: "Cancel Load" }))
      .not.toBeInTheDocument();
    await expect
      .element(screen.getByRole("button", { name: "Quarantine" }))
      .not.toBeInTheDocument();
    await expect
      .element(screen.getByRole("button", { name: /Add To Queue/ }))
      .not.toBeInTheDocument();
    await expect.element(screen.getByRole("button", { name: /Swap/ })).not.toBeInTheDocument();
    await expect
      .element(screen.getByRole("button", { name: /Invalidate Cycle/ }))
      .not.toBeInTheDocument();
    await expect
      .element(screen.getByRole("button", { name: /Remove from Queue/ }))
      .not.toBeInTheDocument();
    await expect.element(screen.getByRole("button", { name: "Close" })).toBeVisible();
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

    await screen.getByRole("button", { name: "Cancel Load" }).click();
    const dialog = screen.getByRole("dialog");
    await expect.element(dialog).toMatchTextContent("SERIAL-101");
    await expect.element(dialog).toMatchTextContent("Material ID 102");
    await expect.element(dialog).not.toMatchTextContent("SERIAL-103");
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

  test("anonymous raw cancellation resolves the selected slot rather than the first placeholder", async () => {
    const selected = raw(2);
    const fetch = vi.spyOn(window, "fetch").mockResolvedValue(new Response(null, { status: 204 }));
    registerNetworkBackend();
    const screen = await renderCancelLoadButton(selected, [raw(1), raw(1), selected, raw(2)]);
    await screen.getByRole("button", { name: "Cancel Load" }).click();
    const dialog = screen.getByRole("dialog");
    await expect.element(dialog).not.toMatchTextContent("Material ID -1");
    expect(dialog.getByRole("listitem").all()).toHaveLength(2);
    await dialog.getByRole("button", { name: "Cancel Load" }).click();
    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/jobs/material/-1/cancel-load?operName=Operator%20A",
      expect.objectContaining({
        method: "PUT",
        body: '{"ExpectedLoadCancellationId":"raw-slot-2"}',
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
    await screen.getByRole("button", { name: "Cancel Load" }).click();
    const dialog = screen.getByRole("dialog");
    await dialog.getByRole("button", { name: "Cancel Load" }).click();

    await expect.element(dialog).toBeVisible();
    await expect.element(dialog.getByRole("alert")).toMatchTextContent("Stale load instruction");
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
    await screen.getByRole("button", { name: "Cancel Load" }).click();
    const dialog = screen.getByRole("dialog");
    await expect.element(dialog).toMatchTextContent("Material ID 102");

    screen.store.set(onLoadCurrentSt, createCurrentStatus());

    await expect.element(dialog).toMatchTextContent("Material ID 102");
    await dialog.getByRole("button", { name: "Cancel Load" }).click();
    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/jobs/material/101/cancel-load?operName=Operator%20A",
      expect.objectContaining({
        body: '{"ExpectedLoadCancellationId":"initial-instruction"}',
      }),
    );
  });
});
