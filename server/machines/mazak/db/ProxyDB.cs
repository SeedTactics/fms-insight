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
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MazakMachineInterface;

public class MazakProxyDB : IMazakDB, IDisposable
{
  private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MazakProxyDB>();

  private readonly HttpClient _client;
  private readonly Thread _longPollThread;
  private readonly CancellationTokenSource _cancelLongPoll = new();

  public MazakProxyDB(MazakConfig cfg)
  {
    _client = new HttpClient();
    _client.BaseAddress = cfg.ProxyDBUri;
    _longPollThread = new Thread(LongPollThread);
    _longPollThread.IsBackground = true;
    _longPollThread.Start();
  }

  public void Dispose()
  {
    _cancelLongPoll.Cancel();
    _longPollThread.Join(TimeSpan.FromSeconds(10));
    _client.Dispose();
  }

  public MazakAllData LoadAllData()
  {
    return Load<MazakAllData>("/all-data");
  }

  public MazakAllDataAndLogs LoadAllDataAndLogs(string maxForeignId)
  {
    return Post<string, MazakAllDataAndLogs>("/all-data-and-logs", maxForeignId);
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
    Post<string, bool>("/delete-logs", maxForeignId);
  }

  public void Save(MazakWriteData data)
  {
    Post<MazakWriteData, bool>("/save", data);
  }

  #region HTTP
  private async Task<T> LoadAsync<T>(string path)
  {
    var resp = await _client.GetAsync(path);
    if (resp.IsSuccessStatusCode)
    {
      // use DataContractJsonSerializer to deserialize, since that is what the proxy db uses
      var ser = new DataContractJsonSerializer(typeof(T));
      return (T)ser.ReadObject(await resp.Content.ReadAsStreamAsync());
    }
    else
    {
      var message = await resp.Content.ReadAsStringAsync();
      var lineIdx = message.IndexOfAny(['\n', '\r']);
      Log.Error("Failed to load {path} from proxy db: {status} {content}", path, resp.StatusCode, message);
      throw new Exception(
        "Failed to load from mazak proxy: " + message[..(lineIdx >= 0 ? lineIdx : message.Length)]
      );
    }
  }

  private T Load<T>(string path, CancellationToken? cancel = null)
  {
    var task = LoadAsync<T>(path);
    if (
      0
      == Task.WaitAny(
        [task],
        millisecondsTimeout: (int)TimeSpan.FromMinutes(1).TotalMilliseconds,
        cancellationToken: cancel ?? CancellationToken.None
      )
    )
    {
      return task.Result;
    }
    else
    {
      Log.Error("Timeout loading {path} from proxy db", path);
      throw new Exception("Timeout loading from mazak proxy");
    }
  }

  private async Task<R> PostAsync<T, R>(string path, T data)
  {
    var ser = new DataContractJsonSerializer(typeof(T));
    using var ms = new System.IO.MemoryStream();
    ser.WriteObject(ms, data);
    ms.Position = 0;
    var resp = await _client.PostAsync(
      path,
      new StreamContent(ms) { Headers = { { "Content-Type", "application/json" } } }
    );
    if (resp.IsSuccessStatusCode)
    {
      var serResp = new DataContractJsonSerializer(typeof(R));
      return (R)serResp.ReadObject(await resp.Content.ReadAsStreamAsync());
    }
    else
    {
      var msg = await resp.Content.ReadAsStringAsync();
      var lineIdx = msg.IndexOfAny(['\n', '\r']);
      Log.Error("Failed to post {path} to proxy db: {status} {content}", path, resp.StatusCode, msg);
      throw new Exception(
        "Failed to send data to mazak proxy: " + msg[..(lineIdx >= 0 ? lineIdx : msg.Length)]
      );
    }
  }

  private R Post<T, R>(string path, T data)
  {
    var task = PostAsync<T, R>(path, data);
    // long timeout, since downloading data can take a while
    if (Task.WaitAll([task], timeout: TimeSpan.FromMinutes(10)))
    {
      return task.Result;
    }
    else
    {
      Log.Error("Timeout posting {path} to proxy db", path);
      throw new Exception("Timeout communicating with mazak proxy");
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
        var evt = Load<bool>("/long-poll-events", cancel: _cancelLongPoll.Token);
        if (evt)
        {
          OnNewEvent?.Invoke();
        }
        // sleep for a bit
        Thread.Sleep(TimeSpan.FromSeconds(0.5));
      }
      catch (OperationCanceledException ex)
      {
        if (_cancelLongPoll.IsCancellationRequested)
        {
          return;
        }
        else
        {
          Log.Error(ex, "Unexpected cancellation when checking for new events in mazak proxy");
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error checking for new events in mazak proxy");
      }
    }
  }

  #endregion
}
