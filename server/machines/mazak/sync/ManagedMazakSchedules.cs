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
  internal const string UnidentifiedQuantityKey = "mazak-unidentified-quantity";

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

  public static void ConfirmObserved(IRepository db, MazakConfig config, MazakAllData observed)
  {
    if (config.UseInsightJobQuantities is null)
      return;
    var bindings = db.LoadMazakManagedSchedules(config.ControllerSource, includeRetired: true)
      .ToLookup(r => r.Comment);
    foreach (var schedule in observed.Schedules)
    {
      if (!MazakPart.IsSailPart(schedule.PartName, schedule.Comment))
        continue;
      var job = db.LoadJob(MazakPart.UniqueFromComment(schedule.Comment));
      if (!Owns(config, job))
        continue;
      var saved = db.LoadMazakManagedSchedule(config.ControllerSource, schedule.PartName);
      if (saved is null && bindings.Contains(schedule.Comment))
        continue;
      if (saved is { ConfirmedScheduleId: not null } or { Retired: true })
        continue;
      try
      {
        var (part, row) = Inspect(observed, job!, schedule.PartName, schedule.Comment, null);
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

  public static void RecordDownload(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    Job job,
    MazakScheduleRow schedule
  )
  {
    var (part, _) = Inspect(observed, job, schedule.PartName, schedule.Comment, null);
    if (part is null)
      throw new ConflictRequestException(
        $"Mazak job '{job.UniqueStr}' has no verified native part."
      );
    var saved = db.LoadMazakManagedSchedule(config.ControllerSource, part.PartName);
    if (saved is null)
      db.RecordMazakManagedSchedule(Definition(config, job, part, null, db.MaxLogCounter()));
    else if (
      saved.JobUnique != job.UniqueStr
      || saved.Comment != part.Comment
      || saved.PartName != part.PartName
      || saved.Retired
      || saved.ConfirmedScheduleId.HasValue
    )
      throw new ConflictRequestException(
        $"Native part '{part.PartName}' already has a reserved lifecycle."
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
      Fixtures = part
        .Processes.Select(p => p.Fixture)
        .Where(s => !string.IsNullOrEmpty(s))
        .ToImmutableHashSet(),
      Programs = part
        .Processes.Select(p => p.MainProgram)
        .Where(s => !string.IsNullOrEmpty(s))
        .ToImmutableHashSet(),
    };

  public static bool ConfirmedRouteMissing(MazakAllData data, Job job, MazakManagedSchedule route)
  {
    try
    {
      var (part, schedule) = Inspect(
        data,
        job,
        route.PartName,
        route.Comment,
        route.ConfirmedScheduleId
      );
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
          .Where(comment =>
          {
            var route = bindings[job.UniqueStr].SingleOrDefault(r => r.Comment == comment);
            return route?.ConfirmedScheduleId is not null
              ? ConfirmedRouteMissing(data, job, route)
              : job.CopiedToSystem;
          })
          .Select(comment => new CurrentStatusAlarm
          {
            Code = "mazak-managed-schedule-unavailable",
            Message =
              $"Mazak controller '{config.ControllerSource}' has no verified schedule for job '{job.UniqueStr}', route '{comment}'. Check the controller before admitting material.",
            Impact = AlarmImpact.Informational,
          })
      )
      .ToImmutableList();
  }
}
