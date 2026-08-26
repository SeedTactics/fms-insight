/* Copyright (c) 2026, SeedTactics

All rights reserved.
*/

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
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

  private static HttpClient Client() =>
    new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };

  private static int AvailablePort()
  {
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
  }
}
