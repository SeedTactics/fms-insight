import { describe, expect, test, vi } from "vitest";

import LoadStation from "../../src/components/station-monitor/LoadStation.js";
import type { SubmitBasketLoadStationCommand } from "../../src/components/station-monitor/BasketLoadStationWork.js";
import * as api from "../../src/network/api.js";
import { onLoadCurrentSt } from "../../src/cell-status/loading.js";
import { renderInsightPage } from "./framework.js";
import {
  activeBasketRegionTestId,
  basketRegionTestId,
  basketsColumnTestId,
  createBasket,
  createCurrentStatus,
  createMaterial,
  queueRegionTestId,
  region,
} from "./load-station-testkit.js";

describe("load station with active basket", () => {
  test("places basket loads, queued material, and staging baskets in the correct regions", async () => {
    const currentStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
          emptySlots: [1],
        }),
        createBasket({
          basketId: 8,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadStationStaging,
            locationNum: 1,
            zone: 1,
          }),
        }),
      ],
      material: [
        createMaterial({
          materialID: 301,
          jobUnique: "",
          partName: "Queue To Basket",
          process: 0,
          path: 1,
          serial: "QB-1",
          location: {
            type: api.LocType.InQueue,
            currentQueue: "Queue A",
            queuePosition: 0,
          },
          action: {
            type: api.ActionType.LoadingToBasket,
            loadToBasketId: 7,
            loadToBasketSlot: 1,
            processAfterLoad: 1,
          },
        }),
        createMaterial({
          materialID: 302,
          jobUnique: "",
          partName: "Basket To Queue",
          process: 1,
          path: 1,
          serial: "BQ-1",
          location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 0 },
          action: {
            type: api.ActionType.UnloadToInProcess,
            unloadIntoQueue: "Queue B",
          },
        }),
        createMaterial({
          materialID: 305,
          jobUnique: "",
          partName: "Legacy Queue To Basket",
          process: 0,
          path: 1,
          serial: "LQB-1",
          location: {
            type: api.LocType.InQueue,
            currentQueue: "Legacy Queue",
            queuePosition: 0,
          },
          action: {
            type: api.ActionType.Loading,
            loadFromBasketId: 7,
            processAfterLoad: 1,
          },
        }),
        createMaterial({
          materialID: 303,
          jobUnique: "JOB-303",
          partName: "Queue Existing",
          process: 1,
          path: 1,
          serial: "QE-3",
          location: {
            type: api.LocType.InQueue,
            currentQueue: "Queue B",
            queuePosition: 0,
          },
          action: { type: api.ActionType.Waiting },
        }),
        createMaterial({
          materialID: 304,
          jobUnique: "",
          partName: "Staging Basket",
          process: 1,
          path: 1,
          serial: "SB-1",
          location: { type: api.LocType.InBasket, basketId: 8, basketSlot: 0 },
          action: { type: api.ActionType.Waiting },
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={["Queue C"]} completed={false} />,
      { currentStatus },
    );

    const activeBasket = region(screen, activeBasketRegionTestId);
    const queueA = region(screen, queueRegionTestId("Queue A"));
    const queueB = region(screen, queueRegionTestId("Queue B"));
    const queueC = region(screen, queueRegionTestId("Queue C"));
    const legacyQueue = region(screen, queueRegionTestId("Legacy Queue"));
    const basketsColumn = region(screen, basketsColumnTestId);
    const stagingBasket = region(screen, basketRegionTestId(8));

    await expect.element(activeBasket).toHaveTextContent("Basket 7");
    await expect.element(activeBasket).toHaveTextContent("Basket To Queue");
    await expect.element(activeBasket).toHaveTextContent("Unload into queue Queue B");
    await expect.element(activeBasket).toHaveTextContent("Load from Queue A");

    await expect.element(queueA).toHaveTextContent("Queue To Basket");
    await expect.element(queueA).toHaveTextContent("Load into Basket 7 slot 2");
    await expect.element(legacyQueue).toHaveTextContent("Legacy Queue To Basket");
    await expect.element(legacyQueue).toHaveTextContent("Load into Basket 7");
    await expect.element(queueB).toHaveTextContent("Queue Existing");
    await expect.element(queueC).not.toHaveTextContent("Queue Existing");
    await expect.element(basketsColumn).toHaveTextContent("Baskets");
    await expect.element(stagingBasket).toHaveTextContent("Staging Basket");
  });

  test("confirms unload and load as separate whole-basket phases", async () => {
    const submit = vi.fn(async () => "accepted" as const);
    const unloadStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
          emptySlots: [2, 3],
        }),
      ],
      material: [
        createMaterial({
          materialID: 401,
          jobUnique: "JOB-1",
          partName: "Process 1 Part",
          process: 1,
          path: 1,
          location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 0 },
          action: {
            type: api.ActionType.UnloadToInProcess,
            workId: "unload-work",
            unloadIntoQueue: "Transfer Queue",
          },
        }),
        createMaterial({
          materialID: 402,
          jobUnique: "JOB-2",
          partName: "Completed Part",
          process: 2,
          path: 1,
          location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 1 },
          action: {
            type: api.ActionType.UnloadToCompletedMaterial,
            workId: "unload-work",
          },
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus: unloadStatus },
    );

    await expect
      .element(region(screen, "basket-load-station-slot-0"))
      .toHaveTextContent("Unload into queue Transfer Queue");
    await expect
      .element(region(screen, "basket-load-station-slot-1"))
      .toHaveTextContent("Unload from Basket 7 to completed material");
    await screen.getByRole("button", { name: "Unload Complete" }).click();
    expect(submit).toHaveBeenCalledWith(1, { workId: "unload-work" });
    await expect.element(screen.getByText(/Confirmation accepted/)).toBeVisible();

    screen.store.set(
      onLoadCurrentSt,
      createCurrentStatus({
        baskets: [
          createBasket({
            basketId: 7,
            position: new api.BasketPosition({
              location: api.BasketLocationEnum.LoadUnload,
              locationNum: 1,
            }),
            emptySlots: [0, 1, 2, 3],
          }),
        ],
        material: [
          createMaterial({
            materialID: 403,
            jobUnique: "JOB-3",
            partName: "Process 2 Part",
            process: 1,
            path: 1,
            location: {
              type: api.LocType.InQueue,
              currentQueue: "Transfer Queue",
              queuePosition: 0,
            },
            action: {
              type: api.ActionType.LoadingToBasket,
              workId: "load-work",
              loadToBasketId: 7,
              loadToBasketSlot: 0,
              processAfterLoad: 2,
              pathAfterLoad: 1,
            },
          }),
          createMaterial({
            materialID: -1,
            jobUnique: "JOB-4",
            partName: "Raw Part",
            process: 0,
            path: 1,
            location: { type: api.LocType.Free },
            action: {
              type: api.ActionType.LoadingToBasket,
              workId: "load-work",
              loadToBasketId: 7,
              loadToBasketSlot: 1,
              processAfterLoad: 1,
              pathAfterLoad: 1,
            },
          }),
        ],
      }),
    );

    await expect.element(screen.getByText(/Confirmation accepted/)).not.toBeInTheDocument();
    await expect
      .element(region(screen, "basket-load-station-slot-0"))
      .toHaveTextContent("Load from Transfer Queue");
    await expect
      .element(region(screen, "basket-load-station-slot-1"))
      .toHaveTextContent("Load from raw material");
    await expect
      .element(region(screen, "load-station-material"))
      .toHaveTextContent("Load into Basket 7 slot 2");
    await screen.getByRole("button", { name: "Load Complete" }).click();
    expect(submit).toHaveBeenLastCalledWith(1, { workId: "load-work" });
  });

  test("explains a stale phase-completion conflict", async () => {
    const submit = vi.fn(async () => "conflict" as const);
    const currentStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
          emptySlots: [0],
        }),
      ],
      material: [
        createMaterial({
          materialID: -1,
          jobUnique: "JOB",
          partName: "Part",
          process: 0,
          path: 1,
          location: { type: api.LocType.Free },
          action: {
            type: api.ActionType.LoadingToBasket,
            workId: "stale-work",
            loadToBasketId: 7,
            loadToBasketSlot: 0,
            processAfterLoad: 1,
          },
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );

    const complete = screen.getByRole("button", { name: "Load Complete" });
    await complete.click();
    await expect.element(screen.getByText(/Basket work changed/)).toBeVisible();
    await expect.element(complete).toBeDisabled();
    expect(submit).toHaveBeenCalledWith(1, { workId: "stale-work" });
    expect(submit).toHaveBeenCalledTimes(1);
  });

  test("suppresses completion when material actions disagree on work", async () => {
    const submit = vi.fn(async () => "accepted" as const);
    const currentStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
          emptySlots: [0, 1],
        }),
      ],
      material: [
        createMaterial({
          materialID: -1,
          jobUnique: "JOB-1",
          partName: "First Part",
          process: 0,
          path: 1,
          location: { type: api.LocType.Free },
          action: {
            type: api.ActionType.LoadingToBasket,
            workId: "first-work",
            loadToBasketId: 7,
            loadToBasketSlot: 0,
            processAfterLoad: 1,
          },
        }),
        createMaterial({
          materialID: -1,
          jobUnique: "JOB-2",
          partName: "Second Part",
          process: 0,
          path: 1,
          location: { type: api.LocType.Free },
          action: {
            type: api.ActionType.LoadingToBasket,
            workId: "second-work",
            loadToBasketId: 7,
            loadToBasketSlot: 1,
            processAfterLoad: 1,
          },
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );

    await expect.element(screen.getByText(/Basket work is inconsistent/)).toBeVisible();
    await expect
      .element(screen.getByRole("button", { name: "Load Complete" }))
      .not.toBeInTheDocument();
    expect(submit).not.toHaveBeenCalled();
    await vi.waitFor(() => {
      const identifiers = Array.from(
        screen.container.querySelectorAll('[data-move-material-identifier^="Material-"]'),
        (element) => element.getAttribute("data-move-material-identifier"),
      );
      expect(identifiers).toHaveLength(2);
      expect(identifiers[0]).not.toBe(identifiers[1]);
    });
  });

  test.each([
    ["missing", undefined],
    ["blank", "   "],
  ])("suppresses completion for a %s work ID", async (_description, workId) => {
    const submit = vi.fn(async () => "accepted" as const);
    const currentStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
          emptySlots: [0],
        }),
      ],
      material: [
        createMaterial({
          materialID: -1,
          jobUnique: "JOB",
          partName: "Part",
          process: 0,
          path: 1,
          location: { type: api.LocType.Free },
          action: {
            type: api.ActionType.LoadingToBasket,
            workId,
            loadToBasketId: 7,
            loadToBasketSlot: 0,
            processAfterLoad: 1,
          },
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );

    await expect.element(screen.getByText(/Basket work is inconsistent/)).toBeVisible();
    await expect
      .element(screen.getByRole("button", { name: "Load Complete" }))
      .not.toBeInTheDocument();
    expect(submit).not.toHaveBeenCalled();
  });

  test.each([
    [
      "load destination slot",
      createMaterial({
        materialID: -1,
        jobUnique: "LOAD-JOB",
        partName: "Load Part",
        process: 0,
        path: 1,
        location: { type: api.LocType.Free },
        action: {
          type: api.ActionType.LoadingToBasket,
          workId: "load-work",
          loadToBasketId: 7,
          processAfterLoad: 1,
        },
      }),
    ],
    [
      "load destination basket",
      createMaterial({
        materialID: -1,
        jobUnique: "LOAD-JOB",
        partName: "Load Part",
        process: 0,
        path: 1,
        location: { type: api.LocType.Free },
        action: {
          type: api.ActionType.LoadingToBasket,
          workId: "load-work",
          loadToBasketSlot: 0,
          processAfterLoad: 1,
        },
      }),
    ],
    [
      "load source queue",
      createMaterial({
        materialID: 400,
        jobUnique: "LOAD-JOB",
        partName: "Load Part",
        process: 1,
        path: 1,
        location: { type: api.LocType.InQueue, queuePosition: 0 },
        action: {
          type: api.ActionType.LoadingToBasket,
          workId: "load-work",
          loadToBasketId: 7,
          loadToBasketSlot: 0,
          processAfterLoad: 2,
        },
      }),
    ],
    [
      "unload source slot",
      createMaterial({
        materialID: 401,
        jobUnique: "UNLOAD-JOB",
        partName: "Unload Part",
        process: 1,
        path: 1,
        location: { type: api.LocType.InBasket, basketId: 7 },
        action: {
          type: api.ActionType.UnloadToCompletedMaterial,
          workId: "unload-work",
        },
      }),
    ],
  ])("suppresses completion when the basket %s is missing", async (_description, material) => {
    const submit = vi.fn(async () => "accepted" as const);
    const currentStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
          emptySlots: [0],
        }),
      ],
      material: [material],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );

    await expect.element(screen.getByText(/Basket work is inconsistent/)).toBeVisible();
    await expect.element(screen.getByRole("button", { name: /Complete/ })).not.toBeInTheDocument();
    expect(submit).not.toHaveBeenCalled();
  });

  test("suppresses completion when one work ID contains both phases", async () => {
    const submit = vi.fn(async () => "accepted" as const);
    const currentStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
          emptySlots: [1],
        }),
      ],
      material: [
        createMaterial({
          materialID: 501,
          jobUnique: "UNLOAD-JOB",
          partName: "Unload Part",
          process: 1,
          path: 1,
          location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 0 },
          action: {
            type: api.ActionType.UnloadToCompletedMaterial,
            workId: "mixed-work",
          },
        }),
        createMaterial({
          materialID: -1,
          jobUnique: "LOAD-JOB",
          partName: "Load Part",
          process: 0,
          path: 1,
          location: { type: api.LocType.Free },
          action: {
            type: api.ActionType.LoadingToBasket,
            workId: "mixed-work",
            loadToBasketId: 7,
            loadToBasketSlot: 1,
            processAfterLoad: 1,
          },
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );

    await expect.element(screen.getByText(/Basket work is inconsistent/)).toBeVisible();
    await expect.element(screen.getByRole("button", { name: /Complete/ })).not.toBeInTheDocument();
    expect(submit).not.toHaveBeenCalled();
  });

  test("allows retry after a command error", async () => {
    const submit = vi
      .fn<SubmitBasketLoadStationCommand>()
      .mockRejectedValueOnce(new Error("network error"))
      .mockResolvedValueOnce("accepted");
    const currentStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
          emptySlots: [0],
        }),
      ],
      material: [
        createMaterial({
          materialID: -1,
          jobUnique: "JOB",
          partName: "Part",
          process: 0,
          path: 1,
          location: { type: api.LocType.Free },
          action: {
            type: api.ActionType.LoadingToBasket,
            workId: "load-work",
            loadToBasketId: 7,
            loadToBasketSlot: 0,
            processAfterLoad: 1,
          },
        }),
      ],
    });

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );
    const complete = screen.getByRole("button", { name: "Load Complete" });

    await complete.click();
    await expect.element(screen.getByText(/Unable to confirm basket work/)).toBeVisible();
    await expect.element(complete).toBeEnabled();
    await complete.click();
    await expect.element(screen.getByText(/Confirmation accepted/)).toBeVisible();
    expect(submit).toHaveBeenCalledTimes(2);
  });
});
