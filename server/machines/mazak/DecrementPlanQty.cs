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
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public interface IDecrementPlanQty
  {
    void Decrement(DateTime? now = null);
  }

  public class DecrementPlanQty : IDecrementPlanQty
  {
    private JobDB _jobDB;
    private IWriteData _write;
    private IReadDataAccess _read;

    private static Serilog.ILogger Log = Serilog.Log.ForContext<DecrementPlanQty>();

    public DecrementPlanQty(JobDB jdb, IWriteData w, IReadDataAccess r)
    {
      _jobDB = jdb;
      _write = w;
      _read = r;
    }

    public void Decrement(DateTime? now = null)
    {
      // This works in three steps:
      //
      // - First, the planned quantity of the schedules are reduced to equal the total number of parts being machined, completed,
      //   or about to be loaded.
      // - Next, the quantites that were removed are recorded.
      // - Finally, the Queues code in Queues.cs will update the material available and remove any allocated castings on
      //   the schedule.
      //
      // We are protected against crashes and failures:
      // - If the a failure happens before the decrement quantities are recorded, the next decrement will
      //   resume and continue reducing the mazak planned quantities.  The decrement quantities are then
      //   calculated by comparing the original job plan quantity with the current mazak schedule quantity, no matter
      //   when the mazak schedule was reduced.
      //
      // - If a failure happens after the decrement quantities are recorded, the job will not be decremented again because
      //   the code checks if a previous decrement has happended.  The queues code will keep re-trying to sync the material
      //   quantities with the new scheduled quantities.

      var jobs = JobsToDecrement(_read.LoadSchedulesAndLoadActions());
      Log.Debug("Found jobs to decrement {@jobs}", jobs);

      if (jobs.Count == 0) return;

      ReducePlannedQuantity(jobs);
      RecordDecrement(jobs, now);
    }


    private class DecrSchedule
    {
      public MazakScheduleRow Schedule { get; set; }
      public JobPlan Job { get; set; }
      public int Proc1Path { get; set; }
      public int NewPlanQty { get; set; }
    }

    private List<DecrSchedule> JobsToDecrement(MazakSchedulesAndLoadActions schedules)
    {
      var jobs = new List<DecrSchedule>();

      foreach (var sch in schedules.Schedules)
      {
        //parse schedule
        if (!MazakPart.IsSailPart(sch.PartName)) continue;
        if (string.IsNullOrEmpty(sch.Comment)) continue;
        MazakPart.ParseComment(sch.Comment, out string unique, out var procToPath, out bool manual);
        if (manual) continue;

        //load the job
        if (string.IsNullOrEmpty(unique)) continue;
        var job = _jobDB.LoadJob(unique);
        if (job == null) continue;

        // if already decremented, ignore
        if (_jobDB.LoadDecrementsForJob(unique).Any()) continue;


        // check load is in process
        var loadOpers = schedules.LoadActions;
        var loadingQty = 0;
        if (loadOpers != null)
        {
          foreach (var action in loadOpers)
          {
            if (action.Unique == job.UniqueStr && action.Process == 1 && action.LoadEvent && action.Path == procToPath.PathForProc(action.Process))
            {
              loadingQty += action.Qty;
              Log.Debug("Found {uniq} is in the process of being loaded action {@action}", job.UniqueStr, action);
            }
          }
        }

        jobs.Add(new DecrSchedule()
        {
          Schedule = sch,
          Job = job,
          Proc1Path = procToPath.PathForProc(proc: 1),
          NewPlanQty = CountCompletedOrMachiningStarted(sch) + loadingQty
        });
      }

      return jobs;
    }


    private void ReducePlannedQuantity(ICollection<DecrSchedule> jobs)
    {
      var write = new MazakWriteData();
      foreach (var job in jobs)
      {
        if (job.Schedule.PlanQuantity > job.NewPlanQty)
        {
          var newSchRow = job.Schedule.Clone();
          newSchRow.Command = MazakWriteCommand.ScheduleSafeEdit;
          newSchRow.PlanQuantity = job.NewPlanQty;
          newSchRow.Processes.Clear();
          write.Schedules.Add(newSchRow);
        }
      }

      if (write.Schedules.Any())
      {
        _write.Save(write, "Decrement preventing new parts starting");
      }
    }

    private void RecordDecrement(List<DecrSchedule> decrs, DateTime? now)
    {
      var decrAmt = new Dictionary<string, int>();
      var partNames = new Dictionary<string, string>();
      foreach (var decr in decrs)
      {
        var planned = decr.Job.GetPlannedCyclesOnFirstProcess(path: decr.Proc1Path);
        if (planned > decr.NewPlanQty)
        {
          if (decrAmt.ContainsKey(decr.Job.UniqueStr))
          {
            decrAmt[decr.Job.UniqueStr] += planned - decr.NewPlanQty;
          }
          else
          {
            partNames.Add(decr.Job.UniqueStr, decr.Job.PartName);
            decrAmt.Add(decr.Job.UniqueStr, planned - decr.NewPlanQty);
          }
        }
      }

      var oldJobs = _jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow.AddHours(1), includeDecremented: false);
      foreach (var j in oldJobs.Jobs)
      {
        decrAmt[j.UniqueStr] = Enumerable.Range(1, j.GetNumPaths(process: 1)).Select(path => j.GetPlannedCyclesOnFirstProcess(path)).Sum();
        partNames[j.UniqueStr] = j.PartName;
      }

      if (decrs.Count > 0)
      {
        _jobDB.AddNewDecrement(decrAmt.Select(kv => new JobDB.NewDecrementQuantity()
        {
          JobUnique = kv.Key,
          Part = partNames[kv.Key],
          Quantity = kv.Value
        }), now);
      }
    }

    private int CountCompletedOrMachiningStarted(MazakScheduleRow sch)
    {
      var cnt = sch.CompleteQuantity;
      foreach (var schProcRow in sch.Processes)
      {
        cnt += schProcRow.ProcessBadQuantity + schProcRow.ProcessExecuteQuantity;
        if (schProcRow.ProcessNumber > 1)
          cnt += schProcRow.ProcessMaterialQuantity;
      }
      return cnt;
    }
  }
}
