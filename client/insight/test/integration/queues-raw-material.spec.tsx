import { describe, expect, test } from "vitest";

import { Queues } from "../../src/components/station-monitor/Queues.js";
import * as api from "../../src/network/api.js";
import { renderInsightPage } from "./framework.js";
import { createMaterial } from "./load-station-testkit.js";

describe("standalone queues page", () => {
  test("shows serialed raw material already in a raw material queue", async () => {
    const currentStatus = new api.CurrentStatus({
      timeOfCurrentStatusUTC: new Date("2026-04-24T12:00:00Z"),
      jobs: {},
      pallets: {},
      material: [
        createMaterial({
          materialID: 401,
          jobUnique: "",
          partName: "Raw Casting",
          process: 0,
          path: 1,
          serial: "FAB-001",
          location: {
            type: api.LocType.InQueue,
            currentQueue: "Fabrication",
            queuePosition: 0,
          },
          action: {
            type: api.ActionType.Waiting,
          },
        }),
      ],
      alarms: [],
      queues: {
        Fabrication: new api.QueueInfo({
          role: api.QueueRole.RawMaterial,
        }),
      },
      baskets: {},
    });

    const screen = await renderInsightPage(<Queues queues={["Fabrication"]} />, {
      currentStatus,
    });

    await expect.element(screen.getByRole("heading", { name: "Fabrication" })).toBeVisible();
    await expect.element(screen.getByText("Raw Casting")).toBeVisible();
    await expect.element(screen.getByText("Serial: FAB-001")).toBeVisible();
  });
});
