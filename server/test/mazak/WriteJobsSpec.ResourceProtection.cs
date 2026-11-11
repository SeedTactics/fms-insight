using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed partial class WriteJobsSpec
{
  private Job ProtectedLegacyJob() =>
    new()
    {
      UniqueStr = "uniq1",
      PartName = "part1",
      Cycles = 15,
      Archived = false,
      RouteStartUTC = fixtureQueueTime.AddHours(-1),
      RouteEndUTC = fixtureQueueTime.AddDays(1),
      Processes = [new ProcessInfo { Paths = [JobLogTest.EmptyPath] }],
    };

  private NewJobs DisjointPendingJobs()
  {
    var jobs = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
      jsonSettings
    )!;
    return jobs with
    {
      Jobs = jobs
        .Jobs.Select(j =>
          j with
          {
            Processes = j
              .Processes.Select(p =>
                p with
                {
                  Paths = p
                    .Paths.Select(path =>
                      path with
                      {
                        PalletNums = path
                          .PalletNums.Select(number => number + 10)
                          .ToImmutableSortedSet(),
                      }
                    )
                    .ToImmutableList(),
                }
              )
              .ToImmutableList(),
          }
        )
        .ToImmutableList(),
    };
  }

  [Test]
  [Arguments("schedule")]
  [Arguments("part")]
  [Arguments("pallet")]
  [Arguments("unknown")]
  [Arguments("archived")]
  [Arguments("malformed")]
  [Arguments("unscoped")]
  public async Task PendingEvidencePreservesNativeDefinitionsDuringIndependentDownload(string scope)
  {
    var legacy = ProtectedLegacyJob();
    if (scope != "unknown")
    {
      _jobDB.AddJobs(new NewJobs { ScheduleId = "legacy", Jobs = [legacy] }, null, true);
      if (scope == "archived")
        _jobDB.ArchiveJob(legacy.UniqueStr);
    }
    _jobDB.AddJobs(DisjointPendingJobs(), null, false);
    var data = _initialAllData with
    {
      Schedules = _initialAllData
        .Schedules.Select(s =>
          s.Id == 1 && scope == "malformed" ? s with { Comment = "-InsightS" } : s
        )
        .ToImmutableList(),
      PalletSubStatuses =
      [
        new MazakPalletSubStatusRow
        {
          PalletNumber = 5,
          ScheduleID = 1,
          FixQuantity = 3,
          PartProcessNumber = 1,
        },
      ],
    };
    var protection = new MazakResourceProtection
    {
      ScheduleIds = scope == "schedule" ? [1] : [],
      PartNames = scope is not ("schedule" or "pallet" or "unscoped") ? ["part1:1:1"] : [],
      PalletNumbers = scope == "pallet" ? [5] : [],
      SuspendCleanup = scope == "unscoped",
    };
    await Assert
      .That(
        WriteJobs.SyncFromDatabase(
          data,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime,
          protection
        )
      )
      .IsTrue();
    var writes = _mazakDbMock
      .ReceivedCalls()
      .Where(c => c.GetMethodInfo().Name == "Save")
      .Select(c => (MazakWriteData)c.GetArguments()[0])
      .ToImmutableList();
    await Assert.That(writes.SelectMany(w => w.Schedules).Any(s => s.Id == 1)).IsFalse();
    await Assert
      .That(writes.SelectMany(w => w.Parts).Any(p => p.PartName == "part1:1:1"))
      .IsFalse();
    await Assert.That(writes.SelectMany(w => w.Pallets).Any(p => p.PalletNumber == 5)).IsFalse();
    await Assert
      .That(writes.SelectMany(w => w.Fixtures).Any(f => f.FixtureName == "fixtoremove"))
      .IsFalse();
    await Assert.That(FindWrite("Add Schedules").Schedules.Count).IsGreaterThan(0);
    if (scope is not ("unknown" or "archived"))
      await Assert.That(_jobDB.LoadJob(legacy.UniqueStr).Archived).IsFalse();
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task BlockedDownloadDoesNotStarveAnIndependentManualPeer(bool sameAcceptance)
  {
    var job = DisjointPendingJobs().Jobs[0];
    var blocked = job with
    {
      UniqueStr = "blocked",
      Processes = job
        .Processes.Select(p =>
          p with
          {
            Paths = p.Paths.Select(path => path with { PalletNums = [5] }).ToImmutableList(),
          }
        )
        .ToImmutableList(),
    };
    var peer = job with { UniqueStr = "manual-peer" };
    _jobDB.AddJobs(
      new NewJobs { ScheduleId = "a-blocked", Jobs = sameAcceptance ? [blocked, peer] : [blocked] },
      null,
      false
    );
    if (!sameAcceptance)
      _jobDB.AddJobs(new NewJobs { ScheduleId = "b-independent", Jobs = [peer] }, null, false);
    var protection = new MazakResourceProtection { PalletNumbers = [5] };
    await Assert
      .That(
        WriteJobs.SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime,
          protection
        )
      )
      .IsTrue();
    await Assert.That(_jobDB.LoadJob(blocked.UniqueStr).CopiedToSystem).IsFalse();
    await Assert.That(_jobDB.LoadJob(blocked.UniqueStr).Archived).IsFalse();
    await Assert.That(_jobDB.LoadDecrementsForJob(blocked.UniqueStr)).IsEmpty();
    await Assert.That(_jobDB.LoadJob(peer.UniqueStr).CopiedToSystem).IsTrue();
    await Assert
      .That(
        FindWrite("Add Schedules")
          .Schedules.All(s => MazakPart.UniqueFromComment(s.Comment) == peer.UniqueStr)
      )
      .IsTrue();
    await Assert
      .That(
        _mazakDbMock
          .ReceivedCalls()
          .Where(c => c.GetMethodInfo().Name == "Save")
          .SelectMany(c => ((MazakWriteData)c.GetArguments()[0]).Pallets)
          .Any(p => p.PalletNumber == 5)
      )
      .IsFalse();
  }

  [Test]
  public async Task MissingRowsDoNotReleaseReservedScheduleAndDefinitionNames()
  {
    var pending = DisjointPendingJobs();
    _jobDB.AddJobs(pending, null, false);
    var data = _initialAllData with
    {
      Schedules = [],
      Parts = [],
      Fixtures = [],
      Pallets = [],
      PalletSubStatuses =
      [
        new MazakPalletSubStatusRow
        {
          PalletNumber = 50,
          ScheduleID = 2,
          FixQuantity = 3,
        },
      ],
    };
    var protection = new MazakResourceProtection
    {
      ScheduleIds = [1, 3],
      PartNames = ["former:0:1"],
    };
    _mazakDbMock
      .LoadAllData()
      .Returns(_ =>
        data with
        {
          Parts = FindWrite("Add Parts")?.Parts ?? [],
          Pallets = FindWrite("Add Parts")?.Pallets ?? [],
        }
      );
    await Assert
      .That(
        WriteJobs.SyncFromDatabase(
          data,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime,
          protection
        )
      )
      .IsTrue();
    await Assert.That(FindWrite("Add Schedules").Schedules.All(s => s.Id >= 4)).IsTrue();
    await Assert
      .That(FindWrite("Add Parts").Parts.All(p => MazakPart.ParseUID(p.PartName) != 0))
      .IsTrue();
  }

  [Test]
  public async Task PalletReservationRetainsItsPartAfterScheduleAndAssignmentDisappear()
  {
    var legacy = ProtectedLegacyJob();
    _jobDB.AddJobs(new NewJobs { ScheduleId = "legacy", Jobs = [legacy] }, null, true);
    _jobDB.AddJobs(DisjointPendingJobs(), null, false);
    var data = _initialAllData with { Schedules = [], PalletSubStatuses = [] };
    await Assert
      .That(
        WriteJobs.SyncFromDatabase(
          data,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime,
          new MazakResourceProtection { PalletNumbers = [5] }
        )
      )
      .IsTrue();
    await Assert
      .That(FindWrite("Delete Parts")?.Parts.Any(p => p.PartName == "part1:1:1") ?? false)
      .IsFalse();
    await Assert
      .That(
        FindWrite("Delete Fixtures")?.Fixtures.Any(f => f.FixtureName == "fixtoremove") ?? false
      )
      .IsFalse();
    await Assert.That(_jobDB.LoadJob(legacy.UniqueStr).Archived).IsFalse();
  }

  [Test]
  public async Task RecordedJobAndProgramReservationsSurviveLossOfAllNativeDefinitions()
  {
    var legacy = ProtectedLegacyJob();
    _jobDB.AddJobs(new NewJobs { ScheduleId = "legacy", Jobs = [legacy] }, null, true);
    _jobDB.AddJobs(DisjointPendingJobs(), null, false);
    const string oldProgram = @"theprogdir\rev2\prog-bbb-1.EIA";
    var data = _initialAllData with
    {
      Schedules = [],
      Parts = [],
      Pallets = [],
      Fixtures = [],
      PalletSubStatuses = [],
    };
    await Assert
      .That(
        WriteJobs.SyncFromDatabase(
          data,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime,
          new MazakResourceProtection
          {
            JobUniques = [legacy.UniqueStr],
            ProgramNames = [oldProgram],
          }
        )
      )
      .IsTrue();
    await Assert.That(_jobDB.LoadJob(legacy.UniqueStr).Archived).IsFalse();
    await Assert
      .That(FindWrite("Delete Fixtures").Programs.Any(p => p.MainProgram == oldProgram))
      .IsFalse();
  }

  [Test]
  public async Task ANewProgramCannotOverwriteAReservedNativeProgramName()
  {
    const string program = "reserved-program";
    var pending = DisjointPendingJobs();
    var job = pending.Jobs[0];
    job = job with
    {
      Processes = job
        .Processes.Select(process =>
          process with
          {
            Paths = process
              .Paths.Select(path =>
                path with
                {
                  Stops = path
                    .Stops.Select(stop => stop with { Program = program, ProgramRevision = 1 })
                    .ToImmutableList(),
                }
              )
              .ToImmutableList(),
          }
        )
        .ToImmutableList(),
    };
    _jobDB.AddJobs(
      pending with
      {
        Jobs = [job],
        Programs =
        [
          new NewProgramContent
          {
            ProgramName = program,
            Revision = 1,
            ProgramContent = "program content",
          },
        ],
      },
      null,
      false
    );
    var reservedName = Path.Combine(_mazakCfg.ProgramDirectory, "rev1", program + ".EIA");
    await Assert
      .That(() =>
        WriteJobs.SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime,
          new MazakResourceProtection { ProgramNames = [reservedName] }
        )
      )
      .Throws<ConflictRequestException>();
    await Assert
      .That(
        _mazakDbMock
          .ReceivedCalls()
          .Where(c => c.GetMethodInfo().Name == "Save")
          .SelectMany(c => ((MazakWriteData)c.GetArguments()[0]).Programs)
          .Any(p => p.MainProgram == reservedName)
      )
      .IsFalse();
    await Assert.That(_jobDB.LoadJob(job.UniqueStr).CopiedToSystem).IsFalse();
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task OriginalFixtureRemainsReservedAfterItsPartAndPalletDefinitionsDisappear(
    bool fixtureMissing
  )
  {
    _jobDB.AddJobs(DisjointPendingJobs(), null, false);
    const string fixture = "F:0:1:1";
    var data = _initialAllData with
    {
      Schedules = [],
      Parts = [],
      Pallets = [],
      Fixtures = fixtureMissing
        ? []
        : [new MazakFixtureRow { FixtureName = fixture, Comment = "Insight" }],
    };
    await Assert
      .That(
        WriteJobs.SyncFromDatabase(
          data,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime,
          new MazakResourceProtection { FixtureNames = [fixture] }
        )
      )
      .IsTrue();
    var fixtureWrites = _mazakDbMock
      .ReceivedCalls()
      .Where(call => call.GetMethodInfo().Name == "Save")
      .SelectMany(call => ((MazakWriteData)call.GetArguments()[0]).Fixtures)
      .ToImmutableList();
    await Assert.That(fixtureWrites.Any(row => row.FixtureName == fixture)).IsFalse();
    await Assert
      .That(FindWrite("Add Fixtures").Fixtures.All(row => MazakPart.ParseUID(row.FixtureName) != 0))
      .IsTrue();
    await Assert.That(FindWrite("Add Schedules").Schedules.Count).IsGreaterThan(0);
  }
}
