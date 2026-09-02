/* Copyright (c) 2026, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted
provided that the conditions in the LICENSE file are met.
 */

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework
{
  internal sealed partial class Repository
  {
    public BasketContents GetBasketContents(int basketId)
    {
      if (basketId <= 0)
        throw new ArgumentOutOfRangeException(nameof(basketId));

      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        var contents = LoadBasketContents(basketId, trans);
        trans.Commit();
        return contents;
      }
    }

    public IEnumerable<LogEntry> RecordBasketContentsOperation(
      BasketContentsOperation operation,
      int locationNum,
      DateTime timeUTC,
      string idempotencyKey,
      EventLogMetadata metadata = null
    )
    {
      var normalized = NormalizeBasketContentsOperation(operation);
      if (locationNum <= 0)
        throw new ArgumentOutOfRangeException(nameof(locationNum));
      if (string.IsNullOrWhiteSpace(idempotencyKey))
        throw new ArgumentException(
          "A basket contents operation requires an idempotency key.",
          nameof(idempotencyKey)
        );
      var fingerprint = BasketContentsFingerprint(normalized, locationNum);
      var eventMetadata = NormalizeEventLogMetadata(metadata);
      ImmutableList<LogEntry> logs;
      var created = false;

      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        using var existingCommand = _connection.CreateCommand();
        existingCommand.Transaction = trans;
        existingCommand.CommandText =
          "SELECT OperationType, Fingerprint, ForeignID, OriginalMessage "
          + "FROM basket_operations WHERE IdempotencyKey = $key";
        existingCommand.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
        using (var reader = existingCommand.ExecuteReader())
        {
          if (reader.Read())
          {
            var existingType = reader.GetString(0);
            var existingFingerprint = reader.GetString(1);
            var existingForeignId = reader.IsDBNull(2) ? null : reader.GetString(2);
            var existingOriginalMessage = reader.GetString(3);
            if (
              existingType != "contents"
              || existingFingerprint != fingerprint
              || existingForeignId != eventMetadata.ForeignId
              || existingOriginalMessage != (eventMetadata.OriginalMessage ?? "")
            )
              throw new ConflictRequestException(
                $"Idempotency key {idempotencyKey} already identifies a different basket operation."
              );

            logs = BasketOperationForIdempotencyKey(idempotencyKey, trans);
            trans.Commit();
            return logs;
          }
        }

        foreach (var change in normalized.Changes)
          if (!SameBasketContents(LoadBasketContents(change.BasketId, trans), change.Expected))
            throw new ConflictRequestException(
              $"Basket {change.BasketId} contents changed before the operation was recorded."
            );

        ValidateMaterialOwnership(normalized, trans);
        foreach (var change in normalized.Changes)
          ClearBasketContents(change.BasketId, trans);
        foreach (var change in normalized.Changes)
          InsertBasketContents(change.Result, trans);

        var newLogs = new List<LogEntry>();
        foreach (var change in normalized.Changes)
          newLogs.Add(
            RecordBasketContents(change.Result, locationNum, timeUTC, eventMetadata, trans)
          );
        RecordBasketOperationIdentity(idempotencyKey, fingerprint, eventMetadata, newLogs, trans);
        trans.Commit();
        logs = newLogs.ToImmutableList();
        created = true;
      }

      if (created)
        foreach (var log in logs)
          _cfg.OnNewLogEntry(log, eventMetadata.ForeignId, this);
      return logs;
    }

    private BasketContents LoadBasketContents(int basketId, SqliteTransaction trans)
    {
      using var exists = _connection.CreateCommand();
      exists.Transaction = trans;
      exists.CommandText = "SELECT 1 FROM current_baskets WHERE BasketId = $basket";
      exists.Parameters.Add("basket", SqliteType.Integer).Value = basketId;
      if (exists.ExecuteScalar() is null)
        return null;

      var material = new Dictionary<int, ImmutableList<BasketMaterial>.Builder>();
      using (var materialCommand = _connection.CreateCommand())
      {
        materialCommand.Transaction = trans;
        materialCommand.CommandText =
          "SELECT Slot, MaterialID, Process FROM current_basket_material "
          + "WHERE BasketId = $basket ORDER BY Slot, MaterialID, Process";
        materialCommand.Parameters.Add("basket", SqliteType.Integer).Value = basketId;
        using var reader = materialCommand.ExecuteReader();
        while (reader.Read())
        {
          var slot = reader.GetInt32(0);
          if (!material.TryGetValue(slot, out var builder))
          {
            builder = ImmutableList.CreateBuilder<BasketMaterial>();
            material.Add(slot, builder);
          }
          builder.Add(
            new BasketMaterial { MaterialID = reader.GetInt64(1), Process = reader.GetInt32(2) }
          );
        }
      }

      var additionalData = new Dictionary<int, ImmutableSortedDictionary<string, string>.Builder>();
      using (var dataCommand = _connection.CreateCommand())
      {
        dataCommand.Transaction = trans;
        dataCommand.CommandText =
          "SELECT Slot, Key, Value FROM current_basket_slot_data "
          + "WHERE BasketId = $basket ORDER BY Slot, Key";
        dataCommand.Parameters.Add("basket", SqliteType.Integer).Value = basketId;
        using var reader = dataCommand.ExecuteReader();
        while (reader.Read())
        {
          var slot = reader.GetInt32(0);
          if (!additionalData.TryGetValue(slot, out var builder))
          {
            builder = ImmutableSortedDictionary.CreateBuilder<string, string>(
              StringComparer.Ordinal
            );
            additionalData.Add(slot, builder);
          }
          builder.Add(reader.GetString(1), reader.GetString(2));
        }
      }

      return new BasketContents
      {
        BasketId = basketId,
        Slots = material.ToImmutableSortedDictionary(
          pair => pair.Key,
          pair => new BasketSlotContents
          {
            Material = pair.Value.ToImmutable(),
            AdditionalData = additionalData.TryGetValue(pair.Key, out var data)
              ? data.ToImmutable()
              : ImmutableSortedDictionary<string, string>.Empty,
          }
        ),
      };
    }

    private void ValidateMaterialOwnership(
      BasketContentsOperation operation,
      SqliteTransaction trans
    )
    {
      var changedBaskets = operation.Changes.Select(change => change.BasketId).ToImmutableHashSet();
      var requestedMaterial = operation
        .Changes.SelectMany(change => change.Result.Slots.Values)
        .SelectMany(slot => slot.Material)
        .Select(material => material.MaterialID)
        .ToImmutableHashSet();
      if (requestedMaterial.IsEmpty)
        return;

      using var command = _connection.CreateCommand();
      command.Transaction = trans;
      command.CommandText = "SELECT NumProcesses FROM matdetails WHERE MaterialID = $material";
      command.Parameters.Add("material", SqliteType.Integer);
      foreach (
        var material in operation
          .Changes.SelectMany(change => change.Result.Slots.Values)
          .SelectMany(slot => slot.Material)
      )
      {
        command.Parameters[0].Value = material.MaterialID;
        var numProcesses = command.ExecuteScalar();
        if (numProcesses is null)
          throw new ArgumentException(
            $"Basket contents contain unknown material ID {material.MaterialID}.",
            nameof(operation)
          );
        if (material.Process > Convert.ToInt32(numProcesses, CultureInfo.InvariantCulture))
          throw new ArgumentException(
            $"Basket material {material.MaterialID} process {material.Process} exceeds its number of processes.",
            nameof(operation)
          );
      }

      command.CommandText =
        "SELECT BasketId FROM current_basket_material WHERE MaterialID = $material";
      foreach (var materialId in requestedMaterial)
      {
        command.Parameters[0].Value = materialId;
        var owner = command.ExecuteScalar();
        if (
          owner is not null
          && !changedBaskets.Contains(Convert.ToInt32(owner, CultureInfo.InvariantCulture))
        )
          throw new ConflictRequestException(
            $"Material {materialId} is already owned by basket {owner}."
          );
      }
    }

    private void ClearBasketContents(int basketId, SqliteTransaction trans)
    {
      using var command = _connection.CreateCommand();
      command.Transaction = trans;
      command.CommandText = "DELETE FROM current_basket_material WHERE BasketId = $basket";
      command.Parameters.Add("basket", SqliteType.Integer).Value = basketId;
      command.ExecuteNonQuery();
      command.CommandText = "DELETE FROM current_basket_slot_data WHERE BasketId = $basket";
      command.ExecuteNonQuery();
    }

    private void InsertBasketContents(BasketContents contents, SqliteTransaction trans)
    {
      using var command = _connection.CreateCommand();
      command.Transaction = trans;
      command.CommandText = "INSERT OR IGNORE INTO current_baskets(BasketId) VALUES($basket)";
      command.Parameters.Add("basket", SqliteType.Integer).Value = contents.BasketId;
      command.ExecuteNonQuery();

      foreach (var (slot, slotContents) in contents.Slots)
      {
        foreach (var material in slotContents.Material)
        {
          command.CommandText =
            "INSERT INTO current_basket_material(BasketId, Slot, MaterialID, Process) "
            + "VALUES($basket, $slot, $material, $process)";
          command.Parameters.Clear();
          command.Parameters.Add("basket", SqliteType.Integer).Value = contents.BasketId;
          command.Parameters.Add("slot", SqliteType.Integer).Value = slot;
          command.Parameters.Add("material", SqliteType.Integer).Value = material.MaterialID;
          command.Parameters.Add("process", SqliteType.Integer).Value = material.Process;
          command.ExecuteNonQuery();
        }
        foreach (var (key, value) in slotContents.AdditionalData)
        {
          command.CommandText =
            "INSERT INTO current_basket_slot_data(BasketId, Slot, Key, Value) "
            + "VALUES($basket, $slot, $key, $value)";
          command.Parameters.Clear();
          command.Parameters.Add("basket", SqliteType.Integer).Value = contents.BasketId;
          command.Parameters.Add("slot", SqliteType.Integer).Value = slot;
          command.Parameters.Add("key", SqliteType.Text).Value = key;
          command.Parameters.Add("value", SqliteType.Text).Value = value;
          command.ExecuteNonQuery();
        }
      }
    }

    private LogEntry RecordBasketContents(
      BasketContents contents,
      int locationNum,
      DateTime timeUTC,
      EventLogMetadata metadata,
      SqliteTransaction trans
    )
    {
      var entry = new NewEventLogEntry
      {
        Material = contents.Slots.SelectMany(pair =>
          pair.Value.Material.Select(material => new EventLogMaterial
          {
            MaterialID = material.MaterialID,
            Process = material.Process,
            Face = pair.Key,
          })
        ),
        LogType = LogType.BasketContentSnapshot,
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        LocationName = "Basket",
        LocationNum = locationNum,
        Pallet = contents.BasketId,
        Program = "CONTENTS",
        Result = "CONTENTS",
      };
      foreach (var (slot, slotContents) in contents.Slots)
      foreach (var (key, value) in slotContents.AdditionalData)
        entry.ProgramDetails.Add(
          $"slot:{slot.ToString(CultureInfo.InvariantCulture)}:{key}",
          value
        );
      return AddLogEntry(trans, entry, metadata);
    }

    private void RecordBasketOperationIdentity(
      string idempotencyKey,
      string fingerprint,
      EventLogMetadata metadata,
      IReadOnlyList<LogEntry> logs,
      SqliteTransaction trans
    )
    {
      using var command = _connection.CreateCommand();
      command.Transaction = trans;
      command.CommandText =
        "INSERT INTO basket_operations"
        + "(IdempotencyKey, OperationType, Fingerprint, ForeignID, OriginalMessage) "
        + "VALUES($key, 'contents', $fingerprint, $foreign, $original)";
      command.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
      command.Parameters.Add("fingerprint", SqliteType.Text).Value = fingerprint;
      command.Parameters.Add("foreign", SqliteType.Text).Value = string.IsNullOrEmpty(
        metadata.ForeignId
      )
        ? DBNull.Value
        : metadata.ForeignId;
      command.Parameters.Add("original", SqliteType.Text).Value = metadata.OriginalMessage ?? "";
      command.ExecuteNonQuery();

      command.CommandText =
        "INSERT INTO basket_operation_events(IdempotencyKey, Position, Counter) "
        + "VALUES($key, $position, $counter)";
      command.Parameters.Clear();
      command.Parameters.Add("key", SqliteType.Text).Value = idempotencyKey;
      command.Parameters.Add("position", SqliteType.Integer);
      command.Parameters.Add("counter", SqliteType.Integer);
      for (var position = 0; position < logs.Count; ++position)
      {
        command.Parameters[1].Value = position;
        command.Parameters[2].Value = logs[position].Counter;
        command.ExecuteNonQuery();
      }
    }

    private static BasketContentsOperation NormalizeBasketContentsOperation(
      BasketContentsOperation operation
    )
    {
      ArgumentNullException.ThrowIfNull(operation);
      ArgumentNullException.ThrowIfNull(operation.Changes);
      if (operation.Changes.IsEmpty)
        throw new ArgumentException(
          "A basket contents operation requires at least one change.",
          nameof(operation)
        );
      var changes = operation
        .Changes.Select(change =>
        {
          ArgumentNullException.ThrowIfNull(change);
          return change with
          {
            Expected = NormalizeBasketContents(change.Expected),
            Result = NormalizeBasketContents(change.Result),
          };
        })
        .OrderBy(change => change.BasketId)
        .ToImmutableList();
      if (changes.Select(change => change.BasketId).Distinct().Count() != changes.Count)
        throw new ArgumentException(
          "A basket contents operation cannot change one basket more than once.",
          nameof(operation)
        );

      foreach (var change in changes)
      {
        ValidateBasketContents(change.Expected, change.BasketId, allowNull: true);
        ValidateBasketContents(change.Result, change.BasketId, allowNull: false);
      }
      var materialIds = changes
        .SelectMany(change => change.Result.Slots.Values)
        .SelectMany(slot => slot.Material)
        .Select(material => material.MaterialID)
        .ToImmutableList();
      if (materialIds.Distinct().Count() != materialIds.Count)
        throw new ArgumentException(
          "A material ID cannot occupy more than one basket slot.",
          nameof(operation)
        );
      return operation with { Changes = changes };
    }

    private static BasketContents NormalizeBasketContents(BasketContents contents) =>
      contents is null
        ? null
        : contents with
        {
          Slots = contents.Slots?.ToImmutableSortedDictionary(
            pair => pair.Key,
            pair =>
              pair.Value is null
                ? null
                : pair.Value with
                {
                  Material = pair
                    .Value.Material?.OrderBy(material => material?.MaterialID ?? long.MinValue)
                    .ThenBy(material => material?.Process ?? int.MinValue)
                    .ToImmutableList(),
                  AdditionalData = pair.Value.AdditionalData?.ToImmutableSortedDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal
                  ),
                }
          ),
        };

    private static void ValidateBasketContents(
      BasketContents contents,
      int basketId,
      bool allowNull
    )
    {
      if (contents is null)
      {
        if (allowNull)
          return;
        throw new ArgumentNullException(nameof(contents));
      }
      if (basketId <= 0 || contents.BasketId != basketId)
        throw new ArgumentException("Basket contents require one matching positive BasketId.");
      ArgumentNullException.ThrowIfNull(contents.Slots);
      foreach (var (slot, slotContents) in contents.Slots)
      {
        if (slot <= 0)
          throw new ArgumentOutOfRangeException(nameof(contents), "Basket slots must be positive.");
        ArgumentNullException.ThrowIfNull(slotContents);
        ArgumentNullException.ThrowIfNull(slotContents.Material);
        ArgumentNullException.ThrowIfNull(slotContents.AdditionalData);
        if (slotContents.Material.IsEmpty)
          throw new ArgumentException("Occupied basket slots require material.", nameof(contents));
        if (
          slotContents.Material.Any(material =>
            material is null
            || material.MaterialID <= 0
            || material.MaterialID > MaterialId.MaxValue
            || material.Process < 0
          )
        )
          throw new ArgumentException("Basket material identity is invalid.", nameof(contents));
        if (
          slotContents.AdditionalData.Any(pair =>
            string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null
          )
        )
          throw new ArgumentException("Basket slot metadata is invalid.", nameof(contents));
      }
    }

    private static bool SameBasketContents(BasketContents left, BasketContents right)
    {
      if (left is null || right is null)
        return left is null && right is null;
      return BasketContentsFingerprint(left) == BasketContentsFingerprint(right);
    }

    private static string BasketContentsFingerprint(
      BasketContentsOperation operation,
      int locationNum
    )
    {
      var fingerprint = new StringBuilder();
      AppendFingerprint(fingerprint, locationNum.ToString(CultureInfo.InvariantCulture));
      foreach (var change in operation.Changes)
      {
        AppendFingerprint(fingerprint, change.BasketId.ToString(CultureInfo.InvariantCulture));
        AppendFingerprint(
          fingerprint,
          change.Expected is null ? "missing" : BasketContentsFingerprint(change.Expected)
        );
        AppendFingerprint(fingerprint, BasketContentsFingerprint(change.Result));
      }
      return fingerprint.ToString();
    }

    private static string BasketContentsFingerprint(BasketContents contents)
    {
      var fingerprint = new StringBuilder();
      AppendFingerprint(fingerprint, contents.BasketId.ToString(CultureInfo.InvariantCulture));
      foreach (var (slot, slotContents) in contents.Slots)
      {
        AppendFingerprint(fingerprint, slot.ToString(CultureInfo.InvariantCulture));
        foreach (
          var material in slotContents.Material.OrderBy(m => m.MaterialID).ThenBy(m => m.Process)
        )
        {
          AppendFingerprint(
            fingerprint,
            material.MaterialID.ToString(CultureInfo.InvariantCulture)
          );
          AppendFingerprint(fingerprint, material.Process.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var (key, value) in slotContents.AdditionalData)
        {
          AppendFingerprint(fingerprint, key);
          AppendFingerprint(fingerprint, value);
        }
      }
      return fingerprint.ToString();
    }
  }
}
