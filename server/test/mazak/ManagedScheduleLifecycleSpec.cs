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
  [Arguments("pieces per fixture")]
  [Arguments("eligible machines")]
  [Arguments("eligible load stations")]
  [Arguments("eligible unload stations")]
  [Arguments("eligible pallets")]
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
    var part = device.Data.Parts.Single(p => p.PartName == partName);
    var process = part.Processes.Single();
    var changed = mismatch switch
    {
      "pieces per fixture" => process with { FixQuantity = 1 },
      "eligible machines" => process with { CutMc = "2" },
      "eligible load stations" => process with { FixLDS = "2" },
      "eligible unload stations" => process with { RemoveLDS = "2" },
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
    var alarm = ManagedMazakSchedules
      .Diagnostics(db, config, observed)
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
    await Assert
      .That(withdrawal!.Schedules.Single().Processes.Single().ProcessMaterialQuantity)
      .IsEqualTo(0);
  }

  [Test]
  [Arguments(true)]
  [Arguments(false)]
  public async Task EachControllerArchivesOnlyItsOwnJobs(bool manualFirst)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var settings = new FMSSettings();
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
    void Poll(DownloadDevice device, MazakConfig config) =>
      WriteJobs.SyncFromDatabase(device.Data, db, device.Native, settings, config, Now);
    if (manualFirst)
    {
      Poll(manual, manualConfig);
      Poll(robot, robotConfig);
    }
    else
    {
      Poll(robot, robotConfig);
      Poll(manual, manualConfig);
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
    bool Poll(MazakConfig c) =>
      WriteJobs.SyncFromDatabase(device.Data, db, device.Native, new FMSSettings(), c, Now);
    await Assert.That(Poll(config)).IsTrue();
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
      .That(Poll(config with { BulkJobFilter = job => job.UniqueStr == local.UniqueStr }))
      .IsTrue();
    if (scheduleComment is not null)
      await Assert
        .That(device.Data.Schedules.Single(s => s.Id == completed.Id))
        .IsEqualTo(completed);
    await Assert.That(device.Data.Parts.Single(p => p.PartName == part.PartName)).IsEqualTo(part);
    await Assert.That(db.LoadJob(peer.UniqueStr)!.Archived).IsFalse();
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
    bool Poll(IRepository repository) =>
      WriteJobs.SyncFromDatabase(device.Data, repository, device.Native, settings, config, Now);
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
        config,
        settings,
        device.Data,
        route,
        managed.PartName,
        code,
        material.Count,
        ++attempts,
        material
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
  public async Task ArchivedUnequalScheduleWaitsForIdleReadbackAndRetainsLateUnloadAttribution()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var config = ManagedConfig() with { ControllerSource = "secondary" };
    var device = new DownloadDevice("");
    var settings = new FMSSettings();
    var job = PendingJob(true) with { Cycles = 6 };
    var peer = PendingJob(false) with { UniqueStr = "peer", PartName = "peer-part" };
    db.AddJobs(new NewJobs { ScheduleId = "initial", Jobs = [job, peer] }, null, false);
    bool Poll(IRepository repo) =>
      WriteJobs.SyncFromDatabase(device.Data, repo, device.Native, settings, config, Now);
    await Assert.That(Poll(db)).IsTrue();
    var p2 = device.Data.Schedules.Single(s => s.Comment == "initial-job-2-1-InsightS");
    var missingIdentity = TranslateManagedTransfer(
      db,
      config,
      settings,
      device.Data,
      p2,
      job.PartName,
      LogCode.LoadEnd,
      1,
      1,
      null
    );
    await Assert.That(missingIdentity.Material).IsEmpty();
    var count = MazakManagedScheduleControl.RecordedLoads(db, config, job.UniqueStr, 2)!;
    await Assert.That(count.PhysicalQuantity).IsEqualTo(1L);
    await Assert.That(count.UnidentifiedQuantity).IsEqualTo(1L);
    TranslateManagedTransfer(
      db,
      config,
      settings,
      device.Data,
      p2,
      job.PartName,
      LogCode.UnloadEnd,
      1,
      2,
      null
    );
    var ids = Enumerable
      .Range(0, 3)
      .Select(_ => db.AllocateMaterialID(job.UniqueStr, job.PartName, 2))
      .ToImmutableList();
    TranslateManagedTransfer(
      db,
      config,
      settings,
      device.Data,
      p2,
      job.PartName,
      LogCode.LoadEnd,
      3,
      3,
      ids
    );
    var fixture = device
      .Data.Parts.Single(p => p.PartName == p2.PartName)
      .Processes.Single()
      .Fixture;
    device.Observe(
      device.Data with
      {
        Parts = device
          .Data.Parts.Select(p =>
            p.Comment == "peer-Insight"
              ? p with
              {
                Processes = p
                  .Processes.Select(process => process with { Fixture = fixture })
                  .ToImmutableList(),
              }
              : p
          )
          .ToImmutableList(),
      }
    );
    db.ArchiveJob(job.UniqueStr);
    void Guard(int ready, int executing, int? fixedQuantity, bool requested)
    {
      device.Observe(
        device.Data with
        {
          Schedules = device
            .Data.Schedules.Select(s =>
              s.Id == p2.Id
                ? s with
                {
                  CompleteQuantity = 1,
                  Processes =
                  [
                    s.Processes.Single() with
                    {
                      ProcessMaterialQuantity = ready,
                      ProcessExecuteQuantity = executing,
                    },
                  ],
                }
                : s
            )
            .ToImmutableList(),
          PalletSubStatuses = fixedQuantity.HasValue
            ?
            [
              new()
              {
                ScheduleID = p2.Id,
                PartName = p2.PartName,
                FixQuantity = fixedQuantity.Value,
                FixtureName = fixture,
                PalletNumber = 2,
              },
            ]
            : [],
          LoadActions = requested
            ?
            [
              new()
              {
                Comment = p2.Comment,
                Qty = 3,
                LoadStation = 1,
              },
            ]
            : [],
        }
      );
    }
    foreach (
      var guard in new (int Ready, int Executing, int? FixedQuantity, bool Requested)[]
      {
        (1, 0, null, false),
        (0, 1, null, false),
        (0, 0, 3, false),
        (0, 0, null, true),
      }
    )
    {
      Guard(guard.Ready, guard.Executing, guard.FixedQuantity, guard.Requested);
      // Let cleanup finish the other split route before checking this route's guard.
      for (var i = 0; i < 8 && Poll(db); i++) { }
      await Assert.That(Poll(db)).IsFalse();
      await Assert.That(device.Data.Schedules.Any(s => s.Id == p2.Id)).IsTrue();
    }
    Guard(0, 0, 0, false);
    device.AfterSave = write =>
    {
      if (write.Schedules.Any(s => s.Id == p2.Id && s.Command == MazakWriteCommand.Delete))
        throw new IOException("Lost archived schedule delete reply");
    };
    await Assert.That(() => Poll(db)).Throws<IOException>();
    await Assert.That(device.Data.Schedules.Any(s => s.Id == p2.Id)).IsFalse();
    device.AfterSave = null!;
    using var reopened = repositories.OpenConnection();
    for (var i = 0; i < 8 && Poll(reopened); i++) { }
    device
      .Native.Received(1)
      .Save(
        Arg.Is<MazakWriteData>(write =>
          write.Parts.Any(part =>
            part.PartName == p2.PartName
            && part.Command == MazakWriteCommand.Delete
            && part.TotalProcess == 1
            && part.Processes.Count == 0
          )
        )
      );
    await Assert.That(db.LoadMazakManagedSchedules(config.ControllerSource)).IsEmpty();
    await Assert
      .That(
        device.Data.Parts.All(p => !p.Comment.StartsWith("initial-job", StringComparison.Ordinal))
      )
      .IsTrue();
    await Assert.That(device.Data.Pallets.Any(p => p.Fixture == fixture)).IsTrue();
    var surviving = device.Data.Schedules.Single() with { Id = p2.Id };
    device.Observe(device.Data with { Schedules = [surviving] });
    var late = TranslateManagedTransfer(
      reopened,
      config,
      settings,
      device.Data,
      p2,
      job.PartName,
      LogCode.UnloadEnd,
      3,
      4,
      ids
    );
    await Assert.That(late.Material.Select(m => m.MaterialID)).IsEquivalentTo(ids);
    await Assert
      .That(late.Material.All(m => m.JobUniqueStr == job.UniqueStr && m.Process == 2))
      .IsTrue();
    await Assert.That(db.GetJobProductionSummary(peer.UniqueStr).Completed).IsEmpty();
  }

  [Test]
  public async Task ConfirmedRouteCannotBeReplacedOrEditedThroughCopiedComment()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var config = ManagedConfig();
    var job = PendingJob(true);
    var device = new DownloadDevice("");
    var settings = new FMSSettings();
    db.AddJobs(new NewJobs { ScheduleId = "initial", Jobs = [job] }, null, false);
    bool Poll() =>
      WriteJobs.SyncFromDatabase(device.Data, db, device.Native, settings, config, Now);
    await Assert.That(Poll()).IsTrue();
    var p2 = device.Data.Schedules.Single(s => s.Comment == "initial-job-2-1-InsightS");
    var installed = device.Data;
    var altered = installed with
    {
      Parts = installed
        .Parts.Select(p =>
          p.PartName == p2.PartName
            ? p with
            {
              Processes = p
                .Processes.Select(proc => proc with { MainProgram = "9999" })
                .ToImmutableList(),
            }
            : p
        )
        .ToImmutableList(),
    };
    await Assert
      .That(() =>
        MazakManagedScheduleControl.SetMaterialOffer(db, config, altered, job.UniqueStr, 2, 1)
      )
      .Throws<ConflictRequestException>();
    var clonedPart = device.Data.Parts.Single(p => p.PartName == p2.PartName) with
    {
      PartName = job.PartName + ":100:2:2",
    };
    var peerSchedule = p2 with { PartName = clonedPart.PartName };
    device.Observe(
      device.Data with
      {
        Parts = device
          .Data.Parts.Where(p => p.PartName != p2.PartName)
          .Append(clonedPart)
          .ToImmutableList(),
        Schedules = device
          .Data.Schedules.Where(s => s.Id != p2.Id)
          .Append(peerSchedule)
          .ToImmutableList(),
      }
    );
    await Assert.That(Poll()).IsFalse();
    await Assert
      .That(() =>
        MazakManagedScheduleControl.EnsurePlan(db, config, device.Data, job.UniqueStr, 2, 10)
      )
      .Throws<ConflictRequestException>();
    await Assert
      .That(() =>
        MazakManagedScheduleControl.SetMaterialOffer(db, config, device.Data, job.UniqueStr, 2, 1)
      )
      .Throws<ConflictRequestException>();
    await Assert
      .That(() =>
        MazakManagedScheduleControl.EnsurePlan(
          db,
          config with
          {
            ControllerSource = "other",
          },
          device.Data,
          job.UniqueStr,
          2,
          10
        )
      )
      .Throws<ConflictRequestException>();
    db.ArchiveJob(job.UniqueStr);
    Poll();
    await Assert.That(device.Data.Schedules.Single(s => s.Id == p2.Id)).IsEqualTo(peerSchedule);
  }

  private static BlackMaple.MachineFramework.LogEntry TranslateManagedTransfer(
    IRepository db,
    MazakConfig config,
    FMSSettings settings,
    MazakAllData data,
    MazakScheduleRow row,
    string partName,
    LogCode code,
    int quantity,
    int sequence,
    ImmutableList<long>? identified
  )
  {
    var log = new MazakMachineInterface.LogEntry
    {
      Code = code,
      ForeignID = $"LG20261111-115000-{sequence * 2:000}.csv",
      TimeUTC = Now.AddMinutes(sequence - 90),
      FullPartName = row.PartName,
      JobPartName = partName,
      FixedQuantity = quantity,
      Pallet = 2,
      StationNumber = 1,
      Process = 1,
    };
    var captured = new MazakAllDataAndLogs
    {
      Parts = data.Parts,
      Schedules = data.Schedules,
      Pallets = data.Pallets,
      Fixtures = data.Fixtures,
      MainPrograms = data.MainPrograms,
      LoadActions = data.LoadActions,
      PalletStatuses = [],
      PalletSubStatuses = data.PalletSubStatuses,
      PalletPositions = data.PalletPositions,
      Alarms = [],
      Logs =
      [
        log,
        log with
        {
          Code = LogCode.LoadBegin,
          ForeignID = $"LG20261111-115000-{sequence * 2 + 1:000}.csv",
        },
      ],
    };
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
    LogTranslation.HandleEvents(db, captured, "MC", settings, _ => { }, resolver, [], Now);
    return db.MostRecentLogEntryForForeignID(config.LogForeignId(log.ForeignID));
  }
}
