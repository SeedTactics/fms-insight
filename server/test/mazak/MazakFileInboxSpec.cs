#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;
using NativeEvent = MazakMachineInterface.LogEntry;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed class MazakFileInboxSpec
{
  private const string Stream = "mazak:csv:v1";
  private const string JobUnique = "disk-job";
  private const string NativePart = "part:7:1:2";
  private const string Queue = "transfer";
  private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

  // Ordinary controller-owned split work exercises disk durability and the real translator.
  // This is not a qualification of selected-job adoption or controller generation replacement.
  [Test]
  public async Task SplitProcessTwoCompletesOnceAfterAllDatabaseHandlesCloseAndNativeLogsDisappear()
  {
    var directory = Directory.CreateTempSubdirectory("mazak-file-inbox-");
    try
    {
      var filename = Path.Combine(directory.FullName, "events.db");
      var clock = new TestClock();
      var native = Substitute.For<IMazakDB>();
      var data = ControllerData();
      var logs = ImmutableList<NativeEvent>.Empty;
      native
        .LoadAllDataAndLogs(Arg.Any<string>())
        .Returns(call =>
          data with
          {
            Logs = logs.Where(e =>
                StringComparer.Ordinal.Compare(e.ForeignID, call.Arg<string>()) > 0
              )
              .ToImmutableList(),
          }
        );
      // Acknowledgment actually removes the equipment's copy of the raw evidence.
      native
        .When(db => db.DeleteLogs(Arg.Any<string>()))
        .Do(call =>
          logs = logs.Where(e =>
              StringComparer.Ordinal.Compare(e.ForeignID, call.Arg<string>()) > 0
            )
            .ToImmutableList()
        );
      native
        .LoadTools()
        .Returns(
          ImmutableList.Create(
            new ToolPocketRow
            {
              MachineNumber = 1,
              PocketNumber = 1,
              GroupNo = "tool",
              IsToolDataValid = true,
              LifeUsed = 60,
              LifeSpan = 3600,
            }
          )
        );
      var settings = new FMSSettings();
      settings.Queues.Add(Queue, new QueueInfo { MaxSizeBeforeStopUnloading = -1 });
      var configuration = new MazakConfig { DBType = MazakDbType.MazakSmooth };
      var load = Event("LG20261105-115700-001.csv", LogCode.LoadEnd, Now.AddMinutes(-3));
      var start = Event("LG20261105-115800-001.csv", LogCode.MachineCycleStart, Now.AddMinutes(-2));
      var end = Event("LG20261105-115959-001.csv", LogCode.MachineCycleEnd, Now.AddSeconds(-1));
      long materialId;

      using (
        var repositories = RepositoryConfig.InitializeEventDatabase(null, filename, pooling: false)
      )
      using (var repository = repositories.OpenConnection())
      using (var sync = new MazakSync(native, settings, configuration, clock))
      {
        materialId = SeedCompletedProcessOne(repository);
        await Assert
          .That(repository.GetMaterialInQueueByUnique(Queue, JobUnique).Single().MaterialID)
          .IsEqualTo(materialId);
        logs = [load, start];
        sync.CalculateCellState(repository);
        await Assert.That(logs).IsEmpty();
        await Assert.That(repository.IsMaterialInQueue(materialId)).IsFalse();
        foreach (var source in new[] { load, start })
          await Assert
            .That(
              repository
                .MostRecentLogEntryForForeignID(source.ForeignID)
                .Material.Select(m => (m.MaterialID, m.Process))
            )
            .IsEquivalentTo([(materialId, 2)]);

        data = data with
        {
          Schedules = data
            .Schedules.Select(schedule =>
              schedule with
              {
                CompleteQuantity = 1,
                Processes = schedule
                  .Processes.Select(process => process with { ProcessExecuteQuantity = 0 })
                  .ToImmutableList(),
              }
            )
            .ToImmutableList(),
        };
        logs = [end];
        var settling = sync.CalculateCellState(repository);
        await Assert.That(settling.StoppedBecauseRecentLogEvent).IsTrue();
        await Assert.That(logs).IsEmpty();
        await Assert
          .That(repository.ControllerEventReceiptWatermark(Stream))
          .IsEqualTo(end.ForeignID);
        await Assert
          .That(repository.LoadPendingControllerEventBatches(Stream).Single().State)
          .IsEqualTo(ControllerEventBatchState.Received);
        await Assert
          .That(repository.GetJobProductionSummary(JobUnique).Completed.GetValueOrDefault((2, 1)))
          .IsEqualTo(0);
      }

      // No repository configuration or SQLite connection survives this boundary.
      clock.Now = Now.AddSeconds(30);
      native.ClearReceivedCalls();
      ImmutableList<BlackMaple.MachineFramework.LogEntry> completedHistory;
      string finalReceipt;
      using (
        var repositories = RepositoryConfig.InitializeEventDatabase(null, filename, pooling: false)
      )
      using (var repository = repositories.OpenConnection())
      using (var sync = new MazakSync(native, settings, configuration, clock))
      {
        await Assert.That(logs).IsEmpty();
        var acquisition = sync.AcquireCellState(sync.CaptureLogWatermark(repository));
        await Assert.That(acquisition.Data.Logs).IsEmpty();
        native.Received(1).LoadTools();
        var resumed = sync.ReconcileCellState(repository, acquisition);
        sync.AcknowledgeLogs(resumed);
        await Assert.That(repository.LoadPendingControllerEventBatches(Stream)).IsEmpty();
        var machining = repository.MostRecentLogEntryForForeignID(end.ForeignID);
        await Assert.That(machining.LogType).IsEqualTo(LogType.MachineCycle);
        await Assert.That(machining.StartOfCycle).IsFalse();
        await Assert
          .That(machining.Material.Select(m => (m.MaterialID, m.Process)))
          .IsEquivalentTo([(materialId, 2)]);

        clock.Now = Now.AddMinutes(2);
        var unload = Event("LG20261105-120100-001.csv", LogCode.UnloadEnd, Now.AddMinutes(1));
        var separator = Event(
          "LG20261105-120101-001.csv",
          LogCode.PalletMoveComplete,
          Now.AddMinutes(1).AddSeconds(1)
        ) with
        {
          TargetPosition = "S1",
        };
        data = data with
        {
          PalletSubStatuses = [],
          PalletPositions =
          [
            new MazakPalletPositionRow { PalletNumber = 1, PalletPosition = "S1" },
          ],
        };
        logs = [unload, separator];
        sync.CalculateCellState(repository);
        await Assert.That(logs).IsEmpty();
        await Assert.That(repository.LoadPendingControllerEventBatches(Stream)).IsEmpty();
        await Assert
          .That(
            repository
              .MostRecentLogEntryForForeignID(unload.ForeignID)
              .Material.Select(m => (m.MaterialID, m.Process))
          )
          .IsEquivalentTo([(materialId, 2)]);
        await Assert
          .That(repository.GetJobProductionSummary(JobUnique).AutomationEntryMaterialIds)
          .IsEquivalentTo([materialId]);
        await Assert
          .That(repository.GetJobProductionSummary(JobUnique).Completed)
          .IsEquivalentTo(
            ImmutableDictionary<(int Process, int Path), int>.Empty.Add((1, 1), 1).Add((2, 1), 1)
          );
        foreach (var source in new[] { load, start, end, unload })
          await Assert
            .That(repository.GetRecentLog(0).Count(e => e.ForeignID == source.ForeignID))
            .IsEqualTo(1);
        completedHistory = repository.GetRecentLog(0).ToImmutableList();
        finalReceipt = repository.ControllerEventReceiptWatermark(Stream);
      }

      using (
        var repositories = RepositoryConfig.InitializeEventDatabase(null, filename, pooling: false)
      )
      using (var repository = repositories.OpenConnection())
      using (var sync = new MazakSync(native, settings, configuration, clock))
      {
        sync.CalculateCellState(repository);
        sync.CalculateCellState(repository);
        await Assert.That(repository.GetRecentLog(0)).IsEquivalentTo(completedHistory);
        await Assert
          .That(repository.ControllerEventReceiptWatermark(Stream))
          .IsEqualTo(finalReceipt);
        await Assert.That(repository.LoadPendingControllerEventBatches(Stream)).IsEmpty();
        await Assert.That(repository.GetMaterialDetails(materialId + 1)).IsNull();
      }
    }
    finally
    {
      directory.Delete(recursive: true);
    }
  }

  private static long SeedCompletedProcessOne(IRepository repository)
  {
    repository.AddJobs(
      new NewJobs
      {
        ScheduleId = "disk-schedule",
        Jobs =
        [
          new Job
          {
            UniqueStr = JobUnique,
            PartName = "part",
            Cycles = 1,
            Archived = false,
            RouteStartUTC = Now.AddHours(-1),
            RouteEndUTC = Now.AddHours(1),
            Processes =
            [
              new ProcessInfo { Paths = [JobLogTest.EmptyPath with { OutputQueue = Queue }] },
              new ProcessInfo { Paths = [JobLogTest.EmptyPath with { InputQueue = Queue }] },
            ],
          },
        ],
      },
      null,
      true
    );
    var id = repository.AllocateMaterialID(JobUnique, "part", 2);
    var material = new EventLogMaterial
    {
      MaterialID = id,
      Process = 1,
      Face = 1,
    };
    repository
      .RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace
          {
            MaterialIDs = [id],
            Process = 1,
            Path = 1,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.FromSeconds(10),
          },
        ],
        previouslyLoaded: null,
        toUnload: null,
        previouslyUnloaded: null,
        lulNum: 1,
        pallet: 1,
        totalElapsed: TimeSpan.FromSeconds(10),
        timeUTC: Now.AddMinutes(-10),
        externalQueues: null
      )
      .ToImmutableList();
    repository.RecordMachineStart([material], 1, "MC", 1, "1001", Now.AddMinutes(-9));
    repository.RecordMachineEnd(
      [material],
      1,
      "MC",
      1,
      "1001",
      "",
      Now.AddMinutes(-8),
      TimeSpan.FromMinutes(1),
      TimeSpan.FromMinutes(1)
    );
    repository
      .RecordLoadUnloadComplete(
        toLoad: null,
        previouslyLoaded: null,
        toUnload:
        [
          new MaterialToUnloadFromFace
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              id,
              new UnloadDestination { Queue = Queue }
            ),
            Process = 1,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.FromSeconds(10),
          },
        ],
        previouslyUnloaded: null,
        lulNum: 1,
        pallet: 1,
        totalElapsed: TimeSpan.FromSeconds(10),
        timeUTC: Now.AddMinutes(-7),
        externalQueues: null
      )
      .ToImmutableList();
    return id;
  }

  private static NativeEvent Event(string id, LogCode code, DateTime time) =>
    new()
    {
      ForeignID = id,
      Code = code,
      TimeUTC = time,
      Pallet = 1,
      StationNumber = 1,
      FullPartName = NativePart,
      JobPartName = "part",
      Process = 1,
      FixedQuantity = 1,
      Program = "1002",
    };

  private static MazakAllDataAndLogs ControllerData() =>
    new()
    {
      Logs = [],
      MainPrograms = [],
      Fixtures = [],
      Pallets = [],
      PalletStatuses = [],
      LoadActions = [],
      Alarms = [],
      Schedules =
      [
        new MazakScheduleRow
        {
          Id = 2,
          PartName = NativePart,
          Comment = JobUnique + "-2-1-InsightS",
          PlanQuantity = 1,
          Processes =
          [
            new MazakScheduleProcessRow { ProcessNumber = 1, ProcessExecuteQuantity = 1 },
          ],
        },
      ],
      Parts =
      [
        new MazakPartRow
        {
          PartName = NativePart,
          Comment = JobUnique + "-2-1-InsightS",
          TotalProcess = 1,
          Processes =
          [
            new MazakPartProcessRow
            {
              PartName = NativePart,
              ProcessNumber = 1,
              FixQuantity = 1,
              FixLDS = "1",
              RemoveLDS = "1",
              CutMc = "1",
              MainProgram = "1002",
            },
          ],
        },
      ],
      PalletPositions = [new MazakPalletPositionRow { PalletNumber = 1, PalletPosition = "M012" }],
      PalletSubStatuses =
      [
        new MazakPalletSubStatusRow
        {
          PalletNumber = 1,
          ScheduleID = 2,
          PartProcessNumber = 1,
          FixQuantity = 1,
        },
      ],
    };

  private sealed class TestClock : TimeProvider
  {
    public DateTime Now { get; set; } = MazakFileInboxSpec.Now;

    public override DateTimeOffset GetUtcNow() => new(Now);
  }
}
