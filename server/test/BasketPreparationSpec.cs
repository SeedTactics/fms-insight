using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class BasketPreparationSpec
{
  [Test]
  [Arguments(0)]
  [Arguments(1)]
  [Arguments(2)]
  public async Task BasketStationQueueTransfersPreserveManufacturingProgress(int completedProcess)
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var time = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    var material = AllocateGroup(db, 1);
    if (completedProcess > 0)
      db.RecordMachineEnd(
        material.Select(m => m with { Process = completedProcess }),
        1,
        "MC",
        1,
        "program",
        "",
        time,
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(5)
      );
    foreach (var mat in material)
      db.RecordAddMaterialToQueue(
        mat with
        {
          Process = completedProcess,
        },
        "transfer",
        -1,
        null,
        null,
        time
      );
    var prepared = material
      .Select(m => m with { Process = Math.Max(1, completedProcess) })
      .ToImmutableList();
    if (completedProcess == 1)
      prepared = prepared.Select(m => m with { Process = 2 }).ToImmutableList();
    var contents = Contents(prepared);
    Prepare(db, prepared, null, contents, time.AddMinutes(1), "prepare");
    db.RecordBasketStationOperation(
      new BasketStationOperation
      {
        Transfers =
        [
          new BasketStationTransfer.UnloadFromBasket
          {
            BasketId = 1,
            Material = prepared,
            ActiveOperationTime = TimeSpan.FromMinutes(1),
            DestinationQueue = "transfer",
          },
        ],
        CycleBoundaries = [new BasketCycleBoundary.End { BasketId = 1, Material = prepared }],
        ContentsChanges =
        [
          new BasketContentsChange
          {
            BasketId = 1,
            Expected = contents,
            Result = Contents([]),
          },
        ],
      },
      1,
      TimeSpan.FromMinutes(1),
      time.AddMinutes(2),
      ImmutableDictionary<string, string>.Empty,
      "unload"
    );
    foreach (var mat in material)
    {
      await Assert
        .That(db.NextProcessForQueuedMaterial(mat.MaterialID))
        .IsEqualTo(completedProcess + 1);
      await Assert
        .That(
          db.GetLogForMaterial(mat.MaterialID)
            .Where(e => e.LogType is LogType.AddToQueue or LogType.RemoveFromQueue)
            .SelectMany(e => e.Material)
            .All(m => m.Process == completedProcess)
        )
        .IsTrue();
    }
  }

  [Test]
  [Arguments(true, false)]
  [Arguments(false, false)]
  [Arguments(false, true)]
  public async Task CancelPreparationPreservesSeparateMachiningGroupsThroughBasketClosure(
    bool loadAForProcessTwo,
    bool changeToCasting
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var time = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    var a = AllocateGroup(db, 1);
    var b = AllocateGroup(db, 2);
    foreach (var group in new[] { a, b })
    {
      foreach (var mat in group)
      {
        db.RecordPathForProcess(mat.MaterialID, 1, 1);
        db.RecordPathForProcess(mat.MaterialID, 2, 1);
      }
      db.RecordMachineEnd(
        group,
        group[0].Face,
        "MC",
        1,
        "program",
        "",
        time,
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(5)
      );
      foreach (var mat in group)
        db.RecordAddMaterialToQueue(mat, "transfer", -1, null, null, time.AddMinutes(1));
    }

    var prepared = a.Concat(b).Select(m => m with { Process = 2 }).ToImmutableList();
    var contents = Contents(prepared);
    Prepare(db, prepared, null, contents, time.AddMinutes(2), "prepare-both");
    foreach (var mat in prepared)
      await Assert.That(db.NextProcessForQueuedMaterial(mat.MaterialID)).IsEqualTo(2);

    // Reject only A's prepared association, without claiming an unperformed physical transfer.
    // The grouped handling and cycle-start records remain truthful historical records.
    var remaining = Contents(prepared.Where(m => m.Face == 2).ToImmutableList());
    db.RecordBasketContentsOperation(
      new BasketContentsOperation
      {
        Changes =
        [
          new BasketContentsChange
          {
            BasketId = 1,
            Expected = contents,
            Result = remaining,
          },
        ],
      },
      "reject-a"
    );
    foreach (var mat in a)
      db.RecordAddMaterialToQueue(mat, "transfer", -1, null, null, time.AddMinutes(3));

    Load(db, b, remaining, prepared, time.AddMinutes(4));
    foreach (var mat in a)
      await Assert
        .That(db.GetMaterialInAllQueues().Single(q => q.MaterialID == mat.MaterialID).NextProcess)
        .IsEqualTo(2);

    var aPrepared = a.Select(m => m with { Process = 2 }).ToImmutableList();
    var empty = Contents([]);
    Prepare(db, aPrepared, empty, Contents(aPrepared), time.AddMinutes(5), "prepare-a");
    if (loadAForProcessTwo)
      Load(db, a, Contents(aPrepared), aPrepared, time.AddMinutes(6));
    else
      db.RecordBasketContentsOperation(
        new BasketContentsOperation
        {
          Changes =
          [
            new BasketContentsChange
            {
              BasketId = 1,
              Expected = Contents(aPrepared),
              Result = empty,
            },
          ],
        },
        "release-a-before-machining"
      );
    foreach (var mat in a)
      await Assert
        .That(db.NextProcessForQueuedMaterial(mat.MaterialID))
        .IsEqualTo(loadAForProcessTwo ? 3 : 2);

    // Invalidating real process-1 manufacturing still acts on the whole execution, while
    // shared basket handling cannot pull B into that group. Keep the repository queue guard.
    foreach (var mat in b)
      db.RecordAddMaterialToQueue(
        mat with
        {
          Process = 2,
        },
        "transfer",
        -1,
        null,
        null,
        time.AddMinutes(7)
      );
    var invalidation = (
      changeToCasting
        ? db.InvalidateAndChangeAssignment(a[0].MaterialID, "operator", null, "casting", 1)
        : db.InvalidatePalletCycle(a[0].MaterialID, 1, "operator")
    ).Single();
    await Assert
      .That(invalidation.Material.Select(m => m.MaterialID).Distinct().Order().ToArray())
      .IsEquivalentTo(a.Select(m => m.MaterialID).Order().ToArray());
    foreach (var mat in a)
    {
      await Assert.That(db.NextProcessForQueuedMaterial(mat.MaterialID)).IsNull();
      await Assert.That(db.GetMaterialDetails(mat.MaterialID).Paths?.Count ?? 0).IsEqualTo(0);
      db.RecordAddMaterialToQueue(
        mat with
        {
          Process = 0,
        },
        "raw",
        -1,
        "operator",
        null,
        time.AddMinutes(8)
      );
      await Assert
        .That(db.GetMaterialInAllQueues().Single(q => q.MaterialID == mat.MaterialID).NextProcess)
        .IsEqualTo(1);
    }
    if (changeToCasting)
    {
      await Assert.That(db.GetMaterialDetails(a[0].MaterialID).NumProcesses).IsEqualTo(1);
      await Assert.That(db.GetMaterialDetails(a[0].MaterialID).PartName).IsEqualTo("casting");
    }
    foreach (var mat in b)
    {
      await Assert
        .That(db.GetMaterialDetails(mat.MaterialID).Paths.Keys.Order().ToArray())
        .IsEquivalentTo(new[] { 1, 2 });
      await Assert.That(db.NextProcessForQueuedMaterial(mat.MaterialID)).IsEqualTo(3);
      await Assert
        .That(
          db.GetLogForMaterial(mat.MaterialID)
            .Any(e =>
              e.LogType == LogType.MachineCycle
              && e.ProgramDetails?.ContainsKey("PalletCycleInvalidated") != true
            )
        )
        .IsTrue();
    }
    await Assert
      .That(
        db.GetLogForMaterial(a[0].MaterialID)
          .Where(e => e.LogType is LogType.BasketCycle or LogType.BasketLoadUnload)
          .All(e => e.ProgramDetails?.ContainsKey("PalletCycleInvalidated") != true)
      )
      .IsTrue();
  }

  private static ImmutableList<EventLogMaterial> AllocateGroup(IRepository db, int slot) =>
    Enumerable
      .Range(0, 3)
      .Select(_ => new EventLogMaterial
      {
        MaterialID = db.AllocateMaterialID("job", "part", 2),
        Process = 1,
        Face = slot,
      })
      .ToImmutableList();

  private static BasketContents Contents(ImmutableList<EventLogMaterial> material) =>
    new()
    {
      BasketId = 1,
      Slots = material
        .GroupBy(m => m.Face)
        .ToImmutableSortedDictionary(
          g => g.Key,
          g => new BasketSlotContents
          {
            Material = g.Select(m => new BasketMaterial
              {
                MaterialID = m.MaterialID,
                Process = m.Process,
              })
              .ToImmutableList(),
          }
        ),
    };

  private static void Prepare(
    IRepository db,
    ImmutableList<EventLogMaterial> material,
    BasketContents expected,
    BasketContents result,
    DateTime time,
    string key
  ) =>
    db.RecordBasketStationOperation(
      new BasketStationOperation
      {
        Transfers =
        [
          new BasketStationTransfer.LoadOntoBasket
          {
            BasketId = 1,
            Material = material,
            ActiveOperationTime = TimeSpan.FromMinutes(1),
          },
        ],
        CycleBoundaries = [new BasketCycleBoundary.Start { BasketId = 1, Material = material }],
        ContentsChanges =
        [
          new BasketContentsChange
          {
            BasketId = 1,
            Expected = expected,
            Result = result,
          },
        ],
      },
      1,
      TimeSpan.FromMinutes(1),
      time,
      ImmutableDictionary<string, string>.Empty,
      key
    );

  private static void Load(
    IRepository db,
    ImmutableList<EventLogMaterial> group,
    BasketContents contents,
    ImmutableList<EventLogMaterial> cycleMaterial,
    DateTime time
  ) =>
    db.RecordLoadUnloadComplete(
      toLoad:
      [
        new MaterialToLoadOntoFace
        {
          MaterialIDs = group.Select(m => m.MaterialID).ToImmutableList(),
          Process = 2,
          Path = 1,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.FromMinutes(1),
        },
      ],
      toUnload: null,
      previouslyLoaded: null,
      previouslyUnloaded: null,
      pallet: group[0].Face,
      lulNum: 1,
      totalElapsed: TimeSpan.FromMinutes(1),
      timeUTC: time,
      externalQueues: ImmutableDictionary<string, string>.Empty,
      palletBasketCompletion: new PalletBasketLoadUnloadCompletion
      {
        Transfers =
        [
          new PalletBasketTransfer.UnloadFromBasket
          {
            BasketId = 1,
            Material = group.Select(m => m with { Process = 2 }).ToImmutableList(),
          },
        ],
        CycleBoundaries = [new BasketCycleBoundary.End { BasketId = 1, Material = cycleMaterial }],
        ContentsChanges =
        [
          new BasketContentsChange
          {
            BasketId = 1,
            Expected = contents,
            Result = Contents([]),
          },
        ],
      }
    );
}
