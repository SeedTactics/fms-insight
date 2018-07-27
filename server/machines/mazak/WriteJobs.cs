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
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace MazakMachineInterface
{
  public interface IWriteJobs
  {
    void AddJobs(NewJobs newJ, string expectedPreviousScheduleId);
    void RecopyJobsToMazak(DateTime? nowUtc = null);
  }

  public class WriteJobs : IWriteJobs
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<WriteJobs>();

    private IWriteData writeDb;
    private IReadDataAccess readDatabase;
    private IHoldManagement hold;
    private BlackMaple.MachineFramework.JobDB jobDB;
    private BlackMaple.MachineFramework.JobLogDB log;

    private bool UseStartingOffsetForDueDate;
    private bool DecrementPriorityOnDownload;
    private bool CheckPalletsUsedOnce;

    public const int JobLookbackHours = 2 * 24;

    public WriteJobs(
      IWriteData d,
      IReadDataAccess readDb,
      IHoldManagement h,
      BlackMaple.MachineFramework.JobDB jDB,
      BlackMaple.MachineFramework.JobLogDB jLog,
      bool check,
      bool useStarting,
      bool decrPriority
    ) {
      writeDb = d;
      readDatabase = readDb;
      hold = h;
      jobDB = jDB;
      log = jLog;
      CheckPalletsUsedOnce = check;
      UseStartingOffsetForDueDate = useStarting;
      DecrementPriorityOnDownload = decrPriority;
    }

    public void AddJobs(NewJobs newJ, string expectedPreviousScheduleId)
    {
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

        var logMessages = new List<string>();

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
          throw BuildTransactionException("Error downloading jobs", logMessages);
        }

        //Now that the parts have been added and we are confident that there no problems with the jobs,
        //add them to the database.  Once this occurrs, the timer will pick up and eventually
        //copy them to the system
        AddJobsToDB(newJ);

        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));

        AddSchedules(newJ.Jobs, logMessages);

        hold.SignalNewSchedules();
    }

    public void RecopyJobsToMazak(DateTime? nowUtc = null)
    {
      var now = nowUtc ?? DateTime.UtcNow;
      var jobs = jobDB.LoadJobsNotCopiedToSystem(now.AddHours(-JobLookbackHours), now.AddHours(1));
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
      if (transSet.Parts.Any() || transSet.Pallets.Any())
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
        if (logMessages.Count > 0) {
          Log.Error("Error saving schedules to mazak {@msgs}", logMessages);
          throw BuildTransactionException("Error creating schedules", logMessages);
        }

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

  }
}