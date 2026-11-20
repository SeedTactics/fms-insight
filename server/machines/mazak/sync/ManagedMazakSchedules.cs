#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

internal static partial class ManagedMazakSchedules
{
  internal const string NativePartKey = "mazak-native-part-name";
  internal const string NativeProcessKey = "mazak-native-process";
  internal const string IdentifiedQuantityKey = "mazak-identified-quantity";
  internal const string UnidentifiedQuantityKey = "mazak-unidentified-quantity";
  internal const string RouteConflictKey = "mazak-managed-route-conflict";

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
      throw new ConflictRequestException(
        $"Mazak job '{job.UniqueStr}' has conflicting readback for route '{comment}' and native part '{partName}'."
      );
    return (parts.SingleOrDefault(), schedules.SingleOrDefault());
  }

  public static (MazakPartRow? Part, MazakScheduleRow? Schedule) Inspect(
    MazakAllData data,
    Job job,
    MazakManagedSchedule binding
  )
  {
    var inspection = Inspect(
      data,
      job,
      binding.PartName,
      binding.Comment,
      binding.ConfirmedScheduleId
    );
    if (inspection.Part is { } part)
      VerifyInstalledProcesses(part, binding);
    return inspection;
  }

  private static void VerifyInstalledProcesses(MazakPartRow part, MazakManagedSchedule binding)
  {
    if (!ProcessDefinitions(part).SequenceEqual(binding.Processes))
      throw new ConflictRequestException(
        $"Mazak job '{binding.JobUnique}' has changed fixtures or programs for route '{binding.Comment}'."
      );
  }

  private static ImmutableList<MazakManagedProcess> ProcessDefinitions(MazakPartRow part) =>
    part
      .Processes.OrderBy(p => p.ProcessNumber)
      .Select(p => new MazakManagedProcess(p.ProcessNumber, p.Fixture, p.MainProgram))
      .ToImmutableList();

  internal static string? ExecutionConflict(
    MazakAllData data,
    MazakConfig config,
    Job job,
    MazakPartRow part
  )
  {
    var route = MazakPart.ParseCommentInfo(part.Comment);
    var download = MazakPart.ParseUID(part.PartName);
    var expectedPart = route.IsSplit
      ? new MazakPart(job, download, 0, route.FmsProcess, 1)
      : new MazakPart(job, download, 0);
    expectedPart.PartName = part.PartName;
    foreach (var process in part.Processes.OrderBy(p => p.ProcessNumber))
    {
      var jobProcess = route.JobProcessForMazakProcess(process.ProcessNumber);
      var path = job.Processes[jobProcess - 1].Paths[0];
      var stop = path.Stops.Last();
      var program = new ProgramRevision
      {
        ProgramName = stop.Program ?? "",
        Revision = stop.ProgramRevision ?? 0,
        CellControllerProgramName = process.MainProgram,
      };
      var expectedProcess = config.ProcessFromJob?.Invoke(
        expectedPart,
        process.ProcessNumber,
        1,
        program
      ) ?? new MazakProcessFromJob(expectedPart, process.ProcessNumber, 1, program);
      expectedPart.Processes.Add(expectedProcess);
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
      var job = db.LoadJob(MazakPart.UniqueFromComment(schedule.Comment));
      if (!Owns(config, job))
        continue;
      var saved = db.LoadMazakManagedSchedule(config.ControllerSource, schedule.PartName);
      if (
        saved is null
        && db.LoadMazakManagedSchedules(
            config.ControllerSource,
            includeRetired: true,
            jobUnique: job!.UniqueStr
          )
          .Any(binding => binding.Comment == schedule.Comment)
      )
        continue;
      if (saved is { ConfirmedScheduleId: not null } or { Retired: true })
        continue;
      try
      {
        var (part, row) = saved is null
          ? Inspect(observed, job!, schedule.PartName, schedule.Comment, null)
          : Inspect(observed, job!, saved);
        if (part is not null && row is not null)
          db.RecordMazakManagedSchedule(
            saved is null
              ? Definition(config, job!, part, row.Id, beforeLoadCounter: null)
              : saved with
              {
                ConfirmedScheduleId = row.Id,
              }
          );
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
      var (installed, schedule) = Inspect(observed, job, planned.PartName, planned.Comment, null);
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
    if (
      observed.Parts.FirstOrDefault(p =>
        p.PartName == planned.PartName && p.Comment == planned.Comment
      ) is
      { } installedPart
    )
    {
      VerifyInstalledProcesses(installedPart, saved);
      return;
    }
    var (_, row) = Inspect(observed, job, saved);
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
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    Job job,
    MazakScheduleRow schedule
  )
  {
    var saved = db.LoadMazakManagedSchedule(config.ControllerSource, schedule.PartName);
    if (
      saved is null
      || saved.Retired
      || saved.ConfirmedScheduleId.HasValue
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

  public static bool ConfirmedRouteUnavailable(
    MazakAllData data,
    Job job,
    MazakManagedSchedule route
  )
  {
    try
    {
      var (part, schedule) = Inspect(data, job, route);
      return part is null || schedule is null;
    }
    catch (ConflictRequestException)
    {
      return true;
    }
  }

  public static ImmutableList<CurrentStatusAlarm> Diagnostics(
    IRepository db,
    MazakConfig config,
    MazakAllData data
  )
  {
    if (config.UseInsightJobQuantities is null)
      return [];
    var bindings = db.LoadMazakManagedSchedules(config.ControllerSource).ToLookup(r => r.JobUnique);
    return db.LoadUnarchivedJobs()
      .Where(j => Owns(config, j) && (j.CopiedToSystem || bindings.Contains(j.UniqueStr)))
      .SelectMany(job =>
        MazakPart
          .InitialScheduleComments(job)
          .Select(comment =>
          {
            var route = bindings[job.UniqueStr].SingleOrDefault(r => r.Comment == comment);
            var unavailable = route?.ConfirmedScheduleId is not null
              ? ConfirmedRouteUnavailable(data, job, route)
              : job.CopiedToSystem
                || route is not null
                  && data.Schedules.Any(s =>
                    s.Comment == comment
                    || string.Equals(s.PartName, route.PartName, StringComparison.OrdinalIgnoreCase)
                  );
            if (unavailable)
              return $"Mazak controller '{config.ControllerSource}' has no verified schedule for job '{job.UniqueStr}', route '{comment}'. Check the controller before admitting material.";
            return route?.ConfirmedScheduleId is not null
              ? ExecutionConflict(
                data,
                config,
                job,
                data.Parts.Single(p => p.PartName == route.PartName)
              )
              : null;
          })
          .OfType<string>()
          .Select(message => new CurrentStatusAlarm
          {
            Code = "mazak-managed-schedule-unavailable",
            Message = message,
            Impact = AlarmImpact.Informational,
          })
      )
      .ToImmutableList();
  }
}
