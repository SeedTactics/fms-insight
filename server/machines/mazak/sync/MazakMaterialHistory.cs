using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

public static class MazakMaterialHistory
{
  // Persisted event marker: keep its value compatible with existing removal history.
  public const string MissingMaterialReason = "MaterialMissingOnPallet";

  // A read-local snapshot, not a cache across translations or repository writes. Batch the
  // histories once rather than loading/deserializing a material's full log for every old event.
  public static ImmutableDictionary<long, long> LoadRemovalCounters(
    IRepository repository,
    IEnumerable<long> materialIds
  )
  {
    var ids = materialIds.Where(id => id >= 0).ToImmutableHashSet();
    if (ids.IsEmpty)
      return ImmutableDictionary<long, long>.Empty;
    return repository
      .GetLogForMaterial(ids, includeInvalidatedCycles: true)
      .Where(e => e.LogType == LogType.AddToQueue && e.Program == MissingMaterialReason)
      .SelectMany(e =>
        e.Material.Where(m => ids.Contains(m.MaterialID)).Select(m => (m.MaterialID, e.Counter))
      )
      .GroupBy(m => m.MaterialID)
      .ToImmutableDictionary(g => g.Key, g => g.Max(m => m.Counter));
  }

  // Removal remains a placement fact when queue progress is invalidated. A later actual
  // LOAD has a newer counter and establishes a new assignment normally.
  public static bool WasRemovedAfter(
    IReadOnlyDictionary<long, long> removalCounters,
    long material,
    long counter
  ) => removalCounters.TryGetValue(material, out var removedAt) && removedAt > counter;

  internal sealed record PalletPlacement(
    ImmutableDictionary<long, LogMaterial> Candidates,
    ImmutableDictionary<int, int> PhysicalQuantities,
    ImmutableHashSet<int> UncertainFaces
  )
  {
    public IEnumerable<LogMaterial> Verified =>
      Candidates.Values.Where(m => !UncertainFaces.Contains(m.Face));
  }

  // A raw unload can remove a physical piece without identifying which recorded serial left.
  // Keep possible identities for exact resolution, and never infer an identity from that face.
  // The sum of actual transfers can establish that the face is empty even without serials.
  internal static PalletPlacement ReplayPalletPlacement(
    IEnumerable<BlackMaple.MachineFramework.LogEntry> events,
    IReadOnlyDictionary<long, long> removals
  )
  {
    var candidates = ImmutableDictionary<long, LogMaterial>.Empty;
    var quantities = ImmutableDictionary<int, int>.Empty;
    var uncertain = ImmutableHashSet<int>.Empty;
    foreach (var entry in events.Where(e => !e.StartOfCycle).OrderBy(e => e.Counter))
    {
      if (entry.LogType == LogType.MachineCycle)
      {
        candidates = candidates.SetItems(
          entry
            .Material.Where(m =>
              !candidates.ContainsKey(m.MaterialID)
              && !uncertain.Contains(m.Face)
              && !WasRemovedAfter(removals, m.MaterialID, entry.Counter)
            )
            .Select(m => KeyValuePair.Create(m.MaterialID, m))
        );
        continue;
      }
      if (entry.LogType != LogType.LoadUnloadCycle || entry.Result is not ("LOAD" or "UNLOAD"))
        continue;

      var load = entry.Result == "LOAD";
      if (
        entry.ProgramDetails is { } details
        && details.ContainsKey(ManagedMazakSchedules.NativePartKey)
        && int.TryParse(
          details.GetValueOrDefault(ManagedMazakSchedules.NativeProcessKey),
          NumberStyles.None,
          CultureInfo.InvariantCulture,
          out var face
        )
        && face > 0
      )
      {
        var before = quantities.TryGetValue(face, out var previous)
          ? previous
          : candidates.Values.Count(m => m.Face == face);
        if (details.ContainsKey(ManagedMazakSchedules.RouteConflictKey))
        {
          // An unverified route cannot establish occupancy for a subsequent verified load.
          // If we already had material on the face, its identity can no longer be inferred.
          if (before > 0)
            uncertain = uncertain.Add(face);
          continue;
        }
        int.TryParse(
          details.GetValueOrDefault(ManagedMazakSchedules.UnidentifiedQuantityKey),
          NumberStyles.None,
          CultureInfo.InvariantCulture,
          out var unidentified
        );
        var identified = int.TryParse(
          details.GetValueOrDefault(ManagedMazakSchedules.IdentifiedQuantityKey),
          NumberStyles.None,
          CultureInfo.InvariantCulture,
          out var count
        )
          ? count
          : entry.Material.Count(m => m.MaterialID >= 0);
        var moved = identified + unidentified;
        var after = load ? before + moved : System.Math.Max(0, before - moved);
        if (after == 0)
        {
          quantities = quantities.Remove(face);
          candidates = candidates.RemoveRange(
            candidates.Values.Where(m => m.Face == face).Select(m => m.MaterialID)
          );
          uncertain = uncertain.Remove(face);
        }
        else
        {
          quantities = quantities.SetItem(face, after);
          if (unidentified > 0)
            uncertain = uncertain.Add(face);
        }
      }

      candidates = load
        ? candidates.SetItems(
          entry
            .Material.Where(m => !WasRemovedAfter(removals, m.MaterialID, entry.Counter))
            .Select(m => KeyValuePair.Create(m.MaterialID, m))
        )
        : candidates.RemoveRange(entry.Material.Select(m => m.MaterialID));
    }
    return new(candidates, quantities, uncertain);
  }
}
