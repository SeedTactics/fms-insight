/* Copyright (c) 2019, John Lenz

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
using Xunit;
using FluentAssertions;

namespace MachineWatchTest
{

  public class JobDBUpgradeSpec : IDisposable
  {
    private readonly JobDB _jobs;
    private string _tempFile;

    public JobDBUpgradeSpec()
    {
      _tempFile = System.IO.Path.GetTempFileName();
      System.IO.File.Copy("job.v16.db", _tempFile, overwrite: true);
      _jobs = JobDB.Config.InitializeJobDatabase(_tempFile).OpenConnection();
    }

    public void Dispose()
    {
      _jobs.Close();
      if (!string.IsNullOrEmpty(_tempFile) && System.IO.File.Exists(_tempFile))
        System.IO.File.Delete(_tempFile);
    }

    [Fact]
    public void ConvertsJobFromV16()
    {
      var expected = CreateJob();
      var actual = _jobs.LoadJob("Unique1");

      JobEqualityChecks.CheckJobEqual(expected, actual, true);
    }

    [Fact]
    public void CreatesNewJob()
    {
      var old = CreateJob();
      var newJob = new JobPlan(old, "mynewunique");
      newJob.PathInspections(1, 1).Clear();
      newJob.PathInspections(1, 2).Clear();
      newJob.PathInspections(2, 1).Clear();
      newJob.PathInspections(2, 2).Clear();
      newJob.PathInspections(2, 3).Clear();

      _jobs.AddJobs(new NewJobs()
      {
        Jobs = new List<JobPlan> { newJob }
      }, null);

      JobEqualityChecks.CheckJobEqual(newJob, _jobs.LoadJob("mynewunique"), true);
    }

    private JobPlan CreateJob()
    {
      var job1 = new JobPlan("Unique1", 2, new int[] { 2, 3 });
      job1.PartName = "Job1";
      job1.SetPlannedCyclesOnFirstProcess(1, 125);
      job1.SetPlannedCyclesOnFirstProcess(2, 53);
      job1.RouteStartingTimeUTC = DateTime.Parse("2019-10-22 20:24 GMT").ToUniversalTime();
      job1.RouteEndingTimeUTC = job1.RouteStartingTimeUTC.AddHours(100);
      job1.Archived = false;
      job1.JobCopiedToSystem = true;
      job1.ScheduleId = "Job1tag1245";
      job1.HoldEntireJob.UserHold = true;
      job1.HoldEntireJob.ReasonForUserHold = "test string";
      job1.HoldEntireJob.HoldUnholdPatternStartUTC = job1.RouteStartingTimeUTC;
      job1.HoldEntireJob.HoldUnholdPatternRepeats = true;
      job1.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(10));
      job1.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(18));
      job1.HoldEntireJob.HoldUnholdPattern.Add(TimeSpan.FromMinutes(125));
      job1.Comment = "Hello there";
      job1.CreateMarkerData = true;
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

      job1.SetSimulatedStartingTimeUTC(1, 1, DateTime.Parse("1/5/2011 11:34 PM GMT").ToUniversalTime());
      job1.SetSimulatedStartingTimeUTC(1, 2, DateTime.Parse("2/10/2011 12:45 AM GMT").ToUniversalTime());
      job1.SetSimulatedStartingTimeUTC(2, 1, DateTime.Parse("3/14/2011 2:03 AM GMT").ToUniversalTime());
      job1.SetSimulatedStartingTimeUTC(2, 2, DateTime.Parse("4/20/2011 3:22 PM GMT").ToUniversalTime());
      job1.SetSimulatedStartingTimeUTC(2, 3, DateTime.Parse("5/22/2011 4:18 AM GMT").ToUniversalTime());

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
      job1.SetFixtureFace(1, 2, "ABC", 4);
      job1.SetFixtureFace(2, 1, "Fix123", 6);
      // 2, 2 has non-integer face, so should be ignored
      job1.SetFixtureFace(2, 3, "Fix17", 7);

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
      route.Stations.Add(12);
      route.Stations.Add(23);
      route.ProgramName = "Emily";
      route.ExpectedCycleTime = TimeSpan.FromHours(1.2);
      route.Tools["tool1"] = TimeSpan.FromMinutes(30);
      route.Tools["tool2"] = TimeSpan.FromMinutes(35);
      job1.AddMachiningStop(1, 1, route);

      route = new JobMachiningStop("Other Machine");
      route.Stations.Add(23);
      route.Stations.Add(12);
      route.ProgramName = "awef";
      route.ExpectedCycleTime = TimeSpan.FromHours(2.8);
      route.Tools["tool1"] = TimeSpan.FromMinutes(9);
      route.Tools["tool33"] = TimeSpan.FromMinutes(42);
      job1.AddMachiningStop(1, 2, route);

      route = new JobMachiningStop("Test");
      route.Stations.Add(64);
      route.Stations.Add(323);
      route.ProgramName = "Goodbye";
      route.ExpectedCycleTime = TimeSpan.FromHours(6.3);
      route.Tools["tool2"] = TimeSpan.FromMinutes(12);
      route.Tools["tool44"] = TimeSpan.FromMinutes(99);
      job1.AddMachiningStop(2, 1, route);

      route = new JobMachiningStop("Test");
      route.Stations.Add(32);
      route.Stations.Add(64);
      route.ProgramName = "wefq";
      job1.AddMachiningStop(2, 2, route);

      route = new JobMachiningStop("Test");
      route.Stations.Add(245);
      route.Stations.Add(36);
      route.ProgramName = "dduuude";
      job1.AddMachiningStop(2, 1, route);

      route = new JobMachiningStop("Test");
      route.Stations.Add(23);
      route.Stations.Add(53);
      route.ProgramName = "so cool";
      job1.AddMachiningStop(2, 2, route);

      // insp1 is on process 1, all paths
      var insp1 = new PathInspection() { InspectionType = "Insp1", Counter = "counter1", MaxVal = 53, RandomFreq = -1, TimeInterval = TimeSpan.FromMinutes(100) };
      for (int path = 1; path <= job1.GetNumPaths(1); path++) job1.PathInspections(1, path).Add(insp1);

      // insp2 has no InspProc so is final process 2.
      var insp2 = new PathInspection() { InspectionType = "Insp2", Counter = "counter1", MaxVal = 12, RandomFreq = -1, TimeInterval = TimeSpan.FromMinutes(64) };
      for (int path = 1; path <= job1.GetNumPaths(2); path++) job1.PathInspections(2, path).Add(insp2);

      // insp3 on first process
      var insp3 = new PathInspection() { InspectionType = "Insp3", Counter = "abcdef", MaxVal = 175, RandomFreq = -1, TimeInterval = TimeSpan.FromMinutes(121) };
      for (int path = 1; path <= job1.GetNumPaths(1); path++) job1.PathInspections(1, path).Add(insp3);

      // insp4 on final proc
      var insp4 = new PathInspection() { InspectionType = "Insp4", Counter = "counter2", RandomFreq = 16.12, MaxVal = -1, TimeInterval = TimeSpan.FromMinutes(33) };
      for (int path = 1; path <= job1.GetNumPaths(2); path++) job1.PathInspections(2, path).Add(insp4);

      // insp5 on final proc
      var insp5 = new PathInspection() { InspectionType = "Insp5", Counter = "counter3", RandomFreq = 0.544, MaxVal = -1, TimeInterval = TimeSpan.FromMinutes(44) };
      for (int path = 1; path <= job1.GetNumPaths(2); path++) job1.PathInspections(2, path).Add(insp5);

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

      return job1;
    }

    /*
    public void CreateV16()
    {
      var job1 = CreateJob();
      _jobs.AddJobs(new NewJobs() { Jobs = new List<JobPlan> { job1 } }, null);
    }
    */

  }

}