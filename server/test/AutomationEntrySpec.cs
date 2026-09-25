using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class AutomationEntrySpec
{
  [Test]
  [Arguments(false, InProcessMaterialAction.ActionType.Loading, 1, true)]
  [Arguments(false, InProcessMaterialAction.ActionType.LoadingToBasket, 1, false)]
  [Arguments(false, InProcessMaterialAction.ActionType.Loading, 2, false)]
  [Arguments(true, InProcessMaterialAction.ActionType.LoadingToBasket, 1, true)]
  [Arguments(true, InProcessMaterialAction.ActionType.Loading, 1, false)]
  [Arguments(true, InProcessMaterialAction.ActionType.LoadingToBasket, 2, false)]
  public async Task ActiveEntryIsAProcessOneLoadOntoTheRouteFirstCarrier(
    bool basketEntry,
    InProcessMaterialAction.ActionType type,
    int processAfterLoad,
    bool entry
  )
  {
    await Assert
      .That(
        JobHelpers.IsActiveAutomationEntry(Job(basketEntry), Material(-1, type, processAfterLoad))
      )
      .IsEqualTo(entry);
  }

  [Test]
  public async Task CommittedQuantityCountsIdentifiedMaterialOnceAndAnonymousByQuantity()
  {
    var committed = JobHelpers.CountCommittedToAutomation(
      Job(basketEntry: true),
      ImmutableHashSet.Create(101L, 102L),
      [
        // Already durably loaded: counts once.
        Material(102, InProcessMaterialAction.ActionType.LoadingToBasket, 1),
        Material(103, InProcessMaterialAction.ActionType.LoadingToBasket, 1),
        Material(-1, InProcessMaterialAction.ActionType.LoadingToBasket, 1),
        Material(-1, InProcessMaterialAction.ActionType.LoadingToBasket, 1),
        // Basket material moving onto a pallet is not another entry.
        Material(-1, InProcessMaterialAction.ActionType.Loading, 1),
        Material(-1, InProcessMaterialAction.ActionType.LoadingToBasket, 1) with
        {
          JobUnique = "other",
        },
      ]
    );

    await Assert.That(committed).IsEqualTo(5);
  }

  private static Job Job(bool basketEntry) =>
    new()
    {
      UniqueStr = "uniq",
      PartName = "part",
      Cycles = 10,
      RouteStartUTC = DateTime.MinValue,
      RouteEndUTC = DateTime.MinValue,
      Archived = false,
      Processes =
      [
        new ProcessInfo
        {
          BasketLoadStations = basketEntry ? [2] : null,
          BasketUnloadStations = basketEntry ? [2] : null,
          Paths = [],
        },
      ],
    };

  private static InProcessMaterial Material(
    long materialId,
    InProcessMaterialAction.ActionType type,
    int processAfterLoad
  ) =>
    new()
    {
      MaterialID = materialId,
      JobUnique = "uniq",
      PartName = "part",
      Process = processAfterLoad - 1,
      Path = 1,
      SignaledInspections = [],
      Location = new InProcessMaterialLocation { Type = InProcessMaterialLocation.LocType.Free },
      Action = new InProcessMaterialAction { Type = type, ProcessAfterLoad = processAfterLoad },
    };
}
