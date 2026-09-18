using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

public static class MazakMaterialHistory
{
  // Queue progress may be invalidated without undoing physical removal. Loads and the
  // established serial correction operation can subsequently establish another assignment.
  public static bool WasRemovedAfter(
    IRepository repository,
    long material,
    long counter,
    int pallet
  )
  {
    var history = repository.GetLogForMaterial(material).ToList();
    var removal = history.LastOrDefault(e =>
      e.Counter > counter
      && e.LogType == LogType.AddToQueue
      && e.Program == "MaterialMissingOnPallet"
    );
    if (removal is null)
      return false;
    var swaps = history
      .Where(e =>
        e.Counter > removal.Counter
        && e.LogType == LogType.SwapMaterialOnPallet
        && e.Pallet == pallet
      )
      .ToList();
    if (swaps.Count == 0)
      return true;

    // A serial correction rewrites the current assignment's event counters in place.
    // Only a correction in this same pallet cycle can supersede removal for this event;
    // a swap in another cycle must not revive this older assignment. Read pallet boundaries
    // too, since an empty cycle is absent from this material's history.
    var boundaries = repository
      .GetRecentLog(counter)
      .Where(e => e.Pallet == pallet && e.LogType == LogType.PalletCycle)
      .ToList();
    return !swaps.Any(swap => !boundaries.Any(b => b.Counter < swap.Counter));
  }
}
