#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

public sealed record MazakRetirementCandidates
{
  public string? ControllerSource { get; init; }
  public required ImmutableHashSet<string> PartNames { get; init; }
  public required ImmutableHashSet<string> FixtureNames { get; init; }
  public required ImmutableHashSet<string> MainPrograms { get; init; }
}

public enum MazakRetirementProgress
{
  WriteRequired,
  WaitingForReadback,
  Complete,
}

public sealed record MazakRetirementStep(
  MazakRetirementProgress Progress,
  MazakWriteData? NextWrite
);

public static class MazakRouteRetirement
{
  // Capture before deleting any part. Retain the candidates across retries, and recalculate
  // each phase using the controller owner's fresh, complete observation.
  internal static MazakRetirementCandidates Capture(
    MazakAllData observed,
    IReadOnlySet<string> ownedPartNames
  )
  {
    var processes = observed
      .Parts.Where(p => ownedPartNames.Contains(p.PartName))
      .SelectMany(p => p.Processes)
      .ToImmutableList();
    return new MazakRetirementCandidates
    {
      PartNames = ownedPartNames.ToImmutableHashSet(StringComparer.Ordinal),
      FixtureNames = processes
        .Select(p => p.Fixture)
        .Where(n => !string.IsNullOrEmpty(n))
        .ToImmutableHashSet(StringComparer.Ordinal),
      MainPrograms = processes
        .Select(p => p.MainProgram)
        .Where(n => !string.IsNullOrEmpty(n))
        .ToImmutableHashSet(StringComparer.Ordinal),
    };
  }

  // Include dependencies prepared before the part became visible, even after a restart.
  public static MazakRetirementCandidates Capture(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    IReadOnlySet<string> ownedPartNames
  )
  {
    VerifyOwnership(db, config, observed, ownedPartNames);
    var candidates = Capture(observed, ownedPartNames) with
    {
      ControllerSource = config.ControllerSource,
    };
    foreach (var name in ownedPartNames)
    {
      var prepared = db.LoadMazakRoutePreparation(config.ControllerSource, name, nativeProcess: 1);
      if (prepared == null)
        continue;
      var program = prepared.AddedProgram ?? prepared.RequiredProgram;
      candidates = candidates with
      {
        FixtureNames = candidates.FixtureNames.Add(prepared.FixtureName),
        MainPrograms =
          program == null ? candidates.MainPrograms : candidates.MainPrograms.Add(program),
      };
    }
    return candidates;
  }

  // Check the saved native identity before relying on readback for route decisions.
  public static ImmutableList<MazakScheduleRow> VerifyOwnership(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    IReadOnlySet<string> ownedPartNames
  ) =>
    ownedPartNames
      .Select(name =>
        db.LoadMazakRouteIdentity(config.ControllerSource, name, 1)
        ?? throw new ConflictRequestException(
          $"Native part '{name}' has no reserved route on Mazak controller '{config.ControllerSource}'."
        )
      )
      .Select(route => FindOwnedSchedule(db, config, observed, route))
      .OfType<MazakScheduleRow>()
      .ToImmutableList();

  internal static MazakScheduleRow? FindOwnedSchedule(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    MazakRouteIdentity route
  )
  {
    if (route.Source != config.ControllerSource)
      throw new ArgumentException(
        "The route belongs to a different Mazak controller.",
        nameof(route)
      );
    if (
      route.NativeProcess != 1
      || route.Path != 1
      || db.LoadMazakRouteIdentity(route.Source, route.PartName, route.NativeProcess) != route
    )
      throw new ConflictRequestException(
        $"Native part '{route.PartName}' has no matching reserved split route."
      );

    var comment = MazakPart.SplitComment(route.JobUnique, route.JobProcess, route.Path);
    bool MatchingName(string name) =>
      string.Equals(name, route.PartName, StringComparison.OrdinalIgnoreCase);
    bool Relevant(string name, string observedComment) =>
      MatchingName(name) || observedComment == comment;
    bool Owns(string name, string observedComment) =>
      name == route.PartName
      && observedComment == comment
      && MazakPart.IsSailPart(name, observedComment);
    ConflictRequestException Conflict(string name, string observedComment) =>
      new(
        $"Mazak job '{route.JobUnique}' has a conflicting definition for part '{name}'. Mazak reported the route comment '{observedComment}'."
      );

    var parts = observed
      .Parts.Where(p => Relevant(p.PartName, p.Comment))
      .Take(2)
      .ToImmutableList();
    foreach (var part in parts)
      if (
        parts.Count != 1
        || !Owns(part.PartName, part.Comment)
        || part.Processes is not [{ ProcessNumber: 1 }]
      )
        throw Conflict(part.PartName, part.Comment);
    var schedules = observed
      .Schedules.Where(s => Relevant(s.PartName, s.Comment))
      .Take(2)
      .ToImmutableList();
    foreach (var row in schedules)
      if (
        schedules.Count != 1
        || !Owns(row.PartName, row.Comment)
        || row.Processes is not [{ ProcessNumber: 1 }]
        || observed.Schedules.Count(s => s.Id == row.Id) != 1
      )
        throw Conflict(row.PartName, row.Comment);
    return schedules.SingleOrDefault();
  }

  // Remove a verified idle schedule and keep the native definition for future capacity.
  public static MazakWriteData? RemoveSchedule(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    MazakRouteIdentity route
  )
  {
    var row = FindOwnedSchedule(db, config, observed, route);
    if (row == null)
      return null;
    CheckIdle(observed, row);
    return BuildMazakSchedules.RemoveSchedules(observed, schedule => schedule.Id == row.Id).Item1;
  }

  // The caller establishes production eligibility. Recalculate after each write using fresh
  // readback from the same controller. A waiting step leaves the preparation reserved.
  public static MazakRetirementStep Calculate(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    MazakRetirementCandidates candidates
  )
  {
    if (candidates.ControllerSource != config.ControllerSource)
      throw new ArgumentException(
        "The retirement belongs to a different Mazak controller.",
        nameof(candidates)
      );
    var rows = VerifyOwnership(db, config, observed, candidates.PartNames);
    foreach (var row in rows)
      CheckIdle(observed, row);
    var scheduled = rows.Select(row => row.Id).ToImmutableHashSet();
    var (scheduleDeletes, survivingParts) = BuildMazakSchedules.RemoveSchedules(observed, s =>
      scheduled.Contains(s.Id)
    );
    if (scheduleDeletes.Schedules.Count > 0)
      return Write(scheduleDeletes);

    if (
      !HasAllScheduledParts(observed)
      || observed.PalletSubStatuses.Any(s =>
        s.FixQuantity > 0
        && candidates.PartNames.WithComparer(StringComparer.OrdinalIgnoreCase).Contains(s.PartName)
      )
    )
      return new(MazakRetirementProgress.WaitingForReadback, null);

    var partDeletes = MazakJobs.DeletePartRows(observed.Parts, p =>
      candidates.PartNames.Contains(p.PartName) && !survivingParts.Contains(p.PartName)
    );
    if (partDeletes.Parts.Count > 0)
      return Write(partDeletes);

    var referencedNames = observed
      .Parts.Select(p => p.PartName)
      .Concat(survivingParts)
      .Concat(observed.PalletSubStatuses.Where(s => s.FixQuantity > 0).Select(s => s.PartName));
    var finishedParts = candidates.PartNames.Except(referencedNames);
    var active = db.LoadMazakRoutePreparations(config.ControllerSource)
      .Where(p => !finishedParts.Contains(p.Key))
      .Select(p => p.Value)
      .ToImmutableList();

    var protectedFixtures = active.Select(p => p.FixtureName);
    var pallets = RemovePalletMappings(observed, candidates, protectedFixtures);
    if (pallets.Pallets.Count > 0)
      return Write(pallets);

    var unused = RemoveUnusedDependencies(
      observed,
      candidates,
      protectedFixtures,
      RequiredProgramComments(observed, active)
    );
    if (unused.Fixtures.Count > 0 || unused.Programs.Count > 0)
      return Write(unused);
    db.ReleaseMazakRoutePreparations(config.ControllerSource, finishedParts);
    return new(MazakRetirementProgress.Complete, null);
  }

  private static MazakRetirementStep Write(MazakWriteData data) =>
    new(MazakRetirementProgress.WriteRequired, data);

  private static void CheckIdle(MazakAllData observed, MazakScheduleRow row)
  {
    if (
      row.Processes.Any(p => p.ProcessMaterialQuantity != 0 || p.ProcessExecuteQuantity != 0)
      || observed.PalletSubStatuses.Any(p =>
        p.FixQuantity > 0
        && (
          p.ScheduleID == row.Id
          || string.Equals(p.PartName, row.PartName, StringComparison.OrdinalIgnoreCase)
        )
      )
    )
      throw new ConflictRequestException(
        $"Mazak schedule {row.Id} can retire only after readback shows zero ready and executing material on every process and no material on a pallet."
      );
  }

  internal static IEnumerable<string> RequiredProgramComments(
    MazakAllData observed,
    IEnumerable<MazakRoutePreparation> preparations
  )
  {
    var names = preparations
      .Select(p => p.RequiredProgram ?? p.AddedProgram)
      .Where(p => p != null)
      .ToImmutableHashSet(StringComparer.Ordinal);
    return observed.MainPrograms.Where(p => names.Contains(p.MainProgram)).Select(p => p.Comment);
  }

  internal static MazakWriteData RemovePalletMappings(
    MazakAllData observed,
    MazakRetirementCandidates candidates,
    IEnumerable<string>? additionalFixtures = null
  )
  {
    if (!HasAllScheduledParts(observed))
      return new MazakWriteData();

    var used = UsedFixtureNames(observed).Union(additionalFixtures ?? []);
    return MazakJobs.DeletePalletRows(observed.Pallets, p =>
      candidates.FixtureNames.Contains(p.Fixture) && !used.Contains(p.Fixture)
    );
  }

  internal static MazakWriteData RemoveUnusedDependencies(
    MazakAllData observed,
    MazakRetirementCandidates candidates,
    IEnumerable<string>? additionalFixtures = null,
    IEnumerable<string>? additionalProgramComments = null
  )
  {
    if (!HasAllScheduledParts(observed))
      return new MazakWriteData();

    var programsInUse = observed
      .Parts.SelectMany(p => p.Processes)
      .Select(p => p.MainProgram)
      .ToImmutableHashSet(StringComparer.Ordinal);
    var deletions = new MazakJobs
    {
      OldMazakData = observed,
      UsedFixtures = UsedFixtureNames(observed)
        .Union(observed.Pallets.Select(p => p.Fixture))
        .Union(additionalFixtures ?? []),
      UsedMainProgramComments = observed
        .MainPrograms.Where(p => programsInUse.Contains(p.MainProgram))
        .Select(p => p.Comment)
        .Concat(additionalProgramComments ?? [])
        .ToHashSet(StringComparer.Ordinal),
    }.DeleteFixtureAndProgramDatabaseRows();
    return deletions with
    {
      Fixtures = deletions
        .Fixtures.Where(f => candidates.FixtureNames.Contains(f.FixtureName))
        .ToImmutableList(),
      Programs = deletions
        .Programs.Where(p => candidates.MainPrograms.Contains(p.MainProgram))
        .ToImmutableList(),
    };
  }

  private static ImmutableHashSet<string> UsedFixtureNames(MazakAllData observed) =>
    observed
      .Parts.SelectMany(p => p.Processes)
      .Select(p => p.Fixture)
      .Concat(observed.PalletSubStatuses.Where(s => s.FixQuantity > 0).Select(s => s.FixtureName))
      .ToImmutableHashSet(StringComparer.Ordinal);

  internal static bool HasAllScheduledParts(MazakAllData observed)
  {
    var parts = observed.Parts.Select(p => p.PartName).ToImmutableHashSet(StringComparer.Ordinal);
    return observed.Schedules.All(s => parts.Contains(s.PartName));
  }
}
