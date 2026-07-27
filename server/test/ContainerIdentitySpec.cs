using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;
using VerifyTUnit;

namespace BlackMaple.FMSInsight.Tests;

#pragma warning disable TUnit0018 // Restart coverage intentionally replaces the repository config.
public sealed class ContainerIdentitySpec : IDisposable
{
  private readonly string _databaseFile = Path.Combine(
    Path.GetTempPath(),
    Guid.NewGuid().ToString("N") + ".db"
  );
  private RepositoryConfig _repositoryConfig;

  public ContainerIdentitySpec()
  {
    _repositoryConfig = RepositoryConfig.InitializeEventDatabase(
      null,
      _databaseFile,
      pooling: false
    );
  }

  public void Dispose()
  {
    _repositoryConfig.Dispose();
    if (File.Exists(_databaseFile))
      File.Delete(_databaseFile);
  }

  [Test]
  public async Task PersistsNumberedUuidAndNoContainerIdentity()
  {
    var id = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();

    var noIdentity = repository.RecordGeneralMessage(
      mats: [],
      program: "NoIdentity",
      result: "",
      pallet: 0
    );
    var numbered = repository.RecordBasketContentSnapshot(
      mats: [],
      new ContainerIdentity.Numbered { ContainerNum = 7 },
      DateTime.UtcNow
    );
    var uuid = repository.RecordBasketContentSnapshot(
      mats: [],
      new ContainerIdentity.Uuid { ContainerId = id },
      DateTime.UtcNow
    );

    var loaded = repository.GetRecentLog(0).ToImmutableList();
    await Assert
      .That(loaded.Single(entry => entry.Counter == noIdentity.Counter).Identity)
      .IsTypeOf<ContainerIdentity.None>();
    await Assert
      .That(loaded.Single(entry => entry.Counter == numbered.Counter).Identity)
      .IsEqualTo(new ContainerIdentity.Numbered { ContainerNum = 7 });
    await Assert
      .That(loaded.Single(entry => entry.Counter == uuid.Counter).Identity)
      .IsEqualTo(new ContainerIdentity.Uuid { ContainerId = id });
  }

  [Test]
  public async Task CompleteSnapshotRoundTripsMaterialForNumberedAndUuidContainers()
  {
    var id = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 2);
    var material = new EventLogMaterial
    {
      MaterialID = materialId,
      Process = 1,
      Face = 3,
    };

    var numbered = repository.RecordBasketContentSnapshot(
      [material],
      new ContainerIdentity.Numbered { ContainerNum = 4 },
      DateTime.UtcNow
    );
    var uuid = repository.RecordBasketContentSnapshot(
      [material],
      new ContainerIdentity.Uuid { ContainerId = id },
      DateTime.UtcNow
    );

    foreach (var counter in new[] { numbered.Counter, uuid.Counter })
    {
      var snapshot = repository.GetRecentLog(0).Single(entry => entry.Counter == counter);
      await Assert.That(snapshot.LogType).IsEqualTo(LogType.BasketContentSnapshot);
      await Assert.That(snapshot.Material.Single().MaterialID).IsEqualTo(materialId);
      await Assert.That(snapshot.Material.Single().Face).IsEqualTo(3);
    }
  }

  [Test]
  public async Task BasketLoadUnloadCanUseUuidIdentity()
  {
    var id = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 1);
    var logs = repository
      .RecordTestBasketLoadUnload(
        new TestMaterialToLoadOntoBasket
        {
          MaterialIDs = [materialId],
          Process = 1,
          ActiveOperationTime = TimeSpan.FromMinutes(1),
        },
        toUnload: null,
        lulNum: 2,
        basketIdentity: new ContainerIdentity.Uuid { ContainerId = id },
        totalElapsed: TimeSpan.FromMinutes(2),
        timeUTC: DateTime.UtcNow,
        externalQueues: ImmutableDictionary<string, string>.Empty
      )
      .Single(entry => entry.LogType == LogType.BasketLoadUnload);

    await Assert.That(logs.Pallet).IsEqualTo(-1);
    await Assert.That(logs.ContainerId).IsEqualTo(id);
    await Assert.That(logs.Material.Single().MaterialID).IsEqualTo(materialId);
  }

  [Test]
  public async Task InvalidatedUuidBasketEventsAreNotCurrentOrOpen()
  {
    var time = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Utc);
    var id = Guid.NewGuid();
    var identity = new ContainerIdentity.Uuid { ContainerId = id };
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 1);
    QueueMaterial(repository, materialId, "raw", time);
    repository.RecordBasketStationOperation(
      LoadOntoBasketOperation(identity, materialId),
      lulNum: 2,
      totalElapsed: TimeSpan.FromMinutes(1),
      timeUTC: time.AddMinutes(1),
      externalQueues: ImmutableDictionary<string, string>.Empty,
      foreignId: "uuid-initial-load"
    );

    repository.InvalidatePalletCycle(materialId, process: 1, "operator", time.AddMinutes(2));

    await Assert.That(repository.CurrentBasketLog(identity)).IsEmpty();
    await Assert.That(repository.GetUnresolvedOpenBasketContainerIds()).DoesNotContain(id);
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketCycleEnd(
        basketId: 7,
        mats:
        [
          new EventLogMaterial
          {
            MaterialID = materialId,
            Process = 1,
            Face = 0,
          },
        ],
        containerIds: ImmutableHashSet.Create(id),
        timeUTC: time.AddMinutes(3),
        foreignId: "invalidated-uuid-finalization"
      )
    );
  }

  [Test]
  public async Task InvalidatedHintedUuidEventsDoNotLeakIntoNumberedBasket()
  {
    var time = new DateTime(2026, 7, 27, 10, 30, 0, DateTimeKind.Utc);
    var id = Guid.NewGuid();
    var uuidIdentity = new ContainerIdentity.Uuid { ContainerId = id };
    var numberedIdentity = new ContainerIdentity.Numbered { ContainerNum = 8 };
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 1);
    QueueMaterial(repository, materialId, "raw", time);
    repository.RecordBasketStationOperation(
      LoadOntoBasketOperation(uuidIdentity, materialId),
      lulNum: 2,
      totalElapsed: TimeSpan.FromMinutes(1),
      timeUTC: time.AddMinutes(1),
      externalQueues: ImmutableDictionary<string, string>.Empty,
      foreignId: "hinted-uuid-load"
    );
    repository.RecordBasketIdentityHint(id, 8, time.AddMinutes(2));

    repository.InvalidatePalletCycle(materialId, process: 1, "operator", time.AddMinutes(3));

    await Assert
      .That(repository.CurrentBasketLog(uuidIdentity).Select(log => log.LogType))
      .IsEquivalentTo([LogType.BasketIdentityHint]);
    await Assert
      .That(repository.CurrentBasketLog(numberedIdentity).Select(log => log.LogType))
      .IsEquivalentTo([LogType.BasketIdentityHint]);
  }

  [Test]
  public async Task ExplicitBasketEndRejectsInvalidatedStart()
  {
    var time = new DateTime(2026, 7, 27, 11, 0, 0, DateTimeKind.Utc);
    var identity = new ContainerIdentity.Numbered { ContainerNum = 5 };
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 1);
    QueueMaterial(repository, materialId, "raw", time);
    repository.RecordBasketStationOperation(
      LoadOntoBasketOperation(identity, materialId),
      lulNum: 2,
      totalElapsed: TimeSpan.FromMinutes(1),
      timeUTC: time.AddMinutes(1),
      externalQueues: ImmutableDictionary<string, string>.Empty,
      foreignId: "numbered-initial-load"
    );
    repository.InvalidatePalletCycle(materialId, process: 1, "operator", time.AddMinutes(2));

    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketStationOperation(
        new BasketStationOperation
        {
          Transfers = [],
          CycleBoundaries =
          [
            new BasketCycleBoundary.End
            {
              BasketIdentity = identity,
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = materialId,
                  Process = 1,
                  Face = 0,
                },
              ],
              ReconciledBasketIdentities = [],
            },
          ],
        },
        lulNum: 2,
        totalElapsed: TimeSpan.Zero,
        timeUTC: time.AddMinutes(3),
        externalQueues: ImmutableDictionary<string, string>.Empty,
        foreignId: "invalidated-numbered-end"
      )
    );
    await Assert.That(repository.CurrentBasketLog(5, includeLastCycleEvt: true)).IsEmpty();
  }

  [Test]
  public async Task InvalidatedBasketMaterialCanBeRescannedAndLoadedAsFreshProcess()
  {
    var time = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
    var identity = new ContainerIdentity.Numbered { ContainerNum = 6 };
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 1);
    QueueMaterial(repository, materialId, "raw", time);
    repository.RecordBasketStationOperation(
      LoadOntoBasketOperation(identity, materialId),
      lulNum: 2,
      totalElapsed: TimeSpan.FromMinutes(1),
      timeUTC: time.AddMinutes(1),
      externalQueues: ImmutableDictionary<string, string>.Empty,
      foreignId: "first-attempt"
    );
    repository.InvalidatePalletCycle(materialId, process: 1, "operator", time.AddMinutes(2));

    QueueMaterial(repository, materialId, "rework", time.AddMinutes(3), "operator", "Rescan");
    var freshLogs = repository
      .RecordBasketStationOperation(
        LoadOntoBasketOperation(identity, materialId),
        lulNum: 2,
        totalElapsed: TimeSpan.FromMinutes(2),
        timeUTC: time.AddMinutes(4),
        externalQueues: ImmutableDictionary<string, string>.Empty,
        foreignId: "second-attempt"
      )
      .Where(log => log.LogType is LogType.BasketLoadUnload or LogType.BasketCycle)
      .ToImmutableList();

    await Assert.That(freshLogs).Count().IsEqualTo(2);
    await Assert
      .That(
        repository
          .GetLogForMaterial(materialId, includeInvalidatedCycles: false)
          .Where(log => log.LogType is LogType.BasketLoadUnload or LogType.BasketCycle)
          .Select(log => log.Counter)
      )
      .IsEquivalentTo(freshLogs.Select(log => log.Counter));
    await Assert.That(repository.NextProcessForQueuedMaterial(materialId)).IsEqualTo(2);
    await Assert.That(repository.GetMaterialInAllQueues()).IsEmpty();
    await Assert
      .That(repository.CurrentBasketLog(identity, includeLastCycleEvt: true).Single().Counter)
      .IsEqualTo(freshLogs.Single(log => log.LogType == LogType.BasketCycle).Counter);
  }

  [Test]
  public async Task BasketStationOperationRecordsEachTransferOnceBeforeItsBoundary()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var unloadedMaterialId = repository.AllocateMaterialID("job", "part", 2);
    var loadedMaterialId = repository.AllocateMaterialID("job", "part", 2);
    repository.RecordAddMaterialToQueue(
      new EventLogMaterial
      {
        MaterialID = loadedMaterialId,
        Process = 1,
        Face = 0,
      },
      "incoming",
      -1,
      operatorName: null,
      reason: null,
      DateTime.UtcNow
    );
    var basketIdentity = new ContainerIdentity.Numbered { ContainerNum = 5 };
    var completion = new PalletBasketLoadUnloadCompletion
    {
      Transfers =
      [
        new PalletBasketTransfer.UnloadFromBasket
        {
          BasketIdentity = basketIdentity,
          Material =
          [
            new EventLogMaterial
            {
              MaterialID = unloadedMaterialId,
              Process = 1,
              Face = 0,
            },
          ],
        },
        new PalletBasketTransfer.LoadOntoBasket
        {
          BasketIdentity = basketIdentity,
          Material =
          [
            new EventLogMaterial
            {
              MaterialID = loadedMaterialId,
              Process = 2,
              Face = 0,
            },
          ],
        },
      ],
      CycleBoundaries =
      [
        new BasketCycleBoundary.End
        {
          BasketIdentity = basketIdentity,
          Material =
          [
            new EventLogMaterial
            {
              MaterialID = unloadedMaterialId,
              Process = 1,
              Face = 4,
            },
          ],
          ReconciledBasketIdentities = [],
        },
        new BasketCycleBoundary.Start
        {
          BasketIdentity = basketIdentity,
          Material =
          [
            new EventLogMaterial
            {
              MaterialID = loadedMaterialId,
              Process = 2,
              Face = 7,
            },
          ],
        },
      ],
    };
    var toLoad = new TestMaterialToLoadOntoBasket
    {
      MaterialIDs = [loadedMaterialId],
      Process = 2,
      ActiveOperationTime = TimeSpan.FromMinutes(1),
      ForeignID = "basket-station-load",
    };
    var toUnload = new TestMaterialToUnloadFromBasket
    {
      MaterialIDToQueue = ImmutableDictionary<long, string>.Empty.Add(
        unloadedMaterialId,
        "outgoing"
      ),
      Process = 1,
      ActiveOperationTime = TimeSpan.FromMinutes(1),
      ForeignID = "basket-station-unload",
    };

    var logs = repository
      .RecordTestBasketLoadUnload(
        toLoad,
        toUnload,
        lulNum: 4,
        basketIdentity,
        totalElapsed: TimeSpan.FromMinutes(2),
        timeUTC: DateTime.UtcNow,
        externalQueues: ImmutableDictionary<string, string>.Empty,
        palletBasketCompletion: completion
      )
      .ToImmutableList();
    var retry = repository
      .RecordTestBasketLoadUnload(
        toLoad,
        toUnload,
        lulNum: 4,
        basketIdentity,
        totalElapsed: TimeSpan.FromMinutes(2),
        timeUTC: DateTime.UtcNow.AddMinutes(1),
        externalQueues: ImmutableDictionary<string, string>.Empty,
        palletBasketCompletion: completion
      )
      .ToImmutableList();

    await Assert.That(logs).Count().IsEqualTo(4);
    await Assert
      .That(
        logs.Select(log => (log.LogType, log.Program, log.StartOfCycle))
          .SequenceEqual([
            (LogType.BasketLoadUnload, "UNLOAD", false),
            (LogType.BasketCycle, "", false),
            (LogType.BasketLoadUnload, "LOAD", false),
            (LogType.BasketCycle, "", true),
          ])
      )
      .IsTrue();
    await Assert
      .That(logs.Select(log => log.Identity))
      .IsEquivalentTo(Enumerable.Repeat<ContainerIdentity>(basketIdentity, 4));
    await Assert.That(logs.Count(log => log.LogType == LogType.BasketLoadUnload)).IsEqualTo(2);
    await Assert.That(logs[0].Material.Single().MaterialID).IsEqualTo(unloadedMaterialId);
    await Assert.That(logs[1].Material.Single().Face).IsEqualTo(4);
    await Assert.That(logs[2].Material.Single().MaterialID).IsEqualTo(loadedMaterialId);
    await Assert.That(logs[3].Material.Single().Face).IsEqualTo(7);
    await Assert
      .That(retry.Select(log => log.Counter))
      .IsEquivalentTo(logs.Select(log => log.Counter));
    var queued = repository.GetMaterialInAllQueues().ToImmutableList();
    await Assert
      .That(queued.Single(material => material.MaterialID == unloadedMaterialId).Queue)
      .IsEqualTo("outgoing");
    await Assert.That(queued).DoesNotContain(material => material.MaterialID == loadedMaterialId);
  }

  [Test]
  public async Task BasketStationTurnoverSupportsDifferentIdentitiesAndCompleteRetryFingerprint()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var unloadedMaterialId = repository.AllocateMaterialID("job", "part", 2);
    var loadedMaterialId = repository.AllocateMaterialID("job", "part", 2);
    var unloadIdentity = new ContainerIdentity.Uuid { ContainerId = Guid.NewGuid() };
    var loadIdentity = new ContainerIdentity.Uuid { ContainerId = Guid.NewGuid() };
    var time = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
    repository.RecordBasketContentSnapshot(
      [
        new EventLogMaterial
        {
          MaterialID = unloadedMaterialId,
          Process = 1,
          Face = 2,
        },
      ],
      unloadIdentity,
      time.AddMinutes(-10)
    );
    repository.RecordBasketContentSnapshot([], loadIdentity, time.AddMinutes(-1));
    repository.RecordAddMaterialToQueue(
      new EventLogMaterial
      {
        MaterialID = loadedMaterialId,
        Process = 1,
        Face = 0,
      },
      "incoming",
      -1,
      operatorName: null,
      reason: null,
      time.AddMinutes(-5)
    );

    BasketStationOperation Operation(
      string destinationQueue = "outgoing",
      TimeSpan? loadActive = null
    ) =>
      new()
      {
        Transfers =
        [
          new BasketStationTransfer.UnloadFromBasket
          {
            BasketIdentity = unloadIdentity,
            Material =
            [
              new EventLogMaterial
              {
                MaterialID = unloadedMaterialId,
                Process = 1,
                Face = 2,
              },
            ],
            ActiveOperationTime = TimeSpan.FromSeconds(20),
            DestinationQueue = destinationQueue,
          },
          new BasketStationTransfer.LoadOntoBasket
          {
            BasketIdentity = loadIdentity,
            Material =
            [
              new EventLogMaterial
              {
                MaterialID = loadedMaterialId,
                Process = 2,
                Face = 7,
              },
            ],
            ActiveOperationTime = loadActive ?? TimeSpan.FromSeconds(30),
          },
        ],
        CycleBoundaries =
        [
          new BasketCycleBoundary.End
          {
            BasketIdentity = new ContainerIdentity.Numbered { ContainerNum = 5 },
            Material =
            [
              new EventLogMaterial
              {
                MaterialID = unloadedMaterialId,
                Process = 1,
                Face = 2,
              },
            ],
            ReconciledBasketIdentities = [unloadIdentity.ContainerId],
          },
          new BasketCycleBoundary.Start
          {
            BasketIdentity = loadIdentity,
            Material =
            [
              new EventLogMaterial
              {
                MaterialID = loadedMaterialId,
                Process = 2,
                Face = 7,
              },
            ],
          },
        ],
      };

    var earlierSharedId = repository.RecordGeneralMessage(
      mats: [],
      program: "Earlier",
      result: "",
      foreignId: "different-identity-turnover"
    );
    var first = repository
      .RecordBasketStationOperation(
        Operation(),
        lulNum: 4,
        totalElapsed: TimeSpan.FromMinutes(2),
        time,
        ImmutableDictionary<string, string>.Empty,
        foreignId: "different-identity-turnover",
        originalMessage: "confirmed work"
      )
      .ToImmutableList();
    var laterSharedId = repository.RecordGeneralMessage(
      mats: [],
      program: "Later",
      result: "",
      foreignId: "different-identity-turnover"
    );
    var retry = repository
      .RecordBasketStationOperation(
        Operation(),
        lulNum: 4,
        totalElapsed: TimeSpan.FromMinutes(2),
        time.AddMinutes(1),
        ImmutableDictionary<string, string>.Empty,
        foreignId: "different-identity-turnover",
        originalMessage: "confirmed work"
      )
      .ToImmutableList();

    await Assert
      .That(first.Select(log => log.Identity))
      .IsEquivalentTo(
        new ContainerIdentity[]
        {
          unloadIdentity,
          new ContainerIdentity.Numbered { ContainerNum = 5 },
          loadIdentity,
          loadIdentity,
        }
      );
    await Assert.That(first.Select(log => log.EndTimeUTC).Distinct()).Count().IsEqualTo(1);
    var unload = first.Single(log => log.Program == "UNLOAD");
    var load = first.Single(log => log.Program == "LOAD");
    await Assert.That(unload.ActiveOperationTime).IsEqualTo(TimeSpan.FromSeconds(20));
    await Assert.That(unload.ElapsedTime).IsEqualTo(TimeSpan.FromSeconds(48));
    await Assert.That(load.ActiveOperationTime).IsEqualTo(TimeSpan.FromSeconds(30));
    await Assert.That(load.ElapsedTime).IsEqualTo(TimeSpan.FromSeconds(72));
    await Assert
      .That(retry.Select(log => log.Counter).SequenceEqual(first.Select(log => log.Counter)))
      .IsTrue();
    await Assert
      .That(retry.Select(log => log.Counter))
      .DoesNotContain(earlierSharedId.Counter)
      .And.DoesNotContain(laterSharedId.Counter);
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketStationOperation(
        Operation(destinationQueue: "changed"),
        4,
        TimeSpan.FromMinutes(2),
        time,
        ImmutableDictionary<string, string>.Empty,
        "different-identity-turnover",
        "confirmed work"
      )
    );
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketStationOperation(
        Operation(loadActive: TimeSpan.FromSeconds(31)),
        4,
        TimeSpan.FromMinutes(2),
        time,
        ImmutableDictionary<string, string>.Empty,
        "different-identity-turnover",
        "confirmed work"
      )
    );
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketStationOperation(
        Operation(),
        4,
        TimeSpan.FromMinutes(3),
        time,
        ImmutableDictionary<string, string>.Empty,
        "different-identity-turnover",
        "confirmed work"
      )
    );
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketStationOperation(
        Operation(),
        4,
        TimeSpan.FromMinutes(2),
        time,
        ImmutableDictionary<string, string>.Empty.Add("outgoing", "https://example.invalid"),
        "different-identity-turnover",
        "confirmed work"
      )
    );
  }

  [Test]
  public async Task BasketStationElapsedFallsBackToPerMaterialWeightsWhenActiveTimeIsMissing()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var firstUnload = repository.AllocateMaterialID("job", "part", 1);
    var secondUnload = repository.AllocateMaterialID("job", "part", 1);
    var loadMaterial = repository.AllocateMaterialID("job", "part", 1);

    var logs = repository
      .RecordBasketStationOperation(
        new BasketStationOperation
        {
          Transfers =
          [
            new BasketStationTransfer.UnloadFromBasket
            {
              BasketIdentity = new ContainerIdentity.Numbered { ContainerNum = 8 },
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = firstUnload,
                  Process = 1,
                  Face = 0,
                },
                new EventLogMaterial
                {
                  MaterialID = secondUnload,
                  Process = 1,
                  Face = 0,
                },
              ],
              ActiveOperationTime = TimeSpan.FromSeconds(20),
            },
            new BasketStationTransfer.LoadOntoBasket
            {
              BasketIdentity = new ContainerIdentity.Numbered { ContainerNum = 8 },
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = loadMaterial,
                  Process = 1,
                  Face = 0,
                },
              ],
              ActiveOperationTime = TimeSpan.Zero,
            },
          ],
          CycleBoundaries = [],
        },
        lulNum: 4,
        totalElapsed: TimeSpan.FromMinutes(2),
        DateTime.UtcNow,
        externalQueues: ImmutableDictionary<string, string>.Empty,
        foreignId: "missing-active-time"
      )
      .ToImmutableList();

    var unload = logs.Single(log => log.Program == "UNLOAD");
    var load = logs.Single(log => log.Program == "LOAD");
    await Assert.That(unload.ActiveOperationTime).IsEqualTo(TimeSpan.FromSeconds(20));
    await Assert.That(unload.ElapsedTime).IsEqualTo(TimeSpan.FromSeconds(80));
    await Assert.That(load.ActiveOperationTime).IsEqualTo(TimeSpan.Zero);
    await Assert.That(load.ElapsedTime).IsEqualTo(TimeSpan.FromSeconds(40));
  }

  [Test]
  public async Task BasketStationOperationRollsBackAndRetryIsIdempotent()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 1);
    repository.RecordAddMaterialToQueue(
      new EventLogMaterial
      {
        MaterialID = materialId,
        Process = 0,
        Face = 0,
      },
      "incoming",
      -1,
      operatorName: null,
      reason: null,
      DateTime.UtcNow
    );
    var basketIdentity = new ContainerIdentity.Uuid { ContainerId = Guid.NewGuid() };
    var toLoad = new TestMaterialToLoadOntoBasket
    {
      MaterialIDs = [materialId],
      Process = 1,
      ActiveOperationTime = TimeSpan.Zero,
      ForeignID = "retry-basket-station-load",
      OriginalMessage = "robot completion",
    };
    var completion = new PalletBasketLoadUnloadCompletion
    {
      Transfers =
      [
        new PalletBasketTransfer.LoadOntoBasket
        {
          BasketIdentity = basketIdentity,
          Material =
          [
            new EventLogMaterial
            {
              MaterialID = materialId,
              Process = 1,
              Face = 0,
            },
          ],
        },
      ],
      CycleBoundaries =
      [
        new BasketCycleBoundary.Start
        {
          BasketIdentity = basketIdentity,
          Material =
          [
            new EventLogMaterial
            {
              MaterialID = materialId,
              Process = 1,
              Face = 6,
            },
          ],
        },
      ],
    };
    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var trigger = connection.CreateCommand();
      trigger.CommandText =
        "CREATE TRIGGER fail_basket_station_cycle BEFORE INSERT ON stations WHEN NEW.StationLoc = "
        + (int)LogType.BasketCycle
        + " BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      trigger.ExecuteNonQuery();
    }

    await AssertThrows<SqliteException>(() =>
      repository.RecordTestBasketLoadUnload(
        toLoad,
        toUnload: null,
        lulNum: 2,
        basketIdentity,
        TimeSpan.Zero,
        DateTime.UtcNow,
        ImmutableDictionary<string, string>.Empty,
        completion
      )
    );
    await Assert
      .That(repository.GetMaterialInAllQueues().Single().MaterialID)
      .IsEqualTo(materialId);
    await Assert
      .That(repository.GetRecentLog(0))
      .DoesNotContain(log => log.ContainerId == basketIdentity.ContainerId);

    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var dropTrigger = connection.CreateCommand();
      dropTrigger.CommandText = "DROP TRIGGER fail_basket_station_cycle";
      dropTrigger.ExecuteNonQuery();
    }
    var first = repository
      .RecordTestBasketLoadUnload(
        toLoad,
        toUnload: null,
        lulNum: 2,
        basketIdentity,
        TimeSpan.Zero,
        DateTime.UtcNow,
        ImmutableDictionary<string, string>.Empty,
        completion
      )
      .ToImmutableList();
    var retry = repository
      .RecordTestBasketLoadUnload(
        toLoad,
        toUnload: null,
        lulNum: 2,
        basketIdentity,
        TimeSpan.Zero,
        DateTime.UtcNow.AddMinutes(1),
        ImmutableDictionary<string, string>.Empty,
        completion
      )
      .ToImmutableList();

    await Assert.That(first).Count().IsEqualTo(2);
    await Assert
      .That(retry.Select(log => log.Counter))
      .IsEquivalentTo(first.Select(log => log.Counter));
    await Assert.That(repository.GetMaterialInAllQueues()).IsEmpty();
    await Assert
      .That(
        repository
          .GetRecentLog(0)
          .Count(log =>
            log.ContainerId == basketIdentity.ContainerId && log.LogType == LogType.BasketLoadUnload
          )
      )
      .IsEqualTo(1);
  }

  [Test]
  public async Task MultiProcessBasketStationOperationIsOrderedAtomicAndIdempotent()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var unloadProcessOne = repository.AllocateMaterialID("job", "part", 3);
    var unloadProcessTwo = repository.AllocateMaterialID("job", "part", 3);
    var loadProcessTwo = repository.AllocateMaterialID("job", "part", 3);
    var loadProcessThree = repository.AllocateMaterialID("job", "part", 3);
    foreach (var (materialId, process) in new[] { (loadProcessTwo, 1), (loadProcessThree, 2) })
      repository.RecordAddMaterialToQueue(
        new EventLogMaterial
        {
          MaterialID = materialId,
          Process = process,
          Face = 0,
        },
        "incoming",
        -1,
        operatorName: null,
        reason: null,
        DateTime.UtcNow
      );

    var basketIdentity = new ContainerIdentity.Numbered { ContainerNum = 8 };
    ImmutableList<EventLogMaterial> Material(long materialId, int process, int face) =>
      [
        new EventLogMaterial
        {
          MaterialID = materialId,
          Process = process,
          Face = face,
        },
      ];
    var toUnload = new[]
    {
      new TestMaterialToUnloadFromBasket
      {
        MaterialIDToQueue = ImmutableDictionary<long, string>.Empty.Add(
          unloadProcessOne,
          "outgoing-one"
        ),
        Process = 1,
        ActiveOperationTime = TimeSpan.FromSeconds(10),
        ForeignID = "multi-unload-one",
      },
      new TestMaterialToUnloadFromBasket
      {
        MaterialIDToQueue = ImmutableDictionary<long, string>.Empty.Add(
          unloadProcessTwo,
          "outgoing-two"
        ),
        Process = 2,
        ActiveOperationTime = TimeSpan.FromSeconds(20),
        ForeignID = "multi-unload-two",
      },
    };
    var toLoad = new[]
    {
      new TestMaterialToLoadOntoBasket
      {
        MaterialIDs = [loadProcessTwo],
        Process = 2,
        ActiveOperationTime = TimeSpan.FromSeconds(30),
        ForeignID = "multi-load-two",
      },
      new TestMaterialToLoadOntoBasket
      {
        MaterialIDs = [loadProcessThree],
        Process = 3,
        ActiveOperationTime = TimeSpan.FromSeconds(40),
        ForeignID = "multi-load-three",
      },
    };
    var completion = new PalletBasketLoadUnloadCompletion
    {
      Transfers =
      [
        new PalletBasketTransfer.UnloadFromBasket
        {
          BasketIdentity = basketIdentity,
          Material = Material(unloadProcessOne, 1, 0),
        },
        new PalletBasketTransfer.UnloadFromBasket
        {
          BasketIdentity = basketIdentity,
          Material = Material(unloadProcessTwo, 2, 0),
        },
        new PalletBasketTransfer.LoadOntoBasket
        {
          BasketIdentity = basketIdentity,
          Material = Material(loadProcessTwo, 2, 0),
        },
        new PalletBasketTransfer.LoadOntoBasket
        {
          BasketIdentity = basketIdentity,
          Material = Material(loadProcessThree, 3, 0),
        },
      ],
      CycleBoundaries =
      [
        new BasketCycleBoundary.End
        {
          BasketIdentity = basketIdentity,
          Material = [.. Material(unloadProcessOne, 1, 3), .. Material(unloadProcessTwo, 2, 4)],
          ReconciledBasketIdentities = [],
        },
        new BasketCycleBoundary.Start
        {
          BasketIdentity = basketIdentity,
          Material = [.. Material(loadProcessTwo, 2, 5), .. Material(loadProcessThree, 3, 6)],
        },
      ],
    };
    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var trigger = connection.CreateCommand();
      trigger.CommandText =
        "CREATE TRIGGER fail_multi_basket_station BEFORE INSERT ON stations WHEN NEW.StationLoc = "
        + (int)LogType.BasketCycle
        + " BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      trigger.ExecuteNonQuery();
    }

    await AssertThrows<SqliteException>(() =>
      repository.RecordTestBasketLoadUnload(
        toLoad,
        toUnload,
        lulNum: 3,
        basketIdentity,
        TimeSpan.FromMinutes(2),
        DateTime.UtcNow,
        ImmutableDictionary<string, string>.Empty,
        completion
      )
    );
    await Assert
      .That(repository.GetMaterialInAllQueues().Select(material => material.MaterialID))
      .IsEquivalentTo([loadProcessTwo, loadProcessThree]);
    await Assert
      .That(repository.GetRecentLog(0))
      .DoesNotContain(log => log.Identity == basketIdentity);

    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var dropTrigger = connection.CreateCommand();
      dropTrigger.CommandText = "DROP TRIGGER fail_multi_basket_station";
      dropTrigger.ExecuteNonQuery();
    }
    var first = repository
      .RecordTestBasketLoadUnload(
        toLoad,
        toUnload,
        lulNum: 3,
        basketIdentity,
        TimeSpan.FromMinutes(2),
        DateTime.UtcNow,
        ImmutableDictionary<string, string>.Empty,
        completion
      )
      .ToImmutableList();
    var retry = repository
      .RecordTestBasketLoadUnload(
        toLoad,
        toUnload,
        lulNum: 3,
        basketIdentity,
        TimeSpan.FromMinutes(2),
        DateTime.UtcNow.AddMinutes(1),
        ImmutableDictionary<string, string>.Empty,
        completion
      )
      .ToImmutableList();

    await Assert
      .That(
        first
          .Select(log => (log.LogType, log.Program, log.StartOfCycle))
          .SequenceEqual([
            (LogType.BasketLoadUnload, "UNLOAD", false),
            (LogType.BasketLoadUnload, "UNLOAD", false),
            (LogType.BasketCycle, "", false),
            (LogType.BasketLoadUnload, "LOAD", false),
            (LogType.BasketLoadUnload, "LOAD", false),
            (LogType.BasketCycle, "", true),
          ])
      )
      .IsTrue();
    await Assert
      .That(
        new[] { first[0], first[1], first[3], first[4] }.Select(log =>
          log.Material.Single().Process
        )
      )
      .IsEquivalentTo([1, 2, 2, 3]);
    await Assert.That(first[2].Material.Select(material => material.Face)).IsEquivalentTo([3, 4]);
    await Assert.That(first[5].Material.Select(material => material.Face)).IsEquivalentTo([5, 6]);
    await Assert
      .That(retry.Select(log => log.Counter).SequenceEqual(first.Select(log => log.Counter)))
      .IsTrue();
    await Assert
      .That(repository.GetMaterialInAllQueues().Select(material => material.MaterialID))
      .IsEquivalentTo([unloadProcessOne, unloadProcessTwo]);
    await Assert
      .That(repository.GetRecentLog(0).Count(log => log.Identity == basketIdentity))
      .IsEqualTo(6);
  }

  [Test]
  public async Task UuidBasketLoadCompletionRecordsLoadAndCompleteCycleStart()
  {
    var basketId = Guid.Parse("418a9c3e-dd45-4b8f-a2f8-064772599927");
    var completionTime = new DateTime(2026, 7, 24, 14, 30, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    var existingMaterialId = repository.AllocateMaterialID("job", "part", 2);
    var loadedMaterialId = repository.AllocateMaterialID("job", "part", 2);
    var completeContents = ImmutableList.Create(
      new EventLogMaterial
      {
        MaterialID = existingMaterialId,
        Process = 2,
        Face = 3,
      },
      new EventLogMaterial
      {
        MaterialID = loadedMaterialId,
        Process = 2,
        Face = 7,
      }
    );

    var completion = new PalletBasketLoadUnloadCompletion
    {
      Transfers =
      [
        new PalletBasketTransfer.LoadOntoBasket
        {
          BasketIdentity = new ContainerIdentity.Uuid { ContainerId = basketId },
          Material = [completeContents[1]],
        },
      ],
      CycleBoundaries =
      [
        new BasketCycleBoundary.Start
        {
          BasketIdentity = new ContainerIdentity.Uuid { ContainerId = basketId },
          Material = completeContents,
        },
      ],
    };
    var logs = repository
      .RecordTestPalletBasketCompletion(
        completion,
        lulNum: 4,
        timeUTC: completionTime,
        foreignId: "uuid-load-complete",
        originalMessage: "robot evidence"
      )
      .ToImmutableList();

    await Assert.That(logs).Count().IsEqualTo(2);
    var loadEnd = logs[0];
    await Assert.That(loadEnd.LogType).IsEqualTo(LogType.BasketLoadUnload);
    await Assert.That(loadEnd.Program).IsEqualTo("LOAD");
    await Assert.That(loadEnd.StartOfCycle).IsFalse();
    await Assert
      .That(loadEnd.Identity)
      .IsEqualTo(new ContainerIdentity.Uuid { ContainerId = basketId });
    await Assert.That(loadEnd.Material.Single().MaterialID).IsEqualTo(loadedMaterialId);
    await Assert.That(loadEnd.Material.Single().Process).IsEqualTo(2);
    await Assert.That(loadEnd.Material.Single().Face).IsEqualTo(7);

    var cycleStart = logs[1];
    await Assert.That(cycleStart.LogType).IsEqualTo(LogType.BasketCycle);
    await Assert.That(cycleStart.StartOfCycle).IsTrue();
    await Assert
      .That(cycleStart.Identity)
      .IsEqualTo(new ContainerIdentity.Uuid { ContainerId = basketId });
    await Assert
      .That(
        cycleStart.Material.Select(material =>
          (material.MaterialID, material.Process, material.Face)
        )
      )
      .IsEquivalentTo(new[] { (existingMaterialId, 2, 3), (loadedMaterialId, 2, 7) });
    var retry = repository
      .RecordTestPalletBasketCompletion(
        completion,
        lulNum: 4,
        timeUTC: completionTime.AddMinutes(1),
        foreignId: "uuid-load-complete",
        originalMessage: "robot evidence"
      )
      .ToImmutableList();
    await Assert
      .That(retry.Select(log => log.Counter))
      .IsEquivalentTo(logs.Select(log => log.Counter));
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordTestPalletBasketCompletion(
        completion,
        lulNum: 4,
        timeUTC: completionTime,
        foreignId: "uuid-load-complete",
        originalMessage: "different evidence"
      )
    );
    await Assert
      .That(repository.CurrentBasketLog(new ContainerIdentity.Uuid { ContainerId = basketId }))
      .Count()
      .IsEqualTo(2);
    await Assert.That(repository.GetUnresolvedOpenBasketContainerIds()).IsEquivalentTo([basketId]);

    await Verifier
      .Verify(
        logs.Select(log => new
        {
          Type = log.LogType.ToString(),
          log.Program,
          log.StartOfCycle,
          log.Pallet,
          log.ContainerId,
          Material = log.Material.Select(material => new
          {
            material.MaterialID,
            material.Process,
            Slot = material.Face,
          }),
        })
      )
      .UseDirectory("snapshots");
  }

  [Test]
  public async Task UuidBasketLoadCompletionIsAtomic()
  {
    var basketId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 1);
    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var trigger = connection.CreateCommand();
      trigger.CommandText =
        "CREATE TRIGGER fail_uuid_load BEFORE INSERT ON stations WHEN NEW.StationLoc = "
        + (int)LogType.BasketCycle
        + " BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      trigger.ExecuteNonQuery();
    }

    await AssertThrows<SqliteException>(() =>
      repository.RecordTestPalletBasketCompletion(
        new PalletBasketLoadUnloadCompletion
        {
          Transfers =
          [
            new PalletBasketTransfer.LoadOntoBasket
            {
              BasketIdentity = new ContainerIdentity.Uuid { ContainerId = basketId },
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = materialId,
                  Process = 1,
                  Face = 5,
                },
              ],
            },
          ],
          CycleBoundaries =
          [
            new BasketCycleBoundary.Start
            {
              BasketIdentity = new ContainerIdentity.Uuid { ContainerId = basketId },
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = materialId,
                  Process = 1,
                  Face = 5,
                },
              ],
            },
          ],
        },
        lulNum: 1,
        DateTime.UtcNow,
        foreignId: "atomic-uuid-load"
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
    await Assert.That(repository.GetUnresolvedOpenBasketContainerIds()).IsEmpty();
  }

  [Test]
  public async Task ExplicitCycleEndFinalizesSeveralUuidTransferFragments()
  {
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var firstMaterial = repository.AllocateMaterialID("job", "part", 1);
    var secondMaterial = repository.AllocateMaterialID("job", "part", 1);
    var firstContents = ImmutableList.Create(
      new EventLogMaterial
      {
        MaterialID = firstMaterial,
        Process = 1,
        Face = 2,
      }
    );
    var secondContents = ImmutableList.Create(
      new EventLogMaterial
      {
        MaterialID = secondMaterial,
        Process = 1,
        Face = 8,
      }
    );
    var firstEvidenceTime = new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc);
    repository.RecordBasketContentSnapshot(
      firstContents,
      new ContainerIdentity.Uuid { ContainerId = first },
      firstEvidenceTime,
      foreignId: "first-fragment"
    );
    repository.RecordBasketContentSnapshot(
      secondContents,
      new ContainerIdentity.Uuid { ContainerId = second },
      firstEvidenceTime.AddMinutes(1),
      foreignId: "second-fragment"
    );
    var completion = new PalletBasketLoadUnloadCompletion
    {
      Transfers =
      [
        new PalletBasketTransfer.UnloadFromBasket
        {
          BasketIdentity = new ContainerIdentity.Uuid { ContainerId = first },
          Material = firstContents,
        },
        new PalletBasketTransfer.UnloadFromBasket
        {
          BasketIdentity = new ContainerIdentity.Uuid { ContainerId = second },
          Material = secondContents,
        },
      ],
      CycleBoundaries =
      [
        new BasketCycleBoundary.End
        {
          BasketIdentity = new ContainerIdentity.Numbered { ContainerNum = 9 },
          Material = [.. firstContents, .. secondContents],
          ReconciledBasketIdentities = [first, second],
        },
      ],
    };

    var logs = repository
      .RecordTestPalletBasketCompletion(
        completion,
        lulNum: 3,
        timeUTC: firstEvidenceTime.AddMinutes(10),
        foreignId: "finalize-basket-9"
      )
      .ToImmutableList();
    var retry = repository
      .RecordTestPalletBasketCompletion(
        completion,
        lulNum: 3,
        timeUTC: firstEvidenceTime.AddMinutes(11),
        foreignId: "finalize-basket-9"
      )
      .ToImmutableList();

    await Assert.That(logs.Select(log => log.Program)).IsEquivalentTo(["UNLOAD", "UNLOAD", ""]);
    var cycleEnd = logs[2];
    await Assert
      .That(cycleEnd.Identity)
      .IsEqualTo(new ContainerIdentity.Numbered { ContainerNum = 9 });
    await Assert.That(cycleEnd.CycleEndContainerIds).IsEquivalentTo([first, second]);
    await Assert.That(cycleEnd.Material.Select(material => material.Face)).IsEquivalentTo([2, 8]);
    await Assert.That(cycleEnd.ElapsedTime).IsEqualTo(TimeSpan.FromMinutes(10));
    await Assert
      .That(retry.Select(log => log.Counter))
      .IsEquivalentTo(logs.Select(log => log.Counter));
    await Assert.That(repository.GetUnresolvedOpenBasketContainerIds()).IsEmpty();
  }

  [Test]
  public async Task ExplicitCycleEndRejectsAnAlreadyFinalizedUuidFragment()
  {
    var fragment = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketContentSnapshot(
      [],
      new ContainerIdentity.Uuid { ContainerId = fragment },
      DateTime.UtcNow
    );
    var completion = new PalletBasketLoadUnloadCompletion
    {
      Transfers = [],
      CycleBoundaries =
      [
        new BasketCycleBoundary.End
        {
          BasketIdentity = new ContainerIdentity.Numbered { ContainerNum = 9 },
          Material = [],
          ReconciledBasketIdentities = [fragment],
        },
      ],
    };

    repository.RecordTestPalletBasketCompletion(
      completion,
      lulNum: 3,
      timeUTC: DateTime.UtcNow,
      foreignId: "first-finalization"
    );

    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordTestPalletBasketCompletion(
        completion,
        lulNum: 3,
        timeUTC: DateTime.UtcNow,
        foreignId: "duplicate-finalization"
      )
    );
  }

  [Test]
  public async Task ExplicitCycleEndRejectsMaterialMissingFromFinalizedFragment()
  {
    var fragment = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var transferredMaterial = repository.AllocateMaterialID("job", "part", 1);
    var unrelatedMaterial = repository.AllocateMaterialID("job", "part", 1);

    await AssertThrows<ArgumentException>(() =>
      repository.RecordTestPalletBasketCompletion(
        new PalletBasketLoadUnloadCompletion
        {
          Transfers =
          [
            new PalletBasketTransfer.UnloadFromBasket
            {
              BasketIdentity = new ContainerIdentity.Uuid { ContainerId = fragment },
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = transferredMaterial,
                  Process = 1,
                  Face = 2,
                },
              ],
            },
          ],
          CycleBoundaries =
          [
            new BasketCycleBoundary.End
            {
              BasketIdentity = new ContainerIdentity.Numbered { ContainerNum = 9 },
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = unrelatedMaterial,
                  Process = 1,
                  Face = 2,
                },
              ],
              ReconciledBasketIdentities = [fragment],
            },
          ],
        },
        lulNum: 3,
        timeUTC: DateTime.UtcNow,
        foreignId: "invalid-fragment-finalization"
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
  }

  [Test]
  public async Task PartialPalletUnloadAcceptsExplicitUuidPalletBasketTransferWithoutStartingCycle()
  {
    var basketId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 2);

    var logs = repository
      .RecordPartialLoadUnload(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              materialId,
              new UnloadDestination()
            ),
            FaceNum = 1,
            Process = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        lulNum: 2,
        pallet: 4,
        totalElapsed: TimeSpan.Zero,
        timeUTC: DateTime.UtcNow,
        externalQueues: ImmutableDictionary<string, string>.Empty,
        palletBasketCompletion: new PalletBasketLoadUnloadCompletion
        {
          Transfers =
          [
            new PalletBasketTransfer.LoadOntoBasket
            {
              BasketIdentity = new ContainerIdentity.Uuid { ContainerId = basketId },
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = materialId,
                  Process = 1,
                  Face = 6,
                },
              ],
            },
          ],
          CycleBoundaries = [],
        }
      )
      .ToImmutableList();

    var basketLoad = logs.Single(log => log.LogType == LogType.BasketLoadUnload);
    await Assert
      .That(basketLoad.Identity)
      .IsEqualTo(new ContainerIdentity.Uuid { ContainerId = basketId });
    await Assert.That(basketLoad.Material.Single().Process).IsEqualTo(1);
    await Assert.That(basketLoad.Material.Single().Face).IsEqualTo(6);
    await Assert.That(logs).DoesNotContain(log => log.LogType == LogType.BasketCycle);
  }

  [Test]
  public async Task PartialPalletUnloadRejectsPalletBasketTransferForDifferentProcess()
  {
    var basketId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 2);

    await AssertThrows<ArgumentException>(() =>
      repository.RecordPartialLoadUnload(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              materialId,
              new UnloadDestination()
            ),
            FaceNum = 1,
            Process = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        lulNum: 2,
        pallet: 4,
        totalElapsed: TimeSpan.Zero,
        timeUTC: DateTime.UtcNow,
        externalQueues: ImmutableDictionary<string, string>.Empty,
        palletBasketCompletion: new PalletBasketLoadUnloadCompletion
        {
          Transfers =
          [
            new PalletBasketTransfer.LoadOntoBasket
            {
              BasketIdentity = new ContainerIdentity.Uuid { ContainerId = basketId },
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = materialId,
                  Process = 2,
                  Face = 6,
                },
              ],
            },
          ],
          CycleBoundaries = [],
        }
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
  }

  [Test]
  public async Task BasketCycleBoundaryRequiresMatchingTransferProcess()
  {
    var basketId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 2);
    var identity = new ContainerIdentity.Uuid { ContainerId = basketId };

    await AssertThrows<ArgumentException>(() =>
      repository.RecordTestPalletBasketCompletion(
        new PalletBasketLoadUnloadCompletion
        {
          Transfers =
          [
            new PalletBasketTransfer.LoadOntoBasket
            {
              BasketIdentity = identity,
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = materialId,
                  Process = 1,
                  Face = 1,
                },
              ],
            },
          ],
          CycleBoundaries =
          [
            new BasketCycleBoundary.Start
            {
              BasketIdentity = identity,
              Material =
              [
                new EventLogMaterial
                {
                  MaterialID = materialId,
                  Process = 2,
                  Face = 1,
                },
              ],
            },
          ],
        },
        lulNum: 2,
        timeUTC: DateTime.UtcNow,
        foreignId: "process-boundary-mismatch"
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
  }

  [Test]
  public async Task PalletUnloadWithoutQueueRequiresMatchingPalletBasketTransfer()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 1);

    await AssertThrows<ArgumentException>(() =>
      repository.RecordPartialLoadUnload(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              materialId,
              new UnloadDestination()
            ),
            FaceNum = 1,
            Process = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        lulNum: 2,
        pallet: 4,
        totalElapsed: TimeSpan.Zero,
        timeUTC: DateTime.UtcNow,
        externalQueues: ImmutableDictionary<string, string>.Empty
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
  }

  [Test]
  public async Task RejectsInvalidOrdinaryIdentityShapes()
  {
    using var repository = _repositoryConfig.OpenConnection();
    await AssertThrows<ArgumentException>(() =>
      repository.RecordBasketContentSnapshot([], new ContainerIdentity.None(), DateTime.UtcNow)
    );
    await AssertThrows<ArgumentException>(() =>
      repository.RecordBasketArriveLocation(
        [],
        new ContainerIdentity.Uuid { ContainerId = Guid.Empty },
        "Staging",
        1,
        DateTime.UtcNow
      )
    );
    await AssertThrows<ArgumentException>(() =>
      repository.RecordBasketIdentityHint(Guid.Empty, 1, DateTime.UtcNow)
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
    await Assert
      .That(
        new LogEntry(
          -1,
          [],
          -1,
          LogType.GeneralMessage,
          "Legacy",
          1,
          "",
          false,
          DateTime.UtcNow,
          ""
        ).Identity
      )
      .IsTypeOf<ContainerIdentity.None>();
  }

  [Test]
  public async Task BasketHintRequiresAnOpenBasketFragment()
  {
    using var repository = _repositoryConfig.OpenConnection();
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketIdentityHint(Guid.NewGuid(), 4, DateTime.UtcNow)
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
    await Assert.That(repository.GetCurrentBasketIdentityHints()).IsEmpty();
  }

  [Test]
  public async Task UnresolvedBasketFragmentsExcludeHintedAndFinalizedIds()
  {
    var id = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketContentSnapshot(
      [],
      new ContainerIdentity.Uuid { ContainerId = id },
      DateTime.UtcNow
    );

    await Assert.That(repository.GetUnresolvedOpenBasketContainerIds()).IsEquivalentTo([id]);
    repository.RecordBasketIdentityHint(id, 4, DateTime.UtcNow);
    await Assert.That(repository.GetUnresolvedOpenBasketContainerIds()).IsEmpty();
    repository.RecordBasketCycleEnd(4, [], ImmutableHashSet.Create(id), DateTime.UtcNow);
    await Assert.That(repository.GetUnresolvedOpenBasketContainerIds()).IsEmpty();
  }

  [Test]
  public async Task BackdatedBasketHintDoesNotChangeFinalizedCycleElapsedTime()
  {
    var id = Guid.NewGuid();
    var fragmentStart = new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketContentSnapshot(
      [],
      new ContainerIdentity.Uuid { ContainerId = id },
      fragmentStart
    );
    repository.RecordBasketIdentityHint(id, 4, fragmentStart.AddDays(-1));

    var cycle = repository.RecordBasketCycleEnd(
      4,
      [],
      ImmutableHashSet.Create(id),
      fragmentStart.AddHours(2)
    );

    await Assert.That(cycle.ElapsedTime).IsEqualTo(TimeSpan.FromHours(2));
  }

  [Test]
  public async Task HintCorrectionUsesCounterAndSurvivesRestart()
  {
    var id = Guid.NewGuid();
    var laterTime = DateTime.UtcNow;
    long correctionCounter;
    using (var repository = _repositoryConfig.OpenConnection())
    {
      repository.RecordBasketContentSnapshot(
        [],
        new ContainerIdentity.Uuid { ContainerId = id },
        laterTime
      );
      var first = repository.RecordBasketIdentityHint(id, 4, laterTime);
      var correction = repository.RecordBasketIdentityHint(id, 6, laterTime.AddDays(-1));
      correctionCounter = correction.Counter;

      var current = repository.GetCurrentBasketIdentityHints().Single();
      await Assert.That(current.BasketNum).IsEqualTo(6);
      await Assert.That(current.HintEventCounter).IsEqualTo(correction.Counter);
      await Assert.That(correction.Counter).IsGreaterThan(first.Counter);
      await Assert.That(repository.ReconstructBasketIdentityHints()[id]).IsEqualTo(current);
      await Assert
        .That(repository.CurrentBasketLog(new ContainerIdentity.Numbered { ContainerNum = 4 }))
        .IsEmpty();
      var assembled = repository.CurrentBasketLog(
        new ContainerIdentity.Numbered { ContainerNum = 6 }
      );
      await Assert.That(assembled).Count().IsEqualTo(3);
      await Assert.That(assembled.Select(entry => entry.Counter).Distinct()).Count().IsEqualTo(3);
    }

    _repositoryConfig.Dispose();
    _repositoryConfig = RepositoryConfig.InitializeEventDatabase(
      null,
      _databaseFile,
      pooling: false
    );
    using var restarted = _repositoryConfig.OpenConnection();
    var restartedHint = restarted.GetCurrentBasketIdentityHints().Single();
    await Assert.That(restartedHint.BasketNum).IsEqualTo(6);
    await Assert.That(restartedHint.HintEventCounter).IsEqualTo(correctionCounter);
  }

  [Test]
  public async Task SeveralOpenFragmentsCanHintToOneNumber()
  {
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    foreach (var id in new[] { first, second })
    {
      repository.RecordBasketContentSnapshot(
        [],
        new ContainerIdentity.Uuid { ContainerId = id },
        DateTime.UtcNow
      );
      repository.RecordBasketIdentityHint(id, 5, DateTime.UtcNow);
    }

    await Assert.That(repository.GetCurrentBasketIdentityHints(5)).Count().IsEqualTo(2);
    await Assert
      .That(
        repository
          .CurrentBasketLog(new ContainerIdentity.Numbered { ContainerNum = 5 })
          .Where(entry => entry.LogType == LogType.BasketContentSnapshot)
          .Select(entry => entry.ContainerId)
      )
      .IsEquivalentTo(new Guid?[] { first, second });
  }

  [Test]
  public async Task HintAndCacheUpdateRollBackTogether()
  {
    var id = Guid.NewGuid();
    using (var setupRepository = _repositoryConfig.OpenConnection())
    {
      setupRepository.RecordBasketContentSnapshot(
        [],
        new ContainerIdentity.Uuid { ContainerId = id },
        DateTime.UtcNow
      );
    }
    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText =
        "CREATE TRIGGER fail_hint_cache BEFORE INSERT ON current_basket_identity_hints BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      command.ExecuteNonQuery();
    }

    using var repository = _repositoryConfig.OpenConnection();
    await AssertThrows<SqliteException>(() =>
      repository.RecordBasketIdentityHint(id, 4, DateTime.UtcNow)
    );
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(1);
    await Assert.That(repository.GetCurrentBasketIdentityHints()).IsEmpty();
  }

  [Test]
  public async Task FinalizationClosesSeveralFragmentsAtomically()
  {
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    foreach (var id in new[] { first, second })
    {
      repository.RecordBasketContentSnapshot(
        [],
        new ContainerIdentity.Uuid { ContainerId = id },
        DateTime.UtcNow
      );
      repository.RecordBasketIdentityHint(id, 5, DateTime.UtcNow);
    }

    var cycle = repository.RecordBasketCycleEnd(
      basketId: 5,
      mats: [],
      containerIds: ImmutableHashSet.Create(first, second),
      timeUTC: DateTime.UtcNow,
      foreignId: "cycle-5-1"
    );
    var retry = repository.RecordBasketCycleEnd(
      basketId: 5,
      mats: [],
      containerIds: ImmutableHashSet.Create(second, first),
      timeUTC: DateTime.UtcNow.AddMinutes(1),
      foreignId: "cycle-5-1"
    );

    await Assert.That(cycle.Pallet).IsEqualTo(5);
    await Assert.That(retry.Counter).IsEqualTo(cycle.Counter);
    await Assert.That(cycle.CycleEndContainerIds).IsEquivalentTo([first, second]);
    await Assert.That(repository.GetCurrentBasketIdentityHints()).IsEmpty();
    await Assert.That(repository.GetUnresolvedOpenBasketContainerIds()).IsEmpty();
    await Assert.That(repository.ReconstructBasketIdentityHints()).IsEmpty();
    var loadedCycle = repository.GetFinalizedBasketCycle(cycle.Counter);
    await Assert.That(loadedCycle.Counter).IsEqualTo(cycle.Counter);
    await Assert.That(loadedCycle.CycleEndContainerIds).IsEquivalentTo([first, second]);
    await Assert
      .That(repository.GetFinalizedBasketCycles(5).Select(entry => entry.Counter))
      .IsEquivalentTo([cycle.Counter]);
    await Assert
      .That(repository.CurrentBasketLog(new ContainerIdentity.Uuid { ContainerId = first }))
      .IsEmpty();
  }

  [Test]
  public async Task FinalizationRetryIsIdempotentAndConflictingReuseIsRejected()
  {
    var id = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketContentSnapshot(
      [],
      new ContainerIdentity.Uuid { ContainerId = id },
      DateTime.UtcNow
    );
    var first = repository.RecordBasketCycleEnd(
      2,
      [],
      ImmutableHashSet.Create(id),
      DateTime.UtcNow,
      foreignId: "finalize-2"
    );
    var retry = repository.RecordBasketCycleEnd(
      2,
      [],
      ImmutableHashSet.Create(id),
      DateTime.UtcNow.AddMinutes(1),
      foreignId: "finalize-2"
    );

    await Assert.That(retry.Counter).IsEqualTo(first.Counter);
    await Assert.That(repository.GetRecentLog(0).Count()).IsEqualTo(2);
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketContentSnapshot(
        [],
        new ContainerIdentity.Uuid { ContainerId = id },
        DateTime.UtcNow
      )
    );
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketIdentityHint(id, 3, DateTime.UtcNow)
    );
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketCycleEnd(
        3,
        [],
        ImmutableHashSet.Create(id),
        DateTime.UtcNow,
        foreignId: "finalize-3"
      )
    );
  }

  [Test]
  public async Task FinalizationAndHintRemovalRollBackTogether()
  {
    var id = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketContentSnapshot(
      [],
      new ContainerIdentity.Uuid { ContainerId = id },
      DateTime.UtcNow
    );
    repository.RecordBasketIdentityHint(id, 9, DateTime.UtcNow);
    using (var command = new SqliteConnection("Data Source=" + _databaseFile))
    {
      command.Open();
      using var trigger = command.CreateCommand();
      trigger.CommandText =
        "CREATE TRIGGER fail_membership BEFORE INSERT ON basket_cycle_container_ids BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      trigger.ExecuteNonQuery();
    }

    await AssertThrows<SqliteException>(() =>
      repository.RecordBasketCycleEnd(
        9,
        [],
        ImmutableHashSet.Create(id),
        DateTime.UtcNow,
        foreignId: "rollback-cycle"
      )
    );
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(2);
    await Assert.That(repository.GetCurrentBasketIdentityHints()).Count().IsEqualTo(1);
    await Assert.That(repository.GetFinalizedBasketCycles(9)).IsEmpty();
  }

  [Test]
  public async Task FinalizedHistoryUsesMembershipAndIgnoresWrongHint()
  {
    var id = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var arrival = repository.RecordBasketArriveLocation(
      [],
      new ContainerIdentity.Uuid { ContainerId = id },
      "Staging",
      1,
      DateTime.UtcNow
    );
    repository.RecordBasketIdentityHint(id, 3, DateTime.UtcNow);
    var departure = repository.RecordBasketDepartLocation(
      [],
      new ContainerIdentity.Uuid { ContainerId = id },
      "Staging",
      1,
      DateTime.UtcNow.AddMinutes(7),
      TimeSpan.FromMinutes(7)
    );
    var cycle = repository.RecordBasketCycleEnd(
      4,
      [],
      ImmutableHashSet.Create(id),
      DateTime.UtcNow.AddMinutes(8)
    );

    var history = repository.GetLogForFinalizedBasketCycle(cycle.Counter);
    await Assert
      .That(history.Select(entry => entry.Counter))
      .IsEquivalentTo([arrival.Counter, departure.Counter, cycle.Counter]);
    await Assert
      .That(history.Single(entry => entry.Counter == departure.Counter).ElapsedTime)
      .IsEqualTo(TimeSpan.FromMinutes(7));
    await Assert.That(history).DoesNotContain(entry => entry.LogType == LogType.BasketIdentityHint);
    await Assert.That(repository.GetFinalizedBasketCycles(3)).IsEmpty();
    await Assert.That(repository.GetFinalizedBasketCycles(4)).Count().IsEqualTo(1);
  }

  [Test]
  public async Task FinalizedHistoryCombinesLocationEvidenceAcrossFragments()
  {
    var arrivalId = Guid.NewGuid();
    var departureId = Guid.NewGuid();
    var arrivalTime = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    var arrival = repository.RecordBasketArriveLocation(
      [],
      new ContainerIdentity.Uuid { ContainerId = arrivalId },
      "Staging",
      1,
      arrivalTime
    );
    repository.RecordBasketIdentityHint(arrivalId, 5, arrivalTime);
    var departure = repository.RecordBasketDepartLocation(
      [],
      new ContainerIdentity.Uuid { ContainerId = departureId },
      "Staging",
      1,
      arrivalTime.AddMinutes(12),
      TimeSpan.FromMinutes(12)
    );
    repository.RecordBasketIdentityHint(departureId, 5, arrivalTime.AddMinutes(12));
    var cycle = repository.RecordBasketCycleEnd(
      5,
      [],
      ImmutableHashSet.Create(arrivalId, departureId),
      arrivalTime.AddMinutes(15)
    );

    var history = repository.GetLogForFinalizedBasketCycle(cycle.Counter);
    await Assert
      .That(history.Select(entry => entry.Counter))
      .IsEquivalentTo([arrival.Counter, departure.Counter, cycle.Counter]);
    await Assert
      .That(history.Single(entry => entry.Counter == departure.Counter).ElapsedTime)
      .IsEqualTo(TimeSpan.FromMinutes(12));
  }

  [Test]
  public async Task LegacyNumberedBasketCycleHasNoUuidMembership()
  {
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordEmptyBasket(8, DateTime.UtcNow);
    var cycleEnd = repository.RecordEmptyBasket(8, DateTime.UtcNow, basketEnd: true).Single();

    await Assert.That(cycleEnd.CycleEndContainerIds).IsNull();
    await Assert
      .That(repository.GetFinalizedBasketCycle(cycleEnd.Counter).Counter)
      .IsEqualTo(cycleEnd.Counter);
  }

  [Test]
  public async Task ContainerIdsAreOmittedUnlessTheCycleEndFinalizesFragments()
  {
    var id = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordEmptyBasket(8, DateTime.UtcNow);
    var legacyCycleEnd = repository.RecordEmptyBasket(8, DateTime.UtcNow, basketEnd: true).Single();
    repository.RecordBasketContentSnapshot(
      [],
      new ContainerIdentity.Uuid { ContainerId = id },
      DateTime.UtcNow
    );
    var finalizedCycleEnd = repository.RecordBasketCycleEnd(
      9,
      [],
      ImmutableHashSet.Create(id),
      DateTime.UtcNow
    );
    var jsonOptions = new JsonSerializerOptions();
    FMSInsightWebHost.JsonSettings(jsonOptions);

    var legacyJson = JsonSerializer.Serialize(legacyCycleEnd, jsonOptions);
    using var finalizedJson = JsonDocument.Parse(
      JsonSerializer.Serialize(finalizedCycleEnd, jsonOptions)
    );

    await Assert.That(legacyJson).DoesNotContain("\"containerIds\"");
    await Assert
      .That(
        finalizedJson.RootElement.GetProperty("containerIds").EnumerateArray().Single().GetGuid()
      )
      .IsEqualTo(id);
  }

  private static async Task AssertThrows<TException>(Action action)
    where TException : Exception
  {
    Exception exception = null;
    try
    {
      action();
    }
    catch (Exception caught)
    {
      exception = caught;
    }
    await Assert.That(exception).IsTypeOf<TException>();
  }

  private static BasketStationOperation LoadOntoBasketOperation(
    ContainerIdentity identity,
    long materialId
  ) =>
    new()
    {
      Transfers =
      [
        new BasketStationTransfer.LoadOntoBasket
        {
          BasketIdentity = identity,
          Material =
          [
            new EventLogMaterial
            {
              MaterialID = materialId,
              Process = 1,
              Face = 0,
            },
          ],
          ActiveOperationTime = TimeSpan.FromMinutes(1),
        },
      ],
      CycleBoundaries =
      [
        new BasketCycleBoundary.Start
        {
          BasketIdentity = identity,
          Material =
          [
            new EventLogMaterial
            {
              MaterialID = materialId,
              Process = 1,
              Face = 0,
            },
          ],
        },
      ],
    };

  private static void QueueMaterial(
    IRepository repository,
    long materialId,
    string queue,
    DateTime time,
    string operatorName = null,
    string reason = null
  ) =>
    repository.RecordAddMaterialToQueue(
      new EventLogMaterial
      {
        MaterialID = materialId,
        Process = 0,
        Face = 0,
      },
      queue,
      -1,
      operatorName,
      reason,
      time
    );
}
