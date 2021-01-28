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
using System.Linq;
using System.Collections.Generic;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using Microsoft.Extensions.Configuration;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.Tests")]

namespace BlackMaple.FMSInsight.Niigata
{

  public class NiigataBackend : IFMSBackend, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<NiigataBackend>();
    private NiigataICC _icc;
    private NiigataJobs _jobControl;
    private SyncPallets _sync;
    private NiigataMachineControl _machControl;

    public RepositoryConfig LogDBConfig { get; private set; }
    public ISyncPallets SyncPallets => _sync;
    public NiigataStationNames StationNames { get; }
    public ICncMachineConnection MachineConnection { get; }
    public bool SupportsQuarantineAtLoadStation { get; } = true;

    public event NewJobsDelegate OnNewJobs;
    public event NewLogEntryDelegate NewLogEntry;
    public event NewCurrentStatus OnNewCurrentStatus;
    private void RaiseNewLogEntry(BlackMaple.MachineWatchInterface.LogEntry e, string foreignId, IRepository db) =>
      NewLogEntry?.Invoke(e, foreignId);
    public event EditMaterialInLogDelegate OnEditMaterialInLog;

    public NiigataBackend(
      IConfigurationSection config,
      FMSSettings cfg,
      bool startSyncThread,
      Func<NiigataStationNames, ICncMachineConnection, IAssignPallets> customAssignment = null,
      IDecrementJobs decrementJobs = null
    )
    {
      try
      {
        Log.Information("Starting niigata backend");
        LogDBConfig = RepositoryConfig.InitializeEventDatabase(
            cfg,
            System.IO.Path.Combine(cfg.DataDirectory, "niigatalog.db"),
            oldJobDbFile: System.IO.Path.Combine(cfg.DataDirectory, "niigatajobs.db")
        );
        LogDBConfig.NewLogEntry += RaiseNewLogEntry;

        var programDir = config.GetValue<string>("Program Directory");
        if (!System.IO.Directory.Exists(programDir))
        {
          Log.Error("Program directory {dir} does not exist", programDir);
        }

        var reclampNames = config.GetValue<string>("Reclamp Group Names");
        var machineNames = config.GetValue<string>("Machine Names");
        StationNames = new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>(
            string.IsNullOrEmpty(reclampNames) ? Enumerable.Empty<string>() : reclampNames.Split(',').Select(s => s.Trim())
          ),
          IccMachineToJobMachNames = string.IsNullOrEmpty(machineNames)
            ? new Dictionary<int, (string group, int num)>()
            : machineNames.Split(',').Select(m => m.Trim()).Select((machineName, idx) =>
            {
              if (!char.IsDigit(machineName.Last()))
              {
                return null;
              }
              var group = new string(machineName.Reverse().SkipWhile(char.IsDigit).Reverse().ToArray());
              if (int.TryParse(machineName.Substring(group.Length), out var num))
              {
                return new { iccMc = idx + 1, group = group, num = num };
              }
              else
              {
                return null;
              }
            })
            .Where(x => x != null)
            .ToDictionary(x => x.iccMc, x => (group: x.group, num: x.num))
        };
        Log.Debug("Using station names {@names}", StationNames);

        var machineIps = config.GetValue<string>("Machine IP Addresses");
        MachineConnection = new CncMachineConnection(
          string.IsNullOrEmpty(machineIps) ? Enumerable.Empty<string>() : machineIps.Split(',').Select(s => s.Trim())
        );

        var connStr = config.GetValue<string>("Connection String", defaultValue: null);

        _icc = new NiigataICC(programDir, connStr, StationNames);
        var createLog = new CreateCellState(cfg, StationNames, MachineConnection);

        _machControl = new NiigataMachineControl(LogDBConfig, _icc, MachineConnection, StationNames);

        IAssignPallets assign;
        if (customAssignment != null)
        {
          assign = customAssignment(StationNames, MachineConnection);
        }
        else
        {
          assign = new MultiPalletAssign(new IAssignPallets[] {
            new AssignNewRoutesOnPallets(StationNames),
            new SizedQueues(cfg.Queues)
          });
        }

        decrementJobs = decrementJobs ?? new DecrementNotYetStartedJobs();

        _sync = new SyncPallets(LogDBConfig, _icc, assign, createLog, decrementJobs, cfg, s => OnNewCurrentStatus?.Invoke(s));
        _jobControl = new NiigataJobs(j: LogDBConfig,
                                      st: cfg,
                                      sy: _sync,
                                      statNames: StationNames,
                                      requireRawMatQ: config.GetValue<bool>("Require Raw Material Queue", false),
                                      requireInProcQ: config.GetValue<bool>("Require In-Process Queues", false),
                                      requireProgsInJobs: config.GetValue<bool>("Require Programs In Jobs", true),
                                      onNewJobs: j => OnNewJobs?.Invoke(j),
                                      onEditMatInLog: o => OnEditMaterialInLog?.Invoke(o));
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
      _sync.StartThread();
    }

    public void Dispose()
    {
      LogDBConfig.NewLogEntry -= RaiseNewLogEntry;
      _jobControl = null;
      if (SyncPallets != null) _sync.Dispose();
      _sync = null;
    }

    public IJobControl JobControl { get => _jobControl; }
    public IOldJobDecrement OldJobDecrement { get => null; }
    public IMachineControl MachineControl => _machControl;

    public IRepository OpenRepository()
    {
      return LogDBConfig.OpenConnection();
    }
  }

  public static class NiigataProgram
  {
    public static void Main()
    {
#if DEBUG
      var useService = false;
#else
      var useService = true;
#endif
      Program.Run(useService, (cfg, fmsSt) =>
        new FMSImplementation()
        {
          Backend = new NiigataBackend(cfg.GetSection("Niigata"), fmsSt, startSyncThread: true),
          Name = "Niigata",
          Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
        });
    }
  }
}
