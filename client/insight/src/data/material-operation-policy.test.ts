import { describe, expect, it } from "vitest";
import {
  ActionType,
  InProcessMaterial,
  InProcessMaterialAction,
  InProcessMaterialLocation,
  LocType,
} from "../network/api.js";
import {
  canAddOrMoveMaterialToQueue,
  canCancelLoad,
  canDirectlyQuarantine,
  canInvalidateMaterial,
  canRemoveFromQueue,
  canSignalQuarantine,
  isActiveLoadStationOperation,
  materialOperationState,
} from "./material-operation-policy.js";

function material(
  location: LocType,
  action: ActionType,
  loadCancellationId?: string,
): InProcessMaterial {
  return new InProcessMaterial({
    materialID: 1,
    jobUnique: "job",
    partName: "part",
    process: 1,
    path: 1,
    signaledInspections: [],
    location: new InProcessMaterialLocation({ type: location, currentQueue: "queue" }),
    action: new InProcessMaterialAction({ type: action, loadCancellationId }),
  });
}

describe("materialOperationState", () => {
  it.each([
    ActionType.Loading,
    ActionType.UnloadToInProcess,
    ActionType.UnloadToCompletedMaterial,
    ActionType.LoadingToBasket,
  ])("classifies %s as active load-station work", (action) => {
    const state = materialOperationState(material(LocType.Free, action));
    expect(state).toEqual({ kind: "ActiveLoadStationOperation", cancellationId: null });
    expect(isActiveLoadStationOperation(material(LocType.Free, action))).toBe(true);
  });

  it("classifies a nonblank cancellation id as active load-station work for any action", () => {
    expect(
      materialOperationState(material(LocType.OnPallet, ActionType.Machining, "operation-1")),
    ).toEqual({ kind: "ActiveLoadStationOperation", cancellationId: "operation-1" });
  });

  it("does not treat a blank cancellation id as cancellation capability", () => {
    expect(materialOperationState(material(LocType.OnPallet, ActionType.Waiting, "  "))).toEqual({
      kind: "AutomationControlled",
    });
    expect(canCancelLoad(material(LocType.OnPallet, ActionType.Waiting, "  "))).toBe(false);
  });

  it("classifies waiting material in a queue as human-controlled", () => {
    expect(materialOperationState(material(LocType.InQueue, ActionType.Waiting))).toEqual({
      kind: "HumanControlledQueue",
    });
  });

  it.each([LocType.OnPallet, LocType.InBasket])(
    "classifies waiting material at %s as automation-controlled",
    (location) => {
      expect(materialOperationState(material(location, ActionType.Waiting))).toEqual({
        kind: "AutomationControlled",
      });
    },
  );

  it("classifies machining material as automation-controlled", () => {
    expect(materialOperationState(material(LocType.Free, ActionType.Machining))).toEqual({
      kind: "AutomationControlled",
    });
  });

  it("classifies other free material as an add-to-queue proposal", () => {
    expect(materialOperationState(material(LocType.Free, ActionType.Waiting))).toEqual({
      kind: "AddToQueueProposal",
    });
  });
});

describe("material operation permissions", () => {
  const activeWithCancellation = material(LocType.InQueue, ActionType.Loading, "load-1");
  const activeWithoutCancellation = material(LocType.InQueue, ActionType.Loading);
  const automated = material(LocType.OnPallet, ActionType.Waiting);
  const queued = material(LocType.InQueue, ActionType.Waiting);
  const proposal = material(LocType.Free, ActionType.Waiting);

  it("allows cancellation only for active work with a nonblank cancellation id", () => {
    expect(canCancelLoad(activeWithCancellation)).toBe(true);
    expect(canCancelLoad(activeWithoutCancellation)).toBe(false);
    expect(canCancelLoad(null)).toBe(false);
  });

  it("allows deferred quarantine only for automation-controlled material", () => {
    expect(canSignalQuarantine(automated)).toBe(true);
    expect(canSignalQuarantine(queued)).toBe(false);
    expect(canSignalQuarantine(activeWithCancellation)).toBe(false);
    expect(canSignalQuarantine(null)).toBe(false);
  });

  it("allows direct quarantine and queue removal only for human-controlled queue material", () => {
    expect(canDirectlyQuarantine(queued)).toBe(true);
    expect(canRemoveFromQueue(queued)).toBe(true);
    expect(canDirectlyQuarantine(automated)).toBe(false);
    expect(canRemoveFromQueue(activeWithCancellation)).toBe(false);
  });

  it("allows invalidation and queue addition for proposals and absent current material", () => {
    expect(canInvalidateMaterial(proposal)).toBe(true);
    expect(canAddOrMoveMaterialToQueue(proposal)).toBe(true);
    expect(canInvalidateMaterial(null)).toBe(true);
    expect(canAddOrMoveMaterialToQueue(null)).toBe(true);
  });

  it("allows queue movement for human-controlled queued material", () => {
    expect(canAddOrMoveMaterialToQueue(queued)).toBe(true);
    expect(canInvalidateMaterial(queued)).toBe(false);
  });

  it("blocks proposal operations for material under active or automated control", () => {
    for (const controlled of [activeWithCancellation, activeWithoutCancellation, automated]) {
      expect(canInvalidateMaterial(controlled)).toBe(false);
      expect(canAddOrMoveMaterialToQueue(controlled)).toBe(false);
    }
  });
});
