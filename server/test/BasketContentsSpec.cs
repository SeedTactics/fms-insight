using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class BasketContentsSpec : IDisposable
{
  private readonly RepositoryConfig _repositoryConfig = RepositoryConfig.InitializeMemoryDB(null);

  public void Dispose() => _repositoryConfig.Dispose();

  [Test]
  public async Task RecordsAndLoadsNamedBasketContentsByBasketId()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    var result = Contents(4, slot: 1, materialId, "tp-101");

    var logs = repository
      .RecordBasketContentsOperation(
        Operation(
          new BasketContentsChange
          {
            BasketId = 4,
            Expected = null,
            Result = result,
          }
        ),
        locationNum: 1,
        DateTime.UtcNow,
        idempotencyKey: "prepare-4"
      )
      .ToImmutableList();
    var loaded = repository.GetBasketContents(4);

    await Assert.That(logs).Count().IsEqualTo(1);
    await Assert.That(logs[0].LogType).IsEqualTo(LogType.BasketContentSnapshot);
    await Assert.That(loaded).IsNotNull();
    await Assert.That(loaded!.BasketId).IsEqualTo(4);
    await Assert.That(loaded.Slots[1].Material.Single().MaterialID).IsEqualTo(materialId);
    await Assert.That(loaded.Slots[1].AdditionalData["transfer-plate-rfid"]).IsEqualTo("tp-101");
  }

  [Test]
  public async Task IdenticalRetryReturnsOriginalEventAndChangedRetryConflicts()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    var operation = Operation(
      new BasketContentsChange
      {
        BasketId = 4,
        Expected = null,
        Result = Contents(4, 1, materialId, "tp-101"),
      }
    );

    var first = repository
      .RecordBasketContentsOperation(operation, 1, DateTime.UtcNow, "prepare-4")
      .Single();
    var retry = repository
      .RecordBasketContentsOperation(operation, 1, DateTime.UtcNow.AddHours(1), "prepare-4")
      .Single();

    await Assert.That(retry.Counter).IsEqualTo(first.Counter);
    await Assert
      .That(() =>
        repository.RecordBasketContentsOperation(
          operation with
          {
            Changes =
            [
              operation.Changes[0] with
              {
                Result = Contents(4, 1, materialId, "tp-different"),
              },
            ],
          },
          1,
          DateTime.UtcNow,
          "prepare-4"
        )
      )
      .Throws<ConflictRequestException>();
  }

  [Test]
  public async Task OneOperationCanAtomicallyMoveMaterialBetweenNamedBaskets()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    var basketFour = Contents(4, 1, materialId, "tp-101");
    var basketFive = Empty(5);
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = basketFour,
        },
        new BasketContentsChange
        {
          BasketId = 5,
          Expected = null,
          Result = basketFive,
        }
      ),
      1,
      DateTime.UtcNow,
      "seed"
    );

    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = basketFour,
          Result = Empty(4),
        },
        new BasketContentsChange
        {
          BasketId = 5,
          Expected = basketFive,
          Result = Contents(5, 2, materialId, "tp-101"),
        }
      ),
      1,
      DateTime.UtcNow,
      "exchange"
    );

    await Assert.That(repository.GetBasketContents(4)!.Slots).IsEmpty();
    await Assert
      .That(repository.GetBasketContents(5)!.Slots[2].Material.Single().MaterialID)
      .IsEqualTo(materialId);
  }

  [Test]
  public async Task HigherNumberedBasketCanAtomicallyMoveMaterialToLowerNumberedBasket()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    var basketFour = Empty(4);
    var basketFive = Contents(5, 1, materialId, "tp-101");
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = basketFour,
        },
        new BasketContentsChange
        {
          BasketId = 5,
          Expected = null,
          Result = basketFive,
        }
      ),
      1,
      DateTime.UtcNow,
      "seed"
    );

    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = basketFour,
          Result = Contents(4, 1, materialId, "tp-101"),
        },
        new BasketContentsChange
        {
          BasketId = 5,
          Expected = basketFive,
          Result = Empty(5),
        }
      ),
      1,
      DateTime.UtcNow,
      "reverse-transfer"
    );

    await Assert
      .That(repository.GetBasketContents(4)!.Slots[1].Material.Single().MaterialID)
      .IsEqualTo(materialId);
    await Assert.That(repository.GetBasketContents(5)!.Slots).IsEmpty();
  }

  [Test]
  public async Task OccupiedBasketsCanAtomicallyExchangeMaterial()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialA = repository.AllocateMaterialID("job-1", "part-a", 2);
    var materialB = repository.AllocateMaterialID("job-2", "part-b", 2);
    var basketFour = Contents(4, 1, materialA, "tp-a");
    var basketFive = Contents(5, 2, materialB, "tp-b");
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = basketFour,
        },
        new BasketContentsChange
        {
          BasketId = 5,
          Expected = null,
          Result = basketFive,
        }
      ),
      1,
      DateTime.UtcNow,
      "seed"
    );

    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = basketFour,
          Result = Contents(4, 2, materialB, "tp-b"),
        },
        new BasketContentsChange
        {
          BasketId = 5,
          Expected = basketFive,
          Result = Contents(5, 1, materialA, "tp-a"),
        }
      ),
      1,
      DateTime.UtcNow,
      "exchange"
    );

    await Assert
      .That(repository.GetBasketContents(4)!.Slots[2].Material.Single().MaterialID)
      .IsEqualTo(materialB);
    await Assert
      .That(repository.GetBasketContents(5)!.Slots[1].Material.Single().MaterialID)
      .IsEqualTo(materialA);
  }

  [Test]
  public async Task ExpectedContentsConflictRollsBackEveryBasketChange()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var firstMaterial = repository.AllocateMaterialID("job-1", "part-a", 2);
    var secondMaterial = repository.AllocateMaterialID("job-1", "part-a", 2);
    var basketFour = Contents(4, 1, firstMaterial, "tp-101");
    var basketFive = Contents(5, 1, secondMaterial, "tp-102");
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = basketFour,
        },
        new BasketContentsChange
        {
          BasketId = 5,
          Expected = null,
          Result = basketFive,
        }
      ),
      1,
      DateTime.UtcNow,
      "seed"
    );

    await Assert
      .That(() =>
        repository.RecordBasketContentsOperation(
          Operation(
            new BasketContentsChange
            {
              BasketId = 4,
              Expected = Empty(4),
              Result = Empty(4),
            },
            new BasketContentsChange
            {
              BasketId = 5,
              Expected = basketFive,
              Result = Empty(5),
            }
          ),
          1,
          DateTime.UtcNow,
          "conflict"
        )
      )
      .Throws<ConflictRequestException>();
    await Assert
      .That(repository.GetBasketContents(4)!.Slots[1].Material.Single().MaterialID)
      .IsEqualTo(firstMaterial);
    await Assert
      .That(repository.GetBasketContents(5)!.Slots[1].Material.Single().MaterialID)
      .IsEqualTo(secondMaterial);
  }

  [Test]
  public async Task ProjectionSurvivesRepositoryRecreation()
  {
    long materialId;
    using (var repository = _repositoryConfig.OpenConnection())
    {
      materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
      repository.RecordBasketContentsOperation(
        Operation(
          new BasketContentsChange
          {
            BasketId = 4,
            Expected = null,
            Result = Contents(4, 1, materialId, "tp-101"),
          }
        ),
        1,
        DateTime.UtcNow,
        "prepare-4"
      );
    }

    using var recreated = _repositoryConfig.OpenConnection();
    await Assert
      .That(recreated.GetBasketContents(4)!.Slots[1].Material.Single().MaterialID)
      .IsEqualTo(materialId);
  }

  [Test]
  public async Task RejectsUnknownOrAlreadyOwnedMaterialWithoutChangingProjection()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = Contents(4, 1, materialId, "tp-101"),
        }
      ),
      1,
      DateTime.UtcNow,
      "seed"
    );

    await Assert
      .That(() =>
        repository.RecordBasketContentsOperation(
          Operation(
            new BasketContentsChange
            {
              BasketId = 5,
              Expected = null,
              Result = Contents(5, 1, materialId, "tp-101"),
            }
          ),
          1,
          DateTime.UtcNow,
          "duplicate-owner"
        )
      )
      .Throws<ConflictRequestException>();
    await Assert
      .That(() =>
        repository.RecordBasketContentsOperation(
          Operation(
            new BasketContentsChange
            {
              BasketId = 6,
              Expected = null,
              Result = Contents(6, 1, MaterialId.MaxValue, "tp-unknown"),
            }
          ),
          1,
          DateTime.UtcNow,
          "unknown-material"
        )
      )
      .Throws<ArgumentException>();
    await Assert.That(repository.GetBasketContents(5)).IsNull();
    await Assert.That(repository.GetBasketContents(6)).IsNull();
    await Assert
      .That(repository.GetBasketContents(4)!.Slots[1].Material.Single().MaterialID)
      .IsEqualTo(materialId);
  }

  [Test]
  public async Task RejectsMaterialProcessBeyondAllocatedProcessCount()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    var original = Contents(4, 1, materialId, "tp-101");
    var invalid = original with
    {
      Slots = original.Slots.SetItem(
        1,
        original.Slots[1] with
        {
          Material = [new BasketMaterial { MaterialID = materialId, Process = 3 }],
        }
      ),
    };

    await Assert
      .That(() =>
        repository.RecordBasketContentsOperation(
          Operation(
            new BasketContentsChange
            {
              BasketId = 4,
              Expected = null,
              Result = invalid,
            }
          ),
          1,
          DateTime.UtcNow,
          "bad-process"
        )
      )
      .Throws<ArgumentException>();
    await Assert.That(repository.GetBasketContents(4)).IsNull();
  }

  [Test]
  public async Task RequestedMaterialValidationUsesExistingPointLookupIndexes()
  {
    var databaseFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db");
    try
    {
      using var config = RepositoryConfig.InitializeEventDatabase(
        null,
        databaseFile,
        pooling: false
      );
      using var connection = new SqliteConnection("Data Source=" + databaseFile);
      connection.Open();

      var allocationPlan = QueryPlan(
        connection,
        "SELECT NumProcesses FROM matdetails WHERE MaterialID = 42"
      );
      var ownershipPlan = QueryPlan(
        connection,
        "SELECT BasketId FROM current_basket_material WHERE MaterialID = 42"
      );

      await Assert.That(allocationPlan).Contains("USING INTEGER PRIMARY KEY");
      await Assert.That(ownershipPlan).Contains("sqlite_autoindex_current_basket_material_");
      await Assert.That(ownershipPlan).Contains("(MaterialID=?)");
      await Assert.That(ownershipPlan).DoesNotContain("SCAN current_basket_material");
    }
    finally
    {
      if (File.Exists(databaseFile))
        File.Delete(databaseFile);
    }
  }

  private static BasketContentsOperation Operation(params BasketContentsChange[] changes) =>
    new() { Changes = changes.ToImmutableList() };

  private static BasketContents Empty(int basketId) =>
    new() { BasketId = basketId, Slots = ImmutableSortedDictionary<int, BasketSlotContents>.Empty };

  private static BasketContents Contents(
    int basketId,
    int slot,
    long materialId,
    string transferPlateRfid
  ) =>
    new()
    {
      BasketId = basketId,
      Slots = ImmutableSortedDictionary<int, BasketSlotContents>.Empty.Add(
        slot,
        new BasketSlotContents
        {
          Material = [new BasketMaterial { MaterialID = materialId, Process = 0 }],
          AdditionalData = ImmutableSortedDictionary<string, string>.Empty.Add(
            "transfer-plate-rfid",
            transferPlateRfid
          ),
        }
      ),
    };

  private static string QueryPlan(SqliteConnection connection, string sql)
  {
    using var command = connection.CreateCommand();
    command.CommandText = "EXPLAIN QUERY PLAN " + sql;
    using var reader = command.ExecuteReader();
    var details = ImmutableList.CreateBuilder<string>();
    while (reader.Read())
      details.Add(reader.GetString(3));
    return string.Join("\n", details);
  }
}
