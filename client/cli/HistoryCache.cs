using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlackMaple.FMSInsight.Client;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Cli;

internal static class HistoryCache
{
  internal const int SchemaVersion = 1;
  internal static readonly JsonSerializerOptions JsonOptions = InsightJson.CreateOptions();

  internal static FileStream FileLease(string path, FileMode mode)
  {
    var options = new FileStreamOptions
    {
      Mode = mode,
      Access = FileAccess.ReadWrite,
      Share = FileShare.None,
    };
    if (!OperatingSystem.IsWindows())
      options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    return new FileStream(path, options);
  }

  internal static SqliteConnection Open(string path)
  {
    var db = new SqliteConnection(
      new SqliteConnectionStringBuilder
      {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false,
      }.ToString()
    );
    try
    {
      db.Open();
      return db;
    }
    catch
    {
      db.Dispose();
      throw;
    }
  }

  internal static SqliteConnection Create(string path)
  {
    // CreateNew preserves existing evidence and sets private permissions before SQLite opens it.
    using (FileLease(path, FileMode.CreateNew)) { }
    var db = Open(path);
    try
    {
      Execute(db, null, Schema);
      return db;
    }
    catch
    {
      db.Dispose();
      throw;
    }
  }

  internal static void Validate(SqliteConnection db, string source)
  {
    using var command = db.CreateCommand();
    command.CommandText = "PRAGMA user_version";
    if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != SchemaVersion)
      throw new ArgumentException(
        "Cache schema differs from this CLI. Rebuild at a new database path; the existing file is preserved."
      );
    if (Value(db, "source") != source)
      throw new ArgumentException(
        "This cache belongs to a different source. Use a new database path."
      );
  }

  internal static string? Value(SqliteConnection db, string key)
  {
    using var command = db.CreateCommand();
    command.CommandText = "SELECT value FROM metadata WHERE key=$key";
    command.Parameters.AddWithValue("key", key);
    return command.ExecuteScalar() as string;
  }

  internal static ImmutableSortedSet<string> Strings(SqliteConnection db, string sql)
  {
    using var command = db.CreateCommand();
    command.CommandText = sql;
    using var reader = command.ExecuteReader();
    var values = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
    while (reader.Read())
      values.Add(reader.GetString(0));
    return values.ToImmutable();
  }

  internal static ImmutableHashSet<long> Numbers(
    SqliteConnection db,
    string sql,
    params (string Name, object? Value)[] parameters
  )
  {
    using var command = db.CreateCommand();
    command.CommandText = sql;
    foreach (var (name, value) in parameters)
      command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    using var reader = command.ExecuteReader();
    var values = ImmutableHashSet.CreateBuilder<long>();
    while (reader.Read())
      values.Add(reader.GetInt64(0));
    return values.ToImmutable();
  }

  internal static ImmutableSortedSet<string> References(SqliteConnection db) =>
    Strings(db, JobReferences);

  internal const string JobReferences = """
    SELECT job_unique FROM event_material WHERE job_unique != ''
    UNION SELECT job_unique FROM sim_station_parts WHERE job_unique != ''
    UNION SELECT job_unique FROM casting_actions WHERE job_unique != ''
    UNION SELECT job_unique FROM job_decrements WHERE event_counter IS NOT NULL
    """;

  internal static void PruneReferences(SqliteConnection db)
  {
    using var transaction = db.BeginTransaction();
    Execute(
      db,
      transaction,
      "DELETE FROM jobs WHERE retrieval='reference' AND job_unique NOT IN (" + JobReferences + ")"
    );
    Execute(
      db,
      transaction,
      "DELETE FROM job_decrements WHERE event_counter IS NULL AND job_unique NOT IN (SELECT job_unique FROM jobs)"
    );
    Execute(
      db,
      transaction,
      "DELETE FROM unresolved_job_references WHERE job_unique NOT IN (" + JobReferences + ")"
    );
    transaction.Commit();
  }

  internal static void StoreEvent(
    SqliteConnection db,
    SqliteTransaction transaction,
    EventRead read,
    string scope
  )
  {
    var log = read.Event;
    var summary =
      $"{log.Type} {(log.Startofcycle ? "start" : "end")} {log.Loc} {log.Locnum}: {log.Program} {log.Result}; "
      + string.Join(
        ", ",
        log.Material.Select(m =>
          $"{m.Part} material {m.Id} process {m.Proc} path {m.Path} serial {m.Serial}"
        )
      );
    Execute(
      db,
      transaction,
      """
      INSERT INTO events(counter,end_utc,log_type,start_of_cycle,location,location_num,pallet,program,result,
        elapsed_seconds,active_seconds,invalidated,summary,json,fetched_utc,scope)
      VALUES($counter,$end,$type,$start,$location,$number,$pallet,$program,$result,$elapsed,$active,$invalid,$summary,$json,$fetched,$scope)
      ON CONFLICT(counter) DO UPDATE SET end_utc=excluded.end_utc,log_type=excluded.log_type,start_of_cycle=excluded.start_of_cycle,
        location=excluded.location,location_num=excluded.location_num,pallet=excluded.pallet,program=excluded.program,result=excluded.result,
        elapsed_seconds=excluded.elapsed_seconds,active_seconds=excluded.active_seconds,invalidated=excluded.invalidated,
        summary=excluded.summary,json=excluded.json,fetched_utc=excluded.fetched_utc,
        scope=CASE WHEN events.scope='window' OR excluded.scope='window' THEN 'window'
          WHEN events.scope='material' OR excluded.scope='material' THEN 'material' ELSE 'correction' END
      """,
      ("counter", log.Counter),
      ("end", InsightHttp.Time(log.EndUTC)),
      ("type", log.Type.ToString()),
      ("start", log.Startofcycle),
      ("location", log.Loc),
      ("number", log.Locnum),
      ("pallet", log.Pal),
      ("program", log.Program),
      ("result", log.Result),
      ("elapsed", log.Elapsed.TotalSeconds),
      ("active", log.Active.TotalSeconds),
      ("invalid", log.Details?.GetValueOrDefault("PalletCycleInvalidated") == "1"),
      ("summary", OneLine(summary)),
      ("json", read.Json),
      ("fetched", InsightHttp.Time(read.FetchedUTC)),
      ("scope", scope)
    );
    Execute(
      db,
      transaction,
      "DELETE FROM event_material WHERE counter=$counter",
      ("counter", log.Counter)
    );
    foreach (var material in log.Material)
    {
      Execute(
        db,
        transaction,
        """
        INSERT INTO event_material(counter,material_id,job_unique,part,process,path,face,num_processes)
        VALUES($counter,$id,$job,$part,$process,$path,$face,$count)
        """,
        ("counter", log.Counter),
        ("id", material.Id),
        ("job", material.Uniq),
        ("part", material.Part),
        ("process", material.Proc),
        ("path", material.Path),
        ("face", material.Face),
        ("count", material.Numproc)
      );
      Execute(
        db,
        transaction,
        """
        INSERT OR REPLACE INTO material(material_id,serial,workorder,fetched_utc) VALUES($id,$serial,$workorder,$fetched)
        """,
        ("id", material.Id),
        ("serial", material.Serial),
        ("workorder", material.Workorder),
        ("fetched", InsightHttp.Time(read.FetchedUTC))
      );
    }
    if (log.Type is LogType.CastingAssigned or LogType.CastingUnassigned)
    {
      var assignment = Read<MaterialDetails>(Detail(log, "Assignment"));
      if (
        log.Material.Count != 1
        || assignment.MaterialID != log.Material[0].Id
        || string.IsNullOrEmpty(assignment.JobUnique)
        || assignment.NumProcesses is null
      )
        throw new JsonException(
          "Casting action material identity does not match its preserved assignment."
        );
      Execute(
        db,
        transaction,
        """
        INSERT OR REPLACE INTO casting_actions(counter,material_id,action,job_unique,part,num_processes,casting,assignment_json)
        VALUES($counter,$id,$action,$job,$part,$processes,$casting,$json)
        """,
        ("counter", log.Counter),
        ("id", assignment.MaterialID),
        ("action", log.Type.ToString()),
        ("job", assignment.JobUnique),
        ("part", assignment.PartName),
        ("processes", assignment.NumProcesses),
        ("casting", Detail(log, "Casting")),
        ("json", Detail(log, "Assignment"))
      );
    }
    if (log.Type == LogType.JobDecrement)
      foreach (
        var group in Read<ImmutableList<JobAndDecrementQuantity>>(Detail(log, "Decrements"))
          .GroupBy(d => (d.JobUnique, d.DecrementId))
      )
      {
        var decrement = group.First();
        if (group.Any(d => d.TimeUTC != decrement.TimeUTC || d.Part != decrement.Part))
          throw new JsonException(
            "A backend decrement identity has inconsistent time or part context."
          );
        StoreDecrement(
          db,
          transaction,
          decrement.JobUnique,
          decrement.Part,
          decrement.DecrementId,
          decrement.TimeUTC,
          group.Sum(d => (long)d.Quantity),
          log.Counter
        );
      }
  }

  internal static void StoreDecrement(
    SqliteConnection db,
    SqliteTransaction transaction,
    string job,
    string part,
    long id,
    DateTime time,
    long quantity,
    long? counter
  )
  {
    using var existing = db.CreateCommand();
    existing.Transaction = transaction;
    existing.CommandText =
      "SELECT quantity,time_utc,part FROM job_decrements WHERE job_unique=$job AND decrement_id=$id";
    existing.Parameters.AddWithValue("job", job);
    existing.Parameters.AddWithValue("id", id);
    using (var reader = existing.ExecuteReader())
      if (
        reader.Read()
        && (
          reader.GetInt64(0) != quantity
          || reader.GetString(1) != InsightHttp.Time(time)
          || reader.GetString(2) != part
        )
      )
        throw new JsonException(
          "Conflicting backend decrement identity. Check the source and rebuild at a new path."
        );
    Execute(
      db,
      transaction,
      """
      INSERT INTO job_decrements(job_unique,decrement_id,part,time_utc,quantity,event_counter)
      VALUES($job,$id,$part,$time,$quantity,$counter)
      ON CONFLICT(job_unique,decrement_id) DO UPDATE SET event_counter=COALESCE(excluded.event_counter,job_decrements.event_counter)
      """,
      ("job", job),
      ("id", id),
      ("part", part),
      ("time", InsightHttp.Time(time)),
      ("quantity", quantity),
      ("counter", counter)
    );
  }

  internal static void StoreHistory(
    SqliteConnection db,
    SqliteTransaction transaction,
    JsonDocument document,
    DateTime fetched
  )
  {
    var history = Read<HistoricData>(document.RootElement.GetRawText());
    var schedules = history
      .Jobs.Values.Select(j => j.ScheduleId)
      .Concat(history.StationUse.Select(s => s.ScheduleId))
      .OfType<string>()
      .Where(s => s.Length != 0)
      .ToImmutableHashSet(StringComparer.Ordinal);
    foreach (var schedule in schedules)
    {
      Execute(
        db,
        transaction,
        "DELETE FROM sim_station_parts WHERE interval_id IN (SELECT id FROM sim_station_use WHERE schedule_id=$id)",
        ("id", schedule)
      );
      Execute(
        db,
        transaction,
        "DELETE FROM sim_station_use WHERE schedule_id=$id",
        ("id", schedule)
      );
      Execute(
        db,
        transaction,
        "INSERT OR REPLACE INTO schedules(schedule_id,fetched_utc) VALUES($id,$time)",
        ("id", schedule),
        ("time", InsightHttp.Time(fetched))
      );
    }
    foreach (var item in Property(document.RootElement, "Jobs").EnumerateObject())
    {
      var job = Read<HistoricJob>(item.Value.GetRawText());
      if (job.Unique != item.Name)
        throw new JsonException("Job history key differs from job identity.");
      StoreJob(db, transaction, job, item.Value.GetRawText(), fetched, "schedule-history");
    }
    foreach (var item in Property(document.RootElement, "StationUse").EnumerateArray())
    {
      var station = Read<SimulatedStationUtilization>(item.GetRawText());
      Execute(
        db,
        transaction,
        """
        INSERT INTO sim_station_use(schedule_id,station_group,station_num,start_utc,end_utc,planned_downtime,pallet,json,fetched_utc)
        VALUES($schedule,$group,$num,$start,$end,$down,$pallet,$json,$fetched)
        """,
        ("schedule", station.ScheduleId),
        ("group", station.StationGroup),
        ("num", station.StationNum),
        ("start", InsightHttp.Time(station.StartUTC)),
        ("end", InsightHttp.Time(station.EndUTC)),
        ("down", station.PlanDown),
        ("pallet", station.Pallet),
        ("json", item.GetRawText()),
        ("fetched", InsightHttp.Time(fetched))
      );
      using var identity = db.CreateCommand();
      identity.Transaction = transaction;
      identity.CommandText = "SELECT last_insert_rowid()";
      var interval = Convert.ToInt64(identity.ExecuteScalar(), CultureInfo.InvariantCulture);
      foreach (var part in station.Parts ?? [])
        Execute(
          db,
          transaction,
          """
          INSERT INTO sim_station_parts(interval_id,job_unique,process,path) VALUES($interval,$job,$process,$path)
          """,
          ("interval", interval),
          ("job", part.JobUnique),
          ("process", part.Process),
          ("path", part.Path)
        );
    }
  }

  internal static void StoreJob(
    SqliteConnection db,
    SqliteTransaction transaction,
    HistoricJob job,
    string json,
    DateTime fetched,
    string retrieval
  )
  {
    var plan = HistoricalPlan.Project(job);
    using var existing = db.CreateCommand();
    existing.Transaction = transaction;
    existing.CommandText = "SELECT plan_json FROM jobs WHERE job_unique=$job";
    existing.Parameters.AddWithValue("job", job.Unique);
    if (
      existing.ExecuteScalar() is string prior
      && !JsonNode.DeepEquals(JsonNode.Parse(prior), JsonNode.Parse(plan))
    )
      throw new JsonException(
        "An immutable historical job changed. Check the source and rebuild at a new path."
      );
    Execute(
      db,
      transaction,
      """
      INSERT INTO jobs(job_unique,part,schedule_id,route_start_utc,route_end_utc,cycles,summary,plan_json,response_json,fetched_utc,retrieval)
      VALUES($unique,$part,$schedule,$start,$end,$cycles,$summary,$plan,$json,$fetched,$retrieval)
      ON CONFLICT(job_unique) DO UPDATE SET response_json=excluded.response_json,fetched_utc=excluded.fetched_utc,
        retrieval=CASE WHEN jobs.retrieval='schedule-history' THEN jobs.retrieval ELSE excluded.retrieval END
      """,
      ("unique", job.Unique),
      ("part", job.PartName),
      ("schedule", job.ScheduleId),
      ("start", InsightHttp.Time(job.RouteStartUTC)),
      ("end", InsightHttp.Time(job.RouteEndUTC)),
      ("cycles", job.Cycles),
      (
        "summary",
        OneLine($"{job.PartName} job {job.Unique}, {job.Cycles} planned cycles before decrements")
      ),
      ("plan", plan),
      ("json", json),
      ("fetched", InsightHttp.Time(fetched)),
      ("retrieval", retrieval)
    );
    // Legacy path rows can share the public decrement ID. Sum within one response, then
    // reconcile that total with other retrievals; upserting individual rows loses quantities.
    foreach (var group in (job.Decrements ?? []).GroupBy(d => d.DecrementId))
    {
      var decrement = group.First();
      if (group.Any(d => d.TimeUTC != decrement.TimeUTC))
        throw new JsonException("A backend decrement identity has inconsistent timestamps.");
      StoreDecrement(
        db,
        transaction,
        job.Unique,
        job.PartName,
        decrement.DecrementId,
        decrement.TimeUTC,
        group.Sum(d => (long)d.Quantity),
        null
      );
    }
    Execute(
      db,
      transaction,
      "DELETE FROM unresolved_job_references WHERE job_unique=$job",
      ("job", job.Unique)
    );
  }

  internal static string Detail(LogEntry log, string name) =>
    log.Details?.GetValueOrDefault(name)
    ?? throw new JsonException($"{log.Type} is missing its {name} payload.");

  internal static T Read<T>(string json) =>
    JsonSerializer.Deserialize<T>(json, JsonOptions)
    ?? throw new JsonException($"Null {typeof(T).Name} record.");

  internal static JsonElement Property(JsonElement json, string name)
  {
    JsonElement? value = null;
    foreach (var property in json.EnumerateObject())
      if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
        value = property.Value;
    return value ?? throw new JsonException($"Insight history is missing {name}.");
  }

  internal static void Metadata(
    SqliteConnection db,
    SqliteTransaction transaction,
    string key,
    string value
  ) =>
    Execute(
      db,
      transaction,
      "INSERT OR REPLACE INTO metadata(key,value) VALUES($key,$value)",
      ("key", key),
      ("value", value)
    );

  internal static void Execute(
    SqliteConnection db,
    SqliteTransaction? transaction,
    string sql,
    params (string Name, object? Value)[] parameters
  )
  {
    using var command = db.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    foreach (var (name, value) in parameters)
      command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    command.ExecuteNonQuery();
  }

  private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');

  private const string Schema = """
    CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
    CREATE TABLE coverage(start_utc TEXT NOT NULL,end_utc TEXT NOT NULL,events_fetched_utc TEXT NOT NULL,jobs_fetched_utc TEXT NOT NULL);
    CREATE TABLE events(counter INTEGER PRIMARY KEY,end_utc TEXT NOT NULL,log_type TEXT NOT NULL,start_of_cycle INTEGER NOT NULL,
      location TEXT NOT NULL,location_num INTEGER NOT NULL,pallet INTEGER NOT NULL,program TEXT NOT NULL,result TEXT NOT NULL,
      elapsed_seconds REAL NOT NULL,active_seconds REAL NOT NULL,invalidated INTEGER NOT NULL,summary TEXT NOT NULL,json TEXT NOT NULL,
      fetched_utc TEXT NOT NULL,scope TEXT NOT NULL);
    CREATE INDEX events_time ON events(end_utc);
    CREATE TABLE event_material(counter INTEGER NOT NULL,material_id INTEGER NOT NULL,job_unique TEXT NOT NULL,part TEXT NOT NULL,
      process INTEGER NOT NULL,path INTEGER,face INTEGER NOT NULL,num_processes INTEGER NOT NULL);
    CREATE INDEX event_material_counter ON event_material(counter);
    CREATE INDEX event_material_id ON event_material(material_id);
    CREATE TABLE material(material_id INTEGER PRIMARY KEY,serial TEXT,workorder TEXT,fetched_utc TEXT NOT NULL);
    CREATE INDEX material_serial ON material(serial);
    CREATE TABLE jobs(job_unique TEXT PRIMARY KEY,part TEXT NOT NULL,schedule_id TEXT,route_start_utc TEXT NOT NULL,
      route_end_utc TEXT NOT NULL,cycles INTEGER NOT NULL,summary TEXT NOT NULL,plan_json TEXT NOT NULL,response_json TEXT NOT NULL,
      fetched_utc TEXT NOT NULL,retrieval TEXT NOT NULL);
    CREATE TABLE job_decrements(job_unique TEXT NOT NULL,decrement_id INTEGER NOT NULL,part TEXT NOT NULL,time_utc TEXT NOT NULL,
      quantity INTEGER NOT NULL,event_counter INTEGER,PRIMARY KEY(job_unique,decrement_id));
    CREATE TABLE casting_actions(counter INTEGER PRIMARY KEY,material_id INTEGER NOT NULL,action TEXT NOT NULL,job_unique TEXT NOT NULL,
      part TEXT NOT NULL,num_processes INTEGER NOT NULL,casting TEXT NOT NULL,assignment_json TEXT NOT NULL);
    CREATE TABLE unresolved_job_references(job_unique TEXT PRIMARY KEY,http_status INTEGER NOT NULL,fetched_utc TEXT NOT NULL);
    CREATE TABLE schedules(schedule_id TEXT PRIMARY KEY,fetched_utc TEXT NOT NULL);
    CREATE TABLE sim_station_use(id INTEGER PRIMARY KEY,schedule_id TEXT NOT NULL,station_group TEXT NOT NULL,station_num INTEGER NOT NULL,
      start_utc TEXT NOT NULL,end_utc TEXT NOT NULL,planned_downtime INTEGER,pallet INTEGER,json TEXT NOT NULL,fetched_utc TEXT NOT NULL);
    CREATE INDEX sim_station_schedule ON sim_station_use(schedule_id);
    CREATE TABLE sim_station_parts(interval_id INTEGER NOT NULL,job_unique TEXT NOT NULL,process INTEGER NOT NULL,path INTEGER NOT NULL);
    CREATE TABLE material_coverage(lookup TEXT NOT NULL,material_ids_json TEXT NOT NULL,fetched_utc TEXT NOT NULL);
    PRAGMA user_version=1;
    """;
}

internal sealed record EventRead
{
  internal required LogEntry Event { get; init; }
  internal required string Json { get; init; }
  internal required DateTime FetchedUTC { get; init; }
}
