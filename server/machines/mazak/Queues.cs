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

    public bool CurrentQueueMismatch { get; private set; }

    public MazakQueues(JobLogDB log, JobDB jDB, IWriteData trans)
    {
      _jobDB = jDB;
      _log = log;
      _transDB = trans;
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

      AttemptToUnallocateCastings(schs);
      CalculateTargetMatQty(mazakData, schs);
      AttemptToAllocateCastings(schs, mazakData);
      return UpdateMazakMaterialCounts(schs);
    }


    private class ScheduleWithQueuesProcess
    {
      public MazakScheduleProcessRow SchProcRow { get; set; }
      public string InputQueue { get; set; }
      public int PathGroup { get; set; }
      public int? TargetMaterialCount { get; set; }
    }

    private class ScheduleWithQueues
    {
      public MazakScheduleRow SchRow { get; set; }
      public string Unique { get; set; }
      public JobPlan Job { get; set; }
      public bool LowerPriorityScheduleMatchingPartSkipped { get; set; }
      public Dictionary<int, ScheduleWithQueuesProcess> Procs { get; set; }
    }

    private IEnumerable<ScheduleWithQueues> LoadSchedules(MazakSchedulesAndLoadActions mazakData)
    {
      var loadOpers = mazakData.LoadActions;
      var schs = new List<ScheduleWithQueues>();
      var pending = _log.AllPendingLoads();
      var skippedParts = new HashSet<string>();
      foreach (var schRow in mazakData.Schedules.OrderBy(s => s.DueDate).ThenBy(s => s.Priority))
      {
        if (!MazakPart.IsSailPart(schRow.PartName)) continue;

        MazakPart.ParseComment(schRow.Comment, out string unique, out var procToPath, out bool manual);

        var job = _jobDB.LoadJob(unique);
        if (job == null) continue;

        // only if no load or unload action is in process
        bool foundJobAtLoad = false;
        foreach (var action in loadOpers)
        {
          if (action.Unique == job.UniqueStr && action.Path == procToPath.PathForProc(action.Process))
          {
            foundJobAtLoad = true;
            skippedParts.Add(job.PartName);
            log.Debug("Not editing queued material because {uniq} is in the process of being loaded or unload with action {@action}", job.UniqueStr, action);
            break;
          }
        }
        foreach (var pendingLoad in pending)
        {
          var s = pendingLoad.Key.Split(',');
          if (schRow.PartName == s[0])
          {
            skippedParts.Add(job.PartName);
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
          LowerPriorityScheduleMatchingPartSkipped = skippedParts.Contains(job.PartName),
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
            PathGroup = job.GetPathGroup(process: proc, path: path),
            InputQueue = job.GetInputQueue(process: proc, path: path)
          });
        }
        if (!missingProc && sch.Procs.Values.Any(p => !string.IsNullOrEmpty(p.InputQueue)))
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
      foreach (var job in schs.GroupBy(s => s.Unique))
      {
        var uniqueStr = job.Key;
        var numProc = job.First().Job.NumProcesses;
        log.Debug("Checking job {uniq} with schedules {@pathGroup}", uniqueStr, job);

        // do processes 2+ so that correct material counts are present during check for
        // castings in proc1.
        for (int proc = 2; proc <= numProc; proc++)
        {
          CheckInProcessMaterial(job.First().Job, job, proc);
        }
        CheckCastingsForProc1(job, mazakData);
      }
    }

    private void CheckCastingsForProc1(IGrouping<string, ScheduleWithQueues> job, MazakSchedulesAndLoadActions mazakData)
    {
      string uniqueStr = job.Key;

      // group the paths for this process by input queue
      var proc1PathsByQueue = job
        .Where(s => s.SchRow.PlanQuantity > CountCompletedOrMachiningStarted(s))
        .Select(s => s.Procs[1])
        .Where(p => !string.IsNullOrEmpty(p.InputQueue))
        .GroupBy(p => p.InputQueue);
      foreach (var queueGroup in proc1PathsByQueue)
      {
        var matInQueue =
          _log.GetMaterialInQueue(queueGroup.Key)
          .Where(m => m.Unique == uniqueStr && FindNextProcess(m.MaterialID) == 1)
          .Count()
          ;
        var curMazakSchMat = queueGroup.Select(s => s.SchProcRow.ProcessMaterialQuantity).Sum();

        log.Debug("Calculated {matInQueue} parts in queue and {mazakCnt} parts in mazak for job {uniq} proccess 1",
          matInQueue, curMazakSchMat, uniqueStr);

        if (queueGroup.Count() == 1)
        {
          // put all material on the single path
          if (matInQueue != curMazakSchMat)
          {
            queueGroup.First().TargetMaterialCount = matInQueue;
          }
        }
        else
        {
          // keep each path at least fixQty piece of material
          if (matInQueue > curMazakSchMat)
          {
            int remain = matInQueue - curMazakSchMat;
            var potentialPaths =
              queueGroup
                .Where(p => p.SchProcRow.ProcessMaterialQuantity == 0)
                .OrderBy(p => p.SchProcRow.ProcessExecuteQuantity);
            foreach (var p in potentialPaths)
            {
              int fixQty = p.SchProcRow.FixQuantity;
              if (fixQty <= remain)
              {
                remain -= fixQty;
                p.TargetMaterialCount = fixQty;
              }
              if (remain <= 0) break;
            }
          }
          else if (matInQueue < curMazakSchMat)
          {
            int toRemove = curMazakSchMat - matInQueue;
            var potentialPaths =
              queueGroup
                .Where(p => p.SchProcRow.ProcessMaterialQuantity > 0)
                .OrderByDescending(p => p.SchProcRow.ProcessMaterialQuantity)
                .ThenBy(p => p.SchProcRow.ProcessExecuteQuantity);
            foreach (var p in potentialPaths)
            {
              if (toRemove >= p.SchProcRow.ProcessMaterialQuantity)
              {
                p.TargetMaterialCount = 0;
                toRemove -= p.SchProcRow.ProcessMaterialQuantity;
              }
              else
              {
                p.TargetMaterialCount = p.SchProcRow.ProcessMaterialQuantity - toRemove;
                toRemove = 0;
              }
              if (toRemove <= 0) break;
            }
          }
        }
      }

      // now deal with the non-input-queue parts. They could have larger material than planned quantity
      // if the schedule has been decremented
      foreach (var sch in job)
      {
        if (string.IsNullOrEmpty(sch.Procs[1].InputQueue)
              && sch.SchRow.PlanQuantity <= CountCompletedOrMachiningStarted(sch)
              && sch.Procs[1].SchProcRow.ProcessMaterialQuantity > 0
           )
        {
          sch.Procs[1].TargetMaterialCount = 0;
        }
      }
    }

    private class UnableToFindPathGroup : Exception { }

    private void CheckInProcessMaterial(JobPlan jobPlan, IGrouping<string, ScheduleWithQueues> job, int proc)
    {
      string uniqueStr = job.Key;
      var paths = job.Select(s => s.Procs[proc]);

      foreach (var path in paths)
      {
        if (string.IsNullOrEmpty(path.InputQueue)) continue;

        try
        {
          int matInQueue;

          if (paths.Count() == 1)
          {
            // just check for parts with the correct unique and next process
            matInQueue = _log.GetMaterialInQueue(path.InputQueue)
              .Where(m => m.Unique == uniqueStr && FindNextProcess(m.MaterialID) == proc)
              .Count()
              ;
          }
          else
          {
            // need a more complicated test involving path groups
            matInQueue = _log.GetMaterialInQueue(path.InputQueue)
              .Where(m => m.Unique == uniqueStr && DoesNextProcessAndPathGroupMatch(jobPlan, m, proc - 1, path.PathGroup))
              .Count()
              ;
          }

          if (matInQueue != path.SchProcRow.ProcessMaterialQuantity)
          {
            path.TargetMaterialCount = matInQueue;
          }
        }
        catch (UnableToFindPathGroup)
        {
          Log.Warning("Unable to calculate path group for material in queue for {job} process {proc}," +
            " ignoring queue material updates to mazak schedule.", jobPlan.UniqueStr, proc);
        }
      }
    }

    private void AttemptToUnallocateCastings(IEnumerable<ScheduleWithQueues> schedules)
    {
      // If a schedule is decremented, there could be more allocated castings than total planned quantity.  In that case, unallocate those
      // so that they can be assigned to a new schedule

      foreach (var job in schedules.GroupBy(s => s.Unique))
      {
        var uniqueStr = job.Key;
        var proc1PathsByQueue = job
          .Where(sch => !string.IsNullOrEmpty(sch.Procs[1].InputQueue))
          .GroupBy(p => p.Procs[1].InputQueue);
        foreach (var queueGroup in proc1PathsByQueue)
        {
          var matInQueue =
            _log.GetMaterialInQueue(queueGroup.Key)
            .Where(m => m.Unique == uniqueStr && FindNextProcess(m.MaterialID) == 1)
            .ToList()
            ;

          var extra = matInQueue.Count();
          foreach (var sch in queueGroup)
          {
            extra -= sch.SchRow.PlanQuantity - CountCompletedOrMachiningStarted(sch);
          }

          if (extra > 0)
          {
            _log.MarkCastingsAsUnallocated(matInQueue.TakeLast(extra).Select(m => m.MaterialID));
          }
        }
      }
    }

    private void AttemptToAllocateCastings(IEnumerable<ScheduleWithQueues> schs, MazakSchedulesAndLoadActions mazakData)
    {
      // go through each job and check for castings in an input queue that have not yet been assigned to a job
      foreach (var job in schs.Where(s => !s.LowerPriorityScheduleMatchingPartSkipped).OrderBy(s => s.Job.RouteStartingTimeUTC).GroupBy(s => s.Unique))
      {
        var uniqueStr = job.Key;
        var partName = job.First().Job.PartName;
        var numProc = job.First().Job.NumProcesses;
        log.Debug("Checking job {uniq} with for unallocated castings", uniqueStr);

        bool needRecheckProc1 = false;

        // check for any raw material to assign to the job
        var proc1Queues =
          job
          .Select(s => s.Procs[1].InputQueue)
          .Where(q => !string.IsNullOrEmpty(q))
          .Distinct();
        foreach (var queue in proc1Queues)
        {
          var inQueue = _log.GetMaterialInQueue(queue);

          var remain = job.Select(s =>
            s.SchRow.PlanQuantity - CountCompletedOrMachiningStarted(s)
          ).Sum();
          var alreadyAssigned =
            inQueue
            .Where(m => m.Unique == uniqueStr && FindNextProcess(m.MaterialID) == 1)
            .Count();
          var partsToAllocate =
            inQueue
            .Where(m => string.IsNullOrEmpty(m.Unique) && m.PartName == partName)
            .Any();
          log.Debug("Checking unique {uniq} for unallocated castings in queue {queue}, " +
            " found {remain} remaining parts, {assigned} already assigned parts, {toAllocate} parts to allocate",
            uniqueStr, queue, remain, alreadyAssigned, partsToAllocate);

          if (remain > alreadyAssigned && partsToAllocate)
          {
            var newMats = _log.AllocateCastingsInQueue(queue, partName, uniqueStr, numProc, remain - alreadyAssigned);
            log.Debug("Alloacted new ids {@matIds} to {unique}, recalculating proc1 castings", newMats, uniqueStr);
            needRecheckProc1 = true;
          }
        }

        if (needRecheckProc1)
          CheckCastingsForProc1(job, mazakData);
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

    private int FindNextProcess(long matId)
    {
      var matLog = _log.GetLogForMaterial(matId);
      var lastProc =
        matLog
        .SelectMany(e => e.Material)
        .Where(m => m.MaterialID == matId)
        .Select(m => m.Process)
        .DefaultIfEmpty(0)
        .Max();
      return lastProc + 1;
    }

    private bool DoesNextProcessAndPathGroupMatch(JobPlan job, JobLogDB.QueuedMaterial qm, int proc, int pathGroup)
    {
      var matLog = _log.GetLogForMaterial(qm.MaterialID);
      var lastProc =
        matLog
        .SelectMany(e => e.Material)
        .Where(m => m.MaterialID == qm.MaterialID)
        .Select(m => m.Process)
        .DefaultIfEmpty(0)
        .Max();

      if (lastProc != proc) return false;

      //now try and calculate path.  Just check pallet.
      var lastPallet =
        matLog
        .SelectMany(e => e.Material.Select(m => new { log = e, mat = m }))
        .Where(x => x.mat.MaterialID == qm.MaterialID && x.mat.Process == proc)
        .Select(x => x.log.Pallet)
        .Where(x => !string.IsNullOrEmpty(x))
        .DefaultIfEmpty("")
        .First()
        ;

      if (string.IsNullOrEmpty(lastPallet)) throw new UnableToFindPathGroup();

      Log.Debug("Calculated last pallet {pal} for {@qm} and proc {proc}", lastPallet, qm);

      for (int path = 1; path <= job.GetNumPaths(proc); path++)
      {
        if (job.HasPallet(proc, path, lastPallet))
        {
          return job.GetPathGroup(proc, path) == pathGroup;
        }
      }

      throw new UnableToFindPathGroup();
    }

    private int CountCompletedOrMachiningStarted(ScheduleWithQueues sch)
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