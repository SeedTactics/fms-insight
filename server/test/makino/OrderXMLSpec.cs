/* Copyright (c) 2024, John Lenz

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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using BlackMaple.MachineFramework;
using FluentAssertions;
using Xunit;

#nullable enable

namespace BlackMaple.FMSInsight.Makino.Tests;

public sealed class OrderXMLSpec : IDisposable
{
  private readonly string _tempFile;
  private readonly JsonSerializerOptions jsonSettings;

  public OrderXMLSpec()
  {
    _tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    jsonSettings = new JsonSerializerOptions();
    Startup.JsonSettings(jsonSettings);
    jsonSettings.WriteIndented = true;
  }

  public void Dispose()
  {
    File.Delete(_tempFile);
  }

  private void CheckSnapshot(string snapshot, bool update = false)
  {
    if (update)
    {
      File.Copy(
        _tempFile,
        Path.Combine("..", "..", "..", "makino", "xml-snapshots", snapshot),
        overwrite: true
      );
    }

    var expected = new XmlDocument();
    expected.Load(Path.Combine("..", "..", "..", "makino", "xml-snapshots", snapshot));

    var actual = new XmlDocument();
    actual.Load(_tempFile);

    actual.Should().BeEquivalentTo(expected);
  }

  [Fact]
  public void FullJobs()
  {
    var newj = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText("../../../sample-newjobs/singleproc.json"),
      jsonSettings
    )!;

    OrderXML.WriteOrderXML(_tempFile, newj.Jobs, onlyOrders: false);

    CheckSnapshot("singleproc.xml");
  }

  [Fact]
  public void OnlyOrders()
  {
    var newj = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText("../../../sample-newjobs/singleproc.json"),
      jsonSettings
    )!;

    OrderXML.WriteOrderXML(_tempFile, newj.Jobs, onlyOrders: true);

    CheckSnapshot("onlyorders.xml");
  }
}
