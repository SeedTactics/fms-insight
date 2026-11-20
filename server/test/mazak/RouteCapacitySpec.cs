#nullable enable

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed partial class DuplicateDownloadSpec
{
  private static MazakConfig RouteConfig =>
    new()
    {
      DBType = MazakDbType.MazakSmooth,
      UseDurableRouteIdentity = true,
      UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
    };

  [Test]
  public async Task RoutePreviewPlansTheSameWriteWithoutReservingNativeResources()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var job = PendingJob(true);
    repository.AddJobs(new NewJobs { ScheduleId = "preview", Jobs = [job] }, null, false);
    var device = new DownloadDevice("");
    var settings = new FMSSettings();
    var preview = MazakRouteCapacity.Preview(
      repository,
      device.Data,
      RouteConfig,
      settings,
      job.UniqueStr,
      2,
      1,
      3
    );
    await Assert
      .That(repository.LoadMazakRouteIdentities(RouteConfig.ControllerSource, job.UniqueStr))
      .IsEmpty();
    await Assert
      .That(repository.LoadMazakRoutePreparations(RouteConfig.ControllerSource))
      .IsEmpty();
    var write = MazakRouteCapacity.Calculate(
      repository,
      device.Data,
      RouteConfig,
      settings,
      job.UniqueStr,
      2,
      1,
      3
    );
    await Assert.That(write.Identity).IsEqualTo(preview.Identity);
    await Assert.That(write.NextWrite).IsEquivalentTo(preview.NextWrite);
    await Assert
      .That(repository.LoadMazakRouteIdentities(RouteConfig.ControllerSource, job.UniqueStr))
      .IsEquivalentTo([preview.Identity]);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task PreparedFixtureGroupSurvivesRestartBeforeAnyNativeWrite(bool ordinaryPeer)
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    var first = PendingJob(true);
    var peer = PendingJob(!ordinaryPeer) with { UniqueStr = "peer" };
    if (ordinaryPeer)
      peer = peer with
      {
        Processes = peer
          .Processes.Select(process =>
            process with
            {
              Paths = process
                .Paths.Select(path => path with { PalletNums = [2] })
                .ToImmutableList(),
            }
          )
          .ToImmutableList(),
      };
    var routeConfig = RouteConfig with
    {
      ControllerSource = "prepared-cell",
      BulkJobFilter = ordinaryPeer ? job => job.UniqueStr == peer.UniqueStr : null,
    };
    var settings = new FMSSettings();
    var device = new DownloadDevice("");
    MazakRouteWrite Step(Job job, IRepository repository) =>
      MazakRouteCapacity.Calculate(
        repository,
        device.Native.LoadAllData(),
        routeConfig,
        settings,
        job.UniqueStr,
        process: 2,
        path: 1,
        planQuantity: 3
      );
    MazakRouteIdentity Install(Job job, IRepository repository)
    {
      for (var step = 0; step < 6; step++)
      {
        var next = Step(job, repository);
        if (next.NextWrite == null)
          return next.Identity;
        device.Native.Save(next.NextWrite);
      }
      throw new InvalidOperationException("Selected route failed to converge.");
    }

    MazakRouteIdentity firstIdentity;
    MazakRoutePreparation firstPreparation;
    using (var repository = config.OpenConnection())
    {
      repository.AddJobs(
        new NewJobs { ScheduleId = "prepared-routes", Jobs = [first, peer] },
        null,
        false
      );
      var pending = Step(first, repository);
      firstIdentity = pending.Identity;
      firstPreparation = repository.LoadMazakRoutePreparation(
        firstIdentity.Source,
        firstIdentity.PartName,
        firstIdentity.NativeProcess
      )!;
      await Assert
        .That(pending.NextWrite!.Fixtures.Single().FixtureName)
        .IsEqualTo(firstPreparation.FixtureName);
      await Assert.That(device.Data.Fixtures).IsEmpty();
      await Assert.That(device.Data.Parts).IsEmpty();
      await Assert.That(device.Data.Pallets).IsEmpty();
    }

    // The service stopped after saving the preparation and before sending its first write.
    using var reopened = config.OpenConnection();
    if (ordinaryPeer)
    {
      await Assert
        .That(
          WriteJobs.SyncFromDatabase(
            device.Native.LoadAllData(),
            reopened,
            device.Native,
            settings,
            routeConfig,
            Now
          )
        )
        .IsTrue();
      await Assert.That(reopened.LoadJob(peer.UniqueStr)!.CopiedToSystem).IsTrue();
    }
    else
      Install(peer, reopened);
    await Assert.That(device.Data.Pallets.Count).IsGreaterThan(0);
    await Assert
      .That(device.Data.Pallets.All(p => p.FixtureGroup != firstPreparation.FixtureGroup))
      .IsTrue();

    var resumed = Step(first, reopened);
    await Assert.That(resumed.Identity).IsEqualTo(firstIdentity);
    await Assert
      .That(resumed.NextWrite!.Fixtures.Single().FixtureName)
      .IsEqualTo(firstPreparation.FixtureName);
    device.Native.Save(resumed.NextWrite);
    await Assert.That(Install(first, reopened)).IsEqualTo(firstIdentity);
    await Assert
      .That(device.Data.Pallets.Single(p => p.Fixture == firstPreparation.FixtureName).FixtureGroup)
      .IsEqualTo(firstPreparation.FixtureGroup);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task PreparedFixtureCanResumeOrRetireBeforePartExists(bool cancel)
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null);
    var routeConfig = RouteConfig;
    var settings = new FMSSettings();
    var job = PendingJob(true);
    var device = new DownloadDevice("");
    MazakRouteWrite Calculate(IRepository db) =>
      MazakRouteCapacity.Calculate(
        db,
        device.Native.LoadAllData(),
        routeConfig,
        settings,
        job.UniqueStr,
        2,
        1,
        3
      );

    MazakRouteIdentity identity;
    string fixture;
    int group;
    using (var db = config.OpenConnection())
    {
      db.AddJobs(new NewJobs { ScheduleId = "selected", Jobs = [job] }, null, false);
      var first = Calculate(db);
      identity = first.Identity;
      fixture = first.NextWrite!.Fixtures.Single().FixtureName;
      var saved = db.LoadMazakRoutePreparation(identity.Source, identity.PartName, 1);
      await Assert.That(saved!.FixtureName).IsEqualTo(fixture);
      group = saved.FixtureGroup;
      device.Native.Save(first.NextWrite);
    }

    using var reopened = config.OpenConnection();
    device.Observe(
      device.Data with
      {
        Pallets =
        [
          new()
          {
            PalletNumber = 10,
            Fixture = "peer",
            FixtureGroupV2 = group,
          },
        ],
      }
    );
    if (cancel)
    {
      var candidates = MazakRouteRetirement.Capture(
        reopened,
        routeConfig,
        device.Data,
        System.Collections.Immutable.ImmutableHashSet.Create(identity.PartName)
      );
      var retirement = MazakRouteRetirement
        .Calculate(reopened, routeConfig, device.Data, candidates)
        .NextWrite!;
      await Assert.That(retirement.Fixtures.Single().FixtureName).IsEqualTo(fixture);
      await Assert.That(retirement.Parts).IsEmpty();
      device.Native.Save(retirement);
      await Assert
        .That(
          MazakRouteRetirement.Calculate(reopened, routeConfig, device.Data, candidates).NextWrite
        )
        .IsNull();
      await Assert.That(device.Data.Pallets.Single().Fixture).IsEqualTo("peer");
      await Assert
        .That(reopened.LoadMazakRouteIdentity(identity.Source, identity.PartName, 1))
        .IsEqualTo(identity);
    }
    else
    {
      var safe = device.Data;
      device.Observe(safe with { Pallets = [safe.Pallets.Single() with { PalletNumber = 2 }] });
      await Assert.That(() => Calculate(reopened)).Throws<ConflictRequestException>();
      device.Observe(safe);
      var resumed = Calculate(reopened);
      await Assert.That(resumed.NextWrite!.Fixtures).IsEmpty();
      await Assert
        .That(resumed.NextWrite.Parts.Single().Processes.Single().Fixture)
        .IsEqualTo(fixture);
      device.Native.Save(resumed.NextWrite);
      await Assert
        .That(Calculate(reopened).NextWrite!.Pallets.Single().FixtureGroup)
        .IsEqualTo(group);
    }
  }

  [Test]
  [Arguments("legacy-fixture", false, false)]
  [Arguments("legacy:7:2:1", false, false)]
  [Arguments("F:7:invalid:1", false, false)]
  [Arguments("legacy-fixture", true, false)]
  [Arguments("legacy-fixture", false, true)]
  public async Task LegacyAdoptionRequiresAMappedOrIdentifiableFixtureGroup(
    string fixtureName,
    bool mapped,
    bool forceGroupZero
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, System.Guid.NewGuid());
    using var db = config.OpenConnection();
    var job = PendingJob(true);
    db.AddJobs(new NewJobs { ScheduleId = "legacy", Jobs = [job] }, null, false);
    var device = new DownloadDevice("");
    await Assert.That(Sync(device, db)).IsTrue();
    var part = device.Data.Parts.Single(p => p.Comment == job.UniqueStr + "-2-1-InsightS");
    var fixture = part.Processes.Single().Fixture;
    var mapping = device.Data.Pallets.Single(p => p.Fixture == fixture);
    device.Observe(
      device.Data with
      {
        Parts = device
          .Data.Parts.Select(p =>
            p.PartName == part.PartName
              ? p with
              {
                Processes = [p.Processes.Single() with { Fixture = fixtureName }],
              }
              : p
          )
          .ToImmutableList(),
        Fixtures = device
          .Data.Fixtures.Select(f =>
            f.FixtureName == fixture ? f with { FixtureName = fixtureName } : f
          )
          .ToImmutableList(),
        Pallets = mapped ? [mapping with { Fixture = fixtureName }] : [],
      }
    );

    MazakRouteWrite Calculate() =>
      MazakRouteCapacity.Calculate(
        db,
        device.Data,
        RouteConfig with
        {
          OverrideFixtureGroupToZero = forceGroupZero,
        },
        new FMSSettings(),
        job.UniqueStr,
        process: 2,
        path: 1,
        planQuantity: job.Cycles,
        fullPartName: part.PartName
      );

    if (mapped)
      await Assert.That(Calculate().NextWrite).IsNull();
    else if (forceGroupZero)
      await Assert.That(Calculate().NextWrite!.Pallets.Single().FixtureGroup).IsEqualTo(0);
    else
      await Assert
        .That(() => Calculate())
        .Throws<ConflictRequestException>()
        .WithMessage(
          $"Installed Mazak part '{part.PartName}' has no pallet mapping and fixture '{fixtureName}' does not identify a fixture group."
        );
  }

  [Test]
  [Arguments(true)]
  [Arguments(false)]
  public async Task InstallsOnlySelectedSplitRouteAndResumesFromObservedPartAfterLostReply(
    bool basket
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null);
    var job = PendingJob(basket);
    var routeConfig = RouteConfig with
    {
      UseInsightJobQuantities = j => j.UniqueStr == job.UniqueStr,
    };
    var manual = PendingJob(false) with { UniqueStr = "manual", PartName = "manual-part" };
    var device = new DownloadDevice("parts-full");
    var manualPart = new MazakPartRow
    {
      PartName = manual.PartName + ":0:1",
      Comment = "manual-Insight",
    };
    var manualSchedule = new MazakScheduleRow
    {
      Id = 1,
      PartName = manualPart.PartName,
      Comment = manualPart.Comment,
      PlanQuantity = 9,
    };
    device.Observe(device.Data with { Parts = [manualPart], Schedules = [manualSchedule] });

    MazakRouteWrite Calculate(IRepository db, int planned = 3) =>
      MazakRouteCapacity.Calculate(
        db,
        device.Native.LoadAllData(),
        routeConfig,
        new FMSSettings(),
        job.UniqueStr,
        process: 2,
        path: 1,
        planQuantity: planned
      );

    MazakRouteIdentity identity;
    using (var db = config.OpenConnection())
    {
      db.AddJobs(new NewJobs { ScheduleId = "group", Jobs = [job] }, null, false);
      db.AddJobs(new NewJobs { ScheduleId = "manual", Jobs = [manual] }, null, true);
      await Assert
        .That(
          WriteJobs.SyncFromDatabase(
            device.Data,
            db,
            device.Native,
            new FMSSettings(),
            routeConfig,
            Now
          )
        )
        .IsFalse();
      var fixtures = Calculate(db);
      identity = fixtures.Identity;
      await Assert
        .That(db.LoadMazakRouteIdentity(identity.Source, identity.PartName, 1))
        .IsEqualTo(identity);
      await Assert.That(fixtures.NextWrite!.Fixtures.Count).IsEqualTo(1);
      device.Native.Save(fixtures.NextWrite);
      var part = Calculate(db);
      await Assert.That(part.Identity).IsEqualTo(identity);
      await Assert
        .That(part.NextWrite!.Parts.Single().Comment)
        .IsEqualTo(job.UniqueStr + "-2-1-InsightS");
      await Assert.That(part.NextWrite.Pallets.Count).IsEqualTo(0);
      await Assert.That(() => device.Native.Save(part.NextWrite)).Throws<IOException>();
    }

    using var reopened = config.OpenConnection();
    var mapping = Calculate(reopened);
    await Assert.That(mapping.Identity).IsEqualTo(identity);
    await Assert.That(mapping.NextWrite!.Parts.Count).IsEqualTo(0);
    await Assert.That(mapping.NextWrite.Pallets.Single().PalletNumber).IsEqualTo(basket ? 2 : 1);
    device.Native.Save(mapping.NextWrite);
    var add = Calculate(reopened).NextWrite!.Schedules.Single();
    await Assert.That(add.Id).IsEqualTo(2);
    await Assert.That(add.PlanQuantity).IsEqualTo(3);
    await Assert.That(add.Processes.Single().ProcessMaterialQuantity).IsEqualTo(0);
    device.Native.Save(new MazakWriteData { Schedules = [add] });
    await Assert.That(Calculate(reopened).NextWrite).IsNull();

    var increase = Calculate(reopened, planned: 6).NextWrite!;
    var row = increase.Schedules.Single();
    await Assert.That(row.Command).IsEqualTo(MazakWriteCommand.ScheduleSafeEdit);
    await Assert.That(row.Id).IsEqualTo(add.Id);
    await Assert.That(row.PlanQuantity).IsEqualTo(6);
    await Assert.That(row.DueDate).IsEqualTo(add.DueDate);
    await Assert.That(row.Priority).IsEqualTo(add.Priority);
    await Assert.That(row.HoldMode).IsEqualTo(add.HoldMode);
    await Assert.That(row.Processes).IsEmpty();
    device.Native.Save(increase);
    MazakWriteData? Offer(int count) =>
      MazakRouteCapacity.SetMaterialQuantity(
        device.Data.Schedules.Single(s => s.Id == add.Id),
        count
      );
    var offer = Offer(3)!;
    await Assert
      .That(offer.Schedules.Single().Command)
      .IsEqualTo(MazakWriteCommand.ScheduleMaterialEdit);
    await Assert
      .That(offer.Schedules.Single().Processes.Single().ProcessMaterialQuantity)
      .IsEqualTo(3);
    device.Native.Save(offer);
    await Assert.That(Calculate(reopened, planned: 6).NextWrite).IsNull();
    await Assert.That(Offer(3)).IsNull();

    await Assert.That(device.Data.Parts.Count()).IsEqualTo(2);
    await Assert
      .That(device.Data.Schedules.Single(s => s.Id == manualSchedule.Id))
      .IsEqualTo(manualSchedule);
    await Assert
      .That(device.Data.Parts.Single(p => p.PartName == manualPart.PartName))
      .IsEqualTo(manualPart);
    await Assert.That(reopened.LoadJob(job.UniqueStr)!.CopiedToSystem).IsFalse();

    var installed = device.Data.Parts.Single(p => p.PartName == identity.PartName);
    var (offeredRoute, _) = BuildMazakSchedules.RemoveSchedules(device.Data, s => s.Id == add.Id);
    await Assert.That(() => device.Native.Save(offeredRoute)).Throws<InvalidOperationException>();
    var candidates = MazakRouteRetirement.Capture(
      reopened,
      routeConfig,
      device.Data,
      ImmutableHashSet.Create(identity.PartName)
    );
    MazakWriteData? Retire() =>
      MazakRouteRetirement.Calculate(reopened, routeConfig, device.Data, candidates).NextWrite;
    await Assert.That(Retire).Throws<ConflictRequestException>();
    device.Native.Save(Offer(0)!);
    device.Native.Save(Retire()!);
    var recreated = Calculate(reopened);
    var replacement = recreated.NextWrite!.Schedules.Single();
    await Assert.That(replacement.Command).IsEqualTo(MazakWriteCommand.Add);
    await Assert.That(replacement.PartName).IsEqualTo(identity.PartName);
    await Assert.That(replacement.Processes.Single().ProcessMaterialQuantity).IsEqualTo(0);
    await Assert
      .That(device.Data.Parts.Single(p => p.PartName == identity.PartName))
      .IsEqualTo(installed);
    device.Native.Save(recreated.NextWrite);
    await Assert.That(Calculate(reopened).NextWrite).IsNull();

    device.Observe(
      device.Data with
      {
        Parts =
        [
          manualPart,
          installed with
          {
            Processes = [installed.Processes.Single() with { MainProgram = "1001" }],
          },
        ],
      }
    );
    await Assert.That(() => Calculate(reopened, planned: 6)).Throws<ConflictRequestException>();
  }

  [Test]
  [Arguments(false, false, false)]
  [Arguments(true, false, false)]
  [Arguments(false, true, false)]
  [Arguments(true, true, false)]
  [Arguments(false, false, true)]
  [Arguments(true, false, true)]
  [Arguments(true, true, true)]
  public async Task RecreatedRouteClosesTheNextPlacementAfterAnOperatorCorrection(
    bool exactUnload,
    bool correctionAtLoadStation,
    bool identifiedWithoutQuarantine
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var job = PendingJob(true);
    db.AddJobs(new NewJobs { ScheduleId = "review", Jobs = [job] }, null, true);
    var device = new DownloadDevice("");
    var transferPending = false;
    var routeConfig = RouteConfig with { CanQuarantineMissingMaterial = _ => !transferPending };
    var settings = new FMSSettings
    {
      QuarantineQueue = identifiedWithoutQuarantine ? null : "quarantine",
    };
    MazakRouteIdentity Install()
    {
      for (var i = 0; i < 10; i++)
      {
        var next = MazakRouteCapacity.Calculate(
          db,
          device.Data,
          routeConfig,
          settings,
          job.UniqueStr,
          2,
          1,
          3
        );
        if (next.NextWrite == null)
          return next.Identity;
        device.Native.Save(next.NextWrite);
      }
      throw new InvalidOperationException("Installation failed to settle.");
    }
    var route = Install();
    device.Native.Save(MazakRouteCapacity.SetMaterialQuantity(device.Data.Schedules.Single(), 3)!);
    var time = Now.AddMinutes(-2);
    var load = new MazakMachineInterface.LogEntry
    {
      ForeignID = "LG20261119-090000-001.csv",
      TimeUTC = time,
      Code = LogCode.LoadEnd,
      Pallet = 2,
      StationNumber = 1,
      FullPartName = route.PartName,
      JobPartName = job.PartName,
      Process = 1,
      Program = "1002",
      FixedQuantity = 3,
    };
    var boundary = load with
    {
      ForeignID = "LG20261119-090100-002.csv",
      TimeUTC = time.AddMinutes(1),
      Code = LogCode.LoadBegin,
      FixedQuantity = 0,
    };
    ImmutableList<MazakMachineInterface.LogEntry> logs = [load, boundary];
    device
      .Native.LoadAllDataAndLogs(Arg.Any<string>())
      .Returns(call => new MazakAllDataAndLogs
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
        Logs = logs.Where(e => StringComparer.Ordinal.Compare(e.ForeignID, call.Arg<string>()) > 0)
          .ToImmutableList(),
      });
    device.Observe(
      device.Data with
      {
        Schedules = device
          .Data.Schedules.Select(s =>
            s with
            {
              Processes = s
                .Processes.Select(p =>
                  p with
                  {
                    ProcessMaterialQuantity = 0,
                    ProcessExecuteQuantity = 3,
                  }
                )
                .ToImmutableList(),
            }
          )
          .ToImmutableList(),
        PalletSubStatuses =
        [
          new()
          {
            PalletNumber = 2,
            PartName = route.PartName,
            ScheduleID = device.Data.Schedules.Single().Id,
            PartProcessNumber = 1,
            FixQuantity = 3,
          },
        ],
        PalletPositions = [new() { PalletNumber = 2, PalletPosition = "S002" }],
      }
    );
    var original = identifiedWithoutQuarantine
      ? Enumerable
        .Range(1, 3)
        .Select(_ => db.AllocateMaterialID(job.UniqueStr, job.PartName, 2))
        .ToImmutableList()
      : [];
    var initialConfig = identifiedWithoutQuarantine
      ? routeConfig with
      {
        ResolveLoadUnloadTransaction = (_, ctx) =>
          new MazakLoadUnloadResolution.Resolved
          {
            MaterialForLoads = ctx.Loads.ToImmutableDictionary(
              f => f.ForeignId,
              _ => new MazakResolvedLoad { MaterialIds = original }
            ),
            MaterialForUnloads = ImmutableDictionary<string, MazakResolvedUnload>.Empty,
          },
      }
      : routeConfig;
    using var sync = new MazakSync(device.Native, settings, initialConfig);
    var loaded = sync.CalculateCellState(db).CurrentStatus;
    await Assert.That(loaded.Material.Count).IsEqualTo(3);
    if (identifiedWithoutQuarantine)
      await Assert.That(loaded.Material.Select(m => m.MaterialID)).IsEquivalentTo(original);
    else
    {
      await Assert.That(loaded.Material.All(m => m.MaterialID == -1)).IsTrue();
      await Assert.That(loaded.AlarmDetails!.Single().Message).Contains("3 unidentified piece");
    }

    // The operator physically removes this set and clears its native assignment without UNLOAD.
    // No material remains in Mazak, and every native ready/executing/bad count is zero.
    logs = [];
    device.Observe(
      device.Data with
      {
        PalletSubStatuses = [],
        PalletPositions =
        [
          new() { PalletNumber = 2, PalletPosition = correctionAtLoadStation ? "LS01" : "S002" },
        ],
        Schedules = device
          .Data.Schedules.Select(s =>
            s with
            {
              Processes = s
                .Processes.Select(p => p with { ProcessExecuteQuantity = 0 })
                .ToImmutableList(),
            }
          )
          .ToImmutableList(),
      }
    );
    await Assert.That(device.Data.LoadActions).IsEmpty();
    await Assert
      .That(
        device
          .Data.Schedules.SelectMany(s => s.Processes)
          .All(p =>
            p.ProcessMaterialQuantity == 0
            && p.ProcessExecuteQuantity == 0
            && p.ProcessBadQuantity == 0
          )
      )
      .IsTrue();
    transferPending = true;
    await Assert.That(sync.CalculateCellState(db).CurrentStatus.Material).IsEmpty();
    await Assert
      .That(db.CurrentPalletLog(2).Any(e => e.Program == "MazakPalletObservedEmpty"))
      .IsFalse();
    transferPending = false;
    sync.CalculateCellState(db);
    sync.CalculateCellState(db);
    await Assert
      .That(db.CurrentPalletLog(2).Count(e => e.Program == "MazakPalletObservedEmpty"))
      .IsEqualTo(correctionAtLoadStation ? 0 : 1);

    var retirement = MazakRouteRetirement.Capture(
      db,
      routeConfig,
      device.Data,
      ImmutableHashSet.Create(route.PartName)
    );
    for (var i = 0; i < 10; i++)
    {
      var next = MazakRouteRetirement.Calculate(db, routeConfig, device.Data, retirement).NextWrite;
      if (next == null)
        break;
      device.Native.Save(next);
    }
    await Assert.That(device.Data.Schedules).IsEmpty();
    await Assert.That(device.Data.Parts).IsEmpty();
    transferPending = true;
    sync.CalculateCellState(db);
    await Assert
      .That(db.CurrentPalletLog(2).Count(e => e.Program == "MazakPalletObservedEmpty"))
      .IsEqualTo(correctionAtLoadStation ? 0 : 1);
    transferPending = false;
    await Assert.That(sync.CalculateCellState(db).CurrentStatus.Material).IsEmpty();
    await Assert
      .That(db.CurrentPalletLog(2).Count(e => e.Program == "MazakPalletObservedEmpty"))
      .IsEqualTo(1);
    await Assert.That(db.GetJobProductionSummary(job.UniqueStr).Completed).IsEmpty();

    var installedAgain = Install();
    await Assert.That(installedAgain).IsEqualTo(route);
    await Assert.That(device.Data.PalletSubStatuses).IsEmpty();
    await Assert.That(device.Data.LoadActions).IsEmpty();
    await Assert
      .That(device.Data.Schedules.Single().Processes.Single().ProcessMaterialQuantity)
      .IsEqualTo(0);
    using var afterReprepare = new MazakSync(device.Native, settings, routeConfig);
    var fresh = afterReprepare.CalculateCellState(db).CurrentStatus;
    await Assert.That(fresh.Material).IsEmpty();
    await Assert.That(fresh.AlarmDetails).IsNull();
    var originalLoad = db.MostRecentLogEntryForForeignID(load.ForeignID);
    if (identifiedWithoutQuarantine)
    {
      await Assert.That(originalLoad.Material.Select(m => m.MaterialID)).IsEquivalentTo(original);
      await Assert.That(db.GetMaterialInAllQueues()).IsEmpty();
    }
    else
      await Assert.That(originalLoad.ProgramDetails!["mazak-unidentified-quantity"]).IsEqualTo("3");

    var ids = Enumerable
      .Range(1, 3)
      .Select(i => db.AllocateMaterialID(job.UniqueStr, job.PartName, 2))
      .ToImmutableList();
    var exact = routeConfig with
    {
      ResolveLoadUnloadTransaction = (_, ctx) =>
        !exactUnload && !ctx.Unloads.IsEmpty
          ? new MazakLoadUnloadResolution.NotApplicable()
          : new MazakLoadUnloadResolution.Resolved
          {
            MaterialForLoads = ctx.Loads.ToImmutableDictionary(
              f => f.ForeignId,
              _ => new MazakResolvedLoad { MaterialIds = ids }
            ),
            MaterialForUnloads = ctx.Unloads.ToImmutableDictionary(
              f => f.ForeignId,
              _ => new MazakResolvedUnload
              {
                MaterialIDToDestination = ids.ToImmutableDictionary(
                  id => id,
                  id => (UnloadDestination?)null
                ),
              }
            ),
          },
    };
    using var nextSync = new MazakSync(device.Native, settings, exact);
    device.Native.Save(MazakRouteCapacity.SetMaterialQuantity(device.Data.Schedules.Single(), 3)!);
    var readSchedule = device.Data.Schedules.Single();
    device.Observe(
      device.Data with
      {
        PalletSubStatuses =
        [
          new()
          {
            PalletNumber = 2,
            PartName = route.PartName,
            ScheduleID = readSchedule.Id,
            PartProcessNumber = 1,
            FixQuantity = 3,
          },
        ],
        Schedules =
        [
          readSchedule with
          {
            Processes =
            [
              readSchedule.Processes.Single() with
              {
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 3,
              },
            ],
          },
        ],
      }
    );
    var newLoad = load with
    {
      ForeignID = "LG20261119-090200-003.csv",
      TimeUTC = time.AddMinutes(2),
    };
    logs =
    [
      newLoad,
      boundary with
      {
        ForeignID = "LG20261119-090300-004.csv",
        TimeUTC = time.AddMinutes(3),
      },
    ];
    var withNew = nextSync.CalculateCellState(db).CurrentStatus;
    await Assert.That(withNew.Material.Select(m => m.MaterialID)).IsEquivalentTo(ids);
    await Assert
      .That(db.MostRecentLogEntryForForeignID(newLoad.ForeignID).Material.Select(m => m.MaterialID))
      .IsEquivalentTo(ids);
    logs =
    [
      load with
      {
        Code = LogCode.MachineCycleStart,
        ForeignID = "LG20261119-090400-004a.csv",
        TimeUTC = time.AddMinutes(4),
        StationNumber = 1,
      },
      load with
      {
        Code = LogCode.MachineCycleEnd,
        ForeignID = "LG20261119-090900-004b.csv",
        TimeUTC = time.AddMinutes(9),
        StationNumber = 1,
      },
    ];
    nextSync.CalculateCellState(db);
    await Assert
      .That(db.MostRecentLogEntryForForeignID(logs[1].ForeignID).Material.Select(m => m.MaterialID))
      .IsEquivalentTo(ids);
    var newUnload = load with
    {
      Code = LogCode.UnloadEnd,
      ForeignID = "LG20261119-091000-005.csv",
      TimeUTC = time.AddMinutes(10),
    };
    logs =
    [
      newUnload,
      boundary with
      {
        ForeignID = "LG20261119-091100-006.csv",
        TimeUTC = time.AddMinutes(11),
      },
    ];
    device.Observe(
      device.Data with
      {
        PalletSubStatuses = [],
        Schedules =
        [
          readSchedule with
          {
            CompleteQuantity = 3,
            Processes =
            [
              readSchedule.Processes.Single() with
              {
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = 0,
              },
            ],
          },
        ],
      }
    );
    nextSync.CalculateCellState(db);
    await Assert
      .That(
        db.MostRecentLogEntryForForeignID(newUnload.ForeignID).Material.Select(m => m.MaterialID)
      )
      .IsEquivalentTo(ids);
    await Assert
      .That(
        db.GetRecentLog(0)
          .Count(e =>
            e.LogType == LogType.PalletCycle && !e.StartOfCycle && e.EndTimeUTC >= newUnload.TimeUTC
          )
      )
      .IsEqualTo(1);
    await Assert.That(db.GetJobProductionSummary(job.UniqueStr).Completed[(2, 1)]).IsEqualTo(3);
  }
}
