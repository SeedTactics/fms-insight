/* Copyright (c) 2026, SeedTactics

All rights reserved.
*/

using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace BlackMaple.FMSInsight.Tests.MazakProxy;

public sealed class ProxyLogRetentionSpec
{
  [Test]
  public async Task CompressesRawLogsAndBoundsCompressedRetention()
  {
    var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(directory);
    try
    {
      var today = new DateTime(2026, 8, 27);
      var recentRaw = DebugFile(directory, today.AddDays(-2), ".txt");
      var oldRaw = DebugFile(directory, today.AddDays(-3), ".txt");
      var recentCompressed = DebugFile(directory, today.AddDays(-30), ".txt.gz");
      var expiredCompressed = DebugFile(directory, today.AddDays(-31), ".txt.gz");
      await File.WriteAllTextAsync(recentRaw, "recent raw");
      await File.WriteAllTextAsync(oldRaw, "old raw");
      await File.WriteAllTextAsync(recentCompressed, "recent compressed placeholder");
      await File.WriteAllTextAsync(expiredCompressed, "expired compressed placeholder");

      Serilog.ProxyLogRetention.Maintain(directory, today);

      await Assert.That(File.Exists(recentRaw)).IsTrue();
      await Assert.That(File.Exists(oldRaw)).IsFalse();
      await Assert.That(File.Exists(oldRaw + ".gz")).IsTrue();
      await Assert.That(File.Exists(recentCompressed)).IsTrue();
      await Assert.That(File.Exists(expiredCompressed)).IsFalse();
      await Assert.That(await Decompress(oldRaw + ".gz")).IsEqualTo("old raw");
    }
    finally
    {
      Directory.Delete(directory, recursive: true);
    }
  }

  private static string DebugFile(string directory, DateTime date, string suffix) =>
    Path.Combine(directory, $"debug{date:yyyy-MM-dd}{suffix}");

  private static async Task<string> Decompress(string file)
  {
    await using var source = File.OpenRead(file);
    await using var gzip = new GZipStream(source, CompressionMode.Decompress);
    using var reader = new StreamReader(gzip);
    return await reader.ReadToEndAsync();
  }
}
