using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class HistoricalPlanningSpec
{
  private static readonly DateTime Start = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);

  private static SimulatedStationUtilization Interval(string schedule, DateTime start) =>
    new()
    {
      ScheduleId = schedule,
      StationGroup = "MC",
      StationNum = 1,
      StartUTC = start,
      EndUTC = start.AddHours(1),
      Parts =
      [
        new SimulatedStationPart
        {
          JobUnique = "referenced",
          Process = 1,
          Path = 2,
        },
      ],
    };

  [Test]
  public async Task LiveJobFieldsRemainMutableWithoutManufacturingHistory()
  {
    using var configuration = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = configuration.OpenConnection();
    var job = new Job
    {
      UniqueStr = "job",
      PartName = "part",
      Cycles = 10,
      Archived = false,
      RouteStartUTC = Start,
      RouteEndUTC = Start.AddDays(1),
      Processes = [new ProcessInfo { Paths = [JobLogTest.EmptyPath] }],
    };
    db.AddJobs(new NewJobs { ScheduleId = "schedule", Jobs = [job] }, null, false);
    db.SetJobComment("job", "live comment");
    db.MarkJobCopiedToSystem("job");
    db.ArchiveJob("job");
    var history = db.LoadJob("job");
    await Assert.That(history.Comment).IsEqualTo("live comment");
    await Assert.That(history.CopiedToSystem).IsTrue();
    await Assert.That(history.Archived).IsTrue();
    await Assert.That(history.Cycles).IsEqualTo(10);
    await Assert.That(db.GetRecentLog(-1)).IsEmpty();
  }

  [Test]
  public async Task SimulationOnlyHistoryReturnsCompleteImmutableSchedulesAndFiltersKnownIds()
  {
    using var configuration = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = configuration.OpenConnection();
    var download = new NewJobs
    {
      ScheduleId = "simulation",
      Jobs = [],
      StationUse = [Interval("simulation", Start), Interval("simulation", Start.AddDays(10))],
    };
    db.AddJobs(download, null, false);
    var history = db.LoadJobHistory(Start, Start);
    await Assert.That(history.Jobs).IsEmpty();
    await Assert.That(history.StationUse.Count).IsEqualTo(2);
    await Assert.That(history.StationUse.All(s => s.Parts!.Single().Path == 2)).IsTrue();
    await Assert.That(db.LoadJobHistory(Start, Start, ["simulation"]).StationUse).IsEmpty();
    await Assert.That(db.LoadRecentJobHistory(Start, ["simulation"]).StationUse).IsEmpty();
    await Assert
      .That(() =>
        db.AddJobs(
          download with
          {
            StationUse = [Interval("simulation", Start.AddDays(1))],
          },
          null,
          false
        )
      )
      .Throws<BadRequestException>();
    await Assert.That(db.LoadJobHistory(Start, Start).StationUse.Count).IsEqualTo(2);
    db.AddJobs(
      download with
      {
        ScheduleId = "revised",
        StationUse = [Interval("revised", Start.AddMinutes(5))],
      },
      null,
      false
    );
    await Assert
      .That(db.LoadRecentJobHistory(Start, ["simulation"]).StationUse.Single().ScheduleId)
      .IsEqualTo("revised");
    using var reopened = configuration.OpenConnection();
    await Assert.That(reopened.LoadJobHistory(Start, Start).StationUse.Count).IsEqualTo(2);
    await Assert
      .That(reopened.LoadJobHistory(Start, Start.AddHours(1)).StationUse.Count)
      .IsEqualTo(3);
  }

  [Test]
  public async Task ADownloadCannotWriteAnotherSchedulesSimulationOrPartLinks()
  {
    using var configuration = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = configuration.OpenConnection();
    var original = Interval("original", Start);
    db.AddJobs(
      new NewJobs
      {
        ScheduleId = "original",
        Jobs = [],
        StationUse = [original],
      },
      null,
      false
    );
    await Assert
      .That(() =>
        db.AddJobs(
          new NewJobs
          {
            ScheduleId = "new",
            Jobs = [],
            StationUse =
            [
              original with
              {
                PlanDown = true,
                Parts =
                [
                  new SimulatedStationPart
                  {
                    JobUnique = "different",
                    Process = 2,
                    Path = 3,
                  },
                ],
              },
            ],
          },
          null,
          false
        )
      )
      .Throws<BadRequestException>();
    var remaining = db.LoadJobHistory(Start, Start).StationUse.Single();
    await Assert.That(remaining.PlanDown).IsNull();
    await Assert.That(remaining.Parts!.Single().JobUnique).IsEqualTo("referenced");
    // The rejected download did not reserve its ID or leak any rows.
    db.AddJobs(
      new NewJobs
      {
        ScheduleId = "new",
        Jobs = [],
        StationUse = [Interval("new", Start)],
      },
      null,
      false
    );
    await Assert.That(db.LoadJobHistory(Start, Start).StationUse.Count).IsEqualTo(2);
  }

  [Test]
  public async Task LargeScheduleMembershipDoesNotExhaustSqlParametersAndKeepsQuotedIds()
  {
    var id = Guid.NewGuid();
    using var configuration = RepositoryConfig.InitializeMemoryDB(null, id);
    using var connection = new SqliteConnection($"Data Source=file:${id}?mode=memory&cache=shared");
    connection.Open();
    using (var seed = connection.CreateCommand())
    {
      seed.CommandText = """
        WITH RECURSIVE n(v) AS (VALUES(1) UNION ALL SELECT v+1 FROM n WHERE v<33000)
        INSERT INTO sim_station_use(SimId,StationGroup,StationNum,StartUTC,EndUTC)
        SELECT 'schedule-'||v,'MC',1,$start,$end FROM n;
        INSERT INTO sim_station_use(SimId,StationGroup,StationNum,StartUTC,EndUTC)
        VALUES('quoted " schedule','MC',1,$start,$end)
        """;
      seed.Parameters.AddWithValue("start", Start.Ticks);
      seed.Parameters.AddWithValue("end", Start.AddHours(1).Ticks);
      seed.ExecuteNonQuery();
    }
    using var db = configuration.OpenConnection();
    var history = db.LoadJobHistory(Start, Start);
    await Assert.That(history.StationUse.Count).IsEqualTo(33001);
    await Assert.That(history.StationUse.Any(s => s.ScheduleId == "quoted \" schedule")).IsTrue();
    var known = history.StationUse.Select(s => s.ScheduleId).ToImmutableHashSet();
    await Assert.That(db.LoadJobHistory(Start, Start, known).StationUse).IsEmpty();
    await Assert.That(db.LoadRecentJobHistory(Start, known).StationUse).IsEmpty();
    await Assert
      .That(db.LoadRecentJobHistory(Start, ["quoted \" schedule"]).StationUse.Count)
      .IsEqualTo(33000);
  }
}
