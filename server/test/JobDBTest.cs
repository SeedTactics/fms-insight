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
using System.Linq;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using Microsoft.Data.Sqlite;
using Xunit;
using FluentAssertions;
using System.Collections.Immutable;

namespace MachineWatchTest
{
  public class JobDBTest : JobEqualityChecks, IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _jobDB;

    public JobDBTest()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _jobDB = _repoCfg.OpenConnection();
    }

    public void Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    private static void SetJob1Data(JobPlan job1)
    {
      var rnd = new Random();
      job1.PartName = "Job1";
      job1.SetPlannedCyclesOnFirstProcess(1, 125);
      job1.SetPlannedCyclesOnFirstProcess(2, 53);
      job1.RouteStartingTimeUTC = DateTime.UtcNow.AddMinutes(-10);
      job1.RouteEndingTimeUTC = DateTime.UtcNow;
      job1.Archived = false;
      job1.ManuallyCreatedJob = false;
      job1.JobCopiedToSystem = rnd.Next(0, 2) > 0;
      job1.ScheduleId = "Job1tag" + rnd.Next().ToString();
      job1.HoldEntireJob.UserHold = true;
      job1.HoldEntireJob.ReasonForUserHold = "test string";
      job1.HoldEntireJob.HoldUnholdPatternStartUTC = DateTime.UtcNow;
      job1.HoldEntireJob.HoldUnholdPatternRepeats = true;
      job1.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(10));
      job1.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(18));
      job1.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(125));
      job1.Comment = "Hello there";
      job1.ScheduledBookingIds.Add("booking1");
      job1.ScheduledBookingIds.Add("booking2");
      job1.ScheduledBookingIds.Add("booking3");

      job1.SetPartsPerPallet(1, 1, 10);
      job1.SetPartsPerPallet(1, 2, 15);
      job1.SetPartsPerPallet(2, 1, 20);
      job1.SetPartsPerPallet(2, 2, 22);
      job1.SetPartsPerPallet(2, 3, 23);

      job1.SetPathGroup(1, 1, 64);
      job1.SetPathGroup(1, 2, 74);
      job1.SetPathGroup(2, 1, 12);
      job1.SetPathGroup(2, 2, 88);
      job1.SetPathGroup(2, 3, 5);

      job1.SetInputQueue(1, 1, "in11");
      job1.SetOutputQueue(1, 2, "out12");
      job1.SetInputQueue(2, 1, "in21");
      job1.SetOutputQueue(2, 3, "out23");

      job1.SetCasting(1, "cast1");
      job1.SetCasting(2, "cast2");

      job1.SetSimulatedStartingTimeUTC(1, 1, DateTime.Parse("1/6/2011 5:34 AM GMT"));
      job1.SetSimulatedStartingTimeUTC(1, 2, DateTime.Parse("2/10/2011 6:45 AM GMT"));
      job1.SetSimulatedStartingTimeUTC(2, 1, DateTime.Parse("3/14/2011 7:03 AM GMT"));
      job1.SetSimulatedStartingTimeUTC(2, 2, DateTime.Parse("4/20/2011 8:22 PM GMT"));
      job1.SetSimulatedStartingTimeUTC(2, 3, DateTime.Parse("5/22/2011 9:18 AM GMT"));

      job1.SetSimulatedProduction(1, 1, RandSimProduction());
      job1.SetSimulatedProduction(1, 2, RandSimProduction());
      job1.SetSimulatedProduction(2, 1, RandSimProduction());
      job1.SetSimulatedProduction(2, 2, RandSimProduction());
      job1.SetSimulatedProduction(2, 3, RandSimProduction());

      job1.SetSimulatedAverageFlowTime(1, 1, TimeSpan.FromMinutes(0.5));
      job1.SetSimulatedAverageFlowTime(1, 2, TimeSpan.FromMinutes(1.5));
      job1.SetSimulatedAverageFlowTime(2, 1, TimeSpan.FromMinutes(2.5));
      job1.SetSimulatedAverageFlowTime(2, 2, TimeSpan.FromMinutes(3.5));
      job1.SetSimulatedAverageFlowTime(2, 3, TimeSpan.FromMinutes(4.5));

      job1.AddProcessOnPallet(1, 1, "Pal2");
      job1.AddProcessOnPallet(1, 1, "Pal5");
      job1.AddProcessOnPallet(1, 2, "Pal4");
      job1.AddProcessOnPallet(1, 2, "Pal35");
      job1.AddProcessOnPallet(2, 1, "Pal12");
      job1.AddProcessOnPallet(2, 1, "Pal64");
      job1.AddProcessOnPallet(2, 2, "Hi");
      job1.AddProcessOnPallet(2, 2, "Pal2");
      job1.AddProcessOnPallet(2, 3, "Pal5");
      job1.AddProcessOnPallet(2, 3, "OMG");

      job1.SetFixtureFace(1, 1, "Fix1", 1);
      job1.SetFixtureFace(1, 2, "Fixxx", 3);
      job1.SetFixtureFace(2, 1, "Fix5", 5);
      job1.SetFixtureFace(2, 2, "Bye", 12);
      // don't set 2, 3 to check null works

      job1.AddLoadStation(1, 1, 35);
      job1.AddLoadStation(1, 1, 64);
      job1.AddLoadStation(1, 2, 785);
      job1.AddLoadStation(1, 2, 15);
      job1.AddLoadStation(2, 1, 647);
      job1.AddLoadStation(2, 1, 474);
      job1.AddLoadStation(2, 2, 785);
      job1.AddLoadStation(2, 2, 53);
      job1.AddLoadStation(2, 3, 15);

      job1.SetExpectedLoadTime(1, 1, TimeSpan.FromSeconds(100));
      job1.SetExpectedLoadTime(1, 2, TimeSpan.FromMinutes(53));
      job1.SetExpectedLoadTime(2, 1, TimeSpan.FromHours(52));
      job1.SetExpectedLoadTime(2, 2, TimeSpan.FromSeconds(98));
      job1.SetExpectedLoadTime(2, 3, TimeSpan.FromSeconds(35));

      job1.AddUnloadStation(1, 1, 75);
      job1.AddUnloadStation(1, 1, 234);
      job1.AddUnloadStation(1, 2, 53);
      job1.AddUnloadStation(2, 1, 563);
      job1.AddUnloadStation(2, 2, 2);
      job1.AddUnloadStation(2, 2, 12);
      job1.AddUnloadStation(2, 3, 32);

      job1.SetExpectedUnloadTime(1, 1, TimeSpan.FromSeconds(13));
      job1.SetExpectedUnloadTime(1, 2, TimeSpan.FromMinutes(12));
      job1.SetExpectedUnloadTime(2, 1, TimeSpan.FromHours(63));
      job1.SetExpectedUnloadTime(2, 2, TimeSpan.FromSeconds(73));
      job1.SetExpectedUnloadTime(2, 3, TimeSpan.FromSeconds(532));

      var route = new JobMachiningStop("Machine");
      route.Stations.Add(23);
      route.Stations.Add(12);
      route.ProgramName = "Hello";
      route.ProgramRevision = 522;
      route.ExpectedCycleTime = TimeSpan.FromHours(1.2);
      route.Tools["tool1"] = TimeSpan.FromMinutes(30);
      route.Tools["tool2"] = TimeSpan.FromMinutes(35);
      job1.AddMachiningStop(1, 1, route);

      route = new JobMachiningStop("Other Machine");
      route.Stations.Add(23);
      route.Stations.Add(12);
      route.ProgramName = "agw";
      route.ExpectedCycleTime = TimeSpan.FromHours(2.8);
      route.Tools["tool1"] = TimeSpan.FromMinutes(9);
      route.Tools["tool33"] = TimeSpan.FromMinutes(42);
      job1.AddMachiningStop(1, 2, route);

      route = new JobMachiningStop("Test");
      route.Stations.Add(64);
      route.Stations.Add(323);
      route.ProgramName = "Whee";
      route.ExpectedCycleTime = TimeSpan.FromHours(6.3);
      route.Tools["tool2"] = TimeSpan.FromMinutes(12);
      route.Tools["tool44"] = TimeSpan.FromMinutes(99);
      job1.AddMachiningStop(2, 1, route);

      route = new JobMachiningStop("Test");
      route.Stations.Add(64);
      route.Stations.Add(32);
      route.ProgramName = "agwe";
      job1.AddMachiningStop(2, 2, route);

      route = new JobMachiningStop("Test");
      route.Stations.Add(245);
      route.Stations.Add(36);
      route.ProgramName = "oh my";
      job1.AddMachiningStop(2, 1, route);

      route = new JobMachiningStop("Test");
      route.Stations.Add(23);
      route.Stations.Add(53);
      route.ProgramName = "so cool";
      job1.AddMachiningStop(2, 2, route);

      route = new JobMachiningStop("Test");
      route.Stations.Add(15);
      route.Stations.Add(88);
      route.ProgramName = "prog2-3";
      job1.AddMachiningStop(2, 3, route);

      job1.PathInspections(1, 1).Add(new PathInspection()
      {
        InspectionType = "Insp1",
        Counter = "counter1",
        MaxVal = 53,
        TimeInterval = TimeSpan.FromMinutes(100),
        ExpectedInspectionTime = TimeSpan.FromMinutes(200)
      });
      job1.PathInspections(1, 1).Add(new PathInspection()
      {
        InspectionType = "Insp2",
        Counter = "counter2",
        MaxVal = 44,
        TimeInterval = TimeSpan.FromMinutes(102),
        ExpectedInspectionTime = TimeSpan.FromMinutes(210)
      });
      job1.PathInspections(2, 1).Add(new PathInspection()
      {
        InspectionType = "Insp1",
        Counter = "counter3",
        RandomFreq = 0.4,
        TimeInterval = TimeSpan.FromMinutes(104),
        ExpectedInspectionTime = TimeSpan.FromMinutes(204)
      });
      job1.PathInspections(2, 2).Add(new PathInspection()
      {
        InspectionType = "Insp1",
        Counter = "counter4",
        MaxVal = 10,
        RandomFreq = 0.2,
        TimeInterval = TimeSpan.FromMinutes(124),
        ExpectedInspectionTime = TimeSpan.FromMinutes(274)
      });
      job1.HoldMachining(1, 1).UserHold = false;
      job1.HoldMachining(1, 1).ReasonForUserHold = "reason for user hold";
      job1.HoldMachining(1, 1).HoldUnholdPatternRepeats = false;
      job1.HoldMachining(1, 1).HoldUnholdPatternStartUTC = DateTime.Parse("2010/5/3 7:32 PM").ToUniversalTime();
      job1.HoldMachining(1, 1).HoldUnholdPattern.Add(TimeSpan.FromMinutes(5));
      job1.HoldMachining(1, 1).HoldUnholdPattern.Add(TimeSpan.FromMinutes(53));

      job1.HoldMachining(1, 2).UserHold = true;
      job1.HoldMachining(1, 2).ReasonForUserHold = "another reason for user hold";
      job1.HoldMachining(1, 2).HoldUnholdPatternRepeats = true;
      job1.HoldMachining(1, 2).HoldUnholdPatternStartUTC = DateTime.Parse("2010/5/12 6:12 PM").ToUniversalTime();
      job1.HoldMachining(1, 2).HoldUnholdPattern.Add(TimeSpan.FromMinutes(84));
      job1.HoldMachining(1, 2).HoldUnholdPattern.Add(TimeSpan.FromMinutes(1));

      job1.HoldMachining(2, 1).UserHold = false;
      job1.HoldMachining(2, 1).ReasonForUserHold = "oh my reason for user hold";
      job1.HoldMachining(2, 1).HoldUnholdPatternRepeats = true;
      job1.HoldMachining(2, 1).HoldUnholdPatternStartUTC = DateTime.Parse("2010/9/1 1:30 PM").ToUniversalTime();
      job1.HoldMachining(2, 1).HoldUnholdPattern.Add(TimeSpan.FromMinutes(532));
      job1.HoldMachining(2, 1).HoldUnholdPattern.Add(TimeSpan.FromMinutes(64));

      job1.HoldLoadUnload(1, 1).UserHold = true;
      job1.HoldLoadUnload(1, 1).ReasonForUserHold = "abcdef";
      job1.HoldLoadUnload(1, 1).HoldUnholdPatternRepeats = true;
      job1.HoldLoadUnload(1, 1).HoldUnholdPatternStartUTC = DateTime.Parse("2010/12/2 3:32 PM").ToUniversalTime();
      job1.HoldLoadUnload(1, 1).HoldUnholdPattern.Add(TimeSpan.FromMinutes(63));
      job1.HoldLoadUnload(1, 1).HoldUnholdPattern.Add(TimeSpan.FromMinutes(7));

      job1.HoldLoadUnload(1, 2).UserHold = false;
      job1.HoldLoadUnload(1, 2).ReasonForUserHold = "agr";
      job1.HoldLoadUnload(1, 2).HoldUnholdPatternRepeats = false;
      job1.HoldLoadUnload(1, 2).HoldUnholdPatternStartUTC = DateTime.Parse("2010/6/1 3:12 PM").ToUniversalTime();
      job1.HoldLoadUnload(1, 2).HoldUnholdPattern.Add(TimeSpan.FromMinutes(174));
      job1.HoldLoadUnload(1, 2).HoldUnholdPattern.Add(TimeSpan.FromMinutes(83));

      job1.HoldLoadUnload(2, 3).UserHold = true;
      job1.HoldLoadUnload(2, 3).ReasonForUserHold = "erhagsad";
      job1.HoldLoadUnload(2, 3).HoldUnholdPatternRepeats = false;
      job1.HoldLoadUnload(2, 3).HoldUnholdPatternStartUTC = DateTime.Parse("2010/11/5 9:30 AM").ToUniversalTime();
      job1.HoldLoadUnload(2, 3).HoldUnholdPattern.Add(TimeSpan.FromMinutes(32));
      job1.HoldLoadUnload(2, 3).HoldUnholdPattern.Add(TimeSpan.FromMinutes(64));
    }

    private static void AddObsoleteInspData(JobPlan job)
    {
      // check obsolete saves properly
      job.GetType().GetField("_inspections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(job,
        new List<JobInspectionData>() {
          new JobInspectionData("OldInsp1", "counter1", 53, TimeSpan.FromMinutes(22)),
          new JobInspectionData("OldInsp2", "counter2", 12.8, TimeSpan.FromMinutes(33), 1)
        }
      );
    }
    private static void AddExpectedPathDataFromObsoleteInspections(JobPlan job)
    {
      // OldInsp1 is null InspProc so should be final process
      var oldInsp1 = new PathInspection() { InspectionType = "OldInsp1", Counter = "counter1", MaxVal = 53, TimeInterval = TimeSpan.FromMinutes(22) };
      for (int path = 1; path <= job.GetNumPaths(job.NumProcesses); path++)
      {
        job.PathInspections(job.NumProcesses, path).Add(oldInsp1);
      }

      // OldInsp2 is InspProc 1 so should be first process
      var oldInsp2 = new PathInspection() { InspectionType = "OldInsp2", Counter = "counter2", RandomFreq = 12.8, TimeInterval = TimeSpan.FromMinutes(33) };
      for (int path = 1; path <= job.GetNumPaths(1); path++)
      {
        job.PathInspections(1, path).Add(oldInsp2);
      }

      // clear old inspection data
      job.GetType().GetField("_inspections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(job, null);
    }

    private static void SetJob2Data(JobPlan job2)
    {
      job2.PartName = "Job1";
      job2.Archived = false;
      job2.RouteStartingTimeUTC = DateTime.UtcNow.AddHours(-10);
      job2.RouteEndingTimeUTC = DateTime.UtcNow.AddHours(-2);
      job2.SetPlannedCyclesOnFirstProcess(1, 4);
      job2.HoldEntireJob.UserHold = false;
      job2.HoldEntireJob.ReasonForUserHold = "another reason";
      job2.HoldEntireJob.HoldUnholdPatternRepeats = false;
      job2.HoldEntireJob.HoldUnholdPatternStartUTC = DateTime.Parse("2010/1/1 5:43 PM").ToUniversalTime();
      job2.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(5));
      job2.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(533));
      job2.Comment = "this is a test";
      job2.ScheduleId = "Job2tag-hello";
      job2.JobCopiedToSystem = false;
      job2.ManuallyCreatedJob = true;

      job2.SetPartsPerPallet(1, 1, 3);
      job2.SetPartsPerPallet(2, 1, 5);
      job2.SetPartsPerPallet(3, 1, 6);

      job2.SetPathGroup(1, 1, 17);
      job2.SetPathGroup(2, 1, 75);
      job2.SetPathGroup(3, 1, 33);

      job2.SetSimulatedStartingTimeUTC(1, 1, DateTime.Parse("8/22/2011 3:14 AM GMT"));
      job2.SetSimulatedStartingTimeUTC(2, 1, DateTime.Parse("9/15/2011 4:25 PM GMT"));
      job2.SetSimulatedStartingTimeUTC(3, 1, DateTime.Parse("10/4/2011 5:33 AM GMT"));


      job2.AddProcessOnPallet(1, 1, "Pal2");
      job2.AddProcessOnPallet(1, 1, "asg");
      job2.AddProcessOnPallet(2, 1, "awerg");
      job2.AddProcessOnPallet(3, 1, "ehe");
      job2.AddProcessOnPallet(3, 1, "awger");

      job2.SetFixtureFace(1, 1, "Fix6", 62);
      job2.SetFixtureFace(2, 1, "a235sg", 52);
      job2.SetFixtureFace(3, 1, "erte34", 2);
      job2.SetFixtureFace(3, 1, "35aert", 33);

      job2.AddLoadStation(1, 1, 375);
      job2.AddLoadStation(2, 1, 86);
      job2.AddLoadStation(2, 1, 36);
      job2.AddLoadStation(3, 1, 86);
      job2.AddLoadStation(3, 1, 375);
      job2.AddUnloadStation(1, 1, 234);
      job2.AddUnloadStation(2, 1, 75);
      job2.AddUnloadStation(3, 1, 786);

      var route = new JobMachiningStop("Abc");
      route.Stations.Add(12);
      route.ProgramName = "agoiuhewg";
      job2.AddMachiningStop(1, 1, route);

      route = new JobMachiningStop("gwerwer");
      route.Stations.Add(23);
      route.ProgramName = "awga";
      job2.AddMachiningStop(2, 1, route);

      route = new JobMachiningStop("agreer");
      route.Stations.Add(75);
      route.Stations.Add(365);
      route.ProgramName = "ahtt";
      job2.AddMachiningStop(3, 1, route);

      route = new JobMachiningStop("duude");
      route.Stations.Add(643);
      route.Stations.Add(7458);
      route.ProgramName = "awgouihag";
      job2.AddMachiningStop(3, 1, route);

    }

    private static ImmutableList<SimulatedStationUtilization> RandSimStationUse()
    {
      var rnd = new Random();
      var ret = ImmutableList.CreateBuilder<SimulatedStationUtilization>();
      for (int i = 0; i < 3; i++)
      {
        ret.Add(new SimulatedStationUtilization()
        {
          ScheduleId = "id" + rnd.Next(0, 10000).ToString(),
          StationGroup = "group" + rnd.Next(0, 100000).ToString(),
          StationNum = rnd.Next(0, 10000),
          StartUTC = DateTime.UtcNow.AddMinutes(-rnd.Next(200, 300)),
          EndUTC = DateTime.UtcNow.AddMinutes(-rnd.Next(0, 100)),
          UtilizationTime = TimeSpan.FromMinutes(rnd.Next(10, 1000)),
          PlannedDownTime = TimeSpan.FromMinutes(rnd.Next(10, 1000))
        });
      }
      return ret.ToImmutable();
    }

    private static ImmutableDictionary<string, int> RandExtraParts()
    {
      var rnd = new Random();
      var ret = ImmutableDictionary<string, int>.Empty.ToBuilder();
      for (int i = 0; i < 3; i++)
      {
        ret.Add("part" + rnd.Next(0, 10000).ToString(),
                rnd.Next(10000));
      }
      return ret.ToImmutable();
    }

    private static List<PartWorkorder> RandUnfilledWorkorders()
    {
      var rnd = new Random();
      var ret = new List<PartWorkorder>();
      for (int i = 0; i < 3; i++)
      {
        ret.Add(new PartWorkorder()
        {
          WorkorderId = "work" + rnd.Next(0, 10000).ToString(),
          Part = "part" + rnd.Next(0, 10000).ToString(),
          Quantity = rnd.Next(10000),
          DueDate = new DateTime(2018, rnd.Next(1, 12), rnd.Next(1, 20)),
          Priority = rnd.Next(10000),
          Programs = ImmutableList<WorkorderProgram>.Empty
        });
      }
      return ret;
    }

    private static IEnumerable<JobPlan.SimulatedProduction> RandSimProduction()
    {
      var rnd = new Random();
      var ret = new List<JobPlan.SimulatedProduction>();
      for (int i = 0; i < 3; i++)
      {
        var prod = default(JobPlan.SimulatedProduction);
        prod.TimeUTC = DateTime.UtcNow.AddMinutes(-100 + i);
        prod.Quantity = rnd.Next(0, 100);
        ret.Add(prod);
      }

      return ret;
    }

    private void PrintJob(JobPlan j)
    {
      var str = Newtonsoft.Json.JsonConvert.SerializeObject(j, Newtonsoft.Json.Formatting.Indented, new Newtonsoft.Json.Converters.StringEnumConverter());
      Console.WriteLine("XXXXX " + str);
    }

    [Fact]
    public void Jobs()
    {
      var job1 = new JobPlan("Unique1", 2, new int[] { 2, 3 });
      SetJob1Data(job1);
      AddObsoleteInspData(job1);

      var job1ExtraParts = RandExtraParts();
      var job1unfilledWorks = RandUnfilledWorkorders();

      _jobDB.AddJobs(new NewJobs
      {
        ScheduleId = job1.ScheduleId,
        Jobs = ImmutableList.Create(job1),
        ExtraParts = job1ExtraParts,
        CurrentUnfilledWorkorders = job1unfilledWorks.ToImmutableList()
      }, null);

      AddExpectedPathDataFromObsoleteInspections(job1);

      CheckJobs(job1, null, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);
      var recent = _jobDB.LoadMostRecentSchedule();
      recent.ExtraParts.Should().BeEquivalentTo(job1ExtraParts);
      Assert.Equal(job1.ScheduleId, recent.LatestScheduleId);
      CheckPlanEqual(job1, recent.Jobs[0], true);

      var job2 = new JobPlan(new JobPlan("Unique2", 3));
      SetJob2Data(job2);

      var job2simStationUse = RandSimStationUse();
      var job2ExtraParts = RandExtraParts();
      var job2unfilledWorks = RandUnfilledWorkorders();
      var rnd = new Random();
      job2unfilledWorks.Add(new PartWorkorder()
      {
        WorkorderId = "work" + rnd.Next(0, 10000).ToString(),
        Part = job1unfilledWorks[0].Part,
        Quantity = rnd.Next(10000),
        DueDate = new DateTime(2018, rnd.Next(1, 12), rnd.Next(1, 20)),
        Priority = rnd.Next(10000)
      });
      job2unfilledWorks.Add(new PartWorkorder()
      {
        WorkorderId = "work" + rnd.Next(0, 10000).ToString(),
        Part = "Job1",
        Quantity = rnd.Next(10000),
        DueDate = new DateTime(2018, rnd.Next(1, 12), rnd.Next(1, 20)),
        Priority = rnd.Next(10000)
      });

      var newJob2 = new NewJobs()
      {
        ScheduleId = job2.ScheduleId,
        Jobs = ImmutableList.Create(job2),
        StationUse = job2simStationUse,
        ExtraParts = job2ExtraParts,
        CurrentUnfilledWorkorders = job2unfilledWorks.ToImmutableList()
      };
      try
      {
        _jobDB.AddJobs(newJob2, "badsch");
        Assert.True(false, "Expecting addjobs to throw exception");
      }
      catch (Exception e)
      {
        Assert.Equal("Mismatch in previous schedule: expected 'badsch' but got '" + job1.ScheduleId + "'",
                e.Message);
      }

      CheckJobs(job1, null, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);

      _jobDB.AddJobs(newJob2, job1.ScheduleId);

      // job2 is manually created so should be skipped when checking for latest schedule
      CheckJobs(job1, job2, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);
      CheckJobsDate(job1, job2, null);
      CheckSimStationUse(job2simStationUse);
      Assert.True(_jobDB.DoesJobExist(job1.UniqueStr));
      Assert.False(_jobDB.DoesJobExist("aoughwoeufeg"));
      var newAfter = _jobDB.LoadJobsAfterScheduleId(job1.ScheduleId);
      newAfter.Jobs.Count.Should().Be(1);
      CheckSimStationEqual(job2simStationUse, newAfter.StationUse);
      CheckPlanEqual(job2, newAfter.Jobs["Unique2"], true);
      _jobDB.LoadJobsAfterScheduleId(job2.ScheduleId).Jobs.Should().BeEmpty();
      CheckWorkordersEqual(
          new[] { job1unfilledWorks[0] }, // job2 is manual, so latest should be from job1
          _jobDB.MostRecentUnfilledWorkordersForPart(job2unfilledWorks[job2unfilledWorks.Count - 2].Part)
      );

      // job2 is manually created so job1 is most recent
      recent = _jobDB.LoadMostRecentSchedule();
      Assert.Equal(job1ExtraParts, recent.ExtraParts);
      Assert.Equal(job1.ScheduleId, recent.LatestScheduleId);
      CheckWorkordersEqual(job1unfilledWorks, recent.CurrentUnfilledWorkorders);
      CheckPlanEqual(job1, recent.Jobs[0], true);

      CheckJobs(job1, job2, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);

      _jobDB.SetJobComment("Unique1", "newcomm");
      job1.Comment = "newcomm";

      CheckJobs(job1, job2, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);

      _jobDB.SetJobComment("Unique1", "hello");
      job1.Comment = "hello";

      CheckJobs(job1, job2, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);

      job1.HoldEntireJob.UserHold = false;
      job1.HoldEntireJob.ReasonForUserHold = "this is the reason";
      job1.HoldEntireJob.HoldUnholdPatternStartUTC = DateTime.Parse("2010/6/29 6:12 AM").ToUniversalTime();
      job1.HoldEntireJob.HoldUnholdPatternRepeats = false;
      job1.HoldEntireJob.HoldUnholdPattern.Clear();
      job1.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromSeconds(1255));
      _jobDB.UpdateJobHold("Unique1", job1.HoldEntireJob);

      CheckJobs(job1, job2, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);

      job1.HoldMachining(1, 1).UserHold = true;
      job1.HoldMachining(1, 1).ReasonForUserHold = "abnceasd";
      job1.HoldMachining(1, 1).HoldUnholdPatternStartUTC = DateTime.Parse("2010/8/3 7:55 AM").ToUniversalTime();
      job1.HoldMachining(1, 1).HoldUnholdPatternRepeats = false;
      job1.HoldMachining(1, 1).HoldUnholdPattern.Clear();
      _jobDB.UpdateJobMachiningHold("Unique1", 1, 1, job1.HoldMachining(1, 1));

      CheckJobs(job1, job2, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);

      job1.HoldLoadUnload(2, 3).UserHold = false;
      job1.HoldLoadUnload(2, 3).ReasonForUserHold = "agrwerg";
      job1.HoldLoadUnload(2, 3).HoldUnholdPatternStartUTC = DateTime.Parse("2010/7/2 9:55 AM").ToUniversalTime();
      job1.HoldLoadUnload(2, 3).HoldUnholdPatternRepeats = true;
      job1.HoldLoadUnload(2, 3).HoldUnholdPattern.Clear();
      job1.HoldLoadUnload(2, 3).HoldUnholdPattern.Add(TimeSpan.FromSeconds(64));
      job1.HoldLoadUnload(2, 3).HoldUnholdPattern.Add(TimeSpan.FromSeconds(12));
      job1.HoldLoadUnload(2, 3).HoldUnholdPattern.Add(TimeSpan.FromSeconds(6743));
      _jobDB.UpdateJobLoadUnloadHold("Unique1", 2, 3, job1.HoldLoadUnload(2, 3));

      CheckJobs(job1, job2, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);

      Assert.Null(_jobDB.LoadJob("aguheriheg"));

      //check job2 is not copied
      var notCopied = _jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddHours(-4), DateTime.UtcNow.AddHours(-3));
      notCopied.Count.Should().Be(1);
      CheckJobEqual(job2, notCopied.FirstOrDefault(), true);

      //mark job2 copied
      _jobDB.MarkJobCopiedToSystem("Unique2");
      job2.JobCopiedToSystem = true;
      CheckJobs(job1, job2, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);
      notCopied = _jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddHours(-4), DateTime.UtcNow.AddHours(-3));
      notCopied.Should().BeEmpty();

      //Archive job2
      _jobDB.ArchiveJob(job2.UniqueStr);
      job2.Archived = true;
      CheckJobs(job1, null, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);
      CheckJobsDate(job1, job2, null);

      //Archive job1
      var archiveTime = DateTime.UtcNow;
      _jobDB.ArchiveJobs(new[] { job1.UniqueStr }, new[] { new NewDecrementQuantity() {
        JobUnique = job1.UniqueStr,
        Proc1Path = 66,
        Part = job1.PartName,
        Quantity = 50
      }}, archiveTime);
      job1.Archived = true;
      _jobDB.LoadJob(job1.UniqueStr).Archived.Should().BeTrue();
      _jobDB.LoadDecrementsForJob(job1.UniqueStr).Should().BeEquivalentTo(new[] {
        new DecrementQuantity() {
          DecrementId = 0,
          Proc1Path = 66,
          TimeUTC = archiveTime,
          Quantity = 50
        }
      });

      _jobDB.UnarchiveJob(job2.UniqueStr);
      job2.Archived = false;
      CheckJobs(job2, null, null, job1.ScheduleId, job1ExtraParts, job1unfilledWorks);
      _jobDB.LoadJob(job2.UniqueStr).Archived.Should().BeFalse();
    }

    [Fact]
    public void DecrementQuantities()
    {
      var dtime = new DateTime(2020, 03, 11, 14, 08, 00);
      var uniq1 = new JobPlan("uniq1", 1);
      uniq1.JobCopiedToSystem = false;
      uniq1.RouteStartingTimeUTC = dtime.AddHours(-12);
      uniq1.RouteEndingTimeUTC = dtime.AddHours(12);
      uniq1.ScheduledBookingIds.Add("AAA");
      uniq1.ScheduledBookingIds.Add("BBB");
      var uniq2 = new JobPlan("uniq2", 1);
      uniq2.JobCopiedToSystem = true;
      uniq2.RouteStartingTimeUTC = dtime.AddHours(-12);
      uniq2.RouteEndingTimeUTC = dtime.AddHours(12);
      uniq2.ScheduledBookingIds.Add("CCC");
      uniq2.ScheduledBookingIds.Add("DDD");

      _jobDB.AddJobs(new NewJobs() { Jobs = ImmutableList.Create(uniq1, uniq2) }, null);

      _jobDB.LoadJobsNotCopiedToSystem(dtime, dtime, includeDecremented: true).Select(j => j.UniqueStr)
        .Should().BeEquivalentTo(new[] { "uniq1" });
      _jobDB.LoadJobsNotCopiedToSystem(dtime, dtime, includeDecremented: false).Select(j => j.UniqueStr)
        .Should().BeEquivalentTo(new[] { "uniq1" });


      var time1 = DateTime.UtcNow.AddHours(-2);
      _jobDB.AddNewDecrement(new[] {
        new NewDecrementQuantity() {
          JobUnique = "uniq1",
          Proc1Path = 1,
          Part = "part1",
          Quantity = 53
        },
        new NewDecrementQuantity() {
          JobUnique = "uniq1",
          Proc1Path = 2,
          Part = "part1",
          Quantity = 77
        },
        new NewDecrementQuantity() {
          JobUnique = "uniq2",
          Proc1Path = 1,
          Part = "part2",
          Quantity = 821
        },
      }, time1);

      var expected1 = new[] {
        new JobAndDecrementQuantity() {
          DecrementId = 0,
          JobUnique = "uniq1",
          Proc1Path = 1,
          TimeUTC = time1,
          Part = "part1",
          Quantity = 53
        },
        new JobAndDecrementQuantity() {
          DecrementId = 0,
          JobUnique = "uniq1",
          Proc1Path = 2,
          TimeUTC = time1,
          Part = "part1",
          Quantity = 77
        },
        new JobAndDecrementQuantity() {
          DecrementId = 0,
          JobUnique = "uniq2",
          Proc1Path = 1,
          TimeUTC = time1,
          Part = "part2",
          Quantity = 821
        }
      };

      _jobDB.LoadDecrementQuantitiesAfter(-1).Should().BeEquivalentTo(expected1);

      _jobDB.LoadJobsNotCopiedToSystem(dtime, dtime, includeDecremented: true).Select(j => j.UniqueStr)
        .Should().BeEquivalentTo(new[] { "uniq1" });
      _jobDB.LoadJobsNotCopiedToSystem(dtime, dtime, includeDecremented: false)
        .Should().BeEmpty();

      var history = _jobDB.LoadJobHistory(dtime, dtime.AddMinutes(10));
      history.Jobs.Keys.Should().BeEquivalentTo(new[] { "uniq1", "uniq2" });
      history.Jobs["uniq1"].ScheduledBookingIds.Should().BeEquivalentTo(new[] { "AAA", "BBB" });
      history.Jobs["uniq1"].Decrements.Should().BeEquivalentTo(new[] {
        new DecrementQuantity() {
          DecrementId = 0,
          Proc1Path = 1,
          TimeUTC = time1,
          Quantity = 53
        },
        new DecrementQuantity() {
          DecrementId = 0,
          Proc1Path = 2,
          TimeUTC = time1,
          Quantity = 77
        }
      });
      history.Jobs["uniq2"].ScheduledBookingIds.Should().BeEquivalentTo(new[] { "CCC", "DDD" });
      history.Jobs["uniq2"].Decrements.Should().BeEquivalentTo(new[] {
        new DecrementQuantity() {
          DecrementId = 0,
          Proc1Path = 1,
          TimeUTC = time1,
          Quantity = 821
        }
      });

      //now second decrement
      var time2 = DateTime.UtcNow.AddHours(-1);
      _jobDB.AddNewDecrement(new[] {
        new NewDecrementQuantity() {
          JobUnique = "uniq1",
          Proc1Path = 1,
          Part = "part1",
          Quantity = 26
        },
        new NewDecrementQuantity() {
          JobUnique = "uniq2",
          Proc1Path = 1,
          Part = "part2",
          Quantity = 44
        },
      },
      time2,
      new[] {
        new RemovedBooking() {
          JobUnique = "uniq1",
          BookingId = "BBB"
        },
        new RemovedBooking() {
          JobUnique = "uniq2",
          BookingId = "CCC"
        }
      });

      var expected2 = new[] {
        new JobAndDecrementQuantity() {
          DecrementId = 1,
          JobUnique = "uniq1",
          Proc1Path = 1,
          TimeUTC = time2,
          Part = "part1",
          Quantity = 26
        },
        new JobAndDecrementQuantity() {
          DecrementId = 1,
          JobUnique = "uniq2",
          Proc1Path = 1,
          TimeUTC = time2,
          Part = "part2",
          Quantity = 44
        }
      };

      _jobDB.LoadDecrementQuantitiesAfter(-1).Should().BeEquivalentTo(expected1.Concat(expected2));
      _jobDB.LoadDecrementQuantitiesAfter(0).Should().BeEquivalentTo(expected2);
      _jobDB.LoadDecrementQuantitiesAfter(1).Should().BeEmpty();

      _jobDB.LoadDecrementQuantitiesAfter(time1.AddHours(-1)).Should().BeEquivalentTo(expected1.Concat(expected2));
      _jobDB.LoadDecrementQuantitiesAfter(time1.AddMinutes(30)).Should().BeEquivalentTo(expected2);
      _jobDB.LoadDecrementQuantitiesAfter(time2.AddMinutes(30)).Should().BeEmpty();

      _jobDB.LoadDecrementsForJob("uniq1").Should().BeEquivalentTo(new[] {
        new DecrementQuantity() {
          DecrementId = 0, Proc1Path = 1, TimeUTC = time1, Quantity = 53
        },
        new DecrementQuantity() {
          DecrementId = 0, Proc1Path = 2, TimeUTC = time1, Quantity = 77
        },
        new DecrementQuantity() {
          DecrementId = 1, Proc1Path = 1, TimeUTC = time2, Quantity = 26
        }
      });

      _jobDB.LoadJobsNotCopiedToSystem(dtime, dtime, includeDecremented: true).Select(j => j.UniqueStr)
        .Should().BeEquivalentTo(new[] { "uniq1" });
      _jobDB.LoadJobsNotCopiedToSystem(dtime, dtime, includeDecremented: false)
        .Should().BeEmpty();

      history = _jobDB.LoadJobHistory(dtime, dtime.AddMinutes(10));
      history.Jobs.Keys.Should().BeEquivalentTo(new[] { "uniq1", "uniq2" });
      history.Jobs["uniq1"].ScheduledBookingIds.Should().BeEquivalentTo(new[] { "AAA" });
      history.Jobs["uniq1"].Decrements.Should().BeEquivalentTo(new[] {
        new DecrementQuantity() {
          DecrementId = 0,
          Proc1Path = 1,
          TimeUTC = time1,
          Quantity = 53
        },
        new DecrementQuantity() {
          DecrementId = 0,
          Proc1Path = 2,
          TimeUTC = time1,
          Quantity = 77
        },
        new DecrementQuantity() {
          DecrementId = 1,
          Proc1Path = 1,
          TimeUTC = time2,
          Quantity = 26
        }
      });
      history.Jobs["uniq2"].ScheduledBookingIds.Should().BeEquivalentTo(new[] { "DDD" });
      history.Jobs["uniq2"].Decrements.Should().BeEquivalentTo(new[] {
        new DecrementQuantity() {
          DecrementId = 0,
          Proc1Path = 1,
          TimeUTC = time1,
          Quantity = 821
        },
        new DecrementQuantity() {
          DecrementId = 1,
          Proc1Path = 1,
          TimeUTC = time2,
          Quantity = 44
        }
      });
    }

    [Fact]
    public void Programs()
    {
      var job1 = new JobPlan("uniq", 2, new int[] { 2, 3 });
      SetJob1Data(job1);
      job1.ScheduleId = "1111";

      job1.GetMachiningStop(1, 1).First().ProgramName = "aaa";
      job1.GetMachiningStop(1, 1).First().ProgramRevision = null;

      job1.GetMachiningStop(1, 2).First().ProgramName = "aaa";
      job1.GetMachiningStop(1, 2).First().ProgramRevision = 1;

      job1.GetMachiningStop(2, 1).First().ProgramName = "bbb";
      job1.GetMachiningStop(2, 1).First().ProgramRevision = null;

      job1.GetMachiningStop(2, 2).First().ProgramName = "bbb";
      job1.GetMachiningStop(2, 2).First().ProgramRevision = 6;

      var initialWorks = RandUnfilledWorkorders();
      initialWorks[0] %= w => w.Programs.AddRange(new[] {
        new WorkorderProgram() { ProcessNumber = 1, ProgramName = "aaa", Revision = null },
        new WorkorderProgram() { ProcessNumber = 2, StopIndex = 0, ProgramName = "aaa", Revision = 1 },
        new WorkorderProgram() { ProcessNumber = 2, StopIndex = 1, ProgramName = "bbb", Revision = null }
      });
      initialWorks[1] %= w => w.Programs.Add(
        new WorkorderProgram() { ProcessNumber = 1, StopIndex = 0, ProgramName = "bbb", Revision = 6 }
      );

      _jobDB.AddJobs(new NewJobs
      {
        ScheduleId = job1.ScheduleId,
        Jobs = ImmutableList.Create(job1),
        CurrentUnfilledWorkorders = initialWorks.ToImmutableList(),
        Programs = ImmutableList.Create(
            new ProgramEntry()
            {
              ProgramName = "aaa",
              Revision = 0, // auto assign
              Comment = "aaa comment",
              ProgramContent = "aaa program content"
            },
            new ProgramEntry()
            {
              ProgramName = "bbb",
              Revision = 6, // new revision
              Comment = "bbb comment",
              ProgramContent = "bbb program content"
            }
          )
      }, null);

      job1.GetMachiningStop(1, 1).First().ProgramRevision = 1; // should lookup latest revision to 1
      job1.GetMachiningStop(2, 1).First().ProgramRevision = 6; // should lookup latest revision to 6
      initialWorks[0] %= w =>
      {
        w.Programs[0] %= p => p.Revision = 1;
        w.Programs[w.Programs.Count - 1] %= p => p.Revision = 6;
      };

      CheckJobEqual(job1, _jobDB.LoadJob(job1.UniqueStr), true);

      _jobDB.LoadMostRecentSchedule().CurrentUnfilledWorkorders.Should().BeEquivalentTo(initialWorks, options => options.ComparingByMembers<PartWorkorder>());
      _jobDB.MostRecentWorkorders().Should().BeEquivalentTo(initialWorks, options => options.ComparingByMembers<PartWorkorder>());

      _jobDB.MostRecentUnfilledWorkordersForPart(initialWorks[0].Part).Should().BeEquivalentTo(new[] { initialWorks[0] }, options => options.ComparingByMembers<PartWorkorder>());

      _jobDB.WorkordersById(initialWorks[0].WorkorderId).Should().BeEquivalentTo(new[] { initialWorks[0] }, options => options.ComparingByMembers<PartWorkorder>());

      _jobDB.LoadProgram("aaa", 1).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 1,
        Comment = "aaa comment",
      });
      _jobDB.LoadMostRecentProgram("aaa").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 1,
        Comment = "aaa comment",
      });
      _jobDB.LoadProgram("bbb", 6).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "bbb",
        Revision = 6,
        Comment = "bbb comment",
      });
      _jobDB.LoadMostRecentProgram("bbb").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "bbb",
        Revision = 6,
        Comment = "bbb comment",
      });
      _jobDB.LoadProgram("aaa", 2).Should().BeNull();
      _jobDB.LoadProgram("ccc", 1).Should().BeNull();

      _jobDB.LoadProgramContent("aaa", 1).Should().Be("aaa program content");
      _jobDB.LoadProgramContent("bbb", 6).Should().Be("bbb program content");
      _jobDB.LoadProgramContent("aaa", 2).Should().BeNull();
      _jobDB.LoadProgramContent("ccc", 1).Should().BeNull();
      _jobDB.LoadProgramsInCellController().Should().BeEmpty();

      // error on program content mismatch
      _jobDB.Invoking(j => j.AddJobs(new NewJobs
      {
        Jobs = ImmutableList<JobPlan>.Empty,
        Programs = ImmutableList.Create(
              new ProgramEntry()
              {
                ProgramName = "aaa",
                Revision = 0, // auto assign
                Comment = "aaa comment rev 2",
                ProgramContent = "aaa program content rev 2"
              },
              new ProgramEntry()
              {
                ProgramName = "bbb",
                Revision = 6, // existing revision
                Comment = "bbb comment",
                ProgramContent = "awofguhweoguhweg"
              }
            )
      }, null)
      ).Should().Throw<BadRequestException>().WithMessage("Program bbb rev6 has already been used and the program contents do not match.");

      _jobDB.Invoking(j => j.AddPrograms(new List<ProgramEntry> {
              new ProgramEntry() {
                ProgramName = "aaa",
                Revision = 0, // auto assign
                Comment = "aaa comment rev 2",
                ProgramContent = "aaa program content rev 2"
              },
              new ProgramEntry() {
                ProgramName = "bbb",
                Revision = 6, // existing revision
                Comment = "bbb comment",
                ProgramContent = "awofguhweoguhweg"
              },
      }, DateTime.Parse("2019-09-14T03:52:12Z")))
      .Should().Throw<BadRequestException>().WithMessage("Program bbb rev6 has already been used and the program contents do not match.");

      _jobDB.LoadProgram("aaa", 2).Should().BeNull();
      _jobDB.LoadMostRecentProgram("aaa").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 1,
        Comment = "aaa comment",
      });
      _jobDB.LoadMostRecentProgram("bbb").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "bbb",
        Revision = 6,
        Comment = "bbb comment",
      });
      _jobDB.LoadProgramContent("aaa", 1).Should().Be("aaa program content");
      _jobDB.LoadProgramContent("aaa", 2).Should().BeNull();

      // replaces workorders
      var newWorkorders = RandUnfilledWorkorders();
      newWorkorders[0] %= w => w.Programs.AddRange(new[] {
        new WorkorderProgram() { ProcessNumber = 1, ProgramName = "aaa", Revision = 1 },
        new WorkorderProgram() { ProcessNumber = 2, StopIndex = 0, ProgramName = "aaa", Revision = 2 },
        new WorkorderProgram() { ProcessNumber = 2, StopIndex = 1, ProgramName = "bbb", Revision = 6 }
      });
      newWorkorders[1] %= w => w.Programs.AddRange(new[] {
        new WorkorderProgram() { ProcessNumber = 1, StopIndex = 0, ProgramName = "ccc", Revision = 0 }
      });

      // replace an existing
      newWorkorders.Add(initialWorks[1] % (draft =>
      {
        draft.Quantity = 10;
        draft.Programs.Clear();
        draft.Programs.Add(
          new WorkorderProgram() { ProcessNumber = 1, StopIndex = 0, ProgramName = "ccc", Revision = 0 }
        );
      }));

      _jobDB.ReplaceWorkordersForSchedule(job1.ScheduleId, newWorkorders, new[] {
        new ProgramEntry() {
          ProgramName = "ccc",
          Revision = 0,
          Comment = "the ccc comment",
          ProgramContent = "ccc first program"
        }
      });

      newWorkorders[1] %= w => w.Programs[0] %= p => p.Revision = 1;
      newWorkorders[newWorkorders.Count - 1] %= w => w.Programs[0] %= p => p.Revision = 1;

      _jobDB.LoadMostRecentSchedule().CurrentUnfilledWorkorders.Should().BeEquivalentTo(newWorkorders, options => options.ComparingByMembers<PartWorkorder>()); // initialWorks have been archived and don't appear
      _jobDB.MostRecentWorkorders().Should().BeEquivalentTo(newWorkorders, options => options.ComparingByMembers<PartWorkorder>());

      _jobDB.WorkordersById(initialWorks[0].WorkorderId).Should().BeEquivalentTo(new[] { initialWorks[0] }, options => options.ComparingByMembers<PartWorkorder>()); // but still exist when looked up directly

      _jobDB.LoadMostRecentProgram("ccc").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "ccc",
        Revision = 1,
        Comment = "the ccc comment",
      });

      // now should ignore when program content matches exact revision or most recent revision
      _jobDB.AddJobs(new NewJobs
      {
        Jobs = ImmutableList<JobPlan>.Empty,
        Programs = ImmutableList.Create(
              new ProgramEntry()
              {
                ProgramName = "aaa",
                Revision = 0, // auto assign
                Comment = "aaa comment rev 2",
                ProgramContent = "aaa program content rev 2"
              },
              new ProgramEntry()
              {
                ProgramName = "bbb",
                Revision = 6, // existing revision
                Comment = "bbb comment",
                ProgramContent = "bbb program content"
              },
              new ProgramEntry()
              {
                ProgramName = "ccc",
                Revision = 0, // auto assign
                Comment = "ccc new comment", // comment does not match most recent revision
                ProgramContent = "ccc first program" // content matches most recent revision
              }
            )
      }, null);

      _jobDB.LoadProgram("aaa", 2).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 2, // creates new revision
        Comment = "aaa comment rev 2",
      });
      _jobDB.LoadMostRecentProgram("aaa").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 2,
        Comment = "aaa comment rev 2",
      });
      _jobDB.LoadMostRecentProgram("bbb").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "bbb",
        Revision = 6,
        Comment = "bbb comment",
      });
      _jobDB.LoadProgramContent("aaa", 2).Should().Be("aaa program content rev 2");
      _jobDB.LoadMostRecentProgram("ccc").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "ccc",
        Revision = 1,
        Comment = "the ccc comment",
      });
      _jobDB.LoadProgramContent("ccc", 1).Should().Be("ccc first program");

      //now set cell controller names
      _jobDB.LoadProgramsInCellController().Should().BeEmpty();
      _jobDB.SetCellControllerProgramForProgram("aaa", 1, "aaa-1");
      _jobDB.SetCellControllerProgramForProgram("bbb", 6, "bbb-6");

      _jobDB.ProgramFromCellControllerProgram("aaa-1").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 1,
        Comment = "aaa comment",
        CellControllerProgramName = "aaa-1"
      });
      _jobDB.LoadProgram("aaa", 1).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 1,
        Comment = "aaa comment",
        CellControllerProgramName = "aaa-1"
      });
      _jobDB.ProgramFromCellControllerProgram("bbb-6").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "bbb",
        Revision = 6,
        Comment = "bbb comment",
        CellControllerProgramName = "bbb-6"
      });
      _jobDB.LoadMostRecentProgram("bbb").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "bbb",
        Revision = 6,
        Comment = "bbb comment",
        CellControllerProgramName = "bbb-6"
      });
      _jobDB.ProgramFromCellControllerProgram("aagaiouhgi").Should().BeNull();
      _jobDB.LoadProgramsInCellController().Should().BeEquivalentTo(new[] {
        new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
            CellControllerProgramName = "aaa-1"
          },
        new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
            CellControllerProgramName = "bbb-6"
          }
      });

      _jobDB.SetCellControllerProgramForProgram("aaa", 1, null);

      _jobDB.LoadProgramsInCellController().Should().BeEquivalentTo(new[] {
        new ProgramRevision()
          {
            ProgramName = "bbb",
            Revision = 6,
            Comment = "bbb comment",
            CellControllerProgramName = "bbb-6"
          }
      });

      _jobDB.ProgramFromCellControllerProgram("aaa-1").Should().BeNull();
      _jobDB.LoadProgram("aaa", 1).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 1,
        Comment = "aaa comment",
      });

      _jobDB.Invoking(j => j.SetCellControllerProgramForProgram("aaa", 2, "bbb-6"))
        .Should().Throw<Exception>().WithMessage("Cell program name bbb-6 already in use");

      _jobDB.AddPrograms(new[] {
        new ProgramEntry() {
          ProgramName = "aaa",
          Revision = 0, // should be ignored because comment and content matches revision 1
          Comment = "aaa comment",
          ProgramContent = "aaa program content"
        },
        new ProgramEntry() {
          ProgramName = "bbb",
          Revision = 0, // allocate new
          Comment = "bbb comment rev7",
          ProgramContent = "bbb program content rev7"
        },
      }, job1.RouteStartingTimeUTC);

      _jobDB.LoadMostRecentProgram("aaa").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 2, // didn't allocate 3
        Comment = "aaa comment rev 2",
      });
      _jobDB.LoadMostRecentProgram("bbb").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "bbb",
        Revision = 7,
        Comment = "bbb comment rev7",
      });
      _jobDB.LoadProgramContent("bbb", 7).Should().Be("bbb program content rev7");

      // adds new when comment matches, but content does not
      _jobDB.AddPrograms(new[] {
        new ProgramEntry() {
          ProgramName = "aaa",
          Revision = 0, // allocate new
          Comment = "aaa comment", // comment matches
          ProgramContent = "aaa program content rev 3" // content does not
        }
      }, job1.RouteStartingTimeUTC);

      _jobDB.LoadMostRecentProgram("aaa").Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 3,
        Comment = "aaa comment",
      });
      _jobDB.LoadProgramContent("aaa", 3).Should().BeEquivalentTo("aaa program content rev 3");

      // loading all revisions
      _jobDB.LoadProgramRevisionsInDescendingOrderOfRevision("aaa", 3, startRevision: null)
        .Should().BeEquivalentTo(new[] {
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 3,
            Comment = "aaa comment",
          },
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 2,
            Comment = "aaa comment rev 2",
          },
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
          }
        }, options => options.WithStrictOrdering());
      _jobDB.LoadProgramRevisionsInDescendingOrderOfRevision("aaa", 1, startRevision: null)
        .Should().BeEquivalentTo(new[] {
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 3,
            Comment = "aaa comment",
          }
        }, options => options.WithStrictOrdering());
      _jobDB.LoadProgramRevisionsInDescendingOrderOfRevision("aaa", 2, startRevision: 1)
        .Should().BeEquivalentTo(new[] {
          new ProgramRevision()
          {
            ProgramName = "aaa",
            Revision = 1,
            Comment = "aaa comment",
          }
        }, options => options.WithStrictOrdering());

      _jobDB.LoadProgramRevisionsInDescendingOrderOfRevision("wesrfohergh", 10000, null).Should().BeEmpty();
    }

    [Fact]
    public void NegativeProgramRevisions()
    {
      // add an existing revision 6 for bbb and 3,4 for ccc
      _jobDB.AddJobs(new NewJobs
      {
        Jobs = ImmutableList<JobPlan>.Empty,
        Programs = ImmutableList.Create(
            new ProgramEntry()
            {
              ProgramName = "bbb",
              Revision = 6,
              Comment = "bbb comment 6",
              ProgramContent = "bbb program content 6"
            },
            new ProgramEntry()
            {
              ProgramName = "ccc",
              Revision = 3,
              Comment = "ccc comment 3",
              ProgramContent = "ccc program content 3"
            },
            new ProgramEntry()
            {
              ProgramName = "ccc",
              Revision = 4,
              Comment = "ccc comment 4",
              ProgramContent = "ccc program content 4"
            }
          )
      }, null);

      var job1 = new JobPlan("uniq", 2, new int[] { 2, 3 });
      SetJob1Data(job1);
      job1.ScheduleId = "1111";

      job1.GetMachiningStop(1, 1).First().ProgramName = "aaa";
      job1.GetMachiningStop(1, 1).First().ProgramRevision = -1;

      job1.GetMachiningStop(1, 2).First().ProgramName = "aaa";
      job1.GetMachiningStop(1, 2).First().ProgramRevision = -2;

      job1.GetMachiningStop(2, 1).First().ProgramName = "bbb";
      job1.GetMachiningStop(2, 1).First().ProgramRevision = -1;

      job1.GetMachiningStop(2, 2).First().ProgramName = "bbb";
      job1.GetMachiningStop(2, 2).First().ProgramRevision = -2;

      job1.GetMachiningStop(2, 3).First().ProgramName = "ccc";
      job1.GetMachiningStop(2, 3).First().ProgramRevision = -2;

      var initialWorks = RandUnfilledWorkorders();
      initialWorks[0] %= w => w.Programs.AddRange(new[] {
        new WorkorderProgram() { ProcessNumber = 1, ProgramName = "aaa", Revision = -1 },
        new WorkorderProgram() { ProcessNumber = 2, StopIndex = 0, ProgramName = "aaa", Revision = -2 },
        new WorkorderProgram() { ProcessNumber = 2, StopIndex = 1, ProgramName = "bbb", Revision = -1 }
      });
      initialWorks[1] %= w => w.Programs.AddRange(new[] {
        new WorkorderProgram() { ProcessNumber = 1, StopIndex = 0, ProgramName = "bbb", Revision = -2 },
        new WorkorderProgram() { ProcessNumber = 2, StopIndex = 1, ProgramName = "ccc", Revision = -1 },
        new WorkorderProgram() { ProcessNumber = 2, StopIndex = 2, ProgramName = "ccc", Revision = -2 }
      });

      _jobDB.AddJobs(new NewJobs
      {
        ScheduleId = job1.ScheduleId,
        Jobs = ImmutableList.Create(job1),
        CurrentUnfilledWorkorders = initialWorks.ToImmutableList(),
        Programs = ImmutableList.Create(
            new ProgramEntry()
            {
              ProgramName = "aaa",
              Revision = -1, // should be created to be revision 1
              Comment = "aaa comment 1",
              ProgramContent = "aaa program content for 1"
            },
            new ProgramEntry()
            {
              ProgramName = "aaa",
              Revision = -2, // should be created to be revision 2
              Comment = "aaa comment 2",
              ProgramContent = "aaa program content for 2"
            },
            new ProgramEntry()
            {
              ProgramName = "bbb",
              Revision = -1, // matches latest content so should be converted to 6
              Comment = "bbb other comment", // comment doesn't match but is ignored
              ProgramContent = "bbb program content 6"
            },
            new ProgramEntry()
            {
              ProgramName = "bbb",
              Revision = -2, // assigned a new val 7 since content differs
              Comment = "bbb comment 7",
              ProgramContent = "bbb program content 7"
            },
            new ProgramEntry()
            {
              ProgramName = "ccc",
              Revision = -1, // assigned to 3 (older than most recent) because comment and content match
              Comment = "ccc comment 3",
              ProgramContent = "ccc program content 3"
            },
            new ProgramEntry()
            {
              ProgramName = "ccc",
              Revision = -2, // assigned a new val since doesn't match existing, even if comment matches
              Comment = "ccc comment 3",
              ProgramContent = "ccc program content 5"
            }
          )
      }, null);

      job1.GetMachiningStop(1, 1).First().ProgramRevision = 1;
      job1.GetMachiningStop(1, 2).First().ProgramRevision = 2;
      job1.GetMachiningStop(2, 1).First().ProgramRevision = 6;
      job1.GetMachiningStop(2, 2).First().ProgramRevision = 7;
      job1.GetMachiningStop(2, 3).First().ProgramRevision = 5;

      initialWorks[0] %= w =>
      {
        w.Programs[0] %= p => p.Revision = 1; // aaa -1
        w.Programs[1] %= p => p.Revision = 2; // aaa -2
        w.Programs[2] %= p => p.Revision = 6; // bbb -1
      };
      initialWorks[1] %= w =>
      {
        w.Programs[0] %= p => p.Revision = 7; // bbb -2
        w.Programs[1] %= p => p.Revision = 3; // ccc -1
        w.Programs[2] %= p => p.Revision = 5; // ccc -2
      };

      CheckJobEqual(job1, _jobDB.LoadJob(job1.UniqueStr), true);

      _jobDB.LoadMostRecentSchedule().CurrentUnfilledWorkorders.Should().BeEquivalentTo(initialWorks, options => options.ComparingByMembers<PartWorkorder>());

      _jobDB.LoadProgram("aaa", 1).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 1,
        Comment = "aaa comment 1",
      });
      _jobDB.LoadProgramContent("aaa", 1).Should().Be("aaa program content for 1");
      _jobDB.LoadProgram("aaa", 2).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "aaa",
        Revision = 2,
        Comment = "aaa comment 2",
      });
      _jobDB.LoadProgramContent("aaa", 2).Should().Be("aaa program content for 2");
      _jobDB.LoadMostRecentProgram("aaa").Revision.Should().Be(2);


      _jobDB.LoadProgram("bbb", 6).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "bbb",
        Revision = 6,
        Comment = "bbb comment 6",
      });
      _jobDB.LoadProgramContent("bbb", 6).Should().Be("bbb program content 6");
      _jobDB.LoadProgram("bbb", 7).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "bbb",
        Revision = 7,
        Comment = "bbb comment 7",
      });
      _jobDB.LoadProgramContent("bbb", 7).Should().Be("bbb program content 7");
      _jobDB.LoadMostRecentProgram("bbb").Revision.Should().Be(7);

      _jobDB.LoadProgram("ccc", 3).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "ccc",
        Revision = 3,
        Comment = "ccc comment 3",
      });
      _jobDB.LoadProgramContent("ccc", 3).Should().Be("ccc program content 3");
      _jobDB.LoadProgram("ccc", 4).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "ccc",
        Revision = 4,
        Comment = "ccc comment 4",
      });
      _jobDB.LoadProgramContent("ccc", 4).Should().Be("ccc program content 4");
      _jobDB.LoadProgram("ccc", 5).Should().BeEquivalentTo(new ProgramRevision()
      {
        ProgramName = "ccc",
        Revision = 5,
        Comment = "ccc comment 3",
      });
      _jobDB.LoadProgramContent("ccc", 5).Should().Be("ccc program content 5");
      _jobDB.LoadMostRecentProgram("ccc").Revision.Should().Be(5);
    }

    private void CheckJobs(JobPlan job1, JobPlan job2, JobPlan job3, string schId, IReadOnlyDictionary<string, int> extraParts, IEnumerable<PartWorkorder> works)
    {
      CheckJobEqual(job1, _jobDB.LoadJob(job1.UniqueStr), true);
      if ((job2 != null)) CheckJobEqual(job2, _jobDB.LoadJob(job2.UniqueStr), true);
      if ((job3 != null)) CheckJobEqual(job3, _jobDB.LoadJob(job3.UniqueStr), true);

      var latestSch = _jobDB.LoadMostRecentSchedule();
      Assert.Equal(schId == null ? "" : schId, latestSch.LatestScheduleId);
      Assert.Equal(extraParts == null ? new Dictionary<string, int>() : extraParts,
       latestSch.ExtraParts);

      if (works == null) works = new List<PartWorkorder>();
      CheckWorkordersEqual(works, latestSch.CurrentUnfilledWorkorders);

      var jobsLst = _jobDB.LoadUnarchivedJobs();
      var jobs = jobsLst.ToDictionary(x => x.UniqueStr, x => x);

      if (job2 == null)
      {
        jobs.Count.Should().Be(1);
      }
      else if (job3 == null)
      {
        jobs.Count().Should().Be(2);
      }
      else
      {
        jobs.Count().Should().Be(3);
      }
      Assert.True(jobs.ContainsKey(job1.UniqueStr));
      if ((job2 != null)) Assert.True(jobs.ContainsKey(job2.UniqueStr));
      if ((job3 != null)) Assert.True(jobs.ContainsKey(job3.UniqueStr));

      CheckJobEqual(job1, jobs[job1.UniqueStr], true);
      if ((job2 != null)) CheckJobEqual(job2, jobs[job2.UniqueStr], true);
      if ((job3 != null)) CheckJobEqual(job3, jobs[job3.UniqueStr], true);
    }

    private void CheckJobsDate(JobPlan job1, JobPlan job2, JobPlan job3)
    {
      var now = DateTime.UtcNow;

      //job1 is from -10 minutes to now
      //job2 is from -10 hours to -2 hours
      //job3 is the same as job1

      //overlap the start of job1: note if job1 != null then job3 also != null
      var justJob1_3 = _jobDB.LoadJobHistory(now.AddMinutes(-20), now.AddMinutes(-5));
      if (job1 == null)
      {
        justJob1_3.Jobs.Should().BeEmpty();
      }
      else if (job3 == null)
      {
        justJob1_3.Jobs.Count.Should().Be(1);
        CheckJobEqual(job1, justJob1_3.Jobs[job1.UniqueStr], true);
        justJob1_3.Jobs[job1.UniqueStr].Decrements.Should().BeEmpty();
      }
      else
      {
        justJob1_3.Jobs.Count.Should().Be(2);
        CheckJobEqual(job1, justJob1_3.Jobs[job1.UniqueStr], true);
        CheckJobEqual(job3, justJob1_3.Jobs[job3.UniqueStr], true);
        justJob1_3.Jobs[job1.UniqueStr].Decrements.Should().BeEmpty();
        justJob1_3.Jobs[job3.UniqueStr].Decrements.Should().BeEmpty();
      }

      //overlap the end of job1
      var justJob1a = _jobDB.LoadJobHistory(now.AddMinutes(-5), now.AddMinutes(10));
      if (job1 == null)
      {
        justJob1a.Jobs.Should().BeEmpty();
      }
      else if (job3 == null)
      {
        justJob1a.Jobs.Count.Should().Be(1);
        CheckJobEqual(job1, justJob1a.Jobs[job1.UniqueStr], true);
      }
      else
      {
        justJob1a.Jobs.Count.Should().Be(2);
        CheckJobEqual(job1, justJob1a.Jobs[job1.UniqueStr], true);
        CheckJobEqual(job3, justJob1a.Jobs[job3.UniqueStr], true);
      }

      //not overlapping
      _jobDB.LoadJobHistory(now.AddMinutes(-70), now.AddMinutes(-40)).Jobs.Should().BeEmpty();

      //everything
      var all = _jobDB.LoadJobHistory(now.AddHours(-6), now.AddMinutes(10));
      int cnt = 0;
      if (job1 != null)
      {
        CheckJobEqual(job1, all.Jobs[job1.UniqueStr], true);
        cnt += 1;
      }
      if (job2 != null)
      {
        CheckJobEqual(job2, all.Jobs[job2.UniqueStr], true);
        cnt += 1;
      }
      if (job3 != null)
      {
        CheckJobEqual(job3, all.Jobs[job3.UniqueStr], true);
        cnt += 1;
      }
      Assert.Equal(cnt, all.Jobs.Count);
    }

    private void CheckSimStationUse(IEnumerable<SimulatedStationUtilization> simStations)
    {
      var now = DateTime.UtcNow;
      var fromDb = ToList(_jobDB.LoadJobHistory(now.AddMinutes(-500), now.AddMinutes(10)).StationUse);
      var actualSims = ToList(simStations);

      CheckSimStationEqual(actualSims, fromDb);
    }

    private void CheckSimStationEqual(IEnumerable<SimulatedStationUtilization> expected, IEnumerable<SimulatedStationUtilization> actual)
    {
      Assert.Equal(actual.Count(), expected.Count());

      var fromDb = actual.OrderBy(x => (x.ScheduleId, x.EndUTC)).ToList();
      var expectedL = expected.OrderBy(x => (x.ScheduleId, x.EndUTC)).ToList();

      for (int i = 0; i < fromDb.Count(); i++)
      {
        var db = fromDb[i];
        var sim = expectedL[i];
        Assert.Equal(db.ScheduleId, sim.ScheduleId);
        Assert.Equal(db.StationGroup, sim.StationGroup);
        Assert.Equal(db.StationNum, sim.StationNum);
        Assert.Equal(db.StartUTC, sim.StartUTC);
        Assert.Equal(db.EndUTC, sim.EndUTC);
        Assert.Equal(db.UtilizationTime, sim.UtilizationTime);
        Assert.Equal(db.PlannedDownTime, sim.PlannedDownTime);
      }
    }

    private static List<T> ToList<T>(IEnumerable<T> l)
    {
      var ret = new List<T>();
      foreach (T k in l)
        ret.Add(k);
      return ret;
    }

    private static void CheckWorkordersEqual(IEnumerable<PartWorkorder> expected, IEnumerable<PartWorkorder> actual)
    {
      var expectedWorks = expected.OrderBy(w => (w.WorkorderId, w.Part)).ToList();
      var actualWorks = actual.OrderBy(w => (w.WorkorderId, w.Part)).ToList();
      Assert.Equal(expectedWorks.Count, actualWorks.Count);
      for (int i = 0; i < expectedWorks.Count; i++)
        CheckWorkorderEqual(expectedWorks[i], actualWorks[i]);
    }

    private static void CheckWorkorderEqual(PartWorkorder w1, PartWorkorder w2)
    {
      Assert.Equal(w1.WorkorderId, w2.WorkorderId);
      Assert.Equal(w1.Part, w2.Part);
      Assert.Equal(w1.Quantity, w2.Quantity);
      Assert.Equal(w1.DueDate, w2.DueDate);
      Assert.Equal(w1.Priority, w2.Priority);
      w1.Programs.Should().BeNullOrEmpty();
      w2.Programs.Should().BeNullOrEmpty();
    }

    private byte[] LoadDebugData(string schId)
    {
      SqliteConnection conn = (SqliteConnection)
        _jobDB.GetType().GetField("_connection",
                                  System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        ).GetValue(_jobDB);
      var cmd = conn.CreateCommand();
      cmd.CommandText = "SELECT DebugMessage FROM schedule_debug WHERE ScheduleId = @sch";
      cmd.Parameters.Add("sch", SqliteType.Text).Value = schId;
      return (byte[])cmd.ExecuteScalar();
    }
  }

  public class JobEqualityChecks
  {
    protected static void EqualSort<T>(IEnumerable<T> e1, IEnumerable<T> e2)
    {
      var lst1 = new List<T>(e1);
      lst1.Sort();
      var lst2 = new List<T>(e2);
      lst2.Sort();
      Assert.Equal(lst1, lst2);
    }

    private static void CheckHoldEqual(JobHoldPattern h1, JobHoldPattern h2)
    {

      DateTime d1 = h1.HoldUnholdPatternStartUTC;
      DateTime d2 = h1.HoldUnholdPatternStartUTC;
      d1 = new DateTime(d1.Year, d1.Month, d1.Day, d1.Hour, d1.Minute, d1.Second);
      d2 = new DateTime(d2.Year, d2.Month, d2.Day, d2.Hour, d2.Minute, d2.Second);

      Assert.Equal(h1.UserHold, h2.UserHold);
      Assert.Equal(h1.ReasonForUserHold, h2.ReasonForUserHold);
      Assert.Equal(d1, d2);
      Assert.Equal(h1.HoldUnholdPatternRepeats, h2.HoldUnholdPatternRepeats);
      Assert.Equal(h1.HoldUnholdPattern.Count, h2.HoldUnholdPattern.Count);
      for (int i = 0; i < h1.HoldUnholdPattern.Count; i++)
      {
        Assert.Equal(h1.HoldUnholdPattern[i], h2.HoldUnholdPattern[i]);
      }
    }

    public static void CheckPlanEqual(JobPlan job1, JobPlan job2, bool checkHolds)
    {
      Assert.NotNull(job1);
      Assert.NotNull(job2);

      Assert.Equal(job1.PartName, job2.PartName);
      Assert.Equal(job1.UniqueStr, job2.UniqueStr);
      Assert.Equal(job1.NumProcesses, job2.NumProcesses);
      Assert.Equal(job1.RouteStartingTimeUTC, job2.RouteStartingTimeUTC);
      Assert.Equal(job1.RouteEndingTimeUTC, job2.RouteEndingTimeUTC);
      Assert.Equal(job1.Archived, job2.Archived);
      Assert.Equal(job1.JobCopiedToSystem, job2.JobCopiedToSystem);
      EqualSort(job1.ScheduledBookingIds, job2.ScheduledBookingIds);
      Assert.Equal(job1.ScheduleId, job2.ScheduleId);
      Assert.Equal(job1.ManuallyCreatedJob, job2.ManuallyCreatedJob);


      if (checkHolds)
        CheckHoldEqual(job1.HoldEntireJob, job2.HoldEntireJob);

      for (int proc = 1; proc <= job1.NumProcesses; proc++)
      {
        Assert.Equal(job1.GetNumPaths(proc), job2.GetNumPaths(proc));
      }

      for (int path = 1; path <= job1.GetNumPaths(1); path++)
      {
        Assert.Equal(job1.GetPlannedCyclesOnFirstProcess(path), job2.GetPlannedCyclesOnFirstProcess(path));
      }

      for (int proc = 1; proc <= job1.NumProcesses; proc++)
      {
        for (int path = 1; path <= job1.GetNumPaths(proc); path++)
        {
          Assert.Equal(job1.GetSimulatedStartingTimeUTC(proc, path),
             job2.GetSimulatedStartingTimeUTC(proc, path));

          Assert.Equal(job1.PartsPerPallet(proc, path),
             job2.PartsPerPallet(proc, path));

          Assert.Equal(job1.GetPathGroup(proc, path),
             job2.GetPathGroup(proc, path));

          Assert.Equal(job1.GetInputQueue(proc, path), job2.GetInputQueue(proc, path));
          Assert.Equal(job1.GetOutputQueue(proc, path), job2.GetOutputQueue(proc, path));

          if (proc == 1)
          {
            Assert.Equal(job1.GetCasting(path), job2.GetCasting(path));
          }

          Assert.Equal(job1.GetSimulatedProduction(proc, path), job2.GetSimulatedProduction(proc, path));
          Assert.Equal(job1.GetSimulatedAverageFlowTime(proc, path), job2.GetSimulatedAverageFlowTime(proc, path));

          Assert.Equal(job1.GetExpectedLoadTime(proc, path), job2.GetExpectedLoadTime(proc, path));
          Assert.Equal(job1.GetExpectedUnloadTime(proc, path), job2.GetExpectedUnloadTime(proc, path));

          EqualSort(job1.LoadStations(proc, path), job2.LoadStations(proc, path));
          EqualSort(job1.UnloadStations(proc, path), job2.UnloadStations(proc, path));
          EqualSort(job1.PlannedPallets(proc, path), job2.PlannedPallets(proc, path));

          Assert.Equal(job1.PlannedFixture(proc, path), job2.PlannedFixture(proc, path));

          CheckInspEqual(job1.PathInspections(proc, path), job2.PathInspections(proc, path));

          if (checkHolds)
          {
            CheckHoldEqual(job1.HoldMachining(proc, path), job2.HoldMachining(proc, path));
            CheckHoldEqual(job1.HoldLoadUnload(proc, path), job2.HoldLoadUnload(proc, path));
          }


          var e1 = job1.GetMachiningStop(proc, path).GetEnumerator();
          var e2 = job2.GetMachiningStop(proc, path).GetEnumerator();

          while (e1.MoveNext())
          {
            if (!e2.MoveNext())
            {
              Assert.True(false, "Unequal number of routes");
            }

            JobMachiningStop route1 = e1.Current;
            JobMachiningStop route2 = e2.Current;

            if (route1 == null)
            {
              Assert.Null(route2);
            }
            else
            {
              Assert.NotNull(route2);
              Assert.Equal(route1.ExpectedCycleTime, route2.ExpectedCycleTime);
              EqualSort(route1.Stations, route2.Stations);
              Assert.Equal(route1.ProgramName, route2.ProgramName);
              Assert.Equal(route1.ProgramRevision, route2.ProgramRevision);
              Assert.Equal(route1.Tools, route2.Tools);
            }
          }
        }
      }
    }

    public static void CheckJobEqual(JobPlan job1, JobPlan job2, bool checkHolds)
    {
      CheckPlanEqual(job1, job2, checkHolds);
    }

    private static void CheckInspEqual(IEnumerable<PathInspection> i1, IEnumerable<PathInspection> i2)
    {
      var i2Copy = new List<PathInspection>(i2);
      foreach (var j1 in i1)
      {
        foreach (var j2 in i2Copy)
        {
          if (j1.InspectionType == j2.InspectionType
              && j1.Counter == j2.Counter
              && j1.MaxVal == j2.MaxVal
              && j1.TimeInterval == j2.TimeInterval
              && j1.RandomFreq == j2.RandomFreq
              && j1.ExpectedInspectionTime == j2.ExpectedInspectionTime)
          {
            i2Copy.Remove(j2);
            goto found;
          }
        }

        Assert.True(false, "Unable to find " + j1.InspectionType + " " + j1.Counter);

      found:;
      }

      if (i2Copy.Count > 0)
      {
        Assert.True(false, "i2 has extra stuff");
      }
    }

  }
}
