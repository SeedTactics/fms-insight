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
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using Xunit;
using FluentAssertions;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace MachineWatchTest
{

  public class EventDBUpgradeSpec : IDisposable
  {
    private readonly IRepository _log;
    private string _tempLogFile;
    private string _tempJobFile;

    public EventDBUpgradeSpec()
    {
      _tempLogFile = System.IO.Path.GetTempFileName();
      System.IO.File.Copy("log.v17.db", _tempLogFile, overwrite: true);
      _tempJobFile = System.IO.Path.GetTempFileName();
      System.IO.File.Copy("job.v16.db", _tempJobFile, overwrite: true);
      _log = RepositoryConfig.InitializeEventDatabase(new FMSSettings(), _tempLogFile, null, _tempJobFile).OpenConnection();
    }

    public void Dispose()
    {
      _log.Dispose();
      if (!string.IsNullOrEmpty(_tempLogFile) && System.IO.File.Exists(_tempLogFile))
        System.IO.File.Delete(_tempLogFile);
      if (!string.IsNullOrEmpty(_tempJobFile) && System.IO.File.Exists(_tempJobFile))
        System.IO.File.Delete(_tempJobFile);
    }

    [Fact]
    public void ConvertsMaterialFromV17()
    {
      // existing v17 file has the following data in it
      var now = new DateTime(2018, 7, 12, 5, 6, 7, DateTimeKind.Utc);

      var mat1_1 = new LogMaterial(1, "uuu1", 1, "part1", 2, "serial1", "work1", face: "A");
      var mat1_2 = new LogMaterial(1, "uuu1", 2, "part1", 2, "serial1", "work1", face: "B");
      var mat2_1 = new LogMaterial(2, "uuu1", 1, "part1", 2, "serial2", "", face: "C");
      var mat2_2 = new LogMaterial(2, "uuu1", 1, "part1", 2, "serial2", "", face: "D");
      var mat3 = new LogMaterial(3, "uuu2", 1, "part2", 1, "", "work3", face: "E");

      _log.GetLogEntries(now, now.AddDays(1)).Should().BeEquivalentTo(new[] {
  new LogEntry(
    cntr: -1,
    mat: new [] {mat1_1, mat2_1},
    pal: "3",
    ty: LogType.MachineCycle,
    locName: "MC",
    locNum: 1,
    prog: "proggg",
    start: false,
    endTime: now,
    result: "result",
    endOfRoute: false
  ),
  new LogEntry(
    cntr: -1,
    mat: new [] {mat1_2, mat2_2},
    pal: "5",
    ty: LogType.MachineCycle,
    locName: "MC",
    locNum: 1,
    prog: "proggg2",
    start: false,
    endTime: now.AddMinutes(10),
    result: "result2",
    endOfRoute: false
  ),
  new LogEntry(
    cntr: -1,
    mat: new [] {mat1_1},
    pal: "",
    ty: LogType.PartMark,
    locName: "Mark",
    locNum: 1,
    prog: "MARK",
    start: false,
    endTime: now.AddMinutes(20),
    result: "serial1",
    endOfRoute: false
  ),
  new LogEntry(
    cntr: -1,
    mat: new [] {mat1_1},
    pal: "",
    ty: LogType.OrderAssignment,
    locName: "Order",
    locNum: 1,
    prog: "",
    start: false,
    endTime: now.AddMinutes(30),
    result: "work1",
    endOfRoute: false
  ),
  new LogEntry(
    cntr: -1,
    mat: new [] {mat2_2},
    pal: "",
    ty: LogType.PartMark,
    locName: "Mark",
    locNum: 1,
    prog: "MARK",
    start: false,
    endTime: now.AddMinutes(40),
    result: "serial2",
    endOfRoute: false
  ),
  new LogEntry(
    cntr: -1,
    mat: new [] {mat3},
    pal: "1",
    ty: LogType.LoadUnloadCycle,
    locName: "L/U",
    locNum: 5,
    prog: "LOAD",
    start: false,
    endTime: now.AddMinutes(50),
    result: "LOAD",
    endOfRoute: false
  ),
  new LogEntry(
    cntr: -1,
    mat: new [] {mat3},
    pal: "",
    ty: LogType.OrderAssignment,
    locName: "Order",
    locNum: 1,
    prog: "",
    start: false,
    endTime: now.AddMinutes(60),
    result: "work3",
    endOfRoute: false
  ),
      }, options =>
  options.Excluding(x => x.Counter).ComparingByMembers<LogEntry>()
      );

      _log.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 1,
        JobUnique = "uuu1",
        PartName = "part1",
        NumProcesses = 2,
        Workorder = "work1",
        Serial = "serial1",
      });
      _log.GetMaterialDetails(2).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 2,
        JobUnique = "uuu1",
        PartName = "part1",
        NumProcesses = 2,
        Workorder = null,
        Serial = "serial2",
      });
      _log.GetMaterialDetails(3).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 3,
        JobUnique = "uuu2",
        PartName = "part2",
        NumProcesses = 1,
        Workorder = "work3",
        Serial = null,
      });
    }

    [Fact]
    public void QueueTablesCorrectlyCreated()
    {
      var now = new DateTime(2018, 7, 12, 5, 6, 7, DateTimeKind.Utc);
      var matId = _log.AllocateMaterialID("uuu5", "part5", 1);
      var mat = new LogMaterial(matId, "uuu5", 1, "part5", 1, "", "", "");

      _log.RecordAddMaterialToQueue(EventLogMaterial.FromLogMat(mat), "queue", 5, null, null, now.AddHours(2));

      _log.GetMaterialInAllQueues().Should().BeEquivalentTo(new[] {
        new QueuedMaterial() {
          MaterialID = matId,
          Queue = "queue",
          Position = 0,
          Unique = "uuu5",
          PartNameOrCasting = "part5",
          NumProcesses = 1,
          AddTimeUTC = now.AddHours(2)
        }
      });
    }

    [Fact]
    public void ConvertsJobFromV16()
    {
      var expected = CreateJob();
      var actual = _log.LoadJob("Unique1")?.ToLegacyJob();

      JobEqualityChecks.CheckJobEqual(expected, actual, true);

      _log.LoadDecrementsForJob("Unique1").Should().BeEquivalentTo(new[] {new DecrementQuantity()
      {
        DecrementId = 12,
        TimeUTC = new DateTime(2020, 10, 22, 4, 5, 6, DateTimeKind.Utc),
        Quantity = 123
      }});
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

      _log.AddJobs(new NewJobs()
      {
        ScheduleId = newJob.ScheduleId,
        Jobs = ImmutableList.Create((Job)newJob.ToHistoricJob())
      }, null, addAsCopiedToSystem: true);

      JobEqualityChecks.CheckJobEqual(newJob, _log.LoadJob("mynewunique")?.ToLegacyJob(), true);

      var now = DateTime.UtcNow;
      _log.AddNewDecrement(new[] {
  new NewDecrementQuantity() {
    JobUnique = "mynewunique",
    Part = "thepart",
    Quantity = 88
  }
      }, now);

      _log.LoadDecrementsForJob("mynewunique").Should().BeEquivalentTo(new[] {
  new DecrementQuantity() {
    DecrementId = 13, // existing old job had decrement id 12
          TimeUTC = now,
    Quantity = 88
  }
      });
    }

    private JobPlan CreateJob()
    {
      var job1 = new JobPlan("Unique1", 2, new int[] { 2, 3 });
      job1.PartName = "Job1";
      job1.SetPlannedCyclesOnFirstProcess(178);
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
		public void CreateV17()
		{
		  var now = new DateTime(2018, 7, 12, 5, 6, 7, DateTimeKind.Utc);

		  var m1 = _log.AllocateMaterialID("uuu1");
		  var m2 = _log.AllocateMaterialID("uuu1");
		  var m3 = _log.AllocateMaterialID("uuu2");

		  var mat1_1 = new LogMaterial(m1, "uuu1", 1, "part1", 2, face: "A");
		  var mat1_2 = new LogMaterial(m1, "uuu1", 2, "part1", 2, face: "B");
		  var mat2_1 = new LogMaterial(m2, "uuu1", 1, "part1", 2, face: "C");
		  var mat2_2 = new LogMaterial(m2, "uuu1", 1, "part1", 2, face: "D");
		  var mat3 = new LogMaterial(m3, "uuu2", 1, "part2", 1, face: "E");

		  var log1 = new LogEntry(
		    cntr: -1,
		    mat: new [] {mat1_1, mat2_1},
		    pal: "3",
		    ty: LogType.MachineCycle,
		    locName: "MC",
		    locNum: 1,
		    prog: "proggg",
		    start: false,
		    endTime: now,
		    result: "result",
		    endOfRoute: false
		  );
		  _log.AddLogEntry(log1);

		  var log2 = new LogEntry(
		    cntr: -1,
		    mat: new [] {mat1_2, mat2_2},
		    pal: "5",
		    ty: LogType.MachineCycle,
		    locName: "MC",
		    locNum: 1,
		    prog: "proggg2",
		    start: false,
		    endTime: now.AddMinutes(10),
		    result: "result2",
		    endOfRoute: false
		  );
		  _log.AddLogEntry(log2);

		  _log.RecordSerialForMaterialID(mat1_1, "serial1", now.AddMinutes(20));
		  _log.RecordWorkorderForMaterialID(mat1_1, "work1", now.AddMinutes(30));
		  _log.RecordSerialForMaterialID(mat2_2, "serial2", now.AddMinutes(40));

		  var log3 = new LogEntry(
		    cntr: -1,
		    mat: new [] {mat3},
		    pal: "1",
		    ty: LogType.LoadUnloadCycle,
		    locName: "L/U",
		    locNum: 5,
		    prog: "LOAD",
		    start: false,
		    endTime: now.AddMinutes(50),
		    result: "LOAD",
		    endOfRoute: false
		  );
		  _log.AddLogEntry(log3);

		  _log.RecordWorkorderForMaterialID(mat3, "work3", now.AddMinutes(60));
		}*/

    /*
		public void CreateV16()
		{
		  var job1 = CreateJob();
		  _jobs.AddJobs(new NewJobs() { Jobs = new List<JobPlan> { job1 } }, null);
		}
		*/

  }

  public static class LegacyToNewJobConvert
  {

    private static HoldPattern ToInsightHold(JobHoldPattern h)
    {
      if (h == null) return null;

      return new HoldPattern()
      {
        UserHold = h.UserHold,
        ReasonForUserHold = h.ReasonForUserHold,
        HoldUnholdPatternStartUTC = h.HoldUnholdPatternStartUTC,
        HoldUnholdPattern = h.HoldUnholdPattern.ToImmutableList(),
        HoldUnholdPatternRepeats = h.HoldUnholdPatternRepeats
      };
    }

    public static HistoricJob ToHistoricJob(this JobPlan job)
    {
      return new HistoricJob()
      {
        Comment = job.Comment,
        HoldJob = ToInsightHold(job.HoldEntireJob),
        // ignoring obsolete job-level inspections
        // ignoring Priority, CreateMarkingData
        ManuallyCreated = job.ManuallyCreatedJob,
        BookingIds = job.ScheduledBookingIds.ToImmutableList(),
        ScheduleId = job.ScheduleId,
        UniqueStr = job.UniqueStr,
        PartName = job.PartName,
        CopiedToSystem = job.JobCopiedToSystem,
        Archived = job.Archived,
        RouteStartUTC = job.RouteStartingTimeUTC,
        RouteEndUTC = job.RouteEndingTimeUTC,
        Cycles = job.GetPlannedCyclesOnFirstProcess(),
        Processes = Enumerable.Range(1, job.NumProcesses).Select(proc =>
          new ProcessInfo()
          {
            Paths = Enumerable.Range(1, job.GetNumPaths(process: proc)).Select(path =>
        new ProcPathInfo()
        {
          ExpectedUnloadTime = job.GetExpectedUnloadTime(proc, path),
          OutputQueue = job.GetOutputQueue(proc, path),
          InputQueue = job.GetInputQueue(proc, path),
          PartsPerPallet = job.PartsPerPallet(proc, path),
          HoldLoadUnload = ToInsightHold(job.HoldLoadUnload(proc, path)),
          HoldMachining = ToInsightHold(job.HoldMachining(proc, path)),
          SimulatedAverageFlowTime = job.GetSimulatedAverageFlowTime(proc, path),
          SimulatedStartingUTC = job.GetSimulatedStartingTimeUTC(proc, path),
          SimulatedProduction = job.GetSimulatedProduction(proc, path).Select(s => new SimulatedProduction()
          {
            TimeUTC = s.TimeUTC,
            Quantity = s.Quantity
          }).ToImmutableList(),
          Stops = job.GetMachiningStop(proc, path).Select(stop => new MachiningStop()
          {
            Stations = stop.Stations.ToImmutableList(),
            Program = stop.ProgramName,
            ProgramRevision = stop.ProgramRevision,
            Tools = stop.Tools.ToImmutableDictionary(k => k.Key, k => k.Value),
            StationGroup = stop.StationGroup,
            ExpectedCycleTime = stop.ExpectedCycleTime
          }).ToImmutableList(),
          Casting = proc == 1 ? job.GetCasting(path) : null,
          Unload = job.UnloadStations(proc, path).ToImmutableList(),
          ExpectedLoadTime = job.GetExpectedLoadTime(proc, path),
          Load = job.LoadStations(proc, path).ToImmutableList(),
          Face = job.PlannedFixture(proc, path).face,
          Fixture = job.PlannedFixture(proc, path).fixture,
          Pallets = job.PlannedPallets(proc, path).ToImmutableList(),
#pragma warning disable CS0612 // obsolete PathGroup
          PathGroup = job.GetPathGroup(proc, path),
#pragma warning restore CS0612
          Inspections =
      job.PathInspections(proc, path).Select(i => new PathInspection()
      {
        InspectionType = i.InspectionType,
        Counter = i.Counter,
        MaxVal = i.MaxVal,
        RandomFreq = i.RandomFreq,
        TimeInterval = i.TimeInterval,
        ExpectedInspectionTime = i.ExpectedInspectionTime
      }
      ).ToImmutableList()
        }
            ).ToImmutableList()
          }
        ).ToImmutableList()
      };
    }

  }

}