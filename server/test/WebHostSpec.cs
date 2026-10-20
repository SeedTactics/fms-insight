using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BlackMaple.FMSInsight.Tests;

public sealed class WebHostSpec
{
  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task CustomControlComposesWithSharedWebHost(bool authenticated)
  {
    var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(directory);
    await File.WriteAllTextAsync(Path.Combine(directory, "index.html"), "<html>Insight</html>");
    await File.WriteAllTextAsync(
      Path.Combine(directory, "instruction.txt"),
      "Operator instructions"
    );
    try
    {
      using var repository = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
      var control = Substitute.For<IJobAndQueueControl>();
      control
        .When(c => c.CompleteBasketLoadStation("bad"))
        .Do(_ => throw new ArgumentException("Invalid work."));
      var server = authenticated
        ? new ServerSettings
        {
          OpenIDConnectAuthority = "https://identity.invalid",
          OpenIDConnectClientId = "client",
          AuthAuthority = "https://identity.invalid",
          AuthTokenAudiences = "audience",
        }
        : new ServerSettings();
      var fms = new FMSSettings
      {
        InstructionFilePath = directory,
        AdditionalLogServers = ["https://history.example"],
      };
      var builder = WebApplication.CreateBuilder(
        new WebApplicationOptions { WebRootPath = directory }
      );
      builder.WebHost.UseUrls("http://127.0.0.1:0");
      builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
      builder.Services.AddSingleton(repository);
      builder.Services.AddSingleton(control);
      builder.Services.AddFMSInsightWebServices(server, fms, hideStartupMessages: true);
      await using var app = builder.Build();
      app.UseFMSInsightMiddleware(fms, server);
      // Integration errors are handled inside the shared exception boundary.
      app.Use(
        async (context, next) =>
        {
          try
          {
            await next(context);
          }
          catch (ArgumentException error)
          {
            await Microsoft
              .AspNetCore.Http.Results.Problem(statusCode: 400, detail: error.Message)
              .ExecuteAsync(context);
          }
        }
      );
      app.MapFMSInsightEndpoints(server);
      await Assert
        .That(app.Services.GetRequiredService<IJobAndQueueControl>())
        .IsSameReferenceAs(control);
      await app.StartAsync();
      using var http = new HttpClient
      {
        BaseAddress = new Uri(System.Linq.Enumerable.Single(app.Urls)),
      };
      using var instruction = await http.GetAsync("/instructions/instruction.txt");
      await Assert
        .That(await instruction.Content.ReadAsStringAsync())
        .IsEqualTo("Operator instructions");
      using var fallback = await http.GetAsync("/station/1");
      await Assert
        .That(await fallback.Content.ReadAsStringAsync())
        .IsEqualTo("<html>Insight</html>");
      using var request = new HttpRequestMessage(
        HttpMethod.Get,
        "/api/v1/log/events/all?startUTC=20260101T000000Z&endUTC=20260102T000000Z"
      );
      request.Headers.Add("Origin", "https://history.example");
      request.Headers.AcceptEncoding.ParseAdd("gzip");
      using var response = await http.SendAsync(request);
      await Assert
        .That(response.StatusCode)
        .IsEqualTo(authenticated ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
      await Assert
        .That(response.Headers.GetValues("Access-Control-Allow-Origin"))
        .Contains("https://history.example");
      using var events = await http.GetAsync("/api/v1/events");
      await Assert.That(events.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
      if (!authenticated)
      {
        await Assert.That(response.Headers.Contains("Content-Security-Policy")).IsTrue();
        await Assert
          .That(response.Headers.GetValues("Cross-Origin-Opener-Policy"))
          .Contains("same-origin");
        await Assert
          .That(response.Headers.GetValues("Cross-Origin-Embedder-Policy"))
          .Contains("require-corp");

        await Assert.That(response.Content.Headers.ContentEncoding).Contains("gzip");
        using var invalid = await http.PostAsync(
          "/api/v1/jobs/basket-load-station/complete",
          new StringContent("{\"WorkId\":\"bad\"}", System.Text.Encoding.UTF8, "application/json")
        );
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert
          .That(invalid.Content.Headers.ContentType!.MediaType)
          .IsEqualTo("application/problem+json");
        await Assert.That(await invalid.Content.ReadAsStringAsync()).Contains("Invalid work.");
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(
          new UriBuilder(http.BaseAddress) { Scheme = "ws", Path = "/api/v1/events" }.Uri,
          CancellationToken.None
        );
        await Assert.That(socket.State).IsEqualTo(WebSocketState.Open);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
      }
      else
      {
        using var socket = new ClientWebSocket();
        await Assert
          .That(async () =>
            await socket.ConnectAsync(
              new UriBuilder(http.BaseAddress) { Scheme = "ws", Path = "/api/v1/events" }.Uri,
              CancellationToken.None
            )
          )
          .Throws<WebSocketException>();
      }
      await app.StopAsync();
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }
}
