#nullable enable

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed partial class DuplicateDownloadSpec
{
  [Test]
  [Arguments(0)]
  [Arguments(100)]
  public async Task ScopedBulkWritesProtectCustomAndPeerWorkAndRetirementKeepsSharedMapping(
    int palletOffset
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null);
    using var db = config.OpenConnection();
    Job WithPalletOffset(Job job) =>
      job with
      {
        Processes = job
          .Processes.Select(process =>
            process with
            {
              Paths = process
                .Paths.Select(path =>
                  path with
                  {
                    PalletNums = path
                      .PalletNums.Select(pallet => pallet + palletOffset)
                      .ToImmutableSortedSet(),
                  }
                )
                .ToImmutableList(),
            }
          )
          .ToImmutableList(),
      };
    var first = WithPalletOffset(PendingJob(true));
    var second = first with { UniqueStr = "second-job" };
    var manual = WithPalletOffset(PendingJob(false)) with
    {
      UniqueStr = "manual",
      PartName = "manual-part",
    };
    var peerPending = manual with { UniqueStr = "peer-pending" };
    var peerInstalled = manual with { UniqueStr = "peer-installed" };
    var peerMissing = manual with { UniqueStr = "peer-missing" };
    db.AddJobs(
      new NewJobs { ScheduleId = "0-excluded", Jobs = [first, second, peerPending] },
      null,
      false
    );
    db.AddJobs(new NewJobs { ScheduleId = "1-manual", Jobs = [manual] }, null, false);
    db.AddJobs(
      new NewJobs { ScheduleId = "2-peer", Jobs = [peerInstalled, peerMissing] },
      null,
      true
    );

    var device = new DownloadDevice("");
    var routeConfig = RouteConfig with
    {
      UseStartingOffsetForDueDate = false,
      StartingPalletNumber = palletOffset + 1,
      BulkJobFilter = j => j.UniqueStr == manual.UniqueStr,
    };
    var settings = new FMSSettings();
    async Task<MazakRouteIdentity> Install(Job job)
    {
      for (var step = 0; step < 6; step++)
      {
        var result = MazakRouteCapacity.Calculate(
          db,
          device.Native.LoadAllData(),
          routeConfig,
          settings,
          job.UniqueStr,
          process: 2,
          path: 1,
          planQuantity: 3
        );
        if (result.NextWrite == null)
          return result.Identity;
        device.Native.Save(result.NextWrite);
        await Assert.That(result.NextWrite.Parts.Count <= 1).IsTrue();
      }
      throw new System.InvalidOperationException("Selected route failed to converge.");
    }

    var a = await Install(first);
    var b = await Install(second);
    var aPart = device.Data.Parts.Single(p => p.PartName == a.PartName);
    var bPart = device.Data.Parts.Single(p => p.PartName == b.PartName);
    var fixture = aPart.Processes.Single().Fixture;
    await Assert.That(bPart.Processes.Single().Fixture).IsEqualTo(fixture);
    var sharedMapping = device.Data.Pallets.Single();
    await Assert.That(sharedMapping.PalletNumber).IsEqualTo(2);
    var peerPart = bPart with
    {
      PartName = "manual-part:7:1",
      Comment = peerInstalled.UniqueStr + "-Insight",
      Processes = [bPart.Processes[0] with { PartName = "manual-part:7:1", Fixture = "F:7:30:1" }],
    };
    var peerSchedule = new MazakScheduleRow
    {
      Id = 8,
      PartName = peerPart.PartName,
      Comment = peerPart.Comment,
      PlanQuantity = 3,
      CompleteQuantity = 3,
    };
    var peerFixture = new MazakFixtureRow { FixtureName = "F:7:30:1", Comment = "Insight" };
    var orphan = new MazakFixtureRow { FixtureName = "F:8:31:1", Comment = "Insight" };
    var peerMapping = new MazakPalletRow
    {
      Fixture = peerFixture.FixtureName,
      PalletNumber = 10,
      FixtureGroupV2 = 30,
    };
    var orphanMapping = new MazakPalletRow
    {
      Fixture = orphan.FixtureName,
      PalletNumber = 11,
      FixtureGroupV2 = 31,
    };
    device.Observe(
      device.Data with
      {
        Parts = [.. device.Data.Parts, peerPart],
        Schedules = [.. device.Data.Schedules, peerSchedule],
        Fixtures = [.. device.Data.Fixtures, peerFixture, orphan],
        Pallets = [sharedMapping, peerMapping, orphanMapping],
      }
    );
    var aSchedule = device.Data.Schedules.Single(s => s.PartName == a.PartName);
    var bSchedule = device.Data.Schedules.Single(s => s.PartName == b.PartName);
    // That retired name is the natural generation for the next download without durable identity.
    var removed = a with { JobUnique = "older-ordinary", PartName = manual.PartName + ":2:1" };
    db.ReserveMazakRouteIdentity(removed);
    var valid = device.Data;
    device.Observe(
      valid with
      {
        Schedules =
        [
          .. valid.Schedules.Where(s => s.Id != aSchedule.Id),
          aSchedule with
          {
            Comment = "manual-Insight",
          },
        ],
      }
    );
    await Assert
      .That(() =>
        WriteJobs.SyncFromDatabase(device.Data, db, device.Native, settings, routeConfig, Now)
      )
      .Throws<ConflictRequestException>();
    await Assert.That(db.LoadJob(manual.UniqueStr)!.CopiedToSystem).IsFalse();
    device.Observe(valid);
    await Assert
      .That(WriteJobs.SyncFromDatabase(device.Data, db, device.Native, settings, routeConfig, Now))
      .IsTrue();
    await Assert.That(db.LoadJob(manual.UniqueStr)!.CopiedToSystem).IsTrue();
    foreach (var job in new[] { first, second, peerPending, peerInstalled, peerMissing })
      await Assert.That(db.LoadJob(job.UniqueStr)!.Archived).IsFalse();
    foreach (var job in new[] { first, second, peerPending })
      await Assert.That(db.LoadJob(job.UniqueStr)!.CopiedToSystem).IsFalse();
    await Assert.That(device.Data.Schedules).Contains(aSchedule);
    await Assert.That(device.Data.Schedules).Contains(bSchedule);
    await Assert.That(device.Data.Schedules).Contains(peerSchedule);
    await Assert.That(device.Data.Parts).Contains(peerPart);
    await Assert.That(device.Data.Fixtures).Contains(orphan);
    await Assert.That(device.Data.Pallets).Contains(orphanMapping);
    await Assert.That(device.Data.Pallets).Contains(peerMapping);
    var ordinary = device.Data.Parts.Single(p => p.Comment == "manual-Insight");
    await Assert.That(MazakPart.ParseUID(ordinary.PartName)).IsGreaterThan(2);
    await Assert
      .That(WriteJobs.SyncFromDatabase(device.Data, db, device.Native, settings, routeConfig, Now))
      .IsFalse();

    var owned = ImmutableHashSet.Create(a.PartName);
    var retiringA = MazakRouteRetirement.Capture(device.Data, owned);
    var dropSchedule = MazakRouteRetirement.Calculate(device.Data, retiringA)!;
    await Assert.That(dropSchedule.Schedules.Single().Id).IsEqualTo(aSchedule.Id);
    await Assert.That(dropSchedule.Parts).IsEmpty();
    device.Native.Save(dropSchedule);
    var dropPart = MazakRouteRetirement.Calculate(device.Data, retiringA)!;
    await Assert.That(dropPart.Parts.Single().PartName).IsEqualTo(a.PartName);
    await Assert.That(dropPart.Pallets).IsEmpty();
    device.Native.Save(dropPart);
    await Assert.That(MazakRouteRetirement.Calculate(device.Data, retiringA)).IsNull();
    await Assert.That(device.Data.Pallets).Contains(sharedMapping);

    var retiringB = MazakRouteRetirement.Capture(device.Data, ImmutableHashSet.Create(b.PartName));
    device.Native.Save(MazakRouteRetirement.Calculate(device.Data, retiringB)!);
    device.Native.Save(MazakRouteRetirement.Calculate(device.Data, retiringB)!);
    using var reopened = config.OpenConnection();
    device.Native.ClearReceivedCalls();
    var dropMapping = MazakRouteRetirement.Calculate(device.Native.LoadAllData(), retiringB)!;
    await Assert.That(dropMapping.Pallets.Single().Fixture).IsEqualTo(fixture);
    await Assert.That(dropMapping.Fixtures).IsEmpty();
    device.Native.Save(dropMapping);
    var dropFixture = MazakRouteRetirement.Calculate(device.Native.LoadAllData(), retiringB)!;
    await Assert.That(dropFixture.Fixtures.Single().FixtureName).IsEqualTo(fixture);
    device.Native.Save(dropFixture);
    await Assert
      .That(MazakRouteRetirement.Calculate(device.Native.LoadAllData(), retiringB))
      .IsNull();
    await Assert
      .That(reopened.LoadMazakRouteIdentity(a.Source, a.PartName, a.NativeProcess))
      .IsEqualTo(a);
    await Assert
      .That(reopened.LoadMazakRouteIdentity(b.Source, b.PartName, b.NativeProcess))
      .IsEqualTo(b);
    await Assert.That(device.Data.Parts).Contains(ordinary);
    await Assert.That(device.Data.Parts).Contains(peerPart);
    await Assert.That(device.Data.Pallets).Contains(peerMapping);
    await Assert.That(device.Data.Pallets).Contains(orphanMapping);
  }
}
