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
