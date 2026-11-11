#nullable enable
using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class MazakRouteProvenanceSpec
{
  private static MazakRouteProvenance Route(int generation = 7) =>
    new()
    {
      FullPartName = "part:" + generation + ":1:2",
      DownloadUid = generation,
      NativeProcess = 1,
      JobUnique = "job",
      JobPartName = "part",
      JobProcess = 2,
      Path = 1,
      NumProcesses = 2,
      NativeProgram = "200.EIA",
      NativeFixture = "fixture:7:1",
      ProgramName = "cut",
      ProgramRevision = 3,
    };

  [Test]
  public async Task NativeIdentityIsImmutableAndSurvivesReopening()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    var route = Route();
    using (var repository = config.OpenConnection())
    {
      repository.RecordMazakRouteProvenance([route]);
      repository.RecordMazakRouteProvenance([route]);
      await Assert
        .That(() =>
          repository.RecordMazakRouteProvenance([route with { JobUnique = "replacement-owner" }])
        )
        .Throws<ConflictRequestException>();
    }
    using var reopened = config.OpenConnection();
    await Assert.That(reopened.LoadMazakRouteProvenance(route.FullPartName, 1)).IsEqualTo(route);
    await Assert.That(reopened.LoadMazakRouteProvenanceForJob("job")).IsEquivalentTo([route]);
    await Assert.That(reopened.LoadMazakRouteProvenance(route.FullPartName, 2)).IsNull();
  }

  [Test]
  public async Task ConflictingBatchRollsBackDefinitionsAndGenerationReservations()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var existing = Route();
    repository.RecordMazakRouteProvenance([existing]);
    var uncommitted = Route(100);
    await Assert
      .That(() =>
        repository.RecordMazakRouteProvenance([
          uncommitted,
          existing with
          {
            NativeProgram = "different.EIA",
          },
        ])
      )
      .Throws<ConflictRequestException>();
    await Assert.That(repository.LoadMazakRouteProvenance(uncommitted.FullPartName, 1)).IsNull();
    await Assert.That(repository.ReserveMazakDownloadUid([])).IsEqualTo(8);
  }

  [Test]
  public async Task ReservedGenerationsAreNotReusedAfterInterruptedDownloadsOrReopening()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using (var first = config.OpenConnection())
    {
      await Assert.That(first.ReserveMazakDownloadUid([0, 1])).IsEqualTo(2);
      // No native definition was written, but an uncertain attempt still owns its generation.
    }
    using var second = config.OpenConnection();
    await Assert.That(second.ReserveMazakDownloadUid([3])).IsEqualTo(4);
    second.RecordMazakRouteProvenance([Route(9)]);
    await Assert.That(second.ReserveMazakDownloadUid([])).IsEqualTo(10);
  }

  [Test]
  public async Task Version46UpgradeAddsOnlyProvenanceAndPreservesHistory()
  {
    var id = Guid.NewGuid();
    using var original = RepositoryConfig.InitializeMemoryDB(null, id);
    using (var repository = original.OpenConnection())
      repository.RecordGeneralMessage([], "existing", "history", foreignId: "LG001");
    using var sql = new SqliteConnection($"Data Source=file:${id}?mode=memory&cache=shared");
    sql.Open();
    using (var command = sql.CreateCommand())
    {
      command.CommandText =
        "DROP TABLE mazak_route_provenance; DROP TABLE mazak_download_uids; UPDATE version SET ver = 46";
      command.ExecuteNonQuery();
    }
    using var upgraded = RepositoryConfig.InitializeMemoryDB(
      null,
      id,
      createTables: false,
      upgradeTables: true
    );
    using var reopened = upgraded.OpenConnection();
    await Assert.That(reopened.MostRecentLogEntryForForeignID("LG001").Result).IsEqualTo("history");
    reopened.RecordMazakRouteProvenance([Route()]);
    await Assert.That(reopened.LoadMazakRouteProvenanceForJob("job").Count).IsEqualTo(1);
    SchemaUpgradeSpec.Check(sql);
  }
}
