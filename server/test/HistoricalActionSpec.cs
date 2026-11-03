using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class HistoricalActionSpec : IDisposable
{
  private readonly Guid _id = Guid.NewGuid();
  private readonly RepositoryConfig _configuration;
  private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

  public HistoricalActionSpec() => _configuration = RepositoryConfig.InitializeMemoryDB(null, _id);

  public void Dispose() => _configuration.Dispose();

  private static Job Job(string id) =>
    new()
    {
      UniqueStr = id,
      PartName = "part",
      Cycles = 10,
      RouteStartUTC = Now,
      RouteEndUTC = Now.AddDays(1),
      Archived = false,
      Processes = [new ProcessInfo { Paths = [JobLogTest.EmptyPath] }],
    };

  private void Reject(LogType type, bool reject)
  {
    using var connection = new SqliteConnection(
      $"Data Source=file:${_id}?mode=memory&cache=shared"
    );
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = reject
      ? $"CREATE TRIGGER reject_action BEFORE INSERT ON stations WHEN NEW.StationLoc={(int)type} BEGIN SELECT RAISE(ABORT,'rejected action'); END"
      : "DROP TRIGGER reject_action";
    command.ExecuteNonQuery();
  }

  private static MaterialDetails Assignment(LogEntry entry) =>
    JsonSerializer.Deserialize<MaterialDetails>(entry.ProgramDetails!["Assignment"])!;

  [Test]
  public async Task CastingHistorySurvivesReleaseAndReassignmentWithoutChangingQueueOrder()
  {
    using var db = _configuration.OpenConnection();
    var material = db.BulkAddNewCastingsInQueue("casting", 2, "stock", ["A", "B"], "order", null)
      .MaterialIds.Order()
      .ToImmutableArray();
    var position = db.GetMaterialInAllQueues()
      .Select(m => (m.MaterialID, m.Position))
      .ToImmutableList();
    var published = ImmutableList<LogEntry>.Empty;
    _configuration.NewLogEntry += (entry, _, repository) =>
    {
      // The event and assignment are visible from a different connection before publication.
      using var observer = _configuration.OpenConnection();
      published = published.Add(
        observer.GetRecentLog(entry.Counter - 1).Single(e => e.Counter == entry.Counter)
      );
    };
    await Assert
      .That(db.AllocateCastingsInQueue("stock", "casting", "job-a", "part-a", 2, 3, 3))
      .IsEmpty();
    await Assert.That(published).IsEmpty();
    await Assert
      .That(db.AllocateCastingsInQueue("stock", "casting", "job-a", "part-a", 2, 3, 2))
      .IsEquivalentTo(material);
    var assigned = published;
    await Assert.That(assigned.Count).IsEqualTo(2);
    await Assert.That(assigned.All(e => e.LogType == LogType.CastingAssigned)).IsTrue();
    await Assert.That(Assignment(assigned[0]).Paths![1]).IsEqualTo(2);
    await Assert.That(Assignment(assigned[0]).JobUnique).IsEqualTo("job-a");
    await Assert.That(db.GetJobProductionSummary("job-a").AutomationEntryMaterialIds).IsEmpty();

    db.MarkCastingsAsUnallocated([material[0], material[0], -1], "casting");
    var released = published.Last();
    await Assert.That(published.Count).IsEqualTo(3);
    await Assert.That(released.LogType).IsEqualTo(LogType.CastingUnassigned);
    await Assert.That(Assignment(released).JobUnique).IsEqualTo("job-a");
    await Assert.That(Assignment(released).Paths![1]).IsEqualTo(2);
    db.MarkCastingsAsUnallocated([material[0]], "casting");
    await Assert.That(published.Count).IsEqualTo(3);
    db.AllocateCastingsInQueue("stock", "casting", "job-b", "part-b", 1, 1, 1);

    using var reopened = _configuration.OpenConnection();
    var history = reopened.GetLogForMaterial(material[0]).ToImmutableList();
    var original = history.Single(e => e.Counter == assigned[0].Counter);
    // Joined material is current; the historical action is independently preserved.
    await Assert.That(original.Material.Single().JobUniqueStr).IsEqualTo("job-b");
    await Assert.That(Assignment(original).JobUnique).IsEqualTo("job-a");
    await Assert
      .That(Assignment(history.Single(e => e.Counter == released.Counter)).Paths![1])
      .IsEqualTo(2);
    await Assert
      .That(reopened.GetMaterialInAllQueues().Select(m => (m.MaterialID, m.Position)))
      .IsEquivalentTo(position);
    await Assert.That(reopened.GetMaterialDetails(material[0])!.Serial).IsEqualTo("A");
    await Assert.That(reopened.GetMaterialDetails(material[0])!.Workorder).IsEqualTo("order");
  }

  [Test]
  [Arguments(LogType.CastingAssigned)]
  [Arguments(LogType.CastingUnassigned)]
  public async Task CastingWritesRollbackWhenTheActionCannotBeRecorded(LogType type)
  {
    using var db = _configuration.OpenConnection();
    var material = db.BulkAddNewCastingsInQueue("casting", 1, "stock", [], null, null)
      .MaterialIds.Single();
    if (type == LogType.CastingUnassigned)
      db.AllocateCastingsInQueue("stock", "casting", "job", "part", 2, 3, 1);
    var before = db.GetMaterialDetails(material);
    var cursor = db.GetRecentLog(-1).Last().Counter;
    void Change()
    {
      if (type == LogType.CastingAssigned)
        db.AllocateCastingsInQueue("stock", "casting", "job", "part", 2, 3, 1);
      else
        db.MarkCastingsAsUnallocated([material], "casting");
    }
    Reject(type, true);
    await Assert.That(Change).Throws<SqliteException>();
    await Assert
      .That(JsonSerializer.Serialize(db.GetMaterialDetails(material)))
      .IsEqualTo(JsonSerializer.Serialize(before));
    await Assert.That(db.GetRecentLog(cursor)).IsEmpty();
    Reject(type, false);
    Change();
    await Assert.That(db.GetRecentLog(cursor).Single().LogType).IsEqualTo(type);
  }

  [Test]
  public async Task IdentityAssignmentRecordsTheNiigataRawCastingTransitionOnce()
  {
    using var db = _configuration.OpenConnection();
    var material = db.AllocateMaterialIDForCasting("casting");
    db.AssignCastingToJob(material, "job", "part", 2, Now);
    var first = db.GetRecentLog(-1).Single();
    await Assert.That(first.LogType).IsEqualTo(LogType.CastingAssigned);
    await Assert.That(Assignment(first).JobUnique).IsEqualTo("job");
    await Assert.That(first.ProgramDetails!["Casting"]).IsEqualTo("casting");
    using var connection = new SqliteConnection(
      $"Data Source=file:${_id}?mode=memory&cache=shared"
    );
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
      "CREATE TRIGGER reject_material_update BEFORE UPDATE ON matdetails BEGIN SELECT RAISE(ABORT,'assignment retry wrote material'); END";
    command.ExecuteNonQuery();
    db.AssignCastingToJob(material, "job", "part", 2, Now);
    using var reopened = _configuration.OpenConnection();
    reopened.AssignCastingToJob(material, "job", "part", 2, Now.AddMinutes(1));
    await Assert.That(db.GetRecentLog(first.Counter)).IsEmpty();
  }

  [Test]
  [Arguments("other-part", 2)]
  [Arguments("part", 3)]
  public async Task SameJobCastingAssignmentRejectsConflictingDetails(string part, int processes)
  {
    using var db = _configuration.OpenConnection();
    var material = db.AllocateMaterialIDForCasting("casting");
    db.AssignCastingToJob(material, "job", "part", 2, Now);
    var assigned = db.GetRecentLog(-1).Single();
    var before = JsonSerializer.Serialize(db.GetMaterialDetails(material));
    using var reopened = _configuration.OpenConnection();
    await Assert
      .That(() => reopened.AssignCastingToJob(material, "job", part, processes, Now.AddMinutes(1)))
      .Throws<ConflictRequestException>();
    await Assert.That(JsonSerializer.Serialize(db.GetMaterialDetails(material))).IsEqualTo(before);
    await Assert.That(db.GetRecentLog(assigned.Counter)).IsEmpty();
    await Assert
      .That(JsonSerializer.Serialize(db.GetRecentLog(-1).Single()))
      .IsEqualTo(JsonSerializer.Serialize(assigned));
  }

  [Test]
  public async Task ExplicitCastingAssignmentRejectsConflictsAndRollsBackOnEventFailure()
  {
    using var db = _configuration.OpenConnection();
    var material = db.AllocateMaterialIDForCasting("casting");
    Reject(LogType.CastingAssigned, true);
    await Assert
      .That(() => db.AssignCastingToJob(material, "job", "part", 2, Now))
      .Throws<SqliteException>();
    await Assert.That(db.GetMaterialDetails(material)!.JobUnique).IsNull();
    await Assert.That(db.GetRecentLog(-1)).IsEmpty();
    Reject(LogType.CastingAssigned, false);
    db.AssignCastingToJob(material, "job", "part", 2, Now);
    var cursor = db.GetRecentLog(-1).Single().Counter;
    await Assert
      .That(() => db.AssignCastingToJob(material, "another-job", "part", 2, Now))
      .Throws<ConflictRequestException>();
    await Assert.That(db.GetMaterialDetails(material)!.JobUnique).IsEqualTo("job");
    await Assert.That(db.GetRecentLog(cursor)).IsEmpty();
  }

  [Test]
  public async Task DecrementEventsReconcileWithJobsAndCancellationReplayAfterRestart()
  {
    using var db = _configuration.OpenConnection();
    db.AddJobs(
      new NewJobs { ScheduleId = "schedule", Jobs = [Job("job"), Job("other")] },
      null,
      false
    );
    var material = db.AllocateMaterialID("job", "part", 1);
    db.RecordPathForProcess(material, 1, 2);
    db.RecordAddMaterialToQueue(material, 0, "stock", -1, null, null);
    var cursor = db.GetRecentLog(-1).Last().Counter;
    var count = new NewDecrementQuantity
    {
      JobUnique = "job",
      Part = "part",
      Quantity = 10,
    };
    var raw = new RawMaterialToUnallocate
    {
      MaterialID = material,
      JobUnique = "job",
      Casting = "casting",
    };
    var result = db.RecordJobDecrementOperation("cancel", [count], [raw], Now);
    var events = db.GetRecentLog(cursor).ToImmutableList();
    await Assert
      .That(events.Select(e => e.LogType))
      .IsEquivalentTo([LogType.CastingUnassigned, LogType.JobDecrement]);
    await Assert.That(events.All(e => e.EndTimeUTC == Now)).IsTrue();
    var payload = JsonSerializer.Deserialize<ImmutableList<JobAndDecrementQuantity>>(
      events.Last().ProgramDetails!["Decrements"]
    )!;
    await Assert.That(payload).IsEquivalentTo(result.Decrements);
    var historical = db.LoadJob("job").Decrements!.Single();
    await Assert.That(payload.Single().DecrementId).IsEqualTo(historical.DecrementId);
    await Assert.That(payload.Single().Quantity).IsEqualTo(historical.Quantity);
    using var reopened = _configuration.OpenConnection();
    reopened.RecordJobDecrementOperation("cancel", [], [], Now.AddDays(1));
    await Assert.That(reopened.GetRecentLog(events.Last().Counter)).IsEmpty();
    reopened.ArchiveJobs(
      ["other"],
      [count with { JobUnique = "other", Quantity = 3 }],
      Now.AddMinutes(1)
    );
    var archived = reopened.GetRecentLog(events.Last().Counter).Single();
    await Assert.That(archived.LogType).IsEqualTo(LogType.JobDecrement);
    await Assert.That(archived.EndTimeUTC).IsEqualTo(Now.AddMinutes(1));
    reopened.AddNewDecrement([], Now);
    reopened.RecordJobDecrementOperation("empty", [], [], Now);
    await Assert.That(reopened.GetRecentLog(archived.Counter)).IsEmpty();
  }

  [Test]
  public async Task DecrementFailureRollsBackTheReceiptAndReleasedCasting()
  {
    using var db = _configuration.OpenConnection();
    db.AddJobs(new NewJobs { ScheduleId = "schedule", Jobs = [Job("job")] }, null, false);
    var material = db.AllocateMaterialID("job", "part", 1);
    db.RecordPathForProcess(material, 1, 2);
    db.RecordAddMaterialToQueue(material, 0, "stock", -1, null, null);
    var cursor = db.GetRecentLog(-1).Last().Counter;
    Reject(LogType.JobDecrement, true);
    await Assert
      .That(() =>
        db.RecordJobDecrementOperation(
          "cancel",
          [
            new NewDecrementQuantity
            {
              JobUnique = "job",
              Part = "part",
              Quantity = 10,
            },
          ],
          [
            new RawMaterialToUnallocate
            {
              MaterialID = material,
              JobUnique = "job",
              Casting = "casting",
            },
          ],
          Now
        )
      )
      .Throws<SqliteException>();
    await Assert.That(db.LoadJobDecrementOperation("cancel")).IsNull();
    await Assert.That(db.LoadDecrementsForJob("job")).IsEmpty();
    await Assert.That(db.GetRecentLog(cursor)).IsEmpty();
    await Assert.That(db.GetMaterialDetails(material)!.JobUnique).IsEqualTo("job");
    await Assert.That(db.GetMaterialDetails(material)!.Paths![1]).IsEqualTo(2);
  }

  [Test]
  public async Task HistoricalActionsDoNotAdvanceTheControllerImportTimeCursor()
  {
    using var db = _configuration.OpenConnection();
    db.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = "job",
          Part = "part",
          Quantity = 1,
        },
      ],
      Now
    );
    await Assert.That(db.MaxLogDate()).IsEqualTo(DateTime.MinValue);
    db.RecordGeneralMessage([], "controller", "", timeUTC: Now.AddHours(-1));
    var material = db.BulkAddNewCastingsInQueue("casting", 1, "stock", [], null, null)
      .MaterialIds.Single();
    // Queue arrival is an existing physical event. Later allocation and release must not advance it.
    var controllerTime = db.MaxLogDate();
    db.AllocateCastingsInQueue("stock", "casting", "job", "part", 1, 1, 1);
    db.MarkCastingsAsUnallocated([material], "casting");
    db.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = "job",
          Part = "part",
          Quantity = 1,
        },
      ],
      controllerTime.AddHours(1)
    );
    await Assert.That(db.MaxLogDate()).IsEqualTo(controllerTime);
  }
}
