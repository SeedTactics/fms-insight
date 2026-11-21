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

    internal static bool HasManagedDownloadDemand(HistoricJob job) =>
      !job.Archived && job.Cycles > (job.Decrements?.Sum(d => (long)d.Quantity) ?? 0);

    public static bool SyncFromDatabase(
      MazakAllData mazakData,
      IRepository db,
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      DateTime now
    ) =>
      SyncFromDatabase(mazakData, db, mazakDb, fmsSt, mazakCfg, now, additionalManagedJobs: null);

    internal static bool SyncFromDatabase(
      MazakAllData mazakData,
      IRepository db,
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      DateTime now,
      ImmutableHashSet<string> additionalManagedJobs
    )
    {
      var jobs = FindPendingDownload(mazakData, db, mazakCfg, now, additionalManagedJobs);
      if (jobs.IsEmpty)
        return false;

      Log.Debug(
        "Sending jobs into mazak {@uniqs} with {@data}",
        jobs.Select(j => j.UniqueStr).ToList(),
        mazakData
      );

      var managedParts = MazakLiveRoutes.ManagedPartNames(db, mazakCfg, mazakData);
      var pending = jobs.Where(job => !HasAllInitialRoutes(mazakData, mazakCfg, job, managedParts))
        .ToImmutableList();
      foreach (var job in jobs.Except(pending))
        db.MarkJobCopiedToSystem(job.UniqueStr);

      if (!pending.IsEmpty)
      {
        ArchiveOldJobs(db, mazakData, jobs, mazakCfg, managedParts);
        var partNames = AddFixturesPalletsParts(
          mazakData,
          db,
          pending,
          mazakDb,
          fmsSt,
          mazakCfg,
          managedParts
        );
        // A failed controller write may have succeeded in part. Retry using fresh native readback.
        mazakData = mazakDb.LoadAllData();
        AddSchedules(mazakData, db, pending, partNames, mazakDb, mazakCfg);
      }

      return true;
    }

    /// <summary>Preview the next eligible batch using current repository and controller state.</summary>
    /// <remarks>Additional managed job IDs extend the download window. ApplyJobActions selects again when it runs.</remarks>
    public static ImmutableList<HistoricJob> FindPendingDownload(
      MazakAllData data,
      IRepository db,
      MazakConfig config,
      DateTime now,
      ImmutableHashSet<string> additionalManagedJobs = null
    )
    {
      var jobs = db.LoadJobsNotCopiedToSystem(
          now.AddHours(-JobLookbackHours),
          now.AddHours(1),
          includeDecremented: config.UseInsightJobQuantities is not null
        )
        .AsEnumerable();

      if (config.UseInsightJobQuantities is not null)
      {
        var managedOutsideWindow = data
          .Parts.Where(part => MazakPart.IsSailPart(part.PartName, part.Comment))
          .Select(part => MazakPart.UniqueFromComment(part.Comment))
          .Concat(additionalManagedJobs ?? [])
          .Distinct(StringComparer.Ordinal)
          .Select(db.LoadJob)
          .Where(job => MazakLiveRoutes.Owns(config, job) && !job.CopiedToSystem && !job.Archived);
        jobs = jobs.Concat(managedOutsideWindow).DistinctBy(job => job.UniqueStr);
      }

      return jobs.Where(job => config.BulkJobFilter?.Invoke(job) != false)
        .Where(job =>
          config.UsesInsightJobQuantities(job)
            ? HasManagedDownloadDemand(job)
              && MazakPart
                .InitialScheduleComments(job)
                .All(comment =>
                  MazakLiveRoutes.ReadbackProblem(db, config, data, job, comment) is null
                )
            : job.Decrements is not { Count: > 0 }
        )
        .GroupBy(job => job.ScheduleId)
        // Only do one schedule at a time.
        .MinBy(group => group.Key, StringComparer.Ordinal)
        ?.ToImmutableList()
        ?? [];
    }

    internal static ProgramRevision LookupProgram(IRepository jobDB, string program, long? rev)
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

    private static ImmutableHashSet<string> AddFixturesPalletsParts(
      MazakAllData mazakData,
      IRepository jobDB,
      IEnumerable<Job> jobs,
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      ImmutableHashSet<string> managedParts
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

        if (mazakData.Parts.Any(part => MazakPart.ParseUID(part.PartName) == UID))
          goto found;

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

      bool InDownloadScope(string part, string comment) =>
        mazakCfg.BulkJobFilter is not { } filter
        || (
          MazakPart.IsSailPart(part, comment)
          && jobDB.LoadJob(MazakPart.UniqueFromComment(comment)) is { } job
          && filter(job)
        );

      var (transSet, savedParts) = BuildMazakSchedules.RemoveCompletedSchedules(mazakData, s =>
        !jobs.Any(job => MazakPart.UniqueFromComment(s.Comment) == job.UniqueStr)
        && InDownloadScope(s.PartName, s.Comment)
        && !managedParts.Contains(s.PartName)
      );
      if (mazakCfg.BulkJobFilter is not null)
        savedParts.UnionWith(
          mazakData
            .Parts.Where(part => !InDownloadScope(part.PartName, part.Comment))
            .Select(part => part.PartName)
        );
      if (mazakCfg.UseInsightJobQuantities is not null)
        savedParts.UnionWith(
          mazakData
            .Parts.Where(part => managedParts.Contains(part.PartName))
            .Where(part =>
              jobDB.LoadJob(MazakPart.UniqueFromComment(part.Comment)) is not { Archived: true } job
              || !MazakLiveRoutes.Owns(mazakCfg, job)
            )
            .Select(part => part.PartName)
        );
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
        errors: jobErrs,
        reuseExistingParts: true,
        managedParts: managedParts
      );
      if (jobErrs.Count != 0)
      {
        throw new BadRequestException(string.Join(Environment.NewLine, jobErrs));
      }

      var selectedParts = mazakJobs
        .ReusedParts.Select(part => part.PartName)
        .Concat(mazakJobs.AllParts.Select(part => part.PartName))
        .ToImmutableHashSet();
      if (!mazakJobs.AllParts.Any())
      {
        // Complete pallet transactions even when every part transaction already succeeded.
        var mappings = mazakJobs.CreatePartPalletDatabaseRows(mazakCfg);
        if (mappings.Pallets.Any())
          mazakDb.Save(mappings);
        return selectedParts;
      }

      if (transSet.Schedules.Any())
        mazakDb.Save(transSet);

      //delete parts
      transSet = mazakJobs.DeleteOldPartRows();
      if (transSet.Parts.Any())
      {
        mazakDb.Save(transSet);
      }

      // delete pallets
      transSet = mazakJobs.DeleteOldPalletRows();
      mazakDb.Save(transSet);

      //have to delete fixtures after schedule, parts, and pallets are already deleted
      transSet = mazakJobs.DeleteFixtureAndProgramDatabaseRows();
      mazakDb.Save(transSet);

      transSet = mazakJobs.AddFixtureAndProgramDatabaseRows(
        jobDB.LoadProgramContent,
        mazakCfg.ProgramDirectory
      );
      mazakDb.Save(transSet);

      //now save the pallets and parts
      transSet = mazakJobs.CreatePartPalletDatabaseRows(mazakCfg);
      mazakDb.Save(transSet);
      return selectedParts;
    }

    private static void AddSchedules(
      MazakAllData mazakData,
      IRepository jobDB,
      IReadOnlyList<Job> jobs,
      ImmutableHashSet<string> partNames,
      IMazakDB writeDb,
      MazakConfig mazakCfg
    )
    {
      Log.Debug("Adding new schedules for {@jobs}, mazak data is {@data}", jobs, mazakData);
      var managedParts = MazakLiveRoutes.ManagedPartNames(jobDB, mazakCfg, mazakData);
      // A managed part may have changed during installation. Check the actual execution
      // definition, including its pallet mappings, before installing a schedule against it.
      var eligibleJobs = jobs.Where(job =>
          !mazakCfg.UsesInsightJobQuantities(job)
          || MazakPart
            .InitialScheduleComments(job)
            .All(comment =>
              MazakLiveRoutes.ReadbackProblem(
                jobDB,
                mazakCfg,
                mazakData,
                job,
                comment,
                requireInstalledPart: true
              )
                is null
            )
        )
        .ToImmutableList();
      var managedComments = eligibleJobs
        .Where(mazakCfg.UsesInsightJobQuantities)
        .SelectMany(MazakPart.InitialScheduleComments)
        .ToImmutableHashSet(StringComparer.Ordinal);
      var transSet = BuildMazakSchedules.AddSchedules(
        mazakData with
        {
          Parts = mazakData
            .Parts.Where(part => partNames.Contains(part.PartName))
            .ToImmutableList(),
        },
        eligibleJobs,
        mazakCfg,
        (part, comment) => !managedParts.Contains(part) || managedComments.Contains(comment)
      );
      if (transSet.Schedules.Count == 0)
        return;

      writeDb.Save(transSet);
      var expected = mazakData with { Schedules = mazakData.Schedules.Concat(transSet.Schedules) };
      var confirmed = jobs.Any(mazakCfg.UsesInsightJobQuantities)
        ? writeDb.LoadAllData()
        : expected;
      foreach (var job in eligibleJobs)
        if (
          HasAllInitialRoutes(
            mazakCfg.UsesInsightJobQuantities(job) ? confirmed : expected,
            mazakCfg,
            job,
            managedParts
          )
        )
          jobDB.MarkJobCopiedToSystem(job.UniqueStr);
    }

    private static bool HasAllInitialRoutes(
      MazakAllData data,
      MazakConfig config,
      Job job,
      ImmutableHashSet<string> managedParts
    )
    {
      if (config.UsesInsightJobQuantities(job))
        return MazakPart
          .InitialScheduleComments(job)
          .All(comment =>
          {
            try
            {
              return MazakLiveRoutes.Inspect(data, job, comment).Schedule is not null;
            }
            catch (ConflictRequestException)
            {
              return false;
            }
          });

      var comments = data
        .Schedules.Where(s =>
          !managedParts.Contains(s.PartName) && MazakPart.IsSailPart(s.PartName, s.Comment)
        )
        .Select(s => s.Comment)
        .ToImmutableHashSet(StringComparer.Ordinal);
      return MazakPart.InitialScheduleComments(job).All(comments.Contains);
    }

    private static void ArchiveOldJobs(
      IRepository jobDB,
      MazakCurrentStatus schedules,
      IEnumerable<Job> toKeep,
      MazakConfig mazakCfg,
      ImmutableHashSet<string> managedParts
    )
    {
      var current = new HashSet<string>(toKeep.Select(j => j.UniqueStr));
      var completed = new Dictionary<string, int>();
      var conflictingOrdinaryJobs = schedules
        .Parts?.Where(p =>
          managedParts.Contains(p.PartName) && MazakPart.IsSailPart(p.PartName, p.Comment)
        )
        .Select(p => jobDB.LoadJob(MazakPart.UniqueFromComment(p.Comment)))
        .Where(job => job is not null && !mazakCfg.UsesInsightJobQuantities(job))
        .Select(job => job.UniqueStr);
      if (conflictingOrdinaryJobs is not null)
        current.UnionWith(conflictingOrdinaryJobs);
      foreach (var sch in schedules.Schedules)
      {
        if (managedParts.Contains(sch.PartName) || string.IsNullOrEmpty(sch.Comment))
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

      var unarchived = jobDB
        .LoadUnarchivedJobs()
        .Where(job => mazakCfg.BulkJobFilter?.Invoke(job) != false)
        .ToImmutableList();

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
  }
}
