#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using MazakMachineInterface;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed partial class DuplicateDownloadSpec
{
  [Test]
  public async Task PendingExactQueueLoadStaysAnonymousUntilResolved()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var template = PendingJob(false);
    var process = template.Processes[0];
    var job = template with
    {
      Processes =
      [
        process with
        {
          Paths = [process.Paths[0] with { InputQueue = "raw", PalletNums = [2] }],
        },
      ],
    };
    var config = ManagedConfig() with
    {
      UseInsightJobQuantities = j => j.UniqueStr == job.UniqueStr,
    };
    var settings = new FMSSettings();
    settings.Queues["raw"] = new QueueInfo();
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db, config, settings)).IsTrue();
    var route = device.Data.Schedules.Single();
    long QueuePiece(string serial)
    {
      var id = db.AllocateMaterialID(job.UniqueStr, job.PartName, job.Processes.Count);
      db.RecordSerialForMaterialID(id, 0, serial, Now.AddHours(-1));
      db.RecordAddMaterialToQueue(id, 0, "raw", -1, null, "Staged", Now.AddHours(-1));
      return id;
    }
    var first = QueuePiece("A");
    var selected = QueuePiece("B");

    var pending = device.Data with
    {
      PalletPositions = [new() { PalletNumber = 2, PalletPosition = "LS011" }],
      LoadActions =
      [
        new()
        {
          LoadStation = 1,
          LoadEvent = true,
          Comment = route.Comment,
          Part = job.PartName,
          Process = 1,
          Qty = 1,
        },
      ],
    };
    CurrentStatus Status(MazakAllData observed, MazakConfig cfg) =>
      BuildCurrentStatus.Build(db, settings, cfg, observed, "MC", null, Now);
    var ordinaryStatus = Status(pending, config with { UseInsightJobQuantities = null });
    var ordinaryLoad = ordinaryStatus.Material.Single(m =>
      m.Action.Type == InProcessMaterialAction.ActionType.Loading
    );
    await Assert.That(ordinaryLoad.Serial).IsEqualTo("A");

    config = config with
    {
      RequireExactLoadIdentities = (j, station) => j.UniqueStr == job.UniqueStr && station == 1,
    };
    var managedStatus = Status(pending, config);
    var managedLoad = managedStatus.Material.Single(m =>
      m.Action.Type == InProcessMaterialAction.ActionType.Loading
    );
    await Assert.That(managedLoad.MaterialID).IsEqualTo(-1);
    await Assert.That(managedLoad.Serial).IsNull();
    var queued = managedStatus.Material.Where(m =>
      m.Location.Type == InProcessMaterialLocation.LocType.InQueue
      && m.Action.Type == InProcessMaterialAction.ActionType.Waiting
    );
    await Assert.That(queued.Select(m => m.MaterialID)).IsEquivalentTo([first, selected]);

    var completed = device.PalletReadback(route, quantity: 1);
    var recordedLoad = TranslateManagedTransfer(
      db,
      completed,
      route,
      LogCode.LoadEnd,
      1,
      1,
      [selected],
      config: config,
      settings: settings
    );
    await Assert.That(recordedLoad.Material.Single().MaterialID).IsEqualTo(selected);
    var after = Status(completed, config);
    await Assert.That(after.Material.Single(m => m.Location.PalletNum == 2).Serial).IsEqualTo("B");
    await Assert
      .That(after.Material.Single(m => m.Location.CurrentQueue == "raw").Serial)
      .IsEqualTo("A");
  }

  [Test]
  [Arguments("no resolver")]
  [Arguments("not applicable")]
  [Arguments("unable to resolve")]
  [Arguments("unable with native quantities")]
  public async Task QueueLoadFallbackFollowsTheIdentityPolicy(string resolution)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var template = PendingJob(false);
    var process = template.Processes[0];
    var job = template with
    {
      Processes =
      [
        process with
        {
          Paths = [process.Paths[0] with { InputQueue = "raw", PalletNums = [2] }],
        },
      ],
    };
    var anonymous = resolution.StartsWith("unable", StringComparison.Ordinal);
    var ownedQuantities = resolution != "unable with native quantities";
    var config = ManagedConfig() with
    {
      UseInsightJobQuantities = ownedQuantities ? j => j.UniqueStr == job.UniqueStr : null,
      RequireExactLoadIdentities =
        resolution == "no resolver" ? null : (_, station) => station == 1,
      ResolveLoadUnloadTransaction = resolution switch
      {
        "no resolver" => null,
        "not applicable" => (_, _) => new MazakLoadUnloadResolution.NotApplicable(),
        _ => (_, _) => new MazakLoadUnloadResolution.UnableToResolve("Missing transfer evidence"),
      },
    };
    var settings = new FMSSettings { Queues = { ["raw"] = new QueueInfo() } };
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db, config, settings)).IsTrue();
    var route = device.Data.Schedules.Single();
    var queued = db.AllocateMaterialID(job.UniqueStr, job.PartName, job.Processes.Count);
    db.RecordSerialForMaterialID(queued, 0, "A", Now.AddHours(-2));
    db.RecordAddMaterialToQueue(queued, 0, "raw", -1, null, "Staged", Now.AddHours(-1));

    var loaded = device.PalletReadback(route, quantity: 1);
    ImmutableList<long> expectedMaterial = anonymous ? [] : [queued];
    var load = TranslateManagedTransfer(
      db,
      loaded,
      route,
      LogCode.LoadEnd,
      1,
      1,
      config: config,
      settings: settings
    );
    await Assert.That(load.Material.Select(m => m.MaterialID)).IsEquivalentTo(expectedMaterial);
    await Assert.That(db.IsMaterialInQueue(queued)).IsEqualTo(anonymous);
    var pallet = BuildCurrentStatus
      .Build(db, settings, config, loaded, "MC", null, Now)
      .Material.Single(m => m.Location.PalletNum == 2);
    await Assert.That(pallet.MaterialID).IsEqualTo(anonymous ? -1L : queued);
    if (ownedQuantities)
      await Assert
        .That(MazakManagedScheduleControl.RecordedLoads(db, config, job.UniqueStr, 1))
        .IsEqualTo(anonymous ? new MazakManagedLoadCount(0, 1) : new MazakManagedLoadCount(1, 0));

    var unloaded = device.PalletReadback(route, quantity: 0);
    var unload = TranslateManagedTransfer(
      db,
      unloaded,
      route,
      LogCode.UnloadEnd,
      1,
      2,
      config: config,
      settings: settings
    );
    await Assert.That(unload.Material.Select(m => m.MaterialID)).IsEquivalentTo(expectedMaterial);
    await Assert.That(db.IsMaterialInQueue(queued)).IsEqualTo(anonymous);
  }

  [Test]
  public async Task ManagedSplitExchangeShowsTheOutgoingSerialOnlyOnTheUnload()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    const string readyQueue = "ready-for-process-2";
    var job = PendingJob(true);
    var second = job.Processes[1];
    job = job with
    {
      Processes = job.Processes.SetItem(
        1,
        second with
        {
          Paths = [second.Paths[0] with { InputQueue = readyQueue }],
        }
      ),
    };
    var config = ManagedConfig() with { RequireExactLoadIdentities = (_, station) => station == 1 };
    var settings = new FMSSettings();
    settings.Queues[readyQueue] = new QueueInfo();
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db, config, settings)).IsTrue();

    var route = device.Data.Schedules.Single(s => s.Comment == "initial-job-2-1-InsightS");
    var outgoing = db.AllocateMaterialID(job.UniqueStr, job.PartName, job.Processes.Count);
    var incoming = db.AllocateMaterialID(job.UniqueStr, job.PartName, job.Processes.Count);
    db.RecordSerialForMaterialID(outgoing, 0, "A", Now.AddHours(-2));
    db.RecordSerialForMaterialID(incoming, 0, "B", Now.AddHours(-2));
    db.RecordAddMaterialToQueue(incoming, 1, readyQueue, -1, null, "Staged", Now.AddHours(-1));
    var occupied = device.PalletReadback(route, quantity: 1);
    var initialLoad = TranslateManagedTransfer(
      db,
      occupied,
      route,
      LogCode.LoadEnd,
      1,
      1,
      [outgoing],
      config: config,
      settings: settings
    );
    await Assert.That(initialLoad.Material.Single().MaterialID).IsEqualTo(outgoing);

    var load = new LoadAction
    {
      LoadStation = 1,
      LoadEvent = true,
      Comment = route.Comment,
      Part = job.PartName,
      Process = 1,
      Qty = 1,
    };
    var exchange = occupied with { LoadActions = [load, load with { LoadEvent = false }] };
    var status = BuildCurrentStatus.Build(db, settings, config, exchange, "MC", null, Now);

    var unloading = status.Material.Single(m => m.MaterialID == outgoing);
    await Assert.That(unloading.Serial).IsEqualTo("A");
    await Assert.That(unloading.Location.PalletNum).IsEqualTo(2);
    await Assert
      .That(unloading.Action.Type)
      .IsEqualTo(InProcessMaterialAction.ActionType.UnloadToCompletedMaterial);
    var loading = status.Material.Single(m =>
      m.Action.Type == InProcessMaterialAction.ActionType.Loading
    );
    await Assert.That(loading.MaterialID).IsEqualTo(-1);
    await Assert.That(loading.Serial).IsNull();
    await Assert.That(loading.Action.LoadOntoPalletNum).IsEqualTo(2);
    await Assert.That(loading.Action.LoadOntoFace).IsEqualTo(2);
    await Assert.That(loading.Action.ProcessAfterLoad).IsEqualTo(2);
    var waiting = status.Material.Single(m => m.MaterialID == incoming);
    await Assert.That(waiting.Location.CurrentQueue).IsEqualTo(readyQueue);
    await Assert.That(waiting.Action.Type).IsEqualTo(InProcessMaterialAction.ActionType.Waiting);
  }

  [Test]
  [Arguments("raw quantities")]
  [Arguments("exact remainder")]
  [Arguments("settled empty observation")]
  [Arguments("guarded empty observation")]
  [Arguments("quarantined set")]
  [Arguments("occupied with changed schedule comment")]
  public async Task PalletRecoveryKeepsSerialsWithTheirOwnLoad(string disposition)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var awaitingTransfer = false;
    var config = ManagedConfig() with
    {
      CanQuarantineMissingMaterial = pallet => pallet != 2 || !awaitingTransfer,
    };
    var job = PendingJob(true);
    var device = new DownloadDevice("");
    var settings = new FMSSettings { QuarantineQueue = "quarantine" };
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db)).IsTrue();
    var route = device.Data.Schedules.Single(s => s.Comment == "initial-job-2-1-InsightS");
    MazakAllData Observation(int quantity, string position = "LS011") =>
      device.PalletReadback(route, quantity, position);
    ImmutableList<long> Allocate(int count) =>
      Enumerable
        .Range(0, count)
        .Select(_ => db.AllocateMaterialID(job.UniqueStr, job.PartName, 2))
        .ToImmutableList();
    var original = Allocate(3);
    var next = Allocate(3);
    var sequence = 0;
    BlackMaple.MachineFramework.LogEntry Transfer(
      IRepository repository,
      LogCode code,
      int moved,
      MazakAllData observed,
      ImmutableList<long>? exact = null
    ) =>
      TranslateManagedTransfer(
        repository,
        observed,
        route,
        code,
        moved,
        ++sequence,
        exact,
        config: config,
        settings: settings
      );

    var firstLoad = Transfer(db, LogCode.LoadEnd, 3, Observation(3), original);
    await Assert.That(firstLoad.Material.Select(m => m.MaterialID)).IsEquivalentTo(original);
    using var reopened = repositories.OpenConnection();
    var resolved = disposition is "exact remainder" or "guarded empty observation"
      ? original.RemoveAt(0)
      : [];
    if (disposition is "quarantined set" or "occupied with changed schedule comment")
    {
      if (disposition == "occupied with changed schedule comment")
      {
        var ordinary = PendingJob(false) with { UniqueStr = "ordinary" };
        db.AddJobs(new NewJobs { ScheduleId = "second", Jobs = [ordinary] }, null, false);
        var occupied = Observation(3, "S01");
        var changed = occupied with
        {
          Schedules = occupied
            .Schedules.Select(s => s.Id == route.Id ? s with { Comment = "ordinary-Insight" } : s)
            .ToImmutableList(),
        };
        Transfer(reopened, LogCode.LoadBegin, 0, changed);
        await Assert.That(original.Any(reopened.IsMaterialInQueue)).IsFalse();
        Transfer(reopened, LogCode.LoadBegin, 0, occupied);
        await Assert.That(original.Any(reopened.IsMaterialInQueue)).IsFalse();
      }
      Transfer(reopened, LogCode.LoadBegin, 0, Observation(0, "S01"));
      await Assert.That(original.All(reopened.IsMaterialInQueue)).IsTrue();
    }
    else
    {
      var unknown = Transfer(db, LogCode.UnloadEnd, 1, Observation(2));
      await Assert.That(unknown.Material).IsEmpty();
      await Assert.That(unknown.ProgramDetails!["mazak-unidentified-quantity"]).IsEqualTo("1");
      var status = BuildCurrentStatus.Build(
        reopened,
        settings,
        config,
        Observation(2),
        "MC",
        null,
        Now
      );
      await Assert
        .That(status.Material.Where(m => m.Location.PalletNum == 2).Select(m => m.MaterialID))
        .IsEquivalentTo([-1L, -1L]);

      var empty = Observation(0);
      if (disposition == "settled empty observation")
      {
        var busy = empty with
        {
          LoadActions =
          [
            new()
            {
              Comment = route.Comment,
              LoadStation = 1,
              Qty = 2,
            },
          ],
        };
        Transfer(reopened, LogCode.LoadBegin, 0, busy);
        await Assert
          .That(reopened.CurrentPalletLog(2).Any(e => e.Counter == unknown.Counter))
          .IsTrue();
        Transfer(reopened, LogCode.LoadBegin, 0, empty);
        await Assert
          .That(reopened.CurrentPalletLog(2).Any(e => e.Counter == unknown.Counter))
          .IsFalse();
        await Assert
          .That(reopened.MostRecentLogEntryForForeignID(unknown.ForeignID).Material)
          .IsEmpty();
      }
      else
      {
        if (disposition == "guarded empty observation")
        {
          awaitingTransfer = true;
          Transfer(reopened, LogCode.LoadBegin, 0, empty);
        }
        var last = Transfer(
          reopened,
          LogCode.UnloadEnd,
          2,
          empty,
          resolved.IsEmpty ? null : resolved
        );
        await Assert.That(last.Material.Select(m => m.MaterialID)).IsEquivalentTo(resolved);
        awaitingTransfer = false;
      }
    }

    var newLoad = Transfer(reopened, LogCode.LoadEnd, 3, Observation(3), next);
    await Assert.That(newLoad.Material.Select(m => m.MaterialID)).IsEquivalentTo(next);
    var newUnload = Transfer(reopened, LogCode.UnloadEnd, 3, Observation(0));
    await Assert.That(newUnload.Material.Select(m => m.MaterialID)).IsEquivalentTo(next);
    await Assert
      .That(reopened.GetJobProductionSummary(job.UniqueStr).Completed[(2, 1)])
      .IsEqualTo(3 + resolved.Count);
  }

  [Test]
  public async Task ManagedExactOneOfThreeUnloadKeepsAttribution()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var managed = PendingJob(true);
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "pending", Jobs = [managed] }, null, false);
    Sync(device, db);
    var row = device.Data.Schedules.Single(s => s.Comment == "initial-job-2-1-InsightS");
    var ids = Enumerable
      .Range(0, 3)
      .Select(_ => db.AllocateMaterialID(managed.UniqueStr, managed.PartName, 2))
      .ToImmutableList();
    BlackMaple.MachineFramework.LogEntry Move(LogCode code, ImmutableList<long> serials, int seq) =>
      TranslateManagedTransfer(db, device.Data, row, code, serials.Count, seq, serials);
    Move(LogCode.LoadEnd, ids, 1);
    var first = Move(LogCode.UnloadEnd, [ids[1]], 2);
    await Assert.That(first.Material.Select(m => m.MaterialID)).IsEquivalentTo([ids[1]]);
    var rest = ids.RemoveAt(1);
    await Assert
      .That(Move(LogCode.UnloadEnd, rest, 3).Material.Select(m => m.MaterialID))
      .IsEquivalentTo(rest);
    await Assert.That(db.GetJobProductionSummary(managed.UniqueStr).Completed[(2, 1)]).IsEqualTo(3);
  }

  [Test]
  public async Task AnUnidentifiedLoadCountsOnlyOnTheControllerThatRecordedIt()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var job = PendingJob(true);
    var config = ManagedConfig() with
    {
      ControllerSource = "secondary",
      RequireExactLoadIdentities = (_, station) => station == 1,
      ResolveLoadUnloadTransaction = (_, _) =>
        new MazakLoadUnloadResolution.UnableToResolve("Missing transfer evidence"),
    };
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "initial", Jobs = [job] }, null, false);
    Sync(device, db, config);
    var route = device.Data.Schedules.Single(s => s.Comment == "initial-job-2-1-InsightS");
    var missing = TranslateManagedTransfer(
      db,
      device.Data,
      route,
      LogCode.LoadEnd,
      2,
      1,
      config: config
    );
    await Assert.That(missing.Material).IsEmpty();
    using var reopened = repositories.OpenConnection();
    await Assert
      .That(MazakManagedScheduleControl.RecordedLoads(reopened, config, job.UniqueStr, 2))
      .IsEqualTo(new MazakManagedLoadCount(0, 2));
    await Assert
      .That(MazakManagedScheduleControl.RecordedLoads(reopened, ManagedConfig(), job.UniqueStr, 2))
      .IsEqualTo(new MazakManagedLoadCount(0, 0));
    await Assert
      .That(MazakManagedScheduleControl.RecordedLoads(reopened, config, job.UniqueStr, 1))
      .IsEqualTo(new MazakManagedLoadCount(0, 0));
  }

  [Test]
  public async Task ReassignmentLeavesTheOriginalPhysicalLoadCountIntact()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var config = ManagedConfig();
    var job = PendingJob(true);
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "initial", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db)).IsTrue();
    var route = device.Data.Schedules.Single(s => s.Comment == "initial-job-1-1-InsightS");
    var id = db.AllocateMaterialID(job.UniqueStr, job.PartName, 2);
    TranslateManagedTransfer(db, device.Data, route, LogCode.LoadEnd, 1, 1, [id]);
    TranslateManagedTransfer(db, device.Data, route, LogCode.UnloadEnd, 1, 2, [id]);
    db.InvalidateAndChangeAssignment(id, "operator", "different-job", job.PartName, 2, Now)
      .ToImmutableList();
    await Assert.That(db.GetMaterialDetails(id)!.JobUnique).IsEqualTo("different-job");
    await Assert.That(db.GetJobProductionSummary(job.UniqueStr).Completed).IsEmpty();
    await Assert
      .That(
        MazakManagedScheduleControl.RecordedLoads(db, config, job.UniqueStr, 1)!.IdentifiedQuantity
      )
      .IsEqualTo(1L);
  }
}
