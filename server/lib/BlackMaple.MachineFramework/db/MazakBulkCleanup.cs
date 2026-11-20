using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework;

public sealed record MazakBulkCleanup
{
  public required ImmutableHashSet<string> Parts { get; init; }
  public required ImmutableHashSet<string> Fixtures { get; init; }
  public required ImmutableHashSet<string> Programs { get; init; }
}

internal sealed partial class Repository
{
  private enum MazakCleanupResource
  {
    Part,
    Fixture,
    Program,
  }

  public void StageMazakBulkCleanup(string source, MazakBulkCleanup cleanup)
  {
    var connection = _connection ?? throw new ObjectDisposedException(nameof(Repository));
    using var trans = connection.BeginTransaction();
    InsertMazakBulkCleanup(connection, trans, source, cleanup);
    trans.Commit();
  }

  private static void InsertMazakBulkCleanup(
    SqliteConnection connection,
    SqliteTransaction trans,
    string source,
    MazakBulkCleanup cleanup
  )
  {
    using var cmd = connection.CreateCommand();
    cmd.Transaction = trans;
    cmd.CommandText = """
      INSERT OR IGNORE INTO mazak_bulk_cleanup(Source, ResourceKind, ResourceName)
      VALUES($source, $kind, $name)
      """;
    cmd.Parameters.AddWithValue("source", source);
    var kind = cmd.Parameters.Add("kind", SqliteType.Integer);
    var name = cmd.Parameters.Add("name", SqliteType.Text);
    foreach (
      var item in cleanup
        .Parts.Select(n => (Kind: MazakCleanupResource.Part, Name: n))
        .Concat(cleanup.Fixtures.Select(n => (Kind: MazakCleanupResource.Fixture, Name: n)))
        .Concat(cleanup.Programs.Select(n => (Kind: MazakCleanupResource.Program, Name: n)))
    )
    {
      kind.Value = (int)item.Kind;
      name.Value = item.Name;
      cmd.ExecuteNonQuery();
    }
  }

  public MazakBulkCleanup LoadMazakBulkCleanup(string source)
  {
    using var cmd = (
      _connection ?? throw new ObjectDisposedException(nameof(Repository))
    ).CreateCommand();
    cmd.CommandText =
      "SELECT ResourceKind, ResourceName FROM mazak_bulk_cleanup WHERE Source = $source";
    cmd.Parameters.AddWithValue("source", source);
    using var reader = cmd.ExecuteReader();
    var parts = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
    var fixtures = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
    var programs = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
    while (reader.Read())
    {
      var names = (MazakCleanupResource)reader.GetInt32(0) switch
      {
        MazakCleanupResource.Part => parts,
        MazakCleanupResource.Fixture => fixtures,
        MazakCleanupResource.Program => programs,
        _ => throw new InvalidOperationException("Unexpected Mazak cleanup resource kind."),
      };
      names.Add(reader.GetString(1));
    }
    return new MazakBulkCleanup
    {
      Parts = parts.ToImmutable(),
      Fixtures = fixtures.ToImmutable(),
      Programs = programs.ToImmutable(),
    };
  }

  public void CompleteMazakBulkCleanup(string source, MazakBulkCleanup? pending = null)
  {
    var connection = _connection ?? throw new ObjectDisposedException(nameof(Repository));
    using var trans = connection.BeginTransaction();
    using var cmd = connection.CreateCommand();
    cmd.Transaction = trans;
    cmd.CommandText = "DELETE FROM mazak_bulk_cleanup WHERE Source = $source";
    cmd.Parameters.AddWithValue("source", source);
    cmd.ExecuteNonQuery();
    if (pending != null)
      InsertMazakBulkCleanup(connection, trans, source, pending);
    trans.Commit();
  }
}
