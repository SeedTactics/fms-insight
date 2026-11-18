#nullable enable

using System;
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
  [Arguments(false)]
  [Arguments(true)]
  public async Task RetirementProtectsPeerPreparationAndReleasesItsOwnFixtureGroup(
    bool peerAlreadyPreparing
  )
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null);
    using var db = config.OpenConnection();
    var device = new DownloadDevice("");
    var first = PendingJob(true);
    var peer = first with { UniqueStr = "peer", PartName = "peer-part" };
    db.AddJobs(new NewJobs { ScheduleId = "routes", Jobs = [first, peer] }, null, false);
    var routeConfig = RouteConfig;
    if (peerAlreadyPreparing)
    {
      device.Observe(
        device.Data with
        {
          MainPrograms =
          [
            .. device.Data.MainPrograms.Select(p =>
              p.MainProgram == "1002"
                ? p with
                {
                  Comment = MazakProcess.CreateMainProgramComment("cut", 1),
                }
                : p
            ),
            new()
            {
              MainProgram = "1003",
              Comment = MazakProcess.CreateMainProgramComment("cut", 2),
            },
          ],
        }
      );
    }
    MazakRouteWrite Step(Job job, IRepository repository) =>
      MazakRouteCapacity.Calculate(
        repository,
        device.Native.LoadAllData(),
        routeConfig,
        new FMSSettings(),
        job.UniqueStr,
        2,
        1,
        3
      );
    MazakRouteIdentity Install(Job job, IRepository repository)
    {
      for (var i = 0; i < 6; i++)
      {
        var next = Step(job, repository);
        if (next.NextWrite == null)
          return next.Identity;
        device.Native.Save(next.NextWrite);
      }
      throw new InvalidOperationException("Route installation did not settle.");
    }
    var a = Install(first, db);
    var originalGroup = db.LoadMazakRoutePreparation(a.Source, a.PartName, 1)!.FixtureGroup;
    if (peerAlreadyPreparing)
    {
      var pending = Step(peer, db);
      await Assert.That(pending.NextWrite!.Fixtures.Count).IsEqualTo(1);
      await Assert
        .That(
          db.LoadMazakRoutePreparation(
            pending.Identity.Source,
            pending.Identity.PartName,
            1
          )!.RequiredProgram
        )
        .IsEqualTo("1002");
      device.Native.Save(pending.NextWrite);
    }

    var retirement = MazakRouteRetirement.Capture(
      db,
      routeConfig,
      device.Data,
      ImmutableHashSet.Create(a.PartName)
    );
    using var reopened = config.OpenConnection();
    for (var i = 0; i < 6; i++)
    {
      var next = MazakRouteRetirement.Calculate(
        reopened,
        routeConfig,
        device.Native.LoadAllData(),
        retirement
      );
      if (next == null)
        break;
      await Assert.That(reopened.LoadMazakRoutePreparation(a.Source, a.PartName, 1)).IsNotNull();
      device.Native.Save(next);
    }
    await Assert.That(reopened.LoadMazakRoutePreparation(a.Source, a.PartName, 1)).IsNull();
    await Assert.That(reopened.LoadMazakRouteIdentity(a.Source, a.PartName, 1)).IsEqualTo(a);
    await Assert.That(device.Data.Parts).IsEmpty();
    await Assert.That(device.Data.MainPrograms.Any(p => p.MainProgram == "1002")).IsTrue();
    var installedPeer = Install(peer, reopened);
    if (!peerAlreadyPreparing)
    {
      await Assert
        .That(reopened.LoadMazakRoutePreparation(a.Source, installedPeer.PartName, 1)!.FixtureGroup)
        .IsEqualTo(originalGroup);
      var reinstalled = Install(first, reopened);
      await Assert.That(reinstalled).IsEqualTo(a);
      await Assert
        .That(reopened.LoadMazakRoutePreparation(a.Source, a.PartName, 1)!.FixtureGroup)
        .IsNotEqualTo(originalGroup);
    }
  }

  [Test]
  public async Task ScopedCleanupRemovesOnlyOldProgramsUnusedAfterPartDeletion()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null);
    using var db = config.OpenConnection();
    var device = new DownloadDevice("");
    var ordinary = PendingJob(false);
    var completed = ordinary with { UniqueStr = "closed", PartName = "closed-part" };
    var peer = ordinary with { UniqueStr = "peer", PartName = "peer-part" };
    const string pendingProgram = "pending-1.eia";
    var process = ordinary.Processes[0];
    var path = process.Paths[0];
    ordinary = ordinary with
    {
      Processes = ordinary.Processes.SetItem(
        0,
        process with
        {
          Paths = [path with { Stops = [path.Stops[0] with { Program = pendingProgram }] }],
        }
      ),
    };
    db.AddJobs(new NewJobs { ScheduleId = "old", Jobs = [completed, peer] }, null, true);
    db.AddJobs(new NewJobs { ScheduleId = "new", Jobs = [ordinary] }, null, false);

    static MazakPartRow Part(Job job, int generation, params string[] programs)
    {
      var name = job.PartName + ":" + generation + ":1";
      return new MazakPartRow
      {
        PartName = name,
        Comment = job.UniqueStr + "-Insight",
        Processes = programs
          .Select(
            (p, i) =>
              new MazakPartProcessRow
              {
                PartName = name,
                ProcessNumber = i + 1,
                MainProgram = p,
                Fixture = "legacy",
              }
          )
          .ToImmutableList(),
      };
    }
    var old = Part(completed, 6, "obsolete-1.eia", "peer-1.eia", pendingProgram);
    var survivor = Part(peer, 7, "peer-1.eia");
    var revisions = new[] { "obsolete", "peer", "pending" }
      .SelectMany(name =>
        Enumerable
          .Range(1, 2)
          .Select(rev => new MazakProgramRow
          {
            MainProgram = $"{name}-{rev}.eia",
            Comment = MazakProcess.CreateMainProgramComment(name, rev),
          })
      )
      .ToImmutableList();
    device.Observe(
      device.Data with
      {
        Parts = [old, survivor],
        Schedules =
        [
          new MazakScheduleRow
          {
            Id = 1,
            PartName = old.PartName,
            Comment = old.Comment,
            PlanQuantity = 3,
            CompleteQuantity = 3,
          },
        ],
        MainPrograms = [.. device.Data.MainPrograms, .. revisions],
      }
    );
    var routeConfig = RouteConfig with { BulkJobFilter = j => j.UniqueStr != peer.UniqueStr };
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
      .IsTrue();
    await Assert.That(device.Data.Parts).DoesNotContain(old);
    await Assert.That(device.Data.Parts).Contains(survivor);
    await Assert
      .That(device.Data.MainPrograms.Where(p => p.Comment.StartsWith("Insight:")))
      .IsEquivalentTo(revisions.Where(p => p.MainProgram != "obsolete-1.eia"));
  }

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
    var dropSchedule = MazakRouteRetirement.Calculate(db, routeConfig, device.Data, retiringA)!;
    await Assert.That(dropSchedule.Schedules.Single().Id).IsEqualTo(aSchedule.Id);
    await Assert.That(dropSchedule.Parts).IsEmpty();
    device.Native.Save(dropSchedule);
    var dropPart = MazakRouteRetirement.Calculate(db, routeConfig, device.Data, retiringA)!;
    await Assert.That(dropPart.Parts.Single().PartName).IsEqualTo(a.PartName);
    await Assert.That(dropPart.Pallets).IsEmpty();
    device.Native.Save(dropPart);
    await Assert
      .That(MazakRouteRetirement.Calculate(db, routeConfig, device.Data, retiringA))
      .IsNull();
    await Assert.That(device.Data.Pallets).Contains(sharedMapping);

    var retiringB = MazakRouteRetirement.Capture(device.Data, ImmutableHashSet.Create(b.PartName));
    device.Native.Save(MazakRouteRetirement.Calculate(db, routeConfig, device.Data, retiringB)!);
    device.Native.Save(MazakRouteRetirement.Calculate(db, routeConfig, device.Data, retiringB)!);
    using var reopened = config.OpenConnection();
    device.Native.ClearReceivedCalls();
    var dropMapping = MazakRouteRetirement.Calculate(
      reopened,
      routeConfig,
      device.Native.LoadAllData(),
      retiringB
    )!;
    await Assert.That(dropMapping.Pallets.Single().Fixture).IsEqualTo(fixture);
    await Assert.That(dropMapping.Fixtures).IsEmpty();
    device.Native.Save(dropMapping);
    var dropFixture = MazakRouteRetirement.Calculate(
      reopened,
      routeConfig,
      device.Native.LoadAllData(),
      retiringB
    )!;
    await Assert.That(dropFixture.Fixtures.Single().FixtureName).IsEqualTo(fixture);
    device.Native.Save(dropFixture);
    await Assert
      .That(
        MazakRouteRetirement.Calculate(
          reopened,
          routeConfig,
          device.Native.LoadAllData(),
          retiringB
        )
      )
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
