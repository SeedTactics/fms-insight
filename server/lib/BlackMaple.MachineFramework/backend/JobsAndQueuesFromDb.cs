/* Copyright (c) 2022, John Lenz

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
using System.Collections.Immutable;
using System.Threading;

namespace BlackMaple.MachineFramework
{
  public class JobsAndQueuesFromDb<St> : IJobControl, IQueueControl, IDisposable where St : ICellState
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<JobsAndQueuesFromDb<St>>();

    private readonly RepositoryConfig _repo;
    private readonly FMSSettings _settings;
    private readonly Action<CurrentStatus> _onNewCurrentStatus;
    private readonly ISynchronizeCellState<St> _syncState;
    private readonly ICheckJobsValid _checkJobsValid;
    private readonly TimeSpan _refreshStateInterval;

    public JobsAndQueuesFromDb(
      RepositoryConfig repo,
      FMSSettings settings,
      Action<CurrentStatus> onNewCurrentStatus,
      ISynchronizeCellState<St> syncSt,
      ICheckJobsValid checkJobs,
      TimeSpan refreshStateInterval
    )
    {
      _repo = repo;
      _settings = settings;
      _onNewCurrentStatus = onNewCurrentStatus;
      _syncState = syncSt;
      _checkJobsValid = checkJobs;
      _refreshStateInterval = refreshStateInterval;
      _syncState.NewCellState += NewCellState;

      _thread = new Thread(Thread);
      _thread.IsBackground = true;
    }

    public void Dispose()
    {
      _syncState.NewCellState -= NewCellState;
      _shutdown.Set();
    }

    #region Thread and Messages
    private readonly System.Threading.Thread _thread;
    private readonly AutoResetEvent _shutdown = new AutoResetEvent(false);
    private readonly AutoResetEvent _recheck = new AutoResetEvent(false);
    private readonly ManualResetEvent _newCellState = new ManualResetEvent(false);

    // lock prevents decrement from occuring at the same time as the thread
    // is deciding what to put onto a pallet
    private readonly object _changeLock = new object();

    public void RecalculateCellState()
    {
      _recheck.Set();
    }

    private void NewCellState()
    {
      _newCellState.Set();
    }

    public void StartThread()
    {
      _thread.Start();
    }

    private void Thread()
    {
      bool raiseNewCurStatus = true; // very first run should raise new pallet state
      while (true)
      {
        try
        {
          Synchronize(raiseNewCurStatus);

          var ret = WaitHandle.WaitAny(
            new WaitHandle[] { _shutdown, _recheck, _newCellState },
            _refreshStateInterval,
            false
          );
          if (ret == 0)
          {
            Log.Debug("Thread shutdown");
            return;
          }
          else if (ret == 1)
          {
            // recheck and guarantee status changed event even if nothing changed
            Log.Debug("Recalculating cell state due to job changes");
            raiseNewCurStatus = true;
          }
          else if (ret == 2)
          {
            // reload status due to event from the _syncState class
            Log.Debug("Recalculating cell state due to event");
            raiseNewCurStatus = true;
            // new current status events may come in batches, so wait briefly so we only recalculate once
            System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(500));
          }
          else
          {
            Log.Debug("Timeout, rechecking cell state");
            raiseNewCurStatus = false;
          }
        }
        catch (Exception ex)
        {
          Log.Fatal(ex, "Unexpected error during thread processing");
        }
      }
    }

    private void Synchronize(bool raiseNewCurStatus)
    {
      try
      {
        lock (_changeLock)
        {
          bool actionPerformed = false;
          using var db = _repo.OpenConnection();
          do
          {
            _newCellState.Reset();
            var st = _syncState.CalculateCellState(db);
            raiseNewCurStatus = raiseNewCurStatus || (st?.PalletStateUpdated ?? false);

            lock (_curStLock)
            {
              _lastCurrentStatus = st?.CurrentStatus;
            }

            actionPerformed = _syncState.ApplyActions(db, st);
            if (actionPerformed)
              raiseNewCurStatus = true;
          } while (actionPerformed);
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error synching cell state");
      }
      finally
      {
        if (raiseNewCurStatus)
        {
          _onNewCurrentStatus(GetCurrentStatus());
        }
      }
    }

    private void DecrementPlannedButNotStartedQty(IRepository jobDB)
    {
      bool requireStateRefresh = false;
      lock (_changeLock)
      {
        var st = _syncState.CalculateCellState(jobDB);
        requireStateRefresh = requireStateRefresh || st.PalletStateUpdated;

        lock (_curStLock)
        {
          _lastCurrentStatus = st.CurrentStatus;
        }

        // Don't perform actions here, wait until after decrement when we will recalculate

        var decrs = new List<NewDecrementQuantity>();
        foreach (var j in st.CurrentStatus.Jobs.Values)
        {
          if (j.ManuallyCreated || j.Decrements?.Count > 0)
            continue;

          if (_checkJobsValid.ExcludeJobFromDecrement(jobDB, j))
            continue;

          int toStart = (int)(j.RemainingToStart ?? 0);
          if (toStart > 0)
          {
            decrs.Add(
              new NewDecrementQuantity()
              {
                JobUnique = j.UniqueStr,
                Part = j.PartName,
                Quantity = toStart
              }
            );
          }
        }

        if (decrs.Count > 0)
        {
          jobDB.AddNewDecrement(decrs, nowUTC: st?.CurrentStatus?.TimeOfCurrentStatusUTC);
          requireStateRefresh = true;
        }
      }

      if (requireStateRefresh)
      {
        RecalculateCellState();
      }
    }
    #endregion

    #region Jobs
    public event NewJobsDelegate OnNewJobs;

    private readonly object _curStLock = new object();
    private CurrentStatus _lastCurrentStatus = null;

    public CurrentStatus GetCurrentStatus()
    {
      lock (_curStLock)
      {
        return _lastCurrentStatus;
      }
    }

    List<string> IJobControl.CheckValidRoutes(IEnumerable<Job> newJobs)
    {
      using (var jdb = _repo.OpenConnection())
      {
        return _checkJobsValid
          .CheckNewJobs(jdb, new NewJobs() { ScheduleId = null, Jobs = newJobs.ToImmutableList() })
          .ToList();
      }
    }

    void IJobControl.AddJobs(NewJobs jobs, string expectedPreviousScheduleId, bool waitForCopyToCell)
    {
      using (var jdb = _repo.OpenConnection())
      {
        Log.Debug("Adding new jobs {@jobs}", jobs);
        var errors = _checkJobsValid.CheckNewJobs(jdb, jobs);
        if (errors.Any())
        {
          throw new BadRequestException(string.Join(Environment.NewLine, errors));
        }

        var curSt = GetCurrentStatus();
        foreach (var j in curSt.Jobs.Values)
        {
          if (IsJobCompleted(j, curSt))
          {
            jdb.ArchiveJob(j.UniqueStr);
          }
        }

        Log.Debug("Adding jobs to database");

        jdb.AddJobs(jobs, expectedPreviousScheduleId, addAsCopiedToSystem: true);
      }

      Log.Debug("Sending new jobs on websocket");

      OnNewJobs?.Invoke(jobs);

      Log.Debug("Signaling new jobs available for routes");

      RecalculateCellState();
    }

    private bool IsJobCompleted(ActiveJob job, CurrentStatus st)
    {
      if (st == null)
        return false;
      if (job.RemainingToStart > 0)
        return false;

      var matInProc = st.Material.Where(m => m.JobUnique == job.UniqueStr).Any();

      if (matInProc)
      {
        return false;
      }
      else
      {
        return true;
      }
    }

    List<JobAndDecrementQuantity> IJobControl.DecrementJobQuantites(
      long loadDecrementsStrictlyAfterDecrementId
    )
    {
      using (var jdb = _repo.OpenConnection())
      {
        DecrementPlannedButNotStartedQty(jdb);
        return jdb.LoadDecrementQuantitiesAfter(loadDecrementsStrictlyAfterDecrementId);
      }
    }

    List<JobAndDecrementQuantity> IJobControl.DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
    {
      using (var jdb = _repo.OpenConnection())
      {
        DecrementPlannedButNotStartedQty(jdb);
        return jdb.LoadDecrementQuantitiesAfter(loadDecrementsAfterTimeUTC);
      }
    }

    void IJobControl.SetJobComment(string jobUnique, string comment)
    {
      using (var jdb = _repo.OpenConnection())
      {
        jdb.SetJobComment(jobUnique, comment);
      }
      RecalculateCellState();
    }

    public void ReplaceWorkordersForSchedule(
      string scheduleId,
      IEnumerable<Workorder> newWorkorders,
      IEnumerable<MachineFramework.NewProgramContent> programs
    )
    {
      var cellState = GetCurrentStatus();
      if (cellState == null)
        return;

      using (var jdb = _repo.OpenConnection())
      {
        var errors = _checkJobsValid.CheckWorkorders(jdb, newWorkorders, programs);
        if (errors.Count > 0)
        {
          throw new BadRequestException(string.Join(Environment.NewLine, errors));
        }

        jdb.ReplaceWorkordersForSchedule(scheduleId, newWorkorders, programs);
      }

      RecalculateCellState();
    }
    #endregion


    #region Queues
    public event EditMaterialInLogDelegate OnEditMaterialInLog;

    private List<InProcessMaterial> AddUnallocatedCastingToQueue(
      IRepository logDB,
      string casting,
      int qty,
      string queue,
      IList<string> serial,
      string operatorName
    )
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      // num proc will be set later once it is allocated to a specific job
      var mats = new List<InProcessMaterial>();

      var newMats = logDB.BulkAddNewCastingsInQueue(
        casting,
        qty,
        queue,
        serial,
        operatorName,
        reason: "SetByOperator"
      );

      foreach (var log in newMats.Logs.Where(l => l.LogType == LogType.AddToQueue))
      {
        mats.Add(
          new InProcessMaterial()
          {
            MaterialID = log.Material.First().MaterialID,
            JobUnique = null,
            PartName = casting,
            Process = 0,
            Path = 1,
            Serial = log.Material.First().Serial,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.InQueue,
              CurrentQueue = queue,
              QueuePosition = log.LocationNum
            },
            Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
            SignaledInspections = ImmutableList<string>.Empty
          }
        );
      }

      RecalculateCellState();

      return mats;
    }

    public InProcessMaterial AddUnallocatedPartToQueue(
      string partName,
      string queue,
      string serial,
      string operatorName
    )
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      string casting = partName;

      // try and see if there is a job for this part with an actual casting
      IReadOnlyList<HistoricJob> unarchived;
      using (var jdb = _repo.OpenConnection())
      {
        unarchived = jdb.LoadUnarchivedJobs();
      }
      var job = unarchived.FirstOrDefault(j => j.PartName == partName);
      if (job != null)
      {
        for (int path = 1; path <= job.Processes[0].Paths.Count; path++)
        {
          var jobCasting = job.Processes[0].Paths[path - 1].Casting;
          if (!string.IsNullOrEmpty(jobCasting))
          {
            casting = jobCasting;
            break;
          }
        }
      }

      using (var ldb = _repo.OpenConnection())
      {
        return AddUnallocatedCastingToQueue(
            ldb,
            casting,
            1,
            queue,
            string.IsNullOrEmpty(serial) ? new string[] { } : new string[] { serial },
            operatorName
          )
          .FirstOrDefault();
      }
    }

    public List<InProcessMaterial> AddUnallocatedCastingToQueue(
      string casting,
      int qty,
      string queue,
      IList<string> serial,
      string operatorName
    )
    {
      using (var ldb = _repo.OpenConnection())
      {
        return AddUnallocatedCastingToQueue(ldb, casting, qty, queue, serial, operatorName);
      }
    }

    public InProcessMaterial AddUnprocessedMaterialToQueue(
      string jobUnique,
      int process,
      string queue,
      int position,
      string serial,
      string operatorName
    )
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      Log.Debug(
        "Adding unprocessed material for job {job} proc {proc} to queue {queue} in position {pos} with serial {serial}",
        jobUnique,
        process,
        queue,
        position,
        serial
      );

      HistoricJob job;
      using (var jdb = _repo.OpenConnection())
      {
        job = jdb.LoadJob(jobUnique);
      }
      if (job == null)
        throw new BlackMaple.MachineFramework.BadRequestException("Unable to find job " + jobUnique);

      int procToCheck = Math.Max(1, process);
      if (procToCheck > job.Processes.Count)
        throw new BlackMaple.MachineFramework.BadRequestException("Invalid process " + process.ToString());

      long matId;
      IEnumerable<LogEntry> logEvt;
      using (var ldb = _repo.OpenConnection())
      {
        matId = ldb.AllocateMaterialID(jobUnique, job.PartName, job.Processes.Count);
        if (!string.IsNullOrEmpty(serial))
        {
          ldb.RecordSerialForMaterialID(
            new BlackMaple.MachineFramework.EventLogMaterial()
            {
              MaterialID = matId,
              Process = process,
              Face = ""
            },
            serial
          );
        }
        logEvt = ldb.RecordAddMaterialToQueue(
          matID: matId,
          process: process,
          queue: queue,
          position: position,
          operatorName: operatorName,
          reason: "SetByOperator"
        );
      }

      RecalculateCellState();

      return new InProcessMaterial()
      {
        MaterialID = matId,
        JobUnique = jobUnique,
        PartName = job.PartName,
        Process = process,
        Path = 1,
        Serial = serial,
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.InQueue,
          CurrentQueue = queue,
          QueuePosition = logEvt.LastOrDefault()?.LocationNum
        },
        Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
        SignaledInspections = ImmutableList<string>.Empty
      };
    }

    public void SetMaterialInQueue(long materialId, string queue, int position, string operatorName)
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }
      Log.Debug("Adding material {matId} to queue {queue} in position {pos}", materialId, queue, position);
      using (var ldb = _repo.OpenConnection())
      {
        var nextProc = ldb.NextProcessForQueuedMaterial(materialId);
        var proc = (nextProc ?? 1) - 1;
        ldb.RecordAddMaterialToQueue(
          matID: materialId,
          process: proc,
          queue: queue,
          position: position,
          operatorName: operatorName,
          reason: "SetByOperator"
        );
      }

      RecalculateCellState();
    }

    public void RemoveMaterialFromAllQueues(IList<long> materialIds, string operatorName)
    {
      Log.Debug("Removing {@matId} from all queues", materialIds);
      using (var ldb = _repo.OpenConnection())
      {
        ldb.BulkRemoveMaterialFromAllQueues(materialIds, operatorName);
      }
      RecalculateCellState();
    }

    public void SignalMaterialForQuarantine(long materialId, string queue, string operatorName)
    {
      Log.Debug("Signaling {matId} for quarantine", materialId);
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      var st = GetCurrentStatus();

      // first, see if it is on a pallet
      var palMat = st.Material
        .Where(m => m.Location.Type == InProcessMaterialLocation.LocType.OnPallet)
        .FirstOrDefault(m => m.MaterialID == materialId);
      var qMat = st.Material
        .Where(m => m.Location.Type == InProcessMaterialLocation.LocType.InQueue)
        .FirstOrDefault(m => m.MaterialID == materialId);

      if (palMat != null)
      {
        using (var ldb = _repo.OpenConnection())
        {
          ldb.SignalMaterialForQuarantine(
            mat: new EventLogMaterial()
            {
              MaterialID = materialId,
              Process = palMat.Process,
              Face = ""
            },
            pallet: palMat.Location.Pallet,
            queue: queue,
            timeUTC: null,
            operatorName: operatorName
          );
        }

        RecalculateCellState();

        return;
      }
      else if (qMat != null)
      {
        this.SetMaterialInQueue(materialId, queue, -1, operatorName);
      }
      else
      {
        throw new BadRequestException("Unable to find material to quarantine");
      }
    }

    public void SwapMaterialOnPallet(string pallet, long oldMatId, long newMatId, string operatorName = null)
    {
      Log.Debug("Overriding {oldMat} to {newMat} on pallet {pal}", oldMatId, newMatId, pallet);

      using (var logDb = _repo.OpenConnection())
      {
        var o = logDb.SwapMaterialInCurrentPalletCycle(
          pallet: pallet,
          oldMatId: oldMatId,
          newMatId: newMatId,
          operatorName: operatorName,
          quarantineQueue: _settings.QuarantineQueue
        );

        OnEditMaterialInLog?.Invoke(
          new EditMaterialInLogEvents()
          {
            OldMaterialID = oldMatId,
            NewMaterialID = newMatId,
            EditedEvents = o.ChangedLogEntries,
          }
        );
      }

      RecalculateCellState();
    }

    public void InvalidatePalletCycle(
      long matId,
      int process,
      string oldMatPutInQueue = null,
      string operatorName = null
    )
    {
      Log.Debug("Invalidating pallet cycle for {matId} and {process}", matId, process);

      using (var logDb = _repo.OpenConnection())
      {
        logDb.InvalidatePalletCycle(
          matId: matId,
          process: process,
          oldMatPutInQueue: oldMatPutInQueue,
          operatorName: operatorName
        );
      }

      RecalculateCellState();
    }
    #endregion
  }
}
