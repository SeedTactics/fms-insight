/* Copyright (c) 2026, SeedTactics

All rights reserved.
*/

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;

namespace Serilog
{
  internal static class ProxyLogRetention
  {
    private const int RawLogDays = 3;
    private const int CompressedLogDays = 30;
    private const string Prefix = "debug";
    private const string RawSuffix = ".txt";
    private const string CompressedSuffix = ".txt.gz";

    internal static void Maintain(string directory, DateTime today)
    {
      foreach (var rawFile in Directory.GetFiles(directory, Prefix + "*" + RawSuffix))
      {
        if (
          TryParseDate(rawFile, RawSuffix, out var date)
          && today.Date.Subtract(date).TotalDays >= RawLogDays
        )
          Compress(rawFile);
      }

      foreach (var compressedFile in Directory.GetFiles(directory, Prefix + "*" + CompressedSuffix))
      {
        if (
          TryParseDate(compressedFile, CompressedSuffix, out var date)
          && today.Date.Subtract(date).TotalDays > CompressedLogDays
        )
          File.Delete(compressedFile);
      }
    }

    private static bool TryParseDate(string path, string suffix, out DateTime date)
    {
      var name = Path.GetFileName(path);
      if (
        !name.StartsWith(Prefix, StringComparison.Ordinal)
        || !name.EndsWith(suffix, StringComparison.Ordinal)
      )
      {
        date = default;
        return false;
      }

      var dateText = name.Substring(Prefix.Length, name.Length - Prefix.Length - suffix.Length);
      return DateTime.TryParseExact(
        dateText,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out date
      );
    }

    private static void Compress(string rawFile)
    {
      var compressedFile = rawFile + ".gz";
      var temporaryFile = compressedFile + ".tmp";
      if (File.Exists(temporaryFile))
        File.Delete(temporaryFile);

      try
      {
        using (
          var source = new FileStream(rawFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
        )
        using (
          var target = new FileStream(
            temporaryFile,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None
          )
        )
        using (var gzip = new GZipStream(target, CompressionMode.Compress))
        {
          var buffer = new byte[81920];
          int bytesRead;
          while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
            gzip.Write(buffer, 0, bytesRead);
        }

        if (File.Exists(compressedFile))
          File.Delete(compressedFile);
        File.Move(temporaryFile, compressedFile);
        File.Delete(rawFile);
      }
      finally
      {
        if (File.Exists(temporaryFile))
          File.Delete(temporaryFile);
      }
    }
  }
}
