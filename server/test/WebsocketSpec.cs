/* Copyright (c) 2026, SeedTactics

All rights reserved.
*/

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using BlackMaple.MachineFramework.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BlackMaple.FMSInsight.Tests;

public sealed class WebsocketSpec
{
  [Test]
  public async Task ActiveWebsocketStopsWhenApplicationStops()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var jobAndQueue = Substitute.For<IJobAndQueueControl>();
    await using var manager = new WebsocketManager(repository, jobAndQueue);
    using var stopping = new CancellationTokenSource();
    var socket = new BlockingWebSocket();
    var handling = manager.HandleWebsocket(socket, stopping.Token);

    await socket.ReceiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    stopping.Cancel();
    await handling.WaitAsync(TimeSpan.FromSeconds(1));

    await Assert.That(socket.Closed).IsTrue();
    await Assert.That(socket.Aborted).IsFalse();
  }

  [Test]
  public async Task CurrentStatusPublishedAfterShutdownIsIgnored()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var jobAndQueue = Substitute.For<IJobAndQueueControl>();
    var manager = new WebsocketManager(repository, jobAndQueue);
    await manager.DisposeAsync();
    jobAndQueue.OnNewCurrentStatus += Raise.Event<NewCurrentStatus>(Status());
  }

  [Test]
  public async Task StalledClientDoesNotDelayHealthyClientAndIsRemoved()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var jobAndQueue = Substitute.For<IJobAndQueueControl>();
    await using var manager = new WebsocketManager(
      repository,
      jobAndQueue,
      clientSendTimeout: TimeSpan.FromSeconds(1)
    );
    using var stopping = new CancellationTokenSource();
    var stalled = new StalledSendWebSocket();
    var healthy = new RecordingWebSocket();
    var stalledHandling = manager.HandleWebsocket(stalled, stopping.Token);
    var healthyHandling = manager.HandleWebsocket(healthy, stopping.Token);
    await Task.WhenAll(stalled.ReceiveStarted.Task, healthy.ReceiveStarted.Task)
      .WaitAsync(TimeSpan.FromSeconds(1));

    jobAndQueue.OnNewCurrentStatus += Raise.Event<NewCurrentStatus>(Status());

    await stalled.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await healthy.FirstSend.Task.WaitAsync(TimeSpan.FromMilliseconds(500));
    jobAndQueue.OnNewCurrentStatus += Raise.Event<NewCurrentStatus>(Status());
    await healthy.SecondSend.Task.WaitAsync(TimeSpan.FromMilliseconds(500));
    await Assert.That(stalled.Aborted).IsFalse();
    await stalled.AbortCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await stalledHandling.WaitAsync(TimeSpan.FromSeconds(1));

    stopping.Cancel();
    await healthyHandling.WaitAsync(TimeSpan.FromSeconds(1));
    await Assert.That(stalled.Aborted).IsTrue();
    await Assert.That(healthy.Aborted).IsFalse();
    await Assert.That(healthy.SendCount).IsEqualTo(2);
    await Assert.That(healthy.Messages).All(message => message.Contains("NewCurrentStatus"));
  }

  [Test]
  public async Task OverflowDisconnectsOnlyTheStalledConnection()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var control = Substitute.For<IJobAndQueueControl>();
    await using var manager = new WebsocketManager(repository, control, TimeSpan.FromSeconds(30));
    using var stopping = new CancellationTokenSource();
    var stalled = new StalledSendWebSocket();
    var healthy = new RecordingWebSocket();
    var handling = manager.HandleWebsocket(stalled, stopping.Token);
    var healthyHandling = manager.HandleWebsocket(healthy, stopping.Token);
    await Task.WhenAll(stalled.ReceiveStarted.Task, healthy.ReceiveStarted.Task);
    control.OnNewCurrentStatus += Raise.Event<NewCurrentStatus>(Status());
    await stalled.SendStarted.Task;
    // Let the healthy sender catch up each time; overflow is local to the stalled peer.
    for (var i = 0; i < 110; i++)
    {
      control.OnNewCurrentStatus += Raise.Event<NewCurrentStatus>(Status());
      var expected = i + 2;
      await WaitFor(() => healthy.SendCount == expected);
    }
    await stalled.AbortCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await handling.WaitAsync(TimeSpan.FromSeconds(1));
    await Assert.That(healthy.Aborted).IsFalse();
    await Assert.That(healthy.SendCount).IsEqualTo(111);
    stopping.Cancel();
    await healthyHandling;
  }

  [Test]
  public async Task ShutdownFinishesAStalledSender()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var control = Substitute.For<IJobAndQueueControl>();
    var manager = new WebsocketManager(repository, control, TimeSpan.FromSeconds(30));
    var socket = new StalledSendWebSocket();
    var handling = manager.HandleWebsocket(socket, CancellationToken.None);
    await socket.ReceiveStarted.Task;
    control.OnNewCurrentStatus += Raise.Event<NewCurrentStatus>(Status());
    await socket.SendStarted.Task;
    await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    await handling.WaitAsync(TimeSpan.FromSeconds(1));
    await Assert.That(socket.Aborted).IsTrue();
  }

  [Test]
  public async Task RealSocketReceivesBurstInOrder()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var control = Substitute.For<IJobAndQueueControl>();
    await using var manager = new WebsocketManager(repository, control);
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
    await using var app = builder.Build();
    app.UseWebSockets();
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    app.Run(async context =>
    {
      using var ws = await context.WebSockets.AcceptWebSocketAsync();
      var handling = manager.HandleWebsocket(ws, context.RequestAborted);
      connected.TrySetResult();
      await handling;
    });
    await app.StartAsync();
    var address = app
      .Services.GetRequiredService<IServer>()
      .Features.Get<IServerAddressesFeature>()!
      .Addresses.Single();
    using var socket = new ClientWebSocket();
    await socket.ConnectAsync(new Uri(address.Replace("http:", "ws:")), CancellationToken.None);
    await connected.Task;
    var initial = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    for (var i = 0; i < 50; i++)
      control.OnNewCurrentStatus += Raise.Event<NewCurrentStatus>(
        Status() with
        {
          TimeOfCurrentStatusUTC = initial.AddSeconds(i),
        }
      );
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var buffer = new byte[16384];
    for (var i = 0; i < 50; i++)
    {
      var length = 0;
      WebSocketReceiveResult result;
      do
      {
        result = await socket.ReceiveAsync(
          new ArraySegment<byte>(buffer, length, buffer.Length - length),
          timeout.Token
        );
        length += result.Count;
      } while (!result.EndOfMessage);
      using var json = JsonDocument.Parse(buffer.AsMemory(0, length));
      await Assert
        .That(
          json.RootElement.GetProperty("NewCurrentStatus")
            .GetProperty("TimeOfCurrentStatusUTC")
            .GetDateTime()
        )
        .IsEqualTo(initial.AddSeconds(i));
    }
    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "finished", timeout.Token);
    await app.StopAsync(timeout.Token);
  }

  private static async Task WaitFor(Func<bool> predicate)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    while (!predicate())
      await Task.Delay(1, timeout.Token);
  }

  private static CurrentStatus Status() =>
    new()
    {
      TimeOfCurrentStatusUTC = DateTime.UtcNow,
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = [],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty,
    };

  private class BlockingWebSocket : WebSocket
  {
    private readonly CancellationTokenSource _aborted = new();

    public TaskCompletionSource<bool> ReceiveStarted { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Aborted { get; private set; }
    public bool Closed { get; private set; }

    public override WebSocketCloseStatus? CloseStatus => null;
    public override string CloseStatusDescription => null;
    public override WebSocketState State => Aborted ? WebSocketState.Aborted : WebSocketState.Open;
    public override string SubProtocol => null;

    public override void Abort()
    {
      Aborted = true;
      _aborted.Cancel();
    }

    public override Task CloseAsync(
      WebSocketCloseStatus closeStatus,
      string statusDescription,
      CancellationToken cancellationToken
    )
    {
      Closed = true;
      return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(
      WebSocketCloseStatus closeStatus,
      string statusDescription,
      CancellationToken cancellationToken
    )
    {
      return Task.CompletedTask;
    }

    public override void Dispose() => _aborted.Dispose();

    public override async Task<WebSocketReceiveResult> ReceiveAsync(
      ArraySegment<byte> buffer,
      CancellationToken cancellationToken
    )
    {
      ReceiveStarted.TrySetResult(true);
      using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        _aborted.Token
      );
      try
      {
        await Task.Delay(Timeout.InfiniteTimeSpan, receiveCancellation.Token);
      }
      catch (OperationCanceledException) when (_aborted.IsCancellationRequested)
      {
        throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
      }
      throw new InvalidOperationException(
        "The blocking receive should only end through cancellation."
      );
    }

    public override Task SendAsync(
      ArraySegment<byte> buffer,
      WebSocketMessageType messageType,
      bool endOfMessage,
      CancellationToken cancellationToken
    ) => Task.CompletedTask;
  }

  private sealed class StalledSendWebSocket : BlockingWebSocket
  {
    public TaskCompletionSource<bool> SendStarted { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> AbortCalled { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override void Abort()
    {
      base.Abort();
      AbortCalled.TrySetResult(true);
    }

    public override async Task SendAsync(
      ArraySegment<byte> buffer,
      WebSocketMessageType messageType,
      bool endOfMessage,
      CancellationToken cancellationToken
    )
    {
      SendStarted.TrySetResult(true);
      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
  }

  private sealed class RecordingWebSocket : BlockingWebSocket
  {
    private ImmutableList<string> _messages = [];

    public TaskCompletionSource<bool> FirstSend { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> SecondSend { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int SendCount => _messages.Count;
    public ImmutableList<string> Messages => _messages;

    public override Task SendAsync(
      ArraySegment<byte> buffer,
      WebSocketMessageType messageType,
      bool endOfMessage,
      CancellationToken cancellationToken
    )
    {
      _messages = _messages.Add(
        Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count)
      );
      if (_messages.Count == 1)
        FirstSend.TrySetResult(true);
      if (_messages.Count == 2)
        SecondSend.TrySetResult(true);
      return Task.CompletedTask;
    }
  }
}
