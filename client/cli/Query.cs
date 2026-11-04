using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace BlackMaple.FMSInsight.Cli;

internal static class Query
{
  public static async Task Run(
    string path,
    string sql,
    string format,
    TextWriter output,
    CancellationToken cancellationToken
  )
  {
    using var db = new SqliteConnection(
      new SqliteConnectionStringBuilder
      {
        DataSource = Path.GetFullPath(path),
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false,
      }.ToString()
    );
    db.Open();
    // ReadOnly protects the main database. The authorizer also rejects writes to attached/temp
    // databases, ATTACH, extension loading, and pragmas that could undo query restrictions.
    raw.sqlite3_set_authorizer(
      db.Handle,
      (strdelegate_authorizer)(
        (_, action, first, second, _, _) => Authorize(action, first, second)
      ),
      null
    );
    raw.sqlite3_progress_handler(
      db.Handle,
      1000,
      _ => cancellationToken.IsCancellationRequested ? 1 : 0,
      null
    );
    using var command = db.CreateCommand();
    command.CommandText = sql;
    cancellationToken.ThrowIfCancellationRequested();
    using var reader = command.ExecuteReader();
    var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToImmutableArray();
    if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
      throw new ArgumentException("Result columns must have distinct names; add SQL aliases.");
    if (format == "csv")
    {
      await output.WriteLineAsync(string.Join(",", names.Select(Csv)));
      while (reader.Read())
      {
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteLineAsync(
          string.Join(
            ",",
            Enumerable
              .Range(0, names.Length)
              .Select(i => reader.IsDBNull(i) ? "" : Csv(Text(reader.GetValue(i))))
          )
        );
      }
    }
    else
    {
      await output.WriteAsync("[");
      var first = true;
      while (reader.Read())
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (!first)
          await output.WriteAsync(",");
        first = false;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
          writer.WriteStartObject();
          for (var i = 0; i < names.Length; i++)
          {
            writer.WritePropertyName(names[i]);
            var value = reader.GetValue(i);
            switch (value)
            {
              case DBNull:
                writer.WriteNullValue();
                break;
              case long integer:
                writer.WriteNumberValue(integer);
                break;
              case double number when double.IsFinite(number):
                writer.WriteNumberValue(number);
                break;
              case double:
                throw new ArgumentException("Non-finite numbers cannot be exported as JSON.");
              default:
                writer.WriteStringValue(Text(value));
                break;
            }
          }
          writer.WriteEndObject();
        }
        await output.WriteAsync(
          Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length))
        );
      }
      await output.WriteLineAsync("]");
    }
    if (reader.NextResult())
      throw new ArgumentException("Use one SQL result set per query command.");
  }

  private static int Authorize(int action, string? first, string? second) =>
    action switch
    {
      raw.SQLITE_SELECT or raw.SQLITE_READ or raw.SQLITE_RECURSIVE => raw.SQLITE_OK,
      raw.SQLITE_FUNCTION
        when !string.Equals(second, "load_extension", StringComparison.OrdinalIgnoreCase) =>
        raw.SQLITE_OK,
      raw.SQLITE_PRAGMA
        when first?.ToLowerInvariant()
          is "table_info"
            or "table_xinfo"
            or "table_list"
            or "index_info"
            or "index_xinfo"
            or "index_list"
            or "foreign_key_list"
            or "database_list"
            or "compile_options"
            or "collation_list"
            or "function_list"
            or "module_list"
            or "pragma_list" => raw.SQLITE_OK,
      raw.SQLITE_PRAGMA
        when second is null && first?.ToLowerInvariant() is "user_version" or "schema_version" =>
        raw.SQLITE_OK,
      _ => raw.SQLITE_DENY,
    };

  private static string Text(object value) =>
    value is byte[] bytes
      ? Convert.ToBase64String(bytes)
      : Convert.ToString(value, CultureInfo.InvariantCulture)!;

  private static string Csv(string value) =>
    value.Length == 0 || value.IndexOfAny([',', '"', '\r', '\n']) >= 0
      ? "\"" + value.Replace("\"", "\"\"") + "\""
      : value;
}
