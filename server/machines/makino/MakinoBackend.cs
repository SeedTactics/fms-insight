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
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using Microsoft.Extensions.Configuration;

namespace Makino
{
  public class MakinoBackend : IFMSBackend, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<MakinoBackend>();

    // Common databases from machine framework
    private Func<JobDB> _openJobDB;
    private EventLogDB.Config _logDbCfg;
    public EventLogDB OpenLogDB => _logDbCfg.OpenConnection();

    // Makino databases
    private MakinoDB _makinoDB;
    private StatusDB _status;

    private Jobs _jobs;
#pragma warning disable 649
    private LogTimer _logTimer;
#pragma warning restore 649
    private string _dataDirectory;

    public MakinoBackend(IConfiguration config, FMSSettings st)
    {
      try
      {
        var cfg = config.GetSection("Makino");

        string adePath = cfg.GetValue<string>("ADE Path");
        if (string.IsNullOrEmpty(adePath))
        {
          adePath = @"c:\Makino\ADE";
        }

        string dbConnStr = cfg.GetValue<string>("SQL Server Connection String");
        if (string.IsNullOrEmpty(dbConnStr))
        {
          dbConnStr = DetectSqlConnectionStr();
        }

        bool downloadOnlyOrders = cfg.GetValue<bool>("Download Only Orders");

        Log.Information(
                    "Starting makino backend. Connection Str: {connStr}, ADE Path: {path}, DownloadOnlyOrders: {downOnlyOrders}",
                     dbConnStr, adePath, downloadOnlyOrders);

        _dataDirectory = st.DataDirectory;

        _logDbCfg = EventLogDB.Config.InitializeEventDatabase(
            st,
            System.IO.Path.Combine(_dataDirectory, "log.db"),
            System.IO.Path.Combine(_dataDirectory, "inspections.db")
        );
        _logDbCfg.NewLogEntry += OnLogEntry;

        // open jobDB
        var jobDBCfg = BlackMaple.MachineFramework.JobDB.Config.InitializeJobDatabase(System.IO.Path.Combine(_dataDirectory, "jobs.db"));
        _openJobDB = () => jobDBCfg.OpenConnection();

        _status = new StatusDB(System.IO.Path.Combine(_dataDirectory, "makino.db"));

#if DEBUG
        _makinoDB = new MakinoDB(_logDbCfg, MakinoDB.DBTypeEnum.SqlLocal, "", _status);
#else
        _makinoDB = new MakinoDB(_logDbCfg, MakinoDB.DBTypeEnum.SqlConnStr, dbConnStr, _status);
#endif

        _logTimer = new LogTimer(_logDbCfg, _openJobDB, _makinoDB, _status, st);

        _jobs = new Jobs(_makinoDB, _openJobDB, adePath, downloadOnlyOrders, onNewJob: j => OnNewJobs?.Invoke(j), onJobCommentChange: OnLogsProcessed);

        _logTimer.LogsProcessed += OnLogsProcessed;

      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error when initializing makino backend");
      }
    }

    private bool _disposed = false;
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      _logTimer.LogsProcessed -= OnLogsProcessed;
      if (_logTimer != null) _logTimer.Halt();
      if (_status != null) _status.Close();
      if (_makinoDB != null) _makinoDB.Close();
      _logDbCfg.NewLogEntry -= OnLogEntry;
    }

    public event NewLogEntryDelegate NewLogEntry;
    private void OnLogEntry(LogEntry entry, string foreignId)
    {
      NewLogEntry?.Invoke(entry, foreignId);
    }

    public event NewCurrentStatus OnNewCurrentStatus;
    public event NewJobsDelegate OnNewJobs;
    public void RaiseNewCurrentStatus(CurrentStatus s) => OnNewCurrentStatus?.Invoke(s);
    private void OnLogsProcessed()
    {
      RaiseNewCurrentStatus(_jobs.GetCurrentStatus());
    }



    private string DetectSqlConnectionStr()
    {
      var b = new System.Data.SqlClient.SqlConnectionStringBuilder();
      b.UserID = "sa";
      b.Password = "M@k1n0Admin";
      b.InitialCatalog = "Makino";
      b.DataSource = "(local)";
      return b.ConnectionString;
    }

    public IJobDatabase OpenJobDatabase()
    {
      return _openJobDB();
    }

    public IJobControl JobControl { get => _jobs; }

    public IOldJobDecrement OldJobDecrement { get => _jobs; }

    public IInspectionControl OpenInspectionControl()
    {
      return _logDbCfg.OpenConnection();
    }

    public ILogDatabase OpenLogDatabase()
    {
      return _logDbCfg.OpenConnection();
    }

    public LogTimer LogTimer
    {
      get { return _logTimer; }
    }

    public string DataDir
    {
      get { return _dataDirectory; }
    }
  }
  public static class MakinoProgram
  {
    public static void Main()
    {
#if DEBUG
      var useService = false;
#else
			var useService = true;
#endif
      Program.Run(useService, (cfg, st) =>
        new FMSImplementation()
        {
          Backend = new MakinoBackend(cfg, st),
          Name = "Makino",
          Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
        });
    }
  }
}

