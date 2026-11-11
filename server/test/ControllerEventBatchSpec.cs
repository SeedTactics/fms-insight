#nullable enable

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Data.Sqlite;
using TUnit.Assertions.Enums;

namespace BlackMaple.FMSInsight.Tests;

public sealed class ControllerEventBatchSpec
{
  private static ControllerEventBatch Batch(
    string id,
    ControllerEventBatchState state = ControllerEventBatchState.Received,
    string? last = null,
    string? payload = null
  ) =>
    new()
    {
      BatchId = id,
      LastEventId = last ?? id,
      Payload = payload ?? id,
      State = state,
    };

  [Test]
  public async Task ReceiptAndApplicationProgressIndependentlyAcrossConnections()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    var first = Batch("LG001");
    var second = Batch("LG002");
    using (var db = config.OpenConnection())
    {
      await Assert.That(db.ControllerEventReceiptWatermark("cell")).IsEqualTo("");
      db.ReceiveControllerEventBatches("cell", [second, first]);
      db.SetControllerEventBatchState(
        "cell",
        first.BatchId,
        ControllerEventBatchState.Received,
        "Missing route provenance"
      );
      db.SetControllerEventBatchState("cell", second.BatchId, ControllerEventBatchState.Applying);
      db.SetControllerEventBatchState("cell", second.BatchId, ControllerEventBatchState.Applied);
    }
    using var reopened = config.OpenConnection();
    await Assert.That(reopened.ControllerEventReceiptWatermark("cell")).IsEqualTo("LG002");
    await Assert
      .That(reopened.LoadPendingControllerEventBatches("cell"))
      .IsEquivalentTo([first with { Diagnostic = "Missing route provenance" }]);
    // Receipt replay must not reset already-applied work or its pending peer's diagnosis.
    reopened.ReceiveControllerEventBatches("cell", [first, second]);
    await Assert
      .That(reopened.LoadPendingControllerEventBatches("cell"))
      .IsEquivalentTo([first with { Diagnostic = "Missing route provenance" }]);
    reopened.SetControllerEventBatchState(
      "cell",
      first.BatchId,
      ControllerEventBatchState.Applying
    );
    using var afterCrash = config.OpenConnection();
    await Assert
      .That(afterCrash.LoadPendingControllerEventBatches("cell")[0].State)
      .IsEqualTo(ControllerEventBatchState.Applying);
    afterCrash.SetControllerEventBatchState(
      "cell",
      first.BatchId,
      ControllerEventBatchState.ReconciliationRequired,
      "Application may have committed"
    );
    reopened.ReceiveControllerEventBatches("cell", [first]);
    await Assert
      .That(reopened.LoadPendingControllerEventBatches("cell")[0].State)
      .IsEqualTo(ControllerEventBatchState.ReconciliationRequired);
  }

  [Test]
  public async Task OpenTailExpandsAndSealsBeforeLaterEvidenceIsReceived()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var tail = Batch("LG001", ControllerEventBatchState.Open, payload: "[1]");
    db.ReceiveControllerEventBatches("cell", [tail]);
    await Assert
      .That(() => db.ReceiveControllerEventBatches("cell", [Batch("LG003")]))
      .Throws<ConflictRequestException>();
    var expanded = tail with
    {
      LastEventId = "LG002",
      Payload = "[1,2]",
      State = ControllerEventBatchState.Received,
    };
    db.ReceiveControllerEventBatches(
      "cell",
      [expanded, Batch("LG003", ControllerEventBatchState.Open)]
    );
    await Assert.That(db.ControllerEventReceiptWatermark("cell")).IsEqualTo("LG003");
    await Assert
      .That(db.LoadPendingControllerEventBatches("cell"))
      .IsEquivalentTo(
        [expanded, Batch("LG003", ControllerEventBatchState.Open)],
        CollectionOrdering.Matching
      );
    await Assert
      .That(() => db.ReceiveControllerEventBatches("cell", [expanded with { Payload = "changed" }]))
      .Throws<ConflictRequestException>();
    await Assert.That(db.LoadPendingControllerEventBatches("cell")[0]).IsEqualTo(expanded);
  }

  [Test]
  public async Task FailedReceiptRollsBackTailAndWatermarkTogether()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var tail = Batch("LG001", ControllerEventBatchState.Open);
    db.ReceiveControllerEventBatches("cell", [tail]);
    await Assert
      .That(() =>
        db.ReceiveControllerEventBatches(
          "cell",
          [
            tail with
            {
              LastEventId = "LG003",
              Payload = "expanded",
              State = ControllerEventBatchState.Received,
            },
            Batch("LG002"),
          ]
        )
      )
      .Throws<ConflictRequestException>();
    using var reopened = config.OpenConnection();
    await Assert.That(reopened.ControllerEventReceiptWatermark("cell")).IsEqualTo("LG001");
    await Assert.That(reopened.LoadPendingControllerEventBatches("cell")).IsEquivalentTo([tail]);
  }

  [Test]
  public async Task SealedPayloadRemainsImmutableAfterApplicationAndStreamsAreIsolated()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    var batch = Batch("LG001");
    db.ReceiveControllerEventBatches("one", [batch]);
    db.SetControllerEventBatchState("one", batch.BatchId, ControllerEventBatchState.Applying);
    db.SetControllerEventBatchState("one", batch.BatchId, ControllerEventBatchState.Applied);
    db.ReceiveControllerEventBatches("two", [batch with { Payload = "other controller" }]);
    await Assert
      .That(() => db.ReceiveControllerEventBatches("one", [batch with { Payload = "changed" }]))
      .Throws<ConflictRequestException>();
    await Assert.That(db.LoadPendingControllerEventBatches("one")).IsEmpty();
    await Assert
      .That(db.LoadPendingControllerEventBatches("two")[0].Payload)
      .IsEqualTo("other controller");
    await Assert.That(db.ControllerEventReceiptWatermark("unseen")).IsEqualTo("");
  }

  [Test]
  public async Task ApplicationCannotSkipExecutionGuardOrReplayUncertainWork()
  {
    using var config = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = config.OpenConnection();
    db.ReceiveControllerEventBatches("cell", [Batch("LG001")]);
    await Assert
      .That(() =>
        db.SetControllerEventBatchState("cell", "LG001", ControllerEventBatchState.Applied)
      )
      .Throws<ConflictRequestException>();
    db.SetControllerEventBatchState("cell", "LG001", ControllerEventBatchState.Applying);
    await Assert
      .That(() =>
        db.SetControllerEventBatchState("cell", "LG001", ControllerEventBatchState.Applying)
      )
      .Throws<ConflictRequestException>();
    db.SetControllerEventBatchState(
      "cell",
      "LG001",
      ControllerEventBatchState.ReconciliationRequired
    );
    await Assert
      .That(() =>
        db.SetControllerEventBatchState("cell", "LG001", ControllerEventBatchState.Applying)
      )
      .Throws<ConflictRequestException>();
    await Assert
      .That(() =>
        db.SetControllerEventBatchState("cell", "LG001", ControllerEventBatchState.Received)
      )
      .Throws<ConflictRequestException>();
  }

  [Test]
  public async Task Version46UpgradePreservesHistoryAndCreatesRestartableInbox()
  {
    var guid = Guid.NewGuid();
    using var original = RepositoryConfig.InitializeMemoryDB(null, guid);
    using (var db = original.OpenConnection())
      db.AllocateMaterialID("retained", "part", 1);
    using var connection = new SqliteConnection(
      $"Data Source=file:${guid}?mode=memory&cache=shared"
    );
    connection.Open();
    using (var cmd = connection.CreateCommand())
    {
      cmd.CommandText =
        "DROP TABLE controller_event_batches; DROP TABLE controller_event_streams; UPDATE version SET ver = 46";
      cmd.ExecuteNonQuery();
    }
    using var upgraded = RepositoryConfig.InitializeMemoryDB(
      null,
      guid,
      createTables: false,
      upgradeTables: true
    );
    using (var db = upgraded.OpenConnection())
    {
      await Assert.That(db.GetMaterialDetails(1)!.JobUnique).IsEqualTo("retained");
      db.ReceiveControllerEventBatches("cell", [Batch("LG001")]);
    }
    using var reopened = RepositoryConfig.InitializeMemoryDB(
      null,
      guid,
      createTables: false,
      upgradeTables: true
    );
    using var persisted = reopened.OpenConnection();
    await Assert
      .That(persisted.LoadPendingControllerEventBatches("cell"))
      .IsEquivalentTo([Batch("LG001")]);
    SchemaUpgradeSpec.Check(connection);
  }
}
