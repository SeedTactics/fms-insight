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
  [Arguments("raw quantities")]
  [Arguments("exact remainder")]
  [Arguments("settled empty observation")]
  public async Task AmbiguousUnloadCannotAttributeOldSerialsToTheNextLoad(string disposition)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var config = ManagedConfig();
    var job = PendingJob(true);
    var device = new DownloadDevice("");
    var settings = new FMSSettings { QuarantineQueue = "quarantine" };
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db)).IsTrue();
    var route = device.Data.Schedules.Single(s => s.Comment == "initial-job-2-1-InsightS");
    var fixture = device
      .Data.Parts.Single(p => p.PartName == route.PartName)
      .Processes.Single()
      .Fixture;
    MazakAllData Observation(int quantity) =>
      device.Data with
      {
        PalletPositions = [new() { PalletNumber = 2, PalletPosition = "LS011" }],
        PalletSubStatuses =
          quantity == 0
            ? []
            :
            [
              new()
              {
                PalletNumber = 2,
                ScheduleID = route.Id,
                PartName = route.PartName,
                PartProcessNumber = 1,
                FixtureName = fixture,
                FixQuantity = quantity,
              },
            ],
      };
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
        config,
        settings,
        observed,
        route,
        job.PartName,
        code,
        moved,
        ++sequence,
        exact
      );

    var firstLoad = Transfer(db, LogCode.LoadEnd, 3, Observation(3), original);
    await Assert.That(firstLoad.Material.Select(m => m.MaterialID)).IsEquivalentTo(original);
    var unknown = Transfer(db, LogCode.UnloadEnd, 1, Observation(2));
    await Assert.That(unknown.Material).IsEmpty();
    await Assert.That(unknown.ProgramDetails!["mazak-unidentified-quantity"]).IsEqualTo("1");

    using var reopened = repositories.OpenConnection();
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

    var resolved = disposition == "exact remainder" ? original.RemoveAt(0) : [];
    if (disposition == "settled empty observation")
    {
      var empty = Observation(0);
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
      var last = Transfer(
        reopened,
        LogCode.UnloadEnd,
        2,
        Observation(0),
        resolved.IsEmpty ? null : resolved
      );
      await Assert.That(last.Material.Select(m => m.MaterialID)).IsEquivalentTo(resolved);
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
      TranslateManagedTransfer(
        db,
        ManagedConfig(),
        new FMSSettings(),
        device.Data,
        row,
        managed.PartName,
        code,
        serials.Count,
        seq,
        serials
      );
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
  public async Task CopiedCommentRecordsUnidentifiedMovementWithoutCreditingEitherSchedule()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var config = ManagedConfig();
    var job = PendingJob(true);
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "initial", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db)).IsTrue();
    var route = device.Data.Schedules.Single(s => s.Comment == "initial-job-2-1-InsightS");
    var alias = route with { PartName = job.PartName + ":100:2:2" };
    var part = device.Data.Parts.Single(p => p.PartName == route.PartName);
    device.Observe(
      device.Data with
      {
        Parts = device
          .Data.Parts.Where(p => p.PartName != route.PartName)
          .Append(part with { PartName = alias.PartName })
          .ToImmutableList(),
        Schedules = device
          .Data.Schedules.Where(s => s.Id != route.Id)
          .Append(alias)
          .ToImmutableList(),
      }
    );
    var settings = new FMSSettings();
    var load = TranslateManagedTransfer(
      db,
      config,
      settings,
      device.Data,
      alias,
      job.PartName,
      LogCode.LoadEnd,
      1,
      1,
      null
    );
    var unload = TranslateManagedTransfer(
      db,
      config,
      settings,
      device.Data,
      alias,
      job.PartName,
      LogCode.UnloadEnd,
      1,
      2,
      null
    );
    await Assert.That(load.Material).IsEmpty();
    await Assert.That(unload.Material).IsEmpty();
    await Assert.That(load.ProgramDetails!["mazak-managed-route-conflict"]).IsEqualTo("true");
    await Assert.That(unload.ProgramDetails!["mazak-unidentified-quantity"]).IsEqualTo("1");
    await Assert.That(db.GetJobProductionSummary(job.UniqueStr).Completed).IsEmpty();
    await Assert
      .That(
        MazakManagedScheduleControl.RecordedLoads(db, config, job.UniqueStr, 2)!.PhysicalQuantity
      )
      .IsEqualTo(0L);
  }

  [Test]
  public async Task TransferForAnUnconfirmedInstallationStaysUnidentified()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var job = PendingJob(true);
    var device = new DownloadDevice("schedules");
    db.AddJobs(new NewJobs { ScheduleId = "initial", Jobs = [job] }, null, false);
    await Assert.That(() => Sync(device, db)).Throws<IOException>();
    var part = device.Data.Parts.Single(p => p.Comment == "initial-job-2-1-InsightS");
    var candidate = new MazakScheduleRow { PartName = part.PartName, Comment = part.Comment };
    var id = db.AllocateMaterialID(job.UniqueStr, job.PartName, 2);
    var entry = TranslateManagedTransfer(
      db,
      ManagedConfig(),
      new FMSSettings(),
      device.Data,
      candidate,
      job.PartName,
      LogCode.LoadEnd,
      1,
      1,
      [id]
    );
    await Assert.That(entry.Material).IsEmpty();
    await Assert.That(entry.ProgramDetails!["mazak-unidentified-quantity"]).IsEqualTo("1");
    await Assert.That(entry.ProgramDetails!["mazak-managed-route-conflict"]).IsEqualTo("true");

    await Assert.That(Sync(device, db)).IsTrue();
    var installed = device.Data.Schedules.Single(s => s.PartName == part.PartName);
    entry = TranslateManagedTransfer(
      db,
      ManagedConfig(),
      new FMSSettings(),
      device.Data,
      installed,
      job.PartName,
      LogCode.LoadEnd,
      1,
      2,
      [id]
    );
    await Assert.That(entry.Material.Single().MaterialID).IsEqualTo(id);
    await Assert
      .That(
        MazakManagedScheduleControl
          .RecordedLoads(db, ManagedConfig(), job.UniqueStr, 2)!
          .PhysicalQuantity
      )
      .IsEqualTo(1L);
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
    var settings = new FMSSettings();
    TranslateManagedTransfer(
      db,
      config,
      settings,
      device.Data,
      route,
      job.PartName,
      LogCode.LoadEnd,
      1,
      1,
      [id]
    );
    TranslateManagedTransfer(
      db,
      config,
      settings,
      device.Data,
      route,
      job.PartName,
      LogCode.UnloadEnd,
      1,
      2,
      [id]
    );
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
