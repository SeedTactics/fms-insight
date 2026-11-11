using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed class HistoricalPalletStatusSpec
{
  [Test]
  public async Task HistoricalMachineEventUsesOnlyTheExactNativePalletFace()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    var job = new Job
    {
      UniqueStr = "job",
      PartName = "part",
      Cycles = 2,
      Archived = false,
      RouteStartUTC = now,
      RouteEndUTC = now.AddDays(1),
      Processes = Enumerable
        .Range(0, 2)
        .Select(_ => new ProcessInfo
        {
          BasketLoadStations = [2],
          BasketUnloadStations = [2],
          Paths = [JobLogTest.EmptyPath],
        })
        .ToImmutableList(),
    };
    repository.AddJobs(new NewJobs { ScheduleId = "accepted", Jobs = [job] }, null, true);
    var ids = Enumerable
      .Range(0, 2)
      .Select(_ => repository.AllocateMaterialID(job.UniqueStr, job.PartName, 2))
      .ToImmutableList();
    repository
      .RecordLoadUnloadComplete(
        toLoad: ids.Select(
            (id, index) =>
              new MaterialToLoadOntoFace
              {
                MaterialIDs = [id],
                Process = 2,
                Path = 1,
                FaceNum = index + 1,
                ActiveOperationTime = TimeSpan.Zero,
              }
          )
          .ToImmutableList(),
        toUnload: null,
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: now,
        externalQueues: null
      )
      .ToImmutableList();
    repository.RecordMazakRouteProvenance([
      new BlackMaple.MachineFramework.MazakRouteProvenance
      {
        FullPartName = "part:7:2:2",
        NativeProcess = 1,
        JobUnique = job.UniqueStr,
        JobPartName = job.PartName,
        JobProcess = 2,
        Path = 1,
        NumProcesses = 2,
        NativeProgram = "1002",
        NativeFixture = "fixture",
        DownloadUid = 7,
      },
    ]);
    LogTranslation.HandleEvents(
      repository,
      new MazakAllDataAndLogs
      {
        Schedules = [],
        Parts = [],
        PalletPositions = [],
        Logs =
        [
          new MazakMachineInterface.LogEntry
          {
            ForeignID = "LG-face-1",
            Code = LogCode.MachineCycleStart,
            FullPartName = "part:7:2:2",
            JobPartName = job.PartName,
            Process = 1,
            Pallet = 1,
            FixedQuantity = 1,
            Program = "1002",
            StationNumber = 1,
            TimeUTC = now.AddMinutes(1),
          },
        ],
      },
      "MC",
      new FMSSettings(),
      _ => { },
      new MazakConfig
      {
        DBType = MazakDbType.MazakSmooth,
        UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
      },
      null,
      now.AddMinutes(2)
    );

    var actual = repository.MostRecentLogEntryForForeignID("LG-face-1");
    await Assert.That(actual.LogType).IsEqualTo(LogType.MachineCycle);
    await Assert.That(actual.Material.Single().MaterialID).IsEqualTo(ids[0]);
    await Assert.That(actual.Material.Single().Process).IsEqualTo(2);
    await Assert.That(actual.Material.Single().Face).IsEqualTo(1);
    await Assert.That(repository.GetJobProductionSummary(job.UniqueStr).Completed).IsEmpty();
  }

  [Test]
  [Arguments(false, false)]
  [Arguments(true, false)]
  [Arguments(true, true)]
  public async Task MissingScheduleKeepsP2MaterialVisibleEvenWhenItsNumericIdIsReused(
    bool reused,
    bool missingPallet
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    var job = new Job
    {
      UniqueStr = "original-job",
      PartName = "part",
      Cycles = 1,
      Archived = false,
      RouteStartUTC = now.AddHours(-1),
      RouteEndUTC = now.AddDays(1),
      Processes = Enumerable
        .Range(0, 2)
        .Select(_ => new ProcessInfo
        {
          BasketLoadStations = [2],
          BasketUnloadStations = [2],
          Paths = [JobLogTest.EmptyPath with { PalletNums = [1], Load = [1], Unload = [1] }],
        })
        .ToImmutableList(),
    };
    repository.AddJobs(new NewJobs { ScheduleId = "accepted", Jobs = [job] }, null, true);
    var id = repository.AllocateMaterialID(job.UniqueStr, job.PartName, 2);
    repository
      .RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace
          {
            MaterialIDs = [id],
            Process = 2,
            Path = 1,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        toUnload: null,
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: 1,
        lulNum: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: now,
        externalQueues: null
      )
      .ToImmutableList();
    repository.RecordMazakRouteProvenance([
      new BlackMaple.MachineFramework.MazakRouteProvenance
      {
        FullPartName = "part:7:2:2",
        NativeProcess = 1,
        JobUnique = job.UniqueStr,
        JobPartName = job.PartName,
        JobProcess = 2,
        Path = 1,
        NumProcesses = 2,
        NativeProgram = "1002",
        NativeFixture = "fixture",
        DownloadUid = 7,
      },
    ]);
    var data = new MazakAllData
    {
      Parts = [],
      Fixtures = [],
      MainPrograms = [],
      LoadActions = [],
      Alarms = [],
      Schedules = reused
        ?
        [
          new MazakScheduleRow
          {
            Id = 8,
            PartName = "unrelated-part",
            Comment = "unrelated",
          },
        ]
        : [],
      Pallets = missingPallet ? [] : [new MazakPalletRow { PalletNumber = 1, Fixture = "fixture" }],
      PalletPositions = [new MazakPalletPositionRow { PalletNumber = 1, PalletPosition = "S1" }],
      PalletSubStatuses =
      [
        new MazakPalletSubStatusRow
        {
          PalletNumber = 1,
          ScheduleID = 8,
          PartName = "part:7:2:2",
          PartProcessNumber = 1,
          FixQuantity = 1,
        },
      ],
    };

    using var restarted = config.OpenConnection();
    var status = BuildCurrentStatus.Build(
      restarted,
      new FMSSettings(),
      new MazakConfig
      {
        DBType = MazakDbType.MazakSmooth,
        UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
      },
      data,
      "MC",
      null,
      now.AddMinutes(1)
    );

    var actual = status.Material.Single(material => material.MaterialID == id);
    await Assert.That(actual.JobUnique).IsEqualTo(job.UniqueStr);
    await Assert.That(actual.Process).IsEqualTo(2);
    await Assert.That(actual.Location.PalletNum).IsEqualTo(1);
    await Assert.That(actual.Location.Face).IsEqualTo(1);
    await Assert.That(status.Jobs[job.UniqueStr].RemainingToStart).IsEqualTo(0);
    await Assert.That(status.Jobs[job.UniqueStr].Completed[1][0]).IsEqualTo(0);
  }
}
