#nullable enable

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BlackMaple.FMSInsight.Tests;

internal sealed class InsightCliTestHost : IAsyncDisposable
{
  public RepositoryConfig Repository { get; }
  public string Directory { get; }
  public string Url => app.Urls.Single();
  private readonly WebApplication app;
  private readonly bool ownsRepository;
  private ImmutableArray<(string Method, string Path, string Body)> requests = [];
  public ImmutableArray<(string Method, string Path, string Body)> Requests => requests;

  private InsightCliTestHost(
    RepositoryConfig repository,
    string directory,
    WebApplication application,
    bool ownsRepository
  )
  {
    Repository = repository;
    Directory = directory;
    app = application;
    this.ownsRepository = ownsRepository;
  }

  public static async Task<InsightCliTestHost> Start(
    RepositoryConfig? suppliedRepository = null,
    string? address = null,
    Action<HttpContext>? beforeRequest = null,
    Func<HttpContext, Task<bool>>? intercept = null
  )
  {
    var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    var webRoot = Path.Combine(directory, "wwwroot");
    System.IO.Directory.CreateDirectory(webRoot);
    var repository =
      suppliedRepository ?? RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = webRoot });
    builder.WebHost.UseUrls(address ?? "http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
    builder.Services.AddSingleton(repository);
    var jobControl = Substitute.For<IJobAndQueueControl>();
    builder.Services.AddSingleton(jobControl);
    var server = new ServerSettings();
    var fms = new FMSSettings();
    builder.Services.AddFMSInsightWebServices(server, fms, hideStartupMessages: true);
    var app = builder.Build();
    InsightCliTestHost? host = null;
    app.Use(
      async (context, next) =>
      {
        context.Request.EnableBuffering();
        using var body = new StreamReader(context.Request.Body, leaveOpen: true);
        var content = await body.ReadToEndAsync();
        context.Request.Body.Position = 0;
        ImmutableInterlocked.Update(
          ref host!.requests,
          requests =>
            requests.Add(
              (context.Request.Method, context.Request.Path + context.Request.QueryString, content)
            )
        );
        beforeRequest?.Invoke(context);
        if (context.Response.StatusCode != 200)
          return;
        if (intercept is not null && await intercept(context))
          return;
        await next(context);
      }
    );
    app.UseFMSInsightMiddleware(fms, server);
    app.MapFMSInsightEndpoints(server);
    try
    {
      host = new InsightCliTestHost(repository, directory, app, suppliedRepository is null);
      await app.StartAsync();
      return host;
    }
    catch
    {
      await app.DisposeAsync();
      if (suppliedRepository is null)
        repository.Dispose();
      System.IO.Directory.Delete(directory, recursive: true);
      throw;
    }
  }

  public Task<(int Exit, string Output, string Errors)> Sync(
    string filename,
    DateTime from,
    DateTime to
  ) =>
    Run([
      "sync",
      "--server",
      Url,
      "--database",
      Path.Combine(Directory, filename),
      "--from",
      from.ToString("O"),
      "--to",
      to.ToString("O"),
    ]);

  public async Task<JsonDocument> Query(string filename, string sql)
  {
    var result = await Run([
      "query",
      "--database",
      Path.Combine(Directory, filename),
      "--sql",
      sql,
    ]);
    await Assert.That(result.Exit).IsEqualTo(0).Because(result.Errors);
    return JsonDocument.Parse(result.Output);
  }

  internal void ExecuteCacheSql(string filename, string sql)
  {
    // These tests exercise the CLI's cache files. Release SQLite's Windows file handles before
    // running another command or checking that a rejected operation preserved the file bytes.
    using var connection = new SqliteConnection(
      new SqliteConnectionStringBuilder
      {
        DataSource = Path.Combine(Directory, filename),
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false,
      }.ToString()
    );
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
  }

  internal static async Task<(int Exit, string Output, string Errors)> Run(
    ImmutableArray<string> arguments,
    CancellationToken cancellationToken = default
  )
  {
    using var input = new StringReader("");
    using var output = new StringWriter();
    using var errors = new StringWriter();
    var result = await BlackMaple.FMSInsight.Cli.Program.Run(
      arguments,
      input,
      output,
      errors,
      cancellationToken
    );
    return (result, output.ToString(), errors.ToString());
  }

  public async ValueTask DisposeAsync()
  {
    await app.DisposeAsync();
    if (ownsRepository)
      Repository.Dispose();
    System.IO.Directory.Delete(Directory, recursive: true);
  }
}
