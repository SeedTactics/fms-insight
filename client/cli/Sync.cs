using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using BlackMaple.FMSInsight.Client;
using Microsoft.Data.Sqlite;
using static BlackMaple.FMSInsight.Cli.HistoryCache;

namespace BlackMaple.FMSInsight.Cli;

internal static class Sync
{
  internal static async Task<int> Run(
    HttpClient http,
    string path,
    DateTime? from,
    DateTime? to,
    CancellationToken cancellationToken
  )
  {
    using var lease = FileLease(path + ".lock", FileMode.OpenOrCreate);
    var isNew = !File.Exists(path);
    if (isNew && (from is null || to is null))
      throw new ArgumentException("A new cache requires both --from and --to.");
    if (from > to)
      throw new ArgumentException("--from must be at or before --to.");
    using var db = isNew ? Create(path) : Open(path);
    var source = http.BaseAddress!.AbsoluteUri;
    if (isNew)
    {
      using var transaction = db.BeginTransaction();
      Metadata(db, transaction, "source", source);
      Metadata(db, transaction, "cli_version", Program.Version);
      Metadata(
        db,
        transaction,
        "schema_version",
        SchemaVersion.ToString(CultureInfo.InvariantCulture)
      );
      Metadata(db, transaction, "requested_from_utc", InsightHttp.Time(from!.Value));
      Metadata(db, transaction, "requested_to_utc", InsightHttp.Time(to!.Value));
      Metadata(db, transaction, "next_window_start_utc", InsightHttp.Time(from.Value));
      Metadata(db, transaction, "loading_complete", "false");
      Metadata(db, transaction, "created_utc", InsightHttp.Time(DateTime.UtcNow));
      transaction.Commit();
    }
    Validate(db, source);
    var start = ParseTime(Value(db, "requested_from_utc")!);
    var end = ParseTime(Value(db, "requested_to_utc")!);
    if (from is not null && from != start)
      throw new ArgumentException(
        "--from must match the existing cache. Use a new path for a different window."
      );
    if (to < end)
      throw new ArgumentException("--to cannot shrink existing coverage.");
    await ValidateCheckpoint(http, db, cancellationToken);
    using (var transaction = db.BeginTransaction())
    {
      if (to > end)
      {
        if (Value(db, "loading_complete") == "true")
          Metadata(db, transaction, "next_window_start_utc", InsightHttp.Time(end));
        end = to.Value;
        Metadata(db, transaction, "requested_to_utc", InsightHttp.Time(end));
        Metadata(db, transaction, "loading_complete", "false");
      }
      Metadata(db, transaction, "complete", "false");
      Metadata(db, transaction, "job_context_complete", "false");
      Metadata(db, transaction, "sync_state", "loading");
      Metadata(db, transaction, "last_attempt_utc", InsightHttp.Time(DateTime.UtcNow));
      transaction.Commit();
    }
    using (var info = await InsightHttp.Get(http, "api/v1/fms/fms-information", cancellationToken))
    {
      using var transaction = db.BeginTransaction();
      Metadata(db, transaction, "server_information_json", info.RootElement.GetRawText());
      transaction.Commit();
    }
    await Load(http, db, end, cancellationToken);
    using (var transaction = db.BeginTransaction())
    {
      Metadata(db, transaction, "sync_state", "catching-up");
      transaction.Commit();
    }
    // Jobs and event counters are independent: recheck unknown schedules on every run.
    await Planning(http, db, start, end, cancellationToken);
    await CatchUp(http, db, start, end, cancellationToken);
    var unresolved = await ResolveJobs(http, db, cancellationToken);
    using (var transaction = db.BeginTransaction())
    {
      Metadata(db, transaction, "complete", "true");
      Metadata(db, transaction, "sync_state", "complete");
      Metadata(db, transaction, "job_context_complete", unresolved == 0 ? "true" : "false");
      Metadata(db, transaction, "last_successful_sync_utc", InsightHttp.Time(DateTime.UtcNow));
      transaction.Commit();
    }
    return unresolved;
  }

  private static async Task Load(
    HttpClient http,
    SqliteConnection db,
    DateTime to,
    CancellationToken cancellationToken
  )
  {
    if (Value(db, "event_counter") is null)
      await ObserveCounter(
        http,
        db,
        ParseTime(Value(db, "requested_from_utc")!),
        to,
        cancellationToken
      );
    while (Value(db, "loading_complete") != "true")
    {
      var from = ParseTime(Value(db, "next_window_start_utc")!);
      var end = to - from > TimeSpan.FromDays(1) ? from.AddDays(1) : to;
      using var events = await InsightHttp.Get(
        http,
        $"api/v1/log/events/all?{Bounds(from, end)}",
        cancellationToken
      );
      var eventsFetched = DateTime.UtcNow;
      var reads = ReadEvents(events);
      using var history = await InsightHttp.History(
        http,
        $"api/v1/jobs/history?{Bounds(from, end)}",
        Known(db),
        cancellationToken
      );
      var jobsFetched = DateTime.UtcNow;
      using var transaction = db.BeginTransaction();
      foreach (var read in reads)
        StoreEvent(db, transaction, read, "window");
      StoreHistory(db, transaction, history, jobsFetched);
      Execute(
        db,
        transaction,
        "INSERT INTO coverage(start_utc,end_utc,events_fetched_utc,jobs_fetched_utc) VALUES($from,$to,$events,$jobs)",
        ("from", InsightHttp.Time(from)),
        ("to", InsightHttp.Time(end)),
        ("events", InsightHttp.Time(eventsFetched)),
        ("jobs", InsightHttp.Time(jobsFetched))
      );
      Metadata(db, transaction, "next_window_start_utc", InsightHttp.Time(end));
      Metadata(db, transaction, "loading_complete", end == to ? "true" : "false");
      transaction.Commit();
    }
  }

  private static async Task ObserveCounter(
    HttpClient http,
    SqliteConnection db,
    DateTime from,
    DateTime to,
    CancellationToken cancellationToken
  )
  {
    var end = to;
    while (true)
    {
      var start = end - from > TimeSpan.FromDays(1) ? end.AddDays(-1) : from;
      using var response = await InsightHttp.Get(
        http,
        $"api/v1/log/events/all?{Bounds(start, end)}",
        cancellationToken
      );
      var anchor = ReadEvents(response).MaxBy(r => r.Event.Counter);
      if (anchor is not null || start == from)
      {
        // These are checkpoint observations, not completed coverage. Load every daily window
        // afterward, including idle probe windows: backdated inserts can arrive during the probe.
        using var transaction = db.BeginTransaction();
        Cursor(db, transaction, anchor);
        transaction.Commit();
        return;
      }
      end = start;
    }
  }

  private static async Task Planning(
    HttpClient http,
    SqliteConnection db,
    DateTime from,
    DateTime to,
    CancellationToken cancellationToken
  )
  {
    using var history = await InsightHttp.History(
      http,
      $"api/v1/jobs/history?{Bounds(from, to)}",
      Known(db),
      cancellationToken
    );
    using var transaction = db.BeginTransaction();
    StoreHistory(db, transaction, history, DateTime.UtcNow);
    Metadata(db, transaction, "planning_fetched_utc", InsightHttp.Time(DateTime.UtcNow));
    transaction.Commit();
  }

  private static async Task CatchUp(
    HttpClient http,
    SqliteConnection db,
    DateTime from,
    DateTime to,
    CancellationToken cancellationToken
  )
  {
    var counter = long.Parse(Value(db, "event_counter")!, CultureInfo.InvariantCulture);
    var expected = Value(db, "event_end_utc");
    var query = $"lastSeenCounter={counter.ToString(CultureInfo.InvariantCulture)}";
    if (!string.IsNullOrEmpty(expected))
      query += $"&expectedEndUTCofLastSeen={Uri.EscapeDataString(expected)}";
    using var recent = await InsightHttp.Get(
      http,
      $"api/v1/log/events/recent?{query}",
      cancellationToken
    );
    var reads = ReadEvents(recent);
    var previous = counter;
    foreach (var read in reads)
    {
      if (read.Event.Counter <= previous)
        throw new JsonException("Event catch-up counters are not strictly increasing.");
      previous = read.Event.Counter;
    }
    var cachedMaterials = Numbers(db, "SELECT material_id FROM material");
    var cachedCounters = Numbers(db, "SELECT counter FROM events");
    var jobs = Strings(db, "SELECT job_unique FROM jobs");
    // A material can first appear in this response. Include its later corrections before
    // advancing the cursor, even when those actions fall outside the requested window.
    var relevantMaterials = cachedMaterials.Union(
      reads
        .Where(r => InWindow(r, from, to) || cachedCounters.Contains(r.Event.Counter))
        .SelectMany(r => r.Event.Material.Select(m => m.Id))
    );
    var selected = reads
      .Where(r =>
        InWindow(r, from, to)
        || cachedCounters.Contains(r.Event.Counter)
        || IsCorrection(r.Event.Type) && r.Event.Material.Any(m => relevantMaterials.Contains(m.Id))
        || r.Event.Type == LogType.JobDecrement
          && Read<ImmutableList<JobAndDecrementQuantity>>(Detail(r.Event, "Decrements"))
            .Any(d => jobs.Contains(d.JobUnique))
      )
      .ToImmutableList();
    var selectedCounters = selected.Select(r => r.Event.Counter).ToImmutableHashSet();
    var refresh = relevantMaterials
      .Union(selected.SelectMany(r => r.Event.Material.Select(m => m.Id)))
      .Intersect(reads.SelectMany(r => r.Event.Material.Select(m => m.Id)));
    var refreshed = await Materials(http, refresh, cancellationToken);
    var affectedCached = Numbers(
      db,
      "SELECT counter FROM event_material WHERE material_id IN (SELECT value FROM json_each($ids))",
      ("ids", JsonSerializer.Serialize(refresh))
    );
    if (!affectedCached.IsSubsetOf(refreshed.Select(r => r.Event.Counter)))
      throw new IOException(
        "Previously cached material events are missing from the source. Check the source and rebuild at a new path."
      );
    using var transaction = db.BeginTransaction();
    foreach (var read in selected)
      StoreEvent(db, transaction, read, InWindow(read, from, to) ? "window" : "correction");
    foreach (var read in refreshed)
      if (
        InWindow(read, from, to)
        || cachedCounters.Contains(read.Event.Counter)
        || selectedCounters.Contains(read.Event.Counter)
      )
        StoreEvent(db, transaction, read, InWindow(read, from, to) ? "window" : "correction");
    if (reads.Count != 0)
      Cursor(db, transaction, reads[^1]);
    Metadata(db, transaction, "events_fetched_utc", InsightHttp.Time(DateTime.UtcNow));
    transaction.Commit();
  }

  internal static async Task Material(
    HttpClient http,
    string path,
    long? id,
    string? serial,
    CancellationToken cancellationToken
  )
  {
    using var lease = FileLease(path + ".lock", FileMode.OpenOrCreate);
    using var db = Open(path);
    Validate(db, http.BaseAddress!.AbsoluteUri);
    await ValidateCheckpoint(http, db, cancellationToken);
    ImmutableList<MaterialDetails> details;
    using (
      var response = await InsightHttp.Get(
        http,
        id.HasValue
          ? $"api/v1/log/material-details/{id.Value.ToString(CultureInfo.InvariantCulture)}"
          : $"api/v1/log/material-for-serial/{Uri.EscapeDataString(serial!)}",
        cancellationToken
      )
    )
      details = id.HasValue
        ? [Read<MaterialDetails>(response.RootElement.GetRawText())]
        : Read<ImmutableList<MaterialDetails>>(response.RootElement.GetRawText());
    var detailsFetched = DateTime.UtcNow;
    var ids = details.Select(d => d.MaterialID).ToImmutableHashSet();
    var reads = await Materials(http, ids, cancellationToken);
    using (var transaction = db.BeginTransaction())
    {
      // Apply observations in retrieval order: event joins can contain newer identity metadata.
      foreach (var material in details)
        Execute(
          db,
          transaction,
          "INSERT OR REPLACE INTO material(material_id,serial,workorder,fetched_utc) VALUES($id,$serial,$workorder,$time)",
          ("id", material.MaterialID),
          ("serial", material.Serial),
          ("workorder", material.Workorder),
          ("time", InsightHttp.Time(detailsFetched))
        );
      foreach (var read in reads)
        StoreEvent(db, transaction, read, "material");
      Execute(
        db,
        transaction,
        "INSERT INTO material_coverage(lookup,material_ids_json,fetched_utc) VALUES($lookup,$ids,$time)",
        ("lookup", id.HasValue ? $"id:{id}" : $"serial:{serial}"),
        ("ids", JsonSerializer.Serialize(ids.Order().ToImmutableArray())),
        ("time", InsightHttp.Time(DateTime.UtcNow))
      );
      Metadata(db, transaction, "job_context_complete", "false");
      transaction.Commit();
    }
    var unresolved = await ResolveJobs(http, db, cancellationToken);
    using var completed = db.BeginTransaction();
    Metadata(db, completed, "job_context_complete", unresolved == 0 ? "true" : "false");
    completed.Commit();
  }

  private static async Task ValidateCheckpoint(
    HttpClient http,
    SqliteConnection db,
    CancellationToken cancellationToken
  )
  {
    var expected = Value(db, "event_end_utc");
    if (string.IsNullOrEmpty(expected))
      return;
    var time = ParseTime(expected);
    var counter = long.Parse(Value(db, "event_counter")!, CultureInfo.InvariantCulture);
    // Check the saved pair before any import can overwrite cached evidence. An exact-time read
    // is bounded even after a long offline period; catch-up still validates the pair afterward.
    using var response = await InsightHttp.Get(
      http,
      $"api/v1/log/events/all?{Bounds(time, time)}",
      cancellationToken
    );
    if (!ReadEvents(response).Any(r => r.Event.Counter == counter && r.Event.EndUTC == time))
      throw new IOException(
        "The saved server checkpoint is missing or changed. Check the source and rebuild the disposable cache at a new path."
      );
  }

  private static async Task<ImmutableList<EventRead>> Materials(
    HttpClient http,
    ImmutableHashSet<long> ids,
    CancellationToken cancellationToken
  )
  {
    var reads = ImmutableList.CreateBuilder<EventRead>();
    foreach (var batch in ids.Order().Chunk(100))
    {
      var query = string.Join(
        "&",
        batch.Select(id => $"id={id.ToString(CultureInfo.InvariantCulture)}")
      );
      using var response = await InsightHttp.Get(
        http,
        $"api/v1/log/events/for-material?{query}",
        cancellationToken
      );
      reads.AddRange(ReadEvents(response));
    }
    return reads.ToImmutable();
  }

  private static async Task<int> ResolveJobs(
    HttpClient http,
    SqliteConnection db,
    CancellationToken cancellationToken
  )
  {
    PruneReferences(db);
    var missing = References(db).Except(Strings(db, "SELECT job_unique FROM jobs"));
    foreach (var unique in missing)
    {
      var response = await InsightHttp.Job(http, unique, cancellationToken);
      using var document = response.Document;
      using var transaction = db.BeginTransaction();
      if (document is not null)
      {
        var job = Read<HistoricJob>(document.RootElement.GetRawText());
        if (job.Unique != unique)
          throw new JsonException("Referenced job response has a different identity.");
        StoreJob(
          db,
          transaction,
          job,
          document.RootElement.GetRawText(),
          DateTime.UtcNow,
          "reference"
        );
      }
      else
        Execute(
          db,
          transaction,
          "INSERT OR REPLACE INTO unresolved_job_references(job_unique,http_status,fetched_utc) VALUES($job,$status,$fetched)",
          ("job", unique),
          ("status", (int)response.Status),
          ("fetched", InsightHttp.Time(DateTime.UtcNow))
        );
      transaction.Commit();
    }
    return Strings(db, "SELECT job_unique FROM unresolved_job_references").Count;
  }

  private static ImmutableList<EventRead> ReadEvents(JsonDocument document)
  {
    var fetched = DateTime.UtcNow;
    return document
      .RootElement.EnumerateArray()
      .Select(e => new EventRead
      {
        Event = Read<LogEntry>(e.GetRawText()),
        Json = e.GetRawText(),
        FetchedUTC = fetched,
      })
      .ToImmutableList();
  }

  private static void Cursor(SqliteConnection db, SqliteTransaction transaction, EventRead? read)
  {
    Metadata(
      db,
      transaction,
      "event_counter",
      (read?.Event.Counter ?? 0).ToString(CultureInfo.InvariantCulture)
    );
    Metadata(
      db,
      transaction,
      "event_end_utc",
      read is null ? "" : InsightHttp.QueryTime(read.Event.EndUTC)
    );
  }

  private static bool IsCorrection(LogType type) =>
    type
      is LogType.PartMark
        or LogType.OrderAssignment
        or LogType.InvalidateCycle
        or LogType.SwapMaterialOnPallet
        or LogType.CastingAssigned
        or LogType.CastingUnassigned;

  private static bool InWindow(EventRead read, DateTime from, DateTime to) =>
    read.Event.EndUTC >= from && read.Event.EndUTC <= to;

  private static ImmutableHashSet<string> Known(SqliteConnection db) =>
    Strings(db, "SELECT schedule_id FROM schedules").ToImmutableHashSet(StringComparer.Ordinal);

  private static string Bounds(DateTime from, DateTime to) =>
    $"startUTC={InsightHttp.QueryTime(from)}&endUTC={InsightHttp.QueryTime(to)}";

  private static DateTime ParseTime(string value) =>
    DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
