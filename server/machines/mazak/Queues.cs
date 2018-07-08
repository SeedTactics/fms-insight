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

    private Thread _thread;
    private AutoResetEvent _shutdown;
    private AutoResetEvent _recheckMaterial;
    private JobLogDB _log;
    private JobDB _jobDB;
    private LoadOperations _loadOper;
    private IReadDataAccess _readonlyDB;
    private TransactionDatabaseAccess _transDB;

    public MazakQueues(JobLogDB log, JobDB jDB, LoadOperations load, IReadDataAccess rDB, TransactionDatabaseAccess db)
    {
      _readonlyDB = rDB;
      _transDB = db;
      _jobDB = jDB;
      _log = log;
      _loadOper = load;
      _shutdown = new AutoResetEvent(false);
      _recheckMaterial = new AutoResetEvent(false);

      _thread = new Thread(new ThreadStart(ThreadFunc));
      // disable queue thread for now
      //_thread.Start();
    }

    public void SignalRecheckMaterial()
    {
      _recheckMaterial.Set();
    }

    public void Shutdown()
    {
      //_shutdown.Set();

      //if (!_thread.Join(TimeSpan.FromSeconds(15)))
      //  _thread.Abort();
    }

    private void ThreadFunc()
    {
      try {
        for (;;)
        {

#if DEBUG
          var sleepTime = TimeSpan.FromMinutes(1);
#else
          var sleepTime = TimeSpan.FromMinutes(10);
#endif
          try
          {
            if (!_transDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(3), true))
            {
              //Downloads usually take a long time and they hold the db lock,
              //so we will probably hit this timeout during the download.
              //For this reason, we wait 3 minutes for the db lock and retry again after only 20 seconds.
              //Thus during the download most of the waiting will be here for the db lock.

              log.Debug("Unable to obtain mazak db lock, trying again in 20 seconds.");
              sleepTime = TimeSpan.FromSeconds(20);
            } else {
              sleepTime = CheckForNewMaterial() ?? sleepTime;
            }
          } catch (Exception ex) {
            log.Error(ex, "Error checking for new material");
          } finally {
            _transDB.MazakTransactionLock.ReleaseMutex();
          }

          log.Debug("Sleeping for {mins} minutes", sleepTime.TotalMinutes);
          var ret = WaitHandle.WaitAny(new WaitHandle[] { _shutdown, _recheckMaterial }, sleepTime, false);

          if (ret == 0)
          {
            //Shutdown was fired.
            log.Debug("Thread Shutdown");
            return;
          }
        }

      } catch (Exception ex) {
        log.Error(ex, "Unhandled error in queue thread");
        throw;
      }
    }

    private class ScheduleWithQueuesProcess
    {
      public ReadOnlyDataSet.ScheduleProcessRow SchProcRow {get;set;}
      public string InputQueue {get;set;}
      public int? TargetMaterialCount {get;set;}
    }

    private class ScheduleWithQueues
    {
      public ReadOnlyDataSet.ScheduleRow SchRow {get;set;}
      public string Unique {get;set;}
      public string PartName {get;set;}
      public int Path {get;set;}
      public int NumProcesses {get;set;}
      public int RemainToLoad {get;set;}
      public Dictionary<int, ScheduleWithQueuesProcess> Procs {get;set;}
    }

    private IEnumerable<ScheduleWithQueues> LoadSchedules(ReadOnlyDataSet read, IEnumerable<LoadAction> loadOpers, out bool foundAtLoad)
    {
      foundAtLoad = false;
      var schs = new List<ScheduleWithQueues>();
      foreach (var schRow in read.Schedule.OrderBy(s => s.DueDate)) {
        if (!MazakPart.IsSailPart(schRow.PartName)) continue;

        var remain  = schRow.PlanQuantity - CountCompletedOrInProc(schRow, proc: 1);
        if (remain <= 0) continue;

        MazakPart.ParseComment(schRow.Comment, out string unique, out int path, out bool manual);

        var job = _jobDB.LoadJob(unique);
        if (job == null) continue;

        // only if no load or unload action is in process
        bool foundJobAtLoad = false;
        foreach (var action in loadOpers) {
          if (action.Unique == job.UniqueStr && action.Path == path) {
            foundJobAtLoad = true;
            foundAtLoad = true;
            log.Debug("Not editing queued material because {uniq} is in the process of being loaded or unload with action {@action}", job.UniqueStr, action);
            break;
          }
        }
        if (foundJobAtLoad) continue;

        // start building the schedule
        var sch = new ScheduleWithQueues() {
          SchRow = schRow,
          Unique = unique,
          PartName = job.PartName,
          Path = path,
          NumProcesses = job.NumProcesses,
          RemainToLoad = remain,
          Procs = new Dictionary<int, ScheduleWithQueuesProcess>(),
        };
        bool missingProc = false;
        for (int proc = 1; proc <= job.NumProcesses; proc++) {
          ReadOnlyDataSet.ScheduleProcessRow schProcRow = null;
          foreach (var row in schRow.GetScheduleProcessRows()) {
            if (schProcRow.ProcessNumber == proc) {
              schProcRow = row;
              break;
            }
          }
          if (schProcRow == null) {
            log.Error("Unable to find process {proc} for job {uniq} and schedule {schid}", proc, job.UniqueStr, schRow.ScheduleID);
            missingProc = true;
            break;
          }
          sch.Procs.Add(proc, new ScheduleWithQueuesProcess() {
            SchProcRow = schProcRow,
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
        var partName = job.First().PartName;
        var numProc = job.First().NumProcesses;
        log.Debug("Checking job {uniq} with for unallocated castings", uniqueStr);

        // check for any raw material to assign to the job
        var proc1Queues =
          job
          .Select(s => s.Procs[1].InputQueue)
          .Where(q => !string.IsNullOrEmpty(q))
          .Distinct();
        foreach (var queue in proc1Queues) {
          var remain = job.Select(s => s.RemainToLoad).Sum();
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

    private void CalculateTargetMatQty(ReadOnlyDataSet read, IEnumerable<ScheduleWithQueues> schs)
    {
      // go through each job and process, and distribute the queued material among the various paths
      // for the job and process.
      foreach (var job in schs.GroupBy(s => s.Unique)) {
        var uniqueStr = job.Key;
        var partName = job.First().PartName;
        var numProc = job.First().NumProcesses;
        log.Debug("Checking job {uniq} with schedules {@pathGroup}", uniqueStr, job);

        for (int proc = 1; proc <= numProc; proc++) {
          // group the processes by input queue
          var byQueue = job.Select(s => s.Procs[proc]).GroupBy(p => p.InputQueue);
          foreach (var queueGroup in byQueue) {
            if (string.IsNullOrEmpty(queueGroup.Key)) continue;

            var matInQueue =
              _log.GetMaterialInQueue(queueGroup.Key)
              .Where(m => m.Unique == uniqueStr && FindNextProcess(m.MaterialID) == proc)
              .Count()
              ;
            var curMazakSchMat = queueGroup.Select(s => s.SchProcRow.ProcessMaterialQuantity).Sum();

            log.Debug("Calculated {matInQueue} parts in queue and {mazakCnt} parts in mazak for job {uniq} proc {proc}",
              matInQueue, curMazakSchMat, uniqueStr, proc);

            if (queueGroup.Count() == 1) {
              // put all material on the single path
              if (matInQueue != curMazakSchMat) {
                queueGroup.First().TargetMaterialCount = matInQueue;
              }
            } else {
              // keep each path at least fixQty piece of material
              // TODO: deal with path groups
              if (matInQueue > curMazakSchMat) {
                int remain = matInQueue - curMazakSchMat;
                foreach (var p in queueGroup.Where(p => p.SchProcRow.ProcessMaterialQuantity == 0)) {
                  int fixQty = PartFixQuantity(read, p.SchProcRow);
                  if (fixQty <= remain) {
                    remain -= fixQty;
                    p.TargetMaterialCount = fixQty;
                  }
                }
              }
            }
          }
        }
      }
    }

    private void UpdateMazakMaterialCounts(TransactionDataSet transDB, IEnumerable<ScheduleWithQueues> schs)
    {
      foreach (var sch in schs) {
        if (!sch.Procs.Values.Any(p => p.TargetMaterialCount.HasValue)) continue;
        log.Debug("Updating material on schedule {schId} for job {uniq} to {@sch}", sch.SchRow.ScheduleID, sch.Unique, sch);

        var tschRow = transDB.Schedule_t.NewSchedule_tRow();
        TransactionDatabaseAccess.BuildScheduleEditRow(tschRow, sch.SchRow, true);

        foreach (var proc in sch.Procs.Values) {
          var tschProcRow = transDB.ScheduleProcess_t.NewScheduleProcess_tRow();
          TransactionDatabaseAccess.BuildScheduleProcEditRow(tschProcRow, proc.SchProcRow);
          if (proc.TargetMaterialCount.HasValue) {
            tschProcRow.ProcessMaterialQuantity = proc.TargetMaterialCount.Value;
          }
          transDB.ScheduleProcess_t.AddScheduleProcess_tRow(tschProcRow);
        }
      }
    }

    private TimeSpan? CheckForNewMaterial()
    {
      log.Debug("Starting check for new queued material to add to mazak");
      var read = _readonlyDB.LoadReadOnly();
      var loadOpers = _loadOper.CurrentLoadActions();
      var transSet = new TransactionDataSet();

      var schs = LoadSchedules(read, loadOpers, out bool foundAtLoad);
      if (!schs.Any()) return null;

      AttemptToAllocateCastings(schs);
      CalculateTargetMatQty(read, schs);
      UpdateMazakMaterialCounts(transSet, schs);

      if (transSet.Schedule_t.Count > 0) {
        _transDB.ClearTransactionDatabase();
        var logs = new List<string>();
        _transDB.SaveTransaction(transSet, logs, "Setting material from queues");
        foreach (var msg in logs) {
          log.Warning(msg);
        }
      }

      if (foundAtLoad) {
        return TimeSpan.FromMinutes(1);  // check again in one minute
      } else {
        return null; // default sleep time
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

    private int CountCompletedOrInProc(ReadOnlyDataSet.ScheduleRow schRow, int proc)
    {
        var counts = new Dictionary<int, int>(); //key is process, value is in-proc + mat
        foreach (var schProcRow in schRow.GetScheduleProcessRows()) {
          counts[schProcRow.ProcessNumber] =
            schProcRow.ProcessBadQuantity + schProcRow.ProcessExecuteQuantity + schProcRow.ProcessMaterialQuantity;
        }
        var inproc =
          counts
          .Where(x => x.Key >= proc)
          .Select(x => x.Value)
          .Sum();
        return inproc + schRow.CompleteQuantity;
    }

    private int PartFixQuantity(ReadOnlyDataSet read, ReadOnlyDataSet.ScheduleProcessRow schProcRow)
    {
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