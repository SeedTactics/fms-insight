using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

internal sealed record TestMaterialToLoadOntoBasket
{
  public required ImmutableList<long> MaterialIDs { get; init; }
  public required int Process { get; init; }
  public required TimeSpan ActiveOperationTime { get; init; }
  public string ForeignID { get; init; }
  public string OriginalMessage { get; init; }
}

internal sealed record TestMaterialToUnloadFromBasket
{
  public required ImmutableDictionary<long, string> MaterialIDToQueue { get; init; }
  public required int Process { get; init; }
  public required TimeSpan ActiveOperationTime { get; init; }
  public string ForeignID { get; init; }
  public string OriginalMessage { get; init; }
}

internal static class BasketStationTestExtensions
{
  public static IEnumerable<LogEntry> RecordTestBasketLoadUnload(
    this IRepository repository,
    TestMaterialToLoadOntoBasket toLoad,
    TestMaterialToUnloadFromBasket toUnload,
    int lulNum,
    ContainerIdentity basketIdentity,
    TimeSpan totalElapsed,
    DateTime timeUTC,
    IReadOnlyDictionary<string, string> externalQueues,
    BasketLoadUnloadCompletion basketCompletion = null
  ) =>
    repository.RecordTestBasketLoadUnload(
      toLoad is null ? [] : [toLoad],
      toUnload is null ? [] : [toUnload],
      lulNum,
      basketIdentity,
      totalElapsed,
      timeUTC,
      externalQueues,
      basketCompletion
    );

  public static IEnumerable<LogEntry> RecordTestBasketLoadUnload(
    this IRepository repository,
    IReadOnlyList<TestMaterialToLoadOntoBasket> toLoad,
    IReadOnlyList<TestMaterialToUnloadFromBasket> toUnload,
    int lulNum,
    ContainerIdentity basketIdentity,
    TimeSpan totalElapsed,
    DateTime timeUTC,
    IReadOnlyDictionary<string, string> externalQueues,
    BasketLoadUnloadCompletion basketCompletion
  )
  {
    var transfers = ImmutableList.CreateBuilder<BasketStationTransfer>();
    foreach (var unload in toUnload)
    {
      var material = Material(
        unload.MaterialIDToQueue.Keys,
        unload.Process,
        basketCompletion?.Transfers.OfType<BasketTransfer.UnloadFromBasket>()
      );
      transfers.Add(
        new BasketStationTransfer.UnloadFromBasket
        {
          BasketIdentity =
            MatchingIdentity(
              material,
              basketCompletion?.Transfers.OfType<BasketTransfer.UnloadFromBasket>()
            ) ?? basketIdentity,
          Material = material,
          ActiveOperationTime = unload.ActiveOperationTime,
          DestinationQueue = unload.MaterialIDToQueue.Values.Distinct().SingleOrDefault(),
        }
      );
    }
    foreach (var load in toLoad)
    {
      var material = Material(
        load.MaterialIDs,
        load.Process,
        basketCompletion?.Transfers.OfType<BasketTransfer.LoadOntoBasket>()
      );
      transfers.Add(
        new BasketStationTransfer.LoadOntoBasket
        {
          BasketIdentity =
            MatchingIdentity(
              material,
              basketCompletion?.Transfers.OfType<BasketTransfer.LoadOntoBasket>()
            ) ?? basketIdentity,
          Material = material,
          ActiveOperationTime = load.ActiveOperationTime,
        }
      );
    }

    var operationIds = toUnload
      .Select(unload => unload.ForeignID)
      .Concat(toLoad.Select(load => load.ForeignID))
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .Distinct()
      .ToImmutableList();
    return repository.RecordBasketStationLoadUnload(
      new BasketStationLoadUnload
      {
        Transfers = transfers.ToImmutable(),
        CycleBoundaries = basketCompletion?.CycleBoundaries ?? [],
      },
      lulNum,
      totalElapsed,
      timeUTC,
      externalQueues,
      operationIds.IsEmpty
        ? $"test-basket-station:{Guid.NewGuid():N}"
        : string.Join("|", operationIds),
      toUnload
        .Select(unload => unload.OriginalMessage)
        .Concat(toLoad.Select(load => load.OriginalMessage))
        .FirstOrDefault(message => message is not null)
    );
  }

  public static IEnumerable<LogEntry> RecordTestBasketCompletion(
    this IRepository repository,
    BasketLoadUnloadCompletion basketCompletion,
    int lulNum,
    DateTime timeUTC,
    string foreignId,
    string originalMessage = null
  ) =>
    repository.RecordBasketStationLoadUnload(
      new BasketStationLoadUnload
      {
        Transfers = basketCompletion
          .Transfers.Select<BasketTransfer, BasketStationTransfer>(transfer =>
            transfer switch
            {
              BasketTransfer.LoadOntoBasket load => new BasketStationTransfer.LoadOntoBasket
              {
                BasketIdentity = load.BasketIdentity,
                Material = load.Material,
                ActiveOperationTime = TimeSpan.Zero,
              },
              BasketTransfer.UnloadFromBasket unload => new BasketStationTransfer.UnloadFromBasket
              {
                BasketIdentity = unload.BasketIdentity,
                Material = unload.Material,
                ActiveOperationTime = TimeSpan.Zero,
              },
              _ => throw new ArgumentOutOfRangeException(nameof(basketCompletion)),
            }
          )
          .ToImmutableList(),
        CycleBoundaries = basketCompletion.CycleBoundaries,
      },
      lulNum,
      TimeSpan.Zero,
      timeUTC,
      ImmutableDictionary<string, string>.Empty,
      foreignId,
      originalMessage
    );

  public static IEnumerable<LogEntry> RecordTestBasketOnlyLoadUnload(
    this IRepository repository,
    TestMaterialToLoadOntoBasket toLoad,
    IReadOnlyList<EventLogMaterial> previouslyLoaded,
    TestMaterialToUnloadFromBasket toUnload,
    int lulNum,
    int basketId,
    TimeSpan totalElapsed,
    DateTime timeUTC,
    IReadOnlyDictionary<string, string> externalQueues
  )
  {
    var identity = new ContainerIdentity.Numbered { ContainerNum = basketId };
    var transfers = ImmutableList.CreateBuilder<BasketStationTransfer>();
    if (toUnload is not null)
      transfers.Add(
        new BasketStationTransfer.UnloadFromBasket
        {
          BasketIdentity = identity,
          Material = Material<BasketTransfer>(
            toUnload.MaterialIDToQueue.Keys,
            toUnload.Process,
            null
          ),
          ActiveOperationTime = toUnload.ActiveOperationTime,
          DestinationQueue = toUnload.MaterialIDToQueue.Values.Distinct().SingleOrDefault(),
        }
      );
    if (toLoad is not null)
      transfers.Add(
        new BasketStationTransfer.LoadOntoBasket
        {
          BasketIdentity = identity,
          Material = Material<BasketTransfer>(toLoad.MaterialIDs, toLoad.Process, null),
          ActiveOperationTime = toLoad.ActiveOperationTime,
        }
      );

    var boundaries = ImmutableList.CreateBuilder<BasketCycleBoundary>();
    var lastCycle = repository
      .CurrentBasketLog(basketId, includeLastCycleEvt: true)
      .LastOrDefault(log => log.LogType == LogType.BasketCycle);
    if (lastCycle?.StartOfCycle == true)
      boundaries.Add(
        new BasketCycleBoundary.End
        {
          BasketIdentity = identity,
          Material = lastCycle.Material.Select(EventLogMaterial.FromLogMat).ToImmutableList(),
          ReconciledBasketIdentities = [],
        }
      );
    var nextContents = (previouslyLoaded ?? [])
      .Concat(
        toLoad?.MaterialIDs.Select(id => new EventLogMaterial
        {
          MaterialID = id,
          Process = toLoad.Process,
          Face = 0,
        }) ?? []
      )
      .ToImmutableList();
    if (!nextContents.IsEmpty)
      boundaries.Add(
        new BasketCycleBoundary.Start { BasketIdentity = identity, Material = nextContents }
      );

    var foreignId =
      toUnload?.ForeignID ?? toLoad?.ForeignID ?? $"test-basket-only:{Guid.NewGuid():N}";
    return repository.RecordBasketStationLoadUnload(
      new BasketStationLoadUnload
      {
        Transfers = transfers.ToImmutable(),
        CycleBoundaries = boundaries.ToImmutable(),
      },
      lulNum,
      totalElapsed,
      timeUTC,
      externalQueues,
      foreignId,
      toUnload?.OriginalMessage ?? toLoad?.OriginalMessage
    );
  }

  private static ImmutableList<EventLogMaterial> Material<TTransfer>(
    IEnumerable<long> materialIds,
    int process,
    IEnumerable<TTransfer> completedTransfers
  )
    where TTransfer : BasketTransfer
  {
    var ids = materialIds.ToImmutableHashSet();
    return completedTransfers
        ?.SingleOrDefault(transfer =>
          transfer
            .Material.Select(material => material.MaterialID)
            .ToImmutableHashSet()
            .SetEquals(ids)
        )
        ?.Material
      ?? ids.Select(id => new EventLogMaterial
        {
          MaterialID = id,
          Process = process,
          Face = 0,
        })
        .ToImmutableList();
  }

  private static ContainerIdentity MatchingIdentity<TTransfer>(
    ImmutableList<EventLogMaterial> material,
    IEnumerable<TTransfer> completedTransfers
  )
    where TTransfer : BasketTransfer =>
    completedTransfers
      ?.SingleOrDefault(transfer =>
        transfer
          .Material.Select(item => item.MaterialID)
          .ToImmutableHashSet()
          .SetEquals(material.Select(item => item.MaterialID))
      )
      ?.BasketIdentity;
}
