/* Copyright (c) 2024, John Lenz

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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Makino
{
  public class MakinoCellState : ICellState
  {
    public required CurrentStatus CurrentStatus { get; init; }

    public required ImmutableList<HistoricJob> JobsNotYetCopied { get; init; }

    public required bool StateUpdated { get; init; }

    public TimeSpan TimeUntilNextRefresh => TimeSpan.FromMinutes(1);
  }

  public sealed class MakinoSync(MakinoSettings settings) : ISynchronizeCellState<MakinoCellState>
  {
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MakinoBackend>();

    public bool AllowQuarantineToCancelLoad => false;
    public bool AddJobsAsCopiedToSystem => false;

    public event Action NewCellState
    {
      // Rely on the timer to refresh the state
      add { }
      remove { }
    }

    public IEnumerable<string> CheckNewJobs(IRepository db, NewJobs jobs)
    {
      var ret = new List<string>();

      string machineName = "MC";
      using (var makino = settings.OpenMakinoConnection())
      {
        var mc = makino.Devices().Values.FirstOrDefault(d => d.Location == PalletLocationEnum.Machine);
        if (mc != null)
        {
          machineName = mc.StationGroup;
        }
      }

      foreach (var j in jobs.Jobs)
      {
        if (j.Processes.Count != 1)
        {
          // current problem is in LogBuilder, material IDs are not looked up between processes
          // and there is no queue support
          ret.Add(
            "FMS Insight does not support multiple processes currently, please change "
              + j.PartName
              + " to have one process."
          );
        }
        else
        {
          foreach (var proc in j.Processes)
          {
            if (proc.Paths.Count == 0)
            {
              ret.Add(j.PartName + " does not have any paths.");
            }
            else if (proc.Paths.Count >= 2)
            {
              ret.Add(
                "FMS Insight does not support paths with the same color, please make sure each path has a distinct color in "
                  + j.PartName
              );
            }
            else
            {
              foreach (var stop in proc.Paths[0].Stops)
              {
                if (stop.StationGroup != machineName)
                {
                  ret.Add(
                    "The Makino machine name is "
                      + machineName
                      + " but the flexibility plan uses "
                      + stop.StationGroup
                      + " in part "
                      + j.PartName
                      + ", please update the flexibility plan."
                  );
                }
              }
            }
          }
        }
      }

      return ret;
    }

    public bool ErrorDownloadingJobs { get; private set; } = false;

    public MakinoCellState CalculateCellState(IRepository db)
    {
      var lastLog = db.MaxLogDate();
      if (DateTime.UtcNow.Subtract(lastLog) > TimeSpan.FromDays(30))
      {
        lastLog = DateTime.UtcNow.AddDays(-30);
      }

      using var makinoDB = settings.OpenMakinoConnection();
      var newEvts = new LogBuilder(makinoDB, db).CheckLogs(lastLog);

      var st = makinoDB.LoadCurrentInfo(db);

      var notCopied = db.LoadJobsNotCopiedToSystem(
        DateTime.UtcNow.AddHours(-10),
        DateTime.UtcNow.AddMinutes(20),
        includeDecremented: false
      );

      return new MakinoCellState
      {
        CurrentStatus = ErrorDownloadingJobs
          ? st with
          {
            Alarms = st.Alarms.Add(
              "Unable to copy orders to Makino: check that the Makino software is running"
            )
          }
          : st,
        JobsNotYetCopied = notCopied,
        StateUpdated = newEvts
      };
    }

    public bool ApplyActions(IRepository db, MakinoCellState state)
    {
      List<HistoricJob> jobsToSend = [];

      foreach (var j in state.JobsNotYetCopied)
      {
        if (state.CurrentStatus.Jobs.ContainsKey(j.UniqueStr))
        {
          // was copied but must have crashed before recording the copy, or
          // makino processed them after the 10 second timeout
          db.MarkJobCopiedToSystem(j.UniqueStr);
        }
        else
        {
          jobsToSend.Add(j);
        }
      }

      if (jobsToSend.Count > 0)
      {
        var fileName = "insight" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".xml";
        var fullPath = Path.Combine(settings.ADEPath, fileName);
        try
        {
          using var fw = new FileSystemWatcher(settings.ADEPath, fileName);
          fw.EnableRaisingEvents = true;
          OrderXML.WriteOrderXML(fullPath, jobsToSend, settings.DownloadOnlyOrders);
          if (fw.WaitForChanged(WatcherChangeTypes.Deleted, TimeSpan.FromSeconds(10)).TimedOut)
          {
            Log.Error("Makino did not process new jobs, perhaps the Makino software is not running?");
            ErrorDownloadingJobs = true;
          }
          else
          {
            ErrorDownloadingJobs = false;
            foreach (var j in jobsToSend)
            {
              db.MarkJobCopiedToSystem(j.UniqueStr);
            }
          }
        }
        finally
        {
          File.Delete(fullPath);
        }
        return true;
      }
      else
      {
        ErrorDownloadingJobs = false;
        return false;
      }
    }

    public bool DecrementJobs(IRepository db, MakinoCellState state)
    {
      // do nothing
      return false;
    }
  }
}
