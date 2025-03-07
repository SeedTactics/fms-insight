/* Copyright (c) 2020, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.IO;
using Shouldly;
using Xunit;

namespace BlackMaple.FMSInsight.Tests
{
  public class SerilogSpec : IDisposable
  {
    private string _dir;

    public SerilogSpec()
    {
      _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
      Directory.CreateDirectory(_dir);
    }

    void IDisposable.Dispose()
    {
      Directory.Delete(_dir, true);
    }

    [Fact]
    public void ArchivesDebugLog()
    {
      var file1 = Path.Combine(_dir, $"fmsinsight-debug{DateTime.Today.ToString("yyyyMMdd")}.txt");
      File.WriteAllText(file1, "some log messages");

      var oldGzFile = Path.Combine(
        _dir,
        $"fmsinsight-debug{DateTime.Today.AddDays(-5).ToString("yyyyMMdd")}.txt.gz"
      );
      File.WriteAllText(oldGzFile, "some oldGzFile");
      var toDelGzFile = Path.Combine(
        _dir,
        $"fmsinsight-debug{DateTime.Today.AddDays(-33).ToString("yyyyMMdd")}.txt.gz"
      );
      File.WriteAllText(toDelGzFile, "toDelGzFile");

      var a = new BlackMaple.MachineFramework.InsightLogging.CompressSerilogDebugLog();
      a.OnFileDeleting(file1);

      File.Exists(file1).ShouldBeTrue();
      File.ReadAllText(file1).ShouldBe("some log messages");
      File.Exists(oldGzFile).ShouldBeTrue();
      File.ReadAllText(oldGzFile).ShouldBe("some oldGzFile");
      File.Exists(toDelGzFile).ShouldBeFalse();

      File.Exists(file1 + ".gz").ShouldBeTrue();

      using (var fs = File.Open(file1 + ".gz", FileMode.Open, FileAccess.Read))
      using (
        var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress)
      )
      using (var reader = new StreamReader(gz))
      {
        reader.ReadToEnd().ShouldBe("some log messages");
      }
    }
  }
}
