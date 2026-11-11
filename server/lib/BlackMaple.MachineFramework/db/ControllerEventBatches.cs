using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework;

public enum ControllerEventBatchState
{
  Open,
  Received,
  Applying,
  Applied,
  ReconciliationRequired,
}

// Payload belongs to the controller adapter; storage does not interpret controller-specific data.
public sealed record ControllerEventBatch
{
  public required string BatchId { get; init; }
  public required string LastEventId { get; init; }
  public required string Payload { get; init; }
  public required ControllerEventBatchState State { get; init; }
  public string? Diagnostic { get; init; }
}

internal sealed partial class Repository
{
  public string ControllerEventReceiptWatermark(string stream)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(stream);
    return ControllerEventReceiptWatermark(stream, null);
  }

  private string ControllerEventReceiptWatermark(string stream, SqliteTransaction? transaction)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.Transaction = transaction;
    cmd.CommandText = "SELECT LastEventId FROM controller_event_streams WHERE Stream = $stream";
    cmd.Parameters.AddWithValue("$stream", stream);
    return cmd.ExecuteScalar() as string ?? "";
  }

  public ImmutableList<ControllerEventBatch> LoadPendingControllerEventBatches(string stream)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(stream);
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText =
      "SELECT BatchId, LastEventId, Payload, State, Diagnostic FROM controller_event_batches WHERE Stream = $stream AND State != $applied";
    cmd.Parameters.AddWithValue("$stream", stream);
    cmd.Parameters.AddWithValue("$applied", (int)ControllerEventBatchState.Applied);
    using var reader = cmd.ExecuteReader();
    var batches = ImmutableList.CreateBuilder<ControllerEventBatch>();
    while (reader.Read())
      batches.Add(ReadControllerEventBatch(reader));
    return batches.OrderBy(b => b.BatchId, StringComparer.Ordinal).ToImmutableList();
  }

  private static ControllerEventBatch ReadControllerEventBatch(SqliteDataReader reader) =>
    new()
    {
      BatchId = reader.GetString(0),
      LastEventId = reader.GetString(1),
      Payload = reader.GetString(2),
      State = (ControllerEventBatchState)reader.GetInt32(3),
      Diagnostic = reader.IsDBNull(4) ? null : reader.GetString(4),
    };

  private ControllerEventBatch? LoadControllerEventBatch(
    string stream,
    string batchId,
    SqliteTransaction transaction
  )
  {
    using var cmd = _connection!.CreateCommand();
    cmd.Transaction = transaction;
    cmd.CommandText =
      "SELECT BatchId, LastEventId, Payload, State, Diagnostic FROM controller_event_batches WHERE Stream = $stream AND BatchId = $batch";
    cmd.Parameters.AddWithValue("$stream", stream);
    cmd.Parameters.AddWithValue("$batch", batchId);
    using var reader = cmd.ExecuteReader();
    return reader.Read() ? ReadControllerEventBatch(reader) : null;
  }

  public void ReceiveControllerEventBatches(
    string stream,
    ImmutableList<ControllerEventBatch> batches
  )
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(stream);
    ArgumentNullException.ThrowIfNull(batches);
    if (batches.Select(b => b.BatchId).Distinct(StringComparer.Ordinal).Count() != batches.Count)
      throw new ArgumentException(
        "Each received batch must have a distinct identity.",
        nameof(batches)
      );
    lock (_cfg)
    {
      using var transaction = _connection!.BeginTransaction();
      var watermark = ControllerEventReceiptWatermark(stream, transaction);
      foreach (var batch in batches.OrderBy(b => b.BatchId, StringComparer.Ordinal))
      {
        ArgumentException.ThrowIfNullOrWhiteSpace(batch.BatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(batch.LastEventId);
        ArgumentNullException.ThrowIfNull(batch.Payload);
        if (
          batch.State is not (ControllerEventBatchState.Open or ControllerEventBatchState.Received)
          || StringComparer.Ordinal.Compare(batch.LastEventId, batch.BatchId) < 0
        )
          throw new ArgumentException(
            "Receipt requires an open or received batch with ordered identities."
          );
        var previous = LoadControllerEventBatch(stream, batch.BatchId, transaction);
        if (previous is not null && previous.State != ControllerEventBatchState.Open)
        {
          if (previous.LastEventId != batch.LastEventId || previous.Payload != batch.Payload)
            throw new ConflictRequestException(
              "Received controller evidence conflicts with a sealed batch."
            );
          continue;
        }
        if (previous is not null)
        {
          var comparison = StringComparer.Ordinal.Compare(batch.LastEventId, previous.LastEventId);
          if (comparison < 0 || comparison == 0 && previous.Payload != batch.Payload)
            throw new ConflictRequestException(
              "An open controller batch can only grow or seal without changing received evidence."
            );
        }
        else
        {
          if (StringComparer.Ordinal.Compare(batch.BatchId, watermark) <= 0)
            throw new ConflictRequestException(
              "A new controller batch overlaps previously received evidence."
            );
          using var open = _connection.CreateCommand();
          open.Transaction = transaction;
          open.CommandText =
            "SELECT COUNT(*) FROM controller_event_batches WHERE Stream = $stream AND State = $open";
          open.Parameters.AddWithValue("$stream", stream);
          open.Parameters.AddWithValue("$open", (int)ControllerEventBatchState.Open);
          if (Convert.ToInt64(open.ExecuteScalar()) != 0)
            throw new ConflictRequestException(
              "Seal the open controller batch before receiving a later batch."
            );
        }
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
          "INSERT INTO controller_event_batches(Stream, BatchId, LastEventId, Payload, State, Diagnostic) VALUES ($stream, $batch, $last, $payload, $state, $diagnostic) "
          + "ON CONFLICT(Stream, BatchId) DO UPDATE SET LastEventId = excluded.LastEventId, Payload = excluded.Payload, State = excluded.State, Diagnostic = excluded.Diagnostic";
        cmd.Parameters.AddWithValue("$stream", stream);
        cmd.Parameters.AddWithValue("$batch", batch.BatchId);
        cmd.Parameters.AddWithValue("$last", batch.LastEventId);
        cmd.Parameters.AddWithValue("$payload", batch.Payload);
        cmd.Parameters.AddWithValue("$state", (int)batch.State);
        cmd.Parameters.AddWithValue("$diagnostic", (object?)batch.Diagnostic ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        if (StringComparer.Ordinal.Compare(batch.LastEventId, watermark) > 0)
          watermark = batch.LastEventId;
      }
      if (!batches.IsEmpty)
      {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
          "INSERT INTO controller_event_streams(Stream, LastEventId) VALUES ($stream, $last) ON CONFLICT(Stream) DO UPDATE SET LastEventId = excluded.LastEventId";
        cmd.Parameters.AddWithValue("$stream", stream);
        cmd.Parameters.AddWithValue("$last", watermark);
        cmd.ExecuteNonQuery();
      }
      transaction.Commit();
    }
  }

  public void SetControllerEventBatchState(
    string stream,
    string batchId,
    ControllerEventBatchState state,
    string? diagnostic = null
  )
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(stream);
    ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
    lock (_cfg)
    {
      using var transaction = _connection!.BeginTransaction();
      var previous =
        LoadControllerEventBatch(stream, batchId, transaction)
        ?? throw new ConflictRequestException("The controller event batch has not been received.");
      if (
        !Enum.IsDefined(state)
        || !(
          previous.State == state && state != ControllerEventBatchState.Applying
          || previous.State == ControllerEventBatchState.Received
            && state == ControllerEventBatchState.Applying
          || previous.State == ControllerEventBatchState.Applying
            && state
              is ControllerEventBatchState.Applied
                or ControllerEventBatchState.ReconciliationRequired
        )
      )
        throw new ConflictRequestException(
          "The controller event application transition is not valid."
        );
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = transaction;
      cmd.CommandText =
        "UPDATE controller_event_batches SET State = $state, Diagnostic = $diagnostic WHERE Stream = $stream AND BatchId = $batch";
      cmd.Parameters.AddWithValue("$state", (int)state);
      cmd.Parameters.AddWithValue("$diagnostic", (object?)diagnostic ?? DBNull.Value);
      cmd.Parameters.AddWithValue("$stream", stream);
      cmd.Parameters.AddWithValue("$batch", batchId);
      cmd.ExecuteNonQuery();
      transaction.Commit();
    }
  }
}
