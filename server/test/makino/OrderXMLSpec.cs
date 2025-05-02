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
using System.Threading.Tasks;
using System.Xml;
using BlackMaple.MachineFramework;
using NSubstitute;
using Shouldly;
using VerifyXunit;
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
    FMSInsightWebHost.JsonSettings(jsonSettings);
    jsonSettings.WriteIndented = true;
  }

  public void Dispose()
  {
    File.Delete(_tempFile);
  }

  [Fact]
  public async Task FullJobs()
  {
    var newj = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText("../../../sample-newjobs/singleproc.json"),
      jsonSettings
    )!;

    OrderXML.WriteJobsXML(
      _tempFile,
      newj.Jobs,
      new MakinoSettings()
      {
        ADEPath = "unused",
        DbConnectionString = "noconn",
        DownloadOnlyOrders = false,
      },
      Substitute.For<IRepository>()
    );

    var actual = new XmlDocument();
    actual.Load(_tempFile);

    await Verifier.Verify(actual);
  }

  [Fact]
  public async Task OnlyOrders()
  {
    var newj = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText("../../../sample-newjobs/singleproc.json"),
      jsonSettings
    )!;

    OrderXML.WriteJobsXML(
      _tempFile,
      newj.Jobs,
      new MakinoSettings()
      {
        ADEPath = "unused",
        DbConnectionString = "noconn",
        DownloadOnlyOrders = true,
      },
      Substitute.For<IRepository>()
    );

    var actual = new XmlDocument();
    actual.Load(_tempFile);

    await Verifier.Verify(actual);
  }

  [Fact]
  public async Task OnlyOrdersCustomDetails()
  {
    var newj = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText("../../../sample-newjobs/singleproc.json"),
      jsonSettings
    )!;

    var db = Substitute.For<IRepository>();

    OrderXML.WriteJobsXML(
      _tempFile,
      newj.Jobs,
      new MakinoSettings()
      {
        ADEPath = "unused",
        DbConnectionString = "noconn",
        DownloadOnlyOrders = true,
        CustomOrderDetails = (j, cdb) =>
        {
          cdb.ShouldBe(db);
          return new OrderXML.OrderDetails()
          {
            Comment = j.PartName + " custom comment",
            Revision = j.PartName + " custom revision",
            Priority = j.PartName.StartsWith("a") ? 1 : 2,
          };
        },
      },
      db
    );

    var actual = new XmlDocument();
    actual.Load(_tempFile);

    await Verifier.Verify(actual);
  }

  [Fact]
  public async Task OnlyOrdersWithoutPrograms()
  {
    var newJ = JsonSerializer.Deserialize<NewJobs>(
      File.ReadAllText("../../../sample-newjobs/singleproc.json"),
      jsonSettings
    )!;

    newJ = newJ with
    {
      Jobs = newJ
        .Jobs.Select(j =>
          j.AdjustAllPaths(p =>
            p with
            {
              Stops = p.Stops.Select(s => s with { Program = null }).ToImmutableList(),
            }
          )
        )
        .ToImmutableList(),
    };

    OrderXML.WriteJobsXML(
      _tempFile,
      newJ.Jobs,
      new MakinoSettings()
      {
        ADEPath = "unused",
        DbConnectionString = "noconn",
        DownloadOnlyOrders = true,
      },
      Substitute.For<IRepository>()
    );

    var actual = new XmlDocument();
    actual.Load(_tempFile);

    await Verifier.Verify(actual);
  }

  [Fact]
  public async Task Decrements()
  {
    OrderXML.WriteDecrementXML(
      _tempFile,
      [
        new RemainingToRun() { JobUnique = "12345", RemainingQuantity = 10 },
        new RemainingToRun() { JobUnique = "67890", RemainingQuantity = 20 },
      ]
    );

    var actual = new XmlDocument();
    actual.Load(_tempFile);
    await Verifier.Verify(actual);
  }
}
