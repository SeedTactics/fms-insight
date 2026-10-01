using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlackMaple.FMSInsight.Tests;

public sealed class SnapshotSpec : IDisposable
{
  private readonly string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

  public void Dispose()
  {
    if (Directory.Exists(directory))
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  [Test]
  public async Task RelativeNamesResolveFromTheRealProjectDirectory()
  {
    Directory.CreateDirectory(directory);
    var name = Path.Combine(directory, "example");
    await File.WriteAllTextAsync(name + ".verified.txt", "expected");
    var projectDirectory = typeof(SnapshotSpec)
      .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
      .Single(a => a.Key == "TestSourceDirectory")
      .Value!;

    // This also exercises mapped CallerFilePath values when built with CI's PathMap.
    await Snapshot.Match("expected", Path.GetRelativePath(projectDirectory, name), "txt");

    await Assert.That(File.Exists(name + ".received.txt")).IsFalse();
  }

  [Test]
  public async Task ConcurrentFailuresCanShareAReceivedFile()
  {
    var name = Path.Combine(directory, "example");
    var actual = new string('a', 10000);
    await Task.WhenAll(
      Enumerable
        .Range(0, 20)
        .Select(async _ =>
          await Assert
            .That(() => Snapshot.Match(actual, name, "txt"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Snapshot is missing.")
        )
    );

    await Assert.That(await File.ReadAllTextAsync(name + ".received.txt")).IsEqualTo(actual);
    await Assert.That(File.Exists(name + ".verified.txt")).IsFalse();
  }

  [Test]
  public async Task MissingBaselineWritesReceivedWithoutCreatingVerified()
  {
    var name = Path.Combine(directory, "nested", "example");
    await Assert
      .That(() => Snapshot.Match("actual\r\n", name, "txt"))
      .Throws<InvalidOperationException>()
      .WithMessageContaining("Snapshot is missing.");

    await Assert.That(File.Exists(name + ".verified.txt")).IsFalse();
    await Assert.That(await File.ReadAllTextAsync(name + ".received.txt")).IsEqualTo("actual\n");
  }

  [Test]
  public async Task MismatchPreservesBaselineAndReportsBothPaths()
  {
    Directory.CreateDirectory(directory);
    var name = Path.Combine(directory, "example");
    var verified = name + ".verified.txt";
    var received = name + ".received.txt";
    await File.WriteAllTextAsync(verified, "expected");

    await Assert
      .That(() => Snapshot.Match("actual", name, "txt"))
      .Throws<InvalidOperationException>()
      .WithMessage($"Snapshot does not match.\nVerified: {verified}\nReceived: {received}");

    await Assert.That(await File.ReadAllTextAsync(verified)).IsEqualTo("expected");
    await Assert.That(await File.ReadAllTextAsync(received)).IsEqualTo("actual");
  }

  [Test]
  public async Task MatchIgnoresLineEndingsAndDeletesStaleReceived()
  {
    Directory.CreateDirectory(directory);
    var name = Path.Combine(directory, "example");
    await File.WriteAllTextAsync(name + ".verified.txt", "expected\r\n");
    await File.WriteAllTextAsync(name + ".received.txt", "stale");

    await Snapshot.Match("expected\n", name, "txt");

    await Assert.That(File.Exists(name + ".received.txt")).IsFalse();
  }

  [Test]
  public async Task WhitespaceChangesStillFail()
  {
    Directory.CreateDirectory(directory);
    var name = Path.Combine(directory, "example");
    await File.WriteAllTextAsync(name + ".verified.txt", "expected");

    await Assert
      .That(() => Snapshot.Match("expected ", name, "txt"))
      .Throws<InvalidOperationException>();
  }

  [Test]
  public async Task JsonComparisonIgnoresFormattingAndObjectPropertyOrder()
  {
    Directory.CreateDirectory(directory);
    var name = Path.Combine(directory, "example");
    await File.WriteAllTextAsync(name + ".verified.json", "{\"z\": [2, 1], \"a\": 3}\n");

    await Snapshot.Match("{\n  \"a\": 3,\n  \"z\": [\n    2,\n    1\n  ]\n}", name, "json");

    await Assert.That(File.Exists(name + ".received.json")).IsFalse();
  }

  [Test]
  [Arguments("{\"items\":[1,2],\"date\":\"2026-09-30\"}")]
  [Arguments("{\"items\":[2,1],\"date\":\"2026-10-01\"}")]
  [Arguments("{\"items\":[2,1],\"date\":\"2026-09-30\",\"extra\":0}")]
  public async Task JsonComparisonRejectsChangesToArrayOrderValuesOrProperties(string actual)
  {
    Directory.CreateDirectory(directory);
    var name = Path.Combine(directory, "example");
    var expected = "{\"items\":[2,1],\"date\":\"2026-09-30\"}";
    await File.WriteAllTextAsync(name + ".verified.json", expected);

    await Assert
      .That(() => Snapshot.Match(actual, name, "json"))
      .Throws<InvalidOperationException>();

    await Assert.That(await File.ReadAllTextAsync(name + ".verified.json")).IsEqualTo(expected);
    await Assert.That(await File.ReadAllTextAsync(name + ".received.json")).IsEqualTo(actual);
  }

  [Test]
  public async Task JsonSortsNestedKeysAndPreservesArrayOrderDatesAndGuids()
  {
    var time = new DateTime(2026, 9, 30, 12, 34, 56, DateTimeKind.Utc);
    var guid = Guid.Parse("b0c7cc94-9a7f-4db2-b536-8eb1df581049");
    var actual = Snapshot.Json(
      new
      {
        Time = time,
        Id = guid,
        Items = ImmutableList.Create(2, 1),
        Nested = ImmutableDictionary<string, int>.Empty.Add("z", 1).Add("a", 2),
      },
      new JsonSerializerOptions()
    );
    using var json = JsonDocument.Parse(actual);

    await Assert.That(json.RootElement.GetProperty("Time").GetDateTime()).IsEqualTo(time);
    await Assert.That(json.RootElement.GetProperty("Id").GetGuid()).IsEqualTo(guid);
    await Assert.That(json.RootElement.GetProperty("Items")[0].GetInt32()).IsEqualTo(2);
    await Assert.That(json.RootElement.GetProperty("Items")[1].GetInt32()).IsEqualTo(1);
    await Assert
      .That(actual.IndexOf("\"Id\"", StringComparison.Ordinal))
      .IsLessThan(actual.IndexOf("\"Items\"", StringComparison.Ordinal));
    await Assert
      .That(actual.IndexOf("\"a\"", StringComparison.Ordinal))
      .IsLessThan(actual.IndexOf("\"z\"", StringComparison.Ordinal));
  }
}
