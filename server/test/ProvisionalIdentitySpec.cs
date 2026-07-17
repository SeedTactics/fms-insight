using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

#pragma warning disable TUnit0018 // Restart coverage intentionally replaces the repository config.
public sealed class ProvisionalIdentitySpec : IDisposable
{
  private readonly string _databaseFile = Path.Combine(
    Path.GetTempPath(),
    Guid.NewGuid().ToString("N") + ".db"
  );
  private RepositoryConfig _repositoryConfig;

  public ProvisionalIdentitySpec()
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
  public async Task PersistsNumberedProvisionalAndResolutionIdentityShapes()
  {
    var provisional = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();

    var noIdentity = repository.RecordGeneralMessage(
      mats: [],
      program: "NoIdentity",
      result: "",
      pallet: 0
    );
    var numbered = repository.RecordBasketContentSnapshot(
      mats: [],
      basketIdentity: new PalletIdentity.Numbered { Pallet = 7 },
      timeUTC: DateTime.UtcNow
    );
    var provisionalSnapshot = repository.RecordBasketContentSnapshot(
      mats: [],
      basketIdentity: new PalletIdentity.Provisional { ProvisionalPallet = provisional },
      timeUTC: DateTime.UtcNow
    );
    var resolution = repository.RecordResolvedPalletIdentity(
      provisional,
      pallet: 7,
      timeUTC: DateTime.UtcNow
    );

    var loaded = repository.GetRecentLog(0).ToImmutableList();
    await Assert
      .That(loaded.Single(entry => entry.Counter == noIdentity.Counter).Identity)
      .IsTypeOf<PalletIdentity.None>();
    await Assert
      .That(loaded.Single(entry => entry.Counter == numbered.Counter).Identity)
      .IsEqualTo(new PalletIdentity.Numbered { Pallet = 7 });
    await Assert
      .That(loaded.Single(entry => entry.Counter == provisionalSnapshot.Counter).Identity)
      .IsEqualTo(new PalletIdentity.Provisional { ProvisionalPallet = provisional });
    await Assert
      .That(loaded.Single(entry => entry.Counter == resolution.Counter).Identity)
      .IsEqualTo(new PalletIdentity.Resolution { ProvisionalPallet = provisional, Pallet = 7 });
    await Assert
      .That(
        new LogEntry(
          cntr: -1,
          mat: [],
          pal: -1,
          ty: LogType.GeneralMessage,
          locName: "Legacy",
          locNum: 1,
          prog: "Legacy",
          start: false,
          endTime: DateTime.UtcNow,
          result: ""
        ).Identity
      )
      .IsTypeOf<PalletIdentity.None>();
  }

  [Test]
  public async Task RejectsInvalidIdentityAndSnapshotShapes()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var now = DateTime.UtcNow;

    await AssertThrows<ArgumentException>(() =>
      repository.RecordBasketArriveLocation(
        mats: [],
        basketIdentity: new PalletIdentity.None(),
        locationName: "Staging",
        locationPosition: 1,
        timeUTC: now
      )
    );
    await AssertThrows<ArgumentException>(() =>
      repository.RecordBasketArriveLocation(
        mats: [],
        basketIdentity: new PalletIdentity.Resolution
        {
          ProvisionalPallet = Guid.NewGuid(),
          Pallet = 2,
        },
        locationName: "Staging",
        locationPosition: 1,
        timeUTC: now
      )
    );
    await AssertThrows<ArgumentException>(() =>
      repository.RecordResolvedPalletIdentity(Guid.Empty, pallet: 2, timeUTC: now)
    );
    await AssertThrows<ArgumentException>(() =>
      repository.RecordResolvedPalletIdentity(Guid.NewGuid(), pallet: 0, timeUTC: now)
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
  }

  [Test]
  public async Task RoundTripsCompleteNumberedAndProvisionalBasketSnapshots()
  {
    var provisional = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job", "part", 2);
    var material = new EventLogMaterial
    {
      MaterialID = materialId,
      Process = 1,
      Face = 2,
    };
    var numbered = repository.RecordBasketContentSnapshot(
      [material],
      new PalletIdentity.Numbered { Pallet = 3 },
      DateTime.UtcNow
    );
    var provisionalSnapshot = repository.RecordBasketContentSnapshot(
      [material],
      new PalletIdentity.Provisional { ProvisionalPallet = provisional },
      DateTime.UtcNow
    );

    var loaded = repository.GetRecentLog(0).ToImmutableList();
    foreach (var counter in new[] { numbered.Counter, provisionalSnapshot.Counter })
    {
      var snapshot = loaded.Single(entry => entry.Counter == counter);
      await Assert.That(snapshot.LogType).IsEqualTo(LogType.BasketContentSnapshot);
      await Assert.That(snapshot.Material.Single().MaterialID).IsEqualTo(materialId);
      await Assert.That(snapshot.Material.Single().Face).IsEqualTo(2);
    }
  }

  [Test]
  public async Task ResolutionCacheUsesEventCounterAndSurvivesRestart()
  {
    var provisional = Guid.NewGuid();
    var laterTime = DateTime.UtcNow;
    using (var repository = _repositoryConfig.OpenConnection())
    {
      var first = repository.RecordResolvedPalletIdentity(provisional, 4, laterTime);
      var correction = repository.RecordResolvedPalletIdentity(
        provisional,
        6,
        laterTime.AddDays(-1)
      );

      var current = repository.GetResolvedPalletIdentity(provisional);
      await Assert.That(current.Pallet).IsEqualTo(6);
      await Assert.That(current.ResolutionEventCounter).IsEqualTo(correction.Counter);
      await Assert.That(correction.Counter).IsGreaterThan(first.Counter);
      await Assert
        .That(repository.ReconstructResolvedPalletIdentities()[provisional])
        .IsEqualTo(current);
    }

    _repositoryConfig.Dispose();
    _repositoryConfig = RepositoryConfig.InitializeEventDatabase(
      null,
      _databaseFile,
      pooling: false
    );
    using var restartedRepository = _repositoryConfig.OpenConnection();
    await Assert
      .That(restartedRepository.GetResolvedPalletIdentity(provisional).Pallet)
      .IsEqualTo(6);
    await Assert
      .That(
        restartedRepository.ResolvePalletIdentity(
          new PalletIdentity.Provisional { ProvisionalPallet = provisional }
        )
      )
      .IsEqualTo(new PalletIdentity.Numbered { Pallet = 6 });
  }

  [Test]
  public async Task ResolutionAndCacheUpdateRollBackTogether()
  {
    var provisional = Guid.NewGuid();
    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText =
        "CREATE TRIGGER fail_resolution_cache BEFORE INSERT ON resolved_identities BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      command.ExecuteNonQuery();
    }

    using var repository = _repositoryConfig.OpenConnection();
    await AssertThrows<SqliteException>(() =>
      repository.RecordResolvedPalletIdentity(provisional, 4, DateTime.UtcNow)
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
    await Assert.That(repository.GetResolvedPalletIdentity(provisional)).IsNull();
  }

  [Test]
  public async Task RejectsTwoProvisionalIdentitiesResolvedToTheSameNumber()
  {
    var firstProvisional = Guid.NewGuid();
    var conflictingProvisional = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();

    var firstResolution = repository.RecordResolvedPalletIdentity(
      firstProvisional,
      pallet: 4,
      timeUTC: DateTime.UtcNow
    );
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordResolvedPalletIdentity(
        conflictingProvisional,
        pallet: 4,
        timeUTC: DateTime.UtcNow
      )
    );

    await Assert
      .That(repository.GetResolvedPalletIdentity(firstProvisional).ResolutionEventCounter)
      .IsEqualTo(firstResolution.Counter);
    await Assert.That(repository.GetResolvedPalletIdentity(conflictingProvisional)).IsNull();
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(1);
  }

  [Test]
  public async Task CurrentBasketLogSearchesOnlyEventsAfterLatestResolvedCycle()
  {
    var provisional = Guid.NewGuid();
    var start = DateTime.UtcNow.AddMinutes(-10);
    using var repository = _repositoryConfig.OpenConnection();

    var beforeCycle = repository.RecordBasketContentSnapshot(
      mats: [],
      basketIdentity: new PalletIdentity.Provisional { ProvisionalPallet = provisional },
      timeUTC: start
    );
    repository.RecordResolvedPalletIdentity(provisional, pallet: 4, timeUTC: start.AddMinutes(1));
    var cycle = repository.RecordEmptyBasket(4, start.AddMinutes(2)).Single();
    var afterCycle = repository.RecordBasketContentSnapshot(
      mats: [],
      basketIdentity: new PalletIdentity.Provisional { ProvisionalPallet = provisional },
      timeUTC: start.AddMinutes(3)
    );

    foreach (
      var identity in new PalletIdentity[]
      {
        new PalletIdentity.Numbered { Pallet = 4 },
        new PalletIdentity.Provisional { ProvisionalPallet = provisional },
      }
    )
    {
      await Assert
        .That(repository.CurrentBasketLog(identity).Select(entry => entry.Counter))
        .IsEquivalentTo([afterCycle.Counter]);
      await Assert
        .That(
          repository
            .CurrentBasketLog(identity, includeLastCycleEvt: true)
            .Select(entry => entry.Counter)
        )
        .IsEquivalentTo([cycle.Counter, afterCycle.Counter]);
    }

    await Assert.That(beforeCycle.Counter).IsLessThan(cycle.Counter);
  }

  [Test]
  public async Task ProvisionalArrivalPairsWithNumberedDepartureAfterResolution()
  {
    var provisional = Guid.NewGuid();
    var start = DateTime.UtcNow.AddMinutes(-15);
    using var repository = _repositoryConfig.OpenConnection();

    var firstObservation = repository.RecordBasketArrivalAndContentSnapshot(
      mats: [],
      basketIdentity: new PalletIdentity.Provisional { ProvisionalPallet = provisional },
      locationName: "Staging",
      locationPosition: 1,
      timeUTC: start
    );
    repository.RecordBasketContentSnapshot(
      mats: [],
      basketIdentity: new PalletIdentity.Provisional { ProvisionalPallet = provisional },
      timeUTC: start.AddMinutes(5)
    );
    repository.RecordResolvedPalletIdentity(provisional, 4, start.AddMinutes(10));
    var departure = repository.RecordBasketDepartLocation(
      mats: [],
      basketIdentity: new PalletIdentity.Numbered { Pallet = 4 },
      locationName: "Staging",
      locationPosition: 1,
      timeUTC: start.AddMinutes(15)
    );

    var currentLog = repository.CurrentBasketLog(4);
    await Assert
      .That(firstObservation.Select(entry => entry.LogType))
      .IsEquivalentTo([LogType.BasketInLocation, LogType.BasketContentSnapshot]);
    await Assert
      .That(
        currentLog.Count(entry =>
          entry.LogType == LogType.BasketInLocation && entry.Program == "Arrive"
        )
      )
      .IsEqualTo(1);
    await Assert
      .That(currentLog.Single(entry => entry.Counter == departure.Counter).ElapsedTime)
      .IsEqualTo(TimeSpan.FromMinutes(15));
    await Assert.That(repository.GetUnresolvedProvisionalPallets()).IsEmpty();
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
