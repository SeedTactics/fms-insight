/* Copyright (c) 2024, John Lenz

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
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MazakMachineInterface;

public class MazakProxyDB : IMazakDB, IDisposable
{
  private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MazakProxyDB>();

  private readonly MazakConfig _cfg;
  private readonly Thread _longPollThread;
  private readonly CancellationTokenSource _cancelLongPoll = new();

  public MazakProxyDB(MazakConfig mazakCfg)
  {
    _cfg = mazakCfg;
    _longPollThread = new Thread(LongPollThread);
    _longPollThread.IsBackground = true;
    _longPollThread.Start();
  }

  public void Dispose()
  {
    _cancelLongPoll.Cancel();
    _longPollThread.Join(TimeSpan.FromSeconds(10));
  }

  public MazakAllData LoadAllData()
  {
    return Load<MazakAllData>("/all-data");
  }

  public MazakAllDataAndLogs LoadAllDataAndLogs(string maxForeignId)
  {
    return Post<string, MazakAllDataAndLogs>(
      "/all-data-and-logs",
      maxForeignId,
      timeout: TimeSpan.FromSeconds(30)
    );
  }

  public IEnumerable<MazakProgramRow> LoadPrograms()
  {
    return Load<IEnumerable<MazakProgramRow>>("/programs");
  }

  public IEnumerable<ToolPocketRow> LoadTools()
  {
    return Load<IEnumerable<ToolPocketRow>>("/tools");
  }

  public void DeleteLogs(string maxForeignId)
  {
    Post<string, bool>("/delete-logs", maxForeignId, TimeSpan.FromMinutes(2));
  }

  public void Save(MazakWriteData data)
  {
    Post<MazakWriteData, bool>("/save", data, timeout: TimeSpan.FromMinutes(8));
  }

  #region HTTP
  private Exception HandleAggregateException(AggregateException ex)
  {
    // Log the full error
    Log.Error(ex, "Error communicating with mazak proxy");

    // Make a more user-friendly error message to show as an alarm to the user
    string msg = "Error communicating with mazak proxy at " + _cfg.ProxyDBUri.ToString();
    foreach (var inner in ex.InnerExceptions)
    {
      switch (inner)
      {
        case TaskCanceledException _:
        case OperationCanceledException cancelEx:
          // TaskCanceled is a timeout from the HttpClient in GetAsync/PostAsync
          msg = $"Timeout communicating with mazak proxy at {_cfg.ProxyDBUri}";
          break;

        case HttpRequestException httpEx:
          // HttpRequestException is from HttpClient with some error communicating
          msg = $"Error communicating with mazak proxy at {_cfg.ProxyDBUri}: {httpEx.Message}";
          break;

        // other inner exceptions don't have a specific message (but the full details are logged to Serilog)
      }
    }

    return new Exception(msg);
  }

  private async Task<T> LoadAsync<T>(string path, CancellationToken cancel)
  {
    // use a new client each time because the proxy doesn't support pipelining... want a new connection each time
    using var client = new HttpClient();
    client.BaseAddress = _cfg.ProxyDBUri;
    client.Timeout = TimeSpan.FromMinutes(10);

    var resp = await client.GetAsync(path, cancellationToken: cancel);
    if (resp.IsSuccessStatusCode)
    {
      // use DataContractJsonSerializer to deserialize, since that is what the proxy db uses
      var ser = new DataContractJsonSerializer(typeof(T));
      return (T)ser.ReadObject(await resp.Content.ReadAsStreamAsync(cancellationToken: cancel));
    }
    else
    {
      var message = await resp.Content.ReadAsStringAsync(cancellationToken: cancel);
      var lineIdx = message.IndexOfAny(['\n', '\r']);
      Log.Error("Failed to load {path} from proxy db: {status} {content}", path, resp.StatusCode, message);
      throw new HttpRequestException(message[..(lineIdx >= 0 ? lineIdx : message.Length)]);
    }
  }

  private T Load<T>(string path, CancellationToken? cancel = null)
  {
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    CancellationToken token;
    if (cancel.HasValue)
    {
      token = cancel.Value;
    }
    else
    {
      token = cts.Token;
    }

    try
    {
      var task = LoadAsync<T>(path, token);
      // normally, cancel token will fire first.  Wait 10 minutes just in case
      task.Wait(TimeSpan.FromMinutes(10));
      return task.Result;
    }
    catch (AggregateException aggEx)
    {
      throw HandleAggregateException(aggEx);
    }
  }

  private async Task<R> PostAsync<T, R>(string path, T data, CancellationToken cancel)
  {
    // use a new client each time because the proxy doesn't support pipelining... want a new connection each time
    using var client = new HttpClient();
    client.BaseAddress = _cfg.ProxyDBUri;
    client.Timeout = TimeSpan.FromMinutes(10);

    var ser = new DataContractJsonSerializer(typeof(T));
    using var ms = new System.IO.MemoryStream();
    ser.WriteObject(ms, data);
    ms.Position = 0;
    var resp = await client.PostAsync(
      path,
      new StreamContent(ms) { Headers = { { "Content-Type", "application/json" } } },
      cancellationToken: cancel
    );
    if (resp.IsSuccessStatusCode)
    {
      var serResp = new DataContractJsonSerializer(typeof(R));
      return (R)serResp.ReadObject(await resp.Content.ReadAsStreamAsync(cancellationToken: cancel));
    }
    else
    {
      var msg = await resp.Content.ReadAsStringAsync(cancellationToken: cancel);
      var lineIdx = msg.IndexOfAny(['\n', '\r']);
      Log.Error("Failed to post {path} to proxy db: {status} {content}", path, resp.StatusCode, msg);
      throw new HttpRequestException(msg[..(lineIdx >= 0 ? lineIdx : msg.Length)]);
    }
  }

  private R Post<T, R>(string path, T data, TimeSpan? timeout = null)
  {
    // long timeout, since downloading data can take a while
    using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));

    try
    {
      var task = PostAsync<T, R>(path, data, cts.Token);
      // normally, cancel token will fire first.  Wait 10 minutes just in case
      task.Wait(TimeSpan.FromMinutes(10));
      return task.Result;
    }
    catch (AggregateException ex)
    {
      throw HandleAggregateException(ex);
    }
  }
  #endregion

  #region Long Poll

  public event Action OnNewEvent;

  private void LongPollThread()
  {
    while (_cancelLongPoll.IsCancellationRequested == false)
    {
      try
      {
        // proxy responds after 1.5 minutes
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
          timeoutSource.Token,
          _cancelLongPoll.Token
        );

        var evt = Load<bool>("/long-poll-events", cancel: linked.Token);
        if (evt)
        {
          OnNewEvent?.Invoke();
        }
        // sleep for a bit
        Thread.Sleep(TimeSpan.FromSeconds(1));
      }
      catch (Exception ex)
      {
        if (_cancelLongPoll.IsCancellationRequested)
        {
          return;
        }
        else
        {
          Log.Error(ex, "Error polling for events in mazak proxy");
        }
      }
    }
  }

  #endregion
}
