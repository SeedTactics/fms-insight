using System;
using System.Collections.Immutable;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework;

public sealed record MazakRouteIdentity
{
  public required string Source { get; init; }
  public required string PartName { get; init; }
  public required int NativeProcess { get; init; }
  public required string JobUnique { get; init; }
  public required int JobProcess { get; init; }
  public required int Path { get; init; }
}

internal sealed partial class Repository
{
  public void ReserveMazakRouteIdentity(MazakRouteIdentity route)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(route.Source);
    ArgumentException.ThrowIfNullOrWhiteSpace(route.PartName);
    ArgumentException.ThrowIfNullOrWhiteSpace(route.JobUnique);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(route.NativeProcess);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(route.JobProcess);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(route.Path);
    using var cmd = (
      _connection ?? throw new ObjectDisposedException(nameof(Repository))
    ).CreateCommand();
    cmd.CommandText = """
      INSERT INTO mazak_route_identity(Source, PartName, NativeProcess, JobUnique, JobProcess, Path)
      VALUES($source, $part, $native, $job, $process, $path)
      ON CONFLICT(Source, PartName, NativeProcess) DO UPDATE SET JobUnique = excluded.JobUnique
      WHERE JobUnique = excluded.JobUnique AND JobProcess = excluded.JobProcess AND Path = excluded.Path
      """;
    cmd.Parameters.AddWithValue("source", route.Source);
    cmd.Parameters.AddWithValue("part", route.PartName);
    cmd.Parameters.AddWithValue("native", route.NativeProcess);
    cmd.Parameters.AddWithValue("job", route.JobUnique);
    cmd.Parameters.AddWithValue("process", route.JobProcess);
    cmd.Parameters.AddWithValue("path", route.Path);
    if (cmd.ExecuteNonQuery() != 1)
      throw new ConflictRequestException(
        $"Mazak source '{route.Source}' part '{route.PartName}' process {route.NativeProcess} is already reserved for another job route."
      );
  }

  public MazakRouteIdentity? LoadMazakRouteIdentity(
    string source,
    string partName,
    int nativeProcess
  )
  {
    using var cmd = (
      _connection ?? throw new ObjectDisposedException(nameof(Repository))
    ).CreateCommand();
    cmd.CommandText = """
      SELECT PartName, NativeProcess, JobUnique, JobProcess, Path FROM mazak_route_identity
      WHERE Source = $source AND PartName = $part AND NativeProcess = $native
      """;
    cmd.Parameters.AddWithValue("source", source);
    cmd.Parameters.AddWithValue("part", partName);
    cmd.Parameters.AddWithValue("native", nativeProcess);
    using var reader = cmd.ExecuteReader();
    return reader.Read() ? ReadMazakRouteIdentity(source, reader) : null;
  }

  public ImmutableList<MazakRouteIdentity> LoadMazakRouteIdentities(string source, string jobUnique)
  {
    using var cmd = (
      _connection ?? throw new ObjectDisposedException(nameof(Repository))
    ).CreateCommand();
    cmd.CommandText = """
      SELECT PartName, NativeProcess, JobUnique, JobProcess, Path FROM mazak_route_identity
      WHERE Source = $source AND JobUnique = $job ORDER BY JobProcess, Path, PartName, NativeProcess
      """;
    cmd.Parameters.AddWithValue("source", source);
    cmd.Parameters.AddWithValue("job", jobUnique);
    using var reader = cmd.ExecuteReader();
    var identities = ImmutableList.CreateBuilder<MazakRouteIdentity>();
    while (reader.Read())
      identities.Add(ReadMazakRouteIdentity(source, reader));
    return identities.ToImmutable();
  }

  public ImmutableHashSet<string> LoadReservedMazakPartNames(string source)
  {
    using var cmd = (
      _connection ?? throw new ObjectDisposedException(nameof(Repository))
    ).CreateCommand();
    cmd.CommandText = "SELECT DISTINCT PartName FROM mazak_route_identity WHERE Source = $source";
    cmd.Parameters.AddWithValue("source", source);
    using var reader = cmd.ExecuteReader();
    var names = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
    while (reader.Read())
      names.Add(reader.GetString(0));
    return names.ToImmutable();
  }

  private static MazakRouteIdentity ReadMazakRouteIdentity(
    string source,
    SqliteDataReader reader
  ) =>
    new()
    {
      Source = source,
      PartName = reader.GetString(0),
      NativeProcess = reader.GetInt32(1),
      JobUnique = reader.GetString(2),
      JobProcess = reader.GetInt32(3),
      Path = reader.GetInt32(4),
    };
}
