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
using System.Collections.Immutable;
using System.IO;
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;

namespace BlackMaple.FMSInsight.Makino
{
  public class MakinoCellState : ICellState
  {
    public required CurrentStatus CurrentStatus { get; init; }

    public required ImmutableList<HistoricJob> JobsNotYetCopied { get; init; }

    public required bool StateUpdated { get; init; }

    public TimeSpan TimeUntilNextRefresh => TimeSpan.FromMinutes(1);
  }

  public sealed class MakinoBackend : IFMSBackend, IDisposable, ISynchronizeCellState<MakinoCellState>
  {
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MakinoBackend>();

    public RepositoryConfig RepoConfig { get; }
    public IJobControl JobControl => _jobs;
    public IQueueControl QueueControl => _jobs;
    public IMachineControl MachineControl => null;
    public bool AllowQuarantineToCancelLoad => false;
    public bool AddJobsAsCopiedToSystem => false;

    private readonly JobsAndQueuesFromDb<MakinoCellState> _jobs;
    private readonly MakinoDB _makinoDB;
    private readonly bool _downloadOnlyOrders;
    private readonly string _adePath;

    public MakinoBackend(IConfiguration config, FMSSettings st, SerialSettings serialSt)
    {
      try
      {
        var cfg = config.GetSection("Makino");

        _adePath = cfg.GetValue<string>("ADE Path");
        if (string.IsNullOrEmpty(_adePath))
        {
          _adePath = @"c:\Makino\ADE";
        }
        try
        {
          foreach (var f in Directory.GetFiles(_adePath, "insight*.xml"))
          {
            File.Delete(f);
          }
        }
        catch (DirectoryNotFoundException)
        {
          Log.Error("ADE Path {path} does not exist", _adePath);
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Error when deleting old insight xml files");
        }

        string dbConnStr = cfg.GetValue<string>("SQL Server Connection String");
        if (string.IsNullOrEmpty(dbConnStr))
        {
          dbConnStr = DetectSqlConnectionStr();
        }

        _downloadOnlyOrders = cfg.GetValue<bool>("Download Only Orders");

        Log.Information(
          "Starting makino backend. Connection Str: {connStr}, ADE Path: {path}, DownloadOnlyOrders: {downOnlyOrders}",
          dbConnStr,
          _adePath,
          _downloadOnlyOrders
        );

        RepoConfig = RepositoryConfig.InitializeEventDatabase(
          serialSt,
          Path.Combine(st.DataDirectory, "log.db"),
          Path.Combine(st.DataDirectory, "inspections.db"),
          Path.Combine(st.DataDirectory, "jobs.db")
        );

        _makinoDB = new MakinoDB(dbConnStr);

        _jobs = new JobsAndQueuesFromDb<MakinoCellState>(RepoConfig, st, RaiseNewCurrentStatus, this);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error when initializing makino backend");
      }
    }

    void IDisposable.Dispose()
    {
      _makinoDB.Close();
      _jobs.Dispose();
    }

    public event NewCurrentStatus OnNewCurrentStatus;

    public void RaiseNewCurrentStatus(CurrentStatus s) => OnNewCurrentStatus?.Invoke(s);

    public event Action NewCellState;

    private static string DetectSqlConnectionStr()
    {
      var b = new System.Data.SqlClient.SqlConnectionStringBuilder
      {
        UserID = "sa",
        Password = "M@k1n0Admin",
        InitialCatalog = "Makino",
        DataSource = "(local)"
      };
      return b.ConnectionString;
    }

    public IEnumerable<string> CheckNewJobs(IRepository db, NewJobs jobs)
    {
      return [];
    }

    private bool _errorDownloadingJobs = false;

    public MakinoCellState CalculateCellState(IRepository db)
    {
      var lastLog = db.MaxLogDate();
      if (DateTime.UtcNow.Subtract(lastLog) > TimeSpan.FromDays(30))
      {
        lastLog = DateTime.UtcNow.AddDays(-30);
      }

      var newEvts = new LogBuilder(_makinoDB, db).CheckLogs(lastLog);

      var st = _makinoDB.LoadCurrentInfo(db);

      var notCopied = db.LoadJobsNotCopiedToSystem(
        DateTime.UtcNow.AddHours(-10),
        DateTime.UtcNow.AddMinutes(20),
        includeDecremented: false
      );

      return new MakinoCellState
      {
        CurrentStatus = _errorDownloadingJobs
          ? st with
          {
            Alarms = st.Alarms.Add(
              "Unable to copy orders to Makino: check that the Makino software is running"
            )
          }
          : st,
        JobsNotYetCopied = notCopied,
        StateUpdated = newEvts
      };
    }

    public bool ApplyActions(IRepository db, MakinoCellState state)
    {
      List<HistoricJob> jobsToSend = [];

      foreach (var j in state.JobsNotYetCopied)
      {
        if (state.CurrentStatus.Jobs.ContainsKey(j.UniqueStr))
        {
          // was copied but must have crashed before recording the copy, or
          // makino processed them after the 10 second timeout
          db.MarkJobCopiedToSystem(j.UniqueStr);
        }
        else
        {
          jobsToSend.Add(j);
        }
      }

      if (jobsToSend.Count > 0)
      {
        var fileName = "insight" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".xml";
        var fullPath = Path.Combine(_adePath, fileName);
        try
        {
          using var fw = new FileSystemWatcher(_adePath, fileName);
          fw.EnableRaisingEvents = true;
          OrderXML.WriteOrderXML(fullPath, jobsToSend, _downloadOnlyOrders);
          if (fw.WaitForChanged(WatcherChangeTypes.Deleted, TimeSpan.FromSeconds(10)).TimedOut)
          {
            Log.Error("Makino did not process new jobs, perhaps the Makino software is not running?");
            _errorDownloadingJobs = true;
          }
          else
          {
            _errorDownloadingJobs = false;
            foreach (var j in jobsToSend)
            {
              db.MarkJobCopiedToSystem(j.UniqueStr);
            }
          }
        }
        finally
        {
          File.Delete(fullPath);
        }
        return true;
      }
      else
      {
        _errorDownloadingJobs = false;
        return false;
      }
    }

    public bool DecrementJobs(IRepository db, MakinoCellState state)
    {
      // do nothing
      return false;
    }
  }
}
