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
    private NiigataICC _icc;
    private NiigataJobs _jobControl;
    private SyncPallets _sync;

    public JobLogDB LogDB { get; private set; }
    public JobDB JobDB { get; private set; }
    public ISyncPallets SyncPallets => _sync;

    public NiigataBackend(
      IConfigurationSection config,
      FMSSettings cfg,
      Func<JobLogDB, IRecordFacesForPallet, IAssignPallets> customAssignment = null
    )
    {
      try
      {
        Log.Information("Starting niigata backend");
        LogDB = new JobLogDB(cfg);
        LogDB.Open(
            System.IO.Path.Combine(cfg.DataDirectory, "log.db"),
            startingSerial: cfg.StartingSerial
        );
        JobDB = new JobDB();
        JobDB.Open(
          System.IO.Path.Combine(cfg.DataDirectory, "jobs.db")
        );

        var programDir = config.GetValue<string>("Program Directory");
        if (!System.IO.Directory.Exists(programDir))
        {
          Log.Error("Program directory {dir} does not exist", programDir);
        }

        var connStr = config.GetValue<string>("Connection String");

        _icc = new NiigataICC(JobDB, programDir, connStr);
        var recordFaces = new RecordFacesForPallet(LogDB);
        var createLog = new CreateCellState(LogDB, JobDB, recordFaces, cfg);

        IAssignPallets assign;
        if (customAssignment != null)
        {
          assign = customAssignment(LogDB, recordFaces);
        }
        else
        {
          assign = new AssignPallets(recordFaces);
        }
        _sync = new SyncPallets(JobDB, LogDB, _icc, assign, createLog);
        _jobControl = new NiigataJobs(JobDB, LogDB, cfg, SyncPallets);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Unhandled exception when initializing niigata backend");
      }
    }

    public void Dispose()
    {
      if (_jobControl != null) _jobControl.Dispose();
      _jobControl = null;
      if (SyncPallets != null) _sync.Dispose();
      _sync = null;
      if (_icc != null) _icc.Dispose();
      _icc = null;
      if (LogDB != null) LogDB.Close();
      LogDB = null;
      if (JobDB != null) JobDB.Close();
      JobDB = null;
    }

    public IInspectionControl InspectionControl()
    {
      return LogDB;
    }

    public IJobDatabase JobDatabase()
    {
      return JobDB;
    }

    public IOldJobDecrement OldJobDecrement()
    {
      return null;
    }

    public IJobControl JobControl()
    {
      return _jobControl;
    }

    public ILogDatabase LogDatabase()
    {
      return LogDB;
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
          Backend = new NiigataBackend(cfg.GetSection("Niigata"), fmsSt),
          Name = "Niigata",
          Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(),
        });
    }
  }
}
