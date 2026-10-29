using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed class QuantityOwnershipSpec : IDisposable
{
  private readonly RepositoryConfig _repositories = RepositoryConfig.InitializeMemoryDB(
    null,
    Guid.NewGuid()
  );
  private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
  private static readonly MazakConfig Configuration = new()
  {
    DBType = MazakDbType.MazakSmooth,
    UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
  };

  public void Dispose() => _repositories.Dispose();

  private static Job Job(string unique, bool backendOwned = true) =>
    new()
    {
      UniqueStr = unique,
      PartName = unique,
      Cycles = 10,
      Archived = false,
      RouteStartUTC = Now.AddHours(-1),
      RouteEndUTC = Now.AddDays(1),
      Processes =
      [
        new ProcessInfo
        {
          BasketLoadStations = backendOwned ? [2] : null,
          Paths =
          [
            JobLogTest.EmptyPath with
            {
              PalletNums = [1],
              Load = [1],
              Unload = [1],
              Fixture = "fixture",
            },
          ],
        },
      ],
    };

  private static MazakAllData Data(params Job[] jobs) =>
    new()
    {
      Schedules = jobs.Select(
          (j, idx) =>
            new MazakScheduleRow
            {
              Id = idx + 1,
              PartName = j.PartName + ":1:1",
              Comment = j.UniqueStr + "-Insight",
              PlanQuantity = j.Cycles,
              Processes =
              [
                new MazakScheduleProcessRow
                {
                  MazakScheduleRowId = idx + 1,
                  ProcessNumber = 1,
                  ProcessMaterialQuantity = j.Cycles,
                },
              ],
            }
        )
        .ToImmutableList(),
      Parts = jobs.Select(j => new MazakPartRow
        {
          PartName = j.PartName + ":1:1",
          Comment = j.UniqueStr + "-Insight",
          Processes =
          [
            new MazakPartProcessRow
            {
              ProcessNumber = 1,
              PartName = j.PartName + ":1:1",
              Fixture = "fixture",
              FixQuantity = 1,
              FixLDS = "1",
              RemoveLDS = "1",
              CutMc = "1",
              MainProgram = "1001",
            },
          ],
        })
        .ToImmutableList(),
      Pallets = [],
      PalletSubStatuses = [],
      PalletPositions = [],
      PalletStatuses = [],
      LoadActions = [],
      Alarms = [],
      Fixtures = [],
      MainPrograms = [],
    };

  private CurrentStatus Status(MazakAllData data)
  {
    // Each observation opens a fresh connection and rebuilds accounting, as after restart.
    using var repository = _repositories.OpenConnection();
    return BuildCurrentStatus.Build(
      repository,
      new FMSSettings(),
      Configuration,
      data,
      "MC",
      null,
      Now
    );
  }

  [Test]
  public async Task CancellationSurvivesCapacityIncreaseAndMissingSchedules()
  {
    using var repository = _repositories.OpenConnection();
    var local = Job("local");
    var ordinary = Job("ordinary", backendOwned: false);
    repository.AddJobs(
      new NewJobs { ScheduleId = "schedule", Jobs = [local, ordinary] },
      null,
      true
    );
    var identities = Enumerable
      .Range(0, 2)
      .Select(_ => repository.AllocateMaterialID(local.UniqueStr, local.PartName, 1))
      .ToImmutableList();
    repository.RecordLoadUnloadComplete(
      toLoad:
      [
        new MaterialToLoadOntoFace
        {
          MaterialIDs = identities,
          Process = 1,
          Path = 1,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      toUnload: [],
      previouslyLoaded: [],
      previouslyUnloaded: [],
      pallet: 1,
      lulNum: 1,
      totalElapsed: TimeSpan.Zero,
      timeUTC: Now,
      externalQueues: ImmutableDictionary<string, string>.Empty
    );
    var controller = Data(local, ordinary);
    await Assert.That(Status(controller).Jobs[local.UniqueStr].RemainingToStart).IsEqualTo(8);
    repository.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = local.UniqueStr,
          Part = local.PartName,
          Quantity = 8,
        },
      ],
      Now
    );
    var canceled = Status(controller).Jobs[local.UniqueStr];
    await Assert.That(canceled.Cycles).IsEqualTo(2);
    await Assert.That(canceled.RemainingToStart).IsEqualTo(0);

    controller = controller with
    {
      Schedules = controller.Schedules.Select(s => s with { PlanQuantity = 40 }).ToImmutableList(),
    };
    var increased = Status(controller);
    await Assert.That(increased.Jobs[local.UniqueStr].Cycles).IsEqualTo(2);
    await Assert.That(increased.Jobs[local.UniqueStr].RemainingToStart).IsEqualTo(0);
    await Assert.That(increased.Jobs[ordinary.UniqueStr].Cycles).IsEqualTo(40);

    repository.RecordLoadUnloadComplete(
      toLoad: [],
      toUnload:
      [
        new MaterialToUnloadFromFace
        {
          MaterialIDToDestination = identities.ToImmutableDictionary(
            id => id,
            _ => (UnloadDestination)null
          ),
          FaceNum = 1,
          Process = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      previouslyLoaded: [],
      previouslyUnloaded: [],
      pallet: 1,
      lulNum: 1,
      totalElapsed: TimeSpan.Zero,
      timeUTC: Now.AddMinutes(1),
      externalQueues: ImmutableDictionary<string, string>.Empty
    );
    var finished = Status(controller).Jobs[local.UniqueStr];
    await Assert.That(finished.Completed[0][0]).IsEqualTo(2);
    await Assert.That(finished.RemainingToStart).IsEqualTo(0);

    var withoutSchedules = Status(Data());
    await Assert.That(withoutSchedules.Jobs[local.UniqueStr].Cycles).IsEqualTo(2);
    await Assert.That(withoutSchedules.Jobs[local.UniqueStr].RemainingToStart).IsEqualTo(0);
    await Assert.That(withoutSchedules.Jobs[local.UniqueStr].CopiedToSystem).IsTrue();
    await Assert.That(withoutSchedules.Jobs[local.UniqueStr].Completed[0][0]).IsEqualTo(2);
    await Assert.That(withoutSchedules.Jobs.ContainsKey(ordinary.UniqueStr)).IsFalse();
    await Assert
      .That(repository.LoadDecrementsForJob(local.UniqueStr).Sum(d => d.Quantity))
      .IsEqualTo(8);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task ArchivedManagedJobsStayHiddenWithControllerSchedules(bool copied)
  {
    using var repository = _repositories.OpenConnection();
    var local = Job("local") with { RouteStartUTC = DateTime.UtcNow.AddMinutes(-1) };
    var ordinary = Job("ordinary", backendOwned: false);
    repository.AddJobs(
      new NewJobs { ScheduleId = "schedule", Jobs = [local, ordinary] },
      null,
      copied
    );
    repository.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = local.UniqueStr,
          Part = local.PartName,
          Quantity = local.Cycles,
        },
      ],
      Now
    );
    var controller = Data(local, ordinary);
    await Assert.That(Status(controller).Jobs[local.UniqueStr].Cycles).IsEqualTo(0);

    // Controller cleanup may lag behind explicit archival. A later or retained observation must
    // not turn that controller capacity back into active demand for a backend-owned job.
    repository.ArchiveJob(local.UniqueStr);
    var archived = Status(controller);
    await Assert.That(archived.Jobs.ContainsKey(local.UniqueStr)).IsFalse();
    await Assert.That(archived.Jobs[ordinary.UniqueStr].Cycles).IsEqualTo(ordinary.Cycles);
    await Assert.That(Status(Data()).Jobs.ContainsKey(local.UniqueStr)).IsFalse();
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task PendingControllerJobsDoNotReopenCanceledDemand(bool fullCancellation)
  {
    using var repository = _repositories.OpenConnection();
    var pending = Job("pending", backendOwned: false) with
    {
      RouteStartUTC = DateTime.UtcNow.AddMinutes(-1),
    };
    repository.AddJobs(new NewJobs { ScheduleId = "pending", Jobs = [pending] }, null, false);
    var canceled = fullCancellation ? 10 : 8;
    repository.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = pending.UniqueStr,
          Part = pending.PartName,
          Quantity = canceled,
        },
      ],
      Now
    );
    var status = BuildCurrentStatus.Build(
      repository,
      new FMSSettings(),
      Configuration with
      {
        UseInsightJobQuantities = null,
      },
      Data(),
      "MC",
      null,
      Now
    );
    await Assert.That(status.Jobs[pending.UniqueStr].Cycles).IsEqualTo(10 - canceled);
    await Assert.That(status.Jobs[pending.UniqueStr].RemainingToStart).IsEqualTo(0);
    await Assert.That(status.Jobs[pending.UniqueStr].CopiedToSystem).IsFalse();
  }

  [Test]
  public async Task PendingManagedJobsOutsideDownloadWindowRemainVisible()
  {
    using var repository = _repositories.OpenConnection();
    var old = Job("old") with { RouteStartUTC = Now.AddDays(-10) };
    var future = Job("future") with { RouteStartUTC = Now.AddDays(10) };
    repository.AddJobs(new NewJobs { ScheduleId = "pending", Jobs = [old, future] }, null, false);
    repository.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = future.UniqueStr,
          Part = future.PartName,
          Quantity = 10,
        },
      ],
      Now
    );
    var status = Status(Data());
    await Assert.That(status.Jobs[old.UniqueStr].Cycles).IsEqualTo(10);
    await Assert.That(status.Jobs[old.UniqueStr].RemainingToStart).IsEqualTo(10);
    await Assert.That(status.Jobs[future.UniqueStr].Cycles).IsEqualTo(0);
    await Assert.That(status.Jobs[future.UniqueStr].RemainingToStart).IsEqualTo(0);
    await Assert.That(status.Jobs[future.UniqueStr].CopiedToSystem).IsFalse();
  }

  [Test]
  public async Task ManagedSchedulesStartWithoutSupplyAndSkipGenericReconciliation()
  {
    using var repository = _repositories.OpenConnection();
    var local = Job("local");
    var ordinary = Job("ordinary", backendOwned: false);
    repository.AddJobs(
      new NewJobs { ScheduleId = "schedule", Jobs = [local, ordinary] },
      null,
      true
    );
    var parts = Data(local, ordinary) with { Schedules = [] };
    var download = BuildMazakSchedules.AddSchedules(parts, [local, ordinary], Configuration);
    await Assert
      .That(
        download
          .Schedules.Single(s => s.Comment == "local-Insight")
          .Processes[0]
          .ProcessMaterialQuantity
      )
      .IsEqualTo(0);
    await Assert
      .That(
        download
          .Schedules.Single(s => s.Comment == "ordinary-Insight")
          .Processes[0]
          .ProcessMaterialQuantity
      )
      .IsEqualTo(10);
    var data = Data(local) with
    {
      Schedules = [Data(local).Schedules.Single() with { PlanQuantity = 0 }],
    };
    // Generic reconciliation would withdraw raw supply against this plan. The custom backend
    // owns the offer, even when capacity is temporarily exhausted or the write is delayed.
    await Assert
      .That(MazakQueues.CalculateScheduleChanges(repository, data, Configuration))
      .IsNull();
    await Assert
      .That(repository.GetJobProductionSummary(local.UniqueStr).AutomationEntryMaterialIds.Count)
      .IsEqualTo(0);
  }

  [Test]
  public async Task GenericDecrementRefusesMixedOwnershipBeforeAnyWrite()
  {
    using var repository = _repositories.OpenConnection();
    var local = Job("local");
    var ordinary = Job("ordinary", backendOwned: false);
    repository.AddJobs(
      new NewJobs { ScheduleId = "schedule", Jobs = [local, ordinary] },
      null,
      true
    );
    var database = Substitute.For<IMazakDB>();
    using var sync = new MazakSync(database, new FMSSettings(), Configuration);
    var state = new MazakState
    {
      AllData = new MazakAllDataAndLogs { Schedules = Data(local, ordinary).Schedules },
      CurrentStatus = Status(Data(local, ordinary)),
      StateUpdated = false,
      TimeUntilNextRefresh = TimeSpan.Zero,
      StoppedBecauseRecentLogEvent = false,
    };
    await Assert.That(() => sync.DecrementJobs(repository, state)).Throws<BadRequestException>();
    database.DidNotReceive().Save(Arg.Any<MazakWriteData>());
    await Assert.That(repository.LoadDecrementsForJob(local.UniqueStr).Count()).IsEqualTo(0);
    await Assert.That(repository.LoadDecrementsForJob(ordinary.UniqueStr).Count()).IsEqualTo(0);
  }

  [Test]
  public async Task CustomRetirementCanKeepCompletedRoutesAndDeleteIncompleteOnes()
  {
    var data = Data(Job("completed"), Job("canceled"), Job("retained"));
    data = data with
    {
      Schedules = data
        .Schedules.Select(s => s.Id == 1 ? s with { CompleteQuantity = 10 } : s)
        .ToImmutableList(),
    };
    var (write, savedParts) = BuildMazakSchedules.RemoveSchedules(data, s => s.Id == 2);
    await Assert.That(write.Schedules.Select(s => s.Id)).IsEquivalentTo([2]);
    await Assert.That(write.Schedules.Single().Command).IsEqualTo(MazakWriteCommand.Delete);
    await Assert.That(savedParts.Order()).IsEquivalentTo(["completed:1:1", "retained:1:1"]);
  }
}
