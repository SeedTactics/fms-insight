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
using System.Collections.Generic;
using System.Linq;
using System.Data;
using System.Diagnostics;
using BlackMaple.MachineWatchInterface;

namespace MazakMachineInterface
{
  public class RoutingInfo : IJobControl, IOldJobDecrement
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<RoutingInfo>();

    private IWriteData writeDb;
    private IReadDataAccess readDatabase;
    private IMazakLogReader logReader;
    private BlackMaple.MachineFramework.JobDB jobDB;
    private BlackMaple.MachineFramework.JobLogDB log;
    private IWriteJobs _writeJobs;
    private System.Timers.Timer _copySchedulesTimer;
    private readonly BlackMaple.MachineFramework.FMSSettings fmsSettings;

    public bool CheckPalletsUsedOnce;

    public event NewCurrentStatus OnNewCurrentStatus;
    public void RaiseNewCurrentStatus(CurrentStatus s) => OnNewCurrentStatus?.Invoke(s);

    public RoutingInfo(
      IWriteData d,
      IReadDataAccess readDb,
      IMazakLogReader logR,
      BlackMaple.MachineFramework.JobDB jDB,
      BlackMaple.MachineFramework.JobLogDB jLog,
      IWriteJobs wJobs,
      bool check,
      BlackMaple.MachineFramework.FMSSettings settings)
    {
      writeDb = d;
      readDatabase = readDb;
      fmsSettings = settings;
      jobDB = jDB;
      logReader = logR;
      log = jLog;
      _writeJobs = wJobs;
      CheckPalletsUsedOnce = check;

      _copySchedulesTimer = new System.Timers.Timer(TimeSpan.FromMinutes(4.5).TotalMilliseconds);
      _copySchedulesTimer.Elapsed += (sender, args) => RecopyJobsToSystem();
      _copySchedulesTimer.Start();
    }

    public void Halt()
    {
      _copySchedulesTimer.Stop();
    }

    #region Reading
    public CurrentStatus GetCurrentStatus()
    {
      MazakSchedulesPartsPallets mazakData;
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

      return BuildCurrentStatus.Build(jobDB, log, fmsSettings, readDatabase.MazakType, mazakData);
    }

    #endregion

    #region "Write Routing Info"

    public List<string> CheckValidRoutes(IEnumerable<JobPlan> jobs)
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
        Log.Debug("Check valid routing info");

        // queue support is still being developed and tested
        foreach (var j in jobs) {
          for (int proc = 1; proc <= j.NumProcesses; proc++) {
            for (int path = 1; path <= j.GetNumPaths(proc); path++) {

              var inQueue = j.GetInputQueue(proc, path);
              if (!string.IsNullOrEmpty(inQueue) && !fmsSettings.Queues.ContainsKey(inQueue)) {
                logMessages.Add(
                  " Job " + j.UniqueStr + " has an input queue " + inQueue + " which is not configured as a local queue in FMS Insight." +
                  " All input queues must be local queues, not an external queue.");
              }

              var outQueue = j.GetOutputQueue(proc, path);
              if (proc == j.NumProcesses) {
                if (!string.IsNullOrEmpty(outQueue) && !fmsSettings.ExternalQueues.ContainsKey(outQueue)) {
                  logMessages.Add("Output queues on the final process must be external queues." +
                    " Job " + j.UniqueStr + " has a queue " + outQueue + " on the final process which is not configured " +
                    " as an external queue");
                }
              } else {
                if (!string.IsNullOrEmpty(outQueue) && !fmsSettings.Queues.ContainsKey(outQueue)) {
                  logMessages.Add(
                    " Job " + j.UniqueStr + " has an output queue " + outQueue + " which is not configured as a queue in FMS Insight." +
                    " Non-final processes must have a configured local queue, not an external queue");
                }
              }
            }
          }
        }

        //The reason we create the clsPalletPartMapping is to see if it throws any exceptions.  We therefore
        //need to ignore the warning that palletPartMap is not used.
#pragma warning disable 168, 219
        var palletPartMap = new clsPalletPartMapping(jobs, mazakData, 1,
                                                     new HashSet<string>(), logMessages, false, "",
                                                     CheckPalletsUsedOnce, writeDb.MazakType);
#pragma warning restore 168, 219

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

    public void AddJobs(NewJobs newJ, string expectedPreviousScheduleId)
    {
      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      try
      {
        _writeJobs.AddJobs(newJ, expectedPreviousScheduleId);
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }
    }

    public void RecopyJobsToSystem()
    {
      try {
        if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
        {
          throw new Exception("Unable to obtain mazak database lock");
        }
        try
        {
          _writeJobs.RecopyJobsToMazak();
        }
        finally
        {
          OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
        }
      } catch (Exception ex) {
        Log.Error(ex, "Error recopying job schedules to mazak");
      }
    }
    #endregion

    #region "Decrement Plan Quantity"
    public List<JobAndDecrementQuantity> DecrementJobQuantites(string loadDecrementsStrictlyAfterDecrementId)
    {
      return new List<JobAndDecrementQuantity>();
    }
    public List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
    {
      return new List<JobAndDecrementQuantity>();
    }

    public Dictionary<JobAndPath, int> OldDecrementJobQuantites()
    {
      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }

      try
      {
        return modDecrementPlanQty.DecrementPlanQty(writeDb, readDatabase);
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }
    }

    public void OldFinalizeDecrement()
    {
      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }

      try
      {
        modDecrementPlanQty.FinalizeDecement(writeDb, readDatabase);
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }
    }
    #endregion

    #region Queues
    public void AddUnallocatedCastingToQueue(string part, string queue, int position, string serial)
    {
      // num proc will be set later once it is allocated inside the MazakQueues thread
      var matId = log.AllocateMaterialIDForCasting(part, 1);
      if (!string.IsNullOrEmpty(serial)) {
        log.RecordSerialForMaterialID(
          new LogMaterial(
            matID: matId,
            uniq: "",
            proc: 0,
            part: part,
            numProc: 1),
          serial);
      }
      // the add to queue log entry will use the process, so later when we lookup the latest completed process
      // for the material in the queue, it will be correctly computed.
      log.RecordAddMaterialToQueue(matId, 0, queue, position);
      logReader.RecheckQueues();
      RaiseNewCurrentStatus(GetCurrentStatus());
    }

    public void AddUnprocessedMaterialToQueue(string jobUnique, int process, string queue, int position, string serial)
    {
      var job = jobDB.LoadJob(jobUnique);
      if (job == null) throw new BlackMaple.MachineFramework.BadRequestException("Unable to find job " + jobUnique);
      var matId = log.AllocateMaterialID(jobUnique, job.PartName, job.NumProcesses);
      if (!string.IsNullOrEmpty(serial)) {
        log.RecordSerialForMaterialID(
          new LogMaterial(
            matID: matId,
            uniq: jobUnique,
            proc: process,
            part: job.PartName,
            numProc: job.NumProcesses),
          serial);
      }
      // the add to queue log entry will use the process, so later when we lookup the latest completed process
      // for the material in the queue, it will be correctly computed.
      log.RecordAddMaterialToQueue(matId, process, queue, position);
      logReader.RecheckQueues();
      RaiseNewCurrentStatus(GetCurrentStatus());
    }

    public void SetMaterialInQueue(long materialId, string queue, int position)
    {
      var proc =
        log.GetLogForMaterial(materialId)
        .SelectMany(e => e.Material)
        .Where(m => m.MaterialID == materialId)
        .Select(m => m.Process)
        .DefaultIfEmpty(0)
        .Max();
      log.RecordAddMaterialToQueue(materialId, proc, queue, position);
      logReader.RecheckQueues();
      RaiseNewCurrentStatus(GetCurrentStatus());
    }

    public void RemoveMaterialFromAllQueues(long materialId)
    {
      log.RecordRemoveMaterialFromAllQueues(materialId);
      logReader.RecheckQueues();
      RaiseNewCurrentStatus(GetCurrentStatus());
    }
    #endregion
  }
}
