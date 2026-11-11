#nullable enable
using System;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework;

// The native full part name carries the download generation into CSV history. Numeric schedule
// IDs are reusable controller resources and are deliberately not historical lookup keys.
public sealed record MazakRouteProvenance
{
  public required string FullPartName { get; init; }
  public required int NativeProcess { get; init; }
  public required int DownloadUid { get; init; }
  public required string JobUnique { get; init; }
  public required string JobPartName { get; init; }
  public required int JobProcess { get; init; }
  public required int Path { get; init; }
  public required int NumProcesses { get; init; }
  public required string NativeProgram { get; init; }
  public required string NativeFixture { get; init; }
  public string? ProgramName { get; init; }
  public long? ProgramRevision { get; init; }
}

internal sealed partial class Repository
{
  public MazakRouteProvenance? LoadMazakRouteProvenance(string fullPartName, int nativeProcess)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText =
      "SELECT Route FROM mazak_route_provenance WHERE FullPartName = $part AND NativeProcess = $process";
    cmd.Parameters.AddWithValue("$part", fullPartName);
    cmd.Parameters.AddWithValue("$process", nativeProcess);
    return cmd.ExecuteScalar() is string json
      ? JsonSerializer.Deserialize<MazakRouteProvenance>(json)
      : null;
  }

  public ImmutableList<MazakRouteProvenance> LoadMazakRouteProvenanceForJob(string jobUnique)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText =
      "SELECT Route FROM mazak_route_provenance WHERE JobUnique = $job ORDER BY FullPartName, NativeProcess";
    cmd.Parameters.AddWithValue("$job", jobUnique);
    using var reader = cmd.ExecuteReader();
    var routes = ImmutableList.CreateBuilder<MazakRouteProvenance>();
    while (reader.Read())
      routes.Add(JsonSerializer.Deserialize<MazakRouteProvenance>(reader.GetString(0))!);
    return routes.ToImmutable();
  }

  public void RecordMazakRouteProvenance(ImmutableList<MazakRouteProvenance> routes)
  {
    ArgumentNullException.ThrowIfNull(routes);
    lock (_cfg)
    {
      using var transaction = _connection!.BeginTransaction();
      foreach (var route in routes)
      {
        ArgumentException.ThrowIfNullOrWhiteSpace(route.FullPartName);
        ArgumentException.ThrowIfNullOrWhiteSpace(route.JobUnique);
        ArgumentException.ThrowIfNullOrWhiteSpace(route.JobPartName);
        if (
          route.DownloadUid < 0
          || route.NativeProcess <= 0
          || route.JobProcess <= 0
          || route.JobProcess > route.NumProcesses
          || route.Path <= 0
        )
          throw new ArgumentException(
            "Mazak provenance requires a valid native generation and job route."
          );
        using var lookup = _connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText =
          "SELECT Route FROM mazak_route_provenance WHERE FullPartName = $part AND NativeProcess = $process";
        lookup.Parameters.AddWithValue("$part", route.FullPartName);
        lookup.Parameters.AddWithValue("$process", route.NativeProcess);
        if (lookup.ExecuteScalar() is string previous)
        {
          if (JsonSerializer.Deserialize<MazakRouteProvenance>(previous) != route)
            throw new ConflictRequestException(
              "The native Mazak identity already belongs to a different recorded route."
            );
          continue;
        }
        using var insert = _connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
          "INSERT INTO mazak_route_provenance(FullPartName, NativeProcess, JobUnique, Route) VALUES ($part, $process, $job, $route)";
        insert.Parameters.AddWithValue("$part", route.FullPartName);
        insert.Parameters.AddWithValue("$process", route.NativeProcess);
        insert.Parameters.AddWithValue("$job", route.JobUnique);
        insert.Parameters.AddWithValue("$route", JsonSerializer.Serialize(route));
        insert.ExecuteNonQuery();
        using var reserve = _connection.CreateCommand();
        reserve.Transaction = transaction;
        reserve.CommandText = "INSERT OR IGNORE INTO mazak_download_uids(Uid) VALUES ($uid)";
        reserve.Parameters.AddWithValue("$uid", route.DownloadUid);
        reserve.ExecuteNonQuery();
      }
      transaction.Commit();
    }
  }

  public int ReserveMazakDownloadUid(ImmutableHashSet<int> unavailable)
  {
    ArgumentNullException.ThrowIfNull(unavailable);
    lock (_cfg)
    {
      using var transaction = _connection!.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = transaction;
      cmd.CommandText = "SELECT MAX(Uid) FROM mazak_download_uids";
      var greatest = cmd.ExecuteScalar();
      var next = greatest is long value ? value + 1 : 0;
      while (next <= int.MaxValue && unavailable.Contains((int)next))
        next++;
      if (next > int.MaxValue)
        throw new ConflictRequestException("No unused Mazak download generation remains.");
      cmd.CommandText = "INSERT INTO mazak_download_uids(Uid) VALUES ($uid)";
      cmd.Parameters.AddWithValue("$uid", next);
      cmd.ExecuteNonQuery();
      transaction.Commit();
      return (int)next;
    }
  }
}
