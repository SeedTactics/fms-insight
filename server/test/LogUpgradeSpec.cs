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
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using Xunit;
using FluentAssertions;

namespace MachineWatchTest
{

  public class EventDBUpgradeSpec : IDisposable
  {
    private readonly JobLogDB _log;
    private string _tempFile;

    public EventDBUpgradeSpec()
    {
      _log = new JobLogDB(new FMSSettings());
      _tempFile = System.IO.Path.GetTempFileName();
      System.IO.File.Copy("log.v17.db", _tempFile, overwrite: true);
      _log.Open(_tempFile);
    }

    public void Dispose()
    {
      _log.Close();
      if (!string.IsNullOrEmpty(_tempFile) && System.IO.File.Exists(_tempFile))
        System.IO.File.Delete(_tempFile);
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
        options.Excluding(x => x.Counter)
      );

      _log.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 1,
        JobUnique = "uuu1",
        PartName = "part1",
        NumProcesses = 2,
        Workorder = "work1",
        Serial = "serial1",
        Paths = new System.Collections.Generic.Dictionary<int, int>()
      });
      _log.GetMaterialDetails(2).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 2,
        JobUnique = "uuu1",
        PartName = "part1",
        NumProcesses = 2,
        Workorder = null,
        Serial = "serial2",
        Paths = new System.Collections.Generic.Dictionary<int, int>()
      });
      _log.GetMaterialDetails(3).Should().BeEquivalentTo(new MaterialDetails()
      {
        MaterialID = 3,
        JobUnique = "uuu2",
        PartName = "part2",
        NumProcesses = 1,
        Workorder = "work3",
        Serial = null,
        Paths = new System.Collections.Generic.Dictionary<int, int>()
      });
    }

    [Fact]
    public void QueueTablesCorrectlyCreated()
    {
      var now = new DateTime(2018, 7, 12, 5, 6, 7, DateTimeKind.Utc);
      var matId = _log.AllocateMaterialID("uuu5", "part5", 1);
      var mat = new LogMaterial(matId, "uuu5", 1, "part5", 1, "", "", "");

      _log.RecordAddMaterialToQueue(JobLogDB.EventLogMaterial.FromLogMat(mat), "queue", 5, null, now.AddHours(2));

      _log.GetMaterialInQueue("queue").Should().BeEquivalentTo(new[] {
        new JobLogDB.QueuedMaterial() {
          MaterialID = matId,
          Queue = "queue",
          Position = 0,
          Unique = "uuu5",
          PartNameOrCasting = "part5",
          NumProcesses = 1,
        }
      });
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

  }

}