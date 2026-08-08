using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class BasketLocationObservationSpec : IDisposable
{
  private readonly string _databaseFile = Path.Combine(
    Path.GetTempPath(),
    Guid.NewGuid().ToString("N") + ".db"
  );
  private readonly RepositoryConfig _repositoryConfig;

  public BasketLocationObservationSpec()
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
  public async Task RecordsSourcedLocationWithoutBasketCycleOrContents()
  {
    var observationId = Guid.NewGuid();
    var correlationId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 3, 18, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();

    var observation = repository.RecordBasketLocationObservation(
      observationId,
      4,
      Storage(" Basket storage "),
      time,
      OperatorSource("move-4-storage"),
      new EventLogMetadata { CorrelationId = correlationId.ToString("D") }
    );

    await Assert.That(observation.Position).IsEqualTo(Storage());
    await Assert.That(observation.Source).IsEqualTo(OperatorSource("move-4-storage"));
    await Assert.That(observation.CorrelationId).IsEqualTo(correlationId.ToString("D"));
    await Assert
      .That(
        repository
          .CurrentBasketLog(4)
          .Where(entry => entry.LogType is LogType.BasketCycle or LogType.BasketContentSnapshot)
      )
      .IsEmpty();
    await Assert
      .That(repository.GetRecentLog(0).Select(entry => entry.LogType))
      .IsEquivalentTo([LogType.BasketLocationObservation]);
    await Assert
      .That(repository.GetCurrentBasketLocationObservations().Single())
      .IsEqualTo(observation);
    await Assert
      .That(repository.GetBasketLocationObservation(observationId))
      .IsEqualTo(observation);
  }

  [Test]
  public async Task SensorSourceRoundTripsAndLatestObservationSurvivesRestart()
  {
    var firstId = Guid.NewGuid();
    var secondId = Guid.NewGuid();
    using (var repository = _repositoryConfig.OpenConnection())
    {
      repository.RecordBasketLocationObservation(
        firstId,
        4,
        Storage(),
        DateTime.UtcNow,
        OperatorSource()
      );
      repository.RecordBasketLocationObservation(
        secondId,
        4,
        LoadStation(),
        DateTime.UtcNow,
        SensorSource()
      );
    }

    using var restarted = _repositoryConfig.OpenConnection();
    var current = restarted.GetCurrentBasketLocationObservations(4).Single();
    await Assert.That(current.ObservationId).IsEqualTo(secondId);
    await Assert.That(current.Position).IsEqualTo(LoadStation());
    await Assert.That(current.Source).IsEqualTo(SensorSource());
  }

  [Test]
  public async Task IdenticalRetryReturnsOriginalAndChangedRetryConflicts()
  {
    var observationId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var first = repository.RecordBasketLocationObservation(
      observationId,
      4,
      Storage(),
      DateTime.UtcNow,
      OperatorSource()
    );
    var retry = repository.RecordBasketLocationObservation(
      observationId,
      4,
      Storage(),
      DateTime.UtcNow.AddMinutes(1),
      OperatorSource()
    );

    await Assert.That(retry).IsEqualTo(first);
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(1);
    await Assert.ThrowsAsync<ConflictRequestException>(() =>
      Task.Run(() =>
        repository.RecordBasketLocationObservation(
          observationId,
          5,
          Storage(),
          DateTime.UtcNow,
          OperatorSource()
        )
      )
    );
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(1);
  }

  [Test]
  public async Task CorrectionAtomicallySupersedesTargetAndRecordsReplacement()
  {
    var targetId = Guid.NewGuid();
    var replacementId = Guid.NewGuid();
    var correctionId = Guid.NewGuid();
    var correlationId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 3, 19, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketLocationObservation(targetId, 5, Storage(), time, OperatorSource());

    var result = repository.CorrectBasketLocationObservation(
      correctionId,
      targetId,
      new BasketLocationObservationReplacement
      {
        ObservationId = replacementId,
        BasketId = 4,
        Position = Storage(),
      },
      time.AddMinutes(1),
      OperatorSource("visual-recheck"),
      "Rechecked the visible basket number",
      metadata: new EventLogMetadata { CorrelationId = correlationId.ToString("D") }
    );

    await Assert.That(result.Replacement!.ObservationId).IsEqualTo(replacementId);
    await Assert
      .That(repository.GetCurrentBasketLocationObservations().Single().BasketId)
      .IsEqualTo(4);
    await Assert.That(repository.GetBasketLocationObservation(targetId)).IsNotNull();
    var correction = repository.GetBasketLocationObservationCorrections(targetId).Single();
    await Assert.That(correction.CorrectionId).IsEqualTo(correctionId);
    await Assert.That(correction.Source).IsEqualTo(OperatorSource("visual-recheck"));
    await Assert.That(correction.CorrelationId).IsEqualTo(correlationId.ToString("D"));
    await Assert.That(correction.Note).IsEqualTo("Rechecked the visible basket number");
    await Assert
      .That(repository.GetRecentLog(0).Select(entry => entry.LogType))
      .IsEquivalentTo([
        LogType.BasketLocationObservation,
        LogType.BasketLocationObservationCorrection,
        LogType.BasketLocationObservation,
      ]);
  }

  [Test]
  public async Task CorrectionRejectsReplacementAtDifferentPosition()
  {
    var targetId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketLocationObservation(
      targetId,
      5,
      Storage(),
      DateTime.UtcNow,
      OperatorSource()
    );

    await Assert.ThrowsAsync<ArgumentException>(() =>
      Task.Run(() =>
        repository.CorrectBasketLocationObservation(
          Guid.NewGuid(),
          targetId,
          new BasketLocationObservationReplacement
          {
            ObservationId = Guid.NewGuid(),
            BasketId = 4,
            Position = LoadStation(),
          },
          DateTime.UtcNow,
          OperatorSource()
        )
      )
    );
    await Assert.That(repository.GetCurrentBasketLocationObservations()).Count().IsEqualTo(1);
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(1);
  }

  [Test]
  public async Task RetractionFallsBackToEarlierPositiveObservationAndSurvivesRestart()
  {
    var earlierId = Guid.NewGuid();
    var targetId = Guid.NewGuid();
    var correctionId = Guid.NewGuid();
    var time = DateTime.UtcNow;
    using (var repository = _repositoryConfig.OpenConnection())
    {
      repository.RecordBasketLocationObservation(earlierId, 4, Storage(), time, OperatorSource());
      repository.RecordBasketLocationObservation(
        targetId,
        4,
        LoadStation(),
        time.AddMinutes(1),
        OperatorSource()
      );
      repository.CorrectBasketLocationObservation(
        correctionId,
        targetId,
        replacement: null,
        time.AddMinutes(2),
        OperatorSource()
      );
    }

    using var restarted = _repositoryConfig.OpenConnection();
    await Assert
      .That(restarted.GetCurrentBasketLocationObservations(4).Single().ObservationId)
      .IsEqualTo(earlierId);
    await Assert.That(restarted.GetBasketLocationObservation(targetId)).IsNotNull();
    await Assert
      .That(restarted.GetBasketLocationObservationCorrections().Single().CorrectionId)
      .IsEqualTo(correctionId);
  }

  [Test]
  public async Task CorrectionRetryConvergesAndChangedRetryConflicts()
  {
    var targetId = Guid.NewGuid();
    var correctionId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketLocationObservation(
      targetId,
      5,
      Storage(),
      DateTime.UtcNow,
      OperatorSource()
    );
    var replacement = new BasketLocationObservationReplacement
    {
      ObservationId = Guid.NewGuid(),
      BasketId = 4,
      Position = Storage(),
    };
    var first = repository.CorrectBasketLocationObservation(
      correctionId,
      targetId,
      replacement,
      DateTime.UtcNow,
      OperatorSource()
    );
    var retry = repository.CorrectBasketLocationObservation(
      correctionId,
      targetId,
      replacement,
      DateTime.UtcNow.AddMinutes(1),
      OperatorSource()
    );

    await Assert.That(retry.Correction.EventCounter).IsEqualTo(first.Correction.EventCounter);
    await Assert.That(retry.Replacement!.EventCounter).IsEqualTo(first.Replacement!.EventCounter);
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(3);
    await Assert.ThrowsAsync<ConflictRequestException>(() =>
      Task.Run(() =>
        repository.CorrectBasketLocationObservation(
          correctionId,
          targetId,
          replacement with
          {
            BasketId = 3,
          },
          DateTime.UtcNow,
          OperatorSource()
        )
      )
    );
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(3);
  }

  [Test]
  public async Task Version40DatabaseUpgradesWithTypedLocationObservationTables()
  {
    var databaseFile = Path.GetTempFileName();
    File.Copy("database-ver40.db", databaseFile, overwrite: true);
    try
    {
      using var upgradedConfig = RepositoryConfig.InitializeEventDatabase(
        null,
        databaseFile,
        pooling: false
      );
      using var upgraded = upgradedConfig.OpenConnection();
      var observation = upgraded.RecordBasketLocationObservation(
        Guid.NewGuid(),
        4,
        Storage(),
        DateTime.UtcNow,
        OperatorSource()
      );
      upgraded.CorrectBasketLocationObservation(
        Guid.NewGuid(),
        observation.ObservationId,
        replacement: null,
        DateTime.UtcNow,
        OperatorSource()
      );

      await Assert.That(upgraded.GetCurrentBasketLocationObservations()).IsEmpty();
    }
    finally
    {
      if (File.Exists(databaseFile))
        File.Delete(databaseFile);
    }
  }

  private static BasketEvidenceSource OperatorSource(string sourceObservationId = null) =>
    new() { Kind = BasketEvidenceSourceKind.Operator, Name = "operator" };

  private static BasketEvidenceSource SensorSource() =>
    new() { Kind = BasketEvidenceSourceKind.Sensor, Name = "robot" };

  private static BasketPosition Storage(string title = "Basket storage") =>
    new()
    {
      Location = BasketLocationEnum.Storage,
      LocationNum = 20,
      LocationTitle = title,
    };

  private static BasketPosition LoadStation() =>
    new()
    {
      Location = BasketLocationEnum.LoadUnload,
      LocationNum = 10,
      LocationTitle = "Basket load station",
    };
}
