using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed partial class MazakAcquisitionSpec
{
  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task MatchingNewRowDoesNotGuessAnOlderSelectedProcessTwoEventOwner(bool openTail)
  {
    using var cell = new Cell();
    var job = SelectedJob(cell);
    cell.Repository.AddJobs(new NewJobs { ScheduleId = "schedule", Jobs = [job] }, null, true);
    var configuration = new MazakConfig
    {
      DBType = MazakDbType.MazakSmooth,
      UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
      CanQuarantineMissingMaterial = _ => false,
    };
    var pending = cell.Start with
    {
      Code = openTail ? LogCode.LoadEnd : LogCode.LoadBegin,
      FullPartName = "part:42:2",
      Process = 1,
      FixedQuantity = 1,
    };
    cell.Data = cell.Data with { Logs = [pending] };
    using (var sync = cell.NewSynchronizer(configuration))
    {
      var waiting = sync.CalculateCellState(cell.Repository);
      await Assert
        .That(waiting.PendingEventBatches.Single().State)
        .IsEqualTo(openTail ? ControllerEventBatchState.Open : ControllerEventBatchState.Received);
      if (!openTail)
        await Assert
          .That(waiting.CurrentStatus.AlarmDetails!.Any(a => a.Code == "mazak-event-pending"))
          .IsTrue();
      await Assert.That(cell.Repository.GetRecentLog(0)).IsEmpty();
      cell.Native.Received().DeleteLogs(pending.ForeignID);
    }
    // A matching new row is not historical provenance for this older event. Retain it for
    // reconciliation even after the normal native cursor and later manufacturing evidence advance.
    cell.Repository.RecordGeneralMessage(
      [],
      "later",
      "independent",
      foreignId: "LG20261105-120000-001.csv"
    );
    cell.Data = SelectedProcessTwoData(cell) with
    {
      Logs = openTail
        ?
        [
          pending with
          {
            ForeignID = "LG20261105-115800-002.csv",
          },
          pending with
          {
            ForeignID = "LG20261105-115800-003.csv",
            Code = LogCode.PalletMoving,
            Pallet = 2,
          },
        ]
        : [],
    };
    using var resumed = cell.NewSynchronizer(configuration);
    using var reopened = cell.Repositories.OpenConnection();
    var corrected = resumed.CalculateCellState(reopened);
    await Assert
      .That(corrected.PendingEventBatches.Single(b => b.BatchId == pending.ForeignID).State)
      .IsEqualTo(ControllerEventBatchState.Received);
    await Assert.That(reopened.MostRecentLogEntryForForeignID(pending.ForeignID)).IsNull();
    var beforeRetry = reopened.GetRecentLog(0).ToImmutableList();
    resumed.CalculateCellState(reopened);
    await Assert.That(reopened.GetRecentLog(0)).IsEquivalentTo(beforeRetry);
  }

  [Test]
  public async Task ExactSelectedTransferRetriesIndependentlyOfLaterAppliedNativeEvidence()
  {
    using var cell = new Cell();
    cell.Repository.AddJobs(
      new NewJobs { ScheduleId = "schedule", Jobs = [SelectedJob(cell)] },
      null,
      true
    );
    var id = cell.Repository.AllocateMaterialID("selected", "part", 2);
    cell.Repository.RecordPathForProcess(id, 2, 1);
    var load = cell.Start with
    {
      Code = LogCode.LoadEnd,
      FullPartName = "part:42:2",
      FixedQuantity = 1,
    };
    var separator = load with
    {
      Code = LogCode.PalletMoving,
      Pallet = 2,
      ForeignID = "LG20261105-115800-002.csv",
    };
    cell.Data = SelectedProcessTwoData(cell) with { Logs = [load, separator] };
    MazakConfig Configuration(bool outcomeKnown) =>
      new()
      {
        DBType = MazakDbType.MazakSmooth,
        UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
        CanQuarantineMissingMaterial = _ => false,
        ResolveLoadUnloadTransaction = (_, context) =>
          outcomeKnown
            ? new MazakLoadUnloadResolution.Resolved
            {
              MaterialForLoads = context.Loads.ToImmutableDictionary(
                face => face.ForeignId,
                _ => new MazakResolvedLoad { MaterialIds = [id] }
              ),
              MaterialForUnloads = ImmutableDictionary<string, MazakResolvedUnload>.Empty,
            }
            : new MazakLoadUnloadResolution.UnableToResolve("awaiting exact outcome"),
      };
    using (var sync = cell.NewSynchronizer(Configuration(false)))
    {
      var waiting = sync.CalculateCellState(cell.Repository);
      await Assert
        .That(waiting.PendingEventBatches.Single().State)
        .IsEqualTo(ControllerEventBatchState.Received);
      await Assert.That(cell.Repository.MostRecentLogEntryForForeignID(load.ForeignID)).IsNull();
      cell.Native.Received().DeleteLogs(separator.ForeignID);
    }
    var protection = MazakEventInbox.ResourceProtection(
      cell.Repository,
      cell.Data with
      {
        Schedules = [],
        Parts = [],
        PalletSubStatuses = [],
      }
    );
    await Assert.That(protection.ScheduleIds.Contains(2)).IsTrue();
    await Assert.That(protection.ProgramNames.Contains("1001")).IsTrue();
    await Assert.That(protection.FixtureNames.Contains("recorded:42:2")).IsTrue();
    cell.Data = cell.Data with { Logs = [] };
    using var resumed = cell.NewSynchronizer(Configuration(true));
    using var reopened = cell.Repositories.OpenConnection();
    var completed = resumed.CalculateCellState(reopened);
    await Assert.That(completed.PendingEventBatches).IsEmpty();
    var transfer = reopened.MostRecentLogEntryForForeignID(load.ForeignID);
    await Assert.That(transfer.Material.Single().MaterialID).IsEqualTo(id);
    await Assert.That(transfer.Material.Single().Process).IsEqualTo(2);
    var committed = reopened.GetRecentLog(0).ToImmutableList();
    resumed.CalculateCellState(reopened);
    await Assert.That(reopened.GetRecentLog(0)).IsEquivalentTo(committed);
  }

  private static Job SelectedJob(Cell cell) =>
    new()
    {
      UniqueStr = "selected",
      PartName = "part",
      Cycles = 1,
      Archived = false,
      RouteStartUTC = cell.Clock.Now.UtcDateTime.AddHours(-1),
      RouteEndUTC = cell.Clock.Now.UtcDateTime.AddDays(1),
      Processes =
      [
        new ProcessInfo { BasketLoadStations = [2], Paths = [JobLogTest.EmptyPath] },
        new ProcessInfo { Paths = [JobLogTest.EmptyPath] },
      ],
    };

  private static MazakAllDataAndLogs SelectedProcessTwoData(Cell cell) =>
    cell.Data with
    {
      Schedules =
      [
        new MazakScheduleRow
        {
          Id = 2,
          PartName = "part:42:2",
          Comment = "selected-2-1-InsightS",
          PlanQuantity = 1,
          Processes = [new MazakScheduleProcessRow { ProcessNumber = 1 }],
        },
      ],
      Parts =
      [
        new MazakPartRow
        {
          PartName = "part:42:2",
          Comment = "selected-2-1-InsightS",
          Processes =
          [
            new MazakPartProcessRow
            {
              ProcessNumber = 1,
              MainProgram = "1001",
              Fixture = "recorded:42:2",
            },
          ],
        },
      ],
    };
}
