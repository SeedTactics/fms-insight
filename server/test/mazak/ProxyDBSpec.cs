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
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using FluentAssertions;
using MazakMachineInterface;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

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

  [Fact]
  public void LoadsAllData()
  {
    var allData = _fixture.Create<MazakAllData>();

    _server.Given(Request.Create().WithPath("/all-data").UsingGet()).RespondWith(JsonBody(allData));

    _db.LoadAllData()
      .Should()
      .BeEquivalentTo(
        allData,
        options =>
          options
            .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(1)))
            .WhenTypeIs<DateTime>()
      );
  }

  [Fact]
  public void HandlesError()
  {
    _server
      .Given(Request.Create().WithPath("/all-data").UsingGet())
      .RespondWith(Response.Create().WithStatusCode(500).WithBody("The error text\n another line"));

    Action act = () => _db.LoadAllData();

    act.Should()
      .Throw<Exception>()
      .WithMessage($"Error communicating with mazak proxy at {_server.Url}/: The error text");
  }

  [Fact]
  public void LoadsAllDataAndLogs()
  {
    var allData = _fixture.Create<MazakAllDataAndLogs>();

    _server.Given(Request.Create().WithPath("/all-data-and-logs").UsingPost()).RespondWith(JsonBody(allData));

    _db.LoadAllDataAndLogs("123")
      .Should()
      .BeEquivalentTo(
        allData,
        options =>
          options
            .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(1)))
            .WhenTypeIs<DateTime>()
      );
  }

  [Fact]
  public void LoadsPrograms()
  {
    var programs = _fixture.Create<IEnumerable<MazakProgramRow>>();

    _server.Given(Request.Create().WithPath("/programs").UsingGet()).RespondWith(JsonBody(programs));

    _db.LoadPrograms().Should().BeEquivalentTo(programs);
  }

  [Fact]
  public void LoadsTools()
  {
    var tools = _fixture.Create<IEnumerable<ToolPocketRow>>();

    _server.Given(Request.Create().WithPath("/tools").UsingGet()).RespondWith(JsonBody(tools));

    _db.LoadTools().Should().BeEquivalentTo(tools);
  }

  [Fact]
  public void DeleteLogs()
  {
    _server
      .Given(Request.Create().WithPath("/delete-logs").UsingPost().WithBody("\"123\""))
      .RespondWith(Response.Create().WithStatusCode(200).WithBody("true"));

    _db.DeleteLogs("123");
  }

  [Fact]
  public void Save()
  {
    var data = _fixture.Create<MazakWriteData>();

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
                Decode<MazakWriteData>(bytes)
                  .Should()
                  .BeEquivalentTo(
                    data,
                    options =>
                      options
                        .Using<DateTime>(ctx =>
                          ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(1))
                        )
                        .WhenTypeIs<DateTime>()
                  );
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

  [Fact]
  public void HandlesErrorInSave()
  {
    _server
      .Given(Request.Create().WithPath("/save").UsingPost())
      .RespondWith(Response.Create().WithStatusCode(500).WithBody("The error text\n another line"));

    Action act = () => _db.Save(_fixture.Create<MazakWriteData>());

    act.Should()
      .Throw<Exception>()
      .WithMessage($"Error communicating with mazak proxy at {_server.Url}/: The error text");
  }

  [Fact]
  public async Task HandlesLongPolling()
  {
    using var monitor = _db.Monitor();

    _server
      .Given(Request.Create().WithPath("/long-poll-events").UsingGet())
      .RespondWith(
        Response.Create().WithStatusCode(200).WithBodyAsJson(false).WithDelay(TimeSpan.FromSeconds(1.5))
      );

    await Task.Delay(TimeSpan.FromSeconds(1));

    monitor.Should().NotRaise("OnNewEvent");

    await Task.Delay(TimeSpan.FromSeconds(1));

    // responded with false
    monitor.Should().NotRaise("OnNewEvent");

    _server
      .Given(Request.Create().WithPath("/long-poll-events").UsingGet())
      .RespondWith(
        Response.Create().WithStatusCode(200).WithBodyAsJson(true).WithDelay(TimeSpan.FromSeconds(1.5))
      );

    await Task.Delay(TimeSpan.FromSeconds(3));

    monitor.Should().Raise("OnNewEvent");
  }
}
