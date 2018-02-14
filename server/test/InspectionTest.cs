/* Copyright (c) 2017, John Lenz

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
using Xunit;
using Microsoft.Data.Sqlite;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace MachineWatchTest {
  public class InspectionTest : IDisposable {

		private JobLogDB _jobLog;
		private InspectionDB _insp;

    public InspectionTest()
		{
      var logConn = SqliteExtensions.ConnectMemory();
      logConn.Open();
      _jobLog = new JobLogDB(logConn);
      _jobLog.CreateTables();

      var inspConn = SqliteExtensions.ConnectMemory();
      inspConn.Open();
			_insp = new InspectionDB(_jobLog, inspConn);
      _insp.CreateTables();
		}

		public void Dispose()
		{
			_jobLog.Close();
			_insp.Close();
		}

		[Fact]
		public void Counts()
		{
			List<InspectCount> cnts = new List<InspectCount>();

			InspectCount cnt = new InspectCount();

			cnt.Counter = "Test1";
			cnt.Value = 15;
			cnt.LastUTC = DateTime.Parse("1/5/2009 4:23:12 GMT");
			cnts.Add(cnt);

			cnt = new InspectCount();

			cnt.Counter = "Test2";
			cnt.Value = 1563;
			cnt.LastUTC = DateTime.Parse("1/15/2009 3:35:24 GMT");
			cnts.Add(cnt);

			cnt = new InspectCount();

			cnt.Counter = "Test3";
			cnt.Value = 532;
			cnt.LastUTC = DateTime.Parse("2/12/2009 15:03:55 GMT");
			cnts.Add(cnt);

			_insp.SetInspectCounts(cnts);

			IList<InspectCount> loaded = _insp.LoadInspectCounts();

			Assert.Equal(loaded, cnts);
		}

		[Fact]
		public void Frequencies()
		{
			var job = new JobPlan("job1", 1);
			job.PartName = "part1";
			var freqProg = new JobInspectionData("insp1", "counter1", 0.5, TimeSpan.FromHours (100));
			job.AddInspection(freqProg);

			for (int i = 0; i < 100; i++)
				_insp.MakeInspectionDecision (i, job, freqProg);

			int numInsp = 0;
			for (int i = 0; i < 100; i++) {
				if (FindDecision(i, "insp1", "counter1"))
					numInsp += 1;
			}

			Assert.True(numInsp > 0);
			Assert.True(numInsp < 100);
		}

		[Fact]
		public void Inspections()
		{
			//set the count as zero, otherwise it chooses a random
      InspectCount cnt = new InspectCount();
			cnt.Counter = "counter1";
			cnt.Value = 0;
			cnt.LastUTC = DateTime.UtcNow.AddHours(-11).AddMinutes(2);
			_insp.SetInspectCounts(new InspectCount[] {cnt});

			var job = new JobPlan("job1", 1);
			job.PartName = "part1";
			var job2 = new JobPlan("job2", 1);
			job2.PartName = "part2";

			//set up a program
			var inspProg = new JobInspectionData("insp1", "counter1", 3, TimeSpan.FromHours(11));
			job.AddInspection(inspProg);
			var inspProg2 = new JobInspectionData(inspProg);
			job2.AddInspection(inspProg2);

			//the lastutc should be 2 minutes too short, so only inspections from the counter should take place

			Assert.False(_insp.MakeInspectionDecision(1, job, inspProg));
			Assert.False(_insp.MakeInspectionDecision(1, job, inspProg));
			CheckCount("counter1", 1);
			CheckLastUTC("counter1", cnt.LastUTC);

			Assert.False(_insp.MakeInspectionDecision(2, job2, inspProg2));
			CheckCount("counter1", 2);
			CheckLastUTC("counter1", cnt.LastUTC);

			Assert.True(_insp.MakeInspectionDecision(3, job, inspProg));

			CheckDecision(1, "insp1", "counter1", false);
			CheckDecision(2, "insp1", "counter1", false);
			CheckDecision(3, "insp1", "counter1", true);

			CheckCount("counter1", 0);
			CheckLastUTC("counter1", DateTime.UtcNow);

			//now check lastutc. set lastutc to be 2 minutes
			cnt = new InspectCount();
			cnt.Counter = "counter1";
			cnt.Value = 0;
			cnt.LastUTC = DateTime.UtcNow.AddHours(-11).AddMinutes(-2);
			_insp.SetInspectCounts(new InspectCount[] {cnt});

			job.PartName = "part5";

			Assert.True(_insp.MakeInspectionDecision(4, job, inspProg));

			CheckDecision(4, "insp1", "counter1", true);
			CheckLastUTC("counter1", DateTime.UtcNow);
		}

		[Fact]
		public void ForcedInspection()
		{
			var job = new JobPlan("job1", 1);
			job.PartName = "part1";
			var job2 = new JobPlan("job2", 1);
			job2.PartName = "part2";

			//set up a program
			var inspProg = new JobInspectionData("insp1", "counter1", 13, TimeSpan.FromHours(11));
			job.AddInspection(inspProg);
			var inspProg2 = new JobInspectionData(inspProg);
			job2.AddInspection(inspProg2);

			//set the count as zero, otherwise it chooses a random
			InspectCount cnt = new InspectCount();
			cnt.Counter = "counter1";
			cnt.Value = 0;
			cnt.LastUTC = DateTime.UtcNow.AddHours(-10);
			_insp.SetInspectCounts(new InspectCount[] {cnt});

			//try making a decision
			_insp.ForceInspection(2, "insp1");

			Assert.False(_insp.MakeInspectionDecision(1, job, inspProg));
			Assert.False(_insp.MakeInspectionDecision(1, job, inspProg));
			CheckCount("counter1", 1);

			Assert.True(_insp.MakeInspectionDecision(2, job2, inspProg2));

			CheckDecision(1, "insp1", "counter1", false);
			CheckDecision(2, "insp1", "counter1", true);

			CheckCount("counter1", 2);
		}

		[Fact]
		public void NextPiece()
		{
			var job = new JobPlan("job1", 1);
			job.PartName = "part1";

			//set up a program
			var inspProg = new JobInspectionData("insp1", "counter1", 3, TimeSpan.FromHours(11));
			job.AddInspection(inspProg);

			//set the count as zero, otherwise it chooses a random
			InspectCount cnt = new InspectCount();
			cnt.Counter = "counter1";
			cnt.Value = 0;
			cnt.LastUTC = DateTime.UtcNow.AddHours(-10);
			_insp.SetInspectCounts(new InspectCount[] {cnt});

			PalletLocation palLoc = new PalletLocation(PalletLocationEnum.Machine, "MC", 1);

			_insp.NextPieceInspection(palLoc, "insp1");
			_insp.CheckMaterialForNextPeiceInspection(palLoc, 1);

			CheckCount("counter1", 0);

			Assert.True(_insp.MakeInspectionDecision(1, job, inspProg));
			CheckCount("counter1", 1);

			CheckDecision(1, "insp1", "counter1", true);
		}

		[Fact]
		public void TranslateCounter()
		{
			var counter = "counter1-" +
				JobInspectionData.LoadFormatFlag(1) + "-" +
				JobInspectionData.UnloadFormatFlag(1) + "-" +
				JobInspectionData.LoadFormatFlag(2) + "-" +
				JobInspectionData.UnloadFormatFlag(2) + "-" +
				JobInspectionData.PalletFormatFlag(1) + "-" +
				JobInspectionData.PalletFormatFlag(2) + "-" +
				JobInspectionData.StationFormatFlag(1, 1) + "-" +
				JobInspectionData.StationFormatFlag(1, 2) + "-" +
				JobInspectionData.StationFormatFlag(2, 1) + "-" +
				JobInspectionData.StationFormatFlag(2, 2);

			var expandedCounter1 = "counter1-1-2-3-4-P1-P2-10-11-12-13";
			var expandedCounter2 = "counter1-6-8-7-9-P5-P4-15-16-18-19";

			//set the count as zero, otherwise it chooses a random
			var cnt = new InspectCount();
			cnt.Counter = expandedCounter1;
			cnt.Value = 0;
			cnt.LastUTC = DateTime.UtcNow.AddHours(-10);
			var cnt2 = new InspectCount();
			cnt2.Counter = expandedCounter2;
			cnt2.Value = 0;
			cnt2.LastUTC = DateTime.UtcNow.AddHours(-10);
			_insp.SetInspectCounts(new InspectCount[] {cnt, cnt2});


			var mat1Proc1 = new LogMaterial[] {new LogMaterial(1, "job1", 1, "part1", 2)};
			var mat1Proc2 = new LogMaterial[] {new LogMaterial(1, "job1", 2, "part1", 2)};
			var mat2Proc1 = new LogMaterial[] {new LogMaterial(2, "job1", 1, "part1", 2)};
			var mat2Proc2 = new LogMaterial[] {new LogMaterial(2, "job1", 2, "part1", 2)};

			_lastCycleTime = DateTime.UtcNow.AddDays(-1);

			AddCycle(mat1Proc1, "P1", LogType.LoadUnloadCycle, 1, false);
			AddCycle(mat2Proc1, "P5", LogType.LoadUnloadCycle, 6, false);
			AddCycle(mat1Proc1, "P1", LogType.MachineCycle, 10, false);
			AddCycle(mat2Proc1, "P5", LogType.MachineCycle, 15, false);
			AddCycle(mat1Proc1, "P1", LogType.MachineCycle, 11, false);
			AddCycle(mat2Proc1, "P5", LogType.MachineCycle, 16, false);
			AddCycle(mat1Proc1, "P1", LogType.LoadUnloadCycle, 2, false);
			AddCycle(mat2Proc1, "P5", LogType.LoadUnloadCycle, 8, false);

			AddCycle(mat1Proc2, "P2", LogType.LoadUnloadCycle, 3, false);
			AddCycle(mat2Proc2, "P4", LogType.LoadUnloadCycle, 7, false);
			AddCycle(mat1Proc2, "P2", LogType.MachineCycle, 12, false);
			AddCycle(mat2Proc2, "P4", LogType.MachineCycle, 18, false);
			AddCycle(mat1Proc2, "P2", LogType.MachineCycle, 13, false);
			AddCycle(mat2Proc2, "P4", LogType.MachineCycle, 19, false);
			AddCycle(mat1Proc2, "P2", LogType.LoadUnloadCycle, 4, true);
			AddCycle(mat2Proc2, "P4", LogType.LoadUnloadCycle, 9, true);

			var job = new JobPlan("job1", 2);
			job.PartName = "part1";

			for (int proc = 1; proc <= 2; proc++) {
				for (int stat = 1; stat <= 10; stat++) {
					job.AddLoadStation(proc, 1, stat);
					job.AddUnloadStation(proc, 1, stat);
				}
			}

			var stop = new JobMachiningStop("Machine");
			for (int stat = 1; stat <= 20; stat++)
				stop.AddProgram(stat, "prog");
			job.AddMachiningStop(1, 1, stop);
			stop = new JobMachiningStop("Machine");
			for (int stat = 1; stat <= 20; stat++)
				stop.AddProgram(stat, "prog");
			job.AddMachiningStop(1, 1, stop);

			stop = new JobMachiningStop("Machine");
			for (int stat = 1; stat <= 20; stat++)
				stop.AddProgram(stat, "prog");
			job.AddMachiningStop(2, 1, stop);
			stop = new JobMachiningStop("Machine");
			for (int stat = 1; stat <= 20; stat++)
				stop.AddProgram(stat, "prog");
			job.AddMachiningStop(2, 1, stop);

			var inspProg = new JobInspectionData("insp1", counter, 10, TimeSpan.FromDays(2));
			job.AddInspection(inspProg);

			Assert.False(_insp.MakeInspectionDecision(1, job, inspProg));
			Assert.Equal(2, _insp.LoadInspectCounts().Count);
			CheckCount(expandedCounter1, 1);
			CheckCount(expandedCounter2, 0);

			Assert.False(_insp.MakeInspectionDecision(2, job, inspProg));
			Assert.Equal(2, _insp.LoadInspectCounts().Count);
			CheckCount(expandedCounter1, 1);
			CheckCount(expandedCounter2, 1);
		}

		private DateTime _lastCycleTime;
		private void AddCycle(LogMaterial[] mat, string pal, LogType loc, int statNum, bool end)
		{
			string name = loc == LogType.MachineCycle ? "MC" : "Load";
      		_jobLog.AddLogEntry(new LogEntry(-1, mat, pal, loc, name, statNum, "", true, _lastCycleTime, "", end));
			_lastCycleTime = _lastCycleTime.AddMinutes(15);
			_jobLog.AddLogEntry(new LogEntry(-1, mat, pal, loc, name, statNum, "", false, _lastCycleTime, "", end));
			_lastCycleTime = _lastCycleTime.AddMinutes(15);
		}

		private void CheckDecision(long matID, string iType, string counter, bool inspect)
		{
			foreach (var d in _insp.LookupInspectionDecisions(matID)) {
				if (d.Counter == counter && d.InspType == iType) {
					Assert.Equal(inspect, d.Inspect);
					return;
				}
			}
			Assert.True(false, "Unable to find counter and inspection type");
		}

		private bool FindDecision(long matID, string iType, string counter)
		{
			foreach (var d in _insp.LookupInspectionDecisions(matID)) {
				if (d.Counter == counter && d.InspType == iType) {
					return d.Inspect;
				}
			}
			Assert.True(false, "Unable to find counter and inspection type");
			return false;
		}

		private void CheckCount(string counter, int val)
		{
			foreach (var c in _insp.LoadInspectCounts()) {
				if (c.Counter == counter) {
					Assert.Equal(val, c.Value);
					return;
				}
			}
			Assert.True(false, "Unable to find counter " + counter);
		}

		private void CheckLastUTC(string counter, DateTime val)
		{
			foreach (var c in _insp.LoadInspectCounts()) {
				if (c.Counter == counter) {
					if (val == DateTime.MaxValue)
						Assert.Equal(DateTime.MaxValue, c.LastUTC);
					else
						Assert.True(5 >= Math.Abs(val.Subtract(c.LastUTC).TotalMinutes));
					return;
				}
			}
			Assert.True(false, "Unable to find counter");
		}
	}
}
