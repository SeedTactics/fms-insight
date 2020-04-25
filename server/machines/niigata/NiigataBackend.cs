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

    private JobLogDB _log;
    private JobDB _jobs;

    public NiigataBackend(
      IConfigurationSection config,
      FMSSettings cfg,
      Func<JobLogDB, IRecordFacesForPallet, IAssignPallets> customAssignment = null
    )
    {
      try
      {
        Log.Information("Starting niigata backend");
        _log = new JobLogDB(cfg);
        _log.Open(
            System.IO.Path.Combine(cfg.DataDirectory, "log.db"),
            startingSerial: cfg.StartingSerial
        );
        _jobs = new JobDB();
        _jobs.Open(
          System.IO.Path.Combine(cfg.DataDirectory, "jobs.db")
        );

        var programDir = config.GetValue<string>("Program Directory");
        if (!System.IO.Directory.Exists(programDir))
        {
          Log.Error("Program directory {dir} does not exist", programDir);
        }

        var connStr = config.GetValue<string>("Connection String");

        NiigataICC = new NiigataICC(_jobs, programDir, connStr);
        var recordFaces = new RecordFacesForPallet(_log);
        var createLog = new CreateCellState(_log, _jobs, recordFaces, cfg);

        IAssignPallets assign;
        if (customAssignment != null)
        {
          assign = customAssignment(_log, recordFaces);
        }
        else
        {
          assign = new AssignPallets(recordFaces);
        }
        SyncPallets = new SyncPallets(_jobs, _log, NiigataICC, assign, createLog);
        NiigataJobs = new NiigataJobs(_jobs, _log, cfg, SyncPallets);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Unhandled exception when initializing niigata backend");
      }
    }

    public void Dispose()
    {
      if (NiigataJobs != null) NiigataJobs.Dispose();
      NiigataJobs = null;
      if (SyncPallets != null) SyncPallets.Dispose();
      SyncPallets = null;
      if (NiigataICC != null) NiigataICC.Dispose();
      if (_log != null) _log.Close();
      _log = null;
      if (_jobs != null) _jobs.Close();
      _jobs = null;
    }

    public IInspectionControl InspectionControl()
    {
      return _log;
    }

    public IJobDatabase JobDatabase()
    {
      return _jobs;
    }

    public IOldJobDecrement OldJobDecrement()
    {
      return null;
    }

    public IJobControl JobControl()
    {
      return NiigataJobs;
    }

    public ILogDatabase LogDatabase()
    {
      return _log;
    }

    public NiigataJobs NiigataJobs { get; private set; }
    public SyncPallets SyncPallets { get; private set; }
    public NiigataICC NiigataICC { get; }
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
          Backend = new NiigataBackend(cfg.GetSection("Niigata"), fmsSt),
          Name = "Niigata",
          Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
        });
    }
  }
}
