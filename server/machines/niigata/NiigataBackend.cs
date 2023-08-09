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
using Microsoft.Extensions.Configuration;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.Tests")]

namespace BlackMaple.FMSInsight.Niigata
{
  public class NiigataBackend : IFMSBackend, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<NiigataBackend>();

    private readonly JobsAndQueuesFromDb<CellState> _jobsAndQueues;

    public IJobControl JobControl => _jobsAndQueues;
    public IQueueControl QueueControl => _jobsAndQueues;
    public IMachineControl MachineControl { get; }
    public RepositoryConfig RepoConfig { get; }

    public event NewCurrentStatus OnNewCurrentStatus;

    public NiigataBackend(
      IConfigurationSection config,
      FMSSettings cfg,
      SerialSettings serialSt,
      bool startSyncThread,
      Func<NiigataStationNames, ICncMachineConnection, IAssignPallets> customAssignment = null,
      Func<NiigataStationNames, INiigataCommunication, ICheckJobsValid> customJobCheck = null
    )
    {
      try
      {
        Log.Information("Starting niigata backend");
        RepoConfig = RepositoryConfig.InitializeEventDatabase(
          serialSt,
          System.IO.Path.Combine(cfg.DataDirectory, "niigatalog.db"),
          oldJobDbFile: System.IO.Path.Combine(cfg.DataDirectory, "niigatajobs.db")
        );

        var programDir = config.GetValue<string>("Program Directory");
        if (!System.IO.Directory.Exists(programDir))
        {
          Log.Error("Program directory {dir} does not exist", programDir);
        }

        if (serialSt.SerialType == SerialType.AssignOneSerialPerCycle)
        {
          Log.Error("Niigata backend does not support serials assigned per cycle");
        }

        var reclampNames = config.GetValue<string>("Reclamp Group Names");
        var machineNames = config.GetValue<string>("Machine Names");
        var stationNames = new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>(
            string.IsNullOrEmpty(reclampNames)
              ? Enumerable.Empty<string>()
              : reclampNames.Split(',').Select(s => s.Trim())
          ),
          IccMachineToJobMachNames = string.IsNullOrEmpty(machineNames)
            ? new Dictionary<int, (string group, int num)>()
            : machineNames
              .Split(',')
              .Select(m => m.Trim())
              .Select(
                (machineName, idx) =>
                {
                  if (!char.IsDigit(machineName.Last()))
                  {
                    return null;
                  }
                  var group = new string(machineName.Reverse().SkipWhile(char.IsDigit).Reverse().ToArray());
                  if (int.TryParse(machineName.Substring(group.Length), out var num))
                  {
                    return new
                    {
                      iccMc = idx + 1,
                      group = group,
                      num = num
                    };
                  }
                  else
                  {
                    return null;
                  }
                }
              )
              .Where(x => x != null)
              .ToDictionary(x => x.iccMc, x => (group: x.group, num: x.num))
        };
        Log.Debug("Using station names {@names}", stationNames);

        var machineIps = config.GetValue<string>("Machine IP Addresses");
        var machConn = new CncMachineConnection(
          string.IsNullOrEmpty(machineIps)
            ? Enumerable.Empty<string>()
            : machineIps.Split(',').Select(s => s.Trim())
        );

        var connStr = config.GetValue<string>("Connection String", defaultValue: null);

        var icc = new NiigataICC(programDir, connStr, stationNames);
        var createLog = new CreateCellState(cfg, stationNames, machConn);

        MachineControl = new NiigataMachineControl(RepoConfig, icc, machConn, stationNames);

        IAssignPallets assign;
        if (customAssignment != null)
        {
          assign = customAssignment(stationNames, machConn);
        }
        else
        {
          assign = new MultiPalletAssign(
            new IAssignPallets[] { new AssignNewRoutesOnPallets(stationNames), new SizedQueues(cfg.Queues) }
          );
        }

        ICheckJobsValid checkJobsValid;
        if (customJobCheck != null)
        {
          checkJobsValid = customJobCheck(stationNames, icc);
        }
        else
        {
          checkJobsValid = new CheckJobsMatchNiigata(
            cfg,
            stationNames,
            requireProgramsInJobs: config.GetValue<bool>("Require Programs In Jobs", true),
            icc
          );
        }

        var syncSt = new SyncNiigataPallets(icc, createLog, assign);

        _jobsAndQueues = new JobsAndQueuesFromDb<CellState>(
          RepoConfig,
          cfg,
          s => OnNewCurrentStatus?.Invoke(s),
          syncSt,
          checkJobsValid,
          allowQuarantineToCancelLoad: false,
          addJobsAsCopiedToSystem: true
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
