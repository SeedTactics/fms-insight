/* Copyright (c) 2026, John Lenz

All rights reserved.
*/

using System;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

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
    await Assert.That(observation.BasketId).IsEqualTo(4);
    await Assert.That(observation.Position).IsEqualTo(Storage());
    await Assert.That(observation.ContentEpisodeIds).IsEmpty();
    await Assert.That(observation.Source).IsEqualTo(OperatorSource());
    await Assert.That(observation.Note).IsEqualTo("Visible during complete storage survey");
    await Assert.That(observation.CorrelationId).IsEqualTo("recovery-1");
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
    repository.RecordBasketContentSnapshot(
      [],
      new BasketLogIdentity.ContentEpisode { ContentEpisodeId = contentEpisodeId },
      time
    );
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

    await Assert.That(moved.ContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
    await Assert.That(repository.GetCurrentBasketObservations(4)).IsEquivalentTo([moved]);
    await Assert.That(repository.GetUnresolvedOpenBasketContentEpisodeIds()).IsEmpty();
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

    await Assert.That(repository.GetCurrentBasketObservations(4)).IsEquivalentTo([loadStation]);
    await Assert.That(repository.GetBasketObservation(storage.ObservationId)).IsEqualTo(storage);
    await Assert.That(repository.GetBasketObservationCorrections()).IsEmpty();
  }

  [Test]
  public async Task CorrectionRetractsBadIdentityAndLeavesContentAnonymous()
  {
    var contentEpisodeId = Guid.NewGuid();
    var targetId = Guid.NewGuid();
    var correctionId = Guid.NewGuid();
    var time = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
    using var repository = _repositoryConfig.OpenConnection();
    repository.RecordBasketContentSnapshot(
      [],
      new BasketLogIdentity.ContentEpisode { ContentEpisodeId = contentEpisodeId },
      time
    );
    repository.RecordBasketObservation(
      targetId,
      2,
      RobotZone(1),
      [contentEpisodeId],
      OperatorSource(),
      time.AddMinutes(1)
    );

    var result = repository.CorrectBasketObservation(
      correctionId,
      targetId,
      replacement: null,
      OperatorSource("recovery"),
      time.AddMinutes(2),
      "The visible number was read incorrectly."
    );

    await Assert.That(result.Replacement).IsNull();
    await Assert.That(result.Correction.TargetObservationId).IsEqualTo(targetId);
    await Assert.That(repository.GetCurrentBasketObservations()).IsEmpty();
    await Assert
      .That(repository.GetUnresolvedOpenBasketContentEpisodeIds())
      .IsEquivalentTo([contentEpisodeId]);
    await Assert.That(repository.GetBasketObservation(targetId)).IsNotNull();
  }

  [Test]
  public async Task CorrectionAtomicallyReplacesTheCompleteClaimAndRetries()
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
    repository.RecordBasketContentSnapshot(
      [],
      new BasketLogIdentity.ContentEpisode { ContentEpisodeId = contentEpisodeId },
      time
    );
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
    await Assert.That(retry.Replacement!.ObservationId).IsEqualTo(first.Replacement!.ObservationId);
    await Assert
      .That(retry.Replacement.ContentEpisodeIds)
      .IsEquivalentTo(first.Replacement.ContentEpisodeIds);
    await Assert.That(first.Replacement!.BasketId).IsEqualTo(4);
    await Assert.That(first.Replacement.ContentEpisodeIds).IsEquivalentTo([contentEpisodeId]);
    await Assert
      .That(repository.GetCurrentBasketObservations())
      .IsEquivalentTo([first.Replacement]);
    await Assert
      .That(repository.GetRecentLog(0).Select(entry => entry.LogType))
      .IsEquivalentTo([
        LogType.BasketContentSnapshot,
        LogType.BasketObservation,
        LogType.BasketObservationCorrection,
        LogType.BasketObservation,
      ]);
  }

  private static BasketEvidenceSource OperatorSource(string name = "operator") =>
    new() { Kind = BasketEvidenceSourceKind.Operator, Name = name };

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
