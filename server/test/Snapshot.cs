using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlackMaple.FMSInsight.Tests;

internal static class Snapshot
{
  public static async Task Match(
    string actual,
    string name,
    string extension,
    [CallerFilePath] string sourceFile = ""
  )
  {
    var path = Path.Combine(Path.GetDirectoryName(sourceFile)!, name);
    var verified = path + ".verified." + extension;
    var received = path + ".received." + extension;
    actual = actual.ReplaceLineEndings("\n");

    if (File.Exists(verified) && Matches(actual, await File.ReadAllTextAsync(verified), extension))
    {
      File.Delete(received);
      return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(received, actual);
    throw new InvalidOperationException(
      $"Snapshot {(File.Exists(verified) ? "does not match" : "is missing")}."
        + $"\nVerified: {verified}\nReceived: {received}"
    );
  }

  private static bool Matches(string actual, string expected, string extension)
  {
    if (extension == "json")
    {
      using var actualJson = JsonDocument.Parse(actual);
      using var expectedJson = JsonDocument.Parse(expected);
      return JsonElement.DeepEquals(actualJson.RootElement, expectedJson.RootElement);
    }
    return expected.ReplaceLineEndings("\n") == actual;
  }

  public static string Json<T>(T value, JsonSerializerOptions options)
  {
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
    {
      WriteJson(writer, JsonSerializer.SerializeToElement(value, options));
    }
    return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
  }

  // Sort object properties (including dictionary keys), while preserving array order and values.
  private static void WriteJson(Utf8JsonWriter writer, JsonElement value)
  {
    switch (value.ValueKind)
    {
      case JsonValueKind.Object:
        writer.WriteStartObject();
        foreach (
          var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
        )
        {
          writer.WritePropertyName(property.Name);
          WriteJson(writer, property.Value);
        }
        writer.WriteEndObject();
        break;
      case JsonValueKind.Array:
        writer.WriteStartArray();
        foreach (var item in value.EnumerateArray())
        {
          WriteJson(writer, item);
        }
        writer.WriteEndArray();
        break;
      default:
        value.WriteTo(writer);
        break;
    }
  }
}
