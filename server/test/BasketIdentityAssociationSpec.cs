using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

#pragma warning disable TUnit0018 // Restart coverage intentionally replaces the repository config.
public sealed class BasketIdentityAssociationSpec : IDisposable
{
  private readonly string _databaseFile = Path.Combine(
    Path.GetTempPath(),
    Guid.NewGuid().ToString("N") + ".db"
  );
  private RepositoryConfig _repositoryConfig;

  public BasketIdentityAssociationSpec()
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
  public async Task AssociationRequiresOpenContentEpisodes()
  {
    using var repository = _repositoryConfig.OpenConnection();
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketIdentityAssociation(
        Guid.NewGuid(),
        4,
        [Guid.NewGuid()],
        BasketIdentityAssociationBasis.DirectObservation,
        OperatorSource(),
        DateTime.UtcNow
      )
    );
    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
    await Assert.That(repository.GetCurrentBasketIdentityAssociations()).IsEmpty();
  }

  [Test]
  public async Task AssociatesSeveralEpisodesAtomicallyAndSurvivesRestart()
  {
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    var associationId = Guid.NewGuid();
    var correlationId = Guid.NewGuid();
    var time = DateTime.UtcNow;
    using (var repository = _repositoryConfig.OpenConnection())
    {
      OpenEpisodes(repository, first, second);
      var association = repository.RecordBasketIdentityAssociation(
        associationId,
        5,
        [first, second],
        BasketIdentityAssociationBasis.DirectObservation,
        OperatorSource("storage-survey"),
        time,
        new BasketPosition
        {
          Location = BasketLocationEnum.Storage,
          LocationNum = 3,
          LocationTitle = " Storage ",
        },
        metadata: new EventLogMetadata { CorrelationId = correlationId.ToString("D") },
        note: "operator note"
      );

      await Assert.That(association.ContentEpisodeIds).IsEquivalentTo([first, second]);
      await Assert.That(association.ObservedPosition!.LocationTitle).IsEqualTo("Storage");
      await Assert.That(association.CorrelationId).IsEqualTo(correlationId.ToString("D"));
      await Assert.That(repository.GetUnresolvedOpenBasketContentEpisodeIds()).IsEmpty();
      await Assert
        .That(
          repository
            .CurrentBasketLog(new ContainerIdentity.Numbered { ContainerNum = 5 })
            .Where(entry => entry.LogType == LogType.BasketContentSnapshot)
            .Select(entry => entry.ContainerId)
        )
        .IsEquivalentTo(new Guid?[] { first, second });
    }

    _repositoryConfig.Dispose();
    _repositoryConfig = RepositoryConfig.InitializeEventDatabase(
      null,
      _databaseFile,
      pooling: false
    );
    using var restarted = _repositoryConfig.OpenConnection();
    var restored = restarted.GetCurrentBasketIdentityAssociations(5).Single();
    await Assert.That(restored.AssociationId).IsEqualTo(associationId);
    await Assert.That(restored.ContentEpisodeIds).IsEquivalentTo([first, second]);
    await Assert.That(restored.Source).IsEqualTo(OperatorSource("storage-survey"));
    await Assert.That(restored.Note).IsEqualTo("operator note");
    await AssertThrows<ConflictRequestException>(() =>
      restarted.RecordBasketIdentityAssociation(
        associationId,
        5,
        [first, second],
        BasketIdentityAssociationBasis.DirectObservation,
        OperatorSource("storage-survey"),
        time.AddDays(1),
        new BasketPosition
        {
          Location = BasketLocationEnum.Storage,
          LocationNum = 3,
          LocationTitle = "Storage",
        },
        metadata: new EventLogMetadata { CorrelationId = correlationId.ToString("D") },
        note: "changed note"
      )
    );
  }

  [Test]
  public async Task ExactRetryReturnsOriginalAndChangedRetryConflicts()
  {
    var episodeId = Guid.NewGuid();
    var associationId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisodes(repository, episodeId);
    var first = repository.RecordBasketIdentityAssociation(
      associationId,
      4,
      [episodeId],
      BasketIdentityAssociationBasis.DirectObservation,
      SensorSource(),
      DateTime.UtcNow,
      note: "sensor frame",
      metadata: new EventLogMetadata
      {
        ForeignId = "robot-frame-1",
        CorrelationId = "recovery-session-1",
        OriginalMessage = "first request",
      }
    );
    var retry = repository.RecordBasketIdentityAssociation(
      associationId,
      4,
      [episodeId],
      BasketIdentityAssociationBasis.DirectObservation,
      SensorSource(),
      DateTime.UtcNow.AddDays(1),
      note: "sensor frame",
      metadata: new EventLogMetadata
      {
        ForeignId = "robot-frame-2",
        CorrelationId = "recovery-session-2",
        OriginalMessage = "retry request",
      }
    );

    await Assert.That(retry.AssociationId).IsEqualTo(first.AssociationId);
    await Assert.That(retry.EventCounter).IsEqualTo(first.EventCounter);
    await Assert.That(retry.TimeUTC).IsEqualTo(first.TimeUTC);
    await Assert.That(retry.ContentEpisodeIds).IsEquivalentTo(first.ContentEpisodeIds);
    await Assert.That(retry.Note).IsEqualTo("sensor frame");
    await Assert
      .That(
        repository
          .GetRecentLog(0)
          .Count(entry => entry.LogType == LogType.BasketIdentityAssociation)
      )
      .IsEqualTo(1);
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketIdentityAssociation(
        associationId,
        4,
        [episodeId],
        BasketIdentityAssociationBasis.DirectObservation,
        SensorSource(),
        DateTime.UtcNow,
        note: "changed note"
      )
    );
  }

  [Test]
  public async Task EpisodeCanNotBelongToTwoActiveAssociations()
  {
    var episodeId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisodes(repository, episodeId);
    repository.RecordBasketIdentityAssociation(
      Guid.NewGuid(),
      4,
      [episodeId],
      BasketIdentityAssociationBasis.DirectObservation,
      OperatorSource(),
      DateTime.UtcNow
    );

    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketIdentityAssociation(
        Guid.NewGuid(),
        6,
        [episodeId],
        BasketIdentityAssociationBasis.DirectObservation,
        OperatorSource(),
        DateTime.UtcNow
      )
    );
    await Assert.That(repository.GetCurrentBasketIdentityAssociations()).Count().IsEqualTo(1);
  }

  [Test]
  public async Task CorrectionReassignsWholeEpisodeSetAtomically()
  {
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    var targetId = Guid.NewGuid();
    var replacementId = Guid.NewGuid();
    var correctionId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisodes(repository, first, second);
    repository.RecordBasketIdentityAssociation(
      targetId,
      4,
      [first, second],
      BasketIdentityAssociationBasis.DirectObservation,
      OperatorSource(),
      DateTime.UtcNow
    );

    var result = repository.CorrectBasketIdentityAssociation(
      correctionId,
      targetId,
      new BasketIdentityAssociationReplacement
      {
        AssociationId = replacementId,
        BasketId = 6,
        ContentEpisodeIds = [first, second],
        Basis = BasketIdentityAssociationBasis.DirectObservation,
        Source = OperatorSource(),
      },
      OperatorSource(),
      DateTime.UtcNow,
      "wrong visible number"
    );

    await Assert.That(result.Replacement!.AssociationId).IsEqualTo(replacementId);
    await Assert.That(repository.GetCurrentBasketIdentityAssociations(4)).IsEmpty();
    await Assert
      .That(repository.GetCurrentBasketIdentityAssociations(6).Single().ContentEpisodeIds)
      .IsEquivalentTo([first, second]);
    await Assert
      .That(repository.GetBasketIdentityAssociationCorrections(targetId).Single().CorrectionId)
      .IsEqualTo(correctionId);

    var retry = repository.CorrectBasketIdentityAssociation(
      correctionId,
      targetId,
      new BasketIdentityAssociationReplacement
      {
        AssociationId = replacementId,
        BasketId = 6,
        ContentEpisodeIds = [first, second],
        Basis = BasketIdentityAssociationBasis.DirectObservation,
        Source = OperatorSource(),
      },
      OperatorSource(),
      DateTime.UtcNow.AddDays(1),
      "wrong visible number"
    );
    await Assert.That(retry.Correction.CorrectionId).IsEqualTo(result.Correction.CorrectionId);
    await Assert.That(retry.Correction.EventCounter).IsEqualTo(result.Correction.EventCounter);
    await Assert.That(retry.Correction.TimeUTC).IsEqualTo(result.Correction.TimeUTC);
    await Assert.That(retry.Replacement!.EventCounter).IsEqualTo(result.Replacement!.EventCounter);
    await Assert
      .That(retry.Replacement.ContentEpisodeIds)
      .IsEquivalentTo(result.Replacement.ContentEpisodeIds);
  }

  [Test]
  public async Task RetractionReturnsEpisodesToUnresolvedState()
  {
    var episodeId = Guid.NewGuid();
    var associationId = Guid.NewGuid();
    using var repository = _repositoryConfig.OpenConnection();
    OpenEpisodes(repository, episodeId);
    repository.RecordBasketIdentityAssociation(
      associationId,
      4,
      [episodeId],
      BasketIdentityAssociationBasis.DirectObservation,
      OperatorSource(),
      DateTime.UtcNow
    );

    repository.CorrectBasketIdentityAssociation(
      Guid.NewGuid(),
      associationId,
      null,
      OperatorSource(),
      DateTime.UtcNow
    );

    await Assert.That(repository.GetCurrentBasketIdentityAssociations()).IsEmpty();
    await Assert
      .That(repository.GetUnresolvedOpenBasketContentEpisodeIds())
      .IsEquivalentTo([episodeId]);
    await Assert.That(repository.GetBasketIdentityAssociation(associationId)).IsNotNull();
  }

  [Test]
  public async Task AssociationAndProjectionUpdateRollBackTogether()
  {
    var episodeId = Guid.NewGuid();
    using (var setupRepository = _repositoryConfig.OpenConnection())
      OpenEpisodes(setupRepository, episodeId);
    using (var connection = new SqliteConnection("Data Source=" + _databaseFile))
    {
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText =
        "CREATE TRIGGER fail_association_projection BEFORE INSERT ON current_basket_identity_associations BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      command.ExecuteNonQuery();
    }

    using var repository = _repositoryConfig.OpenConnection();
    await AssertThrows<SqliteException>(() =>
      repository.RecordBasketIdentityAssociation(
        Guid.NewGuid(),
        4,
        [episodeId],
        BasketIdentityAssociationBasis.DirectObservation,
        OperatorSource(),
        DateTime.UtcNow
      )
    );
    await Assert
      .That(
        repository.GetRecentLog(0).Count(entry => entry.LogType == LogType.BasketContentSnapshot)
      )
      .IsEqualTo(1);
    await Assert.That(repository.GetCurrentBasketIdentityAssociations()).IsEmpty();
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

  private static BasketEvidenceSource OperatorSource(string sourceObservationId = null) =>
    new() { Kind = BasketEvidenceSourceKind.Operator, Name = "operator" };

  private static BasketEvidenceSource SensorSource() =>
    new() { Kind = BasketEvidenceSourceKind.Sensor, Name = "robot" };

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
