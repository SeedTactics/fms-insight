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
    private MazakQueues queues;
    private ICurrentLoadActions loadOper;
    private IMazakLogReader logDataLoader;

    private RepositoryConfig logDbConfig;

    //Settings
    public MazakDbType MazakType { get; }
    public string ProgramDirectory { get; }

    public IWriteData WriteDB => _writeDB;
    public IReadDataAccess ReadDB => _readDB;

    public RepositoryConfig RepoConfig => logDbConfig;
    public IMazakLogReader LogTranslation => logDataLoader;
    public RoutingInfo RoutingInfo => routing;
    public MazakMachineControl MazakMachineControl { get; }

    public event NewCurrentStatus OnNewCurrentStatus;

    public void RaiseCurrentStatusChanged(BlackMaple.MachineFramework.IRepository jobDb)
    {
      if (routing == null)
        return;
      OnNewCurrentStatus?.Invoke(routing.CurrentStatus(jobDb));
    }

    private void OnLoadActions(int lds, IEnumerable<LoadAction> actions)
    {
      // this opens log and db connections to the database again :(
      // Only used for Mazak Version E
      OnNewCurrentStatus?.Invoke(((IJobControl)routing).GetCurrentStatus());
    }

    public MazakBackend(
      IConfiguration configuration,
      FMSSettings st,
      SerialSettings serialSt,
      MazakConfig mazakCfg = null
    )
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
          dbConnStr = "Server=" + localDbPath + "\\pmcsqlserver;" + "User ID=mazakpmc;Password=Fms-978";
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
        Log.Error(
          "Log CSV Directory {path} does not exist.  Set the directory in the config.ini file.",
          logPath
        );
      }
      else if (MazakType != MazakDbType.MazakVersionE)
      {
        Log.Information("Loading log CSV files from {logcsv}", logPath);
      }

      // general config
      string useStarting = cfg.GetValue<string>("Use Starting Offset For Due Date");
      string useStarting2 = cfg.GetValue<string>("Use Starting Offset");
      bool UseStartingOffsetForDueDate = true;
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
            serialSt.SerialType = SerialType.AssignOneSerialPerCycle;
        }
      }

      // queue settings
      bool waitForAllCastings = cfg.GetValue<bool>("Wait For All Castings", false);

      Log.Debug("Configured UseStartingOffsetForDueDate = {useStarting}", UseStartingOffsetForDueDate);

      var oldJobDbName = System.IO.Path.Combine(st.DataDirectory, "jobinspection.db");
      if (!System.IO.File.Exists(oldJobDbName))
        oldJobDbName = System.IO.Path.Combine(st.DataDirectory, "mazakjobs.db");

      logDbConfig = RepositoryConfig.InitializeEventDatabase(
        serialSt,
        System.IO.Path.Combine(st.DataDirectory, "log.db"),
        System.IO.Path.Combine(st.DataDirectory, "insp.db"),
        oldJobDbName
      );

      _writeDB = new OpenDatabaseKitTransactionDB(dbConnStr, MazakType);

      if (MazakType == MazakDbType.MazakVersionE)
        loadOper = new LoadOperationsFromFile(cfg, enableWatcher: true, onLoadActions: OnLoadActions);
      else if (MazakType == MazakDbType.MazakWeb)
        loadOper = new LoadOperationsFromFile(cfg, enableWatcher: false, onLoadActions: (l, a) => { }); // web instead watches the log csv files
      else
        loadOper = new LoadOperationsFromDB(dbConnStr); // smooth db doesn't use the load operations file

      _readDB = new OpenDatabaseKitReadDB(dbConnStr, MazakType, loadOper);

      queues = new MazakQueues(_writeDB, waitForAllCastings);

      var hold = new HoldPattern(_writeDB);
      WriteJobs writeJobs;
      using (var jdb = logDbConfig.OpenConnection())
      {
        writeJobs = new WriteJobs(
          d: _writeDB,
          readDb: _readDB,
          jDB: jdb,
          settings: st,
          useStartingOffsetForDueDate: UseStartingOffsetForDueDate,
          progDir: ProgramDirectory
        );
      }
      var decr = new DecrementPlanQty(_writeDB);

      if (MazakType == MazakDbType.MazakWeb || MazakType == MazakDbType.MazakSmooth)
        logDataLoader = new LogDataWeb(
          logPath,
          logDbConfig,
          writeJobs,
          _readDB,
          queues,
          hold,
          st,
          currentStatusChanged: RaiseCurrentStatusChanged,
          mazakConfig: mazakCfg
        );
      else
      {
#if USE_VERE
        logDataLoader = new LogDataVerE(
          logDbConfig,
          jobDBConfig,
          sendToExternal,
          writeJobs,
          _readDB,
          queues,
          hold,
          st,
          currentStatusChanged: RaiseCurrentStatusChanged,
          mazakConfig: mazakCfg
        );
#else
        throw new Exception("Mazak VerE is not supported on .NET core");
#endif
      }

      routing = new RoutingInfo(
        d: _writeDB,
        machineGroupName: writeJobs,
        readDb: _readDB,
        logR: logDataLoader,
        jLogCfg: logDbConfig,
        wJobs: writeJobs,
        queueSyncFault: queues,
        decrement: decr,
        useStartingOffsetForDueDate: UseStartingOffsetForDueDate,
        settings: st,
        onStatusChange: s => OnNewCurrentStatus?.Invoke(s),
        mazakCfg: mazakCfg
      );

      MazakMachineControl = new MazakMachineControl(logDbConfig, _readDB, writeJobs, mazakCfg);
    }

    private bool _disposed = false;

    public void Dispose()
    {
      if (_disposed)
        return;
      _disposed = true;
      routing.Halt();
      logDataLoader.Halt();
      if (loadOper != null)
        loadOper.Dispose();
    }

    public IJobControl JobControl
    {
      get => routing;
    }
    public IQueueControl QueueControl => routing;

    public IMachineControl MachineControl => MazakMachineControl;

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
        Log.Information(
          "Assuming Mazak WEB version.  If this is incorrect it can be changed in the settings."
        );
        return MazakDbType.MazakWeb;
      }
      else
      {
        Log.Information(
          "Assuming Mazak Smooth version.  If this is incorrect it can be changed in the settings."
        );
        return MazakDbType.MazakSmooth;
      }
    }

    public string CustomizeInstructionPath(string part, string type)
    {
      throw new NotImplementedException();
    }
  }
}
