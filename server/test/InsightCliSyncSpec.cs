#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class InsightCliSyncSpec
{
  private static readonly DateTime Start = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
  private static readonly DateTime End = Start.AddDays(2);

  internal static Job Job(string unique, DateTime? start = null) =>
    new()
    {
      UniqueStr = unique,
      PartName = "part",
      Cycles = 10,
      Archived = false,
      RouteStartUTC = start ?? Start,
      RouteEndUTC = (start ?? Start).AddDays(1),
      Processes = [new ProcessInfo { Paths = [JobLogTest.EmptyPath, JobLogTest.EmptyPath] }],
    };

  internal static EventLogMaterial Material(long id) =>
    new()
    {
      MaterialID = id,
      Process = 1,
      Face = 1,
    };

  internal static Task<(int Exit, string Output, string Errors)> Resume(
    InsightCliTestHost host,
    string file,
    CancellationToken cancellationToken = default
  ) =>
    InsightCliTestHost.Run(
      ["sync", "--server", host.Url, "--database", Path.Combine(host.Directory, file)],
      cancellationToken
    );

  private static async Task Success(Task<(int Exit, string Output, string Errors)> command)
  {
    var result = await command;
    await Assert.That(result.Exit).IsEqualTo(0).Because(result.Errors);
    await Assert.That(result.Output).IsEqualTo("");
  }

  [Test]
  public async Task HistoricalProjectionConvergesThroughExplicitCorrectionsAndLiveFieldsStayOutsideIt()
  {
    await using var host = await InsightCliTestHost.Start();
    using var db = host.Repository.OpenConnection();
    db.AddJobs(
      new NewJobs { ScheduleId = "plan", Jobs = [Job("job") with { BookingIds = ["booking"] }] },
      null,
      false
    );
    var id = db.AllocateMaterialID("job", "part", 1);
    db.RecordPathForProcess(id, 1, 2);
    db.RecordMachineEnd(
      [Material(id)],
      1,
      "MC",
      2,
      "100",
      "end",
      Start.AddHours(1),
      TimeSpan.FromMinutes(3),
      TimeSpan.FromMinutes(2)
    );
    db.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = "job",
          Part = "part",
          Quantity = 1,
        },
      ],
      Start
    );
    await Success(host.Sync("cache.sqlite", Start, End));
    db.SetJobComment("job", "live comment");
    db.UpdateJobHold(
      "job",
      new HoldPattern
      {
        UserHold = true,
        ReasonForUserHold = "live hold",
        HoldUnholdPattern = [],
        HoldUnholdPatternStartUTC = Start,
        HoldUnholdPatternRepeats = false,
      }
    );
    db.MarkJobCopiedToSystem("job");
    db.ArchiveJob("job");
    await Success(Resume(host, "cache.sqlite"));
    using (var plan = await host.Query("cache.sqlite", "SELECT plan_json,summary FROM jobs"))
    {
      await Assert.That(plan.RootElement.GetRawText()).DoesNotContain("live comment");
      await Assert.That(plan.RootElement.GetRawText()).DoesNotContain("HoldEntireJob");
      await Assert.That(plan.RootElement.GetRawText()).DoesNotContain("Archived");
      await Assert.That(plan.RootElement.GetRawText()).DoesNotContain("Bookings");
    }
    db.RecordSerialForMaterialID(id, 1, "serial", End.AddDays(1));
    db.RecordWorkorderForMaterialID(Material(id), "order", End.AddDays(1));
    db.InvalidatePalletCycle(id, 1, "operator", End.AddDays(1)).ToImmutableList();
    db.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = "job",
          Part = "part",
          Quantity = 2,
        },
      ],
      End.AddDays(1),
      [new RemovedBooking { JobUnique = "job", BookingId = "booking" }]
    );
    await Success(Resume(host, "cache.sqlite"));
    using (
      var values = await host.Query(
        "cache.sqlite",
        "SELECT (SELECT serial FROM material) serial,(SELECT active_seconds FROM events WHERE log_type='MachineCycle') active,(SELECT invalidated FROM events WHERE log_type='MachineCycle') invalidated,(SELECT SUM(quantity) FROM job_decrements) decrements"
      )
    )
    {
      var row = values.RootElement[0];
      await Assert.That(row.GetProperty("serial").GetString()).IsEqualTo("serial");
      await Assert.That(row.GetProperty("active").GetDouble()).IsEqualTo(0);
      await Assert.That(row.GetProperty("invalidated").GetInt32()).IsEqualTo(1);
      await Assert.That(row.GetProperty("decrements").GetInt32()).IsEqualTo(3);
    }
    await Success(host.Sync("fresh.sqlite", Start, End));
    foreach (
      var sql in new[]
      {
        "SELECT counter,active_seconds,elapsed_seconds,invalidated FROM events WHERE scope='window' ORDER BY counter",
        "SELECT m.* FROM event_material m JOIN events e USING(counter) WHERE e.scope='window' ORDER BY counter,material_id",
        "SELECT job_unique,plan_json FROM jobs ORDER BY job_unique",
        "SELECT job_unique,decrement_id,quantity,time_utc FROM job_decrements ORDER BY job_unique,decrement_id",
      }
    )
    {
      using var actual = await host.Query("cache.sqlite", sql);
      using var fresh = await host.Query("fresh.sqlite", sql);
      await Assert.That(JsonNodeEqual(actual, fresh)).IsTrue().Because(sql);
    }
    await Assert
      .That(
        host.Requests.All(r =>
          r.Method == "GET"
          || r.Method == "POST"
            && r.Path.StartsWith("/api/v1/jobs/history?", StringComparison.Ordinal)
        )
      )
      .IsTrue();
  }

  [Test]
  public async Task NewlyDiscoveredMaterialKeepsOutOfWindowCastingHistoryAndReferencedPlans()
  {
    await using var host = await InsightCliTestHost.Start();
    using var db = host.Repository.OpenConnection();
    db.RecordGeneralMessage([], "note", "anchor", timeUTC: Start);
    await Success(host.Sync("cache.sqlite", Start, End));
    db.AddJobs(
      new NewJobs
      {
        ScheduleId = "outside",
        Jobs = [Job("first", Start.AddMonths(-1)), Job("second", Start.AddMonths(-1))],
      },
      null,
      false
    );
    var id = db.AllocateMaterialIDForCasting("casting");
    db.RecordAddMaterialToQueue(id, 0, "stock", -1, null, null, Start.AddHours(1))
      .ToImmutableList();
    db.AssignCastingToJob(id, "first", "part", 1, End.AddDays(1));
    db.RecordJobDecrementOperation(
      "release",
      [
        new NewDecrementQuantity
        {
          JobUnique = "first",
          Part = "part",
          Quantity = 1,
        },
      ],
      [
        new RawMaterialToUnallocate
        {
          MaterialID = id,
          JobUnique = "first",
          Casting = "casting",
        },
      ],
      End.AddDays(2)
    );
    db.AssignCastingToJob(id, "second", "part", 1, End.AddDays(3));
    var unrelated = db.AllocateMaterialIDForCasting("casting");
    db.AssignCastingToJob(unrelated, "first", "part", 1, End.AddDays(4));
    await Success(Resume(host, "cache.sqlite"));
    using (
      var actions = await host.Query(
        "cache.sqlite",
        "SELECT action,job_unique FROM casting_actions ORDER BY counter"
      )
    )
    {
      await Assert.That(actions.RootElement.GetArrayLength()).IsEqualTo(3);
      await Assert
        .That(actions.RootElement[0].GetProperty("job_unique").GetString())
        .IsEqualTo("first");
      await Assert
        .That(actions.RootElement[1].GetProperty("action").GetString())
        .IsEqualTo("CastingUnassigned");
      await Assert
        .That(actions.RootElement[1].GetProperty("job_unique").GetString())
        .IsEqualTo("first");
      await Assert
        .That(actions.RootElement[2].GetProperty("job_unique").GetString())
        .IsEqualTo("second");
    }
    using (
      var context = await host.Query(
        "cache.sqlite",
        "SELECT (SELECT COUNT(*) FROM jobs WHERE retrieval='reference') plans,(SELECT COUNT(*) FROM schedules) schedules"
      )
    )
    {
      await Assert.That(context.RootElement[0].GetProperty("plans").GetInt32()).IsEqualTo(2);
      await Assert.That(context.RootElement[0].GetProperty("schedules").GetInt32()).IsEqualTo(0);
    }
    await Success(Resume(host, "cache.sqlite"));
    await Success(host.Sync("fresh.sqlite", Start, End));
    foreach (
      var sql in new[]
      {
        "SELECT counter,json,scope FROM events ORDER BY counter",
        "SELECT * FROM event_material ORDER BY counter,material_id",
        "SELECT * FROM casting_actions ORDER BY counter",
        "SELECT job_unique,plan_json,retrieval FROM jobs ORDER BY job_unique",
        "SELECT material_id,serial,workorder FROM material ORDER BY material_id",
        "SELECT value FROM metadata WHERE key='event_counter'",
      }
    )
    {
      using var actual = await host.Query("cache.sqlite", sql);
      using var fresh = await host.Query("fresh.sqlite", sql);
      await Assert.That(JsonNodeEqual(actual, fresh)).IsTrue().Because(sql);
    }
  }

  [Test]
  public async Task SharedInvalidationRetainsLaterCorrectionsRegardlessOfSyncTiming()
  {
    await using var host = await InsightCliTestHost.Start();
    using var db = host.Repository.OpenConnection();
    var first = db.AllocateMaterialID("", "part", 1);
    var second = db.AllocateMaterialID("", "part", 1);
    var third = db.AllocateMaterialID("", "part", 1);
    var unrelated = db.AllocateMaterialID("", "part", 1);
    db.RecordGeneralMessage([Material(first)], "note", "window", timeUTC: Start);
    await Success(host.Sync("together.sqlite", Start, End));
    await Success(host.Sync("separate.sqlite", Start, End));
    db.RecordMachineEnd(
      [Material(first), Material(second)],
      1,
      "MC",
      1,
      "100",
      "shared outside window",
      End.AddDays(1),
      TimeSpan.FromMinutes(3),
      TimeSpan.FromMinutes(2)
    );
    db.InvalidatePalletCycle(first, 1, "operator", End.AddDays(2)).ToImmutableList();
    await Success(Resume(host, "separate.sqlite"));
    db.RecordMachineEnd(
      [Material(second), Material(third)],
      2,
      "MC",
      1,
      "100",
      "shared retry outside window",
      End.AddDays(3),
      TimeSpan.FromMinutes(3),
      TimeSpan.FromMinutes(2)
    );
    db.InvalidatePalletCycle(second, 1, "operator", End.AddDays(4)).ToImmutableList();
    await Success(Resume(host, "separate.sqlite"));
    db.RecordSerialForMaterialID(third, 1, "later-serial", End.AddDays(5));
    db.RecordSerialForMaterialID(unrelated, 1, "unrelated", End.AddDays(5));
    await Success(Resume(host, "together.sqlite"));
    await Success(Resume(host, "separate.sqlite"));
    using (
      var actions = await host.Query(
        "together.sqlite",
        "SELECT COUNT(*) count FROM events WHERE log_type='PartMark'"
      )
    )
      await Assert.That(actions.RootElement[0].GetProperty("count").GetInt32()).IsEqualTo(1);
    await Success(Resume(host, "together.sqlite"));
    await Success(host.Sync("fresh.sqlite", Start, End));
    foreach (
      var sql in new[]
      {
        "SELECT counter,json,scope FROM events ORDER BY counter",
        "SELECT * FROM event_material ORDER BY counter,material_id",
        "SELECT material_id,serial,workorder FROM material ORDER BY material_id",
        "SELECT value FROM metadata WHERE key='event_counter'",
      }
    )
    {
      using var together = await host.Query("together.sqlite", sql);
      using var separate = await host.Query("separate.sqlite", sql);
      using var fresh = await host.Query("fresh.sqlite", sql);
      await Assert.That(JsonNodeEqual(together, separate)).IsTrue().Because(sql);
      await Assert.That(JsonNodeEqual(together, fresh)).IsTrue().Because(sql);
    }
  }

  [Test]
  public async Task CastingActionsKeepOriginalAssignmentAndBackendDecrementsAreCountedOnce()
  {
    await using var host = await InsightCliTestHost.Start();
    using var db = host.Repository.OpenConnection();
    db.AddJobs(new NewJobs { ScheduleId = "plan", Jobs = [Job("job"), Job("other")] }, null, false);
    var id = db.AllocateMaterialIDForCasting("casting");
    db.RecordAddMaterialToQueue(id, 0, "stock", -1, null, null, Start).ToImmutableList();
    db.AssignCastingToJob(id, "job", "part", 1, Start.AddHours(1));
    db.RecordPathForProcess(id, 1, 2);
    await Success(host.Sync("cache.sqlite", Start, End));
    var receipt = db.RecordJobDecrementOperation(
      "cancel",
      [
        new NewDecrementQuantity
        {
          JobUnique = "job",
          Part = "part",
          Quantity = 2,
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
      End.AddDays(1)
    );
    db.AssignCastingToJob(id, "other", "part", 1, End.AddDays(1).AddHours(1));
    await Success(Resume(host, "cache.sqlite"));
    db.RecordJobDecrementOperation("cancel", [], [], End.AddDays(2));
    await Success(Resume(host, "cache.sqlite"));
    using var rows = await host.Query(
      "cache.sqlite",
      "SELECT (SELECT COUNT(*) FROM casting_actions) actions,(SELECT job_unique FROM casting_actions WHERE action='CastingUnassigned') released,(SELECT quantity FROM job_decrements) quantity,(SELECT COUNT(*) FROM job_decrements) decrements,(SELECT job_unique FROM event_material ORDER BY counter DESC LIMIT 1) current_job"
    );
    await Assert.That(rows.RootElement[0].GetProperty("actions").GetInt32()).IsEqualTo(3);
    await Assert.That(rows.RootElement[0].GetProperty("released").GetString()).IsEqualTo("job");
    await Assert.That(rows.RootElement[0].GetProperty("quantity").GetInt32()).IsEqualTo(2);
    await Assert.That(rows.RootElement[0].GetProperty("decrements").GetInt32()).IsEqualTo(1);
    await Assert
      .That(rows.RootElement[0].GetProperty("current_job").GetString())
      .IsEqualTo("other");
    await Assert.That(receipt.Decrements.Single().Quantity).IsEqualTo(2);
  }

  [Test]
  public async Task PlanningProgressDiscoversSimulationOnlyDownloadsWithoutANewEvent()
  {
    await using var host = await InsightCliTestHost.Start();
    using var db = host.Repository.OpenConnection();
    db.AddJobs(
      new NewJobs { ScheduleId = "outside", Jobs = [Job("referenced", Start.AddMonths(-1))] },
      null,
      false
    );
    var id = db.AllocateMaterialID("referenced", "part", 1);
    db.RecordGeneralMessage([Material(id)], "note", "context", timeUTC: Start);
    await Success(host.Sync("cache.sqlite", Start, End));
    using var before = await host.Query(
      "cache.sqlite",
      "SELECT value FROM metadata WHERE key='event_counter'"
    );
    db.AddJobs(
      new NewJobs
      {
        ScheduleId = "simulation",
        Jobs = [],
        StationUse =
        [
          new SimulatedStationUtilization
          {
            ScheduleId = "simulation",
            StationGroup = "MC",
            StationNum = 1,
            StartUTC = Start,
            EndUTC = End.AddDays(1),
            Parts =
            [
              new SimulatedStationPart
              {
                JobUnique = "referenced",
                Process = 1,
                Path = 2,
              },
              new SimulatedStationPart
              {
                JobUnique = "missing",
                Process = 1,
                Path = 1,
              },
            ],
          },
        ],
      },
      null,
      false
    );
    await Success(Resume(host, "cache.sqlite"));
    await Success(Resume(host, "cache.sqlite"));
    using var after = await host.Query(
      "cache.sqlite",
      "SELECT value FROM metadata WHERE key='event_counter'"
    );
    await Assert.That(JsonNodeEqual(before, after)).IsTrue();
    using var rows = await host.Query(
      "cache.sqlite",
      "SELECT (SELECT COUNT(*) FROM sim_station_use) intervals,(SELECT COUNT(*) FROM sim_station_parts p JOIN sim_station_use s ON s.id=p.interval_id) participants,(SELECT COUNT(*) FROM schedules) schedules,(SELECT retrieval FROM jobs WHERE job_unique='referenced') retrieval,(SELECT COUNT(*) FROM unresolved_job_references) missing"
    );
    await Assert.That(rows.RootElement[0].GetProperty("intervals").GetInt32()).IsEqualTo(1);
    await Assert.That(rows.RootElement[0].GetProperty("participants").GetInt32()).IsEqualTo(2);
    await Assert.That(rows.RootElement[0].GetProperty("schedules").GetInt32()).IsEqualTo(1);
    await Assert
      .That(rows.RootElement[0].GetProperty("retrieval").GetString())
      .IsEqualTo("reference");
    await Assert.That(rows.RootElement[0].GetProperty("missing").GetInt32()).IsEqualTo(1);
    var bodies = host.Requests.Where(r => r.Method == "POST").Select(r => r.Body).ToImmutableList();
    await Assert.That(bodies.Any(b => b.Contains("simulation", StringComparison.Ordinal))).IsTrue();
    await Assert.That(bodies.All(b => !b.Contains("outside", StringComparison.Ordinal))).IsTrue();
    db.AddJobs(
      new NewJobs { ScheduleId = "missing-plan", Jobs = [Job("missing", Start.AddMonths(-1))] },
      null,
      false
    );
    await Success(Resume(host, "cache.sqlite"));
    using var resolved = await host.Query(
      "cache.sqlite",
      "SELECT COUNT(*) count FROM unresolved_job_references"
    );
    await Assert.That(resolved.RootElement[0].GetProperty("count").GetInt32()).IsEqualTo(0);
  }

  [Test]
  public async Task InterruptedLoadingReplaysCorrectionsAndResumesCommittedWindows()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repository.OpenConnection();
    var id = db.AllocateMaterialID("", "part", 1);
    db.RecordGeneralMessage([Material(id)], "note", "first", timeUTC: Start);
    db.RecordGeneralMessage(
      [Material(id)],
      "note",
      "second",
      timeUTC: Start.AddDays(1).AddHours(1)
    );
    using var cancellation = new CancellationTokenSource();
    var requests = 0;
    await using var host = await InsightCliTestHost.Start(
      repository,
      beforeRequest: context =>
      {
        if (context.Request.Path != "/api/v1/jobs/history")
          return;
        if (++requests == 1)
          db.RecordSerialForMaterialID(id, 1, "during-load", End.AddDays(1));
        if (requests == 2)
          cancellation.Cancel();
      }
    );
    var interrupted = await InsightCliTestHost.Run(
      [
        "sync",
        "--server",
        host.Url,
        "--database",
        Path.Combine(host.Directory, "cache.sqlite"),
        "--from",
        Start.ToString("O"),
        "--to",
        End.ToString("O"),
      ],
      cancellation.Token
    );
    await Assert.That(interrupted.Exit).IsEqualTo(130).Because(interrupted.Errors);
    using (var progress = await host.Query("cache.sqlite", "SELECT COUNT(*) count FROM coverage"))
      await Assert.That(progress.RootElement[0].GetProperty("count").GetInt32()).IsEqualTo(1);
    await Success(Resume(host, "cache.sqlite"));
    using var completed = await host.Query(
      "cache.sqlite",
      "SELECT (SELECT serial FROM material) serial,(SELECT value FROM metadata WHERE key='complete') complete,(SELECT COUNT(*) FROM coverage) windows"
    );
    await Assert
      .That(completed.RootElement[0].GetProperty("serial").GetString())
      .IsEqualTo("during-load");
    await Assert
      .That(completed.RootElement[0].GetProperty("complete").GetString())
      .IsEqualTo("true");
    await Assert.That(completed.RootElement[0].GetProperty("windows").GetInt32()).IsEqualTo(2);
  }

  [Test]
  public async Task LocalCommitFailureDoesNotAdvanceCursorOrPartiallyApplyCorrections()
  {
    await using var host = await InsightCliTestHost.Start();
    using var db = host.Repository.OpenConnection();
    var id = db.AllocateMaterialID("", "part", 1);
    db.RecordGeneralMessage([Material(id)], "note", "first", timeUTC: Start);
    await Success(host.Sync("cache.sqlite", Start, End));
    using var before = await host.Query(
      "cache.sqlite",
      "SELECT (SELECT value FROM metadata WHERE key='event_counter') cursor,(SELECT json FROM events LIMIT 1) event"
    );
    db.RecordSerialForMaterialID(id, 1, "after", End.AddDays(1));
    host.ExecuteCacheSql(
      "cache.sqlite",
      "CREATE TRIGGER reject_cursor BEFORE INSERT ON metadata WHEN NEW.key='event_counter' BEGIN SELECT RAISE(ABORT,'commit fault'); END"
    );
    var failed = await Resume(host, "cache.sqlite");
    await Assert.That(failed.Exit).IsEqualTo(1);
    using var after = await host.Query(
      "cache.sqlite",
      "SELECT (SELECT value FROM metadata WHERE key='event_counter') cursor,(SELECT json FROM events LIMIT 1) event"
    );
    await Assert.That(JsonNodeEqual(before, after)).IsTrue();
    host.ExecuteCacheSql("cache.sqlite", "DROP TRIGGER reject_cursor");
    await Success(Resume(host, "cache.sqlite"));
    using var changed = await host.Query("cache.sqlite", "SELECT serial FROM material");
    await Assert.That(changed.RootElement[0].GetProperty("serial").GetString()).IsEqualTo("after");
  }

  [Test]
  public async Task TargetedSerialHistoryIsSeparateAndExtendingTheWindowFindsAlreadyPassedCounters()
  {
    await using var host = await InsightCliTestHost.Start();
    using var db = host.Repository.OpenConnection();
    var id = db.AllocateMaterialID("", "part", 1);
    var other = db.AllocateMaterialID("", "part", 1);
    var unselected = db.AllocateMaterialID("", "part", 1);
    db.RecordSerialForMaterialID(id, 1, "ambiguous", Start.AddDays(-3));
    db.RecordSerialForMaterialID(other, 1, "ambiguous", Start.AddDays(-3));
    db.RecordGeneralMessage([Material(id)], "note", "window", timeUTC: Start);
    db.RecordGeneralMessage([Material(other)], "note", "later", timeUTC: End.AddDays(1));
    db.RecordGeneralMessage(
      [Material(unselected)],
      "note",
      "passed-counter",
      timeUTC: End.AddDays(1)
    );
    await Success(host.Sync("cache.sqlite", Start, End));
    using var before = await host.Query(
      "cache.sqlite",
      "SELECT value FROM metadata WHERE key='event_counter'"
    );
    await Success(
      InsightCliTestHost.Run([
        "material",
        "--server",
        host.Url,
        "--database",
        Path.Combine(host.Directory, "cache.sqlite"),
        "--serial",
        "ambiguous",
      ])
    );
    using var after = await host.Query(
      "cache.sqlite",
      "SELECT value FROM metadata WHERE key='event_counter'"
    );
    await Assert.That(JsonNodeEqual(before, after)).IsTrue();
    using var targeted = await host.Query(
      "cache.sqlite",
      "SELECT COUNT(*) count FROM material WHERE serial='ambiguous'"
    );
    await Assert.That(targeted.RootElement[0].GetProperty("count").GetInt32()).IsEqualTo(2);
    using (
      var absent = await host.Query(
        "cache.sqlite",
        "SELECT COUNT(*) count FROM events WHERE result='passed-counter'"
      )
    )
      await Assert.That(absent.RootElement[0].GetProperty("count").GetInt32()).IsEqualTo(0);
    await Success(
      InsightCliTestHost.Run([
        "sync",
        "--server",
        host.Url,
        "--database",
        Path.Combine(host.Directory, "cache.sqlite"),
        "--to",
        End.AddDays(2).ToString("O"),
      ])
    );
    using var extended = await host.Query(
      "cache.sqlite",
      "SELECT COUNT(*) count FROM events WHERE result='passed-counter' AND scope='window'"
    );
    await Assert.That(extended.RootElement[0].GetProperty("count").GetInt32()).IsEqualTo(1);
    using var materialCoverage = await host.Query(
      "cache.sqlite",
      "SELECT material_ids_json FROM material_coverage"
    );
    await Assert
      .That(
        JsonSerializer
          .Deserialize<ImmutableArray<long>>(
            materialCoverage.RootElement[0].GetProperty("material_ids_json").GetString()!
          )
          .Length
      )
      .IsEqualTo(2);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task TargetedHistoryRetainsNewerSerialAndWorkorderThanTheInitialDetails(
    bool bySerial
  )
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repository.OpenConnection();
    var id = db.AllocateMaterialID("", "part", 1);
    db.RecordSerialForMaterialID(id, 1, "old-serial", Start.AddDays(-1));
    db.RecordWorkorderForMaterialID(Material(id), "old-order", Start.AddDays(-1));
    var changeBeforeHistory = false;
    await using var host = await InsightCliTestHost.Start(
      repository,
      beforeRequest: context =>
      {
        if (!changeBeforeHistory || context.Request.Path != "/api/v1/log/events/for-material")
          return;
        changeBeforeHistory = false;
        db.RecordSerialForMaterialID(id, 1, "new-serial", Start.AddHours(1));
        db.RecordWorkorderForMaterialID(Material(id), "new-order", Start.AddHours(1));
      }
    );
    await Success(host.Sync("cache.sqlite", Start, End));
    changeBeforeHistory = true;
    await Success(
      InsightCliTestHost.Run([
        "material",
        "--server",
        host.Url,
        "--database",
        Path.Combine(host.Directory, "cache.sqlite"),
        bySerial ? "--serial" : "--id",
        bySerial ? "old-serial" : id.ToString(System.Globalization.CultureInfo.InvariantCulture),
      ])
    );
    await Assert.That(changeBeforeHistory).IsFalse();
    using var rows = await host.Query(
      "cache.sqlite",
      "SELECT m.serial,m.workorder,m.fetched_utc=e.fetched_utc AS latest_observation,json_extract(e.json,'$.material[0].serial') event_serial,json_extract(e.json,'$.material[0].workorder') event_workorder FROM material m JOIN event_material em USING(material_id) JOIN events e USING(counter) ORDER BY e.counter"
    );
    await Assert.That(rows.RootElement.GetArrayLength()).IsEqualTo(4);
    foreach (var row in rows.RootElement.EnumerateArray())
    {
      await Assert.That(row.GetProperty("serial").GetString()).IsEqualTo("new-serial");
      await Assert.That(row.GetProperty("workorder").GetString()).IsEqualTo("new-order");
      await Assert.That(row.GetProperty("event_serial").GetString()).IsEqualTo("new-serial");
      await Assert.That(row.GetProperty("event_workorder").GetString()).IsEqualTo("new-order");
      await Assert.That(row.GetProperty("latest_observation").GetInt32()).IsEqualTo(1);
    }
  }

  [Test]
  public async Task ReassignmentRefreshesJoinedHistoryAndPrunesUnneededReferenceOnlyPlans()
  {
    await using var host = await InsightCliTestHost.Start();
    using var db = host.Repository.OpenConnection();
    db.AddJobs(
      new NewJobs { ScheduleId = "old-plan", Jobs = [Job("old", Start.AddMonths(-1))] },
      null,
      false
    );
    db.AddJobs(new NewJobs { ScheduleId = "new-plan", Jobs = [Job("new")] }, null, false);
    var id = db.AllocateMaterialID("old", "part", 1);
    db.RecordPathForProcess(id, 1, 2);
    db.RecordMachineEnd(
      [Material(id)],
      1,
      "MC",
      1,
      "100",
      "cycle",
      Start,
      TimeSpan.FromMinutes(3),
      TimeSpan.FromMinutes(2)
    );
    await Success(host.Sync("cache.sqlite", Start, End));
    db.InvalidateAndChangeAssignment(id, "operator", "new", "part", 1, End.AddDays(1))
      .ToImmutableList();
    await Success(Resume(host, "cache.sqlite"));
    using var changed = await host.Query(
      "cache.sqlite",
      "SELECT (SELECT COUNT(*) FROM jobs WHERE job_unique='old') old_plan,(SELECT job_unique FROM event_material ORDER BY counter LIMIT 1) joined_job,(SELECT path FROM event_material ORDER BY counter LIMIT 1) path"
    );
    await Assert.That(changed.RootElement[0].GetProperty("old_plan").GetInt32()).IsEqualTo(0);
    await Assert
      .That(changed.RootElement[0].GetProperty("joined_job").GetString())
      .IsEqualTo("new");
    await Assert
      .That(changed.RootElement[0].GetProperty("path").ValueKind)
      .IsEqualTo(JsonValueKind.Null);
  }

  [Test]
  public async Task MissingCachedMaterialRecordsStopProgressInsteadOfGuessingADeletion()
  {
    var identity = Guid.NewGuid();
    using var repository = RepositoryConfig.InitializeMemoryDB(null, identity);
    await using var host = await InsightCliTestHost.Start(repository);
    using var db = repository.OpenConnection();
    var id = db.AllocateMaterialID("", "part", 1);
    db.RecordGeneralMessage([Material(id)], "note", "first", timeUTC: Start);
    db.RecordGeneralMessage([Material(id)], "note", "anchor", timeUTC: Start.AddHours(1));
    await Success(host.Sync("cache.sqlite", Start, End));
    using var connection = new SqliteConnection(
      $"Data Source=file:${identity}?mode=memory&cache=shared"
    );
    connection.Open();
    using var remove = connection.CreateCommand();
    remove.CommandText =
      "DELETE FROM stations_mat WHERE Counter=1; DELETE FROM stations WHERE Counter=1";
    remove.ExecuteNonQuery();
    db.RecordSerialForMaterialID(id, 1, "changed", End.AddDays(1));
    var failed = await Resume(host, "cache.sqlite");
    await Assert.That(failed.Exit).IsEqualTo(1);
    await Assert.That(failed.Errors).Contains("missing");
    using var state = await host.Query(
      "cache.sqlite",
      "SELECT (SELECT value FROM metadata WHERE key='event_counter') cursor,(SELECT COUNT(*) FROM events WHERE counter=1) retained"
    );
    await Assert.That(state.RootElement[0].GetProperty("cursor").GetString()).IsEqualTo("2");
    await Assert.That(state.RootElement[0].GetProperty("retained").GetInt32()).IsEqualTo(1);
  }

  [Test]
  public async Task LegacyPathDecrementRowsAreSummedBeforeReconciliationWithLaterEvents()
  {
    var identity = Guid.NewGuid();
    using var repository = RepositoryConfig.InitializeMemoryDB(null, identity);
    await using var host = await InsightCliTestHost.Start(repository);
    using var db = repository.OpenConnection();
    db.AddJobs(new NewJobs { ScheduleId = "plan", Jobs = [Job("job")] }, null, false);
    using var connection = new SqliteConnection(
      $"Data Source=file:${identity}?mode=memory&cache=shared"
    );
    connection.Open();
    using var legacy = connection.CreateCommand();
    // Old path-level decrements expose no path through the current transport record.
    legacy.CommandText =
      "INSERT INTO job_decrements(DecrementId,JobUnique,Proc1Path,TimeUTC,Part,Quantity) VALUES(0,'job',1,$time,'part',2),(0,'job',2,$time,'part',2),(0,'job',3,$time,'part',1)";
    legacy.Parameters.AddWithValue("time", Start.AddDays(-1).Ticks);
    legacy.ExecuteNonQuery();
    await Success(host.Sync("cache.sqlite", Start, End));
    using (var original = await host.Query("cache.sqlite", "SELECT quantity FROM job_decrements"))
      await Assert.That(original.RootElement[0].GetProperty("quantity").GetInt32()).IsEqualTo(5);
    db.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = "job",
          Part = "part",
          Quantity = 3,
        },
      ],
      End.AddDays(1)
    );
    await Success(Resume(host, "cache.sqlite"));
    await Success(Resume(host, "cache.sqlite"));
    await Success(host.Sync("fresh.sqlite", Start, End));
    const string sql = "SELECT SUM(quantity) quantity,COUNT(*) decrements FROM job_decrements";
    using var actual = await host.Query("cache.sqlite", sql);
    using var fresh = await host.Query("fresh.sqlite", sql);
    await Assert.That(actual.RootElement[0].GetProperty("quantity").GetInt32()).IsEqualTo(8);
    await Assert.That(actual.RootElement[0].GetProperty("decrements").GetInt32()).IsEqualTo(2);
    await Assert.That(JsonNodeEqual(actual, fresh)).IsTrue();
  }

  [Test]
  public async Task BootstrapProbesWithinTheWindowAndStillFindsConcurrentBackdatedInserts()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repository.OpenConnection();
    db.RecordGeneralMessage([], "note", "old history", timeUTC: Start.AddYears(-1));
    db.RecordGeneralMessage([], "note", "middle day", timeUTC: Start.AddDays(1).AddHours(1));
    var inserted = false;
    await using var host = await InsightCliTestHost.Start(
      repository,
      beforeRequest: context =>
      {
        if (inserted || context.Request.Path != "/api/v1/jobs/history")
          return;
        inserted = true;
        db.RecordGeneralMessage([], "note", "backdated", timeUTC: Start.AddHours(1));
      }
    );
    await Success(host.Sync("cache.sqlite", Start, Start.AddDays(3)));
    using var rows = await host.Query("cache.sqlite", "SELECT COUNT(*) events FROM events");
    await Assert.That(rows.RootElement[0].GetProperty("events").GetInt32()).IsEqualTo(2);
    var catchups = host
      .Requests.Where(r =>
        r.Path.StartsWith("/api/v1/log/events/recent?", StringComparison.Ordinal)
      )
      .ToImmutableList();
    await Assert.That(catchups.Count).IsEqualTo(1);
    await Assert.That(catchups.Single().Path).Contains("lastSeenCounter=2&");
  }

  private static bool JsonNodeEqual(JsonDocument left, JsonDocument right) =>
    System.Text.Json.Nodes.JsonNode.DeepEquals(
      System.Text.Json.Nodes.JsonNode.Parse(left.RootElement.GetRawText()),
      System.Text.Json.Nodes.JsonNode.Parse(right.RootElement.GetRawText())
    );
}
