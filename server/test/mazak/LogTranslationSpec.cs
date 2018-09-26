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
using BlackMaple.MachineWatchInterface;
using MazakMachineInterface;

namespace MachineWatchTest
{

  public class LogTestBase : IDisposable
  {
    protected JobLogDB jobLog;
    protected JobDB jobDB;
    protected LogTranslation log;
    protected List<BlackMaple.MachineWatchInterface.LogEntry> expected = new List<BlackMaple.MachineWatchInterface.LogEntry>();
    protected List<MazakMachineInterface.LogEntry> raisedByPalletMove = new List<MazakMachineInterface.LogEntry>();
    protected MazakSchedulesAndLoadActions mazakData;
    private List<MazakScheduleRow> _schedules;

    protected LogTestBase()
    {
      var logConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      logConn.Open();
      jobLog = new JobLogDB(logConn);
      jobLog.CreateTables(firstSerialOnEmpty: null);

      var jobConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      jobConn.Open();
      jobDB = new JobDB(jobConn);
      jobDB.CreateTables();

      _schedules = new List<MazakScheduleRow>();
      mazakData = new MazakSchedulesAndLoadActions()
      {
        Schedules = _schedules,
        LoadActions = new LoadAction[] { }
      };

      var settings = new FMSSettings()
      {
        SerialType = SerialType.AssignOneSerialPerMaterial,
        SerialLength = 10
      };
      settings.Queues["thequeue"] = new QueueSize() { MaxSizeBeforeStopUnloading = -1 };
      settings.ExternalQueues["externalq"] = "testserver";

      log = new LogTranslation(jobDB, jobLog, mazakData, settings,
        e => raisedByPalletMove.Add(e)
      );
    }

    public void Dispose()
    {
      jobLog.Close();
      jobDB.Close();
    }

    #region Find Part
    protected void AddTestPart(string unique, string part, int proc, int numProc, int path)
    {
      var sch = new MazakScheduleRow()
      {
        PartName = part + ":4:" + path.ToString(),
        Comment = MazakPart.CreateComment(unique, Enumerable.Repeat(path, numProc), false),
      };
      for (int i = 0; i < numProc; i++)
      {
        sch.Processes.Add(new MazakScheduleProcessRow()
        {
          MazakScheduleRowId = sch.Id,
        });
      }
      _schedules.Add(sch);
    }
    #endregion

    #region Creating Log Entries and Read Data
    protected class TestMaterial
    {
      // data for LogMaterial
      public long MaterialID { get; set; }
      public string MazakPartName { get; set; }
      public string JobPartName { get; set; }
      public string Unique { get; set; }
      public int Process { get; set; }
      public int Path { get; set; }
      public int NumProcess { get; set; }
      public string Face { get; set; }

      // extra data to set data in a single place to keep actual tests shorter.
      public DateTime EventStartTime { get; set; }
      public int Pallet { get; set; }
    }

    protected TestMaterial BuildMaterial(DateTime t, int pal, string unique, string part, int proc, int numProc, string face, long matID, int path = 1)
    {
      return new TestMaterial()
      {
        MaterialID = matID,
        MazakPartName = part + ":4:" + path.ToString(),
        JobPartName = part,
        Unique = unique,
        Process = proc,
        Path = path,
        NumProcess = numProc,
        Face = face,
        EventStartTime = t,
        Pallet = pal,
      };
    }

    protected IEnumerable<TestMaterial> BuildMaterial(DateTime t, int pal, string unique, string part, int proc, int numProc, string face, IEnumerable<long> matIDs, int path = 1)
    {
      return matIDs.Select((matID, idx) =>
        new TestMaterial()
        {
          MaterialID = matID,
          MazakPartName = part + ":4:" + path.ToString(),
          JobPartName = part,
          Unique = unique,
          Process = proc,
          Path = path,
          NumProcess = numProc,
          Face = face + "-" + (idx + 1).ToString(),
          EventStartTime = t,
          Pallet = pal,
        })
        .ToList();
    }

    protected void MachStart(TestMaterial mat, int offset, int mach)
    {
      MachStart(new[] { mat }, offset, mach);
    }
    protected void MachStart(IEnumerable<TestMaterial> mats, int offset, int mach)
    {
      string prog = "program-" + mats.First().MaterialID.ToString();
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.MachineCycleStart,
        ForeignID = "",
        StationNumber = mach,
        Pallet = mats.First().Pallet,
        FullPartName = mats.First().MazakPartName,
        JobPartName = mats.First().JobPartName,
        Process = mats.First().Process,
        FixedQuantity = mats.Count(),
        Program = prog,
        TargetPosition = "",
        FromPosition = "",
      };

      log.HandleEvent(e2);

      expected.Add(new BlackMaple.MachineWatchInterface.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => new BlackMaple.MachineWatchInterface.LogMaterial(
            matID: mat.MaterialID,
            uniq: mat.Unique,
            proc: mat.Process,
            part: mat.JobPartName,
            numProc: mat.NumProcess,
            face: mat.Face
          )),
          pal: mats.First().Pallet.ToString(),
          ty: BlackMaple.MachineWatchInterface.LogType.MachineCycle,
          locName: "MC",
          locNum: e2.StationNumber,
          prog: prog,
          start: true,
          endTime: e2.TimeUTC,
          result: "",
          endOfRoute: false
      ));
    }

    protected void MachEnd(TestMaterial mat, int offset, int mach, int elapMin, int activeMin = 0)
    {
      MachEnd(new[] { mat }, offset, mach, elapMin, activeMin);
    }
    protected void MachEnd(IEnumerable<TestMaterial> mats, int offset, int mach, int elapMin, int activeMin = 0)
    {
      string prog = "program-" + mats.First().MaterialID.ToString();
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.MachineCycleEnd,
        ForeignID = "",
        StationNumber = mach,
        Pallet = mats.First().Pallet,
        FullPartName = mats.First().MazakPartName,
        JobPartName = mats.First().JobPartName,
        Process = mats.First().Process,
        FixedQuantity = mats.Count(),
        Program = prog,
        TargetPosition = "",
        FromPosition = "",
      };

      log.HandleEvent(e2);

      expected.Add(new BlackMaple.MachineWatchInterface.LogEntry(
        cntr: -1,
        mat: mats.Select(mat => new BlackMaple.MachineWatchInterface.LogMaterial(
          matID: mat.MaterialID,
          uniq: mat.Unique,
          proc: mat.Process,
          part: mat.JobPartName,
          numProc: mat.NumProcess,
          face: mat.Face
        )),
        pal: mats.First().Pallet.ToString(),
        ty: BlackMaple.MachineWatchInterface.LogType.MachineCycle,
        locName: "MC",
        locNum: e2.StationNumber,
        prog: prog,
        start: false,
        endTime: e2.TimeUTC,
        result: "",
        endOfRoute: false,
        elapsed: TimeSpan.FromMinutes(elapMin),
        active: TimeSpan.FromMinutes(activeMin)
      ));
    }

    protected void ExpectInspection(TestMaterial mat, string inspTy, string counter, bool result, IEnumerable<MaterialProcessActualPath> path)
    {
      var e = new BlackMaple.MachineWatchInterface.LogEntry(
        cntr: -1,
        mat: new[] {new BlackMaple.MachineWatchInterface.LogMaterial(
          matID: mat.MaterialID,
          uniq: mat.Unique,
          proc: mat.Process,
          part: mat.JobPartName,
          numProc: mat.NumProcess,
          face: ""
        )},
        pal: "",
        ty: BlackMaple.MachineWatchInterface.LogType.Inspection,
        locName: "Inspect",
        locNum: 1,
        prog: counter,
        start: false,
        endTime: DateTime.UtcNow,
        result: result.ToString(),
        endOfRoute: false
      );
      e.ProgramDetails["InspectionType"] = inspTy;
      e.ProgramDetails["ActualPath"] = Newtonsoft.Json.JsonConvert.SerializeObject(path.ToList());
      expected.Add(e);
    }

    protected void LoadStart(TestMaterial mat, int offset, int load)
    {
      LoadStart(new[] { mat }, offset, load);
    }
    protected void LoadStart(IEnumerable<TestMaterial> mats, int offset, int load)
    {
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.LoadBegin,
        ForeignID = "",
        StationNumber = load,
        Pallet = mats.First().Pallet,
        FullPartName = mats.First().MazakPartName,
        JobPartName = mats.First().JobPartName,
        Process = mats.First().Process,
        FixedQuantity = mats.Count(),
        Program = "",
        TargetPosition = "",
        FromPosition = "",
      };

      log.HandleEvent(e2);

      expected.Add(new BlackMaple.MachineWatchInterface.LogEntry(
          cntr: -1,
          mat: new[] {new BlackMaple.MachineWatchInterface.LogMaterial(
            matID: -1,
            uniq: "",
            proc: mats.First().Process,
            part: "",
            numProc: -1,
            face: ""
          )},
          pal: mats.First().Pallet.ToString(),
          ty: BlackMaple.MachineWatchInterface.LogType.LoadUnloadCycle,
          locName: "L/U",
          locNum: e2.StationNumber,
          prog: "LOAD",
          start: true,
          endTime: e2.TimeUTC,
          result: "LOAD",
          endOfRoute: false
      ));
    }

    protected void LoadEnd(TestMaterial mat, int offset, int cycleOffset, int load, int elapMin, int activeMin = 0)
    {
      LoadEnd(new[] { mat }, offset, cycleOffset, load, elapMin, activeMin);
    }
    protected void LoadEnd(IEnumerable<TestMaterial> mats, int offset, int cycleOffset, int load, int elapMin, int activeMin = 0)
    {
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.LoadEnd,
        ForeignID = "",
        StationNumber = load,
        Pallet = mats.First().Pallet,
        FullPartName = mats.First().MazakPartName,
        JobPartName = mats.First().JobPartName,
        Process = mats.First().Process,
        FixedQuantity = mats.Count(),
        Program = "",
        TargetPosition = "",
        FromPosition = "",
      };

      log.HandleEvent(e2);

      expected.Add(new BlackMaple.MachineWatchInterface.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => new BlackMaple.MachineWatchInterface.LogMaterial(
            matID: mat.MaterialID,
            uniq: mat.Unique,
            proc: mat.Process,
            part: mat.JobPartName,
            numProc: mat.NumProcess,
            face: mat.Face
          )),
          pal: mats.First().Pallet.ToString(),
          ty: BlackMaple.MachineWatchInterface.LogType.LoadUnloadCycle,
          locName: "L/U",
          locNum: e2.StationNumber,
          prog: "LOAD",
          start: false,
          endTime: mats.First().EventStartTime.AddMinutes(cycleOffset).AddSeconds(1),
          result: "LOAD",
          endOfRoute: false,
          elapsed: TimeSpan.FromMinutes(elapMin),
          active: TimeSpan.FromMinutes(activeMin)
      ));

      foreach (var mat in mats)
      {
        if (mat.Process > 1) continue;
        expected.Add(new BlackMaple.MachineWatchInterface.LogEntry(
            cntr: -1,
            mat: new[] {new BlackMaple.MachineWatchInterface.LogMaterial(
              matID: mat.MaterialID,
              uniq: mat.Unique,
              proc: mat.Process,
              part: mat.JobPartName,
              numProc: mat.NumProcess,
              face: mat.Face
            )},
            pal: "",
            ty: BlackMaple.MachineWatchInterface.LogType.PartMark,
            locName: "Mark",
            locNum: 1,
            prog: "MARK",
            start: false,
            endTime: mat.EventStartTime.AddMinutes(cycleOffset).AddSeconds(1),
            result: JobLogDB.ConvertToBase62(mat.MaterialID).PadLeft(10, '0'),
            endOfRoute: false
        ));
      }
    }

    protected void UnloadStart(TestMaterial mat, int offset, int load)
    {
      UnloadStart(new[] { mat }, offset, load);
    }
    protected void UnloadStart(IEnumerable<TestMaterial> mats, int offset, int load)
    {
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.UnloadBegin,
        ForeignID = "",
        StationNumber = load,
        Pallet = mats.First().Pallet,
        FullPartName = mats.First().MazakPartName,
        JobPartName = mats.First().JobPartName,
        Process = mats.First().Process,
        FixedQuantity = mats.Count(),
        Program = "",
        TargetPosition = "",
        FromPosition = "",
      };

      log.HandleEvent(e2);

      expected.Add(new BlackMaple.MachineWatchInterface.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => new BlackMaple.MachineWatchInterface.LogMaterial(
            matID: mat.MaterialID,
            uniq: mat.Unique,
            proc: mat.Process,
            part: mat.JobPartName,
            numProc: mat.NumProcess,
            face: mat.Face
          )),
          pal: mats.First().Pallet.ToString(),
          ty: BlackMaple.MachineWatchInterface.LogType.LoadUnloadCycle,
          locName: "L/U",
          locNum: e2.StationNumber,
          prog: "UNLOAD",
          start: true,
          endTime: e2.TimeUTC,
          result: "UNLOAD",
          endOfRoute: false
      ));
    }

    protected List<MaterialToSendToExternalQueue> sendToExternal = new List<MaterialToSendToExternalQueue>();

    protected void UnloadEnd(TestMaterial mat, int offset, int load, int elapMin, int activeMin = 0)
    {
      UnloadEnd(new[] { mat }, offset, load, elapMin, activeMin);
    }
    protected void UnloadEnd(IEnumerable<TestMaterial> mats, int offset, int load, int elapMin, int activeMin = 0)
    {
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.UnloadEnd,
        ForeignID = "",
        StationNumber = load,
        Pallet = mats.First().Pallet,
        FullPartName = mats.First().MazakPartName,
        JobPartName = mats.First().JobPartName,
        Process = mats.First().Process,
        FixedQuantity = mats.Count(),
        Program = "",
        TargetPosition = "",
        FromPosition = "",
      };

      sendToExternal.AddRange(log.HandleEvent(e2));

      expected.Add(new BlackMaple.MachineWatchInterface.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => new BlackMaple.MachineWatchInterface.LogMaterial(
            matID: mat.MaterialID,
            uniq: mat.Unique,
            proc: mat.Process,
            part: mat.JobPartName,
            numProc: mat.NumProcess,
            face: mat.Face
          )),
          pal: mats.First().Pallet.ToString(),
          ty: BlackMaple.MachineWatchInterface.LogType.LoadUnloadCycle,
          locName: "L/U",
          locNum: e2.StationNumber,
          prog: "UNLOAD",
          start: false,
          endTime: e2.TimeUTC.AddSeconds(1),
          result: "UNLOAD",
          endOfRoute: true,
          elapsed: TimeSpan.FromMinutes(elapMin),
          active: TimeSpan.FromMinutes(activeMin)
      ));
    }

    protected void MovePallet(DateTime t, int offset, int pal, int load, int elapMin, bool addExpected = true)
    {
      var e = new MazakMachineInterface.LogEntry();
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

      log.HandleEvent(e);
      raisedByPalletMove.Should().BeEquivalentTo(new[] { e });
      raisedByPalletMove.Clear();

      if (addExpected)
        expected.Add(new BlackMaple.MachineWatchInterface.LogEntry(
          cntr: -1,
          mat: new BlackMaple.MachineWatchInterface.LogMaterial[] { },
          pal: pal.ToString(),
          ty: BlackMaple.MachineWatchInterface.LogType.PalletCycle,
          locName: "Pallet Cycle",
          locNum: 1,
          prog: "",
          start: false,
          endTime: t.AddMinutes(offset),
          result: "PalletCycle",
          endOfRoute: false,
          elapsed: TimeSpan.FromMinutes(elapMin),
          active: TimeSpan.Zero
        ));
    }

    protected void ExpectAddToQueue(TestMaterial mat, int offset, string queue, int pos)
    {
      ExpectAddToQueue(new[] { mat }, offset, queue, pos);
    }
    protected void ExpectAddToQueue(IEnumerable<TestMaterial> mats, int offset, string queue, int startPos)
    {
      foreach (var mat in mats)
      {
        expected.Add(new BlackMaple.MachineWatchInterface.LogEntry(
            cntr: -1,
            mat: new[] { new BlackMaple.MachineWatchInterface.LogMaterial(
              matID: mat.MaterialID,
              uniq: mat.Unique,
              proc: mat.Process,
              part: mat.JobPartName,
              numProc: mat.NumProcess,
              face: mat.Face
            )},
            pal: "",
            ty: BlackMaple.MachineWatchInterface.LogType.AddToQueue,
            locName: queue,
            locNum: startPos,
            prog: "",
            start: false,
            endTime: mat.EventStartTime.AddMinutes(offset),
            result: "",
            endOfRoute: false
        ));
        startPos += 1;
      }
    }

    protected void ExpectRemoveFromQueue(TestMaterial mat, int offset, string queue)
    {
      ExpectRemoveFromQueue(new[] { mat }, offset, queue);
    }
    protected void ExpectRemoveFromQueue(IEnumerable<TestMaterial> mats, int offset, string queue)
    {
      foreach (var mat in mats)
        expected.Add(new BlackMaple.MachineWatchInterface.LogEntry(
            cntr: -1,
            mat: new[] { new BlackMaple.MachineWatchInterface.LogMaterial(
              matID: mat.MaterialID,
              uniq: mat.Unique,
              proc: mat.Process,
              part: mat.JobPartName,
              numProc: mat.NumProcess,
              face: mat.Face
            )},
            pal: "",
            ty: BlackMaple.MachineWatchInterface.LogType.RemoveFromQueue,
            locName: queue,
            locNum: 0,
            prog: "",
            start: false,
            endTime: mat.EventStartTime.AddMinutes(offset).AddSeconds(1),
            result: "",
            endOfRoute: false
        ));
    }

    #endregion

    #region Checking Log
    protected void CheckExpected(DateTime start, DateTime end)
    {
      var log = jobLog.GetLogEntries(start, end);

      log.Should().BeEquivalentTo(expected, options =>
        options
        .Excluding(e => e.Counter)
        .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, 1000))
          .WhenTypeIs<DateTime>()
      );
    }
    #endregion
  }

  public class LogTranslationTests : LogTestBase
  {
    [Fact]
    public void SingleMachineCycle()
    {
      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", proc: 1, numProc: 1, path: 1);

      var p = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, face: "1", numProc: 1, matID: 1);

      LoadStart(p, offset: 0, load: 5);
      LoadEnd(p, offset: 2, load: 5, cycleOffset: 3, elapMin: 2);
      MovePallet(t, offset: 3, load: 1, pal: 3, elapMin: 0);

      MachStart(p, offset: 4, mach: 2);
      MachEnd(p, offset: 20, mach: 2, elapMin: 16);

      UnloadStart(p, offset: 22, load: 1);
      UnloadEnd(p, offset: 23, load: 1, elapMin: 1);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Fact]
    public void MultipleMachineCycles()
    {
      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", proc: 1, numProc: 1, path: 1);
      AddTestPart(unique: "unique", part: "part1", proc: 1, numProc: 1, path: 1);

      var p1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, face: "1", matID: 1);
      var p2 = BuildMaterial(t, pal: 6, unique: "unique", part: "part1", proc: 1, numProc: 1, face: "1", matID: 2);
      var p3 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, face: "1", matID: 3);

      LoadStart(p1, offset: 0, load: 1);
      LoadStart(p2, offset: 1, load: 2);

      LoadEnd(p1, offset: 2, load: 1, cycleOffset: 2, elapMin: 2);
      MovePallet(t, offset: 2, load: 1, pal: 3, elapMin: 0);

      MachStart(p1, offset: 3, mach: 2);

      LoadEnd(p2, offset: 4, load: 2, cycleOffset: 4, elapMin: 3);
      MovePallet(t, offset: 4, load: 2, pal: 6, elapMin: 0);

      MachStart(p2, offset: 5, mach: 3);
      MachEnd(p1, offset: 23, mach: 2, elapMin: 20);

      LoadStart(p3, offset: 25, load: 4);
      UnloadStart(p1, offset: 25, load: 4);

      MachEnd(p2, offset: 30, mach: 3, elapMin: 25);

      UnloadStart(p2, offset: 33, load: 3);

      LoadEnd(p3, offset: 36, load: 4, cycleOffset: 38, elapMin: 11);
      UnloadEnd(p1, offset: 37, load: 4, elapMin: 12);
      MovePallet(t, offset: 38, load: 4, pal: 3, elapMin: 38 - 2);

      MachStart(p3, offset: 40, mach: 1);

      UnloadEnd(p2, offset: 41, load: 3, elapMin: 8);
      MovePallet(t, offset: 41, load: 3, pal: 6, elapMin: 41 - 4);

      MachEnd(p3, offset: 61, mach: 1, elapMin: 21);
      UnloadStart(p3, offset: 62, load: 6);
      UnloadEnd(p3, offset: 66, load: 6, elapMin: 4);
      MovePallet(t, offset: 66, load: 6, pal: 3, elapMin: 66 - 38);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Fact]
    public void MultipleProcess()
    {
      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", proc: 1, numProc: 2, path: 1);
      AddTestPart(unique: "unique", part: "part1", proc: 2, numProc: 2, path: 1);
      AddTestPart(unique: "unique", part: "part1", proc: 1, numProc: 2, path: 1);

      var p1d1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 2, face: "1", matID: 1);
      var p1d2 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 2, numProc: 2, face: "2", matID: 1);
      var p2 = BuildMaterial(t, pal: 6, unique: "unique", part: "part1", proc: 1, numProc: 2, face: "1", matID: 2);
      var p3d1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 2, face: "1", matID: 3);

      LoadStart(p1d1, offset: 0, load: 1);
      LoadStart(p2, offset: 2, load: 2);

      LoadEnd(p1d1, offset: 4, load: 1, elapMin: 4, cycleOffset: 5);
      MovePallet(t, offset: 5, load: 1, pal: 3, elapMin: 0);

      LoadEnd(p2, offset: 6, load: 2, elapMin: 4, cycleOffset: 6);
      MovePallet(t, offset: 6, load: 2, pal: 6, elapMin: 0);

      MachStart(p1d1, offset: 10, mach: 1);
      MachStart(p2, offset: 12, mach: 3);

      MachEnd(p1d1, offset: 20, mach: 1, elapMin: 10);

      LoadStart(p1d2, offset: 22, load: 3);
      UnloadStart(p1d1, offset: 23, load: 3);
      LoadStart(p3d1, offset: 23, load: 3);
      LoadEnd(p3d1, offset: 24, load: 3, cycleOffset: 24, elapMin: 1);
      UnloadEnd(p1d1, offset: 24, load: 3, elapMin: 1);
      LoadEnd(p1d2, offset: 24, load: 3, cycleOffset: 24, elapMin: 1);
      MovePallet(t, offset: 24, load: 3, pal: 3, elapMin: 24 - 5);

      MachStart(p1d2, offset: 30, mach: 4);
      MachEnd(p2, offset: 33, mach: 3, elapMin: 21);

      UnloadStart(p2, offset: 40, load: 4);

      MachEnd(p1d2, offset: 42, mach: 4, elapMin: 12);
      MachStart(p3d1, offset: 43, mach: 4);

      UnloadEnd(p2, offset: 44, load: 4, elapMin: 4);
      MovePallet(t, offset: 45, load: 4, pal: 6, elapMin: 45 - 6);

      MachEnd(p3d1, offset: 50, mach: 4, elapMin: 7);

      UnloadStart(p3d1, offset: 52, load: 1);
      UnloadStart(p1d2, offset: 52, load: 1);
      UnloadEnd(p3d1, offset: 54, load: 1, elapMin: 2);
      UnloadEnd(p1d2, offset: 54, load: 1, elapMin: 2);
      MovePallet(t, offset: 55, load: 1, pal: 3, elapMin: 55 - 24);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Fact]
    public void ActiveTime()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 2);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 2);

      var proc1path1 = BuildMaterial(t, pal: 2, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1, face: "1", matID: 1);
      var proc2path1 = BuildMaterial(t, pal: 2, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1, face: "2", matID: 1);
      var proc1path2 = BuildMaterial(t, pal: 4, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 2, face: "1", matID: 2);
      var proc2path2 = BuildMaterial(t, pal: 4, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 2, face: "2", matID: 2);

      var j = new JobPlan("uuuu", 2, new[] { 2, 2 });
      j.PartName = "pppp";
      j.SetExpectedLoadTime(process: 1, path: 1, t: TimeSpan.FromMinutes(11));
      j.SetExpectedLoadTime(process: 1, path: 2, t: TimeSpan.FromMinutes(12));
      j.SetExpectedLoadTime(process: 2, path: 1, t: TimeSpan.FromMinutes(21));
      j.SetExpectedLoadTime(process: 2, path: 2, t: TimeSpan.FromMinutes(22));
      j.SetExpectedUnloadTime(process: 1, path: 1, t: TimeSpan.FromMinutes(711));
      j.SetExpectedUnloadTime(process: 1, path: 2, t: TimeSpan.FromMinutes(712));
      j.SetExpectedUnloadTime(process: 2, path: 1, t: TimeSpan.FromMinutes(721));
      j.SetExpectedUnloadTime(process: 2, path: 2, t: TimeSpan.FromMinutes(722));
      var stop1 = new JobMachiningStop("MC");
      stop1.ExpectedCycleTime = TimeSpan.FromMinutes(33);
      j.AddMachiningStop(process: 1, path: 1, r: stop1);
      j.AddMachiningStop(process: 1, path: 2, r: stop1);
      var stop2 = new JobMachiningStop("MC");
      stop2.ExpectedCycleTime = TimeSpan.FromMinutes(44);
      j.AddMachiningStop(process: 2, path: 1, r: stop2);
      j.AddMachiningStop(process: 2, path: 2, r: stop2);
      var newJobs = new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      };
      jobDB.AddJobs(newJobs, null);

      LoadStart(proc1path1, offset: 0, load: 1);
      LoadStart(proc1path2, offset: 1, load: 2);

      LoadEnd(proc1path1, offset: 2, cycleOffset: 5, load: 1, elapMin: 2, activeMin: 11);
      MovePallet(t, offset: 5, pal: 2, load: 1, elapMin: 0);

      LoadEnd(proc1path2, offset: 7, cycleOffset: 8, load: 2, elapMin: 6, activeMin: 12);
      MovePallet(t, offset: 8, pal: 4, load: 2, elapMin: 0);

      MachStart(proc1path1, offset: 10, mach: 1);
      MachStart(proc1path2, offset: 11, mach: 2);

      MachEnd(proc1path1, offset: 20, mach: 1, elapMin: 10, activeMin: 33);
      MachEnd(proc1path2, offset: 22, mach: 2, elapMin: 11, activeMin: 33);

      UnloadStart(proc1path1, offset: 24, load: 1);
      LoadStart(proc2path1, offset: 24, load: 1);

      UnloadStart(proc1path2, offset: 27, load: 2);
      LoadStart(proc2path2, offset: 27, load: 2);

      UnloadEnd(proc1path1, offset: 28, load: 1, elapMin: 28 - 24, activeMin: 711);
      LoadEnd(proc2path1, offset: 28, cycleOffset: 29, load: 1, elapMin: 28 - 24, activeMin: 21);
      MovePallet(t, offset: 29, pal: 2, load: 1, elapMin: 29 - 5);

      UnloadEnd(proc1path2, offset: 30, load: 2, elapMin: 30 - 27, activeMin: 712);
      LoadEnd(proc2path2, offset: 30, cycleOffset: 33, load: 2, elapMin: 30 - 27, activeMin: 22);
      MovePallet(t, offset: 33, pal: 4, load: 2, elapMin: 33 - 8);

      MachStart(proc2path1, offset: 40, mach: 1);
      MachStart(proc2path2, offset: 41, mach: 2);

      MachEnd(proc2path1, offset: 50, mach: 1, elapMin: 10, activeMin: 44);
      MachEnd(proc2path2, offset: 52, mach: 2, elapMin: 11, activeMin: 44);

      UnloadStart(proc2path1, offset: 60, load: 2);
      UnloadEnd(proc2path1, offset: 61, load: 2, elapMin: 1, activeMin: 721);

      UnloadStart(proc2path2, offset: 61, load: 1);
      UnloadEnd(proc2path2, offset: 63, load: 1, elapMin: 2, activeMin: 722);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Fact]
    public void LargeFixedQuantites()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1);

      var proc1 = BuildMaterial(t, pal: 1, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1, face: "1", matIDs: new long[] { 1, 2, 3 });
      var proc2 = BuildMaterial(t, pal: 1, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1, face: "2", matIDs: new long[] { 1, 2, 3 });
      var proc1snd = BuildMaterial(t, pal: 1, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1, face: "1", matIDs: new long[] { 4, 5, 6 });
      var proc2snd = BuildMaterial(t, pal: 1, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1, face: "2", matIDs: new long[] { 4, 5, 6 });
      var proc1thrd = BuildMaterial(t, pal: 1, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1, face: "1", matIDs: new long[] { 7, 8, 9 });

      LoadStart(proc1, offset: 0, load: 1);
      LoadEnd(proc1, offset: 5, cycleOffset: 6, load: 1, elapMin: 5);
      MovePallet(t, pal: 1, offset: 6, load: 1, elapMin: 0);

      MachStart(proc1, offset: 10, mach: 5);
      MachEnd(proc1, offset: 15, mach: 5, elapMin: 5);

      UnloadStart(proc1, offset: 20, load: 2);
      LoadStart(proc2, offset: 20, load: 2);
      LoadStart(proc1snd, offset: 20, load: 2);

      UnloadEnd(proc1, offset: 24, load: 2, elapMin: 4);
      LoadEnd(proc2, offset: 24, cycleOffset: 25, load: 2, elapMin: 4);
      LoadEnd(proc1snd, offset: 24, cycleOffset: 25, load: 2, elapMin: 4);
      MovePallet(t, pal: 1, offset: 25, load: 2, elapMin: 25 - 6);

      MachStart(proc2, offset: 30, mach: 6);
      MachEnd(proc2, offset: 33, mach: 6, elapMin: 3);
      MachStart(proc1snd, offset: 35, mach: 6);
      MachEnd(proc1snd, offset: 37, mach: 6, elapMin: 2);

      UnloadStart(proc2, offset: 40, load: 1);
      UnloadStart(proc1snd, offset: 40, load: 1);
      LoadStart(proc2snd, offset: 40, load: 1);
      LoadStart(proc1thrd, offset: 40, load: 1);

      UnloadEnd(proc2, offset: 45, load: 1, elapMin: 5);
      UnloadEnd(proc1snd, offset: 45, load: 1, elapMin: 5);
      LoadEnd(proc2snd, offset: 45, cycleOffset: 50, load: 1, elapMin: 5);
      LoadEnd(proc1thrd, offset: 45, cycleOffset: 50, load: 1, elapMin: 5);
      MovePallet(t, pal: 1, offset: 50, load: 1, elapMin: 50 - 25);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Fact]
    public void SkipShortMachineCycle()
    {
      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", proc: 1, numProc: 1, path: 1);

      var p1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, face: "1", matID: 1);

      LoadStart(p1, offset: 0, load: 1);
      LoadEnd(p1, offset: 2, load: 1, cycleOffset: 2, elapMin: 2);
      MovePallet(t, offset: 2, load: 1, pal: 3, elapMin: 0);

      MachStart(p1, offset: 8, mach: 3);

      // Add machine end after 15 seconds
      var bad = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = t.AddMinutes(8).AddSeconds(15),
        Code = LogCode.MachineCycleEnd,
        ForeignID = "",
        StationNumber = 3,
        Pallet = p1.Pallet,
        FullPartName = p1.MazakPartName,
        JobPartName = p1.JobPartName,
        Process = p1.Process,
        FixedQuantity = 1,
        Program = "program",
        TargetPosition = "",
        FromPosition = "",
      };
      log.HandleEvent(bad);
      // don't add to expected, since it should be skipped

      MachStart(p1, offset: 15, mach: 3);
      MachEnd(p1, offset: 22, mach: 3, elapMin: 7);

      UnloadStart(p1, offset: 30, load: 1);
      UnloadEnd(p1, offset: 33, load: 1, elapMin: 3);
      MovePallet(t, offset: 33, load: 1, pal: 3, elapMin: 33 - 2);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Fact]
    public void Remachining()
    {
      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", proc: 1, numProc: 1, path: 1);

      var p1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, face: "1", matID: 1);
      var p2 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, face: "1", matID: 2);

      LoadStart(p1, offset: 0, load: 5);
      LoadEnd(p1, offset: 2, load: 5, cycleOffset: 3, elapMin: 2);
      MovePallet(t, offset: 3, load: 1, pal: 3, elapMin: 0);

      MachStart(p1, offset: 4, mach: 2);
      MachEnd(p1, offset: 20, mach: 2, elapMin: 16);

      UnloadStart(p1, offset: 22, load: 1);
      LoadStart(p2, offset: 23, load: 1);
      //No unload or load ends since this is a remachining
      MovePallet(t, offset: 26, load: 1, pal: 3, elapMin: 0, addExpected: false);

      MachStart(p1, offset: 30, mach: 1);
      MachEnd(p1, offset: 43, mach: 1, elapMin: 13);

      UnloadStart(p1, offset: 45, load: 2);
      LoadStart(p2, offset: 45, load: 2);
      UnloadEnd(p1, offset: 47, load: 2, elapMin: 2);
      LoadEnd(p2, offset: 47, load: 2, cycleOffset: 48, elapMin: 2);
      MovePallet(t, offset: 48, load: 2, pal: 3, elapMin: 48 - 3);

      MachStart(p2, offset: 50, mach: 1);
      MachEnd(p2, offset: 57, mach: 1, elapMin: 7);

      UnloadStart(p2, offset: 60, load: 1);
      UnloadEnd(p2, offset: 66, load: 1, elapMin: 6);
      MovePallet(t, offset: 66, load: 1, pal: 3, elapMin: 66 - 48);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Fact]
    public void Inspections()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1);

      var proc1 = BuildMaterial(t, pal: 2, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1, face: "1", matID: 1);
      var proc2 = BuildMaterial(t, pal: 2, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1, face: "2", matID: 1);

      var j = new JobPlan("uuuu", 2);
      j.PartName = "pppp";
      j.AddInspection(new JobInspectionData(
        "insp_proc1", "counter1", 10, TimeSpan.FromMinutes(1000), inspSingleProc: 1));
      j.AddInspection(new JobInspectionData(
        "insp_whole", "counter2", 15, TimeSpan.FromMinutes(1500), inspSingleProc: -1));
      var newJobs = new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      };
      jobDB.AddJobs(newJobs, null);

      LoadStart(proc1, offset: 0, load: 6);
      LoadEnd(proc1, offset: 2, cycleOffset: 5, load: 6, elapMin: 2);
      MovePallet(t, offset: 5, pal: 2, load: 1, elapMin: 0);

      MachStart(proc1, offset: 10, mach: 4);

      MachEnd(proc1, offset: 20, mach: 4, elapMin: 10);
      ExpectInspection(proc1, inspTy: "insp_proc1", counter: "counter1", result: false,
        path: new[] {
          new MaterialProcessActualPath() {
            MaterialID = proc1.MaterialID,
            Process = 1,
            Pallet = "2",
            LoadStation = 6,
            Stops = new List<MaterialProcessActualPath.Stop> {
              new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 4}
            },
            UnloadStation = -1
          }
        }
      );

      UnloadStart(proc1, offset: 24, load: 1);
      LoadStart(proc2, offset: 24, load: 1);

      UnloadEnd(proc1, offset: 28, load: 1, elapMin: 28 - 24);
      LoadEnd(proc2, offset: 28, cycleOffset: 29, load: 1, elapMin: 28 - 24);
      MovePallet(t, offset: 29, pal: 2, load: 1, elapMin: 29 - 5);

      MachStart(proc2, offset: 40, mach: 7);
      MachEnd(proc2, offset: 50, mach: 7, elapMin: 10);
      ExpectInspection(proc2, inspTy: "insp_whole", counter: "counter2", result: false,
        path: new[] {
          new MaterialProcessActualPath() {
            MaterialID = proc1.MaterialID,
            Process = 1,
            Pallet = "2",
            LoadStation = 6,
            Stops = new List<MaterialProcessActualPath.Stop> {
              new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 4}
            },
            UnloadStation = 1
          },
          new MaterialProcessActualPath() {
            MaterialID = proc1.MaterialID,
            Process = 2,
            Pallet = "2",
            LoadStation = 1,
            Stops = new List<MaterialProcessActualPath.Stop> {
              new MaterialProcessActualPath.Stop() {StationName = "MC", StationNum = 7}
            },
            UnloadStation = -1
          }
        }
      );

      UnloadStart(proc2, offset: 60, load: 2);
      UnloadEnd(proc2, offset: 61, load: 2, elapMin: 1);


      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Fact]
    public void Queues()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1);

      var proc1 = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1, face: "1", matID: 1);
      var proc1snd = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1, face: "1", matID: 2);

      var proc2 = BuildMaterial(t, pal: 9, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1, face: "2", matID: 1);

      var j = new JobPlan("uuuu", 2);
      j.PartName = "pppp";
      j.SetOutputQueue(1, 1, "thequeue");
      j.SetInputQueue(2, 1, "thequeue");
      var newJobs = new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      };
      jobDB.AddJobs(newJobs, null);

      LoadStart(proc1, offset: 0, load: 1);
      LoadEnd(proc1, offset: 5, cycleOffset: 6, load: 1, elapMin: 5);
      MovePallet(t, pal: 8, offset: 6, load: 1, elapMin: 0);

      MachStart(proc1, offset: 10, mach: 5);
      MachEnd(proc1, offset: 15, mach: 5, elapMin: 5);

      UnloadStart(proc1, offset: 20, load: 2);
      LoadStart(proc1snd, offset: 20, load: 2);
      UnloadEnd(proc1, offset: 24, load: 2, elapMin: 4);
      ExpectAddToQueue(proc1, offset: 24, queue: "thequeue", pos: 0);
      LoadEnd(proc1snd, offset: 24, cycleOffset: 25, load: 2, elapMin: 4);
      MovePallet(t, pal: 8, offset: 25, load: 2, elapMin: 25 - 6);

      LoadStart(proc2, offset: 28, load: 1);
      LoadEnd(proc2, offset: 29, cycleOffset: 30, load: 1, elapMin: 1);
      MovePallet(t, pal: 9, offset: 30, load: 1, elapMin: 0);
      ExpectRemoveFromQueue(proc2, offset: 30, queue: "thequeue");

      MachStart(proc2, offset: 30, mach: 6);
      MachStart(proc1snd, offset: 35, mach: 3);
      MachEnd(proc1snd, offset: 37, mach: 3, elapMin: 2);
      MachEnd(proc2, offset: 39, mach: 6, elapMin: 9);

      CheckExpected(t.AddHours(-1), t.AddHours(10));

      sendToExternal.Should().BeEmpty();
    }

    [Fact]
    public void QueuesFirstInFirstOut()
    {
      // run multiple process 1s.  Also have multiple parts on a face.

      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1);
      AddTestPart(unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1);

      var proc1 = BuildMaterial(t, pal: 4, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1, face: "1", matIDs: new long[] { 1, 2, 3 });
      var proc1snd = BuildMaterial(t, pal: 4, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1, face: "1", matIDs: new long[] { 4, 5, 6 });
      var proc1thrd = BuildMaterial(t, pal: 4, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, path: 1, face: "1", matIDs: new long[] { 7, 8, 9 });

      var proc2 = BuildMaterial(t, pal: 5, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1, face: "2", matIDs: new long[] { 1, 2, 3 });
      var proc2snd = BuildMaterial(t, pal: 5, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, path: 1, face: "2", matIDs: new long[] { 4, 5, 6 });

      var j = new JobPlan("uuuu", 2);
      j.PartName = "pppp";
      j.SetOutputQueue(1, 1, "thequeue");
      j.SetInputQueue(2, 1, "thequeue");
      j.SetOutputQueue(2, 1, "externalq");
      var newJobs = new NewJobs()
      {
        Jobs = new List<JobPlan> { j }
      };
      jobDB.AddJobs(newJobs, null);

      LoadStart(proc1, offset: 0, load: 10);
      LoadEnd(proc1, offset: 2, cycleOffset: 3, load: 10, elapMin: 2);
      MovePallet(t, offset: 3, pal: 4, load: 10, elapMin: 0);

      MachStart(proc1, offset: 5, mach: 7);
      MachEnd(proc1, offset: 10, mach: 7, elapMin: 5);

      UnloadStart(proc1, offset: 12, load: 3);
      LoadStart(proc1snd, offset: 12, load: 3);
      UnloadEnd(proc1, offset: 15, load: 3, elapMin: 3);
      ExpectAddToQueue(proc1, offset: 15, queue: "thequeue", startPos: 0);
      LoadEnd(proc1snd, offset: 15, cycleOffset: 16, load: 3, elapMin: 3);
      MovePallet(t, offset: 16, pal: 4, load: 3, elapMin: 16 - 3);

      MachStart(proc1snd, offset: 20, mach: 2);
      MachEnd(proc1snd, offset: 25, mach: 2, elapMin: 5);

      UnloadStart(proc1snd, offset: 30, load: 1);
      LoadStart(proc1thrd, offset: 30, load: 1);
      UnloadEnd(proc1snd, offset: 33, load: 1, elapMin: 3);
      ExpectAddToQueue(proc1snd, offset: 33, queue: "thequeue", startPos: 3);
      LoadEnd(proc1thrd, offset: 33, cycleOffset: 34, load: 1, elapMin: 3);
      MovePallet(t, offset: 34, pal: 4, load: 1, elapMin: 34 - 16);

      //queue now has 6 elements
      jobLog.GetMaterialInQueue("thequeue").Should().BeEquivalentTo(
        Enumerable.Range(1, 6).Select(i =>
          new JobLogDB.QueuedMaterial()
          {
            MaterialID = i,
            Queue = "thequeue",
            Position = i - 1,
            Unique = "uuuu",
            PartName = "pppp",
            NumProcesses = 2
          }
        )
      );

      //first load should pull in mat ids 1, 2, 3

      LoadStart(proc2, offset: 40, load: 2);
      LoadEnd(proc2, offset: 44, cycleOffset: 45, load: 2, elapMin: 4);
      MovePallet(t, offset: 45, pal: 5, load: 2, elapMin: 0);
      ExpectRemoveFromQueue(proc2, offset: 45, queue: "thequeue");

      MachStart(proc2, offset: 50, mach: 100);
      MachEnd(proc2, offset: 55, mach: 100, elapMin: 5);

      UnloadStart(proc2, offset: 60, load: 1);
      LoadStart(proc2snd, offset: 60, load: 1);
      UnloadEnd(proc2, offset: 65, load: 1, elapMin: 5);
      LoadEnd(proc2snd, offset: 65, cycleOffset: 66, load: 1, elapMin: 5);
      MovePallet(t, offset: 66, pal: 5, load: 1, elapMin: 66 - 45);
      ExpectRemoveFromQueue(proc2snd, offset: 66, queue: "thequeue");

      jobLog.GetMaterialInQueue("thequeue").Should().BeEmpty();

      CheckExpected(t.AddHours(-1), t.AddHours(10));

      sendToExternal.Should().BeEquivalentTo(new[] {
        new MaterialToSendToExternalQueue() {
          Server = "testserver",
          PartName = "pppp",
          Queue = "externalq",
          Serial = "0000000001"
        },
        new MaterialToSendToExternalQueue() {
          Server = "testserver",
          PartName = "pppp",
          Queue = "externalq",
          Serial = "0000000002"
        },
        new MaterialToSendToExternalQueue() {
          Server = "testserver",
          PartName = "pppp",
          Queue = "externalq",
          Serial = "0000000003"
        },
      });
    }
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

      log = new LogTranslation(jobLog, new ConstantRead(dset), logRead);
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
  */
}