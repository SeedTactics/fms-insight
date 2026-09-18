using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public class MazakMaterialHistorySpec
{
  [Test]
  public async Task RemovalSnapshotBatchesDistinctIdsAndRefreshesOnlyOnANewRead()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var repository = config.OpenConnection();
    var first = repository.AllocateMaterialID("job", "part", 2);
    var second = repository.AllocateMaterialID("job", "part", 2);
    var query = Substitute.For<IRepository>();
    query
      .GetLogForMaterial(Arg.Any<IEnumerable<long>>(), true)
      .Returns(call =>
        repository.GetLogForMaterial(call.Arg<IEnumerable<long>>(), includeInvalidatedCycles: true)
      );
    var healthy = MazakMaterialHistory.LoadRemovalCounters(query, [first, second, first]);
    await Assert.That(healthy).IsEmpty();
    for (var counter = 0; counter < 20; counter++)
      await Assert.That(MazakMaterialHistory.WasRemovedAfter(healthy, first, counter)).IsFalse();
    query
      .Received(1)
      .GetLogForMaterial(
        Arg.Is<IEnumerable<long>>(ids =>
          ids.Count() == 2 && ids.Contains(first) && ids.Contains(second)
        ),
        true
      );
    query.DidNotReceive().GetLogForMaterial(Arg.Any<long>(), Arg.Any<bool>());

    repository.RecordAddMaterialToQueue(
      first,
      1,
      "removed",
      -1,
      null,
      MazakMaterialHistory.MissingMaterialReason
    );
    repository.RecordRemoveMaterialFromAllQueues(first, 1);
    var removal = repository
      .GetLogForMaterial(first)
      .Single(e => e.Program == MazakMaterialHistory.MissingMaterialReason);
    var observed = MazakMaterialHistory.LoadRemovalCounters(query, [first, second]);
    await Assert
      .That(MazakMaterialHistory.WasRemovedAfter(observed, first, removal.Counter - 1))
      .IsTrue();
    await Assert
      .That(MazakMaterialHistory.WasRemovedAfter(observed, first, removal.Counter))
      .IsFalse();
    await Assert
      .That(MazakMaterialHistory.WasRemovedAfter(observed, first, removal.Counter + 1))
      .IsFalse();
    await Assert.That(MazakMaterialHistory.WasRemovedAfter(observed, second, 0)).IsFalse();
    await Assert.That(healthy).IsEmpty();
    query.Received(2).GetLogForMaterial(Arg.Any<IEnumerable<long>>(), true);

    query.ClearReceivedCalls();
    await Assert.That(MazakMaterialHistory.LoadRemovalCounters(query, [])).IsEmpty();
    await Assert.That(query.ReceivedCalls()).IsEmpty();
  }
}
