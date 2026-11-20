#nullable enable
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

public sealed partial class MazakAcquisitionSpec
{
  [Test]
  [Arguments(null)]
  [Arguments("original-group")]
  public async Task ControllerGroupSurvivesANewerPeerSchedule(string? group)
  {
    using var cell = new Cell();
    var db = cell.Repository;
    NewJobs Schedule(string id, string stationGroup) =>
      new()
      {
        ScheduleId = id,
        Jobs =
        [
          new Job
          {
            UniqueStr = id,
            PartName = "part",
            Cycles = 1,
            Archived = false,
            RouteStartUTC = cell.Clock.Now.UtcDateTime.AddHours(-1),
            RouteEndUTC = cell.Clock.Now.UtcDateTime.AddHours(1),
            Processes =
            [
              new()
              {
                Paths =
                [
                  JobLogTest.EmptyPath with
                  {
                    Stops =
                    [
                      new()
                      {
                        StationGroup = stationGroup,
                        Stations = [1],
                        Program = "1001",
                        ExpectedCycleTime = TimeSpan.FromMinutes(1),
                      },
                    ],
                  },
                ],
              },
            ],
          },
        ],
      };
    db.AddJobs(Schedule("first", "original-group"), null, addAsCopiedToSystem: true);
    var config = new MazakConfig { DBType = MazakDbType.MazakSmooth, MachineGroupName = group };
    var settings = new FMSSettings();
    using var synchronization = new MazakSync(cell.Native, settings, config, cell.Clock);
    cell.Data = cell.Data with { Logs = [cell.Start, cell.End] };
    synchronization.CalculateCellState(db);
    await Assert
      .That(db.MostRecentLogEntryForForeignID(cell.Start.ForeignID).LocationName)
      .IsEqualTo("original-group");

    db.AddJobs(Schedule("second", "peer-group"), "first", addAsCopiedToSystem: true);
    cell.Clock.Now = cell.Clock.Now.AddMinutes(1);
    var next = cell.Start with
    {
      ForeignID = "LG20261105-120030-003.csv",
      TimeUTC = cell.Clock.Now.UtcDateTime.AddSeconds(-30),
    };
    cell.Data = cell.Data with
    {
      Logs = [next],
      Pallets = [new() { PalletNumber = 1, Fixture = "fixture" }],
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "M013" }],
    };
    var state = synchronization.CalculateCellState(db);
    var rebuilt = BuildCurrentStatus.Build(
      db,
      settings,
      config,
      state.AllData,
      "peer-group",
      null,
      cell.Clock.Now.UtcDateTime
    );
    var control = new MazakMachineControl(cell.Repositories, cell.Native, config);
    var expected = group ?? "peer-group";
    await Assert
      .That(db.MostRecentLogEntryForForeignID(next.ForeignID).LocationName)
      .IsEqualTo(expected);
    await Assert
      .That(state.CurrentStatus.Pallets[1].CurrentPalletLocation.StationGroup)
      .IsEqualTo(expected);
    await Assert.That(rebuilt.Pallets[1].CurrentPalletLocation.StationGroup).IsEqualTo(expected);
    await Assert
      .That(control.CurrentToolsInMachines().Single().MachineGroupName)
      .IsEqualTo(expected);
  }

  [Test]
  [Arguments(1)]
  [Arguments(101)]
  public async Task PendingLoadUnloadProjectionRetainsItsPalletAndCurrentQueueFacts(
    int startingPallet
  )
  {
    using var cell = new Cell();
    var json = new JsonSerializerOptions();
    FMSInsightWebHost.JsonSettings(json);
    var jobs = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
      json
    )!;
    cell.Repository.AddJobs(jobs, null, addAsCopiedToSystem: true);
    var data = JsonSerializer.Deserialize<MazakAllDataAndLogs>(
      File.ReadAllText(
        Path.Combine("..", "..", "..", "mazak", "read-snapshots", "basic-after-load.data.json")
      ),
      json
    )!;
    var assignment = data.PalletSubStatuses.Single();
    var schedule = data.Schedules.Single(s => s.Id == assignment.ScheduleID);
    var unique = MazakPart.ParseCommentInfo(schedule.Comment).Unique;
    var job = jobs.Jobs.Single(j => j.UniqueStr == unique);
    var configuration = new MazakConfig
    {
      DBType = MazakDbType.MazakSmooth,
      StartingPalletNumber = startingPallet,
    };
    var settings = new FMSSettings();
    settings.Queues["castings"] = new QueueInfo();
    var pallet = configuration.TranslatePalletNumber(assignment.PalletNumber);
    var previous = cell.Repository.AllocateMaterialID(unique, job.PartName, job.Processes.Count);
    cell.Repository.RecordLoadUnloadComplete(
      toLoad:
      [
        new MaterialToLoadOntoFace
        {
          MaterialIDs = [previous],
          Process = 1,
          Path = 1,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      toUnload: [],
      previouslyLoaded: [],
      previouslyUnloaded: [],
      pallet: pallet,
      lulNum: 1,
      totalElapsed: TimeSpan.Zero,
      timeUTC: cell.Clock.Now.UtcDateTime.AddMinutes(-10),
      externalQueues: ImmutableDictionary<string, string>.Empty
    );
    long QueueRaw()
    {
      var id = cell.Repository.AllocateMaterialID(unique, job.PartName, job.Processes.Count);
      cell.Repository.RecordAddMaterialToQueue(id, 0, "castings", -1, null, "SetByOperator");
      return id;
    }
    var raw = QueueRaw();
    var pending = cell.Start with
    {
      Code = LogCode.UnloadEnd,
      Pallet = assignment.PalletNumber,
      FullPartName = schedule.PartName,
      JobPartName = job.PartName,
      FixedQuantity = 1,
    };
    cell.Data = data with { Logs = [pending] };
    using var synchronization = new MazakSync(cell.Native, settings, configuration, cell.Clock);
    var acquired = synchronization.AcquireCellState("");
    var receipt = synchronization.ReconcileCellState(cell.Repository, acquired);
    var state = receipt.State;
    await Assert.That(state.PalletWithUnprocessedUnloads).IsEqualTo(pallet);
    await Assert.That(state.StoppedBecauseRecentLogEvent).IsTrue();
    await Assert
      .That(state.CurrentStatus.Material.Single(m => m.Location.PalletNum == pallet).MaterialID)
      .IsEqualTo(raw);

    // Current queue corrections contribute to a projection of retained controller tables, but
    // the incomplete batch must not revive the former pallet identity or process its events.
    cell.Repository.RecordRemoveMaterialFromAllQueues(raw, 0);
    var replacement = QueueRaw();
    var correctedHistory = cell.Repository.GetRecentLog(0).ToImmutableList();
    cell.Native.ClearReceivedCalls();
    var rebuilt = BuildCurrentStatus.Build(
      cell.Repository,
      settings,
      configuration,
      state.AllData,
      BuildCurrentStatus.FindMachineGroupName(cell.Repository),
      state.PalletWithUnprocessedUnloads,
      state.CurrentStatus.TimeOfCurrentStatusUTC
    );
    await Assert
      .That(rebuilt.Material.Single(m => m.Location.PalletNum == pallet).MaterialID)
      .IsEqualTo(replacement);
    await Assert
      .That(rebuilt.TimeOfCurrentStatusUTC)
      .IsEqualTo(state.CurrentStatus.TimeOfCurrentStatusUTC);
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEquivalentTo(correctedHistory);
    await Assert.That(cell.Native.ReceivedCalls()).IsEmpty();

    var replayed = synchronization.ReconcileCellState(cell.Repository, acquired);
    await Assert.That(replayed.State.PalletWithUnprocessedUnloads).IsEqualTo(pallet);
    await Assert.That(replayed.State.CurrentStatus.Material).IsEquivalentTo(rebuilt.Material);
    await Assert.That(replayed.CommittedLogWatermark).IsEqualTo(receipt.CommittedLogWatermark);
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEquivalentTo(correctedHistory);
    await Assert.That(cell.Native.ReceivedCalls()).IsEmpty();
  }
}
