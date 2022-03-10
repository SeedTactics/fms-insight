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

namespace MachineWatchTest
{
  public class BuildMazakPartsSpec
  {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BasicFromJob(bool useStartingOffset)
    {
      var job1 = new JobPlan("Job1", 2, new int[] { 1, 1 });
      job1.PartName = "Part1";

      //proc 1 and proc 2 on same pallets
      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(2, 1, "4");
      job1.AddProcessOnPallet(2, 1, "5");

      AddBasicStopsWithProg(job1);

      var job2 = new JobPlan("Job2", 2, new int[] { 1, 1 });
      job2.PartName = "Part2";

      //process groups on the same pallet.
      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(2, 1, "4");
      job2.AddProcessOnPallet(2, 1, "5");

      AddBasicStopsWithProg(job2);

      var job3 = new JobPlan("Job3", 1, new int[] { 1 });
      job3.PartName = "Part3";
      job3.AddProcessOnPallet(1, 1, "20");
      job3.AddProcessOnPallet(1, 1, "21");

      AddBasicStopsWithProg(job3);

      var log = new List<string>();

      var dset = CreateReadSet();

      CreateProgram(dset, "1234");
      CreateFixture(dset, "unusedfixture");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1.ToHistoricJob(), job2.ToHistoricJob(), job3.ToHistoricJob() },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: useStartingOffset,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      CheckNewFixtures(pMap,
        new string[] {
          "F:3:1:1",
          "F:3:1:2",
          "F:3:2:1",
        },
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
      var job1 = new JobPlan("Job1", 2, new int[] { 1, 1 });
      job1.PartName = "Part1";

      //proc 1 and proc 2 on same pallets
      job1.AddProcessOnPallet(1, 1, "10");
      job1.AddProcessOnPallet(1, 1, "11");
      job1.AddProcessOnPallet(1, 1, "12");
      job1.AddProcessOnPallet(2, 1, "10");
      job1.AddProcessOnPallet(2, 1, "11");
      job1.AddProcessOnPallet(2, 1, "12");

      AddBasicStopsWithProg(job1);

      var job2 = new JobPlan("Job2", 2, new int[] { 1, 1 });
      job2.PartName = "Part2";

      //process groups on the same pallet.
      job2.AddProcessOnPallet(1, 1, "10");
      job2.AddProcessOnPallet(1, 1, "11");
      job2.AddProcessOnPallet(1, 1, "12");
      job2.AddProcessOnPallet(2, 1, "10");
      job2.AddProcessOnPallet(2, 1, "11");
      job2.AddProcessOnPallet(2, 1, "12");

      AddBasicStopsWithProg(job2);

      var job3 = new JobPlan("Job3", 1, new int[] { 1 });
      job3.PartName = "Part3";
      job3.AddProcessOnPallet(1, 1, "30");
      job3.AddProcessOnPallet(1, 1, "31");

      AddBasicStopsWithProg(job3);

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
        new Job[] { job1.ToHistoricJob(), job2.ToHistoricJob(), job3.ToHistoricJob() },
        3,
        dset,
        savedParts,
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: useStartingOffset,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      if (useStartingOffset)
      {
        CheckNewFixtures(pMap,
          new string[] {
            "F:3:2:1",
            "F:3:2:2",
            "F:3:3:1",
          },
          new[] { "unusedfixture", "Test", }
        );
      }
      else
      {
        CheckNewFixtures(pMap,
          new string[] {
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
      var job1 = new JobPlan("Job1", 2, new int[] { 1, 1 });
      job1.PartName = "Part1";

      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(2, 1, "40");
      job1.AddProcessOnPallet(2, 1, "50");

      AddBasicStopsWithProg(job1);

      var job2 = new JobPlan("Job2", 2, new int[] { 1, 1 });
      job2.PartName = "Part2";

      //process groups on the same pallet.
      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(2, 1, "40");
      job2.AddProcessOnPallet(2, 1, "50");

      AddBasicStopsWithProg(job2);

      var job3 = new JobPlan("Job3", 2, new int[] { 1, 1 });
      job3.PartName = "Part3";

      job3.AddProcessOnPallet(1, 1, "4");
      job3.AddProcessOnPallet(1, 1, "5");
      job3.AddProcessOnPallet(2, 1, "30");
      job3.AddProcessOnPallet(2, 1, "31");

      AddBasicStopsWithProg(job3);

      var log = new List<string>();

      var dset = new MazakTestData();
      CreateProgram(dset, "1234");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1.ToHistoricJob(), job2.ToHistoricJob(), job3.ToHistoricJob() },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: useStartingOffset,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      CheckNewFixtures(pMap, new string[] {
        "F:3:1:1",
        "F:3:2:2",
        "F:3:3:2",
      });

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
      var job1 = new JobPlan("Job1", 2, new int[] { 1, 1 });
      job1.PartName = "Part1";

      //proc 1 and proc 2 on same pallets
      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(2, 1, "4");
      job1.AddProcessOnPallet(2, 1, "5");

      //each process uses different faces
      job1.SetFixtureFace(1, 1, "fixAA", 1);
      job1.SetFixtureFace(2, 1, "fixAA", 2);

      AddBasicStopsWithProg(job1);

      var job2 = new JobPlan("Job2", 2, new int[] { 1, 1 });
      job2.PartName = "Part2";

      //process groups on the same pallet.
      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(2, 1, "4");
      job2.AddProcessOnPallet(2, 1, "5");

      //each process uses different faces
      job2.SetFixtureFace(1, 1, "fixAA", 1);
      job2.SetFixtureFace(2, 1, "fixAA", 2);

      AddBasicStopsWithProg(job2);

      var job3 = new JobPlan("Job3", 1, new int[] { 1 });
      job3.PartName = "Part3";
      job3.AddProcessOnPallet(1, 1, "20");
      job3.AddProcessOnPallet(1, 1, "21");

      //job3 uses separate fixture than job 4, but same fixture and face for both procs
      job3.SetFixtureFace(1, 1, "fix3", 1);

      AddBasicStopsWithProg(job3);

      var job4 = new JobPlan("Job4", 1, new int[] { 1 });
      job4.PartName = "Part3";
      job4.AddProcessOnPallet(1, 1, "20");
      job4.AddProcessOnPallet(1, 1, "21");

      //job3 uses separate fixture than job 4
      job4.SetFixtureFace(1, 1, "fix4", 1);

      AddBasicStopsWithProg(job4);

      var log = new List<string>();
      var dset = CreateReadSet();
      CreateProgram(dset, "1234");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1.ToHistoricJob(), job2.ToHistoricJob(), job3.ToHistoricJob(), job4.ToHistoricJob() },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: true,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      CheckNewFixtures(pMap, new string[] {
        "F:3:1:fixAA:1",
        "F:3:1:fixAA:2",
        "F:3:2:fix3:1",
        "F:3:3:fix4:1",
      }, new[] { "Test" });

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
      var job1 = new JobPlan("Job1", 2, new int[] { 1, 1 });
      job1.PartName = "Part1";
      job1.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2020, 08, 20, 3, 4, 5, DateTimeKind.Utc));

      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(2, 1, "4");
      job1.AddProcessOnPallet(2, 1, "5");

      AddBasicStopsWithProg(job1);

      var job2 = new JobPlan("Job2", 2, new int[] { 1, 1 });
      job2.PartName = "Part2";
      job2.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2020, 08, 10, 3, 4, 5, DateTimeKind.Utc));

      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(2, 1, "4");
      job2.AddProcessOnPallet(2, 1, "5");

      AddBasicStopsWithProg(job2);

      var job3 = new JobPlan("Job3", 2, new int[] { 1, 1 });
      job3.PartName = "Part3";
      job3.SetSimulatedStartingTimeUTC(1, 1, new DateTime(2020, 08, 15, 3, 4, 5, DateTimeKind.Utc));

      if (sharePallets)
      {
        job3.AddProcessOnPallet(1, 1, "4");
        job3.AddProcessOnPallet(1, 1, "5");
        job3.AddProcessOnPallet(1, 1, "6");
        job3.AddProcessOnPallet(2, 1, "4");
        job3.AddProcessOnPallet(2, 1, "5");
        job3.AddProcessOnPallet(2, 1, "6");
      }
      else
      {
        job3.AddProcessOnPallet(1, 1, "10");
        job3.AddProcessOnPallet(1, 1, "11");
        job3.AddProcessOnPallet(2, 1, "10");
        job3.AddProcessOnPallet(2, 1, "11");
      }

      AddBasicStopsWithProg(job3);

      // job3 is between job2 and job1 in simulated starting time, and if sharePallets is true also has an extra pallet 6
      var log = new List<string>();
      var dset = CreateReadSet();
      CreateProgram(dset, "1234");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1.ToHistoricJob(), job2.ToHistoricJob(), job3.ToHistoricJob() },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: useStartingOffset,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      string part1BaseFix, part2BaseFix, part3BaseFix;
      if (useStartingOffset && sharePallets)
      {
        // creates three different groups
        CheckNewFixtures(pMap, new string[] {
          "F:3:1:1",
          "F:3:1:2",
          "F:3:2:1",
          "F:3:2:2",
          "F:3:3:1",
          "F:3:3:2",
        }, new[] { "Test" });
        part2BaseFix = "F:3:1";
        part3BaseFix = "F:3:2";
        part1BaseFix = "F:3:3";
      }
      else
      {
        // creates only two groups
        CheckNewFixtures(pMap, new string[] {
          "F:3:1:1",
          "F:3:1:2",
          "F:3:2:1",
          "F:3:2:2",
        }, new[] { "Test" });
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
      var job1 = new JobPlan("Job1", 2, new int[] { 1, 1 });
      job1.PartName = "Part1";

      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(2, 1, "40");
      job1.AddProcessOnPallet(2, 1, "50");

      AddBasicStopsWithProg(job1);

      var dset = CreateReadSet();
      CreateFixture(dset, "aaaa:1");
      CreatePart(dset, "uniq1", "part1:1:1", 1, "aaaa");
      CreatePart(dset, "uniq2", "part2:1:1", 1, "Test");
      CreatePallet(dset, 5, "aaaa", 1, group: 1); // this should be deleted since part1:1:1 is being deleted
      CreatePallet(dset, 6, "Test", 1, group: 2); // this should be kept because part2:1:1 is being kept
      CreateProgram(dset, "1234");

      var log = new List<string>();
      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1.ToHistoricJob() },
        3,
        dset,
        new HashSet<string>() { "part2:1:1" },
        MazakDbType.MazakVersionE,
        useStartingOffsetForDueDate: false,
        fmsSettings: new FMSSettings(),
        lookupProgram: (p, r) => throw new Exception("Unexpected program lookup"),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      var del = pMap.DeleteOldPalletRows();
      del.Pallets.Should().BeEquivalentTo(new[] {
        new MazakPalletRow()
        {
          PalletNumber = 5,
          Fixture = "aaaa:1",
          Command = MazakWriteCommand.Delete,
          FixtureGroupV2 = 1
        }
      });
      del.Parts.Should().BeEmpty();
      del.Fixtures.Should().BeEmpty();
      del.Schedules.Should().BeEmpty();

      del = pMap.DeleteOldPartRows();
      del.Parts.Should().BeEquivalentTo(new[] {
        dset.TestParts[0] with {
          Command = MazakWriteCommand.Delete,
          TotalProcess = dset.TestParts[0].Processes.Count(),
          Processes = new List<MazakPartProcessRow>(),
        }
      }, options => options.ComparingByMembers<MazakPartRow>());
      del.Pallets.Should().BeEmpty();
      del.Fixtures.Should().BeEmpty();
      del.Schedules.Should().BeEmpty();
    }

    [Fact]
    public void ErrorsOnMissingProgram()
    {
      //Test when processes have different pallet lists
      var job1 = new JobPlan("Job1", 2, new int[] { 1, 1 });
      job1.PartName = "Part1";

      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(2, 1, "40");

      AddBasicStopsWithProg(job1);

      var dset = CreateReadSet();

      var log = new List<string>();
      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1.ToHistoricJob() },
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

      log.Should().BeEquivalentTo(new[] {
				// one for each process
				"Part Part1 program 1234 does not exist in the cell controller.",
      });
    }

    [Fact]
    public void CreatesPrograms()
    {
      var job1 = new JobPlan("Job1", 4);
      job1.PartName = "Part1";
      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(2, 1, "10");
      job1.AddProcessOnPallet(3, 1, "10");
      job1.AddProcessOnPallet(4, 1, "3");

      AddBasicStopsWithProg(job1);
      job1.GetMachiningStop(1, 1).First().ProgramName = "aaa";
      job1.GetMachiningStop(1, 1).First().ProgramRevision = null;
      job1.GetMachiningStop(2, 1).First().ProgramName = "bbb";
      job1.GetMachiningStop(2, 1).First().ProgramRevision = 7;
      job1.GetMachiningStop(3, 1).First().ProgramName = "ccc";
      job1.GetMachiningStop(3, 1).First().ProgramRevision = 9;
      job1.GetMachiningStop(4, 1).First().ProgramName = "aaa"; // repeat program to check if only adds once
      job1.GetMachiningStop(4, 1).First().ProgramRevision = null;

      var log = new List<string>();
      var dset = new MazakTestData();

      CreatePart(dset, "OldJob", "Part1", 1, "fix", System.IO.Path.Combine("theprogdir", "ccc_rev7.EIA"));

      CreateProgram(dset, System.IO.Path.Combine("theprogdir", "ccc_rev7.EIA"), "Insight:7:ccc"); // 7 is used by OldJob part
      CreateProgram(dset, System.IO.Path.Combine("theprogdir", "ccc_rev8.EIA"), "Insight:8:ccc"); // 8 is not used, should be deleted
      CreateProgram(dset, System.IO.Path.Combine("theprogdir", "ccc_rev9.EIA"), "Insight:9:ccc"); // 9 is used by new job, should not be deleted
      CreateProgram(dset, System.IO.Path.Combine("theprogdir", "ddd_rev7.EIA"), "Insight:7:ddd"); // latest revision of unused program, should be kept

      var lookupProgram = Substitute.For<Func<string, long?, ProgramRevision>>();
      lookupProgram("aaa", null).Returns(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 3,
      });
      lookupProgram("bbb", 7).Returns(new ProgramRevision()
      {
        ProgramName = "bbb",
        Revision = 7,
      });
      lookupProgram("ccc", 9).Returns(new ProgramRevision()
      {
        ProgramName = "ccc",
        Revision = 9
      });

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1.ToHistoricJob() },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakSmooth,
        useStartingOffsetForDueDate: false,
        fmsSettings: new FMSSettings(),
        lookupProgram: lookupProgram,
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      var getProgramCt = Substitute.For<Func<string, long, string>>();
      getProgramCt("aaa", 3).Returns("aaa 3 ct");
      getProgramCt("bbb", 7).Returns("bbb 7 ct");

      pMap.AddFixtureAndProgramDatabaseRows(getProgramCt, "theprogdir")
        .Programs.Should().BeEquivalentTo(new[] {
          new NewMazakProgram() {
            Command = MazakWriteCommand.Add,
            MainProgram = System.IO.Path.Combine("theprogdir", "aaa_rev3.EIA"),
            Comment = "Insight:3:aaa",
            ProgramName = "aaa",
            ProgramRevision = 3,
            ProgramContent = "aaa 3 ct"
          },
          new NewMazakProgram() {
            Command = MazakWriteCommand.Add,
            MainProgram = System.IO.Path.Combine("theprogdir", "bbb_rev7.EIA"),
            Comment = "Insight:7:bbb",
            ProgramName = "bbb",
            ProgramRevision = 7,
            ProgramContent = "bbb 7 ct"
          },
        });
      pMap.DeleteFixtureAndProgramDatabaseRows()
        .Programs.Should().BeEquivalentTo(new[] {
          new NewMazakProgram() {
            Command = MazakWriteCommand.Delete,
            MainProgram = System.IO.Path.Combine("theprogdir", "ccc_rev8.EIA"),
            Comment = "Insight:8:ccc"
          }
          // ccc rev9 already exists, should not be added
        });

      var trans = pMap.CreatePartPalletDatabaseRows();

      trans.Parts.First().Processes.Select(p => (proc: p.ProcessNumber, prog: p.MainProgram)).Should().BeEquivalentTo(new[] {
        (1, System.IO.Path.Combine("theprogdir", "aaa_rev3.EIA")),
        (2, System.IO.Path.Combine("theprogdir", "bbb_rev7.EIA")),
        (3, System.IO.Path.Combine("theprogdir", "ccc_rev9.EIA")),
        (4, System.IO.Path.Combine("theprogdir", "aaa_rev3.EIA")),
      });
    }

    [Fact]
    public void ErrorsOnMissingManagedProgram()
    {
      var job1 = new JobPlan("Job1", 2);
      job1.PartName = "Part1";
      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(2, 1, "10");

      AddBasicStopsWithProg(job1);
      job1.GetMachiningStop(1, 1).First().ProgramName = "aaa";
      job1.GetMachiningStop(1, 1).First().ProgramRevision = null;
      job1.GetMachiningStop(2, 1).First().ProgramName = "bbb";
      job1.GetMachiningStop(2, 1).First().ProgramRevision = 7;

      var log = new List<string>();
      var dset = new MazakTestData();

      // create bbb with older revision
      CreateProgram(dset, System.IO.Path.Combine("theprogdir", "bbb_rev6.EIA"), "Insight:6:bbb");

      var lookupProgram = Substitute.For<Func<string, long?, ProgramRevision>>();
      lookupProgram("aaa", null).Returns(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 3,
      });
      lookupProgram("bbb", 7).Returns((ProgramRevision)null);

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new Job[] { job1.ToHistoricJob() },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakSmooth,
        useStartingOffsetForDueDate: false,
        fmsSettings: new FMSSettings(),
        lookupProgram: lookupProgram,
        errors: log
      );

      log.Should().BeEquivalentTo(new[] {
        "Part Part1 program bbb rev7 does not exist in the cell controller.",
      });
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

    private void CreatePart(MazakTestData dset, string unique, string name, int numProc, string fix, string program = null)
    {
      var pRow = new MazakPartRow() { Comment = "comment -Path", PartName = name };
      dset.TestParts.Add(pRow);

      for (int proc = 1; proc <= numProc; proc++)
      {
        pRow.Processes.Add(new MazakPartProcessRow()
        {
          ProcessNumber = proc,
          Fixture = fix + ":" + proc.ToString(),
          PartName = name,
          MainProgram = program
        });
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
        dset.TestPallets.Add(new MazakPalletRow() { Fixture = fix + ":" + i.ToString(), PalletNumber = pal, FixtureGroupV2 = group });
      }
    }

    private void CreateProgram(MazakTestData dset, string program, string comment = "")
    {
      dset.TestPrograms.Add(new MazakProgramRow() { MainProgram = program, Comment = comment });
    }

    private void AddBasicStopsWithProg(JobPlan job)
    {
      for (int proc = 1; proc <= job.NumProcesses; proc++)
      {
        for (int path = 1; path <= job.GetNumPaths(proc); path++)
        {
          job.AddLoadStation(proc, path, 1);
          job.AddUnloadStation(proc, path, 1);
          var stop = new JobMachiningStop("machine");
          stop.Stations.Add(1);
          stop.ProgramName = "1234";
          job.AddMachiningStop(proc, path, stop);
        }
      }
    }

    private void CheckNewFixtures(MazakJobs map, ICollection<string> newFix, ICollection<string> delFix = null)
    {
      var add = newFix
        .Select(f => new MazakFixtureRow()
        {
          FixtureName = f,
          Comment = "Insight",
          Command = MazakWriteCommand.Add
        }).ToList();
      var del = (delFix ?? Enumerable.Empty<string>())
        .Select(f => new MazakFixtureRow()
        {
          FixtureName = f,
          Comment = "Insight",
          Command = MazakWriteCommand.Delete
        }).ToList();

      var actions = map.AddFixtureAndProgramDatabaseRows((p, r) => throw new Exception("Unexpected program lookup"), "C:\\NCProgs");
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

    private void CheckPartProcess(MazakWriteData dset, string part, int proc, string fixture,
                                  string fix, string rem, string cut)
    {
      foreach (var mpart in dset.Parts)
      {
        if (mpart.PartName != part) continue;
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
      CheckPalletGroup(dset, groupNum,
        Enumerable.Range(1, numProc).Select(i => fix + ":" + i.ToString()),
        pals);
    }

    private void CheckPalletGroup(MazakWriteData dset, int groupNum, IEnumerable<string> fixs, IList<int> pals)
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

    private void CheckPallet(MazakWriteData dset, string fix, int pal, int expectedAngle, int expectedFixGroup)
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
        Assert.True(false, "Extra part row: " + row.PartName);
      }

      foreach (var row in dset.Pallets)
      {
        Assert.True(false, "Extra pallet row: " + row.PalletNumber.ToString() + " " + row.Fixture);
      }

      foreach (var row in dset.Fixtures)
      {
        Assert.True(false, "Extra fixture row: " + row.FixtureName);
      }
    }
    #endregion
  }
}