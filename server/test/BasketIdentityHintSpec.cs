using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

#pragma warning disable TUnit0018 // Restart coverage intentionally replaces the repository config.
public sealed class BasketIdentityHintSpec : IDisposable
{
  private readonly string _databaseFile = Path.Combine(
    Path.GetTempPath(),
    Guid.NewGuid().ToString("N") + ".db"
  );
  private RepositoryConfig _repositoryConfig;

  public BasketIdentityHintSpec()
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
  public async Task UnresolvedBasketFragmentsExcludeHintedIds()
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
