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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace BlackMaple.MachineFramework
{
  public class JobsAndQueuesFromDb<St> : IJobControl, IQueueControl, IDisposable
    where St : ICellState
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<JobsAndQueuesFromDb<St>>();

    private readonly RepositoryConfig _repo;
    private readonly FMSSettings _settings;
    private readonly Action<CurrentStatus> _onNewCurrentStatus;
    private readonly ISynchronizeCellState<St> _syncState;
    private readonly ICheckJobsValid _checkJobsValid;

    public bool AllowQuarantineToCancelLoad { get; }
    public bool AddJobsAsCopiedToSystem { get; }

    public JobsAndQueuesFromDb(
      RepositoryConfig repo,
      FMSSettings settings,
      Action<CurrentStatus> onNewCurrentStatus,
      ISynchronizeCellState<St> syncSt,
      ICheckJobsValid checkJobs,
      bool allowQuarantineToCancelLoad,
      bool addJobsAsCopiedToSystem
    )
    {
      _repo = repo;
      _settings = settings;
      _onNewCurrentStatus = onNewCurrentStatus;
      _syncState = syncSt;
      _checkJobsValid = checkJobs;
      _syncState.NewCellState += NewCellState;
      AllowQuarantineToCancelLoad = allowQuarantineToCancelLoad;
      AddJobsAsCopiedToSystem = addJobsAsCopiedToSystem;

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

    // lock prevents decrement and queue changes from occuring at the same time as the thread
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
          var untilRefresh = Synchronize(raiseNewCurStatus);

          var ret = WaitHandle.WaitAny(
            new WaitHandle[] { _shutdown, _recheck, _newCellState },
            untilRefresh,
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
            raiseNewCurStatus = false;
          }
        }
        catch (Exception ex)
        {
          Log.Fatal(ex, "Unexpected error during thread processing");
        }
      }
    }

    private int _syncronizeErrorCount = 0;

    private TimeSpan Synchronize(bool raiseNewCurStatus)
    {
      try
      {
        lock (_changeLock)
        {
          bool actionPerformed = false;
          using var db = _repo.OpenConnection();
          TimeSpan timeUntilNextRefresh;
          do
          {
            _newCellState.Reset();
            var st = _syncState.CalculateCellState(db);
            raiseNewCurStatus = raiseNewCurStatus || (st?.StateUpdated ?? false);
            timeUntilNextRefresh = st?.TimeUntilNextRefresh ?? TimeSpan.FromSeconds(30);

            lock (_curStLock)
            {
              _lastCurrentStatus = st?.CurrentStatus;
            }

            actionPerformed = _syncState.ApplyActions(db, st);
            if (actionPerformed)
              raiseNewCurStatus = true;
          } while (actionPerformed);

          _syncronizeErrorCount = 0;
          return timeUntilNextRefresh;
        }
      }
      catch (Exception ex)
      {
        _syncronizeErrorCount++;
        // exponential decay backoff starting at 2 seconds, maximum of 5 minutes
        var msToWait = Math.Min(5 * 60 * 1000, 2000 * Math.Pow(2, _syncronizeErrorCount - 1));
        Log.Error(
          ex,
          "Error synching cell state on retry {_syncronizeErrorCount}, waiting {msToWait} milliseconds",
          _syncronizeErrorCount,
          msToWait
        );
        return TimeSpan.FromMilliseconds(msToWait);
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
        CurrentStatus st;
        lock (_curStLock)
        {
          st = _lastCurrentStatus;
        }

        var jobsDecremented = _syncState.DecrementJobs(jobDB, st);
        requireStateRefresh = requireStateRefresh || jobsDecremented;
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

    void IJobControl.AddJobs(NewJobs jobs, string expectedPreviousScheduleId)
    {
      using (var jdb = _repo.OpenConnection())
      {
        Log.Debug("Adding new jobs {@jobs}", jobs);
        var errors = _checkJobsValid.CheckNewJobs(jdb, jobs);
        if (errors.Any())
        {
          throw new BadRequestException(string.Join(Environment.NewLine, errors));
        }

        Log.Debug("Adding jobs to database");

        jdb.AddJobs(jobs, expectedPreviousScheduleId, addAsCopiedToSystem: AddJobsAsCopiedToSystem);
      }

      Log.Debug("Sending new jobs on websocket");

      OnNewJobs?.Invoke(jobs);

      Log.Debug("Signaling new jobs available for routes");

      RecalculateCellState();
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
    #endregion


    #region Queues
    public event EditMaterialInLogDelegate OnEditMaterialInLog;

    private List<InProcessMaterial> AddUnallocatedCastingToQueue(
      IRepository logDB,
      string casting,
      int qty,
      string queue,
      IList<string> serial,
      string workorder,
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
        casting: casting,
        qty: qty,
        queue: queue,
        serials: serial,
        workorder: workorder,
        operatorName: operatorName,
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
            WorkorderId = workorder,
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

    public List<InProcessMaterial> AddUnallocatedCastingToQueue(
      string casting,
      int qty,
      string queue,
      IList<string> serial,
      string workorder,
      string operatorName
    )
    {
      using (var ldb = _repo.OpenConnection())
      {
        return AddUnallocatedCastingToQueue(
          logDB: ldb,
          casting: casting,
          qty: qty,
          queue: queue,
          serial: serial,
          workorder: workorder,
          operatorName: operatorName
        );
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
            serial,
            DateTime.UtcNow
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

      string error = null;
      bool requireStateRefresh = false;

      using (var ldb = _repo.OpenConnection())
      {
        lock (_changeLock)
        {
          CurrentStatus st;
          lock (_curStLock)
          {
            st = _lastCurrentStatus;
          }

          var mat = st.Material.FirstOrDefault(m => m.MaterialID == materialId);
          if (mat != null && mat.Location.Type == InProcessMaterialLocation.LocType.OnPallet)
          {
            error = "Material on pallet can not be moved to a queue";
          }
          else if (
            mat != null
            && mat.Action.Type != InProcessMaterialAction.ActionType.Waiting
            && mat.Location.CurrentQueue != queue
          )
          {
            error = "Only waiting material can be moved between queues";
          }
          else
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
            requireStateRefresh = true;
          }
        }
      }

      if (requireStateRefresh)
      {
        RecalculateCellState();
      }

      if (error != null)
      {
        throw new BadRequestException(error);
      }
    }

    public void RemoveMaterialFromAllQueues(IList<long> materialIds, string operatorName)
    {
      Log.Debug("Removing {@matId} from all queues", materialIds);

      string error = null;
      bool requireStateRefresh = false;

      lock (_changeLock)
      {
        CurrentStatus st;
        lock (_curStLock)
        {
          st = _lastCurrentStatus;
        }

        using (var ldb = _repo.OpenConnection())
        {
          foreach (var matId in materialIds)
          {
            var mat = st.Material.FirstOrDefault(m => m.MaterialID == matId);
            if (mat != null && mat.Location.Type == InProcessMaterialLocation.LocType.OnPallet)
            {
              error = "Material on pallet can not be removed from queues";
              break;
            }
            else if (mat != null && mat.Action.Type != InProcessMaterialAction.ActionType.Waiting)
            {
              error = "Only waiting material can be removed from queues";
              break;
            }
          }

          if (error == null)
          {
            ldb.BulkRemoveMaterialFromAllQueues(materialIds, operatorName);
            requireStateRefresh = true;
          }
        }
      }

      if (requireStateRefresh)
      {
        RecalculateCellState();
      }

      if (error != null)
      {
        throw new BadRequestException(error);
      }
    }

    public void SignalMaterialForQuarantine(long materialId, string operatorName, string reason)
    {
      Log.Debug("Signaling {matId} for quarantine", materialId);

      string error = null;
      bool requireStateRefresh = false;

      using (var ldb = _repo.OpenConnection())
      {
        lock (_changeLock)
        {
          CurrentStatus st;
          lock (_curStLock)
          {
            st = _lastCurrentStatus;
          }

          var mat = st.Material.FirstOrDefault(m => m.MaterialID == materialId);

          if (mat == null)
          {
            error = "Material not found";
          }
          else
          {
            switch (mat.Location.Type, mat.Action.Type)
            {
              case (InProcessMaterialLocation.LocType.OnPallet, _):
                if (!AllowQuarantineToCancelLoad)
                {
                  if (mat.Action.Type == InProcessMaterialAction.ActionType.Loading)
                  {
                    error = "Material on pallet can not be quarantined while loading";
                  }
                  else
                  {
                    // If the material will eventually stay on the pallet, disallow quarantine
                    var job = st.Jobs.GetValueOrDefault(mat.JobUnique);
                    if (job == null)
                    {
                      error = "Job not found";
                    }
                    else
                    {
                      var path = job.Processes[mat.Process - 1].Paths[mat.Path - 1];
                      if ((mat.Process != job.Processes.Count && (path == null || path.OutputQueue == null)))
                      {
                        error =
                          "Can only signal material for quarantine if the current process and path has an output queue";
                      }
                    }
                  }
                }

                if (error == null)
                {
                  ldb.SignalMaterialForQuarantine(
                    mat: new EventLogMaterial()
                    {
                      MaterialID = materialId,
                      Process = mat.Process,
                      Face = ""
                    },
                    pallet: mat.Location.PalletNum ?? 0,
                    queue: _settings.QuarantineQueue ?? "",
                    timeUTC: null,
                    operatorName: operatorName,
                    reason: reason
                  );
                  requireStateRefresh = true;
                }
                break;

              case (InProcessMaterialLocation.LocType.Free, InProcessMaterialAction.ActionType.Waiting)
                when !string.IsNullOrEmpty(_settings.QuarantineQueue):
              case (InProcessMaterialLocation.LocType.InQueue, InProcessMaterialAction.ActionType.Waiting)
                when !string.IsNullOrEmpty(_settings.QuarantineQueue):
              case (InProcessMaterialLocation.LocType.InQueue, InProcessMaterialAction.ActionType.Loading)
                when !string.IsNullOrEmpty(_settings.QuarantineQueue) && AllowQuarantineToCancelLoad:

                {
                  var nextProc = ldb.NextProcessForQueuedMaterial(materialId);
                  var proc = (nextProc ?? 1) - 1;
                  ldb.RecordOperatorNotes(
                    materialId: materialId,
                    process: proc,
                    notes: reason,
                    operatorName: operatorName
                  );
                  ldb.RecordAddMaterialToQueue(
                    matID: materialId,
                    process: proc,
                    queue: _settings.QuarantineQueue,
                    position: -1,
                    operatorName: operatorName,
                    reason: "SetByOperator"
                  );
                  requireStateRefresh = true;
                }
                break;

              case (InProcessMaterialLocation.LocType.InQueue, InProcessMaterialAction.ActionType.Waiting)
                when string.IsNullOrEmpty(_settings.QuarantineQueue):
              case (InProcessMaterialLocation.LocType.InQueue, InProcessMaterialAction.ActionType.Loading)
                when string.IsNullOrEmpty(_settings.QuarantineQueue) && AllowQuarantineToCancelLoad:

                {
                  var nextProc = ldb.NextProcessForQueuedMaterial(materialId);
                  var proc = (nextProc ?? 1) - 1;
                  ldb.RecordOperatorNotes(
                    materialId: materialId,
                    process: proc,
                    notes: reason,
                    operatorName: operatorName
                  );
                  ldb.BulkRemoveMaterialFromAllQueues(new[] { materialId }, operatorName);
                  requireStateRefresh = true;
                }
                break;

              default:
                error = "Invalid material state for quarantine";
                break;
            }
          }
        }
      }

      if (requireStateRefresh)
      {
        RecalculateCellState();
      }

      if (error != null)
      {
        throw new BadRequestException(error);
      }
    }

    public void SwapMaterialOnPallet(int pallet, long oldMatId, long newMatId, string operatorName = null)
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
