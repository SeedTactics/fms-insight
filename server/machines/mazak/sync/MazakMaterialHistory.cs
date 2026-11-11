using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

public static class MazakMaterialHistory
{
  // Persisted event marker: keep its value compatible with existing removal history.
  public const string MissingMaterialReason = "MaterialMissingOnPallet";

  public static ImmutableList<LogMaterial> MaterialOnPallet(
    IRepository repository,
    IEnumerable<BlackMaple.MachineFramework.LogEntry> events
  )
  {
    var history = events.ToImmutableList();
    var removals = LoadRemovalCounters(
      repository,
      history.SelectMany(e => e.Material).Select(m => m.MaterialID)
    );
    return history
      .Where(e => !e.StartOfCycle && e.LogType is LogType.LoadUnloadCycle or LogType.MachineCycle)
      .OrderBy(e => e.Counter)
      .Aggregate(
        ImmutableDictionary<long, LogMaterial>.Empty,
        (current, e) =>
          e.Result == "LOAD" || e.LogType == LogType.MachineCycle
            ? current.SetItems(
              e.Material.Where(m =>
                  e.LogType != LogType.MachineCycle || !current.ContainsKey(m.MaterialID)
                )
                .Where(m => !WasRemovedAfter(removals, m.MaterialID, e.Counter))
                .Select(m => KeyValuePair.Create(m.MaterialID, m))
            )
          : e.Result == "UNLOAD" ? current.RemoveRange(e.Material.Select(m => m.MaterialID))
          : current
      )
      .Values.Where(m => !repository.IsMaterialInQueue(m.MaterialID))
      .ToImmutableList();
  }

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
}
