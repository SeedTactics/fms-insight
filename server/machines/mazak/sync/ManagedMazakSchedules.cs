#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

internal static partial class ManagedMazakSchedules
{
  public static bool Owns(MazakConfig config, Job? job) =>
    job is not null
    && config.UsesInsightJobQuantities(job)
    && config.BulkJobFilter?.Invoke(job) != false;

  private static ImmutableArray<int> NativeProcesses(Job job, string comment) =>
    Enumerable
      .Range(1, MazakPart.IsSplitComment(comment) ? 1 : job.Processes.Count)
      .ToImmutableArray();

  public static (MazakPartRow? Part, MazakScheduleRow? Schedule) Inspect(
    MazakAllData observed,
    Job job,
    string partName,
    string comment,
    int? confirmedId
  )
  {
    bool Relevant(string name, string route) =>
      string.Equals(name, partName, StringComparison.OrdinalIgnoreCase) || route == comment;
    var expected = NativeProcesses(job, comment);
    var parts = observed
      .Parts.Where(p => Relevant(p.PartName, p.Comment))
      .Take(2)
      .ToImmutableList();
    var schedules = observed
      .Schedules.Where(s => Relevant(s.PartName, s.Comment))
      .Take(2)
      .ToImmutableList();
    if (
      !MazakPart.InitialScheduleComments(job).Contains(comment)
      || !MazakPart.IsSailPart(partName, comment)
      || MazakPart.ExtractPartNameFromMazakPartName(partName) != job.PartName
      || parts.Count > 1
      || schedules.Count > 1
      || parts.Any(p =>
        p.PartName != partName
        || p.Comment != comment
        || !p.Processes.Select(proc => proc.ProcessNumber).Order().SequenceEqual(expected)
      )
      || schedules.Any(s =>
        s.PartName != partName
        || s.Comment != comment
        || (confirmedId.HasValue && s.Id != confirmedId)
        || observed.Schedules.Count(other => other.Id == s.Id) != 1
        || !s.Processes.Select(proc => proc.ProcessNumber).Order().SequenceEqual(expected)
      )
    )
    {
      var rows = schedules
        .Select(s => $"schedule {s.Id} for '{s.PartName}' with comment '{s.Comment}'")
        .Concat(parts.Select(p => $"part '{p.PartName}' with comment '{p.Comment}'"));
      throw new ConflictRequestException(
        $"Mazak job '{job.UniqueStr}' requires route comment '{comment}' on native part '{partName}'. Conflicting readback: {string.Join(", ", rows)}."
      );
    }
    return (parts.SingleOrDefault(), schedules.SingleOrDefault());
  }

  public static (MazakPartRow? Part, MazakScheduleRow? Schedule) Inspect(
    MazakAllData data,
    Job job,
    MazakManagedSchedule binding,
    bool verifyDefinition = true
  )
  {
    var inspection = Inspect(
      data,
      job,
      binding.PartName,
      binding.Comment,
      binding.ConfirmedScheduleId
    );
    if (verifyDefinition && inspection.Part is { } part)
      VerifyInstalledProcesses(part, binding);
    return inspection;
  }

  private static void VerifyInstalledProcesses(MazakPartRow part, MazakManagedSchedule binding)
  {
    var installed = ProcessDefinitions(part);
    if (!installed.SequenceEqual(binding.Processes))
      throw new ConflictRequestException(
        $"Mazak job '{binding.JobUnique}' has changed fixtures or programs on native part '{binding.PartName}' for route '{binding.Comment}'. Expected {Describe(binding.Processes)}. Observed {Describe(installed)}."
      );

    static string Describe(ImmutableList<MazakManagedProcess> processes) =>
      string.Join(
        ", ",
        processes.Select(p =>
          $"process {p.Process} with fixture '{p.Fixture}' and program '{p.Program}'"
        )
      );
  }

  private static ImmutableList<MazakManagedProcess> ProcessDefinitions(MazakPartRow part) =>
    part
      .Processes.OrderBy(p => p.ProcessNumber)
      .Select(p => new MazakManagedProcess(p.ProcessNumber, p.Fixture, p.MainProgram))
      .ToImmutableList();

  private static (MazakPartRow? Part, MazakScheduleRow? Schedule) InspectForAdoption(
    IRepository db,
    MazakConfig config,
    MazakAllData data,
    Job job,
    string partName,
    string comment
  )
  {
    var inspection = Inspect(data, job, partName, comment, confirmedId: null);
    if (
      inspection.Part is { } part
      && ExecutionConflict(db, data, config, job, part, checkExecutionFields: false) is { } conflict
    )
      throw new ConflictRequestException(conflict);
    return inspection;
  }

  internal static string? ExecutionConflict(
    IRepository db,
    MazakAllData data,
    MazakConfig config,
    Job job,
    MazakPartRow part,
    bool checkExecutionFields = true
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

      if (!checkExecutionFields)
        continue;
      var mappings = data
        .Pallets.Where(p => p.Fixture == process.Fixture)
        .Select(p => p.PalletNumber);
      var mismatch =
        process.FixQuantity != expected.FixQuantity ? "pieces per fixture"
        : process.CutMc?.Trim() != expected.CutMc?.Trim() ? "eligible machines"
        : process.FixLDS?.Trim() != expected.FixLDS?.Trim() ? "eligible load stations"
        : process.RemoveLDS?.Trim() != expected.RemoveLDS?.Trim() ? "eligible unload stations"
        : process.ContinueCut != expected.ContinueCut ? "continue-cut settings"
        : !mappings
          .ToImmutableHashSet()
          .SetEquals(expectedProcess.Pallets().Select(config.InversePalletNumber))
          ? "eligible pallets"
        : null;
      if (mismatch is not null)
        return $"Mazak controller '{config.ControllerSource}' has different {mismatch} for job '{job.UniqueStr}', process {jobProcess}, native part '{part.PartName}'. Check the controller before admitting material.";
    }
    return null;
  }

  public static void ConfirmObserved(IRepository db, MazakConfig config, MazakAllData observed)
  {
    if (config.UseInsightJobQuantities is null)
      return;
    foreach (var schedule in observed.Schedules)
    {
      if (!MazakPart.IsSailPart(schedule.PartName, schedule.Comment))
        continue;
      if (
        db.LoadJob(MazakPart.UniqueFromComment(schedule.Comment)) is not { } job
        || !Owns(config, job)
      )
        continue;
      var saved = db.LoadMazakManagedSchedule(config.ControllerSource, schedule.PartName);
      if (
        saved is null
        && db.LoadMazakManagedSchedules(
            config.ControllerSource,
            includeRetired: true,
            jobUnique: job.UniqueStr
          )
          .Any(binding => binding.Comment == schedule.Comment)
      )
        continue;
      if (saved is { ConfirmedScheduleId: not null } or { Retired: true })
        continue;
      try
      {
        var (part, row) = saved is null
          ? InspectForAdoption(db, config, observed, job, schedule.PartName, schedule.Comment)
          : Inspect(observed, job, saved);
        if (part is not null && row is not null)
        {
          db.RecordMazakManagedSchedule(
            saved is null
              ? Definition(config, job, part, row.Id, beforeLoadCounter: null)
              : saved with
              {
                ConfirmedScheduleId = row.Id,
              }
          );
        }
      }
      catch (ConflictRequestException)
      {
        // Preserve conflicting readback for the route diagnostic and leave unrelated downloads eligible.
      }
    }
  }

  public static void RecordInstallation(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    Job job,
    MazakPartRow planned
  )
  {
    var saved = db.LoadMazakManagedSchedule(config.ControllerSource, planned.PartName);
    if (saved is null)
    {
      var (installed, schedule) = InspectForAdoption(
        db,
        config,
        observed,
        job,
        planned.PartName,
        planned.Comment
      );
      db.RecordMazakManagedSchedule(
        Definition(
          config,
          job,
          installed ?? planned,
          schedule?.Id,
          beforeLoadCounter: schedule is null ? db.MaxLogCounter() : null
        )
      );
      return;
    }

    if (
      saved.Retired
      || saved.PartName != planned.PartName
      || saved.Comment != planned.Comment
      || saved.JobUnique != job.UniqueStr
    )
      throw new ConflictRequestException(
        $"Native part '{planned.PartName}' already has a reserved lifecycle."
      );

    // If Mazak installed the part, its original definition remains authoritative. A missing
    // provisional part can be planned again under the same name, retaining earlier dependencies
    // in case only their native transactions succeeded.
    var (part, row) = Inspect(observed, job, saved);
    if (part is not null)
      return;
    if (row is not null || saved.ConfirmedScheduleId.HasValue)
      throw new ConflictRequestException(
        $"Mazak job '{job.UniqueStr}' has an installed schedule with no verified part."
      );
    var processes = ProcessDefinitions(planned);
    if (!saved.Processes.SequenceEqual(processes))
      db.RecordMazakManagedSchedule(
        saved with
        {
          Processes = processes,
          PreviousFixtures = saved
            .Fixtures.Except(processes.Select(p => p.Fixture).OfType<string>())
            .ToImmutableHashSet(),
          PreviousPrograms = saved
            .Programs.Except(processes.Select(p => p.Program).OfType<string>())
            .ToImmutableHashSet(),
        }
      );
  }

  public static void CheckScheduleInstallation(
    MazakAllData observed,
    Job job,
    MazakScheduleRow schedule,
    MazakManagedSchedule? saved
  )
  {
    if (
      saved is null
      || saved.Retired
      || saved.ConfirmedScheduleId.HasValue
      || saved.PartName != schedule.PartName
      || saved.JobUnique != job.UniqueStr
      || saved.Comment != schedule.Comment
    )
      throw new ConflictRequestException(
        $"Mazak job '{job.UniqueStr}' has no pending installation for '{schedule.PartName}'."
      );
    var (part, _) = Inspect(observed, job, saved);
    if (part is null)
      throw new ConflictRequestException(
        $"Mazak job '{job.UniqueStr}' has no verified native part."
      );
  }

  private static MazakManagedSchedule Definition(
    MazakConfig config,
    Job job,
    MazakPartRow part,
    int? confirmed,
    long? beforeLoadCounter
  ) =>
    new()
    {
      ControllerSource = config.ControllerSource,
      JobUnique = job.UniqueStr,
      PartName = part.PartName,
      Comment = part.Comment,
      ConfirmedScheduleId = confirmed,
      BeforeLoadCounter = beforeLoadCounter,
      Processes = ProcessDefinitions(part),
    };

  public static bool CanContinueDownload(
    IRepository db,
    MazakConfig config,
    MazakAllData data,
    HistoricJob job
  )
  {
    var routes = db.LoadMazakManagedSchedules(
      config.ControllerSource,
      includeRetired: true,
      jobUnique: job.UniqueStr
    );
    return MazakPart
      .InitialScheduleComments(job)
      .All(comment =>
        RouteReadbackProblem(
          db,
          config,
          data,
          job,
          comment,
          routes.SingleOrDefault(r => r.Comment == comment),
          forDownload: true
        )
          is null
      );
  }

  private static string? RouteReadbackProblem(
    IRepository db,
    MazakConfig config,
    MazakAllData data,
    HistoricJob job,
    string comment,
    MazakManagedSchedule? binding,
    bool forDownload
  )
  {
    var unavailable =
      $"Mazak controller '{config.ControllerSource}' has no verified schedule for job '{job.UniqueStr}', route '{comment}'. Check the controller before admitting material.";
    if (binding is { Retired: true })
      return $"Mazak controller '{config.ControllerSource}' has already retired native part '{binding.PartName}' for job '{job.UniqueStr}', route '{comment}'.";

    try
    {
      var nativePartName = binding?.PartName
        ?? data.Parts.FirstOrDefault(p => p.Comment == comment)?.PartName
        ?? data.Schedules.FirstOrDefault(s => s.Comment == comment)?.PartName;
      var (part, schedule) =
        binding is not null ? Inspect(data, job, binding)
        : nativePartName is not null ? Inspect(data, job, nativePartName, comment, null)
        : (null, null);

      if (part is null && schedule is not null)
        return $"Mazak controller '{config.ControllerSource}' has schedule {schedule.Id} for job '{job.UniqueStr}', route '{comment}', but its native part '{schedule.PartName}' is missing.";
      if (
        (part is null || schedule is null)
        && (binding?.ConfirmedScheduleId is not null || job.CopiedToSystem)
      )
        return unavailable;

      if (part is null)
      {
        var route = MazakPart.ParseCommentInfo(comment);
        foreach (var nativeProcess in NativeProcesses(job, comment))
        {
          var (_, error) = ConvertJobsToMazakParts.ResolveProcessRoute(
            job,
            route.JobProcessForMazakProcess(nativeProcess),
            config,
            data,
            (program, revision) => WriteJobs.LookupProgram(db, program, revision)
          );
          if (error is not null)
            return $"Mazak controller '{config.ControllerSource}' cannot download job '{job.UniqueStr}' on route '{comment}'. {error}";
        }
        return null;
      }

      if (
        ExecutionConflict(
          db,
          data,
          config,
          job,
          part,
          checkExecutionFields: !forDownload && binding?.ConfirmedScheduleId is not null
        ) is
        { } mismatch
      )
        return mismatch;

      return !forDownload && schedule is not null && binding?.ConfirmedScheduleId is null
        ? unavailable
        : null;
    }
    catch (ConflictRequestException conflict)
    {
      return conflict.Message;
    }
  }

  public static ImmutableList<CurrentStatusAlarm> Diagnostics(
    IRepository db,
    MazakConfig config,
    MazakAllData data,
    DateTime observationTimeUtc
  )
  {
    if (config.UseInsightJobQuantities is null)
      return [];
    var observedComments = data
      .Parts.Select(p => p.Comment)
      .Concat(data.Schedules.Select(s => s.Comment))
      .ToImmutableHashSet();
    var window = WriteJobs.DownloadWindow(observationTimeUtc);
    var unarchived = db.LoadUnarchivedJobs();
    var active =
      from job in unarchived
      where Owns(config, job)
      let bindings = db.LoadMazakManagedSchedules(
        config.ControllerSource,
        includeRetired: true,
        jobUnique: job.UniqueStr
      )
      let comments = MazakPart.InitialScheduleComments(job)
      where
        job.CopiedToSystem
        || !bindings.IsEmpty
        || comments.Any(observedComments.Contains)
        || (
          job.RouteEndUTC >= window.EarliestEnd
          && job.RouteStartUTC <= window.LatestStart
          && WriteJobs.HasDownloadDemand(job, config)
        )
      from comment in comments
      let message = RouteReadbackProblem(
        db,
        config,
        data,
        job,
        comment,
        bindings.SingleOrDefault(r => r.Comment == comment),
        forDownload: false
      )
      where message != null
      select new CurrentStatusAlarm
      {
        Code = "mazak-managed-schedule-unavailable",
        Message = message,
        Impact = AlarmImpact.Informational,
      };
    var unarchivedJobs = unarchived.Select(job => job.UniqueStr).ToImmutableHashSet();
    var retiring =
      from routes in db.LoadMazakManagedSchedules(config.ControllerSource).GroupBy(r => r.JobUnique)
      where !unarchivedJobs.Contains(routes.Key)
      let job = db.LoadJob(routes.Key)
      where job is { Archived: true } && Owns(config, job)
      from route in routes
      let alarm = RetirementConflict(data, job, route)
      where alarm != null
      select alarm;
    return active.Concat(retiring).ToImmutableList();
  }

  private static CurrentStatusAlarm? RetirementConflict(
    MazakAllData data,
    Job job,
    MazakManagedSchedule route
  )
  {
    try
    {
      Inspect(data, job, route);
      return null;
    }
    catch (ConflictRequestException conflict)
    {
      return new CurrentStatusAlarm
      {
        Code = "mazak-managed-retirement-conflict",
        Message = conflict.Message,
        Impact = AlarmImpact.Informational,
      };
    }
  }
}
