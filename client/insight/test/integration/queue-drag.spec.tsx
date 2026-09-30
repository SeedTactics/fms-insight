import { useAtomValue } from "jotai";
import { LazySeq } from "@seedtactics/immutable-collections";
import { afterEach, describe, expect, test, vi } from "vitest";
import { userEvent } from "vitest/browser";

import { currentStatus } from "../../src/cell-status/current-status.js";
import { AllMaterial } from "../../src/components/operations/AllMaterial.js";
import {
  DragOverlayInProcMaterial,
  SortableInProcMaterial,
} from "../../src/components/station-monitor/Material.js";
import { SortableRegion } from "../../src/components/station-monitor/Whiteboard.js";
import * as api from "../../src/network/api.js";
import { registerNetworkBackend } from "../../src/network/backend.js";
import { renderInsightPage } from "./framework.js";
import { createCurrentStatus, createMaterial } from "./load-station-testkit.js";

afterEach(() => vi.restoreAllMocks());

function QueueWhiteboard() {
  const status = useAtomValue(currentStatus);
  const material = LazySeq.of(status.material)
    .sortBy((m) => m.location.queuePosition ?? 0)
    .toRArray();
  return (
    <SortableRegion
      queueName="Queue A"
      matIds={material.map((m) => m.materialID)}
      direction="vertical"
      renderDragOverlay={(mat) => <DragOverlayInProcMaterial mat={mat} />}
    >
      {material.map((mat) => (
        <SortableInProcMaterial key={mat.materialID} mat={mat} />
      ))}
    </SortableRegion>
  );
}

function queueStatus(action: api.ActionType, automatedTransfer = false) {
  return createCurrentStatus({
    queues: { "Queue A": new api.QueueInfo({}), "Queue B": new api.QueueInfo({}) },
    material: [1, 2].map((id) =>
      createMaterial({
        materialID: id,
        jobUnique: "JOB",
        partName: "Part",
        serial: `SERIAL-${id}`,
        process: 1,
        path: 1,
        location: { type: api.LocType.InQueue, currentQueue: "Queue A", queuePosition: id - 1 },
        action: { type: action, automatedTransfer },
      }),
    ),
  });
}

describe.each(["all material", "whiteboard"])("%s queue drag", (view) => {
  test.each([
    { action: api.ActionType.Waiting, responseStatus: 204 },
    { action: api.ActionType.Waiting, responseStatus: 409 },
    { action: api.ActionType.Loading, responseStatus: 204 },
    { action: api.ActionType.Loading, responseStatus: 409 },
  ])(
    "updates $action order only after a $responseStatus response",
    async ({ action, responseStatus }) => {
      let respond!: (response: Response) => void;
      const response = new Promise<Response>((resolve) => {
        respond = resolve;
      });
      const fetch = vi.spyOn(window, "fetch").mockReturnValue(response);
      const error = vi.spyOn(console, "error").mockImplementation(() => {});
      registerNetworkBackend();
      const initial = queueStatus(action);
      const screen = await renderInsightPage(
        view === "all material" ? <AllMaterial displaySystemBins={false} /> : <QueueWhiteboard />,
        { currentStatus: initial },
      );

      screen.getByRole("button", { name: "", exact: true }).first().element().focus();
      await userEvent.keyboard("{Space}");
      for (let step = 0; step < (view === "all material" ? 6 : 1); step += 1) {
        await userEvent.keyboard("{ArrowDown}");
        await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
      }
      await userEvent.keyboard("{Space}");

      await expect.poll(() => fetch.mock.calls.length).toBe(1);
      expect(fetch).toHaveBeenCalledWith(
        "/api/v1/jobs/material/1/queue",
        expect.objectContaining({
          method: "PUT",
          body: JSON.stringify({ Queue: "Queue A", Position: 1 }),
        }),
      );
      expect(screen.store.get(currentStatus)).toBe(initial);

      respond(
        new Response(responseStatus === 409 ? "Material is now automated" : null, {
          status: responseStatus,
        }),
      );
      if (responseStatus === 409) {
        await expect.poll(() => error.mock.calls.length).toBe(1);
        expect(screen.store.get(currentStatus)).toBe(initial);
      } else {
        await expect
          .poll(() => screen.store.get(currentStatus).material.map((m) => m.location.queuePosition))
          .toEqual([1, 0]);
        expect(error).not.toHaveBeenCalled();
      }
    },
  );

  test("does not expose drag handles for automated queued material", async () => {
    const fetch = vi.spyOn(window, "fetch");
    registerNetworkBackend();
    const initial = queueStatus(api.ActionType.Loading, true);
    const screen = await renderInsightPage(
      view === "all material" ? <AllMaterial displaySystemBins={false} /> : <QueueWhiteboard />,
      { currentStatus: initial },
    );
    await expect
      .element(screen.getByRole("button", { name: "", exact: true }))
      .not.toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
    expect(screen.store.get(currentStatus)).toBe(initial);
  });
});

test.each([
  { action: api.ActionType.Waiting, responseStatus: 204 },
  { action: api.ActionType.Waiting, responseStatus: 409 },
  { action: api.ActionType.Loading, responseStatus: 204 },
])(
  "all material cross-queue drag for $action with $responseStatus response",
  async ({ action, responseStatus }) => {
    let respond!: (response: Response) => void;
    const response = new Promise<Response>((resolve) => {
      respond = resolve;
    });
    const fetch = vi.spyOn(window, "fetch").mockReturnValue(response);
    const error = vi.spyOn(console, "error").mockImplementation(() => {});
    registerNetworkBackend();
    const initial = queueStatus(action);
    const screen = await renderInsightPage(<AllMaterial displaySystemBins={false} />, {
      currentStatus: initial,
    });

    screen.getByRole("button", { name: "", exact: true }).first().element().focus();
    await userEvent.keyboard("{Space}");
    for (let step = 0; step < 14; step += 1) {
      await userEvent.keyboard("{ArrowRight}");
      await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
    }
    await userEvent.keyboard("{Space}");

    expect(screen.store.get(currentStatus)).toBe(initial);
    if (action === api.ActionType.Loading) {
      expect(fetch).not.toHaveBeenCalled();
      return;
    }
    await expect.poll(() => fetch.mock.calls.length).toBe(1);
    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/jobs/material/1/queue",
      expect.objectContaining({
        method: "PUT",
        body: JSON.stringify({ Queue: "Queue B", Position: 0 }),
      }),
    );
    respond(
      new Response(responseStatus === 409 ? "Material is now automated" : null, {
        status: responseStatus,
      }),
    );
    if (responseStatus === 409) {
      await expect.poll(() => error.mock.calls.length).toBe(1);
      expect(screen.store.get(currentStatus)).toBe(initial);
    } else {
      await expect
        .poll(() => screen.store.get(currentStatus).material.map((m) => m.location.currentQueue))
        .toEqual(["Queue B", "Queue A"]);
      expect(error).not.toHaveBeenCalled();
    }
  },
);
