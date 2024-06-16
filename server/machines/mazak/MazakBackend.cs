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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.Tests")]

namespace MazakMachineInterface
{
  public static class MazakServices
  {
    public static void AddMazakBackend(this IServiceCollection s, MazakConfig mazakCfg)
    {
      if (mazakCfg.DBType == MazakDbType.MazakVersionE)
      {
        throw new Exception("This version of FMS Insight does not support Mazak Version E");
      }

      s.AddSingleton(mazakCfg);
      s.AddSingleton<IWriteData, OpenDatabaseKitTransactionDB>();

      if (mazakCfg.DBType == MazakDbType.MazakWeb)
        s.AddSingleton<ICurrentLoadActions, LoadOperationsFromFile>();
      else
        s.AddSingleton<ICurrentLoadActions, LoadOperationsFromDB>();

      s.AddSingleton<IReadDataAccess, OpenDatabaseKitReadDB>();
      s.AddSingleton<ISynchronizeCellState<MazakState>, MazakSync>();
      s.AddSingleton<IMachineControl, MazakMachineControl>();
    }

    public static void AddMazakBackend(this IHostBuilder h, MazakConfig mazakCfg)
    {
      h.ConfigureServices((_, s) => s.AddMazakBackend(mazakCfg));
    }
  }

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
    public IJobAndQueueControl JobControl => _jobsAndQueues;
    public IMachineControl MachineControl => MazakMachineControl;

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

      _writeDB = new OpenDatabaseKitTransactionDB(mazakCfg);

      if (mazakCfg.DBType == MazakDbType.MazakWeb)
        loadOper = new LoadOperationsFromFile(mazakCfg); // web instead watches the log csv files
      else
        loadOper = new LoadOperationsFromDB(mazakCfg); // smooth db doesn't use the load operations file

      _readDB = new OpenDatabaseKitReadDB(mazakCfg, loadOper);

      var syncSt = new MazakSync(readDb: _readDB, writeDb: _writeDB, settings: st, mazakConfig: mazakCfg);

      _jobsAndQueues = new JobsAndQueuesFromDb<MazakState>(RepoConfig, st, syncSt);

      MazakMachineControl = new MazakMachineControl(RepoConfig, _readDB, mazakCfg);

      _jobsAndQueues.StartThread();
    }

    public void Dispose()
    {
      _jobsAndQueues?.Dispose();
    }
  }
}
