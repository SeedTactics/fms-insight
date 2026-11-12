using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed class DuplicateDownloadSpec
{
  private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

  private static Job PendingJob(bool basket) =>
    new()
    {
      UniqueStr = "initial-job",
      PartName = "sample-part",
      Cycles = 9,
      Archived = false,
      RouteStartUTC = Now.AddHours(-1),
      RouteEndUTC = Now.AddDays(1),
      Processes = Enumerable
        .Range(1, 2)
        .Select(process => new ProcessInfo
        {
          BasketLoadStations = basket ? [10] : [],
          BasketUnloadStations = basket ? [10] : [],
          Paths =
          [
            JobLogTest.EmptyPath with
            {
              PalletNums = basket ? [process] : [1],
              PartsPerPallet = 3,
              Fixture = "fixture" + process,
              Face = basket ? 1 : null,
              Load = [1],
              Unload = [1],
              Stops =
              [
                new MachiningStop
                {
                  StationGroup = "MC",
                  ExpectedCycleTime = TimeSpan.FromMinutes(1),
                  Stations = [1],
                  Program = "100" + process,
                },
              ],
            },
          ],
        })
        .ToImmutableList(),
    };

  private static bool Sync(DownloadDevice device, IRepository repository) =>
    WriteJobs.SyncFromDatabase(
      device.Native.LoadAllData(),
      repository,
      device.Native,
      new FMSSettings(),
      new MazakConfig
      {
        DBType = MazakDbType.MazakSmooth,
        UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
      },
      Now
    );

  [Test]
  [Arguments("schedules", true)]
  [Arguments("parts-full", true)]
  [Arguments("parts-partial", true)]
  [Arguments("schedules-partial", true)]
  [Arguments("schedules-full", true)]
  [Arguments("schedules", false)]
  [Arguments("parts-full", false)]
  [Arguments("parts-partial", false)]
  [Arguments("schedules-full", false)]
  public async Task RetryAfterRestartPreservesInitialRoutesAndQuantities(
    string failure,
    bool basket
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    var job = PendingJob(basket);
    var device = new DownloadDevice(failure);
    using (var repository = config.OpenConnection())
    {
      repository.AddJobs(
        new NewJobs { ScheduleId = "initial-download", Jobs = [job] },
        null,
        false
      );
      await Assert.That(() => Sync(device, repository)).Throws<IOException>();
      await Assert.That(repository.LoadJob(job.UniqueStr)!.CopiedToSystem).IsFalse();
    }
    var installedParts = device.Data.Parts.ToImmutableList();
    var installedSchedules = device.Data.Schedules.ToImmutableList();
    device.Native.ClearReceivedCalls();
    // A fresh repository connection models restart: no download state survives in the writer.
    using var restarted = config.OpenConnection();
    await Assert.That(Sync(device, restarted)).IsTrue();
    await Assert.That(restarted.LoadJob(job.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(device.Data.Schedules.Count).IsEqualTo(basket ? 2 : 1);
    foreach (var comment in MazakPart.InitialScheduleComments(job))
    {
      var schedule = device.Data.Schedules.Single(schedule => schedule.Comment == comment);
      await Assert.That(schedule.PlanQuantity).IsEqualTo(9);
      await Assert.That(schedule.Processes.Count).IsEqualTo(basket ? 1 : 2);
      await Assert.That(schedule.Processes[0].ProcessMaterialQuantity).IsEqualTo(basket ? 0 : 9);
    }
    // Installed definitions and accepted schedules are reused exactly, including native names.
    foreach (var part in installedParts)
      await Assert.That(device.Data.Parts.Single(p => p.PartName == part.PartName)).IsEqualTo(part);
    foreach (var schedule in installedSchedules)
      await Assert.That(device.Data.Schedules.Single(s => s.Id == schedule.Id)).IsEqualTo(schedule);
    if (failure != "parts-partial" || !basket)
      device.Native.DidNotReceive().Save(Arg.Is<MazakWriteData>(write => write.Parts.Count > 0));
    device.Native.ClearReceivedCalls();
    for (var attempt = 0; attempt < 3; attempt++)
      await Assert.That(Sync(device, restarted)).IsFalse();
    device.Native.DidNotReceive().Save(Arg.Any<MazakWriteData>());
  }

  [Test]
  [Arguments(true, false, true)]
  [Arguments(false, true, true)]
  [Arguments(true, true, true)]
  [Arguments(true, false, false)]
  [Arguments(false, true, false)]
  [Arguments(true, true, false)]
  public async Task NativeReadbackNormalizationDoesNotRebuildValidInstalledDefinitions(
    bool unsetTotalProcess,
    bool blankPhotos,
    bool basket
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = PendingJob(basket);
    repository.AddJobs(new NewJobs { ScheduleId = "initial-download", Jobs = [job] }, null, false);
    var device = new DownloadDevice("schedules");
    await Assert.That(() => Sync(device, repository)).Throws<IOException>();
    device.Observe(
      device.Data with
      {
        Parts = device
          .Data.Parts.Select(part =>
            part with
            {
              TotalProcess = unsetTotalProcess ? 0 : part.TotalProcess,
              Processes = part
                .Processes.Select(process =>
                  process with
                  {
                    FixPhoto = blankPhotos ? " " : process.FixPhoto,
                    RemovePhoto = blankPhotos ? " " : process.RemovePhoto,
                  }
                )
                .ToImmutableList(),
            }
          )
          .ToImmutableList(),
      }
    );
    var installedNames = device.Data.Parts.Select(part => part.PartName).ToImmutableList();
    device.Native.ClearReceivedCalls();

    await Assert.That(Sync(device, repository)).IsTrue();
    device.Native.DidNotReceive().Save(Arg.Is<MazakWriteData>(write => write.Parts.Count > 0));
    await Assert
      .That(device.Data.Parts.Select(part => part.PartName))
      .IsEquivalentTo(installedNames);
    await Assert.That(device.Data.Schedules.Count).IsEqualTo(basket ? 2 : 1);
    await Assert.That(repository.LoadJob(job.UniqueStr)!.CopiedToSystem).IsTrue();
  }

  [Test]
  [Arguments(10, true, false)]
  [Arguments(11, true, false)]
  [Arguments(20, false, false)]
  [Arguments(21, false, false)]
  [Arguments(10, true, true)]
  public async Task RetryAfterNativePartChunkRepairsRequiredPalletMappings(
    int jobCount,
    bool basket,
    bool loseMappingResponse
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var jobs = Enumerable
      .Range(1, jobCount)
      .Select(index => PendingJob(basket) with { UniqueStr = "initial-job-" + index })
      .ToImmutableList();
    repository.AddJobs(new NewJobs { ScheduleId = "initial-download", Jobs = jobs }, null, false);
    var device = new DownloadDevice("parts-before-pallet-chunk");

    await Assert.That(() => Sync(device, repository)).Throws<IOException>();
    // The actual ODK chunker places twenty complete parts before any pallet rows.
    await Assert.That(device.Data.Parts.Count).IsEqualTo(20);
    await Assert.That(device.Data.Pallets).IsEmpty();
    await Assert.That(jobs.All(job => !repository.LoadJob(job.UniqueStr)!.CopiedToSystem)).IsTrue();
    var installedParts = device.Data.Parts.ToImmutableList();
    var requiredMappings = installedParts
      .SelectMany(part =>
        part.Processes.Select(process =>
          (
            Pallet: basket ? MazakPart.ProcessFromComment(part.Comment) : 1,
            Fixture: process.Fixture
          )
        )
      )
      .Distinct()
      .ToImmutableList();
    device.Native.ClearReceivedCalls();

    if (loseMappingResponse)
    {
      var failOnce = true;
      device
        .Native.When(native =>
          native.Save(Arg.Is<MazakWriteData>(write => write.Pallets.Count > 0))
        )
        .Do(_ =>
        {
          if (failOnce)
          {
            failOnce = false;
            throw new IOException("Pallet mappings installed; response lost.");
          }
        });
      await Assert.That(() => Sync(device, repository)).Throws<IOException>();
      await Assert.That(device.Data.Schedules).IsEmpty();
      await Assert
        .That(jobs.All(job => !repository.LoadJob(job.UniqueStr)!.CopiedToSystem))
        .IsTrue();
      await Assert.That(device.Data.Pallets.Count).IsEqualTo(requiredMappings.Count);
      device.Native.ClearReceivedCalls();
    }
    await Assert.That(Sync(device, repository)).IsTrue();
    var expectedPartCount = jobCount * (basket ? 2 : 1);
    await Assert.That(device.Data.Parts.Count).IsEqualTo(expectedPartCount);
    await Assert.That(device.Data.Schedules.Count).IsEqualTo(expectedPartCount);
    await Assert.That(jobs.All(job => repository.LoadJob(job.UniqueStr)!.CopiedToSystem)).IsTrue();
    foreach (var part in installedParts)
      await Assert
        .That(device.Data.Parts.Single(row => row.PartName == part.PartName))
        .IsEqualTo(part);
    if (expectedPartCount == installedParts.Count)
      device.Native.DidNotReceive().Save(Arg.Is<MazakWriteData>(write => write.Parts.Count > 0));
    if (loseMappingResponse)
      device.Native.DidNotReceive().Save(Arg.Is<MazakWriteData>(write => write.Pallets.Count > 0));
    var missingMappings = requiredMappings
      .Where(mapping =>
        !device.Data.Pallets.Any(pallet =>
          pallet.PalletNumber == mapping.Pallet && pallet.Fixture == mapping.Fixture
        )
      )
      .ToImmutableList();
    await Assert.That(missingMappings).IsEmpty();
    // New definitions must not consume the fixture groups still needed by reused definitions.
    foreach (var mapping in device.Data.Pallets)
      await Assert.That(mapping.FixtureGroup).IsEqualTo(int.Parse(mapping.Fixture.Split(':')[2]));
    await Assert
      .That(
        device
          .Data.Pallets.GroupBy(row => row.FixtureGroup)
          .All(group => group.Select(row => row.Fixture).Distinct().Count() == 1)
      )
      .IsTrue();
    device.Native.ClearReceivedCalls();
    await Assert.That(Sync(device, repository)).IsFalse();
    device.Native.DidNotReceive().Save(Arg.Any<MazakWriteData>());
  }

  [Test]
  public async Task TwoProcessOneSchedulesDoNotHideMissingProcessTwo()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = PendingJob(true);
    repository.AddJobs(new NewJobs { ScheduleId = "initial-download", Jobs = [job] }, null, false);
    var device = new DownloadDevice("schedules");
    await Assert.That(() => Sync(device, repository)).Throws<IOException>();
    var p1 = device.Data.Parts.Single(p => p.Comment == "initial-job-1-1-InsightS");
    var existing = new MazakScheduleRow
    {
      Id = 10,
      PartName = p1.PartName,
      Comment = p1.Comment,
      PlanQuantity = 9,
      CompleteQuantity = 3,
      Processes = [new MazakScheduleProcessRow { ProcessNumber = 1, ProcessExecuteQuantity = 3 }],
    };
    device.Observe(device.Data with { Schedules = [existing, existing with { Id = 11 }] });
    await Assert.That(Sync(device, repository)).IsTrue();
    await Assert.That(repository.LoadJob(job.UniqueStr)!.CopiedToSystem).IsTrue();
    await Assert.That(device.Data.Schedules.Count).IsEqualTo(3);
    await Assert
      .That(device.Data.Schedules.Count(s => s.Comment == "initial-job-2-1-InsightS"))
      .IsEqualTo(1);
    await Assert.That(device.Data.Schedules.Single(s => s.Id == 10)).IsEqualTo(existing);
    // Existing excess authorizations are preserved for separate operator recovery.
    await Assert
      .That(device.Data.Schedules.Single(s => s.Id == 11))
      .IsEqualTo(existing with { Id = 11 });
  }

  [Test]
  public async Task MissingRouteDefinitionDoesNotReplaceCompletedRouteOrPeerWork()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = PendingJob(true);
    repository.AddJobs(new NewJobs { ScheduleId = "initial-download", Jobs = [job] }, null, false);
    var device = new DownloadDevice("parts-partial");
    await Assert.That(() => Sync(device, repository)).Throws<IOException>();
    var p1 = device.Data.Parts.Single();
    var completed = new MazakScheduleRow
    {
      Id = 10,
      PartName = p1.PartName,
      Comment = p1.Comment,
      PlanQuantity = 9,
      CompleteQuantity = 9,
    };
    var peerPart = p1 with
    {
      PartName = "peer:50:1",
      Comment = "peer-Insight",
      Processes = p1
        .Processes.Select(process => process with { PartName = "peer:50:1" })
        .ToImmutableList(),
    };
    var peerSchedule = completed with
    {
      Id = 11,
      PartName = peerPart.PartName,
      Comment = peerPart.Comment,
      CompleteQuantity = 3,
    };
    device.Observe(
      device.Data with
      {
        Parts = [p1, peerPart],
        Schedules = [completed, peerSchedule],
      }
    );
    await Assert.That(Sync(device, repository)).IsTrue();
    await Assert.That(device.Data.Schedules.Count).IsEqualTo(3);
    await Assert.That(device.Data.Schedules.Single(s => s.Id == 10)).IsEqualTo(completed);
    await Assert.That(device.Data.Schedules.Single(s => s.Id == 11)).IsEqualTo(peerSchedule);
    await Assert
      .That(device.Data.Parts.Single(p => p.PartName == peerPart.PartName))
      .IsEqualTo(peerPart);
    await Assert.That(device.Data.Parts.Single(p => p.PartName == p1.PartName)).IsEqualTo(p1);
    await Assert.That(repository.LoadJob(job.UniqueStr)!.CopiedToSystem).IsTrue();
  }

  [Test]
  public async Task AlreadyAuthorizedPartWithConflictingRouteCommentIsNotScheduledAgain()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = PendingJob(true);
    repository.AddJobs(new NewJobs { ScheduleId = "initial-download", Jobs = [job] }, null, false);
    var device = new DownloadDevice("schedules");
    await Assert.That(() => Sync(device, repository)).Throws<IOException>();
    var p1 = device.Data.Parts.Single(p => p.Comment == "initial-job-1-1-InsightS");
    var existing = new MazakScheduleRow
    {
      Id = 10,
      PartName = p1.PartName,
      Comment = "initial-job-1-2-InsightS",
      PlanQuantity = 9,
      CompleteQuantity = 3,
    };
    device.Observe(device.Data with { Schedules = [existing] });
    device.Native.ClearReceivedCalls();
    for (var attempt = 0; attempt < 3; attempt++)
    {
      var error = await Assert
        .That(() => Sync(device, repository))
        .Throws<InvalidOperationException>();
      await Assert.That(error!.Message).Contains(job.UniqueStr);
      await Assert.That(error.Message).Contains(p1.PartName);
      await Assert.That(error.Message).Contains(p1.Comment);
      await Assert.That(error.Message).Contains(existing.Comment);
      await Assert.That(error.Message).Contains("schedule 10");
      await Assert.That(device.Data.Schedules).IsEquivalentTo([existing]);
      await Assert.That(repository.LoadJob(job.UniqueStr)!.CopiedToSystem).IsFalse();
    }
    device.Native.DidNotReceive().Save(Arg.Is<MazakWriteData>(write => write.Schedules.Count > 0));
  }

  [Test]
  public async Task ConflictingAuthorizationReleasesGenericSyncLockAndRecoversWaitingRequests()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    var job = PendingJob(true);
    var device = new DownloadDevice("schedules");
    using (var repository = config.OpenConnection())
    {
      repository.AddJobs(
        new NewJobs { ScheduleId = "initial-download", Jobs = [job] },
        null,
        false
      );
      await Assert.That(() => Sync(device, repository)).Throws<IOException>();
    }
    var p1 = device.Data.Parts.Single(part => part.Comment == "initial-job-1-1-InsightS");
    var existing = new MazakScheduleRow
    {
      Id = 10,
      PartName = p1.PartName,
      Comment = "initial-job-1-2-InsightS",
      PlanQuantity = 9,
      CompleteQuantity = 3,
    };
    device.Observe(device.Data with { Schedules = [existing] });
    using var mazak = new MazakSync(
      device.Native,
      new FMSSettings(),
      new MazakConfig
      {
        DBType = MazakDbType.MazakSmooth,
        UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
      }
    );
    // Stub acquisition/status construction, retaining real Mazak actions and the generic caller.
    var synchronization = Substitute.For<ISynchronizeCellState<MazakState>>();
    synchronization
      .CalculateCellState(Arg.Any<IRepository>())
      .Returns(_ => new MazakState
      {
        StateUpdated = false,
        TimeUntilNextRefresh = TimeSpan.FromMinutes(1),
        StoppedBecauseRecentLogEvent = false,
        CurrentStatus = new CurrentStatus
        {
          Revision = 0,
          TimeOfCurrentStatusUTC = Now,
          Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
          Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
          Material = [],
          Queues = ImmutableDictionary<string, QueueInfo>.Empty,
          Alarms = [],
        },
        AllData = new MazakAllDataAndLogs
        {
          Parts = device.Data.Parts,
          Schedules = device.Data.Schedules,
          Fixtures = device.Data.Fixtures,
          Pallets = device.Data.Pallets,
          MainPrograms = device.Data.MainPrograms,
          LoadActions = device.Data.LoadActions,
          PalletStatuses = [],
          PalletSubStatuses = device.Data.PalletSubStatuses,
          PalletPositions = device.Data.PalletPositions,
          Alarms = [],
          Logs = [],
        },
      });
    var actionPasses = 0;
    synchronization
      .ApplyActions(Arg.Any<IRepository>(), Arg.Any<MazakState>())
      .Returns(call =>
      {
        // Bound a regression in the progress loop so a failed test can shut down its host.
        if (Interlocked.Increment(ref actionPasses) > 5)
          throw new InvalidOperationException("Synchronization did not stop after five passes");
        return mazak.ApplyActions(call.Arg<IRepository>(), call.Arg<MazakState>());
      });
    using var host = new JobsAndQueuesFromDb<MazakState>(
      config,
      new ServerSettings(),
      new FMSSettings(),
      synchronization,
      startThread: false
    );
    var failed = new TaskCompletionSource<CurrentStatus>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var retried = new TaskCompletionSource<CurrentStatus>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    host.OnNewCurrentStatus += status =>
    {
      if (!failed.TrySetResult(status))
        retried.TrySetResult(status);
    };
    host.StartThread();
    var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await Assert
      .That(error.Alarms.Single())
      .Contains("requires route comment '" + p1.Comment + "'");
    await Assert.That(Volatile.Read(ref actionPasses)).IsEqualTo(1);
    // This operation needs the same mutation lock; failed synchronization must release it.
    await Assert
      .That(async () =>
        await Task.Run(() => ((IJobAndQueueControl)host).DecrementJobQuantites(0L))
          .WaitAsync(TimeSpan.FromSeconds(5))
      )
      .Throws<ConflictRequestException>();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var waiting = host.RecalculateCellStateAndWaitAsync(timeout.Token);
    var secondError = await retried.Task.WaitAsync(timeout.Token);
    await Assert.That(secondError.Alarms).IsEquivalentTo(error.Alarms);
    await Assert.That(Volatile.Read(ref actionPasses)).IsEqualTo(2);
    await Assert.That(waiting.IsCompleted).IsFalse();
    await Assert.That(device.Data.Schedules).IsEquivalentTo([existing]);
    // After operator correction, the queued synchronization request completes and the missing
    // route is admitted once. The existing schedule's production quantities remain unchanged.
    device.Observe(device.Data with { Schedules = [existing with { Comment = p1.Comment }] });
    host.RecalculateCellState();
    await Assert.That(await waiting).IsTrue();
    await Assert.That(host.GetCurrentStatus().Alarms).IsEmpty();
    await Assert.That(device.Data.Schedules.Count).IsEqualTo(2);
    await Assert
      .That(device.Data.Schedules.Single(schedule => schedule.Id == existing.Id))
      .IsEqualTo(existing with { Comment = p1.Comment });
    using var final = config.OpenConnection();
    await Assert.That(final.LoadJob(job.UniqueStr)!.CopiedToSystem).IsTrue();
  }

  [Test]
  public async Task PartialCombinedScheduleBatchPreservesAcceptedAuthorization()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    var jobs = Enumerable
      .Range(1, 2)
      .Select(index => PendingJob(false) with { UniqueStr = "combined-job-" + index })
      .ToImmutableList();
    var device = new DownloadDevice("schedules-partial");
    using (var repository = config.OpenConnection())
    {
      repository.AddJobs(new NewJobs { ScheduleId = "initial-download", Jobs = jobs }, null, false);
      await Assert.That(() => Sync(device, repository)).Throws<IOException>();
      await Assert
        .That(jobs.All(job => !repository.LoadJob(job.UniqueStr)!.CopiedToSystem))
        .IsTrue();
    }
    var accepted = device.Data.Schedules.Single();
    // Production can advance before recovery; neither its authorization nor its quantities reset.
    accepted = accepted with
    {
      PlanQuantity = 7,
      CompleteQuantity = 2,
      Processes = accepted
        .Processes.Select(process => process with { ProcessMaterialQuantity = 4 })
        .ToImmutableList(),
    };
    device.Observe(device.Data with { Schedules = [accepted] });
    device.Native.ClearReceivedCalls();
    using var restarted = config.OpenConnection();
    await Assert.That(Sync(device, restarted)).IsTrue();
    await Assert.That(device.Data.Schedules.Count).IsEqualTo(2);
    await Assert
      .That(device.Data.Schedules.Single(schedule => schedule.Id == accepted.Id))
      .IsEqualTo(accepted);
    var added = device.Data.Schedules.Single(schedule => schedule.Id != accepted.Id);
    await Assert.That(added.PlanQuantity).IsEqualTo(9);
    await Assert.That(added.Processes.Count).IsEqualTo(2);
    await Assert.That(added.Processes[0].ProcessMaterialQuantity).IsEqualTo(9);
    await Assert.That(jobs.All(job => restarted.LoadJob(job.UniqueStr)!.CopiedToSystem)).IsTrue();
    device.Native.Received(1).Save(Arg.Is<MazakWriteData>(write => write.Schedules.Count == 1));
    device.Native.DidNotReceive().Save(Arg.Is<MazakWriteData>(write => write.Parts.Count > 0));
    device.Native.ClearReceivedCalls();
    for (var attempt = 0; attempt < 3; attempt++)
      await Assert.That(Sync(device, restarted)).IsFalse();
    device.Native.DidNotReceive().Save(Arg.Any<MazakWriteData>());
  }

  private sealed class DownloadDevice
  {
    public IMazakDB Native { get; } = Substitute.For<IMazakDB>();
    public MazakAllData Data { get; private set; } =
      new()
      {
        Schedules = [],
        Parts = [],
        Fixtures = [],
        Pallets = [],
        PalletSubStatuses = [],
        PalletPositions = [],
        LoadActions = [],
        MainPrograms =
        [
          new MazakProgramRow { MainProgram = "1001", Comment = "" },
          new MazakProgramRow { MainProgram = "1002", Comment = "" },
        ],
      };

    public void Observe(MazakAllData observation) => Data = observation;

    private bool _failOnce = true;

    public DownloadDevice(string failure)
    {
      Native.LoadAllData().Returns(_ => Data);
      Native
        .When(native => native.Save(Arg.Any<MazakWriteData>()))
        .Do(call =>
        {
          var write = call.Arg<MazakWriteData>();
          if (_failOnce && failure == "schedules" && write.Prefix == "Add Schedules")
          {
            _failOnce = false;
            throw new IOException("Schedule submission failed before controller admission.");
          }
          if (_failOnce && failure == "parts-before-pallet-chunk" && write.Prefix == "Add Parts")
            write = OpenDatabaseKitDB.SplitWriteData(write).First();
          var schedules =
            _failOnce && failure == "schedules-partial" && write.Prefix == "Add Schedules"
              ? write.Schedules.Take(1)
              : write.Schedules;
          // A partial batch contains a subset of complete parts, never part rows missing processes.
          var parts =
            _failOnce && failure == "parts-partial" && write.Prefix == "Add Parts"
              ? write.Parts.Take(1)
              : write.Parts;
          Data = Data with
          {
            Schedules = Apply(Data.Schedules, schedules, row => row.Id, row => row.Command),
            Parts = Apply(Data.Parts, parts, row => row.PartName, row => row.Command),
            Pallets = Apply(
              Data.Pallets,
              write.Pallets,
              row => (row.PalletNumber, row.Fixture),
              row => row.Command
            ),
            Fixtures = Apply(
              Data.Fixtures,
              write.Fixtures,
              row => row.FixtureName,
              row => row.Command
            ),
          };
          if (
            _failOnce
            && (
              (
                failure.StartsWith("parts-", StringComparison.Ordinal)
                && write.Prefix == "Add Parts"
              )
              || (
                failure.StartsWith("schedules-", StringComparison.Ordinal)
                && write.Prefix == "Add Schedules"
              )
            )
          )
          {
            _failOnce = false;
            throw new IOException("Native writes applied before response failure.");
          }
        });
    }

    private static ImmutableList<T> Apply<T, TKey>(
      IEnumerable<T> current,
      IEnumerable<T> edits,
      Func<T, TKey> key,
      Func<T, MazakWriteCommand> command
    ) =>
      current
        .Where(old =>
          !edits.Any(edit => EqualityComparer<TKey>.Default.Equals(key(edit), key(old)))
        )
        .Concat(edits.Where(edit => command(edit) != MazakWriteCommand.Delete))
        .ToImmutableList();
  }
}
