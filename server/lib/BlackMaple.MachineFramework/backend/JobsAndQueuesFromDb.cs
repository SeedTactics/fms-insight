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

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace BlackMaple.MachineFramework
{
  public delegate void NewCurrentStatus(CurrentStatus status);
  public delegate void NewJobsDelegate(NewJobs j);
  public delegate void EditMaterialInLogDelegate(EditMaterialInLogEvents o);

  public interface IJobAndQueueControl
  {
    ///loads info
    CurrentStatus GetCurrentStatus();
    void RecalculateCellState();
    event NewCurrentStatus OnNewCurrentStatus;

    //checks to see if the jobs are valid.  Some machine types might not support all the different
    //pallet->part->machine->process combinations.
    //Return value is a list of strings, detailing the problems.
    //An empty list or nothing signals the jobs are valid.
    List<string> CheckValidRoutes(IEnumerable<Job> newJobs);

    ///Adds new jobs into the cell controller
    void AddJobs(NewJobs jobs, string expectedPreviousScheduleId);
    event NewJobsDelegate OnNewJobs;

    void SetJobComment(string jobUnique, string comment);

    //Remove all planned parts from all jobs in the system.
    //
    //The function does 2 things:
    // - Check for planned but not yet machined quantities and if found remove them
    //   and store locally in the machine watch database with a new DecrementId.
    // - Load all decremented quantities (including the potentially new quantities)
    //   strictly after the given decrement ID.
    //Thus this function can be called multiple times to receive the same data.
    List<JobAndDecrementQuantity> DecrementJobQuantites(long loadDecrementsStrictlyAfterDecrementId);
    List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC);

    /// Add new castings.  The casting has not yet been assigned to a specific job,
    /// and will be assigned to the job with remaining demand and earliest priority.
    /// The serial is optional and is passed only if the material has already been marked with a serial.
    List<InProcessMaterial> AddUnallocatedCastingToQueue(
      string casting,
      int qty,
      string queue,
      IList<string> serial,
      string workorder,
      string operatorName
    );

    /// Add a new unprocessed piece of material for the given job into the given queue.  The serial is optional
    /// and is passed only if the material has already been marked with a serial.
    /// Use -1 or 0 for lastCompletedProcess if the material is a casting.
    InProcessMaterial AddUnprocessedMaterialToQueue(
      string jobUnique,
      int lastCompletedProcess,
      string queue,
      int position,
      string serial,
      string workorder,
      string operatorName = null
    );

    /// Add material into a queue or just into free material if the queue name is the empty string.
    /// The material will be inserted into the given position, bumping any later material to a
    /// larger position.  If the material is currently in another queue or a different position,
    /// it will be removed and placed in the given position.
    void SetMaterialInQueue(long materialId, string queue, int position, string operatorName = null);

    // If true, material that is currently being loaded onto a pallet can be canceled by calling
    // SignalMaterialForQuarantine.  Otherwise, SignalMaterialForQuarantine will give an error
    // for currently loading material.
    bool AllowQuarantineToCancelLoad { get; }

    /// Mark the material for quarantine.  If the material is already in a queue, it is directly moved.
    /// If the material is still on a pallet, it will be moved after unload completes.
    void SignalMaterialForQuarantine(long materialId, string operatorName = null, string reason = null);

    void RemoveMaterialFromAllQueues(IList<long> materialIds, string operatorName = null);

    void SwapMaterialOnPallet(int pallet, long oldMatId, long newMatId, string operatorName = null);
    event EditMaterialInLogDelegate OnEditMaterialInLog;

    void InvalidatePalletCycle(
      long matId,
      int process,
      string oldMatPutInQueue = null,
      string operatorName = null
    );
  }

  public sealed class JobsAndQueuesFromDb<St> : IJobAndQueueControl, IDisposable
    where St : ICellState
  {
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<JobsAndQueuesFromDb<St>>();

    private readonly RepositoryConfig _repo;
    private readonly ServerSettings _serverSettings;
    private readonly FMSSettings _settings;
    private readonly ISynchronizeCellState<St> _syncState;
    private readonly IEnumerable<IAdditionalCheckJobs> _additionalCheckJobs;

    public event NewCurrentStatus OnNewCurrentStatus;
    public bool AllowQuarantineToCancelLoad => _syncState.AllowQuarantineToCancelLoad;

    public JobsAndQueuesFromDb(
      RepositoryConfig repo,
      ServerSettings serverSt,
      FMSSettings settings,
      ISynchronizeCellState<St> syncSt,
      IEnumerable<IAdditionalCheckJobs> additionalCheckJobs = null,
      bool startThread = true
    )
    {
      _repo = repo;
      _settings = settings;
      _serverSettings = serverSt;
      _syncState = syncSt;
      _additionalCheckJobs = additionalCheckJobs ?? Enumerable.Empty<IAdditionalCheckJobs>();
      _syncState.NewCellState += NewCellState;

      _thread = new Thread(Thread) { IsBackground = true };
      if (startThread)
      {
        _thread.Start();
      }
    }

    public void StartThread()
    {
      _thread.Start();
    }

    public void Dispose()
    {
      _syncState.NewCellState -= NewCellState;
      _shutdown.Set();
      _thread.Join(TimeSpan.FromSeconds(5));
    }

    // changeLock prevents decrement and queue changes from occuring at the same time as the thread
    // is deciding what to put onto a pallet
    private readonly Lock _changeLock = new();

    // curStLock protects _lastCurrentStatus and _syncError field
    private readonly Lock _curStLock = new();
    private St _lastCurrentStatus = default;
    private string _syncError = null;

    #region Thread and Messages
    private readonly Thread _thread;
    private readonly AutoResetEvent _shutdown = new(false);
    private readonly AutoResetEvent _recheck = new(false);
    private readonly ManualResetEvent _newCellState = new(false);
    private DateTime _timeOfLastStatusDump = DateTime.MinValue;

    public void RecalculateCellState()
    {
      _recheck.Set();
    }

    private void NewCellState()
    {
      _newCellState.Set();
    }

    private void Thread()
    {
      bool raiseNewCurStatus = true; // very first run should raise new pallet state
      while (true)
      {
        try
        {
          var untilRefresh = Synchronize(raiseNewCurStatus);

          var ret = WaitHandle.WaitAny([_shutdown, _recheck, _newCellState], untilRefresh, false);
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
              _lastCurrentStatus = st;
            }

            if (raiseNewCurStatus && Log.IsEnabled(Serilog.Events.LogEventLevel.Verbose))
            {
              Log.Verbose("New current status: {@state}", st);
              _timeOfLastStatusDump = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - _timeOfLastStatusDump > TimeSpan.FromMinutes(10))
            {
              Log.Debug("Periodic cell state: {@state}", st);
              _timeOfLastStatusDump = DateTime.UtcNow;
            }

            actionPerformed = _syncState.ApplyActions(db, st);
            if (actionPerformed)
              raiseNewCurStatus = true;
          } while (actionPerformed);

          _syncronizeErrorCount = 0;
          if (_syncError != null)
          {
            _syncError = null;
            raiseNewCurStatus = true;
          }

          return timeUntilNextRefresh;
        }
      }
      catch (Exception ex)
      {
        lock (_curStLock)
        {
          if (_syncError != ex.Message)
          {
            _syncError = ex.Message;
            raiseNewCurStatus = true;
          }
        }

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
          OnNewCurrentStatus?.Invoke(GetCurrentStatus());
        }
      }
    }

    private void DecrementPlannedButNotStartedQty(IRepository jobDB)
    {
      bool requireStateRefresh = false;
      lock (_changeLock)
      {
        St st;
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

    public CurrentStatus GetCurrentStatus()
    {
      lock (_curStLock)
      {
        if (_lastCurrentStatus == null)
        {
          return new CurrentStatus()
          {
            TimeOfCurrentStatusUTC = DateTime.UtcNow,
            Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
            Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
            Material = [],
            Queues = ImmutableDictionary<string, QueueInfo>.Empty,
            Alarms = ["FMS Insight is starting up..."],
          };
        }
        else if (_syncError != null)
        {
          return _lastCurrentStatus.CurrentStatus with
          {
            Alarms = _lastCurrentStatus.CurrentStatus.Alarms.Add(
              $"Error communicating with machines: {_syncError}. Will try again in a few minutes."
            ),
          };
        }
        else
        {
          if (
            _serverSettings.ExpectedTimeZone != null
            && _serverSettings.ExpectedTimeZone.Id != TimeZoneInfo.Local.Id
          )
          {
            return _lastCurrentStatus.CurrentStatus with
            {
              Alarms = _lastCurrentStatus.CurrentStatus.Alarms.Add(
                $"The server is running in timezone {TimeZoneInfo.Local.Id} but was expected to be {_serverSettings.ExpectedTimeZone.Id}."
              ),
            };
          }
          else
          {
            return _lastCurrentStatus.CurrentStatus;
          }
        }
      }
    }

    List<string> IJobAndQueueControl.CheckValidRoutes(IEnumerable<Job> newJobs)
    {
      using var jdb = _repo.OpenConnection();
      var newJ = new NewJobs() { ScheduleId = null, Jobs = newJobs.ToImmutableList() };
      return _syncState
        .CheckNewJobs(jdb, newJ)
        .Concat(_additionalCheckJobs.SelectMany(c => c.CheckNewJobs(jdb, newJ)))
        .ToList();
    }

    void IJobAndQueueControl.AddJobs(NewJobs jobs, string expectedPreviousScheduleId)
    {
      using (var jdb = _repo.OpenConnection())
      {
        Log.Debug("Adding new jobs {@jobs}", jobs);
        var errors = _syncState
          .CheckNewJobs(jdb, jobs)
          .Concat(_additionalCheckJobs.SelectMany(c => c.CheckNewJobs(jdb, jobs)));
        if (errors.Any())
        {
          throw new BadRequestException(string.Join(Environment.NewLine, errors));
        }

        jdb.AddJobs(
          jobs,
          expectedPreviousScheduleId,
          addAsCopiedToSystem: _syncState.AddJobsAsCopiedToSystem
        );
      }

      OnNewJobs?.Invoke(jobs);

      RecalculateCellState();
    }

    List<JobAndDecrementQuantity> IJobAndQueueControl.DecrementJobQuantites(
      long loadDecrementsStrictlyAfterDecrementId
    )
    {
      using var jdb = _repo.OpenConnection();
      DecrementPlannedButNotStartedQty(jdb);
      return jdb.LoadDecrementQuantitiesAfter(loadDecrementsStrictlyAfterDecrementId);
    }

    List<JobAndDecrementQuantity> IJobAndQueueControl.DecrementJobQuantites(
      DateTime loadDecrementsAfterTimeUTC
    )
    {
      using var jdb = _repo.OpenConnection();
      DecrementPlannedButNotStartedQty(jdb);
      return jdb.LoadDecrementQuantitiesAfter(loadDecrementsAfterTimeUTC);
    }

    void IJobAndQueueControl.SetJobComment(string jobUnique, string comment)
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

    public List<InProcessMaterial> AddUnallocatedCastingToQueue(
      string casting,
      int qty,
      string queue,
      IList<string> serial,
      string workorder,
      string operatorName
    )
    {
      using var ldb = _repo.OpenConnection();

      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      // num proc will be set later once it is allocated to a specific job
      var mats = new List<InProcessMaterial>();

      var newMats = ldb.BulkAddNewCastingsInQueue(
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
              QueuePosition = log.LocationNum,
            },
            Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
            SignaledInspections = [],
            QuarantineAfterUnload = null,
          }
        );
      }

      RecalculateCellState();

      return mats;
    }

    public InProcessMaterial AddUnprocessedMaterialToQueue(
      string jobUnique,
      int process,
      string queue,
      int position,
      string serial,
      string workorder,
      string operatorName
    )
    {
      if (!_settings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      Log.Debug(
        "Adding unprocessed material for job {job} proc {proc} to queue {queue} in position {pos} with serial {serial} and workorder {workorder}",
        jobUnique,
        process,
        queue,
        position,
        serial,
        workorder
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
              Face = 0,
            },
            serial,
            DateTime.UtcNow
          );
        }
        if (!string.IsNullOrEmpty(workorder))
        {
          ldb.RecordWorkorderForMaterialID(
            new BlackMaple.MachineFramework.EventLogMaterial()
            {
              MaterialID = matId,
              Process = process,
              Face = 0,
            },
            workorder,
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
        WorkorderId = workorder,
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.InQueue,
          CurrentQueue = queue,
          QueuePosition = logEvt.LastOrDefault()?.LocationNum,
        },
        Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
        SignaledInspections = [],
        QuarantineAfterUnload = null,
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
            st = _lastCurrentStatus?.CurrentStatus;
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
          st = _lastCurrentStatus?.CurrentStatus;
        }

        using var ldb = _repo.OpenConnection();
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
            st = _lastCurrentStatus?.CurrentStatus;
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
                if (!_syncState.AllowQuarantineToCancelLoad)
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
                      Face = 0,
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
                when !string.IsNullOrEmpty(_settings.QuarantineQueue)
                  && _syncState.AllowQuarantineToCancelLoad:
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
                    reason: "Quarantine"
                  );
                  requireStateRefresh = true;
                }
                break;

              case (InProcessMaterialLocation.LocType.InQueue, InProcessMaterialAction.ActionType.Waiting)
                when string.IsNullOrEmpty(_settings.QuarantineQueue):
              case (InProcessMaterialLocation.LocType.InQueue, InProcessMaterialAction.ActionType.Loading)
                when string.IsNullOrEmpty(_settings.QuarantineQueue)
                  && _syncState.AllowQuarantineToCancelLoad:
                {
                  var nextProc = ldb.NextProcessForQueuedMaterial(materialId);
                  var proc = (nextProc ?? 1) - 1;
                  ldb.RecordOperatorNotes(
                    materialId: materialId,
                    process: proc,
                    notes: reason,
                    operatorName: operatorName
                  );
                  ldb.BulkRemoveMaterialFromAllQueues(
                    new[] { materialId },
                    operatorName: operatorName,
                    reason: "Quarantine"
                  );
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
