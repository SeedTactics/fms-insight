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
    )
    {
      var jobs = db.LoadJobsNotCopiedToSystem(
          now.AddHours(-JobLookbackHours),
          now.AddHours(1),
          includeDecremented: false
        )
        .Where(j =>
          mazakCfg.BulkJobFilter is { } owns
            ? !j.Archived && owns(j)
            : !j.Archived || !mazakCfg.UsesInsightJobQuantities(j)
        )
        .GroupBy(j => j.ScheduleId)
        // Only do one schedule at a time
        .MinBy(g => g.Key, StringComparer.Ordinal);

      if (jobs == null)
      {
        return mazakCfg.BulkJobFilter != null
          && ResumeScopedCleanup(mazakData, db, mazakDb, mazakCfg);
      }

      //there are jobs to copy
      Log.Debug(
        "Sending jobs into mazak {@uniqs} with {@data}",
        jobs.Select(j => j.UniqueStr).ToList(),
        mazakData
      );

      var reserved = db.LoadReservedMazakPartNames(mazakCfg.ControllerSource)
        .WithComparer(StringComparer.OrdinalIgnoreCase);
      if (!reserved.IsEmpty)
      {
        var reservedComments = mazakData
          .Parts.Where(p => reserved.Contains(p.PartName))
          .Select(p => p.Comment)
          .Concat(
            mazakData.Schedules.Where(s => reserved.Contains(s.PartName)).Select(s => s.Comment)
          )
          .ToImmutableHashSet(StringComparer.Ordinal);
        var conflict = jobs.FirstOrDefault(j =>
          MazakPart.InitialScheduleComments(j).Any(reservedComments.Contains)
        );
        if (conflict != null)
          throw new ConflictRequestException(
            $"Bulk download of '{conflict.UniqueStr}' conflicts with a reserved native Mazak part."
          );
      }

      var pending = jobs.Where(job => !HasAllInitialRoutes(mazakData.Schedules, job))
        .ToImmutableList();
      foreach (var job in jobs.Except(pending))
        db.MarkJobCopiedToSystem(job.UniqueStr);

      if (!pending.IsEmpty)
      {
        ArchiveOldJobs(db, mazakData, jobs, mazakCfg, reserved);
        var partNames = AddFixturesPalletsParts(
          mazakData,
          db,
          pending,
          mazakDb,
          fmsSt,
          mazakCfg,
          reserved
        );
        // A failed native write is not a rollback. Each retry starts from a fresh observation;
        // after successful construction, schedule only the selected definitions, not retained peers.
        mazakData = mazakDb.LoadAllData();
        AddSchedules(
          mazakData with
          {
            Parts = mazakData
              .Parts.Where(part => partNames.Contains(part.PartName))
              .ToImmutableList(),
          },
          db,
          pending,
          mazakDb,
          mazakCfg
        );
      }
      else if (mazakCfg.BulkJobFilter != null)
      {
        ResumeScopedCleanup(mazakData, db, mazakDb, mazakCfg);
      }

      return true;
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

    internal static int FindUnusedDownloadId(MazakAllData data, IReadOnlySet<string> reserved)
    {
      var used = data
        .Schedules.Select(s => s.PartName)
        .Concat(data.Parts.Select(p => p.PartName))
        .Concat(data.Fixtures.Select(f => f.FixtureName))
        .Concat(reserved)
        .Where(name => !string.IsNullOrEmpty(name))
        .Select(MazakPart.ParseUID)
        .ToImmutableHashSet();
      for (var id = 0; id < int.MaxValue; id++)
        if (!used.Contains(id))
          return id;
      throw new InvalidOperationException("Unable to find an unused Mazak download ID.");
    }

    private static ImmutableHashSet<string> AddFixturesPalletsParts(
      MazakAllData mazakData,
      IRepository jobDB,
      IEnumerable<Job> jobs,
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      IReadOnlySet<string> reserved
    )
    {
      var UID = FindUnusedDownloadId(mazakData, reserved);
      Log.Debug("Creating new schedule with UID {uid}", UID);

      var scope = mazakCfg.BulkJobFilter;
      Job ObservedJob(string name, string comment) =>
        MazakPart.IsSailPart(name, comment)
          ? jobDB.LoadJob(MazakPart.UniqueFromComment(comment))
          : null;

      var (transSet, savedParts) = BuildMazakSchedules.RemoveCompletedSchedules(mazakData, s =>
      {
        if (
          reserved.Contains(s.PartName)
          || jobs.Any(job => MazakPart.UniqueFromComment(s.Comment ?? "") == job.UniqueStr)
        )
          return false;
        var job =
          scope != null || mazakCfg.UseInsightJobQuantities != null
            ? ObservedJob(s.PartName, s.Comment)
            : null;
        return (scope == null || (job != null && scope(job)))
          && !mazakCfg.UsesInsightJobQuantities(job);
      });
      savedParts.UnionWith(
        mazakData.Parts.Where(p => reserved.Contains(p.PartName)).Select(p => p.PartName)
      );
      if (mazakCfg.UseInsightJobQuantities != null)
      {
        // Retiring a schedule does not retire the job or its reusable route definitions.
        // Keep definitions for backend-owned work until that backend archives the job.
        savedParts.UnionWith(
          mazakData
            .Parts.Where(p => MazakPart.IsSailPart(p.PartName, p.Comment))
            .Where(p =>
              ObservedJob(p.PartName, p.Comment) is { Archived: false } job
              && mazakCfg.UsesInsightJobQuantities(job)
            )
            .Select(p => p.PartName)
        );
      }
      if (scope != null)
      {
        savedParts.UnionWith(
          mazakData
            .Parts.Where(part =>
              ObservedJob(part.PartName, part.Comment) is not { } job || !scope(job)
            )
            .Select(part => part.PartName)
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
        preparedFixtureGroups: jobDB
          .LoadMazakRoutePreparations(mazakCfg.ControllerSource)
          .Values.Select(p => p.FixtureGroup)
          .ToImmutableHashSet()
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
        if (scope != null)
          ResumeScopedCleanup(mazakData, jobDB, mazakDb, mazakCfg, mazakJobs);
        // Complete pallet transactions even when every part transaction already succeeded.
        var mappings = mazakJobs.CreatePartPalletDatabaseRows(mazakCfg);
        if (mappings.Pallets.Any())
          mazakDb.Save(mappings);
        return selectedParts;
      }

      if (transSet.Schedules.Any())
        mazakDb.Save(transSet);

      transSet = mazakJobs.DeleteOldPartRows();
      if (transSet.Parts.Count > 0)
      {
        if (scope != null)
        {
          var retiredParts = transSet.Parts.Select(p => p.PartName).ToImmutableHashSet();
          var candidates = MazakRouteRetirement.Capture(mazakData, retiredParts);
          jobDB.StageMazakBulkCleanup(
            mazakCfg.ControllerSource,
            new MazakBulkCleanup
            {
              Parts = candidates.PartNames,
              Fixtures = candidates.FixtureNames,
              Programs = candidates.MainPrograms,
            }
          );
        }
        mazakDb.Save(transSet);
      }

      if (scope == null)
      {
        var preparations = jobDB
          .LoadMazakRoutePreparations(mazakCfg.ControllerSource)
          .Values.ToImmutableList();
        var fixtures = preparations
          .Select(p => p.FixtureName)
          .ToImmutableHashSet(StringComparer.Ordinal);
        var programs = preparations
          .Select(p => p.RequiredProgram ?? p.AddedProgram)
          .ToImmutableHashSet(StringComparer.Ordinal);
        var mappings = mazakJobs.DeleteOldPalletRows();
        mazakDb.Save(
          mappings with
          {
            Pallets = mappings.Pallets.Where(p => !fixtures.Contains(p.Fixture)).ToImmutableList(),
          }
        );
        var dependencies = mazakJobs.DeleteFixtureAndProgramDatabaseRows();
        mazakDb.Save(
          dependencies with
          {
            Fixtures = dependencies
              .Fixtures.Where(f => !fixtures.Contains(f.FixtureName))
              .ToImmutableList(),
            Programs = dependencies
              .Programs.Where(p => !programs.Contains(p.MainProgram))
              .ToImmutableList(),
          }
        );
      }
      else
      {
        ResumeScopedCleanup(mazakDb.LoadAllData(), jobDB, mazakDb, mazakCfg, mazakJobs);
      }

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

    // Keep the candidates until fresh readback shows no eligible deletions. Programs and
    // fixtures used by another route remain subject to ordinary retention.
    private static bool ResumeScopedCleanup(
      MazakAllData observed,
      IRepository db,
      IMazakDB native,
      MazakConfig config,
      MazakJobs download = null
    )
    {
      var saved = db.LoadMazakBulkCleanup(config.ControllerSource);
      if (
        saved.Parts.IsEmpty
        || !MazakRouteRetirement.HasAllScheduledParts(observed)
        || observed.Parts.Any(p => saved.Parts.Contains(p.PartName))
      )
        return false;
      var candidates = new MazakRetirementCandidates
      {
        PartNames = [],
        FixtureNames = saved.Fixtures,
        MainPrograms = saved.Programs,
      };
      var preparations = db.LoadMazakRoutePreparations(config.ControllerSource)
        .Values.ToImmutableList();
      var fixtures = (download?.UsedFixtures ?? Enumerable.Empty<string>())
        .Concat(preparations.Select(p => p.FixtureName))
        .ToImmutableHashSet(StringComparer.Ordinal);
      var mappings = MazakRouteRetirement.RemovePalletMappings(observed, candidates, fixtures);
      var wrote = mappings.Pallets.Count != 0;
      if (wrote)
      {
        native.Save(mappings);
        observed = native.LoadAllData();
        if (!MazakRouteRetirement.HasAllScheduledParts(observed))
          return true;
        if (
          MazakRouteRetirement.RemovePalletMappings(observed, candidates, fixtures).Pallets.Count
          != 0
        )
          throw new ConflictRequestException(
            "Mazak did not confirm scoped pallet mapping deletion."
          );
      }

      MazakWriteData Dependencies(MazakAllData view) =>
        MazakRouteRetirement.RemoveUnusedDependencies(
          view,
          candidates,
          fixtures,
          MazakRouteRetirement
            .RequiredProgramComments(view, preparations)
            .Concat(ConvertJobsToMazakParts.RequiredProgramComments(view, download?.AllParts ?? []))
        );
      var unused = Dependencies(observed);
      if (unused.Fixtures.Count != 0 || unused.Programs.Count != 0)
      {
        native.Save(unused);
        observed = native.LoadAllData();
        if (!MazakRouteRetirement.HasAllScheduledParts(observed))
          return true;
        var unconfirmed = Dependencies(observed);
        if (unconfirmed.Fixtures.Count != 0 || unconfirmed.Programs.Count != 0)
          throw new ConflictRequestException(
            "Mazak did not confirm scoped fixture or program deletion."
          );
        wrote = true;
      }
      db.CompleteMazakBulkCleanup(config.ControllerSource);
      return wrote;
    }

    private static void AddSchedules(
      MazakAllData mazakData,
      IRepository jobDB,
      IReadOnlyList<Job> jobs,
      IMazakDB writeDb,
      MazakConfig mazakCfg
    )
    {
      Log.Debug("Adding new schedules for {@jobs}, mazak data is {@mazakData}", jobs, mazakData);

      var transSet = BuildMazakSchedules.AddSchedules(mazakData, jobs, mazakCfg);
      if (transSet.Schedules.Any())
      {
        writeDb.Save(transSet);

        foreach (var job in jobs)
          if (HasAllInitialRoutes(mazakData.Schedules.Concat(transSet.Schedules), job))
            jobDB.MarkJobCopiedToSystem(job.UniqueStr);
      }
    }

    private static bool HasAllInitialRoutes(IEnumerable<MazakScheduleRow> schedules, Job job)
    {
      var comments = schedules
        .Where(schedule => MazakPart.IsSailPart(schedule.PartName, schedule.Comment))
        .Select(schedule => schedule.Comment)
        .ToImmutableHashSet();
      return MazakPart.InitialScheduleComments(job).All(comments.Contains);
    }

    private static void ArchiveOldJobs(
      IRepository jobDB,
      MazakCurrentStatus schedules,
      IEnumerable<Job> toKeep,
      MazakConfig mazakCfg,
      IReadOnlySet<string> reserved
    )
    {
      var current = new HashSet<string>(toKeep.Select(j => j.UniqueStr));
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

        if (sch.PlanQuantity == sch.CompleteQuantity && !reserved.Contains(sch.PartName))
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
        .Where(j =>
          !current.Contains(j.UniqueStr)
          && (mazakCfg.BulkJobFilter == null || mazakCfg.BulkJobFilter(j))
        )
        .Select(j => j.UniqueStr)
        .ToImmutableHashSet(StringComparer.Ordinal);

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
