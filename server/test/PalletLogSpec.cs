using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class PalletLogSpec
{
  [Test]
  public async Task CurrentAndPreviousPalletLogExcludesOlderCycles()
  {
    using var repositoryConfig = RepositoryConfig.InitializeMemoryDB(null);
    using var repository = repositoryConfig.OpenConnection();
    var now = DateTime.UtcNow;
    repository.RecordGeneralMessage([], "old", "", pallet: 1, timeUTC: now);
    var previousBoundary = repository.RecordEmptyPallet(1, now.AddMinutes(1)).Single();
    var previousCycle = repository.RecordGeneralMessage(
      [],
      "previous",
      "",
      pallet: 1,
      timeUTC: now.AddMinutes(2)
    );
    var cycleEndBoundary = repository.RecordEmptyPallet(1, now.AddMinutes(3)).Single();
    var currentBoundary = repository.RecordEmptyPallet(1, now.AddMinutes(4)).Single();
    var currentCycle = repository.RecordGeneralMessage(
      [],
      "current",
      "",
      pallet: 1,
      timeUTC: now.AddMinutes(5)
    );
    repository.RecordGeneralMessage([], "other", "", pallet: 2, timeUTC: now.AddMinutes(6));

    var counters = repository
      .CurrentAndPreviousPalletLog(1)
      .Select(entry => entry.Counter)
      .ToImmutableList();

    await Assert
      .That(counters)
      .IsEquivalentTo([
        previousBoundary.Counter,
        previousCycle.Counter,
        cycleEndBoundary.Counter,
        currentBoundary.Counter,
        currentCycle.Counter,
      ]);
  }
}
