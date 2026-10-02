import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

import LoadStation from "../../src/components/station-monitor/LoadStation.js";
import { registerNetworkBackend } from "../../src/network/backend.js";
type CompletionHandler = (command: { readonly workId: string }) => Promise<"accepted" | "conflict">;

afterEach(() => vi.restoreAllMocks());
beforeEach(() => {
  registerNetworkBackend();
  vi.spyOn(window, "fetch").mockResolvedValue(new Response("[]", { status: 200 }));
});

function completionRequests(submit: CompletionHandler): void {
  registerNetworkBackend();
  vi.spyOn(window, "fetch").mockImplementation(async (input, init) => {
    const url =
      typeof input === "string" ? input : input instanceof URL ? input.toString() : input.url;
    if (!url.endsWith("/jobs/basket-load-station/complete"))
      return new Response("[]", { status: 200 });
    if (typeof init?.body !== "string") throw new Error("Expected a JSON completion body");
    const body: unknown = JSON.parse(init.body);
    if (
      typeof body !== "object" ||
      body === null ||
      !("WorkId" in body) ||
      typeof body.WorkId !== "string"
    )
      throw new Error("Expected the opaque basket WorkId request.");
    const result = await submit({ workId: body.WorkId });
    return new Response(null, { status: result === "conflict" ? 409 : 204 });
  });
}
import { onLoadCurrentSt } from "../../src/cell-status/loading.js";
import * as api from "../../src/network/api.js";
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

function confirmableBasketStatus(workId: string, partName: string): Readonly<api.ICurrentStatus> {
  return createCurrentStatus({
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
        materialID: -1,
        jobUnique: "JOB",
        partName,
        process: 0,
        path: 1,
        location: { type: api.LocType.Free },
        action: {
          type: api.ActionType.LoadingToBasket,
          workId,
          loadToBasketId: 7,
          loadToBasketSlot: 1,
          processAfterLoad: 1,
        },
      }),
    ],
  });
}

function explicitBasketWork(
  work: Readonly<api.IBasketLoadStationWork>,
  withMaterial = true,
): Readonly<api.ICurrentStatus> {
  const status = confirmableBasketStatus("load-1", "Retained part");
  return {
    ...status,
    baskets: {
      "7": new api.BasketStatus({
        basketId: status.baskets!["7"].basketId,
        position: status.baskets!["7"].position,
        emptySlots: status.baskets!["7"].emptySlots,
        loadStationWork: new api.BasketLoadStationWork(work),
      }),
    },
    material: withMaterial ? status.material : [],
  };
}

describe("explicit basket station work", () => {
  test("shows preparation advice without blocking confirmation of the achieved result", async () => {
    const submit = vi.fn<CompletionHandler>().mockResolvedValue("accepted");
    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus: explicitBasketWork({
        workId: "load-1",
        type: api.BasketLoadStationWorkType.Material,
        readyToConfirm: true,
        instructionWarning: "Review transfer-plate availability for the preserved targets.",
      }),
    });
    await expect
      .element(
        screen.getByText("Review transfer-plate availability for the preserved targets.", {
          exact: true,
        }),
      )
      .toBeVisible();
    const confirm = screen.getByRole("button", { name: "Confirm", exact: true });
    await expect.element(confirm).toBeEnabled();
    await confirm.click();
    expect(submit).toHaveBeenCalledOnce();
  });

  test.for([false, true])(
    "permits the same work after a transient conflict (refresh before response: %s)",
    async (refreshBeforeResponse) => {
      let resolveResponse: ((result: "conflict") => void) | undefined;
      const response = new Promise<"conflict">((resolve) => {
        resolveResponse = resolve;
      });
      const submit = vi
        .fn<CompletionHandler>()
        .mockImplementationOnce(() => response)
        .mockResolvedValue("accepted");
      const ready: Readonly<api.IBasketLoadStationWork> = {
        workId: "load-1",
        type: api.BasketLoadStationWorkType.Material,
        readyToConfirm: true,
      };
      completionRequests(submit);
      const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
        currentStatus: explicitBasketWork(ready),
      });
      const confirm = screen.getByRole("button", { name: "Confirm", exact: true });
      await confirm.click();
      await expect.element(confirm).toBeDisabled();
      if (!refreshBeforeResponse) {
        resolveResponse!("conflict");
        await expect
          .element(screen.getByText(/Basket work changed before confirmation/))
          .toBeVisible();
        await expect.element(confirm).toBeDisabled();
      }
      screen.store.set(
        onLoadCurrentSt,
        explicitBasketWork(
          new api.BasketLoadStationWork({
            ...ready,
            readyToConfirm: false,
            confirmationBlockedReason: "  Basket 4 is still sensed at the robot.  ",
          }),
        ),
      );
      await expect
        .element(screen.getByText("Basket 4 is still sensed at the robot."))
        .toBeVisible();
      if (refreshBeforeResponse) resolveResponse!("conflict");
      await expect.element(confirm).toBeDisabled();
      await expect
        .element(screen.getByText(/Basket work changed before confirmation/))
        .not.toBeInTheDocument();
      screen.store.set(onLoadCurrentSt, explicitBasketWork(ready));
      await expect.element(confirm).toBeEnabled();
      await confirm.click();
      expect(submit.mock.calls).toEqual([[{ workId: "load-1" }], [{ workId: "load-1" }]]);
      await expect.element(screen.getByText(/Confirmation accepted/)).toBeVisible();
      screen.store.set(onLoadCurrentSt, explicitBasketWork(ready));
      await expect.element(confirm).toBeDisabled();
      await expect.element(screen.getByText(/Confirmation accepted/)).toBeVisible();
    },
  );

  test("confirms an empty basket without material and retries the same occurrence after a lost response", async () => {
    const work = new api.BasketLoadStationWork({
      workId: "empty-1",
      type: api.BasketLoadStationWorkType.ConfirmEmptyBasket,
      readyToConfirm: true,
    });
    const submit = vi
      .fn<CompletionHandler>()
      .mockRejectedValueOnce(new Error("Lost response"))
      .mockResolvedValue("accepted");
    const status = explicitBasketWork(work, false);
    expect(status.material).toHaveLength(0);
    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus: status,
    });
    await expect.element(screen.getByText("Confirm basket 7 is empty.")).toBeVisible();
    const confirm = screen.getByRole("button", { name: "Confirm", exact: true });
    await confirm.click();
    await expect.element(confirm).toBeEnabled();
    await confirm.click();
    expect(submit.mock.calls).toEqual([[{ workId: "empty-1" }], [{ workId: "empty-1" }]]);
    await expect.element(screen.getByText(/Confirmation accepted/)).toBeVisible();
    await expect.element(confirm).toBeDisabled();
  });

  test.for(["", " \t "])("falls back for a blank confirmation reason (%j)", async (reason) => {
    const pending: Readonly<api.IBasketLoadStationWork> = {
      workId: "load-1",
      type: api.BasketLoadStationWorkType.Material,
      readyToConfirm: false,
      awaitingMaterialSlots: [2, 3],
      confirmationBlockedReason: reason,
    };
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus: explicitBasketWork(pending),
    });
    await expect.element(screen.getByText("Waiting for material for slots B, C.")).toBeVisible();
    await expect
      .element(screen.getByRole("button", { name: "Confirm", exact: true }))
      .toBeDisabled();
    screen.store.set(
      onLoadCurrentSt,
      explicitBasketWork({ ...pending, awaitingMaterialSlots: [] }),
    );
    await expect
      .element(screen.getByText("Basket work is not ready for confirmation."))
      .toBeVisible();
    await expect
      .element(screen.getByRole("button", { name: "Confirm", exact: true }))
      .toBeDisabled();
  });

  test("keeps valid material visible while waiting and only confirms after readiness arrives", async () => {
    const pending: Readonly<api.IBasketLoadStationWork> = {
      workId: "load-1",
      type: api.BasketLoadStationWorkType.Material,
      readyToConfirm: false,
      awaitingMaterialSlots: [2, 3],
    };
    const submit = vi.fn<CompletionHandler>(async () => "accepted");
    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus: explicitBasketWork(pending),
    });
    await expect.element(screen.getByText("Retained part", { exact: true })).toBeVisible();
    await expect.element(screen.getByText("Waiting for material for slots B, C.")).toBeVisible();
    const confirm = screen.getByRole("button", { name: "Confirm", exact: true });
    await expect.element(confirm).toBeDisabled();
    expect(submit).not.toHaveBeenCalled();
    screen.store.set(
      onLoadCurrentSt,
      explicitBasketWork(
        new api.BasketLoadStationWork({
          ...pending,
          readyToConfirm: true,
          awaitingMaterialSlots: [],
        }),
      ),
    );
    await expect.element(confirm).toBeEnabled();
    await confirm.click();
    expect(submit).toHaveBeenCalledWith({ workId: "load-1" });
  });

  test.for([
    ["mismatched occurrence", { workId: "another-work" }],
    ["blank occurrence", { workId: " " }],
    ["missing readiness", { readyToConfirm: undefined }],
    ["unknown type", { type: "Unknown" }],
    ["ready with missing slots", { awaitingMaterialSlots: [2] }],
    ["invalid missing slot", { readyToConfirm: false, awaitingMaterialSlots: [0] }],
    ["duplicate missing slot", { readyToConfirm: false, awaitingMaterialSlots: [2, 2] }],
    [
      "empty assertion with material actions",
      { type: api.BasketLoadStationWorkType.ConfirmEmptyBasket },
    ],
  ] as const)(
    "rejects %s without falling back to action-derived confirmation",
    async ([, overrides]) => {
      const fields = {
        workId: "load-1",
        type: api.BasketLoadStationWorkType.Material,
        readyToConfirm: true,
        awaitingMaterialSlots: [],
        ...overrides,
      };
      const work = api.BasketLoadStationWork.fromJS({
        WorkId: fields.workId,
        Type: fields.type,
        ReadyToConfirm: fields.readyToConfirm,
        AwaitingMaterialSlots: fields.awaitingMaterialSlots,
      });
      const submit = vi.fn<CompletionHandler>(async () => "accepted");
      completionRequests(submit);
      const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
        currentStatus: explicitBasketWork(work),
      });
      await expect.element(screen.getByText(/Basket work is inconsistent/)).toBeVisible();
      await expect
        .element(screen.getByRole("button", { name: "Confirm", exact: true }))
        .not.toBeInTheDocument();
      expect(submit).not.toHaveBeenCalled();
    },
  );

  test("does not apply an old empty confirmation response to a new occurrence", async () => {
    let finish: ((result: "accepted") => void) | undefined;
    const submit = vi.fn<CompletionHandler>(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const work: Readonly<api.IBasketLoadStationWork> = {
      workId: "empty-old",
      type: api.BasketLoadStationWorkType.ConfirmEmptyBasket,
      readyToConfirm: true,
    };
    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus: explicitBasketWork(work, false),
    });
    const confirm = screen.getByRole("button", { name: "Confirm", exact: true });
    await confirm.click();
    screen.store.set(
      onLoadCurrentSt,
      explicitBasketWork(new api.BasketLoadStationWork({ ...work, workId: "empty-new" }), false),
    );
    finish!("accepted");
    await expect.element(confirm).toBeEnabled();
    await expect.element(screen.getByText(/Confirmation accepted/)).not.toBeInTheDocument();
  });
});

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
          emptySlots: [2],
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
            loadToBasketSlot: 2,
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
          location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 1 },
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
          location: { type: api.LocType.InBasket, basketId: 8, basketSlot: 1 },
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

    await expect.element(activeBasket).toMatchTextContent("Basket 7");
    await expect.element(activeBasket).toMatchTextContent("Basket To Queue");
    await expect.element(activeBasket).toMatchTextContent("Unload into queue Queue B");
    await expect.element(activeBasket).toMatchTextContent("Load from Queue A");

    await expect.element(queueA).toMatchTextContent("Queue To Basket");
    await expect.element(queueA).toMatchTextContent("Load into Basket 7 slot B");
    await expect.element(legacyQueue).toMatchTextContent("Legacy Queue To Basket");
    await expect.element(legacyQueue).toMatchTextContent("Load into Basket 7");
    await expect.element(queueB).toMatchTextContent("Queue Existing");
    await expect.element(queueC).not.toMatchTextContent("Queue Existing");
    await expect.element(basketsColumn).toMatchTextContent("Baskets");
    await expect.element(stagingBasket).toMatchTextContent("Staging Basket");
  });

  test("confirms all unload and load work with one button press", async () => {
    const submit = vi.fn(async () => "accepted" as const);
    const currentStatus = createCurrentStatus({
      baskets: [
        createBasket({
          basketId: 7,
          position: new api.BasketPosition({
            location: api.BasketLocationEnum.LoadUnload,
            locationNum: 1,
          }),
          emptySlots: [3],
        }),
      ],
      material: [
        createMaterial({
          materialID: 401,
          jobUnique: "JOB-1",
          partName: "Process 1 Part",
          process: 1,
          path: 1,
          location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 1 },
          action: {
            type: api.ActionType.UnloadToInProcess,
            workId: "basket-work",
            unloadIntoQueue: "Transfer Queue",
          },
        }),
        createMaterial({
          materialID: 402,
          jobUnique: "JOB-2",
          partName: "Completed Part",
          process: 2,
          path: 1,
          location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 2 },
          action: {
            type: api.ActionType.UnloadToCompletedMaterial,
            workId: "basket-work",
          },
        }),
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
            workId: "basket-work",
            loadToBasketId: 7,
            loadToBasketSlot: 1,
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
            workId: "basket-work",
            loadToBasketId: 7,
            loadToBasketSlot: 3,
            processAfterLoad: 1,
            pathAfterLoad: 1,
          },
        }),
        createMaterial({
          materialID: 404,
          jobUnique: "JOB-5",
          partName: "Direct Replate Part",
          process: 1,
          path: 1,
          location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 4 },
          action: {
            type: api.ActionType.LoadingToBasket,
            workId: "basket-work",
            loadToBasketId: 7,
            loadToBasketSlot: 4,
            processAfterLoad: 2,
            pathAfterLoad: 1,
          },
        }),
      ],
    });

    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus,
    });

    await expect
      .element(region(screen, "basket-load-station-slot-1"))
      .toMatchTextContent("Unload into queue Transfer Queue");
    await expect
      .element(region(screen, "basket-load-station-slot-1"))
      .toMatchTextContent("Load from Transfer Queue");
    await expect
      .element(region(screen, "basket-load-station-slot-2"))
      .toMatchTextContent("Unload from Basket 7 to completed material");
    await expect
      .element(region(screen, "basket-load-station-slot-3"))
      .toMatchTextContent("Load from raw material");
    await expect
      .element(region(screen, "basket-load-station-slot-4"))
      .toMatchTextContent("Unload, re-plate, and reload this slot");
    await expect
      .element(region(screen, "load-station-material"))
      .toMatchTextContent("Load into Basket 7 slot C");

    await screen.getByRole("button", { name: "Confirm" }).click();

    expect(submit).toHaveBeenCalledWith({ workId: "basket-work" });
    expect(submit).toHaveBeenCalledTimes(1);
    await expect.element(screen.getByText(/Confirmation accepted/)).toBeVisible();
  });

  test("explains a stale work confirmation conflict", async () => {
    const submit = vi.fn(async () => "conflict" as const);
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
            loadToBasketSlot: 1,
            processAfterLoad: 1,
          },
        }),
      ],
    });

    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus,
    });

    const complete = screen.getByRole("button", { name: "Confirm" });
    await complete.click();
    await expect.element(screen.getByText(/Basket work changed/)).toBeVisible();
    await expect.element(complete).toBeDisabled();
    expect(submit).toHaveBeenCalledWith({ workId: "stale-work" });
    expect(submit).toHaveBeenCalledTimes(1);
  });

  test("ignores a submission response after the displayed work changes", async () => {
    let resolveSubmission!: (result: "accepted" | "conflict") => void;
    const pendingSubmission = new Promise<"accepted" | "conflict">((resolve) => {
      resolveSubmission = resolve;
    });
    const submit = vi.fn(() => pendingSubmission);

    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus: confirmableBasketStatus("first-work", "First Part"),
    });
    const confirm = screen.getByRole("button", { name: "Confirm" });

    await confirm.click();
    await expect.element(confirm).toBeDisabled();
    screen.store.set(
      onLoadCurrentSt,
      confirmableBasketStatus("replacement-work", "Replacement Part"),
    );
    await expect.element(screen.getByText("Replacement Part · Process 1")).toBeVisible();
    await expect.element(confirm).toBeEnabled();

    resolveSubmission("accepted");
    await pendingSubmission;

    await expect.element(confirm).toBeEnabled();
    await expect.element(screen.getByText(/Confirmation accepted/)).not.toBeInTheDocument();
    expect(submit).toHaveBeenCalledWith({ workId: "first-work" });
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
          emptySlots: [1, 2],
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
            loadToBasketSlot: 1,
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
            loadToBasketSlot: 2,
            processAfterLoad: 1,
          },
        }),
      ],
    });

    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus,
    });

    await expect.element(screen.getByText(/Basket work is inconsistent/)).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Confirm" })).not.toBeInTheDocument();
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
          emptySlots: [1],
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
            loadToBasketSlot: 1,
            processAfterLoad: 1,
          },
        }),
      ],
    });

    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus,
    });

    await expect.element(screen.getByText(/Basket work is inconsistent/)).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Confirm" })).not.toBeInTheDocument();
    expect(submit).not.toHaveBeenCalled();
  });

  test.each([
    [
      "load destination slot is missing",
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
      "load destination basket is missing",
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
          loadToBasketSlot: 1,
          processAfterLoad: 1,
        },
      }),
    ],
    [
      "load source queue is missing",
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
          loadToBasketSlot: 1,
          processAfterLoad: 2,
        },
      }),
    ],
    [
      "unload source slot is missing",
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
    [
      "load destination slot is zero",
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
          loadToBasketSlot: 0,
          processAfterLoad: 1,
        },
      }),
    ],
    [
      "unload source slot is zero",
      createMaterial({
        materialID: 401,
        jobUnique: "UNLOAD-JOB",
        partName: "Unload Part",
        process: 1,
        path: 1,
        location: { type: api.LocType.InBasket, basketId: 7, basketSlot: 0 },
        action: {
          type: api.ActionType.UnloadToCompletedMaterial,
          workId: "unload-work",
        },
      }),
    ],
  ])("suppresses completion when the basket %s", async (_description, material) => {
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
      material: [material],
    });

    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus,
    });

    await expect.element(screen.getByText(/Basket work is inconsistent/)).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Confirm" })).not.toBeInTheDocument();
    expect(submit).not.toHaveBeenCalled();
  });

  test("allows retry after a command error", async () => {
    const submit = vi
      .fn<CompletionHandler>()
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
          emptySlots: [1],
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
            loadToBasketSlot: 1,
            processAfterLoad: 1,
          },
        }),
      ],
    });

    completionRequests(submit);
    const screen = await renderInsightPage(<LoadStation loadNum={1} queues={[]} completed />, {
      currentStatus,
    });
    const complete = screen.getByRole("button", { name: "Confirm" });

    await complete.click();
    await expect.element(screen.getByText(/Unable to confirm basket work/)).toBeVisible();
    await expect.element(complete).toBeEnabled();
    await complete.click();
    await expect.element(screen.getByText(/Confirmation accepted/)).toBeVisible();
    expect(submit).toHaveBeenCalledTimes(2);
  });
});
