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

namespace MazakMachineInterface
{
  public class RoutingInfo : BlackMaple.MachineFramework.IJobControl, BlackMaple.MachineFramework.IOldJobDecrement
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<RoutingInfo>();

    private IWriteData writeDb;
    private IReadDataAccess readDatabase;
    private IMazakLogReader logReader;
    private BlackMaple.MachineFramework.RepositoryConfig logDbCfg;
    private IWriteJobs _writeJobs;
    private IMachineGroupName _machineGroupName;
    private IDecrementPlanQty _decr;
    private readonly IQueueSyncFault queueFault;
    private System.Timers.Timer _copySchedulesTimer;
    private readonly BlackMaple.MachineFramework.FMSSettings fmsSettings;
    private readonly Action<NewJobs> _onNewJobs;
    private readonly Action<CurrentStatus> _onCurStatusChange;
    private readonly Action<EditMaterialInLogEvents> _onEditMatInLog;
    public readonly bool _useStartingOffsetForDueDate;

    public Action<NewJobs> NewJobTransform = null;

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
      Action<NewJobs> onNewJobs,
      Action<CurrentStatus> onStatusChange,
      Action<EditMaterialInLogEvents> onEditMatInLog
    )
    {
      writeDb = d;
      readDatabase = readDb;
      fmsSettings = settings;
      logReader = logR;
      logDbCfg = jLogCfg;
      _writeJobs = wJobs;
      _decr = decrement;
      _machineGroupName = machineGroupName;
      queueFault = queueSyncFault;
      _useStartingOffsetForDueDate = useStartingOffsetForDueDate;
      _onNewJobs = onNewJobs;
      _onCurStatusChange = onStatusChange;
      _onEditMatInLog = onEditMatInLog;

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

      return MazakMachineInterface.BuildCurrentStatus.Build(eventLogDB, fmsSettings, _machineGroupName, queueFault, readDatabase.MazakType, mazakData, DateTime.UtcNow);
    }

    #endregion

    #region "Write Routing Info"

    List<string> BlackMaple.MachineFramework.IJobControl.CheckValidRoutes(IEnumerable<BlackMaple.MachineFramework.Job> jobs)
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
            jobs: jobs.Select(j => LegacyJobConversions.ToLegacyJob(j, copiedToSystem: false)).ToArray(),
            downloadUID: 1,
            mazakData: mazakData,
            savedParts: new HashSet<string>(),
            MazakType: writeDb.MazakType,
            useStartingOffsetForDueDate: _useStartingOffsetForDueDate,
            fmsSettings: fmsSettings,
            lookupProgram: lookupProg,
            errors: logMessages);
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

    void BlackMaple.MachineFramework.IJobControl.AddJobs(NewJobs newJ, string expectedPreviousScheduleId)
    {
      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      CurrentStatus curSt;
      try
      {
        NewJobTransform?.Invoke(newJ);
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

      _onNewJobs(newJ);
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

    public void ReplaceWorkordersForSchedule(string scheduleId, IEnumerable<PartWorkorder> newWorkorders, IEnumerable<ProgramEntry> programs)
    {
      if (newWorkorders != null && newWorkorders.Any(w => w.Programs != null && w.Programs.Any()))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Mazak does not support per-workorder programs");
      }
      using (var jdb = logDbCfg.OpenConnection())
      {
        jdb.ReplaceWorkordersForSchedule(scheduleId, newWorkorders, programs);
      }
    }
    #endregion

    #region "Decrement Plan Quantity"
    List<JobAndDecrementQuantity> BlackMaple.MachineFramework.IJobControl.DecrementJobQuantites(long loadDecrementsStrictlyAfterDecrementId)
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
    List<JobAndDecrementQuantity> BlackMaple.MachineFramework.IJobControl.DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
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

    Dictionary<JobAndPath, int> BlackMaple.MachineFramework.IOldJobDecrement.OldDecrementJobQuantites()
    {
      throw new NotImplementedException();
    }

    void BlackMaple.MachineFramework.IOldJobDecrement.OldFinalizeDecrement()
    {
      throw new NotImplementedException();
    }
    #endregion

    #region Queues
    InProcessMaterial BlackMaple.MachineFramework.IJobControl.AddUnallocatedPartToQueue(string partName, string queue, int position, string serial, string operatorName)
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
          if (!string.IsNullOrEmpty(casting))
          {
            casting = jobCasting;
            break;
          }
        }
      }

      var mats = ((BlackMaple.MachineFramework.IJobControl)this).AddUnallocatedCastingToQueue(casting, 1, queue, position, string.IsNullOrEmpty(serial) ? new string[] { } : new string[] { serial }, operatorName);
      return mats.FirstOrDefault();
    }

    List<InProcessMaterial> BlackMaple.MachineFramework.IJobControl.AddUnallocatedCastingToQueue(string casting, int qty, string queue, int position, IList<string> serial, string operatorName)
    {
      if (!fmsSettings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      CurrentStatus newSt;
      var matIds = new HashSet<long>();
      using (var logDb = logDbCfg.OpenConnection())
      {
        for (int i = 0; i < qty; i++)
        {
          var matId = logDb.AllocateMaterialIDForCasting(casting);
          matIds.Add(matId);

          Log.Debug("Adding unprocessed casting for casting {casting} to queue {queue} in position {pos} with serial {serial}. " +
                    "Assigned matId {matId}",
            casting, queue, position, serial, matId
          );

          if (i < serial.Count)
          {
            logDb.RecordSerialForMaterialID(
              new BlackMaple.MachineFramework.EventLogMaterial()
              {
                MaterialID = matId,
                Process = 0,
                Face = ""
              },
              serial[i]);
          }
          // the add to queue log entry will use the process, so later when we lookup the latest completed process
          // for the material in the queue, it will be correctly computed.
          logDb.RecordAddMaterialToQueue(
            matID: matId,
            process: 0,
            queue: queue,
            position: position >= 0 ? position + i : -1,
            operatorName: operatorName,
            reason: "SetByOperator"
          );
        }

        logReader.RecheckQueues(wait: true);

        newSt = CurrentStatus(logDb);
      }
      _onCurStatusChange(newSt);

      return newSt.Material.Where(m => matIds.Contains(m.MaterialID)).ToList();
    }

    InProcessMaterial BlackMaple.MachineFramework.IJobControl.AddUnprocessedMaterialToQueue(string jobUnique, int process, int pathGroup, string queue, int position, string serial, string operatorName)
    {
      if (!fmsSettings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }
      Log.Debug("Adding unprocessed material for job {job} proc {proc} group {pathGroup} to queue {queue} in position {pos} with serial {serial}",
        jobUnique, process, pathGroup, queue, position, serial
      );

      CurrentStatus st;
      long matId;
      using (var logDb = logDbCfg.OpenConnection())
      {
        var job = logDb.LoadJob(jobUnique)?.ToLegacyJob();
        if (job == null) throw new BlackMaple.MachineFramework.BadRequestException("Unable to find job " + jobUnique);

        int? path = null;
        for (var p = 1; p <= job.GetNumPaths(Math.Max(1, process)); p++)
        {
          if (job.GetPathGroup(Math.Max(1, process), p) == pathGroup)
          {
            path = p;
            break;
          }
        }
        if (!path.HasValue) throw new BlackMaple.MachineFramework.BadRequestException("Unable to find path group " + pathGroup.ToString() + " for job " + jobUnique + " and process " + process.ToString());

        matId = logDb.AllocateMaterialID(jobUnique, job.PartName, job.NumProcesses);
        if (!string.IsNullOrEmpty(serial))
        {
          logDb.RecordSerialForMaterialID(
            new BlackMaple.MachineFramework.EventLogMaterial()
            {
              MaterialID = matId,
              Process = process,
              Face = ""
            },
            serial);
        }
        // the add to queue log entry will use the process, so later when we lookup the latest completed process
        // for the material in the queue, it will be correctly computed.
        logDb.RecordAddMaterialToQueue(
          matID: matId,
          process: process,
          queue: queue,
          position: position,
          operatorName: operatorName,
          reason: "SetByOperator");
        logDb.RecordPathForProcess(matId, Math.Max(1, process), path.Value);

        logReader.RecheckQueues(wait: true);

        st = CurrentStatus(logDb);
      }

      _onCurStatusChange(st);
      return st.Material.FirstOrDefault(m => m.MaterialID == matId);
    }

    void BlackMaple.MachineFramework.IJobControl.SetMaterialInQueue(long materialId, string queue, int position, string operatorName)
    {
      if (!fmsSettings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }
      Log.Debug("Adding material {matId} to queue {queue} in position {pos}",
        materialId, queue, position
      );

      CurrentStatus status;
      using (var logDb = logDbCfg.OpenConnection())
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
        logReader.RecheckQueues(wait: true);

        status = CurrentStatus(logDb);
      }
      _onCurStatusChange(status);
    }

    void BlackMaple.MachineFramework.IJobControl.RemoveMaterialFromAllQueues(IList<long> materialIds, string operatorName)
    {
      Log.Debug("Removing {@matId} from all queues", materialIds);

      CurrentStatus status;
      using (var logDb = logDbCfg.OpenConnection())
      {
        foreach (var materialId in materialIds)
        {
          var nextProc = logDb.NextProcessForQueuedMaterial(materialId);
          var proc = (nextProc ?? 1) - 1;
          logDb.RecordRemoveMaterialFromAllQueues(materialId, proc, operatorName);
        }
        logReader.RecheckQueues(wait: true);

        status = CurrentStatus(logDb);
      }
      _onCurStatusChange(status);
    }

    void BlackMaple.MachineFramework.IJobControl.SignalMaterialForQuarantine(long materialId, string queue, string operatorName)
    {
      Log.Debug("Signaling {matId} for quarantine", materialId);
      if (!fmsSettings.Queues.ContainsKey(queue))
      {
        throw new BlackMaple.MachineFramework.BadRequestException("Queue " + queue + " does not exist");
      }

      using (var logDb = logDbCfg.OpenConnection())
      {
        var status = CurrentStatus(logDb);

        var mat = status.Material.FirstOrDefault(m => m.MaterialID == materialId);
        if (mat == null)
        {
          throw new BlackMaple.MachineFramework.BadRequestException("Unable to find material to quarantine");
        }
        else if (mat.Location.Type != InProcessMaterialLocation.LocType.InQueue)
        {
          throw new BlackMaple.MachineFramework.BadRequestException("Mazak FMS Insight does not support quarantining material on a pallet");
        }
        else
        {
          ((BlackMaple.MachineFramework.IJobControl)this).SetMaterialInQueue(materialId, queue, -1, operatorName);
        }
      }
    }

    public void SwapMaterialOnPallet(string pallet, long oldMatId, long newMatId, string operatorName = null)
    {
      Log.Debug("Overriding {oldMat} to {newMat} on pallet {pal}", oldMatId, newMatId, pallet);

      using (var logDb = logDbCfg.OpenConnection())
      {
        var o = logDb.SwapMaterialInCurrentPalletCycle(
          pallet: pallet,
          oldMatId: oldMatId,
          newMatId: newMatId,
          operatorName: operatorName
        );

        _onEditMatInLog(new EditMaterialInLogEvents()
        {
          OldMaterialID = oldMatId,
          NewMaterialID = newMatId,
          EditedEvents = o.ChangedLogEntries,
        });
        _onCurStatusChange(CurrentStatus(logDb));
      }
    }

    public void InvalidatePalletCycle(long matId, int process, string oldMatPutInQueue = null, string operatorName = null)
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
