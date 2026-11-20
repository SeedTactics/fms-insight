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

    internal static (DateTime EarliestEnd, DateTime LatestStart) DownloadWindow(DateTime now) =>
      (now.AddHours(-JobLookbackHours), now.AddHours(1));

    // A backend can cancel unstarted demand while admitted pieces still need their routes.
    internal static bool HasDownloadDemand(HistoricJob job, MazakConfig config) =>
      config.UsesInsightJobQuantities(job)
        ? job.Cycles > (job.Decrements?.Sum(d => (long)d.Quantity) ?? 0)
        : job.Decrements is not { Count: > 0 };

    public static bool SyncFromDatabase(
      MazakAllData mazakData,
      IRepository db,
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      DateTime now
    ) => SyncFromDatabase(mazakData, db, mazakDb, fmsSt, mazakCfg, now, attemptCleanup: true);

    internal static bool SyncFromDatabase(
      MazakAllData mazakData,
      IRepository db,
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      DateTime now,
      bool attemptCleanup,
      ImmutableHashSet<string> additionalManagedJobs = null
    )
    {
      ManagedMazakSchedules.ConfirmObserved(db, mazakCfg, mazakData);
      if (attemptCleanup && ManagedMazakSchedules.Cleanup(db, mazakCfg, mazakData, mazakDb))
        return true;
      var routes = mazakCfg.UseInsightJobQuantities is null
        ? []
        : db.LoadMazakManagedSchedules(mazakCfg.ControllerSource);
      var jobs = PendingDownload(mazakData, db, mazakCfg, now, routes, additionalManagedJobs);
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

      var managedParts = routes.ToLookup(r => r.PartName, StringComparer.OrdinalIgnoreCase);
      var pending = jobs.Where(job =>
          !HasAllInitialRoutes(mazakData.Schedules, job, mazakCfg, managedParts)
        )
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
          routes,
          managedParts
        );
        // A failed native write is not a rollback. Each retry starts from a fresh observation;
        // after successful construction, schedule only the selected definitions, not retained peers.
        mazakData = mazakDb.LoadAllData();
        if (pending.Any(mazakCfg.UsesInsightJobQuantities))
          managedParts = db.LoadMazakManagedSchedules(mazakCfg.ControllerSource)
            .ToLookup(r => r.PartName, StringComparer.OrdinalIgnoreCase);
        var wroteSchedules = AddSchedules(
          mazakData with
          {
            Parts = mazakData
              .Parts.Where(part => partNames.Contains(part.PartName))
              .ToImmutableList(),
          },
          db,
          pending,
          mazakDb,
          mazakCfg,
          managedParts
        );
        if (
          !wroteSchedules
          && jobs.All(mazakCfg.UsesInsightJobQuantities)
          && pending.Count == jobs.Count()
        )
          return false;
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
    ) =>
      PendingDownload(
        data,
        db,
        config,
        now,
        config.UseInsightJobQuantities is null
          ? []
          : db.LoadMazakManagedSchedules(config.ControllerSource),
        additionalManagedJobs
      )
        ?.ToImmutableList()
      ?? [];

    private static IGrouping<string, HistoricJob> PendingDownload(
      MazakAllData mazakData,
      IRepository db,
      MazakConfig mazakCfg,
      DateTime now,
      ImmutableList<MazakManagedSchedule> routes,
      ImmutableHashSet<string> additionalManagedJobs
    )
    {
      var window = DownloadWindow(now);
      return db.LoadJobsNotCopiedToSystem(
          window.EarliestEnd,
          window.LatestStart,
          includeDecremented: mazakCfg.UseInsightJobQuantities is not null
        )
        .Concat(InterruptedManagedDownloads(db, mazakCfg, mazakData, routes))
        .Concat(
          (additionalManagedJobs ?? [])
            .Select(db.LoadJob)
            .OfType<HistoricJob>()
            .Where(j => !j.CopiedToSystem && !j.Archived && ManagedMazakSchedules.Owns(mazakCfg, j))
        )
        .DistinctBy(j => j.UniqueStr)
        .Where(j => mazakCfg.BulkJobFilter?.Invoke(j) != false)
        .Where(j => !j.Archived || !mazakCfg.UsesInsightJobQuantities(j))
        .Where(j => HasDownloadDemand(j, mazakCfg))
        .Where(j =>
          !mazakCfg.UsesInsightJobQuantities(j)
          || ManagedMazakSchedules.CanContinueDownload(db, mazakCfg, mazakData, j)
        )
        .GroupBy(j => j.ScheduleId)
        // Only do one schedule at a time
        .MinBy(g => g.Key, StringComparer.Ordinal);
    }

    private static IEnumerable<HistoricJob> InterruptedManagedDownloads(
      IRepository db,
      MazakConfig config,
      MazakAllData observed,
      ImmutableList<MazakManagedSchedule> routes
    )
    {
      if (config.UseInsightJobQuantities is null)
        yield break;
      // A controller may retain a part from an older download before Insight has a binding
      // or Mazak has its schedule. Its route remains eligible for the usual ownership checks.
      var comments = routes
        .Select(r => r.Comment)
        .Concat(
          observed
            .Parts.Where(p => MazakPart.IsSailPart(p.PartName, p.Comment))
            .Select(p => p.Comment)
        );
      foreach (var group in comments.Distinct().GroupBy(MazakPart.UniqueFromComment))
        if (
          db.LoadJob(group.Key) is { CopiedToSystem: false, Archived: false } job
          && ManagedMazakSchedules.Owns(config, job)
          && MazakPart.InitialScheduleComments(job).Any(group.Contains)
        )
          yield return job;
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
      ImmutableList<MazakManagedSchedule> routes,
      ILookup<string, MazakManagedSchedule> managedPartsByName
    )
    {
      //first allocate a UID to use for this download
      var reservedUIDs = mazakCfg.UseInsightJobQuantities is null
        ? ImmutableHashSet<int>.Empty
        : jobDB
          .LoadMazakManagedPartNames(mazakCfg.ControllerSource, includeRetired: true)
          .Select(MazakPart.ParseUID)
          .ToImmutableHashSet();
      int UID = 0;
      while (UID < int.MaxValue)
      {
        if (reservedUIDs.Contains(UID))
          goto found;
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
        && !managedPartsByName.Contains(s.PartName)
        && (
          mazakCfg.UseInsightJobQuantities == null
          || !MazakPart.IsSailPart(s.PartName, s.Comment)
          || !mazakCfg.UsesInsightJobQuantities(
            jobDB.LoadJob(MazakPart.UniqueFromComment(s.Comment))
          )
        )
      );
      if (mazakCfg.BulkJobFilter is not null)
        savedParts.UnionWith(
          mazakData
            .Parts.Where(p => !InDownloadScope(p.PartName, p.Comment))
            .Select(p => p.PartName)
        );
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
        savedParts.UnionWith(
          mazakData
            .Parts.Where(p => managedPartsByName.Contains(p.PartName))
            .Select(p => p.PartName)
        );
      }
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
        reservedPartNames: routes.ToImmutableDictionary(r => r.Comment, r => r.PartName)
      );
      if (jobErrs.Count != 0)
      {
        throw new BadRequestException(string.Join(Environment.NewLine, jobErrs));
      }
      if (mazakCfg.UseInsightJobQuantities != null)
      {
        var protectedPrograms = routes.SelectMany(r => r.Programs).ToImmutableHashSet();
        mazakJobs.UsedMainProgramComments.UnionWith(
          mazakData
            .MainPrograms.Where(p => protectedPrograms.Contains(p.MainProgram))
            .Select(p => p.Comment)
        );
      }

      var selectedParts = mazakJobs
        .ReusedParts.Select(part => part.PartName)
        .Concat(mazakJobs.AllParts.Select(part => part.PartName))
        .ToImmutableHashSet();
      MazakWriteData managedDefinitions = null;
      MazakWriteData managedParts = null;
      var managedJobs = jobs.Where(mazakCfg.UsesInsightJobQuantities)
        .ToDictionary(job => job.UniqueStr);
      if (managedJobs.Count != 0)
      {
        if (mazakJobs.AllParts.Any())
          managedDefinitions = mazakJobs.AddFixtureAndProgramDatabaseRows(
            jobDB.LoadProgramContent,
            mazakCfg.ProgramDirectory
          );
        managedParts = mazakJobs.CreatePartPalletDatabaseRows(mazakCfg);
        foreach (var part in managedParts.Parts.Concat(mazakJobs.ReusedParts))
          if (managedJobs.TryGetValue(MazakPart.UniqueFromComment(part.Comment), out var job))
            ManagedMazakSchedules.RecordInstallation(jobDB, mazakCfg, mazakData, job, part);
      }
      if (!mazakJobs.AllParts.Any())
      {
        // Complete pallet transactions even when every part transaction already succeeded.
        var mappings = managedParts ?? mazakJobs.CreatePartPalletDatabaseRows(mazakCfg);
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
      var retainedFixtures = routes
        .SelectMany(r => r.Fixtures)
        .Concat(
          mazakData.PalletSubStatuses?.Where(p => p.FixQuantity > 0).Select(p => p.FixtureName)
            ?? []
        )
        .ToImmutableHashSet();
      if (mazakCfg.UseInsightJobQuantities is not null)
        transSet = transSet with
        {
          Pallets = transSet
            .Pallets.Where(p => !retainedFixtures.Contains(p.Fixture))
            .ToImmutableList(),
        };
      mazakDb.Save(transSet);

      //have to delete fixtures after schedule, parts, and pallets are already deleted
      transSet = mazakJobs.DeleteFixtureAndProgramDatabaseRows();
      if (mazakCfg.UseInsightJobQuantities is not null)
        transSet = transSet with
        {
          Fixtures = transSet
            .Fixtures.Where(f => !retainedFixtures.Contains(f.FixtureName))
            .ToImmutableList(),
        };
      mazakDb.Save(transSet);

      transSet = managedDefinitions
        ?? mazakJobs.AddFixtureAndProgramDatabaseRows(
          jobDB.LoadProgramContent,
          mazakCfg.ProgramDirectory
        );
      mazakDb.Save(transSet);

      //now save the pallets and parts
      transSet = managedParts ?? mazakJobs.CreatePartPalletDatabaseRows(mazakCfg);
      mazakDb.Save(transSet);
      return selectedParts;
    }

    private static bool AddSchedules(
      MazakAllData mazakData,
      IRepository jobDB,
      IReadOnlyList<Job> jobs,
      IMazakDB writeDb,
      MazakConfig mazakCfg,
      ILookup<string, MazakManagedSchedule> managedParts
    )
    {
      Log.Debug("Adding new schedules for {@jobs}, mazak data is {@mazakData}", jobs, mazakData);

      // Preserve every native ID when excluding contradictory comments from route confirmation.
      var transSet = BuildMazakSchedules.AddSchedules(mazakData, jobs, mazakCfg, (name, comment) =>
        managedParts[name].All(binding => binding.Comment == comment)
      );
      if (!transSet.Schedules.Any())
        return false;

      var jobsByUnique = jobs.ToDictionary(j => j.UniqueStr);
      var managedSchedules = transSet
        .Schedules.Select(s =>
          (Schedule: s, Job: jobsByUnique[MazakPart.UniqueFromComment(s.Comment)])
        )
        .Where(entry => mazakCfg.UsesInsightJobQuantities(entry.Job))
        .ToImmutableList();
      foreach (var (schedule, job) in managedSchedules)
        ManagedMazakSchedules.CheckScheduleInstallation(
          mazakData,
          job,
          schedule,
          managedParts[schedule.PartName]
            .SingleOrDefault(route => route.PartName == schedule.PartName)
        );
      writeDb.Save(transSet);

      if (!managedSchedules.IsEmpty)
      {
        ManagedMazakSchedules.ConfirmObserved(jobDB, mazakCfg, writeDb.LoadAllData());
        managedParts = jobDB
          .LoadMazakManagedSchedules(mazakCfg.ControllerSource)
          .ToLookup(r => r.PartName, StringComparer.OrdinalIgnoreCase);
      }
      var installed = mazakData.Schedules.Concat(transSet.Schedules);
      foreach (var job in jobs)
        if (HasAllInitialRoutes(installed, job, mazakCfg, managedParts))
          jobDB.MarkJobCopiedToSystem(job.UniqueStr);
      return true;
    }

    private static bool HasAllInitialRoutes(
      IEnumerable<MazakScheduleRow> schedules,
      Job job,
      MazakConfig config,
      ILookup<string, MazakManagedSchedule> managedParts
    )
    {
      var managed = config.UsesInsightJobQuantities(job);
      return MazakPart
        .InitialScheduleComments(job)
        .All(comment =>
          schedules.Any(schedule =>
            schedule.Comment == comment
            && (
              managed
                ? managedParts[schedule.PartName]
                  .Any(route =>
                    route.PartName == schedule.PartName
                    && route.JobUnique == job.UniqueStr
                    && route.Comment == comment
                    && route.ConfirmedScheduleId == schedule.Id
                  )
                : MazakPart.IsSailPart(schedule.PartName, comment)
                  && !managedParts.Contains(schedule.PartName)
            )
          )
        );
    }

    private static void ArchiveOldJobs(
      IRepository jobDB,
      MazakCurrentStatus schedules,
      IEnumerable<Job> toKeep,
      MazakConfig mazakCfg,
      ILookup<string, MazakManagedSchedule> managedParts
    )
    {
      var current = new HashSet<string>(toKeep.Select(j => j.UniqueStr));
      var completed = new Dictionary<string, int>();
      foreach (var sch in schedules.Schedules)
      {
        if (managedParts.Contains(sch.PartName) || !MazakPart.IsSailPart(sch.PartName, sch.Comment))
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
        .Where(j => mazakCfg.BulkJobFilter?.Invoke(j) != false)
        .ToImmutableList();

      // Keep pending downloads and managed jobs, whose backend owns archival.
      current.UnionWith(
        unarchived
          .Where(j =>
            mazakCfg.UsesInsightJobQuantities(j)
            || (!j.CopiedToSystem && j.Decrements is not { Count: > 0 })
          )
          .Select(j => j.UniqueStr)
      );

      var toArchive = unarchived.Where(j => !current.Contains(j.UniqueStr)).ToImmutableList();
      if (toArchive.IsEmpty)
        return;

      var newDecrs = toArchive
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

      jobDB.ArchiveJobs(toArchive.Select(j => j.UniqueStr), newDecrs);
    }
  }
}
