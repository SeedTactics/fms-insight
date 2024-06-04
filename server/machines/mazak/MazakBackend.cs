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
  public sealed class MazakBackend : IFMSBackend, IDisposable
  {
    private readonly JobsAndQueuesFromDb<MazakState> _jobsAndQueues;
    private readonly IReadDataAccess _readDB;
    private readonly IWriteData _writeDB;
    private readonly ICurrentLoadActions loadOper;

    public RepositoryConfig RepoConfig { get; }
    public MazakMachineControl MazakMachineControl { get; }

    public IReadDataAccess ReadDB => _readDB;
    public IWriteData WriteDB => _writeDB;
    public IJobControl JobControl => _jobsAndQueues;
    public IQueueControl QueueControl => _jobsAndQueues;
    public IMachineControl MachineControl => MazakMachineControl;

    public event NewCurrentStatus OnNewCurrentStatus;

    public void RaiseCurrentStatusChanged(CurrentStatus s)
    {
      OnNewCurrentStatus?.Invoke(s);
    }

    public MazakBackend(
      IConfiguration configuration,
      FMSSettings st,
      SerialSettings serialSt,
      MazakConfig mazakCfg = null
    )
    {
      mazakCfg ??= MazakConfig.Load(configuration);

      if (mazakCfg.DBType == MazakDbType.MazakVersionE)
      {
        throw new Exception("This version of FMS Insight does not support Mazak Version E");
      }

      var oldJobDbName = System.IO.Path.Combine(st.DataDirectory, "jobinspection.db");
      if (!System.IO.File.Exists(oldJobDbName))
        oldJobDbName = System.IO.Path.Combine(st.DataDirectory, "mazakjobs.db");

      RepoConfig = RepositoryConfig.InitializeEventDatabase(
        serialSt,
        System.IO.Path.Combine(st.DataDirectory, "log.db"),
        System.IO.Path.Combine(st.DataDirectory, "insp.db"),
        oldJobDbName
      );

      _writeDB = new OpenDatabaseKitTransactionDB(mazakCfg.SQLConnectionString, mazakCfg.DBType);

      if (mazakCfg.DBType == MazakDbType.MazakWeb)
        loadOper = new LoadOperationsFromFile(mazakCfg, enableWatcher: false, onLoadActions: (l, a) => { }); // web instead watches the log csv files
      else
        loadOper = new LoadOperationsFromDB(mazakCfg.SQLConnectionString); // smooth db doesn't use the load operations file

      _readDB = new OpenDatabaseKitReadDB(mazakCfg.SQLConnectionString, mazakCfg.DBType, loadOper);

      var writeJobs = new WriteJobs(
        d: _writeDB,
        readDb: _readDB,
        repoCfg: RepoConfig,
        settings: st,
        useStartingOffsetForDueDate: mazakCfg.UseStartingOffsetForDueDate,
        progDir: mazakCfg.ProgramDirectory
      );

      var syncSt = new MazakSync(
        machineGroupName: writeJobs,
        readDb: _readDB,
        writeDb: _writeDB,
        settings: st,
        mazakConfig: mazakCfg,
        writeJobs: writeJobs
      );

      _jobsAndQueues = new JobsAndQueuesFromDb<MazakState>(
        RepoConfig,
        st,
        s => OnNewCurrentStatus?.Invoke(s),
        syncSt
      );

      MazakMachineControl = new MazakMachineControl(RepoConfig, _readDB, writeJobs, mazakCfg);

      _jobsAndQueues.StartThread();
    }

    public void Dispose()
    {
      _jobsAndQueues?.Dispose();
      loadOper?.Dispose();
    }
  }
}
