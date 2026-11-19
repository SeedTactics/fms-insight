#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

internal static class MazakRouteHistory
{
  private static readonly Serilog.ILogger Log = Serilog.Log.ForContext(typeof(MazakRouteHistory));

  internal const string PlacementAlarmCode = "mazak-pallet-placement-unconfirmed";
  internal const string NativePartNameKey = "mazak-native-part-name";
  internal const string AmbiguousUnloadKey = "mazak-unidentified-unload-ambiguous";
  internal const string UnidentifiedQuantityKey = "mazak-unidentified-quantity";

  internal const string ObservedEmptyProgram = "MazakPalletObservedEmpty";

  internal static List<BlackMaple.MachineFramework.LogEntry> CurrentPlacement(
    IRepository db,
    MazakConfig config,
    int pallet
  )
  {
    var events = db.CurrentPalletLog(pallet);
    if (!config.UseDurableRouteIdentity)
      return events;
    var boundary = events.FindLastIndex(e =>
      e.LogType == LogType.GeneralMessage && e.Program == ObservedEmptyProgram
    );
    return boundary < 0 ? events : events.GetRange(boundary + 1, events.Count - boundary - 1);
  }

  internal static MazakRouteIdentity? Find(
    IRepository db,
    MazakConfig config,
    string? partName,
    int nativeProcess
  ) =>
    config.UseDurableRouteIdentity && !string.IsNullOrEmpty(partName)
      ? db.LoadMazakRouteIdentity(config.ControllerSource, partName, nativeProcess)
      : null;

  internal static bool AgreesWithObservation(MazakRouteIdentity route, MazakAllData observed)
  {
    var comment = MazakPart.SplitComment(route.JobUnique, route.JobProcess, route.Path);
    var parts = observed.Parts.Where(p => SameName(p.PartName)).Take(2).ToArray();
    if (
      parts.Length <= 1
      && parts.All(p =>
        p.PartName == route.PartName
        && p.Comment == comment
        && p.Processes.Count == 1
        && p.Processes[0].ProcessNumber == route.NativeProcess
      )
      && observed
        .Schedules.Where(s => SameName(s.PartName))
        .All(s => s.PartName == route.PartName && s.Comment == comment)
    )
      return true;

    Log.Error("Native part or schedule conflicts with reserved Mazak route {@Route}", route);
    return false;

    bool SameName(string name) =>
      string.Equals(name, route.PartName, StringComparison.OrdinalIgnoreCase);
  }

  internal static ImmutableDictionary<string, long> UnidentifiedOnPallet(
    IEnumerable<BlackMaple.MachineFramework.LogEntry> cycle
  )
  {
    var quantities = ImmutableDictionary.CreateBuilder<string, long>(StringComparer.Ordinal);
    foreach (var e in cycle)
    {
      if (
        e.LogType != LogType.LoadUnloadCycle
        || e.StartOfCycle
        || e.ProgramDetails?.TryGetValue(NativePartNameKey, out var name) != true
        || string.IsNullOrEmpty(name)
        || !e.ProgramDetails.TryGetValue(UnidentifiedQuantityKey, out var quantity)
        || !int.TryParse(quantity, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
        || count <= 0
      )
        continue;
      if (e.Result == "LOAD")
        quantities[name] = quantities.GetValueOrDefault(name) + count;
      else if (
        e.Result == "UNLOAD"
        && !e.ProgramDetails.ContainsKey(AmbiguousUnloadKey)
        && quantities.TryGetValue(name, out var previous)
      )
        quantities[name] = Math.Max(0, previous - count);
    }
    return quantities.Where(p => p.Value > 0).ToImmutableDictionary(StringComparer.Ordinal);
  }

  internal static bool HasUnidentifiedMaterial(
    IEnumerable<BlackMaple.MachineFramework.LogEntry> cycle,
    IReadOnlyDictionary<string, AmbiguousPlacement> ambiguous
  ) =>
    UnidentifiedOnPallet(cycle).Keys.Any(name => !ambiguous.ContainsKey(name))
    || ambiguous.Values.Any(p => p.Unidentified > 0);

  internal static ImmutableHashSet<string> UnresolvedRoutes(
    IEnumerable<BlackMaple.MachineFramework.LogEntry> cycle,
    IReadOnlyDictionary<string, AmbiguousPlacement> ambiguous
  ) =>
    UnidentifiedOnPallet(cycle)
      .Keys.Where(name => !ambiguous.ContainsKey(name))
      .Concat(
        ambiguous
          .Where(p => p.Value.Unidentified > 0 || p.Value.Candidates.Except(p.Value.Departed).Any())
          .Select(p => p.Key)
      )
      .ToImmutableHashSet(StringComparer.Ordinal);

  internal static bool AllPlacementSchedulesRetired(
    IRepository db,
    MazakConfig config,
    IReadOnlyList<BlackMaple.MachineFramework.LogEntry> cycle,
    IEnumerable<LogMaterial> material,
    IReadOnlySet<string> unresolvedRoutes,
    IEnumerable<MazakScheduleRow> schedules
  )
  {
    var observedSchedules = schedules.ToImmutableList();
    var live = observedSchedules
      .Select(s => s.PartName)
      .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    var liveSplitComments = observedSchedules
      .Select(s => s.Comment)
      .Where(MazakPart.IsSplitComment)
      .ToImmutableHashSet(StringComparer.Ordinal);
    return unresolvedRoutes.All(name => Find(db, config, name, 1) != null && !live.Contains(name))
      && material.All(m =>
      {
        var definitions = db.LoadMazakRouteIdentities(config.ControllerSource, m.JobUniqueStr)
          .Where(r =>
            r.JobProcess == m.Process
            && r.NativeProcess == m.Face
            && (m.Path == null || m.Path == r.Path)
          );
        var recorded = RecordedNativeName(cycle, m.MaterialID);
        var candidates = definitions
          .Where(r => recorded == null || r.PartName == recorded)
          .ToImmutableList();
        return !candidates.IsEmpty
          && candidates.All(r =>
            !live.Contains(r.PartName)
            && (
              recorded != null
              || !liveSplitComments.Contains(
                MazakPart.SplitComment(r.JobUnique, r.JobProcess, r.Path)
              )
            )
          );
      });
  }

  internal sealed record AmbiguousPlacement
  {
    public required int Face { get; init; }
    public required long Unidentified { get; init; }
    public required ImmutableHashSet<long> Candidates { get; init; }
    public required ImmutableHashSet<long> Departed { get; init; }
  }

  // Unload quantities are physical evidence even when no serial can be credited. Replay the
  // affected routes independently of material attribution. A later identified load starts a new
  // placement for that serial, while an empty face can still have unattributed prior unloads.
  internal static ImmutableDictionary<string, AmbiguousPlacement> AmbiguousPlacements(
    IRepository db,
    MazakConfig config,
    IEnumerable<BlackMaple.MachineFramework.LogEntry> cycle
  )
  {
    var result = ImmutableDictionary.CreateBuilder<string, AmbiguousPlacement>(
      StringComparer.Ordinal
    );
    if (!config.UseDurableRouteIdentity)
      return result.ToImmutable();
    var names = cycle
      .Where(e => e.ProgramDetails?.ContainsKey(AmbiguousUnloadKey) == true)
      .Select(e => e.ProgramDetails!.GetValueOrDefault(NativePartNameKey))
      .Where(n => !string.IsNullOrEmpty(n))
      .Distinct(StringComparer.Ordinal);
    foreach (var name in names)
    {
      var route = Find(db, config, name, 1);
      if (route == null)
        continue;
      long quantity = 0;
      var onFace = ImmutableHashSet<long>.Empty;
      var candidates = ImmutableHashSet<long>.Empty;
      var departed = ImmutableHashSet<long>.Empty;
      foreach (var e in cycle)
      {
        if (e.StartOfCycle || e.LogType is not (LogType.LoadUnloadCycle or LogType.MachineCycle))
          continue;
        var stamp = e.ProgramDetails?.GetValueOrDefault(NativePartNameKey);
        if (stamp != null && stamp != name)
          continue;
        var ids = e
          .Material.Where(m =>
            m.JobUniqueStr == route.JobUnique
            && m.Process == route.JobProcess
            && m.Face == route.NativeProcess
            && m.MaterialID >= 0
          )
          .Select(m => m.MaterialID)
          .ToImmutableHashSet();
        var unidentified =
          stamp == name
          && long.TryParse(
            e.ProgramDetails?.GetValueOrDefault(UnidentifiedQuantityKey),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var count
          )
            ? count
            : 0;
        if (e.Result == "LOAD")
        {
          quantity += ids.Except(onFace.Except(candidates)).Count + unidentified;
          onFace = onFace.Union(ids);
          candidates = candidates.Except(ids);
          departed = departed.Except(ids);
        }
        else if (e.LogType == LogType.MachineCycle)
        {
          var newIds = ids.Except(onFace).Except(departed);
          quantity += newIds.Count;
          onFace = onFace.Union(newIds);
        }
        else if (e.Result == "UNLOAD" && (!ids.IsEmpty || stamp == name))
        {
          quantity = Math.Max(0, quantity - ids.Count - unidentified);
          onFace = onFace.Except(ids);
          candidates = candidates.Except(ids);
          departed = departed.Except(ids);
          if (e.ProgramDetails?.ContainsKey(AmbiguousUnloadKey) == true)
            candidates = candidates.Union(onFace);
          if (quantity == 0)
          {
            departed = departed.Union(candidates);
            onFace = onFace.Clear();
          }
        }
      }
      result[route.PartName] = new AmbiguousPlacement
      {
        Face = route.NativeProcess,
        Unidentified = Math.Max(0, quantity - onFace.Count),
        Candidates = candidates,
        Departed = departed,
      };
    }
    return result.ToImmutable();
  }

  internal static bool MatchesRecordedPlacement(
    IEnumerable<BlackMaple.MachineFramework.LogEntry> cycle,
    IReadOnlyCollection<MazakRouteIdentity> definitions,
    LogMaterial material,
    MazakRouteIdentity liveRoute
  )
  {
    var nativeName = RecordedNativeName(cycle, material.MaterialID);
    return nativeName != null ? nativeName == liveRoute.PartName : definitions.Count == 1;
  }

  internal static string? RecordedNativeName(
    IEnumerable<BlackMaple.MachineFramework.LogEntry> cycle,
    long materialId
  ) =>
    cycle
      .LastOrDefault(e =>
        e.LogType == LogType.LoadUnloadCycle
        && !e.StartOfCycle
        && e.Result == "LOAD"
        && e.Material.Any(m => m.MaterialID == materialId)
      )
      ?.ProgramDetails?.GetValueOrDefault(NativePartNameKey);
}
