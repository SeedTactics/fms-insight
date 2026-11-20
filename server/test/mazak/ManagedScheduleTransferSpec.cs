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
