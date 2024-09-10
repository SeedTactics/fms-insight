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
using System.Linq;
using System.Threading;
using MazakMachineInterface;
using Serilog;

namespace BlackMaple.FMSInsight.Mazak.Proxy;

public class ProxyService : System.ServiceProcess.ServiceBase
{
  private MazakConfig _cfg;
  private ICurrentLoadActions _load;
  private OpenDatabaseKitDB _db;
  private IHttpServer _server;
  private readonly ManualResetEvent _onNewData = new ManualResetEvent(false);

  public ProxyService()
  {
    _cfg = MazakConfig.LoadFromRegistry();
  }

  public ProxyService(MazakConfig cfg)
  {
    _cfg = cfg;
  }

  public static void Main(string[] args)
  {
#if DEBUG
    if (args.Length > 0 && args[0] == "--console")
    {
      var svc = new ProxyService(
        new MazakConfig()
        {
          DBType = MazakDbType.MazakVersionE,
          OleDbDatabasePath = "c:\\Mazak\\NFMS\\DB",
          SQLConnectionString = MazakConfig.DefaultConnectionStr,
          LogCSVPath = "c:\\Mazak\\FMS\\Log",
          LoadCSVPath = "c:\\Mazak\\FMS\\LDS",
        }
      );
      svc.OnStart(args.Where(e => e != "--console").ToArray());
      System.Console.WriteLine("Press enter to stop");
      System.Console.ReadLine();
      svc.OnStop();
      return;
    }
#endif

    System.ServiceProcess.ServiceBase.Run(new ProxyService());
  }

  private void OnNewEvent()
  {
    _onNewData.Set();
  }

  protected override void OnStart(string[] args)
  {
    try
    {
      Log.Information(
        string.Join(
          "",
          [
            "Starting Mazak Proxy Service with config: ",
            " DBType: " + _cfg.DBType,
            " OleDbDatabasePath: " + _cfg.OleDbDatabasePath,
            " LogCSVPath: " + _cfg.LogCSVPath,
            " LoadCSVPath: " + _cfg.LoadCSVPath,
            " SQLConnectionString: " + _cfg.SQLConnectionString,
          ]
        )
      );

      if (_cfg.DBType == MazakDbType.MazakSmooth)
      {
        _load = new LoadOperationsFromDB(_cfg);
      }
      else
      {
        _load = new LoadOperationsFromFile(_cfg);
      }

      _db = new OpenDatabaseKitDB(_cfg, _load);
      _db.OnNewEvent += OnNewEvent;

      _server = new HttpServer("http://*:5000/");

      _server.AddLoadingHandler("/all-data", _db.LoadAllData);
      _server.AddPostHandler<string, MazakAllDataAndLogs>("/all-data-and-logs", _db.LoadAllDataAndLogs);
      _server.AddLoadingHandler("/programs", _db.LoadPrograms);
      _server.AddLoadingHandler("/tools", _db.LoadTools);
      _server.AddPostHandler<string, bool>(
        "/delete-logs",
        maxForeign =>
        {
          _db.DeleteLogs(maxForeign);
          return true;
        }
      );
      _server.AddPostHandler<MazakWriteData, bool>(
        "/save",
        w =>
        {
          _db.Save(w);
          return true;
        }
      );

      _server.AddLoadingHandler(
        "/long-poll-events",
        () =>
        {
          var evts = _onNewData.WaitOne(TimeSpan.FromMinutes(1.5));
          if (evts)
          {
            _onNewData.Reset();
          }
          return evts;
        }
      );

      _server.Start();
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Error starting Mazak Proxy Service");
      throw;
    }
  }

  protected override void OnStop()
  {
    Log.Information("Stopping Mazak Proxy Service");

    if (_db != null)
    {
      _db.OnNewEvent -= OnNewEvent;
      _db.Dispose();
    }
    _server?.Dispose();
    _load = null;
    _db = null;
    _server = null;
  }
}
