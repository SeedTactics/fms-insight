/* Copyright (c) 2026, SeedTactics

All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted
provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright notice, this list of
      conditions and the following disclaimer.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
 */

namespace BlackMaple.MachineFramework;

internal enum MaterialOperationKind
{
  ActiveLoadStationOperation,
  AutomationControlled,
  HumanControlledQueuedMaterial,
  EligibleAddToQueueProposal,
}

/// <summary>
/// Determines material-operation permissions from current status. Backend callers must hold
/// their mutation gate while using these permissions to validate and apply an operation.
/// </summary>
public static class MaterialOperationState
{
  internal static MaterialOperationKind Classify(InProcessMaterial material)
  {
    if (material.Action.AutomatedTransfer)
      return MaterialOperationKind.AutomationControlled;

    // A cancellation ID is authoritative even when a backend uses an unusual action or location
    // representation. A non-null blank ID is invalid backend state, but remains protected here so
    // a direct caller cannot accidentally bypass station-operation exclusivity.
    if (material.Action.LoadCancellationId is not null || IsLoadStationAction(material.Action.Type))
      return MaterialOperationKind.ActiveLoadStationOperation;

    if (
      material.Location.Type == InProcessMaterialLocation.LocType.InQueue
      && material.Action.Type == InProcessMaterialAction.ActionType.Waiting
    )
      return MaterialOperationKind.HumanControlledQueuedMaterial;

    if (
      material.Location.Type
        is InProcessMaterialLocation.LocType.OnPallet
          or InProcessMaterialLocation.LocType.InBasket
      || material.Action.Type == InProcessMaterialAction.ActionType.Machining
    )
      return MaterialOperationKind.AutomationControlled;

    return MaterialOperationKind.EligibleAddToQueueProposal;
  }

  public static bool CanSignalQuarantine(InProcessMaterial material) =>
    material.MaterialID >= 0
    && (
      Classify(material) == MaterialOperationKind.AutomationControlled
      || material.Action.Type
        is InProcessMaterialAction.ActionType.UnloadToInProcess
          or InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
    );

  public static bool CanCancelLoad(InProcessMaterial material) =>
    !material.Action.AutomatedTransfer
    && Classify(material) == MaterialOperationKind.ActiveLoadStationOperation
    && !string.IsNullOrWhiteSpace(material.Action.LoadCancellationId);

  public static bool CanDirectlyQuarantine(InProcessMaterial material) =>
    Classify(material) == MaterialOperationKind.HumanControlledQueuedMaterial;

  public static bool CanAddOrMoveToQueue(InProcessMaterial material) =>
    Classify(material)
      is MaterialOperationKind.HumanControlledQueuedMaterial
        or MaterialOperationKind.EligibleAddToQueueProposal;

  public static bool CanInvalidate(InProcessMaterial material) =>
    Classify(material) == MaterialOperationKind.EligibleAddToQueueProposal;

  public static (int Process, int Path) QuarantineRoute(InProcessMaterial material) =>
    material.Action.AutomatedTransfer
    && material.Action.Type
      is InProcessMaterialAction.ActionType.Loading
        or InProcessMaterialAction.ActionType.LoadingToBasket
      ? (material.Action.ProcessAfterLoad ?? 0, material.Action.PathAfterLoad ?? 0)
      : (material.Process, material.Path);

  private static bool IsLoadStationAction(InProcessMaterialAction.ActionType action) =>
    action
      is InProcessMaterialAction.ActionType.Loading
        or InProcessMaterialAction.ActionType.UnloadToInProcess
        or InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
        or InProcessMaterialAction.ActionType.LoadingToBasket;
}
