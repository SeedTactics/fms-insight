/* Copyright (c) 2018, John Lenz

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
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlackMaple.MachineFramework.Controllers
{
  public record ServerEvent
  {
    public LogEntry? LogEntry { get; init; }

    public NewJobs? NewJobs { get; init; }

    public CurrentStatus? NewCurrentStatus { get; init; }

    public EditMaterialInLogEvents? EditMaterialInLog { get; init; }
  }

  public sealed class WebsocketManager : IAsyncDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<WebsocketManager>();

    private class ServerClosingException : Exception { }

    private class WebsocketDict
    {
      private System.Threading.Lock _lock = new();
      private Dictionary<Guid, WebSocket> _sockets = new Dictionary<Guid, WebSocket>();

      public List<WebSocket> AllSockets()
      {
        lock (_lock)
        {
          return _sockets.Values.ToList();
        }
      }

      public List<WebSocket> Clear()
      {
        lock (_lock)
        {
          var sockets = _sockets.Values.ToList();
          _sockets = new();
          return sockets;
        }
      }

      public void Add(Guid guid, WebSocket ws)
      {
        lock (_lock)
        {
          if (_sockets == null)
          {
            throw new ServerClosingException();
          }
          _sockets.Add(guid, ws);
        }
      }

      public void Remove(Guid guid)
      {
        lock (_lock)
        {
          if (_sockets != null)
            _sockets.Remove(guid);
        }
      }
    }

    private readonly WebsocketDict _sockets = new WebsocketDict();
    private readonly JsonSerializerOptions _serSettings;
    private readonly System.Collections.Concurrent.BlockingCollection<ServerEvent> _messages;
    private readonly Thread _thread;

    // Injecting IJobAndQueueControl here ensures that logging starts as soon as FMS Insight starts
    public WebsocketManager(RepositoryConfig repo, IJobAndQueueControl jobAndQueue)
    {
      _serSettings = new JsonSerializerOptions();
      FMSInsightWebHost.JsonSettings(_serSettings);

      _messages = new System.Collections.Concurrent.BlockingCollection<ServerEvent>(100);
      _thread = new System.Threading.Thread(SendThread);
      _thread.IsBackground = true;
      _thread.Start();

      repo.NewLogEntry += (e, foreignId, db) => Send(new ServerEvent() { LogEntry = e });
      jobAndQueue.OnNewJobs += (jobs) =>
        Send(new ServerEvent() { NewJobs = jobs with { Programs = null, DebugMessage = null } });
      jobAndQueue.OnNewCurrentStatus += (status) => Send(new ServerEvent() { NewCurrentStatus = status });
      jobAndQueue.OnEditMaterialInLog += (o) => Send(new ServerEvent() { EditMaterialInLog = o });
    }

    private void Send(ServerEvent val)
    {
      if (!_messages.TryAdd(val, TimeSpan.FromSeconds(1)))
      {
        Log.Error("Unable to add server event {@val} to outgoing websocket messages", val);
      }
    }

    private void SendThread()
    {
      while (!_messages.IsCompleted)
      {
        ServerEvent msg;
        try
        {
          msg = _messages.Take();
        }
        catch (InvalidOperationException)
        {
          // The InvalidOperationException is thrown when the BlockingCollection is completed, so just exit
          return;
        }

        var data = JsonSerializer.Serialize(msg, _serSettings);
        var encoded = System.Text.Encoding.UTF8.GetBytes(data);
        var buffer = new ArraySegment<Byte>(encoded, 0, encoded.Length);

        var sockets = _sockets.AllSockets();
        foreach (var ws in sockets)
        {
          if (ws.CloseStatus.HasValue)
            continue;
          ws.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
        }
      }
    }

    public async Task HandleWebsocket(WebSocket ws)
    {
      var buffer = new byte[1024 * 4];
      var guid = Guid.NewGuid();
      try
      {
        _sockets.Add(guid, ws);

        var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        while (res.MessageType != WebSocketMessageType.Close)
        {
          //process client to server messages here.  Currently there are no messages from the client to the server.

          res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        }
      }
      catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
      {
        //do nothing, just exit the loop
      }
      catch (ServerClosingException)
      {
        await ws.CloseOutputAsync(
          WebSocketCloseStatus.NormalClosure,
          "Server is closing",
          CancellationToken.None
        );
      }
      finally
      {
        _sockets.Remove(guid);
      }

      if (ws.CloseStatus.HasValue)
      {
        await ws.CloseAsync(ws.CloseStatus.Value, ws.CloseStatusDescription, CancellationToken.None);
      }
    }

    private bool _disposed = false;

    public async ValueTask DisposeAsync()
    {
      if (_disposed)
        return;
      _disposed = true;

      var tasks = new List<Task>();
      var sockets = _sockets.Clear();

      _messages.CompleteAdding();

      foreach (var ws in sockets)
      {
        var tokenSource = new CancellationTokenSource();
        var closeTask = ws.CloseOutputAsync(
          WebSocketCloseStatus.NormalClosure,
          "Server is stopping",
          tokenSource.Token
        );
        var cancelTask = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => tokenSource.Cancel());
        tasks.Add(Task.WhenAny(closeTask, cancelTask));
      }

      await Task.WhenAll(tasks);
    }
  }
}
