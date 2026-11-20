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
  [Arguments("parts-full")]
  [Arguments("parts-partial")]
  [Arguments("parts-before-pallet-chunk")]
  [Arguments("schedules-partial")]
  public async Task AgedInitialInstallationCanFinish(string fault)
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var managed = PendingJob(true);
    var device = new DownloadDevice(fault);
    db.AddJobs(new NewJobs { ScheduleId = "pending", Jobs = [managed] }, null, false);
    await Assert.That(() => Sync(device, db)).Throws<IOException>();
    using var reopened = config.OpenConnection();
    var afterLookback = managed.RouteEndUTC.AddHours(WriteJobs.JobLookbackHours + 1);
    WriteJobs.SyncFromDatabase(
      device.Data,
      reopened,
      device.Native,
      new FMSSettings(),
      ManagedConfig(),
      afterLookback
    );
    await Assert.That(device.Data.Schedules.Count).IsEqualTo(2);
    await Assert.That(reopened.LoadJob(managed.UniqueStr)!.CopiedToSystem).IsTrue();
  }

  [Test]
  [Arguments(true)]
  [Arguments(false)]
  public async Task ManagedDownloadRequiresRemainingDemandAfterBackout(bool interrupted)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var active = PendingJob(true);
    var canceled = active with { UniqueStr = "fully-canceled" };
    var ordinary = PendingJob(false) with { UniqueStr = "ordinary" };
    var device = new DownloadDevice(interrupted ? "schedules-partial" : "");
    db.AddJobs(
      new NewJobs { ScheduleId = "first", Jobs = [active, canceled, ordinary] },
      null,
      false
    );
    if (interrupted)
      await Assert.That(() => Sync(device, db)).Throws<IOException>();
    var installed = device.Data.Schedules.ToImmutableList();
    db.RecordJobDecrementOperation(
      "backout",
      [
        new()
        {
          JobUnique = active.UniqueStr,
          Part = active.PartName,
          Quantity = active.Cycles - 3,
        },
        new()
        {
          JobUnique = canceled.UniqueStr,
          Part = canceled.PartName,
          Quantity = canceled.Cycles,
        },
        new()
        {
          JobUnique = ordinary.UniqueStr,
          Part = ordinary.PartName,
          Quantity = ordinary.Cycles - 3,
        },
      ],
      [],
      Now
    );
    using var reopened = repositories.OpenConnection();
    var retry = interrupted ? active.RouteEndUTC.AddHours(WriteJobs.JobLookbackHours + 1) : Now;
    WriteJobs.SyncFromDatabase(
      device.Data,
      reopened,
      device.Native,
      new FMSSettings(),
      ManagedConfig(),
      retry
    );
    await Assert.That(reopened.LoadJob(active.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(reopened.LoadJob(canceled.UniqueStr)!.CopiedToSystem).IsFalse();
    await Assert.That(reopened.LoadJob(ordinary.UniqueStr)!.CopiedToSystem).IsFalse();
    var added = device
      .Data.Schedules.Where(s => !installed.Any(before => before.Id == s.Id))
      .ToImmutableList();
    await Assert.That(added).IsNotEmpty();
    await Assert
      .That(added.All(s => MazakPart.UniqueFromComment(s.Comment) == active.UniqueStr))
      .IsTrue();
    await Assert
      .That(added.SelectMany(s => s.Processes).All(p => p.ProcessMaterialQuantity == 0))
      .IsTrue();
  }

  [Test]
  public async Task ArchiveAfterUnacknowledgedPartInstallationCleansNativeDefinitions()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var managed = PendingJob(true);
    var device = new DownloadDevice("parts-full");
    db.AddJobs(new NewJobs { ScheduleId = "pending", Jobs = [managed] }, null, false);
    await Assert.That(() => Sync(device, db)).Throws<IOException>();
    await Assert.That(device.Data.Parts.Count).IsEqualTo(2);
    db.ArchiveJob(managed.UniqueStr);
    using var reopened = config.OpenConnection();
    for (var i = 0; i < 12 && Sync(device, reopened); ++i) { }
    await Assert.That(device.Data.Parts).IsEmpty();
    await Assert.That(device.Data.Pallets).IsEmpty();
  }

  [Test]
  public async Task ArchivedInstallationFreesFixtureCapacityForAPendingDownload()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var device = new DownloadDevice("");
    var managed = PendingJob(true);
    var next = PendingJob(false) with { UniqueStr = "next", PartName = "next-part" };
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [managed] }, null, false);
    await Assert.That(Sync(device, db)).IsTrue();
    var used = device.Data.Pallets.Select(p => p.FixtureGroup).ToImmutableHashSet();
    device.Observe(
      device.Data with
      {
        Pallets = device
          .Data.Pallets.Concat(
            Enumerable
              .Range(1, 998)
              .Where(g => !used.Contains(g))
              .Select(group => new MazakPalletRow
              {
                PalletNumber = 1,
                Fixture = $"operator-fixture-{group}",
                FixtureGroupV2 = group,
              })
          )
          .ToImmutableList(),
      }
    );
    db.AddJobs(new NewJobs { ScheduleId = "next", Jobs = [next] }, null, false);
    db.ArchiveJob(managed.UniqueStr);
    for (var i = 0; i < 10 && !db.LoadJob(next.UniqueStr)!.CopiedToSystem; ++i)
      await Assert.That(Sync(device, db)).IsTrue();
    await Assert.That(db.LoadJob(next.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(device.Data.Schedules.Single().Comment).IsEqualTo("next-Insight");
  }
}
