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
      _loadOper = load;
      _shutdown = new AutoResetEvent(false);
      _recheckMaterial = new AutoResetEvent(false);

      _thread = new Thread(new ThreadStart(ThreadFunc));
      _thread.Start();
    }

    public void SignalRecheckMaterial()
    {
      _recheckMaterial.Set();
    }

    public void Shutdown()
    {
      _shutdown.Set();

      if (!_thread.Join(TimeSpan.FromSeconds(15)))
        _thread.Abort();
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

    private TimeSpan? CheckForNewMaterial()
    {
      log.Debug("Starting check for new queued material to add to mazak");
      var read = _readonlyDB.LoadReadOnly();
      var loadOpers = _loadOper.CurrentLoadActions();
      var queuedMats = _log.GetMaterialInAllQueues();
      var transSet = new TransactionDataSet();

      // check for any raw material to assign to a job
      var partsToAllocate =
        queuedMats
        .Where(m => string.IsNullOrEmpty(m.Unique))
        .Select(m => new ValueTuple<string, string>(m.Queue, m.PartName))
        .ToHashSet();
      foreach (var (qname, p) in partsToAllocate) {
        log.Debug("Found {part} in queue {queue} without an assigned job, searching for mazak schedule to use", qname, p);
        foreach (var schRow in read.Schedule.OrderBy(schRow => schRow.DueDate)) {
          if (!MazakPart.IsSailPart(schRow.PartName)) continue;
          if (MazakPart.ExtractPartNameFromMazakPartName(schRow.PartName) != p) continue;
          int remain = schRow.PlanQuantity - CountCompletedOrInProc(schRow, 1);
          if (remain <= 0) continue;

          MazakPart.ParseComment(schRow.Comment, out string unique, out int path, out bool manual);
          log.Debug("Allocating castings in queue {queue} with part {part} to job {uniq} remaining qty {remain}", qname, p, unique, remain);
          _log.AllocateCastingsInQueue(qname, p, unique, remain);
        }
      }

      //now check for queued material on an input queue
      bool foundAtLoad = false;
      var jobUniquesToCheck =
        queuedMats
        .Where(m => !string.IsNullOrEmpty(m.Unique))
        .GroupBy(m => m.Unique);
      foreach (var uniqGroup in jobUniquesToCheck) {
        var job = _jobDB.LoadJob(uniqGroup.Key);
        if (job == null) continue;

        // only if no load or unload action is in process
        bool foundJobAtLoad = false;
        foreach (var action in loadOpers) {
          if (action.Unique == job.UniqueStr) {
            foundAtLoad = true;
            foundJobAtLoad = true;
            log.Debug("Not editing queued material because {uniq} is in the process of being loaded or unload with action {@action}", job.UniqueStr, action);
            break;
          }
        }
        if (foundJobAtLoad) continue;

        var procsWithQueuedMat =
          uniqGroup
          .Select(m => new ValueTuple<string, int>(m.Queue, FindNextProcess(m.MaterialID)))
          .GroupBy(m => m)
          .Select(group => new { Queue = group.Key.Item1, NextProcess = group.Key.Item2, QueuedCount = group.Count()})
          ;

        foreach (var queuedMat in procsWithQueuedMat) {
          log.Debug("Checking material for job {uniq} with material {@mat}", uniqGroup.Key, queuedMat);

          // TODO: Check for matching path

          if (queuedMat.NextProcess < job.NumProcesses) {
            for (int path = 1; path <= job.GetNumPaths(queuedMat.NextProcess); path++) {
              var inputQueue = job.GetInputQueue(queuedMat.NextProcess, path);
              if (!string.IsNullOrEmpty(inputQueue) && inputQueue == queuedMat.Queue) {
                EnsureMaterialOnScheduleIs(transSet, read, loadOpers, unique: job.UniqueStr, path: path, proc: queuedMat.NextProcess, count: queuedMat.QueuedCount);
                break;
              }
            }
          }
        }
      }

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

    private void EnsureMaterialOnScheduleIs(TransactionDataSet transDB, ReadOnlyDataSet curSet, IEnumerable<LoadAction> loadOpers, string unique, int path, int proc, int count)
    {
        log.Debug("Ensuring material on unique {uniq} path {path} proc {proc} is equal to {count}", unique, path, proc, count);
        foreach (var schRow in curSet.Schedule) {
          if (!MazakPart.IsSailPart(schRow.PartName)) continue;
          MazakPart.ParseComment(schRow.Comment, out string schUnique, out int schPath, out bool manual);
          if (schUnique != unique || schPath != path) continue;

          bool needReset = false;
          foreach (var schProcRow in schRow.GetScheduleProcessRows()) {
            if (schProcRow.ProcessNumber == proc && schProcRow.ProcessMaterialQuantity != count) {
              //need to reset material
              needReset = true;
              break;
            }
          }

          if (!needReset) continue;

          log.Debug("Editing the material count for {uniq} path {path} proc {proc}", unique, path, proc);
          // edit the schedule with the new counts
          var tschRow = transDB.Schedule_t.NewSchedule_tRow();
          TransactionDatabaseAccess.BuildScheduleEditRow(tschRow, schRow, true);
          foreach (var schProcRow in schRow.GetScheduleProcessRows()) {
            var tschProcRow = transDB.ScheduleProcess_t.NewScheduleProcess_tRow();
            TransactionDatabaseAccess.BuildScheduleProcEditRow(tschProcRow, schProcRow);
            if (schProcRow.ProcessNumber == proc) {
              tschProcRow.ProcessMaterialQuantity = count;
            }
            transDB.ScheduleProcess_t.AddScheduleProcess_tRow(tschProcRow);
          }
          transDB.Schedule_t.AddSchedule_tRow(tschRow);

        }
    }
  }
}