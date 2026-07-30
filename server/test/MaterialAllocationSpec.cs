using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class MaterialAllocationSpec
{
  [Test]
  public async Task IdempotentBatchAllocationSurvivesRestartAndConcurrentRetries()
  {
    var databaseFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db");
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

    try
    {
      ImmutableList<MaterialDetails>[] allocations;
      using (
        var repositoryConfig = RepositoryConfig.InitializeEventDatabase(
          null,
          databaseFile,
          pooling: false
        )
      )
      {
        allocations = await Task.WhenAll(
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
      }

      using var restartedConfig = RepositoryConfig.InitializeEventDatabase(
        null,
        databaseFile,
        pooling: false
      );
      using var verify = restartedConfig.OpenConnection();
      var retry = verify.AllocateMaterialIDs(request, "raw-basket-work");

      await Assert
        .That(
          allocations
            .Append(retry)
            .All(allocation =>
              allocation.Select(material => material.MaterialID).SequenceEqual([1L, 2L])
              && allocation.All(material => material.Paths![1] == 3)
            )
        )
        .IsTrue();
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
    finally
    {
      if (File.Exists(databaseFile))
        File.Delete(databaseFile);
    }
  }

  [Test]
  public async Task IdempotentBatchAllocationRejectsDifferentItemAndPathBoundaries()
  {
    using var repositoryConfig = RepositoryConfig.InitializeMemoryDB(null);
    using var repository = repositoryConfig.OpenConnection();
    var first = ImmutableList.Create(
      new MaterialToAllocate
      {
        JobUnique = "a",
        PartName = "b",
        NumProcesses = 1,
        Paths = ImmutableDictionary<int, int>.Empty.Add(1, 2),
      },
      new MaterialToAllocate
      {
        JobUnique = "1",
        PartName = "1",
        NumProcesses = 1,
        Paths = ImmutableDictionary<int, int>.Empty,
      }
    );
    var second = ImmutableList.Create(
      first[0] with
      {
        Paths = ImmutableDictionary<int, int>.Empty,
      },
      first[1] with
      {
        PartName = "2",
        Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1),
      }
    );

    repository.AllocateMaterialIDs(first, "same-key");

    await Assert
      .That(() => repository.AllocateMaterialIDs(second, "same-key"))
      .Throws<ConflictRequestException>();
  }
}
