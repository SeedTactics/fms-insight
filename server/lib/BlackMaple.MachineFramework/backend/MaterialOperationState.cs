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
/// Classifies the current owner of material-changing operations. Callers must hold the
/// <c>JobsAndQueuesFromDb</c> change lock while using the classification with current status.
/// </summary>
internal static class MaterialOperationState
{
  public static MaterialOperationKind Classify(InProcessMaterial material)
  {
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

  private static bool IsLoadStationAction(InProcessMaterialAction.ActionType action) =>
    action
      is InProcessMaterialAction.ActionType.Loading
        or InProcessMaterialAction.ActionType.UnloadToInProcess
        or InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
        or InProcessMaterialAction.ActionType.LoadingToBasket;
}
