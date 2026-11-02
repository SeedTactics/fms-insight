#nullable enable
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class InsightCliSpec
{
  private static readonly DateTime Start = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);

  [Test]
  public async Task UnknownResponseFieldsArePreservedButDoNotExpandTheHistoricalProjection()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repository.OpenConnection();
    db.AddJobs(
      new NewJobs { ScheduleId = "plan", Jobs = [InsightCliSyncSpec.Job("job")] },
      null,
      false
    );
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    FMSInsightWebHost.JsonSettings(options);
    await using var host = await InsightCliTestHost.Start(
      repository,
      intercept: async context =>
      {
        if (context.Request.Path != "/api/v1/jobs/history")
          return false;
        var known = await JsonSerializer.DeserializeAsync<ImmutableHashSet<string>>(
          context.Request.Body
        );
        var history = db.LoadJobHistory(Start, Start, known);
        var response = JsonSerializer.SerializeToNode(history, options)!;
        if (history.Jobs.Count != 0)
          response["Jobs"]!["job"]!["futureField"] = "preserve me";
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(response.ToJsonString());
        return true;
      }
    );
    var result = await host.Sync("cache.sqlite", Start, Start);
    await Assert.That(result.Exit).IsEqualTo(0).Because(result.Errors);
    using var rows = await host.Query("cache.sqlite", "SELECT plan_json,response_json FROM jobs");
    await Assert
      .That(rows.RootElement[0].GetProperty("response_json").GetString())
      .Contains("preserve me");
    await Assert
      .That(rows.RootElement[0].GetProperty("plan_json").GetString())
      .DoesNotContain("futureField");
  }

  [Test]
  public async Task MalformedActionPayloadDoesNotAdvanceTheEventCursor()
  {
    var malformed = true;
    using var repository = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = repository.OpenConnection();
    db.AddJobs(
      new NewJobs { ScheduleId = "plan", Jobs = [InsightCliSyncSpec.Job("job")] },
      null,
      false
    );
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    FMSInsightWebHost.JsonSettings(options);
    await using var host = await InsightCliTestHost.Start(
      repository,
      intercept: async context =>
      {
        if (!malformed || context.Request.Path != "/api/v1/log/events/recent")
          return false;
        var after = long.Parse(
          context.Request.Query["lastSeenCounter"].ToString(),
          System.Globalization.CultureInfo.InvariantCulture
        );
        var response = JsonSerializer
          .SerializeToNode(db.GetRecentLog(after).ToImmutableList(), options)!
          .AsArray();
        foreach (var entry in response)
          if (entry!["type"]!.GetValue<string>() == "JobDecrement")
            entry["details"]!["Decrements"] = "invalid JSON";
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(response.ToJsonString());
        return true;
      }
    );
    var initial = await host.Sync("cache.sqlite", Start, Start);
    await Assert.That(initial.Exit).IsEqualTo(0).Because(initial.Errors);
    db.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = "job",
          Part = "part",
          Quantity = 2,
        },
      ],
      Start.AddDays(1)
    );
    var failed = await InsightCliSyncSpec.Resume(host, "cache.sqlite");
    await Assert.That(failed.Exit).IsEqualTo(1);
    using var unchanged = await host.Query(
      "cache.sqlite",
      "SELECT (SELECT value FROM metadata WHERE key='event_counter') cursor,(SELECT COUNT(*) FROM job_decrements) decrements"
    );
    await Assert.That(unchanged.RootElement[0].GetProperty("cursor").GetString()).IsEqualTo("0");
    await Assert.That(unchanged.RootElement[0].GetProperty("decrements").GetInt32()).IsEqualTo(0);
    malformed = false;
    var retry = await InsightCliSyncSpec.Resume(host, "cache.sqlite");
    await Assert.That(retry.Exit).IsEqualTo(0).Because(retry.Errors);
    using var repaired = await host.Query("cache.sqlite", "SELECT quantity FROM job_decrements");
    await Assert.That(repaired.RootElement[0].GetProperty("quantity").GetInt32()).IsEqualTo(2);
  }

  [Test]
  public async Task StatusAndProgramAreIndependentObservationsAndRespectAProxyPrefix()
  {
    await using var host = await InsightCliTestHost.Start(intercept: async context =>
    {
      if (!context.Request.Path.StartsWithSegments("/proxy"))
        return false;
      context.Response.ContentType = "application/json";
      await context.Response.WriteAsync(
        context.Request.Path.Value!.Contains("/jobs/status", StringComparison.Ordinal)
          ? "{\"liveHold\":true,\"unknownStatusField\":42}"
          : JsonSerializer.Serialize("G01 X1\n(comment: π)\n")
      );
      return true;
    });
    var status = await InsightCliTestHost.Run(["status", "--server", host.Url + "/proxy"]);
    await Assert.That(status.Exit).IsEqualTo(0).Because(status.Errors);
    using var json = JsonDocument.Parse(status.Output);
    await Assert.That(json.RootElement.GetProperty("unknownStatusField").GetInt32()).IsEqualTo(42);
    var program = await InsightCliTestHost.Run([
      "program",
      "--server",
      host.Url + "/proxy",
      "--name",
      "100",
      "--revision",
      "2",
    ]);
    await Assert.That(program.Exit).IsEqualTo(0).Because(program.Errors);
    await Assert.That(program.Output).IsEqualTo("G01 X1\n(comment: π)\n");
    await Assert.That(host.Requests.All(r => r.Method == "GET")).IsTrue();
    await Assert
      .That(host.Requests.Last().Path)
      .IsEqualTo("/proxy/api/v1/machines/program/100/revision/2/content");
  }

  [Test]
  public async Task EmptyWindowProgressAndCoverageDoNotClaimACompleteMaterialLifecycle()
  {
    await using var host = await InsightCliTestHost.Start();
    using var db = host.Repository.OpenConnection();
    db.RecordGeneralMessage([], "note", "elsewhere", timeUTC: Start.AddYears(-1));
    var result = await host.Sync("cache.sqlite", Start, Start.AddDays(1));
    await Assert.That(result.Exit).IsEqualTo(0).Because(result.Errors);
    using var rows = await host.Query(
      "cache.sqlite",
      "SELECT (SELECT COUNT(*) FROM events) events,(SELECT value FROM metadata WHERE key='event_counter') cursor,(SELECT COUNT(*) FROM coverage) windows"
    );
    await Assert.That(rows.RootElement[0].GetProperty("events").GetInt32()).IsEqualTo(0);
    await Assert.That(rows.RootElement[0].GetProperty("cursor").GetString()).IsEqualTo("1");
    await Assert.That(rows.RootElement[0].GetProperty("windows").GetInt32()).IsEqualTo(1);
    if (!OperatingSystem.IsWindows())
      await Assert
        .That(File.GetUnixFileMode(Path.Combine(host.Directory, "cache.sqlite")))
        .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
  }

  [Test]
  [Arguments("DELETE FROM metadata")]
  [Arguments("CREATE TABLE other(value)")]
  [Arguments("CREATE TEMP TABLE other(value)")]
  [Arguments("ATTACH DATABASE ':memory:' AS other")]
  [Arguments("PRAGMA writable_schema=ON")]
  [Arguments("PRAGMA user_version=99")]
  [Arguments("SELECT load_extension('bad')")]
  [Arguments("SELECT 1; DELETE FROM metadata")]
  public async Task QueryRejectsWritesIncludingAfterAnInitialSelect(string sql)
  {
    await using var host = await InsightCliTestHost.Start();
    var captured = await host.Sync("cache.sqlite", Start, Start);
    await Assert.That(captured.Exit).IsEqualTo(0).Because(captured.Errors);
    var result = await InsightCliTestHost.Run([
      "query",
      "--database",
      Path.Combine(host.Directory, "cache.sqlite"),
      "--sql",
      sql,
    ]);
    await Assert.That(result.Exit).IsEqualTo(1).Because(sql);
    using var metadata = await host.Query(
      "cache.sqlite",
      "SELECT value FROM metadata WHERE key='source'"
    );
    await Assert
      .That(metadata.RootElement[0].GetProperty("value").GetString())
      .IsEqualTo(host.Url + "/");
  }

  [Test]
  public async Task QueryExportsNumbersNullsEmptyStringsAndQuotedCsvAndAllowsSchemaInspection()
  {
    await using var host = await InsightCliTestHost.Start();
    var capture = await host.Sync("cache.sqlite", Start, Start);
    await Assert.That(capture.Exit).IsEqualTo(0).Because(capture.Errors);
    var file = Path.Combine(host.Directory, "cache.sqlite");
    const string sql =
      "SELECT 42 integer_value,1.25 real_value,NULL null_value,'' empty_value,'a,\"b\"' quoted_value,x'0102' blob_value";
    var json = await InsightCliTestHost.Run(["query", "--database", file, "--sql", sql]);
    await Assert.That(json.Exit).IsEqualTo(0).Because(json.Errors);
    using var rows = JsonDocument.Parse(json.Output);
    await Assert.That(rows.RootElement[0].GetProperty("integer_value").GetInt32()).IsEqualTo(42);
    await Assert.That(rows.RootElement[0].GetProperty("real_value").GetDouble()).IsEqualTo(1.25);
    await Assert
      .That(rows.RootElement[0].GetProperty("null_value").ValueKind)
      .IsEqualTo(JsonValueKind.Null);
    await Assert.That(rows.RootElement[0].GetProperty("blob_value").GetString()).IsEqualTo("AQI=");
    var csv = await InsightCliTestHost.Run([
      "query",
      "--database",
      file,
      "--sql",
      sql,
      "--format",
      "csv",
    ]);
    await Assert.That(csv.Exit).IsEqualTo(0).Because(csv.Errors);
    await Assert
      .That(csv.Output.Replace("\r\n", "\n"))
      .IsEqualTo(
        "integer_value,real_value,null_value,empty_value,quoted_value,blob_value\n42,1.25,,\"\",\"a,\"\"b\"\"\",AQI=\n"
      );
    using var schema = await host.Query("cache.sqlite", "PRAGMA table_info(events)");
    await Assert
      .That(
        schema.RootElement.EnumerateArray().Any(c => c.GetProperty("name").GetString() == "counter")
      )
      .IsTrue();
    var duplicate = await InsightCliTestHost.Run([
      "query",
      "--database",
      file,
      "--sql",
      "SELECT 1 value,2 value",
    ]);
    await Assert.That(duplicate.Exit).IsEqualTo(1);
    var multiple = await InsightCliTestHost.Run([
      "query",
      "--database",
      file,
      "--sql",
      "SELECT 1; SELECT 2",
    ]);
    await Assert.That(multiple.Exit).IsEqualTo(1);
  }

  [Test]
  public async Task SourceMismatchSchemaMismatchAndConcurrentWriterPreserveExistingEvidence()
  {
    await using var host = await InsightCliTestHost.Start();
    var capture = await host.Sync("cache.sqlite", Start, Start);
    await Assert.That(capture.Exit).IsEqualTo(0).Because(capture.Errors);
    var file = Path.Combine(host.Directory, "cache.sqlite");
    var before = await File.ReadAllBytesAsync(file);
    var mismatch = await InsightCliTestHost.Run([
      "sync",
      "--server",
      host.Url + "/different",
      "--database",
      file,
    ]);
    await Assert.That(mismatch.Exit).IsEqualTo(1);
    await Assert.That(mismatch.Errors).Contains("different source");
    await Assert.That((await File.ReadAllBytesAsync(file)).SequenceEqual(before)).IsTrue();
    using (
      var lease = new FileStream(
        file + ".lock",
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.None
      )
    )
    {
      var locked = await InsightCliSyncSpec.Resume(host, file);
      await Assert.That(locked.Exit).IsEqualTo(1);
      await Assert.That((await File.ReadAllBytesAsync(file)).SequenceEqual(before)).IsTrue();
    }
    using var connection = new SqliteConnection($"Data Source={file};Pooling=False");
    connection.Open();
    using var version = connection.CreateCommand();
    version.CommandText = "PRAGMA user_version=999";
    version.ExecuteNonQuery();
    var old = await File.ReadAllBytesAsync(file);
    var schema = await InsightCliSyncSpec.Resume(host, file);
    await Assert.That(schema.Exit).IsEqualTo(1);
    await Assert.That(schema.Errors).Contains("new database path");
    await Assert.That((await File.ReadAllBytesAsync(file)).SequenceEqual(old)).IsTrue();
  }

  [Test]
  [Arguments("sync")]
  [Arguments("extend")]
  [Arguments("material")]
  [Arguments("serial")]
  public async Task CheckpointConflictPreservesExistingEvidence(string operation)
  {
    var identity = Guid.NewGuid();
    using var repository = RepositoryConfig.InitializeMemoryDB(null, identity);
    await using var host = await InsightCliTestHost.Start(repository);
    using var db = repository.OpenConnection();
    var id = db.AllocateMaterialID("", "part", 1);
    db.RecordSerialForMaterialID(id, 1, "serial", Start.AddDays(-1));
    // Both window bounds and checkpoint comparisons must preserve sub-millisecond precision.
    var time = Start.AddTicks(1234567);
    db.RecordGeneralMessage([InsightCliSyncSpec.Material(id)], "note", "anchor", timeUTC: time);
    var capture = await host.Sync("cache.sqlite", time, time);
    await Assert.That(capture.Exit).IsEqualTo(0).Because(capture.Errors);
    var file = Path.Combine(host.Directory, "cache.sqlite");
    var original = await File.ReadAllBytesAsync(file);
    using var connection = new SqliteConnection(
      $"Data Source=file:${identity}?mode=memory&cache=shared"
    );
    connection.Open();
    using var change = connection.CreateCommand();
    change.CommandText = "UPDATE stations SET TimeUTC=TimeUTC+1 WHERE Counter=2";
    change.ExecuteNonQuery();
    ImmutableArray<string> args = operation switch
    {
      "sync" => ["sync"],
      "extend" => ["sync", "--to", time.AddDays(1).ToString("O")],
      "material" =>
      [
        "material",
        "--id",
        id.ToString(System.Globalization.CultureInfo.InvariantCulture),
      ],
      "serial" => ["material", "--serial", "serial"],
      _ => throw new ArgumentException("Unknown test operation."),
    };
    ImmutableArray<string> command = [.. args, "--server", host.Url, "--database", file];
    var failed = await InsightCliTestHost.Run(command);
    await Assert.That(failed.Exit).IsEqualTo(1);
    await Assert.That(failed.Errors).Contains("checkpoint");
    await Assert.That((await File.ReadAllBytesAsync(file)).SequenceEqual(original)).IsTrue();
    change.CommandText = "UPDATE stations SET TimeUTC=TimeUTC-1 WHERE Counter=2";
    change.ExecuteNonQuery();
    var retry = await InsightCliTestHost.Run(command);
    await Assert.That(retry.Exit).IsEqualTo(0).Because(retry.Errors);
  }

  [Test]
  public async Task HttpFailureAndCancellationKeepTheCacheResumableAndDiagnosticsOmitResponseBodies()
  {
    var fail = true;
    await using var host = await InsightCliTestHost.Start(beforeRequest: context =>
    {
      if (fail && context.Request.Path == "/api/v1/jobs/history")
        context.Response.StatusCode = 503;
    });
    var failed = await host.Sync("cache.sqlite", Start, Start.AddDays(1));
    await Assert.That(failed.Exit).IsEqualTo(1);
    await Assert.That(failed.Output).IsEqualTo("");
    await Assert.That(failed.Errors).Contains("503");
    fail = false;
    var retry = await InsightCliSyncSpec.Resume(host, "cache.sqlite");
    await Assert.That(retry.Exit).IsEqualTo(0).Because(retry.Errors);
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    var cancelled = await InsightCliSyncSpec.Resume(host, "cache.sqlite", cancel.Token);
    await Assert.That(cancelled.Exit).IsEqualTo(130);
  }

  [Test]
  public async Task LongSqlCanBeCancelledThroughTheSqliteProgressHandler()
  {
    await using var host = await InsightCliTestHost.Start();
    var capture = await host.Sync("cache.sqlite", Start, Start);
    await Assert.That(capture.Exit).IsEqualTo(0).Because(capture.Errors);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    var query = await InsightCliTestHost.Run(
      [
        "query",
        "--database",
        Path.Combine(host.Directory, "cache.sqlite"),
        "--sql",
        "WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<1000000000) SELECT SUM(x) FROM n",
      ],
      cancellation.Token
    );
    await Assert.That(query.Exit).IsEqualTo(130);
  }

  [Test]
  public async Task RealExecutableUsesBearerEnvironmentWithoutPersistingTheToken()
  {
    const string token = "test-token-only";
    var authenticated = false;
    await using var host = await InsightCliTestHost.Start(beforeRequest: context =>
    {
      if (context.Request.Headers.Authorization == "Bearer " + token)
        authenticated = true;
      else
        context.Response.StatusCode = 401;
    });
    var start = new ProcessStartInfo("dotnet")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    start.ArgumentList.Add(typeof(BlackMaple.FMSInsight.Cli.Program).Assembly.Location);
    foreach (
      var argument in new[]
      {
        "sync",
        "--server",
        host.Url,
        "--database",
        Path.Combine(host.Directory, "cache.sqlite"),
        "--from",
        Start.ToString("O"),
        "--to",
        Start.ToString("O"),
      }
    )
      start.ArgumentList.Add(argument);
    start.Environment["INSIGHT_TOKEN"] = token;
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var errors = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await process.WaitForExitAsync(timeout.Token);
    var diagnostics = await errors;
    await Assert.That(process.ExitCode).IsEqualTo(0).Because(diagnostics);
    await Assert.That(authenticated).IsTrue();
    await Assert.That(await output).IsEqualTo("");
    await Assert.That(diagnostics).DoesNotContain(token);
    using var metadata = await host.Query("cache.sqlite", "SELECT * FROM metadata");
    await Assert.That(metadata.RootElement.GetRawText()).DoesNotContain(token);
  }

  [Test]
  [Arguments("http://user:password@localhost/")]
  [Arguments("http://localhost/?token=secret")]
  [Arguments("http://localhost/#fragment")]
  [Arguments("file:///tmp/source")]
  public async Task ServerUrlsRejectEmbeddedCredentialsAndNonHttpSchemes(string server)
  {
    var result = await InsightCliTestHost.Run(["status", "--server", server]);
    await Assert.That(result.Exit).IsEqualTo(1);
    await Assert.That(result.Errors).DoesNotContain("password");
    await Assert.That(result.Errors).DoesNotContain("secret");
  }
}
