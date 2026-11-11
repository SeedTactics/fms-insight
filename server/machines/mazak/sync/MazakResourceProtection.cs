/* Copyright (c) 2026, SeedTactics */
#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

// An immutable reservation captured with the controller observation, before leaving the local
// mutation owner. Numbers are native Mazak coordinates; part names retain their full suffixes.
// Missing rows do not release reservations. Callers retain them until their evidence resolves.
public sealed record MazakResourceProtection
{
  public static readonly MazakResourceProtection Empty = new();

  public ImmutableHashSet<int> ScheduleIds { get; init; } = [];
  public ImmutableHashSet<string> PartNames { get; init; } =
    ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
  public ImmutableHashSet<int> PalletNumbers { get; init; } = [];
  public ImmutableHashSet<string> JobUniques { get; init; } =
    ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
  public ImmutableHashSet<string> ProgramNames { get; init; } =
    ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
  public ImmutableHashSet<string> FixtureNames { get; init; } =
    ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

  // Use when unresolved evidence cannot be scoped reliably. Existing definitions and job
  // intent are retained; additions may only use resources disjoint from those observations.
  public bool SuspendCleanup { get; init; }

  // Current physical work can outlive its definitions. Reserve only those broken associations;
  // ordinary intact assignments retain the established shared-pallet download behavior.
  public static MazakResourceProtection ForOrphanedAssignments(MazakAllData data)
  {
    var assignments = (data.PalletSubStatuses ?? [])
      .Where(assignment => assignment.FixQuantity > 0 && HasMissingDefinition(data, assignment))
      .ToImmutableList();
    return assignments.IsEmpty
      ? Empty
      : new MazakResourceProtection
      {
        PalletNumbers = assignments.Select(a => a.PalletNumber).ToImmutableHashSet(),
        ScheduleIds = assignments.Select(a => a.ScheduleID).ToImmutableHashSet(),
        PartNames = assignments
          .Select(a => a.PartName)
          .Where(name => !string.IsNullOrEmpty(name))
          .ToImmutableHashSet(StringComparer.Ordinal),
        FixtureNames = assignments
          .Select(a => a.FixtureName)
          .Where(name => !string.IsNullOrEmpty(name))
          .ToImmutableHashSet(StringComparer.Ordinal),
      };
  }

  private static bool HasMissingDefinition(MazakAllData data, MazakPalletSubStatusRow assignment)
  {
    var schedules = (data.Schedules ?? [])
      .Where(s => s.Id == assignment.ScheduleID)
      .Take(2)
      .ToImmutableList();
    if (
      schedules.Count != 1
      || !string.IsNullOrEmpty(assignment.PartName) && schedules[0].PartName != assignment.PartName
    )
      return true;
    var schedule = schedules[0];
    var parts = (data.Parts ?? [])
      .Where(p => p.PartName == schedule.PartName)
      .Take(2)
      .ToImmutableList();
    if (parts.Count != 1 || parts[0].Comment != schedule.Comment)
      return true;
    if (assignment.PartProcessNumber > 0)
    {
      var processes = parts[0]
        .Processes.Where(p => p.ProcessNumber == assignment.PartProcessNumber)
        .Take(2)
        .ToImmutableList();
      if (
        processes.Count != 1
        || !schedule.Processes.Any(p => p.ProcessNumber == assignment.PartProcessNumber)
        || !string.IsNullOrEmpty(assignment.FixtureName)
          && processes[0].Fixture != assignment.FixtureName
      )
        return true;
    }
    return !(data.Pallets ?? []).Any(p =>
      p.PalletNumber == assignment.PalletNumber
      && (string.IsNullOrEmpty(assignment.FixtureName) || p.Fixture == assignment.FixtureName)
    );
  }

  public MazakResourceProtection Union(MazakResourceProtection other) =>
    new()
    {
      ScheduleIds = ScheduleIds.Union(other.ScheduleIds),
      PartNames = PartNames.Union(other.PartNames),
      PalletNumbers = PalletNumbers.Union(other.PalletNumbers),
      JobUniques = JobUniques.Union(other.JobUniques),
      ProgramNames = ProgramNames.Union(other.ProgramNames),
      FixtureNames = FixtureNames.Union(other.FixtureNames),
      SuspendCleanup = SuspendCleanup || other.SuspendCleanup,
    };

  internal MazakProtectedResources Resolve(MazakAllData data)
  {
    if (
      !SuspendCleanup
      && ScheduleIds.IsEmpty
      && PartNames.IsEmpty
      && PalletNumbers.IsEmpty
      && JobUniques.IsEmpty
      && ProgramNames.IsEmpty
      && FixtureNames.IsEmpty
    )
      return new MazakProtectedResources
      {
        ScheduleIds = [],
        PartNames = [],
        PalletNumbers = [],
        FixtureNames = [],
        ProgramNames = [],
        JobUniques = [],
        SuspendCleanup = false,
      };
    var schedules = ScheduleIds;
    var parts = PartNames;
    var pallets = PalletNumbers;
    var jobs = JobUniques;
    var fixtures = FixtureNames;
    var programs = ProgramNames;
    if (SuspendCleanup)
    {
      schedules = schedules.Union(data.Schedules.Select(s => s.Id));
      parts = parts.Union(data.Parts.Select(p => p.PartName));
      pallets = pallets
        .Union(data.Pallets.Select(p => p.PalletNumber))
        .Union(data.PalletSubStatuses.Select(p => p.PalletNumber))
        .Union(
          data.LoadActions.Select(action => action.StationPallet)
            .OfType<MazakStationPallet>()
            .Select(p => p.PalletNumber)
        );
      fixtures = fixtures.Union(data.Fixtures.Select(f => f.FixtureName));
      programs = programs.Union(data.MainPrograms.Select(p => p.MainProgram));
    }
    while (true)
    {
      var previous = (schedules.Count, parts.Count, pallets.Count, fixtures.Count, jobs.Count);
      foreach (var assignment in data.PalletSubStatuses)
        if (
          pallets.Contains(assignment.PalletNumber)
          || schedules.Contains(assignment.ScheduleID)
          || parts.Contains(assignment.PartName)
          || fixtures.Contains(assignment.FixtureName)
        )
        {
          pallets = pallets.Add(assignment.PalletNumber);
          schedules = schedules.Add(assignment.ScheduleID);
          // Assignments retain their own full identities when schedule or pallet definitions
          // disappear. Follow those edges in both directions; absent definitions do not release
          // the surviving assignment or its part, fixture and program dependencies.
          if (!string.IsNullOrEmpty(assignment.PartName))
            parts = parts.Add(assignment.PartName);
          if (!string.IsNullOrEmpty(assignment.FixtureName))
            fixtures = fixtures.Add(assignment.FixtureName);
        }
      foreach (var schedule in data.Schedules)
      {
        var owner = MazakPart.ParseCommentInfo(schedule.Comment);
        if (
          schedules.Contains(schedule.Id)
          || parts.Contains(schedule.PartName)
          || owner.HasUnique && jobs.Contains(owner.Unique)
        )
        {
          schedules = schedules.Add(schedule.Id);
          parts = parts.Add(schedule.PartName);
          if (owner.HasUnique)
            jobs = jobs.Add(owner.Unique);
        }
      }
      foreach (var request in data.LoadActions)
        if (request.StationPallet is { } pallet && pallets.Contains(pallet.PalletNumber))
          parts = parts.Union(
            data.Parts.Where(p =>
                p.Comment == request.Comment
                && MazakPart.ExtractPartNameFromMazakPartName(p.PartName) == request.Part
              )
              .Select(p => p.PartName)
          );
      foreach (var part in data.Parts)
      {
        var owner = MazakPart.ParseCommentInfo(part.Comment);
        if (
          !parts.Contains(part.PartName)
          && !(owner.HasUnique && jobs.Contains(owner.Unique))
          && !part.Processes.Any(process => fixtures.Contains(process.Fixture))
        )
          continue;
        parts = parts.Add(part.PartName);
        if (owner.HasUnique)
          jobs = jobs.Add(owner.Unique);
        foreach (var process in part.Processes)
        {
          if (!string.IsNullOrEmpty(process.Fixture))
            fixtures = fixtures.Add(process.Fixture);
          if (!string.IsNullOrEmpty(process.MainProgram))
            programs = programs.Add(process.MainProgram);
        }
      }
      foreach (var pallet in data.Pallets)
        if (pallets.Contains(pallet.PalletNumber) || fixtures.Contains(pallet.Fixture))
        {
          pallets = pallets.Add(pallet.PalletNumber);
          fixtures = fixtures.Add(pallet.Fixture);
        }
      if (previous == (schedules.Count, parts.Count, pallets.Count, fixtures.Count, jobs.Count))
        break;
    }
    return new MazakProtectedResources
    {
      ScheduleIds = schedules,
      PartNames = parts,
      PalletNumbers = pallets,
      FixtureNames = fixtures,
      ProgramNames = programs,
      JobUniques = jobs,
      SuspendCleanup = SuspendCleanup,
    };
  }
}

internal sealed record MazakProtectedResources
{
  public required ImmutableHashSet<int> ScheduleIds { get; init; }
  public required ImmutableHashSet<string> PartNames { get; init; }
  public required ImmutableHashSet<int> PalletNumbers { get; init; }
  public required ImmutableHashSet<string> FixtureNames { get; init; }
  public required ImmutableHashSet<string> ProgramNames { get; init; }
  public required ImmutableHashSet<string> JobUniques { get; init; }
  public required bool SuspendCleanup { get; init; }

  public bool BlocksDownload(Job job, MazakConfig configuration) =>
    JobUniques.Contains(job.UniqueStr)
    || job.Processes.SelectMany(p => p.Paths)
      .SelectMany(p => p.PalletNums ?? [])
      .Select(configuration.InversePalletNumber)
      .Any(PalletNumbers.Contains);

  public void ValidateWrite(MazakWriteData write)
  {
    if (
      write.Schedules.Any(s => ScheduleIds.Contains(s.Id) || PartNames.Contains(s.PartName))
      || write.Parts.Any(p => PartNames.Contains(p.PartName))
      || write.Pallets.Any(p => PalletNumbers.Contains(p.PalletNumber))
      || write.Fixtures.Any(f => FixtureNames.Contains(f.FixtureName))
      || write.Programs.Any(p => ProgramNames.Contains(p.MainProgram))
    )
      throw new ConflictRequestException(
        "Mazak definitions are reserved by unresolved equipment evidence; wait for reconciliation before changing them."
      );
  }
}
