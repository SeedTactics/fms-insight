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
using System.Linq;
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.Tests")]

namespace MazakMachineInterface
{
  public class MazakBackend : IFMSBackend, IDisposable
  {
    private readonly IReadDataAccess _readDB;
    private readonly IWriteData _writeDB;
    private readonly ICurrentLoadActions loadOper;

    private readonly RoutingInfo routing;
    private readonly IMazakLogReader logDataLoader;

    private readonly RepositoryConfig logDbConfig;

    //Settings
    public IReadDataAccess ReadDB => _readDB;
    public IWriteData WriteDB => _writeDB;

    public RepositoryConfig RepoConfig => logDbConfig;
    public MazakMachineControl MazakMachineControl { get; }

    public IMazakLogReader LogTranslation => logDataLoader;
    public RoutingInfo RoutingInfo => routing;

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
      mazakCfg ??= MazakConfig.Load(configuration);

      var oldJobDbName = System.IO.Path.Combine(st.DataDirectory, "jobinspection.db");
      if (!System.IO.File.Exists(oldJobDbName))
        oldJobDbName = System.IO.Path.Combine(st.DataDirectory, "mazakjobs.db");

      logDbConfig = RepositoryConfig.InitializeEventDatabase(
        serialSt,
        System.IO.Path.Combine(st.DataDirectory, "log.db"),
        System.IO.Path.Combine(st.DataDirectory, "insp.db"),
        oldJobDbName
      );

      _writeDB = new OpenDatabaseKitTransactionDB(mazakCfg.SQLConnectionString, mazakCfg.DBType);

      if (mazakCfg.DBType == MazakDbType.MazakVersionE)
        loadOper = new LoadOperationsFromFile(mazakCfg, enableWatcher: true, onLoadActions: OnLoadActions);
      else if (mazakCfg.DBType == MazakDbType.MazakWeb)
        loadOper = new LoadOperationsFromFile(mazakCfg, enableWatcher: false, onLoadActions: (l, a) => { }); // web instead watches the log csv files
      else
        loadOper = new LoadOperationsFromDB(mazakCfg.SQLConnectionString); // smooth db doesn't use the load operations file

      _readDB = new OpenDatabaseKitReadDB(mazakCfg.SQLConnectionString, mazakCfg.DBType, loadOper);

      var hold = new HoldPattern(_writeDB);
      WriteJobs writeJobs;
      using (var jdb = logDbConfig.OpenConnection())
      {
        writeJobs = new WriteJobs(
          d: _writeDB,
          readDb: _readDB,
          jDB: jdb,
          settings: st,
          useStartingOffsetForDueDate: mazakCfg.UseStartingOffsetForDueDate,
          progDir: mazakCfg.ProgramDirectory
        );
      }
      if (mazakCfg.DBType == MazakDbType.MazakWeb || mazakCfg.DBType == MazakDbType.MazakSmooth)
        logDataLoader = new LogDataWeb(
          logDbConfig,
          writeJobs,
          _readDB,
          _writeDB,
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
  }
}
