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
    private JobLogDB _logDB;
		private JobDB _jobDB;
    private IWriteJobs _writeJobs;
    private IWriteData _writeMock;
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

      _writeMock = Substitute.For<IWriteData>();
      _writeMock.MazakType.Returns(MazakDbType.MazakSmooth);

      _readMock = Substitute.For<IReadDataAccess>();
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
      _readMock.LoadSchedulesPartsPallets().Returns(new MazakSchedulesPartsPallets() {
        Schedules = Enumerable.Empty<MazakScheduleRow>(),
        Parts = Enumerable.Empty<MazakPartRow>(),
        Pallets = Enumerable.Empty<MazakPalletRow>(),
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

    [Theory(Skip="pending")]
    [InlineData("fixtures-queues")]
    public void CreateTransactions(string newJobsFile)
    {
      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "sample-newjobs", newJobsFile + ".json")),
          jsonSettings
      );

      _writeJobs.AddJobs(newJobs, null);
    }
  }
}