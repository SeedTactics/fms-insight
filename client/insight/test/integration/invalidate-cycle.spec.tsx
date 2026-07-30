import { Suspense, useState } from "react";
import { afterEach, describe, expect, test, vi } from "vitest";

import {
  InvalidateCycleDialogButton,
  InvalidateCycleDialogContent,
  type InvalidateCycleState,
} from "../../src/components/station-monitor/InvalidateCycle.js";
import { QuarantineMatButton } from "../../src/components/station-monitor/QuarantineButton.js";
import { AddToQueueButton } from "../../src/components/station-monitor/QueuesAddMaterial.js";
import { materialDialogOpen } from "../../src/cell-status/material-details.js";
import { registerNetworkBackend, setOtherLogBackends } from "../../src/network/backend.js";
import * as api from "../../src/network/api.js";
import { renderInsightPage, type InsightTestData } from "./framework.js";
import { createCurrentStatus, createMaterial } from "./load-station-testkit.js";

afterEach(() => {
  vi.restoreAllMocks();
  setOtherLogBackends([]);
});

function material({
  materialId,
  location = { type: api.LocType.Free },
}: {
  readonly materialId: number;
  readonly location?: api.IInProcessMaterialLocation;
}): api.InProcessMaterial {
  return createMaterial({
    materialID: materialId,
    jobUnique: "JOB-1",
    partName: "Part",
    process: 1,
    path: 1,
    serial: `SERIAL-${materialId}`,
    location,
    action: { type: api.ActionType.Waiting },
  });
}

function statusWithMaterial(materials: ReadonlyArray<api.InProcessMaterial>): api.CurrentStatus {
  return createCurrentStatus({
    material: materials,
    queues: {
      "Queue A": new api.QueueInfo({ role: api.QueueRole.InProcessTransfer }),
    },
  });
}

function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === "string") return input;
  return input instanceof URL ? input.toString() : input.url;
}

function dialogData(selected: api.InProcessMaterial): Pick<InsightTestData, "seedStore"> {
  return {
    seedStore: (store) => store.set(materialDialogOpen, { type: "InProcMat", inproc: selected }),
  };
}

function InvalidateButton({ state = null }: { readonly state?: InvalidateCycleState | null }) {
  const [invalidation, setInvalidation] = useState<InvalidateCycleState | null>(state);
  return (
    <InvalidateCycleDialogButton st={invalidation} setState={setInvalidation} onClose={() => {}} />
  );
}

function InvalidateConfirmation({ onClose }: { readonly onClose: () => void }) {
  const [invalidation, setInvalidation] = useState<InvalidateCycleState | null>({
    process: 1,
    changeRawMat: null,
    changeJobUnique: null,
    updating: false,
    error: null,
  });
  return (
    <>
      <InvalidateCycleDialogContent st={invalidation} setState={setInvalidation} />
      <InvalidateCycleDialogButton st={invalidation} setState={setInvalidation} onClose={onClose} />
    </>
  );
}

function logEvent({
  counter,
  process,
  peerId,
  peerSerial,
  invalidated = false,
}: {
  readonly counter: number;
  readonly process: number;
  readonly peerId: number;
  readonly peerSerial?: string;
  readonly invalidated?: boolean;
}): api.LogEntry {
  return new api.LogEntry({
    counter,
    material: [
      new api.LogMaterial({
        id: 101,
        uniq: "JOB-1",
        part: "Part",
        proc: process,
        path: 1,
        numproc: 2,
        face: 1,
        serial: "SELECTED",
        workorder: "",
      }),
      new api.LogMaterial({
        id: peerId,
        uniq: "JOB-1",
        part: "Part",
        proc: process,
        path: 1,
        numproc: 2,
        face: 2,
        serial: peerSerial,
        workorder: "",
      }),
    ],
    type: api.LogType.MachineCycle,
    startofcycle: false,
    endUTC: new Date(`2026-04-24T12:0${counter}:00Z`),
    loc: "MC",
    locnum: 1,
    pal: 1,
    program: "PROGRAM",
    result: "",
    elapsed: "PT1M",
    active: "PT1M",
    details: invalidated ? { PalletCycleInvalidated: "1" } : undefined,
  });
}

function eventResponse(events: ReadonlyArray<api.LogEntry>): Response {
  return new Response(JSON.stringify(events.map((event) => event.toJSON())), { status: 200 });
}

describe("cycle invalidation workflow", () => {
  test("does not offer invalidation while the selected material is queued", async () => {
    const selected = material({
      materialId: 101,
      location: { type: api.LocType.InQueue, currentQueue: "Queue A", queuePosition: 0 },
    });
    const screen = await renderInsightPage(
      <Suspense fallback={<div>Loading</div>}>
        <InvalidateButton />
      </Suspense>,
      {
        currentStatus: statusWithMaterial([selected]),
        fmsInfo: { allowInvalidateMaterialOnQueuesPage: true },
        ...dialogData(selected),
      },
    );

    await expect
      .element(screen.getByRole("button", { name: "Invalidate Cycle" }))
      .not.toBeInTheDocument();
  });

  test("previews only current local events in the selected process range", async () => {
    const selected = material({ materialId: 101 });
    const queuedPeer = material({
      materialId: 102,
      location: { type: api.LocType.InQueue, currentQueue: "Queue A", queuePosition: 0 },
    });
    const localEvents = [
      logEvent({ counter: 1, process: 0, peerId: 100, peerSerial: "EARLIER" }),
      logEvent({
        counter: 2,
        process: 1,
        peerId: 103,
        peerSerial: "INVALID-PEER",
        invalidated: true,
      }),
      logEvent({ counter: 3, process: 2, peerId: 102 }),
    ];
    const fetch = vi.spyOn(window, "fetch").mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (url === "/api/v1/log/events/for-material/101") return eventResponse(localEvents);
      if (url.startsWith("https://additional.test")) {
        return eventResponse([
          logEvent({ counter: 4, process: 2, peerId: 999, peerSerial: "FOREIGN" }),
        ]);
      }
      throw new Error(`Unexpected request: ${url}`);
    });
    registerNetworkBackend();
    setOtherLogBackends(["https://additional.test"]);

    const screen = await renderInsightPage(
      <Suspense fallback={<div>Loading</div>}>
        <InvalidateCycleDialogContent
          st={{
            process: 1,
            changeRawMat: null,
            changeJobUnique: null,
            updating: false,
            error: null,
          }}
          setState={() => {}}
        />
      </Suspense>,
      {
        currentStatus: statusWithMaterial([selected, queuedPeer]),
        ...dialogData(selected),
      },
    );

    await expect.element(screen.getByText("Material ID 102 (currently in a queue)")).toBeVisible();
    await expect.element(screen.getByText("EARLIER")).not.toBeInTheDocument();
    await expect.element(screen.getByText("INVALID-PEER", { exact: true })).not.toBeInTheDocument();
    await expect.element(screen.getByText("FOREIGN")).not.toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith("/api/v1/log/events/for-material/101", expect.anything());
  });

  test("removes waiting queued material without losing the scanned dialog context", async () => {
    const selected = material({
      materialId: 101,
      location: { type: api.LocType.InQueue, currentQueue: "Queue A", queuePosition: 0 },
    });
    const fetch = vi.spyOn(window, "fetch").mockResolvedValue(new Response(null, { status: 204 }));
    registerNetworkBackend();

    const screen = await renderInsightPage(
      <Suspense fallback={<div>Loading</div>}>
        <QuarantineMatButton />
      </Suspense>,
      { currentStatus: statusWithMaterial([selected]), ...dialogData(selected) },
    );

    await screen.getByRole("button", { name: "Remove from all queues" }).click();
    const dialog = screen.getByRole("dialog");
    await dialog.getByRole("button", { name: "Remove from Queues" }).click();

    await expect.element(dialog).not.toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(1);
    expect(screen.store.get(materialDialogOpen)).toMatchObject({ type: "MatSummary" });
  });

  test("keeps quarantine available alongside direct queue removal", async () => {
    const selected = material({
      materialId: 101,
      location: { type: api.LocType.InQueue, currentQueue: "Queue A", queuePosition: 0 },
    });
    const fetch = vi.spyOn(window, "fetch").mockResolvedValue(new Response(null, { status: 204 }));
    registerNetworkBackend();

    const screen = await renderInsightPage(
      <Suspense fallback={<div>Loading</div>}>
        <QuarantineMatButton />
      </Suspense>,
      {
        currentStatus: statusWithMaterial([selected]),
        fmsInfo: { quarantineQueue: "Quarantine" },
        ...dialogData(selected),
      },
    );

    await expect.element(screen.getByRole("button", { name: "Move to Quarantine" })).toBeVisible();
    await expect
      .element(screen.getByRole("button", { name: "Remove from all queues" }))
      .toBeVisible();

    await screen.getByRole("button", { name: "Move to Quarantine" }).click();
    const dialog = screen.getByRole("dialog");
    await dialog.getByRole("button", { name: "Quarantine" }).click();

    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/jobs/material/101/signal-quarantine",
      expect.objectContaining({ method: "PUT" }),
    );
  });

  test("keeps failed queue removal visible with the server error", async () => {
    const selected = material({
      materialId: 101,
      location: { type: api.LocType.InQueue, currentQueue: "Queue A", queuePosition: 0 },
    });
    vi.spyOn(window, "fetch").mockResolvedValue(new Response("Queue changed", { status: 409 }));
    registerNetworkBackend();

    const screen = await renderInsightPage(
      <Suspense fallback={<div>Loading</div>}>
        <QuarantineMatButton />
      </Suspense>,
      { currentStatus: statusWithMaterial([selected]), ...dialogData(selected) },
    );

    await screen.getByRole("button", { name: "Remove from all queues" }).click();
    const dialog = screen.getByRole("dialog");
    await dialog.getByRole("button", { name: "Remove from Queues" }).click();

    await expect.element(dialog).toBeVisible();
    await expect.element(dialog.getByRole("alert")).toHaveTextContent("Queue changed");
  });

  test("retains the dialog context after a successful invalidation", async () => {
    const selected = material({ materialId: 101 });
    const closed = vi.fn();
    const details = new api.MaterialDetails({
      materialID: 101,
      jobUnique: "JOB-1",
      partName: "Part",
      numProcesses: 2,
      serial: "SERIAL-101",
    });
    vi.spyOn(window, "fetch").mockImplementation(async (input, init) => {
      if (init?.method === "GET")
        return eventResponse([logEvent({ counter: 1, process: 1, peerId: 102 })]);
      if (init?.method === "PUT")
        return new Response(JSON.stringify(details.toJSON()), { status: 200 });
      throw new Error(`Unexpected request: ${requestUrl(input)}`);
    });
    registerNetworkBackend();

    const screen = await renderInsightPage(
      <Suspense fallback={<div>Loading</div>}>
        <InvalidateConfirmation onClose={closed} />
      </Suspense>,
      {
        currentStatus: statusWithMaterial([selected]),
        fmsInfo: { allowInvalidateMaterialOnQueuesPage: true },
        ...dialogData(selected),
      },
    );

    await screen.getByRole("button", { name: "Invalidate Process 1" }).click();

    expect(screen.store.get(materialDialogOpen)).toMatchObject({ type: "MatDetails" });
    expect(closed).not.toHaveBeenCalled();
  });

  test("retains invalidation failures in the dialog", async () => {
    const selected = material({ materialId: 101 });
    vi.spyOn(window, "fetch").mockImplementation(async (input, init) => {
      if (init?.method === "GET")
        return eventResponse([logEvent({ counter: 1, process: 1, peerId: 102 })]);
      if (init?.method === "PUT") return new Response("Material is queued", { status: 409 });
      throw new Error(`Unexpected request: ${requestUrl(input)}`);
    });
    registerNetworkBackend();

    const screen = await renderInsightPage(
      <Suspense fallback={<div>Loading</div>}>
        <InvalidateConfirmation onClose={() => {}} />
      </Suspense>,
      {
        currentStatus: statusWithMaterial([selected]),
        fmsInfo: { allowInvalidateMaterialOnQueuesPage: true },
        ...dialogData(selected),
      },
    );

    await screen.getByRole("button", { name: "Invalidate Process 1" }).click();

    await expect.element(screen.getByRole("alert")).toHaveTextContent("Material is queued");
    expect(screen.store.get(materialDialogOpen)).not.toBeNull();
  });

  test("closes the dialog only after queue addition succeeds", async () => {
    const selected = material({ materialId: 101 });
    const closed = vi.fn();
    vi.spyOn(window, "fetch").mockImplementation(async (input, init) => {
      if (init?.method === "GET") return eventResponse([]);
      if (init?.method === "PUT") return new Response(null, { status: 204 });
      throw new Error(`Unexpected request: ${requestUrl(input)}`);
    });
    registerNetworkBackend();

    const screen = await renderInsightPage(
      <Suspense fallback={<div>Loading</div>}>
        <AddToQueueButton
          st={{ toQueue: "Queue A", enteredOperator: null, newMaterialTy: null }}
          queueNames={["Queue A"]}
          onClose={closed}
        />
      </Suspense>,
      { currentStatus: statusWithMaterial([selected]), ...dialogData(selected) },
    );

    await screen.getByRole("button", { name: /Add To Queue A/ }).click();

    expect(closed).toHaveBeenCalledOnce();
    expect(screen.store.get(materialDialogOpen)).toBeNull();
  });

  test("retains queue addition errors in the dialog", async () => {
    const selected = material({ materialId: 101 });
    const closed = vi.fn();
    vi.spyOn(window, "fetch").mockImplementation(async (input, init) => {
      if (init?.method === "GET") return eventResponse([]);
      if (init?.method === "PUT") return new Response("Queue is full", { status: 409 });
      throw new Error(`Unexpected request: ${requestUrl(input)}`);
    });
    registerNetworkBackend();

    const screen = await renderInsightPage(
      <Suspense fallback={<div>Loading</div>}>
        <AddToQueueButton
          st={{ toQueue: "Queue A", enteredOperator: null, newMaterialTy: null }}
          queueNames={["Queue A"]}
          onClose={closed}
        />
      </Suspense>,
      { currentStatus: statusWithMaterial([selected]), ...dialogData(selected) },
    );

    await screen.getByRole("button", { name: /Add To Queue A/ }).click();

    await expect.element(screen.getByRole("alert")).toHaveTextContent("Queue is full");
    expect(closed).not.toHaveBeenCalled();
    expect(screen.store.get(materialDialogOpen)).not.toBeNull();
  });
});
