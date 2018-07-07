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
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using MazakMachineInterface;

namespace MachineWatchTest
{

  public class LogTestBase : IDisposable
  {
    protected JobLogDB jobLog;
		protected JobDB jobDB;
    protected LogTranslation log;
    protected List<TestLogEntry> expected = new List<TestLogEntry>();
		private List<TestPartData> partData;
		private TestFindPart findPart;

    protected LogTestBase()
    {
      var logConn = BlackMaple.MachineFramework.SqliteExtensions.ConnectMemory();
      logConn.Open();
      jobLog = new JobLogDB(logConn);
      jobLog.CreateTables();

      var jobConn = BlackMaple.MachineFramework.SqliteExtensions.ConnectMemory();
      jobConn.Open();
      jobDB = new JobDB(jobConn);
      jobDB.CreateTables();

			partData = new List<TestPartData>();
			findPart = new TestFindPart(partData);

			var settings = new FMSSettings() {
				SerialType = SerialType.AssignOneSerialPerMaterial
			};

      log = new LogTranslation(jobLog, jobDB, null, settings, null);
      log.Halt(); // stop the timer, we will inject events directly
    }

		public void Dispose()
		{
			jobLog.Close();
			jobDB.Close();
		}

    #region Find Part
		private class TestPartData {
			public int Pallet {get;set;}
			public string MazakPartName {get;set;}
			public int Proc {get;set;}

			public string Unique {get;set;}
			public int Path {get;set;}
			public int NumProc {get;set;}
		}

    private class TestFindPart : LogTranslation.IFindPart
    {
			private IEnumerable<TestPartData> testPartData;
			public TestFindPart(IEnumerable<TestPartData> td) { testPartData = td; }
      public void FindPart(int pallet, string mazakPartName, int proc, out string unique, out int path, out int numProc)
      {
				var data = testPartData
					.Where(p => p.Pallet == pallet && p.MazakPartName == mazakPartName && p.Proc == proc)
					.FirstOrDefault();
				if (data != null) {
						unique = data.Unique;
						path = data.Path;
						numProc = data.NumProc;
				} else {
					throw new Exception("Unable to find part for " + pallet.ToString() + " " + mazakPartName + " " + proc.ToString());
				}
      }
    }

		protected void AddTestPart(int pallet, string unique, string part, int proc, int numProc, int path) {
			partData.Add(new TestPartData() {
				Pallet = pallet,
				MazakPartName = part + ":4:1",
				Proc = proc,
				Unique = unique,
				Path = path,
				NumProc = numProc
			});
		}
    #endregion

    #region Creating Log Entries and Read Data
    protected class TestLogEntry : LogEntry
    {
      public long MaterialID;
      public TimeSpan Elapsed;
      public string Unique;
      public int NumProcess;

      public TestLogEntry Clone()
      {
        return (TestLogEntry)MemberwiseClone();
      }
    }

    protected TestLogEntry BuildPart(DateTime t, int pal, string unique, string part, int proc, int numProc, int fix, string program, long matID)
    {
      var e = new TestLogEntry();
      e.TimeUTC = t;
      e.ForeignID = "";

      e.Pallet = pal;
      e.FullPartName = part + ":4:1";
      e.JobPartName = part;
      e.Process = proc;
      e.FixedQuantity = fix;
      e.Program = program;

      e.TargetPosition = "";
      e.FromPosition = "";

      e.MaterialID = matID;
      e.Elapsed = TimeSpan.Zero;
      e.NumProcess = numProc;
      e.Unique = unique;

      return e;
    }

    protected void MachStart(TestLogEntry le, int offset, int mach)
    {
      var e2 = (TestLogEntry)le.Clone();
      e2.TimeUTC = e2.TimeUTC.AddMinutes(offset);
      e2.Code = LogCode.MachineCycleStart;
      e2.StationNumber = mach;

      log.HandleEvent(e2, findPart, e => {});
      expected.Add(e2);
    }

    protected void MachEnd(TestLogEntry le, int offset, int mach, int elapMin)
    {
      var e2 = le.Clone();
      e2.TimeUTC = e2.TimeUTC.AddMinutes(offset);
      e2.Code = LogCode.MachineCycleEnd;
      e2.StationNumber = mach;
      e2.Elapsed = TimeSpan.FromMinutes(elapMin);

			bool raisedEvt = false;
      log.HandleEvent(e2, findPart, logE => {
				logE.ShouldBeEquivalentTo(new BlackMaple.MachineWatchInterface.LogEntry(
					cntr: logE.Counter,
					mat: new [] {new BlackMaple.MachineWatchInterface.LogMaterial(
						matID: e2.MaterialID,
						uniq: le.Unique,
						proc: le.Process,
						part: le.JobPartName,
						numProc: le.NumProcess,
						face: le.Process.ToString()
					)},
					pal: le.Pallet.ToString(),
					ty: BlackMaple.MachineWatchInterface.LogType.MachineCycle,
					locName: "MC",
					locNum: e2.StationNumber,
					prog: le.Program,
					start: false,
					endTime: e2.TimeUTC,
					result: "",
					endOfRoute: false,
					elapsed: e2.Elapsed,
					active: TimeSpan.FromMinutes(-1)
				));

				raisedEvt = true;
			});
			raisedEvt.Should().BeTrue();
      expected.Add(e2);
    }

    protected void LoadStart(TestLogEntry le, int offset, int load)
    {Assert.Equal(0, expected.Count);
      var e2 = le.Clone();
      e2.MaterialID = -1;
      e2.FixedQuantity = 1;
      e2.TimeUTC = e2.TimeUTC.AddMinutes(offset);
      e2.Code = LogCode.LoadBegin;
      e2.StationNumber = load;

      log.HandleEvent(e2, findPart, e => {});

			e2.Program = "LOAD";

			// since no material id is used, the part name and unique are not stored
			e2.Unique = "";
			e2.JobPartName = "";
			e2.NumProcess = -1;

      expected.Add(e2);
    }

    protected void LoadEnd(TestLogEntry le, int offset, int cycleOffset, int load, int elapMin)
    {
      var e2 = le.Clone();
      e2.TimeUTC = e2.TimeUTC.AddMinutes(offset);
      e2.Code = LogCode.LoadEnd;
      e2.StationNumber = load;
      e2.Elapsed = TimeSpan.FromMinutes(elapMin);

      log.HandleEvent(e2, findPart, e => {});

      e2.TimeUTC = le.TimeUTC.AddMinutes(cycleOffset).AddSeconds(1);
      e2.Program = "LOAD";
      expected.Add(e2);

      //This is for one serial per material
      //for (int i = 0; i < e2.FixedQuantity; i += 1) {
      //	var mark = e2.Clone ();
      //	mark.TimeUTC = e2.TimeUTC.AddSeconds(1);
      //	mark.Program = "MARK";
      //	mark.MaterialID = e2.MaterialID + i;
      //	mark.FixedQuantity = 1;
      //	expected.Add(mark);
      //}

      var mark = e2.Clone();
      mark.TimeUTC = e2.TimeUTC.AddSeconds(1);
      mark.Program = "MARK";
      expected.Add(mark);
    }

    protected void UnloadStart(TestLogEntry le, int offset, int load)
    {
      var e2 = le.Clone();
      e2.TimeUTC = e2.TimeUTC.AddMinutes(offset);
      e2.Code = LogCode.UnloadBegin;
      e2.StationNumber = load;

      log.HandleEvent(e2, findPart, e => {});

			e2.Program = "UNLOAD";
      expected.Add(e2);
    }

    protected void UnloadEnd(TestLogEntry le, int offset, int load, int elapMin)
    {
      var e2 = le.Clone();
      e2.TimeUTC = e2.TimeUTC.AddMinutes(offset);
      e2.Code = LogCode.UnloadEnd;
      e2.StationNumber = load;
      e2.Elapsed = TimeSpan.FromMinutes(elapMin);

      log.HandleEvent(e2, findPart, e => {});
			e2.Program = "UNLOAD";
      expected.Add(e2);
    }

    protected void MovePallet(DateTime t, int offset, int pal, int load, bool addExpected = true)
    {
      var e = new TestLogEntry();
      e.Code = LogCode.PalletMoving;
      e.TimeUTC = t.AddMinutes(offset);
      e.ForeignID = "";

      e.Pallet = pal;
      e.FullPartName = "";
      e.JobPartName = "";
      e.Process = 1;
      e.FixedQuantity = -1;
      e.Program = "";

      e.TargetPosition = "S011"; //stacker
      e.FromPosition = "LS01" + load.ToString();

      e.MaterialID = -1;
      e.Elapsed = TimeSpan.Zero;
      e.NumProcess = 1;
      e.Unique = "";

      log.HandleEvent(e, findPart, ev => {});
      if (addExpected)
        expected.Add(e);
    }

    #endregion

    #region Checking Log
    private void CheckValidExpected()
    {
      for (int i = 0; i < expected.Count; i += 1)
      {
        var e = expected[i];
        if (e.Code == LogCode.LoadEnd || e.Code == LogCode.MachineCycleEnd
            || e.Code == LogCode.UnloadEnd || e.Code == LogCode.PalletMoving)
          continue;
        LogCode target = LogCode.MachineCycleEnd;  // To get rid of the unint warning
        if (e.Code == LogCode.LoadBegin)
          target = LogCode.LoadEnd;
        if (e.Code == LogCode.MachineCycleStart)
          target = LogCode.MachineCycleEnd;
        if (e.Code == LogCode.UnloadBegin)
          target = LogCode.UnloadEnd;

        for (int j = i + 1; j < expected.Count; j += 1)
        {
          var e2 = expected[j];

          if (e2.Code == target && e2.StationNumber == e.StationNumber)
          {
            if (e2.Code == LogCode.LoadEnd || e2.MaterialID == e.MaterialID)
              goto found;
          }
        }

        Assert.True(false, "Did not find ending event for " + e.Code.ToString() + " - " + e.TimeUTC.ToString());

      found:;
      }
    }

    protected void CheckExpected(DateTime start, DateTime end, bool checkValid = true)
    {
      if (checkValid)
        CheckValidExpected();

      var log = jobLog.GetLogEntries(start, end);

      foreach (var stat in log)
      {
        foreach (var e in expected)
        {
					if (e.Program == "MARK" && stat.EndTimeUTC == e.TimeUTC)
					{
						var serial = JobLogDB.ConvertToBase62(e.MaterialID).PadLeft(10, '0');
						Assert.Equal(BlackMaple.MachineWatchInterface.LogType.PartMark, stat.LogType);
						Assert.Equal(false, stat.StartOfCycle);
						Assert.Equal(serial, stat.Result);
						Assert.Equal(false, stat.EndOfRoute);
						CheckMaterial(e, stat, false);
						expected.Remove(e);
						goto foundExpected;
					}

          if (stat.Pallet == e.Pallet.ToString() && stat.EndTimeUTC == e.TimeUTC)
          {
            switch (e.Code)
            {

              case LogCode.LoadBegin:
                Assert.Equal(BlackMaple.MachineWatchInterface.LogType.LoadUnloadCycle, stat.LogType);
                Assert.Equal(stat.LocationNum, e.StationNumber);
                Assert.Equal(true, stat.StartOfCycle);
                Assert.Equal("LOAD", stat.Result);
                Assert.Equal(false, stat.EndOfRoute);
                CheckMaterial(e, stat);
                break;

              case LogCode.LoadEnd:
                Assert.Equal(BlackMaple.MachineWatchInterface.LogType.LoadUnloadCycle, stat.LogType);
                Assert.Equal(stat.LocationNum, e.StationNumber);
                Assert.Equal(false, stat.StartOfCycle);
                Assert.Equal("LOAD", stat.Result);
                Assert.Equal(false, stat.EndOfRoute);
                Assert.Equal(e.Elapsed, stat.ElapsedTime);
                CheckMaterial(e, stat);
                break;

              case LogCode.MachineCycleStart:
                Assert.Equal(BlackMaple.MachineWatchInterface.LogType.MachineCycle, stat.LogType);
                Assert.Equal(stat.LocationNum, e.StationNumber);
                Assert.Equal(true, stat.StartOfCycle);
                Assert.Equal("", stat.Result);
                Assert.Equal(false, stat.EndOfRoute);
                CheckMaterial(e, stat);
                break;

              case LogCode.MachineCycleEnd:
                Assert.Equal(BlackMaple.MachineWatchInterface.LogType.MachineCycle, stat.LogType);
                Assert.Equal(stat.LocationNum, e.StationNumber);
                Assert.Equal(false, stat.StartOfCycle);
                Assert.Equal("", stat.Result);
                Assert.Equal(false, stat.EndOfRoute);
                Assert.Equal(e.Elapsed, stat.ElapsedTime);
                CheckMaterial(e, stat);
                break;

              case LogCode.UnloadBegin:
                Assert.Equal(BlackMaple.MachineWatchInterface.LogType.LoadUnloadCycle, stat.LogType);
                Assert.Equal(stat.LocationNum, e.StationNumber);
                Assert.Equal(true, stat.StartOfCycle);
                Assert.Equal("UNLOAD", stat.Result);
                Assert.Equal(false, stat.EndOfRoute);
                CheckMaterial(e, stat);
                break;

              case LogCode.UnloadEnd:
                Assert.Equal(BlackMaple.MachineWatchInterface.LogType.LoadUnloadCycle, stat.LogType);
                Assert.Equal(stat.LocationNum, e.StationNumber);
                Assert.Equal(false, stat.StartOfCycle);
                Assert.Equal("UNLOAD", stat.Result);
                Assert.Equal(true, stat.EndOfRoute);
                Assert.Equal(e.Elapsed, stat.ElapsedTime);
                CheckMaterial(e, stat);
                break;

              case LogCode.PalletMoving:
                Assert.Equal(BlackMaple.MachineWatchInterface.LogType.PalletCycle, stat.LogType);
                Assert.Equal(false, stat.StartOfCycle);
                Assert.Equal(false, stat.EndOfRoute);
                Assert.Equal("PalletCycle", stat.Result);
                break;

            }

            expected.Remove(e);
            goto foundExpected;
          }
        }

        Assert.True(false, "Unexpected station cycle: " + stat.Pallet + " " + stat.EndTimeUTC.ToString());

      foundExpected:;
      }

      Assert.Equal(0, expected.Count);
    }

    private void CheckMaterial(TestLogEntry e, BlackMaple.MachineWatchInterface.LogEntry stat, bool checkFace = true)
    {
      Assert.Equal(e.Program, stat.Program);
      Assert.Equal(e.FixedQuantity, stat.Material.Count());

      var matIDs = new Dictionary<long, bool>();
      var faces = new Dictionary<string, bool>();
      for (long mat = e.MaterialID; mat < e.MaterialID + e.FixedQuantity; mat++)
      {
        matIDs.Add(mat, false);
        if (e.FixedQuantity > 1)
          faces.Add(e.Process.ToString() + "-" + (mat - e.MaterialID + 1).ToString(), false);
        else
          faces.Add(e.Process.ToString(), false);
      }

      foreach (var mat in stat.Material)
      {
        Assert.Equal(e.Unique, mat.JobUniqueStr);
        Assert.Equal(e.JobPartName, mat.PartName);
        Assert.Equal(e.Process, mat.Process);
        Assert.Equal(e.NumProcess, mat.NumProcesses);

        if (checkFace && mat.Face != "")
        { // LoadBegin have empty faces
          Assert.True(faces.ContainsKey(mat.Face), "Face " + mat.Face);
          Assert.False(faces[mat.Face], "Face " + mat.Face);
          faces[mat.Face] = true;
        }
        else
        {
          faces.Clear();
        }

        Assert.True(matIDs.ContainsKey(mat.MaterialID), "Mat " + mat.MaterialID.ToString());
        Assert.False(matIDs[mat.MaterialID], "Mat " + mat.MaterialID.ToString());
        matIDs[mat.MaterialID] = true;
      }

      foreach (var i in matIDs)
        Assert.True(i.Value, "Missing material id " + i.Key.ToString());
      foreach (var i in faces)
        Assert.True(i.Value, "Missing face " + i.Key.ToString());
    }
    #endregion
  }

  public class LogTranslationTests : LogTestBase
  {
    [Fact]
    public void SingleMachineCycle()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      long mat = jobLog.AllocateMaterialID("", "", 1) + 1;

			AddTestPart(pallet: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, path: 1);

      var p = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, fix: 1, program: "prog", matID: mat);

      LoadStart(p, offset: 0, load: 5);
      LoadEnd(p, offset: 2, load: 5, cycleOffset: 3, elapMin: 2);
      MovePallet(t, offset: 3, load: 1, pal: 3);

      MachStart(p, offset: 4, mach: 2);
      MachEnd(p, offset: 20, mach: 2, elapMin: 16);

      UnloadStart(p, offset: 22, load: 1);
      UnloadEnd(p, offset: 23, load: 1, elapMin: 1);

      CheckExpected(t.AddHours(-1), t.AddHours(5));
    }

		/*
    [Test]
    public void MultipleMachineCycles()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      long mat = jobLog.AllocateMaterialID("") + 1;

      AddSchedule(schID: 1, unique: "unique", part: "part1", numProc: 1);
      AddPallet(pallet: 3, schID: 1, part: "part1", proc: 1);
      //Don't add pallet 6, make sure the fallback lookup works.

      var p1 = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, fix: 1, program: "prog", matID: mat);
      var p2 = BuildPart(t, pal: 6, unique: "unique", part: "part1", proc: 1, numProc: 1, fix: 1, program: "prog", matID: mat + 1);
      var p3 = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, fix: 1, program: "prog", matID: mat + 2);

      LoadStart(p1, offset: 0, load: 1);
      LoadStart(p2, offset: 1, load: 2);

      LoadEnd(p1, offset: 2, load: 1, cycleOffset: 2, elapMin: 2);
      MovePallet(t, offset: 2, load: 1, pal: 3);

      MachStart(p1, offset: 3, mach: 2);

      LoadEnd(p2, offset: 4, load: 2, cycleOffset: 4, elapMin: 3);
      MovePallet(t, offset: 4, load: 2, pal: 6);

      MachStart(p2, offset: 5, mach: 3);
      MachEnd(p1, offset: 23, mach: 2, elapMin: 20);

      LoadStart(p3, offset: 25, load: 4);
      UnloadStart(p1, offset: 25, load: 4);

      MachEnd(p2, offset: 30, mach: 3, elapMin: 25);

      UnloadStart(p2, offset: 33, load: 3);

      LoadEnd(p3, offset: 36, load: 4, cycleOffset: 38, elapMin: 11);
      UnloadEnd(p1, offset: 37, load: 4, elapMin: 12);
      MovePallet(t, offset: 38, load: 4, pal: 3);

      MachStart(p3, offset: 40, mach: 1);

      UnloadEnd(p2, offset: 41, load: 3, elapMin: 8);
      MovePallet(t, offset: 41, load: 3, pal: 6);

      MachEnd(p3, offset: 61, mach: 1, elapMin: 21);
      UnloadStart(p3, offset: 62, load: 6);
      UnloadEnd(p3, offset: 66, load: 6, elapMin: 4);
      MovePallet(t, offset: 66, load: 6, pal: 3);

      CheckExpected(t.AddHours(-1), t.AddHours(5));
    }

    [Test]
    public void MultipleProcess()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      long mat = jobLog.AllocateMaterialID("") + 1;

      AddSchedule(schID: 1, unique: "unique", part: "part1", numProc: 2);
      AddPallet(pallet: 3, schID: 1, part: "part1", proc: 1);
      AddPallet(pallet: 3, schID: 1, part: "part1", proc: 2);
      AddPallet(pallet: 6, schID: 1, part: "part1", proc: 1);

      var p1d1 = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 2, fix: 1, program: "prog1", matID: mat);
      var p1d2 = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 2, numProc: 2, fix: 1, program: "prog2", matID: mat);
      var p2 = BuildPart(t, pal: 6, unique: "unique", part: "part1", proc: 1, numProc: 2, fix: 1, program: "prog1", matID: mat + 1);
      var p3d1 = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 2, fix: 1, program: "prog3", matID: mat + 2);

      LoadStart(p1d1, offset: 0, load: 1);
      LoadStart(p2, offset: 2, load: 2);

      LoadEnd(p1d1, offset: 4, load: 1, elapMin: 4, cycleOffset: 5);
      MovePallet(t, offset: 5, load: 1, pal: 3);

      LoadEnd(p2, offset: 6, load: 2, elapMin: 4, cycleOffset: 6);
      MovePallet(t, offset: 6, load: 2, pal: 6);

      MachStart(p1d1, offset: 10, mach: 1);
      MachStart(p2, offset: 12, mach: 3);

      MachEnd(p1d1, offset: 20, mach: 1, elapMin: 10);

      LoadStart(p1d2, offset: 22, load: 3);
      UnloadStart(p1d1, offset: 23, load: 3);
      LoadStart(p3d1, offset: 23, load: 3);
      LoadEnd(p3d1, offset: 24, load: 3, cycleOffset: 24, elapMin: 1);
      UnloadEnd(p1d1, offset: 24, load: 3, elapMin: 1);
      LoadEnd(p1d2, offset: 24, load: 3, cycleOffset: 24, elapMin: 1);
      MovePallet(t, offset: 24, load: 3, pal: 3);

      MachStart(p1d2, offset: 30, mach: 4);
      MachEnd(p2, offset: 33, mach: 3, elapMin: 21);

      UnloadStart(p2, offset: 40, load: 4);

      MachEnd(p1d2, offset: 42, mach: 4, elapMin: 12);
      MachStart(p3d1, offset: 43, mach: 4);

      UnloadEnd(p2, offset: 44, load: 4, elapMin: 4);
      MovePallet(t, offset: 45, load: 4, pal: 6);

      MachEnd(p3d1, offset: 50, mach: 4, elapMin: 7);

      UnloadStart(p3d1, offset: 52, load: 1);
      UnloadStart(p1d2, offset: 52, load: 1);
      UnloadEnd(p3d1, offset: 54, load: 1, elapMin: 2);
      UnloadEnd(p1d2, offset: 54, load: 1, elapMin: 2);
      MovePallet(t, offset: 55, load: 1, pal: 3);

      CheckExpected(t.AddHours(-1), t.AddHours(5));
    }

    [Test]
    public void FixedQuantites()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      long mat = jobLog.AllocateMaterialID("") + 1;

      AddSchedule(schID: 1, unique: "unique", part: "part1", numProc: 2);
      AddSchedule(schID: 2, unique: "unique2", part: "part2", numProc: 2);
      AddPallet(pallet: 3, schID: 2, part: "part2", proc: 1);
      AddPallet(pallet: 3, schID: 1, part: "part1", proc: 2);
      AddPallet(pallet: 4, schID: 1, part: "part1", proc: 1);
      AddPallet(pallet: 5, schID: 2, part: "part2", proc: 1);

      var p1d1 = BuildPart(t, pal: 3, unique: "unique2", part: "part2", proc: 1, numProc: 2, fix: 3, program: "prog2", matID: mat);
      var p1d2 = BuildPart(t, pal: 3, unique: "unique2", part: "part2", proc: 2, numProc: 2, fix: 3, program: "prog2", matID: mat);
      var p2 = BuildPart(t, pal: 4, unique: "unique", part: "part1", proc: 1, numProc: 2, fix: 2, program: "prog1", matID: mat + 3);
      var p3 = BuildPart(t, pal: 5, unique: "unique2", part: "part2", proc: 2, numProc: 2, fix: 3, program: "prog2", matID: mat + 5);
      var p4 = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 2, fix: 2, program: "prog1", matID: mat + 8);

      LoadStart(p1d1, offset: 0, load: 1);
      LoadStart(p2, offset: 0, load: 2);
      LoadStart(p3, offset: 1, load: 3);
      LoadEnd(p1d1, offset: 1, load: 1, cycleOffset: 2, elapMin: 1);
      LoadEnd(p3, offset: 2, load: 3, cycleOffset: 2, elapMin: 1);
      LoadEnd(p2, offset: 2, load: 2, cycleOffset: 2, elapMin: 2);
      MovePallet(t, offset: 2, load: 2, pal: 3);
      MovePallet(t, offset: 2, load: 1, pal: 4);
      MovePallet(t, offset: 2, load: 3, pal: 5);

      MachStart(p1d1, offset: 10, mach: 1);
      MachStart(p2, offset: 10, mach: 2);
      MachEnd(p1d1, offset: 20, mach: 1, elapMin: 10);
      MachStart(p3, offset: 22, mach: 1);
      MachEnd(p2, offset: 32, mach: 2, elapMin: 22);

      UnloadStart(p1d1, offset: 40, load: 1);
      LoadStart(p1d2, offset: 40, load: 1);
      LoadStart(p4, offset: 40, load: 1);

      MachEnd(p3, offset: 43, mach: 1, elapMin: 21);

      LoadEnd(p4, offset: 44, load: 1, cycleOffset: 46, elapMin: 4);
      LoadEnd(p1d2, offset: 44, load: 1, cycleOffset: 46, elapMin: 4);
      UnloadEnd(p1d1, offset: 45, load: 1, elapMin: 5);
      MovePallet(t, offset: 46, load: 1, pal: 3);

      UnloadStart(p2, offset: 50, load: 1);
      UnloadStart(p3, offset: 52, load: 2);
      UnloadEnd(p3, offset: 54, load: 2, elapMin: 2);
      UnloadEnd(p2, offset: 55, load: 1, elapMin: 5);
      MovePallet(t, offset: 55, load: 1, pal: 4);

      MachStart(p4, offset: 56, mach: 6);

      MovePallet(t, offset: 58, load: 2, pal: 5);

      MachEnd(p4, offset: 60, mach: 6, elapMin: 4);
      MachStart(p1d2, offset: 62, mach: 7);
      MachEnd(p1d2, offset: 80, mach: 7, elapMin: 18);

      UnloadStart(p1d2, offset: 90, load: 9);
      UnloadEnd(p1d2, offset: 92, load: 9, elapMin: 2);
      UnloadStart(p4, offset: 94, load: 9);
      UnloadEnd(p4, offset: 97, load: 9, elapMin: 3);
      MovePallet(t, offset: 99, load: 9, pal: 3);

      CheckExpected(t.AddHours(-1), t.AddHours(5));
    }

    [Test]
    public void MultipleSchedulesSamePart()
    {
      // This tests that looking the unique up by schedule ID on the pallet status works properly

      var t = DateTime.UtcNow.AddHours(-5);
      long mat = jobLog.AllocateMaterialID("") + 1;

      AddSchedule(schID: 1, unique: "unique", part: "part1", numProc: 2);
      AddSchedule(schID: 2, unique: "unique2", part: "part1", numProc: 2);
      AddPallet(pallet: 3, schID: 2, part: "part1", proc: 1);
      AddPallet(pallet: 3, schID: 1, part: "part1", proc: 2);
      AddPallet(pallet: 4, schID: 1, part: "part1", proc: 1);
      AddPallet(pallet: 4, schID: 2, part: "part1", proc: 2);

      var p3d1 = BuildPart(t, pal: 3, unique: "unique2", part: "part1", proc: 1, numProc: 2, fix: 2, program: "prog1", matID: mat);
      var p3d2 = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 2, numProc: 2, fix: 2, program: "prog2", matID: mat + 2);
      var p4d1 = BuildPart(t, pal: 4, unique: "unique", part: "part1", proc: 1, numProc: 2, fix: 2, program: "prog1", matID: mat + 4);
      var p4d2 = BuildPart(t, pal: 4, unique: "unique2", part: "part1", proc: 2, numProc: 2, fix: 2, program: "prog2", matID: mat + 6);

      LoadStart(p3d1, offset: 0, load: 1);
      LoadStart(p3d2, offset: 0, load: 1);
      LoadEnd(p3d1, offset: 1, load: 1, cycleOffset: 1, elapMin: 1);
      LoadEnd(p3d2, offset: 1, load: 1, cycleOffset: 1, elapMin: 1);
      MovePallet(t, offset: 1, load: 1, pal: 3);

      MachStart(p3d2, offset: 5, mach: 4);

      LoadStart(p4d1, offset: 10, load: 1);
      LoadStart(p4d2, offset: 10, load: 1);
      LoadEnd(p4d1, offset: 12, load: 1, cycleOffset: 12, elapMin: 2);
      LoadEnd(p4d2, offset: 12, load: 1, cycleOffset: 12, elapMin: 2);
      MovePallet(t, offset: 12, load: 1, pal: 4);

      MachEnd(p3d2, offset: 17, mach: 4, elapMin: 12);
      MachStart(p3d1, offset: 19, mach: 4);

      MachStart(p4d1, offset: 22, mach: 5);

      MachEnd(p3d1, offset: 24, mach: 4, elapMin: 5);

      MachEnd(p4d1, offset: 30, mach: 5, elapMin: 8);
      MachStart(p4d2, offset: 32, mach: 5);
      MachEnd(p4d2, offset: 44, mach: 5, elapMin: 12);

      UnloadStart(p3d1, offset: 44, load: 1);
      UnloadStart(p3d2, offset: 44, load: 1);
      UnloadEnd(p3d1, offset: 46, load: 1, elapMin: 2);
      UnloadEnd(p3d2, offset: 46, load: 1, elapMin: 2);
      MovePallet(t, offset: 46, load: 1, pal: 3);

      UnloadStart(p4d1, offset: 50, load: 1);
      UnloadStart(p4d2, offset: 50, load: 1);
      UnloadEnd(p4d1, offset: 51, load: 1, elapMin: 1);
      UnloadEnd(p4d2, offset: 51, load: 1, elapMin: 1);
      MovePallet(t, offset: 51, load: 1, pal: 4);

      CheckExpected(t.AddHours(-1), t.AddHours(5));
    }

    [Test]
    public void SkipShortMachineCycle()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      long mat = jobLog.AllocateMaterialID("") + 1;

      AddSchedule(schID: 1, unique: "unique", part: "part1", numProc: 1);
      AddPallet(pallet: 3, schID: 1, part: "part1", proc: 1);
      //Don't add pallet 6, make sure the fallback lookup works.

      var p1 = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, fix: 1, program: "prog", matID: mat);

      LoadStart(p1, offset: 0, load: 1);
      LoadEnd(p1, offset: 2, load: 1, cycleOffset: 2, elapMin: 2);
      MovePallet(t, offset: 2, load: 1, pal: 3);

      MachStart(p1, offset: 8, mach: 3);

      // Add machine end after 15 seconds
      var bad = p1.Clone();
      bad.TimeUTC = t.AddMinutes(8).AddSeconds(15);
      bad.Code = LogCode.MachineCycleEnd;
      bad.StationNumber = 3;
      bad.Elapsed = TimeSpan.FromSeconds(15);
      log.HandleEvent(bad, dset);
      // don't add to expected, since it should be skipped

      MachStart(p1, offset: 15, mach: 3);
      MachEnd(p1, offset: 22, mach: 3, elapMin: 7);

      UnloadStart(p1, offset: 30, load: 1);
      UnloadEnd(p1, offset: 33, load: 1, elapMin: 3);
      MovePallet(t, offset: 33, load: 1, pal: 3);

      CheckExpected(t.AddHours(-1), t.AddHours(5));
    }

    [Test]
    public void Remachining()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      long mat = jobLog.AllocateMaterialID("") + 1;

      AddSchedule(schID: 1, unique: "unique", part: "part1", numProc: 1);
      AddPallet(pallet: 3, schID: 1, part: "part1", proc: 1);

      var p1 = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, fix: 1, program: "prog", matID: mat);
      var p2 = BuildPart(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, fix: 1, program: "prog", matID: mat + 1);

      LoadStart(p1, offset: 0, load: 5);
      LoadEnd(p1, offset: 2, load: 5, cycleOffset: 3, elapMin: 2);
      MovePallet(t, offset: 3, load: 1, pal: 3);

      MachStart(p1, offset: 4, mach: 2);
      MachEnd(p1, offset: 20, mach: 2, elapMin: 16);

      UnloadStart(p1, offset: 22, load: 1);
      LoadStart(p2, offset: 23, load: 1);
      //No unload or load ends since this is a remachining
      MovePallet(t, offset: 26, load: 1, pal: 3, addExpected: false);

      MachStart(p1, offset: 30, mach: 1);
      MachEnd(p1, offset: 43, mach: 1, elapMin: 13);

      UnloadStart(p1, offset: 45, load: 2);
      LoadStart(p2, offset: 45, load: 2);
      UnloadEnd(p1, offset: 47, load: 2, elapMin: 2);
      LoadEnd(p2, offset: 47, load: 2, cycleOffset: 48, elapMin: 2);
      MovePallet(t, offset: 48, load: 2, pal: 3);

      MachStart(p2, offset: 50, mach: 1);
      MachEnd(p2, offset: 57, mach: 1, elapMin: 7);

      UnloadStart(p2, offset: 60, load: 1);
      UnloadEnd(p2, offset: 66, load: 1, elapMin: 6);
      MovePallet(t, offset: 66, load: 1, pal: 3);

      CheckExpected(t.AddHours(-1), t.AddHours(5), checkValid: false);
    }
		*/
  }

	/*
  [TestFixture]
  public class LogCSVTests : LogTestBase
  {
    private string _logPath;
    private string _sourcePath;

    [SetUp]
    public void Setup()
    {
      ClearLog();
      dset = new ReadOnlyDataSet();
      expected = new List<TestLogEntry>();

      _logPath = System.IO.Path.Combine("bin", "testoutput", "logs");
      _sourcePath = System.IO.Path.Combine("Mazak", "logtest");
      if (!System.IO.Directory.Exists(_logPath))
        System.IO.Directory.CreateDirectory(_logPath);
      var logRead = new LogDataWeb(_logPath);

      foreach (var f in System.IO.Directory.GetFiles(_logPath, "*.csv"))
        System.IO.File.Delete(f);

      AddSchedule(1, "unitest", "testpart:0:1", 2, false);
      AddSchedule(2, "uniother", "otherpart:0:1", 2, false);

      log = new LogTranslation(jobLog, new ConstantRead(dset), logRead, new System.Diagnostics.TraceSource("temp"));
      log.Halt(); // stop the timer, we will inject events directly
    }

    [TearDown]
    public void TearDown()
    {
      jobLog.Close();
    }

    [Test]
    public void All()
    {
      foreach (var f in System.IO.Directory.GetFiles(_sourcePath, "*.csv"))
        System.IO.File.Copy(f, System.IO.Path.Combine(_logPath, System.IO.Path.GetFileName(f)));
      log.HandleElapsed(null, null);

      Check();
    }

    [Test]
    public void SplitInHalf()
    {
      var files = new List<string>(System.IO.Directory.GetFiles(_sourcePath, "*.csv"));
      files.Sort();
      int half = files.Count / 2;

      for (int i = 0; i < half; i += 1)
        System.IO.File.Copy(files[i], System.IO.Path.Combine(_logPath, System.IO.Path.GetFileName(files[i])));
      log.HandleElapsed(null, null);

      for (int i = half; i < files.Count; i += 1)
        System.IO.File.Copy(files[i], System.IO.Path.Combine(_logPath, System.IO.Path.GetFileName(files[i])));
      log.HandleElapsed(null, null);


      Check();
    }

    private void Check()
    {
      //for now, just load and see something is there
      var data = jobLog.GetLogEntries(DateTime.Parse("2012-07-01"), DateTime.Parse("2012-07-04"));
      Assert.GreaterOrEqual(data.Count, 1);

      // there is one file left, a file with a 302 code which we don't process and so therefore don't delete
      Assert.AreEqual(1, System.IO.Directory.GetFiles(_logPath, "*.csv").Length);
    }
  }

  [TestFixture]
  public class RoutingTests
  {
    [Test]
    public void SortSchedules()
    {
      var sortMethod = typeof(RoutingInfo).GetMethod("SortSchedulesByDate",
                                                     System.Reflection.BindingFlags.NonPublic |
                                                     System.Reflection.BindingFlags.Static);

      var tset = new TransactionDataSet();

      var d = DateTime.Now;

      //ScheduleID 15
      tset.Schedule_t.AddSchedule_tRow(1, "Abc", 15, d.AddMinutes(15), 1, 0, 0, 0, 0, 0, "Part1", 3, 4, 1, 15, 1);
      tset.ScheduleProcess_t.AddScheduleProcess_tRow(0, 0, 0, 0, 1, 0, 15);
      tset.ScheduleProcess_t.AddScheduleProcess_tRow(0, 0, 0, 0, 2, 0, 15);

      //ScheduleID 16
      tset.Schedule_t.AddSchedule_tRow(1, "Def", 20, d.AddMinutes(5), 1, 0, 0, 0, 0, 0, "Part2", 8, 4, 1, 16, 1);
      tset.ScheduleProcess_t.AddScheduleProcess_tRow(0, 0, 0, 0, 1, 0, 16);
      tset.ScheduleProcess_t.AddScheduleProcess_tRow(0, 0, 0, 0, 2, 0, 16);

      Assert.AreEqual(15, tset.Schedule_t[0].ScheduleID);
      Assert.AreEqual(16, tset.Schedule_t[1].ScheduleID);
      Assert.AreEqual(15, tset.ScheduleProcess_t[0].ScheduleID);

      sortMethod.Invoke(null, new object[] { tset });

      Assert.AreEqual(2, tset.Schedule_t.Rows.Count);
      Assert.AreEqual(16, tset.Schedule_t[0].ScheduleID);
      Assert.AreEqual(15, tset.Schedule_t[1].ScheduleID);
      Assert.AreEqual(15, tset.ScheduleProcess_t[0].ScheduleID);
    }
  }
	*/
}