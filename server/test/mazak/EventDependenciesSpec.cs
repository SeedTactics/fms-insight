using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed class EventDependenciesSpec
{
  private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
  private static readonly MazakConfig Configuration = new()
  {
    DBType = MazakDbType.MazakSmooth,
    StartingPalletNumber = 101,
  };
  private static readonly FMSSettings Settings = new() { QuarantineQueue = "quarantine" };

  [Test]
  public async Task NoPendingDependencyDoesNotBlockUnknownWork()
  {
    var empty = ImmutableHashSet<string>.Empty;
    var unknown = ImmutableHashSet.Create(MazakEventDependencies.Unknown);

    await Assert.That(MazakEventDependencies.Conflicts(empty, unknown)).IsFalse();
    await Assert.That(MazakEventDependencies.Conflicts(unknown, empty)).IsFalse();
    await Assert.That(MazakEventDependencies.Conflicts(unknown, unknown)).IsTrue();
  }

  [Test]
  public async Task IndependentVerifiedJobsCanProgressWithDisjointPalletsAndQueues()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var first = Job("first", input: "raw-a", output: "output-a");
    var second = Job("second", input: "raw-b", output: "output-b");
    AddJobs(repository, first, second);
    var data = Data(first, second);

    var a = Read(repository, data, Event(first, 1));
    var b = Read(repository, data, Event(second, 2));

    await Assert.That(a.Contains(MazakEventDependencies.Unknown)).IsFalse();
    await Assert.That(b.Contains(MazakEventDependencies.Unknown)).IsFalse();
    await Assert.That(a.Contains(MazakEventDependencies.Pallet(1))).IsTrue();
    await Assert.That(a.Contains(MazakEventDependencies.Pallet(101))).IsFalse();
    await Assert.That(a.Contains(MazakEventDependencies.Part("first:1:1"))).IsTrue();
    await Assert.That(a.Contains(MazakEventDependencies.Job(first.UniqueStr))).IsTrue();
    await Assert.That(a.Contains(MazakEventDependencies.Queue("quarantine"))).IsFalse();
    await Assert.That(MazakEventDependencies.Conflicts(a, b)).IsFalse();
  }

  [Test]
  public async Task DifferentJobsCompetingForRawQueueConflict()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var first = Job("first", input: "shared-raw");
    var second = Job("second", input: "shared-raw");
    AddJobs(repository, first, second);
    var data = Data(first, second);

    var a = Read(repository, data, Event(first, 1));
    var b = Read(repository, data, Event(second, 2));

    await Assert.That(a.Contains(MazakEventDependencies.Unknown)).IsFalse();
    await Assert.That(b.Contains(MazakEventDependencies.Unknown)).IsFalse();
    await Assert.That(MazakEventDependencies.Conflicts(a, b)).IsTrue();
  }

  [Test]
  public async Task OutputToAnotherJobsInputQueueConflictsAndRetainsExactMaterial()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var first = Job("first", output: "handoff");
    var second = Job("second", input: "handoff");
    AddJobs(repository, first, second);
    var id = PalletLoad(repository, first, rawPallet: 1);
    var data = Data(first, second);

    var a = Read(repository, data, Event(first, 1, LogCode.UnloadEnd));
    var b = Read(repository, data, Event(second, 2));

    await Assert.That(a.Contains(MazakEventDependencies.Unknown)).IsFalse();
    await Assert.That(a.Contains(MazakEventDependencies.Material(id))).IsTrue();
    await Assert.That(a.Contains(MazakEventDependencies.Queue("handoff"))).IsTrue();
    await Assert.That(MazakEventDependencies.Conflicts(a, b)).IsTrue();
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task UnverifiedMaterialOwnershipConflictsWithEveryScope(bool duplicate)
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = Job("first");
    AddJobs(repository, job);
    var data = Data(job);
    data = data with
    {
      Schedules = duplicate ? data.Schedules.Concat(data.Schedules).ToImmutableList() : [],
    };
    var unresolved = Read(repository, data, Event(job, 1));
    var unrelated = Read(
      repository,
      data,
      new MazakMachineInterface.LogEntry { Code = LogCode.PalletMoving, Pallet = 99 }
    );

    await Assert.That(unresolved.Contains(MazakEventDependencies.Unknown)).IsTrue();
    await Assert.That(MazakEventDependencies.Conflicts(unresolved, unrelated)).IsTrue();
    await Assert.That(MazakEventDependencies.Conflicts(unrelated, unresolved)).IsTrue();
  }

  [Test]
  public async Task MissingPalletTrackingPreservesKnownDependenciesAndGlobalUncertainty()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = Job("first");
    AddJobs(repository, job);

    var scopes = Read(repository, Data(job), Event(job, 1, LogCode.MachineCycleStart));

    await Assert.That(scopes.Contains(MazakEventDependencies.Unknown)).IsTrue();
    await Assert.That(scopes.Contains(MazakEventDependencies.Job(job.UniqueStr))).IsTrue();
    await Assert.That(scopes.Contains(MazakEventDependencies.Pallet(1))).IsTrue();
  }

  [Test]
  public async Task MovesRemainPalletScopedWithoutDefinitionOwnership()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var data = Data();
    var moving = new MazakMachineInterface.LogEntry { Code = LogCode.PalletMoving, Pallet = 1 };
    var a = Read(repository, data, moving);
    var same = Read(repository, data, moving with { Code = LogCode.PalletMoveComplete });
    var other = Read(repository, data, moving with { Pallet = 2 });

    await Assert.That(a).IsEquivalentTo([MazakEventDependencies.Pallet(1)]);
    await Assert.That(MazakEventDependencies.Conflicts(a, same)).IsTrue();
    await Assert.That(MazakEventDependencies.Conflicts(a, other)).IsFalse();
  }

  [Test]
  public async Task RecordedQuarantineDestinationConflictsWithItsQueueConsumer()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var first = Job("first", output: "normal-output");
    var second = Job("second", input: "quality-review");
    AddJobs(repository, first, second);
    var id = PalletLoad(repository, first, rawPallet: 1);
    repository.SignalMaterialForQuarantine(
      new EventLogMaterial
      {
        MaterialID = id,
        Process = 1,
        Face = 1,
      },
      pallet: 101,
      queue: "quality-review",
      operatorName: "operator",
      reason: "review",
      timeUTC: Now
    );
    var data = Data(first, second);

    var a = Read(repository, data, Event(first, 1, LogCode.UnloadEnd));
    var b = Read(repository, data, Event(second, 2));

    await Assert.That(a.Contains(MazakEventDependencies.Queue("quality-review"))).IsTrue();
    await Assert.That(MazakEventDependencies.Conflicts(a, b)).IsTrue();
  }

  [Test]
  public async Task SplitNativeProcessUsesItsLogicalProcessQueues()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = Job("split", input: "raw") with
    {
      Processes =
      [
        Job("split", input: "raw").Processes[0],
        new ProcessInfo { Paths = [JobLogTest.EmptyPath with { InputQueue = "p2-input" }] },
      ],
    };
    AddJobs(repository, job);
    var data = Data(job);
    data = data with
    {
      Schedules = [data.Schedules.Single() with { Comment = "split-2-1-InsightS" }],
      Parts = [data.Parts.Single() with { Comment = "split-2-1-InsightS" }],
    };

    var scopes = Read(repository, data, Event(job, 1));

    await Assert.That(scopes.Contains(MazakEventDependencies.Unknown)).IsFalse();
    await Assert.That(scopes.Contains(MazakEventDependencies.Queue("p2-input"))).IsTrue();
    await Assert.That(scopes.Contains(MazakEventDependencies.Queue("raw"))).IsFalse();
  }

  [Test]
  [Arguments("-InsightS")]
  [Arguments("first-2-x-InsightS")]
  [Arguments("first-0-1-InsightS")]
  public async Task MalformedNativeIdentityIsUncertainWithoutThrowing(string comment)
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = Job("first");
    AddJobs(repository, job);
    var data = Data(job);
    data = data with
    {
      Schedules = [data.Schedules.Single() with { Comment = comment }],
      Parts = [data.Parts.Single() with { Comment = comment }],
    };

    var scopes = Read(repository, data, Event(job, 1));

    await Assert.That(scopes.Contains(MazakEventDependencies.Unknown)).IsTrue();
  }

  [Test]
  [Arguments("schedule")]
  [Arguments("part")]
  [Arguments("duplicate")]
  [Arguments("duplicate-process")]
  [Arguments("comment")]
  public async Task SelectedWorkWaitsForAnExactUnambiguousNativeRoute(string loss)
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = SelectedJob("selected");
    AddJobs(repository, job);
    var data = Data(job);
    data = loss switch
    {
      "schedule" => data with { Schedules = [] },
      "part" => data with { Parts = [] },
      "duplicate" => data with
      {
        Schedules = data.Schedules.Concat(data.Schedules).ToImmutableList(),
      },
      "duplicate-process" => data with
      {
        Parts =
        [
          data.Parts.Single() with
          {
            Processes = data
              .Parts.Single()
              .Processes.Concat(data.Parts.Single().Processes)
              .ToImmutableList(),
          },
        ],
      },
      "comment" => data with { Parts = [data.Parts.Single() with { Comment = "other-Insight" }] },
      _ => throw new InvalidOperationException(),
    };
    var configuration = SelectedConfiguration;
    var events = ImmutableList.Create(Event(job, 1));

    await Assert
      .That(MazakEventDependencies.HasSelectedWork(repository, data, configuration, events))
      .IsTrue();
    await Assert
      .That(MazakEventDependencies.DeferredReason(repository, data, configuration, events))
      .IsNotNull();
  }

  [Test]
  public async Task MissingDefinitionsWithSeveralMatchingSelectedJobsDoNotGuessAnOwner()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var first = SelectedJob("first") with { PartName = "shared-part" };
    var second = SelectedJob("second") with { PartName = "shared-part" };
    AddJobs(repository, first, second);
    var events = ImmutableList.Create(Event(first, 1));

    await Assert
      .That(
        MazakEventDependencies.HasSelectedWork(repository, Data(), SelectedConfiguration, events)
      )
      .IsTrue();
    await Assert
      .That(
        MazakEventDependencies.DeferredReason(repository, Data(), SelectedConfiguration, events)
      )
      .IsNotNull();
  }

  [Test]
  public async Task ArchivedSelectedPalletMaterialStillRequiresItsMissingRoute()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = SelectedJob("selected");
    AddJobs(repository, job);
    PalletLoad(repository, job, rawPallet: 1);
    repository.ArchiveJob(job.UniqueStr);
    var events = ImmutableList.Create(Event(job, 1, LogCode.MachineCycleStart));

    await Assert
      .That(
        MazakEventDependencies.HasSelectedWork(repository, Data(), SelectedConfiguration, events)
      )
      .IsTrue();
    await Assert
      .That(
        MazakEventDependencies.DeferredReason(repository, Data(), SelectedConfiguration, events)
      )
      .IsNotNull();
  }

  [Test]
  public async Task SelectedWorkWithVerifiedRoutePassesCurrentEligibility()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = SelectedJob("selected");
    AddJobs(repository, job);
    var events = ImmutableList.Create(Event(job, 1));
    var data = Data(job);

    await Assert
      .That(MazakEventDependencies.HasSelectedWork(repository, data, SelectedConfiguration, events))
      .IsTrue();
    await Assert
      .That(MazakEventDependencies.DeferredReason(repository, data, SelectedConfiguration, events))
      .IsNull();
  }

  [Test]
  public async Task VerifiedOrdinaryJobDoesNotInheritSelectedPolicyFromItsBasePartName()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var selected = SelectedJob("selected") with { PartName = "shared-part" };
    var ordinary = Job("ordinary") with { PartName = "shared-part" };
    AddJobs(repository, selected, ordinary);
    var data = Data(ordinary);
    var events = ImmutableList.Create(Event(ordinary, 1));

    await Assert
      .That(MazakEventDependencies.HasSelectedWork(repository, data, SelectedConfiguration, events))
      .IsFalse();
    await Assert
      .That(MazakEventDependencies.DeferredReason(repository, data, SelectedConfiguration, events))
      .IsNull();
  }

  [Test]
  public async Task SelectedPalletEvidenceCannotBeOverriddenByAnOrdinaryCurrentRow()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var selected = SelectedJob("selected");
    var ordinary = Job("ordinary");
    AddJobs(repository, selected, ordinary);
    PalletLoad(repository, selected, rawPallet: 1);
    var events = ImmutableList.Create(Event(ordinary, 1, LogCode.MachineCycleStart));
    var data = Data(ordinary);

    await Assert
      .That(MazakEventDependencies.HasSelectedWork(repository, data, SelectedConfiguration, events))
      .IsTrue();
    await Assert
      .That(MazakEventDependencies.DeferredReason(repository, data, SelectedConfiguration, events))
      .IsNotNull();
  }

  [Test]
  public async Task OrdinaryMissingRowsAndDefaultOwnershipKeepExistingTranslation()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var ordinary = Job("ordinary");
    var selected = SelectedJob("selected");
    AddJobs(repository, ordinary, selected);

    await Assert
      .That(
        MazakEventDependencies.DeferredReason(
          repository,
          Data(),
          SelectedConfiguration,
          [Event(ordinary, 1)]
        )
      )
      .IsNull();
    await Assert
      .That(
        MazakEventDependencies.HasSelectedWork(
          repository,
          Data(),
          SelectedConfiguration,
          [Event(ordinary, 1)]
        )
      )
      .IsFalse();
    await Assert
      .That(
        MazakEventDependencies.DeferredReason(
          repository,
          Data(),
          Configuration,
          [Event(selected, 1)]
        )
      )
      .IsNull();
    await Assert
      .That(
        MazakEventDependencies.HasSelectedWork(
          repository,
          Data(),
          Configuration,
          [Event(selected, 1)]
        )
      )
      .IsFalse();
  }

  [Test]
  public async Task MissingSelectedRouteAtReceiptCannotBeEstablishedByANewMatchingRow()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = SelectedJob("selected");
    AddJobs(repository, job);
    var events = ImmutableList.Create(Event(job, 1));
    var missing = Data(job) with { Schedules = [] };
    var received = MazakEventDependencies.CaptureVerifiedRoutes(repository, missing, events);

    await Assert.That(received).IsEmpty();
    await Assert
      .That(
        MazakEventDependencies.DeferredReasonFromReceipt(
          repository,
          Data(job),
          SelectedConfiguration,
          events,
          received,
          selectedWorkObserved: true
        )
      )
      .IsNotNull();
  }

  [Test]
  public async Task MissingMaterialIdentityIsDistinctFromALaterVerifiedEventOnTheSameRoute()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = SelectedJob("selected");
    AddJobs(repository, job);
    var earlier = Event(job, 1, LogCode.UnloadEnd);
    var later = Event(job, 1, LogCode.LoadEnd);
    var missing = Data(job) with { Schedules = [] };

    await Assert
      .That(MazakEventDependencies.HasUnverifiedMaterialIdentity(repository, missing, [earlier]))
      .IsTrue();
    await Assert
      .That(MazakEventDependencies.HasUnverifiedMaterialIdentity(repository, Data(job), [later]))
      .IsFalse();
    await Assert
      .That(
        MazakEventDependencies.HasUnverifiedMaterialIdentity(
          repository,
          missing,
          [earlier with { Code = LogCode.PalletMoving }]
        )
      )
      .IsFalse();
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task ReceivedSelectedIdentityCannotFollowReusedNativeNameToAnotherOwner(
    bool selectedReplacement
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var original = SelectedJob("original") with { PartName = "part" };
    var replacement = (selectedReplacement ? SelectedJob("replacement") : Job("replacement")) with
    {
      PartName = original.PartName,
    };
    AddJobs(repository, original, replacement);
    var events = ImmutableList.Create(Event(original, 1));
    var received = MazakEventDependencies.CaptureVerifiedRoutes(repository, Data(original), events);

    await Assert.That(received.Count).IsEqualTo(1);
    await Assert
      .That(
        MazakEventDependencies.DeferredReasonFromReceipt(
          repository,
          Data(replacement),
          SelectedConfiguration,
          events,
          received,
          selectedWorkObserved: true
        )
      )
      .IsNotNull();
  }

  [Test]
  [Arguments("program")]
  [Arguments("fixture")]
  [Arguments("schedule")]
  public async Task ReceivedSelectedIdentityRejectsChangedNativeDefinitions(string change)
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = SelectedJob("selected");
    AddJobs(repository, job);
    var events = ImmutableList.Create(Event(job, 1));
    var data = Data(job);
    var received = MazakEventDependencies.CaptureVerifiedRoutes(repository, data, events);
    var changed =
      change == "schedule"
        ? data with
        {
          Schedules = [data.Schedules.Single() with { Id = 12 }],
        }
        : data with
        {
          Parts =
          [
            data.Parts.Single() with
            {
              Processes =
              [
                change == "program"
                  ? data.Parts.Single().Processes.Single() with
                  {
                    MainProgram = "changed",
                  }
                  : data.Parts.Single().Processes.Single() with
                  {
                    Fixture = "changed",
                  },
              ],
            },
          ],
        };

    await Assert
      .That(
        MazakEventDependencies.DeferredReasonFromReceipt(
          repository,
          changed,
          SelectedConfiguration,
          events,
          received,
          selectedWorkObserved: true
        )
      )
      .IsNotNull();
  }

  [Test]
  public async Task UnchangedReceivedIdentityIgnoresNativeAttemptAndSupplyCounters()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = SelectedJob("selected");
    AddJobs(repository, job);
    var events = ImmutableList.Create(Event(job, 1), Event(job, 1, LogCode.LoadBegin));
    var data = Data(job);
    var received = MazakEventDependencies.CaptureVerifiedRoutes(repository, data, events);
    var changed = data with
    {
      Schedules =
      [
        data.Schedules.Single() with
        {
          PlanQuantity = 12,
          CompleteQuantity = 5,
          Processes =
          [
            data.Schedules.Single().Processes.Single() with
            {
              ProcessExecuteQuantity = 1,
              ProcessBadQuantity = 2,
              ProcessMaterialQuantity = 3,
            },
          ],
        },
      ],
      Parts = [data.Parts.Single() with { CheckCount = 7, ProductCount = 5 }],
    };

    await Assert.That(received.Count).IsEqualTo(1);
    await Assert
      .That(
        MazakEventDependencies
          .CaptureVerifiedRoutes(repository, changed, events)
          .Zip(received)
          .All(pair => MazakEventDependencies.SameIdentity(pair.First, pair.Second))
      )
      .IsTrue();
    await Assert
      .That(
        MazakEventDependencies.DeferredReasonFromReceipt(
          repository,
          changed,
          SelectedConfiguration,
          events,
          received,
          selectedWorkObserved: true
        )
      )
      .IsNull();
  }

  [Test]
  public async Task ReceivedNativeResourceNamesSurviveSerializationWithSemanticIdentity()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var selected = SelectedJob("selected");
    var job = selected with { Processes = [selected.Processes[0], selected.Processes[0]] };
    AddJobs(repository, job);
    var data = Data(job);
    data = data with
    {
      Parts =
      [
        data.Parts.Single() with
        {
          Processes =
          [
            data.Parts.Single().Processes.Single() with
            {
              MainProgram = "program-b",
              Fixture = "fixture-a",
            },
            data.Parts.Single().Processes.Single() with
            {
              ProcessNumber = 2,
              MainProgram = "program-a",
              Fixture = "fixture-a",
            },
          ],
        },
      ],
    };
    var received = MazakEventDependencies.CaptureVerifiedRoutes(repository, data, [Event(job, 1)]);
    var restored = JsonSerializer.Deserialize<ImmutableList<MazakEventRouteIdentity>>(
      JsonSerializer.Serialize(received)
    );

    await Assert
      .That(received.Single().NativeProgramNames)
      .IsEquivalentTo(["program-a", "program-b"]);
    await Assert.That(received.Single().NativeFixtureNames).IsEquivalentTo(["fixture-a"]);
    await Assert
      .That(MazakEventDependencies.SameIdentity(received.Single(), restored.Single()))
      .IsTrue();
    await Assert
      .That(
        received.Concat(restored).Distinct(MazakEventDependencies.RouteIdentityComparer).Count()
      )
      .IsEqualTo(1);
    await Assert
      .That(
        MazakEventDependencies.SameIdentity(
          received.Single(),
          restored.Single() with
          {
            NativeProgramNames = ["different-program"],
          }
        )
      )
      .IsFalse();
  }

  [Test]
  public async Task OrdinaryReceiptWithoutSelectedEvidenceRetainsExistingEligibility()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = Job("ordinary");
    AddJobs(repository, job);

    await Assert
      .That(
        MazakEventDependencies.DeferredReasonFromReceipt(
          repository,
          Data(),
          SelectedConfiguration,
          [Event(job, 1)],
          [],
          selectedWorkObserved: false
        )
      )
      .IsNull();
  }

  private static MazakConfig SelectedConfiguration =>
    Configuration with
    {
      UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
    };

  private static Job SelectedJob(string unique)
  {
    var job = Job(unique);
    return job with { Processes = [job.Processes[0] with { BasketLoadStations = [3] }] };
  }

  private static ImmutableHashSet<string> Read(
    IRepository repository,
    MazakAllData data,
    MazakMachineInterface.LogEntry entry
  ) => MazakEventDependencies.Read(repository, data, Configuration, Settings, [entry]);

  private static Job Job(string unique, string input = null, string output = null) =>
    new()
    {
      UniqueStr = unique,
      PartName = unique,
      Cycles = 10,
      Archived = false,
      Processes =
      [
        new ProcessInfo
        {
          Paths = [JobLogTest.EmptyPath with { InputQueue = input, OutputQueue = output }],
        },
      ],
      RouteStartUTC = Now,
      RouteEndUTC = Now.AddDays(1),
    };

  private static void AddJobs(IRepository repository, params Job[] jobs) =>
    repository.AddJobs(
      new NewJobs { ScheduleId = "schedule", Jobs = jobs.ToImmutableList() },
      null,
      true
    );

  private static MazakMachineInterface.LogEntry Event(
    Job job,
    int rawPallet,
    LogCode code = LogCode.LoadEnd
  ) =>
    new()
    {
      Code = code,
      Pallet = rawPallet,
      Process = 1,
      FullPartName = job.PartName + ":1:1",
      JobPartName = job.PartName,
      FixedQuantity = 1,
      TimeUTC = Now,
    };

  private static MazakAllData Data(params Job[] jobs) =>
    new()
    {
      Schedules = jobs.Select(
          (job, index) =>
            new MazakScheduleRow
            {
              Id = index + 1,
              PartName = job.PartName + ":1:1",
              Comment = job.UniqueStr + "-Insight",
              Processes = [new MazakScheduleProcessRow { ProcessNumber = 1 }],
            }
        )
        .ToImmutableList(),
      Parts = jobs.Select(job => new MazakPartRow
        {
          PartName = job.PartName + ":1:1",
          Comment = job.UniqueStr + "-Insight",
          Processes = [new MazakPartProcessRow { ProcessNumber = 1 }],
        })
        .ToImmutableList(),
    };

  private static long PalletLoad(IRepository repository, Job job, int rawPallet)
  {
    var id = repository.AllocateMaterialID(job.UniqueStr, job.PartName, job.Processes.Count);
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
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        toUnload: null,
        previouslyLoaded: null,
        previouslyUnloaded: null,
        pallet: Configuration.TranslatePalletNumber(rawPallet),
        lulNum: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: Now,
        externalQueues: null
      )
      .ToImmutableList();
    return id;
  }
}
