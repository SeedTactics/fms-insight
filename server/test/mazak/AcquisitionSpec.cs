#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using Microsoft.Data.Sqlite;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed partial class MazakAcquisitionSpec
{
  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task StatusFailureAfterCommitRecoversAcrossRestartWithoutReplay(bool staged)
  {
    using var cell = new Cell();
    cell.Data = cell.Data with { Logs = [cell.Start] };
    cell.FailStatus = true;
    var acquired = cell.Sync.AcquireCellState("");
    await Assert
      .That(() =>
      {
        if (staged)
          cell.Sync.ReconcileCellState(cell.Repository, acquired);
        else
          cell.Sync.CalculateCellState(cell.Repository);
      })
      .Throws<InvalidOperationException>();
    cell.Native.DidNotReceive().DeleteLogs(Arg.Any<string>());
    var committed = cell.Repository.GetRecentLog(0).ToImmutableList();
    await Assert.That(committed.Count(e => e.LogType == LogType.MachineCycle)).IsEqualTo(1);
    // A new synchronizer/connection recovers from durable evidence, not the failed call's result.
    cell.FailStatus = false;
    using var resumed = cell.NewSynchronizer();
    using var reopened = cell.Repository.RepoConfig.OpenConnection();
    var next = resumed.AcquireCellState(resumed.CaptureLogWatermark(reopened));
    await Assert.That(next.Data.Logs).IsEmpty();
    resumed.AcknowledgeLogs(resumed.ReconcileCellState(reopened, next));
    cell.Native.Received(1).DeleteLogs(cell.Start.ForeignID);
    await Assert.That(reopened.GetRecentLog(0)).IsEquivalentTo(committed);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task IsolatedEventFailureAllowsLaterEvidenceAndStatusToContinue(bool staged)
  {
    using var cell = new Cell();
    var earlier = cell.Start with
    {
      ForeignID = "LG20261105-115700-001.csv",
      Code = LogCode.LoadBegin,
      TimeUTC = cell.Clock.Now.UtcDateTime.AddMinutes(-3),
    };
    cell.Data = cell.Data with
    {
      Logs = [earlier, cell.Start, cell.End with { Code = LogCode.LoadBegin }],
    };
    using var connection = new SqliteConnection(
      $"Data Source=file:${cell.DatabaseId}?mode=memory&cache=shared"
    );
    connection.Open();
    using var sql = connection.CreateCommand();
    sql.CommandText =
      $"CREATE TRIGGER reject_event BEFORE INSERT ON stations WHEN NEW.ForeignID='{cell.Start.ForeignID}' BEGIN SELECT RAISE(ABORT,'event commit failed'); END";
    sql.ExecuteNonQuery();
    MazakState Poll()
    {
      if (staged)
      {
        var acquired = cell.Sync.AcquireCellState(cell.Sync.CaptureLogWatermark(cell.Repository));
        var result = cell.Sync.ReconcileCellState(cell.Repository, acquired);
        cell.Sync.AcknowledgeLogs(result);
        return result.State;
      }
      else
        return cell.Sync.CalculateCellState(cell.Repository);
    }
    var state = Poll();
    await Assert.That(state.CurrentStatus.Jobs).IsEmpty();
    await Assert
      .That(state.CurrentStatus.TimeOfCurrentStatusUTC)
      .IsEqualTo(cell.Clock.Now.UtcDateTime);
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(0);
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.LoadUnloadCycle))
      .IsEqualTo(2);
    await Assert.That(cell.Sync.CaptureLogWatermark(cell.Repository)).IsEqualTo(cell.End.ForeignID);
    cell.Native.Received(1).DeleteLogs(cell.End.ForeignID);
    var committed = cell.Repository.GetRecentLog(0).ToImmutableList();
    Poll(); // A persistent bad record cannot prevent repeated fresh status construction.
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEquivalentTo(committed);
    sql.CommandText = "DROP TRIGGER reject_event";
    sql.ExecuteNonQuery();
    var next = cell.Start with { ForeignID = "LG20261105-120000-003.csv" };
    cell.Data = cell.Data with { Logs = cell.Data.Logs.Append(next).ToImmutableList() };
    Poll();
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(1);
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.LoadUnloadCycle))
      .IsEqualTo(2);
    cell.Native.Received(1).DeleteLogs(next.ForeignID);
  }

  [Test]
  [Arguments(false, "start")]
  [Arguments(true, "start")]
  [Arguments(false, "end")]
  [Arguments(true, "end")]
  [Arguments(false, "empty-end")]
  [Arguments(true, "empty-end")]
  public async Task MissingToolsDoNotBlockManufacturingOrInventMeasurements(
    bool staged,
    string failure
  )
  {
    using var cell = new Cell();
    var sync = cell.Sync;
    var repository = cell.Repository;
    MazakState Poll()
    {
      if (!staged)
        return sync.CalculateCellState(repository);
      var acquired = sync.AcquireCellState(sync.CaptureLogWatermark(repository));
      var result = sync.ReconcileCellState(repository, acquired);
      sync.AcknowledgeLogs(result);
      return result.State;
    }
    cell.Data = cell.Data with { Logs = [cell.Start] };
    if (failure == "start")
      cell.BeforeTools = () => throw new IOException("private tool error detail");
    var starting = Poll();
    await Assert
      .That(
        starting.CurrentStatus.AlarmDetails?.Any(a => a.Code == "mazak-tools-unavailable") == true
      )
      .IsEqualTo(failure == "start");
    var start = cell.Repository.GetRecentLog(0).Single(e => e.LogType == LogType.MachineCycle);
    await Assert
      .That(cell.Repository.ToolPocketSnapshotForCycle(start.Counter).Count())
      .IsEqualTo(failure == "start" ? 0 : 1);
    using var restarted = cell.NewSynchronizer();
    using var reopened = cell.Repository.RepoConfig.OpenConnection();
    sync = restarted;
    repository = reopened;
    cell.BeforeTools =
      failure == "end" ? () => throw new IOException("private tool error detail") : null;
    cell.Tools =
      failure == "empty-end"
        ? []
        : cell.Tools.Select(t => t with { LifeUsed = 120 }).ToImmutableList();
    var following = cell.End with
    {
      Code = LogCode.LoadBegin,
      ForeignID = "LG20261105-120000-003.csv",
    };
    cell.Data = cell.Data with { Logs = [cell.Start, cell.End, following] };
    var completed = Poll();
    var end = cell
      .Repository.GetRecentLog(0)
      .Single(e => e.LogType == LogType.MachineCycle && !e.StartOfCycle);
    await Assert.That(end.ElapsedTime).IsEqualTo(TimeSpan.FromSeconds(90));
    await Assert.That(end.Tools?.Count ?? 0).IsEqualTo(failure == "empty-end" ? 1 : 0);
    await Assert
      .That(repository.ToolPocketSnapshotForCycle(end.Counter).Count())
      .IsEqualTo(failure == "start" ? 1 : 0);
    if (failure == "empty-end")
      await Assert.That(end.Tools!.Single().ToolChangeOccurred).IsTrue();
    await Assert
      .That(cell.Sync.CaptureLogWatermark(cell.Repository))
      .IsEqualTo(following.ForeignID);
    await Assert
      .That(
        completed.CurrentStatus.AlarmDetails?.Any(a => a.Code == "mazak-tools-unavailable") == true
      )
      .IsEqualTo(failure == "end");
    await Assert
      .That(string.Join(" ", completed.CurrentStatus.Alarms))
      .DoesNotContain("private tool error detail");
    if (failure == "end")
      await Assert
        .That(
          completed
            .CurrentStatus.AlarmDetails!.Single(a => a.Code == "mazak-tools-unavailable")
            .Impact
        )
        .IsEqualTo(AlarmImpact.Informational);
    var committed = cell.Repository.GetRecentLog(0).ToImmutableList();
    var reads = cell
      .Native.ReceivedCalls()
      .Count(call => call.GetMethodInfo().Name == nameof(IMazakDB.LoadTools));
    var idle = Poll();
    await Assert
      .That(idle.CurrentStatus.AlarmDetails?.Any(a => a.Code == "mazak-tools-unavailable") == true)
      .IsEqualTo(failure == "end");
    await Assert
      .That(
        cell.Native.ReceivedCalls()
          .Count(call => call.GetMethodInfo().Name == nameof(IMazakDB.LoadTools))
      )
      .IsEqualTo(reads);
    await Assert.That(idle.StateUpdated).IsFalse();
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEquivalentTo(committed);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task ToolReadFailureClearsOnlyAfterSuccessfulObservation(bool observedEmpty)
  {
    using var cell = new Cell();
    cell.Data = cell.Data with { Logs = [cell.Start] };
    cell.BeforeTools = () => throw new IOException("unavailable");
    var failed = cell.Sync.CalculateCellState(cell.Repository);
    await Assert
      .That(
        failed.CurrentStatus.AlarmDetails?.Any(a => a.Code == "mazak-tools-unavailable") == true
      )
      .IsTrue();
    var committed = cell.Repository.GetRecentLog(0).ToImmutableList();
    var idle = cell.Sync.CalculateCellState(cell.Repository);
    await Assert
      .That(idle.CurrentStatus.AlarmDetails?.Any(a => a.Code == "mazak-tools-unavailable") == true)
      .IsTrue();
    await Assert.That(idle.StateUpdated).IsFalse();
    cell.Native.Received(1).LoadTools();
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEquivalentTo(committed);

    cell.BeforeTools = null;
    if (observedEmpty)
      cell.Tools = [];
    cell.Data = cell.Data with { Logs = [cell.Start, cell.End] };
    var recovered = cell.Sync.CalculateCellState(cell.Repository);
    cell.Native.Received(2).LoadTools();
    await Assert
      .That(
        recovered.CurrentStatus.AlarmDetails?.Any(a => a.Code == "mazak-tools-unavailable") == true
      )
      .IsFalse();
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(2);
    var completed = cell
      .Repository.GetRecentLog(0)
      .Single(e => e.LogType == LogType.MachineCycle && !e.StartOfCycle);
    await Assert.That(completed.Tools).IsNull();
  }

  [Test]
  public async Task ToolReadEvidenceSurvivesFailedStatusConstruction()
  {
    using var cell = new Cell();
    cell.Data = cell.Data with { Logs = [cell.Start] };
    cell.BeforeTools = () => throw new IOException("unavailable");
    cell.FailStatus = true;
    await Assert
      .That(() => cell.Sync.CalculateCellState(cell.Repository))
      .Throws<InvalidOperationException>();
    cell.FailStatus = false;
    var missing = cell.Sync.CalculateCellState(cell.Repository);
    await Assert.That(missing.AllData.Logs).IsEmpty();
    await Assert
      .That(
        missing.CurrentStatus.AlarmDetails?.Any(a => a.Code == "mazak-tools-unavailable") == true
      )
      .IsTrue();
    cell.Native.Received(1).LoadTools();
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(1);

    cell.BeforeTools = null;
    cell.FailStatus = true;
    cell.Data = cell.Data with { Logs = [cell.Start, cell.End] };
    await Assert
      .That(() => cell.Sync.CalculateCellState(cell.Repository))
      .Throws<InvalidOperationException>();
    cell.FailStatus = false;
    var recovered = cell.Sync.CalculateCellState(cell.Repository);
    await Assert.That(recovered.AllData.Logs).IsEmpty();
    await Assert
      .That(
        recovered.CurrentStatus.AlarmDetails?.Any(a => a.Code == "mazak-tools-unavailable") == true
      )
      .IsFalse();
    cell.Native.Received(2).LoadTools();
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(2);
  }

  [Test]
  [Arguments("controller")]
  [Arguments("tools")]
  [Arguments("acknowledgment")]
  public async Task BlockedEquipmentIoLeavesRepositoryOwnerAvailable(string boundary)
  {
    using var cell = new Cell();
    var owner = new Lock();
    var healthy = cell.Sync.AcquireCellState(cell.Sync.CaptureLogWatermark(cell.Repository));
    cell.Sync.AcknowledgeLogs(cell.Sync.ReconcileCellState(cell.Repository, healthy));
    cell.Data = cell.Data with { Logs = [cell.Start] };
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    void Block(string operation)
    {
      if (boundary != operation)
        return;
      entered.TrySetResult();
      release.Task.GetAwaiter().GetResult();
    }
    cell.BeforeRead = () => Block("controller");
    // This callback runs inside the deferred tool enumeration, not when LoadTools returns it.
    cell.BeforeTools = () => Block("tools");
    cell.BeforeAcknowledgment = () => Block("acknowledgment");
    var callbacks = 0;
    var callbackOwned = false;
    cell.Sync.MazakLogEvent += (_, _) =>
    {
      callbacks++;
      callbackOwned = owner.IsHeldByCurrentThread;
    };
    string watermark;
    lock (owner)
      watermark = cell.Sync.CaptureLogWatermark(cell.Repository);
    var cycle = Task.Run(() =>
    {
      var acquired = cell.Sync.AcquireCellState(watermark);
      MazakReconciliation reconciled;
      lock (owner)
        reconciled = cell.Sync.ReconcileCellState(cell.Repository, acquired);
      cell.Sync.AcknowledgeLogs(reconciled);
    });
    try
    {
      await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
      await Task.Run(() =>
        {
          lock (owner)
            cell.Repository.RecordGeneralMessage(
              mat: null,
              "operator",
              "changed during I/O",
              foreignId: "operator:edit"
            );
        })
        .WaitAsync(TimeSpan.FromSeconds(2));
      // Acquisition cannot commit early. A blocked acknowledgment follows the actual commit.
      string committed;
      lock (owner)
        committed = cell.Sync.CaptureLogWatermark(cell.Repository);
      await Assert
        .That(committed)
        .IsEqualTo(boundary == "acknowledgment" ? cell.Start.ForeignID : "");
    }
    finally
    {
      release.TrySetResult();
      await cycle.WaitAsync(TimeSpan.FromSeconds(10));
    }
    await Assert.That(callbacks).IsEqualTo(1);
    await Assert.That(callbackOwned).IsTrue();
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.GeneralMessage))
      .IsEqualTo(1);
    var start = cell.Repository.GetRecentLog(0).Single(e => e.LogType == LogType.MachineCycle);
    await Assert.That(start.StartOfCycle).IsTrue();
    await Assert
      .That(cell.Repository.ToolPocketSnapshotForCycle(start.Counter).Single().ToolName)
      .IsEqualTo("tool-1");
    cell.Native.Received(1).DeleteLogs(cell.Start.ForeignID);
    // Reconciliation has no built-in controller calls, including hidden tool enumerations.
    cell.Data = cell.Data with
    {
      Logs = [],
    };
    var next = cell.Sync.AcquireCellState(cell.Sync.CaptureLogWatermark(cell.Repository));
    cell.Native.ClearReceivedCalls();
    var unchanged = cell.Sync.ReconcileCellState(cell.Repository, next);
    await Assert.That(cell.Native.ReceivedCalls()).IsEmpty();
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(1);
    cell.Sync.AcknowledgeLogs(unchanged);
  }

  [Test]
  public async Task AvailableObservationCanRebuildStatusAfterLocalFailureWithoutRereadingSource()
  {
    using var cell = new Cell();
    cell.Data = cell.Data with { Logs = [cell.Start] };
    var observation = cell.Sync.AcquireCellState(cell.Sync.CaptureLogWatermark(cell.Repository));
    cell.FailStatus = true;
    await Assert
      .That(() => cell.Sync.ReconcileCellState(cell.Repository, observation))
      .Throws<InvalidOperationException>();
    cell.Repository.RecordGeneralMessage(
      mat: null,
      "operator",
      "current repository correction",
      foreignId: "operator:correction"
    );
    var committed = cell.Repository.GetRecentLog(0).ToImmutableList();
    cell.FailStatus = false;
    cell.BeforeRead = () => throw new IOException("source now unavailable");
    cell.Native.ClearReceivedCalls();
    var recovered = cell.Sync.ReconcileCellState(cell.Repository, observation);
    await Assert.That(recovered.State.AllData.Logs).IsEmpty();
    await Assert
      .That(recovered.State.CurrentStatus.TimeOfCurrentStatusUTC)
      .IsEqualTo(observation.StartedAtUtc);
    await Assert.That(recovered.CommittedLogWatermark).IsEqualTo(cell.Start.ForeignID);
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEquivalentTo(committed);
    await Assert.That(cell.Native.ReceivedCalls()).IsEmpty();
    cell.Sync.AcknowledgeLogs(recovered);
    cell.Native.Received(1).DeleteLogs(cell.Start.ForeignID);
  }

  [Test]
  public async Task DelayedAndRetriedAcknowledgmentUsesItsOwnCommittedReceipt()
  {
    using var cell = new Cell();
    cell.Data = cell.Data with { Logs = [cell.Start] };
    var acquired = cell.Sync.AcquireCellState(cell.Sync.CaptureLogWatermark(cell.Repository));
    cell.Native.ClearReceivedCalls();
    var first = cell.Sync.ReconcileCellState(cell.Repository, acquired);
    await Assert.That(cell.Native.ReceivedCalls()).IsEmpty();
    var beforeReplay = cell.Repository.GetRecentLog(0).ToImmutableList();
    var replayed = cell.Sync.ReconcileCellState(cell.Repository, acquired);
    await Assert.That(replayed.State.AllData.Logs).IsEmpty();
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEquivalentTo(beforeReplay);

    cell.Data = cell.Data with { Logs = [cell.Start, cell.End] };
    var second = cell.Sync.ReconcileCellState(
      cell.Repository,
      cell.Sync.AcquireCellState(cell.Sync.CaptureLogWatermark(cell.Repository))
    );
    cell.Repository.RecordGeneralMessage(
      mat: null,
      "other producer",
      "unrelated",
      foreignId: "zz:later"
    );
    cell.Sync.AcknowledgeLogs(first);
    cell.Native.Received(1).DeleteLogs(cell.Start.ForeignID);
    cell.Native.DidNotReceive().DeleteLogs(cell.End.ForeignID);
    await Assert.That(second.CommittedLogWatermark).IsEqualTo(cell.End.ForeignID);

    cell.BeforeAcknowledgment = () => throw new IOException("acknowledgment response lost");
    await Assert.That(() => cell.Sync.AcknowledgeLogs(second)).Throws<IOException>();
    var afterCommit = cell.Repository.GetRecentLog(0).ToImmutableList();
    cell.BeforeAcknowledgment = null;
    cell.Sync.AcknowledgeLogs(second);
    cell.Native.Received(2).DeleteLogs(cell.End.ForeignID);
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEquivalentTo(afterCommit);
    var cycles = afterCommit.Where(e => e.LogType == LogType.MachineCycle).ToImmutableList();
    await Assert.That(cycles.Count).IsEqualTo(2);
    await Assert.That(cycles.Count(e => e.StartOfCycle)).IsEqualTo(1);
    await Assert
      .That(cycles.Single(e => !e.StartOfCycle).ElapsedTime)
      .IsEqualTo(TimeSpan.FromSeconds(90));
  }

  [Test]
  public async Task RecentCycleEndWaitsForToolsAcquiredAfterItsSettlingWindow()
  {
    using var cell = new Cell();
    cell.Data = cell.Data with { Logs = [cell.Start] };
    cell.Sync.ReconcileCellState(cell.Repository, cell.Sync.AcquireCellState(""));
    var before = cell.Repository.GetRecentLog(0).ToImmutableList();
    var watermark = cell.Sync.CaptureLogWatermark(cell.Repository);
    cell.Native.ClearReceivedCalls();
    var recent = cell.End with { TimeUTC = cell.Clock.Now.UtcDateTime.AddSeconds(-1) };
    cell.Data = cell.Data with { Logs = [recent] };
    var early = cell.Sync.AcquireCellState(watermark);
    cell.Native.DidNotReceive().LoadTools();
    cell.Clock.Now = cell.Clock.Now.AddSeconds(30);
    var deferred = cell.Sync.ReconcileCellState(cell.Repository, early);
    await Assert.That(deferred.State.StoppedBecauseRecentLogEvent).IsTrue();
    await Assert.That(deferred.CommittedLogWatermark).IsEqualTo(watermark);
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEquivalentTo(before);
    await Assert
      .That(deferred.State.CurrentStatus.TimeOfCurrentStatusUTC)
      .IsEqualTo(early.StartedAtUtc);
    var settled = cell.Sync.AcquireCellState(watermark);
    cell.Native.Received(1).LoadTools();
    // The full later read supplies tools and is eligible to consume the event.
    var completed = cell.Sync.ReconcileCellState(cell.Repository, settled);
    await Assert.That(completed.State.StoppedBecauseRecentLogEvent).IsFalse();
    await Assert.That(completed.CommittedLogWatermark).IsEqualTo(recent.ForeignID);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task CycleEndingDuringAcquisitionWaitsForLaterObservation(bool staged)
  {
    using var cell = new Cell();
    MazakState Poll()
    {
      if (!staged)
        return cell.Sync.CalculateCellState(cell.Repository);
      var acquisition = cell.Sync.AcquireCellState(cell.Sync.CaptureLogWatermark(cell.Repository));
      var result = cell.Sync.ReconcileCellState(cell.Repository, acquisition);
      cell.Sync.AcknowledgeLogs(result);
      return result.State;
    }

    cell.Data = cell.Data with { Logs = [cell.Start] };
    Poll();
    var before = cell.Repository.GetRecentLog(0).ToImmutableList();
    var began = cell.Clock.Now;
    var end = cell.End with { TimeUTC = began.UtcDateTime.AddSeconds(20) };
    cell.Data = cell.Data with { Logs = [end] };
    // The controller read completes just after a cycle ends, before its tool counters settle.
    cell.BeforeRead = () => cell.Clock.Now = began.AddSeconds(21);
    cell.Native.ClearReceivedCalls();
    var early = Poll();
    await Assert.That(early.StoppedBecauseRecentLogEvent).IsTrue();
    cell.Native.DidNotReceive().LoadTools();
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEquivalentTo(before);

    cell.BeforeRead = null;
    cell.Clock.Now = began.AddSeconds(36);
    var settled = Poll();
    await Assert.That(settled.StoppedBecauseRecentLogEvent).IsFalse();
    cell.Native.Received(1).LoadTools();
    await Assert.That(cell.Sync.CaptureLogWatermark(cell.Repository)).IsEqualTo(end.ForeignID);
    await Assert
      .That(cell.Repository.GetRecentLog(0).Count(e => e.LogType == LogType.MachineCycle))
      .IsEqualTo(2);
  }

  [Test]
  public async Task AcquisitionMaterializesDeferredRowsAndCopiesNestedCollections()
  {
    using var cell = new Cell();
    var processes = new List<MazakPartProcessRow> { new() { ProcessNumber = 1 } };
    var scheduleProcesses = new List<MazakScheduleProcessRow> { new() { ProcessNumber = 1 } };
    var logs = new List<MazakMachineInterface.LogEntry> { cell.Start };
    var material = new List<MazakStationMaterial>
    {
      new()
      {
        PartName = "original",
        Process = 1,
        Quantity = 1,
      },
    };
    var enumerations = 0;
    IEnumerable<MazakPartRow> DeferredParts()
    {
      enumerations++;
      yield return new MazakPartRow { PartName = "part", Processes = processes };
    }
    cell.Data = cell.Data with
    {
      Parts = DeferredParts(),
      Schedules = [new MazakScheduleRow { Id = 1, Processes = scheduleProcesses }],
      Logs = logs,
      LoadActions =
      [
        new LoadAction
        {
          LoadStation = 1,
          StationPallet = new MazakStationPallet { PalletNumber = 1, Material = material },
        },
      ],
    };
    var acquired = cell.Sync.AcquireCellState("");
    await Assert.That(enumerations).IsEqualTo(1);
    processes.Clear();
    scheduleProcesses.Clear();
    logs.Clear();
    material.Clear();
    cell.Tools = [];
    await Assert.That(acquired.Data.Parts.Single().Processes.Count).IsEqualTo(1);
    await Assert.That(acquired.Data.Schedules.Single().Processes.Count).IsEqualTo(1);
    await Assert.That(acquired.Data.Logs.Count).IsEqualTo(1);
    await Assert.That(acquired.Tools!.Count).IsEqualTo(1);
    await Assert
      .That(acquired.Data.LoadActions.Single().StationPallet.Material.Single().PartName)
      .IsEqualTo("original");
    await Assert.That(enumerations).IsEqualTo(1);
  }

  private sealed class Clock : TimeProvider
  {
    public DateTimeOffset Now { get; set; } = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
  }

  private sealed class Cell : IDisposable
  {
    public Guid DatabaseId { get; } = Guid.NewGuid();
    private readonly RepositoryConfig repositories;
    public IRepository Repository { get; }
    public RepositoryConfig Repositories => repositories;
    public IMazakDB Native { get; } = Substitute.For<IMazakDB>();
    public Clock Clock { get; } = new();
    public MazakSync Sync { get; }
    public Action? BeforeRead { get; set; }
    public Action? BeforeTools { get; set; }
    public Action? BeforeAcknowledgment { get; set; }
    public bool FailStatus { get; set; }
    public ImmutableList<ToolPocketRow> Tools { get; set; } =
    [
      new()
      {
        MachineNumber = 1,
        PocketNumber = 1,
        GroupNo = "tool-1",
        IsToolDataValid = true,
        LifeUsed = 60,
        LifeSpan = 3600,
      },
    ];
    public MazakAllDataAndLogs Data { get; set; } =
      new()
      {
        MainPrograms = [],
        Fixtures = [],
        Pallets = [],
        Parts = [],
        Schedules = [],
        PalletPositions = [],
        PalletSubStatuses = [],
        LoadActions = [],
        Logs = [],
      };
    public MazakMachineInterface.LogEntry Start =>
      new()
      {
        ForeignID = "LG20261105-115800-001.csv",
        TimeUTC = Clock.Now.UtcDateTime.AddMinutes(-2),
        Code = LogCode.MachineCycleStart,
        Pallet = 1,
        StationNumber = 1,
        FullPartName = "part",
        JobPartName = "part",
        Process = 1,
        Program = "1001",
        FixedQuantity = 0,
      };
    public MazakMachineInterface.LogEntry End =>
      Start with
      {
        ForeignID = "LG20261105-115930-002.csv",
        TimeUTC = Clock.Now.UtcDateTime.AddSeconds(-30),
        Code = LogCode.MachineCycleEnd,
      };

    public Cell()
    {
      repositories = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings { ConvertMaterialIDToSerial = _ => "" },
        DatabaseId
      );
      Repository = repositories.OpenConnection();
      Native
        .LoadAllDataAndLogs(Arg.Any<string>())
        .Returns(call =>
        {
          BeforeRead?.Invoke();
          return Data with
          {
            Logs = Data
              .Logs.Where(e => StringComparer.Ordinal.Compare(e.ForeignID, call.Arg<string>()) > 0)
              .ToImmutableList(),
          };
        });
      Native.LoadTools().Returns(_ => ReadTools());
      Native.When(n => n.DeleteLogs(Arg.Any<string>())).Do(_ => BeforeAcknowledgment?.Invoke());
      Sync = NewSynchronizer();
    }

    public MazakSync NewSynchronizer() =>
      new(
        Native,
        new FMSSettings(),
        new MazakConfig
        {
          DBType = MazakDbType.MazakSmooth,
          ProgramDirectory = "unused",
          CanQuarantineMissingMaterial = _ => false,
          AdjustCurrentStatus = (_, status) =>
            FailStatus ? throw new InvalidOperationException("status construction failed") : status,
        },
        Clock
      );

    private IEnumerable<ToolPocketRow> ReadTools()
    {
      BeforeTools?.Invoke();
      foreach (var tool in Tools)
        yield return tool;
    }

    public void Dispose()
    {
      Sync.Dispose();
      Repository.Dispose();
      repositories.Dispose();
    }
  }
}
