/* Copyright (c) 2018, John Lenz

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
using System.IO;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using MazakMachineInterface;
using NSubstitute;
using Newtonsoft.Json;

namespace MachineWatchTest
{
  public class WriteJobsSpec : IDisposable
  {
    private class WriteMock : IWriteData
    {
      public MazakDbType MazakType => MazakDbType.MazakSmooth;
      public MazakWriteData DeletePartsPals {get;set;}
      public MazakWriteData Fixtures {get;set;}
      public MazakWriteData AddParts {get;set;}
      public MazakWriteData AddSchedules {get;set;}
      public string errorForPrefix;

      public void Save(MazakWriteData data, string prefix, IList<string> log)
      {
        if (errorForPrefix == prefix) {
          log.Add("Sample error");
        }
        if (prefix == "Delete Parts Pallets") {
          DeletePartsPals = data;
        } else if (prefix == "Fixtures") {
          Fixtures = data;
        } else if (prefix == "Add Parts") {
          AddParts = data;
        } else if (prefix == "Add Schedules") {
          AddSchedules = data;
        } else {
          Assert.True(false, "Unexpected prefix " + prefix);
        }
      }
    }

    private JobLogDB _logDB;
		private JobDB _jobDB;
    private IWriteJobs _writeJobs;
    private WriteMock _writeMock;
    private IReadDataAccess _readMock;
    private JsonSerializerSettings jsonSettings;
    private FMSSettings _settings;

    public WriteJobsSpec()
    {
			var logConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      logConn.Open();
      _logDB = new JobLogDB(logConn);
      _logDB.CreateTables(firstSerialOnEmpty: null);

			var jobConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      jobConn.Open();
      _jobDB = new JobDB(jobConn);
      _jobDB.CreateTables();

      _writeMock = new WriteMock();

      _readMock = Substitute.For<IReadDataAccess>();
      _readMock.MazakType.Returns(MazakDbType.MazakSmooth);
      _readMock.LoadAllData().Returns(new MazakAllData() {
        Schedules = Enumerable.Empty<MazakScheduleRow>(),
        Parts = Enumerable.Empty<MazakPartRow>(),
        Pallets = Enumerable.Empty<MazakPalletRow>(),
        PalletSubStatuses = Enumerable.Empty<MazakPalletSubStatusRow>(),
        PalletPositions = Enumerable.Empty<MazakPalletPositionRow>(),
        LoadActions = Enumerable.Empty<LoadAction>(),
        MainPrograms = new HashSet<string>(new [] {
          "1001", "1002", "1003", "1004"
        }),
        Fixtures = Enumerable.Empty<MazakFixtureRow>()
      });
      _readMock.LoadSchedulesPartsPallets().Returns(x => new MazakSchedulesPartsPallets() {
        Schedules = Enumerable.Empty<MazakScheduleRow>(),
        Parts = _writeMock.AddParts.Parts,
        Pallets = _writeMock.AddParts.Pallets,
        PalletSubStatuses = Enumerable.Empty<MazakPalletSubStatusRow>(),
        PalletPositions = Enumerable.Empty<MazakPalletPositionRow>(),
        LoadActions = Enumerable.Empty<LoadAction>(),
        MainPrograms = new HashSet<string>(new [] {
          "1001", "1002", "1003", "1004"
        }),
      });

      _settings = new FMSSettings();
      _settings.Queues["castings"] = new QueueSize();
      _settings.Queues["queueAAA"] = new QueueSize();
      _settings.Queues["queueBBB"] = new QueueSize();
      _settings.Queues["queueCCC"] = new QueueSize();

      _writeJobs = new WriteJobs(
        _writeMock,
        _readMock,
        Substitute.For<IHoldManagement>(),
        _jobDB,
        _logDB,
        check: false,
        useStarting: true,
        decrPriority: false);

        jsonSettings = new JsonSerializerSettings();
        jsonSettings.Converters.Add(new BlackMaple.MachineFramework.TimespanConverter());
        jsonSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
        jsonSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
        jsonSettings.Formatting = Formatting.Indented;
    }

		public void Dispose()
		{
			_logDB.Close();
			_jobDB.Close();
		}

    public void ShouldMatchSnapshot<T>(T val, string snapshot)
    {
      var expected = JsonConvert.DeserializeObject<T>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "write-snapshots", snapshot)),
          jsonSettings
      );

      val.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void RejectsMismatchedPreviousSchedule()
    {
      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
          jsonSettings
      );
      _jobDB.AddJobs(newJobs, null);

      var newJobsMultiFace = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "multi-face.json")),
          jsonSettings
      );

      _writeJobs.Invoking(x => x.AddJobs(newJobsMultiFace, "xxxx"))
        .Should()
        .Throw<BadRequestException>()
        .WithMessage(
        "Expected previous schedule ID does not match current schedule ID.  Another user may have already created a schedule."
      );
    }

    [Fact]
    public void BasicCreate()
    {
      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
          jsonSettings
      );
      _writeJobs.AddJobs(newJobs, null);

      _writeMock.DeletePartsPals.Should().BeNull();
      ShouldMatchSnapshot(_writeMock.Fixtures, "fixtures-queues-fixtures.json");
      ShouldMatchSnapshot(_writeMock.AddParts, "fixtures-queues-parts.json");
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");

      var start = newJobs.Jobs.First().RouteStartingTimeUTC;
      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Jobs.Should().BeEmpty();
    }

    [Fact]
    public void ErrorDuringPartsPallets()
    {
      _writeMock.errorForPrefix = "Add Parts";

      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
          jsonSettings
      );
      _writeJobs.Invoking(x => x.AddJobs(newJobs, null))
        .Should()
        .Throw<Exception>()
        .WithMessage("Error creating parts and pallets" + Environment.NewLine + "Sample error");

      _writeMock.DeletePartsPals.Should().BeNull();
      ShouldMatchSnapshot(_writeMock.Fixtures, "fixtures-queues-fixtures.json");
      ShouldMatchSnapshot(_writeMock.AddParts, "fixtures-queues-parts.json");
      _writeMock.AddSchedules.Should().BeNull();

      var start = newJobs.Jobs.First().RouteStartingTimeUTC;
      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Jobs.Should().BeEmpty();
    }

    [Fact]
    public void ErrorDuringSchedule()
    {
      _writeMock.errorForPrefix = "Add Schedules";

      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
          jsonSettings
      );
      _writeJobs.Invoking(x => x.AddJobs(newJobs, null))
        .Should()
        .Throw<Exception>()
        .WithMessage("Error creating schedules" + Environment.NewLine + "Sample error");

      _writeMock.DeletePartsPals.Should().BeNull();
      ShouldMatchSnapshot(_writeMock.Fixtures, "fixtures-queues-fixtures.json");
      ShouldMatchSnapshot(_writeMock.AddParts, "fixtures-queues-parts.json");
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");

      var start = newJobs.Jobs.First().RouteStartingTimeUTC;
      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Jobs
        .Should().BeEquivalentTo(
          newJobs.Jobs,
          options => options
            .Excluding(j => j.Comment)
            .Excluding(j => j.HoldEntireJob));

      //try again still with error
      _writeMock.AddSchedules = null;
      _writeJobs.Invoking(x => x.RecopyJobsToMazak(start))
        .Should()
        .Throw<Exception>()
        .WithMessage("Error creating schedules" + Environment.NewLine + "Sample error");

      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");
      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Jobs
        .Should().BeEquivalentTo(
          newJobs.Jobs,
          options => options
            .Excluding(j => j.Comment)
            .Excluding(j => j.HoldEntireJob));

      //finally succeed without error
      _writeMock.errorForPrefix = null;
      _writeJobs.RecopyJobsToMazak(start);
      ShouldMatchSnapshot(_writeMock.AddSchedules, "fixtures-queues-schedules.json");

      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).Jobs.Should().BeEmpty();
    }


    [Fact(Skip="pending")]
    public void RecopyJobsDuringNextAdd()
    {

    }

    [Fact(Skip="pending")]
    public void DeleteCompletedSchedules()
    {

    }

    [Fact(Skip="pending")]
    public void DeletePartsPalletsWithoutSchedule()
    {

    }

    [Fact(Skip="pending")]
    public void DecrementPriority()
    {

    }

    [Fact(Skip="pending")]
    public void StartingOffsetForDueDate()
    {

    }
  }
}