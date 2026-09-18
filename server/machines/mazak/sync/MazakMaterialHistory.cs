using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

public static class MazakMaterialHistory
{
  // Removal is a placement fact even if its queue progress is later invalidated. A later
  // actual LOAD has a newer counter and establishes a new pallet assignment normally.
  public static bool WasRemovedAfter(IRepository repository, long material, long counter) =>
    repository
      .GetLogForMaterial(material)
      .Any(e =>
        e.Counter > counter
        && e.LogType == LogType.AddToQueue
        && e.Program == "MaterialMissingOnPallet"
      );
}
