using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed class QueueEventAdmissionSpec
{
  private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

  [Test]
  [Arguments("part")]
  [Arguments("pallet")]
  [Arguments("eligible-pallet")]
  [Arguments("job")]
  [Arguments("input")]
  [Arguments("output")]
  [Arguments("later-process")]
  [Arguments("unknown")]
  public async Task PendingEvidencePreventsCastingAssignmentBeforeNativeSupplyCalculation(
    string scope
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var affected = Job("affected");
    var healthy = Job("healthy");
    repository.AddJobs(new NewJobs { ScheduleId = "jobs", Jobs = [affected, healthy] }, null, true);
    var affectedCasting = AddCasting(repository, "affected-input");
    var healthyCasting = AddCasting(repository, "healthy-input");
    var dependency = scope switch
    {
      "job" => MazakEventDependencies.Job(affected.UniqueStr),
      "input" => MazakEventDependencies.Queue("affected-input"),
      "output" => MazakEventDependencies.Queue("affected-output"),
      "later-process" => MazakEventDependencies.Queue("affected-next-input"),
      "unknown" => MazakEventDependencies.Unknown,
      _ => null,
    };
    var evidence = new MazakEventInbox.Evidence
    {
      Events =
      [
        new MazakMachineInterface.LogEntry
        {
          ForeignID = "LG001",
          Code = LogCode.PalletMoveComplete,
          Pallet = scope is "pallet" or "eligible-pallet" ? 1 : 99,
          FullPartName = scope == "part" ? "affected:1:1" : "former:9:1",
          TimeUTC = Now,
        },
      ],
      Dependencies = dependency is null ? [] : [dependency],
      Routes = [],
      SelectedWorkObserved = false,
      UnverifiedMaterialIdentityObserved = false,
    };
    repository.ReceiveControllerEventBatches(
      MazakEventInbox.Stream,
      [
        new ControllerEventBatch
        {
          BatchId = "LG001",
          LastEventId = "LG001",
          State = ControllerEventBatchState.Received,
          Payload = JsonSerializer.Serialize(evidence),
        },
      ]
    );
    var changes = MazakQueues.CalculateScheduleChanges(
      repository,
      new MazakCurrentStatus
      {
        Schedules = [Schedule(affected, 1), Schedule(healthy, 2)],
        LoadActions = [],
        PalletSubStatuses =
          scope == "eligible-pallet"
            ? []
            :
            [
              new MazakPalletSubStatusRow { ScheduleID = 1, PalletNumber = 1 },
              new MazakPalletSubStatusRow { ScheduleID = 2, PalletNumber = 2 },
            ],
      },
      new MazakConfig { DBType = MazakDbType.MazakSmooth }
    );
    await Assert.That(repository.GetMaterialDetails(affectedCasting)!.JobUnique).IsNull();
    await Assert.That(repository.GetMaterialInAllQueues().Count()).IsEqualTo(2);
    if (scope == "unknown")
    {
      await Assert.That(changes).IsNull();
      await Assert.That(repository.GetMaterialDetails(healthyCasting)!.JobUnique).IsNull();
    }
    else
    {
      await Assert.That(changes.Schedules.Select(s => s.Id)).IsEquivalentTo([2]);
      await Assert
        .That(changes.Schedules.Single().Processes[0].ProcessMaterialQuantity)
        .IsEqualTo(1);
      await Assert
        .That(repository.GetMaterialDetails(healthyCasting)!.JobUnique)
        .IsEqualTo(healthy.UniqueStr);
    }
    await Assert
      .That(repository.LoadPendingControllerEventBatches(MazakEventInbox.Stream).Count)
      .IsEqualTo(1);
  }

  private static Job Job(string unique) =>
    new()
    {
      UniqueStr = unique,
      PartName = unique,
      Cycles = 1,
      Archived = false,
      RouteStartUTC = Now,
      RouteEndUTC = Now.AddDays(1),
      Processes =
      [
        new ProcessInfo
        {
          Paths =
          [
            JobLogTest.EmptyPath with
            {
              InputQueue = unique + "-input",
              OutputQueue = unique + "-output",
              Casting = "stock",
              PalletNums = unique == "affected" ? [1] : [2],
            },
          ],
        },
        new ProcessInfo
        {
          Paths =
          [
            JobLogTest.EmptyPath with
            {
              InputQueue = unique + "-next-input",
              PalletNums = unique == "affected" ? [1] : [2],
            },
          ],
        },
      ],
    };

  private static MazakScheduleRow Schedule(Job job, int id) =>
    new()
    {
      Id = id,
      PartName = job.PartName + ":1:1",
      Comment = job.UniqueStr + "-Insight",
      PlanQuantity = 1,
      DueDate = Now.Date,
      Processes =
      [
        new MazakScheduleProcessRow
        {
          MazakScheduleRowId = id,
          ProcessNumber = 1,
          FixQuantity = 1,
        },
        new MazakScheduleProcessRow
        {
          MazakScheduleRowId = id,
          ProcessNumber = 2,
          FixQuantity = 1,
        },
      ],
    };

  private static long AddCasting(IRepository repository, string queue)
  {
    var id = repository.AllocateMaterialIDForCasting("stock");
    repository.RecordAddMaterialToQueue(id, 0, queue, -1, null, "prepared stock", Now);
    return id;
  }
}
