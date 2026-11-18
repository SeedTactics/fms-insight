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
  [Test]
  [Arguments(false, 1)]
  [Arguments(true, 1)]
  [Arguments(true, 2)]
  public async Task DelayedSplitUnloadSurvivesRetirementAndScheduleIdReuse(
    bool deletePart,
    int reportedQuantity
  )
  {
    using var cell = new Cell(id => "serial-" + id);
    var db = cell.Repository;
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
    db.RecordLoadUnloadComplete(
      toLoad:
      [
        new()
        {
          MaterialIDs = [material],
          Process = 2,
          FaceNum = 1,
          Path = 1,
          ActiveOperationTime = TimeSpan.Zero,
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
    MazakConfig Configure(MazakConfig config) =>
      config with
      {
        UseDurableRouteIdentity = true,
        UseInsightJobQuantities = j => j.UniqueStr == job.UniqueStr,
        ResolveLoadUnloadTransaction = (_, context) =>
        {
          var face = context.Unloads.Single();
          resolvedFace = face;
          return reportedQuantity == 1
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
    using var sync = cell.NewSynchronizer(configure: Configure);
    var before = sync.CalculateCellState(db).CurrentStatus;
    await Assert.That(before.Jobs[job.UniqueStr].Completed![1][0]).IsEqualTo(0);
    await Assert
      .That(before.Jobs[job.UniqueStr].RemainingToStart.GetValueOrDefault())
      .IsEqualTo(9L);

    cell.Data = cell.Data with
    {
      Schedules = deletePart ? [Schedule(newPart, 10, 3)] : [],
      Parts = deletePart ? [newPart] : [oldPart],
    };
    using var reopened = cell.Repositories.OpenConnection();
    using var restarted = cell.NewSynchronizer(configure: Configure);
    var retired = restarted.CalculateCellState(reopened).CurrentStatus;
    var stillOnPallet = retired.Material.Single(m => m.MaterialID == material);
    await Assert.That(stillOnPallet.JobUnique).IsEqualTo(job.UniqueStr);
    await Assert.That(stillOnPallet.Process).IsEqualTo(2);
    await Assert.That(stillOnPallet.Serial).IsEqualTo(mark.Material.Single().Serial);

    var unload = cell.Start with
    {
      Code = LogCode.UnloadEnd,
      FullPartName = route.PartName,
      FixedQuantity = reportedQuantity,
    };
    cell.Data = cell.Data with
    {
      Logs = [unload, cell.End with { Code = LogCode.LoadBegin }],
      PalletSubStatuses = [],
    };
    var complete = restarted.CalculateCellState(reopened).CurrentStatus;
    await Assert.That(resolvedFace!.JobUnique).IsEqualTo(job.UniqueStr);
    await Assert.That(resolvedFace.Process).IsEqualTo(2);
    await Assert.That(resolvedFace.Path).IsEqualTo(1);
    var log = reopened.MostRecentLogEntryForForeignID(unload.ForeignID);
    await Assert.That(log.Material.Single().MaterialID).IsEqualTo(material);
    await Assert.That(log.Material.Single().Process).IsEqualTo(2);
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
      .IsEqualTo(9L);
  }
}
