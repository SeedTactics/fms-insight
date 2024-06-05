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
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public static class WriteJobs
  {
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<NewJobs>();

    public const int JobLookbackHours = 2 * 24;

    public static bool SyncFromDatabase(
      MazakAllData mazakData,
      IRepository db,
      IWriteData writeDb,
      IReadDataAccess readDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      DateTime now
    )
    {
      var jobs = db.LoadJobsNotCopiedToSystem(
          now.AddHours(-JobLookbackHours),
          now.AddHours(1),
          includeDecremented: false
        )
        .GroupBy(j => j.ScheduleId)
        // Only do one schedule at a time
        .MinBy(g => g.Key, StringComparer.Ordinal);

      if (jobs == null)
      {
        return false;
      }

      //there are jobs to copy
      Log.Debug("Sending jobs into mazak {uniqs}", jobs.Select(j => j.UniqueStr).ToList());

      // check if a previous download was interrupted during the middle of schedule downloads
      var alreadyDownloadedSchs = mazakData
        .Schedules.Select(s =>
        {
          if (MazakPart.IsSailPart(s.PartName, s.Comment))
          {
            MazakPart.ParseComment(s.Comment, out var uniq, out var _, out var _);
            return uniq;
          }
          else
          {
            return null;
          }
        })
        .Where(u => u != null)
        .ToHashSet();

      bool existsPrev = false;
      foreach (var j in jobs)
      {
        if (alreadyDownloadedSchs.Contains(j.UniqueStr))
        {
          existsPrev = true;
          db.MarkJobCopiedToSystem(j.UniqueStr);
        }
      }

      // if there are schedules that were already downloaded, resume the download
      if (existsPrev)
      {
        AddSchedules(
          mazakData,
          db,
          jobs.Where(j => !alreadyDownloadedSchs.Contains(j.UniqueStr)).ToImmutableList(),
          writeDb,
          mazakCfg
        );
      }
      else
      {
        ArchiveOldJobs(db, mazakData, jobs);

        AddFixturesPalletsParts(mazakData, db, jobs, writeDb, fmsSt, mazakCfg, readDb);

        // Reload data after syncing fixtures, pallets, and parts
        mazakData = readDb.LoadAllData();

        AddSchedules(mazakData, db, jobs.ToImmutableList(), writeDb, mazakCfg);
      }

      return true;
    }

    private static ProgramRevision LookupProgram(IRepository jobDB, string program, long? rev)
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

    private static void AddFixturesPalletsParts(
      MazakAllData mazakData,
      IRepository jobDB,
      IEnumerable<Job> jobs,
      IWriteData writeDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      IReadDataAccess readDb
    )
    {
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

      var (transSet, savedParts) = BuildMazakSchedules.RemoveCompletedSchedules(mazakData);
      if (transSet.Schedules.Any())
        writeDb.Save(transSet, "Update schedules");

      Log.Debug("Saved Parts: {parts}", savedParts);

      var jobErrs = new List<string>();
      var mazakJobs = ConvertJobsToMazakParts.JobsToMazak(
        jobs: jobs,
        downloadUID: UID,
        mazakData: mazakData,
        savedParts: savedParts,
        MazakType: writeDb.MazakType,
        useStartingOffsetForDueDate: mazakCfg.UseStartingOffsetForDueDate,
        fmsSettings: fmsSt,
        lookupProgram: (prog, rev) => LookupProgram(jobDB, prog, rev),
        errors: jobErrs
      );
      if (jobErrs.Count != 0)
      {
        throw new BadRequestException(string.Join(Environment.NewLine, jobErrs));
      }

      //delete parts
      transSet = mazakJobs.DeleteOldPartRows();
      if (transSet.Parts.Any())
      {
        try
        {
          writeDb.Save(transSet, "Delete Parts");
        }
        catch (ErrorModifyingParts e)
        {
          foreach (var partName in e.PartNames)
          {
            if (readDb.CheckPartExists(partName))
            {
              throw new Exception("Mazak returned an error when attempting to delete part " + partName);
            }
          }
        }
      }

      // delete pallets
      transSet = mazakJobs.DeleteOldPalletRows();
      writeDb.Save(transSet, "Delete Pallets");

      //have to delete fixtures after schedule, parts, and pallets are already deleted
      transSet = mazakJobs.DeleteFixtureAndProgramDatabaseRows();
      writeDb.Save(transSet, "Delete Fixtures");

      transSet = mazakJobs.AddFixtureAndProgramDatabaseRows(
        jobDB.LoadProgramContent,
        mazakCfg.ProgramDirectory
      );
      writeDb.Save(transSet, "Add Fixtures");

      //now save the pallets and parts
      transSet = mazakJobs.CreatePartPalletDatabaseRows();
      writeDb.Save(transSet, "Add Parts");
    }

    private static void AddSchedules(
      MazakAllData mazakData,
      IRepository jobDB,
      IReadOnlyList<Job> jobs,
      IWriteData writeDb,
      MazakConfig mazakCfg
    )
    {
      Log.Debug("Adding new schedules for {@jobs}, mazak data is {@mazakData}", jobs, mazakData);

      var transSet = BuildMazakSchedules.AddSchedules(mazakData, jobs, mazakCfg.UseStartingOffsetForDueDate);
      if (transSet.Schedules.Any())
      {
        writeDb.Save(transSet, "Add Schedules");
        foreach (var j in jobs)
        {
          jobDB.MarkJobCopiedToSystem(j.UniqueStr);
        }
      }
    }

    private static void ArchiveOldJobs(
      IRepository jobDB,
      MazakCurrentStatus schedules,
      IEnumerable<Job> toKeep
    )
    {
      var current = new HashSet<string>(toKeep.Select(j => j.UniqueStr));
      var completed = new Dictionary<(string uniq, int proc1path), int>();
      foreach (var sch in schedules.Schedules)
      {
        if (string.IsNullOrEmpty(sch.Comment))
          continue;
        if (!MazakPart.IsSailPart(sch.PartName, sch.Comment))
          continue;
        MazakPart.ParseComment(sch.Comment, out string unique, out var procToPath, out bool manual);
        if (jobDB.LoadJob(unique) == null)
          continue;

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

      var toArchive = unarchived.Where(j => !current.Contains(j.UniqueStr)).Select(j => j.UniqueStr);

      var newDecrs = unarchived
        .Where(j => toArchive.Contains(j.UniqueStr))
        .Select(j =>
        {
          int qty = 0;
          for (int path = 1; path <= j.Processes[0].Paths.Count; path++)
          {
            if (completed.TryGetValue((uniq: j.UniqueStr, proc1path: path), out var compCnt))
            {
              qty += compCnt;
            }
          }

          if (j.Cycles > qty)
          {
            return new NewDecrementQuantity()
            {
              JobUnique = j.UniqueStr,
              Part = j.PartName,
              Quantity = j.Cycles - qty
            };
          }
          else
          {
            return null;
          }
        })
        .Where(n => n != null);

      if (toArchive.Any())
      {
        jobDB.ArchiveJobs(toArchive, newDecrs);
      }
    }
  }
}
