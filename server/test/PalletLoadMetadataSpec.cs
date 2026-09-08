using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class PalletLoadMetadataSpec
{
  [Test]
  [Arguments("carrier-a")]
  [Arguments("")]
  [Arguments(null)]
  public async Task LoadMetadataSurvivesDatabaseRestart(string value)
  {
    var file = System.IO.Path.Combine(
      System.IO.Path.GetTempPath(),
      Guid.NewGuid().ToString("N") + ".db"
    );
    try
    {
      using (var config = RepositoryConfig.InitializeEventDatabase(null, file, pooling: false))
      using (var repo = config.OpenConnection())
      {
        var recorded = Load(repo, [Face(repo.AllocateMaterialID("job", "part", 1), 1, value)]);
        await Assert
          .That(
            recorded.Single(e => e.LogType == LogType.LoadUnloadCycle).ProgramDetails["carrier-id"]
          )
          .IsEqualTo(value ?? "");
      }
      using var restarted = RepositoryConfig.InitializeEventDatabase(null, file, pooling: false);
      using var reopened = restarted.OpenConnection();
      await Assert
        .That(
          reopened
            .CurrentPalletLog(5, true)
            .Single(e => e.LogType == LogType.LoadUnloadCycle)
            .ProgramDetails["carrier-id"]
        )
        .IsEqualTo(value ?? "");
    }
    finally
    {
      System.IO.File.Delete(file);
    }
  }

  [Test]
  public async Task CarriedFaceRetainsMaterialButItsLoadMetadataFallsOutsideCurrentCycle()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repo = config.OpenConnection();
    var first = repo.AllocateMaterialID("job", "part", 2);
    var second = repo.AllocateMaterialID("job", "part", 2);
    Load(repo, [Face(first, 1, "carrier-a"), Face(second, 2, "carrier-b")]);
    var original = repo.CurrentPalletLog(5, true)
      .Single(e => e.LogType == LogType.LoadUnloadCycle && e.Material.Single().Face == 2);
    repo.RecordLoadUnloadComplete(
      toLoad: [Face(first, 1, "carrier-c")],
      previouslyLoaded:
      [
        new EventLogMaterial
        {
          MaterialID = second,
          Face = 2,
          Process = 1,
        },
      ],
      toUnload:
      [
        new MaterialToUnloadFromFace
        {
          FaceNum = 1,
          Process = 1,
          MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
            first,
            null
          ),
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      previouslyUnloaded: null,
      lulNum: 1,
      pallet: 5,
      totalElapsed: TimeSpan.Zero,
      timeUTC: DateTime.UtcNow.AddMinutes(1),
      externalQueues: null
    );
    var current = repo.CurrentPalletLog(5, true);
    await Assert
      .That(
        current
          .Single(e => e.LogType == LogType.PalletCycle && e.StartOfCycle)
          .Material.Any(m => m.MaterialID == second && m.Face == 2)
      )
      .IsTrue();
    await Assert.That(current.Any(e => e.Counter == original.Counter)).IsFalse();
    await Assert
      .That(
        current.Any(e =>
          e.ProgramDetails?.ContainsKey("carrier-id") == true
          && e.ProgramDetails["carrier-id"] == "carrier-b"
        )
      )
      .IsFalse();
    await Assert
      .That(current.Single(e => e.LogType == LogType.LoadUnloadCycle).ProgramDetails["carrier-id"])
      .IsEqualTo("carrier-c");
    // Event metadata remains historical; a new pallet-cycle marker does not copy it forward.
    await Assert
      .That(
        repo.GetRecentLog(0).Single(e => e.Counter == original.Counter).ProgramDetails["carrier-id"]
      )
      .IsEqualTo("carrier-b");
  }

  [Test]
  public async Task PerFaceMetadataSurvivesReopeningAndSameMaterialReload()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    long first,
      second;
    using (var repo = config.OpenConnection())
    {
      first = repo.AllocateMaterialID("job", "part", 2);
      second = repo.AllocateMaterialID("job", "part", 2);
      Load(repo, [Face(first, 1, "carrier-a"), Face(second, 2, "carrier-b")]);
    }
    using (var repo = config.OpenConnection())
    {
      var loads = repo.CurrentPalletLog(5, true)
        .Where(e => e.LogType == LogType.LoadUnloadCycle)
        .ToImmutableList();
      await Assert
        .That(loads.Single(e => e.Material.Single().Face == 1).ProgramDetails["carrier-id"])
        .IsEqualTo("carrier-a");
      await Assert
        .That(loads.Single(e => e.Material.Single().Face == 2).ProgramDetails["carrier-id"])
        .IsEqualTo("carrier-b");
      repo.RecordLoadUnloadComplete(
        toLoad: null,
        previouslyLoaded: null,
        toUnload:
        [
          new MaterialToUnloadFromFace
          {
            FaceNum = 1,
            Process = 1,
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              first,
              null
            ),
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        previouslyUnloaded: null,
        lulNum: 1,
        pallet: 5,
        totalElapsed: TimeSpan.Zero,
        timeUTC: DateTime.UtcNow.AddMinutes(1),
        externalQueues: null
      );
      Load(repo, [Face(first, 1, "carrier-c")], DateTime.UtcNow.AddMinutes(2));
      var latest = repo.CurrentPalletLog(5, true)
        .Where(e =>
          e.LogType == LogType.LoadUnloadCycle
          && e.Result == "LOAD"
          && e.Material.Any(m => m.MaterialID == first)
        )
        .MaxBy(e => e.Counter)!;
      await Assert.That(latest.ProgramDetails["carrier-id"]).IsEqualTo("carrier-c");
    }
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task OmittedOrNullMetadataPreservesOrdinaryLoads(bool explicitNull)
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repo = config.OpenConnection();
    var face = Face(repo.AllocateMaterialID("job", "part", 1), 1, "unused") with
    {
      AdditionalData = explicitNull ? null : ImmutableDictionary<string, string>.Empty,
    };
    Load(repo, [face]);
    await Assert
      .That(
        repo.CurrentPalletLog(5, true)
          .Single(e => e.LogType == LogType.LoadUnloadCycle)
          .ProgramDetails
      )
      .IsNull();
  }

  private static MaterialToLoadOntoFace Face(long id, int face, string carrier) =>
    new()
    {
      MaterialIDs = [id],
      FaceNum = face,
      Process = 1,
      Path = 1,
      ActiveOperationTime = TimeSpan.Zero,
      AdditionalData = ImmutableDictionary<string, string>.Empty.Add("carrier-id", carrier),
    };

  private static ImmutableList<LogEntry> Load(
    IRepository repo,
    ImmutableList<MaterialToLoadOntoFace> faces,
    DateTime? time = null
  ) =>
    repo.RecordLoadUnloadComplete(
        toLoad: faces,
        previouslyLoaded: null,
        toUnload: null,
        previouslyUnloaded: null,
        lulNum: 1,
        pallet: 5,
        totalElapsed: TimeSpan.Zero,
        timeUTC: time ?? DateTime.UtcNow,
        externalQueues: null
      )
      .ToImmutableList();
}
