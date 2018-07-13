/* Copyright (c) 2018, John Lenz

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
using System.Threading;
using Serilog;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public class MazakQueues
  {
    private static ILogger log = Serilog.Log.ForContext<MazakQueues>();

    private JobLogDB _log;
    private JobDB _jobDB;
    private LoadOperations _loadOper;
    private IWriteData _transDB;

    public MazakQueues(JobLogDB log, JobDB jDB, LoadOperations loadOper, IWriteData trans)
    {
      _jobDB = jDB;
      _log = log;
      _transDB = trans;
      _loadOper = loadOper;
    }

    public void CheckQueues(IMazakData mazakData)
    {
      try {
        if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(3), true))
        {
          //Downloads usually take a long time and they hold the db lock,
          //so we will probably hit this timeout during the download.
          //For this reason, we wait 3 minutes for the db lock and retry again after only 20 seconds.
          //Thus during the download most of the waiting will be here for the db lock.

          log.Debug("Unable to obtain mazak db lock, trying again soon.");
        } else {

          var loadOpers = _loadOper.CurrentLoadActions();
          var transSet = CalculateScheduleChanges(mazakData, loadOpers);

          if (transSet.Schedule_t.Count > 0) {
            _transDB.ClearTransactionDatabase();
            var logs = new List<string>();
            _transDB.SaveTransaction(transSet, logs, "Setting material from queues");
            foreach (var msg in logs) {
              log.Warning(msg);
            }
          }
        }

      } catch (Exception ex) {
        log.Error(ex, "Error checking for new material");
      } finally {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }
    }

    public TransactionDataSet CalculateScheduleChanges(IMazakData mazakData, IEnumerable<LoadAction> loadOpers)
    {
      log.Debug("Starting check for new queued material to add to mazak");
      var transSet = new TransactionDataSet();

      var schs = LoadSchedules(mazakData, loadOpers);
      if (!schs.Any()) return null;

      AttemptToAllocateCastings(schs);
      CalculateTargetMatQty(mazakData, schs);
      UpdateMazakMaterialCounts(transSet, schs);

      return transSet;
    }


    private class ScheduleWithQueuesProcess
    {
      public ReadOnlyDataSet.ScheduleProcessRow SchProcRow {get;set;}
      public string InputQueue {get;set;}
      public int PathGroup {get;set;}
      public int? TargetMaterialCount {get;set;}
    }

    private class ScheduleWithQueues
    {
      public ReadOnlyDataSet.ScheduleRow SchRow {get;set;}
      public string Unique {get;set;}
      public JobPlan Job {get;set;}
      public Dictionary<int, ScheduleWithQueuesProcess> Procs {get;set;}
    }

    private IEnumerable<ScheduleWithQueues> LoadSchedules(IMazakData mazakData, IEnumerable<LoadAction> loadOpers)
    {
      var schs = new List<ScheduleWithQueues>();
      foreach (var schRow in mazakData.ReadSet.Schedule.OrderBy(s => s.DueDate)) {
        if (!MazakPart.IsSailPart(schRow.PartName)) continue;

        MazakPart.ParseComment(schRow.Comment, out string unique, out var procToPath, out bool manual);

        var job = _jobDB.LoadJob(unique);
        if (job == null) continue;

        // only if no load or unload action is in process
        bool foundJobAtLoad = false;
        foreach (var action in loadOpers) {
          if (action.Unique == job.UniqueStr && action.Path == procToPath.PathForProc(action.Process)) {
            foundJobAtLoad = true;
            log.Debug("Not editing queued material because {uniq} is in the process of being loaded or unload with action {@action}", job.UniqueStr, action);
            break;
          }
        }
        if (foundJobAtLoad) continue;

        // start building the schedule
        var sch = new ScheduleWithQueues() {
          SchRow = schRow,
          Unique = unique,
          Job = job,
          Procs = new Dictionary<int, ScheduleWithQueuesProcess>(),
        };
        bool missingProc = false;
        for (int proc = 1; proc <= job.NumProcesses; proc++) {
          ReadOnlyDataSet.ScheduleProcessRow schProcRow = null;
          foreach (var row in schRow.GetScheduleProcessRows()) {
            if (row.ProcessNumber == proc) {
              schProcRow = row;
              break;
            }
          }
          if (schProcRow == null) {
            log.Error("Unable to find process {proc} for job {uniq} and schedule {schid}", proc, job.UniqueStr, schRow.ScheduleID);
            missingProc = true;
            break;
          }
          var path = procToPath.PathForProc(proc);
          sch.Procs.Add(proc, new ScheduleWithQueuesProcess() {
            SchProcRow = schProcRow,
            PathGroup = job.GetPathGroup(process: proc, path: path),
            InputQueue = job.GetInputQueue(process: proc, path: path)
          });
        }
        if (!missingProc && sch.Procs.Values.Any(p => !string.IsNullOrEmpty(p.InputQueue))) {
          schs.Add(sch);
        }
      }
      return schs;
    }

    private void AttemptToAllocateCastings(IEnumerable<ScheduleWithQueues> schs)
    {
      // go through each job and check for castings in an input queue that have not yet been assigned to a job
      foreach (var job in schs.GroupBy(s => s.Unique)) {
        var uniqueStr = job.Key;
        var partName = job.First().Job.PartName;
        var numProc = job.First().Job.NumProcesses;
        log.Debug("Checking job {uniq} with for unallocated castings", uniqueStr);

        // check for any raw material to assign to the job
        var proc1Queues =
          job
          .Select(s => s.Procs[1].InputQueue)
          .Where(q => !string.IsNullOrEmpty(q))
          .Distinct();
        foreach (var queue in proc1Queues) {
          var remain = job.Select(s =>
            s.SchRow.PlanQuantity - CountCompletedOrInProc(s.SchRow)
          ).Sum();
          if (remain <= 0) continue;

          var partsToAllocate =
            _log.GetMaterialInQueue(queue)
            .Where(m => string.IsNullOrEmpty(m.Unique) && m.PartName == partName)
            .Any();
          if (partsToAllocate) {
            log.Debug("Found {part} in queue {queue} without an assigned job, assigning {remain} to job {uniq}", queue, partName, remain, uniqueStr);
            _log.AllocateCastingsInQueue(queue, partName, uniqueStr, numProc, remain);
          }
        }
      }
    }

    private void CheckCastingsForProc1(IGrouping<string, ScheduleWithQueues> job, IMazakData mazakData)
    {
      string uniqueStr = job.Key;

      // group the paths for this process by input queue
      var proc1PathsByQueue = job.Select(s => s.Procs[1]).GroupBy(p => p.InputQueue);
      foreach (var queueGroup in proc1PathsByQueue) {
        if (string.IsNullOrEmpty(queueGroup.Key)) continue;

        var matInQueue =
          _log.GetMaterialInQueue(queueGroup.Key)
          .Where(m => m.Unique == uniqueStr && FindNextProcess(m.MaterialID) == 1)
          .Count()
          ;
        var curMazakSchMat = queueGroup.Select(s => s.SchProcRow.ProcessMaterialQuantity).Sum();

        log.Debug("Calculated {matInQueue} parts in queue and {mazakCnt} parts in mazak for job {uniq} proccess 1",
          matInQueue, curMazakSchMat, uniqueStr);

        if (queueGroup.Count() == 1) {
          // put all material on the single path
          if (matInQueue != curMazakSchMat) {
            queueGroup.First().TargetMaterialCount = matInQueue;
          }
        } else {
          // keep each path at least fixQty piece of material
          if (matInQueue > curMazakSchMat) {
            int remain = matInQueue - curMazakSchMat;
            var potentialPaths =
              queueGroup
                .Where(p => p.SchProcRow.ProcessMaterialQuantity == 0)
                .OrderBy(p => p.SchProcRow.ProcessExecuteQuantity);
            foreach (var p in potentialPaths) {
              int fixQty = PartFixQuantity(mazakData, p.SchProcRow);
              if (fixQty <= remain) {
                remain -= fixQty;
                p.TargetMaterialCount = fixQty;
              }
              if (remain <= 0) break;
            }
          } else if (matInQueue < curMazakSchMat) {
            int toRemove = curMazakSchMat - matInQueue;
            var potentialPaths =
              queueGroup
                .Where(p => p.SchProcRow.ProcessMaterialQuantity > 0)
                .OrderByDescending(p => p.SchProcRow.ProcessMaterialQuantity)
                .ThenBy(p => p.SchProcRow.ProcessExecuteQuantity);
            foreach (var p in potentialPaths) {
              if (toRemove >= p.SchProcRow.ProcessMaterialQuantity) {
                p.TargetMaterialCount = 0;
                toRemove -= p.SchProcRow.ProcessMaterialQuantity;
              } else {
                p.TargetMaterialCount = p.SchProcRow.ProcessMaterialQuantity - toRemove;
                toRemove = 0;
              }
              if (toRemove <= 0) break;
            }
          }
        }
      }
    }

    private class UnableToFindPathGroup : Exception {}

    private void CheckInProcessMaterial(JobPlan jobPlan, IGrouping<string, ScheduleWithQueues> job, int proc)
    {
      string uniqueStr = job.Key;
      var paths = job.Select(s => s.Procs[proc]);

      foreach (var path in paths) {
        if (string.IsNullOrEmpty(path.InputQueue)) continue;

        try {
          int matInQueue;

          if (paths.Count() == 1) {
            // just check for parts with the correct unique and next process
            matInQueue = _log.GetMaterialInQueue(path.InputQueue)
              .Where(m => m.Unique == uniqueStr && FindNextProcess(m.MaterialID) == proc)
              .Count()
              ;
          } else {
            // need a more complicated test involving path groups
            matInQueue = _log.GetMaterialInQueue(path.InputQueue)
              .Where(m => m.Unique == uniqueStr && DoesNextProcessAndPathGroupMatch(jobPlan, m, proc - 1, path.PathGroup))
              .Count()
              ;
          }

          if (matInQueue != path.SchProcRow.ProcessMaterialQuantity) {
            path.TargetMaterialCount = matInQueue;
          }
        } catch (UnableToFindPathGroup) {
          Log.Warning("Unable to calculate path group for material in queue for {job} process {proc}," +
            " ignoring queue material updates to mazak schedule.", jobPlan.UniqueStr, proc);
        }
      }
    }

    private void CalculateTargetMatQty(IMazakData mazakData, IEnumerable<ScheduleWithQueues> schs)
    {
      // go through each job and process, and distribute the queued material among the various paths
      // for the job and process.
      foreach (var job in schs.GroupBy(s => s.Unique)) {
        var uniqueStr = job.Key;
        var numProc = job.First().Job.NumProcesses;
        log.Debug("Checking job {uniq} with schedules {@pathGroup}", uniqueStr, job);

        CheckCastingsForProc1(job, mazakData);
        for (int proc = 2; proc <= numProc; proc++) {
          CheckInProcessMaterial(job.First().Job, job, proc);
        }

      }
    }

    private void UpdateMazakMaterialCounts(TransactionDataSet transDB, IEnumerable<ScheduleWithQueues> schs)
    {
      foreach (var sch in schs) {
        if (!sch.Procs.Values.Any(p => p.TargetMaterialCount.HasValue)) continue;
        log.Debug("Updating material on schedule {schId} for job {uniq} to {@sch}", sch.SchRow.ScheduleID, sch.Unique, sch);

        var tschRow = transDB.Schedule_t.NewSchedule_tRow();
        OpenDatabaseKitTransactionDB.BuildScheduleEditRow(tschRow, sch.SchRow, true);
        transDB.Schedule_t.AddSchedule_tRow(tschRow);

        foreach (var proc in sch.Procs.Values) {
          var tschProcRow = transDB.ScheduleProcess_t.NewScheduleProcess_tRow();
          OpenDatabaseKitTransactionDB.BuildScheduleProcEditRow(tschProcRow, proc.SchProcRow);
          if (proc.TargetMaterialCount.HasValue) {
            tschProcRow.ProcessMaterialQuantity = proc.TargetMaterialCount.Value;
          }
          transDB.ScheduleProcess_t.AddScheduleProcess_tRow(tschProcRow);
        }
      }
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
        .SelectMany(e => e.Material.Select(m => new { log = e, mat = m}))
        .Where(x => x.mat.MaterialID == qm.MaterialID && x.mat.Process == proc)
        .Select(x => x.log.Pallet)
        .Where(x => !string.IsNullOrEmpty(x))
        .DefaultIfEmpty("")
        .First()
        ;

      if (string.IsNullOrEmpty(lastPallet)) throw new UnableToFindPathGroup();

      Log.Debug("Calculated last pallet {pal} for {@qm} and proc {proc}", lastPallet, qm);

      for (int path = 1; path <= job.GetNumPaths(proc); path++) {
        if (job.HasPallet(proc, path, lastPallet)) {
          return job.GetPathGroup(proc, path) == pathGroup;
        }
      }

      throw new UnableToFindPathGroup();
    }

    private int CountCompletedOrInProc(ReadOnlyDataSet.ScheduleRow schRow)
    {
        var cnt = schRow.CompleteQuantity;
        foreach (var schProcRow in schRow.GetScheduleProcessRows()) {
          if (schProcRow.ProcessNumber == 1) {
            cnt += schProcRow.ProcessBadQuantity + schProcRow.ProcessExecuteQuantity;
          } else {
            cnt += schProcRow.ProcessBadQuantity + schProcRow.ProcessExecuteQuantity + schProcRow.ProcessMaterialQuantity;
          }
        }
        return cnt;
    }

    private int PartFixQuantity(IMazakData mazakData, ReadOnlyDataSet.ScheduleProcessRow schProcRow)
    {
      var read = mazakData.ReadSet;
      var schRow = schProcRow.ScheduleRow;
      foreach (var procRow in read.PartProcess) {
        if (procRow.PartName == schRow.PartName && procRow.ProcessNumber == schProcRow.ProcessNumber) {
          return procRow.FixQuantity;
        }
      }
      return 1;
    }

  }
}