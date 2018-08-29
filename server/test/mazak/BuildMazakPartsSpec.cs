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
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using System.Data;
using System.Linq;
using MazakMachineInterface;
using Xunit;
using FluentAssertions;

namespace MachineWatchTest
{
  public class BuildMazakPartsSpec
  {
    [Fact]
    public void BasicFromTemplate()
    {
      //Test everything copied from the template
      // The JobPlan's have only 1 process but the template has 2 processes so
      // the resulting parts should have 2 processes

      var job1 = new JobPlan("Job1", 1, new int[] { 2 });
      job1.PartName = "Part1";
      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(1, 2, "10");
      job1.AddProcessOnPallet(1, 2, "11");
      job1.AddProcessOnPallet(1, 2, "12");
      job1.AddLoadStation(1, 1, 1);
      job1.AddLoadStation(1, 1, 2);
      job1.AddLoadStation(1, 2, 5);
      job1.AddUnloadStation(1, 1, 4);
      job1.AddUnloadStation(1, 2, 3);
      var stop = new JobMachiningStop("machine");
      stop.AddProgram(1, "");
      job1.AddMachiningStop(1, 1, stop);
      stop = new JobMachiningStop("machine");
      stop.AddProgram(3, "");
      stop.AddProgram(4, "");
      job1.AddMachiningStop(1, 2, stop);

      var job2 = new JobPlan("Job2", 1, new int[] { 2 });
      job2.PartName = "Part2";
      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(1, 2, "10");
      job2.AddProcessOnPallet(1, 2, "11");
      job2.AddProcessOnPallet(1, 2, "12");

      var job3 = new JobPlan("Job3", 1, new int[] { 1 });
      job3.PartName = "Part3";
      job3.AddProcessOnPallet(1, 1, "20");
      job3.AddProcessOnPallet(1, 1, "21");

      var job4 = new JobPlan("Job4", 1, new int[] { 1 });
      job4.PartName = "Part4";
      job4.AddProcessOnPallet(1, 1, "20");
      job4.AddProcessOnPallet(1, 1, "21");

      var log = new List<string>();

      var dset = CreateReadSet();

      CreatePart(dset, "Job1", "Part1", 2, "Test");
      CreatePart(dset, "Job2", "Part2", 2, "Test");
      CreatePart(dset, "Job3", "Part3", 1, "Test");
      CreatePart(dset, "Job4", "Part4", 1, "Test");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new JobPlan[] { job1, job2, job3, job4 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        checkPalletsUsedOnce: false,
        fmsSettings: new FMSSettings(),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      CheckNewFixtures(pMap, new string[] {
        "Fixt:3:0:4:1",
        "Fixt:3:0:4:2",
        "Fixt:3:1:10:1",
        "Fixt:3:1:10:2",
        "Fixt:3:2:20:1"
      }, new[] { "Test" });

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcess(trans, "Part1:3:1", 1, "Fixt:3:0:4:1", "1200000000", "0004000000", "10000000");
      CheckPartProcess(trans, "Part1:3:1", 2, "Fixt:3:0:4:2", "1200000000", "0004000000", "10000000");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcess(trans, "Part1:3:2", 1, "Fixt:3:1:10:1", "0000500000", "0030000000", "00340000");
      CheckPartProcess(trans, "Part1:3:2", 2, "Fixt:3:1:10:2", "0000500000", "0030000000", "00340000");
      CheckPart(trans, "Part1:3:2", "Job1-Path2-2-0");

      CheckPartProcess(trans, "Part2:3:1", 1, "Fixt:3:0:4:1");
      CheckPartProcess(trans, "Part2:3:1", 2, "Fixt:3:0:4:2");
      CheckPart(trans, "Part2:3:1", "Job2-Path1-1-0");

      CheckPartProcess(trans, "Part2:3:2", 1, "Fixt:3:1:10:1");
      CheckPartProcess(trans, "Part2:3:2", 2, "Fixt:3:1:10:2");
      CheckPart(trans, "Part2:3:2", "Job2-Path2-2-0");

      CheckPartProcess(trans, "Part3:3:1", 1, "Fixt:3:2:20:1");
      CheckPart(trans, "Part3:3:1", "Job3-Path1-0");

      CheckPartProcess(trans, "Part4:3:1", 1, "Fixt:3:2:20:1");
      CheckPart(trans, "Part4:3:1", "Job4-Path1-0");

      CheckPalletGroup(trans, 31, "Fixt:3:0:4", 2, new int[] { 4, 5 });
      CheckPalletGroup(trans, 32, "Fixt:3:1:10", 2, new int[] { 10, 11, 12 });
      CheckPalletGroup(trans, 33, "Fixt:3:2:20", 1, new int[] { 20, 21 });

      AssertPartsPalletsDeleted(trans);
    }

    [Fact]
    public void UseExistingFixture()
    {
      //again, mazak parts are created from template, not the jobplan
      var job1 = new JobPlan("Job1", 1, new int[] { 2 });
      job1.PartName = "Part1";
      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(1, 2, "10");
      job1.AddProcessOnPallet(1, 2, "11");
      job1.AddProcessOnPallet(1, 2, "12");

      var job2 = new JobPlan("Job2", 1, new int[] { 2 });
      job2.PartName = "Part2";
      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(1, 2, "10");
      job2.AddProcessOnPallet(1, 2, "11");
      job2.AddProcessOnPallet(1, 2, "12");

      var job3 = new JobPlan("Job3", 1, new int[] { 1 });
      job3.PartName = "Part3";
      job3.AddProcessOnPallet(1, 1, "20");
      job3.AddProcessOnPallet(1, 1, "21");

      var job4 = new JobPlan("Job4", 1, new int[] { 1 });
      job4.PartName = "Part4";
      job4.AddProcessOnPallet(1, 1, "20");
      job4.AddProcessOnPallet(1, 1, "21");

      var log = new List<string>();

      var dset = CreateReadSet();

      CreatePart(dset, "Job1", "Part1", 2, "Test");
      CreatePart(dset, "Job2", "Part2", 2, "Test");
      CreatePart(dset, "Job3", "Part3", 1, "Test");
      CreatePart(dset, "Job4", "Part4", 1, "Test");

      //Create fixtures which match for Parts 1 and 2.
      var savedParts = new HashSet<string>();
      CreateFixture(dset, "Fixt:2:0:4:1");
      CreateFixture(dset, "Fixt:2:0:4:2");
      CreateFixture(dset, "Fixt:2:0:10:1");
      CreateFixture(dset, "Fixt:2:0:10:2");
      CreatePart(dset, "Job1.0", "Part1:2:1", 2, "Fixt:2:0:4");
      savedParts.Add("Part1:2:1");
      CreatePart(dset, "Job1.0", "Part1:2:2", 2, "Fixt:2:0:10");
      savedParts.Add("Part1:2:2");
      CreatePallet(dset, 4, "Fixt:2:0:4", 2);
      CreatePallet(dset, 5, "Fixt:2:0:4", 2);
      CreatePallet(dset, 10, "Fixt:2:0:10", 2);
      CreatePallet(dset, 11, "Fixt:2:0:10", 2);
      CreatePallet(dset, 12, "Fixt:2:0:10", 2);

      //Create several fixtures which almost but not quite match for parts 3 and 4.

      //group with an extra pallet
      CreateFixture(dset, "Fixt:1:0:20:1");
      CreatePart(dset, "Job3.0", "Part3:1:1", 1, "Fixt:1:0:20");
      savedParts.Add("Part3:1:1");
      CreatePallet(dset, 20, "Fixt:1:0:20", 1);
      CreatePallet(dset, 21, "Fixt:1:0:20", 1);
      CreatePallet(dset, 22, "Fixt:1:0:20", 1);

      //group with a missing pallet
      CreateFixture(dset, "Fixt:7:0:20:1");
      CreatePart(dset, "Job3.1", "Part3:7:1", 1, "Fixt:7:0:20");
      savedParts.Add("Part3:7:1");
      CreatePallet(dset, 20, "Fixt:7:0:20", 1);

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new JobPlan[] { job1, job2, job3, job4 },
        3,
        dset,
        savedParts,
        MazakDbType.MazakVersionE,
        checkPalletsUsedOnce: false,
        fmsSettings: new FMSSettings(),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      CheckNewFixtures(pMap, new string[] {
        "Fixt:3:2:20:1"
      }, new[] { "Test" });

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcess(trans, "Part1:3:1", 1, "Fixt:2:0:4:1");
      CheckPartProcess(trans, "Part1:3:1", 2, "Fixt:2:0:4:2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcess(trans, "Part1:3:2", 1, "Fixt:2:0:10:1");
      CheckPartProcess(trans, "Part1:3:2", 2, "Fixt:2:0:10:2");
      CheckPart(trans, "Part1:3:2", "Job1-Path2-2-0");

      CheckPartProcess(trans, "Part2:3:1", 1, "Fixt:2:0:4:1");
      CheckPartProcess(trans, "Part2:3:1", 2, "Fixt:2:0:4:2");
      CheckPart(trans, "Part2:3:1", "Job2-Path1-1-0");

      CheckPartProcess(trans, "Part2:3:2", 1, "Fixt:2:0:10:1");
      CheckPartProcess(trans, "Part2:3:2", 2, "Fixt:2:0:10:2");
      CheckPart(trans, "Part2:3:2", "Job2-Path2-2-0");

      CheckPartProcess(trans, "Part3:3:1", 1, "Fixt:3:2:20:1");
      CheckPart(trans, "Part3:3:1", "Job3-Path1-0");

      CheckPartProcess(trans, "Part4:3:1", 1, "Fixt:3:2:20:1");
      CheckPart(trans, "Part4:3:1", "Job4-Path1-0");

      CheckPalletGroup(trans, 33, "Fixt:3:2:20", 1, new int[] { 20, 21 });

      AssertPartsPalletsDeleted(trans);
    }

    [Fact]
    public void MultiProcess()
    {
      //A test where Jobs have different number of processes but the same pallet list
      //again, mazak parts are created from template, not the jobplan

      var job1 = new JobPlan("Job1", 1, new int[] { 2 });
      job1.PartName = "Part1";
      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(1, 2, "10");
      job1.AddProcessOnPallet(1, 2, "11");
      job1.AddProcessOnPallet(1, 2, "12");

      var job2 = new JobPlan("Job2", 1, new int[] { 2 });
      job2.PartName = "Part2";
      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(1, 2, "10");
      job2.AddProcessOnPallet(1, 2, "11");
      job2.AddProcessOnPallet(1, 2, "12");

      var job3 = new JobPlan("Job3", 1, new int[] { 1 });
      job3.PartName = "Part3";
      job3.AddProcessOnPallet(1, 1, "20");
      job3.AddProcessOnPallet(1, 1, "21");

      var job4 = new JobPlan("Job4", 1, new int[] { 1 });
      job4.PartName = "Part4";
      job4.AddProcessOnPallet(1, 1, "20");
      job4.AddProcessOnPallet(1, 1, "21");

      var log = new List<string>();

      var dset = CreateReadSet();

      CreatePart(dset, "Job1", "Part1", 2, "Test");
      CreatePart(dset, "Job2", "Part2", 3, "Test");
      CreatePart(dset, "Job3", "Part3", 1, "Test");
      CreatePart(dset, "Job4", "Part4", 1, "Test");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new JobPlan[] { job1, job2, job3, job4 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        checkPalletsUsedOnce: false,
        fmsSettings: new FMSSettings(),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      CheckNewFixtures(pMap, new string[] {
        "Fixt:3:0:4:1",
        "Fixt:3:0:4:2",
        "Fixt:3:0:4:3",
        "Fixt:3:1:10:1",
        "Fixt:3:1:10:2",
        "Fixt:3:1:10:3",
        "Fixt:3:2:20:1"
      }, new[] { "Test" });

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcess(trans, "Part1:3:1", 1, "Fixt:3:0:4:1");
      CheckPartProcess(trans, "Part1:3:1", 2, "Fixt:3:0:4:2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcess(trans, "Part1:3:2", 1, "Fixt:3:1:10:1");
      CheckPartProcess(trans, "Part1:3:2", 2, "Fixt:3:1:10:2");
      CheckPart(trans, "Part1:3:2", "Job1-Path2-2-0");

      CheckPartProcess(trans, "Part2:3:1", 1, "Fixt:3:0:4:1");
      CheckPartProcess(trans, "Part2:3:1", 2, "Fixt:3:0:4:2");
      CheckPartProcess(trans, "Part2:3:1", 3, "Fixt:3:0:4:3");
      CheckPart(trans, "Part2:3:1", "Job2-Path1-1-1-0");

      CheckPartProcess(trans, "Part2:3:2", 1, "Fixt:3:1:10:1");
      CheckPartProcess(trans, "Part2:3:2", 2, "Fixt:3:1:10:2");
      CheckPartProcess(trans, "Part2:3:2", 3, "Fixt:3:1:10:3");
      CheckPart(trans, "Part2:3:2", "Job2-Path2-2-2-0");

      CheckPartProcess(trans, "Part3:3:1", 1, "Fixt:3:2:20:1");
      CheckPart(trans, "Part3:3:1", "Job3-Path1-0");

      CheckPartProcess(trans, "Part4:3:1", 1, "Fixt:3:2:20:1");
      CheckPart(trans, "Part4:3:1", "Job4-Path1-0");

      CheckPalletGroup(trans, 31, "Fixt:3:0:4", 3, new int[] { 4, 5 });
      CheckPalletGroup(trans, 32, "Fixt:3:1:10", 3, new int[] { 10, 11, 12 });
      CheckPalletGroup(trans, 33, "Fixt:3:2:20", 1, new int[] { 20, 21 });

      AssertPartsPalletsDeleted(trans);

    }

    [Fact]
    public void NonOverlappingPallets()
    {
      CheckNonOverlapping(false);

      try
      {

        CheckNonOverlapping(true);

        Assert.True(false, "Was expecting an exception");

      }
      catch (Exception ex)
      {
        Assert.Equal("Invalid pallet->part mapping. Part1-1 and Part2-1 do not " +
                        "have matching pallet lists.  Part1-1 is assigned to pallets 4,5" +
                        " and Part2-1 is assigned to pallets 4,5,6",
                        ex.Message);
      }

    }

    private void CheckNonOverlapping(bool checkPalletUsedOnce)
    {
      var job1 = new JobPlan("Job1", 1, new int[] { 2 });
      job1.PartName = "Part1";
      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(1, 2, "10");
      job1.AddProcessOnPallet(1, 2, "11");
      job1.AddProcessOnPallet(1, 2, "12");

      var job2 = new JobPlan("Job2", 1, new int[] { 2 });
      job2.PartName = "Part2";
      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(1, 1, "6");
      job2.AddProcessOnPallet(1, 2, "10");
      job2.AddProcessOnPallet(1, 2, "11");
      job2.AddProcessOnPallet(1, 2, "12");

      var log = new List<string>();

      var dset = CreateReadSet();

      CreatePart(dset, "Job1", "Part1", 2, "Test");
      CreatePart(dset, "Job2", "Part2", 2, "Test");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new JobPlan[] { job1, job2 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        checkPalletsUsedOnce: checkPalletUsedOnce,
        fmsSettings: new FMSSettings(),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      CheckNewFixtures(pMap, new string[] {
        "Fixt:3:0:4:1",
        "Fixt:3:0:4:2",
        "Fixt:3:1:10:1",
        "Fixt:3:1:10:2",
        "Fixt:3:2:4:1",
        "Fixt:3:2:4:2",
      }, new[] { "Test" });

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcess(trans, "Part1:3:1", 1, "Fixt:3:0:4:1");
      CheckPartProcess(trans, "Part1:3:1", 2, "Fixt:3:0:4:2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcess(trans, "Part1:3:2", 1, "Fixt:3:1:10:1");
      CheckPartProcess(trans, "Part1:3:2", 2, "Fixt:3:1:10:2");
      CheckPart(trans, "Part1:3:2", "Job1-Path2-2-0");

      CheckPartProcess(trans, "Part2:3:1", 1, "Fixt:3:2:4:1");
      CheckPartProcess(trans, "Part2:3:1", 2, "Fixt:3:2:4:2");
      CheckPart(trans, "Part2:3:1", "Job2-Path1-1-0");

      CheckPartProcess(trans, "Part2:3:2", 1, "Fixt:3:1:10:1");
      CheckPartProcess(trans, "Part2:3:2", 2, "Fixt:3:1:10:2");
      CheckPart(trans, "Part2:3:2", "Job2-Path2-2-0");

      CheckPalletGroup(trans, 31, "Fixt:3:0:4", 2, new int[] { 4, 5 });
      CheckPalletGroup(trans, 32, "Fixt:3:1:10", 2, new int[] { 10, 11, 12 });
      CheckPalletGroup(trans, 33, "Fixt:3:2:4", 2, new int[] { 4, 5, 6 });

      AssertPartsPalletsDeleted(trans);
    }

    [Fact]
    public void BasicFromJob()
    {
      var job1 = new JobPlan("Job1", 2, new int[] { 2, 2 });
      job1.PartName = "Part1";
      job1.SetPathGroup(1, 1, 1);
      job1.SetPathGroup(1, 2, 2);
      job1.SetPathGroup(2, 1, 1);
      job1.SetPathGroup(2, 2, 2);

      //proc 1 and proc 2 on same pallets
      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(1, 2, "10");
      job1.AddProcessOnPallet(1, 2, "11");
      job1.AddProcessOnPallet(1, 2, "12");
      job1.AddProcessOnPallet(2, 1, "4");
      job1.AddProcessOnPallet(2, 1, "5");
      job1.AddProcessOnPallet(2, 2, "10");
      job1.AddProcessOnPallet(2, 2, "11");
      job1.AddProcessOnPallet(2, 2, "12");

      AddBasicStopsWithProg(job1);

      var job2 = new JobPlan("Job2", 2, new int[] { 2, 2 });
      job2.PartName = "Part2";

      //make path groups twisted
      job2.SetPathGroup(1, 1, 1);
      job2.SetPathGroup(1, 2, 2);
      job2.SetPathGroup(2, 1, 2);
      job2.SetPathGroup(2, 2, 1);

      //process groups on the same pallet.
      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(1, 2, "10");
      job2.AddProcessOnPallet(1, 2, "11");
      job2.AddProcessOnPallet(1, 2, "12");
      job2.AddProcessOnPallet(2, 2, "4");
      job2.AddProcessOnPallet(2, 2, "5");
      job2.AddProcessOnPallet(2, 1, "10");
      job2.AddProcessOnPallet(2, 1, "11");
      job2.AddProcessOnPallet(2, 1, "12");

      AddBasicStopsWithProg(job2);

      var job3 = new JobPlan("Job3", 1, new int[] { 2 });
      job3.PartName = "Part3";
      job3.AddProcessOnPallet(1, 1, "20");
      job3.AddProcessOnPallet(1, 1, "21");
      job3.AddProcessOnPallet(1, 2, "30");
      job3.AddProcessOnPallet(1, 2, "31");

      AddBasicStopsWithProg(job3);

      //make Job 4 a template
      var job4 = new JobPlan("Job4", 1, new int[] { 2 });
      job4.PartName = "Part4";
      job4.AddProcessOnPallet(1, 1, "20");
      job4.AddProcessOnPallet(1, 1, "21");
      job4.AddProcessOnPallet(1, 2, "30");
      job4.AddProcessOnPallet(1, 2, "31");


      var log = new List<string>();

      var dset = CreateReadSet();

      CreatePart(dset, "Job4", "Part4", 1, "Test");
      CreateProgram(dset, "1234");
      CreateFixture(dset, "unusedfixture");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new JobPlan[] { job1, job2, job3, job4 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        checkPalletsUsedOnce: false,
        fmsSettings: new FMSSettings(),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      CheckNewFixtures(pMap,
        new string[] {
          "Fixt:3:0:4:1",
          "Fixt:3:0:4:2",
          "Fixt:3:1:10:1",
          "Fixt:3:1:10:2",
          "Fixt:3:2:20:1",
          "Fixt:3:3:30:1"
        },
        new[] { "unusedfixture", "Test" }
      );

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcessFromJob(trans, "Part1:3:1", 1, "Fixt:3:0:4:1");
      CheckPartProcessFromJob(trans, "Part1:3:1", 2, "Fixt:3:0:4:2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part1:3:2", 1, "Fixt:3:1:10:1");
      CheckPartProcessFromJob(trans, "Part1:3:2", 2, "Fixt:3:1:10:2");
      CheckPart(trans, "Part1:3:2", "Job1-Path2-2-0");

      CheckPartProcessFromJob(trans, "Part2:3:1", 1, "Fixt:3:0:4:1");
      CheckPartProcessFromJob(trans, "Part2:3:1", 2, "Fixt:3:0:4:2");
      CheckPart(trans, "Part2:3:1", "Job2-Path1-2-0");

      CheckPartProcessFromJob(trans, "Part2:3:2", 1, "Fixt:3:1:10:1");
      CheckPartProcessFromJob(trans, "Part2:3:2", 2, "Fixt:3:1:10:2");
      CheckPart(trans, "Part2:3:2", "Job2-Path2-1-0");

      CheckPartProcessFromJob(trans, "Part3:3:1", 1, "Fixt:3:2:20:1");
      CheckPart(trans, "Part3:3:1", "Job3-Path1-0");

      CheckPartProcessFromJob(trans, "Part3:3:2", 1, "Fixt:3:3:30:1");
      CheckPart(trans, "Part3:3:2", "Job3-Path2-0");

      CheckPartProcess(trans, "Part4:3:1", 1, "Fixt:3:2:20:1");
      CheckPart(trans, "Part4:3:1", "Job4-Path1-0");

      CheckPartProcess(trans, "Part4:3:2", 1, "Fixt:3:3:30:1");
      CheckPart(trans, "Part4:3:2", "Job4-Path2-0");

      CheckPalletGroup(trans, 31, "Fixt:3:0:4", 2, new int[] { 4, 5 });
      CheckPalletGroup(trans, 32, "Fixt:3:1:10", 2, new int[] { 10, 11, 12 });
      CheckPalletGroup(trans, 33, "Fixt:3:2:20", 1, new int[] { 20, 21 });
      CheckPalletGroup(trans, 34, "Fixt:3:3:30", 1, new int[] { 30, 31 });

      AssertPartsPalletsDeleted(trans);
    }

    [Fact]
    public void DifferentPallets()
    {
      //Test when processes have different pallet lists
      var job1 = new JobPlan("Job1", 2, new int[] { 2, 2 });
      job1.PartName = "Part1";
      job1.SetPathGroup(1, 1, 1);
      job1.SetPathGroup(1, 2, 2);
      job1.SetPathGroup(2, 1, 1);
      job1.SetPathGroup(2, 2, 2);

      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(1, 2, "10");
      job1.AddProcessOnPallet(1, 2, "11");
      job1.AddProcessOnPallet(1, 2, "12");
      job1.AddProcessOnPallet(2, 1, "40");
      job1.AddProcessOnPallet(2, 1, "50");
      job1.AddProcessOnPallet(2, 2, "100");
      job1.AddProcessOnPallet(2, 2, "110");
      job1.AddProcessOnPallet(2, 2, "120");

      AddBasicStopsWithProg(job1);

      var job2 = new JobPlan("Job2", 2, new int[] { 2, 2 });
      job2.PartName = "Part2";

      //make path groups twisted
      job2.SetPathGroup(1, 1, 1);
      job2.SetPathGroup(1, 2, 2);
      job2.SetPathGroup(2, 1, 2);
      job2.SetPathGroup(2, 2, 1);

      //process groups on the same pallet.
      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(1, 2, "10");
      job2.AddProcessOnPallet(1, 2, "11");
      job2.AddProcessOnPallet(1, 2, "12");
      job2.AddProcessOnPallet(2, 2, "40");
      job2.AddProcessOnPallet(2, 2, "50");
      job2.AddProcessOnPallet(2, 1, "100");
      job2.AddProcessOnPallet(2, 1, "110");
      job2.AddProcessOnPallet(2, 1, "120");

      AddBasicStopsWithProg(job2);

      var job3 = new JobPlan("Job3", 2, new int[] { 2, 2 });
      job3.PartName = "Part3";

      job3.SetPathGroup(1, 1, 1);
      job3.SetPathGroup(1, 2, 2);
      job3.SetPathGroup(2, 1, 1);
      job3.SetPathGroup(2, 2, 2);

      //These do not all match above (some do, but not all)
      job3.AddProcessOnPallet(1, 1, "4");
      job3.AddProcessOnPallet(1, 1, "5");
      job3.AddProcessOnPallet(1, 2, "22");
      job3.AddProcessOnPallet(1, 2, "23");
      job3.AddProcessOnPallet(1, 2, "24");
      job3.AddProcessOnPallet(2, 1, "30");
      job3.AddProcessOnPallet(2, 1, "31");
      job3.AddProcessOnPallet(2, 2, "100");
      job3.AddProcessOnPallet(2, 2, "110");
      job3.AddProcessOnPallet(2, 2, "120");

      AddBasicStopsWithProg(job3);

      var log = new List<string>();

      var dset = new MazakTestData();
      CreateProgram(dset, "1234");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new JobPlan[] { job1, job2, job3 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        checkPalletsUsedOnce: false,
        fmsSettings: new FMSSettings(),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      CheckNewFixtures(pMap, new string[] {
        "Fixt:3:0:4:1",
        "Fixt:3:1:40:2",
        "Fixt:3:2:10:1",
        "Fixt:3:3:100:2",
        "Fixt:3:4:30:2",
        "Fixt:3:5:22:1"
      });

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcessFromJob(trans, "Part1:3:1", 1, "Fixt:3:0:4:1");
      CheckPartProcessFromJob(trans, "Part1:3:1", 2, "Fixt:3:1:40:2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part1:3:2", 1, "Fixt:3:2:10:1");
      CheckPartProcessFromJob(trans, "Part1:3:2", 2, "Fixt:3:3:100:2");
      CheckPart(trans, "Part1:3:2", "Job1-Path2-2-0");

      CheckPartProcessFromJob(trans, "Part2:3:1", 1, "Fixt:3:0:4:1");
      CheckPartProcessFromJob(trans, "Part2:3:1", 2, "Fixt:3:1:40:2");
      CheckPart(trans, "Part2:3:1", "Job2-Path1-2-0");

      CheckPartProcessFromJob(trans, "Part2:3:2", 1, "Fixt:3:2:10:1");
      CheckPartProcessFromJob(trans, "Part2:3:2", 2, "Fixt:3:3:100:2");
      CheckPart(trans, "Part2:3:2", "Job2-Path2-1-0");

      CheckPartProcessFromJob(trans, "Part3:3:1", 1, "Fixt:3:0:4:1");
      CheckPartProcessFromJob(trans, "Part3:3:1", 2, "Fixt:3:4:30:2");
      CheckPart(trans, "Part3:3:1", "Job3-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part3:3:2", 1, "Fixt:3:5:22:1");
      CheckPartProcessFromJob(trans, "Part3:3:2", 2, "Fixt:3:3:100:2");
      CheckPart(trans, "Part3:3:2", "Job3-Path2-2-0");

      CheckSingleProcPalletGroup(trans, 31, "Fixt:3:0:4:1", new int[] { 4, 5 });
      CheckSingleProcPalletGroup(trans, 32, "Fixt:3:1:40:2", new int[] { 40, 50 });
      CheckSingleProcPalletGroup(trans, 33, "Fixt:3:2:10:1", new int[] { 10, 11, 12 });
      CheckSingleProcPalletGroup(trans, 34, "Fixt:3:3:100:2", new int[] { 100, 110, 120 });
      CheckSingleProcPalletGroup(trans, 35, "Fixt:3:4:30:2", new int[] { 30, 31 });
      CheckSingleProcPalletGroup(trans, 36, "Fixt:3:5:22:1", new int[] { 22, 23, 24 });

      AssertPartsPalletsDeleted(trans);
    }

    [Fact]
    public void ManualFixtureAssignment()
    {
      var job1 = new JobPlan("Job1", 2, new int[] { 2, 2 });
      job1.PartName = "Part1";
      job1.SetPathGroup(1, 1, 1);
      job1.SetPathGroup(1, 2, 2);
      job1.SetPathGroup(2, 1, 1);
      job1.SetPathGroup(2, 2, 2);

      //proc 1 and proc 2 on same pallets
      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(1, 2, "10");
      job1.AddProcessOnPallet(1, 2, "11");
      job1.AddProcessOnPallet(1, 2, "12");
      job1.AddProcessOnPallet(2, 1, "4");
      job1.AddProcessOnPallet(2, 1, "5");
      job1.AddProcessOnPallet(2, 2, "10");
      job1.AddProcessOnPallet(2, 2, "11");
      job1.AddProcessOnPallet(2, 2, "12");

      //each process uses different faces
      job1.AddProcessOnFixture(1, 1, "fixAA", "face1");
      job1.AddProcessOnFixture(2, 1, "fixAA", "face2");
      job1.AddProcessOnFixture(1, 2, "fixBB", "face1");
      job1.AddProcessOnFixture(2, 2, "fixBB", "face2");

      AddBasicStopsWithProg(job1);

      var job2 = new JobPlan("Job2", 2, new int[] { 2, 2 });
      job2.PartName = "Part2";

      //make path groups twisted
      job2.SetPathGroup(1, 1, 1);
      job2.SetPathGroup(1, 2, 2);
      job2.SetPathGroup(2, 1, 2);
      job2.SetPathGroup(2, 2, 1);

      //process groups on the same pallet.
      job2.AddProcessOnPallet(1, 1, "4");
      job2.AddProcessOnPallet(1, 1, "5");
      job2.AddProcessOnPallet(1, 2, "10");
      job2.AddProcessOnPallet(1, 2, "11");
      job2.AddProcessOnPallet(1, 2, "12");
      job2.AddProcessOnPallet(2, 2, "4");
      job2.AddProcessOnPallet(2, 2, "5");
      job2.AddProcessOnPallet(2, 1, "10");
      job2.AddProcessOnPallet(2, 1, "11");
      job2.AddProcessOnPallet(2, 1, "12");

      //each process uses different faces
      job2.AddProcessOnFixture(1, 1, "fixAA", "face1");
      job2.AddProcessOnFixture(2, 2, "fixAA", "face2");
      job2.AddProcessOnFixture(1, 2, "fixBB", "face1");
      job2.AddProcessOnFixture(2, 1, "fixBB", "face2");

      AddBasicStopsWithProg(job2);

      var job3 = new JobPlan("Job3", 1, new int[] { 2 });
      job3.PartName = "Part3";
      job3.AddProcessOnPallet(1, 1, "20");
      job3.AddProcessOnPallet(1, 1, "21");
      job3.AddProcessOnPallet(1, 2, "30");
      job3.AddProcessOnPallet(1, 2, "31");

      //job3 uses separate fixture than job 4
      job3.AddProcessOnFixture(1, 1, "fix3", "face1");
      job3.AddProcessOnFixture(1, 2, "fix3e", "face1");

      AddBasicStopsWithProg(job3);

      var job4 = new JobPlan("Job4", 1, new int[] { 2 });
      job4.PartName = "Part4";
      job4.AddProcessOnPallet(1, 1, "20");
      job4.AddProcessOnPallet(1, 1, "21");
      job4.AddProcessOnPallet(1, 2, "30");
      job4.AddProcessOnPallet(1, 2, "31");

      //job3 uses separate fixture than job 4
      job4.AddProcessOnFixture(1, 1, "fix4", "face1");
      job4.AddProcessOnFixture(1, 2, "fix4e", "face1");

      AddBasicStopsWithProg(job4);

      var log = new List<string>();

      var dset = CreateReadSet();
      CreateProgram(dset, "1234");

      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new JobPlan[] { job1, job2, job3, job4 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        checkPalletsUsedOnce: false,
        fmsSettings: new FMSSettings(),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      CheckNewFixtures(pMap, new string[] {
        "Fixt:3:fixAA:face1",
        "Fixt:3:fixAA:face2",
        "Fixt:3:fixBB:face1",
        "Fixt:3:fixBB:face2",
        "Fixt:3:fix3:face1",
        "Fixt:3:fix3e:face1",
        "Fixt:3:fix4:face1",
        "Fixt:3:fix4e:face1",
      }, new[] { "Test" });

      var trans = pMap.CreatePartPalletDatabaseRows();

      CheckPartProcessFromJob(trans, "Part1:3:1", 1, "Fixt:3:fixAA:face1");
      CheckPartProcessFromJob(trans, "Part1:3:1", 2, "Fixt:3:fixAA:face2");
      CheckPart(trans, "Part1:3:1", "Job1-Path1-1-0");

      CheckPartProcessFromJob(trans, "Part1:3:2", 1, "Fixt:3:fixBB:face1");
      CheckPartProcessFromJob(trans, "Part1:3:2", 2, "Fixt:3:fixBB:face2");
      CheckPart(trans, "Part1:3:2", "Job1-Path2-2-0");

      CheckPartProcessFromJob(trans, "Part2:3:1", 1, "Fixt:3:fixAA:face1");
      CheckPartProcessFromJob(trans, "Part2:3:1", 2, "Fixt:3:fixAA:face2");
      CheckPart(trans, "Part2:3:1", "Job2-Path1-2-0");

      CheckPartProcessFromJob(trans, "Part2:3:2", 1, "Fixt:3:fixBB:face1");
      CheckPartProcessFromJob(trans, "Part2:3:2", 2, "Fixt:3:fixBB:face2");
      CheckPart(trans, "Part2:3:2", "Job2-Path2-1-0");

      CheckPartProcessFromJob(trans, "Part3:3:1", 1, "Fixt:3:fix3:face1");
      CheckPart(trans, "Part3:3:1", "Job3-Path1-0");

      CheckPartProcessFromJob(trans, "Part3:3:2", 1, "Fixt:3:fix3e:face1");
      CheckPart(trans, "Part3:3:2", "Job3-Path2-0");

      CheckPartProcessFromJob(trans, "Part4:3:1", 1, "Fixt:3:fix4:face1");
      CheckPart(trans, "Part4:3:1", "Job4-Path1-0");

      CheckPartProcessFromJob(trans, "Part4:3:2", 1, "Fixt:3:fix4e:face1");
      CheckPart(trans, "Part4:3:2", "Job4-Path2-0");

      CheckPalletGroup(trans, 31, new[] { "Fixt:3:fixAA:face1", "Fixt:3:fixAA:face2" }, new int[] { 4, 5 });
      CheckPalletGroup(trans, 32, new[] { "Fixt:3:fixBB:face1", "Fixt:3:fixBB:face2" }, new int[] { 10, 11, 12 });
      CheckPalletGroup(trans, 33, new[] { "Fixt:3:fix3:face1" }, new int[] { 20, 21 });
      CheckPalletGroup(trans, 34, new[] { "Fixt:3:fix3e:face1" }, new int[] { 30, 31 });
      CheckPalletGroup(trans, 35, new[] { "Fixt:3:fix4:face1" }, new int[] { 20, 21 });
      CheckPalletGroup(trans, 36, new[] { "Fixt:3:fix4e:face1" }, new int[] { 30, 31 });

      AssertPartsPalletsDeleted(trans);
    }

    [Fact]
    public void DeleteUnusedPartsPals()
    {
      //Test when processes have different pallet lists
      var job1 = new JobPlan("Job1", 2, new int[] { 2, 2 });
      job1.PartName = "Part1";
      job1.SetPathGroup(1, 1, 1);
      job1.SetPathGroup(1, 2, 2);
      job1.SetPathGroup(2, 1, 1);
      job1.SetPathGroup(2, 2, 2);

      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 1, "5");
      job1.AddProcessOnPallet(1, 2, "10");
      job1.AddProcessOnPallet(1, 2, "11");
      job1.AddProcessOnPallet(1, 2, "12");
      job1.AddProcessOnPallet(2, 1, "40");
      job1.AddProcessOnPallet(2, 1, "50");
      job1.AddProcessOnPallet(2, 2, "100");
      job1.AddProcessOnPallet(2, 2, "110");
      job1.AddProcessOnPallet(2, 2, "120");

      AddBasicStopsWithProg(job1);

      var dset = CreateReadSet();
      CreateFixture(dset, "aaaa:1");
      CreatePart(dset, "uniq1", "part1:1:1", 1, "aaaa");
      CreatePart(dset, "uniq2", "part2:1:1", 1, "Test");
      CreatePallet(dset, 5, "aaaa", 1); // this should be deleted since part1:1:1 is being deleted
      CreatePallet(dset, 6, "Test", 1); // this should be kept because part2:1:1 is being kept
      CreateProgram(dset, "1234");

      var log = new List<string>();
      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new JobPlan[] { job1 },
        3,
        dset,
        new HashSet<string>() { "part2:1:1" },
        MazakDbType.MazakVersionE,
        checkPalletsUsedOnce: false,
        fmsSettings: new FMSSettings(),
        errors: log
      );
      if (log.Count > 0) Assert.True(false, log[0]);

      var del = pMap.DeleteOldPartPalletRows();
      del.Pallets.Should().BeEquivalentTo(new[] {
        new MazakPalletRow()
        {
          PalletNumber = 5,
          Fixture = "aaaa:1",
          Command = MazakWriteCommand.Delete
        }
      });
      dset.TestParts[0].Command = MazakWriteCommand.Delete;
      dset.TestParts[0].TotalProcess = dset.TestParts[0].Processes.Count;
      dset.TestParts[0].Processes.Clear();
      del.Parts.Should().BeEquivalentTo(new[] {
        dset.TestParts[0]
      });
      del.Fixtures.Should().BeEmpty();
      del.Schedules.Should().BeEmpty();
    }

    [Fact]
    public void ErrorsOnMissingProgram()
    {
      //Test when processes have different pallet lists
      var job1 = new JobPlan("Job1", 2, new int[] { 2, 2 });
      job1.PartName = "Part1";
      job1.SetPathGroup(1, 1, 1);
      job1.SetPathGroup(1, 2, 2);
      job1.SetPathGroup(2, 1, 1);
      job1.SetPathGroup(2, 2, 2);

      job1.AddProcessOnPallet(1, 1, "4");
      job1.AddProcessOnPallet(1, 2, "10");
      job1.AddProcessOnPallet(2, 1, "40");
      job1.AddProcessOnPallet(2, 2, "100");

      AddBasicStopsWithProg(job1);

      var dset = CreateReadSet();

      var log = new List<string>();
      var pMap = ConvertJobsToMazakParts.JobsToMazak(
        new JobPlan[] { job1 },
        3,
        dset,
        new HashSet<string>(),
        MazakDbType.MazakVersionE,
        checkPalletsUsedOnce: false,
        fmsSettings: new FMSSettings(),
        errors: log
      );

      log.Should().BeEquivalentTo(new[] {
				// one for each process
				"Part Part1 program 1234 does not exist in the cell controller.",
        "Part Part1 program 1234 does not exist in the cell controller."
      });
    }

    #region Checking
    private class MazakTestData : MazakAllData
    {
      public List<MazakPartRow> TestParts { get; } = new List<MazakPartRow>();
      public List<MazakFixtureRow> TestFixtures { get; } = new List<MazakFixtureRow>();
      public List<MazakPalletRow> TestPallets { get; } = new List<MazakPalletRow>();
      public List<MazakScheduleRow> TestSchedules { get; } = new List<MazakScheduleRow>();

      public MazakTestData()
      {
        Schedules = TestSchedules;
        Parts = TestParts;
        Fixtures = TestFixtures;
        Pallets = TestPallets;
        MainPrograms = new HashSet<string>();
      }
    }

    private MazakTestData CreateReadSet()
    {
      var dset = new MazakTestData();
      CreateFixture(dset, "Test");
      return dset;
    }

    private void CreatePart(MazakTestData dset, string unique, string name, int numProc, string fix)
    {
      var pRow = new MazakPartRow() { Comment = "comment", PartName = name };
      dset.TestParts.Add(pRow);

      for (int proc = 1; proc <= numProc; proc++)
      {
        pRow.Processes.Add(new MazakPartProcessRow()
        {
          ProcessNumber = proc,
          Fixture = fix + ":" + proc.ToString(),
          PartName = name,
        });
      }
    }

    private void CreateFixture(MazakTestData dset, string name)
    {
      dset.TestFixtures.Add(new MazakFixtureRow() { Comment = "Insight", FixtureName = name });
    }

    private void CreatePallet(MazakTestData dset, int pal, string fix, int numProc)
    {
      for (int i = 1; i <= numProc; i++)
      {
        dset.TestPallets.Add(new MazakPalletRow() { Fixture = fix + ":" + i.ToString(), PalletNumber = pal });
      }
    }

    private void CreateProgram(MazakTestData dset, string program)
    {
      dset.MainPrograms.Add(program);
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
          stop.AddProgram(1, "1234");
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
        });
      var del = (delFix ?? Enumerable.Empty<string>())
        .Select(f => new MazakFixtureRow()
        {
          FixtureName = f,
          Comment = "Insight",
          Command = MazakWriteCommand.Delete
        });
      var actions = map.CreateDeleteFixtureDatabaseRows();
      actions.Fixtures.Should().BeEquivalentTo(add.Concat(del));
      actions.Schedules.Should().BeEmpty();
      actions.Parts.Should().BeEmpty();
      actions.Pallets.Should().BeEmpty();
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
          dset.Parts.Remove(row);
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
          dset.Pallets.Remove(row);
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