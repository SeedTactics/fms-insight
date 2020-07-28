/* Copyright (c) 2020, John Lenz

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
  public interface IMachineGroupName
  {
    string MachineGroupName { get; }
  }

  public interface IWriteJobs
  {
    void AddJobs(JobDB jobDB, NewJobs newJ, string expectedPreviousScheduleId);
    void RecopyJobsToMazak(JobDB jobDB, DateTime? nowUtc = null);
  }

  public class WriteJobs : IWriteJobs, IMachineGroupName
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<WriteJobs>();

    private IWriteData writeDb;
    private IReadDataAccess readDatabase;
    private IHoldManagement hold;
    private BlackMaple.MachineFramework.JobLogDB log;
    private FMSSettings fmsSettings;

    private bool UseStartingOffsetForDueDate;
    private bool CheckPalletsUsedOnce;
    private string ProgramDirectory;

    public const int JobLookbackHours = 2 * 24;

    private string _machineGroupName = null;
    public string MachineGroupName => _machineGroupName ?? "MC";

    public WriteJobs(
      IWriteData d,
      IReadDataAccess readDb,
      IHoldManagement h,
      BlackMaple.MachineFramework.JobDB jDB,
      BlackMaple.MachineFramework.JobLogDB jLog,
      FMSSettings settings,
      bool check,
      bool useStarting,
      string progDir
    )
    {
      writeDb = d;
      readDatabase = readDb;
      hold = h;
      log = jLog;
      CheckPalletsUsedOnce = check;
      UseStartingOffsetForDueDate = useStarting;
      fmsSettings = settings;
      ProgramDirectory = progDir;

      PlannedSchedule sch;
      sch = jDB.LoadMostRecentSchedule();
      if (sch.Jobs != null)
      {
        foreach (var j in sch.Jobs)
        {
          for (int proc = 1; proc <= j.NumProcesses; proc++)
          {
            for (int path = 1; path <= j.GetNumPaths(proc); path++)
            {
              foreach (var stop in j.GetMachiningStop(proc, path))
              {
                if (!string.IsNullOrEmpty(stop.StationGroup))
                {
                  _machineGroupName = stop.StationGroup;
                  goto foundGroup;
                }
              }
            }
          }
        }
      foundGroup:;
      }
    }

    public void AddJobs(JobDB jobDB, NewJobs newJ, string expectedPreviousScheduleId)
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
      var oldJobs = jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddHours(1), includeDecremented: false);
      if (oldJobs.Jobs.Count > 0)
      {
        //there are jobs to copy
        Log.Information("Resuming copy of job schedules into mazak {uniqs}",
            oldJobs.Jobs.Select(j => j.UniqueStr).ToList());

        AddSchedules(jobDB, oldJobs.Jobs);
      }

      // add programs here first so that they exist in the database when looking up most recent revision for use in parts
      jobDB.AddPrograms(newJ.Programs, DateTime.UtcNow);

      //add fixtures, pallets, parts.  If this fails, just throw an exception,
      //they will be deleted during the next download.
      AddFixturesPalletsParts(jobDB, newJ);

      //Now that the parts have been added and we are confident that there no problems with the jobs,
      //add them to the database.  Once this occurrs, the timer will pick up and eventually
      //copy them to the system
      AddJobsToDB(jobDB, newJ);

      System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));

      AddSchedules(jobDB, newJ.Jobs);

      hold.SignalNewSchedules();
    }

    public void RecopyJobsToMazak(JobDB jobDB, DateTime? nowUtc = null)
    {
      var now = nowUtc ?? DateTime.UtcNow;
      var jobs = jobDB.LoadJobsNotCopiedToSystem(now.AddHours(-JobLookbackHours), now.AddHours(1), includeDecremented: false);
      if (jobs.Jobs.Count == 0) return;

      //there are jobs to copy
      Log.Information("Resuming copy of job schedules into mazak {uniqs}",
          jobs.Jobs.Select(j => j.UniqueStr).ToList());

      List<string> logMessages = new List<string>();

      AddSchedules(jobDB, jobs.Jobs);

      hold.SignalNewSchedules();
    }

    private JobDB.ProgramRevision LookupProgram(JobDB jobDB, string program, long? rev)
    {
      if (rev.HasValue)
      {
        return jobDB.LoadProgram(program, rev.Value);
      }
      else
      {
        return jobDB.LoadMostRecentProgram(program);
      }
    }

    private void AddFixturesPalletsParts(JobDB jobDB, NewJobs newJ)
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

      ArchiveOldJobs(jobDB, mazakData);

      var (transSet, savedParts) = BuildMazakSchedules.RemoveCompletedSchedules(mazakData);
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
        (prog, rev) => LookupProgram(jobDB, prog, rev),
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
      transSet = mazakJobs.CreateDeleteFixtureAndProgramDatabaseRows(jobDB.LoadProgramContent, ProgramDirectory);
      writeDb.Save(transSet, "Fixtures");

      //now save the pallets and parts
      transSet = mazakJobs.CreatePartPalletDatabaseRows();
      writeDb.Save(transSet, "Add Parts");
    }

    private void AddSchedules(JobDB jobDB, IEnumerable<JobPlan> jobs)
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

    private void AddJobsToDB(JobDB jobDB, NewJobs newJ)
    {
      foreach (var j in newJ.Jobs)
      {
        j.JobCopiedToSystem = false;
      }
      jobDB.AddJobs(newJ, null);

      //update the station group name
      foreach (var j in newJ.Jobs)
      {
        for (int proc = 1; proc <= j.NumProcesses; proc++)
        {
          for (int path = 1; path <= j.GetNumPaths(proc); path++)
          {
            foreach (var stop in j.GetMachiningStop(proc, path))
            {
              if (!string.IsNullOrEmpty(stop.StationGroup))
              {
                _machineGroupName = stop.StationGroup;
                goto foundGroup;
              }
            }
          }
        }
      }
    foundGroup:;

    }

    private void ArchiveOldJobs(JobDB jobDB, MazakSchedules schedules)
    {
      var current = new HashSet<string>();
      var completed = new Dictionary<(string uniq, int proc1path), int>();
      foreach (var sch in schedules.Schedules)
      {
        if (!MazakPart.IsSailPart(sch.PartName)) continue;
        if (string.IsNullOrEmpty(sch.Comment)) continue;
        MazakPart.ParseComment(sch.Comment, out string unique, out var procToPath, out bool manual);

        if (sch.PlanQuantity == sch.CompleteQuantity)
        {
          completed[(uniq: unique, proc1path: procToPath.PathForProc(1))] = sch.PlanQuantity;
        }
        else
        {
          current.Add(unique);
        }
      }

      var unarchived = jobDB.LoadUnarchivedJobs();

      var toArchive = unarchived.Jobs.Where(j => !current.Contains(j.UniqueStr)).Select(j => j.UniqueStr);

      var newDecrs = unarchived.Jobs.Select(j =>
      {
        int toDecr = 0;
        for (int path = 1; path <= j.GetNumPaths(process: 1); path += 1)
        {
          if (completed.TryGetValue((uniq: j.UniqueStr, proc1path: path), out var compCnt))
          {
            if (compCnt < j.GetPlannedCyclesOnFirstProcess(path))
            {
              toDecr += j.GetPlannedCyclesOnFirstProcess(path) - compCnt;
            }
          }
        }
        if (toDecr > 0)
        {
          return new JobDB.NewDecrementQuantity()
          {
            JobUnique = j.UniqueStr,
            Part = j.PartName,
            Quantity = toDecr
          };
        }
        else
        {
          return null;
        }
      }).Where(n => n != null);

      if (toArchive.Any())
      {
        jobDB.ArchiveJobs(toArchive, newDecrs);
      }
    }
  }
}