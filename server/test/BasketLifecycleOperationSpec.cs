using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class BasketLifecycleOperationSpec : IDisposable
{
  private readonly RepositoryConfig _repositoryConfig = RepositoryConfig.InitializeMemoryDB(null);

  public void Dispose() => _repositoryConfig.Dispose();

  [Test]
  public async Task RequiresBoundaryAndLeavesStandaloneObservationApiUnchanged()
  {
    using var repository = _repositoryConfig.OpenConnection();

    await AssertThrows<ArgumentException>(() =>
      repository.RecordBasketLifecycleOperation(
        new BasketLifecycleOperation { CycleBoundaries = [] },
        1,
        DateTime.UtcNow,
        "observation-only"
      )
    );

    var observation = repository.RecordBasketObservation(
      Guid.NewGuid(),
      4,
      RobotZone(),
      [],
      Source(),
      DateTime.UtcNow
    );
    await Assert.That(observation.BasketId).IsEqualTo(4);
  }

  [Test]
  public async Task BoundariesAndStationWorkDoNotInventObservations()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var firstEpisode = Guid.NewGuid();
    var secondEpisode = Guid.NewGuid();
    var firstMaterial = Material(repository, 1);
    var secondMaterial = Material(repository, 2);

    repository.RecordBasketLifecycleOperation(
      new BasketLifecycleOperation { CycleBoundaries = [Start(firstEpisode, firstMaterial)] },
      1,
      DateTime.UtcNow,
      "boundary-only"
    );
    repository.RecordBasketStationOperation(
      new BasketStationOperation
      {
        Transfers = [],
        CycleBoundaries = [Start(secondEpisode, secondMaterial)],
      },
      2,
      TimeSpan.Zero,
      DateTime.UtcNow,
      ImmutableDictionary<string, string>.Empty,
      "station-boundary-only"
    );

    await Assert.That(repository.GetActiveBasketObservationEvidence()).IsEmpty();
    await Assert
      .That(repository.GetRecentLog(0).Count(entry => entry.LogType == LogType.BasketCycle))
      .IsEqualTo(2);
  }

  [Test]
  public async Task StartsEpisodeAndObservesItAtomicallyAgainstResultingState()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var episode = Guid.NewGuid();
    var observationId = Guid.NewGuid();
    var material = Material(repository, 1);
    var operation = Operation(episode, observationId, basketId: 4, material);

    var first = repository
      .RecordBasketLifecycleOperation(operation, 1, DateTime.UtcNow, "atomic-start")
      .ToImmutableList();
    var retry = repository
      .RecordBasketLifecycleOperation(operation, 1, DateTime.UtcNow.AddHours(1), "atomic-start")
      .ToImmutableList();

    await Assert
      .That(first.Select(log => log.Counter))
      .IsEquivalentTo(retry.Select(log => log.Counter));
    await Assert
      .That(repository.GetActiveBasketObservationEvidence(4).Single().ActiveContentEpisodeIds)
      .IsEquivalentTo([episode]);
    await Assert.That(first.Select(log => log.LogType)).Contains(LogType.BasketCycle);
    await Assert.That(first.Select(log => log.LogType)).Contains(LogType.BasketObservation);
  }

  [Test]
  public async Task RejectsEndedEpisodeObservationWithoutDurableChurn()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var episode = Guid.NewGuid();
    var material = Material(repository, 1);
    repository.RecordBasketLifecycleOperation(
      new BasketLifecycleOperation { CycleBoundaries = [Start(episode, material)] },
      1,
      DateTime.UtcNow,
      "seed-ended-episode"
    );
    var before = repository.GetRecentLog(0).Select(log => log.Counter).ToImmutableList();

    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketLifecycleOperation(
        new BasketLifecycleOperation
        {
          CycleBoundaries =
          [
            new BasketCycleBoundary.End
            {
              BasketIdentity = new BasketLogIdentity.NumberedBasket { BasketId = 4 },
              Material = [material],
              ReconciledBasketIdentities = [episode],
            },
          ],
          Observations = [Observation(Guid.NewGuid(), 4, episode)],
        },
        1,
        DateTime.UtcNow,
        "observe-ended-episode"
      )
    );

    await Assert.That(repository.GetRecentLog(0).Select(log => log.Counter)).IsEquivalentTo(before);
    await Assert.That(repository.GetUnresolvedOpenBasketContentEpisodeIds()).Contains(episode);
  }

  [Test]
  public async Task RejectsTwoBasketClaimsAndRollsBackTheStart()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var episode = Guid.NewGuid();
    var material = Material(repository, 1);

    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketLifecycleOperation(
        new BasketLifecycleOperation
        {
          CycleBoundaries = [Start(episode, material)],
          Observations =
          [
            Observation(Guid.NewGuid(), 4, episode),
            Observation(Guid.NewGuid(), 5, episode),
          ],
        },
        1,
        DateTime.UtcNow,
        "two-basket-claims"
      )
    );

    await Assert.That(repository.GetRecentLog(0)).IsEmpty();
    await Assert.That(repository.GetUnresolvedOpenBasketContentEpisodeIds()).IsEmpty();
  }

  [Test]
  public async Task ChangedBoundaryOrObservationConflictsWithOperationKey()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var episode = Guid.NewGuid();
    var material = Material(repository, 1);
    var operation = Operation(episode, Guid.NewGuid(), basketId: 4, material);
    repository.RecordBasketLifecycleOperation(operation, 1, DateTime.UtcNow, "semantic-retry");

    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketLifecycleOperation(
        operation with
        {
          Observations = [operation.Observations.Single() with { ObservationId = Guid.NewGuid() }],
        },
        1,
        DateTime.UtcNow,
        "semantic-retry"
      )
    );
    await AssertThrows<ConflictRequestException>(() =>
      repository.RecordBasketLifecycleOperation(
        operation with
        {
          CycleBoundaries = [Start(episode, material with { Face = 2 })],
        },
        1,
        DateTime.UtcNow,
        "semantic-retry"
      )
    );
  }

  [Test]
  public async Task IdempotencyIgnoresRecoveryCorrelationIdAndReturnsOriginalMetadata()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var episode = Guid.NewGuid();
    var operation = Operation(episode, Guid.NewGuid(), basketId: 4, Material(repository, 1));

    var first = repository
      .RecordBasketLifecycleOperation(operation, 1, DateTime.UtcNow, "correlation-idempotency")
      .ToImmutableList();
    var retry = repository
      .RecordBasketLifecycleOperation(
        operation,
        1,
        DateTime.UtcNow.AddHours(1),
        "correlation-idempotency",
        metadata: new EventLogMetadata { CorrelationId = "recovery-session" }
      )
      .ToImmutableList();

    await Assert
      .That(retry.Select(log => log.Counter))
      .IsEquivalentTo(first.Select(log => log.Counter));
    await Assert.That(retry.All(log => log.CorrelationId is null)).IsTrue();
  }

  private static BasketLifecycleOperation Operation(
    Guid episode,
    Guid observationId,
    int basketId,
    EventLogMaterial material
  ) =>
    new()
    {
      CycleBoundaries = [Start(episode, material)],
      Observations = [Observation(observationId, basketId, episode)],
    };

  private static BasketCycleBoundary.Start Start(Guid episode, EventLogMaterial material) =>
    new()
    {
      BasketIdentity = new BasketLogIdentity.ContentEpisode { ContentEpisodeId = episode },
      Material = [material],
    };

  private static BasketObservationInput Observation(
    Guid observationId,
    int basketId,
    Guid episode
  ) =>
    new()
    {
      ObservationId = observationId,
      BasketId = basketId,
      Position = RobotZone(),
      ContentEpisodeIds = [episode],
      Source = Source(),
      Note = "Directly observed continuity",
    };

  private static EventLogMaterial Material(IRepository repository, int face)
  {
    var materialId = repository.AllocateMaterialID("job", "part", 1);
    return new EventLogMaterial
    {
      MaterialID = materialId,
      Process = 1,
      Face = face,
    };
  }

  private static BasketPosition RobotZone() =>
    new()
    {
      Location = BasketLocationEnum.LoadStationStaging,
      LocationNum = 1,
      Zone = 1,
      LocationTitle = "Robot staging zone 1",
    };

  private static BasketEvidenceSource Source() =>
    new() { Kind = BasketEvidenceSourceKind.Integration, Name = "DirectTestEvidence" };

  private static async Task AssertThrows<TException>(Action action)
    where TException : Exception
  {
    var exception = Assert.Throws<TException>(action);
    await Assert.That(exception).IsNotNull();
  }
}
