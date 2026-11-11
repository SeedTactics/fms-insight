using System.Collections.Immutable;
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
  public async Task OrphanedActualAssignmentRetainsItsAsBuiltProgramAndFixtureAfterDefinitionsDisappear(
    bool recordedOccupancyOnly
  )
  {
    const string program = "retained-program";
    var template = DisjointPendingJobs().Jobs[0];
    var original = WithProgram(template with { UniqueStr = "original-owner" }, program, 1);
    _jobDB.AddJobs(
      new NewJobs
      {
        ScheduleId = "first",
        Jobs = [original],
        Programs = [ProgramContent(program, 1)],
      },
      null,
      false
    );
    WriteJobs.SyncFromDatabase(
      _initialAllData,
      _jobDB,
      _mazakDbMock,
      _settings,
      _mazakCfg,
      fixtureQueueTime
    );
    var part = FindWrite("Add Parts").Parts.First();
    var process = part.Processes.First();
    var pallet = FindWrite("Add Parts").Pallets.First(row => row.Fixture == process.Fixture);
    var schedule = FindWrite("Add Schedules")
      .Schedules.Single(row => row.PartName == part.PartName);
    var recorded = _jobDB.LoadMazakRouteProvenance(part.PartName, process.ProcessNumber)!;
    await Assert.That(recorded.NativeProgram).IsEqualTo(process.MainProgram);
    var configuration = recordedOccupancyOnly
      ? _mazakCfg with
      {
        UseInsightJobQuantities = job => job.UniqueStr == original.UniqueStr,
      }
      : _mazakCfg;
    var materialId = -1L;
    if (recordedOccupancyOnly)
    {
      materialId = _jobDB.AllocateMaterialID(
        original.UniqueStr,
        original.PartName,
        original.Processes.Count
      );
      _jobDB
        .RecordLoadUnloadComplete(
          toLoad:
          [
            new MaterialToLoadOntoFace
            {
              MaterialIDs = [materialId],
              Process = recorded.JobProcess,
              Path = recorded.Path,
              FaceNum = recorded.NativeProcess,
              ActiveOperationTime = System.TimeSpan.Zero,
            },
          ],
          previouslyLoaded: null,
          toUnload: null,
          previouslyUnloaded: null,
          pallet: configuration.TranslatePalletNumber(pallet.PalletNumber),
          lulNum: 1,
          totalElapsed: System.TimeSpan.Zero,
          timeUTC: fixtureQueueTime,
          externalQueues: null
        )
        .ToImmutableList();
    }
    var peer = template with
    {
      UniqueStr = "independent-peer",
      Processes = template
        .Processes.Select(info =>
          info with
          {
            Paths = info
              .Paths.Select(path =>
                path with
                {
                  PalletNums = path.PalletNums.Select(number => number + 10).ToImmutableSortedSet(),
                }
              )
              .ToImmutableList(),
          }
        )
        .ToImmutableList(),
    };
    _jobDB.AddJobs(new NewJobs { ScheduleId = "later", Jobs = [peer] }, null, false);
    var data = _initialAllData with
    {
      Parts = [],
      Schedules = [],
      Pallets = recordedOccupancyOnly ? [pallet] : [],
      Fixtures =
      [
        new MazakFixtureRow { FixtureName = recorded.NativeFixture, Comment = "Insight" },
      ],
      PalletSubStatuses = recordedOccupancyOnly
        ? []
        :
        [
          new MazakPalletSubStatusRow
          {
            PalletNumber = pallet.PalletNumber,
            ScheduleID = schedule.Id,
            PartName = part.PartName,
            PartProcessNumber = process.ProcessNumber,
            FixQuantity = 1,
            // The native fixture field is absent; the original as-built association still knows it.
            FixtureName = "",
          },
        ],
      MainPrograms = _initialAllData
        .MainPrograms.Concat(
          new[]
          {
            new MazakProgramRow
            {
              MainProgram = recorded.NativeProgram,
              Comment = MazakProcess.CreateMainProgramComment(program, 1),
            },
            new MazakProgramRow
            {
              MainProgram = System.IO.Path.Combine(
                _mazakCfg.ProgramDirectory,
                "rev2",
                program + ".EIA"
              ),
              Comment = MazakProcess.CreateMainProgramComment(program, 2),
            },
          }
        )
        .ToImmutableList(),
    };
    _mazakDbMock.ClearReceivedCalls();

    await Assert
      .That(
        WriteJobs.SyncFromDatabase(
          data,
          _jobDB,
          _mazakDbMock,
          _settings,
          configuration,
          fixtureQueueTime
        )
      )
      .IsTrue();

    await Assert.That(_jobDB.LoadJob(peer.UniqueStr).CopiedToSystem).IsTrue();
    await Assert.That(_jobDB.LoadJob(original.UniqueStr).Archived).IsFalse();
    var writes = _mazakDbMock
      .ReceivedCalls()
      .Where(call => call.GetMethodInfo().Name == "Save")
      .Select(call => (MazakWriteData)call.GetArguments()[0])
      .ToImmutableList();
    await Assert
      .That(
        writes
          .SelectMany(write => write.Programs)
          .Any(row => row.MainProgram == recorded.NativeProgram)
      )
      .IsFalse();
    await Assert
      .That(
        writes
          .SelectMany(write => write.Fixtures)
          .Any(row => row.FixtureName == recorded.NativeFixture)
      )
      .IsFalse();
    await Assert
      .That(
        writes
          .SelectMany(write => write.Pallets)
          .Any(row => row.PalletNumber == pallet.PalletNumber)
      )
      .IsFalse();

    if (recordedOccupancyOnly)
    {
      // Real handling releases the same resource reservation. History alone must not
      // retain a retired route or its old program after the material actually leaves.
      _jobDB
        .RecordLoadUnloadComplete(
          toLoad: null,
          previouslyLoaded: null,
          toUnload:
          [
            new MaterialToUnloadFromFace
            {
              MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
                materialId,
                new UnloadDestination()
              ),
              Process = recorded.JobProcess,
              FaceNum = recorded.NativeProcess,
              ActiveOperationTime = System.TimeSpan.Zero,
            },
          ],
          previouslyUnloaded: null,
          pallet: configuration.TranslatePalletNumber(pallet.PalletNumber),
          lulNum: 1,
          totalElapsed: System.TimeSpan.Zero,
          timeUTC: fixtureQueueTime.AddMinutes(1),
          externalQueues: null
        )
        .ToImmutableList();
      var laterPeer = peer with { UniqueStr = "later-independent-peer" };
      _jobDB.AddJobs(new NewJobs { ScheduleId = "after-unload", Jobs = [laterPeer] }, null, false);
      _mazakDbMock.ClearReceivedCalls();
      await Assert
        .That(
          WriteJobs.SyncFromDatabase(
            data,
            _jobDB,
            _mazakDbMock,
            _settings,
            configuration,
            fixtureQueueTime.AddMinutes(1)
          )
        )
        .IsTrue();
      var afterUnload = _mazakDbMock
        .ReceivedCalls()
        .Where(call => call.GetMethodInfo().Name == "Save")
        .Select(call => (MazakWriteData)call.GetArguments()[0])
        .ToImmutableList();
      await Assert
        .That(
          afterUnload
            .SelectMany(write => write.Programs)
            .Any(row =>
              row.MainProgram == recorded.NativeProgram && row.Command == MazakWriteCommand.Delete
            )
        )
        .IsTrue();
      await Assert
        .That(
          afterUnload
            .SelectMany(write => write.Fixtures)
            .Any(row =>
              row.FixtureName == recorded.NativeFixture && row.Command == MazakWriteCommand.Delete
            )
        )
        .IsTrue();
      await Assert
        .That(
          afterUnload
            .SelectMany(write => write.Pallets)
            .Any(row =>
              row.PalletNumber == pallet.PalletNumber && row.Command == MazakWriteCommand.Delete
            )
        )
        .IsTrue();
    }
  }

  [Test]
  public async Task NativeDefinitionsHaveDurableRouteProvenanceBeforePublication()
  {
    var pending = DisjointPendingJobs();
    _jobDB.AddJobs(pending, null, false);
    var observed = ImmutableList.CreateBuilder<string>();
    _mazakDbMock
      .When(database => database.Save(Arg.Is<MazakWriteData>(write => write.Prefix == "Add Parts")))
      .Do(call =>
      {
        var write = (MazakWriteData)call.Args()[0];
        foreach (var part in write.Parts)
        foreach (var process in part.Processes)
        {
          var route = _jobDB.LoadMazakRouteProvenance(part.PartName, process.ProcessNumber);
          if (
            route is null
            || route.JobUnique != MazakPart.UniqueFromComment(part.Comment)
            || route.NativeProgram != process.MainProgram
            || route.NativeFixture != process.Fixture
            || route.DownloadUid != MazakPart.ParseUID(part.PartName)
          )
            throw new System.InvalidOperationException(
              "Native publication preceded exact provenance."
            );
          observed.Add(part.PartName);
        }
      });

    await Assert
      .That(
        WriteJobs.SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime
        )
      )
      .IsTrue();
    await Assert.That(observed.Count).IsGreaterThan(0);
    foreach (var job in pending.Jobs)
      await Assert
        .That(_jobDB.LoadMazakRouteProvenanceForJob(job.UniqueStr).Count)
        .IsEqualTo(job.Processes.Count);
  }

  [Test]
  public async Task LaterDownloadsCannotReuseRetiredNativeGenerationNames()
  {
    var pending = DisjointPendingJobs();
    var template = pending.Jobs[0];
    _jobDB.AddJobs(
      pending with
      {
        ScheduleId = "first",
        Jobs = [template],
        StationUse = [],
        StationUseForCurrentStatus = [],
      },
      null,
      false
    );
    WriteJobs.SyncFromDatabase(
      _initialAllData,
      _jobDB,
      _mazakDbMock,
      _settings,
      _mazakCfg,
      fixtureQueueTime
    );
    var original = FindWrite("Add Parts").Parts.ToImmutableList();
    _mazakDbMock.ClearReceivedCalls();
    var replacement = template with { UniqueStr = "later-job" };
    _jobDB.AddJobs(
      pending with
      {
        ScheduleId = "later",
        Jobs = [replacement],
        StationUse = [],
        StationUseForCurrentStatus = [],
      },
      null,
      false
    );

    // The old generation has no rows in this observation, but its native names remain reserved.
    await Assert
      .That(
        WriteJobs.SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          fixtureQueueTime
        )
      )
      .IsTrue();
    var next = FindWrite("Add Parts").Parts;
    await Assert
      .That(
        next.Any(part =>
          original.Any(old => MazakPart.ParseUID(old.PartName) == MazakPart.ParseUID(part.PartName))
        )
      )
      .IsFalse();
    foreach (var part in original)
    foreach (var process in part.Processes)
      await Assert
        .That(_jobDB.LoadMazakRouteProvenance(part.PartName, process.ProcessNumber))
        .IsNotNull();
  }
}
