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
using System.Threading.Tasks;
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

  public sealed class MakinoSync(MakinoSettings settings, IMakinoDB makinoDb)
    : ISynchronizeCellState<MakinoCellState>
  {
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MakinoSync>();

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

      var mcs = makinoDb.Devices().Values.Where(d => d.Location == PalletLocationEnum.Machine).ToList();
      string allMachineNames = string.Join(
        ',',
        mcs.OrderBy(m => m.StationGroup).ThenBy(m => m.Num).Select(m => m.StationGroup + m.Num.ToString())
      );

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
                foreach (var statNum in stop.Stations)
                {
                  if (!mcs.Any(m => stop.StationGroup == m.StationGroup && statNum == m.Num))
                    ret.Add(
                      $"The flexibility plan for part {j.PartName} uses machine {stop.StationGroup} number {statNum}, but that machine does not exist in the Makino system."
                        + $"  The makino system contains machines {allMachineNames}"
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
      return CalculateCellState(db, DateTime.UtcNow);
    }

    public MakinoCellState CalculateCellState(IRepository db, DateTime nowUTC)
    {
      var lastLog = db.MaxLogDate();
      if (nowUTC.Subtract(lastLog) > TimeSpan.FromDays(30))
      {
        lastLog = nowUTC.AddDays(-30);
      }

      var newEvts = new LogBuilder(makinoDb, db).CheckLogs(lastLog, nowUTC);

      var st = makinoDb.LoadCurrentInfo(db, nowUTC);

      var notCopied = db.LoadJobsNotCopiedToSystem(
        nowUTC.AddHours(-10),
        nowUTC.AddMinutes(20),
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
          var tcs = new TaskCompletionSource<bool>();
          using var fw = new FileSystemWatcher(settings.ADEPath, fileName);
          fw.Deleted += (s, e) =>
          {
            if (e.Name == fileName)
            {
              tcs.SetResult(true);
            }
          };
          fw.EnableRaisingEvents = true;
          OrderXML.WriteOrderXML(fullPath, jobsToSend, settings.DownloadOnlyOrders);
          if (Task.WaitAny([tcs.Task, Task.Delay(TimeSpan.FromSeconds(10))]) == 1)
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
