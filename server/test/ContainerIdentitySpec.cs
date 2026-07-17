using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

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
      .RecordBasketLoadUnload(
        new MaterialToLoadOntoBasket
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
    await Assert.That(cycle.BasketCycleContainerIds).IsEquivalentTo([first, second]);
    await Assert.That(repository.GetCurrentBasketIdentityHints()).IsEmpty();
    await Assert.That(repository.GetUnresolvedOpenBasketContainerIds()).IsEmpty();
    await Assert.That(repository.ReconstructBasketIdentityHints()).IsEmpty();
    var loadedCycle = repository.GetFinalizedBasketCycle(cycle.Counter);
    await Assert.That(loadedCycle.Counter).IsEqualTo(cycle.Counter);
    await Assert.That(loadedCycle.BasketCycleContainerIds).IsEquivalentTo([first, second]);
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

    await Assert.That(cycleEnd.BasketCycleContainerIds).IsEmpty();
    await Assert
      .That(repository.GetFinalizedBasketCycle(cycleEnd.Counter).Counter)
      .IsEqualTo(cycleEnd.Counter);
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
}
