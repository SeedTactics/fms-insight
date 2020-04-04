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
using System.Linq;
using System.Collections.Generic;
using Serilog;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public interface IQueueSyncFault
  {
    bool CurrentQueueMismatch { get; }
  }

  public class MazakQueues : IQueueSyncFault
  {
    private static ILogger log = Serilog.Log.ForContext<MazakQueues>();

    private JobLogDB _log;
    private JobDB _jobDB;
    private IWriteData _transDB;
    private bool _waitForAllCastings;

    public bool CurrentQueueMismatch { get; private set; }

    public MazakQueues(JobLogDB log, JobDB jDB, IWriteData trans, bool waitForAllCastings)
    {
      _jobDB = jDB;
      _log = log;
      _transDB = trans;
      _waitForAllCastings = waitForAllCastings;
      CurrentQueueMismatch = false;
    }

    public void CheckQueues(MazakSchedulesAndLoadActions mazakData)
    {
      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(3), true))
      {
        log.Debug("Unable to obtain mazak db lock, trying again soon.");
        return;
      }
      try
      {
        var transSet = CalculateScheduleChanges(mazakData);

        if (transSet != null && transSet.Schedules.Count() > 0)
        {
          _transDB.Save(transSet, "Setting material from queues");
        }
        CurrentQueueMismatch = false;
      }
      catch (Exception ex)
      {
        CurrentQueueMismatch = true;
        log.Error(ex, "Error checking for new material");
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }
    }

    public MazakWriteData CalculateScheduleChanges(MazakSchedulesAndLoadActions mazakData)
    {
      log.Debug("Starting check for new queued material to add to mazak");

      var schs = LoadSchedules(mazakData);
      if (!schs.Any()) return null;

      CalculateTargetMatQty(mazakData, schs);
      return UpdateMazakMaterialCounts(schs);
    }


    private class ScheduleWithQueuesProcess
    {
      public MazakScheduleProcessRow SchProcRow { get; set; }
      public string InputQueue { get; set; }
      public string Casting { get; set; }
      public int Path { get; set; }
      public int? TargetMaterialCount { get; set; }
    }

    private class ScheduleWithQueues
    {
      public MazakScheduleRow SchRow { get; set; }
      public string Unique { get; set; }
      public JobPlan Job { get; set; }
      public bool LowerPriorityScheduleMatchingCastingSkipped { get; set; }
      public Dictionary<int, ScheduleWithQueuesProcess> Procs { get; set; }
    }

    private IEnumerable<ScheduleWithQueues> LoadSchedules(MazakSchedulesAndLoadActions mazakData)
    {
      var loadOpers = mazakData.LoadActions;
      var schs = new List<ScheduleWithQueues>();
      var pending = _log.AllPendingLoads();
      var skippedCastings = new HashSet<string>();
      foreach (var schRow in mazakData.Schedules.OrderBy(s => s.DueDate).ThenBy(s => s.Priority))
      {
        if (!MazakPart.IsSailPart(schRow.PartName)) continue;

        MazakPart.ParseComment(schRow.Comment, out string unique, out var procToPath, out bool manual);

        var job = _jobDB.LoadJob(unique);
        if (job == null) continue;

        var casting = job.GetCasting(procToPath.PathForProc(1));
        if (string.IsNullOrEmpty(casting))
        {
          casting = job.PartName;
        }

        // only if no load or unload action is in process
        bool foundJobAtLoad = false;
        foreach (var action in loadOpers)
        {
          if (action.Unique == job.UniqueStr && action.Path == procToPath.PathForProc(action.Process))
          {
            foundJobAtLoad = true;
            skippedCastings.Add(casting);
            log.Debug("Not editing queued material because {uniq} is in the process of being loaded or unload with action {@action}", job.UniqueStr, action);
            break;
          }
        }
        foreach (var pendingLoad in pending)
        {
          var s = pendingLoad.Key.Split(',');
          if (schRow.PartName == s[0])
          {
            skippedCastings.Add(casting);
            foundJobAtLoad = true;
            log.Debug("Not editing queued material because found a pending load {@pendingLoad}", pendingLoad);
            break;
          }
        }
        if (foundJobAtLoad) continue;

        // start building the schedule
        var sch = new ScheduleWithQueues()
        {
          SchRow = schRow,
          Unique = unique,
          Job = job,
          LowerPriorityScheduleMatchingCastingSkipped = skippedCastings.Contains(casting),
          Procs = new Dictionary<int, ScheduleWithQueuesProcess>(),
        };
        bool missingProc = false;
        for (int proc = 1; proc <= job.NumProcesses; proc++)
        {
          MazakScheduleProcessRow schProcRow = null;
          foreach (var row in schRow.Processes)
          {
            if (row.ProcessNumber == proc)
            {
              schProcRow = row;
              break;
            }
          }
          if (schProcRow == null)
          {
            log.Error("Unable to find process {proc} for job {uniq} and schedule {schid}", proc, job.UniqueStr, schRow.Id);
            missingProc = true;
            break;
          }
          var path = procToPath.PathForProc(proc);
          sch.Procs.Add(proc, new ScheduleWithQueuesProcess()
          {
            SchProcRow = schProcRow,
            Path = path,
            InputQueue = job.GetInputQueue(process: proc, path: path),
            Casting = proc == 1 ? casting : null,
          });
        }
        if (!missingProc)
        {
          schs.Add(sch);
        }
      }
      return schs;
    }

    private void CalculateTargetMatQty(MazakSchedulesAndLoadActions mazakData, IEnumerable<ScheduleWithQueues> schs)
    {
      // go through each job and process, and distribute the queued material among the various paths
      // for the job and process.
      foreach (var schsForJob in schs.GroupBy(s => s.Unique))
      {
        var job = schsForJob.First().Job;
        for (int proc = job.NumProcesses; proc >= 1; proc--)
        {
          CheckAssignedMaterial(job, schsForJob, proc);
        }
      }
      AssignCastings(schs);
    }

    private void CheckAssignedMaterial(JobPlan jobPlan, IEnumerable<ScheduleWithQueues> schsForJob, int proc)
    {
      foreach (var sch in schsForJob.Where(s => !string.IsNullOrEmpty(s.Procs[proc].InputQueue)))
      {
        var schProc = sch.Procs[proc];
        if (string.IsNullOrEmpty(schProc.InputQueue)) continue;

        var matInQueue = QueuedMaterialForLoading(jobPlan, schProc.InputQueue, proc, schProc.Path, _log);
        var numMatInQueue = matInQueue.Count;

        if (proc == 1)
        {
          // check for too many assigned
          var started = CountCompletedOrMachiningStarted(sch);
          if (started + numMatInQueue > sch.SchRow.PlanQuantity)
          {
            _log.MarkCastingsAsUnallocated(
              matInQueue
                .Skip(Math.Max(0, sch.SchRow.PlanQuantity - started))
                .Select(m => m.MaterialID),
              schProc.Casting);
            numMatInQueue = Math.Max(0, sch.SchRow.PlanQuantity - started);
          }
        }

        if (numMatInQueue != schProc.SchProcRow.ProcessMaterialQuantity)
        {
          schProc.TargetMaterialCount = numMatInQueue;
        }
      }

      if (proc == 1)
      {
        // now deal with the non-input-queue raw material. They could have larger material than planned quantity
        // if the schedule has been decremented
        foreach (var sch in schsForJob.Where(s => string.IsNullOrEmpty(s.Procs[proc].InputQueue)))
        {
          if (sch.SchRow.PlanQuantity <= CountCompletedOrMachiningStarted(sch) && sch.Procs[1].SchProcRow.ProcessMaterialQuantity > 0)
          {
            sch.Procs[1].TargetMaterialCount = 0;
          }
        }
      }
    }

    private void AssignCastings(IEnumerable<ScheduleWithQueues> allSchs)
    {
      var schsToAssign =
        allSchs
        .Where(s => !s.LowerPriorityScheduleMatchingCastingSkipped && !string.IsNullOrEmpty(s.Procs[1].InputQueue))
        .OrderBy(s => s.Job.GetSimulatedStartingTimeUTC(1, s.Procs[1].Path));

      foreach (var sch in schsToAssign)
      {
        var schProc1 = sch.Procs[1];
        var started = CountCompletedOrMachiningStarted(sch);
        var curCastings = (schProc1.TargetMaterialCount ?? schProc1.SchProcRow.ProcessMaterialQuantity);

        if (started + curCastings < sch.SchRow.PlanQuantity && curCastings < schProc1.SchProcRow.FixQuantity)
        {
          // find some new castings
          var unassignedCastings =
            _log.GetMaterialInQueue(schProc1.InputQueue)
            .Where(m => string.IsNullOrEmpty(m.Unique) && m.PartNameOrCasting == schProc1.Casting)
            .Count();

          var toAdd =
            _waitForAllCastings
            ? sch.SchRow.PlanQuantity - started - curCastings
            : schProc1.SchProcRow.FixQuantity - curCastings;

          if (toAdd > 0 && unassignedCastings >= toAdd)
          {
            var allocated = _log.AllocateCastingsInQueue(
              queue: schProc1.InputQueue,
              casting: schProc1.Casting,
              unique: sch.Unique,
              part: sch.Job.PartName,
              proc1Path: schProc1.Path,
              numProcesses: sch.Job.NumProcesses,
              count: toAdd);
            if (allocated.Count != toAdd)
            {
              Log.Error("Did not allocate {toAdd} parts from queue! {sch}, {queue}", toAdd, sch, unassignedCastings);
            }
            schProc1.TargetMaterialCount = allocated.Count;
          }
        }
      }
    }

    private MazakWriteData UpdateMazakMaterialCounts(IEnumerable<ScheduleWithQueues> schs)
    {
      var newSchs = new List<MazakScheduleRow>();
      foreach (var sch in schs)
      {
        if (!sch.Procs.Values.Any(p => p.TargetMaterialCount.HasValue)) continue;
        log.Debug("Updating material on schedule {schId} for job {uniq} to {@sch}", sch.SchRow.Id, sch.Unique, sch);

        var newSch = sch.SchRow.Clone();
        newSch.Command = MazakWriteCommand.ScheduleMaterialEdit;
        newSchs.Add(newSch);

        foreach (var newProc in newSch.Processes)
        {
          var oldProc = sch.Procs[newProc.ProcessNumber];
          if (oldProc.TargetMaterialCount.HasValue)
          {
            newProc.ProcessMaterialQuantity = oldProc.TargetMaterialCount.Value;
          }
        }
      }

      return new MazakWriteData()
      {
        Schedules = newSchs
      };
    }

    private static int FindNextProcess(JobLogDB log, long matId)
    {
      var matLog = log.GetLogForMaterial(matId);
      var lastProc =
        matLog
        .SelectMany(e => e.Material)
        .Where(m => m.MaterialID == matId)
        .Select(m => m.Process)
        .DefaultIfEmpty(0)
        .Max();
      return lastProc + 1;
    }

    private static int? FindPathGroup(JobLogDB log, JobPlan job, long matId)
    {
      var details = log.GetMaterialDetails(matId);
      if (details.Paths.Count > 0)
      {
        var path = details.Paths.Aggregate((max, v) => max.Key > v.Key ? max : v);
        return job.GetPathGroup(process: path.Key, path: path.Value);
      }
      else
      {
        Log.Warning("Material {matId} has no path groups! {@details}", matId, details);
        return null;
      }
    }

    public static List<JobLogDB.QueuedMaterial> QueuedMaterialForLoading(JobPlan job, string inputQueue, int proc, int path, JobLogDB log)
    {
      var mats = new List<JobLogDB.QueuedMaterial>();
      foreach (var m in log.GetMaterialInQueue(inputQueue))
      {
        if (m.Unique != job.UniqueStr) continue;
        if (FindNextProcess(log, m.MaterialID) != proc) continue;
        if (job.GetNumPaths(proc) > 1)
        {
          if (FindPathGroup(log, job, m.MaterialID) != job.GetPathGroup(proc, path)) continue;
        }

        mats.Add(m);
      }
      return mats;
    }

    private static int CountCompletedOrMachiningStarted(ScheduleWithQueues sch)
    {
      var cnt = sch.SchRow.CompleteQuantity;
      foreach (var schProcRow in sch.Procs.Values)
      {
        cnt += schProcRow.SchProcRow.ProcessBadQuantity + schProcRow.SchProcRow.ProcessExecuteQuantity;
        if (schProcRow.SchProcRow.ProcessNumber > 1)
          cnt += schProcRow.TargetMaterialCount ?? schProcRow.SchProcRow.ProcessMaterialQuantity;
      }
      return cnt;
    }
  }
}