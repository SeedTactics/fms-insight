/* Copyright (c) 2026, John Lenz

All rights reserved.
*/

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class BasketObservationSpec : IDisposable
{
  private readonly RepositoryConfig _repositoryConfig;

  public BasketObservationSpec()
  {
    _repositoryConfig = RepositoryConfig.InitializeMemoryDB(null);
  }

  public void Dispose()
  {
    _repositoryConfig.Dispose();
  }

  [Test]
  public async Task RecordsLocationWithoutInventingContentContinuity()
  {
    var observationId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();

    var observation = repository.RecordBasketObservation(
      observationId,
      4,
      Storage(),
      [],
      OperatorSource(),
      time,
      new EventLogMetadata { ForeignId = "storage-4", CorrelationId = "recovery-1" },
      "Visible during complete storage survey"
    );

    await Assert.That(observation.ObservationId).IsEqualTo(observationId);
    await Assert.That(observation.ContentEpisodeIds).IsEmpty();
    var evidence = repository.GetActiveBasketObservationEvidence(4).Single();
    await Assert.That(evidence.Observation).IsEqualTo(observation);
    await Assert.That(evidence.IsCurrentPositionEvidence).IsTrue();
    await Assert.That(evidence.ActiveContentEpisodeIds).IsEmpty();
    await Assert.That(repository.GetUnresolvedOpenBasketContentEpisodeIds()).IsEmpty();
    await Assert
      .That(repository.GetRecentLog(0).Select(entry => entry.LogType))
      .IsEquivalentTo([LogType.BasketObservation]);
  }

  [Test]
  public async Task DirectContinuityFollowsOneTrackedOccupantAcrossMovement()
  {
    var contentEpisodeId = Guid.NewGuid();
    var firstId = Guid.NewGuid();
    var movementId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisode(repository, contentEpisodeId, time);
    repository.RecordBasketObservation(
      firstId,
      4,
      RobotZone(1),
      [contentEpisodeId],
      OperatorSource(),
      time.AddMinutes(1),
      note: "Operator read basket 4; integration attached the unique zone occupant."
    );

    var moved = repository.RecordBasketObservation(
      movementId,
      4,
      Storage(),
      [contentEpisodeId],
      OperatorSource("movement-completion"),
      time.AddMinutes(2),
      note: "Operator confirmed basket 4 arrived; movement occurrence carried continuity."
    );

    var evidence = repository.GetActiveBasketObservationEvidence(4).Single();
    await Assert.That(moved.ContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
    await Assert.That(evidence.Observation).IsEquivalentTo(moved);
    await Assert.That(evidence.IsCurrentPositionEvidence).IsTrue();
    await Assert.That(evidence.ActiveContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
    await Assert.That(repository.GetUnresolvedOpenBasketContentEpisodeIds()).IsEmpty();
  }

  [Test]
  public async Task SeparatesCurrentPositionFromOlderContentContinuity()
  {
    var contentEpisodeId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisode(repository, contentEpisodeId, time);
    var first = repository.RecordBasketObservation(
      Guid.NewGuid(),
      4,
      RobotZone(1),
      [contentEpisodeId],
      OperatorSource(),
      time.AddMinutes(1)
    );
    var second = repository.RecordBasketObservation(
      Guid.NewGuid(),
      4,
      Storage(),
      [],
      OperatorSource(),
      time.AddMinutes(2)
    );

    var evidence = repository.GetActiveBasketObservationEvidence(4);
    var firstEvidence = evidence.Single(item =>
      item.Observation.ObservationId == first.ObservationId
    );
    var secondEvidence = evidence.Single(item =>
      item.Observation.ObservationId == second.ObservationId
    );
    await Assert
      .That(firstEvidence.Observation.ContentEpisodeIds)
      .IsEquivalentTo([contentEpisodeId]);
    await Assert.That(firstEvidence.IsCurrentPositionEvidence).IsFalse();
    await Assert.That(firstEvidence.ActiveContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
    await Assert.That(secondEvidence.IsCurrentPositionEvidence).IsTrue();
    await Assert.That(secondEvidence.ActiveContentEpisodeIds).IsEmpty();
    AssertActiveEvidenceInvariant(evidence);
  }

  [Test]
  public async Task NumberedCurrentLogTracksActiveUuidAssociationAcrossPositionAndCorrections()
  {
    var associatedEpisode = Guid.NewGuid();
    var unrelatedEpisode = Guid.NewGuid();
    var associationId = Guid.NewGuid();
    var replacementId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    var associatedSnapshot = repository.RecordBasketContentSnapshot(
      [],
      new BasketLogIdentity.ContentEpisode { ContentEpisodeId = associatedEpisode },
      time
    );
    var unrelatedSnapshot = repository.RecordBasketContentSnapshot(
      [],
      new BasketLogIdentity.ContentEpisode { ContentEpisodeId = unrelatedEpisode },
      time
    );
    repository.RecordBasketObservation(
      associationId,
      4,
      RobotZone(1),
      [associatedEpisode],
      OperatorSource(),
      time.AddMinutes(1)
    );
    repository.RecordBasketObservation(
      Guid.NewGuid(),
      4,
      Storage(),
      [],
      OperatorSource(),
      time.AddMinutes(2)
    );

    var basketFour = repository.CurrentBasketLog(
      new BasketLogIdentity.NumberedBasket { BasketId = 4 }
    );
    await Assert
      .That(basketFour.Select(entry => entry.Counter))
      .Contains(associatedSnapshot.Counter);
    await Assert
      .That(basketFour.Select(entry => entry.Counter))
      .DoesNotContain(unrelatedSnapshot.Counter);

    repository.CorrectBasketObservation(
      Guid.NewGuid(),
      associationId,
      new BasketObservationReplacement
      {
        ObservationId = replacementId,
        BasketId = 5,
        Position = RobotZone(1),
        ContentEpisodeIds = [associatedEpisode],
        Source = OperatorSource("recovery"),
      },
      OperatorSource("recovery"),
      time.AddMinutes(3),
      "The basket label was 5, not 4."
    );

    await Assert
      .That(
        repository
          .CurrentBasketLog(new BasketLogIdentity.NumberedBasket { BasketId = 4 })
          .Any(entry => entry.BasketContentEpisodeId == associatedEpisode)
      )
      .IsFalse();
    await Assert
      .That(
        repository
          .CurrentBasketLog(new BasketLogIdentity.NumberedBasket { BasketId = 5 })
          .Select(entry => entry.Counter)
      )
      .Contains(associatedSnapshot.Counter);

    repository.CorrectBasketObservation(
      Guid.NewGuid(),
      replacementId,
      replacement: null,
      OperatorSource("recovery"),
      time.AddMinutes(4),
      "Retracted after a second inspection."
    );

    await Assert
      .That(
        repository
          .CurrentBasketLog(new BasketLogIdentity.NumberedBasket { BasketId = 5 })
          .Any(entry => entry.BasketContentEpisodeId == associatedEpisode)
      )
      .IsFalse();
    await Assert
      .That(repository.GetUnresolvedOpenBasketContentEpisodeIds())
      .IsEquivalentTo([associatedEpisode, unrelatedEpisode]);
  }

  [Test]
  public async Task LaterLocationEvidenceSupersedesChronologicallyWithoutCorrection()
  {
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    var storage = repository.RecordBasketObservation(
      Guid.NewGuid(),
      4,
      Storage(),
      [],
      OperatorSource(),
      time
    );
    var loadStation = repository.RecordBasketObservation(
      Guid.NewGuid(),
      4,
      LoadStation(),
      [],
      OperatorSource("movement-completion"),
      time.AddHours(1)
    );

    var evidence = repository.GetActiveBasketObservationEvidence(4);
    await Assert.That(evidence).Count().IsEqualTo(1);
    await Assert.That(evidence.Single().Observation).IsEqualTo(loadStation);
    await Assert
      .That(repository.GetBasketObservation(storage.ObservationId))
      .IsEquivalentTo(storage);
    await Assert.That(repository.GetBasketObservationCorrections()).IsEmpty();
  }

  [Test]
  public async Task RetractingOlderContentObservationLeavesLaterPositionEvidence()
  {
    var contentEpisodeId = Guid.NewGuid();
    var targetId = Guid.NewGuid();
    var correctionId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisode(repository, contentEpisodeId, time);
    var target = repository.RecordBasketObservation(
      targetId,
      4,
      RobotZone(1),
      [contentEpisodeId],
      OperatorSource(),
      time.AddMinutes(1)
    );
    var laterPosition = repository.RecordBasketObservation(
      Guid.NewGuid(),
      4,
      Storage(),
      [],
      OperatorSource(),
      time.AddMinutes(2)
    );

    repository.CorrectBasketObservation(
      correctionId,
      targetId,
      replacement: null,
      OperatorSource("recovery"),
      time.AddMinutes(3),
      "The visible occupant was not basket 4."
    );

    var evidence = repository.GetActiveBasketObservationEvidence(4);
    await Assert.That(evidence).Count().IsEqualTo(1);
    await Assert.That(evidence.Single().Observation).IsEqualTo(laterPosition);
    await Assert.That(repository.GetBasketObservation(targetId)).IsEquivalentTo(target);
    await Assert
      .That(repository.GetUnresolvedOpenBasketContentEpisodeIds())
      .IsEquivalentTo([contentEpisodeId]);
  }

  [Test]
  public async Task RetractingNewerLocationObservationRestoresOlderPositionAndContent()
  {
    var contentEpisodeId = Guid.NewGuid();
    var targetId = Guid.NewGuid();
    var correctionId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisode(repository, contentEpisodeId, time);
    var older = repository.RecordBasketObservation(
      Guid.NewGuid(),
      4,
      RobotZone(1),
      [contentEpisodeId],
      OperatorSource(),
      time.AddMinutes(1)
    );
    repository.RecordBasketObservation(
      targetId,
      4,
      Storage(),
      [],
      OperatorSource(),
      time.AddMinutes(2)
    );

    repository.CorrectBasketObservation(
      correctionId,
      targetId,
      replacement: null,
      OperatorSource("recovery"),
      time.AddMinutes(3),
      "The storage observation was not trustworthy."
    );

    var evidence = repository.GetActiveBasketObservationEvidence(4);
    await Assert.That(evidence).Count().IsEqualTo(1);
    await Assert.That(evidence.Single().Observation).IsEquivalentTo(older);
    await Assert.That(evidence.Single().IsCurrentPositionEvidence).IsTrue();
    await Assert.That(evidence.Single().ActiveContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
    await Assert.That(repository.GetUnresolvedOpenBasketContentEpisodeIds()).IsEmpty();
  }

  [Test]
  public async Task CorrectingAnObservationRemovesItWhenItOwnsNoActiveClaim()
  {
    var targetId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketObservation(targetId, 2, RobotZone(1), [], OperatorSource(), time);

    repository.CorrectBasketObservation(
      Guid.NewGuid(),
      targetId,
      replacement: null,
      OperatorSource("recovery"),
      time.AddMinutes(1),
      "The visible number was read incorrectly."
    );

    await Assert.That(repository.GetActiveBasketObservationEvidence()).IsEmpty();
    await Assert.That(repository.GetBasketObservation(targetId)).IsNotNull();
  }

  [Test]
  public async Task CorrectionReplacementIsAtomicAndRetriesIdentically()
  {
    var contentEpisodeId = Guid.NewGuid();
    var targetId = Guid.NewGuid();
    var replacementId = Guid.NewGuid();
    var correctionId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    var replacement = new BasketObservationReplacement
    {
      ObservationId = replacementId,
      BasketId = 4,
      Position = RobotZone(1),
      ContentEpisodeIds = [contentEpisodeId],
      Source = OperatorSource("recovery"),
    };
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisode(repository, contentEpisodeId, time);
    repository.RecordBasketObservation(
      targetId,
      2,
      RobotZone(1),
      [contentEpisodeId],
      OperatorSource(),
      time.AddMinutes(1)
    );

    var first = repository.CorrectBasketObservation(
      correctionId,
      targetId,
      replacement,
      OperatorSource("recovery"),
      time.AddMinutes(2),
      "Actually basket 4"
    );
    var retry = repository.CorrectBasketObservation(
      correctionId,
      targetId,
      replacement,
      OperatorSource("recovery"),
      time.AddMinutes(2),
      "Actually basket 4"
    );

    await Assert.That(retry.Correction).IsEqualTo(first.Correction);
    await Assert.That(retry.Replacement).IsEquivalentTo(first.Replacement);
    await Assert.That(first.Replacement!.BasketId).IsEqualTo(4);
    await Assert.That(first.Replacement.ContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
    var evidence = repository.GetActiveBasketObservationEvidence().Single();
    await Assert.That(evidence.Observation).IsEquivalentTo(first.Replacement);
    await Assert.That(evidence.IsCurrentPositionEvidence).IsTrue();
    await Assert.That(evidence.ActiveContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
    await Assert
      .That(repository.GetRecentLog(0).Select(entry => entry.LogType))
      .IsEquivalentTo([
        LogType.BasketContentSnapshot,
        LogType.BasketObservation,
        LogType.BasketObservationCorrection,
        LogType.BasketObservation,
      ]);
  }

  [Test]
  public async Task RecordRetryIsIdempotentAndChangedArgumentsConflict()
  {
    var observationId = Guid.NewGuid();
    var time = DateTime.UtcNow;
    using var repository = _repositoryConfig.OpenConnection();
    var first = repository.RecordBasketObservation(
      observationId,
      4,
      Storage(),
      [],
      OperatorSource(),
      time,
      new EventLogMetadata { ForeignId = "first" },
      "recorded"
    );
    var retry = repository.RecordBasketObservation(
      observationId,
      4,
      Storage(),
      [],
      OperatorSource(),
      time.AddHours(1),
      new EventLogMetadata { ForeignId = "retry" },
      "recorded"
    );

    await Assert.That(retry).IsEqualTo(first);
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(1);
    await Assert.ThrowsAsync<ConflictRequestException>(() =>
      Task.Run(() =>
        repository.RecordBasketObservation(
          observationId,
          4,
          LoadStation(),
          [],
          OperatorSource(),
          time,
          note: "changed"
        )
      )
    );
  }

  [Test]
  public async Task RestartRoundTripsHistoricalAndActiveEvidence()
  {
    var databaseId = Guid.NewGuid();
    var observationId = Guid.NewGuid();
    var contentEpisodeId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);

    using var config = RepositoryConfig.InitializeMemoryDB(null, databaseId);
    using (var repository = config.OpenConnection())
    {
      OpenEpisode(repository, contentEpisodeId, time);
      repository.RecordBasketObservation(
        observationId,
        4,
        RobotZone(1),
        [contentEpisodeId],
        IntegrationSource(),
        time.AddMinutes(1),
        new EventLogMetadata { CorrelationId = "restart-correlation" },
        "restart note"
      );
    }

    using var restartedConfig = RepositoryConfig.InitializeMemoryDB(
      null,
      databaseId,
      createTables: false
    );
    using var restarted = restartedConfig.OpenConnection();
    var observation = restarted.GetBasketObservation(observationId);
    var evidence = restarted.GetActiveBasketObservationEvidence(4).Single();
    await Assert.That(observation).IsNotNull();
    await Assert.That(observation!.ContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
    await Assert.That(observation.CorrelationId).IsEqualTo("restart-correlation");
    await Assert.That(observation.Note).IsEqualTo("restart note");
    await Assert.That(evidence.Observation).IsEquivalentTo(observation);
    await Assert.That(evidence.IsCurrentPositionEvidence).IsTrue();
    await Assert.That(evidence.ActiveContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
  }

  [Test]
  public async Task ContentEpisodeCannotBeActiveForTwoBaskets()
  {
    var contentEpisodeId = Guid.NewGuid();
    var time = DateTime.UtcNow;
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisode(repository, contentEpisodeId, time);
    repository.RecordBasketObservation(
      Guid.NewGuid(),
      4,
      Storage(),
      [contentEpisodeId],
      IntegrationSource(),
      time.AddMinutes(1)
    );

    await Assert.ThrowsAsync<ConflictRequestException>(() =>
      Task.Run(() =>
        repository.RecordBasketObservation(
          Guid.NewGuid(),
          6,
          Storage(),
          [contentEpisodeId],
          IntegrationSource(),
          time.AddMinutes(2)
        )
      )
    );
    var evidence = repository.GetActiveBasketObservationEvidence().Single();
    await Assert.That(evidence.Observation.BasketId).IsEqualTo(4);
    await Assert.That(evidence.ActiveContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
  }

  [Test]
  public async Task ObservationAndActiveProjectionRollBackTogether()
  {
    var databaseId = Guid.NewGuid();
    using var config = RepositoryConfig.InitializeMemoryDB(null, databaseId);
    using (
      var connection = new SqliteConnection(
        $"Data Source=file:${databaseId}?mode=memory&cache=shared"
      )
    )
    {
      connection.Open();
      using var trigger = connection.CreateCommand();
      trigger.CommandText =
        "CREATE TRIGGER fail_basket_observation_projection BEFORE INSERT ON current_basket_observation_episodes BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      trigger.ExecuteNonQuery();
    }

    using var repository = config.OpenConnection();
    var contentEpisodeId = Guid.NewGuid();
    OpenEpisode(repository, contentEpisodeId, DateTime.UtcNow);
    await Assert.ThrowsAsync<SqliteException>(() =>
      Task.Run(() =>
        repository.RecordBasketObservation(
          Guid.NewGuid(),
          4,
          Storage(),
          [contentEpisodeId],
          IntegrationSource(),
          DateTime.UtcNow
        )
      )
    );
    await Assert.That(repository.GetRecentLog(0)).Count().IsEqualTo(1);
    await Assert
      .That(repository.GetRecentLog(0).Single().LogType)
      .IsEqualTo(LogType.BasketContentSnapshot);
    await Assert.That(repository.GetActiveBasketObservationEvidence()).IsEmpty();
    await Assert.That(repository.GetBasketObservationCorrections()).IsEmpty();
  }

  [Test]
  public async Task CorrectionAndReplacementRollBackTogether()
  {
    var databaseId = Guid.NewGuid();
    using var config = RepositoryConfig.InitializeMemoryDB(null, databaseId);
    var targetId = Guid.NewGuid();
    var replacementId = Guid.NewGuid();
    var correctionId = Guid.NewGuid();
    using (var repository = config.OpenConnection())
    {
      repository.RecordBasketObservation(
        targetId,
        4,
        Storage(),
        [],
        IntegrationSource(),
        DateTime.UtcNow
      );
    }
    using (
      var connection = new SqliteConnection(
        $"Data Source=file:${databaseId}?mode=memory&cache=shared"
      )
    )
    {
      connection.Open();
      using var trigger = connection.CreateCommand();
      trigger.CommandText =
        "CREATE TRIGGER fail_basket_observation_replacement BEFORE INSERT ON basket_observations WHEN NEW.ObservationId = '"
        + replacementId.ToString("D")
        + "' BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      trigger.ExecuteNonQuery();
    }

    using var repositoryAfterFailure = config.OpenConnection();
    await Assert.ThrowsAsync<SqliteException>(() =>
      Task.Run(() =>
        repositoryAfterFailure.CorrectBasketObservation(
          correctionId,
          targetId,
          new BasketObservationReplacement
          {
            ObservationId = replacementId,
            BasketId = 4,
            Position = LoadStation(),
            ContentEpisodeIds = [],
            Source = IntegrationSource(),
          },
          IntegrationSource(),
          DateTime.UtcNow,
          "replacement"
        )
      )
    );
    await Assert.That(repositoryAfterFailure.GetBasketObservationCorrections()).IsEmpty();
    await Assert.That(repositoryAfterFailure.GetBasketObservation(replacementId)).IsNull();
    var evidence = repositoryAfterFailure.GetActiveBasketObservationEvidence(4).Single();
    await Assert.That(evidence.Observation.ObservationId).IsEqualTo(targetId);
    await Assert.That(evidence.IsCurrentPositionEvidence).IsTrue();
    await Assert.That(evidence.ActiveContentEpisodeIds).IsEmpty();
  }

  [Test]
  public async Task CorrectionRetryWithChangedArgumentsConflicts()
  {
    var targetId = Guid.NewGuid();
    var correctionId = Guid.NewGuid();
    var time = DateTime.UtcNow;
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketObservation(targetId, 4, Storage(), [], IntegrationSource(), time);
    repository.CorrectBasketObservation(
      correctionId,
      targetId,
      replacement: null,
      IntegrationSource(),
      time.AddMinutes(1),
      "retracted"
    );

    await Assert.ThrowsAsync<ConflictRequestException>(() =>
      Task.Run(() =>
        repository.CorrectBasketObservation(
          correctionId,
          targetId,
          replacement: null,
          OperatorSource(),
          time.AddMinutes(2),
          "changed"
        )
      )
    );
  }

  [Test]
  public async Task PositionEvidenceMarkerSurvivesObservationRetraction()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var observation = repository.RecordBasketObservation(
      Guid.NewGuid(),
      4,
      Storage(),
      [],
      OperatorSource(),
      DateTime.UtcNow
    );
    repository.CorrectBasketObservation(
      Guid.NewGuid(),
      observation.ObservationId,
      replacement: null,
      OperatorSource(),
      DateTime.UtcNow,
      "retracted"
    );

    await Assert.That(repository.GetActiveBasketObservationEvidence(4)).IsEmpty();
    await Assert
      .That(repository.GetBasketPositionEvidenceSeen([4]))
      .IsEquivalentTo(ImmutableDictionary<int, long>.Empty.Add(4, observation.EventCounter));
  }

  private static void AssertActiveEvidenceInvariant(
    ImmutableList<ActiveBasketObservationEvidence> evidence
  )
  {
    foreach (var item in evidence)
      if (!item.IsCurrentPositionEvidence && item.ActiveContentEpisodeIds.IsEmpty)
        throw new InvalidOperationException("Inactive basket observation has no active evidence.");
  }

  private static void OpenEpisode(
    IRepository repository,
    Guid contentEpisodeId,
    DateTime timeUTC
  ) =>
    repository.RecordBasketContentSnapshot(
      [],
      new BasketLogIdentity.ContentEpisode { ContentEpisodeId = contentEpisodeId },
      timeUTC
    );

  private static BasketEvidenceSource OperatorSource(string name = "operator") =>
    new() { Kind = BasketEvidenceSourceKind.Operator, Name = name };

  private static BasketEvidenceSource IntegrationSource() =>
    new() { Kind = BasketEvidenceSourceKind.Integration, Name = "integration" };

  private static BasketPosition Storage() =>
    new()
    {
      Location = BasketLocationEnum.Storage,
      LocationNum = 1,
      LocationTitle = "Basket storage",
    };

  private static BasketPosition RobotZone(int zone) =>
    new()
    {
      Location = BasketLocationEnum.LoadStationStaging,
      LocationNum = 1,
      Zone = zone,
      LocationTitle = "Robot staging",
    };

  private static BasketPosition LoadStation() =>
    new()
    {
      Location = BasketLocationEnum.LoadUnload,
      LocationNum = 2,
      LocationTitle = "Basket load station",
    };
}
