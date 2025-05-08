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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;
using Shouldly;
using VerifyTUnit;

namespace BlackMaple.FMSInsight.Mazak.Tests
{
  public sealed class WriteJobsSpec : IDisposable
  {
    private readonly RepositoryConfig _repoCfg;
    private readonly IRepository _jobDB;
    private readonly IMazakDB _mazakDbMock;
    private readonly JsonSerializerOptions jsonSettings;
    private readonly FMSSettings _settings;
    private readonly MazakConfig _mazakCfg;
    private readonly MazakAllData _initialAllData;
    private static readonly DateTime fixtureQueueTime = new(2018, 07, 19, 1, 2, 3, DateTimeKind.Utc);

    private MazakWriteData FindWrite(string prefix)
    {
      return (MazakWriteData)
        _mazakDbMock
          .ReceivedCalls()
          .LastOrDefault(e =>
            e.GetMethodInfo().Name == "Save" && ((MazakWriteData)e.GetArguments()[0]).Prefix == prefix
          )
          ?.GetArguments()[0];
    }

    public WriteJobsSpec()
    {
      _repoCfg = RepositoryConfig.InitializeMemoryDB(
        new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() }
      );
      _jobDB = _repoCfg.OpenConnection();

      _mazakDbMock = Substitute.For<IMazakDB>();

      _initialAllData = new MazakAllData()
      {
        Schedules = new[]
        {
          // a completed schedule, should be deleted
          new MazakScheduleRow()
          {
            Id = 1,
            PartName = "part1:1:1",
            Comment = "uniq1-Insight",
            PlanQuantity = 15,
            CompleteQuantity = 15,
            Priority = 50,
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 1,
                FixedMachineFlag = 1,
                ProcessNumber = 1,
              },
            },
          },
          // a non-completed schedule, should be untouched
          new MazakScheduleRow()
          {
            Id = 2,
            PartName = "part2:1:1",
            Comment = "uniq2-Insight",
            PlanQuantity = 15,
            CompleteQuantity = 10,
            Priority = 50,
            Processes =
            {
              new MazakScheduleProcessRow()
              {
                MazakScheduleRowId = 1,
                FixedMachineFlag = 1,
                ProcessNumber = 1,
                ProcessMaterialQuantity = 3,
                ProcessExecuteQuantity = 2,
              },
            },
          },
        },
        Parts = new[]
        {
          // should be deleted, since corresponding schedule is deleted
          new MazakPartRow()
          {
            PartName = "part1:1:1",
            Comment = "uniq1-Insight",
            Processes = new[]
            {
              new MazakPartProcessRow()
              {
                PartName = "part1:1:1",
                ProcessNumber = 1,
                FixQuantity = 5,
                Fixture = "fixtoremove",
              },
            },
          },
          //should be kept, since schedule is kept
          new MazakPartRow()
          {
            PartName = "part2:1:1",
            Comment = "uniq2-Insight",
            Processes = new[]
            {
              new MazakPartProcessRow()
              {
                PartName = "part2:1:1",
                ProcessNumber = 1,
                FixQuantity = 2,
                Fixture = "fixtokeep",
              },
            },
          },
        },
        Fixtures = new[]
        {
          new MazakFixtureRow() { FixtureName = "fixtoremove", Comment = "Insight" },
          new MazakFixtureRow() { FixtureName = "fixtokeep", Comment = "Insight" },
        },
        Pallets = new[]
        {
          new MazakPalletRow() { PalletNumber = 5, Fixture = "fixtoremove" },
          new MazakPalletRow() { PalletNumber = 6, Fixture = "fixtokeep" },
        },
        PalletSubStatuses = Enumerable.Empty<MazakPalletSubStatusRow>(),
        PalletPositions = Enumerable.Empty<MazakPalletPositionRow>(),
        LoadActions = Enumerable.Empty<LoadAction>(),
        MainPrograms = Enumerable.Concat(
          ImmutableList
            .Create("1001", "1002", "1003", "1004", "1005")
            .Select(p => new MazakProgramRow() { MainProgram = p, Comment = "" }),
          new[]
          {
            new MazakProgramRow()
            {
              MainProgram = System.IO.Path.Combine("theprogdir", "rev2", "prog-bbb-1.EIA"),
              Comment = "Insight:2:prog-bbb-1",
            },
            new MazakProgramRow()
            {
              MainProgram = System.IO.Path.Combine("theprogdir", "rev3", "prog-bbb-1.EIA"),
              Comment = "Insight:3:prog-bbb-1",
            },
          }
        ),
      };

      //  write jobs calls LoadAllData between creating fixtures and schedules
      _mazakDbMock
        .LoadAllData()
        .Returns(
          (context) =>
            new MazakAllData()
            {
              Schedules = Enumerable.Empty<MazakScheduleRow>(),
              Parts = FindWrite("Add Parts")?.Parts ?? [],
              Pallets = FindWrite("Add Parts")?.Pallets ?? [],
              PalletSubStatuses = Enumerable.Empty<MazakPalletSubStatusRow>(),
              PalletPositions = Enumerable.Empty<MazakPalletPositionRow>(),
              LoadActions = Enumerable.Empty<LoadAction>(),
              MainPrograms = ImmutableList
                .Create("1001", "1002", "1003", "1004", "1005")
                .Select(p => new MazakProgramRow() { MainProgram = p, Comment = "" }),
            }
        );

      _settings = new FMSSettings();
      _settings.Queues["castings"] = new QueueInfo();
      _settings.Queues["queueAAA"] = new QueueInfo();
      _settings.Queues["queueBBB"] = new QueueInfo();
      _settings.Queues["queueCCC"] = new QueueInfo();

      _mazakCfg = new MazakConfig()
      {
        SQLConnectionString = "unused connection string",
        LogCSVPath = "unused log path",
        DBType = MazakDbType.MazakSmooth,
        ProgramDirectory = "theprogdir",
        UseStartingOffsetForDueDate = true,
      };

      jsonSettings = new JsonSerializerOptions();
      FMSInsightWebHost.JsonSettings(jsonSettings);
      jsonSettings.WriteIndented = true;
    }

    public void Dispose()
    {
      _jobDB.Dispose();
      _repoCfg.Dispose();
    }

    private async Task ShouldMatchSnapshot<T>(T val, string snapshot)
    {
      await Verifier
        .Verify(val)
        .UseDirectory("write-snapshots")
        .UseFileName(snapshot)
        .DisableRequireUniquePrefix()
        .ScrubLinesWithReplace(input =>
        {
          // MainProgram paths use Directory separator which is different on windows and linux
          // scrub to always use /
          if (input.Trim().StartsWith("theprogdir"))
          {
            return input.Replace('/', '\\');
          }
          else
          {
            return input;
          }
        });
    }

    [Test]
    public async Task BasicCreate()
    {
      var completedJob = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        RouteStartUTC = DateTime.UtcNow.AddMinutes(-20),
        RouteEndUTC = DateTime.UtcNow,
        Archived = false,
        Processes = [new ProcessInfo() { Paths = [JobLogTest.EmptyPath] }],
        Cycles = 15,
      };
      var inProcJob = new Job()
      {
        UniqueStr = "uniq2",
        PartName = "part2",
        RouteStartUTC = DateTime.UtcNow.AddHours(-4),
        RouteEndUTC = DateTime.UtcNow,
        Archived = false,
        Processes = [new ProcessInfo() { Paths = [JobLogTest.EmptyPath] }],
        Cycles = 15,
      };
      _jobDB.AddJobs(
        new NewJobs() { Jobs = [completedJob, inProcJob], ScheduleId = "thebasicSchId" },
        null,
        addAsCopiedToSystem: true
      );

      _jobDB.LoadUnarchivedJobs().Select(j => j.UniqueStr).ShouldBe(["uniq1", "uniq2"]);

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

      WriteJobs
        .SyncFromDatabase(_initialAllData, _jobDB, _mazakDbMock, _settings, _mazakCfg, fixtureQueueTime)
        .ShouldBeTrue();

      await ShouldMatchSnapshot(FindWrite("Update schedules"), "fixtures-queues-updatesch");
      await ShouldMatchSnapshot(FindWrite("Delete Parts"), "fixtures-queues-delparts");
      FindWrite("Delete Pallets")?.Pallets.ShouldBeEmpty();
      await ShouldMatchSnapshot(FindWrite("Add Fixtures"), "fixtures-queues-add-fixtures");
      await ShouldMatchSnapshot(FindWrite("Delete Fixtures"), "fixtures-queues-del-fixtures");
      await ShouldMatchSnapshot(FindWrite("Add Parts"), "fixtures-queues-parts");
      await ShouldMatchSnapshot(FindWrite("Add Schedules"), "fixtures-queues-schedules");

      var start = newJobs.Jobs.First().RouteStartUTC;
      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).ShouldBeEmpty();

      // uniq1 was archived
      _jobDB
        .LoadUnarchivedJobs()
        .Select(j => j.UniqueStr)
        .ShouldBe(["uniq2", "aaa-schId1234", "bbb-schId1234", "ccc-schId1234"]);

      // without any decrements
      _jobDB.LoadDecrementsForJob("uniq1").ShouldBeEmpty();

      _jobDB
        .LoadUnarchivedJobs()
        .Select(j => (j.UniqueStr, j.CopiedToSystem))
        .ShouldBe(
          [("uniq2", true), ("aaa-schId1234", true), ("bbb-schId1234", true), ("ccc-schId1234", true)]
        );

      WriteJobs
        .SyncFromDatabase(_initialAllData, _jobDB, _mazakDbMock, _settings, _mazakCfg, fixtureQueueTime)
        .ShouldBeFalse();
    }

    [Test]
    public async Task CreatesPrograms()
    {
      //aaa-1  has prog prog-aaa-1 rev null
      //aaa-2 has prog prog-aaa-2 rev 4
      //bbb-1 has prog prog-bbb-1 rev 3
      //bbb-2 has prog prog-bbb-2 rev null

      //ccc is same as aaa

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "managed-progs.json")),
        jsonSettings
      );

      _jobDB.AddPrograms(
        new[]
        {
          new NewProgramContent()
          {
            ProgramName = "prog-aaa-1",
            Revision = 7,
            ProgramContent = "prog-aaa-1 content rev 7",
          },
          new NewProgramContent()
          {
            ProgramName = "prog-bbb-1",
            Revision = 3,
            ProgramContent = "prog-bbb-1 content rev 3",
          },
        },
        newJobs.Jobs.First().RouteStartUTC
      );

      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);
      WriteJobs.SyncFromDatabase(
        _initialAllData,
        _jobDB,
        _mazakDbMock,
        _settings,
        _mazakCfg,
        newJobs.Jobs.First().RouteStartUTC
      );

      await ShouldMatchSnapshot(FindWrite("Update schedules"), "fixtures-queues-updatesch");
      await ShouldMatchSnapshot(FindWrite("Delete Parts"), "fixtures-queues-delparts");
      await ShouldMatchSnapshot(FindWrite("Add Fixtures"), "managed-progs-add-fixtures");
      await ShouldMatchSnapshot(FindWrite("Delete Fixtures"), "managed-progs-del-fixtures");
      await ShouldMatchSnapshot(FindWrite("Add Parts"), "managed-progs-parts");
      await ShouldMatchSnapshot(FindWrite("Add Schedules"), "fixtures-queues-schedules");
    }

    [Test]
    public async Task OnlyDownloadsOneScheduleAtATime()
    {
      var newJ1 = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _jobDB.AddJobs(newJ1, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

      var newJ2 = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "singleproc.json")),
        jsonSettings
      ) with
      {
        ScheduleId = "zzzzzzzzzzzzz",
      };
      _jobDB.AddJobs(newJ2, expectedPreviousScheduleId: newJ1.ScheduleId, addAsCopiedToSystem: false);

      WriteJobs
        .SyncFromDatabase(_initialAllData, _jobDB, _mazakDbMock, _settings, _mazakCfg, fixtureQueueTime)
        .ShouldBeTrue();

      await ShouldMatchSnapshot(FindWrite("Update schedules"), "fixtures-queues-updatesch");
      await ShouldMatchSnapshot(FindWrite("Delete Parts"), "fixtures-queues-delparts");
      FindWrite("Delete Pallets")?.Pallets.ShouldBeEmpty();
      await ShouldMatchSnapshot(FindWrite("Add Fixtures"), "fixtures-queues-add-fixtures");
      await ShouldMatchSnapshot(FindWrite("Delete Fixtures"), "fixtures-queues-del-fixtures");
      await ShouldMatchSnapshot(FindWrite("Add Parts"), "fixtures-queues-parts");
      await ShouldMatchSnapshot(FindWrite("Add Schedules"), "fixtures-queues-schedules");
    }

    [Test]
    public async Task ErrorDuringPartsPallets()
    {
      _mazakDbMock
        .When(x => x.Save(Arg.Is<MazakWriteData>(m => m.Prefix == "Add Parts")))
        .Do(x => throw new Exception("Sample error"));

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

      Should
        .Throw<Exception>(() =>
          WriteJobs.SyncFromDatabase(
            _initialAllData,
            _jobDB,
            _mazakDbMock,
            _settings,
            _mazakCfg,
            fixtureQueueTime
          )
        )
        .Message.ShouldBe("Sample error");

      await ShouldMatchSnapshot(FindWrite("Update schedules"), "fixtures-queues-updatesch");
      await ShouldMatchSnapshot(FindWrite("Delete Parts"), "fixtures-queues-delparts");
      await ShouldMatchSnapshot(FindWrite("Add Fixtures"), "fixtures-queues-add-fixtures");
      await ShouldMatchSnapshot(FindWrite("Delete Fixtures"), "fixtures-queues-del-fixtures");
      await ShouldMatchSnapshot(FindWrite("Add Parts"), "fixtures-queues-parts");
      FindWrite("Add Schedules").ShouldBeNull();

      var start = newJobs.Jobs.First().RouteStartUTC;
      _jobDB
        .LoadJobsNotCopiedToSystem(start, start.AddMinutes(1))
        .Select(j => j.UniqueStr)
        .ShouldBe(["aaa-schId1234", "bbb-schId1234", "ccc-schId1234"]);
    }

    [Test]
    public async Task ErrorDuringSchedule()
    {
      bool throwError = true;
      _mazakDbMock
        .When(x => x.Save(Arg.Is<MazakWriteData>(m => m.Prefix == "Add Schedules")))
        .Do(x =>
        {
          if (throwError)
          {
            throw new Exception("Sample error");
          }
        });

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

      Should
        .Throw<Exception>(() =>
          WriteJobs.SyncFromDatabase(
            _initialAllData,
            _jobDB,
            _mazakDbMock,
            _settings,
            _mazakCfg,
            fixtureQueueTime
          )
        )
        .Message.ShouldBe("Sample error");

      await ShouldMatchSnapshot(FindWrite("Update schedules"), "fixtures-queues-updatesch");
      await ShouldMatchSnapshot(FindWrite("Delete Parts"), "fixtures-queues-delparts");
      await ShouldMatchSnapshot(FindWrite("Add Fixtures"), "fixtures-queues-add-fixtures");
      await ShouldMatchSnapshot(FindWrite("Delete Fixtures"), "fixtures-queues-del-fixtures");
      await ShouldMatchSnapshot(FindWrite("Add Parts"), "fixtures-queues-parts");
      await ShouldMatchSnapshot(FindWrite("Add Schedules"), "fixtures-queues-schedules");

      var start = newJobs.Jobs.First().RouteStartUTC;
      _jobDB
        .LoadJobsNotCopiedToSystem(start, start.AddMinutes(1))
        .Select(j => j.UniqueStr)
        .ShouldBe(newJobs.Jobs.Select(j => j.UniqueStr), ignoreOrder: true);

      //try again still with error
      Should
        .Throw<Exception>(() =>
          WriteJobs.SyncFromDatabase(
            _initialAllData,
            _jobDB,
            _mazakDbMock,
            _settings,
            _mazakCfg,
            fixtureQueueTime
          )
        )
        .Message.ShouldBe("Sample error");

      await ShouldMatchSnapshot(FindWrite("Add Schedules"), "fixtures-queues-schedules");
      _jobDB
        .LoadJobsNotCopiedToSystem(start, start.AddMinutes(1))
        .Select(j => j.UniqueStr)
        .ShouldBe(newJobs.Jobs.Select(j => j.UniqueStr), ignoreOrder: true);

      //finally succeed without error
      throwError = false;
      WriteJobs.SyncFromDatabase(
        _initialAllData,
        _jobDB,
        _mazakDbMock,
        _settings,
        _mazakCfg,
        fixtureQueueTime
      );
      await ShouldMatchSnapshot(FindWrite("Add Schedules"), "fixtures-queues-schedules");

      _jobDB.LoadJobsNotCopiedToSystem(start, start.AddMinutes(1)).ShouldBeEmpty();
    }

    [Test]
    public void ResumesADownloadInterruptedDuringSchedules()
    {
      bool throwError = true;
      _mazakDbMock
        .When(x => x.Save(Arg.Is<MazakWriteData>(m => m.Prefix == "Add Schedules")))
        .Do(x =>
        {
          if (throwError)
          {
            throwError = false;
            throw new Exception("Sample error");
          }
        });

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "fixtures-queues.json")),
        jsonSettings
      );
      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);

      Should
        .Throw<Exception>(() =>
          WriteJobs.SyncFromDatabase(
            _initialAllData,
            _jobDB,
            _mazakDbMock,
            _settings,
            _mazakCfg,
            fixtureQueueTime
          )
        )
        .Message.ShouldBe("Sample error");

      // Now with the parts and only the aaa schedule
      var allParts = FindWrite("Add Parts").Parts.ToList();
      var aaaSch = FindWrite("Add Schedules").Schedules.Where(s => s.PartName.StartsWith("aaa")).ToList();
      var bbbAndCCCSch = FindWrite("Add Schedules")
        .Schedules.Where(s => !s.PartName.StartsWith("aaa"))
        .ToList();

      throwError = false;
      _mazakDbMock.ClearReceivedCalls();

      WriteJobs.SyncFromDatabase(
        new MazakAllData()
        {
          Schedules = aaaSch,
          Parts = allParts,
          Pallets = [],
          Fixtures = [],
        },
        _jobDB,
        _mazakDbMock,
        _settings,
        _mazakCfg,
        fixtureQueueTime
      );

      FindWrite("Add Fixtures").ShouldBeNull();
      FindWrite("Add Parts").ShouldBeNull();
      FindWrite("Delete Fixtures").ShouldBeNull();
      FindWrite("Delete Parts").ShouldBeNull();
      FindWrite("Delete Pallets")?.Pallets.ShouldBeEmpty();
      FindWrite("Update schedules").ShouldBeNull();
      // adds only bbb and ccc
      FindWrite("Add Schedules")
        .Schedules.ShouldBeEquivalentTo(
          bbbAndCCCSch.Select(s => s with { Priority = s.Priority + 1 }).ToList()
        );

      _jobDB
        .LoadJobsNotCopiedToSystem(
          newJobs.Jobs.First().RouteStartUTC,
          newJobs.Jobs.First().RouteStartUTC.AddMinutes(1)
        )
        .ShouldBeEmpty();
    }

    [Test]
    public void SplitsWrites()
    {
      //Arrange
      var rng = new Random();

      int cnt = rng.Next(15, 25);
      var schs = new List<MazakScheduleRow>();
      for (int i = 0; i < cnt; i++)
      {
        schs.Add(new MazakScheduleRow() { Id = i });
      }

      cnt = rng.Next(15, 25);
      var parts = new List<MazakPartRow>();
      for (int i = 0; i < cnt; i++)
      {
        parts.Add(new MazakPartRow() { PartName = "Part" + i.ToString() });
      }

      cnt = rng.Next(15, 25);
      var pals = new List<MazakPalletRow>();
      for (int i = 0; i < cnt; i++)
      {
        pals.Add(new MazakPalletRow() { PalletNumber = i });
      }

      cnt = rng.Next(15, 25);
      var fixtures = new List<MazakFixtureRow>();
      for (int i = 0; i < cnt; i++)
      {
        fixtures.Add(new MazakFixtureRow() { FixtureName = "fix" + i.ToString() });
      }

      cnt = rng.Next(15, 25);
      var progs = new List<NewMazakProgram>();
      for (int i = 0; i < cnt; i++)
      {
        progs.Add(new NewMazakProgram() { ProgramName = "prog " + i.ToString() });
      }

      var orig = new MazakWriteData()
      {
        Prefix = "test",
        Schedules = schs,
        Parts = parts,
        Pallets = pals,
        Fixtures = fixtures,
        Programs = progs,
      };

      //act
      var chunks = OpenDatabaseKitDB.SplitWriteData(orig);

      // check

      chunks.SelectMany(c => c.Schedules).ToList().ShouldBeEquivalentTo(orig.Schedules);
      chunks.SelectMany(c => c.Parts).ToList().ShouldBeEquivalentTo(orig.Parts);
      chunks.SelectMany(c => c.Pallets).ToList().ShouldBeEquivalentTo(orig.Pallets);
      chunks.SelectMany(c => c.Fixtures).ToList().ShouldBeEquivalentTo(orig.Fixtures);
      chunks.SelectMany(c => c.Programs).ToList().ShouldBeEquivalentTo(orig.Programs);

      foreach (var chunk in chunks)
      {
        (
          chunk.Schedules.Count
          + chunk.Parts.Count
          + chunk.Pallets.Count
          + chunk.Fixtures.Count
          + chunk.Programs.Count
        ).ShouldBeLessThanOrEqualTo(20);
      }
    }

    [Test]
    public async Task CorrectPriorityForPalletSubsets()
    {
      // If the starting times are equal but there is a pallet subset, the priorty
      // should be split

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "pallet-subset.json")),
        jsonSettings
      );

      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);
      WriteJobs
        .SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg,
          new DateTime(2024, 6, 18, 22, 0, 0, DateTimeKind.Utc)
        )
        .ShouldBeTrue();

      await ShouldMatchSnapshot(FindWrite("Update schedules"), "pallet-subset-updatesch");
      await ShouldMatchSnapshot(FindWrite("Delete Parts"), "pallet-subset-delparts");
      FindWrite("Delete Pallets")?.Pallets.ShouldBeEmpty();
      await ShouldMatchSnapshot(FindWrite("Add Fixtures"), "pallet-subset-add-fixtures");
      await ShouldMatchSnapshot(FindWrite("Delete Fixtures"), "pallet-subset-del-fixtures");
      await ShouldMatchSnapshot(FindWrite("Add Parts"), "pallet-subset-parts");
      await ShouldMatchSnapshot(FindWrite("Add Schedules"), "pallet-subset-schedules");

      // The schedule snapshot should have checked this, but do it here too since it was the
      // bug which triggered this test case
      FindWrite("Add Schedules").Schedules.DistinctBy(p => p.Priority).Count().ShouldBe(2);
    }

    [Test]
    public async Task LargeMachineNumbers()
    {
      // should translate machine numbers 101, 102, 103, 104 to 1, 2, 3, 4
      // Careful when looking at the resulting snapshots, since machine numbers are
      // sent to Mazak in binary format, i.e. 14 is machines 2, 3, 4

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        File.ReadAllText(Path.Combine("..", "..", "..", "sample-newjobs", "machine-numbers.json")),
        jsonSettings
      );

      _jobDB.AddJobs(newJobs, expectedPreviousScheduleId: null, addAsCopiedToSystem: false);
      WriteJobs
        .SyncFromDatabase(
          _initialAllData,
          _jobDB,
          _mazakDbMock,
          _settings,
          _mazakCfg with
          {
            MachineNumbers = [101, 102, 103, 104],
          },
          new DateTime(2024, 9, 24, 16, 0, 0, DateTimeKind.Utc)
        )
        .ShouldBeTrue();

      await ShouldMatchSnapshot(FindWrite("Update schedules"), "machine-numbers-updatesch");
      await ShouldMatchSnapshot(FindWrite("Delete Parts"), "machine-numbers-delparts");
      FindWrite("Delete Pallets")?.Pallets.ShouldBeEmpty();
      await ShouldMatchSnapshot(FindWrite("Add Fixtures"), "machine-numbers-fixtures");
      await ShouldMatchSnapshot(FindWrite("Delete Fixtures"), "machine-numbers-del-fixtures");
      await ShouldMatchSnapshot(FindWrite("Add Parts"), "machine-numbers-parts");
      await ShouldMatchSnapshot(FindWrite("Add Schedules"), "machine-numbers-schedules");
    }
  }
}
