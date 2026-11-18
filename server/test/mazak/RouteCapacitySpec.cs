#nullable enable

using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using MazakMachineInterface;

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
  public async Task InstallsOnlySelectedSplitRouteAndResumesFromObservedPartAfterLostReply()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null);
    var job = PendingJob(true);
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

    MazakRouteWrite Calculate(IRepository db, int planned = 3, int offered = 0) =>
      MazakRouteCapacity.Calculate(
        db,
        device.Native.LoadAllData(),
        RouteConfig,
        new FMSSettings(),
        job.UniqueStr,
        process: 2,
        path: 1,
        planQuantity: planned,
        offeredQuantity: offered
      );

    MazakRouteIdentity identity;
    using (var db = config.OpenConnection())
    {
      db.AddJobs(new NewJobs { ScheduleId = "group", Jobs = [job] }, null, false);
      db.AddJobs(new NewJobs { ScheduleId = "manual", Jobs = [manual] }, null, true);
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
    await Assert.That(mapping.NextWrite.Pallets.Single().PalletNumber).IsEqualTo(2);
    device.Native.Save(mapping.NextWrite);
    var add = Calculate(reopened).NextWrite!.Schedules.Single();
    await Assert.That(add.Id).IsEqualTo(2);
    await Assert.That(add.PlanQuantity).IsEqualTo(3);
    await Assert.That(add.Processes.Single().ProcessMaterialQuantity).IsEqualTo(0);
    device.Native.Save(new MazakWriteData { Schedules = [add] });
    await Assert.That(Calculate(reopened).NextWrite).IsNull();

    var increase = Calculate(reopened, planned: 6, offered: 3).NextWrite!;
    var row = increase.Schedules.Single();
    await Assert.That(row.Command).IsEqualTo(MazakWriteCommand.ScheduleSafeEdit);
    await Assert.That(row.Id).IsEqualTo(add.Id);
    await Assert.That(row.PlanQuantity).IsEqualTo(6);
    await Assert.That(row.DueDate).IsEqualTo(add.DueDate);
    await Assert.That(row.Priority).IsEqualTo(add.Priority);
    await Assert.That(row.HoldMode).IsEqualTo(add.HoldMode);
    await Assert.That(row.Processes).IsEmpty();
    device.Native.Save(increase);
    var offer = Calculate(reopened, planned: 6, offered: 3).NextWrite!;
    await Assert
      .That(offer.Schedules.Single().Command)
      .IsEqualTo(MazakWriteCommand.ScheduleMaterialEdit);
    await Assert
      .That(offer.Schedules.Single().Processes.Single().ProcessMaterialQuantity)
      .IsEqualTo(3);
    device.Native.Save(offer);
    await Assert.That(Calculate(reopened, planned: 6, offered: 3).NextWrite).IsNull();

    await Assert.That(device.Data.Parts.Count()).IsEqualTo(2);
    await Assert
      .That(device.Data.Schedules.Single(s => s.Id == manualSchedule.Id))
      .IsEqualTo(manualSchedule);
    await Assert
      .That(device.Data.Parts.Single(p => p.PartName == manualPart.PartName))
      .IsEqualTo(manualPart);
    await Assert.That(reopened.LoadJob(job.UniqueStr)!.CopiedToSystem).IsFalse();

    var installed = device.Data.Parts.Single(p => p.PartName == identity.PartName);
    device.Native.Save(Calculate(reopened, planned: 6, offered: 0).NextWrite!);
    var (retire, _) = BuildMazakSchedules.RemoveSchedules(device.Data, s => s.Id == add.Id);
    device.Native.Save(retire);
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
    await Assert
      .That(() => Calculate(reopened, planned: 6, offered: 3))
      .Throws<ConflictRequestException>();
  }
}
