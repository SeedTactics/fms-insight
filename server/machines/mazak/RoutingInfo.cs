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
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public class RoutingInfo
    : BlackMaple.MachineFramework.IJobControl,
      BlackMaple.MachineFramework.IQueueControl
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<RoutingInfo>();

    public event NewJobsDelegate OnNewJobs;
    public event EditMaterialInLogDelegate OnEditMaterialInLog;

    private IWriteData writeDb;
    private IReadDataAccess readDatabase;
    private IMazakLogReader logReader;
    private BlackMaple.MachineFramework.RepositoryConfig logDbCfg;
    private IWriteJobs _writeJobs;
    private IMachineGroupName _machineGroupName;
    private IDecrementPlanQty _decr;
    private readonly IQueueSyncFault queueFault;
    private readonly MazakConfig _mazakCfg;
    private System.Timers.Timer _copySchedulesTimer;
    private readonly BlackMaple.MachineFramework.FMSSettings fmsSettings;
    private readonly Action<CurrentStatus> _onCurStatusChange;
    public readonly bool _useStartingOffsetForDueDate;

    public RoutingInfo(
      IWriteData d,
      IMachineGroupName machineGroupName,
      IReadDataAccess readDb,
      IMazakLogReader logR,
      BlackMaple.MachineFramework.RepositoryConfig jLogCfg,
      IWriteJobs wJobs,
      IQueueSyncFault queueSyncFault,
      IDecrementPlanQty decrement,
      bool useStartingOffsetForDueDate,
      BlackMaple.MachineFramework.FMSSettings settings,
      Action<CurrentStatus> onStatusChange,
      MazakConfig mazakCfg
    )
    {
      writeDb = d;
      readDatabase = readDb;
      fmsSettings = settings;
      logReader = logR;
      logDbCfg = jLogCfg;
      _writeJobs = wJobs;
      _decr = decrement;
      _mazakCfg = mazakCfg;
      _machineGroupName = machineGroupName;
      queueFault = queueSyncFault;
      _useStartingOffsetForDueDate = useStartingOffsetForDueDate;
      _onCurStatusChange = onStatusChange;

      _copySchedulesTimer = new System.Timers.Timer(TimeSpan.FromMinutes(4.5).TotalMilliseconds);
      _copySchedulesTimer.Elapsed += (sender, args) => RecopyJobsToSystem();
      _copySchedulesTimer.Start();
    }

    public void Halt()
    {
      _copySchedulesTimer.Stop();
    }

    #region Reading
    CurrentStatus BlackMaple.MachineFramework.IJobControl.GetCurrentStatus()
    {
      using (var log = logDbCfg.OpenConnection())
      {
        return CurrentStatus(log);
      }
    }

    public CurrentStatus CurrentStatus(BlackMaple.MachineFramework.IRepository eventLogDB)
    {
      MazakAllData mazakData;
      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      try
      {
        mazakData = readDatabase.LoadAllData();
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }

      var st = MazakMachineInterface.BuildCurrentStatus.Build(
        eventLogDB,
        fmsSettings,
        _machineGroupName,
        queueFault,
        readDatabase.MazakType,
        mazakData,
        DateTime.UtcNow
      );
      if (_mazakCfg != null && _mazakCfg.AdjustCurrentStatus != null)
      {
        st = _mazakCfg.AdjustCurrentStatus(eventLogDB, st);
      }
      return st;
    }

    #endregion

    #region "Write Routing Info"

    List<string> BlackMaple.MachineFramework.IJobControl.CheckValidRoutes(
      IEnumerable<BlackMaple.MachineFramework.Job> jobs
    )
    {
      var logMessages = new List<string>();
      MazakAllData mazakData;

      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      try
      {
        mazakData = readDatabase.LoadAllData();
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }

      try
      {
        using (var jobDB = logDbCfg.OpenConnection())
        {
          ProgramRevision lookupProg(string prog, long? rev)
          {
            if (rev.HasValue)
            {
              return jobDB.LoadProgram(prog, rev.Value);
            }
            else
            {
              return jobDB.LoadMostRecentProgram(prog);
            }
          }

          //The reason we create the clsPalletPartMapping is to see if it throws any exceptions.  We therefore
          //need to ignore the warning that palletPartMap is not used.
#pragma warning disable 168, 219
          var mazakJobs = ConvertJobsToMazakParts.JobsToMazak(
            jobs: jobs,
            downloadUID: 1,
            mazakData: mazakData,
            savedParts: new HashSet<string>(),
            MazakType: writeDb.MazakType,
            useStartingOffsetForDueDate: _useStartingOffsetForDueDate,
            fmsSettings: fmsSettings,
            lookupProgram: lookupProg,
            errors: logMessages
          );
#pragma warning restore 168, 219
        }
      }
      catch (Exception ex)
      {
        if (ex.Message.StartsWith("Invalid pallet->part mapping"))
        {
          logMessages.Add(ex.Message);
        }
        else
        {
          throw;
        }
      }

      return logMessages;
    }

    void BlackMaple.MachineFramework.IJobControl.AddJobs(
      NewJobs newJ,
      string expectedPreviousScheduleId,
      bool waitForCopyToCell
    )
    {
      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      CurrentStatus curSt;
      try
      {
        if (_mazakCfg != null && _mazakCfg.NewJobTransform != null)
        {
          newJ = _mazakCfg.NewJobTransform(newJ);
        }
        using (var jobDB = logDbCfg.OpenConnection())
        {
          _writeJobs.AddJobs(jobDB, newJ, expectedPreviousScheduleId);
          logReader.RecheckQueues(wait: false);
          curSt = CurrentStatus(jobDB);
        }
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }

      OnNewJobs?.Invoke(newJ);
      _onCurStatusChange(curSt);
    }

    private void RecopyJobsToSystem()
    {
      try
      {
        if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
        {
          throw new Exception("Unable to obtain mazak database lock");
        }
        try
        {
          using (var jobDB = logDbCfg.OpenConnection())
          {
            _writeJobs.RecopyJobsToMazak(jobDB);
            logReader.RecheckQueues(wait: false);
          }
        }
        finally
        {
          OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error recopying job schedules to mazak");
      }
    }

    void BlackMaple.MachineFramework.IJobControl.SetJobComment(string jobUnique, string comment)
    {
      CurrentStatus st;
      using (var jdb = logDbCfg.OpenConnection())
      {
        jdb.SetJobComment(jobUnique, comment);
        st = CurrentStatus(jdb);
      }
      _onCurStatusChange(st);
    }

    public void ReplaceWorkordersForSchedule(
      string scheduleId,
      IEnumerable<Workorder> newWorkorders,
      IEnumerable<NewProgramContent> programs
    )
    {
      if (newWorkorders != null && newWorkorders.Any(w => w.Programs != null && w.Programs.Any()))
      {
        throw new BlackMaple.MachineFramework.BadRequestException(
          "Mazak does not support per-workorder programs"
        );
      }
      using (var jdb = logDbCfg.OpenConnection())
      {
        jdb.ReplaceWorkordersForSchedule(scheduleId, newWorkorders, programs);
      }
    }
    #endregion

    #region "Decrement Plan Quantity"
    List<JobAndDecrementQuantity> BlackMaple.MachineFramework.IJobControl.DecrementJobQuantites(
      long loadDecrementsStrictlyAfterDecrementId
    )
    {
      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      List<JobAndDecrementQuantity> ret;
      try
      {
        using (var jdb = logDbCfg.OpenConnection())
        {
          _decr.Decrement(jdb);
          ret = jdb.LoadDecrementQuantitiesAfter(loadDecrementsStrictlyAfterDecrementId);
        }
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }
      logReader.RecheckQueues(wait: false);
      return ret;
    }

    List<JobAndDecrementQuantity> BlackMaple.MachineFramework.IJobControl.DecrementJobQuantites(
      DateTime loadDecrementsAfterTimeUTC
    )
    {
      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      List<JobAndDecrementQuantity> ret;
      try
      {
        using (var jdb = logDbCfg.OpenConnection())
        {
          _decr.Decrement(jdb);
          ret = jdb.LoadDecrementQuantitiesAfter(loadDecrementsAfterTimeUTC);
        }
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }
      logReader.RecheckQueues(wait: false);
      return ret;
    }

    #endregion

    #region Queues
    InProcessMaterial BlackMaple.MachineFramework.IQueueControl.AddUnallocatedPartToQueue(
      string partName,
      string queue,
      string serial,
      string operatorName
    )
    {
      string casting = partName;

      // try and see if there is a job for this part with an actual casting
      IReadOnlyList<BlackMaple.MachineFramework.HistoricJob> sch;
      using (var jdb = logDbCfg.OpenConnection())
      {
        sch = jdb.LoadUnarchivedJobs();
      }
      var job = sch.FirstOrDefault(j => j.PartName == partName);
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

      var mats = ((BlackMaple.MachineFramework.IQueueControl)this).AddUnallocatedCastingToQueue(
        casting,
        1,
        queue,
        string.IsNullOrEmpty(serial) ? new string[] { } : new string[] { serial },
        operatorName
      );
      return mats.FirstOrDefault();
    }

    List<InProcessMaterial> BlackMaple.MachineFramework.IQueueControl.AddUnallocatedCastingToQueue(
      string casting,
      int qty,
      string queue,
      IList<string> serial,
      string operatorName
    )
    {
      if (!fmsSettings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      CurrentStatus newSt;
      HashSet<long> matIds;
      using (var logDb = logDbCfg.OpenConnection())
      {
        var mats = logDb.BulkAddNewCastingsInQueue(
          casting,
          qty,
          queue,
          serial,
          operatorName,
          reason: "SetByOperator"
        );
        matIds = mats.MaterialIds;

        logReader.RecheckQueues(wait: true);

        newSt = CurrentStatus(logDb);
      }
      _onCurStatusChange(newSt);

      return newSt.Material.Where(m => matIds.Contains(m.MaterialID)).ToList();
    }

    InProcessMaterial BlackMaple.MachineFramework.IQueueControl.AddUnprocessedMaterialToQueue(
      string jobUnique,
      int process,
      string queue,
      int position,
      string serial,
      string operatorName
    )
    {
      if (!fmsSettings.Queues.ContainsKey(queue))
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

      CurrentStatus st;
      long matId;
      using (var logDb = logDbCfg.OpenConnection())
      {
        var job = logDb.LoadJob(jobUnique);
        if (job == null)
          throw new BlackMaple.MachineFramework.BadRequestException("Unable to find job " + jobUnique);

        int procToCheck = Math.Max(1, process);
        if (procToCheck > job.Processes.Count)
          throw new BlackMaple.MachineFramework.BadRequestException("Invalid process " + process.ToString());

        matId = logDb.AllocateMaterialID(jobUnique, job.PartName, job.Processes.Count);
        if (!string.IsNullOrEmpty(serial))
        {
          logDb.RecordSerialForMaterialID(
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
        // the add to queue log entry will use the process, so later when we lookup the latest completed process
        // for the material in the queue, it will be correctly computed.
        logDb.RecordAddMaterialToQueue(
          matID: matId,
          process: process,
          queue: queue,
          position: position,
          operatorName: operatorName,
          reason: "SetByOperator"
        );
        logDb.RecordPathForProcess(matId, Math.Max(1, process), 1);

        logReader.RecheckQueues(wait: true);

        st = CurrentStatus(logDb);
      }

      _onCurStatusChange(st);
      return st.Material.FirstOrDefault(m => m.MaterialID == matId);
    }

    void BlackMaple.MachineFramework.IQueueControl.SetMaterialInQueue(
      long materialId,
      string queue,
      int position,
      string operatorName
    )
    {
      if (!fmsSettings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }
      Log.Debug("Adding material {matId} to queue {queue} in position {pos}", materialId, queue, position);

      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(3), true))
      {
        return;
      }
      try
      {
        using (var logDb = logDbCfg.OpenConnection())
        {
          var status = CurrentStatus(logDb);

          var mat = status.Material.FirstOrDefault(m => m.MaterialID == materialId);
          if (mat != null && mat.Location.Type == InProcessMaterialLocation.LocType.OnPallet)
          {
            throw new BadRequestException("Material on pallet can not be moved to a queue");
          }
          else if (
            mat != null
            && mat.Action.Type != InProcessMaterialAction.ActionType.Waiting
            && mat.Location.CurrentQueue != queue
          )
          {
            throw new BadRequestException("Only waiting material can be moved between queues");
          }
          else
          {
            var nextProc = logDb.NextProcessForQueuedMaterial(materialId);
            var proc = (nextProc ?? 1) - 1;
            logDb.RecordAddMaterialToQueue(
              matID: materialId,
              process: proc,
              queue: queue,
              position: position,
              operatorName: operatorName,
              reason: "SetByOperator"
            );
          }
        }
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }

      logReader.RecheckQueues(wait: true);
    }

    void BlackMaple.MachineFramework.IQueueControl.RemoveMaterialFromAllQueues(
      IList<long> materialIds,
      string operatorName
    )
    {
      Log.Debug("Removing {@matId} from all queues", materialIds);

      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(3), true))
      {
        return;
      }
      try
      {
        using (var logDb = logDbCfg.OpenConnection())
        {
          var status = CurrentStatus(logDb);

          foreach (var matId in materialIds)
          {
            var mat = status.Material.FirstOrDefault(m => m.MaterialID == matId);
            if (mat != null && mat.Location.Type == InProcessMaterialLocation.LocType.OnPallet)
            {
              throw new BadRequestException("Material on pallet can not be removed from queues");
            }
            else if (mat != null && mat.Action.Type != InProcessMaterialAction.ActionType.Waiting)
            {
              throw new BadRequestException("Only waiting material can be removed from queues");
            }
          }

          logDb.BulkRemoveMaterialFromAllQueues(materialIds, operatorName);
        }
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }

      logReader.RecheckQueues(wait: true);
    }

    bool BlackMaple.MachineFramework.IQueueControl.AllowQuarantineToCancelLoad { get; } = false;

    void BlackMaple.MachineFramework.IQueueControl.SignalMaterialForQuarantine(
      long materialId,
      string operatorName,
      string reason
    )
    {
      Log.Debug("Signaling {matId} for quarantine", materialId);

      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(3), true))
      {
        return;
      }
      try
      {
        using (var logDb = logDbCfg.OpenConnection())
        {
          var status = CurrentStatus(logDb);

          var mat = status.Material.FirstOrDefault(m => m.MaterialID == materialId);

          if (mat == null)
          {
            throw new BadRequestException("Material not found");
          }

          switch (mat.Location.Type, mat.Action.Type)
          {
            case (InProcessMaterialLocation.LocType.OnPallet, _):
            {
              if (mat.Action.Type == InProcessMaterialAction.ActionType.Loading)
              {
                throw new BadRequestException("Material on pallet can not be quarantined while loading");
              }
              else
              {
                // If the material will eventually stay on the pallet, disallow quarantine
                var job = status.Jobs.GetValueOrDefault(mat.JobUnique);
                if (job == null)
                {
                  throw new BadRequestException("Unable to find job for material");
                }
                var path = job.Processes[mat.Process - 1].Paths[mat.Path - 1];
                if (mat.Process != job.Processes.Count && (path == null || path.OutputQueue == null))
                {
                  throw new BadRequestException(
                    "Can only signal material for quarantine if the current process and path has an output queue"
                  );
                }
              }

              logDb.SignalMaterialForQuarantine(
                mat: new EventLogMaterial()
                {
                  MaterialID = materialId,
                  Process = mat.Process,
                  Face = ""
                },
                pallet: mat.Location.PalletNum ?? 0,
                queue: fmsSettings.QuarantineQueue ?? "",
                timeUTC: null,
                operatorName: operatorName,
                reason: reason
              );
              break;
            }

            case (InProcessMaterialLocation.LocType.Free, InProcessMaterialAction.ActionType.Waiting)
              when !string.IsNullOrEmpty(fmsSettings.QuarantineQueue):
            case (InProcessMaterialLocation.LocType.InQueue, InProcessMaterialAction.ActionType.Waiting)
              when !string.IsNullOrEmpty(fmsSettings.QuarantineQueue):

              {
                var nextProc = logDb.NextProcessForQueuedMaterial(materialId);
                var proc = (nextProc ?? 1) - 1;
                logDb.RecordOperatorNotes(
                  materialId: materialId,
                  process: proc,
                  notes: reason,
                  operatorName: operatorName
                );
                logDb.RecordAddMaterialToQueue(
                  matID: materialId,
                  process: proc,
                  queue: fmsSettings.QuarantineQueue,
                  position: -1,
                  operatorName: operatorName,
                  reason: "SetByOperator"
                );
              }
              break;

            case (InProcessMaterialLocation.LocType.InQueue, InProcessMaterialAction.ActionType.Waiting)
              when string.IsNullOrEmpty(fmsSettings.QuarantineQueue):

              {
                var nextProc = logDb.NextProcessForQueuedMaterial(materialId);
                var proc = (nextProc ?? 1) - 1;
                logDb.RecordOperatorNotes(
                  materialId: materialId,
                  process: proc,
                  notes: reason,
                  operatorName: operatorName
                );
                logDb.BulkRemoveMaterialFromAllQueues(new[] { materialId }, operatorName);
              }
              break;

            default:
              throw new BadRequestException("Invalid material state for quarantine");
          }
        }
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }

      logReader.RecheckQueues(wait: true);
    }

    public void SwapMaterialOnPallet(int pallet, long oldMatId, long newMatId, string operatorName = null)
    {
      Log.Debug("Overriding {oldMat} to {newMat} on pallet {pal}", oldMatId, newMatId, pallet);

      using (var logDb = logDbCfg.OpenConnection())
      {
        var o = logDb.SwapMaterialInCurrentPalletCycle(
          pallet: pallet,
          oldMatId: oldMatId,
          newMatId: newMatId,
          operatorName: operatorName,
          quarantineQueue: fmsSettings.QuarantineQueue
        );

        OnEditMaterialInLog?.Invoke(
          new EditMaterialInLogEvents()
          {
            OldMaterialID = oldMatId,
            NewMaterialID = newMatId,
            EditedEvents = o.ChangedLogEntries,
          }
        );
        _onCurStatusChange(CurrentStatus(logDb));
      }
    }

    public void InvalidatePalletCycle(
      long matId,
      int process,
      string oldMatPutInQueue = null,
      string operatorName = null
    )
    {
      Log.Debug("Invalidating pallet cycle for {matId} and {process}", matId, process);

      using (var logDb = logDbCfg.OpenConnection())
      {
        logDb.InvalidatePalletCycle(
          matId: matId,
          process: process,
          oldMatPutInQueue: oldMatPutInQueue,
          operatorName: operatorName
        );
        _onCurStatusChange(CurrentStatus(logDb));
      }
    }
    #endregion
  }
}
