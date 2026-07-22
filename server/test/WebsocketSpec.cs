/* Copyright (c) 2026, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using BlackMaple.MachineFramework.Controllers;
using NSubstitute;

namespace BlackMaple.FMSInsight.Tests;

public sealed class WebsocketSpec
{
  private sealed class RecordingWebSocket : WebSocket
  {
    private readonly TaskCompletionSource _closeRequested = new(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();
    private WebSocketCloseStatus? _closeStatus;
    private string _closeStatusDescription;
    private WebSocketState _state = WebSocketState.Open;

    public ValueTask<string> NextMessage(CancellationToken cancellationToken) =>
      _messages.Reader.ReadAsync(cancellationToken);

    public override WebSocketCloseStatus? CloseStatus => _closeStatus;
    public override string CloseStatusDescription => _closeStatusDescription;
    public override WebSocketState State => _state;
    public override string SubProtocol => null;

    public void RequestClose() => _closeRequested.TrySetResult();

    public override void Abort()
    {
      _state = WebSocketState.Aborted;
      RequestClose();
    }

    public override Task CloseAsync(
      WebSocketCloseStatus closeStatus,
      string statusDescription,
      CancellationToken cancellationToken
    )
    {
      _closeStatus = closeStatus;
      _closeStatusDescription = statusDescription;
      _state = WebSocketState.Closed;
      return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(
      WebSocketCloseStatus closeStatus,
      string statusDescription,
      CancellationToken cancellationToken
    )
    {
      _closeStatus = closeStatus;
      _closeStatusDescription = statusDescription;
      _state = WebSocketState.CloseSent;
      return Task.CompletedTask;
    }

    public override void Dispose()
    {
      _state = WebSocketState.Closed;
      RequestClose();
    }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(
      ArraySegment<byte> buffer,
      CancellationToken cancellationToken
    )
    {
      await _closeRequested.Task.WaitAsync(cancellationToken);
      _state = WebSocketState.CloseReceived;
      return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
    }

    public override Task SendAsync(
      ArraySegment<byte> buffer,
      WebSocketMessageType messageType,
      bool endOfMessage,
      CancellationToken cancellationToken
    )
    {
      _messages.Writer.TryWrite(Encoding.UTF8.GetString(buffer));
      return Task.CompletedTask;
    }
  }

  [Test]
  public async Task InitialConnectionReceivesCurrentStatusAndCustomState()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var jobControl = Substitute.For<IJobAndQueueControl>();
    jobControl
      .GetCurrentStatusAndCustomState()
      .Returns(
        new CurrentStatusAndCustomState
        {
          CurrentStatus = EmptyCurrentStatus(),
          CustomState = JsonSerializer.SerializeToElement(new { Schema = "custom", Version = 2 }),
        }
      );

    await using var manager = new WebsocketManager(repository, jobControl);
    using var socket = new RecordingWebSocket();
    var handling = manager.HandleWebsocket(socket);

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var message = JsonDocument.Parse(await socket.NextMessage(timeout.Token));
    await Assert.That(message.RootElement.TryGetProperty("NewCurrentStatus", out _)).IsTrue();
    await Assert
      .That(message.RootElement.GetProperty("CustomState").GetProperty("Schema").GetString())
      .IsEqualTo("custom");
    await Assert
      .That(message.RootElement.GetProperty("CustomState").GetProperty("Version").GetInt32())
      .IsEqualTo(2);

    socket.RequestClose();
    await handling;
  }

  [Test]
  public async Task InitialConnectionOmitsCustomStateWithoutAProjection()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var jobControl = Substitute.For<IJobAndQueueControl>();
    jobControl
      .GetCurrentStatusAndCustomState()
      .Returns(new CurrentStatusAndCustomState { CurrentStatus = EmptyCurrentStatus() });

    await using var manager = new WebsocketManager(repository, jobControl);
    using var socket = new RecordingWebSocket();
    var handling = manager.HandleWebsocket(socket);

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var message = JsonDocument.Parse(await socket.NextMessage(timeout.Token));
    await Assert.That(message.RootElement.TryGetProperty("NewCurrentStatus", out _)).IsTrue();
    await Assert.That(message.RootElement.TryGetProperty("CustomState", out _)).IsFalse();

    socket.RequestClose();
    await handling;
  }

  [Test]
  public async Task InitialSnapshotCannotOverwriteNewerPublishedState()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var jobControl = Substitute.For<IJobAndQueueControl>();
    using var getterEntered = new ManualResetEventSlim();
    using var allowGetterToReturn = new ManualResetEventSlim();
    using var publicationStarted = new ManualResetEventSlim();
    var initialSnapshot = SnapshotWithVersion(1);
    var newerSnapshot = SnapshotWithVersion(2);
    jobControl
      .GetCurrentStatusAndCustomState()
      .Returns(_ =>
      {
        getterEntered.Set();
        allowGetterToReturn.Wait();
        return initialSnapshot;
      });
    jobControl.OnNewCurrentStatusAndCustomState += _ => publicationStarted.Set();

    await using var manager = new WebsocketManager(repository, jobControl);
    using var socket = new RecordingWebSocket();
    var handling = Task.Run(() => manager.HandleWebsocket(socket));
    await Assert.That(getterEntered.Wait(TimeSpan.FromSeconds(5))).IsTrue();

    var publish = Task.Run(() =>
      jobControl.OnNewCurrentStatusAndCustomState += Raise.Event<
        Action<CurrentStatusAndCustomState>
      >(newerSnapshot)
    );
    await Assert.That(publicationStarted.Wait(TimeSpan.FromSeconds(5))).IsTrue();
    allowGetterToReturn.Set();

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var initialMessage = JsonDocument.Parse(await socket.NextMessage(timeout.Token));
    using var newerMessage = JsonDocument.Parse(await socket.NextMessage(timeout.Token));
    await publish;
    await Assert.That(CustomStateVersion(initialMessage)).IsEqualTo(1);
    await Assert.That(CustomStateVersion(newerMessage)).IsEqualTo(2);

    socket.RequestClose();
    await handling;
  }

  private static CurrentStatusAndCustomState SnapshotWithVersion(int version) =>
    new()
    {
      CurrentStatus = EmptyCurrentStatus(),
      CustomState = JsonSerializer.SerializeToElement(new { Version = version }),
    };

  private static int CustomStateVersion(JsonDocument message) =>
    message.RootElement.GetProperty("CustomState").GetProperty("Version").GetInt32();

  private static CurrentStatus EmptyCurrentStatus() =>
    new()
    {
      TimeOfCurrentStatusUTC = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc),
      Jobs = System.Collections.Immutable.ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = System.Collections.Immutable.ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = [],
      Queues = System.Collections.Immutable.ImmutableDictionary<string, QueueInfo>.Empty,
    };
}
