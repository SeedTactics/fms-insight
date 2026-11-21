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
        .Where(j => !j.Archived || !mazakCfg.UsesInsightJobQuantities(j))
        .GroupBy(j => j.ScheduleId)
        // Only do one schedule at a time
        .MinBy(g => g.Key, StringComparer.Ordinal);

      if (jobs == null)
      {
        return false;
      }

      InstallJobs(mazakData, jobs.ToImmutableList<Job>(), db, mazakDb, fmsSt, mazakCfg, true);
      return true;
    }

    /// <summary>
    /// Installs or resumes the accepted jobs selected for this controller, using fresh native
    /// data. Only jobs with all expected routes visible on the controller are marked copied.
    /// </summary>
    /// <remarks>
    /// The batch is additive. The caller selects jobs that still need initial delivery and
    /// serializes the entire operation with all other writes to the same physical controller.
    /// A failure can leave some native writes applied. Retry from a fresh controller observation.
    /// </remarks>
    public static void InstallJobs(
      IEnumerable<Job> jobs,
      IRepository db,
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg
    )
    {
      var batch = jobs.ToImmutableList();
      if (!batch.IsEmpty)
        InstallJobs(mazakDb.LoadAllData(), batch, db, mazakDb, fmsSt, mazakCfg, false);
    }

    private static void InstallJobs(
      MazakAllData mazakData,
      ImmutableList<Job> jobs,
      IRepository db,
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      bool stockMaintenance
    )
    {
      Log.Debug(
        "Sending jobs into mazak {@uniqs} with {@data}",
        jobs.Select(j => j.UniqueStr).ToList(),
        mazakData
      );

      var observedRoutes = stockMaintenance
        ? ScheduleComments(mazakData.Schedules)
        : ConfirmedRouteComments(mazakData);
      var pending = jobs.Where(job =>
          !MazakPart.InitialScheduleComments(job).All(observedRoutes.Contains)
        )
        .ToImmutableList();
      foreach (var job in jobs.Except(pending))
        db.MarkJobCopiedToSystem(job.UniqueStr);

      if (pending.IsEmpty)
        return;

      if (stockMaintenance)
        ArchiveOldJobs(db, mazakData, jobs, mazakCfg);

      var partNames = AddFixturesPalletsParts(
        mazakData,
        db,
        pending,
        mazakDb,
        fmsSt,
        mazakCfg,
        stockMaintenance
      );
      mazakData = mazakDb.LoadAllData();
      // Schedule only the definitions selected by this download.
      var writes = BuildMazakSchedules.AddSchedules(
        mazakData with
        {
          Parts = mazakData.Parts.Where(part => partNames.Contains(part.PartName)),
        },
        pending,
        mazakCfg
      );
      if (writes.Schedules.Count > 0)
        mazakDb.Save(writes);

      observedRoutes = stockMaintenance
        ? ScheduleComments(mazakData.Schedules.Concat(writes.Schedules))
        : ConfirmedRouteComments(writes.Schedules.Count > 0 ? mazakDb.LoadAllData() : mazakData);
      var unconfirmed = pending
        .Where(job => !MazakPart.InitialScheduleComments(job).All(observedRoutes.Contains))
        .ToImmutableList();
      foreach (var job in pending.Except(unconfirmed))
        db.MarkJobCopiedToSystem(job.UniqueStr);

      if (!stockMaintenance && !unconfirmed.IsEmpty)
        throw new InvalidOperationException(
          "Mazak did not report all expected schedules for: "
            + string.Join(", ", unconfirmed.Select(job => job.UniqueStr))
        );
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

    private static ImmutableHashSet<string> AddFixturesPalletsParts(
      MazakAllData mazakData,
      IRepository jobDB,
      IEnumerable<Job> jobs,
      IMazakDB mazakDb,
      FMSSettings fmsSt,
      MazakConfig mazakCfg,
      bool stockMaintenance
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

      var (cleanup, savedParts) = stockMaintenance
        ? StockScheduleCleanup(mazakData, jobDB, jobs, mazakCfg)
        : (null, mazakData.Parts.Select(part => part.PartName).ToHashSet());
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
        reuseExistingParts: true
      );
      if (jobErrs.Count != 0)
      {
        throw new BadRequestException(string.Join(Environment.NewLine, jobErrs));
      }

      if (mazakJobs.AllParts.Any())
      {
        if (cleanup != null)
        {
          if (cleanup.Schedules.Any())
            mazakDb.Save(cleanup);

          var oldParts = mazakJobs.DeleteOldPartRows();
          if (oldParts.Parts.Any())
            mazakDb.Save(oldParts);

          mazakDb.Save(mazakJobs.DeleteOldPalletRows());
          // Fixtures and programs can be removed after the dependent rows.
          mazakDb.Save(mazakJobs.DeleteFixtureAndProgramDatabaseRows());
        }

        mazakDb.Save(
          mazakJobs.AddFixtureAndProgramDatabaseRows(
            jobDB.LoadProgramContent,
            mazakCfg.ProgramDirectory
          )
        );
      }

      // Complete missing pallet transactions even when every part already exists.
      var mappings = mazakJobs.CreatePartPalletDatabaseRows(mazakCfg);
      if (mappings.Parts.Any() || mappings.Pallets.Any())
        mazakDb.Save(mappings);

      return mazakJobs
        .ReusedParts.Select(part => part.PartName)
        .Concat(mazakJobs.AllParts.Select(part => part.PartName))
        .ToImmutableHashSet();
    }

    private static (MazakWriteData, ISet<string>) StockScheduleCleanup(
      MazakAllData mazakData,
      IRepository jobDB,
      IEnumerable<Job> jobs,
      MazakConfig mazakCfg
    )
    {
      var (cleanup, savedParts) = BuildMazakSchedules.RemoveCompletedSchedules(mazakData, s =>
        !jobs.Any(job => MazakPart.UniqueFromComment(s.Comment ?? "") == job.UniqueStr)
        && (
          mazakCfg.UseInsightJobQuantities == null
          || !MazakPart.IsSailPart(s.PartName, s.Comment)
          || !mazakCfg.UsesInsightJobQuantities(
            jobDB.LoadJob(MazakPart.UniqueFromComment(s.Comment))
          )
        )
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
      }
      return (cleanup, savedParts);
    }

    private static ImmutableHashSet<string> ConfirmedRouteComments(MazakAllData data)
    {
      var parts = data.Parts.Select(part => (part.PartName, part.Comment)).ToImmutableHashSet();
      return ScheduleComments(
        data.Schedules.Where(schedule => parts.Contains((schedule.PartName, schedule.Comment)))
      );
    }

    private static ImmutableHashSet<string> ScheduleComments(
      IEnumerable<MazakScheduleRow> schedules
    ) =>
      schedules
        .Where(schedule => MazakPart.IsSailPart(schedule.PartName, schedule.Comment))
        .Select(schedule => schedule.Comment)
        .ToImmutableHashSet();

    private static void ArchiveOldJobs(
      IRepository jobDB,
      MazakCurrentStatus schedules,
      IEnumerable<Job> toKeep,
      MazakConfig mazakCfg
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
  }
}
