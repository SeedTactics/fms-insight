/* Copyright (c) 2019, John Lenz

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
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cincron
{

  public class MessageResponse
  {
    public IReadOnlyList<string> RawLines { get; set; }
    public IReadOnlyDictionary<string, string> Fields { get; set; }
  }

  public delegate void UnsolicitedResponse(MessageResponse r);

  public interface IPlantHostCommunication
  {
    event UnsolicitedResponse OnUnsolicitiedResponse;
    Task<MessageResponse> Send(string cmd);
    int Port { get; }
  }

  public class PlantHostCommunication : IPlantHostCommunication, IDisposable
  {
    public event UnsolicitedResponse OnUnsolicitiedResponse;



    #region Communication
    private readonly object _sendLock = new object();
    private StreamWriter _send;
    private TcpListener _listener;
    private bool _exiting = false;
    private readonly Dictionary<int, TaskCompletionSource<MessageResponse>> _inFlightMsgs = new Dictionary<int, TaskCompletionSource<MessageResponse>>();
    private int _lastUsedTrans = 1;
    public async Task<MessageResponse> Send(string cmd)
    {

      var comp = new TaskCompletionSource<MessageResponse>();
      lock (_sendLock)
      {
        if (_send == null)
        {
          throw new Exception("Unable to communicate with cell controller");
        }

        _lastUsedTrans += 1;
        if (_lastUsedTrans >= 5000)
        {
          _lastUsedTrans = 1;
        }
        lock (_inFlightMsgs)
        {
          if (_inFlightMsgs.ContainsKey(_lastUsedTrans))
          {
            Log.Error("Too many outstanding commands to cell controller");
            throw new Exception("Too many outstanding commands to cell controller");
          }

          _inFlightMsgs.Add(_lastUsedTrans, comp);
        }

        Log.Debug("Sending command {cmd} with trans number {trans}", cmd, _lastUsedTrans);
        _send.Write(cmd + " -t " + _lastUsedTrans.ToString() + "\n");
        _send.Flush();

      }

      var resp = await comp.Task;
      if (resp.Fields.ContainsKey("statu") && resp.Fields["statu"] != "SUCCESS")
      {
        Log.Warning("Received error from cincron cell {@response}", resp);
        throw new Exception("Received error from cincron cell controller");
      }

      return resp;
    }

    private class SocketClosed : Exception { }


    private void ReadResponseMessage(StreamReader r)
    {
      var lines = new List<string>();
      var curLine = new System.Text.StringBuilder();
      var lastWasNewline = false;

      //read until double-newline
      while (true)
      {
        int i = r.Read();
        if (i < 0)
        {
          throw new SocketClosed();
        }
        char c = (char)i;

        if (c == '\n' && lastWasNewline)
        {
          break;
        }
        else if (c == '\n')
        {
          lastWasNewline = true;
          lines.Add(curLine.ToString());
          curLine.Clear();
        }
        else
        {
          lastWasNewline = false;
          curLine.Append(c);
        }
      }

      // process response
      var fields = new Dictionary<string, string>();
      foreach (var line in lines)
      {
        int i = line.IndexOf(":");
        if (i > 0 && i < line.Length - 2)
        {
          fields[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
        }
      }

      // ignore ackhs
      if (fields.ContainsKey("ackhs"))
      {
        Log.Debug("Received ackhs message: {lines}, {fields}", lines, fields);
        return;
      }

      // check and extract some common fields
      if (!fields.ContainsKey("trans"))
      {
        Log.Error("Response from cincron did not contain transaction! {lines} {fields}", lines, fields);
        return;
      }
      if (!int.TryParse(fields["trans"], out int transNumber))
      {
        Log.Error("Unable to parse transaction number as integer: {lines} {fields}", lines, fields);
        return;
      }
      if (!fields.ContainsKey("respn"))
      {
        Log.Error("Response from cincron did not contain response type! {lines} {fields}", lines, fields);
        return;
      }
      string respn = fields["respn"];

      // send an ack
      lock (_sendLock)
      {
        _send.Write("ackcel -r " + respn + " -t " + transNumber.ToString() + " -s SUCCESS\n");
        _send.Flush();
      }

      //create parsed response
      var resp = new MessageResponse()
      {
        RawLines = lines,
        Fields = fields
      };

      if (transNumber >= 5000)
      {
        OnUnsolicitiedResponse?.Invoke(resp);
      }
      else
      {
        lock (_inFlightMsgs)
        {
          if (_inFlightMsgs.ContainsKey(transNumber))
          {
            _inFlightMsgs[transNumber].SetResult(resp);
            _inFlightMsgs.Remove(transNumber);
          }
          else
          {
            Log.Error("Received response for transaction that was not sent {lines} {fields}", lines, fields);
          }
        }
      }
    }

    private void ListenThread()
    {
      try
      {
        _listener = new TcpListener(System.Net.IPAddress.Parse("0.0.0.0"), Port);
        _listener.Start();
        if (Port == 0)
        {
          Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
        }

        while (!_exiting)
        {
          try
          {
            //there is only one client (the cell controller) that will connect, so just do it here on this thread
            using (var client = _listener.AcceptTcpClient())
            using (var ns = client.GetStream())
            using (var reader = new StreamReader(ns))
            using (var writer = new StreamWriter(ns))
            {
              lock (_sendLock)
              {
                _send = writer;
              }

              try
              {
                while (!_exiting)
                {
                  ReadResponseMessage(reader);
                }
              }
              finally
              {
                lock (_sendLock)
                {
                  _send = null;
                }
              }
            }
          }
          catch (SocketClosed)
          {
            Log.Debug("Socket closed");
          }
          catch (SocketException ex)
          {
            Log.Debug(ex, "Socket exception");
          }
          catch (Exception ex)
          {
            Log.Error(ex, "Error while reading cell responses");
          }
        }
      }
      catch (Exception ex)
      {
        Log.Fatal(ex, "Unhandled error on socket thread!!!");
      }
    }

    #endregion

    #region IDisposable Support
    private bool disposedValue = false; // To detect redundant calls

    protected virtual void Dispose(bool disposing)
    {
      if (!disposedValue)
      {
        if (disposing)
        {
          // dispose managed state (managed objects).
        }

        _exiting = true;
        #if !NET461
        // https://github.com/dotnet/corefx/issues/26034
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)) {
          _listener.Server.Shutdown(SocketShutdown.Both);
        }
        #endif
        _listener.Stop();
        _thread.Join();

        disposedValue = true;
      }
    }

    ~PlantHostCommunication()
    {
      Dispose(false);
    }

    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }
    #endregion

    private static Serilog.ILogger Log = Serilog.Log.ForContext<PlantHostCommunication>();
    public int Port { get; private set; }
    private readonly Thread _thread;

    public PlantHostCommunication(int port)
    {
      this.Port = port;
      _thread = new Thread(ListenThread);
      _thread.Start();
    }

  }

}