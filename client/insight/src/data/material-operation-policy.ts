/* Copyright (c) 2026, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or promote
      products derived from this software without specific prior written
      permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
 */

import { ActionType, IInProcessMaterial, LocType } from "../network/api.js";

export type MaterialOperationState =
  | {
      readonly kind: "ActiveLoadStationOperation";
      readonly cancellationId: string | null;
    }
  | {
      readonly kind: "AutomationControlled";
    }
  | {
      readonly kind: "HumanControlledQueue";
    }
  | {
      readonly kind: "AddToQueueProposal";
    };

type MaterialForPolicy = Readonly<IInProcessMaterial> | null;

function nonBlankCancellationId(material: Readonly<IInProcessMaterial>): string | null {
  const cancellationId = material.action.loadCancellationId;
  return cancellationId !== undefined && cancellationId.trim() !== "" ? cancellationId : null;
}

function hasLoadStationAction(material: Readonly<IInProcessMaterial>): boolean {
  switch (material.action.type) {
    case ActionType.Loading:
    case ActionType.UnloadToInProcess:
    case ActionType.UnloadToCompletedMaterial:
    case ActionType.LoadingToBasket:
      return true;
    default:
      return false;
  }
}

export function materialOperationState(
  material: Readonly<IInProcessMaterial>,
): MaterialOperationState {
  const cancellationId = nonBlankCancellationId(material);
  if (cancellationId !== null || hasLoadStationAction(material)) {
    return { kind: "ActiveLoadStationOperation", cancellationId };
  }

  if (material.location.type === LocType.InQueue && material.action.type === ActionType.Waiting) {
    return { kind: "HumanControlledQueue" };
  }

  if (
    material.location.type === LocType.OnPallet ||
    material.location.type === LocType.InBasket ||
    material.action.type === ActionType.Machining
  ) {
    return { kind: "AutomationControlled" };
  }

  return { kind: "AddToQueueProposal" };
}

export function isActiveLoadStationOperation(material: Readonly<IInProcessMaterial>): boolean {
  return materialOperationState(material).kind === "ActiveLoadStationOperation";
}

export function canCancelLoad(material: MaterialForPolicy): boolean {
  if (material === null) return false;
  const state = materialOperationState(material);
  return state.kind === "ActiveLoadStationOperation" && state.cancellationId !== null;
}

export function canSignalQuarantine(material: MaterialForPolicy): boolean {
  return material !== null && materialOperationState(material).kind === "AutomationControlled";
}

export function canDirectlyQuarantine(material: MaterialForPolicy): boolean {
  return material !== null && materialOperationState(material).kind === "HumanControlledQueue";
}

export function canRemoveFromQueue(material: MaterialForPolicy): boolean {
  return canDirectlyQuarantine(material);
}

export function canInvalidateMaterial(material: MaterialForPolicy): boolean {
  return material === null || materialOperationState(material).kind === "AddToQueueProposal";
}

export function canAddOrMoveMaterialToQueue(material: MaterialForPolicy): boolean {
  if (material === null) return true;

  const state = materialOperationState(material);
  return state.kind === "AddToQueueProposal" || state.kind === "HumanControlledQueue";
}
