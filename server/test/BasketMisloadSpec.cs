using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

#pragma warning disable TUnit0018 // Restart coverage intentionally replaces the repository config.
public sealed class BasketMisloadSpec : IDisposable
{
  private readonly string _databaseFile = Path.Combine(
    Path.GetTempPath(),
    Guid.NewGuid().ToString("N") + ".db"
  );
  private RepositoryConfig _repositoryConfig;

  public BasketMisloadSpec()
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
  public async Task RecordsOperatorMisloadForNumberedBasket()
  {
    var misloadId = Guid.NewGuid();
    var correlationId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var misload = repository.RecordBasketMisload(
      misloadId,
      basketId: 5,
      contentEpisodeIds: [],
      Storage(),
      OperatorSource(),
      " Contents require inspection ",
      DateTime.UtcNow,
      new EventLogMetadata
      {
        ForeignId = "misload-command",
        CorrelationId = correlationId.ToString("D"),
      }
    );

    await Assert.That(misload.Reason).IsEqualTo("Contents require inspection");
    await Assert.That(misload.CorrelationId).IsEqualTo(correlationId.ToString("D"));
    await Assert.That(repository.GetActiveBasketMisloads().Single()).IsEqualTo(misload);
    await Assert.That(repository.GetBasketMisload(misloadId)).IsEqualTo(misload);
    var log = repository.GetRecentLog(0).Single();
    await Assert.That(log.LogType).IsEqualTo(LogType.BasketMisload);
    await Assert.That(log.Material).IsEmpty();
    await Assert.That(log.ForeignID).IsEqualTo("misload-command");
    await Assert.That(log.CorrelationId).IsEqualTo(correlationId.ToString("D"));
    await Assert
      .That(repository.GetLogForCorrelationId(correlationId.ToString("D")))
      .IsEquivalentTo([log]);
  }

  [Test]
  public async Task RecordsSensorMisloadForOpenContentEpisodes()
  {
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisodes(repository, first, second);
    var misload = repository.RecordBasketMisload(
      Guid.NewGuid(),
      basketId: null,
      [first, second],
      Staging(),
      SensorSource(),
      "robot reported slot mismatch",
      DateTime.UtcNow
    );

    await Assert.That(misload.BasketId).IsNull();
    await Assert.That(misload.ContentEpisodeIds).IsEquivalentTo([first, second]);
    await Assert.That(misload.Source).IsEqualTo(SensorSource());
    await Assert.That(repository.GetRecentLog(0).Last().Pallet).IsEqualTo(0);
  }

  [Test]
  public async Task NumberAndEpisodesRemainOneActiveTargetAcrossRestart()
  {
    var contentEpisodeId = Guid.NewGuid();
    var misloadId = Guid.NewGuid();
    using (var repository = _repositoryConfig.OpenConnection())
    {
      OpenEpisodes(repository, contentEpisodeId);
      repository.RecordBasketMisload(
        misloadId,
        basketId: 5,
        [contentEpisodeId],
        Staging(),
        SensorSource(),
        "inspection",
        DateTime.UtcNow
      );
    }

    _repositoryConfig.Dispose();
    _repositoryConfig = RepositoryConfig.InitializeEventDatabase(
      null,
      _databaseFile,
      pooling: false
    );
    using var restarted = _repositoryConfig.OpenConnection();
    var active = restarted.GetActiveBasketMisloads().Single();
    await Assert.That(active.MisloadId).IsEqualTo(misloadId);
    await Assert.That(active.BasketId).IsEqualTo(5);
    await Assert.That(active.ContentEpisodeIds.Single()).IsEqualTo(contentEpisodeId);
  }

  [Test]
  public async Task RejectsPositionOnlyAndMissingEpisodeTargets()
  {
    using var repository = _repositoryConfig.OpenConnection();
    await Assert.ThrowsAsync<ArgumentException>(() =>
      Task.Run(() =>
        repository.RecordBasketMisload(
          Guid.NewGuid(),
          basketId: null,
          [],
          Storage(),
          OperatorSource(),
          "inspection",
          DateTime.UtcNow
        )
      )
    );
    await Assert.ThrowsAsync<ConflictRequestException>(() =>
      Task.Run(() =>
        repository.RecordBasketMisload(
          Guid.NewGuid(),
          basketId: null,
          [Guid.NewGuid()],
          Staging(),
          SensorSource(),
          "inspection",
          DateTime.UtcNow
        )
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
  }

  [Test]
  public async Task RejectsClosedContentEpisodeTarget()
  {
    var contentEpisodeId = Guid.NewGuid();
    using (var repository = _repositoryConfig.OpenConnection())
      OpenEpisodes(repository, contentEpisodeId);
    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText =
        "INSERT INTO basket_cycle_container_ids(CycleCounter, ContainerId) VALUES(1, $id)";
      command.Parameters.AddWithValue("id", contentEpisodeId.ToString("D"));
      command.ExecuteNonQuery();
    }
    using var reopened = _repositoryConfig.OpenConnection();

    await Assert.ThrowsAsync<ConflictRequestException>(() =>
      Task.Run(() =>
        reopened.RecordBasketMisload(
          Guid.NewGuid(),
          basketId: null,
          [contentEpisodeId],
          Staging(),
          SensorSource(),
          "inspection",
          DateTime.UtcNow
        )
      )
    );
    await Assert.That(reopened.GetActiveBasketMisloads()).IsEmpty();
  }

  [Test]
  public async Task ExactMisloadRetryReturnsOriginalAndChangedRetryConflicts()
  {
    var misloadId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var first = repository.RecordBasketMisload(
      misloadId,
      5,
      [],
      Storage(),
      OperatorSource(),
      "inspection",
      DateTime.UtcNow
    );
    var retry = repository.RecordBasketMisload(
      misloadId,
      5,
      [],
      Storage(),
      OperatorSource(),
      "inspection",
      DateTime.UtcNow.AddHours(1)
    );

    await Assert.That(retry.EventCounter).IsEqualTo(first.EventCounter);
    await Assert.That(retry.TimeUTC).IsEqualTo(first.TimeUTC);
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(1);
    await Assert.ThrowsAsync<ConflictRequestException>(() =>
      Task.Run(() =>
        repository.RecordBasketMisload(
          misloadId,
          6,
          [],
          Storage(),
          OperatorSource(),
          "inspection",
          DateTime.UtcNow
        )
      )
    );
  }

  [Test]
  public async Task ResolutionClearsActiveProjectionAndPreservesHistoryAcrossRestart()
  {
    var misloadId = Guid.NewGuid();
    var resolutionId = Guid.NewGuid();
    using (var repository = _repositoryConfig.OpenConnection())
    {
      repository.RecordBasketMisload(
        misloadId,
        5,
        [],
        Storage(),
        OperatorSource(),
        "inspection",
        DateTime.UtcNow
      );
      var resolution = repository.ResolveBasketMisload(
        resolutionId,
        misloadId,
        BasketMisloadResolutionKind.ClearedAfterInspection,
        OperatorSource(),
        DateTime.UtcNow,
        "inspected"
      );
      await Assert.That(resolution.Note).IsEqualTo("inspected");
      await Assert.That(repository.GetActiveBasketMisloads()).IsEmpty();
    }

    _repositoryConfig.Dispose();
    _repositoryConfig = RepositoryConfig.InitializeEventDatabase(
      null,
      _databaseFile,
      pooling: false
    );
    using var restarted = _repositoryConfig.OpenConnection();
    await Assert.That(restarted.GetActiveBasketMisloads()).IsEmpty();
    await Assert.That(restarted.GetBasketMisload(misloadId)).IsNotNull();
    await Assert
      .That(restarted.GetBasketMisloadResolutions(misloadId).Single().ResolutionId)
      .IsEqualTo(resolutionId);
  }

  [Test]
  public async Task ResolutionRetryConvergesAndSecondResolutionConflicts()
  {
    var misloadId = Guid.NewGuid();
    var resolutionId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketMisload(
      misloadId,
      5,
      [],
      Storage(),
      OperatorSource(),
      "inspection",
      DateTime.UtcNow
    );
    var first = repository.ResolveBasketMisload(
      resolutionId,
      misloadId,
      BasketMisloadResolutionKind.ReportedInError,
      OperatorSource(),
      DateTime.UtcNow
    );
    var retry = repository.ResolveBasketMisload(
      resolutionId,
      misloadId,
      BasketMisloadResolutionKind.ReportedInError,
      OperatorSource(),
      DateTime.UtcNow.AddHours(1)
    );

    await Assert.That(retry.EventCounter).IsEqualTo(first.EventCounter);
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(2);
    await Assert.ThrowsAsync<ConflictRequestException>(() =>
      Task.Run(() =>
        repository.ResolveBasketMisload(
          Guid.NewGuid(),
          misloadId,
          BasketMisloadResolutionKind.Superseded,
          OperatorSource(),
          DateTime.UtcNow
        )
      )
    );
  }

  [Test]
  public async Task MisloadAndActiveProjectionRollBackTogether()
  {
    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText =
        "CREATE TRIGGER fail_active_misload BEFORE INSERT ON active_basket_misloads BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      command.ExecuteNonQuery();
    }
    using var repository = _repositoryConfig.OpenConnection();

    await Assert.ThrowsAsync<SqliteException>(() =>
      Task.Run(() =>
        repository.RecordBasketMisload(
          Guid.NewGuid(),
          5,
          [],
          Storage(),
          OperatorSource(),
          "inspection",
          DateTime.UtcNow
        )
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
    await Assert.That(repository.GetActiveBasketMisloads()).IsEmpty();
  }

  private static void OpenEpisodes(IRepository repository, params Guid[] episodeIds)
  {
    foreach (var id in episodeIds)
      repository.RecordBasketContentSnapshot(
        [],
        new ContainerIdentity.Uuid { ContainerId = id },
        DateTime.UtcNow
      );
  }

  private static BasketEvidenceSource OperatorSource() =>
    new() { Kind = BasketEvidenceSourceKind.Operator, Name = "operator" };

  private static BasketEvidenceSource SensorSource() =>
    new() { Kind = BasketEvidenceSourceKind.Sensor, Name = "robot" };

  private static BasketPosition Storage() =>
    new()
    {
      Location = BasketLocationEnum.Storage,
      LocationNum = 20,
      LocationTitle = "Storage",
    };

  private static BasketPosition Staging() =>
    new()
    {
      Location = BasketLocationEnum.LoadStationStaging,
      LocationNum = 10,
      Zone = 1,
      LocationTitle = "Robot staging",
    };
}
