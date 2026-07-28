using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class MaterialAllocationSpec
{
  [Test]
  public async Task IdempotentBatchAllocationSurvivesRestartAndConcurrentRetries()
  {
    using var repositoryConfig = RepositoryConfig.InitializeMemoryDB(null);
    var request = ImmutableList.Create(
      new MaterialToAllocate
      {
        JobUnique = "job",
        PartName = "part",
        NumProcesses = 2,
        Paths = ImmutableDictionary<int, int>.Empty.Add(1, 3),
      },
      new MaterialToAllocate
      {
        JobUnique = "job",
        PartName = "part",
        NumProcesses = 2,
        Paths = ImmutableDictionary<int, int>.Empty.Add(1, 3),
      }
    );

    ImmutableList<MaterialDetails> allocated;
    using (var repository = repositoryConfig.OpenConnection())
      allocated = repository.AllocateMaterialIDs(request, "raw-basket-work");

    var retries = await Task.WhenAll(
      Enumerable
        .Range(0, 8)
        .Select(_ =>
          Task.Run(() =>
          {
            using var repository = repositoryConfig.OpenConnection();
            return repository.AllocateMaterialIDs(request, "raw-basket-work");
          })
        )
    );

    await Assert.That(allocated.Select(material => material.MaterialID)).IsEquivalentTo([1L, 2L]);
    await Assert
      .That(
        retries.All(retry =>
          retry.Select(material => material.MaterialID).SequenceEqual([1L, 2L])
          && retry.All(material => material.Paths![1] == 3)
        )
      )
      .IsTrue();
    using var verify = repositoryConfig.OpenConnection();
    await Assert.That(verify.AllocateMaterialID("next", "part", 1)).IsEqualTo(3L);
    await Assert
      .That(() =>
        verify.AllocateMaterialIDs(
          request.SetItem(1, request[1] with { PartName = "changed" }),
          "raw-basket-work"
        )
      )
      .Throws<ConflictRequestException>();
  }
}
