#nullable enable
using System;
using System.Collections.Generic;
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

public sealed class MazakScheduleProvenanceSpec
{
  private const string OriginalJob = "original-job";
  private const string PeerJob = "peer-job";
  private const string OriginalPart = "part:7:1:2";
  private const string PeerPart = "peer:8:1";
  private const string Transfer = "transfer";
  private const string Prepared = "prepared";
  private const string Quarantine = "quarantine";
  private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

  [Test]
  public async Task DeletedSplitRouteRetainsExactHistoryAcrossDiskRestartAndScheduleIdReuse()
  {
    await WithDisk(
      async (filename, device) =>
      {
        var ids = await Initialize(filename, device, constructProvenance: true);
        device.DeleteOriginalDefinitionsAndReuseScheduleId();
        var originalEnd = Event(
          "LG20261105-115901-001.csv",
          LogCode.MachineCycleEnd,
          Now.AddMinutes(-1),
          original: true,
          originalPart: device.OriginalPartName
        );
        var peerEnd = Event(
          "LG20261105-115902-002.csv",
          LogCode.MachineCycleEnd,
          Now.AddMinutes(-1).AddSeconds(1),
          original: false
        );
        ImmutableList<BlackMaple.MachineFramework.LogEntry> completed;
        string watermark;
        // All configuration and connection handles from initialization have closed before reopen.
        using (
          var repositories = RepositoryConfig.InitializeEventDatabase(
            null,
            filename,
            pooling: false
          )
        )
        using (var repository = repositories.OpenConnection())
        using (var sync = device.Synchronizer())
        {
          await Assert
            .That(repository.LoadMazakRouteProvenance(device.OriginalPartName, 1)!.JobProcess)
            .IsEqualTo(2);
          device.Logs = [originalEnd, peerEnd];
          var state = sync.CalculateCellState(repository);
          await Assert.That(device.Logs).IsEmpty();
          await Assert.That(sync.CaptureLogWatermark(repository)).IsEqualTo(peerEnd.ForeignID);
          await Assert
            .That(
              state
                .CurrentStatus.Material.Where(m => m.MaterialID == ids.Original)
                .Select(m => (m.JobUnique, m.Process))
            )
            .IsEquivalentTo([(OriginalJob, 2)]);
          await AssertEvent(repository, originalEnd, ids.Original, 2, "1002");
          await AssertEvent(repository, peerEnd, ids.Peer, 1, "1001");
          await Assert
            .That(
              repository.GetJobProductionSummary(OriginalJob).Completed.GetValueOrDefault((2, 1))
            )
            .IsEqualTo(0);

          var originalUnload = Event(
            "LG20261105-115910-003.csv",
            LogCode.UnloadEnd,
            Now.AddSeconds(-50),
            original: true,
            originalPart: device.OriginalPartName
          );
          var peerUnload = Event(
            "LG20261105-115911-004.csv",
            LogCode.UnloadEnd,
            Now.AddSeconds(-49),
            original: false
          );
          var separator = Separator();
          device.ClearAssignments();
          device.Logs = [originalUnload, peerUnload, separator];
          sync.CalculateCellState(repository);
          await Assert.That(device.Logs).IsEmpty();
          await AssertEvent(repository, originalUnload, ids.Original, 2, "UNLOAD");
          await AssertEvent(repository, peerUnload, ids.Peer, 1, "UNLOAD");
          var summary = repository.GetJobProductionSummary(OriginalJob);
          await Assert.That(summary.AutomationEntryMaterialIds).IsEquivalentTo([ids.Original]);
          await Assert
            .That(summary.Completed)
            .IsEquivalentTo(
              ImmutableDictionary<(int Process, int Path), int>.Empty.Add((1, 1), 1).Add((2, 1), 1)
            );
          await Assert
            .That(repository.GetJobProductionSummary(PeerJob).Completed[(1, 1)])
            .IsEqualTo(1);
          completed = repository.GetRecentLog(0).ToImmutableList();
          watermark = sync.CaptureLogWatermark(repository);
          await Assert.That(watermark).IsEqualTo(separator.ForeignID);
        }
        using (
          var repositories = RepositoryConfig.InitializeEventDatabase(
            null,
            filename,
            pooling: false
          )
        )
        using (var repository = repositories.OpenConnection())
        using (var sync = device.Synchronizer())
        {
          sync.CalculateCellState(repository);
          sync.CalculateCellState(repository);
          await Assert.That(repository.GetRecentLog(0)).IsEquivalentTo(completed);
          await Assert.That(sync.CaptureLogWatermark(repository)).IsEqualTo(watermark);
          await Assert.That(repository.GetMaterialDetails(ids.Peer + 1)).IsNull();
        }
      }
    );
  }

  [Test]
  public async Task MissingHistoryRecordsKnownDiscrepancyAndOrdinaryCorrectionPreservesPeerProgress()
  {
    await WithDisk(
      async (filename, device) =>
      {
        var ids = await Initialize(filename, device, constructProvenance: false);
        device.DeleteOriginalDefinitionsAndReuseScheduleId();
        var missing = Event(
          "LG20261105-115901-001.csv",
          LogCode.MachineCycleEnd,
          Now.AddMinutes(-1),
          original: true,
          originalPart: device.OriginalPartName
        );
        var healthy = Event(
          "LG20261105-115902-002.csv",
          LogCode.MachineCycleEnd,
          Now.AddMinutes(-1).AddSeconds(1),
          original: false
        );
        using var repositories = RepositoryConfig.InitializeEventDatabase(
          null,
          filename,
          pooling: false
        );
        using var repository = repositories.OpenConnection();
        using var sync = device.Synchronizer();
        await Assert.That(repository.LoadMazakRouteProvenance(device.OriginalPartName, 1)).IsNull();
        device.Logs = [missing, healthy];
        sync.CalculateCellState(repository);
        var diagnostic = repository.MostRecentLogEntryForForeignID(missing.ForeignID);
        await Assert.That(diagnostic.LogType).IsEqualTo(LogType.GeneralMessage);
        await Assert.That(diagnostic.Program).IsEqualTo("MazakReconciliation");
        await Assert
          .That(diagnostic.Material.Select(m => m.MaterialID))
          .IsEquivalentTo([ids.Original]);
        await Assert
          .That(repository.OriginalMessageByForeignID(missing.ForeignID))
          .Contains(OriginalPart);
        await Assert.That(repository.IsMaterialInQueue(ids.Original)).IsFalse();
        await Assert
          .That(repository.GetJobProductionSummary(OriginalJob).Completed.GetValueOrDefault((2, 1)))
          .IsEqualTo(0);
        await AssertEvent(repository, healthy, ids.Peer, 1, "1001");
        await Assert.That(device.Logs).IsEmpty();
        var beforeCorrection = repository.GetLogForJobUnique(OriginalJob).ToImmutableList();
        sync.CalculateCellState(repository);
        await Assert
          .That(repository.GetLogForJobUnique(OriginalJob))
          .IsEquivalentTo(beforeCorrection);

        // Confirmed physical removal uses the existing placement marker, so removing the
        // quarantine queue entry later cannot resurrect the previous pallet assignment.
        repository
          .RecordAddMaterialToQueue(
            ids.Original,
            2,
            Quarantine,
            -1,
            "operator",
            MazakMaterialHistory.MissingMaterialReason,
            Now
          )
          .ToImmutableList();
        await Assert
          .That(repository.GetMaterialInQueueByUnique(Quarantine, OriginalJob).Single().MaterialID)
          .IsEqualTo(ids.Original);
        device.ClearAssignments();
        var peerUnload = Event(
          "LG20261105-115911-004.csv",
          LogCode.UnloadEnd,
          Now.AddSeconds(-49),
          original: false
        );
        device.Logs = [peerUnload, Separator()];
        sync.CalculateCellState(repository);
        await AssertEvent(repository, peerUnload, ids.Peer, 1, "UNLOAD");
        await Assert.That(device.Logs).IsEmpty();
        await Assert
          .That(repository.GetJobProductionSummary(PeerJob).Completed[(1, 1)])
          .IsEqualTo(1);
        var original = repository.GetJobProductionSummary(OriginalJob);
        await Assert.That(original.AutomationEntryMaterialIds).IsEquivalentTo([ids.Original]);
        await Assert.That(original.Completed[(1, 1)]).IsEqualTo(1);
        await Assert.That(original.Completed.GetValueOrDefault((2, 1))).IsEqualTo(0);
        repository
          .RecordRemoveMaterialFromAllQueues(ids.Original, 2, "operator", Now.AddMinutes(1))
          .ToImmutableList();
        await Assert.That(repository.IsMaterialInQueue(ids.Original)).IsFalse();
        await Assert
          .That(MazakMaterialHistory.MaterialOnPallet(repository, repository.CurrentPalletLog(1)))
          .IsEmpty();
        sync.CalculateCellState(repository);
        await Assert
          .That(repository.GetJobProductionSummary(OriginalJob).Completed.GetValueOrDefault((2, 1)))
          .IsEqualTo(0);
        await Assert.That(repository.GetMaterialDetails(ids.Peer + 1)).IsNull();
      }
    );
  }

  private static async Task<(long Original, long Peer)> Initialize(
    string filename,
    Device device,
    bool constructProvenance
  )
  {
    using var repositories = RepositoryConfig.InitializeEventDatabase(
      null,
      filename,
      pooling: false
    );
    using var repository = repositories.OpenConnection();
    repository.AddJobs(
      new NewJobs
      {
        ScheduleId = "schedule",
        Jobs =
        [
          Job(
            OriginalJob,
            "part",
            [
              new ProcessInfo
              {
                Paths =
                [
                  constructProvenance
                    ? DownloadPath("1001", 3, null, Transfer)
                    : JobLogTest.EmptyPath with
                    {
                      OutputQueue = Transfer,
                    },
                ],
              },
              new ProcessInfo
              {
                Paths =
                [
                  constructProvenance
                    ? DownloadPath("1002", 1, Prepared, null)
                    : JobLogTest.EmptyPath with
                    {
                      InputQueue = Transfer,
                    },
                ],
              },
            ]
          ),
          Job(PeerJob, "peer", [new ProcessInfo { Paths = [JobLogTest.EmptyPath] }]),
        ],
      },
      null,
      !constructProvenance
    );
    if (constructProvenance)
    {
      repository.MarkJobCopiedToSystem(PeerJob);
      await Assert.That(device.ConstructNewJob(repository)).IsTrue();
      await Assert.That(repository.LoadMazakRouteProvenanceForJob(OriginalJob).Count).IsEqualTo(2);
    }
    var original = repository.AllocateMaterialID(OriginalJob, "part", 2);
    var peer = repository.AllocateMaterialID(PeerJob, "peer", 1);
    Load(repository, original, 1, 3, Now.AddMinutes(-10));
    var p1 = new EventLogMaterial
    {
      MaterialID = original,
      Process = 1,
      Face = 1,
    };
    repository.RecordMachineStart([p1], 3, "MC", 1, "1001", Now.AddMinutes(-9));
    repository.RecordMachineEnd(
      [p1],
      3,
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
              original,
              new UnloadDestination { Queue = Transfer }
            ),
            Process = 1,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.FromSeconds(10),
          },
        ],
        previouslyUnloaded: null,
        lulNum: 1,
        pallet: 3,
        totalElapsed: TimeSpan.FromSeconds(10),
        timeUTC: Now.AddMinutes(-7),
        externalQueues: null
      )
      .ToImmutableList();
    if (constructProvenance)
      repository
        .RecordAddMaterialToQueue(
          original,
          1,
          Prepared,
          -1,
          "operator",
          "Prepared for second process",
          Now.AddMinutes(-6)
        )
        .ToImmutableList();
    Load(repository, original, 2, 1, Now.AddMinutes(-5));
    Load(repository, peer, 1, 2, Now.AddMinutes(-5));
    device.InstallLoadedAssignments();
    using var sync = device.Synchronizer();
    var originalStart = Event(
      "LG20261105-115600-001.csv",
      LogCode.MachineCycleStart,
      Now.AddMinutes(-4),
      original: true,
      originalPart: device.OriginalPartName
    );
    var peerStart = Event(
      "LG20261105-115601-002.csv",
      LogCode.MachineCycleStart,
      Now.AddMinutes(-4).AddSeconds(1),
      original: false
    );
    if (!constructProvenance)
      // Unsupported historical facts exercise correction without granting legacy adoption.
      repository.RecordMachineStart(
        [
          new EventLogMaterial
          {
            MaterialID = original,
            Process = 2,
            Face = 1,
          },
        ],
        1,
        "MC",
        1,
        "1002",
        originalStart.TimeUTC,
        foreignId: originalStart.ForeignID
      );
    device.Logs = constructProvenance ? [originalStart, peerStart] : [peerStart];
    sync.CalculateCellState(repository);
    await AssertEvent(repository, originalStart, original, 2, "1002");
    await AssertEvent(repository, peerStart, peer, 1, "1001");
    await Assert.That(device.Logs).IsEmpty();
    if (constructProvenance)
    {
      var created = repository.LoadMazakRouteProvenance(device.OriginalPartName, 1)!;
      await Assert.That(created.JobUnique).IsEqualTo(OriginalJob);
      await Assert.That(created.JobProcess).IsEqualTo(2);
      await Assert.That(created.NativeProgram).IsEqualTo("1002");
    }
    else
      await Assert.That(repository.LoadMazakRouteProvenance(device.OriginalPartName, 1)).IsNull();
    return (original, peer);
  }

  private static ProcPathInfo DownloadPath(
    string program,
    int pallet,
    string? input,
    string? output
  ) =>
    JobLogTest.EmptyPath with
    {
      InputQueue = input,
      OutputQueue = output,
      PalletNums = [pallet],
      Load = [1],
      Unload = [1],
      Fixture = "fixture-" + pallet,
      Face = 1,
      Stops =
      [
        new MachiningStop
        {
          StationGroup = "MC",
          Stations = [1],
          Program = program,
          ExpectedCycleTime = TimeSpan.FromMinutes(1),
        },
      ],
    };

  private static Job Job(string unique, string part, ImmutableList<ProcessInfo> processes) =>
    new()
    {
      UniqueStr = unique,
      PartName = part,
      Cycles = 1,
      Archived = false,
      RouteStartUTC = Now.AddHours(-1),
      RouteEndUTC = Now.AddHours(1),
      Processes = processes,
    };

  private static void Load(
    IRepository repository,
    long id,
    int process,
    int pallet,
    DateTime time
  ) =>
    repository
      .RecordLoadUnloadComplete(
        toLoad:
        [
          new MaterialToLoadOntoFace
          {
            MaterialIDs = [id],
            Process = process,
            Path = 1,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.FromSeconds(10),
          },
        ],
        previouslyLoaded: null,
        toUnload: null,
        previouslyUnloaded: null,
        lulNum: 1,
        pallet: pallet,
        totalElapsed: TimeSpan.FromSeconds(10),
        timeUTC: time,
        externalQueues: null
      )
      .ToImmutableList();

  private static async Task AssertEvent(
    IRepository repository,
    NativeEvent source,
    long id,
    int process,
    string program
  )
  {
    var entry = repository.MostRecentLogEntryForForeignID(source.ForeignID);
    await Assert.That(entry.Program).IsEqualTo(program);
    await Assert
      .That(entry.Material.Select(m => (m.MaterialID, m.Process)))
      .IsEquivalentTo([(id, process)]);
    await Assert
      .That(repository.GetRecentLog(0).Count(e => e.ForeignID == source.ForeignID))
      .IsEqualTo(1);
  }

  private static NativeEvent Event(
    string id,
    LogCode code,
    DateTime time,
    bool original,
    string originalPart = OriginalPart
  ) =>
    new()
    {
      ForeignID = id,
      Code = code,
      TimeUTC = time,
      Pallet = original ? 1 : 2,
      StationNumber = 1,
      FullPartName = original ? originalPart : PeerPart,
      JobPartName = original ? "part" : "peer",
      Process = 1,
      FixedQuantity = 1,
      Program = original ? "1002" : "1001",
    };

  private static NativeEvent Separator() =>
    Event(
      "LG20261105-115912-005.csv",
      LogCode.PalletMoveComplete,
      Now.AddSeconds(-48),
      original: false
    ) with
    {
      Pallet = 3,
      TargetPosition = "S3",
    };

  private static async Task WithDisk(Func<string, Device, Task> scenario)
  {
    var directory = Directory.CreateTempSubdirectory("mazak-provenance-");
    try
    {
      await scenario(Path.Combine(directory.FullName, "events.db"), new Device());
    }
    finally
    {
      directory.Delete(recursive: true);
    }
  }

  private sealed class Device
  {
    private readonly IMazakDB native = Substitute.For<IMazakDB>();
    private MazakAllDataAndLogs data = Data();
    public ImmutableList<NativeEvent> Logs { get; set; } = [];
    public string OriginalPartName { get; private set; } = OriginalPart;
    private int originalScheduleId = 2;

    public Device()
    {
      native.LoadAllData().Returns(_ => data);
      native
        .LoadAllDataAndLogs(Arg.Any<string>())
        .Returns(call =>
          data with
          {
            Logs = Logs.Where(e =>
                StringComparer.Ordinal.Compare(e.ForeignID, call.Arg<string>()) > 0
              )
              .ToImmutableList(),
          }
        );
      native.LoadTools().Returns(ImmutableList<ToolPocketRow>.Empty);
      native
        .When(db => db.DeleteLogs(Arg.Any<string>()))
        .Do(call =>
          Logs = Logs.Where(e =>
              StringComparer.Ordinal.Compare(e.ForeignID, call.Arg<string>()) > 0
            )
            .ToImmutableList()
        );
      native
        .When(db => db.Save(Arg.Any<MazakWriteData>()))
        .Do(call =>
        {
          var write = call.Arg<MazakWriteData>();
          if (write.Programs.Count != 0)
            throw new InvalidOperationException("This fixture reuses installed numeric programs.");
          data = data with
          {
            Parts = Apply(data.Parts, write.Parts, row => row.PartName, row => row.Command),
            Schedules = Apply(data.Schedules, write.Schedules, row => row.Id, row => row.Command),
            Fixtures = Apply(
              data.Fixtures,
              write.Fixtures,
              row => row.FixtureName,
              row => row.Command
            ),
            Pallets = Apply(
              data.Pallets,
              write.Pallets,
              row => row.PalletNumber,
              row => row.Command
            ),
          };
        });
    }

    private static FMSSettings Settings()
    {
      var settings = new FMSSettings { QuarantineQueue = Quarantine };
      foreach (var queue in new[] { Transfer, Prepared, Quarantine })
        settings.Queues.Add(queue, new QueueInfo { MaxSizeBeforeStopUnloading = -1 });
      return settings;
    }

    private static MazakConfig Configuration() =>
      new()
      {
        DBType = MazakDbType.MazakSmooth,
        UseInsightJobQuantities = job => job?.UniqueStr == OriginalJob,
      };

    public MazakSync Synchronizer() => new(native, Settings(), Configuration(), new Clock());

    public bool ConstructNewJob(IRepository repository)
    {
      // Commissioning is clear before the new selected job is first installed.
      data = data with
      {
        Parts = data.Parts.Where(part => part.PartName == PeerPart).ToImmutableList(),
        Schedules = data
          .Schedules.Where(schedule => schedule.PartName == PeerPart)
          .ToImmutableList(),
        PalletSubStatuses = [],
        PalletPositions = [],
      };
      var changed = WriteJobs.SyncFromDatabase(
        data,
        repository,
        native,
        Settings(),
        Configuration(),
        Now
      );
      OriginalPartName = data
        .Parts.Single(part =>
          MazakPart.UniqueFromComment(part.Comment) == OriginalJob
          && MazakPart.ProcessFromComment(part.Comment) == 2
        )
        .PartName;
      originalScheduleId = data
        .Schedules.Single(schedule => schedule.PartName == OriginalPartName)
        .Id;
      return changed;
    }

    public void InstallLoadedAssignments() =>
      data = data with
      {
        PalletPositions =
        [
          new MazakPalletPositionRow { PalletNumber = 1, PalletPosition = "M012" },
          new MazakPalletPositionRow { PalletNumber = 2, PalletPosition = "M022" },
        ],
        PalletSubStatuses =
        [
          new MazakPalletSubStatusRow
          {
            PalletNumber = 1,
            ScheduleID = originalScheduleId,
            PartName = OriginalPartName,
            PartProcessNumber = 1,
            FixQuantity = 1,
          },
          new MazakPalletSubStatusRow
          {
            PalletNumber = 2,
            ScheduleID = 8,
            PartName = PeerPart,
            PartProcessNumber = 1,
            FixQuantity = 1,
          },
        ],
      };

    public void DeleteOriginalDefinitionsAndReuseScheduleId() =>
      data = data with
      {
        Parts = data.Parts.Where(part => part.PartName == PeerPart).ToImmutableList(),
        Schedules = data
          .Schedules.Where(schedule => schedule.PartName == PeerPart)
          .Select(schedule => schedule with { Id = originalScheduleId })
          .ToImmutableList(),
        PalletSubStatuses = data
          .PalletSubStatuses.Select(assignment =>
            assignment with
            {
              ScheduleID = originalScheduleId,
            }
          )
          .ToImmutableList(),
      };

    public void ClearAssignments() =>
      data = data with
      {
        PalletSubStatuses = [],
        PalletPositions =
        [
          new MazakPalletPositionRow { PalletNumber = 1, PalletPosition = "S1" },
          new MazakPalletPositionRow { PalletNumber = 2, PalletPosition = "S2" },
        ],
      };

    private static ImmutableList<T> Apply<T, TKey>(
      IEnumerable<T> current,
      IEnumerable<T> changes,
      Func<T, TKey> key,
      Func<T, MazakWriteCommand> command
    )
    {
      var result = current.ToImmutableList();
      foreach (var change in changes)
      {
        result = result.RemoveAll(row =>
          EqualityComparer<TKey>.Default.Equals(key(row), key(change))
        );
        if (command(change) != MazakWriteCommand.Delete)
          result = result.Add(change);
      }
      return result;
    }

    private static MazakAllDataAndLogs Data() =>
      new()
      {
        Logs = [],
        MainPrograms =
        [
          new MazakProgramRow { MainProgram = "1001", Comment = "" },
          new MazakProgramRow { MainProgram = "1002", Comment = "" },
        ],
        Fixtures = [new MazakFixtureRow { FixtureName = "fixture", Comment = "" }],
        Pallets = [],
        PalletStatuses = [],
        LoadActions = [],
        Alarms = [],
        Schedules =
        [
          Schedule(2, OriginalPart, OriginalJob + "-2-1-InsightS"),
          Schedule(8, PeerPart, PeerJob + "-Insight"),
        ],
        Parts =
        [
          Part(OriginalPart, OriginalJob + "-2-1-InsightS", "1002"),
          Part(PeerPart, PeerJob + "-Insight", "1001"),
        ],
        PalletPositions = [],
        PalletSubStatuses = [],
      };

    private static MazakScheduleRow Schedule(int id, string part, string comment) =>
      new()
      {
        Id = id,
        PartName = part,
        Comment = comment,
        PlanQuantity = 1,
        Processes = [new MazakScheduleProcessRow { ProcessNumber = 1, ProcessExecuteQuantity = 1 }],
      };

    private static MazakPartRow Part(string part, string comment, string program) =>
      new()
      {
        PartName = part,
        Comment = comment,
        TotalProcess = 1,
        Processes =
        [
          new MazakPartProcessRow
          {
            PartName = part,
            ProcessNumber = 1,
            FixQuantity = 1,
            Fixture = "fixture",
            FixLDS = "1",
            RemoveLDS = "1",
            CutMc = "1",
            MainProgram = program,
          },
        ],
      };
  }

  private sealed class Clock : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => new(Now);
  }
}
