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
  [Arguments(false)]
  [Arguments(true)]
  public async Task ReservedProgramSkipsBlockedWorkAndDownloadsItsPeer(bool sameAcceptance)
  {
    const string program = "reserved-program";
    var template = DisjointPendingJobs().Jobs[0];
    var blocked = WithProgram(template with { UniqueStr = "blocked" }, program, 1);
    var peer = template with { UniqueStr = "independent-peer" };
    _jobDB.AddJobs(
      new NewJobs
      {
        ScheduleId = "a-blocked",
        Jobs = sameAcceptance ? [blocked, peer] : [blocked],
        Programs = [ProgramContent(program, 1)],
      },
      null,
      false
    );
    if (!sameAcceptance)
      _jobDB.AddJobs(new NewJobs { ScheduleId = "b-independent", Jobs = [peer] }, null, false);
    var reserved = Path.Combine(_mazakCfg.ProgramDirectory, "rev1", program + ".EIA");
    var protection = new MazakResourceProtection { ProgramNames = [reserved] };

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
    await Assert.That(_jobDB.LoadJob(peer.UniqueStr).CopiedToSystem).IsTrue();
    await Assert
      .That(
        FindWrite("Add Schedules")
          .Schedules.All(schedule =>
            MazakPart.UniqueFromComment(schedule.Comment) == peer.UniqueStr
          )
      )
      .IsTrue();

    _mazakDbMock.ClearReceivedCalls();
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
      .IsFalse();
    _mazakDbMock.DidNotReceive().Save(Arg.Any<MazakWriteData>());
  }

  [Test]
  [Arguments("direct")]
  [Arguments("alias")]
  [Arguments("tracked")]
  public async Task ProtectedProgramReuseDoesNotRequireANativeProgramWrite(string source)
  {
    var logical = source == "direct" ? "1001" : "managed-program";
    var native = source == "direct" ? logical : "installed-native-alias";
    var job = WithProgram(DisjointPendingJobs().Jobs[0], logical, 7);
    _jobDB.AddJobs(
      new NewJobs
      {
        ScheduleId = "reuse",
        Jobs = [job],
        Programs = [ProgramContent(logical, 7)],
      },
      null,
      false
    );
    if (source == "tracked")
      _jobDB.SetCellControllerProgramForProgram(logical, 7, native);
    var data =
      source == "alias"
        ? _initialAllData with
        {
          MainPrograms = _initialAllData
            .MainPrograms.Append(
              new MazakProgramRow
              {
                MainProgram = native,
                Comment = MazakProcess.CreateMainProgramComment(logical, 7),
              }
            )
            .ToImmutableList(),
        }
        : _initialAllData;
    var protection = new MazakResourceProtection
    {
      ProgramNames = [native, Path.Combine(_mazakCfg.ProgramDirectory, "rev7", logical + ".EIA")],
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
    await Assert.That(_jobDB.LoadJob(job.UniqueStr).CopiedToSystem).IsTrue();
    await Assert
      .That(
        FindWrite("Add Parts")
          .Parts.SelectMany(part => part.Processes)
          .All(process => process.MainProgram == native)
      )
      .IsTrue();
    await Assert
      .That(ProgramWrites().Any(program => program.Command == MazakWriteCommand.Add))
      .IsFalse();
    await Assert.That(ProgramWrites().Any(program => program.MainProgram == native)).IsFalse();
  }

  [Test]
  [Arguments(null, false)]
  [Arguments(1L, true)]
  public async Task ProgramReservationUsesTheResolvedRequestedOrLatestRevision(
    long? requestedRevision,
    bool downloads
  )
  {
    const string logical = "versioned-program";
    var job = WithProgram(DisjointPendingJobs().Jobs[0], logical, requestedRevision);
    _jobDB.AddJobs(
      new NewJobs
      {
        ScheduleId = "revisions",
        Jobs = [job],
        Programs = [ProgramContent(logical, 1), ProgramContent(logical, 2)],
      },
      null,
      false
    );
    var protection = new MazakResourceProtection
    {
      ProgramNames = [Path.Combine(_mazakCfg.ProgramDirectory, "rev2", logical + ".EIA")],
    };

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
      .IsEqualTo(downloads);
    await Assert.That(_jobDB.LoadJob(job.UniqueStr).CopiedToSystem).IsEqualTo(downloads);
    if (downloads)
      await Assert
        .That(
          ProgramWrites()
            .Single(program => program.Command == MazakWriteCommand.Add)
            .ProgramRevision
        )
        .IsEqualTo(1);
    else
      _mazakDbMock.DidNotReceive().Save(Arg.Any<MazakWriteData>());
  }

  [Test]
  public async Task ReservedProgramDoesNotBlockScheduleOnlyDownloadRecovery()
  {
    const string program = "reserved-program";
    var template = WithProgram(DisjointPendingJobs().Jobs[0], program, 1);
    var process = template.Processes[0] with
    {
      BasketLoadStations = [11],
      BasketUnloadStations = [12],
    };
    var job = template with { UniqueStr = "split-retry", Processes = [process, process] };
    _jobDB.AddJobs(new NewJobs { ScheduleId = "retry", Jobs = [job] }, null, false);
    var firstName = job.PartName + ":7:1:1";
    var secondName = job.PartName + ":7:2:2";
    var data = _initialAllData with
    {
      Parts =
      [
        new MazakPartRow { PartName = firstName, Comment = job.UniqueStr + "-1-1-InsightS" },
        new MazakPartRow { PartName = secondName, Comment = job.UniqueStr + "-2-1-InsightS" },
      ],
      Schedules =
      [
        new MazakScheduleRow
        {
          Id = 5,
          PartName = firstName,
          Comment = job.UniqueStr + "-1-1-InsightS",
        },
      ],
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
            ProgramNames = [Path.Combine(_mazakCfg.ProgramDirectory, "rev1", program + ".EIA")],
          }
        )
      )
      .IsTrue();
    await Assert.That(_jobDB.LoadJob(job.UniqueStr).CopiedToSystem).IsTrue();
    await Assert.That(FindWrite("Add Schedules").Schedules.Single().PartName).IsEqualTo(secondName);
    await Assert.That(FindWrite("Add Parts")).IsNull();
    await Assert.That(ProgramWrites()).IsEmpty();
  }

  [Test]
  public async Task ProtectedWriteGuardStillRejectsAProgramRewrittenByCustomConstruction()
  {
    const string rewrittenProgram = "reserved-rewrite";
    var job = WithProgram(DisjointPendingJobs().Jobs[0], "1001", null);
    _jobDB.AddJobs(
      new NewJobs
      {
        ScheduleId = "custom",
        Jobs = [job],
        Programs = [ProgramContent(rewrittenProgram, 1)],
      },
      null,
      false
    );
    var reserved = Path.Combine(_mazakCfg.ProgramDirectory, "rev1", rewrittenProgram + ".EIA");
    var configuration = _mazakCfg with
    {
      ProcessFromJob = (parent, process, path, program) =>
        new MazakProcessFromJob(
          parent,
          process,
          path,
          program with
          {
            ProgramName = rewrittenProgram,
            Revision = 1,
            CellControllerProgramName = null,
          }
        ),
    };

    await Assert
      .That(() =>
        WriteJobs.SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _mazakDbMock,
          _settings,
          configuration,
          fixtureQueueTime,
          new MazakResourceProtection { ProgramNames = [reserved] }
        )
      )
      .Throws<ConflictRequestException>();
    await Assert.That(ProgramWrites().Any(program => program.MainProgram == reserved)).IsFalse();
    await Assert.That(_jobDB.LoadJob(job.UniqueStr).CopiedToSystem).IsFalse();
  }

  private static NewProgramContent ProgramContent(string name, long revision) =>
    new()
    {
      ProgramName = name,
      Revision = revision,
      ProgramContent = "program content",
    };

  private static Job WithProgram(Job job, string program, long? revision) =>
    job with
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
                    .Stops.Select(stop =>
                      stop with
                      {
                        Program = program,
                        ProgramRevision = revision,
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

  private ImmutableList<NewMazakProgram> ProgramWrites() =>
    _mazakDbMock
      .ReceivedCalls()
      .Where(call => call.GetMethodInfo().Name == "Save")
      .SelectMany(call => ((MazakWriteData)call.GetArguments()[0]).Programs)
      .ToImmutableList();
}
