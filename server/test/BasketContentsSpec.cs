using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class BasketContentsSpec : IDisposable
{
  private readonly Guid _databaseId = Guid.NewGuid();
  private readonly RepositoryConfig _repositoryConfig;

  public BasketContentsSpec() =>
    _repositoryConfig = RepositoryConfig.InitializeMemoryDB(null, _databaseId);

  public void Dispose() => _repositoryConfig.Dispose();

  [Test]
  public async Task RecordsAndLoadsNamedBasketContentsByBasketId()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    var result = Contents(4, slot: 1, materialId, "tp-101");

    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = result,
        }
      ),
      idempotencyKey: "prepare-4"
    );
    var loaded = repository.GetBasketContents(4);

    await Assert.That(loaded).IsNotNull();
    await Assert.That(loaded!.BasketId).IsEqualTo(4);
    await Assert.That(loaded.Slots[1].Material.Single().MaterialID).IsEqualTo(materialId);
    await Assert.That(loaded.Slots[1].AdditionalData["transfer-plate-rfid"]).IsEqualTo("tp-101");
  }

  [Test]
  public async Task BasketStationOperationUpdatesContentsInItsManufacturingTransaction()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    var contents = Contents(4, slot: 1, materialId, "tp-101");
    repository.RecordAddMaterialToQueue(
      new EventLogMaterial
      {
        MaterialID = materialId,
        Process = 0,
        Face = 1,
      },
      "raw",
      -1,
      null,
      null
    );

    repository.RecordBasketStationOperation(
      StationPreparation(materialId, contents),
      lulNum: 1,
      totalElapsed: TimeSpan.Zero,
      timeUTC: DateTime.UtcNow,
      externalQueues: ImmutableDictionary<string, string>.Empty,
      idempotencyKey: "station-prepare-4"
    );

    var loaded = repository.GetBasketContents(4);
    await Assert.That(loaded).IsNotNull();
    await Assert.That(loaded!.Slots[1].Material.Single().MaterialID).IsEqualTo(materialId);
    await Assert.That(loaded.Slots[1].AdditionalData["transfer-plate-rfid"]).IsEqualTo("tp-101");
  }

  [Test]
  [Arguments(5, 1, false, false)]
  [Arguments(4, 2, false, false)]
  [Arguments(4, 1, true, false)]
  [Arguments(4, 1, false, true)]
  public async Task BasketStationTransfersMustExactlyMatchContentsDelta(
    int resultBasketId,
    int resultSlot,
    bool useDifferentMaterial,
    bool addUnloggedMaterial
  )
  {
    using var repository = _repositoryConfig.OpenConnection();
    var transferredMaterial = repository.AllocateMaterialID("job-1", "part-a", 2);
    var otherMaterial = repository.AllocateMaterialID("job-1", "part-a", 2);
    repository.RecordAddMaterialToQueue(
      new EventLogMaterial
      {
        MaterialID = transferredMaterial,
        Process = 0,
        Face = 1,
      },
      "raw",
      -1,
      null,
      null
    );
    var resultMaterial = useDifferentMaterial ? otherMaterial : transferredMaterial;
    var result = Contents(resultBasketId, resultSlot, resultMaterial, "tp-101");
    if (addUnloggedMaterial)
      result = result with
      {
        Slots = result.Slots.Add(
          2,
          new BasketSlotContents
          {
            Material = [new BasketMaterial { MaterialID = otherMaterial, Process = 0 }],
            AdditionalData = ImmutableSortedDictionary<string, string>.Empty,
          }
        ),
      };

    await Assert
      .That(() =>
        repository.RecordBasketStationOperation(
          StationPreparation(transferredMaterial, result, transferBasketId: 4, transferSlot: 1),
          lulNum: 1,
          totalElapsed: TimeSpan.Zero,
          timeUTC: DateTime.UtcNow,
          externalQueues: ImmutableDictionary<string, string>.Empty,
          idempotencyKey: Guid.NewGuid().ToString()
        )
      )
      .Throws<ArgumentException>();
    await Assert.That(repository.GetBasketContents(4)).IsNull();
    await Assert.That(repository.GetBasketContents(5)).IsNull();
  }

  [Test]
  public async Task BasketStationContentsCannotSilentlyRemoveMaterial()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var existingMaterial = repository.AllocateMaterialID("job-1", "part-a", 2);
    var loadedMaterial = repository.AllocateMaterialID("job-1", "part-a", 2);
    var before = Contents(4, 1, existingMaterial, "tp-101");
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = before,
        }
      ),
      "seed-4"
    );
    repository.RecordAddMaterialToQueue(
      new EventLogMaterial
      {
        MaterialID = loadedMaterial,
        Process = 0,
        Face = 2,
      },
      "raw",
      -1,
      null,
      null
    );

    await Assert
      .That(() =>
        repository.RecordBasketStationOperation(
          StationPreparation(
            loadedMaterial,
            Contents(4, 2, loadedMaterial, "tp-102"),
            expected: before,
            transferSlot: 2
          ),
          lulNum: 1,
          totalElapsed: TimeSpan.Zero,
          timeUTC: DateTime.UtcNow,
          externalQueues: ImmutableDictionary<string, string>.Empty,
          idempotencyKey: "replace-without-unload"
        )
      )
      .Throws<ArgumentException>();
    var persisted = repository.GetBasketContents(4);
    await Assert.That(persisted).IsNotNull();
    await Assert.That(persisted!.Slots.Keys).IsEquivalentTo([1]);
    await Assert.That(persisted.Slots[1].Material.Single().MaterialID).IsEqualTo(existingMaterial);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task EveryBasketTransferRequiresManufacturingProjectionChange(
    bool initializeProjection
  )
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    if (initializeProjection)
      repository.RecordBasketContentsOperation(
        Operation(
          new BasketContentsChange
          {
            BasketId = 4,
            Expected = null,
            Result = Empty(4),
          }
        ),
        "initialize-4"
      );
    repository.RecordAddMaterialToQueue(
      new EventLogMaterial
      {
        MaterialID = materialId,
        Process = 0,
        Face = 1,
      },
      "raw",
      -1,
      null,
      null
    );

    var operation = StationPreparation(materialId, Contents(4, 1, materialId, "tp-101")) with
    {
      ContentsChanges = [],
    };
    await Assert
      .That(() =>
        repository.RecordBasketStationOperation(
          operation,
          lulNum: 1,
          totalElapsed: TimeSpan.Zero,
          timeUTC: DateTime.UtcNow,
          externalQueues: ImmutableDictionary<string, string>.Empty,
          idempotencyKey: "missing-projection-change"
        )
      )
      .Throws<ArgumentException>();
    if (initializeProjection)
      await Assert.That(repository.GetBasketContents(4)).IsEqualTo(Empty(4));
    else
      await Assert.That(repository.GetBasketContents(4)).IsNull();
  }

  [Test]
  public async Task PalletTransfersUpdateBasketContentsInTheManufacturingTransaction()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    var empty = Empty(4);
    var occupied = Contents(4, 1, materialId, "tp-101", process: 1);
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = empty,
        }
      ),
      "initialize-4"
    );
    LoadMaterialOntoPallet(repository, materialId);

    var loadEvents = repository
      .RecordLoadUnloadComplete(
        toLoad: null,
        previouslyLoaded: null,
        toUnload:
        [
          new MaterialToUnloadFromFace
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              materialId,
              new UnloadDestination()
            ),
            FaceNum = 1,
            Process = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        previouslyUnloaded: null,
        lulNum: 1,
        pallet: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: DateTime.UtcNow,
        externalQueues: ImmutableDictionary<string, string>.Empty,
        palletBasketCompletion: PalletLoadOntoBasketCompletion(materialId, empty, occupied)
      )
      .ToImmutableList();

    await Assert.That(repository.GetBasketContents(4)!.Slots.Keys).IsEquivalentTo([1]);
    await Assert
      .That(repository.GetBasketContents(4)!.Slots[1].Material.Single().MaterialID)
      .IsEqualTo(materialId);
    await Assert.That(loadEvents.Any(log => log.LogType == LogType.BasketLoadUnload)).IsTrue();

    repository.RecordLoadUnloadComplete(
      toLoad:
      [
        new MaterialToLoadOntoFace
        {
          MaterialIDs = [materialId],
          Process = 1,
          Path = null,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      previouslyLoaded: null,
      toUnload: null,
      previouslyUnloaded: null,
      lulNum: 1,
      pallet: 2,
      totalElapsed: TimeSpan.Zero,
      timeUTC: DateTime.UtcNow,
      externalQueues: ImmutableDictionary<string, string>.Empty,
      palletBasketCompletion: new PalletBasketLoadUnloadCompletion
      {
        Transfers =
        [
          new PalletBasketTransfer.UnloadFromBasket
          {
            BasketId = 4,
            Material = [LogMaterial(materialId, process: 1, slot: 1)],
          },
        ],
        CycleBoundaries = [],
        ContentsChanges =
        [
          new BasketContentsChange
          {
            BasketId = 4,
            Expected = occupied,
            Result = empty,
          },
        ],
      }
    );

    await Assert.That(repository.GetBasketContents(4)!.Slots).IsEmpty();
  }

  [Test]
  public async Task PalletTransferRejectsContradictoryBasketContentsWithoutManufacturingEffects()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var transferredMaterial = repository.AllocateMaterialID("job-1", "part-a", 2);
    var otherMaterial = repository.AllocateMaterialID("job-1", "part-a", 2);
    var empty = Empty(4);
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = empty,
        }
      ),
      "initialize-4"
    );
    LoadMaterialOntoPallet(repository, transferredMaterial);

    await Assert
      .That(() =>
        repository.RecordLoadUnloadComplete(
          toLoad: null,
          previouslyLoaded: null,
          toUnload:
          [
            new MaterialToUnloadFromFace
            {
              MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
                transferredMaterial,
                new UnloadDestination()
              ),
              FaceNum = 1,
              Process = 1,
              ActiveOperationTime = TimeSpan.Zero,
            },
          ],
          previouslyUnloaded: null,
          lulNum: 1,
          pallet: 1,
          totalElapsed: TimeSpan.Zero,
          timeUTC: DateTime.UtcNow,
          externalQueues: ImmutableDictionary<string, string>.Empty,
          palletBasketCompletion: PalletLoadOntoBasketCompletion(
            transferredMaterial,
            empty,
            Contents(4, 1, otherMaterial, "tp-202", process: 1)
          )
        )
      )
      .Throws<ArgumentException>();

    await Assert.That(repository.GetBasketContents(4)!.Slots).IsEmpty();
    await Assert
      .That(repository.GetLogForMaterial(transferredMaterial).Any(log => log.Pallet == 4))
      .IsFalse();
  }

  [Test]
  public async Task PalletManufacturingEffectsRollBackWhenContentsUpdateFails()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    var empty = Empty(4);
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = empty,
        }
      ),
      "initialize-4"
    );
    LoadMaterialOntoPallet(repository, materialId);
    var eventCountBefore = repository.GetLogForMaterial(materialId).Count();
    using (
      var connection = new SqliteConnection(
        $"Data Source=file:${_databaseId}?mode=memory&cache=shared"
      )
    )
    {
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText =
        "CREATE TRIGGER fail_pallet_contents BEFORE INSERT ON current_basket_material "
        + "WHEN NEW.BasketId = 4 BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      command.ExecuteNonQuery();
    }

    await Assert
      .That(() =>
        repository.RecordLoadUnloadComplete(
          toLoad: null,
          previouslyLoaded: null,
          toUnload:
          [
            new MaterialToUnloadFromFace
            {
              MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
                materialId,
                new UnloadDestination()
              ),
              FaceNum = 1,
              Process = 1,
              ActiveOperationTime = TimeSpan.Zero,
            },
          ],
          previouslyUnloaded: null,
          lulNum: 1,
          pallet: 1,
          totalElapsed: TimeSpan.Zero,
          timeUTC: DateTime.UtcNow,
          externalQueues: ImmutableDictionary<string, string>.Empty,
          palletBasketCompletion: PalletLoadOntoBasketCompletion(
            materialId,
            empty,
            Contents(4, 1, materialId, "tp-101", process: 1)
          )
        )
      )
      .Throws<SqliteException>();

    await Assert.That(repository.GetBasketContents(4)!.Slots).IsEmpty();
    await Assert.That(repository.GetLogForMaterial(materialId).Count()).IsEqualTo(eventCountBefore);
  }

  [Test]
  public async Task BasketStationFailureRollsBackContentsChange()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var materialId = repository.AllocateMaterialID("job-1", "part-a", 2);
    repository.RecordAddMaterialToQueue(
      new EventLogMaterial
      {
        MaterialID = materialId,
        Process = 0,
        Face = 1,
      },
      "raw",
      -1,
      null,
      null
    );
    using (
      var connection = new SqliteConnection(
        $"Data Source=file:${_databaseId}?mode=memory&cache=shared"
      )
    )
    {
      connection.Open();
      using var command = connection.CreateCommand();
      command.CommandText =
        "CREATE TRIGGER fail_station_operation BEFORE INSERT ON basket_operations "
        + "WHEN NEW.OperationType = 'station' BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
      command.ExecuteNonQuery();
    }

    await Assert
      .That(() =>
        repository.RecordBasketStationOperation(
          StationPreparation(materialId, Contents(4, slot: 1, materialId, "tp-101")),
          lulNum: 1,
          totalElapsed: TimeSpan.Zero,
          timeUTC: DateTime.UtcNow,
          externalQueues: ImmutableDictionary<string, string>.Empty,
          idempotencyKey: "station-prepare-4"
        )
      )
      .Throws<SqliteException>();
    await Assert.That(repository.GetBasketContents(4)).IsNull();
    await Assert
      .That(repository.GetMaterialInAllQueues().Single().MaterialID)
      .IsEqualTo(materialId);
  }

  [Test]
  public async Task IdenticalRetrySucceedsAndChangedRetryConflicts()
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

    repository.RecordBasketContentsOperation(operation, "prepare-4");
    repository.RecordBasketContentsOperation(operation, "prepare-4");
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
          "bad-process"
        )
      )
      .Throws<ArgumentException>();
    await Assert.That(repository.GetBasketContents(4)).IsNull();
  }

  [Test]
  public async Task RequestedMaterialValidationUsesExistingPointLookupIndexes()
  {
    var databaseId = Guid.NewGuid();
    using var config = RepositoryConfig.InitializeMemoryDB(null, databaseId);
    using var connection = new SqliteConnection(
      $"Data Source=file:${databaseId}?mode=memory&cache=shared"
    );
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

  private static BasketContentsOperation Operation(params BasketContentsChange[] changes) =>
    new() { Changes = changes.ToImmutableList() };

  private static BasketStationOperation StationPreparation(
    long materialId,
    BasketContents result,
    BasketContents expected = null,
    int? transferBasketId = null,
    int transferSlot = 1
  ) =>
    new()
    {
      Transfers =
      [
        new BasketStationTransfer.LoadOntoBasket
        {
          BasketId = transferBasketId ?? result.BasketId,
          Material =
          [
            new EventLogMaterial
            {
              MaterialID = materialId,
              Process = 0,
              Face = transferSlot,
            },
          ],
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      CycleBoundaries = [],
      ContentsChanges =
      [
        new BasketContentsChange
        {
          BasketId = result.BasketId,
          Expected = expected,
          Result = result,
        },
      ],
    };

  private static BasketContents Empty(int basketId) =>
    new() { BasketId = basketId, Slots = ImmutableSortedDictionary<int, BasketSlotContents>.Empty };

  private static BasketContents Contents(
    int basketId,
    int slot,
    long materialId,
    string transferPlateRfid,
    int process = 0
  ) =>
    new()
    {
      BasketId = basketId,
      Slots = ImmutableSortedDictionary<int, BasketSlotContents>.Empty.Add(
        slot,
        new BasketSlotContents
        {
          Material = [new BasketMaterial { MaterialID = materialId, Process = process }],
          AdditionalData = ImmutableSortedDictionary<string, string>.Empty.Add(
            "transfer-plate-rfid",
            transferPlateRfid
          ),
        }
      ),
    };

  private static EventLogMaterial LogMaterial(long materialId, int process, int slot) =>
    new()
    {
      MaterialID = materialId,
      Process = process,
      Face = slot,
    };

  [Test]
  public async Task CollectionBoundaryCollisionConflictsForRetryAndExpectedContents()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var first = repository.AllocateMaterialID("job-1", "part-a", 2);
    var second = repository.AllocateMaterialID("job-1", "part-a", 2);
    var two = new BasketContents
    {
      BasketId = 4,
      Slots = ImmutableSortedDictionary<int, BasketSlotContents>.Empty.Add(
        1,
        new BasketSlotContents
        {
          Material =
          [
            new BasketMaterial { MaterialID = first, Process = 1 },
            new BasketMaterial { MaterialID = second, Process = 1 },
          ],
          AdditionalData = ImmutableSortedDictionary<string, string>.Empty,
        }
      ),
    };
    var one = two with
    {
      Slots = two.Slots.SetItem(
        1,
        two.Slots[1] with
        {
          Material = [two.Slots[1].Material[0]],
          AdditionalData = ImmutableSortedDictionary<string, string>.Empty.Add(
            second.ToString(),
            "1"
          ),
        }
      ),
    };
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = two,
        }
      ),
      "collision"
    );
    await Assert
      .That(() =>
        repository.RecordBasketContentsOperation(
          Operation(
            new BasketContentsChange
            {
              BasketId = 4,
              Expected = null,
              Result = one,
            }
          ),
          "collision"
        )
      )
      .Throws<ConflictRequestException>();
    await Assert
      .That(() =>
        repository.RecordBasketContentsOperation(
          Operation(
            new BasketContentsChange
            {
              BasketId = 4,
              Expected = one,
              Result = Empty(4),
            }
          ),
          "wrong-expectation"
        )
      )
      .Throws<ConflictRequestException>();
    var reordered = two with
    {
      Slots = two.Slots.SetItem(
        1,
        two.Slots[1] with
        {
          Material = two.Slots[1].Material.Reverse().ToImmutableList(),
        }
      ),
    };
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = reordered,
        }
      ),
      "collision"
    );
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = reordered,
          Result = Empty(4),
        }
      ),
      "correct-expectation"
    );
    await Assert.That(repository.GetBasketContents(4)!.Slots).IsEmpty();
  }

  [Test]
  public async Task StationReceiptPreservesContentsCollectionBoundaries()
  {
    using var repository = _repositoryConfig.OpenConnection();
    var first = repository.AllocateMaterialID("job-1", "part-a", 2);
    var second = repository.AllocateMaterialID("job-1", "part-a", 2);
    var id = repository.AllocateMaterialID("job-1", "part-a", 2);
    var retained = Contents(4, 2, first, "tp-retained");
    retained = retained with
    {
      Slots = retained.Slots.SetItem(
        2,
        retained.Slots[2] with
        {
          Material =
          [
            new BasketMaterial { MaterialID = first, Process = 1 },
            new BasketMaterial { MaterialID = second, Process = 1 },
          ],
          AdditionalData = ImmutableSortedDictionary<string, string>.Empty,
        }
      ),
    };
    var collision = retained with
    {
      Slots = retained.Slots.SetItem(
        2,
        retained.Slots[2] with
        {
          Material = [retained.Slots[2].Material[0]],
          AdditionalData = ImmutableSortedDictionary<string, string>.Empty.Add(
            second.ToString(),
            "1"
          ),
        }
      ),
    };
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = retained,
        }
      ),
      "retained"
    );
    var contents = Contents(4, 1, id, "tp-101") with
    {
      Slots = retained.Slots.Add(1, Contents(4, 1, id, "tp-101").Slots[1]),
    };
    var operation = StationPreparation(id, contents, retained);
    repository.RecordBasketStationOperation(
      operation,
      1,
      TimeSpan.Zero,
      DateTime.UtcNow,
      ImmutableDictionary<string, string>.Empty,
      idempotencyKey: "station-receipt"
    );
    var changed = operation with
    {
      ContentsChanges =
      [
        operation.ContentsChanges[0] with
        {
          Expected = collision,
          Result = contents with { Slots = contents.Slots.SetItem(2, collision.Slots[2]) },
        },
      ],
    };
    await Assert
      .That(() =>
        repository.RecordBasketStationOperation(
          changed,
          1,
          TimeSpan.Zero,
          DateTime.UtcNow,
          ImmutableDictionary<string, string>.Empty,
          idempotencyKey: "station-receipt"
        )
      )
      .Throws<ConflictRequestException>();
    var reordered = operation with
    {
      ContentsChanges =
      [
        operation.ContentsChanges[0] with
        {
          Result = contents with
          {
            Slots = contents.Slots.ToImmutableSortedDictionary(
              p => p.Key,
              p =>
                p.Value with
                {
                  AdditionalData = p.Value.AdditionalData.WithComparers(
                    StringComparer.OrdinalIgnoreCase
                  ),
                }
            ),
          },
        },
      ],
    };
    repository.RecordBasketStationOperation(
      reordered,
      1,
      TimeSpan.Zero,
      DateTime.UtcNow,
      ImmutableDictionary<string, string>.Empty,
      idempotencyKey: "station-receipt"
    );
  }

  [Test]
  [Arguments(true)]
  [Arguments(false)]
  public async Task OmittedPalletCounterpartRollsBackBasketTransfer(bool ontoBasket)
  {
    using var repository = _repositoryConfig.OpenConnection();
    var id = repository.AllocateMaterialID("job-1", "part-a", 2);
    var occupied = Contents(4, 1, id, "tp-101", process: 1);
    var before = ontoBasket ? Empty(4) : occupied;
    var after = ontoBasket ? occupied : Empty(4);
    repository.RecordBasketContentsOperation(
      Operation(
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = null,
          Result = before,
        }
      ),
      "initial"
    );
    var completion = new PalletBasketLoadUnloadCompletion
    {
      Transfers = ontoBasket
        ?
        [
          new PalletBasketTransfer.LoadOntoBasket
          {
            BasketId = 4,
            Material = [LogMaterial(id, 1, 1)],
          },
        ]
        :
        [
          new PalletBasketTransfer.UnloadFromBasket
          {
            BasketId = 4,
            Material = [LogMaterial(id, 1, 1)],
          },
        ],
      CycleBoundaries = [],
      ContentsChanges =
      [
        new BasketContentsChange
        {
          BasketId = 4,
          Expected = before,
          Result = after,
        },
      ],
    };
    var events = repository.GetLogForMaterial(id).ToImmutableList();
    await Assert
      .That(() =>
        repository.RecordPartialLoadUnload(
          toLoad: null,
          toUnload: null,
          lulNum: 1,
          pallet: 1,
          totalElapsed: TimeSpan.Zero,
          timeUTC: DateTime.UtcNow,
          externalQueues: ImmutableDictionary<string, string>.Empty,
          palletBasketCompletion: completion
        )
      )
      .Throws<ArgumentException>();
    await Assert.That(repository.GetLogForMaterial(id)).IsEquivalentTo(events);
    await Assert
      .That(repository.GetBasketContents(4)!.Slots.Keys)
      .IsEquivalentTo(before.Slots.Keys);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task OrdinaryNoQueueUnloadCanAccompanyBasketTransfer(bool includeBasket)
  {
    using var repository = _repositoryConfig.OpenConnection();
    var ordinary = repository.AllocateMaterialID("job-1", "part-a", 2);
    var basket = repository.AllocateMaterialID("job-1", "part-a", 2);
    LoadMaterialOntoPallet(repository, ordinary);
    LoadMaterialOntoPallet(repository, basket);
    var occupied = Contents(4, 1, basket, "tp-101", process: 1);
    var logs = repository
      .RecordLoadUnloadComplete(
        toLoad: null,
        previouslyLoaded: null,
        toUnload:
        [
          new MaterialToUnloadFromFace
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>
              .Empty.Add(ordinary, new UnloadDestination { Queue = null })
              .Add(basket, null),
            FaceNum = 1,
            Process = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        previouslyUnloaded: null,
        lulNum: 1,
        pallet: 1,
        totalElapsed: TimeSpan.Zero,
        timeUTC: DateTime.UtcNow,
        externalQueues: ImmutableDictionary<string, string>.Empty,
        palletBasketCompletion: includeBasket
          ? PalletLoadOntoBasketCompletion(basket, null, occupied)
          : null
      )
      .ToImmutableList();
    await Assert
      .That(
        logs.Where(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "UNLOAD")
          .SelectMany(e => e.Material)
          .Select(m => m.MaterialID)
      )
      .IsEquivalentTo([ordinary, basket]);
    await Assert.That(repository.GetBasketContents(4) is not null).IsEqualTo(includeBasket);
    await Assert.That(repository.GetMaterialInAllQueues()).IsEmpty();
  }

  private static PalletBasketLoadUnloadCompletion PalletLoadOntoBasketCompletion(
    long materialId,
    BasketContents expected,
    BasketContents result
  ) =>
    new()
    {
      Transfers =
      [
        new PalletBasketTransfer.LoadOntoBasket
        {
          BasketId = result.BasketId,
          Material = [LogMaterial(materialId, process: 1, slot: 1)],
        },
      ],
      CycleBoundaries = [],
      ContentsChanges =
      [
        new BasketContentsChange
        {
          BasketId = result.BasketId,
          Expected = expected,
          Result = result,
        },
      ],
    };

  private static void LoadMaterialOntoPallet(IRepository repository, long materialId)
  {
    repository.RecordAddMaterialToQueue(
      LogMaterial(materialId, process: 0, slot: 1),
      "raw",
      -1,
      null,
      null
    );
    repository.RecordLoadUnloadComplete(
      toLoad:
      [
        new MaterialToLoadOntoFace
        {
          MaterialIDs = [materialId],
          Process = 1,
          Path = null,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      previouslyLoaded: null,
      toUnload: null,
      previouslyUnloaded: null,
      lulNum: 1,
      pallet: 1,
      totalElapsed: TimeSpan.Zero,
      timeUTC: DateTime.UtcNow,
      externalQueues: ImmutableDictionary<string, string>.Empty
    );
  }

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
