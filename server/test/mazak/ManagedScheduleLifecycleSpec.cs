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
  private static MazakConfig ManagedConfig() =>
    new()
    {
      DBType = MazakDbType.MazakSmooth,
      UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
    };

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
  public async Task JobMaintenanceCanDownloadAnAgedManagedJobWithoutAuthorizingCleanup()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var managed = PendingJob(true);
    var peer = managed with { UniqueStr = "peer", PartName = "peer-part" };
    var ordinary = PendingJob(false) with { UniqueStr = "ordinary", PartName = "ordinary-part" };
    db.AddJobs(new NewJobs { ScheduleId = "old", Jobs = [managed, peer, ordinary] }, null, false);
    var config = ManagedConfig() with { BulkJobFilter = job => job.UniqueStr != peer.UniqueStr };
    var settings = new FMSSettings();
    var device = new DownloadDevice("");
    using var sync = new MazakSync(device.Native, settings, config);
    var later = managed.RouteEndUTC.AddHours(WriteJobs.JobLookbackHours + 1);
    var required = ImmutableHashSet.Create(managed.UniqueStr, peer.UniqueStr, ordinary.UniqueStr);

    MazakState Observe() =>
      sync.ReconcileCellState(
        db,
        new MazakAcquisition
        {
          StartedAtUtc = later,
          DataLoadedAtUtc = later,
          CompletedAtUtc = later,
          Tools = null,
          Data = DownloadDevice.WithLogs(device.Data, []),
        }
      ).State;

    var initial = Observe();
    await Assert.That(WriteJobs.FindPendingDownload(initial.AllData, db, config, later)).IsEmpty();
    var planned = WriteJobs.FindPendingDownload(initial.AllData, db, config, later, required);
    await Assert.That(planned.Single().UniqueStr).IsEqualTo(managed.UniqueStr);
    device.Native.DidNotReceive().Save(Arg.Any<MazakWriteData>());

    await Assert
      .That(sync.ApplyJobActions(db, initial, allowManagedCleanup: false, required))
      .IsTrue();
    await Assert.That(db.LoadJob(managed.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(db.LoadJob(peer.UniqueStr)!.CopiedToSystem).IsFalse();
    await Assert.That(db.LoadJob(ordinary.UniqueStr)!.CopiedToSystem).IsFalse();
    await Assert.That(device.Data.Schedules.Count).IsEqualTo(2);
    await Assert
      .That(device.Data.Schedules.SelectMany(s => s.Processes).Sum(p => p.ProcessMaterialQuantity))
      .IsEqualTo(0);

    db.ArchiveJob(managed.UniqueStr);
    await Assert
      .That(sync.ApplyJobActions(db, Observe(), allowManagedCleanup: false, required))
      .IsFalse();
    await Assert.That(device.Data.Schedules.Count).IsEqualTo(2);
    await Assert
      .That(sync.ApplyJobActions(db, Observe(), additionalManagedJobs: required))
      .IsTrue();
    await Assert.That(device.Data.Schedules.Count).IsLessThan(2);
  }

  [Test]
  [Arguments(true)]
  [Arguments(false)]
  public async Task EachControllerArchivesOnlyItsOwnJobs(bool manualFirst)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var ordinary = PendingJob(false) with { UniqueStr = "ordinary" };
    var managed = PendingJob(true) with { UniqueStr = "managed" };
    var manual = new DownloadDevice("");
    var robot = new DownloadDevice("");
    var manualConfig = new MazakConfig
    {
      DBType = MazakDbType.MazakSmooth,
      ControllerSource = "manual",
      BulkJobFilter = job => job.UniqueStr == ordinary.UniqueStr,
    };
    var robotConfig = ManagedConfig() with
    {
      ControllerSource = "robot",
      BulkJobFilter = job => job.UniqueStr == managed.UniqueStr,
    };
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [ordinary, managed] }, null, false);
    if (manualFirst)
    {
      Sync(manual, db, manualConfig);
      Sync(robot, db, robotConfig);
    }
    else
    {
      Sync(robot, db, robotConfig);
      Sync(manual, db, manualConfig);
    }
    await Assert.That(manual.Data.Schedules.Single().CompleteQuantity).IsEqualTo(0);
    await Assert.That(db.LoadJob(ordinary.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(db.LoadJob(managed.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(db.LoadJob(ordinary.UniqueStr)!.Archived).IsFalse();
    await Assert.That(db.LoadJob(managed.UniqueStr)!.Archived).IsFalse();
  }

  [Test]
  [Arguments("peer-Insight", "peer-Insight")]
  [Arguments("unknown-job-Insight", "unknown-job-Insight")]
  [Arguments("unknown-job-Insight", null)]
  [Arguments("peer-Insight", "operator schedule")]
  public async Task OrdinaryDownloadPreservesNativeRowsOutsideItsScope(
    string partComment,
    string? scheduleComment
  )
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var device = new DownloadDevice("");
    var local = PendingJob(false) with { UniqueStr = "local" };
    var peer = PendingJob(false) with { UniqueStr = "peer", PartName = "peer-part" };
    var config = new MazakConfig
    {
      DBType = MazakDbType.MazakSmooth,
      BulkJobFilter = job => job.UniqueStr == peer.UniqueStr,
    };
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [peer] }, null, false);
    await Assert.That(Sync(device, db, config)).IsTrue();
    var schedule = device.Data.Schedules.Single();
    var completed = schedule with
    {
      CompleteQuantity = schedule.PlanQuantity,
      Comment = scheduleComment,
    };
    var part = device.Data.Parts.Single() with { Comment = partComment };
    device.Observe(
      device.Data with
      {
        Parts = [part],
        Schedules = scheduleComment is null ? [] : [completed],
      }
    );

    db.AddJobs(new NewJobs { ScheduleId = "second", Jobs = [local] }, null, false);
    await Assert
      .That(
        Sync(device, db, config with { BulkJobFilter = job => job.UniqueStr == local.UniqueStr })
      )
      .IsTrue();
    if (scheduleComment is not null)
      await Assert
        .That(device.Data.Schedules.Single(s => s.Id == completed.Id))
        .IsEqualTo(completed);
    await Assert.That(device.Data.Parts.Single(p => p.PartName == part.PartName)).IsEqualTo(part);
    await Assert.That(db.LoadJob(peer.UniqueStr)!.Archived).IsFalse();
  }

  [Test]
  [Arguments(2, Categories = new[] { "IncompleteManagedRoute" })]
  [Arguments(3, Categories = new[] { "CompleteManagedRoute" })]
  public async Task DriftedManagedScheduleCannotAffectOrdinaryArchival(int managedComplete)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var managedTemplate = PendingJob(true);
    var managed = managedTemplate with { Processes = [managedTemplate.Processes[0]] };
    var ordinary = PendingJob(false) with
    {
      UniqueStr = "ordinary",
      PartName = "ordinary-part",
      Cycles = 10,
    };
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [managed, ordinary] }, null, false);
    await Assert.That(Sync(device, db)).IsTrue();
    var managedSchedule = device.Data.Schedules.Single(s =>
      MazakPart.UniqueFromComment(s.Comment) == managed.UniqueStr
    );
    var ordinarySchedule = device.Data.Schedules.Single(s =>
      MazakPart.UniqueFromComment(s.Comment) == ordinary.UniqueStr
    );
    var drifted = managedSchedule with
    {
      Comment = ordinarySchedule.Comment,
      PlanQuantity = 3,
      CompleteQuantity = managedComplete,
    };
    device.Observe(
      device.Data with
      {
        Schedules = [ordinarySchedule with { PlanQuantity = 4, CompleteQuantity = 4 }, drifted],
      }
    );

    db.ArchiveJob(managed.UniqueStr);
    var next = ordinary with { UniqueStr = "next", PartName = "next-part" };
    db.AddJobs(new NewJobs { ScheduleId = "second", Jobs = [next] }, null, false);
    await Assert.That(Sync(device, db)).IsTrue();
    var archived = db.LoadJob(ordinary.UniqueStr)!;
    await Assert.That(archived.Archived).IsTrue();
    await Assert.That(archived.Decrements!.Single().Quantity).IsEqualTo(6);
    await Assert.That(db.LoadJob(next.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert
      .That(device.Data.Schedules.Single(s => s.Id == managedSchedule.Id))
      .IsEqualTo(drifted);
  }

  [Test]
  public async Task CompletedManagedScheduleTakesNineAttemptsForSixValidCompletions()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var config = ManagedConfig();
    var settings = new FMSSettings { Queues = { ["rework"] = new QueueInfo() } };
    var device = new DownloadDevice("schedules-partial");
    var managed = PendingJob(true) with { UniqueStr = "managed", Cycles = 6 };
    var ordinary = PendingJob(false) with
    {
      UniqueStr = "ordinary",
      PartName = "ordinary-part",
      Cycles = 6,
    };
    db.AddJobs(new NewJobs { ScheduleId = "initial", Jobs = [managed, ordinary] }, null, false);
    bool Poll(IRepository repository) => Sync(device, repository, config, settings);
    await Assert.That(() => Poll(db)).Throws<IOException>();
    var installedBeforeRetry = device.Data.Schedules.Single();
    using var restarted = repositories.OpenConnection();
    await Assert.That(Poll(restarted)).IsTrue();
    await Assert
      .That(device.Data.Schedules.Single(s => s.Id == installedBeforeRetry.Id))
      .IsEqualTo(installedBeforeRetry);
    await Assert.That(db.LoadJob(managed.UniqueStr)!.CopiedToSystem).IsTrue();
    var p1 = device.Data.Schedules.Single(s => s.Comment == "managed-1-1-InsightS");
    var p2 = device.Data.Schedules.Single(s => s.Comment == "managed-2-1-InsightS");
    await Assert.That(p1.Processes.Single().ProcessMaterialQuantity).IsEqualTo(0);
    await Assert.That(p2.Processes.Single().ProcessMaterialQuantity).IsEqualTo(0);

    var ids = Enumerable
      .Range(0, 6)
      .Select(_ => db.AllocateMaterialID(managed.UniqueStr, managed.PartName, 2))
      .ToImmutableList();
    var attempts = 0;
    BlackMaple.MachineFramework.LogEntry Transfer(
      MazakScheduleRow route,
      LogCode code,
      ImmutableList<long> material
    ) =>
      TranslateManagedTransfer(
        restarted,
        device.Data,
        route,
        code,
        material.Count,
        ++attempts,
        material,
        config: config,
        settings: settings
      );
    foreach (var group in Enumerable.Chunk(ids, 3).Select(g => g.ToImmutableList()))
    {
      Transfer(p1, LogCode.LoadEnd, group);
      Transfer(p1, LogCode.UnloadEnd, group);
      Transfer(p2, LogCode.LoadEnd, group);
      Transfer(p2, LogCode.UnloadEnd, group);
    }
    device.Observe(
      device.Data with
      {
        Schedules = device
          .Data.Schedules.Select(s =>
            s with
            {
              CompleteQuantity = 6,
              Processes = s
                .Processes.Select(p => p with { ProcessMaterialQuantity = 0 })
                .ToImmutableList(),
            }
          )
          .ToImmutableList(),
      }
    );
    db.AddJobs(
      new NewJobs
      {
        ScheduleId = "next",
        Jobs = [ordinary with { UniqueStr = "next", PartName = "next-part" }],
      },
      null,
      false
    );
    await Assert.That(Poll(restarted)).IsTrue();
    await Assert.That(device.Data.Schedules.Any(s => s.Comment == "ordinary-Insight")).IsFalse();
    await Assert
      .That(device.Data.Schedules.Single(s => s.Id == p2.Id).CompleteQuantity)
      .IsEqualTo(6);

    var repeated = ids.Take(3).ToImmutableList();
    db.InvalidatePalletCycle(repeated[0], 2, "test", Now).ToImmutableList();
    await Assert.That(db.GetJobProductionSummary(managed.UniqueStr).Completed[(2, 1)]).IsEqualTo(3);
    foreach (var serial in repeated)
      db.RecordAddMaterialToQueue(serial, 1, "rework", -1, "test", "rework");
    await Assert
      .That(() =>
        MazakManagedScheduleControl.SetMaterialOffer(
          restarted,
          config,
          device.Data,
          managed.UniqueStr,
          2,
          3
        )
      )
      .Throws<ConflictRequestException>();
    var capacity = MazakManagedScheduleControl.EnsurePlan(
      restarted,
      config,
      device.Data,
      managed.UniqueStr,
      2,
      9
    )!;
    await Assert.That(capacity.Schedules.Single().Id).IsEqualTo(p2.Id);
    device.AfterSave = _ => throw new IOException("Lost plan edit reply");
    await Assert.That(() => device.Native.Save(capacity)).Throws<IOException>();
    device.AfterSave = null!;
    using var afterLostReply = repositories.OpenConnection();
    await Assert
      .That(
        MazakManagedScheduleControl.EnsurePlan(
          afterLostReply,
          config,
          device.Data,
          managed.UniqueStr,
          2,
          9
        )
      )
      .IsNull();
    await Assert
      .That(
        device.Data.Schedules.Single(s => s.Id == p2.Id).Processes.Single().ProcessMaterialQuantity
      )
      .IsEqualTo(0);
    device.Native.Save(
      MazakManagedScheduleControl.SetMaterialOffer(
        afterLostReply,
        config,
        device.Data,
        managed.UniqueStr,
        2,
        3
      )!
    );
    var reloaded = Transfer(p2, LogCode.LoadEnd, repeated);
    await Assert.That(reloaded.Material.Select(m => m.MaterialID)).IsEquivalentTo(repeated);
    await Assert.That(db.GetMaterialInAllQueues()).IsEmpty();
    Transfer(p2, LogCode.UnloadEnd, repeated);
    device.Observe(
      device.Data with
      {
        Schedules = device
          .Data.Schedules.Select(s =>
            s.Id == p2.Id
              ? s with
              {
                CompleteQuantity = 9,
                Processes = [s.Processes.Single() with { ProcessMaterialQuantity = 0 }],
              }
              : s
          )
          .ToImmutableList(),
      }
    );
    var counts = MazakManagedScheduleControl.RecordedLoads(
      afterLostReply,
      config,
      managed.UniqueStr,
      2
    )!;
    await Assert.That(counts.IdentifiedQuantity).IsEqualTo(9L);
    await Assert.That(counts.UnidentifiedQuantity).IsEqualTo(0L);
    var summary = db.GetJobProductionSummary(managed.UniqueStr);
    await Assert.That(summary.AutomationEntryMaterialIds).IsEquivalentTo(ids);
    await Assert.That(summary.Completed[(2, 1)]).IsEqualTo(6);
    var projected = BuildCurrentStatus.Build(db, settings, config, device.Data, "MC", null, Now);
    await Assert.That(projected.Jobs[managed.UniqueStr].Cycles).IsEqualTo(6);
    await Assert.That(projected.Jobs[managed.UniqueStr].Completed![1][0]).IsEqualTo(6);
  }

  [Test]
  public async Task CombinedRouteOffersOnlyEntryMaterialAndLeavesDownstreamWorkOnItsPallet()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var job = PendingJob(false);
    var config = ManagedConfig() with
    {
      UseInsightJobQuantities = j => j.UniqueStr == job.UniqueStr,
    };
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "combined", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db, config)).IsTrue();
    var installed = device.Data.Schedules.Single();
    var entry = installed.Processes.Single(p => p.ProcessNumber == 1);
    var downstream = installed.Processes.Single(p => p.ProcessNumber == 2) with
    {
      ProcessExecuteQuantity = 2,
      ProcessMaterialQuantity = 1,
    };
    device.Observe(
      device.Data with
      {
        Schedules = [installed with { CompleteQuantity = 4, Processes = [entry, downstream] }],
      }
    );

    var plan = MazakManagedScheduleControl.EnsurePlan(
      db,
      config,
      device.Data,
      job.UniqueStr,
      2,
      12
    )!;
    await Assert.That(plan.Schedules.Single().Processes).IsEmpty();
    device.Native.Save(plan);
    var offer = MazakManagedScheduleControl.SetMaterialOffer(
      db,
      config,
      device.Data,
      job.UniqueStr,
      1,
      3
    )!;
    await Assert.That(offer.Schedules.Single().Processes.Single().ProcessNumber).IsEqualTo(1);
    device.Native.Save(offer);
    var zero = MazakManagedScheduleControl.WithdrawMaterialOffer(
      db,
      config,
      device.Data,
      job.UniqueStr,
      1
    );
    device.Native.Save(zero);
    var repeatedZero = MazakManagedScheduleControl.WithdrawMaterialOffer(
      db,
      config,
      device.Data,
      job.UniqueStr,
      1
    );
    await Assert
      .That(repeatedZero.Schedules.Single().Processes.Single().ProcessMaterialQuantity)
      .IsEqualTo(0);
    await Assert
      .That(device.Data.Schedules.Single().Processes.Single(p => p.ProcessNumber == 2))
      .IsEqualTo(downstream);
    await Assert.That(db.LoadJob(job.UniqueStr)!.Cycles).IsEqualTo(9);

    var wrongComment = device.Data with
    {
      Schedules = [installed with { Comment = "other-Insight" }],
    };
    var duplicate = device.Data with
    {
      Schedules = [installed, installed with { Id = installed.Id + 1 }],
    };
    var missing = device.Data with { Schedules = [] };
    foreach (var observed in new[] { wrongComment, duplicate, missing })
    {
      await Assert
        .That(() =>
          MazakManagedScheduleControl.EnsurePlan(db, config, observed, job.UniqueStr, 1, 11)
        )
        .Throws<ConflictRequestException>();
      await Assert
        .That(() =>
          MazakManagedScheduleControl.WithdrawMaterialOffer(db, config, observed, job.UniqueStr, 1)
        )
        .Throws<ConflictRequestException>();
    }
  }

  [Test]
  public async Task ArchiveRetiresAnIdleUnequalScheduleAfterTheFinalUnloadSettles()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var config = ManagedConfig();
    var device = new DownloadDevice("");
    var job = PendingJob(true);
    db.AddJobs(new NewJobs { ScheduleId = "archive", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db, config)).IsTrue();
    using var sync = new MazakSync(device.Native, new FMSSettings(), config);
    var route = device.Data.Schedules.Single(s => s.Comment == job.UniqueStr + "-2-1-InsightS");
    var completed = route with { CompleteQuantity = 1 };
    device.Observe(device.Data with { Schedules = [completed] });
    var action = new LoadAction
    {
      Comment = route.Comment,
      Part = job.PartName,
      Process = 1,
      Qty = 1,
      LoadStation = 1,
    };
    var process = completed.Processes.Single();
    var finalLoad = new MazakMachineInterface.LogEntry
    {
      Code = LogCode.UnloadEnd,
      FullPartName = route.PartName,
      JobPartName = job.PartName,
      ForeignID = "LG20261111-115000-001.csv",
      TimeUTC = Now.AddMinutes(-1),
      FixedQuantity = 1,
      Pallet = 2,
      StationNumber = 1,
      Process = 1,
    };
    MazakState Read(MazakAllData readback, params MazakMachineInterface.LogEntry[] logs) =>
      sync.ReconcileCellState(
        db,
        new MazakAcquisition
        {
          StartedAtUtc = Now,
          DataLoadedAtUtc = Now,
          CompletedAtUtc = Now,
          Tools = null,
          Data = DownloadDevice.WithLogs(readback, [.. logs]),
        }
      ).State;

    db.ArchiveJob(job.UniqueStr);
    var pending = Read(device.Data, finalLoad);
    await Assert.That(pending.StoppedBecauseRecentLogEvent).IsTrue();
    await Assert.That(sync.ApplyJobActions(db, pending)).IsFalse();
    var idle = Read(
      device.Data,
      finalLoad,
      finalLoad with
      {
        Code = LogCode.LoadBegin,
        ForeignID = "LG20261111-115000-002.csv",
      }
    );
    await Assert.That(idle.StoppedBecauseRecentLogEvent).IsFalse();
    await Assert
      .That(db.MostRecentLogEntryForForeignID(finalLoad.ForeignID).Result)
      .IsEqualTo("UNLOAD");
    foreach (
      var active in new[]
      {
        device.Data with
        {
          LoadActions = [action],
        },
        device.Data with
        {
          PalletSubStatuses = [device.Occupancy(route, 1)],
        },
        device.Data with
        {
          Schedules =
          [
            completed with
            {
              Processes = [process with { ProcessExecuteQuantity = 1 }],
            },
          ],
        },
        device.Data with
        {
          Schedules =
          [
            completed with
            {
              Processes = [process with { ProcessMaterialQuantity = 1 }],
            },
          ],
        },
      }
    )
      await Assert.That(sync.ApplyJobActions(db, Read(active))).IsFalse();
    await Assert.That(device.Data.Schedules.Single().Id).IsEqualTo(route.Id);
    device.AfterSave = write =>
    {
      if (write.Schedules.Any(s => s.Command == MazakWriteCommand.Delete))
        throw new IOException("Lost delete acknowledgement");
    };
    await Assert.That(() => sync.ApplyJobActions(db, idle)).Throws<IOException>();
    device.AfterSave = null!;
    using var reopened = repositories.OpenConnection();
    using var restarted = new MazakSync(device.Native, new FMSSettings(), config);
    await Assert.That(restarted.ApplyJobActions(reopened, Read(device.Data))).IsFalse();
    await Assert.That(device.Data.Schedules).IsEmpty();
  }

  [Test]
  public async Task NextDownloadReclaimsUnusedDefinitionsAndPreservesOperatorPalletMappings()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var config = ManagedConfig();
    var managed = PendingJob(true);
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "pending", Jobs = [managed] }, null, false);
    await Assert.That(Sync(device, db, config with { UseInsightJobQuantities = null })).IsTrue();

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

    db.ArchiveJob(managed.UniqueStr);
    using var reopened = repositories.OpenConnection();
    using var sync = new MazakSync(device.Native, new FMSSettings(), config);
    MazakState Read() =>
      sync.ReconcileCellState(
        reopened,
        new MazakAcquisition
        {
          StartedAtUtc = Now,
          DataLoadedAtUtc = Now,
          CompletedAtUtc = Now,
          Tools = null,
          Data = DownloadDevice.WithLogs(device.Data, []),
        }
      ).State;
    foreach (var _ in device.Data.Schedules.ToImmutableList())
      await Assert.That(sync.ApplyJobActions(reopened, Read())).IsTrue();
    await Assert.That(device.Data.Schedules).IsEmpty();
    var next = PendingJob(false) with { UniqueStr = "next", PartName = "different" };
    db.AddJobs(new NewJobs { ScheduleId = "next", Jobs = [next] }, null, false);
    await Assert.That(sync.ApplyJobActions(reopened, Read())).IsTrue();
    await Assert.That(device.Data.Parts.Any(p => p.PartName == part.PartName)).IsFalse();
    await Assert.That(device.Data.Pallets.Contains(mapping)).IsTrue();
    await Assert.That(device.Data.Fixtures.Contains(fixture)).IsTrue();
  }

  [Test]
  [Arguments("pieces per fixture")]
  [Arguments("eligible pallets")]
  public async Task InterruptedManagedDownloadWithNativeDriftLetsNextBatchDownload(string drift)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var managed = PendingJob(true);
    var ordinary = PendingJob(false) with { UniqueStr = "ordinary", PartName = "ordinary-part" };
    var config = ManagedConfig();
    var device = new DownloadDevice("schedules");
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [managed] }, null, false);
    await Assert.That(() => Sync(device, db, config)).Throws<IOException>();
    await Assert.That(device.Data.Schedules).IsEmpty();
    var part = device.Data.Parts.Single(p => p.Comment == "initial-job-1-1-InsightS");
    var process = part.Processes.Single();
    var mapping = device.Data.Pallets.Single(p => p.Fixture == process.Fixture);
    device.Observe(
      drift == "pieces per fixture"
        ? device.Data with
        {
          Parts = device
            .Data.Parts.ToImmutableList()
            .Replace(
              part,
              part with
              {
                Processes = [process with { FixQuantity = process.FixQuantity + 1 }],
              }
            ),
        }
        : device.Data with
        {
          Pallets = [.. device.Data.Pallets, mapping with { PalletNumber = 19 }],
        }
    );
    db.AddJobs(new NewJobs { ScheduleId = "second", Jobs = [ordinary] }, null, false);

    using var reopened = repositories.OpenConnection();
    await Assert.That(Sync(device, reopened, config)).IsTrue();
    await Assert.That(device.Data.Schedules.Single().Comment).IsEqualTo("ordinary-Insight");
    await Assert.That(reopened.LoadJob(ordinary.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(reopened.LoadJob(managed.UniqueStr)!.CopiedToSystem).IsFalse();
    await Assert.That(Sync(device, reopened, config)).IsFalse();
    var alarm = MazakLiveRoutes.Diagnostics(reopened, config, device.Data, Now).Single();
    await Assert.That(alarm.Code).IsEqualTo("mazak-managed-schedule-unavailable");
    await Assert.That(alarm.Message).Contains(drift);
    await Assert.That(alarm.Message).Contains(managed.UniqueStr);
  }

  [Test]
  public async Task ConflictingLiveScheduleCannotConfirmAnOrdinaryDownload()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var managed = PendingJob(true);
    var statusTime = DateTime.UtcNow;
    var ordinary = PendingJob(false) with
    {
      UniqueStr = "ordinary",
      Cycles = 10,
      RouteStartUTC = statusTime.AddHours(-1),
      RouteEndUTC = statusTime.AddDays(1),
    };
    var config = ManagedConfig();
    var settings = new FMSSettings();
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [managed] }, null, false);
    await Assert.That(Sync(device, db)).IsTrue();
    var nativeSchedule = device.Data.Schedules.Single(s => s.Comment == "initial-job-2-1-InsightS");
    var nativePart = device.Data.Parts.Single(p => p.PartName == nativeSchedule.PartName);
    var occupied = device.PalletReadback(nativeSchedule, quantity: 1);
    var materialID = db.AllocateMaterialID(
      managed.UniqueStr,
      managed.PartName,
      managed.Processes.Count
    );
    db.RecordSerialForMaterialID(materialID, 0, "managed-serial", Now.AddHours(-2));
    var load = TranslateManagedTransfer(
      db,
      occupied,
      nativeSchedule,
      LogCode.LoadEnd,
      1,
      1,
      [materialID],
      config: config,
      settings: settings
    );
    await Assert.That(load.Material.Single().MaterialID).IsEqualTo(materialID);

    const string ordinaryComment = "ordinary-Insight";
    var driftedSchedule = nativeSchedule with
    {
      Comment = ordinaryComment,
      PlanQuantity = 15,
      CompleteQuantity = 3,
    };
    device.Observe(
      occupied with
      {
        Schedules = occupied.Schedules.ToImmutableList().Replace(nativeSchedule, driftedSchedule),
      }
    );

    db.AddJobs(new NewJobs { ScheduleId = "second", Jobs = [ordinary] }, null, false);
    bool Poll(IRepository repository) => Sync(device, repository, config, settings, statusTime);
    CurrentStatus Status(MazakAllData observed) =>
      BuildCurrentStatus.Build(db, settings, config, observed, "MC", null, statusTime);
    var beforeDownload = Status(device.Data);
    await Assert.That(beforeDownload.Jobs[ordinary.UniqueStr].CopiedToSystem).IsFalse();
    await Assert.That(beforeDownload.Jobs[ordinary.UniqueStr].Cycles).IsEqualTo(ordinary.Cycles);
    var knownManagedMaterial = beforeDownload.Material.Single(m => m.MaterialID == materialID);
    await Assert.That(knownManagedMaterial.Serial).IsEqualTo("managed-serial");
    await Assert.That(knownManagedMaterial.Process).IsEqualTo(2);
    await Assert.That(knownManagedMaterial.Location.PalletNum).IsEqualTo(2);

    device.BeforeSave = write =>
    {
      if (write.Prefix == "Add Schedules")
        throw new IOException("Ordinary schedule was not installed.");
    };
    await Assert.That(() => Poll(db)).Throws<IOException>();
    await Assert.That(db.LoadJob(ordinary.UniqueStr)!.CopiedToSystem).IsFalse();
    var ordinaryPart = device.Data.Parts.Single(p =>
      p.Comment == ordinaryComment && p.PartName != nativePart.PartName
    );
    await Assert
      .That(device.Data.Schedules.Single(s => s.Comment == ordinaryComment))
      .IsEqualTo(driftedSchedule);

    device.BeforeSave = null!;
    using var reopened = repositories.OpenConnection();
    await Assert.That(Poll(reopened)).IsTrue();
    await Assert.That(reopened.LoadJob(ordinary.UniqueStr)!.CopiedToSystem).IsTrue();
    var ordinarySchedule = device.Data.Schedules.Single(s => s.PartName == ordinaryPart.PartName);
    await Assert.That(ordinarySchedule.Comment).IsEqualTo(ordinaryComment);
    await Assert
      .That(device.Data.Schedules.Single(s => s.Id == nativeSchedule.Id))
      .IsEqualTo(driftedSchedule);
    var published = Status(device.Data).Jobs[ordinary.UniqueStr];
    await Assert.That(published.Cycles).IsEqualTo(ordinary.Cycles);
    await Assert.That(published.RemainingToStart).IsEqualTo(ordinary.Cycles);
    await Assert.That(published.Completed!.SelectMany(counts => counts)).IsEquivalentTo([0, 0]);

    var unconfirmed = device.Data with
    {
      Schedules = device
        .Data.Schedules.ToImmutableList()
        .Replace(driftedSchedule, driftedSchedule with { Id = 1001 }),
    };
    await Assert
      .That(Status(unconfirmed).Jobs[ordinary.UniqueStr].Cycles)
      .IsEqualTo(ordinary.Cycles);
  }

  [Test]
  [Arguments("pieces per fixture")]
  [Arguments("eligible machines")]
  [Arguments("eligible load stations")]
  [Arguments("eligible unload stations")]
  [Arguments("eligible pallets")]
  [Arguments("verify the program")]
  public async Task MaterialOfferRequiresTheInstalledExecutionRoute(string mismatch)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var device = new DownloadDevice("");
    var config = ManagedConfig();
    var job = PendingJob(true);
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db)).IsTrue();

    var offer = MazakManagedScheduleControl.SetMaterialOffer(
      db,
      config,
      device.Data,
      job.UniqueStr,
      2,
      1
    )!;
    device.Native.Save(offer);
    var partName = offer.Schedules.Single().PartName;
    await Assert
      .That(
        MazakManagedScheduleControl.VerifyInstalledRoute(db, config, device.Data, job.UniqueStr, 2)
      )
      .IsEqualTo(device.Data.Schedules.Single(s => s.PartName == partName));
    var part = device.Data.Parts.Single(p => p.PartName == partName);
    var process = part.Processes.Single();
    var changed = mismatch switch
    {
      "pieces per fixture" => process with { FixQuantity = 1 },
      "eligible machines" => process with { CutMc = "2" },
      "eligible load stations" => process with { FixLDS = "2" },
      "eligible unload stations" => process with { RemoveLDS = "2" },
      "verify the program" => process with { MainProgram = "1001" },
      _ => process,
    };
    var observed = device.Data with
    {
      Parts = device
        .Data.Parts.Select(p => p.PartName == partName ? p with { Processes = [changed] } : p)
        .ToImmutableList(),
      Pallets =
        mismatch == "eligible pallets"
          ? device.Data.Pallets.Where(p => p.Fixture != process.Fixture).ToImmutableList()
          : device.Data.Pallets,
    };
    await Assert
      .That(() =>
        MazakManagedScheduleControl.SetMaterialOffer(db, config, observed, job.UniqueStr, 2, 2)
      )
      .Throws<ConflictRequestException>()
      .WithMessageContaining(mismatch);
    await Assert
      .That(() =>
        MazakManagedScheduleControl.VerifyInstalledRoute(db, config, observed, job.UniqueStr, 2)
      )
      .Throws<ConflictRequestException>()
      .WithMessageContaining(mismatch);
    await Assert
      .That(() =>
        MazakManagedScheduleControl.EnsurePlan(
          db,
          config,
          observed,
          job.UniqueStr,
          2,
          job.Cycles + 1
        )
      )
      .Throws<ConflictRequestException>()
      .WithMessageContaining(mismatch);
    var alarm = MazakLiveRoutes
      .Diagnostics(db, config, observed, Now)
      .Single(a => a.Code == "mazak-managed-schedule-unavailable");
    await Assert.That(alarm.Message).Contains(mismatch);
    await Assert.That(alarm.Message).Contains(partName);
    await Assert
      .That(MazakManagedScheduleControl.SetMaterialOffer(db, config, observed, job.UniqueStr, 1, 1))
      .IsNotNull();
    var withdrawal = MazakManagedScheduleControl.SetMaterialOffer(
      db,
      config,
      observed,
      job.UniqueStr,
      2,
      0
    );
    var zero = withdrawal!.Schedules.Single();
    await Assert.That(zero.Processes.Single().ProcessMaterialQuantity).IsEqualTo(0);
    var zeroReadback = observed with
    {
      Schedules = observed
        .Schedules.Select(row => row.Id == zero.Id ? zero : row)
        .ToImmutableList(),
    };
    await Assert
      .That(
        MazakManagedScheduleControl.SetMaterialOffer(db, config, zeroReadback, job.UniqueStr, 2, 0)
      )
      .IsNull();
    var repeat = MazakManagedScheduleControl.WithdrawMaterialOffer(
      db,
      config,
      zeroReadback,
      job.UniqueStr,
      2
    );
    await Assert.That(repeat.Schedules).IsEquivalentTo(withdrawal.Schedules);
  }

  private static BlackMaple.MachineFramework.LogEntry TranslateManagedTransfer(
    IRepository db,
    MazakAllData data,
    MazakScheduleRow row,
    LogCode code,
    int quantity,
    int sequence,
    ImmutableList<long>? identified = null,
    MazakConfig? config = null,
    FMSSettings? settings = null
  )
  {
    config ??= ManagedConfig();
    var log = new MazakMachineInterface.LogEntry
    {
      Code = code,
      ForeignID = $"LG20261111-115000-{sequence * 2:000}.csv",
      TimeUTC = Now.AddMinutes(sequence - 90),
      FullPartName = row.PartName,
      JobPartName = row.PartName.Split(':', 2)[0],
      FixedQuantity = quantity,
      Pallet = 2,
      StationNumber = 1,
      Process = 1,
    };
    var captured = DownloadDevice.WithLogs(
      data,
      [
        log,
        log with
        {
          Code = LogCode.LoadBegin,
          ForeignID = $"LG20261111-115000-{sequence * 2 + 1:000}.csv",
        },
      ]
    );
    var resolver = config with
    {
      ResolveLoadUnloadTransaction = identified is null
        ? null
        : (_, batch) =>
          new MazakLoadUnloadResolution.Resolved
          {
            MaterialForLoads = batch.Loads.ToImmutableDictionary(
              f => f.ForeignId,
              f => new MazakResolvedLoad { MaterialIds = identified }
            ),
            MaterialForUnloads = batch.Unloads.ToImmutableDictionary(
              f => f.ForeignId,
              f => new MazakResolvedUnload
              {
                MaterialIDToDestination = identified.ToImmutableDictionary(
                  id => id,
                  _ => (UnloadDestination?)null
                ),
              }
            ),
          },
    };
    LogTranslation.HandleEvents(
      db,
      captured,
      "MC",
      settings ?? new FMSSettings(),
      _ => { },
      resolver,
      [],
      Now
    );
    return db.MostRecentLogEntryForForeignID(config.LogForeignId(log.ForeignID));
  }
}
