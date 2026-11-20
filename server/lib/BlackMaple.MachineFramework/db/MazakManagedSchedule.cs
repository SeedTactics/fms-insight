#nullable enable

using System;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework;

public sealed record MazakManagedSchedule
{
  public required string ControllerSource { get; init; }
  public required string JobUnique { get; init; }
  public required string PartName { get; init; }
  public required string Comment { get; init; }
  public required int? ConfirmedScheduleId { get; init; }
  public required long? BeforeLoadCounter { get; init; }
  public required ImmutableHashSet<string> Fixtures { get; init; }
  public required ImmutableHashSet<string> Programs { get; init; }
  public bool Retired { get; init; }
}

internal sealed partial class Repository
{
  public ImmutableList<MazakManagedSchedule> LoadMazakManagedSchedules(
    string source,
    bool includeRetired = false
  )
  {
    using var cmd = ManagedScheduleCommand();
    cmd.CommandText = SelectManagedSchedules + (includeRetired ? "" : " AND Retired = 0");
    cmd.Parameters.AddWithValue("$source", source);
    using var reader = cmd.ExecuteReader();
    var schedules = ImmutableList.CreateBuilder<MazakManagedSchedule>();
    while (reader.Read())
      schedules.Add(ReadManagedSchedule(source, reader));
    return schedules.ToImmutable();
  }

  public MazakManagedSchedule? LoadMazakManagedSchedule(string source, string nativePart)
  {
    using var cmd = ManagedScheduleCommand();
    cmd.CommandText = SelectManagedSchedules + " AND PartName = $part";
    cmd.Parameters.AddWithValue("$source", source);
    cmd.Parameters.AddWithValue("$part", nativePart);
    using var reader = cmd.ExecuteReader();
    return reader.Read() ? ReadManagedSchedule(source, reader) : null;
  }

  public void RecordMazakManagedSchedule(MazakManagedSchedule schedule)
  {
    using var cmd = ManagedScheduleCommand();
    cmd.CommandText = """
      INSERT INTO mazak_managed_schedule
        (Source, PartName, JobUnique, Comment, ConfirmedId, BeforeLoadCounter, Fixtures, Programs)
      VALUES ($source, $part, $job, $comment, $id, $counter, $fixtures, $programs)
      ON CONFLICT (Source, PartName) DO UPDATE SET
        ConfirmedId = coalesce(ConfirmedId, excluded.ConfirmedId)
      WHERE JobUnique = excluded.JobUnique AND Comment = excluded.Comment
        AND PartName COLLATE BINARY = excluded.PartName AND Retired = 0
        AND (ConfirmedId IS NULL OR ConfirmedId = excluded.ConfirmedId)
      """;
    cmd.Parameters.AddWithValue("$source", schedule.ControllerSource);
    cmd.Parameters.AddWithValue("$part", schedule.PartName);
    cmd.Parameters.AddWithValue("$job", schedule.JobUnique);
    cmd.Parameters.AddWithValue("$comment", schedule.Comment);
    cmd.Parameters.AddWithValue("$id", (object?)schedule.ConfirmedScheduleId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$counter", (object?)schedule.BeforeLoadCounter ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$fixtures", JsonSerializer.Serialize(schedule.Fixtures));
    cmd.Parameters.AddWithValue("$programs", JsonSerializer.Serialize(schedule.Programs));
    if (cmd.ExecuteNonQuery() != 1)
      throw new ConflictRequestException(
        $"Native part '{schedule.PartName}' is reserved for another schedule."
      );
  }

  public void RetireMazakManagedSchedule(string source, string nativePart)
  {
    using var cmd = ManagedScheduleCommand();
    cmd.CommandText =
      "UPDATE mazak_managed_schedule SET Retired = 1 WHERE Source = $source AND PartName = $part";
    cmd.Parameters.AddWithValue("$source", source);
    cmd.Parameters.AddWithValue("$part", nativePart);
    cmd.ExecuteNonQuery();
  }

  private const string SelectManagedSchedules = """
    SELECT PartName, JobUnique, Comment, ConfirmedId, BeforeLoadCounter, Fixtures, Programs, Retired
    FROM mazak_managed_schedule WHERE Source = $source
    """;

  private SqliteCommand ManagedScheduleCommand() =>
    (_connection ?? throw new ObjectDisposedException(nameof(Repository))).CreateCommand();

  private static MazakManagedSchedule ReadManagedSchedule(string source, SqliteDataReader row) =>
    new()
    {
      ControllerSource = source,
      PartName = row.GetString(0),
      JobUnique = row.GetString(1),
      Comment = row.GetString(2),
      ConfirmedScheduleId = row.IsDBNull(3) ? null : row.GetInt32(3),
      BeforeLoadCounter = row.IsDBNull(4) ? null : row.GetInt64(4),
      Fixtures = JsonSerializer.Deserialize<ImmutableHashSet<string>>(row.GetString(5))!,
      Programs = JsonSerializer.Deserialize<ImmutableHashSet<string>>(row.GetString(6))!,
      Retired = row.GetInt64(7) != 0,
    };
}
