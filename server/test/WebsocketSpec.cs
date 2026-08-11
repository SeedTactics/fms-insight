/* Copyright (c) 2026, SeedTactics

All rights reserved.
*/

using System;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using BlackMaple.MachineFramework.Controllers;
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
    var status = new CurrentStatus
    {
      TimeOfCurrentStatusUTC = DateTime.UtcNow,
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = [],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty,
    };

    jobAndQueue.OnNewCurrentStatus += Raise.Event<NewCurrentStatus>(status);
  }

  private sealed class BlockingWebSocket : WebSocket
  {
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

    public override void Dispose() { }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(
      ArraySegment<byte> buffer,
      CancellationToken cancellationToken
    )
    {
      ReceiveStarted.TrySetResult(true);
      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
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
}
