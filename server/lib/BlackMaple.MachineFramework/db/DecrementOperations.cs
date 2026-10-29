using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework;

public sealed record JobDecrementOperation
{
  public required string OperationId { get; init; }
  public required DateTime TimeUTC { get; init; }
  public required ImmutableList<JobAndDecrementQuantity> Decrements { get; init; }
}

public sealed record RawMaterialToUnallocate
{
  public required long MaterialID { get; init; }
  public required string JobUnique { get; init; }
  public required string Casting { get; init; }
}

internal sealed partial class Repository
{
  public JobDecrementOperation? LoadJobDecrementOperation(string operationId) =>
    LoadJobDecrementOperation(operationId, null);

  private JobDecrementOperation? LoadJobDecrementOperation(
    string operationId,
    SqliteTransaction? transaction
  )
  {
    using var cmd = _connection!.CreateCommand();
    cmd.Transaction = transaction;
    cmd.CommandText =
      "SELECT TimeUTC, DecrementId FROM job_decrement_operations WHERE OperationId = $id";
    cmd.Parameters.AddWithValue("$id", operationId);
    DateTime time;
    long? decrementId;
    using (var reader = cmd.ExecuteReader())
    {
      if (!reader.Read())
        return null;
      time = new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
      decrementId = reader.IsDBNull(1) ? null : reader.GetInt64(1);
    }
    cmd.CommandText =
      "SELECT DecrementId,JobUnique,TimeUTC,Part,Quantity FROM job_decrements WHERE DecrementId = $decrement ORDER BY JobUnique";
    cmd.Parameters.Clear();
    cmd.Parameters.AddWithValue("$decrement", (object?)decrementId ?? DBNull.Value);
    return new JobDecrementOperation
    {
      OperationId = operationId,
      TimeUTC = time,
      Decrements = LoadDecrementQuantitiesHelper(cmd).ToImmutableList(),
    };
  }

  public JobDecrementOperation RecordJobDecrementOperation(
    string operationId,
    ImmutableList<NewDecrementQuantity> counts,
    ImmutableList<RawMaterialToUnallocate> rawMaterial,
    DateTime nowUTC
  )
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
    lock (_cfg)
    {
      using var transaction = _connection!.BeginTransaction();
      if (LoadJobDecrementOperation(operationId, transaction) is { } previous)
        return previous;
      if (
        counts.Any(c => c.Quantity <= 0)
        || counts.Select(c => c.JobUnique).Distinct().Count() != counts.Count
      )
        throw new ArgumentException(
          "Cancellation quantities must be positive and contain each job once."
        );
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = transaction;
      foreach (var count in counts)
      {
        cmd.CommandText =
          "SELECT COUNT(*) FROM jobs j WHERE UniqueStr = $job AND Part = $part AND Archived = 0 "
          + "AND (SELECT SUM(PlanQty) FROM planqty WHERE UniqueStr = $job) >= $quantity "
          + "AND NOT EXISTS (SELECT 1 FROM job_decrements WHERE JobUnique = $job)";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$job", count.JobUnique);
        cmd.Parameters.AddWithValue("$part", count.Part);
        cmd.Parameters.AddWithValue("$quantity", count.Quantity);
        if (Convert.ToInt64(cmd.ExecuteScalar()) != 1)
          throw new ConflictRequestException(
            "The job cancellation no longer matches uncanceled demand."
          );
      }
      foreach (var raw in rawMaterial)
      {
        if (!counts.Any(c => c.JobUnique == raw.JobUnique))
          throw new ArgumentException("Released raw allocations must belong to a canceled job.");
        cmd.CommandText =
          "UPDATE matdetails SET UniqueStr = NULL, PartName = $casting WHERE MaterialID = $material AND UniqueStr = $job "
          + "AND EXISTS (SELECT 1 FROM queues WHERE MaterialID = $material) "
          + "AND NOT EXISTS (SELECT 1 FROM stations_mat m JOIN stations s ON s.Counter = m.Counter "
          + "WHERE m.MaterialID = $material AND (s.Start = 0 OR s.StationLoc = $machine) AND (s.StationLoc = $machine OR s.Result IN ('LOAD','UNLOAD') AND s.StationLoc IN ($pallet, $basket)))";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$casting", raw.Casting);
        cmd.Parameters.AddWithValue("$material", raw.MaterialID);
        cmd.Parameters.AddWithValue("$job", raw.JobUnique);
        cmd.Parameters.AddWithValue("$pallet", (int)LogType.LoadUnloadCycle);
        cmd.Parameters.AddWithValue("$basket", (int)LogType.BasketLoadUnload);
        cmd.Parameters.AddWithValue("$machine", (int)LogType.MachineCycle);
        if (cmd.ExecuteNonQuery() != 1)
          throw new ConflictRequestException(
            "The raw allocation is no longer available for release."
          );
        cmd.CommandText = "DELETE FROM mat_path_details WHERE MaterialID = $material";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$material", raw.MaterialID);
        cmd.ExecuteNonQuery();
      }
      long? decrementId = null;
      if (!counts.IsEmpty)
      {
        AddNewDecrement(transaction, counts, null, nowUTC);
        cmd.CommandText = "SELECT MAX(DecrementId) FROM job_decrements";
        cmd.Parameters.Clear();
        decrementId = Convert.ToInt64(cmd.ExecuteScalar());
      }
      cmd.CommandText =
        "INSERT INTO job_decrement_operations(OperationId,TimeUTC,DecrementId) VALUES ($id,$time,$decrement)";
      cmd.Parameters.Clear();
      cmd.Parameters.AddWithValue("$id", operationId);
      cmd.Parameters.AddWithValue("$time", nowUTC.Ticks);
      cmd.Parameters.AddWithValue("$decrement", (object?)decrementId ?? DBNull.Value);
      cmd.ExecuteNonQuery();
      var result = LoadJobDecrementOperation(operationId, transaction)!;
      transaction.Commit();
      return result;
    }
  }
}
