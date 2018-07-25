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
    private IHoldManagement hold;
    private IMazakLogReader logReader;
    private BlackMaple.MachineFramework.JobDB jobDB;
    private BlackMaple.MachineFramework.JobLogDB log;
    private System.Timers.Timer _copySchedulesTimer;
    private readonly BlackMaple.MachineFramework.FMSSettings fmsSettings;

    public bool UseStartingOffsetForDueDate;
    public bool DecrementPriorityOnDownload;
    public bool CheckPalletsUsedOnce;

    public const int JobLookbackHours = 2 * 24;

    public event NewCurrentStatus OnNewCurrentStatus;
    public void RaiseNewCurrentStatus(CurrentStatus s) => OnNewCurrentStatus?.Invoke(s);

    public RoutingInfo(
      IWriteData d,
      IReadDataAccess readDb,
      IHoldManagement h,
      IMazakLogReader logR,
      BlackMaple.MachineFramework.JobDB jDB,
      BlackMaple.MachineFramework.JobLogDB jLog,
      bool check,
      bool useStarting,
      bool decrPriority,
      BlackMaple.MachineFramework.FMSSettings settings)
    {
      writeDb = d;
      readDatabase = readDb;
      fmsSettings = settings;
      hold = h;
      jobDB = jDB;
      logReader = logR;
      log = jLog;
      CheckPalletsUsedOnce = check;
      UseStartingOffsetForDueDate = useStarting;
      DecrementPriorityOnDownload = decrPriority;

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
        List<string> logMessages = new List<string>();

        // check previous schedule id
        if (!string.IsNullOrEmpty(newJ.ScheduleId))
        {
          var recentDbSchedule = jobDB.LoadMostRecentSchedule();
          if (!string.IsNullOrEmpty(expectedPreviousScheduleId) &&
              expectedPreviousScheduleId != recentDbSchedule.LatestScheduleId)
          {
            throw new BlackMaple.MachineFramework.BadRequestException(
              "Expected previous schedule ID does not match current schedule ID.  Another user may have already created a schedule.");
          }
        }

        //check for an old schedule that has not yet been copied
        var oldJobs = jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddHours(1));
        if (oldJobs.Jobs.Count > 0)
        {
          //there are jobs to copy
          Log.Information("Resuming copy of job schedules into mazak {uniqs}",
              oldJobs.Jobs.Select(j => j.UniqueStr).ToList());

          AddSchedules(oldJobs.Jobs, logMessages);
        }

        //add fixtures, pallets, parts.  If this fails, just throw an exception,
        //they will be deleted during the next download.
        var palPartMap = AddFixturesPalletsParts(newJ, logMessages, newJ.ScheduleId);
        if (logMessages.Count > 0)
        {
          throw BuildTransactionException("Error downloading routing info", logMessages);
        }

        //Now that the parts have been added and we are confident that there no problems with the jobs,
        //add them to the database.  Once this occurrs, the timer will pick up and eventually
        //copy them to the system
        AddJobsToDB(newJ);

        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));

        AddSchedules(newJ.Jobs, logMessages);

        hold.SignalNewSchedules();
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
          var jobs = jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddHours(-JobLookbackHours), DateTime.UtcNow.AddHours(1));
          if (jobs.Jobs.Count == 0) return;

          //there are jobs to copy
          Log.Information("Resuming copy of job schedules into mazak {uniqs}",
              jobs.Jobs.Select(j => j.UniqueStr).ToList());

          List<string> logMessages = new List<string>();

          AddSchedules(jobs.Jobs, logMessages);
          if (logMessages.Count > 0) {
            Log.Error("Error copying job schedules to mazak {msgs}", logMessages);
          }

          hold.SignalNewSchedules();
        }
        finally
        {
          OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
        }
      } catch (Exception ex) {
        Log.Error(ex, "Error recopying job schedules to mazak");
      }
    }

    private clsPalletPartMapping AddFixturesPalletsParts(
            NewJobs newJ,
            IList<string> logMessages,
        string newGlobal)
    {
      var transSet = new MazakWriteData();
      var mazakData = readDatabase.LoadAllData();

      int UID = 0;
      var savedParts = new HashSet<string>();

      //first allocate a UID to use for this download
      UID = 0;
      while (UID < int.MaxValue)
      {
        //check schedule rows for UID
        foreach (var schRow in mazakData.Schedules)
        {
          if (MazakPart.ParseUID(schRow.PartName) == UID)
            goto found;
        }

        //check fixture rows for UID
        foreach (var fixRow in mazakData.Fixtures)
        {
          if (MazakPart.ParseUID(fixRow.FixtureName) == UID)
            goto found;
        }

        break;
      found:
        UID += 1;
      }
      if (UID == int.MaxValue)
      {
        throw new Exception("Unable to find unused UID");
      }

      //remove all completed production
      foreach (var schRow in mazakData.Schedules)
      {
        var newSchRow = schRow.Clone();
        if (schRow.PlanQuantity == schRow.CompleteQuantity)
        {
          newSchRow.Command = MazakWriteCommand.Delete;
          transSet.Schedules.Add(newSchRow);

          MazakPart.ParseComment(schRow.Comment, out string unique, out var paths, out bool manual);
          if (unique != null && unique != "")
            jobDB.ArchiveJob(unique);

        }
        else
        {
          savedParts.Add(schRow.PartName);

          if (DecrementPriorityOnDownload)
          {
            newSchRow.Command = MazakWriteCommand.ScheduleSafeEdit;
            newSchRow.Priority = Math.Max(newSchRow.Priority - 1, 1);
            transSet.Schedules.Add(newSchRow);
          }
        }
      }

      Log.Debug("Creating new schedule with UID {uid}", UID);
      Log.Debug("Saved Parts: {parts}", savedParts);

      //build the pallet->part mapping
      var palletPartMap = new clsPalletPartMapping(newJ.Jobs, mazakData, UID, savedParts, logMessages,
                                                   !string.IsNullOrEmpty(newGlobal), newGlobal,
                                                   CheckPalletsUsedOnce, writeDb.MazakType);

      //delete everything
      palletPartMap.DeletePartPallets(transSet);
      writeDb.Save(transSet, "Delete Parts Pallets", logMessages);

      Log.Debug("Completed deletion of parts and pallets with messages: {msgs}", logMessages);

      //have to delete fixtures after schedule, parts, and pallets are already deleted
      //also, add new fixtures
      transSet = new MazakWriteData();
      palletPartMap.DeleteFixtures(transSet);
      palletPartMap.AddFixtures(transSet);
      writeDb.Save(transSet, "Fixtures", logMessages);

      Log.Debug("Deleted fixtures with messages: {msgs}", logMessages);

      //now save the pallets and parts
      transSet = new MazakWriteData();
      palletPartMap.CreateRows(transSet);
      writeDb.Save(transSet, "Add Parts", logMessages);

      Log.Debug("Added parts and pallets with messages: {msgs}", logMessages);

      if (logMessages.Count > 0)
      {
        Log.Error("Aborting schedule creation during download because" +
          " mazak returned an error while creating parts and pallets. {msgs}", logMessages);

        throw BuildTransactionException("Error creating parts and pallets", logMessages);
      }

      return palletPartMap;
    }

    private void AddSchedules(IEnumerable<JobPlan> jobs,
                              IList<string> logMessages)
    {
      var mazakData = readDatabase.LoadSchedulesPartsPallets();
      var transSet = new MazakWriteData();
      var now = DateTime.Now;

      var usedScheduleIDs = new HashSet<int>();
      var scheduledParts = new HashSet<string>();
      foreach (var schRow in mazakData.Schedules)
      {
        usedScheduleIDs.Add(schRow.Id);
        scheduledParts.Add(schRow.PartName);
      }

      //now add the new schedule
      int scheduleCount = 0;
      foreach (JobPlan part in jobs)
      {
        for (int proc1path = 1; proc1path <= part.GetNumPaths(1); proc1path++)
        {
          if (part.GetPlannedCyclesOnFirstProcess(proc1path) <= 0) continue;

          //check if part exists downloaded
          int downloadUid = -1;
          string mazakPartName = "";
          string mazakComment = "";
          foreach (var partRow in mazakData.Parts)
          {
            if (MazakPart.IsSailPart(partRow.PartName)) {
              MazakPart.ParseComment(partRow.Comment, out string u, out var ps, out bool m);
              if (u == part.UniqueStr && ps.PathForProc(proc: 1) == proc1path) {
                downloadUid = MazakPart.ParseUID(partRow.PartName);
                mazakPartName = partRow.PartName;
                mazakComment = partRow.Comment;
                break;
              }
            }
          }
          if (downloadUid < 0) {
            throw new BlackMaple.MachineFramework.BadRequestException(
              "Attempting to create schedule for " + part.UniqueStr + " but a part does not exist");
          }

          if (!scheduledParts.Contains(mazakPartName))
          {
            int schid = FindNextScheduleId(usedScheduleIDs);
            SchedulePart(transSet, schid, mazakPartName, mazakComment, part.NumProcesses, part, proc1path, now, scheduleCount);
            hold.SaveHoldMode(schid, part, proc1path);
            scheduleCount += 1;
          }
        }
      }

      if (transSet.Schedules.Any())
      {

        if (UseStartingOffsetForDueDate)
          SortSchedulesByDate(transSet);

        writeDb.Save(transSet, "Add Schedules", logMessages);

        Log.Debug("Completed adding schedules with messages: {msgs}", logMessages);

        foreach (var j in jobs)
        {
          jobDB.MarkJobCopiedToSystem(j.UniqueStr);
        }
      }
    }

    private void SchedulePart(MazakWriteData transSet, int SchID, string mazakPartName, string mazakComment, int numProcess,
                              JobPlan part, int proc1path, DateTime now, int scheduleCount)
    {
      var newSchRow = new MazakScheduleRow();
      newSchRow.Command = MazakWriteCommand.Add;
      newSchRow.Id = SchID;
      newSchRow.PartName = mazakPartName;
      newSchRow.PlanQuantity = part.GetPlannedCyclesOnFirstProcess(proc1path);
      newSchRow.CompleteQuantity = 0;
      newSchRow.FixForMachine = 0;
      newSchRow.MissingFixture = 0;
      newSchRow.MissingProgram = 0;
      newSchRow.MissingTool = 0;
      newSchRow.MixScheduleID = 0;
      newSchRow.ProcessingPriority = 0;
      newSchRow.Priority = 75;
      newSchRow.Comment = mazakComment;

      if (UseStartingOffsetForDueDate)
      {
        DateTime d;
        if (part.GetSimulatedStartingTimeUTC(1, proc1path) != DateTime.MinValue)
          d = part.GetSimulatedStartingTimeUTC(1, proc1path);
        else
          d = DateTime.Today;
        newSchRow.DueDate = d.AddSeconds(5 * scheduleCount);
      }
      else
      {
        newSchRow.DueDate = DateTime.Parse("1/1/2008 12:00:00 AM");
      }

      bool entireHold = false;
      if (part.HoldEntireJob != null) entireHold = part.HoldEntireJob.IsJobOnHold;
      bool machiningHold = false;
      if (part.HoldMachining(1, proc1path) != null) machiningHold = part.HoldMachining(1, proc1path).IsJobOnHold;
      newSchRow.HoldMode = (int)HoldPattern.CalculateHoldMode(entireHold, machiningHold);

      int matQty = newSchRow.PlanQuantity;

      if (!string.IsNullOrEmpty(part.GetInputQueue(process: 1, path: proc1path))) {
        matQty = 0;
      }

      //need to add all the ScheduleProcess rows
      for (int i = 1; i <= numProcess; i++)
      {
        var newSchProcRow = new MazakScheduleProcessRow();
        newSchProcRow.MazakScheduleRowId = SchID;
        newSchProcRow.ProcessNumber = i;
        if (i == 1)
        {
          newSchProcRow.ProcessMaterialQuantity = matQty;
        }
        else
        {
          newSchProcRow.ProcessMaterialQuantity = 0;
        }
        newSchProcRow.ProcessBadQuantity = 0;
        newSchProcRow.ProcessExecuteQuantity = 0;
        newSchProcRow.ProcessMachine = 0;

        newSchRow.Processes.Add(newSchProcRow);
      }

      transSet.Schedules.Add(newSchRow);
    }

    private static void SortSchedulesByDate(MazakWriteData transSet)
    {
      transSet.Schedules =
        transSet.Schedules
        .OrderBy(x => x.DueDate)
        .ToList();
    }

    private void AddJobsToDB(NewJobs newJ)
    {
      foreach (var j in newJ.Jobs)
      {
        j.Archived = true;
        j.JobCopiedToSystem = false;
        if (!jobDB.DoesJobExist(j.UniqueStr))
        {
          for (int proc = 1; proc <= j.NumProcesses; proc++)
          {
            for (int path = 1; path <= j.GetNumPaths(proc); path++)
            {
              foreach (var stop in j.GetMachiningStop(proc, path))
              {
                //The station group name on the job and the LocationName from the
                //generated log entries must match.  Rather than store and try and lookup
                //the station name when creating log entries, since we only support a single
                //machine group, just set the group name to MC here during storage and
                //always create log entries with MC.
                stop.StationGroup = "MC";
              }
            }
          }
        }
      }
      jobDB.AddJobs(newJ, null);
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

    #region "Helpers"
    internal static int CountMaterial(MazakScheduleRow schRow)
    {
      int cnt = schRow.CompleteQuantity;
      foreach (var schProcRow in schRow.Processes)
      {
        cnt += schProcRow.ProcessMaterialQuantity;
        cnt += schProcRow.ProcessExecuteQuantity;
        cnt += schProcRow.ProcessBadQuantity;
      }

      return cnt;
    }

    private static int FindNextScheduleId(HashSet<int> usedScheduleIds)
    {
      for (int i = 1; i <= 9999; i++)
      {
        if (!usedScheduleIds.Contains(i))
        {
          usedScheduleIds.Add(i);
          return i;
        }
      }
      throw new Exception("All Schedule Ids are currently being used");
    }


    public static Exception BuildTransactionException(string msg, IList<string> log)
    {
      string s = msg;
      foreach (string r in log)
      {
        s += Environment.NewLine + r;
      }
      return new Exception(s);
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
            proc: 1,
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
            proc: 1,
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
