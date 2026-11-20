#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;

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
  [Arguments(false)]
  [Arguments(true)]
  public async Task RetryingPartInstallationKeepsTheIdentityUsedByTheProcessHook(bool split)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var job = PendingJob(split);
    var device = new DownloadDevice("");
    var config = ManagedConfig() with
    {
      UseInsightJobQuantities = _ => true,
      ProcessFromJob = (part, process, path, program) =>
        new MazakProcessFromJob(
          part,
          process,
          path,
          part.DownloadID == 0 && MazakPart.ParseUID(part.PartName) == 0
            ? program
            : program with
            {
              CellControllerProgramName = "1002",
            }
        ),
    };
    var settings = new FMSSettings();
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [job] }, null, false);
    bool Poll(IRepository repository) =>
      WriteJobs.SyncFromDatabase(device.Data, repository, device.Native, settings, config, Now);

    device.BeforeSave = write =>
    {
      if (write.Parts.Any(part => part.Command == MazakWriteCommand.Add))
        throw new IOException("Part installation failed before applying the write.");
    };
    await Assert.That(() => Poll(db)).Throws<IOException>();
    await Assert.That(device.Data.Parts).IsEmpty();
    var reserved = db.LoadMazakManagedSchedules(config.ControllerSource);
    await Assert.That(reserved.Count).IsEqualTo(split ? 2 : 1);
    device.BeforeSave = null!;

    using var reopened = repositories.OpenConnection();
    await Assert.That(Poll(reopened)).IsTrue();
    await Assert.That(reopened.LoadJob(job.UniqueStr)!.CopiedToSystem).IsTrue();
    foreach (var route in reserved)
    {
      var installed = device.Data.Parts.Single(part => part.PartName == route.PartName);
      await Assert
        .That(installed.Processes.Select(process => process.MainProgram))
        .IsEquivalentTo(route.Processes.Select(process => process.Program));
    }
    await Assert
      .That(
        MazakManagedScheduleControl.SetMaterialOffer(
          reopened,
          config,
          device.Data,
          job.UniqueStr,
          1,
          1
        )
      )
      .IsNotNull();
  }

  [Test]
  public async Task SharedBatchValidatesOnlyTheProgramsForThisController()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var template = PendingJob(false);
    var local = template with { Processes = [template.Processes[0]] };
    var peer = template with { UniqueStr = "peer", Processes = [template.Processes[1]] };
    var batch = new NewJobs { ScheduleId = "shared", Jobs = [local, peer] };
    var device = new DownloadDevice("");
    device.Observe(
      device.Data with
      {
        MainPrograms = device
          .Data.MainPrograms.Where(p => p.MainProgram == "1001")
          .ToImmutableList(),
      }
    );
    using var mazak = new MazakSync(
      device.Native,
      new FMSSettings(),
      ManagedConfig() with
      {
        BulkJobFilter = job => job.UniqueStr == local.UniqueStr,
      }
    );
    await Assert.That(mazak.CheckNewJobs(db, batch)).IsEmpty();

    device.Observe(device.Data with { MainPrograms = [] });
    await Assert.That(mazak.CheckNewJobs(db, batch).Single()).Contains("program 1001");
    device.Native.ClearReceivedCalls();
    await Assert.That(mazak.CheckNewJobs(db, batch with { Jobs = [peer] })).IsEmpty();
    device.Native.DidNotReceive().LoadAllData();
  }

  [Test]
  public async Task ZeroCycleManagedJobLetsTheNextBatchDownload()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var managed = PendingJob(true) with { Cycles = 0 };
    var ordinary = PendingJob(false) with { UniqueStr = "next", PartName = "next-part" };
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [managed] }, null, false);
    db.AddJobs(new NewJobs { ScheduleId = "second", Jobs = [ordinary] }, null, false);

    await Assert.That(Sync(device, db)).IsTrue();
    await Assert.That(db.LoadJob(ordinary.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(device.Data.Parts.Single().Comment).IsEqualTo("next-Insight");
    await Assert.That(db.LoadMazakManagedSchedules(ManagedConfig().ControllerSource)).IsEmpty();
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
  public async Task RetryRejectsAChangedFixtureBeforeWritingPalletMappings()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var device = new DownloadDevice("parts-full");
    var job = PendingJob(true);
    db.AddJobs(new NewJobs { ScheduleId = "pending", Jobs = [job] }, null, false);
    await Assert.That(() => Sync(device, db)).Throws<IOException>();

    var parts = device.Data.Parts.OrderBy(p => p.Comment).ToArray();
    var changed = parts[0] with
    {
      Processes =
      [
        parts[0].Processes.Single() with
        {
          Fixture = parts[1].Processes.Single().Fixture,
        },
      ],
    };
    device.Observe(device.Data with { Parts = [changed, parts[1]] });
    device.Native.ClearReceivedCalls();
    using var reopened = repositories.OpenConnection();
    await Assert.That(Sync(device, reopened)).IsFalse();
    var alarm = ManagedMazakSchedules
      .Diagnostics(reopened, ManagedConfig(), device.Data, Now)
      .Single();
    await Assert.That(alarm.Message).Contains("changed fixtures or programs");
    await Assert.That(alarm.Message).Contains(parts[1].Processes.Single().Fixture);
    device.Native.DidNotReceive().Save(Arg.Any<MazakWriteData>());
  }

  [Test]
  [Arguments("fixture", false)]
  [Arguments("program", true)]
  [Arguments("adopted program", false)]
  [Arguments("adopted part without schedule", true)]
  public async Task PendingRouteConflictHoldsOnlyTheAffectedJob(string cause, bool afterLookback)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var managed = PendingJob(true);
    var settings = new FMSSettings();
    var config = ManagedConfig();
    var device = new DownloadDevice(cause == "adopted program" ? "" : "parts-full");
    var pollAt = afterLookback ? managed.RouteEndUTC.AddHours(51) : Now;
    var ordinary = PendingJob(false);
    var peer = ordinary with
    {
      UniqueStr = "peer",
      PartName = "peer-part",
      RouteStartUTC = pollAt.AddHours(-1),
      RouteEndUTC = pollAt.AddDays(1),
      Processes = ordinary
        .Processes.Select(p =>
          p with
          {
            Paths = p
              .Paths.Select(path =>
                path with
                {
                  PalletNums = [9],
                  Load = [2],
                  Unload = [2],
                  Stops = path.Stops.Select(stop => stop with { Stations = [2] }).ToImmutableList(),
                }
              )
              .ToImmutableList(),
          }
        )
        .ToImmutableList(),
    };
    var sameBatch = cause == "fixture";
    db.AddJobs(
      new NewJobs { ScheduleId = "first", Jobs = sameBatch ? [managed, peer] : [managed] },
      null,
      false
    );
    if (cause is "adopted program" or "adopted part without schedule")
    {
      using var oldRepository = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
      using var before = oldRepository.OpenConnection();
      before.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [managed] }, null, false);
      bool InstallLegacyRoutes() =>
        WriteJobs.SyncFromDatabase(
          device.Data,
          before,
          device.Native,
          settings,
          config with
          {
            UseInsightJobQuantities = null,
          },
          Now
        );
      if (cause == "adopted part without schedule")
      {
        await Assert.That(() => InstallLegacyRoutes()).Throws<IOException>();
        await Assert.That(device.Data.Schedules).IsEmpty();
        await Assert.That(db.LoadMazakManagedSchedules(config.ControllerSource)).IsEmpty();
      }
      else
        await Assert.That(InstallLegacyRoutes()).IsTrue();
    }
    else
      await Assert.That(() => Sync(device, db)).Throws<IOException>();

    var parts = device.Data.Parts.ToImmutableList();
    var route = parts.Single(p => p.Comment == "initial-job-2-1-InsightS");
    var process = route.Processes.Single();
    var wrong = route with
    {
      Processes =
      [
        cause == "fixture"
          ? process with
          {
            Fixture = parts
              .Single(p => p.Comment == "initial-job-1-1-InsightS")
              .Processes.Single()
              .Fixture,
          }
          : process with
          {
            MainProgram = "1001",
          },
      ],
    };
    device.Observe(device.Data with { Parts = parts.Replace(route, wrong) });

    if (cause == "adopted part without schedule")
      await Assert.That(Sync(device, db)).IsFalse();
    if (!sameBatch)
      db.AddJobs(new NewJobs { ScheduleId = "second", Jobs = [peer] }, null, false);
    using var reopened = repositories.OpenConnection();
    bool Poll() =>
      WriteJobs.SyncFromDatabase(device.Data, reopened, device.Native, settings, config, pollAt);
    await Assert.That(Poll()).IsTrue();
    await Assert.That(reopened.LoadJob(peer.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(reopened.LoadJob(managed.UniqueStr)!.CopiedToSystem).IsFalse();
    for (var i = 0; i < 3; ++i)
      await Assert.That(Poll()).IsFalse();
    await Assert.That(device.Data.Parts.Single(p => p.PartName == route.PartName)).IsEqualTo(wrong);
    var alarm = ManagedMazakSchedules.Diagnostics(reopened, config, device.Data, pollAt).Single();
    await Assert.That(alarm.Message).Contains(managed.UniqueStr);
    await Assert.That(alarm.Message).Contains(route.PartName);
    await Assert
      .That(alarm.Message)
      .Contains(cause == "fixture" ? wrong.Processes.Single().Fixture : "1001");

    device.Observe(
      device.Data with
      {
        Parts = device
          .Data.Parts.Select(p => p.PartName == route.PartName ? route : p)
          .ToImmutableList(),
      }
    );
    await Assert.That(Poll()).IsTrue();
    await Assert.That(reopened.LoadJob(managed.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert
      .That(ManagedMazakSchedules.Diagnostics(reopened, config, device.Data, pollAt))
      .IsEmpty();
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task MissingProgramOnAnUninstalledRouteLetsIndependentJobsDownload(bool interrupted)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var managed = PendingJob(true);
    var device = new DownloadDevice(interrupted ? "parts-partial" : "");
    var config = ManagedConfig();
    var accepted = new NewJobs { ScheduleId = "first", Jobs = [managed] };
    using var mazak = new MazakSync(device.Native, new FMSSettings(), config);
    await Assert.That(mazak.CheckNewJobs(db, accepted)).IsEmpty();
    db.AddJobs(accepted, null, false);
    if (interrupted)
    {
      await Assert.That(() => Sync(device, db)).Throws<IOException>();
      await Assert
        .That(device.Data.Parts.Single().Processes.Single().MainProgram)
        .IsEqualTo("1001");
    }
    var programs = device.Data.MainPrograms;
    device.Observe(
      device.Data with
      {
        MainPrograms = programs.Where(p => p.MainProgram != "1002").ToImmutableList(),
      }
    );

    var retryAt = interrupted ? managed.RouteEndUTC.AddHours(WriteJobs.JobLookbackHours + 1) : Now;
    if (!interrupted)
      await Assert
        .That(
          ManagedMazakSchedules.Diagnostics(
            db,
            config,
            device.Data,
            managed.RouteStartUTC.AddHours(-2)
          )
        )
        .IsEmpty();
    var ordinary = PendingJob(false);
    var peer = ordinary with
    {
      UniqueStr = "peer",
      PartName = "peer-part",
      Processes = [ordinary.Processes[0]],
      RouteStartUTC = retryAt,
      RouteEndUTC = retryAt.AddDays(1),
    };
    db.AddJobs(new NewJobs { ScheduleId = "second", Jobs = [peer] }, null, false);
    using var reopened = repositories.OpenConnection();
    bool Poll() =>
      WriteJobs.SyncFromDatabase(
        device.Data,
        reopened,
        device.Native,
        new FMSSettings(),
        config,
        retryAt
      );
    await Assert.That(Poll()).IsTrue();
    await Assert.That(reopened.LoadJob(peer.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(reopened.LoadJob(managed.UniqueStr)!.CopiedToSystem).IsFalse();
    await Assert.That(Poll()).IsFalse();
    var alarm = ManagedMazakSchedules.Diagnostics(reopened, config, device.Data, retryAt).Single();
    await Assert.That(alarm.Message).Contains(managed.UniqueStr);
    await Assert.That(alarm.Message).Contains("1002");

    device.Observe(device.Data with { MainPrograms = programs });
    await Assert.That(Poll()).IsTrue();
    await Assert.That(reopened.LoadJob(managed.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert
      .That(ManagedMazakSchedules.Diagnostics(reopened, config, device.Data, retryAt))
      .IsEmpty();
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
  public async Task ArchivingAnAdoptedRoutePreservesOperatorPalletMappings()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var config = ManagedConfig();
    var managed = PendingJob(true);
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "pending", Jobs = [managed] }, null, false);
    await Assert
      .That(
        WriteJobs.SyncFromDatabase(
          device.Data,
          db,
          device.Native,
          new FMSSettings(),
          config with
          {
            UseInsightJobQuantities = null,
          },
          Now
        )
      )
      .IsTrue();

    var part = device.Data.Parts.Single(p => p.Comment == "initial-job-1-1-InsightS");
    var process = part.Processes.Single();
    var oldFixture = device.Data.Fixtures.Single(f => f.FixtureName == process.Fixture);
    var fixture = oldFixture with { FixtureName = "VISE1", Comment = "Operator fixture" };
    var oldMapping = device.Data.Pallets.Single(p => p.Fixture == process.Fixture);
    var mapping = oldMapping with { Fixture = fixture.FixtureName };
    device.Observe(
      device.Data with
      {
        Parts = device
          .Data.Parts.Select(p =>
            p == part ? p with { Processes = [process with { Fixture = fixture.FixtureName }] } : p
          )
          .ToImmutableList(),
        Fixtures = device
          .Data.Fixtures.Select(f => f == oldFixture ? fixture : f)
          .ToImmutableList(),
        Pallets = device.Data.Pallets.Select(p => p == oldMapping ? mapping : p).ToImmutableList(),
      }
    );
    await Assert.That(Sync(device, db)).IsFalse();
    var adopted = db.LoadMazakManagedSchedule(config.ControllerSource, part.PartName);
    await Assert.That(adopted?.Processes.Single().Fixture).IsEqualTo(fixture.FixtureName);

    db.ArchiveJob(managed.UniqueStr);
    using var reopened = repositories.OpenConnection();
    for (var poll = 0; poll < 12 && Sync(device, reopened); poll++) { }
    await Assert.That(device.Data.Schedules).IsEmpty();
    await Assert.That(device.Data.Parts).IsEmpty();
    await Assert.That(reopened.LoadMazakManagedSchedules(config.ControllerSource)).IsEmpty();
    await Assert.That(device.Data.Pallets.Single()).IsEqualTo(mapping);
    await Assert.That(device.Data.Fixtures.Single()).IsEqualTo(fixture);
  }

  [Test]
  public async Task ArchivingBothOwnersCleansSharedFixturesAfterAnInterruptedDownload()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var first = PendingJob(true);
    var second = first with { UniqueStr = "second-job" };
    var device = new DownloadDevice("");
    device.AfterSave = write =>
    {
      if (write.Fixtures.Any(f => f.Command == MazakWriteCommand.Add))
        throw new IOException("Lost fixture write reply");
    };
    db.AddJobs(new NewJobs { ScheduleId = "both", Jobs = [first, second] }, null, false);
    await Assert.That(() => Sync(device, db)).Throws<IOException>();
    await Assert.That(device.Data.Fixtures.Count()).IsEqualTo(2);
    await Assert.That(device.Data.Parts).IsEmpty();

    db.ArchiveJobs([first.UniqueStr, second.UniqueStr]);
    device.AfterSave = null!;
    using var reopened = repositories.OpenConnection();
    for (var i = 0; i < 8 && Sync(device, reopened); i++) { }
    await Assert.That(device.Data.Fixtures).IsEmpty();
    await Assert
      .That(reopened.LoadMazakManagedSchedules(ManagedConfig().ControllerSource))
      .IsEmpty();
  }

  [Test]
  public async Task RefusedArchiveDeletionLetsOtherRoutesRetireAfterFreshReadback()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var device = new DownloadDevice("");
    var first = PendingJob(true);
    var peer = first with { UniqueStr = "other", PartName = "other-part" };
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [first, peer] }, null, false);
    await Assert.That(Sync(device, db)).IsTrue();
    db.ArchiveJobs([first.UniqueStr, peer.UniqueStr]);
    MazakScheduleRow? rejected = null;
    var config = ManagedConfig();
    var reads = 0;
    var readAtRefusal = -1;
    var peerDeletedWithoutReadback = false;
    device
      .Native.LoadAllData()
      .Returns(_ =>
      {
        ++reads;
        return device.Data;
      });
    device.BeforeSave = write =>
    {
      if (write.Schedules.SingleOrDefault() is not { } schedule)
        return;
      rejected ??= schedule;
      if (schedule.Id == rejected.Id)
      {
        readAtRefusal = reads;
        throw new IOException("Mazak refused deletion.");
      }
      if (
        MazakPart.UniqueFromComment(schedule.Comment)
        != MazakPart.UniqueFromComment(rejected.Comment)
      )
        peerDeletedWithoutReadback |= reads == readAtRefusal;
    };
    bool Poll() =>
      WriteJobs.SyncFromDatabase(device.Data, db, device.Native, new FMSSettings(), config, Now);
    await Assert
      .That(() =>
      {
        for (var poll = 0; poll < 15 && Poll(); ++poll) { }
      })
      .Throws<IOException>()
      .WithMessageContaining("Mazak refused deletion.");
    await Assert.That(peerDeletedWithoutReadback).IsFalse();
    await Assert.That(device.Data.Schedules.Single().Id).IsEqualTo(rejected!.Id);
    await Assert
      .That(db.LoadMazakManagedSchedules(config.ControllerSource).Single().PartName)
      .IsEqualTo(rejected.PartName);

    device.BeforeSave = null!;
    for (var poll = 0; poll < 8 && Poll(); ++poll) { }
    await Assert.That(db.LoadMazakManagedSchedules(config.ControllerSource)).IsEmpty();
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
