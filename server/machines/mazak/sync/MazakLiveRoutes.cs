#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.ExceptionServices;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

internal static class MazakLiveRoutes
{
  internal static bool Owns(MazakConfig config, Job? job) =>
    job is not null
    && config.UsesInsightJobQuantities(job)
    && config.BulkJobFilter?.Invoke(job) != false;

  internal static (MazakPartRow? Part, MazakScheduleRow? Schedule) Inspect(
    MazakAllData observed,
    Job job,
    string comment
  )
  {
    var parts = observed.Parts.Where(part => part.Comment == comment).Take(2).ToImmutableList();
    var part = parts.FirstOrDefault();
    var schedules = observed
      .Schedules.Where(row =>
        row.Comment == comment
        || part is not null
          && string.Equals(row.PartName, part.PartName, StringComparison.OrdinalIgnoreCase)
      )
      .Take(2)
      .ToImmutableList();
    var schedule = schedules.FirstOrDefault();
    var nativeProcesses = Enumerable.Range(
      1,
      MazakPart.IsSplitComment(comment) ? 1 : job.Processes.Count
    );

    if (
      !MazakPart.InitialScheduleComments(job).Contains(comment)
      || parts.Count > 1
      || schedules.Count > 1
      || part is not null
        && (
          !MazakPart.IsSailPart(part.PartName, comment)
          || MazakPart.ExtractPartNameFromMazakPartName(part.PartName) != job.PartName
          || observed.Parts.Count(row =>
            string.Equals(row.PartName, part.PartName, StringComparison.OrdinalIgnoreCase)
          ) != 1
          || !part
            .Processes.Select(proc => proc.ProcessNumber)
            .Order()
            .SequenceEqual(nativeProcesses)
        )
      || schedule is not null
        && (
          part is null
          || schedule.PartName != part.PartName
          || schedule.Comment != comment
          || observed.Schedules.Count(row => row.Id == schedule.Id) != 1
          || !schedule
            .Processes.Select(proc => proc.ProcessNumber)
            .Order()
            .SequenceEqual(nativeProcesses)
        )
    )
    {
      var nativeNames = parts
        .Select(row => row.PartName)
        .Concat(schedules.Select(row => row.PartName))
        .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
      var observedRows = observed
        .Parts.Where(row => nativeNames.Contains(row.PartName))
        .Select(row => $"part '{row.PartName}' with comment '{row.Comment}'")
        .Concat(
          schedules.Select(row =>
            $"schedule {row.Id} for '{row.PartName}' with comment '{row.Comment}'"
          )
        );
      throw new ConflictRequestException(
        $"Mazak job '{job.UniqueStr}' has conflicting readback for route '{comment}'. Observed: {string.Join(", ", observedRows)}."
      );
    }

    return (part, schedule);
  }

  // Check both native definitions. Either comment may have been edited to another job's comment.
  internal static ImmutableHashSet<string> ManagedPartNames(
    IRepository db,
    MazakConfig config,
    MazakCurrentStatus data
  )
  {
    if (config.UseInsightJobQuantities is null)
      return ImmutableHashSet<string>.Empty;

    return (data.Parts ?? Enumerable.Empty<MazakPartRow>())
      .Select(p => (p.PartName, p.Comment))
      .Concat(data.Schedules.Select(s => (s.PartName, s.Comment)))
      .Where(row => MazakPart.IsSailPart(row.PartName, row.Comment))
      .GroupBy(row => MazakPart.UniqueFromComment(row.Comment), StringComparer.Ordinal)
      .Where(group => config.UsesInsightJobQuantities(db.LoadJob(group.Key)))
      .SelectMany(group => group.Select(row => row.PartName))
      .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
  }

  internal static bool RetireSchedules(
    IRepository db,
    MazakConfig config,
    MazakAllData data,
    IMazakDB controller
  )
  {
    var rejected = ImmutableHashSet<string>.Empty;
    ExceptionDispatchInfo? firstFailure = null;
    while (PlanRetirement(db, config, data, rejected) is { } write)
    {
      var schedule = write.Schedules.Single();
      try
      {
        controller.Save(write);
        return true;
      }
      catch (Exception error)
      {
        firstFailure ??= ExceptionDispatchInfo.Capture(error);
        rejected = rejected.Add(schedule.PartName);
        BuildMazakSchedules.Log.Warning(
          error,
          "Could not retire archived Mazak schedule {Schedule} for {Part} on {Controller}",
          schedule.Id,
          schedule.PartName,
          config.ControllerSource
        );
        // Mazak can apply a write before a lost reply. Check fresh native state before trying a peer.
        data = controller.LoadAllData();
      }
    }
    firstFailure?.Throw();
    return false;
  }

  private static MazakWriteData? PlanRetirement(
    IRepository db,
    MazakConfig config,
    MazakAllData data,
    ImmutableHashSet<string> rejected
  )
  {
    if (
      config.UseInsightJobQuantities is null
      || data.Parts is null
      || data.LoadActions is null
      || data.PalletSubStatuses is null
    )
      return null;

    var occupied = data.PalletSubStatuses.Where(pallet => pallet.FixQuantity > 0).ToImmutableList();
    var candidate = data.Schedules.FirstOrDefault(CanRetire);
    return candidate is null
      ? null
      : BuildMazakSchedules.RemoveSchedules(data, schedule => schedule.Id == candidate.Id).Item1;

    bool CanRetire(MazakScheduleRow schedule)
    {
      if (
        rejected.Contains(schedule.PartName)
        || !MazakPart.IsSailPart(schedule.PartName, schedule.Comment)
        || schedule.Processes.Any(p =>
          p.ProcessMaterialQuantity != 0 || p.ProcessExecuteQuantity != 0
        )
        || occupied.Any(p =>
          p.ScheduleID == schedule.Id
          || string.Equals(p.PartName, schedule.PartName, StringComparison.OrdinalIgnoreCase)
        )
      )
        return false;
      var job = db.LoadJob(MazakPart.UniqueFromComment(schedule.Comment));
      if (job is not { Archived: true } || !Owns(config, job))
        return false;
      if (
        data.LoadActions.Any(action =>
          action.Comment == schedule.Comment
          || string.Equals(action.Part, schedule.PartName, StringComparison.OrdinalIgnoreCase)
          || string.IsNullOrEmpty(action.Comment)
            && string.Equals(action.Part, job.PartName, StringComparison.OrdinalIgnoreCase)
        )
      )
        return false;

      try
      {
        return Inspect(data, job, schedule.Comment).Schedule == schedule;
      }
      catch (ConflictRequestException conflict)
      {
        BuildMazakSchedules.Log.Warning("{Conflict}", conflict.Message);
        return false;
      }
    }
  }

  internal static string? ReadbackProblem(
    IRepository db,
    MazakConfig config,
    MazakAllData data,
    Job job,
    string comment,
    bool requireInstalledPart = false
  )
  {
    try
    {
      var (part, schedule) = Inspect(data, job, comment);
      if (job is HistoricJob { CopiedToSystem: true } && schedule is null)
        return $"Mazak controller '{config.ControllerSource}' has no verified schedule for job '{job.UniqueStr}', route '{comment}'. Check the controller before admitting material.";

      if (part is not null)
        return ExecutionConflict(
          db,
          data,
          config,
          job,
          part,
          allowMissingPalletMappings: !requireInstalledPart && schedule is null
        );
      if (requireInstalledPart)
        return $"Mazak job '{job.UniqueStr}' is missing its installed part for route '{comment}'.";

      var route = MazakPart.ParseCommentInfo(comment);
      return Enumerable
        .Range(1, route.IsSplit ? 1 : job.Processes.Count)
        .Select(nativeProcess =>
          ConvertJobsToMazakParts
            .ResolveProcessRoute(
              job,
              route.JobProcessForMazakProcess(nativeProcess),
              config,
              data,
              (program, revision) => WriteJobs.LookupProgram(db, program, revision)
            )
            .Error
        )
        .FirstOrDefault(error => error is not null);
    }
    catch (ConflictRequestException conflict)
    {
      return conflict.Message;
    }
  }

  internal static ImmutableList<CurrentStatusAlarm> Diagnostics(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    DateTime now
  )
  {
    if (config.UseInsightJobQuantities is null)
      return [];

    var observedComments = observed
      .Parts.Select(part => part.Comment)
      .Concat(observed.Schedules.Select(schedule => schedule.Comment))
      .ToImmutableHashSet(StringComparer.Ordinal);
    return (
      from job in db.LoadUnarchivedJobs()
      where Owns(config, job)
      let comments = MazakPart.InitialScheduleComments(job)
      where
        job.CopiedToSystem
        || comments.Any(observedComments.Contains)
        || job.RouteEndUTC >= now.AddHours(-WriteJobs.JobLookbackHours)
          && job.RouteStartUTC <= now.AddHours(1)
          && WriteJobs.HasManagedDownloadDemand(job)
      from comment in comments
      let message = ReadbackProblem(db, config, observed, job, comment)
      where message is not null
      select new CurrentStatusAlarm
      {
        Code = "mazak-managed-schedule-unavailable",
        Message = message,
        Impact = AlarmImpact.Informational,
      }
    ).ToImmutableList();
  }

  internal static string? ExecutionConflict(
    IRepository db,
    MazakAllData data,
    MazakConfig config,
    Job job,
    MazakPartRow part,
    bool allowMissingPalletMappings = false
  )
  {
    var route = MazakPart.ParseCommentInfo(part.Comment);
    var expectedPart = route.IsSplit
      ? MazakPart.FromNativeName(job, part.PartName, route.FmsProcess, 1)
      : MazakPart.FromNativeName(job, part.PartName, null, null);
    var error = ConvertJobsToMazakParts.BuildPartProcesses(
      expectedPart,
      config,
      data,
      (program, revision) => WriteJobs.LookupProgram(db, program, revision),
      // A custom hook can select a different program based on its input alias. Resolve its
      // input the same way as a download so the installed program cannot change that choice.
      installedPrograms: config.ProcessFromJob is null
        ? part.Processes.ToImmutableDictionary(p => p.ProcessNumber, p => p.MainProgram)
        : null
    );
    if (error is not null)
      return $"Mazak controller '{config.ControllerSource}' cannot verify native part '{part.PartName}' for job '{job.UniqueStr}'. {error}";
    foreach (var expectedProcess in expectedPart.Processes)
    {
      var process = part.Processes.Single(p => p.ProcessNumber == expectedProcess.ProcessNumber);
      var jobProcess = expectedProcess.JobProcessNumber;
      var row = expectedPart.ToDatabaseRow();
      try
      {
        expectedProcess.CreateDatabaseRow(row, process.Fixture, config);
      }
      catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
      {
        return $"Mazak controller '{config.ControllerSource}' cannot map the stations for job '{job.UniqueStr}', process {jobProcess}.";
      }
      var expected = row.Processes.Single();
      var required = expectedProcess.PartProgram;
      if (
        string.IsNullOrEmpty(expected.MainProgram)
        || process.MainProgram != expected.MainProgram
        || !data.MainPrograms.Any(native =>
          native.MainProgram == expected.MainProgram
          && (
            string.IsNullOrEmpty(required.ProgramName)
            || (
              MazakProcess.TryParseMainProgramComment(
                native.Comment,
                out var name,
                out var revision
              )
              && name == required.ProgramName
              && revision == required.Revision
            )
          )
        )
      )
      {
        var requiredProgram = string.IsNullOrEmpty(required.ProgramName)
          ? expected.MainProgram
          : $"'{required.ProgramName}' revision {required.Revision}";
        return $"Mazak controller '{config.ControllerSource}' cannot verify the program for job '{job.UniqueStr}', process {jobProcess}, native part '{part.PartName}'. The part references '{process.MainProgram}', and the route requires {requiredProgram}.";
      }

      var mappings = data
        .Pallets.Where(p => p.Fixture == process.Fixture)
        .Select(p => p.PalletNumber)
        .ToImmutableHashSet();
      var expectedPallets = expectedProcess.Pallets().Select(config.InversePalletNumber);
      // The retry can add missing mappings for its generated fixtures. The installed process
      // row is reused unchanged, and the retry cannot remove unexpected mappings.
      var palletsMatch =
        allowMissingPalletMappings && MazakPart.IsInsightFixture(process.Fixture)
          ? mappings.IsSubsetOf(expectedPallets)
          : mappings.SetEquals(expectedPallets);
      var mismatch =
        process.FixQuantity != expected.FixQuantity ? "pieces per fixture"
        : process.CutMc?.Trim() != expected.CutMc?.Trim() ? "eligible machines"
        : process.FixLDS?.Trim() != expected.FixLDS?.Trim() ? "eligible load stations"
        : process.RemoveLDS?.Trim() != expected.RemoveLDS?.Trim() ? "eligible unload stations"
        : process.ContinueCut != expected.ContinueCut ? "continue-cut settings"
        : !palletsMatch ? "eligible pallets"
        : null;
      if (mismatch is not null)
        return $"Mazak controller '{config.ControllerSource}' has different {mismatch} for job '{job.UniqueStr}', process {jobProcess}, native part '{part.PartName}'. Check the controller before admitting material.";
    }
    return null;
  }
}
