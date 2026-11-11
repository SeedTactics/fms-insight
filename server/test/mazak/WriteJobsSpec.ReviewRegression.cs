using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed partial class WriteJobsSpec
{
  [Test]
  [Arguments("pallet")]
  [Arguments("schedule")]
  [Arguments("observed-assignment")]
  public async Task ReviewSurvivingAssignmentProtectsDefinitionsWhenScheduleAndPalletRowsDisappear(
    string reservation
  )
  {
    var legacy = ProtectedLegacyJob();
    _jobDB.AddJobs(new NewJobs { ScheduleId = "legacy", Jobs = [legacy] }, null, true);
    _jobDB.AddJobs(DisjointPendingJobs(), null, false);
    const string program = @"theprogdir\rev2\prog-bbb-1.EIA";
    var data = _initialAllData with
    {
      Schedules = [],
      Pallets = [],
      Parts = _initialAllData
        .Parts.Select(part =>
          part.PartName == "part1:1:1"
            ? part with
            {
              Processes = part
                .Processes.Select(process => process with { MainProgram = program })
                .ToImmutableList(),
            }
            : part
        )
        .ToImmutableList(),
      PalletSubStatuses =
      [
        new MazakPalletSubStatusRow
        {
          PalletNumber = 5,
          ScheduleID = 1,
          PartName = "part1:1:1",
          FixtureName = "fixtoremove",
          PartProcessNumber = 1,
          FixQuantity = 3,
        },
      ],
    };
    var downloaded =
      reservation == "observed-assignment"
        ? WriteJobs.SyncFromDatabase(
          data,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime
        )
        : WriteJobs.SyncFromDatabase(
          data,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime,
          reservation switch
          {
            "schedule" => new MazakResourceProtection { ScheduleIds = [1] },
            "pallet" => new MazakResourceProtection { PalletNumbers = [5] },
            _ => MazakResourceProtection.Empty,
          }
        );
    await Assert.That(downloaded).IsTrue();
    var writes = _mazakDbMock
      .ReceivedCalls()
      .Where(call => call.GetMethodInfo().Name == "Save")
      .Select(call => (MazakWriteData)call.GetArguments()[0])
      .ToImmutableList();
    var forbidden = writes
      .SelectMany(write => write.Parts)
      .Where(part => part.PartName == "part1:1:1")
      .Select(part => "part:" + part.PartName)
      .Concat(
        writes
          .SelectMany(write => write.Fixtures)
          .Where(fixture => fixture.FixtureName == "fixtoremove")
          .Select(fixture => "fixture:" + fixture.FixtureName)
      )
      .Concat(
        writes
          .SelectMany(write => write.Programs)
          .Where(row => row.MainProgram == program)
          .Select(row => "program:" + row.MainProgram)
      )
      .ToImmutableList();
    await Assert.That(forbidden).IsEmpty();
  }

  [Test]
  public async Task ReviewReservedProgramCollisionDoesNotStarveIndependentDownload()
  {
    const string program = "reserved-program";
    var pending = DisjointPendingJobs();
    var template = pending.Jobs[0];
    var blocked = template with
    {
      UniqueStr = "blocked",
      Processes = template
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
    var healthy = template with { UniqueStr = "healthy-peer" };
    _jobDB.AddJobs(
      new NewJobs
      {
        ScheduleId = "a-blocked",
        Jobs = [blocked],
        Programs =
        [
          new NewProgramContent
          {
            ProgramName = program,
            Revision = 1,
            ProgramContent = "content",
          },
        ],
      },
      null,
      false
    );
    _jobDB.AddJobs(new NewJobs { ScheduleId = "b-independent", Jobs = [healthy] }, null, false);
    var protection = new MazakResourceProtection
    {
      ProgramNames = [Path.Combine(_mazakCfg.ProgramDirectory, "rev1", program + ".EIA")],
    };
    for (var attempt = 0; attempt < 2; attempt++)
    {
      try
      {
        WriteJobs.SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime,
          protection
        );
      }
      catch (ConflictRequestException)
      {
        // A local collision must not cause later polling to repeatedly select the blocked job.
      }
    }
    await Assert.That(_jobDB.LoadJob(blocked.UniqueStr).CopiedToSystem).IsFalse();
    await Assert.That(_jobDB.LoadJob(healthy.UniqueStr).CopiedToSystem).IsTrue();
  }
}
