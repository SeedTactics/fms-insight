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
using System.Linq;
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.Tests")]

namespace MazakMachineInterface
{
  public class MazakBackend : IFMSBackend, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<MazakBackend>();

    private IReadDataAccess _readDB;
    private OpenDatabaseKitTransactionDB _writeDB;
    private RoutingInfo routing;
    private HoldPattern hold;
    private MazakQueues queues;
    private LoadOperationsFromFile loadOper;
    private IMazakLogReader logDataLoader;

    private EventLogDB.Config logDbConfig;
    private JobDB.Config jobDBConfig;

    //Settings
    public MazakDbType MazakType { get; }
    public bool UseStartingOffsetForDueDate { get; set; }
    public bool CheckPalletsUsedOnce { get; set; }
    public string ProgramDirectory { get; set; }

    public IWriteData WriteDB => _writeDB;
    public IReadDataAccess ReadDB => _readDB;

    public EventLogDB.Config EventLogDBConfig => logDbConfig;
    public JobDB.Config JobDBConfig => jobDBConfig;
    public IMazakLogReader LogTranslation => logDataLoader;
    public RoutingInfo RoutingInfo => routing;

    public event NewJobsDelegate OnNewJobs;
    public event NewLogEntryDelegate NewLogEntry;
    public event NewCurrentStatus OnNewCurrentStatus;

    private void RaiseNewLogEntry(BlackMaple.MachineWatchInterface.LogEntry e, string foreignId) =>
      NewLogEntry?.Invoke(e, foreignId);

    private void RaiseCurrentStatusChanged(BlackMaple.MachineFramework.JobDB jobDb, BlackMaple.MachineFramework.EventLogDB logDB)
    {
      if (routing == null) return;
      OnNewCurrentStatus?.Invoke(routing.CurrentStatus(jobDb, logDB));
    }

    private void OnLoadActions(int lds, IEnumerable<LoadAction> actions)
    {
      // this opens log and db connections to the database again :(
      // Only used for Mazak Version E
      OnNewCurrentStatus?.Invoke(((IJobControl)routing).GetCurrentStatus());
    }


    public MazakBackend(IConfiguration configuration, FMSSettings st)
    {
      var cfg = configuration.GetSection("Mazak");
      string localDbPath = cfg.GetValue<string>("Database Path");
      MazakType = DetectMazakType(cfg, localDbPath);

      // database settings
      string sqlConnectString = cfg.GetValue<string>("SQL ConnectionString");
      string dbConnStr;
      if (MazakType == MazakDbType.MazakSmooth)
      {
        if (!string.IsNullOrEmpty(sqlConnectString))
        {
          dbConnStr = sqlConnectString;
        }
        else if (!string.IsNullOrEmpty(localDbPath))
        {
          // old installers put sql server computer name in localDbPath
          dbConnStr = "Server=" + localDbPath + "\\pmcsqlserver;" +
              "User ID=mazakpmc;Password=Fms-978";
        }
        else
        {
          var b = new System.Data.SqlClient.SqlConnectionStringBuilder();
          b.UserID = "mazakpmc";
          b.Password = "Fms-978";
          b.DataSource = "(local)";
          dbConnStr = b.ConnectionString;
        }
      }
      else
      {
        dbConnStr = localDbPath;
        if (string.IsNullOrEmpty(dbConnStr))
        {
          dbConnStr = "c:\\Mazak\\NFMS\\DB";
        }
      }

      // log csv
      string logPath = cfg.GetValue<string>("Log CSV Path");
      if (logPath == null || logPath == "")
        logPath = "c:\\Mazak\\FMS\\Log";

      if (MazakType != MazakDbType.MazakVersionE && !System.IO.Directory.Exists(logPath))
      {
        Log.Error("Log CSV Directory {path} does not exist.  Set the directory in the config.ini file.", logPath);
      }
      else if (MazakType != MazakDbType.MazakVersionE)
      {
        Log.Information("Loading log CSV files from {logcsv}", logPath);
      }

      // general config
      string useStarting = cfg.GetValue<string>("Use Starting Offset For Due Date");
      string useStarting2 = cfg.GetValue<string>("Use Starting Offset");
      if (string.IsNullOrEmpty(useStarting))
      {
        if (string.IsNullOrEmpty(useStarting2))
        {
          UseStartingOffsetForDueDate = true;
        }
        else
        {
          UseStartingOffsetForDueDate = Convert.ToBoolean(useStarting2);
        }
      }
      else
      {
        UseStartingOffsetForDueDate = Convert.ToBoolean(useStarting);
      }
      //Perhaps this should be a new setting, but if you don't check for pallets used once
      //then you don't care if all faces on a pallet are full so might as well use priority
      //which causes pallet positions to go empty.
      CheckPalletsUsedOnce = !UseStartingOffsetForDueDate;

      ProgramDirectory = cfg.GetValue<string>("Program Directory");
      if (string.IsNullOrEmpty(ProgramDirectory))
      {
        ProgramDirectory = "C:\\NCProgs";
      }

      // serial settings
      string serialPerMaterial = cfg.GetValue<string>("Assign Serial Per Material");
      if (!string.IsNullOrEmpty(serialPerMaterial))
      {
        bool result;
        if (bool.TryParse(serialPerMaterial, out result))
        {
          if (!result)
            st.SerialType = SerialType.AssignOneSerialPerCycle;
        }
      }

      // queue settings
      bool waitForAllCastings = cfg.GetValue<bool>("Wait For All Castings", false);

      Log.Debug(
        "Configured UseStartingOffsetForDueDate = {useStarting}",
        UseStartingOffsetForDueDate);

      logDbConfig = EventLogDB.Config.InitializeEventDatabase(
        st,
        System.IO.Path.Combine(st.DataDirectory, "log.db"),
        System.IO.Path.Combine(st.DataDirectory, "insp.db")
      );
      logDbConfig.NewLogEntry += RaiseNewLogEntry;

      var jobInspName = System.IO.Path.Combine(st.DataDirectory, "jobinspection.db");
      if (System.IO.File.Exists(jobInspName))
        jobDBConfig = BlackMaple.MachineFramework.JobDB.Config.InitializeJobDatabase(jobInspName);
      else
        jobDBConfig = BlackMaple.MachineFramework.JobDB.Config.InitializeJobDatabase(System.IO.Path.Combine(st.DataDirectory, "mazakjobs.db"));

      _writeDB = new OpenDatabaseKitTransactionDB(dbConnStr, MazakType);

      if (MazakType == MazakDbType.MazakVersionE)
        loadOper = new LoadOperationsFromFile(cfg, enableWatcher: true, onLoadActions: OnLoadActions);
      else if (MazakType == MazakDbType.MazakWeb)
        loadOper = new LoadOperationsFromFile(cfg, enableWatcher: false, onLoadActions: (l, a) => { }); // web instead watches the log csv files
      else
        loadOper = null; // smooth db doesn't use the load operations file

      var openReadDb = new OpenDatabaseKitReadDB(dbConnStr, MazakType, loadOper);
      if (MazakType == MazakDbType.MazakSmooth)
        _readDB = new SmoothReadOnlyDB(dbConnStr, openReadDb);
      else
        _readDB = openReadDb;

      queues = new MazakQueues(_writeDB, waitForAllCastings);
      var sendToExternal = new SendMaterialToExternalQueue();

      hold = new HoldPattern(_writeDB, _readDB, jobDBConfig, true);
      WriteJobs writeJobs;
      using (var jdb = jobDBConfig.OpenConnection())
      {
        writeJobs = new WriteJobs(_writeDB, _readDB, hold, jdb, st, CheckPalletsUsedOnce, UseStartingOffsetForDueDate, ProgramDirectory);
      }
      var decr = new DecrementPlanQty(_writeDB, _readDB);

      if (MazakType == MazakDbType.MazakWeb || MazakType == MazakDbType.MazakSmooth)
        logDataLoader = new LogDataWeb(logPath, logDbConfig, jobDBConfig, writeJobs, sendToExternal, _readDB, queues, st,
          currentStatusChanged: RaiseCurrentStatusChanged
        );
      else
      {
#if USE_OLEDB
				logDataLoader = new LogDataVerE(logDbConfig, jobDBConfig, sendToExternal, writeJobs, _readDB, queues, st);
#else
        throw new Exception("Mazak Web and VerE are not supported on .NET core");
#endif
      }

      routing = new RoutingInfo(_writeDB, writeJobs, _readDB, logDataLoader, jobDBConfig, logDbConfig, writeJobs, queues, decr,
          CheckPalletsUsedOnce, st,
          onNewJobs: j => OnNewJobs?.Invoke(j),
          onStatusChange: s => OnNewCurrentStatus?.Invoke(s)
      );
    }

    private bool _disposed = false;
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      logDbConfig.NewLogEntry -= RaiseNewLogEntry;
      routing.Halt();
      hold.Shutdown();
      logDataLoader.Halt();
      if (loadOper != null) loadOper.Halt();
    }

    public IJobControl JobControl { get => routing; }

    public IOldJobDecrement OldJobDecrement { get => routing; }

    public IJobDatabase OpenJobDatabase()
    {
      return jobDBConfig.OpenConnection();
    }
    public ILogDatabase OpenLogDatabase()
    {
      return logDbConfig.OpenConnection();
    }
    public IInspectionControl OpenInspectionControl()
    {
      return logDbConfig.OpenConnection();
    }

    private MazakDbType DetectMazakType(IConfigurationSection cfg, string localDbPath)
    {
      var verE = cfg.GetValue<bool>("VersionE");
      var webver = cfg.GetValue<bool>("Web Version");
      var smoothVer = cfg.GetValue<bool>("Smooth Version");

      if (verE)
        return MazakDbType.MazakVersionE;
      else if (webver)
        return MazakDbType.MazakWeb;
      else if (smoothVer)
        return MazakDbType.MazakSmooth;

      string testPath;
      if (string.IsNullOrEmpty(localDbPath))
      {
        testPath = "C:\\Mazak\\NFMS\\DB\\FCREADDAT01.mdb";
      }
      else
      {
        testPath = System.IO.Path.Combine(localDbPath, "FCREADDAT01.mdb");
      }

      if (System.IO.File.Exists(testPath))
      {
        //TODO: open database to check column existance for web vs E.
        Log.Information("Assuming Mazak WEB version.  If this is incorrect it can be changed in the settings.");
        return MazakDbType.MazakWeb;
      }
      else
      {
        Log.Information("Assuming Mazak Smooth version.  If this is incorrect it can be changed in the settings.");
        return MazakDbType.MazakSmooth;
      }
    }

    public string CustomizeInstructionPath(string part, string type)
    {
      throw new NotImplementedException();
    }
  }

  public static class MazakProgram
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
          Backend = new MazakBackend(cfg, st),
          Name = "Mazak",
          Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
        });
    }
  }
}