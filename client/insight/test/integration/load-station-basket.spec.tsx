import { describe, expect, test, vi } from "vitest";

import LoadStation from "../../src/components/station-monitor/LoadStation.js";
import type { SubmitBasketLoadStationCommand } from "../../src/components/station-monitor/BasketLoadStationWork.js";
import type {
  BasketMovementCompletionCommand,
  BasketMovementCompletionReceipt,
  SubmitBasketMovementCompletion,
} from "../../src/components/station-monitor/BasketMovementArrival.js";
import type { SubmitBasketLocationCorrection } from "../../src/components/station-monitor/BasketMovementArrival.js";
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

describe("basket arrival", () => {
  test("completes the current load-station instruction with the visible basket", async () => {
    const submit = vi.fn<SubmitBasketMovementCompletion>(async (stationNumber, command) =>
      movementReceipt(stationNumber, command),
    );
    const currentStatus = createCurrentStatus({
      basketMoveInstructions: [loadStationInstruction("bring-5", 5)],
    });
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
      />,
      { currentStatus },
    );

    await expect.element(screen.getByText("Bring basket 5 to this station")).toBeVisible();
    await screen.getByRole("button", { name: "Basket 5 arrived" }).click();

    expect(submit).toHaveBeenCalledTimes(1);
    expect(submit).toHaveBeenCalledWith(1, {
      commandId: expect.any(String),
      instructionId: "bring-5",
      observedBasketId: 5,
    });
    await expect.element(screen.getByText(/Arrival recorded/)).toBeVisible();
  });

  test("records a different visible basket and waits for prerequisites", async () => {
    const submit = vi.fn<SubmitBasketMovementCompletion>(async (stationNumber, command) =>
      movementReceipt(stationNumber, command),
    );
    const currentStatus = createCurrentStatus({
      basketMoveInstructions: [
        storageInstruction("move-out", 4),
        loadStationInstruction("bring-5", 5, "move-out"),
      ],
    });
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
      />,
      { currentStatus },
    );

    await expect
      .element(screen.getByText("Bring basket 5 to this station"))
      .not.toBeInTheDocument();

    screen.store.set(
      onLoadCurrentSt,
      createCurrentStatus({
        basketMoveInstructions: [loadStationInstruction("bring-5", 5, "move-out")],
      }),
    );
    await expect.element(screen.getByText("Bring basket 5 to this station")).toBeVisible();
    await screen.getByRole("button", { name: "Different basket" }).click();
    await screen.getByRole("spinbutton", { name: "Basket number" }).fill("12");
    await screen.getByRole("button", { name: "Record basket arrival" }).click();

    expect(submit).toHaveBeenLastCalledWith(1, {
      commandId: expect.any(String),
      instructionId: "bring-5",
      observedBasketId: 12,
    });
  });

  test("offers immediate change and undo for an accepted arrival", async () => {
    const submit = vi.fn<SubmitBasketMovementCompletion>(async (stationNumber, command) =>
      movementReceipt(stationNumber, command),
    );
    const correct = vi.fn<SubmitBasketLocationCorrection>(async () => "accepted");
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
        submitBasketLocationCorrection={correct}
      />,
      {
        currentStatus: createCurrentStatus({
          basketMoveInstructions: [loadStationInstruction("bring-5", 5)],
        }),
      },
    );

    await screen.getByRole("button", { name: "Basket 5 arrived" }).click();
    const targetObservationId = movementObservationId(submit.mock.calls[0][1]);
    await screen.getByRole("button", { name: "Change" }).click();
    await screen.getByRole("spinbutton", { name: "Correct basket number" }).fill("12");
    await screen.getByRole("button", { name: "Save correction" }).click();

    expect(correct).toHaveBeenCalledWith(1, {
      correctionId: expect.any(String),
      targetObservationId,
      replacementBasketId: 12,
      replacementObservationId: expect.any(String),
    });
    await expect.element(screen.getByText("Arrival corrected to basket 12.")).toBeVisible();
  });

  test("retracts an accepted arrival with Undo", async () => {
    const submit = vi.fn<SubmitBasketMovementCompletion>(async (stationNumber, command) =>
      movementReceipt(stationNumber, command),
    );
    const correct = vi.fn<SubmitBasketLocationCorrection>(async () => "accepted");
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
        submitBasketLocationCorrection={correct}
      />,
      {
        currentStatus: createCurrentStatus({
          basketMoveInstructions: [loadStationInstruction("bring-5", 5)],
        }),
      },
    );

    await screen.getByRole("button", { name: "Basket 5 arrived" }).click();
    const targetObservationId = movementObservationId(submit.mock.calls[0][1]);
    await screen.getByRole("button", { name: "Undo" }).click();

    expect(correct).toHaveBeenCalledWith(1, {
      correctionId: expect.any(String),
      targetObservationId,
      replacementBasketId: null,
      replacementObservationId: null,
    });
    await expect.element(screen.getByText("Arrival observation retracted.")).toBeVisible();
    await expect.element(screen.getByText(/Arrival recorded/)).not.toBeInTheDocument();
  });

  test("changes relative to the basket actually recorded", async () => {
    const submit = vi.fn<SubmitBasketMovementCompletion>(async (stationNumber, command) =>
      movementReceipt(stationNumber, command),
    );
    const correct = vi.fn<SubmitBasketLocationCorrection>(async () => "accepted");
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
        submitBasketLocationCorrection={correct}
      />,
      {
        currentStatus: createCurrentStatus({
          basketMoveInstructions: [loadStationInstruction("bring-5", 5)],
        }),
      },
    );

    await screen.getByRole("button", { name: "Different basket" }).click();
    await screen.getByRole("spinbutton", { name: "Basket number" }).fill("12");
    await screen.getByRole("button", { name: "Record basket arrival" }).click();
    await screen.getByRole("button", { name: "Change" }).click();
    await screen.getByRole("spinbutton", { name: "Correct basket number" }).fill("5");
    await screen.getByRole("button", { name: "Save correction" }).click();

    expect(correct).toHaveBeenCalledWith(1, {
      correctionId: expect.any(String),
      targetObservationId: movementObservationId(submit.mock.calls[0][1]),
      replacementBasketId: 5,
      replacementObservationId: expect.any(String),
    });
  });

  test("retries an uncertain correction with the same command", async () => {
    const submit = vi
      .fn<SubmitBasketMovementCompletion>()
      .mockImplementation(async (stationNumber, command) =>
        movementReceipt(stationNumber, command),
      );
    const correct = vi
      .fn<SubmitBasketLocationCorrection>()
      .mockRejectedValueOnce(new Error("response lost"))
      .mockResolvedValue("accepted");
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
        submitBasketLocationCorrection={correct}
      />,
      {
        currentStatus: createCurrentStatus({
          basketMoveInstructions: [loadStationInstruction("bring-5", 5)],
        }),
      },
    );

    await screen.getByRole("button", { name: "Basket 5 arrived" }).click();
    await screen.getByRole("button", { name: "Undo" }).click();
    await expect.element(screen.getByText("Unable to correct the recorded arrival.")).toBeVisible();
    const firstCommand = correct.mock.calls[0][1];
    await screen.getByRole("button", { name: "Retry correction" }).click();

    expect(correct).toHaveBeenCalledTimes(2);
    expect(correct.mock.calls[1][1]).toEqual(firstCommand);
    await expect.element(screen.getByText("Arrival observation retracted.")).toBeVisible();
  });

  test("discards an uncertain arrival retry when a newer instruction appears", async () => {
    const submit = vi
      .fn<SubmitBasketMovementCompletion>()
      .mockRejectedValueOnce(new Error("response lost"))
      .mockImplementation(async (stationNumber, command) =>
        movementReceipt(stationNumber, command),
      );
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
      />,
      {
        currentStatus: createCurrentStatus({
          basketMoveInstructions: [loadStationInstruction("arrival-a", 5)],
        }),
      },
    );

    await screen.getByRole("button", { name: "Basket 5 arrived" }).click();
    await expect.element(screen.getByRole("button", { name: "Retry arrival" })).toBeVisible();

    screen.store.set(
      onLoadCurrentSt,
      createCurrentStatus({
        basketMoveInstructions: [loadStationInstruction("arrival-b", 12)],
      }),
    );

    await expect
      .element(screen.getByRole("button", { name: "Retry arrival" }))
      .not.toBeInTheDocument();
    await expect.element(screen.getByRole("button", { name: "Basket 12 arrived" })).toBeEnabled();
    await screen.getByRole("button", { name: "Basket 12 arrived" }).click();
    expect(submit).toHaveBeenCalledTimes(2);
    expect(submit.mock.calls[1][1]).toMatchObject({ instructionId: "arrival-b" });
  });

  test("keeps the accepted arrival correction card after status removes the instruction", async () => {
    const submit = vi.fn<SubmitBasketMovementCompletion>(async (stationNumber, command) =>
      movementReceipt(stationNumber, command),
    );
    const correct = vi.fn<SubmitBasketLocationCorrection>(async () => "accepted");
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
        submitBasketLocationCorrection={correct}
      />,
      {
        currentStatus: createCurrentStatus({
          basketMoveInstructions: [loadStationInstruction("bring-5", 5)],
        }),
      },
    );

    await screen.getByRole("button", { name: "Basket 5 arrived" }).click();
    screen.store.set(onLoadCurrentSt, createCurrentStatus());

    await expect.element(screen.getByText(/Arrival recorded/)).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Change" })).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Undo" })).toBeVisible();
  });

  test("does not carry an accepted receipt to another load station", async () => {
    const submit = vi.fn<SubmitBasketMovementCompletion>(async (stationNumber, command) =>
      movementReceipt(stationNumber, command),
    );
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
      />,
      {
        currentStatus: createCurrentStatus({
          basketMoveInstructions: [loadStationInstruction("bring-5", 5)],
        }),
      },
    );

    await screen.getByRole("button", { name: "Basket 5 arrived" }).click();
    await screen.rerender(
      <LoadStation
        loadNum={2}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
      />,
    );
    screen.store.set(onLoadCurrentSt, createCurrentStatus());

    await expect.element(screen.getByText(/Arrival recorded/)).not.toBeInTheDocument();
    await expect
      .element(screen.getByText("Bring basket 5 to this station"))
      .not.toBeInTheDocument();
  });

  test("ignores a late arrival response after changing stations", async () => {
    let resolveSubmit: ((receipt: BasketMovementCompletionReceipt) => void) | undefined;
    const pending = new Promise<BasketMovementCompletionReceipt>((resolve) => {
      resolveSubmit = resolve;
    });
    const submit = vi.fn<SubmitBasketMovementCompletion>(() => pending);
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
      />,
      {
        currentStatus: createCurrentStatus({
          basketMoveInstructions: [loadStationInstruction("bring-5", 5)],
        }),
      },
    );

    await screen.getByRole("button", { name: "Basket 5 arrived" }).click();
    const command = submit.mock.calls[0][1];
    await screen.rerender(
      <LoadStation
        loadNum={2}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
      />,
    );
    screen.store.set(onLoadCurrentSt, createCurrentStatus());
    resolveSubmit?.(movementReceipt(1, command));

    await expect.element(screen.getByText(/Arrival recorded/)).not.toBeInTheDocument();
  });

  test("does not resurrect an earlier receipt after a later instruction disappears", async () => {
    const submit = vi.fn<SubmitBasketMovementCompletion>(async (stationNumber, command) =>
      movementReceipt(stationNumber, command),
    );
    const screen = await renderInsightPage(
      <LoadStation
        loadNum={1}
        queues={[]}
        completed={false}
        submitBasketMovementCompletion={submit}
      />,
      {
        currentStatus: createCurrentStatus({
          basketMoveInstructions: [loadStationInstruction("arrival-a", 5)],
        }),
      },
    );

    await screen.getByRole("button", { name: "Basket 5 arrived" }).click();
    screen.store.set(
      onLoadCurrentSt,
      createCurrentStatus({
        basketMoveInstructions: [loadStationInstruction("arrival-b", 12)],
      }),
    );
    await expect.element(screen.getByText("Bring basket 12 to this station")).toBeVisible();

    screen.store.set(onLoadCurrentSt, createCurrentStatus());

    await expect
      .element(screen.getByText("Bring basket 5 to this station"))
      .not.toBeInTheDocument();
    await expect.element(screen.getByText(/Arrival recorded/)).not.toBeInTheDocument();
  });
});

function loadStationInstruction(
  instructionId: string,
  basketId: number,
  prerequisiteInstructionId?: string,
): api.BasketMoveInstruction {
  return new api.BasketMoveInstruction({
    instructionId,
    basketId,
    source: new api.BasketPosition({
      location: api.BasketLocationEnum.Storage,
      locationNum: 1,
    }),
    destination: new api.BasketPosition({
      location: api.BasketLocationEnum.LoadUnload,
      locationNum: 1,
    }),
    reason: api.BasketMoveReason.LoadMaterial,
    displayText: `Bring basket ${basketId}`,
    prerequisiteInstructionId,
  });
}

function movementReceipt(
  stationNumber: number,
  command: Readonly<BasketMovementCompletionCommand>,
): BasketMovementCompletionReceipt {
  return {
    stationNumber,
    instructionId: command.instructionId,
    observationId: movementObservationId(command),
    observedBasketId: command.observedBasketId,
  };
}

function movementObservationId(command: Readonly<BasketMovementCompletionCommand>): string {
  return `observation-${command.commandId}`;
}

function storageInstruction(instructionId: string, basketId: number): api.BasketMoveInstruction {
  return new api.BasketMoveInstruction({
    instructionId,
    basketId,
    source: new api.BasketPosition({
      location: api.BasketLocationEnum.LoadStationStaging,
      locationNum: 1,
      zone: 1,
    }),
    destination: new api.BasketPosition({
      location: api.BasketLocationEnum.Storage,
      locationNum: 1,
    }),
    reason: api.BasketMoveReason.ReturnToStorage,
    displayText: `Move basket ${basketId} to storage`,
  });
}

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

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );

    await expect
      .element(region(screen, "basket-load-station-slot-1"))
      .toHaveTextContent("Unload into queue Transfer Queue");
    await expect
      .element(region(screen, "basket-load-station-slot-1"))
      .toHaveTextContent("Load from Transfer Queue");
    await expect
      .element(region(screen, "basket-load-station-slot-2"))
      .toHaveTextContent("Unload from Basket 7 to completed material");
    await expect
      .element(region(screen, "basket-load-station-slot-3"))
      .toHaveTextContent("Load from raw material");
    await expect
      .element(region(screen, "basket-load-station-slot-4"))
      .toHaveTextContent("Unload, re-plate, and reload this slot");
    await expect
      .element(region(screen, "load-station-material"))
      .toHaveTextContent("Load into Basket 7 slot 3");

    await screen.getByRole("button", { name: "Confirm" }).click();

    expect(submit).toHaveBeenCalledWith(1, { workId: "basket-work" });
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

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );

    const complete = screen.getByRole("button", { name: "Confirm" });
    await complete.click();
    await expect.element(screen.getByText(/Basket work changed/)).toBeVisible();
    await expect.element(complete).toBeDisabled();
    expect(submit).toHaveBeenCalledWith(1, { workId: "stale-work" });
    expect(submit).toHaveBeenCalledTimes(1);
  });

  test("ignores a submission response after the displayed work changes", async () => {
    let resolveSubmission!: (result: "accepted" | "conflict") => void;
    const pendingSubmission = new Promise<"accepted" | "conflict">((resolve) => {
      resolveSubmission = resolve;
    });
    const submit = vi.fn(() => pendingSubmission);

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus: confirmableBasketStatus("first-work", "First Part") },
    );
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
    expect(submit).toHaveBeenCalledWith(1, { workId: "first-work" });
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

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );

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

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );

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

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );

    await expect.element(screen.getByText(/Basket work is inconsistent/)).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Confirm" })).not.toBeInTheDocument();
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

    const screen = await renderInsightPage(
      <LoadStation loadNum={1} queues={[]} completed submitBasketLoadStationCommand={submit} />,
      { currentStatus },
    );
    const complete = screen.getByRole("button", { name: "Confirm" });

    await complete.click();
    await expect.element(screen.getByText(/Unable to confirm basket work/)).toBeVisible();
    await expect.element(complete).toBeEnabled();
    await complete.click();
    await expect.element(screen.getByText(/Confirmation accepted/)).toBeVisible();
    expect(submit).toHaveBeenCalledTimes(2);
  });
});
