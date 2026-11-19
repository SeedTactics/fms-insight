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
  [Arguments("none")]
  [Arguments("reserved")]
  [Arguments("unreserved")]
  [Arguments("stamped")]
  [Arguments("other-path")]
  [Arguments("unmapped")]
  public async Task EmptyStationRetirementResolvesEveryPlausibleLegacyAlias(string liveAlias)
  {
    using var cell = new Cell();
    var db = cell.Repository;
    var process = new ProcessInfo { Paths = [JobLogTest.EmptyPath] };
    var job = new Job
    {
      UniqueStr = "legacy",
      PartName = "part",
      Cycles = 1,
      Archived = false,
      RouteStartUTC = cell.Clock.Now.UtcDateTime.AddHours(-1),
      RouteEndUTC = cell.Clock.Now.UtcDateTime.AddHours(1),
      Processes = [process, process],
    };
    db.AddJobs(new NewJobs { ScheduleId = "legacy", Jobs = [job] }, null, true);
    var route = new MazakRouteIdentity
    {
      Source = MazakConfig.DefaultControllerSource,
      PartName = "part:7:2:2",
      NativeProcess = 1,
      JobUnique = job.UniqueStr,
      JobProcess = 2,
      Path = 1,
    };
    var sibling = route with { PartName = "part:8:2:2" };
    if (liveAlias != "unmapped")
    {
      db.ReserveMazakRouteIdentity(route);
      db.ReserveMazakRouteIdentity(sibling);
    }
    var material = db.AllocateMaterialID(job.UniqueStr, job.PartName, 2);
    db.RecordPartialLoadUnload(
      [
        new()
        {
          MaterialIDs = [material],
          Process = 2,
          FaceNum = 1,
          Path = 1,
          ActiveOperationTime = TimeSpan.Zero,
          AdditionalData =
            liveAlias == "stamped"
              ? ImmutableDictionary<string, string>.Empty.Add(
                "mazak-native-part-name",
                route.PartName
              )
              : null,
        },
      ],
      [],
      1,
      1,
      TimeSpan.Zero,
      cell.Clock.Now.UtcDateTime.AddMinutes(-1),
      null
    );
    var live = new MazakScheduleRow
    {
      Id = 1,
      PartName = liveAlias is "reserved" or "stamped" ? sibling.PartName : "part:9:2:2",
      Comment = liveAlias == "other-path" ? "legacy-2-2-InsightS" : "legacy-2-1-InsightS",
      Processes = [new() { ProcessNumber = 1 }],
    };
    cell.Data = cell.Data with
    {
      PalletPositions = [new() { PalletNumber = 1, PalletPosition = "LS01" }],
      Schedules = liveAlias is "none" or "unmapped" ? [] : [live],
    };
    using var sync = cell.NewSynchronizer(configure: config =>
      config with
      {
        UseDurableRouteIdentity = true,
        CanQuarantineMissingMaterial = null,
      }
    );
    sync.CalculateCellState(db);
    sync.CalculateCellState(db);
    var blocked = liveAlias is "reserved" or "unreserved" or "unmapped";
    await Assert
      .That(db.CurrentPalletLog(1).Count(e => e.Program == "MazakPalletObservedEmpty"))
      .IsEqualTo(blocked ? 0 : 1);
    if (liveAlias is "reserved" or "unreserved")
    {
      cell.Data = cell.Data with { Schedules = [] };
      sync.CalculateCellState(db);
      sync.CalculateCellState(db);
      await Assert
        .That(db.CurrentPalletLog(1).Count(e => e.Program == "MazakPalletObservedEmpty"))
        .IsEqualTo(1);
    }
  }
}
