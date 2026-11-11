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
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      DateTime now
    ) => SyncFromDatabase(mazakData, db, mazakDb, fmsSt, mazakCfg, now, null);

    public static bool SyncFromDatabase(
      MazakAllData mazakData,
      IRepository db,
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      DateTime now,
      MazakResourceProtection protection
    )
    {
      var protectedResources = (protection ?? MazakResourceProtection.Empty)
        .Union(MazakEventInbox.ResourceProtection(db, mazakData))
        .Resolve(mazakData);
      var jobs = db.LoadJobsNotCopiedToSystem(
          now.AddHours(-JobLookbackHours),
          now.AddHours(1),
          includeDecremented: false
        )
        .Where(j => !j.Archived || !mazakCfg.UsesInsightJobQuantities(j))
        .Where(job => !protectedResources.BlocksDownload(job, mazakCfg))
        .GroupBy(j => j.ScheduleId)
        // Only do one schedule at a time
        .MinBy(g => g.Key, StringComparer.Ordinal);

      if (jobs == null)
      {
        return false;
      }

      //there are jobs to copy
      Log.Debug(
        "Sending jobs into mazak {@uniqs} with {@data}",
        jobs.Select(j => j.UniqueStr).ToList(),
        mazakData
      );

      // check if a previous download was interrupted during the middle of schedule downloads
      var alreadyDownloadedSchs = mazakData
        .Schedules.Where(s =>
          MazakPart.IsSailPart(s.PartName, s.Comment) && !string.IsNullOrEmpty(s.Comment)
        )
        .GroupBy(s => MazakPart.UniqueFromComment(s.Comment))
        .ToDictionary(g => g.Key, g => g.Count());

      bool existsPrev = false;
      var jobsToSchedule = new List<Job>();
      foreach (var j in jobs)
      {
        var expectedSchedules = ConvertJobsToMazakParts.IsSplitScheduleJob(j)
          ? j.Processes.Count
          : 1;
        alreadyDownloadedSchs.TryGetValue(j.UniqueStr, out var existingSchedules);

        if (existingSchedules >= expectedSchedules)
        {
          existsPrev = true;
          db.MarkJobCopiedToSystem(j.UniqueStr);
        }
        else
        {
          if (existingSchedules > 0)
            existsPrev = true;
          jobsToSchedule.Add(j);
        }
      }

      // if there are schedules that were already downloaded, resume the download
      if (existsPrev)
      {
        AddSchedules(
          mazakData,
          db,
          jobsToSchedule.ToImmutableList(),
          mazakDb,
          mazakCfg,
          protectedResources
        );
      }
      else
      {
        ArchiveOldJobs(db, mazakData, jobs, mazakCfg, protectedResources);

        AddFixturesPalletsParts(mazakData, db, jobs, mazakDb, fmsSt, mazakCfg, protectedResources);

        // Reload data after syncing fixtures, pallets, and parts
        mazakData = mazakDb.LoadAllData();

        AddSchedules(mazakData, db, jobs.ToImmutableList(), mazakDb, mazakCfg, protectedResources);
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
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      MazakProtectedResources protection
    )
    {
      //first allocate a UID to use for this download
      int UID = 0;
      while (UID < int.MaxValue)
      {
        if (
          protection.PartNames.Any(name => MazakPart.ParseUID(name) == UID)
          || protection.FixtureNames.Any(name => MazakPart.ParseUID(name) == UID)
        )
          goto found;
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

      var (transSet, savedParts) = BuildMazakSchedules.RemoveCompletedSchedules(
        mazakData,
        s =>
          !protection.ScheduleIds.Contains(s.Id)
          && !protection.PartNames.Contains(s.PartName)
          && (
            mazakCfg.UseInsightJobQuantities == null
            || !MazakPart.IsSailPart(s.PartName, s.Comment)
            || !mazakCfg.UsesInsightJobQuantities(
              jobDB.LoadJob(MazakPart.UniqueFromComment(s.Comment))
            )
          )
      );
      savedParts.UnionWith(protection.PartNames);
      if (mazakCfg.UseInsightJobQuantities != null)
      {
        // Retiring a schedule does not retire the job or its reusable route definitions.
        // Keep definitions for backend-owned work until that backend archives the job.
        savedParts.UnionWith(
          mazakData
            .Parts.Where(p => MazakPart.IsSailPart(p.PartName, p.Comment))
            .Where(p =>
              jobDB.LoadJob(MazakPart.UniqueFromComment(p.Comment)) is { Archived: false } job
              && mazakCfg.UsesInsightJobQuantities(job)
            )
            .Select(p => p.PartName)
        );
      }
      if (transSet.Schedules.Any())
        SaveProtected(mazakDb, transSet, protection);

      Log.Debug("Saved Parts: {parts}", savedParts);

      var jobErrs = new List<string>();
      var mazakJobs = ConvertJobsToMazakParts.JobsToMazak(
        jobs: jobs,
        downloadUID: UID,
        mazakData: mazakData,
        savedParts: savedParts,
        mazakCfg: mazakCfg,
        fmsSettings: fmsSt,
        lookupProgram: (prog, rev) => LookupProgram(jobDB, prog, rev),
        errors: jobErrs
      );
      if (jobErrs.Count != 0)
      {
        throw new BadRequestException(string.Join(Environment.NewLine, jobErrs));
      }
      mazakJobs.UsedFixtures.UnionWith(protection.FixtureNames);
      mazakJobs.UsedMainProgramComments.UnionWith(
        mazakData
          .MainPrograms.Where(p => protection.ProgramNames.Contains(p.MainProgram))
          .Select(p => p.Comment)
      );

      //delete parts
      transSet = mazakJobs.DeleteOldPartRows();
      if (transSet.Parts.Any())
      {
        SaveProtected(mazakDb, transSet, protection);
      }

      // delete pallets
      transSet = mazakJobs.DeleteOldPalletRows();
      SaveProtected(mazakDb, transSet, protection);

      //have to delete fixtures after schedule, parts, and pallets are already deleted
      transSet = mazakJobs.DeleteFixtureAndProgramDatabaseRows();
      SaveProtected(mazakDb, transSet, protection);

      transSet = mazakJobs.AddFixtureAndProgramDatabaseRows(
        jobDB.LoadProgramContent,
        mazakCfg.ProgramDirectory
      );
      // UsedFixtures also retains legacy dependencies. Retention is not permission to
      // recreate a missing reserved fixture during an unrelated download. A new fixture
      // group colliding with that name must still fail the protected-write check below.
      transSet = transSet with
      {
        Fixtures = transSet
          .Fixtures.Where(fixture =>
            !protection.FixtureNames.Contains(fixture.FixtureName)
            || mazakJobs.Fixtures.Any(group => group.MazakFixtureName == fixture.FixtureName)
          )
          .ToImmutableList(),
      };
      SaveProtected(mazakDb, transSet, protection);

      //now save the pallets and parts
      transSet = mazakJobs.CreatePartPalletDatabaseRows(mazakCfg);
      SaveProtected(mazakDb, transSet, protection);
    }

    private static void AddSchedules(
      MazakAllData mazakData,
      IRepository jobDB,
      IReadOnlyList<Job> jobs,
      IMazakDB writeDb,
      MazakConfig mazakCfg,
      MazakProtectedResources protection
    )
    {
      Log.Debug("Adding new schedules for {@jobs}, mazak data is {@mazakData}", jobs, mazakData);

      var transSet = BuildMazakSchedules.AddSchedules(
        mazakData,
        jobs,
        mazakCfg,
        protection.ScheduleIds
      );
      if (transSet.Schedules.Any())
      {
        SaveProtected(writeDb, transSet, protection);

        var existingScheduleCounts = mazakData
          .Schedules.Where(s =>
            MazakPart.IsSailPart(s.PartName, s.Comment) && !string.IsNullOrEmpty(s.Comment)
          )
          .GroupBy(s => MazakPart.UniqueFromComment(s.Comment))
          .ToDictionary(g => g.Key, g => g.Count());

        var addedScheduleCounts = transSet
          .Schedules.GroupBy(s => MazakPart.UniqueFromComment(s.Comment))
          .ToDictionary(g => g.Key, g => g.Count());

        foreach (var job in jobs)
        {
          var expectedSchedules = ConvertJobsToMazakParts.IsSplitScheduleJob(job)
            ? job.Processes.Count
            : 1;
          existingScheduleCounts.TryGetValue(job.UniqueStr, out var existingSchedules);
          addedScheduleCounts.TryGetValue(job.UniqueStr, out var addedSchedules);

          if (existingSchedules + addedSchedules >= expectedSchedules)
          {
            jobDB.MarkJobCopiedToSystem(job.UniqueStr);
          }
        }
      }
    }

    private static void ArchiveOldJobs(
      IRepository jobDB,
      MazakCurrentStatus schedules,
      IEnumerable<Job> toKeep,
      MazakConfig mazakCfg,
      MazakProtectedResources protection
    )
    {
      if (protection.SuspendCleanup)
        return;
      var current = new HashSet<string>(toKeep.Select(j => j.UniqueStr));
      current.UnionWith(protection.JobUniques);
      var completed = new Dictionary<string, int>();
      foreach (var sch in schedules.Schedules)
      {
        if (string.IsNullOrEmpty(sch.Comment))
          continue;
        if (!MazakPart.IsSailPart(sch.PartName, sch.Comment))
          continue;
        var unique = MazakPart.UniqueFromComment(sch.Comment);
        if (jobDB.LoadJob(unique) == null)
          continue;

        if (sch.PlanQuantity == sch.CompleteQuantity)
        {
          completed[unique] = sch.PlanQuantity;
        }
        else
        {
          current.Add(unique);
        }
      }

      var unarchived = jobDB.LoadUnarchivedJobs();

      // Accepted jobs remain committed while controller delivery is pending, including
      // jobs outside this pass's download window or selected schedule group.
      // Any decrement makes an uncopied job ineligible for delivery.
      current.UnionWith(
        unarchived
          .Where(j => !j.CopiedToSystem && (j.Decrements == null || j.Decrements.Count == 0))
          .Select(j => j.UniqueStr)
      );

      // A controller schedule's completion or disappearance says nothing about retained
      // basket/station/queue work owned by a custom backend. That backend archives explicitly.
      current.UnionWith(
        unarchived.Where(mazakCfg.UsesInsightJobQuantities).Select(j => j.UniqueStr)
      );

      var toArchive = unarchived
        .Where(j => !current.Contains(j.UniqueStr))
        .Select(j => j.UniqueStr);

      var newDecrs = unarchived
        .Where(j => toArchive.Contains(j.UniqueStr))
        .Select(j =>
        {
          var authorized = j.Cycles - (j.Decrements?.Sum(d => d.Quantity) ?? 0);
          if (completed.TryGetValue(j.UniqueStr, out var compCnt) && authorized > compCnt)
          {
            return new NewDecrementQuantity()
            {
              JobUnique = j.UniqueStr,
              Part = j.PartName,
              Quantity = authorized - compCnt,
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

    private static void SaveProtected(
      IMazakDB database,
      MazakWriteData write,
      MazakProtectedResources protection
    )
    {
      protection.ValidateWrite(write);
      database.Save(write);
    }
  }
}
