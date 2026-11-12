/* Copyright (c) 2026, SeedTactics

All rights reserved.
*/

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Mazak.Proxy;

namespace BlackMaple.FMSInsight.Tests.MazakProxy;

public sealed class HttpServerSpec
{
  [Test]
  public async Task HandlerFailureDoesNotStopListener()
  {
    var port = AvailablePort();
    using var server = new HttpServer($"http://127.0.0.1:{port}/");
    server.AddLoadingHandler<string>(
      "/failure",
      () => throw new InvalidOperationException("expected handler failure")
    );
    server.AddLoadingHandler("/healthy", () => "healthy");
    server.Start();
    using var client = Client();

    using var failed = await client.GetAsync($"http://127.0.0.1:{port}/failure");
    using var healthy = await client.GetAsync($"http://127.0.0.1:{port}/healthy");

    await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
    await Assert.That(failed.Headers.Contains("X-FMS-Insight-Request-ID")).IsTrue();
    await Assert
      .That(await failed.Content.ReadAsStringAsync())
      .DoesNotContain("expected handler failure");
    await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
    await Assert.That(await healthy.Content.ReadAsStringAsync()).IsEqualTo("\"healthy\"");
  }

  [Test]
  public async Task DisconnectedClientDoesNotStopListener()
  {
    var port = AvailablePort();
    using var server = new HttpServer($"http://127.0.0.1:{port}/");
    server.AddLoadingHandler("/large", () => new string('x', 4 * 1024 * 1024));
    server.AddLoadingHandler("/healthy", () => "healthy");
    server.Start();

    using (var disconnected = new TcpClient())
    {
      await disconnected.ConnectAsync(IPAddress.Loopback, port);
      var request = Encoding.ASCII.GetBytes(
        $"GET /large HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n"
      );
      await disconnected.GetStream().WriteAsync(request);
    }

    await Task.Delay(TimeSpan.FromMilliseconds(100));
    using var client = Client();
    using var healthy = await client.GetAsync($"http://127.0.0.1:{port}/healthy");

    await Assert.That(healthy.StatusCode).IsEqualTo(HttpStatusCode.OK);
  }

  [Test]
  public async Task DisposeWithPendingAcceptDoesNotEscapeCallback()
  {
    var port = AvailablePort();
    var server = new HttpServer($"http://127.0.0.1:{port}/");
    server.Start();

    server.Dispose();
    await Task.Delay(TimeSpan.FromMilliseconds(100));
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task DisconnectedSaveSettlesBeforeRecoveryReadsOrCompetingSaves(bool failAfterWrite)
  {
    var port = AvailablePort();
    var url = $"http://127.0.0.1:{port}";
    using var server = new HttpServer(url + "/");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var originalEntered = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    using var releaseOriginal = new ManualResetEventSlim();
    using var recoveryReadEntered = new ManualResetEventSlim();
    using var recoverySaveEntered = new ManualResetEventSlim();
    var controller = "empty";
    server.AddPostHandler<string, bool>(
      "/save",
      operation =>
      {
        if (operation == "original")
        {
          controller = "partial";
          originalEntered.TrySetResult();
          releaseOriginal.Wait(timeout.Token);
          controller = "settled";
          if (failAfterWrite)
            throw new InvalidOperationException(
              "Save finished applying before its response failed"
            );
        }
        else
        {
          recoverySaveEntered.Set();
          if (controller != "settled")
            throw new InvalidOperationException(
              "Competing save entered an unfinished native transaction"
            );
        }
        return true;
      }
    );
    server.AddLoadingHandler(
      "/all-data",
      () =>
      {
        recoveryReadEntered.Set();
        return controller;
      }
    );
    // Event waiting remains independent of the database operation gate.
    using var releasePoll = new ManualResetEventSlim();
    var pollEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    server.AddLoadingHandler(
      "/long-poll-events",
      () =>
      {
        pollEntered.TrySetResult();
        releasePoll.Wait(timeout.Token);
        return true;
      },
      allowConcurrent: true
    );
    server.Start();
    using var client = Client();
    // All phases share one deadline. A held handler must not release itself just as an HTTP
    // timeout expires, masking whether recovery really waited for the original operation.
    client.Timeout = Timeout.InfiniteTimeSpan;
    using var disconnected = new TcpClient();
    await disconnected.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
    var request = Encoding.ASCII.GetBytes(
      $"POST /save HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nContent-Type: application/json\r\nContent-Length: 10\r\nConnection: close\r\n\r\n\"original\""
    );
    await disconnected.GetStream().WriteAsync(request, timeout.Token);
    try
    {
      await originalEntered.Task.WaitAsync(timeout.Token);
      // Dropping the HTTP connection must not release the database operation gate.
      disconnected.Dispose();
      var read = client.GetAsync(url + "/all-data", timeout.Token);
      var save = client.PostAsync(
        url + "/save",
        new StringContent("\"recovery\"", Encoding.UTF8, "application/json"),
        timeout.Token
      );
      var poll = client.GetAsync(url + "/long-poll-events", timeout.Token);
      await pollEntered.Task.WaitAsync(timeout.Token);
      await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token);
      await Assert.That(recoveryReadEntered.IsSet).IsFalse();
      await Assert.That(recoverySaveEntered.IsSet).IsFalse();
      await Assert.That(read.IsCompleted).IsFalse();
      await Assert.That(save.IsCompleted).IsFalse();
      releaseOriginal.Set();
      // A long poll in progress must not hold up database recovery either.
      using var observed = await read;
      using var saved = await save;
      await Assert.That(observed.StatusCode).IsEqualTo(HttpStatusCode.OK);
      await Assert.That(await observed.Content.ReadAsStringAsync()).IsEqualTo("\"settled\"");
      await Assert.That(saved.StatusCode).IsEqualTo(HttpStatusCode.OK);
      await Assert.That(poll.IsCompleted).IsFalse();
      releasePoll.Set();
      using var polled = await poll;
      await Assert.That(polled.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
    finally
    {
      releaseOriginal.Set();
      releasePoll.Set();
    }
  }

  private static HttpClient Client() =>
    new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };

  private static int AvailablePort()
  {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
  }
}
