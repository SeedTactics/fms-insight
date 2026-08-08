using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class BasketRegionSurveySpec : IDisposable
{
  private readonly string _databaseFile = Path.Combine(
    Path.GetTempPath(),
    Guid.NewGuid().ToString("N") + ".db"
  );
  private readonly RepositoryConfig _repositoryConfig;

  public BasketRegionSurveySpec()
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
  public async Task RecordsOneAtomicScopedSurveyWithoutMaterialSubjects()
  {
    var surveyId = Guid.NewGuid();
    var correlationId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var survey = repository.RecordBasketRegionSurvey(
      surveyId,
      Storage(" Storage "),
      [1, 3, 5],
      unidentifiedBasketCount: 2,
      BasketRegionSurveyCompleteness.Complete,
      DateTime.UtcNow,
      OperatorSource(),
      new EventLogMetadata { CorrelationId = correlationId.ToString("D") }
    );

    await Assert.That(survey.Region).IsEqualTo(Storage());
    await Assert.That(survey.ObservedBasketIds).IsEquivalentTo([1, 3, 5]);
    await Assert.That(survey.UnidentifiedBasketCount).IsEqualTo(2);
    await Assert.That(survey.CorrelationId).IsEqualTo(correlationId.ToString("D"));
    var log = repository.GetRecentLog(0).Single();
    await Assert.That(log.Counter).IsEqualTo(survey.EventCounter);
    await Assert.That(log.LogType).IsEqualTo(LogType.BasketRegionSurvey);
    await Assert.That(log.Material).IsEmpty();
    await Assert.That(log.Pallet).IsEqualTo(0);
  }

  [Test]
  public async Task PartialAndCompleteSurveysPersistWithoutDerivingAbsentBaskets()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var partial = repository.RecordBasketRegionSurvey(
      Guid.NewGuid(),
      Storage(),
      [2],
      0,
      BasketRegionSurveyCompleteness.Partial,
      DateTime.UtcNow,
      OperatorSource()
    );
    var complete = repository.RecordBasketRegionSurvey(
      Guid.NewGuid(),
      Storage(),
      [1, 3],
      0,
      BasketRegionSurveyCompleteness.Complete,
      DateTime.UtcNow,
      OperatorSource()
    );

    var surveys = repository.GetBasketRegionSurveys(Storage());
    await Assert.That(surveys).Count().IsEqualTo(2);
    await Assert.That(surveys[0].SurveyId).IsEqualTo(partial.SurveyId);
    await Assert.That(surveys[0].ObservedBasketIds).IsEquivalentTo(partial.ObservedBasketIds);
    await Assert.That(surveys[1].SurveyId).IsEqualTo(complete.SurveyId);
    await Assert.That(surveys[1].ObservedBasketIds).IsEquivalentTo(complete.ObservedBasketIds);
    await Assert
      .That(surveys.SelectMany(survey => survey.ObservedBasketIds).Distinct())
      .IsEquivalentTo([1, 2, 3]);
  }

  [Test]
  public async Task ExactRetryReturnsOriginalAndChangedRetryConflicts()
  {
    var surveyId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    var first = repository.RecordBasketRegionSurvey(
      surveyId,
      Storage(),
      [1, 2],
      0,
      BasketRegionSurveyCompleteness.Complete,
      DateTime.UtcNow,
      OperatorSource()
    );
    var retry = repository.RecordBasketRegionSurvey(
      surveyId,
      Storage(),
      [1, 2],
      0,
      BasketRegionSurveyCompleteness.Complete,
      DateTime.UtcNow.AddHours(1),
      OperatorSource()
    );

    await Assert.That(retry.EventCounter).IsEqualTo(first.EventCounter);
    await Assert.That(retry.TimeUTC).IsEqualTo(first.TimeUTC);
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(1);
    await Assert.ThrowsAsync<ConflictRequestException>(() =>
      Task.Run(() =>
        repository.RecordBasketRegionSurvey(
          surveyId,
          Storage(),
          [1],
          0,
          BasketRegionSurveyCompleteness.Complete,
          DateTime.UtcNow,
          OperatorSource()
        )
      )
    );
  }

  [Test]
  public async Task AcceptsPositiveBasketIdsWithoutConfiguredFleet()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var survey = repository.RecordBasketRegionSurvey(
      Guid.NewGuid(),
      Storage(),
      [98123],
      0,
      BasketRegionSurveyCompleteness.Partial,
      DateTime.UtcNow,
      IntegrationSource()
    );

    await Assert.That(survey.ObservedBasketIds).IsEquivalentTo([98123]);
    await Assert.That(survey.Source).IsEqualTo(IntegrationSource());
  }

  [Test]
  public async Task RejectsInvalidValuesWithoutWrites()
  {
    using var repository = _repositoryConfig.OpenConnection();
    await Assert.ThrowsAsync<ArgumentException>(() =>
      Task.Run(() =>
        repository.RecordBasketRegionSurvey(
          Guid.NewGuid(),
          Storage(),
          [0],
          0,
          BasketRegionSurveyCompleteness.Partial,
          DateTime.UtcNow,
          OperatorSource()
        )
      )
    );
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
      Task.Run(() =>
        repository.RecordBasketRegionSurvey(
          Guid.NewGuid(),
          Storage(),
          [],
          -1,
          BasketRegionSurveyCompleteness.Complete,
          DateTime.UtcNow,
          OperatorSource()
        )
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
  }

  [Test]
  public async Task RejectsMultipleBasketsAtLoadStation()
  {
    using var repository = _repositoryConfig.OpenConnection();
    await Assert.ThrowsAsync<ArgumentException>(() =>
      Task.Run(() =>
        repository.RecordBasketRegionSurvey(
          Guid.NewGuid(),
          LoadStation(),
          [1, 2],
          0,
          BasketRegionSurveyCompleteness.Complete,
          DateTime.UtcNow,
          OperatorSource()
        )
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
  }

  [Test]
  public async Task LatestSurveyPerNormalizedRegionSurvivesRestart()
  {
    var storageLatest = Guid.NewGuid();
    var stationLatest = Guid.NewGuid();
    using (var repository = _repositoryConfig.OpenConnection())
    {
      repository.RecordBasketRegionSurvey(
        Guid.NewGuid(),
        Storage(),
        [1],
        0,
        BasketRegionSurveyCompleteness.Partial,
        DateTime.UtcNow,
        OperatorSource()
      );
      repository.RecordBasketRegionSurvey(
        storageLatest,
        Storage(),
        [1, 2],
        0,
        BasketRegionSurveyCompleteness.Complete,
        DateTime.UtcNow,
        OperatorSource()
      );
      repository.RecordBasketRegionSurvey(
        stationLatest,
        LoadStation(),
        [],
        0,
        BasketRegionSurveyCompleteness.Complete,
        DateTime.UtcNow,
        OperatorSource()
      );
    }

    using var restarted = _repositoryConfig.OpenConnection();
    await Assert
      .That(restarted.GetLatestBasketRegionSurveys().Select(survey => survey.SurveyId))
      .IsEquivalentTo([storageLatest, stationLatest]);
    await Assert.That(restarted.GetBasketRegionSurvey(storageLatest)).IsNotNull();
  }

  [Test]
  public async Task PayloadAndCanonicalEventRollBackTogether()
  {
    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText =
        "CREATE TRIGGER fail_survey_payload BEFORE INSERT ON basket_region_survey_baskets BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      command.ExecuteNonQuery();
    }
    using var repository = _repositoryConfig.OpenConnection();

    await Assert.ThrowsAsync<SqliteException>(() =>
      Task.Run(() =>
        repository.RecordBasketRegionSurvey(
          Guid.NewGuid(),
          Storage(),
          [1],
          0,
          BasketRegionSurveyCompleteness.Complete,
          DateTime.UtcNow,
          OperatorSource()
        )
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
    await Assert.That(repository.GetBasketRegionSurveys()).IsEmpty();
  }

  private static BasketEvidenceSource OperatorSource() =>
    new() { Kind = BasketEvidenceSourceKind.Operator, Name = "operator" };

  private static BasketEvidenceSource IntegrationSource() =>
    new() { Kind = BasketEvidenceSourceKind.Integration, Name = "inventory-system" };

  private static BasketPosition Storage(string title = "Storage") =>
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
