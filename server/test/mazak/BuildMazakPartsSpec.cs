/* Copyright (c) 2021, John Lenz

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
using BlackMaple.MachineFramework;
using System.Data;
using System.Linq;
using MazakMachineInterface;
using Xunit;
using NSubstitute;
using FluentAssertions;
using System.Collections.Immutable;

namespace MachineWatchTest
{
  public class BuildMazakPartsSpec
  {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BasicFromJob(bool useStartingOffset)
    {
      var job1 = CreateBasicStopsWithProg(
        uniq: "Job1",
        part: "Part1",
        numProc: 2,
        pals: new[] { new[] { 4, 5 }, new[] { 4, 5 } }
      );

      var job2 = CreateBasicStopsWithProg(
        uniq: "Job2",
        part: "Part2",
        numProc: 2,
        //process groups on the same pallet.
        pals: new[] { new[] { 4, 5 }, new[] { 4, 5 } }
      );

      var job3 = CreateBasicStopsWithProg(
        uniq: "Job3",
        part: "Part3",
        numProc: 1,
        pals: new[] { new[] { 20, 21 } }
      );

      var log = new List<string>();

      var dset = CreateReadSet();

      CreateProgram(dset, "1234");
      CreateFixture(dset, "unusedfixture");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1, job2, job3 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: useStartingOffset,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0)
        Assert.Fail(log[0]);

      CheckNewFixtures(
        pMap,
        new string[] { "F:3:1:1", "F:3:1:2", "F:3:2:1", },
        new[] { "unusedfixture", "Test" }
      );

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcessFromJob(trans, "Part1:3:1", 1, "F:3:1:1");
      CheckPartProcessFromJob(trans, "Part1:3:1", 2, "F:3:1:2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part2:3:2", 1, "F:3:1:1");
      CheckPartProcessFromJob(trans, "Part2:3:2", 2, "F:3:1:2");
      CheckPart(trans, "Part2:3:2", "Job2-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part3:3:3", 1, "F:3:2:1");
      CheckPart(trans, "Part3:3:3", "Job3-Path1-0");

      CheckPartProcess(trans, "Part4:3:4", 1, "F:3:2:1");
      CheckPart(trans, "Part4:3:4", "Job4-Path1-0");

      CheckPalletGroup(trans, 1, "F:3:1", 2, new int[] { 4, 5 });
      CheckPalletGroup(trans, 2, "F:3:2", 1, new int[] { 20, 21 });
      CheckPalletGroup(trans, 3, "F:3:3", 1, new int[] { 30, 31 });

      AssertPartsPalletsDeleted(trans);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromJobReuseOrIgnoreFixtures(bool useStartingOffset)
    {
      //proc 1 and proc 2 on same pallets
      var job1 = CreateBasicStopsWithProg(
        uniq: "Job1",
        part: "Part1",
        numProc: 2,
        pals: new[] { new[] { 10, 11, 12 }, new[] { 10, 11, 12 } }
      );

      var job2 = CreateBasicStopsWithProg(
        uniq: "Job2",
        part: "Part2",
        numProc: 2,
        pals: new[] { new[] { 10, 11, 12 }, new[] { 10, 11, 12 } }
      );

      var job3 = CreateBasicStopsWithProg(
        uniq: "Job3",
        part: "Part3",
        numProc: 1,
        pals: new[] { new[] { 30, 31 } }
      );

      var log = new List<string>();

      var dset = CreateReadSet();
      var savedParts = new HashSet<string>();

      CreateProgram(dset, "1234");
      CreateFixture(dset, "unusedfixture");

      CreateFixture(dset, "F:4:2:30:1");
      CreatePallet(dset, pal: 30, fix: "F:4:2:30", 1, group: 1);
      CreatePallet(dset, pal: 31, fix: "F:4:2:30", 1, group: 1);
      CreatePart(dset, "oldpart1", "oldpart1:1", 1, "F:4:2:30");
      savedParts.Add("oldpart1:1");

      string pal30fix;
      if (useStartingOffset)
      {
        pal30fix = "F:3:3:1";
      }
      else
      {
        // reuse the existing one one
        pal30fix = "F:4:2:30:1";
      }

      // pallets 4, 5, 6 doesn't match the exsting so shouldn't be used in either case
      CreateFixture(dset, "F:4:3:4:1");
      CreatePart(dset, "oldpart2", "oldpart2:2", 1, "F:4:3:4");
      savedParts.Add("oldpart2:2");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1, job2, job3 },
        3,
        dset,
        savedParts,
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: useStartingOffset,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0)
        Assert.Fail(log[0]);

      if (useStartingOffset)
      {
        CheckNewFixtures(
          pMap,
          new string[] { "F:3:2:1", "F:3:2:2", "F:3:3:1", },
          new[] { "unusedfixture", "Test", }
        );
      }
      else
      {
        CheckNewFixtures(
          pMap,
          new string[]
          {
            "F:3:2:1",
            "F:3:2:2",
            // don't create one for pallets 30 and 31 "F:3:4:1",
          },
          new[] { "unusedfixture", "Test" }
        );
      }

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcessFromJob(trans, "Part1:3:1", 1, "F:3:2:1");
      CheckPartProcessFromJob(trans, "Part1:3:1", 2, "F:3:2:2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part2:3:2", 1, "F:3:2:1");
      CheckPartProcessFromJob(trans, "Part2:3:2", 2, "F:3:2:2");
      CheckPart(trans, "Part2:3:2", "Job2-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part3:3:3", 1, pal30fix);
      CheckPart(trans, "Part3:3:3", "Job3-Path1-0");

      CheckPalletGroup(trans, 2, "F:3:2", 2, new int[] { 10, 11, 12 });
      if (useStartingOffset)
      {
        CheckPalletGroup(trans, 3, "F:3:3", 1, new int[] { 30, 31 });
      }

      AssertPartsPalletsDeleted(trans);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DifferentPallets(bool useStartingOffset)
    {
      //Test when processes have different pallet lists
      var job1 = CreateBasicStopsWithProg(
        uniq: "Job1",
        part: "Part1",
        numProc: 2,
        pals: new[] { new[] { 4, 5 }, new[] { 40, 50 } }
      );

      var job2 = CreateBasicStopsWithProg(
        uniq: "Job2",
        part: "Part2",
        numProc: 2,
        pals: new[] { new[] { 4, 5 }, new[] { 40, 50 } }
      );

      var job3 = CreateBasicStopsWithProg(
        uniq: "Job3",
        part: "Part3",
        numProc: 2,
        pals: new[] { new[] { 4, 5 }, new[] { 30, 31 } }
      );

      var log = new List<string>();

      var dset = new MazakTestData();
      CreateProgram(dset, "1234");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1, job2, job3 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: useStartingOffset,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0)
        Assert.Fail(log[0]);

      CheckNewFixtures(pMap, new string[] { "F:3:1:1", "F:3:2:2", "F:3:3:2", });

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcessFromJob(trans, "Part1:3:1", 1, "F:3:1:1");
      CheckPartProcessFromJob(trans, "Part1:3:1", 2, "F:3:2:2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part2:3:2", 1, "F:3:1:1");
      CheckPartProcessFromJob(trans, "Part2:3:2", 2, "F:3:2:2");
      CheckPart(trans, "Part2:3:2", "Job2-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part3:3:3", 1, "F:3:1:1");
      CheckPartProcessFromJob(trans, "Part3:3:3", 2, "F:3:3:2");
      CheckPart(trans, "Part3:3:3", "Job3-Path1-1-0");

      CheckSingleProcPalletGroup(trans, 1, "F:3:1:1", new int[] { 4, 5 });
      CheckSingleProcPalletGroup(trans, 2, "F:3:2:2", new int[] { 40, 50 });
      CheckSingleProcPalletGroup(trans, 3, "F:3:3:2", new int[] { 30, 31 });

      AssertPartsPalletsDeleted(trans);
    }

    [Fact]
    public void ManualFixtureAssignment()
    {
      //each process uses different faces
      var job1 = CreateBasicStopsWithProg(
        uniq: "Job1",
        part: "Part1",
        numProc: 2,
        pals: new[] { new[] { 4, 5 }, new[] { 4, 5 } },
        fixtures: new[] { ("fixAA", 1), ("fixAA", 2) }
      );

      var job2 = CreateBasicStopsWithProg(
        uniq: "Job2",
        part: "Part2",
        numProc: 2,
        pals: new[] { new[] { 4, 5 }, new[] { 4, 5 } },
        fixtures: new[] { ("fixAA", 1), ("fixAA", 2) }
      );

      //job3 uses separate fixture than job 4
      var job3 = CreateBasicStopsWithProg(
        uniq: "Job3",
        part: "Part3",
        numProc: 1,
        pals: new[] { new[] { 20, 21 } },
        fixtures: new[] { ("fix3", 1) }
      );

      var job4 = CreateBasicStopsWithProg(
        uniq: "Job4",
        part: "Part3",
        numProc: 1,
        pals: new[] { new[] { 20, 21 } },
        fixtures: new[] { ("fix4", 1) } // different than job3
      );

      var log = new List<string>();
      var dset = CreateReadSet();
      CreateProgram(dset, "1234");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1, job2, job3, job4 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: true,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0)
        Assert.Fail(log[0]);

      CheckNewFixtures(
        pMap,
        new string[] { "F:3:1:fixAA:1", "F:3:1:fixAA:2", "F:3:2:fix3:1", "F:3:3:fix4:1", },
        new[] { "Test" }
      );

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcessFromJob(trans, "Part1:3:1", 1, "F:3:1:fixAA:1");
      CheckPartProcessFromJob(trans, "Part1:3:1", 2, "F:3:1:fixAA:2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part2:3:2", 1, "F:3:1:fixAA:1");
      CheckPartProcessFromJob(trans, "Part2:3:2", 2, "F:3:1:fixAA:2");
      CheckPart(trans, "Part2:3:2", "Job2-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part3:3:3", 1, "F:3:2:fix3:1");
      CheckPart(trans, "Part3:3:3", "Job3-Path1-0");

      CheckPartProcessFromJob(trans, "Part3:3:4", 1, "F:3:3:fix4:1");
      CheckPart(trans, "Part3:3:4", "Job4-Path1-0");

      CheckPalletGroup(trans, 1, new[] { "F:3:1:fixAA:1", "F:3:1:fixAA:2" }, new int[] { 4, 5 });
      CheckPalletGroup(trans, 2, new[] { "F:3:2:fix3:1" }, new int[] { 20, 21 });
      CheckPalletGroup(trans, 3, new[] { "F:3:3:fix4:1" }, new int[] { 20, 21 });

      AssertPartsPalletsDeleted(trans);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SortsFixtureGroupsBySimStartingTime(bool useStartingOffset, bool sharePallets)
    {
      var job1 = CreateBasicStopsWithProg(
        uniq: "Job1",
        part: "Part1",
        numProc: 2,
        pals: new[] { new[] { 4, 5 }, new[] { 4, 5 } },
        simStart: new DateTime(2020, 08, 20, 3, 4, 5, DateTimeKind.Utc)
      );

      var job2 = CreateBasicStopsWithProg(
        uniq: "Job2",
        part: "Part2",
        numProc: 2,
        //process groups on the same pallet.
        pals: new[] { new[] { 4, 5 }, new[] { 4, 5 } },
        simStart: new DateTime(2020, 08, 10, 3, 4, 5, DateTimeKind.Utc)
      );

      var job3 = CreateBasicStopsWithProg(
        uniq: "Job3",
        part: "Part3",
        numProc: 2,
        simStart: new DateTime(2020, 08, 15, 3, 4, 5, DateTimeKind.Utc),
        pals: sharePallets
          ? new[] { new[] { 4, 5, 6 }, new[] { 4, 5, 6 } }
          : new[] { new[] { 10, 11 }, new[] { 10, 11 } }
      );

      // job3 is between job2 and job1 in simulated starting time, and if sharePallets is true also has an extra pallet 6
      var log = new List<string>();
      var dset = CreateReadSet();
      CreateProgram(dset, "1234");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1, job2, job3 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: useStartingOffset,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0)
        Assert.Fail(log[0]);

      string part1BaseFix,
        part2BaseFix,
        part3BaseFix;
      if (useStartingOffset && sharePallets)
      {
        // creates three different groups
        CheckNewFixtures(
          pMap,
          new string[] { "F:3:1:1", "F:3:1:2", "F:3:2:1", "F:3:2:2", "F:3:3:1", "F:3:3:2", },
          new[] { "Test" }
        );
        part2BaseFix = "F:3:1";
        part3BaseFix = "F:3:2";
        part1BaseFix = "F:3:3";
      }
      else
      {
        // creates only two groups
        CheckNewFixtures(
          pMap,
          new string[] { "F:3:1:1", "F:3:1:2", "F:3:2:1", "F:3:2:2", },
          new[] { "Test" }
        );
        part2BaseFix = "F:3:1";
        part3BaseFix = "F:3:2";
        part1BaseFix = "F:3:1";
      }

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcessFromJob(trans, "Part1:3:1", 1, part1BaseFix + ":1");
      CheckPartProcessFromJob(trans, "Part1:3:1", 2, part1BaseFix + ":2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part2:3:2", 1, part2BaseFix + ":1");
      CheckPartProcessFromJob(trans, "Part2:3:2", 2, part2BaseFix + ":2");
      CheckPart(trans, "Part2:3:2", "Job2-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part3:3:3", 1, part3BaseFix + ":1");
      CheckPartProcessFromJob(trans, "Part3:3:3", 2, part3BaseFix + ":2");
      CheckPart(trans, "Part3:3:3", "Job3-Path1-1-0");

      CheckPalletGroup(trans, 1, new[] { "F:3:1:1", "F:3:1:2" }, new int[] { 4, 5 });

      if (sharePallets)
      {
        CheckPalletGroup(trans, 2, new[] { "F:3:2:1", "F:3:2:2" }, new int[] { 4, 5, 6 });
      }
      else
      {
        CheckPalletGroup(trans, 2, new[] { "F:3:2:1", "F:3:2:2" }, new int[] { 10, 11 });
      }

      if (useStartingOffset && sharePallets)
      {
        CheckPalletGroup(trans, 3, new[] { "F:3:3:1", "F:3:3:2" }, new int[] { 4, 5 });
      }

      AssertPartsPalletsDeleted(trans);
    }

    [Fact]
    public void DeleteUnusedPartsPals()
    {
      var job1 = CreateBasicStopsWithProg(
        uniq: "Job1",
        part: "Part1",
        numProc: 2,
        pals: new[] { new[] { 4, 5 }, new[] { 40, 50 } }
      );

      var dset = CreateReadSet();
      CreateFixture(dset, "aaaa:1");
      CreatePart(dset, "uniq1", "part1:1:1", 1, "aaaa");
      CreatePart(dset, "uniq2", "part2:1:1", 1, "Test");
      CreatePallet(dset, 5, "aaaa", 1, group: 1); // this should be deleted since part1:1:1 is being deleted
      CreatePallet(dset, 6, "Test", 1, group: 2); // this should be kept because part2:1:1 is being kept
      CreateProgram(dset, "1234");

      var log = new List<string>();
      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1 },
        3,
        dset,
        new HashSet<string>() { "part2:1:1" },
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: false,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0)
        Assert.Fail(log[0]);

      var del = pMap.DeleteOldPalletRows();
      del.Pallets
        .Should()
        .BeEquivalentTo(
          new[]
          {
            new MazakPalletRow()
            {
              PalletNumber = 5,
              Fixture = "aaaa:1",
              Command = MazakWriteCommand.Delete,
              FixtureGroupV2 = 1
            }
          }
        );
      del.Parts.Should().BeEmpty();
      del.Fixtures.Should().BeEmpty();
      del.Schedules.Should().BeEmpty();

      del = pMap.DeleteOldPartRows();
      del.Parts
        .Should()
        .BeEquivalentTo(
          new[]
          {
            dset.TestParts[0] with
            {
              Command = MazakWriteCommand.Delete,
              TotalProcess = dset.TestParts[0].Processes.Count(),
              Processes = new List<MazakPartProcessRow>(),
            }
          },
          options => options.ComparingByMembers<MazakPartRow>()
        );
      del.Pallets.Should().BeEmpty();
      del.Fixtures.Should().BeEmpty();
      del.Schedules.Should().BeEmpty();
    }

    [Fact]
    public void ErrorsOnMissingProgram()
    {
      //Test when processes have different pallet lists
      var job1 = CreateBasicStopsWithProg(
        uniq: "Job1",
        part: "Part1",
        numProc: 2,
        pals: new[] { new[] { 4 }, new[] { 40 } }
      );

      var dset = CreateReadSet();

      var log = new List<string>();
      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: false,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) =>
        {
          if (p == "1234" && r == null)
          {
            return null;
          }
          else
          {
            throw new Exception("Unexpected program lookup");
          }
        },
        errors: log
      );

      log.Should()
        .BeEquivalentTo(
          new[]
          {
            // one for each process
            "Part Part1 program 1234 does not exist in the cell controller.",
          }
        );
    }

    [Fact]
    public void CreatesPrograms()
    {
      var job1 = CreateBasicStopsWithProg(
        uniq: "Job1",
        part: "Part1",
        numProc: 4,
        pals: new[] { new[] { 4 }, new[] { 10 }, new[] { 10 }, new[] { 3 } },
        // repeat program to check if only adds once
        progs: new[] { ("aaa", (int?)null), ("bbb", 7), ("ccc", 9), ("aaa", null) }
      );

      var log = new List<string>();
      var dset = new MazakTestData();

      CreatePart(dset, "OldJob", "Part1", 1, "fix", System.IO.Path.Combine("theprogdir", "rev7", "ccc.EIA"));

      CreateProgram(dset, System.IO.Path.Combine("theprogdir", "rev7", "ccc.EIA"), "Insight:7:ccc"); // 7 is used by OldJob part
      CreateProgram(dset, System.IO.Path.Combine("theprogdir", "rev8", "ccc.EIA"), "Insight:8:ccc"); // 8 is not used, should be deleted
      CreateProgram(dset, System.IO.Path.Combine("theprogdir", "rev9", "ccc.EIA"), "Insight:9:ccc"); // 9 is used by new job, should not be deleted
      CreateProgram(dset, System.IO.Path.Combine("theprogdir", "rev7", "ddd.EIA"), "Insight:7:ddd"); // latest revision of unused program, should be kept

      var lookupProgram = Substitute.For<Func<string, long?, ProgramRevision>>();
      lookupProgram("aaa", null).Returns(new ProgramRevision() { ProgramName = "aaa", Revision = 3, });
      lookupProgram("bbb", 7).Returns(new ProgramRevision() { ProgramName = "bbb", Revision = 7, });
      lookupProgram("ccc", 9).Returns(new ProgramRevision() { ProgramName = "ccc", Revision = 9 });

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakSmooth,
        useStartingOffsetForDueDate: false,
        fmsSettings: new FMSSettings(),
        lookupProgram: lookupProgram,
        errors: log
      );
      if (log.Count > 0)
        Assert.Fail(log[0]);

      var getProgramCt = Substitute.For<Func<string, long, string>>();
      getProgramCt("aaa", 3).Returns("aaa 3 ct");
      getProgramCt("bbb", 7).Returns("bbb 7 ct");

      pMap.AddFixtureAndProgramDatabaseRows(getProgramCt, "theprogdir")
        .Programs.Should()
        .BeEquivalentTo(
          new[]
          {
            new NewMazakProgram()
            {
              Command = MazakWriteCommand.Add,
              MainProgram = System.IO.Path.Combine("theprogdir", "rev3", "aaa.EIA"),
              Comment = "Insight:3:aaa",
              ProgramName = "aaa",
              ProgramRevision = 3,
              ProgramContent = "aaa 3 ct"
            },
            new NewMazakProgram()
            {
              Command = MazakWriteCommand.Add,
              MainProgram = System.IO.Path.Combine("theprogdir", "rev7", "bbb.EIA"),
              Comment = "Insight:7:bbb",
              ProgramName = "bbb",
              ProgramRevision = 7,
              ProgramContent = "bbb 7 ct"
            },
          }
        );
      pMap.DeleteFixtureAndProgramDatabaseRows()
        .Programs.Should()
        .BeEquivalentTo(
          new[]
          {
            new NewMazakProgram()
            {
              Command = MazakWriteCommand.Delete,
              MainProgram = System.IO.Path.Combine("theprogdir", "rev8", "ccc.EIA"),
              Comment = "Insight:8:ccc"
            }
            // ccc rev9 already exists, should not be added
          }
        );

      var trans = pMap.CreatePartPalletDatabaseRows();

      trans.Parts
        .First()
        .Processes.Select(p => (proc: p.ProcessNumber, prog: p.MainProgram))
        .Should()
        .BeEquivalentTo(
          new[]
          {
            (1, System.IO.Path.Combine("theprogdir", "rev3", "aaa.EIA")),
            (2, System.IO.Path.Combine("theprogdir", "rev7", "bbb.EIA")),
            (3, System.IO.Path.Combine("theprogdir", "rev9", "ccc.EIA")),
            (4, System.IO.Path.Combine("theprogdir", "rev3", "aaa.EIA")),
          }
        );
    }

    [Fact]
    public void ErrorsOnMissingManagedProgram()
    {
      var job1 = CreateBasicStopsWithProg(
        uniq: "Job1",
        part: "Part1",
        numProc: 2,
        pals: new[] { new[] { 4 }, new[] { 10 } },
        progs: new[] { ("aaa", (int?)null), ("bbb", 7) }
      );

      var log = new List<string>();
      var dset = new MazakTestData();

      // create bbb with older revision
      CreateProgram(dset, System.IO.Path.Combine("theprogdir", "rev6", "bbb.EIA"), "Insight:6:bbb");

      var lookupProgram = Substitute.For<Func<string, long?, ProgramRevision>>();
      lookupProgram("aaa", null).Returns(new ProgramRevision() { ProgramName = "aaa", Revision = 3, });
      lookupProgram("bbb", 7).Returns((ProgramRevision)null);

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakSmooth,
        useStartingOffsetForDueDate: false,
        fmsSettings: new FMSSettings(),
        lookupProgram: lookupProgram,
        errors: log
      );

      log.Should()
        .BeEquivalentTo(new[] { "Part Part1 program bbb rev7 does not exist in the cell controller.", });
    }

    #region Checking
    private record MazakTestData : MazakAllData
    {
      public List<MazakPartRow> TestParts { get; } = new List<MazakPartRow>();
      public List<MazakFixtureRow> TestFixtures { get; } = new List<MazakFixtureRow>();
      public List<MazakPalletRow> TestPallets { get; } = new List<MazakPalletRow>();
      public List<MazakScheduleRow> TestSchedules { get; } = new List<MazakScheduleRow>();
      public List<MazakProgramRow> TestPrograms { get; } = new List<MazakProgramRow>();

      public MazakTestData()
      {
        Schedules = TestSchedules;
        Parts = TestParts;
        Fixtures = TestFixtures;
        Pallets = TestPallets;
        MainPrograms = TestPrograms;
      }
    }

    private MazakTestData CreateReadSet()
    {
      var dset = new MazakTestData();
      CreateFixture(dset, "Test");
      return dset;
    }

    private void CreatePart(
      MazakTestData dset,
      string unique,
      string name,
      int numProc,
      string fix,
      string program = null
    )
    {
      var pRow = new MazakPartRow() { Comment = "comment -Path", PartName = name };
      dset.TestParts.Add(pRow);

      for (int proc = 1; proc <= numProc; proc++)
      {
        pRow.Processes.Add(
          new MazakPartProcessRow()
          {
            ProcessNumber = proc,
            Fixture = fix + ":" + proc.ToString(),
            PartName = name,
            MainProgram = program
          }
        );
      }
    }

    private void CreateFixture(MazakTestData dset, string name)
    {
      dset.TestFixtures.Add(new MazakFixtureRow() { Comment = "Insight", FixtureName = name });
    }

    private void CreatePallet(MazakTestData dset, int pal, string fix, int numProc, int group)
    {
      for (int i = 1; i <= numProc; i++)
      {
        dset.TestPallets.Add(
          new MazakPalletRow()
          {
            Fixture = fix + ":" + i.ToString(),
            PalletNumber = pal,
            FixtureGroupV2 = group
          }
        );
      }
    }

    private void CreateProgram(MazakTestData dset, string program, string comment = "")
    {
      dset.TestPrograms.Add(new MazakProgramRow() { MainProgram = program, Comment = comment });
    }

    private Job CreateBasicStopsWithProg(
      string uniq,
      string part,
      int numProc,
      int[][] pals,
      (string fix, int face)[] fixtures = null,
      DateTime? simStart = null,
      (string prog, int? rev)[] progs = null
    )
    {
      return new Job()
      {
        Cycles = 0,
        UniqueStr = uniq,
        PartName = part,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = Enumerable
          .Range(1, numProc)
          .Select(
            p =>
              new ProcessInfo()
              {
                Paths = ImmutableList.Create(
                  new ProcPathInfo()
                  {
                    PalletNums = ImmutableList.CreateRange(pals[p - 1]),
                    SimulatedStartingUTC = (p == 1 ? simStart : null) ?? DateTime.MinValue,
                    SimulatedAverageFlowTime = TimeSpan.Zero,
                    Load = ImmutableList.Create(1),
                    Unload = ImmutableList.Create(1),
                    Stops = ImmutableList.Create(
                      new MachiningStop()
                      {
                        StationGroup = "machine",
                        Stations = ImmutableList.Create(1),
                        Program = progs?[p - 1].prog ?? "1234",
                        ProgramRevision = progs?[p - 1].rev,
                        ExpectedCycleTime = TimeSpan.Zero,
                      }
                    ),
                    Fixture = fixtures?[p - 1].fix,
                    Face = fixtures?[p - 1].face,
                    PartsPerPallet = 1,
                    ExpectedLoadTime = TimeSpan.Zero,
                    ExpectedUnloadTime = TimeSpan.Zero,
                  }
                )
              }
          )
          .ToImmutableList(),
      };
    }

    private void CheckNewFixtures(
      MazakJobs map,
      ICollection<string> newFix,
      ICollection<string> delFix = null
    )
    {
      var add = newFix
        .Select(
          f =>
            new MazakFixtureRow()
            {
              FixtureName = f,
              Comment = "Insight",
              Command = MazakWriteCommand.Add
            }
        )
        .ToList();
      var del = (delFix ?? Enumerable.Empty<string>())
        .Select(
          f =>
            new MazakFixtureRow()
            {
              FixtureName = f,
              Comment = "Insight",
              Command = MazakWriteCommand.Delete
            }
        )
        .ToList();

      var actions = map.AddFixtureAndProgramDatabaseRows(
        (p, r) => throw new Exception("Unexpected program lookup"),
        "C:\\NCProgs"
      );
      actions.Fixtures.Should().BeEquivalentTo(add);
      actions.Schedules.Should().BeEmpty();
      actions.Parts.Should().BeEmpty();
      actions.Pallets.Should().BeEmpty();
      actions.Programs.Should().BeEmpty();

      actions = map.DeleteFixtureAndProgramDatabaseRows();
      actions.Fixtures.Should().BeEquivalentTo(del);
      actions.Schedules.Should().BeEmpty();
      actions.Parts.Should().BeEmpty();
      actions.Pallets.Should().BeEmpty();
      actions.Programs.Should().BeEmpty();
    }

    private void CheckPartProcess(MazakWriteData dset, string part, int proc, string fixture)
    {
      CheckPartProcess(dset, part, proc, fixture, "0000000000", "0000000000", "00000000");
    }

    private void CheckPartProcessFromJob(MazakWriteData dset, string part, int proc, string fixture)
    {
      //checks stuff created with AddBasicStopsWithProg
      CheckPartProcess(dset, part, proc, fixture, "1000000000", "1000000000", "10000000");
    }

    private void CheckPartProcess(
      MazakWriteData dset,
      string part,
      int proc,
      string fixture,
      string fix,
      string rem,
      string cut
    )
    {
      foreach (var mpart in dset.Parts)
      {
        if (mpart.PartName != part)
          continue;
        foreach (var row in mpart.Processes)
        {
          if (row.PartName == part && row.ProcessNumber == proc)
          {
            row.Fixture.Should().Be(fixture, because: "on " + part);
            row.FixLDS.Should().Be(fix, because: "on " + part);
            row.RemoveLDS.Should().Be(rem, because: "on " + part);
            row.CutMc.Should().Be(cut, because: "on " + part);
            mpart.Processes.Remove(row);
            break;
          }
        }
      }
    }

    private void CheckPart(MazakWriteData dset, string part, string comment)
    {
      foreach (var row in dset.Parts)
      {
        if (row.PartName == part)
        {
          Assert.Equal(comment, row.Comment);
          row.Processes.Should().BeEmpty();
          ((List<MazakPartRow>)dset.Parts).Remove(row);
          break;
        }
      }
    }

    private void CheckSingleProcPalletGroup(MazakWriteData dset, int groupNum, string fix, IList<int> pals)
    {
      int angle = groupNum * 1000;

      foreach (int pal in pals)
      {
        CheckPallet(dset, fix, pal, angle, groupNum);
      }
    }

    private void CheckPalletGroup(MazakWriteData dset, int groupNum, string fix, int numProc, IList<int> pals)
    {
      CheckPalletGroup(
        dset,
        groupNum,
        Enumerable.Range(1, numProc).Select(i => fix + ":" + i.ToString()),
        pals
      );
    }

    private void CheckPalletGroup(
      MazakWriteData dset,
      int groupNum,
      IEnumerable<string> fixs,
      IList<int> pals
    )
    {
      int angle = groupNum * 1000;

      foreach (int pal in pals)
      {
        foreach (var fix in fixs)
        {
          CheckPallet(dset, fix, pal, angle, groupNum);
        }
      }
    }

    private void CheckPallet(
      MazakWriteData dset,
      string fix,
      int pal,
      int expectedAngle,
      int expectedFixGroup
    )
    {
      foreach (var row in dset.Pallets.ToList())
      {
        if (row.PalletNumber == pal && row.Fixture == fix)
        {
          row.AngleV1.Should().Be(expectedAngle);
          row.FixtureGroupV2.Should().Be(expectedFixGroup);
          ((List<MazakPalletRow>)dset.Pallets).Remove(row);
        }
      }
    }

    private void AssertPartsPalletsDeleted(MazakWriteData dset)
    {
      foreach (var row in dset.Parts)
      {
        Assert.Fail("Extra part row: " + row.PartName);
      }

      foreach (var row in dset.Pallets)
      {
        Assert.Fail( "Extra pallet row: " + row.PalletNumber.ToString() + " " + row.Fixture);
      }

      foreach (var row in dset.Fixtures)
      {
        Assert.Fail("Extra fixture row: " + row.FixtureName);
      }
    }
    #endregion
  }
}
