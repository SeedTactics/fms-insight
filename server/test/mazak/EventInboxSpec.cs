#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using Microsoft.Data.Sqlite;
using NSubstitute;
using TUnit.Assertions.Enums;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed partial class MazakAcquisitionSpec
{
  private const string InboxStream = "mazak:csv:v1";

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task PendingMaterialScopePreventsInferredRemovalOnAnotherPallet(bool pending)
  {
    using var cell = new Cell();
    var material = cell.Repository.AllocateMaterialID("tracked", "part", 1);
    cell.Repository.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace
          {
            MaterialIDs = [material],
            Process = 1,
            Path = 1,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        previouslyLoaded: null,
        toUnload: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: cell.Clock.Now.UtcDateTime.AddMinutes(-3),
        externalQueues: null
      )
      .ToImmutableList();
    if (pending)
    {
      var evidence = new MazakEventInbox.Evidence
      {
        Events = [cell.Start with { Pallet = 2, Code = LogCode.PalletMoving }],
        Dependencies = [MazakEventDependencies.Job("tracked")],
        Routes = [],
        SelectedWorkObserved = false,
        UnverifiedMaterialIdentityObserved = false,
      };
      cell.Repository.ReceiveControllerEventBatches(
        InboxStream,
        [
          new ControllerEventBatch
          {
            BatchId = cell.Start.ForeignID,
            LastEventId = cell.Start.ForeignID,
            Payload = JsonSerializer.Serialize(evidence),
            State = ControllerEventBatchState.Received,
          },
        ]
      );
      cell.Repository.SetControllerEventBatchState(
        InboxStream,
        cell.Start.ForeignID,
        ControllerEventBatchState.Applying
      );
    }
    cell.Data = cell.Data with
    {
      PalletPositions = [new MazakPalletPositionRow { PalletNumber = 1, PalletPosition = "S1" }],
    };
    using var sync = new MazakSync(
      cell.Native,
      new FMSSettings { QuarantineQueue = "quarantine" },
      new MazakConfig { DBType = MazakDbType.MazakSmooth },
      cell.Clock
    );
    sync.CalculateCellState(cell.Repository);
    await Assert.That(cell.Repository.IsMaterialInQueue(material)).IsEqualTo(!pending);
  }

  [Test]
  public async Task ReceivedMachineEndResumesAfterNativeAcknowledgmentAndRestart()
  {
    using var cell = new Cell();
    cell.Data = cell.Data with { Logs = [cell.Start] };
    cell.Sync.CalculateCellState(cell.Repository);
    var end = cell.End with { TimeUTC = cell.Clock.Now.UtcDateTime.AddSeconds(-1) };
    cell.Data = cell.Data with { Logs = [end] };
    var settling = cell.Sync.CalculateCellState(cell.Repository);
    await Assert.That(settling.StoppedBecauseRecentLogEvent).IsTrue();
    cell.Native.Received().DeleteLogs(end.ForeignID);
    await Assert
      .That(cell.Repository.LoadPendingControllerEventBatches(InboxStream).Single().State)
      .IsEqualTo(ControllerEventBatchState.Received);
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(1);

    cell.Data = cell.Data with { Logs = [] };
    cell.Clock.Now = cell.Clock.Now.AddSeconds(30);
    cell.Native.ClearReceivedCalls();
    using var resumed = cell.NewSynchronizer();
    using var reopened = cell.Repositories.OpenConnection();
    var acquisition = resumed.AcquireCellState(resumed.CaptureLogWatermark(reopened));
    await Assert.That(acquisition.Data.Logs).IsEmpty();
    cell.Native.Received(1).LoadTools();
    var completed = resumed.ReconcileCellState(reopened, acquisition);
    resumed.AcknowledgeLogs(completed);
    await Assert.That(completed.State.StoppedBecauseRecentLogEvent).IsFalse();
    await Assert.That(reopened.LoadPendingControllerEventBatches(InboxStream)).IsEmpty();
    await Assert
      .That(reopened.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(2);
  }

  [Test]
  public async Task OpenLoadUnloadTailRetainsGroupingAcrossAcknowledgmentAndRestart()
  {
    using var cell = new Cell();
    var load = cell.Start with
    {
      Code = LogCode.LoadEnd,
      FixedQuantity = 1,
      FullPartName = "part:1:1",
    };
    cell.Repository.AddJobs(
      new NewJobs
      {
        ScheduleId = "manual",
        Jobs =
        [
          new Job
          {
            UniqueStr = "manual",
            PartName = "part",
            Cycles = 2,
            Archived = false,
            RouteStartUTC = load.TimeUTC.AddHours(-1),
            RouteEndUTC = load.TimeUTC.AddHours(1),
            Processes =
            [
              new ProcessInfo { Paths = [BlackMaple.FMSInsight.Tests.JobLogTest.EmptyPath] },
            ],
          },
        ],
      },
      null,
      false
    );
    var previousMaterial = cell.Repository.AllocateMaterialID("manual", "part", 1);
    cell.Repository.RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace
          {
            MaterialIDs = [previousMaterial],
            Process = 1,
            Path = 1,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.FromSeconds(30),
          },
        ],
        previouslyLoaded: null,
        toUnload: null,
        previouslyUnloaded: null,
        lulNum: 1,
        pallet: 1,
        totalElapsed: TimeSpan.FromSeconds(30),
        timeUTC: load.TimeUTC.AddMinutes(-1),
        externalQueues: null
      )
      .ToImmutableList();
    cell.Repository.RecordMachineStart(
      [
        new EventLogMaterial
        {
          MaterialID = previousMaterial,
          Process = 1,
          Face = 1,
        },
      ],
      pallet: 1,
      statName: "MC",
      statNum: 1,
      program: "1001",
      timeUTC: load.TimeUTC.AddSeconds(-50)
    );
    cell.Repository.RecordMachineEnd(
      [
        new EventLogMaterial
        {
          MaterialID = previousMaterial,
          Process = 1,
          Face = 1,
        },
      ],
      pallet: 1,
      statName: "MC",
      statNum: 1,
      program: "1001",
      result: "",
      timeUTC: load.TimeUTC.AddSeconds(-10),
      elapsed: TimeSpan.FromSeconds(40),
      active: TimeSpan.FromSeconds(40)
    );
    cell.Data = cell.Data with
    {
      Schedules =
      [
        new MazakScheduleRow
        {
          Id = 1,
          PartName = load.FullPartName,
          Comment = "manual-Insight",
          PlanQuantity = 2,
          Processes = [new MazakScheduleProcessRow { ProcessNumber = 1 }],
        },
      ],
      Parts =
      [
        new MazakPartRow
        {
          PartName = load.FullPartName,
          Comment = "manual-Insight",
          TotalProcess = 1,
          Processes =
          [
            new MazakPartProcessRow
            {
              ProcessNumber = 1,
              FixQuantity = 1,
              FixLDS = "1",
              RemoveLDS = "1",
              CutMc = "1",
              MainProgram = "1001",
            },
          ],
        },
      ],
      PalletPositions = [new MazakPalletPositionRow { PalletNumber = 1, PalletPosition = "LS011" }],
      PalletSubStatuses =
      [
        new MazakPalletSubStatusRow
        {
          PalletNumber = 1,
          ScheduleID = 1,
          PartProcessNumber = 1,
          FixQuantity = 1,
        },
      ],
    };
    var unload = load with
    {
      ForeignID = "LG20261105-115800-002.csv",
      Code = LogCode.UnloadEnd,
      TimeUTC = load.TimeUTC.AddSeconds(1),
    };
    var separator = load with
    {
      ForeignID = "LG20261105-115800-003.csv",
      Code = LogCode.PalletMoving,
      TimeUTC = load.TimeUTC.AddSeconds(2),
    };
    cell.Data = cell.Data with { Logs = [load] };
    cell.Sync.CalculateCellState(cell.Repository);
    cell.Native.Received().DeleteLogs(load.ForeignID);
    await Assert
      .That(cell.Repository.LoadPendingControllerEventBatches(InboxStream).Single().State)
      .IsEqualTo(ControllerEventBatchState.Open);

    using var resumed = cell.NewSynchronizer();
    using var reopened = cell.Repositories.OpenConnection();
    var callbacks = ImmutableList.CreateBuilder<string>();
    resumed.MazakLogEvent += (entry, _) => callbacks.Add(entry.ForeignID);
    // The first file has disappeared from Mazak after its durable receipt.
    cell.Data = cell.Data with
    {
      Logs = [unload],
    };
    resumed.CalculateCellState(reopened);
    var open = reopened.LoadPendingControllerEventBatches(InboxStream).Single();
    await Assert.That(open.State).IsEqualTo(ControllerEventBatchState.Open);
    await Assert.That(callbacks).IsEmpty();
    using (var payload = JsonDocument.Parse(open.Payload))
      await Assert
        .That(
          payload
            .RootElement.GetProperty("Events")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("ForeignID").GetString()!)
        )
        .IsEquivalentTo([load.ForeignID, unload.ForeignID], CollectionOrdering.Matching);

    cell.Data = cell.Data with { Logs = [separator] };
    resumed.CalculateCellState(reopened);
    await Assert
      .That(callbacks)
      .IsEquivalentTo(
        [load.ForeignID, unload.ForeignID, separator.ForeignID],
        CollectionOrdering.Matching
      );
    await Assert.That(reopened.LoadPendingControllerEventBatches(InboxStream)).IsEmpty();
    var transferEvents = reopened
      .GetLogForJobUnique("manual")
      .Where(entry => entry.ForeignID == load.ForeignID || entry.ForeignID == unload.ForeignID)
      .ToImmutableList();
    await Assert.That(transferEvents.Count(entry => entry.Result == "UNLOAD")).IsEqualTo(1);
    await Assert.That(transferEvents.Count(entry => entry.Result == "LOAD")).IsEqualTo(1);
    await Assert
      .That(transferEvents.Single(entry => entry.Result == "UNLOAD").Material.Single().MaterialID)
      .IsEqualTo(previousMaterial);
    await Assert
      .That(transferEvents.Single(entry => entry.Result == "LOAD").Material.Single().MaterialID)
      .IsNotEqualTo(previousMaterial);
    resumed.CalculateCellState(reopened);
    await Assert.That(callbacks.Count).IsEqualTo(3);
  }

  [Test]
  public async Task InterruptedApplyingBatchRequiresReconciliationWithoutAutomaticReplay()
  {
    using var cell = new Cell();
    var recent = cell.End with { TimeUTC = cell.Clock.Now.UtcDateTime.AddSeconds(-1) };
    cell.Data = cell.Data with { Logs = [recent] };
    cell.Sync.CalculateCellState(cell.Repository);
    cell.Repository.SetControllerEventBatchState(
      InboxStream,
      recent.ForeignID,
      ControllerEventBatchState.Applying
    );
    cell.Data = cell.Data with { Logs = [] };
    cell.Clock.Now = cell.Clock.Now.AddMinutes(1);
    using var resumed = cell.NewSynchronizer();
    using var reopened = cell.Repositories.OpenConnection();
    var state = resumed.CalculateCellState(reopened);
    await Assert
      .That(() => resumed.DecrementJobs(reopened, state))
      .Throws<ConflictRequestException>();
    cell.Native.DidNotReceive().Save(Arg.Any<MazakWriteData>());
    resumed.CalculateCellState(reopened);
    var pending = reopened.LoadPendingControllerEventBatches(InboxStream).Single();
    await Assert.That(pending.State).IsEqualTo(ControllerEventBatchState.ReconciliationRequired);
    await Assert.That(pending.Diagnostic).IsNotNull();
    await Assert.That(reopened.GetRecentLog(0)).IsEmpty();
  }

  [Test]
  public async Task NotificationFailureAfterCommitRetainsUncertaintyWithoutDuplicatingEvents()
  {
    using var cell = new Cell();
    var start = cell.Start;
    void RejectNotification(
      BlackMaple.MachineFramework.LogEntry entry,
      string _,
      IRepository repository
    )
    {
      if (entry.ForeignID == start.ForeignID)
        throw new InvalidOperationException("notification failed after commit");
    }
    cell.Repositories.NewLogEntry += RejectNotification;
    cell.Data = cell.Data with { Logs = [start] };
    try
    {
      cell.Sync.CalculateCellState(cell.Repository);
    }
    finally
    {
      cell.Repositories.NewLogEntry -= RejectNotification;
    }
    var committed = cell.Repository.GetRecentLog(0).ToImmutableList();
    await Assert.That(committed.Count(e => e.LogType == LogType.MachineCycle)).IsEqualTo(1);
    await Assert
      .That(cell.Repository.LoadPendingControllerEventBatches(InboxStream).Single().State)
      .IsEqualTo(ControllerEventBatchState.ReconciliationRequired);
    cell.Data = cell.Data with { Logs = [] };
    using var resumed = cell.NewSynchronizer();
    using var reopened = cell.Repositories.OpenConnection();
    resumed.CalculateCellState(reopened);
    await Assert.That(reopened.GetRecentLog(0)).IsEquivalentTo(committed);
    await Assert
      .That(reopened.LoadPendingControllerEventBatches(InboxStream).Single().State)
      .IsEqualTo(ControllerEventBatchState.ReconciliationRequired);
  }

  [Test]
  public async Task FailedDurableReceiptPreventsNativeAcknowledgmentAndManufacturingEffects()
  {
    using var cell = new Cell();
    cell.Data = cell.Data with { Logs = [cell.Start] };
    using var connection = new SqliteConnection(
      $"Data Source=file:${cell.DatabaseId}?mode=memory&cache=shared"
    );
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
      "CREATE TRIGGER reject_receipt BEFORE INSERT ON controller_event_batches BEGIN SELECT RAISE(ABORT,'receipt failed'); END";
    command.ExecuteNonQuery();
    await Assert
      .That(() => cell.Sync.CalculateCellState(cell.Repository))
      .Throws<SqliteException>();
    cell.Native.DidNotReceive().DeleteLogs(Arg.Any<string>());
    await Assert.That(cell.Repository.ControllerEventReceiptWatermark(InboxStream)).IsEqualTo("");
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEmpty();
    command.CommandText = "DROP TRIGGER reject_receipt";
    command.ExecuteNonQuery();
    cell.Sync.CalculateCellState(cell.Repository);
    cell.Native.Received(1).DeleteLogs(cell.Start.ForeignID);
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(1);
  }

  [Test]
  public async Task ManufacturingForeignIdCannotAdvanceAnEstablishedReceiptCursor()
  {
    using var cell = new Cell();
    var start = cell.Start;
    cell.Data = cell.Data with { Logs = [start] };
    cell.Sync.CalculateCellState(cell.Repository);
    cell.Repository.RecordGeneralMessage(
      mats: [],
      program: "imported",
      result: "unrelated manufacturing evidence",
      foreignId: "LG20261105-120000-001.csv"
    );
    var end = cell.End;
    cell.Data = cell.Data with { Logs = [end] };
    using var resumed = cell.NewSynchronizer();
    using var reopened = cell.Repositories.OpenConnection();
    var receipt = resumed.CaptureLogWatermark(reopened);
    await Assert.That(receipt).IsEqualTo(start.ForeignID);
    var acquisition = resumed.AcquireCellState(receipt);
    await Assert.That(acquisition.Data.Logs).IsEquivalentTo([end]);
    resumed.AcknowledgeLogs(resumed.ReconcileCellState(reopened, acquisition));
    await Assert
      .That(reopened.ControllerEventReceiptWatermark(InboxStream))
      .IsEqualTo(end.ForeignID);
    await Assert
      .That(reopened.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(2);
  }
}
