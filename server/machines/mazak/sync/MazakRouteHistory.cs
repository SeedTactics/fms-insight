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

  internal sealed record AmbiguousPlacement
  {
    public required int Face { get; init; }
    public required long Quantity { get; init; }
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
        Quantity = quantity,
        Unidentified = Math.Max(0, quantity - onFace.Count),
        Candidates = candidates,
        Departed = departed,
      };
    }
    return result.ToImmutable();
  }

  // An incomplete physical batch or the loss of a native definition leaves the latest known
  // placement in force. New controller work with a reused numeric ID is independent evidence.
  internal static bool HasUnconfirmedPlacement(
    IRepository db,
    MazakConfig config,
    MazakAllData observed,
    IReadOnlyList<BlackMaple.MachineFramework.LogEntry> cycle,
    LogMaterial material
  )
  {
    if (!config.UseDurableRouteIdentity)
      return false;
    if (cycle.Any(e => e.ProgramDetails?.ContainsKey(UnidentifiedQuantityKey) == true))
      return true;
    if (string.IsNullOrEmpty(material.JobUniqueStr))
      return false;

    var placement = cycle.LastOrDefault(e =>
      e.LogType == LogType.LoadUnloadCycle
      && !e.StartOfCycle
      && e.Result == "LOAD"
      && e.Material.Any(m => m.MaterialID == material.MaterialID)
    );
    // Older confirmed loads may predate the exact native name in the manufacturing log.
    var definitions = db.LoadMazakRouteIdentities(config.ControllerSource, material.JobUniqueStr)
      .Where(r => r.JobProcess == material.Process)
      .ToArray();
    if (placement?.ProgramDetails?.TryGetValue(NativePartNameKey, out var name) == true)
      return !observed.Schedules.Any(s => s.PartName == name)
        || definitions.Any(r => r.PartName == name && !AgreesWithObservation(r, observed));

    return definitions.Length > 0
      && definitions.All(r =>
        !observed.Schedules.Any(s => s.PartName == r.PartName)
        || !AgreesWithObservation(r, observed)
      );
  }
}
