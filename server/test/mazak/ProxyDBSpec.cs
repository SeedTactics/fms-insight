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
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed class ProxyDBspec : IDisposable
{
  private readonly WireMockServer _server;
  private readonly MazakProxyDB _db;
  private readonly Fixture _fixture = new();

  public ProxyDBspec()
  {
    _server = WireMockServer.Start();
    _db = new MazakProxyDB(
      new MazakConfig() { DBType = MazakDbType.MazakSmooth, ProxyDBUri = new Uri(_server.Url) }
    );
  }

  public void Dispose()
  {
    _db.Dispose();
    _server.Stop();
  }

  private WireMock.ResponseProviders.IResponseProvider JsonBody<T>(T body)
  {
    var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
    using var mem = new System.IO.MemoryStream();
    ser.WriteObject(mem, body);
    mem.Seek(0, System.IO.SeekOrigin.Begin);
    return Response.Create().WithStatusCode(200).WithBody(mem.ToArray());
  }

  private T Decode<T>(byte[] bytes)
  {
    var ser = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
    using var mem = new System.IO.MemoryStream(bytes);
    return (T)ser.ReadObject(mem);
  }

  private MazakAllData MkRandData()
  {
    // need the elements to be arrays
    return new MazakAllData()
    {
      Schedules = _fixture
        .Create<MazakScheduleRow[]>()
        .Select(x =>
          x with
          {
            DueDate = x.DueDate?.Date,
            StartDate = x.StartDate?.Date,
            Processes = x.Processes.ToArray(),
          }
        )
        .ToArray(),
      LoadActions = _fixture.Create<LoadAction[]>(),
      PalletStatuses = _fixture.Create<MazakPalletStatusRow[]>(),
      PalletSubStatuses = _fixture.Create<MazakPalletSubStatusRow[]>(),
      PalletPositions = _fixture.Create<MazakPalletPositionRow[]>(),
      Alarms = _fixture.Create<MazakAlarmRow[]>(),
      Parts = _fixture
        .Create<MazakPartRow[]>()
        .Select(x => x with { Processes = x.Processes.ToArray() })
        .ToArray(),
      Pallets = _fixture.Create<MazakPalletRow[]>(),
      MainPrograms = _fixture.Create<MazakProgramRow[]>(),
      Fixtures = _fixture.Create<MazakFixtureRow[]>(),
    };
  }

  [Test]
  public void LoadsAllData()
  {
    // need the elements to be arrays
    var allData = MkRandData();

    _server.Given(Request.Create().WithPath("/all-data").UsingGet()).RespondWith(JsonBody(allData));

    _db.LoadAllData().ShouldBeEquivalentTo(allData);
  }

  [Test]
  public void HandlesError()
  {
    _server
      .Given(Request.Create().WithPath("/all-data").UsingGet())
      .RespondWith(Response.Create().WithStatusCode(500).WithBody("The error text\n another line"));

    Should
      .Throw<Exception>(() => _db.LoadAllData())
      .Message.ShouldBe($"Error communicating with mazak proxy at {_server.Url}/: The error text");
  }

  [Test]
  public void LoadsAllDataAndLogs()
  {
    var allData = MkRandData().CloneToDerived<MazakAllDataAndLogs, MazakAllData>() with
    {
      Logs = _fixture
        .Create<MazakMachineInterface.LogEntry[]>()
        .Select(x =>
          x with
          {
            TimeUTC = new DateTime(
              x.TimeUTC.Year,
              x.TimeUTC.Month,
              x.TimeUTC.Day,
              x.TimeUTC.Hour,
              x.TimeUTC.Minute,
              x.TimeUTC.Second,
              DateTimeKind.Utc
            ),
          }
        )
        .ToArray(),
    };

    _server.Given(Request.Create().WithPath("/all-data-and-logs").UsingPost()).RespondWith(JsonBody(allData));

    _db.LoadAllDataAndLogs("123").ShouldBeEquivalentTo(allData);
  }

  [Test]
  public void LoadsPrograms()
  {
    var programs = _fixture.Create<MazakProgramRow[]>();

    _server.Given(Request.Create().WithPath("/programs").UsingGet()).RespondWith(JsonBody(programs));

    _db.LoadPrograms().ShouldBeEquivalentTo(programs);
  }

  [Test]
  public void LoadsTools()
  {
    var tools = _fixture.Create<ToolPocketRow[]>();

    _server.Given(Request.Create().WithPath("/tools").UsingGet()).RespondWith(JsonBody(tools));

    _db.LoadTools().ShouldBeEquivalentTo(tools);
  }

  [Test]
  public void DeleteLogs()
  {
    _server
      .Given(Request.Create().WithPath("/delete-logs").UsingPost().WithBody("\"123\""))
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("true"));

    _db.DeleteLogs("123");
  }

  [Test]
  public void Save()
  {
    var data = new MazakWriteData()
    {
      Schedules = _fixture
        .Create<MazakScheduleRow[]>()
        .Select(x =>
          x with
          {
            DueDate = x.DueDate?.Date,
            StartDate = x.StartDate?.Date,
            Processes = x.Processes.ToArray(),
          }
        )
        .ToArray(),
      Parts = _fixture
        .Create<MazakPartRow[]>()
        .Select(x => x with { Processes = x.Processes.ToArray() })
        .ToArray(),
      Pallets = _fixture.Create<MazakPalletRow[]>(),
      Fixtures = _fixture.Create<MazakFixtureRow[]>(),
      Programs = _fixture.Create<NewMazakProgram[]>(),
    };

    _server
      .Given(
        Request
          .Create()
          .WithPath("/save")
          .UsingPost()
          .WithBody(bytes =>
          {
            if (bytes == null)
            {
              return false;
            }
            else
            {
              try
              {
                Decode<MazakWriteData>(bytes).ShouldBeEquivalentTo(data);
              }
              catch (Exception)
              {
                return false;
              }
              return true;
            }
          })
      )
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("true"));

    _db.Save(data);
  }

  [Test]
  public void HandlesErrorInSave()
  {
    _server
      .Given(Request.Create().WithPath("/save").UsingPost())
      .RespondWith(Response.Create().WithStatusCode(500).WithBody("The error text\n another line"));

    Should
      .Throw<Exception>(() => _db.Save(_fixture.Create<MazakWriteData>()))
      .Message.ShouldBe($"Error communicating with mazak proxy at {_server.Url}/: The error text");
  }

  [Test]
  public async Task HandlesLongPolling()
  {
    bool eventRaised = false;
    _db.OnNewEvent += () => eventRaised = true;

    _server
      .Given(Request.Create().WithPath("/long-poll-events").UsingGet())
      .RespondWith(
        Response.Create().WithStatusCode(200).WithBodyAsJson(false).WithDelay(TimeSpan.FromSeconds(1.5))
      );

    await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

    eventRaised.ShouldBeFalse();

    await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

    // responded with false
    eventRaised.ShouldBeFalse();

    _server
      .Given(Request.Create().WithPath("/long-poll-events").UsingGet())
      .RespondWith(
        Response.Create().WithStatusCode(200).WithBodyAsJson(true).WithDelay(TimeSpan.FromSeconds(1.5))
      );

    await Task.Delay(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);

    eventRaised.ShouldBeTrue();
  }
}
