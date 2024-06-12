/* Copyright (c) 2020, John Lenz

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
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.Tests")]

namespace BlackMaple.FMSInsight.Niigata
{
  public sealed class NiigataBackend : IFMSBackend, IDisposable
  {
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<NiigataBackend>();

    private readonly JobsAndQueuesFromDb<CellState> _jobsAndQueues;

    public IJobAndQueueControl JobControl => _jobsAndQueues;
    public IMachineControl MachineControl { get; }
    public RepositoryConfig RepoConfig { get; }

    public event NewCurrentStatus OnNewCurrentStatus;

    public NiigataBackend(
      IConfigurationSection config,
      FMSSettings cfg,
      SerialSettings serialSt,
      bool startSyncThread,
      Func<NiigataStationNames, ICncMachineConnection, IAssignPallets> customAssignment = null,
      CheckJobsValid customJobCheck = null,
      Func<ActiveJob, bool> decrementJobFilter = null
    )
    {
      try
      {
        var settings = new NiigataSettings(config, cfg);
        Log.Debug("Starting niigata backend with {@settings}", settings);

        RepoConfig = RepositoryConfig.InitializeEventDatabase(
          serialSt,
          System.IO.Path.Combine(cfg.DataDirectory, "niigatalog.db")
        );

        var machConn = new CncMachineConnection(settings.MachineIPs);
        var icc = new NiigataICC(settings);
        var createLog = new CreateCellState(settings.FMSSettings, settings.StationNames, machConn);
        MachineControl = new NiigataMachineControl(RepoConfig, icc, machConn, settings.StationNames);

        IAssignPallets assign = null;
        if (customAssignment != null)
        {
          assign = customAssignment(settings.StationNames, machConn);
        }

        CheckJobsValid checkJobsValid = customJobCheck ?? CheckJobsMatchNiigata.CheckNewJobs;

        var syncSt = new SyncNiigataPallets(
          settings,
          icc,
          createLog,
          checkJobs: customJobCheck,
          assign: assign,
          decrementJobFilter: decrementJobFilter
        );

        _jobsAndQueues = new JobsAndQueuesFromDb<CellState>(
          RepoConfig,
          settings.FMSSettings,
          s => OnNewCurrentStatus?.Invoke(s),
          syncSt
        );

        if (startSyncThread)
        {
          StartSyncThread();
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Unhandled exception when initializing niigata backend");
      }
    }

    public void StartSyncThread()
    {
      _jobsAndQueues.StartThread();
    }

    public void Dispose()
    {
      _jobsAndQueues?.Dispose();
    }
  }
}
