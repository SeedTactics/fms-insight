using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class JobProductionSummarySpec
{
  [Test]
  public async Task EmptyHistoryHasNoProductionFacts()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();

    var summary = db.GetJobProductionSummary("missing");

    await Assert.That(summary.AutomationEntryMaterialIds.Count).IsEqualTo(0);
    await Assert.That(summary.Completed.Count).IsEqualTo(0);
    await Assert.That(summary.LastLoadUnloadTime).IsNull();
    await Assert.That(summary.LastUnloadTime).IsNull();
  }

  [Test]
  public async Task ReadsProductionFactsWithoutHydratingJobLog()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var start = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    var ids = Enumerable.Range(0, 7).Select(_ => db.AllocateMaterialID("job", "part", 2)).ToArray();
    var otherId = db.AllocateMaterialID("other", "part", 2);
    db.RecordPathForProcess(ids[4], 1, 2);

    db.RecordLoadStart([Material(ids[5], 1)], 1, 1, start);
    db.RecordBasketLoadBegin([Material(ids[6], 1)], 2, 1, start);
    PalletLoad(db, [ids[0], ids[4], otherId], 1, start.AddMinutes(1));
    PalletLoad(db, [ids[1]], 2, start.AddMinutes(2));
    BasketTransfer(db, [ids[2], ids[4]], 1, true, start.AddMinutes(3));
    BasketTransfer(db, [ids[3]], 2, true, start.AddMinutes(4));
    BasketTransfer(db, [ids[2]], 1, false, start.AddMinutes(5));
    PalletUnload(db, [ids[4]], 1, start.AddMinutes(6));
    PalletUnload(db, [ids[0]], 1, start.AddMinutes(7));
    PalletLoad(db, [ids[0]], 1, start.AddMinutes(8));
    PalletLoad(db, [otherId], 1, start.AddMinutes(9));

    var summary = db.GetJobProductionSummary("job");
    var palletEvents = db.GetLogForJobUnique("job")
      .Where(e => !e.StartOfCycle && e.LogType == LogType.LoadUnloadCycle)
      .ToImmutableList();

    await Assert
      .That(summary.AutomationEntryMaterialIds.SetEquals(ids.Take(3).Append(ids[4])))
      .IsTrue();
    await Assert.That(summary.Completed[(1, 2)]).IsEqualTo(1);
    await Assert.That(summary.Completed[(1, 1)]).IsEqualTo(1);
    await Assert.That(summary.Completed.Count).IsEqualTo(2);
    await Assert.That(summary.LastLoadUnloadTime).IsEqualTo(palletEvents.Max(e => e.EndTimeUTC));
    await Assert
      .That(summary.LastUnloadTime)
      .IsEqualTo(palletEvents.Where(e => e.Result == "UNLOAD").Max(e => e.EndTimeUTC));
  }

  [Test]
  public async Task TenValidPiecesCanRequireElevenAttemptsWithoutAnotherFirstEntry()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var start = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    var ids = Enumerable
      .Range(0, 10)
      .Select(_ => db.AllocateMaterialID("job", "part", 1))
      .ToArray();
    for (var index = 0; index < 9; index++)
    {
      PalletLoad(db, [ids[index]], 1, start.AddMinutes(index * 2));
      PalletUnload(db, [ids[index]], 1, start.AddMinutes(index * 2 + 1), queue: null);
    }
    await AssertProduction(config, entered: 9, completed: 9);

    db.InvalidatePalletCycle(ids[0], 1, "operator", start.AddMinutes(18)).ToImmutableList();
    await AssertProduction(config, entered: 9, completed: 8);

    PalletLoad(db, [ids[0]], 1, start.AddMinutes(19));
    PalletUnload(db, [ids[0]], 1, start.AddMinutes(20), queue: null);
    await AssertProduction(config, entered: 9, completed: 9);

    PalletLoad(db, [ids[9]], 1, start.AddMinutes(21));
    PalletUnload(db, [ids[9]], 1, start.AddMinutes(22), queue: null);
    await AssertProduction(config, entered: 10, completed: 10);
    await Assert.That(db.GetLogForJobUnique("job").Count(e => e.Result == "UNLOAD")).IsEqualTo(11);
  }

  [Test]
  public async Task RepeatedInvalidationPreservesEntryAndEarlierProcessOutput()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var start = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    var id = db.AllocateMaterialID("job", "part", 2);
    PalletLoad(db, [id], 1, start);
    PalletUnload(db, [id], 1, start.AddMinutes(1), queue: null);
    for (var attempt = 0; attempt < 3; attempt++)
    {
      var time = start.AddMinutes(2 + attempt * 3);
      PalletLoad(db, [id], 2, time);
      PalletUnload(db, [id], 2, time.AddMinutes(1), queue: null);
      using (var observation = config.OpenConnection())
      {
        var summary = observation.GetJobProductionSummary("job");
        await Assert.That(summary.AutomationEntryMaterialIds).IsEquivalentTo([id]);
        await Assert.That(summary.Completed[(1, 1)]).IsEqualTo(1);
        await Assert.That(summary.Completed[(2, 1)]).IsEqualTo(1);
      }
      db.InvalidatePalletCycle(id, 2, "operator", time.AddMinutes(2)).ToImmutableList();
      using (var observation = config.OpenConnection())
      {
        var summary = observation.GetJobProductionSummary("job");
        await Assert.That(summary.AutomationEntryMaterialIds).IsEquivalentTo([id]);
        await Assert.That(summary.Completed[(1, 1)]).IsEqualTo(1);
        await Assert.That(summary.Completed.ContainsKey((2, 1))).IsFalse();
      }
    }
  }

  [Test]
  public async Task RepeatedValidUnloadsCountOneOutputForEachIdentityAndProcess()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var start = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    var id = db.AllocateMaterialID("job", "part", 2);
    foreach (var process in new[] { 1, 2 })
    {
      PalletLoad(db, [id], process, start.AddMinutes(process * 2));
      PalletUnload(db, [id], process, start.AddMinutes(process * 2 + 1), queue: null);
      PalletUnload(db, [id], process, start.AddMinutes(process * 2 + 2), queue: null);
    }

    using var observation = config.OpenConnection();
    var summary = observation.GetJobProductionSummary("job");
    await Assert.That(summary.AutomationEntryMaterialIds).IsEquivalentTo([id]);
    await Assert.That(summary.Completed[(1, 1)]).IsEqualTo(1);
    await Assert.That(summary.Completed[(2, 1)]).IsEqualTo(1);
  }

  [Test]
  public async Task InvalidatedOutputDoesNotFollowReadmissionOntoAnotherPath()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var start = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    var id = db.AllocateMaterialID("job", "part", 1);
    db.RecordPathForProcess(id, 1, 2);
    PalletLoad(db, [id], 1, start);
    PalletUnload(db, [id], 1, start.AddMinutes(1), queue: null);
    await Assert.That(db.GetJobProductionSummary("job").Completed[(1, 2)]).IsEqualTo(1);

    db.InvalidatePalletCycle(id, 1, "operator", start.AddMinutes(2)).ToImmutableList();
    db.RecordPathForProcess(id, 1, 3);
    PalletLoad(db, [id], 1, start.AddMinutes(3));
    using (var observation = config.OpenConnection())
    {
      var summary = observation.GetJobProductionSummary("job");
      await Assert.That(summary.AutomationEntryMaterialIds).IsEquivalentTo([id]);
      await Assert.That(summary.Completed).IsEmpty();
    }

    PalletUnload(db, [id], 1, start.AddMinutes(4), queue: null);
    using (var observation = config.OpenConnection())
    {
      var summary = observation.GetJobProductionSummary("job");
      await Assert.That(summary.AutomationEntryMaterialIds).IsEquivalentTo([id]);
      await Assert.That(summary.Completed.Count).IsEqualTo(1);
      await Assert.That(summary.Completed[(1, 3)]).IsEqualTo(1);
    }
  }

  private static async Task AssertProduction(RepositoryConfig config, int entered, int completed)
  {
    using var observation = config.OpenConnection();
    var summary = observation.GetJobProductionSummary("job");
    await Assert.That(summary.AutomationEntryMaterialIds.Count).IsEqualTo(entered);
    await Assert.That(summary.Completed.GetValueOrDefault((1, 1))).IsEqualTo(completed);
  }

  private static EventLogMaterial Material(long id, int process) =>
    new()
    {
      MaterialID = id,
      Process = process,
      Face = 1,
    };

  private static void PalletLoad(IRepository db, long[] ids, int process, DateTime time) =>
    db.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace
          {
            MaterialIDs = ids.ToImmutableList(),
            Process = process,
            Path = null,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        toUnload: null,
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: time,
        externalQueues: null
      )
      .ToImmutableList();

  private static void PalletUnload(
    IRepository db,
    long[] ids,
    int process,
    DateTime time,
    string queue = "completed"
  ) =>
    db.RecordLoadUnloadComplete(
        toLoad: null,
        toUnload:
        [
          new MaterialToUnloadFromFace
          {
            MaterialIDToDestination = ids.ToImmutableDictionary(
              id => id,
              _ => queue is null ? null : new UnloadDestination { Queue = queue }
            ),
            FaceNum = 1,
            Process = process,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: time,
        externalQueues: null
      )
      .ToImmutableList();

  private static void BasketTransfer(
    IRepository db,
    long[] ids,
    int process,
    bool load,
    DateTime time
  )
  {
    var basketId = process == 1 ? 2 : 3;
    var before = db.GetBasketContents(basketId);
    var slots = before?.Slots ?? ImmutableSortedDictionary<int, BasketSlotContents>.Empty;
    var nextSlot = slots.Count + 1;
    var material = ids.Select(
        (id, index) =>
          new EventLogMaterial
          {
            MaterialID = id,
            Process = process,
            Face = load
              ? nextSlot + index
              : slots.Single(slot => slot.Value.Material.Any(mat => mat.MaterialID == id)).Key,
          }
      )
      .ToImmutableList();
    var afterSlots = slots;
    foreach (var mat in material)
    {
      afterSlots = load
        ? afterSlots.Add(
          mat.Face,
          new BasketSlotContents
          {
            Material = [new BasketMaterial { MaterialID = mat.MaterialID, Process = process }],
          }
        )
        : afterSlots.Remove(mat.Face);
    }
    BasketStationTransfer transfer = load
      ? new BasketStationTransfer.LoadOntoBasket
      {
        BasketId = basketId,
        Material = material,
        ActiveOperationTime = TimeSpan.Zero,
      }
      : new BasketStationTransfer.UnloadFromBasket
      {
        BasketId = basketId,
        Material = material,
        ActiveOperationTime = TimeSpan.Zero,
      };
    db.RecordBasketStationOperation(
        new BasketStationOperation
        {
          Transfers = [transfer],
          CycleBoundaries = [],
          ContentsChanges =
          [
            new BasketContentsChange
            {
              BasketId = basketId,
              Expected = before,
              Result = new BasketContents { BasketId = basketId, Slots = afterSlots },
            },
          ],
        },
        lulNum: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: time,
        externalQueues: null,
        idempotencyKey: Guid.NewGuid().ToString()
      )
      .ToImmutableList();
  }
}
