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
using Newtonsoft.Json;

namespace MachineWatchTest
{
  public class CurrentStatusSpec : IDisposable
  {
    private JobLogDB _logDB;
		private JobDB _jobDB;
    private JsonSerializerSettings jsonSettings;
    private FMSSettings _settings;

    public CurrentStatusSpec()
    {
			var logConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      logConn.Open();
      _logDB = new JobLogDB(logConn);
      _logDB.CreateTables(firstSerialOnEmpty: null);

			var jobConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      jobConn.Open();
      _jobDB = new JobDB(jobConn);
      _jobDB.CreateTables();

      _settings = new FMSSettings();
      _settings.Queues["castings"] = new QueueSize();
      _settings.Queues["queueAAA"] = new QueueSize();
      _settings.Queues["queueBBB"] = new QueueSize();
      _settings.Queues["queueCCC"] = new QueueSize();

      jsonSettings = new JsonSerializerSettings();
      jsonSettings.Converters.Add(new BlackMaple.MachineFramework.TimespanConverter());
      jsonSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
      jsonSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
      jsonSettings.Formatting = Formatting.Indented;
      jsonSettings.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;

    }

		public void Dispose()
		{
			_logDB.Close();
			_jobDB.Close();
		}

    /*
    [Fact]
    public void CreateSnapshot()
    {
      var scenario = "basic-cutting";

      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".jobs.json")),
          jsonSettings
      );
      _jobDB.AddJobs(newJobs, null);

      var connStr = "Server=172.16.11.6;User Id=mazakpmc;Password=Fms-978";
      var open = new OpenDatabaseKitReadDB(connStr, MazakDbType.MazakSmooth, null);
      var smooth = new SmoothReadOnlyDB(connStr, open);

      var all = smooth.LoadAllData();

      File.WriteAllText(
        Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".data.json"),
        JsonConvert.SerializeObject(all, jsonSettings)
      );

      var status = BuildCurrentStatus.Build(_jobDB, _logDB, _settings, MazakDbType.MazakSmooth, all);

      File.WriteAllText(
        Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".status.json"),
        JsonConvert.SerializeObject(status, jsonSettings)
      );
    }
    */

    [Theory]
    [InlineData("basic-no-material")]
    [InlineData("basic-load-material")]
    [InlineData("basic-cutting")]
    public void StatusSnapshot(string scenario)
    {
      var newJobs = JsonConvert.DeserializeObject<NewJobs>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".jobs.json")),
          jsonSettings
      );
      _jobDB.AddJobs(newJobs, null);

      var allData = JsonConvert.DeserializeObject<MazakAllData>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".data.json")),
          jsonSettings
      );

      var status = BuildCurrentStatus.Build(_jobDB, _logDB, _settings, MazakDbType.MazakSmooth, allData);

      var expectedStatus = JsonConvert.DeserializeObject<CurrentStatus>(
        File.ReadAllText(
          Path.Combine("..", "..", "..", "mazak", "read-snapshots", scenario + ".status.json")),
          jsonSettings
      );

      status.Should().BeEquivalentTo(expectedStatus);
    }
  }
}