#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed partial class MazakAcquisitionSpec
{
  private static MazakAllDataAndLogs WithInstalledRoute(
    MazakAllDataAndLogs data,
    MazakRouteIdentity route,
    int scheduleId = 1
  )
  {
    var comment = $"{route.JobUnique}-{route.JobProcess}-{route.Path}-InsightS";
    return data with
    {
      Parts =
      [
        .. data.Parts,
        new MazakPartRow
        {
          PartName = route.PartName,
          Comment = comment,
          Processes =
          [
            new()
            {
              PartName = route.PartName,
              ProcessNumber = 1,
              Fixture = "fixture",
              FixLDS = "1",
              RemoveLDS = "1",
              CutMc = "1",
              MainProgram = "1001",
              FixQuantity = 1,
            },
          ],
        },
      ],
      Schedules =
      [
        .. data.Schedules,
        new MazakScheduleRow
        {
          Id = scheduleId,
          PartName = route.PartName,
          Comment = comment,
          PlanQuantity = 1,
          Processes = [new() { ProcessNumber = 1 }],
        },
      ],
    };
  }

  private static MazakPalletSubStatusRow AssignedFace(
    MazakRouteIdentity route,
    int scheduleId = 1
  ) =>
    new()
    {
      PalletNumber = 1,
      PartName = route.PartName,
      ScheduleID = scheduleId,
      PartProcessNumber = route.NativeProcess,
      FixQuantity = 1,
    };

  [Test]
  public async Task ManagedNextProcessRequestDoesNotSelectThePieceOnThePalletOrTheFirstInQueue()
  {
    using var cell = new Cell();
    var db = cell.Repository;
    var process = new ProcessInfo { Paths = [JobLogTest.EmptyPath] };
    var job = new Job
    {
      UniqueStr = "managed",
      PartName = "part",
      Cycles = 2,
      Archived = false,
      RouteStartUTC = cell.Clock.Now.UtcDateTime,
      RouteEndUTC = cell.Clock.Now.UtcDateTime.AddHours(1),
      Processes =
      [
        process,
        new() { Paths = [JobLogTest.EmptyPath with { InputQueue = "staging" }] },
      ],
    };
    db.AddJobs(new NewJobs { ScheduleId = "batch", Jobs = [job] }, null, true);
    var p1 = new MazakRouteIdentity
    {
      Source = MazakConfig.DefaultControllerSource,
      PartName = "part:1:1:1",
      NativeProcess = 1,
      JobUnique = job.UniqueStr,
      JobProcess = 1,
      Path = 1,
    };
    var p2 = p1 with { PartName = "part:2:1:2", JobProcess = 2 };
    db.ReserveMazakRouteIdentity(p1);
    db.ReserveMazakRouteIdentity(p2);
    var queued = db.AllocateMaterialID(job.UniqueStr, job.PartName, 2);
    db.RecordAddMaterialToQueue(queued, 1, "staging", -1, null, "staging");
    var p2Action = new LoadAction
    {
      LoadStation = 1,
      Part = "part",
      Comment = "managed-2-1-InsightS",
      Process = 1,
      Qty = 1,
    };
    cell.Data = WithInstalledRoute(WithInstalledRoute(cell.Data, p1), p2, 2) with
    {
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "LS01" }],
      PalletSubStatuses = [AssignedFace(p1)],
      LoadActions = [p2Action with { LoadEvent = true }, p2Action with { LoadEvent = false }],
    };
    using var sync = cell.NewSynchronizer(configure: cfg =>
      cfg with
      {
        UseDurableRouteIdentity = true,
        UseInsightJobQuantities = _ => true,
      }
    );
    var status = sync.CalculateCellState(db).CurrentStatus;
    await Assert
      .That(status.Material.Single(m => m.Location.PalletNum == 1).Action.Type)
      .IsEqualTo(InProcessMaterialAction.ActionType.Waiting);
    var load = status.Material.Single(m =>
      m.Action.Type == InProcessMaterialAction.ActionType.Loading
    );
    await Assert.That(load.MaterialID).IsEqualTo(-1L);
    await Assert.That(load.Action.ProcessAfterLoad).IsEqualTo(2);
    await Assert.That(load.Action.LoadOntoFace).IsEqualTo(1);
    await Assert
      .That(status.Material.Single(m => m.MaterialID == queued).Location.CurrentQueue)
      .IsEqualTo("staging");
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task ExactPartialUnloadUsesTheLoadedDefinition(bool unloadForRetiredDefinition)
  {
    using var cell = new Cell();
    var db = cell.Repository;
    var now = cell.Clock.Now.UtcDateTime;
    var job = new Job
    {
      UniqueStr = "managed",
      PartName = "part",
      Cycles = 2,
      Archived = false,
      RouteStartUTC = now.AddHours(-1),
      RouteEndUTC = now.AddDays(1),
      Processes = [new() { Paths = [JobLogTest.EmptyPath] }],
    };
    db.AddJobs(new NewJobs { ScheduleId = "batch", Jobs = [job] }, null, true);
    var route = new MazakRouteIdentity
    {
      Source = MazakConfig.DefaultControllerSource,
      PartName = "part:2:1:1",
      NativeProcess = 1,
      JobUnique = job.UniqueStr,
      JobProcess = 1,
      Path = 1,
    };
    var retired = route with { PartName = "part:1:1:1" };
    db.ReserveMazakRouteIdentity(route);
    db.ReserveMazakRouteIdentity(retired);
    var first = db.AllocateMaterialID(job.UniqueStr, job.PartName, 1);
    var second = db.AllocateMaterialID(job.UniqueStr, job.PartName, 1);
    db.RecordLoadUnloadComplete(
      [
        new()
        {
          MaterialIDs = [first, second],
          Process = 1,
          FaceNum = 1,
          Path = 1,
          ActiveOperationTime = TimeSpan.Zero,
          AdditionalData = ImmutableDictionary<string, string>.Empty.Add(
            "mazak-native-part-name",
            route.PartName
          ),
        },
      ],
      null,
      [],
      null,
      1,
      1,
      TimeSpan.Zero,
      now.AddMinutes(-5),
      null
    );
    var eventRoute = unloadForRetiredDefinition ? retired : route;
    var unload = cell.Start with
    {
      Code = LogCode.UnloadEnd,
      FullPartName = eventRoute.PartName,
      FixedQuantity = 1,
    };
    cell.Data = WithInstalledRoute(cell.Data, route) with
    {
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "LS01" }],
      PalletSubStatuses =
      [
        AssignedFace(route) with
        {
          FixQuantity = unloadForRetiredDefinition ? 2 : 1,
        },
      ],
      Logs = [unload, cell.End with { Code = LogCode.LoadBegin }],
    };
    MazakRouteIdentity? resolvedRoute = null;
    using var sync = cell.NewSynchronizer(configure: cfg =>
      cfg with
      {
        UseDurableRouteIdentity = true,
        UseInsightJobQuantities = _ => true,
        ResolveLoadUnloadTransaction = (_, ctx) =>
        {
          var face = ctx.Unloads.Single();
          resolvedRoute = face.RouteIdentity;
          return new MazakLoadUnloadResolution.Resolved
          {
            MaterialForLoads = ImmutableDictionary<string, MazakResolvedLoad>.Empty,
            MaterialForUnloads = ImmutableDictionary<string, MazakResolvedUnload>.Empty.Add(
              face.ForeignId,
              new MazakResolvedUnload
              {
                MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination?>.Empty.Add(
                  first,
                  null
                ),
              }
            ),
          };
        },
      }
    );
    var status = sync.CalculateCellState(db).CurrentStatus;
    await Assert.That(resolvedRoute).IsEqualTo(eventRoute);
    await Assert
      .That(db.MostRecentLogEntryForForeignID(unload.ForeignID).Material.Select(m => m.MaterialID))
      .IsEquivalentTo(unloadForRetiredDefinition ? [] : new[] { first });
    await Assert
      .That(status.Material.Select(m => m.MaterialID))
      .IsEquivalentTo(unloadForRetiredDefinition ? [first, second] : new[] { second });
    await Assert
      .That(status.Jobs[job.UniqueStr].Completed![0][0])
      .IsEqualTo(unloadForRetiredDefinition ? 0 : 1);
  }

  [Test]
  public async Task ReplacementScheduleRunsAnInvalidatedPieceFromAQueue()
  {
    using var cell = new Cell();
    var db = cell.Repository;
    var now = cell.Clock.Now.UtcDateTime;
    var path = JobLogTest.EmptyPath with
    {
      Load = [1],
      Unload = [1],
      PalletNums = [1],
      PartsPerPallet = 1,
      Stops =
      [
        new()
        {
          StationGroup = "MC",
          Stations = [1],
          Program = "1001",
          ExpectedCycleTime = TimeSpan.FromMinutes(1),
        },
      ],
    };
    var job = new Job
    {
      UniqueStr = "rework",
      PartName = "part",
      Cycles = 1,
      Archived = false,
      RouteStartUTC = now.AddHours(-1),
      RouteEndUTC = now.AddDays(1),
      Processes = [new() { Paths = [path] }],
    };
    db.AddJobs(new NewJobs { ScheduleId = "batch", Jobs = [job] }, null, true);
    var id = db.AllocateMaterialID(job.UniqueStr, job.PartName, 1);
    var route = new MazakRouteIdentity
    {
      Source = MazakConfig.DefaultControllerSource,
      PartName = "part:7:1:1",
      NativeProcess = 1,
      JobUnique = job.UniqueStr,
      JobProcess = 1,
      Path = 1,
    };
    db.ReserveMazakRouteIdentity(route);
    cell.Data = WithInstalledRoute(cell.Data, route) with
    {
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "LS01" }],
      Pallets =
      [
        new()
        {
          PalletNumber = 1,
          Fixture = "fixture",
          FixtureGroupV2 = 1,
        },
      ],
      Fixtures = [new() { FixtureName = "fixture" }],
      MainPrograms = [new() { MainProgram = "1001" }],
    };
    var config = new MazakConfig
    {
      DBType = MazakDbType.MazakSmooth,
      UseDurableRouteIdentity = true,
      UseInsightJobQuantities = _ => true,
      ResolveLoadUnloadTransaction = (_, ctx) =>
        new MazakLoadUnloadResolution.Resolved
        {
          MaterialForLoads = ctx.Loads.ToImmutableDictionary(
            f => f.ForeignId,
            _ => new MazakResolvedLoad { MaterialIds = [id] }
          ),
          MaterialForUnloads = ctx.Unloads.ToImmutableDictionary(
            f => f.ForeignId,
            _ => new MazakResolvedUnload
            {
              MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination?>.Empty.Add(
                id,
                null
              ),
            }
          ),
        },
    };
    var settings = new FMSSettings();
    using var sync = new MazakSync(cell.Native, settings, config, cell.Clock);
    CurrentStatus Transfer(LogCode code, int minute)
    {
      var loaded = code == LogCode.LoadEnd;
      var observed = cell.Data.Schedules.Single();
      var transfer = cell.Start with
      {
        Code = code,
        FullPartName = route.PartName,
        FixedQuantity = 1,
        TimeUTC = now.AddMinutes(minute),
        ForeignID = $"LG-transfer-{minute}-001.csv",
      };
      cell.Data = cell.Data with
      {
        Schedules =
        [
          observed with
          {
            CompleteQuantity = loaded ? 0 : 1,
            Processes =
            [
              observed.Processes.Single() with
              {
                ProcessMaterialQuantity = 0,
                ProcessExecuteQuantity = loaded ? 1 : 0,
              },
            ],
          },
        ],
        PalletSubStatuses = loaded ? [AssignedFace(route, observed.Id)] : [],
        Logs =
        [
          transfer,
          transfer with
          {
            Code = LogCode.LoadBegin,
            ForeignID = $"LG-transfer-{minute}-002.csv",
          },
        ],
      };
      return sync.CalculateCellState(db).CurrentStatus;
    }
    Transfer(LogCode.LoadEnd, 1);
    var completed = Transfer(LogCode.UnloadEnd, 2);
    await Assert.That(completed.Jobs[job.UniqueStr].Completed![0][0]).IsEqualTo(1);

    db.InvalidatePalletCycle(id, 1, "operator", now.AddMinutes(3));
    db.RecordAddMaterialToQueue(id, 0, "rework", -1, "operator", "rework", now.AddMinutes(3));
    var invalidated = sync.CalculateCellState(db).CurrentStatus.Jobs[job.UniqueStr];
    await Assert.That(invalidated.Completed![0][0]).IsEqualTo(0);
    await Assert.That(invalidated.RemainingToStart).IsEqualTo(0L);
    var beforeReplacement = db.MaxLogCounter();
    await Assert
      .That(MazakRouteCapacity.RecordedLoads(db, config, route, 0).PhysicalQuantity)
      .IsEqualTo(1L);
    await Assert
      .That(MazakRouteCapacity.RecordedLoads(db, config, route, beforeReplacement).PhysicalQuantity)
      .IsEqualTo(0L);

    var retire = MazakRouteRetirement.RemoveSchedule(db, config, cell.Data, route)!;
    await Assert.That(retire.Schedules.Single().Command).IsEqualTo(MazakWriteCommand.Delete);
    await Assert.That(retire.Parts).IsEmpty();
    cell.Data = cell.Data with { Schedules = [] };
    var repeat = MazakRouteCapacity.Calculate(
      db,
      cell.Data,
      config,
      settings,
      job.UniqueStr,
      1,
      1,
      1
    );
    var replacement = repeat.NextWrite!.Schedules.Single();
    await Assert.That(replacement.PartName).IsEqualTo(route.PartName);
    await Assert.That(replacement.Processes.Single().ProcessMaterialQuantity).IsEqualTo(0);
    cell.Data = cell.Data with { Schedules = [replacement] };
    var offer = MazakRouteCapacity.SetMaterialQuantity(db, config, cell.Data, route, 1)!;
    cell.Data = cell.Data with { Schedules = [offer.Schedules.Single()] };

    var reloaded = Transfer(LogCode.LoadEnd, 4);
    await Assert.That(reloaded.Material.Single().MaterialID).IsEqualTo(id);
    await Assert.That(db.IsMaterialInQueue(id)).IsFalse();
    var reworked = Transfer(LogCode.UnloadEnd, 5).Jobs[job.UniqueStr];
    await Assert.That(reworked.Completed![0][0]).IsEqualTo(1);
    await Assert.That(reworked.RemainingToStart).IsEqualTo(0L);
    await Assert
      .That(MazakRouteCapacity.RecordedLoads(db, config, route, 0).PhysicalQuantity)
      .IsEqualTo(2L);
    await Assert
      .That(MazakRouteCapacity.RecordedLoads(db, config, route, beforeReplacement).PhysicalQuantity)
      .IsEqualTo(1L);
    await Assert
      .That(db.GetJobProductionSummary(job.UniqueStr).AutomationEntryMaterialIds)
      .IsEquivalentTo([id]);
    await Assert
      .That(
        db.GetLogForMaterial(id, includeInvalidatedCycles: true)
          .Count(e => e.Result == "UNLOAD" && !e.StartOfCycle)
      )
      .IsEqualTo(2);
  }

  [Test]
  [Arguments(false, false)]
  [Arguments(true, false)]
  [Arguments(true, true)]
  [Arguments(false, true)]
  public async Task DelayedMachineEndUsesTheOriginalRunAfterAnEmptyObservation(
    bool replacementLoadRecorded,
    bool endBelongsToReplacementWithMissingStart
  )
  {
    using var cell = new Cell();
    var db = cell.Repository;
    var now = cell.Clock.Now.UtcDateTime;
    var process = new ProcessInfo { Paths = [JobLogTest.EmptyPath with { PalletNums = [1] }] };
    var job = new Job
    {
      UniqueStr = "delayed-machine-end",
      PartName = "part",
      Cycles = 3,
      Archived = false,
      RouteStartUTC = now.AddHours(-1),
      RouteEndUTC = now.AddHours(1),
      Processes = [process],
    };
    db.AddJobs(new NewJobs { ScheduleId = "delayed-machine-end", Jobs = [job] }, null, true);
    var route = new MazakRouteIdentity
    {
      Source = MazakConfig.DefaultControllerSource,
      PartName = "part:7:1:1",
      NativeProcess = 1,
      JobUnique = job.UniqueStr,
      JobProcess = 1,
      Path = 1,
    };
    db.ReserveMazakRouteIdentity(route);
    var original = db.AllocateMaterialID(job.UniqueStr, job.PartName, 1);
    var load = new MaterialToLoadOntoFace
    {
      MaterialIDs = [original],
      Process = 1,
      Path = 1,
      FaceNum = 1,
      ActiveOperationTime = TimeSpan.Zero,
      AdditionalData = ImmutableDictionary<string, string>.Empty.Add(
        "mazak-native-part-name",
        route.PartName
      ),
    };
    db.RecordPartialLoadUnload(
      [load with { AdditionalData = load.AdditionalData.Add("mazak-unidentified-quantity", "1") }],
      [],
      1,
      1,
      TimeSpan.Zero,
      now.AddMinutes(-14),
      null
    );
    var start = cell.Start with
    {
      ForeignID = "LG20261105-114700-001.csv",
      TimeUTC = now.AddMinutes(-13),
      FullPartName = route.PartName,
      FixedQuantity = 2,
    };
    cell.Data = WithInstalledRoute(cell.Data, route) with
    {
      Logs = [start],
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "S001" }],
      PalletSubStatuses = [AssignedFace(route) with { FixQuantity = 2 }],
    };
    MazakConfig Configure(MazakConfig cfg) =>
      cfg with
      {
        UseDurableRouteIdentity = true,
        UseInsightJobQuantities = j => j.UniqueStr == job.UniqueStr,
        CanQuarantineMissingMaterial = _ => true,
      };
    using var sync = cell.NewSynchronizer(configure: Configure);
    cell.Tools = [cell.Tools.Single() with { LifeUsed = 100 }];
    cell.Clock.Now = new DateTimeOffset(start.TimeUTC);
    sync.CalculateCellState(db);
    var recordedStart = db.MostRecentLogEntryForForeignID(start.ForeignID);
    await Assert.That(recordedStart.Material.Single().MaterialID).IsEqualTo(original);
    await Assert
      .That(db.ToolPocketSnapshotForCycle(recordedStart.Counter).Single().CurrentUse)
      .IsEqualTo(TimeSpan.FromSeconds(100));

    // The run finishes, then the operator clears the pallet before the end CSV is available.
    cell.Clock.Now = new DateTimeOffset(now);
    cell.Data = cell.Data with { Logs = [], PalletSubStatuses = [] };
    await Assert.That(sync.CalculateCellState(db).CurrentStatus.Material).IsEmpty();
    await Assert
      .That(db.CurrentPalletLog(1).Count(e => e.Program == "MazakPalletObservedEmpty"))
      .IsEqualTo(1);

    var replacement = db.AllocateMaterialID(job.UniqueStr, job.PartName, 1);
    if (replacementLoadRecorded)
    {
      db.RecordLoadUnloadComplete(
        [load with { MaterialIDs = [replacement] }],
        [],
        [],
        [],
        1,
        1,
        TimeSpan.Zero,
        now.AddSeconds(10),
        null
      );
      if (!endBelongsToReplacementWithMissingStart)
        db.RecordMachineStart(
          [
            new EventLogMaterial
            {
              MaterialID = replacement,
              Face = 1,
              Process = 1,
            },
          ],
          1,
          recordedStart.LocationName,
          1,
          recordedStart.Program,
          now.AddSeconds(20)
        );
    }
    if (replacementLoadRecorded || endBelongsToReplacementWithMissingStart)
      cell.Data = cell.Data with { PalletSubStatuses = [AssignedFace(route)] };

    cell.Tools = [cell.Tools.Single() with { LifeUsed = replacementLoadRecorded ? 145 : 110 }];
    cell.Clock.Now = cell.Clock.Now.AddMinutes(1);
    var end = start with
    {
      ForeignID = endBelongsToReplacementWithMissingStart
        ? "LG20261105-120040-002.csv"
        : "LG20261105-115700-002.csv",
      TimeUTC = endBelongsToReplacementWithMissingStart ? now.AddSeconds(40) : now.AddMinutes(-3),
      Code = LogCode.MachineCycleEnd,
    };
    cell.Data = cell.Data with { Logs = [end] };
    using var reopened = cell.Repositories.OpenConnection();
    using var restarted = cell.NewSynchronizer(configure: Configure);
    var status = restarted.CalculateCellState(reopened).CurrentStatus;
    var recordedEnd = reopened.MostRecentLogEntryForForeignID(end.ForeignID);
    if (endBelongsToReplacementWithMissingStart)
    {
      // The observed empty pallet separates the runs even when the new LOAD CSV was lost.
      await Assert.That(recordedEnd).IsNull();
      await Assert
        .That(status.Material.Single().MaterialID)
        .IsEqualTo(replacementLoadRecorded ? replacement : -1);
      return;
    }
    await Assert.That(recordedEnd).IsNotNull();
    await Assert.That(recordedEnd.ElapsedTime).IsEqualTo(TimeSpan.FromMinutes(10));
    await Assert.That(recordedEnd.Program).IsEqualTo(recordedStart.Program);
    await Assert.That(recordedEnd.Material.Single().MaterialID).IsEqualTo(original);
    await Assert.That(recordedEnd.Tools).IsNull();
    await Assert.That(reopened.ToolPocketSnapshotForCycle(recordedEnd.Counter)).IsEmpty();
    await Assert
      .That(recordedEnd.ProgramDetails!["MazakToolSnapshotUnavailable"])
      .IsEqualTo("true");
    await Assert
      .That(reopened.GetLogForMaterial(original).Any(e => e.Counter == recordedEnd.Counter))
      .IsTrue();
    await Assert
      .That(
        reopened
          .CurrentPalletLog(1, includeLastPalletCycleEvt: true)
          .Any(e => e.Counter == recordedEnd.Counter)
      )
      .IsFalse();
    if (replacementLoadRecorded)
    {
      var active = status.Material.Single();
      await Assert.That(active.MaterialID).IsEqualTo(replacement);
      await Assert.That(active.Action.Type).IsEqualTo(InProcessMaterialAction.ActionType.Machining);
    }
    else
    {
      await Assert.That(status.Material).IsEmpty();
      await Assert
        .That(reopened.CurrentPalletLog(1).Count(e => e.Program == "MazakPalletObservedEmpty"))
        .IsEqualTo(1);
    }
  }

  [Test]
  public async Task ScheduleCanDisappearWithAllMaterialStillInBaskets()
  {
    using var cell = new Cell();
    var db = cell.Repository;
    var now = cell.Clock.Now.UtcDateTime;
    var process = new ProcessInfo
    {
      BasketLoadStations = [1],
      BasketUnloadStations = [1],
      Paths = [JobLogTest.EmptyPath with { PalletNums = [1] }],
    };
    var job = new Job
    {
      UniqueStr = "basket-job",
      PartName = "part",
      Cycles = 2,
      Archived = false,
      RouteStartUTC = now.AddHours(-1),
      RouteEndUTC = now.AddHours(1),
      Processes = [process, process],
    };
    db.AddJobs(new NewJobs { ScheduleId = "basket-only", Jobs = [job] }, null, true);
    var route = new MazakRouteIdentity
    {
      Source = MazakConfig.DefaultControllerSource,
      PartName = "part:7:1:1",
      NativeProcess = 1,
      JobUnique = job.UniqueStr,
      JobProcess = 1,
      Path = 1,
    };
    db.ReserveMazakRouteIdentity(route);
    var material = db.AllocateMaterialID(job.UniqueStr, job.PartName, 2);
    var contents = new BasketContents
    {
      BasketId = 7,
      Slots = ImmutableSortedDictionary<int, BasketSlotContents>.Empty.Add(
        1,
        new BasketSlotContents { Material = [new() { MaterialID = material, Process = 1 }] }
      ),
    };
    db.RecordBasketStationOperation(
      new BasketStationOperation
      {
        Transfers =
        [
          new BasketStationTransfer.LoadOntoBasket
          {
            BasketId = 7,
            Material =
            [
              new()
              {
                MaterialID = material,
                Process = 1,
                Face = 1,
              },
            ],
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        CycleBoundaries = [],
        ContentsChanges =
        [
          new()
          {
            BasketId = 7,
            Expected = null,
            Result = contents,
          },
        ],
      },
      lulNum: 1,
      totalElapsed: TimeSpan.Zero,
      timeUTC: now,
      externalQueues: null,
      idempotencyKey: "basket-only-load"
    );
    cell.Data = WithInstalledRoute(cell.Data, route);
    static MazakConfig Configure(MazakConfig config) =>
      config with
      {
        UseDurableRouteIdentity = true,
        UseInsightJobQuantities = JobHelpers.EntersThroughBasket,
      };
    using var sync = cell.NewSynchronizer(configure: Configure);
    var before = sync.CalculateCellState(db).CurrentStatus;
    await Assert.That(before.Jobs[job.UniqueStr].RemainingToStart).IsEqualTo(1L);

    // The basket remains outside Mazak. Its contents never populated a native pallet assignment.
    cell.Data = cell.Data with { Schedules = [], Parts = [] };
    using var reopened = cell.Repositories.OpenConnection();
    using var restarted = cell.NewSynchronizer(configure: Configure);
    var after = restarted.CalculateCellState(reopened).CurrentStatus;
    await Assert.That(after.Jobs[job.UniqueStr].RemainingToStart).IsEqualTo(1L);
    await Assert.That(after.Jobs[job.UniqueStr].Completed![0][0]).IsEqualTo(0);
    await Assert
      .That(reopened.GetBasketContents(7)!.Slots[1].Material.Single().MaterialID)
      .IsEqualTo(material);
    await Assert.That(reopened.GetMaterialInAllQueues()).IsEmpty();
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task OperatorCorrectionQuarantinesRemovedMaterialEvenIfItsScheduleWasRetired(
    bool retireAfterCorrection
  )
  {
    using var cell = new Cell();
    var db = cell.Repository;
    var now = cell.Clock.Now.UtcDateTime;
    var process = new ProcessInfo { Paths = [JobLogTest.EmptyPath with { PalletNums = [1] }] };
    var job = new Job
    {
      UniqueStr = "managed",
      PartName = "part",
      Cycles = 1,
      Archived = false,
      RouteStartUTC = now.AddHours(-1),
      RouteEndUTC = now.AddHours(1),
      Processes = [process, process],
    };
    db.AddJobs(new NewJobs { ScheduleId = "manual-correction", Jobs = [job] }, null, true);
    var route = new MazakRouteIdentity
    {
      Source = MazakConfig.DefaultControllerSource,
      PartName = "part:7:2:2",
      NativeProcess = 1,
      JobUnique = job.UniqueStr,
      JobProcess = 2,
      Path = 1,
    };
    db.ReserveMazakRouteIdentity(route);
    var material = db.AllocateMaterialID(job.UniqueStr, job.PartName, 2);
    db.RecordPartialLoadUnload(
      [
        new MaterialToLoadOntoFace
        {
          MaterialIDs = [material],
          Process = 2,
          Path = 1,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.Zero,
          AdditionalData = ImmutableDictionary<string, string>.Empty.Add(
            "mazak-native-part-name",
            route.PartName
          ),
        },
      ],
      [],
      1,
      1,
      TimeSpan.Zero,
      now.AddMinutes(-2),
      null
    );
    cell.Data = WithInstalledRoute(cell.Data, route) with
    {
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "S001" }],
      PalletSubStatuses = [AssignedFace(route)],
    };
    using var sync = new MazakSync(
      cell.Native,
      new FMSSettings { QuarantineQueue = "quarantine" },
      new MazakConfig
      {
        DBType = MazakDbType.MazakSmooth,
        UseDurableRouteIdentity = true,
        UseInsightJobQuantities = j => j.UniqueStr == job.UniqueStr,
      },
      cell.Clock
    );
    var initial = sync.CalculateCellState(db).CurrentStatus;
    await Assert
      .That(initial.Material.Single(m => m.Location.PalletNum == 1).MaterialID)
      .IsEqualTo(material);
    await Assert.That(db.GetMaterialInAllQueues()).IsEmpty();

    // The operator physically removed the material and then cleared the native assignment.
    // Mazak can now permit retirement. The correction generates no normal UNLOAD.
    cell.Data = cell.Data with { PalletSubStatuses = [] };
    if (retireAfterCorrection)
      cell.Data = cell.Data with { Schedules = [], Parts = [] };
    var corrected = sync.CalculateCellState(db);
    await Assert.That(corrected.StateUpdated).IsTrue();
    await Assert
      .That(corrected.CurrentStatus.Material.Where(m => m.Location.PalletNum == 1))
      .IsEmpty();
    var quarantined = db.GetMaterialInAllQueues().Single();
    await Assert.That(quarantined.MaterialID).IsEqualTo(material);
    await Assert.That(quarantined.Queue).IsEqualTo("quarantine");
    await Assert.That(db.GetJobProductionSummary(job.UniqueStr).Completed).IsEmpty();

    sync.CalculateCellState(db);
    var correction = db.GetLogForMaterial(material)
      .Single(e => e.Program == MazakMaterialHistory.MissingMaterialReason);
    await Assert.That(correction.Material.Single().Process).IsEqualTo(2);
  }

  [Test]
  [Arguments(1)]
  [Arguments(2)]
  public async Task PartialReloadStartsANewMachiningPlacement(int processAfterLoad)
  {
    using var cell = new Cell();
    var db = cell.Repository;
    var now = cell.Clock.Now.UtcDateTime;
    var process = new ProcessInfo { Paths = [JobLogTest.EmptyPath with { PalletNums = [1] }] };
    var job = new Job
    {
      UniqueStr = "placement",
      PartName = "part",
      Cycles = 1,
      Archived = false,
      RouteStartUTC = now.AddHours(-1),
      RouteEndUTC = now.AddHours(1),
      Processes = [process, process],
    };
    db.AddJobs(new NewJobs { ScheduleId = "placement", Jobs = [job] }, null, true);
    var route = new MazakRouteIdentity
    {
      Source = MazakConfig.DefaultControllerSource,
      PartName = "part:7:1:1",
      NativeProcess = 1,
      JobUnique = job.UniqueStr,
      JobProcess = 1,
      Path = 1,
    };
    db.ReserveMazakRouteIdentity(route);
    var laterRoute = route with { PartName = "part:7:2:2", JobProcess = 2 };
    db.ReserveMazakRouteIdentity(laterRoute);
    var material = db.AllocateMaterialID(job.UniqueStr, job.PartName, 2);
    var load = new MaterialToLoadOntoFace
    {
      MaterialIDs = [material],
      Process = 1,
      Path = 1,
      FaceNum = 1,
      ActiveOperationTime = TimeSpan.Zero,
    };
    db.RecordPartialLoadUnload([load], [], 1, 1, TimeSpan.Zero, now.AddMinutes(-2), null);
    var machiningMaterial = new EventLogMaterial
    {
      MaterialID = material,
      Process = 1,
      Face = 1,
    };
    db.RecordMachineStart([machiningMaterial], 1, "MC", 1, "1001", now.AddMinutes(-1));
    cell.Data = WithInstalledRoute(WithInstalledRoute(cell.Data, route), laterRoute, 2) with
    {
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "S001" }],
      PalletSubStatuses = [AssignedFace(route)],
    };
    using var sync = cell.NewSynchronizer(configure: config =>
      config with
      {
        UseDurableRouteIdentity = true,
        UseInsightJobQuantities = accepted => accepted.UniqueStr == job.UniqueStr,
      }
    );
    var retained = sync.CalculateCellState(db).CurrentStatus.Material.Single();
    await Assert.That(retained.Action.Type).IsEqualTo(InProcessMaterialAction.ActionType.Machining);
    await Assert.That(retained.Action.ElapsedMachiningTime).IsEqualTo(TimeSpan.FromMinutes(1));

    db.RecordPartialLoadUnload(
      [load with { Process = processAfterLoad }],
      [
        new()
        {
          MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination?>.Empty.Add(
            material,
            null
          ),
          Process = 1,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      1,
      1,
      TimeSpan.Zero,
      now.AddSeconds(-30),
      null
    );
    cell.Data = cell.Data with
    {
      PalletSubStatuses =
      [
        processAfterLoad == 1 ? AssignedFace(route) : AssignedFace(laterRoute, 2),
      ],
    };
    var reloaded = sync.CalculateCellState(db).CurrentStatus.Material.Single();
    await Assert.That(reloaded.Process).IsEqualTo(processAfterLoad);
    await Assert.That(reloaded.Action.Type).IsEqualTo(InProcessMaterialAction.ActionType.Waiting);
    await Assert.That(reloaded.Action.ElapsedMachiningTime).IsNull();

    db.RecordMachineStart(
      [machiningMaterial with { Process = processAfterLoad }],
      1,
      "MC",
      1,
      "1002",
      now.AddSeconds(-10)
    );
    var started = sync.CalculateCellState(db).CurrentStatus.Material.Single();
    await Assert.That(started.Action.Type).IsEqualTo(InProcessMaterialAction.ActionType.Machining);
    await Assert.That(started.Action.ElapsedMachiningTime).IsEqualTo(TimeSpan.FromSeconds(10));
  }

  [Test]
  public async Task UnidentifiedLoadStaysInHistoryWhileAnEmptyPalletCanHaveAPendingTransfer()
  {
    using var cell = new Cell();
    var db = cell.Repository;
    var route = new MazakRouteIdentity
    {
      Source = MazakConfig.DefaultControllerSource,
      PartName = "part:7:2:2",
      NativeProcess = 1,
      JobUnique = "selected",
      JobProcess = 2,
      Path = 1,
    };
    db.ReserveMazakRouteIdentity(route);

    var process = new ProcessInfo { Paths = [JobLogTest.EmptyPath with { PalletNums = [1] }] };
    db.AddJobs(
      new NewJobs
      {
        ScheduleId = "test",
        Jobs =
        [
          new Job
          {
            UniqueStr = route.JobUnique,
            PartName = "part",
            Cycles = 1,
            Archived = false,
            RouteStartUTC = cell.Clock.Now.UtcDateTime.AddHours(-1),
            RouteEndUTC = cell.Clock.Now.UtcDateTime.AddHours(1),
            Processes = [process, process],
          },
        ],
      },
      null,
      true
    );
    var load = cell.Start with
    {
      Code = LogCode.LoadEnd,
      FullPartName = route.PartName,
      FixedQuantity = 1,
    };
    var next = cell.End with { Code = LogCode.LoadBegin };
    cell.Data = WithInstalledRoute(cell.Data, route) with
    {
      Logs = [load, next],
      PalletSubStatuses = [AssignedFace(route)],
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "S001" }],
    };
    static MazakConfig Configure(MazakConfig config) =>
      config with
      {
        UseDurableRouteIdentity = true,
      };
    using (var sync = cell.NewSynchronizer(configure: Configure))
    {
      var loaded = sync.CalculateCellState(db).CurrentStatus;
      var card = loaded.Material.Single();
      await Assert.That(card.MaterialID).IsEqualTo(-1L);
      await Assert.That(card.Location.PalletNum).IsEqualTo(1);
      await Assert.That(loaded.AlarmDetails!.Single().Message).Contains("1 unidentified piece");
    }

    using var reopened = cell.Repositories.OpenConnection();
    using var restarted = cell.NewSynchronizer(configure: Configure);
    cell.Data = cell.Data with { Logs = [], PalletSubStatuses = [] };
    var empty = restarted.CalculateCellState(reopened).CurrentStatus;
    await Assert.That(cell.Data.Schedules.Single().PartName).IsEqualTo(route.PartName);
    await Assert.That(empty.Material).IsEmpty();
    await Assert.That(empty.AlarmDetails).IsNull();
    await Assert
      .That(
        reopened.MostRecentLogEntryForForeignID(load.ForeignID).ProgramDetails![
          "mazak-unidentified-quantity"
        ]
      )
      .IsEqualTo("1");
    var config = Configure(new MazakConfig { DBType = MazakDbType.MazakSmooth });
    var persistedLoad = reopened.MostRecentLogEntryForForeignID(load.ForeignID);
    var peer = config with { ControllerSource = "peer" };
    reopened.RecordLoadUnloadComplete(
      toLoad:
      [
        new MaterialToLoadOntoFace
        {
          MaterialIDs = [],
          Process = route.JobProcess,
          FaceNum = route.NativeProcess,
          Path = route.Path,
          ForeignID = peer.LogForeignId(load.ForeignID),
          AdditionalData = persistedLoad.ProgramDetails!,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      previouslyLoaded: [],
      toUnload: [],
      previouslyUnloaded: [],
      lulNum: 2,
      pallet: 2,
      totalElapsed: TimeSpan.Zero,
      timeUTC: persistedLoad.EndTimeUTC,
      externalQueues: ImmutableDictionary<string, string>.Empty
    );
    var recorded = MazakRouteCapacity.RecordedLoads(reopened, config, route, 0);
    await Assert.That(recorded.IdentifiedQuantity).IsEqualTo(0L);
    await Assert.That(recorded.UnidentifiedQuantity).IsEqualTo(1L);
    await Assert.That(recorded.PhysicalQuantity).IsEqualTo(1L);
    await Assert
      .That(
        MazakRouteCapacity
          .RecordedLoads(reopened, config, route, persistedLoad.Counter)
          .PhysicalQuantity
      )
      .IsEqualTo(0L);
    await Assert
      .That(
        MazakRouteCapacity
          .RecordedLoads(reopened, peer, route with { Source = "peer" }, persistedLoad.Counter)
          .UnidentifiedQuantity
      )
      .IsEqualTo(1L);

    cell.Data = cell.Data with
    {
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "LS01" }],
      LoadActions =
      [
        new()
        {
          LoadStation = 1,
          Part = "part",
          Comment = "selected-2-1-InsightS",
          Process = 1,
          Qty = 1,
        },
      ],
    };
    var waitingForAssignment = restarted.CalculateCellState(reopened).CurrentStatus;
    await Assert.That(waitingForAssignment.Material).IsEmpty();
    await Assert
      .That(waitingForAssignment.AlarmDetails!.Single().Message)
      .Contains("without a matching pallet assignment");

    var unload = load with { Code = LogCode.UnloadEnd, ForeignID = next.ForeignID + "1" };
    cell.Data = cell.Data with { Logs = [unload], LoadActions = [] };
    var settling = restarted.CalculateCellState(reopened);
    await Assert.That(settling.PalletWithUnprocessedUnloads).IsEqualTo(1);
    await Assert.That(settling.CurrentStatus.Material).IsEmpty();
    await Assert
      .That(settling.CurrentStatus.AlarmDetails!.Single().Message)
      .Contains("still settling");

    cell.Data = cell.Data with { Logs = [unload, next with { ForeignID = next.ForeignID + "2" }] };
    var resolved = restarted.CalculateCellState(reopened).CurrentStatus;
    await Assert.That(resolved.Material).IsEmpty();
    await Assert.That(resolved.AlarmDetails).IsNull();
  }

  [Test]
  [Arguments(false, 1, false, false, false)]
  [Arguments(true, 1, false, false, false)]
  [Arguments(false, 2, false, false, false)]
  [Arguments(false, 1, true, false, false)]
  [Arguments(false, 1, false, true, false)]
  [Arguments(false, 1, false, true, true)]
  public async Task SplitUnloadUsesTheReservedRouteForLiveAndRetiredSchedules(
    bool deletePart,
    int reportedQuantity,
    bool ambiguousIdentities,
    bool conflictingSchedule,
    bool stampNativeName
  )
  {
    using var cell = new Cell(id => "serial-" + id);
    var db = cell.Repository;
    var retireBeforeIngestion =
      !ambiguousIdentities && !conflictingSchedule && reportedQuantity == 1;
    var now = cell.Clock.Now.UtcDateTime;
    var path = JobLogTest.EmptyPath with { PalletNums = [1], Load = [1], Unload = [1] };
    var process = new ProcessInfo { Paths = [path] };
    var job = new Job
    {
      UniqueStr = "historical",
      PartName = "part",
      Cycles = 10,
      Archived = false,
      RouteStartUTC = now.AddHours(-1),
      RouteEndUTC = now.AddHours(1),
      Processes = [process, process],
    };
    var manual = job with { UniqueStr = "manual", Processes = [process] };
    db.AddJobs(new NewJobs { ScheduleId = "group", Jobs = [job, manual] }, null, true);
    var route = new MazakRouteIdentity
    {
      Source = MazakConfig.DefaultControllerSource,
      PartName = "part:7:2:2",
      NativeProcess = 1,
      JobUnique = job.UniqueStr,
      JobProcess = 2,
      Path = 1,
    };
    db.ReserveMazakRouteIdentity(route);
    db.ReserveMazakRouteIdentity(route);
    await Assert
      .That(() => db.ReserveMazakRouteIdentity(route with { JobUnique = "manual" }))
      .Throws<ConflictRequestException>();
    await Assert
      .That(db.LoadMazakRouteIdentities(route.Source, job.UniqueStr))
      .IsEquivalentTo([route]);

    var material = db.AllocateMaterialIDAndGenerateSerial(
      job.UniqueStr,
      job.PartName,
      job.Processes.Count,
      now.AddMinutes(-5),
      out var mark
    );
    var loaded = ambiguousIdentities
      ? ImmutableList.Create(material, db.AllocateMaterialID(job.UniqueStr, job.PartName, 2))
      : ImmutableList.Create(material);
    db.RecordLoadUnloadComplete(
      toLoad:
      [
        new()
        {
          MaterialIDs = loaded,
          Process = 2,
          FaceNum = 1,
          Path = 1,
          ActiveOperationTime = TimeSpan.Zero,
          AdditionalData = stampNativeName
            ? ImmutableDictionary<string, string>.Empty.Add(
              "mazak-native-part-name",
              route.PartName
            )
            : null,
        },
      ],
      previouslyLoaded: null,
      toUnload: [],
      previouslyUnloaded: null,
      pallet: 1,
      lulNum: 1,
      totalElapsed: TimeSpan.Zero,
      timeUTC: now.AddMinutes(-5),
      externalQueues: null
    );
    MazakLoadUnloadFace? resolvedFace = null;
    bool transferPending = false;
    MazakConfig Configure(MazakConfig config) =>
      config with
      {
        UseDurableRouteIdentity = true,
        UseInsightJobQuantities = j => j.UniqueStr == job.UniqueStr,
        ResolveLoadUnloadTransaction = (_, context) =>
        {
          var face = context.Unloads.Single();
          resolvedFace = face;
          return reportedQuantity == 1 && !ambiguousIdentities
            ? new MazakLoadUnloadResolution.Resolved
            {
              MaterialForLoads = ImmutableDictionary<string, MazakResolvedLoad>.Empty,
              MaterialForUnloads = ImmutableDictionary<string, MazakResolvedUnload>.Empty.Add(
                face.ForeignId,
                new MazakResolvedUnload
                {
                  MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination?>.Empty.Add(
                    material,
                    null
                  ),
                }
              ),
            }
            : new MazakLoadUnloadResolution.UnableToResolve("Missing a material identity.");
        },
      };
    MazakSync NewSynchronizer() =>
      new(
        cell.Native,
        new FMSSettings { QuarantineQueue = "quarantine" },
        Configure(
          new MazakConfig
          {
            DBType = MazakDbType.MazakSmooth,
            CanQuarantineMissingMaterial = _ =>
              !transferPending && (conflictingSchedule || retireBeforeIngestion),
          }
        ),
        cell.Clock
      );
    static MazakPartRow Part(string name, string comment) =>
      new()
      {
        PartName = name,
        Comment = comment,
        Processes =
        [
          new()
          {
            PartName = name,
            ProcessNumber = 1,
            Fixture = "fixture",
            FixLDS = "1",
            RemoveLDS = "1",
            CutMc = "1",
            MainProgram = "1001",
            FixQuantity = 1,
          },
        ],
      };
    static MazakScheduleRow Schedule(MazakPartRow part, int planned, int complete) =>
      new()
      {
        Id = 1,
        PartName = part.PartName,
        Comment = part.Comment,
        PlanQuantity = planned,
        CompleteQuantity = complete,
        Processes = [new() { ProcessNumber = 1 }],
      };
    var oldPart = Part(route.PartName, job.UniqueStr + "-2-1-InsightS");
    var newPart = Part("part:8:1", manual.UniqueStr + "-Insight");
    cell.Data = cell.Data with
    {
      Parts = [oldPart],
      Schedules = [Schedule(oldPart, 80, 40)],
      Pallets = [new() { PalletNumber = 1, Fixture = "fixture" }],
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "S001" }],
      PalletSubStatuses =
      [
        new()
        {
          PalletNumber = 1,
          PartName = route.PartName,
          ScheduleID = 1,
          PartProcessNumber = 1,
          FixQuantity = 1,
        },
      ],
    };
    db.RecordMachineStart(
      [
        new()
        {
          MaterialID = material,
          Process = 2,
          Face = 1,
        },
      ],
      pallet: 1,
      statName: "MC",
      statNum: 1,
      program: "1001",
      timeUTC: now.AddMinutes(-1)
    );
    using var sync = NewSynchronizer();
    var before = sync.CalculateCellState(db).CurrentStatus;
    await Assert.That(before.Jobs[job.UniqueStr].Completed![1][0]).IsEqualTo(0);
    await Assert
      .That(before.Jobs[job.UniqueStr].RemainingToStart.GetValueOrDefault())
      .IsEqualTo(10L - loaded.Count);

    if (ambiguousIdentities)
    {
      await Assert.That(before.Material.Single().MaterialID).IsEqualTo(-1L);
      var alarm = before.AlarmDetails!.Single();
      await Assert.That(alarm.Message).Contains("Mazak reports 1 piece");
      await Assert.That(alarm.Message).Contains(mark.Material.Single().Serial!);
      await Assert.That(alarm.Message).Contains($"material {loaded[1]}");
    }

    if (conflictingSchedule)
    {
      var schedules = cell.Data.Schedules;
      cell.Data = cell.Data with
      {
        Schedules = schedules.Select(s => s with { Comment = "other-Insight" }).ToImmutableList(),
      };
      var conflicted = sync.CalculateCellState(db).CurrentStatus;
      await Assert.That(db.IsMaterialInQueue(material)).IsFalse();
      var unassigned = conflicted.Material.Single();
      await Assert.That(unassigned.MaterialID).IsEqualTo(-1L);
      await Assert
        .That(unassigned.Location.Type)
        .IsEqualTo(InProcessMaterialLocation.LocType.OnPallet);
      await Assert
        .That(conflicted.AlarmDetails!.Single().Message)
        .Contains(mark.Material.Single().Serial!);
      await Assert
        .That(db.GetLogForMaterial(material).Any(e => e.Program == "MaterialMissingOnPallet"))
        .IsFalse();
      cell.Data = cell.Data with { Schedules = schedules };
      var recovered = sync.CalculateCellState(db).CurrentStatus;
      await Assert.That(recovered.Material.Single().MaterialID).IsEqualTo(material);
      await Assert.That(db.GetMaterialInAllQueues()).IsEmpty();

      var assignments = cell.Data.PalletSubStatuses;
      cell.Data = cell.Data with
      {
        PalletSubStatuses = assignments.Select(s => s with { FixQuantity = 2 }).ToImmutableList(),
      };
      var additional = sync.CalculateCellState(db);
      await Assert.That(additional.StateUpdated).IsTrue();
      var additionalQuantity = additional.CurrentStatus;
      await Assert.That(db.IsMaterialInQueue(material)).IsFalse();
      await Assert
        .That(additionalQuantity.Material.Single(m => m.MaterialID == material).Serial)
        .IsEqualTo(mark.Material.Single().Serial);
      await Assert.That(sync.CalculateCellState(db).StateUpdated).IsFalse();
      await Assert.That(db.GetMaterialInAllQueues()).IsEmpty();
      await Assert
        .That(db.GetLogForMaterial(material).Any(e => e.Program == "MaterialMissingOnPallet"))
        .IsFalse();
      cell.Data = cell.Data with { PalletSubStatuses = assignments };
      var cleared = sync.CalculateCellState(db);
      await Assert.That(cleared.StateUpdated).IsTrue();
      await Assert.That(cleared.CurrentStatus.AlarmDetails).IsNull();
    }

    using var reopened = cell.Repositories.OpenConnection();
    using var restarted = NewSynchronizer();
    if (retireBeforeIngestion)
    {
      // The controller has already completed the physical unload. Its CSV is still in flight
      // when the now-idle native schedule is deleted and may have its number reused.
      cell.Data = cell.Data with
      {
        PalletSubStatuses = [],
        Schedules = [Schedule(oldPart, 80, 41)],
      };
      cell.Data = cell.Data with
      {
        Schedules = deletePart ? [Schedule(newPart, 10, 3)] : [],
        Parts = deletePart ? [newPart] : [oldPart],
      };
      var replacement = route with { PartName = "part:9:2:2" };
      reopened.ReserveMazakRouteIdentity(replacement);
      var replacementPart = Part(replacement.PartName, oldPart.Comment);
      cell.Data = cell.Data with
      {
        Parts = [.. cell.Data.Parts, replacementPart],
        Schedules = [.. cell.Data.Schedules, Schedule(replacementPart, 20, 5) with { Id = 2 }],
      };
      // The handling owner still has an outstanding unload. A poll can observe the empty pallet
      // before Mazak publishes the CSV that identifies the transfer.
      transferPending = true;
      var awaitingHistory = restarted.CalculateCellState(reopened);
      await Assert.That(awaitingHistory.CurrentStatus.Material).IsEmpty();
      await Assert.That(reopened.GetMaterialInAllQueues()).IsEmpty();
    }
    else
    {
      await Assert.That(cell.Data.Schedules.Single().PartName).IsEqualTo(route.PartName);
    }

    var unload = cell.Start with
    {
      Code = LogCode.UnloadEnd,
      FullPartName = route.PartName,
      FixedQuantity = reportedQuantity,
    };
    cell.Data = cell.Data with
    {
      Logs = [unload, cell.End with { Code = LogCode.LoadBegin }],
      PalletSubStatuses = ambiguousIdentities ? [AssignedFace(route)] : [],
    };
    var complete = restarted.CalculateCellState(reopened).CurrentStatus;
    transferPending = false;
    await Assert.That(resolvedFace!.JobUnique).IsEqualTo(job.UniqueStr);
    await Assert.That(resolvedFace.Process).IsEqualTo(2);
    await Assert.That(resolvedFace.Path).IsEqualTo(1);
    var log = reopened.MostRecentLogEntryForForeignID(unload.ForeignID);
    if (ambiguousIdentities)
    {
      await Assert.That(log.Material).IsEmpty();
      await Assert.That(log.ProgramDetails!["mazak-unidentified-quantity"]).IsEqualTo("1");
      await Assert.That(complete.Material.Single().MaterialID).IsEqualTo(-1L);
      await Assert.That(complete.Jobs[job.UniqueStr].Completed![1][0]).IsEqualTo(0);
      await Assert
        .That(complete.AlarmDetails!.Any(a => a.Message.Contains("Mazak reports 1 piece")))
        .IsTrue();
      var boundary = cell.End with { Code = LogCode.LoadBegin };
      cell.Data = cell.Data with
      {
        PalletSubStatuses = [],
        Logs =
        [
          unload with
          {
            ForeignID = boundary.ForeignID + "1",
            TimeUTC = boundary.TimeUTC.AddSeconds(1),
          },
          boundary with
          {
            ForeignID = boundary.ForeignID + "2",
            TimeUTC = boundary.TimeUTC.AddSeconds(2),
          },
        ],
      };
      using var afterRestart = NewSynchronizer();
      var empty = afterRestart.CalculateCellState(reopened).CurrentStatus;
      await Assert.That(empty.Material.Where(m => m.Location.PalletNum == 1)).IsEmpty();
      await Assert
        .That(empty.AlarmDetails!.Single().Message)
        .Contains("unresolved serial attribution");
      await Assert
        .That(empty.AlarmDetails!.Single().Message)
        .Contains(mark.Material.Single().Serial!);
      await Assert.That(empty.Jobs[job.UniqueStr].Completed![1][0]).IsEqualTo(0);
      await Assert
        .That(afterRestart.CalculateCellState(reopened).CurrentStatus.AlarmDetails)
        .IsEquivalentTo(empty.AlarmDetails!);
      return;
    }
    await Assert.That(log.Material.Single().MaterialID).IsEqualTo(material);
    await Assert.That(log.Material.Single().Process).IsEqualTo(2);
    if (retireBeforeIngestion)
      await Assert.That(reopened.GetMaterialInAllQueues()).IsEmpty();
    if (reportedQuantity == 2)
    {
      await Assert.That(log.ProgramDetails!["mazak-unidentified-quantity"]).IsEqualTo("1");
      await Assert
        .That(
          reopened.GetRecentLog(0).Any(e => e.LogType == LogType.PalletCycle && !e.StartOfCycle)
        )
        .IsFalse();
    }
    await Assert.That(complete.Jobs[job.UniqueStr].Completed![1][0]).IsEqualTo(1);
    if (deletePart)
      await Assert.That(complete.Jobs[manual.UniqueStr].Completed![0][0]).IsEqualTo(3);
    restarted.CalculateCellState(reopened);
    await Assert
      .That(
        reopened
          .GetLogForMaterial(material)
          .Count(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD")
      )
      .IsEqualTo(1);

    reopened.InvalidatePalletCycle(material, 2, "operator", now);
    var invalidated = restarted.CalculateCellState(reopened).CurrentStatus;
    await Assert.That(invalidated.Jobs[job.UniqueStr].Completed![1][0]).IsEqualTo(0);
    await Assert
      .That(invalidated.Jobs[job.UniqueStr].RemainingToStart.GetValueOrDefault())
      .IsEqualTo(10L - loaded.Count);
  }
}
