#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework;

public sealed record MazakManagedProcess(int Process, string? Fixture, string? Program);

public sealed record MazakManagedSchedule
{
  public required string ControllerSource { get; init; }
  public required string JobUnique { get; init; }
  public required string PartName { get; init; }
  public required string Comment { get; init; }
  public required int? ConfirmedScheduleId { get; init; }
  public required long? BeforeLoadCounter { get; init; }
  public required ImmutableList<MazakManagedProcess> Processes { get; init; }
  public IEnumerable<string> Fixtures =>
    Processes.Select(p => p.Fixture).OfType<string>().Where(s => s.Length > 0);
  public IEnumerable<string> Programs =>
    Processes.Select(p => p.Program).OfType<string>().Where(s => s.Length > 0);
  public bool Retired { get; init; }
}

internal sealed partial class Repository
{
  public ImmutableList<MazakManagedSchedule> LoadMazakManagedSchedules(
    string source,
    bool includeRetired = false,
    string? jobUnique = null
  )
  {
    using var cmd = ManagedScheduleCommand();
    cmd.CommandText =
      SelectManagedSchedules
      + (includeRetired ? "" : " AND Retired = 0")
      + (jobUnique == null ? "" : " AND JobUnique = $job");
    cmd.Parameters.AddWithValue("$source", source);
    if (jobUnique != null)
      cmd.Parameters.AddWithValue("$job", jobUnique);
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
        (Source, PartName, JobUnique, Comment, ConfirmedId, BeforeLoadCounter, Processes)
      VALUES ($source, $part, $job, $comment, $id, $counter, $processes)
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
    cmd.Parameters.AddWithValue("$processes", JsonSerializer.Serialize(schedule.Processes));
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
    SELECT PartName, JobUnique, Comment, ConfirmedId, BeforeLoadCounter, Processes, Retired
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
      Processes = JsonSerializer.Deserialize<ImmutableList<MazakManagedProcess>>(row.GetString(5))!,
      Retired = row.GetInt64(6) != 0,
    };
}
