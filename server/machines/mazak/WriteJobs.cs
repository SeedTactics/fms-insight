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
    private FMSSettings fmsSettings;

    private bool UseStartingOffsetForDueDate;
    private bool CheckPalletsUsedOnce;

    public const int JobLookbackHours = 2 * 24;

    public WriteJobs(
      IWriteData d,
      IReadDataAccess readDb,
      IHoldManagement h,
      BlackMaple.MachineFramework.JobDB jDB,
      BlackMaple.MachineFramework.JobLogDB jLog,
      FMSSettings settings,
      bool check,
      bool useStarting
    )
    {
      writeDb = d;
      readDatabase = readDb;
      hold = h;
      jobDB = jDB;
      log = jLog;
      CheckPalletsUsedOnce = check;
      UseStartingOffsetForDueDate = useStarting;
      fmsSettings = settings;
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

      //check for an old schedule that has not yet been copied
      var oldJobs = jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddHours(1));
      if (oldJobs.Jobs.Count > 0)
      {
        //there are jobs to copy
        Log.Information("Resuming copy of job schedules into mazak {uniqs}",
            oldJobs.Jobs.Select(j => j.UniqueStr).ToList());

        AddSchedules(oldJobs.Jobs);
      }

      //add fixtures, pallets, parts.  If this fails, just throw an exception,
      //they will be deleted during the next download.
      AddFixturesPalletsParts(newJ);

      //Now that the parts have been added and we are confident that there no problems with the jobs,
      //add them to the database.  Once this occurrs, the timer will pick up and eventually
      //copy them to the system
      AddJobsToDB(newJ);

      System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));

      AddSchedules(newJ.Jobs);

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

      AddSchedules(jobs.Jobs);

      hold.SignalNewSchedules();
    }


    private void AddFixturesPalletsParts(NewJobs newJ)
    {
      var mazakData = readDatabase.LoadAllData();

      //first allocate a UID to use for this download
      int UID = 0;
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
      Log.Debug("Creating new schedule with UID {uid}", UID);

      var (transSet, savedParts) = BuildMazakSchedules.RemoveCompletedAndDecrementSchedules(
        mazakData, UseStartingOffsetForDueDate
      );
      if (transSet.Schedules.Any())
        writeDb.Save(transSet, "Update schedules");

      Log.Debug("Saved Parts: {parts}", savedParts);

      var jobErrs = new List<string>();
      var mazakJobs = ConvertJobsToMazakParts.JobsToMazak(
        newJ.Jobs,
        UID,
        mazakData,
        savedParts,
        writeDb.MazakType,
        CheckPalletsUsedOnce,
        fmsSettings,
        jobErrs);
      if (jobErrs.Any())
      {
        throw new BlackMaple.MachineFramework.BadRequestException(
          string.Join(Environment.NewLine, jobErrs)
        );
      }

      //delete everything
      transSet = mazakJobs.DeleteOldPartPalletRows();
      if (transSet.Parts.Any() || transSet.Pallets.Any())
      {
        try
        {
          writeDb.Save(transSet, "Delete Parts Pallets");
        }
        catch (ErrorModifyingParts e)
        {
          foreach (var partName in e.PartNames)
          {
            if (readDatabase.CheckPartExists(partName))
            {
              throw new Exception("Mazak returned an error when attempting to delete part " + partName);
            }
          }
        }
      }

      //have to delete fixtures after schedule, parts, and pallets are already deleted
      //also, add new fixtures
      transSet = mazakJobs.CreateDeleteFixtureDatabaseRows();
      writeDb.Save(transSet, "Fixtures");

      //now save the pallets and parts
      transSet = mazakJobs.CreatePartPalletDatabaseRows();
      writeDb.Save(transSet, "Add Parts");
    }

    private void AddSchedules(IEnumerable<JobPlan> jobs)
    {
      var mazakData = readDatabase.LoadSchedulesPartsPallets();
      var transSet = BuildMazakSchedules.AddSchedules(mazakData, jobs, UseStartingOffsetForDueDate);
      if (transSet.Schedules.Any())
      {
        writeDb.Save(transSet, "Add Schedules");
        foreach (var j in jobs)
        {
          jobDB.MarkJobCopiedToSystem(j.UniqueStr);
        }
      }
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
  }
}