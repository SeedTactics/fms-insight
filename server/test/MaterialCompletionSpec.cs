using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;

namespace BlackMaple.FMSInsight.Tests;

public sealed class MaterialCompletionSpec : IDisposable
{
  private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
  private readonly Guid _databaseId = Guid.NewGuid();
  private readonly RepositoryConfig _config;
  private readonly IRepository _repository;

  public MaterialCompletionSpec()
  {
    _config = RepositoryConfig.InitializeMemoryDB(null, _databaseId);
    _repository = _config.OpenConnection();
    _repository.UpdateCachedWorkorders([
      new Workorder
      {
        WorkorderId = "order",
        Part = "part",
        Quantity = 4,
        DueDate = Now,
        Priority = 1,
      },
    ]);
    foreach (var id in new[] { 1L, 2L, 3L, 4L })
    {
      _repository.CreateMaterialID(id, "job", "part", 2);
      _repository.RecordWorkorderForMaterialID(id, 0, "order");
    }
  }

  public void Dispose()
  {
    _repository.Dispose();
    _config.Dispose();
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task MixedPalletDestinationsRecordPerMaterialCompletion(bool partial)
  {
    var unloads = ImmutableList.Create(
      new MaterialToUnloadFromFace
      {
        MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>
          .Empty.Add(1, null)
          .Add(2, new() { Queue = "output" })
          .Add(3, new()),
        FaceNum = 1,
        Process = 2,
        ActiveOperationTime = TimeSpan.Zero,
      }
    );
    Initialize(Empty(4));
    var basket = new PalletBasketLoadUnloadCompletion
    {
      Transfers =
      [
        new PalletBasketTransfer.LoadOntoBasket { BasketId = 4, Material = Material(3, 2) },
      ],
      CycleBoundaries = [],
      ContentsChanges = [Change(Empty(4), Contents(4, 3, 2))],
    };
    var logs = (
      partial
        ? _repository.RecordPartialLoadUnload(
          null,
          unloads,
          1,
          5,
          TimeSpan.FromMinutes(1),
          Now,
          null,
          basket
        )
        : _repository.RecordLoadUnloadComplete(
          null,
          null,
          unloads,
          null,
          1,
          5,
          TimeSpan.FromMinutes(1),
          Now,
          null,
          basket
        )
    ).ToImmutableList();
    var unload = logs.Single(entry =>
      entry.LogType == LogType.LoadUnloadCycle && entry.Result == "UNLOAD"
    );
    await Assert.That(unload.ProgramDetails["MaterialCompleted:1"]).IsEqualTo("True");
    await Assert.That(unload.ProgramDetails["MaterialCompleted:2"]).IsEqualTo("False");
    await Assert.That(unload.ProgramDetails["MaterialCompleted:3"]).IsEqualTo("False");
    await Assert.That(_repository.GetActiveWorkorders().Single().CompletedQuantity).IsEqualTo(1);
    await Assert
      .That(CompletedHistory().Select(entry => entry.Counter))
      .IsEquivalentTo(_repository.GetLogForMaterial(1).Select(entry => entry.Counter));
    // This API still reports all completed pallet unloading, including internal transfers.
    await Assert.That(_repository.CompletedUnloadsSince(0).Single().Material.Count).IsEqualTo(3);
  }

  [Test]
  public async Task BasketPalletHandoffsAreInternalUntilTerminalBasketUnload()
  {
    Initialize(Contents(4, 1, 2));
    var loaded = new MaterialToLoadOntoFace
    {
      MaterialIDs = [1],
      Process = 2,
      Path = 1,
      FaceNum = 1,
      ActiveOperationTime = TimeSpan.Zero,
    };
    _repository.RecordLoadUnloadComplete(
      [loaded],
      null,
      null,
      null,
      1,
      5,
      TimeSpan.Zero,
      Now,
      null,
      new PalletBasketLoadUnloadCompletion
      {
        Transfers =
        [
          new PalletBasketTransfer.UnloadFromBasket { BasketId = 4, Material = Material(1, 2) },
        ],
        CycleBoundaries = [],
        ContentsChanges = [Change(Contents(4, 1, 2), Empty(4))],
      }
    );
    await Assert.That(CompletedHistory()).IsEmpty();
    await Assert.That(_repository.GetActiveWorkorders().Single().CompletedQuantity).IsEqualTo(0);
    _repository.RecordLoadUnloadComplete(
      null,
      null,
      [
        new MaterialToUnloadFromFace
        {
          MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
            1,
            new()
          ),
          Process = 2,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      null,
      1,
      5,
      TimeSpan.Zero,
      Now.AddMinutes(1),
      null,
      new PalletBasketLoadUnloadCompletion
      {
        Transfers =
        [
          new PalletBasketTransfer.LoadOntoBasket { BasketId = 4, Material = Material(1, 2) },
        ],
        CycleBoundaries = [],
        ContentsChanges = [Change(Empty(4), Contents(4, 1, 2))],
      }
    );
    await Assert.That(CompletedHistory()).IsEmpty();
    await Assert.That(_repository.GetActiveWorkorders().Single().CompletedQuantity).IsEqualTo(0);

    var operation = new BasketStationOperation
    {
      Transfers =
      [
        new BasketStationTransfer.UnloadFromBasket
        {
          BasketId = 4,
          Material = Material(1, 2),
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      CycleBoundaries = [],
      ContentsChanges = [Change(Contents(4, 1, 2), Empty(4))],
    };
    var final = _repository
      .RecordBasketStationOperation(
        operation,
        1,
        TimeSpan.FromMinutes(1),
        Now.AddMinutes(2),
        null,
        "exit"
      )
      .ToImmutableList();
    await Assert.That(final.Single().ProgramDetails["MaterialCompleted:1"]).IsEqualTo("True");
    await Assert.That(_repository.GetActiveWorkorders().Single().CompletedQuantity).IsEqualTo(1);
    await Assert.That(CompletedHistory()).IsNotEmpty();
    await Assert.That(_repository.GetLogOfAllCompletedParts(Now, Now.AddMinutes(1))).IsEmpty();
    // Once committed, retry cannot reclassify the exit using changed material details.
    _repository.SetDetailsForMaterialID(1, "job", "part", 3);
    var retry = _repository.RecordBasketStationOperation(
      operation,
      1,
      TimeSpan.FromMinutes(1),
      Now.AddMinutes(3),
      null,
      "exit"
    );
    await Assert
      .That(retry.Select(entry => entry.Counter))
      .IsEquivalentTo(final.Select(entry => entry.Counter));
    await Assert.That(_repository.GetActiveWorkorders().Single().CompletedQuantity).IsEqualTo(1);
    await Assert
      .That(() =>
        _repository.RecordBasketStationOperation(
          operation,
          2,
          TimeSpan.FromMinutes(1),
          Now,
          null,
          "exit"
        )
      )
      .Throws<ConflictRequestException>();
  }

  [Test]
  [Arguments("queue")]
  [Arguments("direct")]
  [Arguments("nonfinal")]
  [Arguments("terminal")]
  public async Task BasketStationRecordsDispositionFromTheWholeOperation(string disposition)
  {
    var material = Material(1, disposition == "nonfinal" ? 1 : 2);
    var contents = Contents(4, 1, material[0].Process);
    Initialize(contents);
    Initialize(Empty(7));
    var unload = new BasketStationTransfer.UnloadFromBasket
    {
      BasketId = 4,
      Material = material,
      ActiveOperationTime = TimeSpan.Zero,
      DestinationQueue = disposition == "queue" ? "output" : null,
    };
    var operation = new BasketStationOperation
    {
      Transfers =
        disposition == "direct"
          ?
          [
            unload,
            new BasketStationTransfer.LoadOntoBasket
            {
              BasketId = 7,
              Material = material,
              ActiveOperationTime = TimeSpan.Zero,
            },
          ]
          : [unload],
      CycleBoundaries = [],
      ContentsChanges =
        disposition == "direct"
          ? [Change(contents, Empty(4)), Change(Empty(7), Contents(7, 1, material[0].Process))]
          : [Change(contents, Empty(4))],
    };
    var logs = _repository
      .RecordBasketStationOperation(operation, 1, TimeSpan.Zero, Now, null, "station")
      .ToImmutableList();
    await Assert
      .That(logs.Single(entry => entry.Result == "UNLOAD").ProgramDetails["MaterialCompleted:1"])
      .IsEqualTo((disposition == "terminal").ToString());
    await Assert
      .That(_repository.GetActiveWorkorders().Single().CompletedQuantity)
      .IsEqualTo(disposition == "terminal" ? 1 : 0);
    await Assert.That(CompletedHistory().Any()).IsEqualTo(disposition == "terminal");
  }

  [Test]
  public async Task UnmarkedHistoricalPalletUnloadsRetainLegacyAccounting()
  {
    _repository.RecordLoadUnloadComplete(
      null,
      null,
      [
        new MaterialToUnloadFromFace
        {
          MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(1, null),
          Process = 2,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      null,
      1,
      5,
      TimeSpan.Zero,
      Now,
      null
    );
    using var connection = new SqliteConnection(
      $"Data Source=file:${_databaseId}?mode=memory&cache=shared"
    );
    connection.Open();
    using var command = connection.CreateCommand();
    // Simulate an existing pre-disposition event; no historical rewrite or migration is needed.
    command.CommandText = "DELETE FROM program_details WHERE Key LIKE 'MaterialCompleted:%'";
    command.ExecuteNonQuery();
    await Assert.That(_repository.GetActiveWorkorders().Single().CompletedQuantity).IsEqualTo(1);
    await Assert.That(CompletedHistory()).IsNotEmpty();
  }

  [Test]
  public async Task FailedBasketProjectionCannotPartiallyRecordCompletion()
  {
    var contents = Contents(4, 1, 2);
    Initialize(contents);
    var operation = new BasketStationOperation
    {
      Transfers =
      [
        new BasketStationTransfer.UnloadFromBasket
        {
          BasketId = 4,
          Material = Material(1, 2),
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      CycleBoundaries = [],
      ContentsChanges =
      [
        Change(
          contents with
          {
            Slots = contents.Slots.SetItem(
              1,
              contents.Slots[1] with
              {
                AdditionalData = ImmutableSortedDictionary<string, string>.Empty.Add(
                  "tool",
                  "wrong"
                ),
              }
            ),
          },
          Empty(4)
        ),
      ],
    };
    var count = _repository.GetLogForMaterial(1).Count();
    await Assert
      .That(() =>
        _repository.RecordBasketStationOperation(operation, 1, TimeSpan.Zero, Now, null, "exit")
      )
      .Throws<ConflictRequestException>();
    await Assert.That(_repository.GetLogForMaterial(1).Count()).IsEqualTo(count);
    await Assert.That(CompletedHistory()).IsEmpty();
    await Assert.That(_repository.GetActiveWorkorders().Single().CompletedQuantity).IsEqualTo(0);
    await Assert.That(_repository.GetBasketContents(4)).IsEquivalentTo(contents);
    _repository.RecordBasketStationOperation(
      operation with
      {
        ContentsChanges = [Change(contents, Empty(4))],
      },
      1,
      TimeSpan.Zero,
      Now,
      null,
      "exit"
    );
    await Assert.That(_repository.GetActiveWorkorders().Single().CompletedQuantity).IsEqualTo(1);
  }

  [Test]
  public async Task PalletUnloadReloadInOneTransactionIsInternal()
  {
    var logs = _repository
      .RecordLoadUnloadComplete(
        [
          new MaterialToLoadOntoFace
          {
            MaterialIDs = [1],
            Process = 2,
            Path = 1,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        null,
        [
          new MaterialToUnloadFromFace
          {
            MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
              1,
              null
            ),
            Process = 2,
            FaceNum = 1,
            ActiveOperationTime = TimeSpan.Zero,
          },
        ],
        null,
        1,
        5,
        TimeSpan.Zero,
        Now,
        null
      )
      .ToImmutableList();
    await Assert
      .That(
        logs.Single(entry =>
          entry.LogType == LogType.LoadUnloadCycle && entry.Result == "UNLOAD"
        ).ProgramDetails["MaterialCompleted:1"]
      )
      .IsEqualTo("False");
    await Assert.That(CompletedHistory()).IsEmpty();
    await Assert.That(_repository.GetActiveWorkorders().Single().CompletedQuantity).IsEqualTo(0);
  }

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task MaterialCorrectionMovesTheDispositionWithItsEventMaterial(bool terminal)
  {
    _repository.RecordLoadUnloadComplete(
      [
        new MaterialToLoadOntoFace
        {
          MaterialIDs = [1],
          Process = 2,
          Path = 1,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      null,
      null,
      null,
      1,
      5,
      TimeSpan.Zero,
      Now,
      null
    );
    _repository.RecordPartialLoadUnload(
      null,
      [
        new MaterialToUnloadFromFace
        {
          MaterialIDToDestination = ImmutableDictionary<long, UnloadDestination>.Empty.Add(
            1,
            terminal ? null : new() { Queue = "output" }
          ),
          Process = 2,
          FaceNum = 1,
          ActiveOperationTime = TimeSpan.Zero,
        },
      ],
      1,
      5,
      TimeSpan.Zero,
      Now.AddSeconds(1),
      null
    );
    var swap = _repository.SwapMaterialInCurrentPalletCycle(
      5,
      1,
      4,
      "operator",
      "quarantine",
      Now.AddMinutes(1)
    );
    var unload = _repository
      .GetLogForMaterial(4)
      .Single(entry => entry.LogType == LogType.LoadUnloadCycle && entry.Result == "UNLOAD");
    await Assert.That(unload.ProgramDetails.ContainsKey("MaterialCompleted:1")).IsFalse();
    await Assert.That(unload.ProgramDetails["MaterialCompleted:4"]).IsEqualTo(terminal.ToString());
    await Assert
      .That(swap.ChangedLogEntries.Single(entry => entry.Result == "UNLOAD").ProgramDetails)
      .IsEquivalentTo(unload.ProgramDetails);
    await Assert
      .That(_repository.GetActiveWorkorders().Single().CompletedQuantity)
      .IsEqualTo(terminal ? 1 : 0);
  }

  private ImmutableList<LogEntry> CompletedHistory() =>
    _repository.GetLogOfAllCompletedParts(Now.AddDays(-1), Now.AddDays(1)).ToImmutableList();

  private static BasketContents Empty(int basketId) =>
    new() { BasketId = basketId, Slots = ImmutableSortedDictionary<int, BasketSlotContents>.Empty };

  private static BasketContents Contents(int basketId, long id, int process) =>
    new()
    {
      BasketId = basketId,
      Slots = ImmutableSortedDictionary<int, BasketSlotContents>.Empty.Add(
        1,
        new()
        {
          Material = [new BasketMaterial { MaterialID = id, Process = process }],
          AdditionalData = ImmutableSortedDictionary<string, string>.Empty,
        }
      ),
    };

  private static BasketContentsChange Change(BasketContents before, BasketContents after) =>
    new()
    {
      BasketId = before.BasketId,
      Expected = before,
      Result = after,
    };

  private void Initialize(BasketContents contents) =>
    _repository.RecordBasketContentsOperation(
      new BasketContentsOperation
      {
        Changes =
        [
          new BasketContentsChange
          {
            BasketId = contents.BasketId,
            Expected = null,
            Result = contents,
          },
        ],
      },
      "initialize-" + contents.BasketId
    );

  private static ImmutableList<EventLogMaterial> Material(long id, int process) =>
    [
      new EventLogMaterial
      {
        MaterialID = id,
        Process = process,
        Face = 1,
      },
    ];
}
