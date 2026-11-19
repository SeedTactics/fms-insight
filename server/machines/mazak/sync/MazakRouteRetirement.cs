#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

public sealed record MazakRetirementCandidates
{
  public required ImmutableHashSet<string> PartNames { get; init; }
  public required ImmutableHashSet<string> FixtureNames { get; init; }
  public required ImmutableHashSet<string> MainPrograms { get; init; }
}

public static class MazakRouteRetirement
{
  // Capture before deleting any part. Retain the candidates across retries, and recalculate
  // each phase using the controller owner's fresh, complete observation.
  public static MazakRetirementCandidates Capture(
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
    var candidates = Capture(observed, ownedPartNames);
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

  // The caller establishes retirement eligibility. The optional predicate can keep another
  // schedule on a shared definition. Surviving observations always protect their dependencies.
  public static MazakWriteData? Calculate(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    MazakRetirementCandidates candidates,
    Func<MazakScheduleRow, bool>? shouldRemoveSchedule = null
  )
  {
    var (scheduleDeletes, survivingParts) = BuildMazakSchedules.RemoveSchedules(observed, s =>
      candidates.PartNames.Contains(s.PartName) && shouldRemoveSchedule?.Invoke(s) != false
    );
    var busy = scheduleDeletes.Schedules.FirstOrDefault(s =>
      s.Processes.Count == 0
      || s.Processes.Any(p => p.ProcessMaterialQuantity != 0 || p.ProcessExecuteQuantity != 0)
      || observed.PalletSubStatuses.Any(p => p.ScheduleID == s.Id && p.FixQuantity > 0)
    );
    if (busy != null)
      throw new ConflictRequestException(
        $"Mazak schedule {busy.Id} can retire only after readback shows zero ready and executing material on every process and no material on a pallet."
      );
    if (scheduleDeletes.Schedules.Count > 0)
      return scheduleDeletes;

    var partDeletes = MazakJobs.DeletePartRows(observed.Parts, p =>
      candidates.PartNames.Contains(p.PartName)
      && !survivingParts.Contains(p.PartName)
      && !observed.PalletSubStatuses.Any(s => s.PartName == p.PartName && s.FixQuantity > 0)
    );
    if (partDeletes.Parts.Count > 0)
      return partDeletes;

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
      return pallets;

    var unused = RemoveUnusedDependencies(
      observed,
      candidates,
      protectedFixtures,
      RequiredProgramComments(observed, active)
    );
    if (unused.Fixtures.Count > 0 || unused.Programs.Count > 0)
      return unused;
    if (HasAllScheduledParts(observed))
      db.ReleaseMazakRoutePreparations(config.ControllerSource, finishedParts);
    return null;
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
