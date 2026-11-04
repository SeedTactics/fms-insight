using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Cli;

public static class Program
{
  private const string Usage = """
    insight-cli sync --server URL --database FILE [--from UTC --to UTC]
    insight-cli material --server URL --database FILE (--id NUMBER | --serial SERIAL)
    insight-cli query --database FILE [--sql SQL | --sql-file FILE] [--format json|csv]
    insight-cli status --server URL
    insight-cli program --server URL --name NAME [--revision NUMBER]
    insight-cli --version

    Sync requires --from and --to for a new database; later runs resume that window.
    A later --to extends coverage. Times must include Z or an explicit UTC offset.
    Material adds targeted history without advancing global event progress.
    SQL defaults to stdin and JSON output. Set INSIGHT_TOKEN for bearer authentication.
    Results go to stdout; diagnostics go to stderr. Check exit codes before using output.
    Inspect metadata, coverage and unresolved_job_references before analyzing cached history.
    Completed syncs do not prove complete material lifecycles or inspection outcomes.
    """;

  public static async Task<int> Main(string[] args)
  {
    Console.InputEncoding = new UTF8Encoding(false);
    Console.OutputEncoding = new UTF8Encoding(false);
    using var stopping = new CancellationTokenSource();
    ConsoleCancelEventHandler cancel = (_, e) =>
    {
      e.Cancel = true;
      stopping.Cancel();
    };
    Console.CancelKeyPress += cancel;
    try
    {
      return await Run(
        args.ToImmutableArray(),
        Console.In,
        Console.Out,
        Console.Error,
        stopping.Token
      );
    }
    finally
    {
      Console.CancelKeyPress -= cancel;
    }
  }

  public static async Task<int> Run(
    ImmutableArray<string> args,
    TextReader input,
    TextWriter output,
    TextWriter errors,
    CancellationToken cancellationToken = default
  )
  {
    try
    {
      if (
        args.Length == 0
        || args is ["--help"] or ["help"]
        || args.Length == 2 && args[1] == "--help"
      )
      {
        await output.WriteLineAsync(Usage);
        return 0;
      }
      if (args is ["--version"])
      {
        await output.WriteLineAsync(Version);
        return 0;
      }
      var options = ParseOptions(args.Skip(1).ToImmutableArray());
      string Required(string name) =>
        options.TryGetValue(name, out var value)
          ? value
          : throw new ArgumentException($"Missing --{name}.");
      void Allow(params string[] names)
      {
        foreach (var option in options.Keys)
          if (!names.Contains(option, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown option --{option}.");
      }

      switch (args[0])
      {
        case "sync":
          Allow("server", "database", "from", "to");
          var syncPath = Path.GetFullPath(Required("database"));
          int missing;
          using (var http = InsightHttp.Create(Required("server")))
            missing = await Sync.Run(
              http,
              syncPath,
              options.TryGetValue("from", out var syncFrom) ? ParseTime(syncFrom) : null,
              options.TryGetValue("to", out var syncTo) ? ParseTime(syncTo) : null,
              cancellationToken
            );
          await errors.WriteLineAsync($"Synchronized historical data in {syncPath}.");
          if (missing != 0)
            await errors.WriteLineAsync(
              $"{missing} referenced job plan(s) were unavailable; inspect unresolved_job_references."
            );
          break;
        case "material":
          Allow("server", "database", "id", "serial");
          if (options.ContainsKey("id") == options.ContainsKey("serial"))
            throw new ArgumentException("Use exactly one of --id or --serial.");
          long? materialId = null;
          if (options.TryGetValue("id", out var id))
          {
            if (
              !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId)
              || parsedId < 0
            )
              throw new ArgumentException("--id must be a nonnegative integer.");
            materialId = parsedId;
          }
          using (var http = InsightHttp.Create(Required("server")))
            await Sync.Material(
              http,
              Path.GetFullPath(Required("database")),
              materialId,
              options.GetValueOrDefault("serial"),
              cancellationToken
            );
          await errors.WriteLineAsync(
            "Retrieved targeted material history; global event progress is unchanged."
          );
          break;
        case "query":
          Allow("database", "sql", "sql-file", "format");
          if (options.ContainsKey("sql") && options.ContainsKey("sql-file"))
            throw new ArgumentException("Use either --sql or --sql-file.");
          var format = options.GetValueOrDefault("format", "json");
          if (format is not ("json" or "csv"))
            throw new ArgumentException("--format must be json or csv.");
          var sql =
            options.TryGetValue("sql", out var query) ? query
            : options.TryGetValue("sql-file", out var file)
              ? await File.ReadAllTextAsync(file, cancellationToken)
            : await input.ReadToEndAsync(cancellationToken);
          if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL is empty.");
          await Query.Run(Required("database"), sql, format, output, cancellationToken);
          break;
        case "status":
          Allow("server");
          using (var http = InsightHttp.Create(Required("server")))
          using (var status = await InsightHttp.Get(http, "api/v1/jobs/status", cancellationToken))
            await output.WriteLineAsync(status.RootElement.GetRawText());
          break;
        case "program":
          Allow("server", "name", "revision");
          var name = Uri.EscapeDataString(Required("name"));
          var revisionPath = "latest-revision";
          if (options.TryGetValue("revision", out var revision))
          {
            if (
              !long.TryParse(
                revision,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number
              )
              || number <= 0
            )
              throw new ArgumentException("--revision must be a positive integer.");
            revisionPath = $"revision/{number.ToString(CultureInfo.InvariantCulture)}";
          }
          using (var http = InsightHttp.Create(Required("server")))
          using (
            var content = await InsightHttp.Get(
              http,
              $"api/v1/machines/program/{name}/{revisionPath}/content",
              cancellationToken
            )
          )
            await output.WriteAsync(content.RootElement.GetString());
          break;
        default:
          throw new ArgumentException("Unknown command. Use --help for usage.");
      }
      return 0;
    }
    catch (Exception e)
      when (e is OperationCanceledException
        || e is SqliteException && cancellationToken.IsCancellationRequested
      )
    {
      await errors.WriteLineAsync("Cancelled or request timed out.");
      return 130;
    }
    catch (Exception e)
      when (e
          is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or HttpRequestException
            or JsonException
            or SqliteException
      )
    {
      await errors.WriteLineAsync(
        e is HttpRequestException httpError
        && httpError.StatusCode == System.Net.HttpStatusCode.Conflict
          ? "The server checkpoint conflicts with this cache (HTTP 409). Check the source and rebuild the disposable cache at a new path."
        : e is HttpRequestException httpErrorOther
          ? $"HTTP request failed{(httpErrorOther.StatusCode is { } code ? $" ({(int)code})" : "")}. Check the server address, access and authentication."
        : e.Message
      );
      return 1;
    }
  }

  internal static string Version =>
    typeof(Program)
      .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
      ?.InformationalVersion
    ?? typeof(Program).Assembly.GetName().Version?.ToString()
    ?? "unknown";

  private static ImmutableDictionary<string, string> ParseOptions(ImmutableArray<string> args)
  {
    var options = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2)
    {
      if (
        !args[i].StartsWith("--", StringComparison.Ordinal)
        || args[i].Length == 2
        || i + 1 >= args.Length
      )
        throw new ArgumentException("Options must be --name value pairs. Use --help for usage.");
      if (!options.TryAdd(args[i][2..], args[i + 1]))
        throw new ArgumentException($"Duplicate option {args[i]}.");
    }
    return options.ToImmutable();
  }

  private static DateTime ParseTime(string value)
  {
    // DateTimeOffset would otherwise interpret a missing zone using this workstation's time zone.
    var timeSeparator = value.IndexOf('T');
    var explicitZone =
      value.EndsWith('Z')
      || (
        timeSeparator >= 0
        && (value.LastIndexOf('+') > timeSeparator || value.LastIndexOf('-') > timeSeparator)
      );
    if (
      !explicitZone
      || !DateTimeOffset.TryParse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out var parsed
      )
    )
      throw new ArgumentException(
        "Times must be ISO 8601 timestamps with Z or an explicit offset."
      );
    return parsed.UtcDateTime;
  }
}
