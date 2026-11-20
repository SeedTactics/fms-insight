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
  [Arguments(false)]
  [Arguments(true)]
  public async Task ExistingScheduleRequiresTheAcceptedProgramBeforeAdoption(bool namedRevision)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var job = PendingJob(true);
    var device = new DownloadDevice("");
    if (namedRevision)
    {
      var second = job.Processes[1];
      var path = second.Paths[0];
      job = job with
      {
        Processes = job.Processes.SetItem(
          1,
          second with
          {
            Paths =
            [
              path with
              {
                Stops = [path.Stops.Single() with { Program = "op2", ProgramRevision = 7 }],
              },
            ],
          }
        ),
      };
      device.Observe(
        device.Data with
        {
          MainPrograms = device.Data.MainPrograms.Concat(
            new[]
            {
              new MazakProgramRow { MainProgram = "1012", Comment = "Insight:7:op2" },
              new MazakProgramRow { MainProgram = "1013", Comment = "Insight:8:op2" },
            }
          ),
        }
      );
    }
    db.AddJobs(
      new NewJobs
      {
        ScheduleId = "first",
        Jobs = [job],
        Programs = namedRevision
          ?
          [
            new NewProgramContent
            {
              ProgramName = "op2",
              Revision = 7,
              ProgramContent = "accepted",
            },
            new NewProgramContent
            {
              ProgramName = "op2",
              Revision = 8,
              ProgramContent = "later",
            },
          ]
          : [],
      },
      null,
      false
    );
    var config = ManagedConfig();
    await Assert.That(Sync(device, db, config with { UseInsightJobQuantities = null })).IsTrue();
    var original = device.Data;
    var schedule = original.Schedules.Single(s => s.Comment == "initial-job-2-1-InsightS");
    var part = original.Parts.Single(p => p.PartName == schedule.PartName);
    await Assert
      .That(part.Processes.Single().MainProgram)
      .IsEqualTo(namedRevision ? "1012" : "1002");
    device.Observe(
      original with
      {
        Parts = original
          .Parts.Select(p =>
            p == part
              ? p with
              {
                Processes =
                [
                  p.Processes.Single() with
                  {
                    MainProgram = namedRevision ? "1013" : "1001",
                  },
                ],
              }
              : p
          )
          .ToImmutableList(),
      }
    );
    ManagedMazakSchedules.ConfirmObserved(db, config, device.Data);
    await Assert.That(db.LoadMazakManagedSchedule(config.ControllerSource, part.PartName)).IsNull();
    await Assert
      .That(() =>
        MazakManagedScheduleControl.SetMaterialOffer(db, config, device.Data, job.UniqueStr, 2, 1)
      )
      .Throws<ConflictRequestException>();

    // The same revision can appear under more than one native number, in either order.
    var equivalentReadback = namedRevision
      ? original with
      {
        MainPrograms = original
          .MainPrograms.Prepend(
            new MazakProgramRow { MainProgram = "1014", Comment = "Insight:7:op2" }
          )
          .ToImmutableList(),
      }
      : original;
    device.Observe(equivalentReadback);
    if (namedRevision)
    {
      // A downloader hook selects revision 8 when given the first revision-7 alias (1014).
      // The installed alias (1012) must not let the legacy route bypass that choice.
      var customConfig = config with
      {
        ProcessFromJob = (p, proc, path, program) =>
          new MazakProcessFromJob(
            p,
            proc,
            path,
            program.CellControllerProgramName == "1014"
              ? program with
              {
                Revision = 8,
                CellControllerProgramName = "1013",
              }
              : program
          ),
      };
      ManagedMazakSchedules.ConfirmObserved(db, customConfig, device.Data);
      await Assert
        .That(db.LoadMazakManagedSchedule(config.ControllerSource, part.PartName))
        .IsNull();
      await Assert
        .That(() =>
          MazakManagedScheduleControl.SetMaterialOffer(
            db,
            customConfig,
            device.Data,
            job.UniqueStr,
            2,
            1
          )
        )
        .Throws<ConflictRequestException>();
    }
    ManagedMazakSchedules.ConfirmObserved(db, config, device.Data);
    var offer = MazakManagedScheduleControl.SetMaterialOffer(
      db,
      config,
      device.Data,
      job.UniqueStr,
      2,
      1
    );
    await Assert.That(offer).IsNotNull();
    if (namedRevision)
    {
      await Assert
        .That(
          MazakManagedScheduleControl.SetMaterialOffer(db, config, original, job.UniqueStr, 2, 1)
        )
        .IsNotNull();
      device.Native.Save(offer!);
      // A previously saved program number cannot override the controller's current revision.
      db.SetCellControllerProgramForProgram("op2", 7, "1012");
      device.Observe(
        device.Data with
        {
          MainPrograms = device
            .Data.MainPrograms.Select(p =>
              p.MainProgram == "1012" ? p with { Comment = "Insight:8:op2" } : p
            )
            .ToImmutableList(),
        }
      );
      await Assert
        .That(() =>
          MazakManagedScheduleControl.SetMaterialOffer(db, config, device.Data, job.UniqueStr, 2, 2)
        )
        .Throws<ConflictRequestException>()
        .WithMessageContaining("revision 7");
      var withdrawal = MazakManagedScheduleControl.SetMaterialOffer(
        db,
        config,
        device.Data,
        job.UniqueStr,
        2,
        0
      );
      await Assert.That(withdrawal!.Schedules.Single().Id).IsEqualTo(schedule.Id);
      await Assert
        .That(withdrawal.Schedules.Single().Processes.Single().ProcessMaterialQuantity)
        .IsEqualTo(0);
    }
  }

  [Test]
  public async Task NativeProgramNumberCannotOverrideTheAcceptedRevision()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var device = new DownloadDevice("");
    var job = PendingJob(true);
    var second = job.Processes[1];
    var path = second.Paths.Single();
    var request = new NewJobs
    {
      ScheduleId = "first",
      Jobs =
      [
        job with
        {
          Processes = job.Processes.SetItem(
            1,
            second with
            {
              Paths = [path with { Stops = [path.Stops.Single() with { ProgramRevision = 7 }] }],
            }
          ),
        },
      ],
      Programs =
      [
        new()
        {
          ProgramName = "1002",
          Revision = 7,
          ProgramContent = "accepted",
        },
      ],
    };
    device.Observe(
      device.Data with
      {
        MainPrograms = device
          .Data.MainPrograms.Select(p =>
            p.MainProgram == "1002" ? p with { Comment = "Insight:8:1002" } : p
          )
          .Append(new MazakProgramRow { MainProgram = "1012", Comment = "Insight:7:1002" })
          .ToImmutableList(),
      }
    );
    db.AddJobs(request, null, false);
    await Assert.That(Sync(device, db)).IsTrue();
    var part = device.Data.Parts.Single(p => p.Comment == "initial-job-2-1-InsightS");
    var process = part.Processes.Single();
    await Assert.That(process.MainProgram).IsEqualTo("1012");

    using var recovery = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var reopened = recovery.OpenConnection();
    reopened.AddJobs(request, null, false);
    var config = ManagedConfig();
    var wrong = device.Data with
    {
      Parts = device
        .Data.Parts.Select(p =>
          p == part ? p with { Processes = [process with { MainProgram = "1002" }] } : p
        )
        .ToImmutableList(),
    };
    ManagedMazakSchedules.ConfirmObserved(reopened, config, wrong);
    await Assert
      .That(() =>
        MazakManagedScheduleControl.SetMaterialOffer(reopened, config, wrong, job.UniqueStr, 2, 1)
      )
      .Throws<ConflictRequestException>();
    ManagedMazakSchedules.ConfirmObserved(reopened, config, device.Data);
    await Assert
      .That(
        MazakManagedScheduleControl.SetMaterialOffer(
          reopened,
          config,
          device.Data,
          job.UniqueStr,
          2,
          1
        )
      )
      .IsNotNull();
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task AdoptionAndSupplyUseTheConfiguredProcessProgram(bool overrideRow)
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repositories.OpenConnection();
    var job = PendingJob(true);
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [job] }, null, false);
    var config = ManagedConfig() with
    {
      ProcessFromJob = (part, process, path, program) =>
        overrideRow
          ? new RemappedProgramProcess(part, process, path, program)
          : new MazakProcessFromJob(
            part,
            process,
            path,
            program with
            {
              CellControllerProgramName = "1002",
            }
          ),
    };
    await Assert
      .That(Sync(device, db, config with { UseInsightJobQuantities = null, ProcessFromJob = null }))
      .IsTrue();
    var first = device.Data.Parts.Single(p => p.Comment == "initial-job-1-1-InsightS");
    await Assert.That(first.Processes.Single().MainProgram).IsEqualTo("1001");
    ManagedMazakSchedules.ConfirmObserved(db, config, device.Data);
    await Assert
      .That(db.LoadMazakManagedSchedule(config.ControllerSource, first.PartName))
      .IsNull();

    device.Observe(
      device.Data with
      {
        Parts = device
          .Data.Parts.Select(p =>
            p == first
              ? p with
              {
                Processes = [p.Processes.Single() with { MainProgram = "1002" }],
              }
              : p
          )
          .ToImmutableList(),
      }
    );
    ManagedMazakSchedules.ConfirmObserved(db, config, device.Data);
    await Assert
      .That(
        MazakManagedScheduleControl.SetMaterialOffer(db, config, device.Data, job.UniqueStr, 1, 1)
      )
      .IsNotNull();
    await Assert
      .That(() =>
        MazakManagedScheduleControl.SetMaterialOffer(
          db,
          config with
          {
            ProcessFromJob = null,
          },
          device.Data,
          job.UniqueStr,
          1,
          1
        )
      )
      .Throws<ConflictRequestException>()
      .WithMessageContaining("1001");
  }

  private sealed class RemappedProgramProcess(
    MazakPart part,
    int process,
    int path,
    ProgramRevision program
  ) : MazakProcessFromJob(part, process, path, program)
  {
    public override void CreateDatabaseRow(MazakPartRow newPart, string fixture, MazakConfig config)
    {
      base.CreateDatabaseRow(newPart, fixture, config);
      newPart.Processes[^1] = newPart.Processes[^1] with { MainProgram = "1002" };
    }
  }

  [Test]
  [Arguments("pieces per fixture")]
  [Arguments("eligible machines")]
  [Arguments("eligible load stations")]
  [Arguments("eligible unload stations")]
  [Arguments("eligible pallets")]
  [Arguments("changed fixtures or programs")]
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
      "changed fixtures or programs" => process with { MainProgram = "1001" },
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
    var alarm = ManagedMazakSchedules
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
  public async Task ConfirmedRouteCannotBeReplacedOrEditedThroughCopiedComment()
  {
    using var repositories = RepositoryConfig.InitializeMemoryDB(null);
    using var db = repositories.OpenConnection();
    var config = ManagedConfig();
    var job = PendingJob(true);
    var device = new DownloadDevice("");
    db.AddJobs(new NewJobs { ScheduleId = "initial", Jobs = [job] }, null, false);
    await Assert.That(Sync(device, db, config)).IsTrue();
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
    var peerSchedule = device.ReplaceRouteWithCopy(p2, job.PartName + ":100:2:2");
    await Assert.That(Sync(device, db, config)).IsFalse();
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
    Sync(device, db, config);
    await Assert.That(device.Data.Schedules.Single(s => s.Id == p2.Id)).IsEqualTo(peerSchedule);
    var retirementAlarm = ManagedMazakSchedules.Diagnostics(db, config, device.Data, Now).Single();
    await Assert.That(retirementAlarm.Code).IsEqualTo("mazak-managed-retirement-conflict");
    await Assert.That(retirementAlarm.Message).Contains(p2.PartName);
    await Assert.That(retirementAlarm.Message).Contains(peerSchedule.PartName);
  }
}
