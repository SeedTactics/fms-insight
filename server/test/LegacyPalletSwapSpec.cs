using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class LegacyPalletSwapSpec
{
  [Test]
  public async Task ReadsLegacyAuditWithoutTreatingBothIdentitiesAsPalletMaterial()
  {
    var guid = Guid.NewGuid();
    using var config = RepositoryConfig.InitializeMemoryDB(null, guid);
    long original;
    long replacement;
    using (var repository = config.OpenConnection())
    {
      original = repository.AllocateMaterialID("job", "part", 2);
      replacement = repository.AllocateMaterialID("job", "part", 2);
      // Existing databases already contain the corrected membership on their ordinary events.
      repository.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace
          {
            MaterialIDs = [replacement],
            FaceNum = 1,
            Process = 1,
            Path = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        toUnload: [],
        previouslyLoaded: [],
        previouslyUnloaded: [],
        pallet: 2,
        lulNum: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: DateTime.UtcNow.AddMinutes(-1),
        externalQueues: ImmutableDictionary<string, string>.Empty
      );
    }
    // Seed the retired persisted representation, not a live mutation API.
    using (
      var connection = new SqliteConnection($"Data Source=file:${guid}?mode=memory&cache=shared")
    )
    {
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText = """
        INSERT INTO stations(Counter, Pallet, StationLoc, StationName, StationNum, Program,
          Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime)
        VALUES(100, 2, 113, 'SwapMatOnPallet', 1, 'SwapMatOnPallet', 0, $time,
          'Replace A with B on pallet 2', 0, 0, 0);
        INSERT INTO stations_mat(Counter, MaterialID, Process, Face)
        VALUES(100, $old, 1, 0), (100, $new, 1, 0);
        """;
      command.Parameters.AddWithValue("time", DateTime.UtcNow.Ticks);
      command.Parameters.AddWithValue("old", original);
      command.Parameters.AddWithValue("new", replacement);
      command.ExecuteNonQuery();
    }
    using var reopened = config.OpenConnection();
    var audit = reopened.GetLogForMaterial(original).Single();
    await Assert.That((int)audit.LogType).IsEqualTo(113);
    await Assert.That(audit.LogType).IsEqualTo(LogType.SwapMaterialOnPallet);
    await Assert.That(audit.Result).IsEqualTo("Replace A with B on pallet 2");
    await Assert
      .That(audit.Material.Select(m => m.MaterialID))
      .IsEquivalentTo([original, replacement]);
    var json = JsonSerializer.Serialize(
      audit,
      new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }
    );
    await Assert.That(json).Contains("SwapMaterialOnPallet");
    var current = reopened.CurrentPalletLog(2, includeLastPalletCycleEvt: true);
    await Assert.That(current.Any(e => e.Counter == audit.Counter)).IsFalse();
    await Assert
      .That(current.SelectMany(e => e.Material).Select(m => m.MaterialID).Distinct())
      .IsEquivalentTo([replacement]);
    await Assert.That(reopened.NextProcessForQueuedMaterial(original)).IsNull();
    await Assert.That(reopened.NextProcessForQueuedMaterial(replacement)).IsEqualTo(2);
  }
}
