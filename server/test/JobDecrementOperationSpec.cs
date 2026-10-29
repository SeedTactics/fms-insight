using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class JobDecrementOperationSpec : IDisposable
{
  private readonly RepositoryConfig _configuration = RepositoryConfig.InitializeMemoryDB(
    null,
    Guid.NewGuid()
  );
  private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

  public void Dispose() => _configuration.Dispose();

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task RealMachiningWithoutLoadEstablishesEntryAndCannotBeReleasedAsRaw(
    bool machiningStarted
  )
  {
    using var repository = _configuration.OpenConnection();
    repository.AddJobs(new NewJobs { ScheduleId = "schedule", Jobs = [Job("job")] }, null, false);
    var id = repository.AllocateMaterialID("job", "part", 1);
    if (machiningStarted)
      repository.RecordMachineStart(
        [
          new EventLogMaterial
          {
            MaterialID = id,
            Process = 1,
            Face = 1,
          },
        ],
        1,
        "MC",
        1,
        "program",
        Now
      );
    else
      repository.RecordMachineEnd(
        [
          new EventLogMaterial
          {
            MaterialID = id,
            Process = 1,
            Face = 1,
          },
        ],
        1,
        "MC",
        1,
        "program",
        "",
        Now,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(1)
      );
    repository.RecordAddMaterialToQueue(id, 0, "stock", -1, null, null);
    await Assert
      .That(repository.GetJobProductionSummary("job").AutomationEntryMaterialIds)
      .IsEquivalentTo([id]);
    await Assert
      .That(() =>
        repository.RecordJobDecrementOperation(
          "raw",
          [
            new NewDecrementQuantity
            {
              JobUnique = "job",
              Part = "part",
              Quantity = 9,
            },
          ],
          [
            new RawMaterialToUnallocate
            {
              MaterialID = id,
              JobUnique = "job",
              Casting = "casting",
            },
          ],
          Now
        )
      )
      .Throws<ConflictRequestException>();
    await Assert.That(repository.LoadJobDecrementOperation("raw")).IsNull();
    await Assert.That(repository.GetMaterialDetails(id)!.JobUnique).IsEqualTo("job");
  }

  private static Job Job(string unique) =>
    new()
    {
      UniqueStr = unique,
      PartName = "part",
      Cycles = 10,
      RouteStartUTC = Now,
      RouteEndUTC = Now.AddDays(1),
      Archived = false,
      Processes = [new ProcessInfo { Paths = [JobLogTest.EmptyPath] }],
    };

  [Test]
  public async Task ReceiptReplaysAcrossConnectionsWithoutCapturingReplacementDemand()
  {
    using var repository = _configuration.OpenConnection();
    repository.AddJobs(new NewJobs { ScheduleId = "first", Jobs = [Job("first")] }, null, false);
    var receipt = repository.RecordJobDecrementOperation(
      "request",
      [
        new NewDecrementQuantity
        {
          JobUnique = "first",
          Part = "part",
          Quantity = 8,
        },
      ],
      [],
      Now
    );
    repository.AddJobs(
      new NewJobs { ScheduleId = "replacement", Jobs = [Job("replacement")] },
      "first",
      false
    );
    using var reopened = _configuration.OpenConnection();
    var replay = reopened.RecordJobDecrementOperation(
      "request",
      [
        new NewDecrementQuantity
        {
          JobUnique = "replacement",
          Part = "part",
          Quantity = 10,
        },
      ],
      [],
      Now.AddMinutes(1)
    );
    await Assert.That(replay.OperationId).IsEqualTo(receipt.OperationId);
    await Assert.That(replay.TimeUTC).IsEqualTo(receipt.TimeUTC);
    await Assert.That(replay.Decrements).IsEquivalentTo(receipt.Decrements);
    await Assert.That(reopened.LoadDecrementsForJob("replacement").Count).IsEqualTo(0);
    await Assert.That(reopened.LoadDecrementsForJob("first").Sum(d => d.Quantity)).IsEqualTo(8);
    await Assert
      .That(() =>
        reopened.RecordJobDecrementOperation(
          "another",
          [
            new NewDecrementQuantity
            {
              JobUnique = "first",
              Part = "part",
              Quantity = 2,
            },
          ],
          [],
          Now
        )
      )
      .Throws<ConflictRequestException>();
  }

  [Test]
  public async Task EmptyResultRetainsRequestIdentity()
  {
    using var repository = _configuration.OpenConnection();
    var result = repository.RecordJobDecrementOperation("empty", [], [], Now);
    repository.AddJobs(new NewJobs { ScheduleId = "later", Jobs = [Job("later")] }, null, false);
    var replay = repository.RecordJobDecrementOperation(
      "empty",
      [
        new NewDecrementQuantity
        {
          JobUnique = "later",
          Part = "part",
          Quantity = 10,
        },
      ],
      [],
      Now.AddDays(1)
    );
    await Assert.That(result.Decrements.Count).IsEqualTo(0);
    await Assert.That(replay).IsEqualTo(result);
    await Assert.That(repository.LoadDecrementsForJob("later").Count).IsEqualTo(0);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task RawAllocationReleaseCommitsWithCancellationOrRollsBack(bool invalidRelease)
  {
    using var repository = _configuration.OpenConnection();
    repository.AddJobs(new NewJobs { ScheduleId = "schedule", Jobs = [Job("job")] }, null, false);
    var id = repository.AllocateMaterialID("job", "part", 1);
    repository.RecordPathForProcess(id, 1, 1);
    repository.RecordAddMaterialToQueue(id, 0, "stock", 3, null, null);
    var raw = new RawMaterialToUnallocate
    {
      MaterialID = id,
      JobUnique = "job",
      Casting = "casting",
    };
    JobDecrementOperation Commit() =>
      repository.RecordJobDecrementOperation(
        "release",
        [
          new NewDecrementQuantity
          {
            JobUnique = "job",
            Part = "part",
            Quantity = 10,
          },
        ],
        invalidRelease ? [raw, raw with { MaterialID = -1 }] : [raw],
        Now
      );
    if (invalidRelease)
    {
      await Assert.That(Commit).Throws<ConflictRequestException>();
      await Assert.That(repository.GetMaterialDetails(id)!.JobUnique).IsEqualTo("job");
      await Assert.That(repository.LoadDecrementsForJob("job").Count).IsEqualTo(0);
      await Assert.That(repository.LoadJobDecrementOperation("release")).IsNull();
    }
    else
    {
      var result = Commit();
      await Assert.That(result.Decrements.Single().Quantity).IsEqualTo(10);
      await Assert
        .That(string.IsNullOrEmpty(repository.GetMaterialDetails(id)!.JobUnique))
        .IsTrue();
      await Assert.That(repository.GetMaterialDetails(id)!.PartName).IsEqualTo("casting");
      await Assert.That(repository.GetMaterialInAllQueues().Single().MaterialID).IsEqualTo(id);
    }
  }
}
