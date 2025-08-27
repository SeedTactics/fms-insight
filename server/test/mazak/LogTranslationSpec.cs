/* Copyright (c) 2024, John Lenz

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
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.FMSInsight.Tests;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlackMaple.FMSInsight.Mazak.Tests
{
  public class LogTestBase : IDisposable
  {
    protected RepositoryConfig _repoCfg;
    protected IRepository jobLog;
    protected List<BlackMaple.MachineFramework.LogEntry> expected =
      new List<BlackMaple.MachineFramework.LogEntry>();
    protected List<MazakMachineInterface.LogEntry> raisedByEvent = new List<MazakMachineInterface.LogEntry>();
    protected List<MazakMachineInterface.LogEntry> expectedMazakLogEntries =
      new List<MazakMachineInterface.LogEntry>();
    private List<MazakMachineInterface.LogEntry> _cachedLulEvents = [];
    protected FMSSettings settings;
    protected MazakConfig mazakCfg;
    protected MazakAllDataAndLogs mazakData;
    protected List<ToolPocketRow> mazakDataTools;
    private List<MazakScheduleRow> _schedules;
    private List<MazakPalletSubStatusRow> _palletSubStatus;
    private List<MazakPalletPositionRow> _palletPositions;
    private List<MazakPartRow> _mazakPartRows;

    protected LogTestBase()
    {
      var serialSt = new SerialSettings()
      {
        ConvertMaterialIDToSerial = (m) => SerialSettings.ConvertToBase62(m, 10),
      };
      settings = new FMSSettings() { QuarantineQueue = "quarantineQ" };
      settings.Queues["thequeue"] = new QueueInfo() { MaxSizeBeforeStopUnloading = -1 };

      _repoCfg = RepositoryConfig.InitializeMemoryDB(serialSt);
      jobLog = _repoCfg.OpenConnection();

      _schedules = new List<MazakScheduleRow>();
      _palletSubStatus = new List<MazakPalletSubStatusRow>();
      _palletPositions = new List<MazakPalletPositionRow>();
      _mazakPartRows = new List<MazakPartRow>();
      mazakDataTools = new List<ToolPocketRow>();
      mazakData = new MazakAllDataAndLogs()
      {
        Schedules = _schedules,
        LoadActions = Enumerable.Empty<LoadAction>(),
        PalletPositions = _palletPositions,
        PalletSubStatuses = _palletSubStatus,
        Parts = _mazakPartRows,
      };

      mazakCfg = new MazakConfig() { DBType = MazakDbType.MazakSmooth };
    }

    public void Dispose()
    {
      jobLog.Dispose();
      _repoCfg.Dispose();
    }

    protected void HandleEvent(
      MazakMachineInterface.LogEntry e,
      bool expectedMachineEnd = false,
      bool expectedFinalLulEvt = false
    )
    {
      expectedMazakLogEntries.Add(e);

      var ret = LogTranslation.HandleEvents(
        repo: jobLog,
        mazakData: mazakData with
        {
          Logs = [.. _cachedLulEvents, e],
        },
        machGroupName: "machinespec",
        fmsSettings: settings,
        e => raisedByEvent.Add(e),
        mazakConfig: mazakCfg,
        loadTools: () => mazakDataTools
      );

      ret.StoppedBecauseRecentMachineEvent.ShouldBe(expectedMachineEnd);
      ret.PalletWithMostRecentEventAsLoadUnloadEnd.ShouldBe(expectedFinalLulEvt ? e.Pallet : null);

      if (expectedFinalLulEvt)
      {
        _cachedLulEvents.Add(e);
      }
      else
      {
        _cachedLulEvents.Clear();
      }
    }

    protected LogTranslation.HandleEventResult CheckPalletStatusMatchesLogs()
    {
      return LogTranslation.HandleEvents(
        repo: jobLog,
        mazakData: mazakData with
        {
          Logs = [],
        },
        machGroupName: "machinespec",
        fmsSettings: settings,
        e => raisedByEvent.Add(e),
        mazakConfig: mazakCfg,
        loadTools: () => mazakDataTools
      );
    }

    #region Mazak Data Setup
    protected int AddTestPart(string unique, string part, int numProc)
    {
      var cnt = _schedules.Count(s => s.PartName.Substring(0, s.PartName.IndexOf(":")) == part);
      var sch = new MazakScheduleRow()
      {
        Id = 50 + _schedules.Count(),
        PartName = part + ":4:" + (cnt + 1).ToString(),
        Comment = unique + "-Insight",
      };
      for (int i = 0; i < numProc; i++)
      {
        sch.Processes.Add(new MazakScheduleProcessRow() { MazakScheduleRowId = sch.Id });
      }
      _schedules.Add(sch);
      return sch.Id;
    }

    protected void AddTestPartPrograms(int schId, bool insightPart, IReadOnlyList<string> programs)
    {
      var sch = _schedules.First(s => s.Id == schId);
      var partRow = new MazakPartRow()
      {
        PartName = sch.PartName,
        Comment = insightPart ? sch.Comment : null,
      };
      for (int i = 0; i < programs.Count; i++)
      {
        partRow.Processes.Add(new MazakPartProcessRow() { ProcessNumber = i + 1, MainProgram = programs[i] });
      }
      _mazakPartRows.Add(partRow);
    }

    protected void SetPallet(int pal, bool atLoadStation)
    {
      _palletPositions.RemoveAll(p => p.PalletNumber == pal);
      _palletPositions.Add(
        new MazakPalletPositionRow() { PalletNumber = pal, PalletPosition = atLoadStation ? "LS01" : "MMM" }
      );
    }

    protected void SetPalletFace(int pal, int schId, int proc, int fixQty)
    {
      _palletSubStatus.Add(
        new MazakPalletSubStatusRow()
        {
          PalletNumber = pal,
          ScheduleID = schId,
          PartProcessNumber = proc,
          FixQuantity = fixQty,
        }
      );
    }

    protected void ClearPalletFace(int pal, int proc)
    {
      _palletSubStatus.RemoveAll(s => s.PalletNumber == pal && s.PartProcessNumber == proc);
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
      public string Workorder { get; set; } = "";

      // extra data to set data in a single place to keep actual tests shorter.
      public DateTime EventStartTime { get; set; }
      public int Pallet { get; set; }

      public LogMaterial ToLogMat()
      {
        return new LogMaterial()
        {
          MaterialID = MaterialID,
          JobUniqueStr = Unique,
          PartName = JobPartName,
          Process = Process,
          Path = Path,
          NumProcesses = NumProcess,
          Face = Process,
          Serial = SerialSettings.ConvertToBase62(MaterialID).PadLeft(10, '0'),
          Workorder = Workorder,
        };
      }
    }

    protected TestMaterial BuildMaterial(
      DateTime t,
      int pal,
      string unique,
      string part,
      int proc,
      int numProc,
      long matID,
      int partIdx = 1,
      string workorder = ""
    )
    {
      return new TestMaterial()
      {
        MaterialID = matID,
        MazakPartName = part + ":4:" + partIdx.ToString(),
        JobPartName = part,
        Unique = unique,
        Process = proc,
        Path = 1,
        NumProcess = numProc,
        EventStartTime = t,
        Pallet = pal,
        Workorder = workorder,
      };
    }

    protected IEnumerable<TestMaterial> BuildMaterial(
      DateTime t,
      int pal,
      string unique,
      string part,
      int proc,
      int numProc,
      IEnumerable<long> matIDs,
      int partIdx = 1
    )
    {
      return matIDs
        .Select(
          (matID, idx) =>
            new TestMaterial()
            {
              MaterialID = matID,
              MazakPartName = part + ":4:" + partIdx.ToString(),
              JobPartName = part,
              Unique = unique,
              Process = proc,
              Path = 1,
              NumProcess = numProc,
              EventStartTime = t,
              Pallet = pal,
            }
        )
        .ToList();
    }

    protected TestMaterial AdjUnique(TestMaterial m, string uniq, int? numProc = null)
    {
      return new TestMaterial()
      {
        MaterialID = m.MaterialID,
        MazakPartName = m.MazakPartName,
        JobPartName = m.JobPartName,
        Unique = uniq,
        Process = m.Process,
        Path = m.Path,
        NumProcess = numProc ?? m.NumProcess,
        EventStartTime = m.EventStartTime,
        Pallet = m.Pallet,
      };
    }

    protected TestMaterial AdjProcess(TestMaterial m, int proc)
    {
      return new TestMaterial()
      {
        MaterialID = m.MaterialID,
        MazakPartName = m.MazakPartName,
        JobPartName = m.JobPartName,
        Unique = m.Unique,
        Process = proc,
        Path = m.Path,
        NumProcess = m.NumProcess,
        EventStartTime = m.EventStartTime,
        Pallet = m.Pallet,
      };
    }

    protected enum AllocateTy
    {
      None,
      Assigned,
      Casting,
    }

    protected void AddMaterialToQueue(
      TestMaterial material,
      int proc,
      string queue,
      int offset,
      AllocateTy allocate = AllocateTy.None
    )
    {
      switch (allocate)
      {
        case AllocateTy.Assigned:
        {
          var matId = jobLog.AllocateMaterialID(material.Unique, material.JobPartName, material.NumProcess);
          if (matId != material.MaterialID)
          {
            throw new Exception(
              "Allocating matId " + material.MaterialID.ToString() + " returned id " + matId.ToString()
            );
          }
          expected.Add(
            jobLog.RecordSerialForMaterialID(
              matId,
              proc,
              SerialSettings.ConvertToBase62(material.MaterialID).PadLeft(10, '0'),
              DateTime.UtcNow
            )
          );
          break;
        }
        case AllocateTy.Casting:
        {
          var matId = jobLog.AllocateMaterialIDForCasting(material.JobPartName);
          if (matId != material.MaterialID)
          {
            throw new Exception(
              "Allocating matId " + material.MaterialID.ToString() + " returned id " + matId.ToString()
            );
          }
          expected.Add(
            jobLog.RecordSerialForMaterialID(
              matId,
              proc,
              SerialSettings.ConvertToBase62(material.MaterialID).PadLeft(10, '0'),
              DateTime.UtcNow
            )
          );
          break;
        }
      }
      expected.AddRange(
        jobLog.RecordAddMaterialToQueue(
          mat: new EventLogMaterial()
          {
            MaterialID = material.MaterialID,
            Process = proc,
            Face = 0,
          },
          queue: queue,
          position: -1,
          operatorName: null,
          reason: "FromTestSuite",
          timeUTC: material.EventStartTime.AddMinutes(offset)
        )
      );
    }

    protected void RemoveFromAllQueues(TestMaterial mat, int proc, int offset)
    {
      expected.AddRange(
        jobLog.RecordRemoveMaterialFromAllQueues(
          new EventLogMaterial()
          {
            MaterialID = mat.MaterialID,
            Process = proc,
            Face = 0,
          },
          null,
          mat.EventStartTime.AddMinutes(offset)
        )
      );
    }

    protected void MachStart(
      TestMaterial mat,
      int offset,
      int mach,
      string mazakProg = null,
      string logProg = null,
      long? progRev = null
    )
    {
      MachStart(new[] { mat }, offset, mach, mazakProg, logProg, progRev);
    }

    protected void MachStart(
      IEnumerable<TestMaterial> mats,
      int offset,
      int mach,
      string mazakProg = null,
      string logProg = null,
      long? progRev = null
    )
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
        Program = mazakProg ?? prog,
        TargetPosition = "",
        FromPosition = "",
      };

      HandleEvent(e2);

      var expectedLog = new BlackMaple.MachineFramework.LogEntry(
        cntr: -1,
        mat: mats.Select(mat => mat.ToLogMat()),
        pal: mats.First().Pallet,
        ty: LogType.MachineCycle,
        locName: "machinespec",
        locNum: mazakCfg.MachineNumbers?[e2.StationNumber - 1] ?? e2.StationNumber,
        prog: logProg ?? prog,
        start: true,
        endTime: e2.TimeUTC,
        result: ""
      );
      if (progRev.HasValue)
      {
        expectedLog = expectedLog with
        {
          ProgramDetails = ImmutableDictionary<string, string>.Empty.Add(
            "ProgramRevision",
            progRev.Value.ToString()
          ),
        };
      }
      expected.Add(expectedLog);
    }

    protected void MachEnd(
      TestMaterial mat,
      int offset,
      int mach,
      int elapMin,
      int activeMin = 0,
      IEnumerable<ToolUse> tools = null,
      string mazakProg = null,
      string logProg = null,
      long? progRev = null
    )
    {
      MachEnd(new[] { mat }, offset, mach, elapMin, activeMin, tools, mazakProg, logProg, progRev);
    }

    protected void MachEnd(
      IEnumerable<TestMaterial> mats,
      int offset,
      int mach,
      int elapMin,
      int activeMin = 0,
      IEnumerable<ToolUse> tools = null,
      string mazakProg = null,
      string logProg = null,
      long? progRev = null
    )
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
        Program = mazakProg ?? prog,
        TargetPosition = "",
        FromPosition = "",
      };

      HandleEvent(e2);

      var newEntry = new BlackMaple.MachineFramework.LogEntry(
        cntr: -1,
        mat: mats.Select(mat => mat.ToLogMat()),
        pal: mats.First().Pallet,
        ty: LogType.MachineCycle,
        locName: "machinespec",
        locNum: mazakCfg.MachineNumbers?[e2.StationNumber - 1] ?? e2.StationNumber,
        prog: logProg ?? prog,
        start: false,
        endTime: e2.TimeUTC,
        result: "",
        elapsed: TimeSpan.FromMinutes(elapMin),
        active: TimeSpan.FromMinutes(activeMin)
      );
      if (tools != null)
      {
        newEntry = newEntry with { Tools = tools.ToImmutableList() };
      }
      if (progRev.HasValue)
      {
        newEntry = newEntry with
        {
          ProgramDetails = ImmutableDictionary<string, string>.Empty.Add(
            "ProgramRevision",
            progRev.Value.ToString()
          ),
        };
      }
      expected.Add(newEntry);
    }

    protected void ExpectInspection(
      TestMaterial mat,
      string inspTy,
      string counter,
      int offset,
      bool result,
      IEnumerable<MaterialProcessActualPath> path
    )
    {
      var e = new BlackMaple.MachineFramework.LogEntry(
        cntr: -1,
        mat: [mat.ToLogMat() with { Face = 0 }],
        pal: 0,
        ty: LogType.Inspection,
        locName: "Inspect",
        locNum: 1,
        prog: counter,
        start: false,
        endTime: mat.EventStartTime.AddMinutes(offset),
        result: result.ToString()
      );
      e = e with
      {
        ProgramDetails = ImmutableDictionary<string, string>
          .Empty.Add("InspectionType", inspTy)
          .Add("ActualPath", JsonSerializer.Serialize(path.ToList())),
      };
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

      HandleEvent(e2);

      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: new[]
          {
            MkLogMat.Mk(
              matID: -1,
              uniq: "",
              proc: mats.First().Process,
              part: "",
              numProc: -1,
              face: "",
              serial: "",
              workorder: ""
            ),
          },
          pal: mats.First().Pallet,
          ty: LogType.LoadUnloadCycle,
          locName: "L/U",
          locNum: e2.StationNumber,
          prog: "LOAD",
          start: true,
          endTime: e2.TimeUTC,
          result: "LOAD"
        )
      );
    }

    protected void LoadEnd(
      TestMaterial mat,
      int offset,
      int load,
      int elapMin,
      int activeMin = 0,
      bool expectMark = true,
      string expectWork = null,
      int totalActiveMin = 0,
      int? totalMatCnt = null
    )
    {
      LoadEnd(
        new[] { mat },
        offset,
        load,
        elapMin,
        activeMin,
        expectMark,
        expectWork,
        totalActiveMin,
        totalMatCnt
      );
    }

    protected void LoadEnd(
      IEnumerable<TestMaterial> mats,
      int offset,
      int load,
      int elapMin,
      int activeMin = 0,
      bool expectMark = true,
      string expectWork = null,
      int totalActiveMin = 0,
      int? totalMatCnt = null,
      IReadOnlySet<long> matsToSkipMark = null
    )
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

      HandleEvent(e2, expectedFinalLulEvt: true);

      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => mat.ToLogMat()),
          pal: mats.First().Pallet,
          ty: LogType.LoadUnloadCycle,
          locName: "L/U",
          locNum: e2.StationNumber,
          prog: "LOAD",
          start: false,
          endTime: mats.First().EventStartTime.AddMinutes(offset).AddSeconds(1),
          result: "LOAD",
          elapsed: TimeSpan.FromSeconds(
            Math.Round(
              totalActiveMin > 0 ? elapMin * 60.0 * activeMin / totalActiveMin
                : totalMatCnt.HasValue ? elapMin * 60.0 * mats.Count() / totalMatCnt.Value
                : elapMin * 60.0,
              1
            )
          ),
          active: TimeSpan.FromMinutes(activeMin)
        )
      );

      foreach (var mat in mats)
      {
        if (mat.Process > 1)
          continue;

        if (expectMark == true && matsToSkipMark?.Contains(mat.MaterialID) != true)
        {
          expected.Add(
            new BlackMaple.MachineFramework.LogEntry(
              cntr: -1,
              mat: [mat.ToLogMat() with { Process = 0, Path = null, Face = 0 }],
              pal: 0,
              ty: LogType.PartMark,
              locName: "Mark",
              locNum: 1,
              prog: "MARK",
              start: false,
              endTime: mat.EventStartTime.AddMinutes(offset),
              result: SerialSettings.ConvertToBase62(mat.MaterialID).PadLeft(10, '0')
            )
          );
        }

        if (!string.IsNullOrEmpty(expectWork))
        {
          expected.Add(
            new BlackMaple.MachineFramework.LogEntry(
              cntr: -1,
              mat: [mat.ToLogMat() with { Process = 0, Path = null, Face = 0 }],
              pal: 0,
              ty: LogType.OrderAssignment,
              locName: "Order",
              locNum: 1,
              prog: "",
              start: false,
              endTime: mat.EventStartTime.AddMinutes(offset),
              result: expectWork
            )
          );
        }
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

      HandleEvent(e2);

      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => mat.ToLogMat()),
          pal: mats.First().Pallet,
          ty: LogType.LoadUnloadCycle,
          locName: "L/U",
          locNum: e2.StationNumber,
          prog: "UNLOAD",
          start: true,
          endTime: e2.TimeUTC,
          result: "UNLOAD"
        )
      );
    }

    protected void UnloadEnd(
      TestMaterial mat,
      int offset,
      int load,
      int elapMin,
      int activeMin = 0,
      int totalActiveMin = 0,
      int? totalMatCnt = null
    )
    {
      UnloadEnd(new[] { mat }, offset, load, elapMin, activeMin, totalActiveMin, totalMatCnt);
    }

    protected void UnloadEnd(
      IEnumerable<TestMaterial> mats,
      int offset,
      int load,
      int elapMin,
      int activeMin = 0,
      int totalActiveMin = 0,
      int? totalMatCnt = null
    )
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

      HandleEvent(e2, expectedFinalLulEvt: true);

      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => mat.ToLogMat()),
          pal: mats.First().Pallet,
          ty: LogType.LoadUnloadCycle,
          locName: "L/U",
          locNum: e2.StationNumber,
          prog: "UNLOAD",
          start: false,
          endTime: e2.TimeUTC,
          result: "UNLOAD",
          elapsed: TimeSpan.FromSeconds(
            Math.Round(
              totalActiveMin > 0 ? elapMin * 60.0 * activeMin / totalActiveMin
                : totalMatCnt.HasValue ? elapMin * 60.0 * mats.Count() / totalMatCnt.Value
                : elapMin * 60.0,
              1
            )
          ),
          active: TimeSpan.FromMinutes(activeMin / (totalMatCnt ?? 1))
        )
      );
    }

    protected void ExpectPalletStart(DateTime t, int pal, int offset, IEnumerable<TestMaterial> mats)
    {
      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => mat.ToLogMat()),
          pal: pal,
          ty: LogType.PalletCycle,
          locName: "Pallet Cycle",
          locNum: 1,
          prog: "",
          start: true,
          endTime: t.AddMinutes(offset),
          result: "PalletCycle",
          elapsed: TimeSpan.Zero,
          active: TimeSpan.Zero
        )
      );
    }

    protected void ExpectPalletCycle(
      DateTime t,
      int pal,
      int offset,
      int elapMin,
      IEnumerable<TestMaterial> mats = null
    )
    {
      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: (mats ?? []).Select(mat => mat.ToLogMat()),
          pal: pal,
          ty: LogType.PalletCycle,
          locName: "Pallet Cycle",
          locNum: 1,
          prog: "",
          start: false,
          endTime: t.AddMinutes(offset),
          result: "PalletCycle",
          elapsed: TimeSpan.FromMinutes(elapMin),
          active: TimeSpan.Zero
        )
      );
    }

    protected void MovePallet(DateTime t, int offset, int pal, int load)
    {
      var e = new MazakMachineInterface.LogEntry()
      {
        Code = LogCode.PalletMoving,
        TimeUTC = t.AddMinutes(offset),
        ForeignID = "",
        Pallet = pal,
        FullPartName = "",
        JobPartName = "",
        Process = 1,
        FixedQuantity = -1,
        Program = "",
        TargetPosition = "S011", //stacker
        FromPosition = "LS01" + load.ToString(),
      };

      HandleEvent(e);
    }

    protected void StockerStart(TestMaterial mat, int offset, int stocker, bool waitForMachine)
    {
      StockerStart(new[] { mat }, offset, stocker, waitForMachine);
    }

    protected void StockerStart(IEnumerable<TestMaterial> mats, int offset, int stocker, bool waitForMachine)
    {
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.PalletMoveComplete,
        ForeignID = "",
        Pallet = mats.First().Pallet,
        TargetPosition = "S" + stocker.ToString().PadLeft(3, '0'),
        FromPosition = "",
      };

      HandleEvent(e2);

      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => mat.ToLogMat()),
          pal: mats.First().Pallet,
          ty: LogType.PalletInStocker,
          locName: "Stocker",
          locNum: stocker,
          prog: "Arrive",
          start: true,
          endTime: e2.TimeUTC,
          result: waitForMachine ? "WaitForMachine" : "WaitForUnload"
        )
      );
    }

    protected void StockerEnd(TestMaterial mat, int offset, int stocker, int elapMin, bool waitForMachine)
    {
      StockerEnd(new[] { mat }, offset, stocker, elapMin, waitForMachine);
    }

    protected void StockerEnd(
      IEnumerable<TestMaterial> mats,
      int offset,
      int stocker,
      int elapMin,
      bool waitForMachine
    )
    {
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.PalletMoving,
        ForeignID = "",
        Pallet = mats.First().Pallet,
        FromPosition = "S" + stocker.ToString().PadLeft(3, '0'),
        TargetPosition = "",
      };

      HandleEvent(e2);

      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => mat.ToLogMat()),
          pal: mats.First().Pallet,
          ty: LogType.PalletInStocker,
          locName: "Stocker",
          locNum: stocker,
          prog: "Depart",
          start: false,
          endTime: e2.TimeUTC,
          result: waitForMachine ? "WaitForMachine" : "WaitForUnload",
          elapsed: TimeSpan.FromMinutes(elapMin),
          active: TimeSpan.Zero
        )
      );
    }

    protected void RotaryQueueStart(TestMaterial mat, int offset, int mc)
    {
      RotaryQueueStart(new[] { mat }, offset, mc);
    }

    protected void RotaryQueueStart(IEnumerable<TestMaterial> mats, int offset, int mc)
    {
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.PalletMoveComplete,
        ForeignID = "",
        Pallet = mats.First().Pallet,
        TargetPosition = "M" + mc.ToString().PadLeft(2, '0') + "1",
        FromPosition = "",
      };

      HandleEvent(e2);

      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => mat.ToLogMat()),
          pal: mats.First().Pallet,
          ty: LogType.PalletOnRotaryInbound,
          locName: "machinespec",
          locNum: mazakCfg.MachineNumbers?[mc - 1] ?? mc,
          prog: "Arrive",
          start: true,
          endTime: e2.TimeUTC,
          result: "Arrive"
        )
      );
    }

    protected void RotateIntoWorktable(TestMaterial mat, int offset, int mc, int elapMin)
    {
      RotateIntoWorktable(new[] { mat }, offset, mc, elapMin);
    }

    protected void RotateIntoWorktable(IEnumerable<TestMaterial> mats, int offset, int mc, int elapMin)
    {
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.StartRotatePalletIntoMachine,
        StationNumber = mc,
        ForeignID = "",
        Pallet = mats.First().Pallet,
      };

      HandleEvent(e2);

      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => mat.ToLogMat()),
          pal: mats.First().Pallet,
          ty: LogType.PalletOnRotaryInbound,
          locName: "machinespec",
          locNum: mazakCfg.MachineNumbers?[mc - 1] ?? mc,
          prog: "Depart",
          result: "RotateIntoWorktable",
          start: false,
          endTime: e2.TimeUTC,
          elapsed: TimeSpan.FromMinutes(elapMin),
          active: TimeSpan.Zero
        )
      );
    }

    protected void MoveFromInboundRotaryTable(TestMaterial mat, int offset, int mc, int elapMin)
    {
      MoveFromInboundRotaryTable(new[] { mat }, offset, mc, elapMin);
    }

    protected void MoveFromInboundRotaryTable(IEnumerable<TestMaterial> mats, int offset, int mc, int elapMin)
    {
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.PalletMoving,
        ForeignID = "",
        FromPosition = "M" + mc.ToString().PadLeft(2, '0') + "1",
        Pallet = mats.First().Pallet,
      };

      HandleEvent(e2);

      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: mats.Select(mat => mat.ToLogMat()),
          pal: mats.First().Pallet,
          ty: LogType.PalletOnRotaryInbound,
          locName: "machinespec",
          locNum: mazakCfg.MachineNumbers?[mc - 1] ?? mc,
          prog: "Depart",
          start: false,
          endTime: e2.TimeUTC,
          result: "LeaveMachine",
          elapsed: TimeSpan.FromMinutes(elapMin),
          active: TimeSpan.Zero
        )
      );
    }

    protected void MoveFromOutboundRotaryTable(TestMaterial mat, int offset, int mc)
    {
      MoveFromOutboundRotaryTable(new[] { mat }, offset, mc);
    }

    protected void MoveFromOutboundRotaryTable(IEnumerable<TestMaterial> mats, int offset, int mc)
    {
      // event is the same when moving from inbound or outbound either case
      var e2 = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = mats.First().EventStartTime.AddMinutes(offset),
        Code = LogCode.PalletMoving,
        ForeignID = "",
        FromPosition = "M" + mc.ToString().PadLeft(2, '0') + "1",
        Pallet = mats.First().Pallet,
      };

      HandleEvent(e2);

      // event should be ignored, so no expected event is added
    }

    protected void ExpectAddToQueue(TestMaterial mat, int offset, string queue, int pos, string reason = null)
    {
      ExpectAddToQueue(new[] { mat }, offset, queue, pos, reason);
    }

    protected void ExpectAddToQueue(
      IEnumerable<TestMaterial> mats,
      int offset,
      string queue,
      int startPos,
      string reason = null
    )
    {
      foreach (var mat in mats)
      {
        expected.Add(
          new BlackMaple.MachineFramework.LogEntry(
            cntr: -1,
            mat: [mat.ToLogMat() with { Face = 0 }],
            pal: 0,
            ty: LogType.AddToQueue,
            locName: queue,
            locNum: startPos,
            prog: reason ?? "Unloaded",
            start: false,
            endTime: mat.EventStartTime.AddMinutes(offset),
            result: ""
          )
        );
        startPos += 1;
      }
    }

    protected void ExpectRemoveFromQueue(
      TestMaterial mat,
      int offset,
      string queue,
      int startingPos,
      string reason,
      int elapMin
    )
    {
      ExpectRemoveFromQueue(new[] { mat }, offset, queue, startingPos, reason, elapMin);
    }

    protected void ExpectRemoveFromQueue(
      IEnumerable<TestMaterial> mats,
      int offset,
      string queue,
      int startingPos,
      string reason,
      int elapMin
    )
    {
      foreach (var mat in mats)
        expected.Add(
          new BlackMaple.MachineFramework.LogEntry(
            cntr: -1,
            mat: [mat.ToLogMat() with { Face = 0, Path = mat.Process == 0 ? null : mat.Path }],
            pal: 0,
            ty: LogType.RemoveFromQueue,
            locName: queue,
            locNum: startingPos,
            prog: reason ?? "",
            start: false,
            endTime: mat.EventStartTime.AddMinutes(offset),
            result: "",
            elapsed: TimeSpan.FromMinutes(elapMin),
            active: TimeSpan.Zero
          )
        );
    }

    protected void SignalForQuarantine(TestMaterial mat, int offset, string queue)
    {
      SignalForQuarantine(new[] { mat }, offset, queue);
    }

    protected void SignalForQuarantine(IEnumerable<TestMaterial> mats, int offset, string queue)
    {
      foreach (var mat in mats)
      {
        jobLog.SignalMaterialForQuarantine(
          new EventLogMaterial()
          {
            MaterialID = mat.MaterialID,
            Process = mat.Process,
            Face = mat.Process,
          },
          pallet: mat.Pallet,
          queue: queue,
          operatorName: null,
          reason: null,
          timeUTC: mat.EventStartTime.AddMinutes(offset)
        );

        expected.Add(
          new BlackMaple.MachineFramework.LogEntry(
            cntr: -1,
            mat: [mat.ToLogMat()],
            pal: mat.Pallet,
            ty: LogType.SignalQuarantine,
            locName: queue,
            locNum: -1,
            prog: "QuarantineAfterUnload",
            result: "QuarantineAfterUnload",
            start: false,
            endTime: mat.EventStartTime.AddMinutes(offset)
          )
        );
      }
    }

    protected void SwapMaterial(TestMaterial matOnPal, TestMaterial matToAdd, int offset, bool unassigned)
    {
      var time = matOnPal.EventStartTime.AddMinutes(offset);
      var swap = jobLog.SwapMaterialInCurrentPalletCycle(
        pallet: matOnPal.Pallet,
        oldMatId: matOnPal.MaterialID,
        newMatId: matToAdd.MaterialID,
        operatorName: null,
        quarantineQueue: settings.QuarantineQueue,
        timeUTC: time
      );

      for (int i = 0; i < expected.Count; i++)
      {
        if (
          expected[i].Pallet == matOnPal.Pallet
          && expected[i].Material.Any(m => m.MaterialID == matOnPal.MaterialID)
        )
        {
          expected[i] = JobLogTest.TransformLog(
            matOnPal.MaterialID,
            logMat =>
              logMat with
              {
                MaterialID = matToAdd.MaterialID,
                Serial = SerialSettings.ConvertToBase62(matToAdd.MaterialID).PadLeft(10, '0'),
                Path = logMat.Process == 0 ? null : matToAdd.Path,
              }
          )(expected[i]);
        }
      }

      if (unassigned)
      {
        // remove uniq on existing events
        for (int i = 0; i < expected.Count; i++)
        {
          if (expected[i].Material.Any(m => m.MaterialID == matOnPal.MaterialID))
          {
            expected[i] = JobLogTest.TransformLog(matOnPal.MaterialID, JobLogTest.SetUniqInMat(""))(
              expected[i]
            );
          }
          if (expected[i].Material.Any(m => m.MaterialID == matToAdd.MaterialID))
          {
            expected[i] = JobLogTest.TransformLog(
              matToAdd.MaterialID,
              m =>
                m with
                {
                  JobUniqueStr = matToAdd.Unique,
                  NumProcesses = 2,
                  Path = m.Process == 0 ? null : matToAdd.Path,
                }
            )(expected[i]);
          }
        }
      }

      expected.AddRange(swap.NewLogEntries);
    }

    protected void AdjustLogMaterial(TestMaterial mat, Func<LogMaterial, LogMaterial> adjust)
    {
      for (int i = 0; i < expected.Count; i++)
      {
        if (expected[i].Material.Any(m => m.MaterialID == mat.MaterialID))
        {
          expected[i] = JobLogTest.TransformLog(mat.MaterialID, adjust)(expected[i]);
        }
      }
    }

    #endregion

    #region Checking Log
    protected void CheckExpected(DateTime start, DateTime end)
    {
      jobLog.GetLogEntries(start, end).EventsShouldBe(expected);

      raisedByEvent.ShouldBeEquivalentTo(expectedMazakLogEntries);
    }

    protected void CheckMatInQueue(string queue, IEnumerable<TestMaterial> mats)
    {
      var actual = jobLog.GetMaterialInAllQueues().Where(m => m.Queue == queue).ToList();

      actual.ShouldBeEquivalentTo(
        mats.Select(
            (m, idx) =>
              new QueuedMaterial()
              {
                MaterialID = m.MaterialID,
                Queue = queue,
                Position = idx,
                Unique = m.Unique,
                NextProcess = 1,
                Serial = SerialSettings.ConvertToBase62(m.MaterialID).PadLeft(10, '0'),
                Paths = ImmutableDictionary<int, int>.Empty,
                PartNameOrCasting = m.JobPartName,
                NumProcesses = m.NumProcess,
                AddTimeUTC = actual[idx].AddTimeUTC, // Ignore AddTimeUTC for comparison
              }
          )
          .ToList()
      );
    }

    protected void CheckMatInQueueIgnoreAddTime(string queue, IEnumerable<QueuedMaterial> mats)
    {
      var actual = jobLog.GetMaterialInAllQueues().Where(m => m.Queue == queue).ToList();

      actual.ShouldBeEquivalentTo(
        mats.Select(
            (m, idx) =>
              m with
              {
                AddTimeUTC = actual[idx].AddTimeUTC, // Ignore AddTimeUTC for comparison
              }
          )
          .ToList()
      );
    }
    #endregion
  }

  public class LogTranslationTests : LogTestBase
  {
    [Test]
    public void SingleMachineCycle()
    {
      var j = new Job()
      {
        UniqueStr = "unique",
        PartName = "part1",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "singlematSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", numProc: 1);

      var p = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, matID: 1);

      LoadStart(p, offset: 0, load: 5);
      LoadEnd(p, offset: 2, load: 5, elapMin: 2);
      ExpectPalletStart(t, pal: 3, offset: 2, mats: [p]);
      MovePallet(t, offset: 3, load: 1, pal: 3);

      MachStart(p, offset: 4, mach: 2);
      MachEnd(p, offset: 20, mach: 2, elapMin: 16);

      UnloadStart(p, offset: 22, load: 1);
      UnloadEnd(p, offset: 23, load: 1, elapMin: 1);
      ExpectPalletCycle(t, pal: 3, offset: 23, elapMin: 21, mats: [p]);
      MovePallet(t, offset: 24, pal: 3, load: 1);

      CheckExpected(t.AddHours(-1), t.AddHours(10));

      jobLog
        .GetMaterialDetails(p.MaterialID)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = p.MaterialID,
            JobUnique = "unique",
            PartName = "part1",
            NumProcesses = 1,
            Serial = SerialSettings.ConvertToBase62(p.MaterialID).PadLeft(10, '0'),
            Workorder = null,
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1),
          }
        );
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void MultipleMachineCycles(bool customMachineNums)
    {
      if (customMachineNums)
      {
        mazakCfg = mazakCfg with { MachineNumbers = [201, 202, 203, 204, 205] };
      }

      var j = new Job()
      {
        UniqueStr = "unique",
        PartName = "part1",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "multipleSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", numProc: 1);
      AddTestPart(unique: "unique", part: "part1", numProc: 1);

      var p1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, matID: 1);
      var p2 = BuildMaterial(t, pal: 6, unique: "unique", part: "part1", proc: 1, numProc: 1, matID: 2);
      var p3 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, matID: 3);

      LoadStart(p1, offset: 0, load: 1);
      LoadStart(p2, offset: 1, load: 2);

      LoadEnd(p1, offset: 2, load: 1, elapMin: 2);
      ExpectPalletStart(t, pal: 3, offset: 2, mats: [p1]);
      MovePallet(t, offset: 2, load: 1, pal: 3);

      MachStart(p1, offset: 3, mach: 2);

      LoadEnd(p2, offset: 4, load: 2, elapMin: 3);
      ExpectPalletStart(t, pal: 6, offset: 4, mats: [p2]);
      MovePallet(t, offset: 4, load: 2, pal: 6);

      MachStart(p2, offset: 5, mach: 3);
      MachEnd(p1, offset: 23, mach: 2, elapMin: 20);

      LoadStart(p3, offset: 25, load: 4);
      UnloadStart(p1, offset: 25, load: 4);

      MachEnd(p2, offset: 30, mach: 3, elapMin: 25);

      UnloadStart(p2, offset: 33, load: 3);

      LoadEnd(p3, offset: 37, load: 4, elapMin: 12 / 2);
      UnloadEnd(p1, offset: 37, load: 4, elapMin: 12 / 2);
      ExpectPalletCycle(t, pal: 3, offset: 37, elapMin: 37 - 2, mats: [p1]);
      ExpectPalletStart(t, pal: 3, offset: 37, mats: [p3]);
      MovePallet(t, offset: 38, load: 4, pal: 3);

      MachStart(p3, offset: 40, mach: 1);

      UnloadEnd(p2, offset: 41, load: 3, elapMin: 8);
      ExpectPalletCycle(t, pal: 6, offset: 41, elapMin: 41 - 4, mats: [p2]);
      MovePallet(t, offset: 41, load: 3, pal: 6);

      MachEnd(p3, offset: 61, mach: 1, elapMin: 21);
      UnloadStart(p3, offset: 62, load: 6);
      UnloadEnd(p3, offset: 66, load: 6, elapMin: 4);
      ExpectPalletCycle(t, pal: 3, offset: 66, elapMin: 66 - 37, mats: [p3]);
      MovePallet(t, offset: 66, load: 6, pal: 3);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void MultipleProcess()
    {
      var j = new Job()
      {
        UniqueStr = "unique",
        PartName = "part1",
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) },
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "multipleprocSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", numProc: 2);
      AddTestPart(unique: "unique", part: "part1", numProc: 2);
      AddTestPart(unique: "unique", part: "part1", numProc: 2);

      var p1d1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 2, matID: 1);
      var p1d2 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 2, numProc: 2, matID: 1);
      var p2 = BuildMaterial(t, pal: 6, unique: "unique", part: "part1", proc: 1, numProc: 2, matID: 2);
      var p3d1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 2, matID: 3);

      LoadStart(p1d1, offset: 0, load: 1);
      LoadStart(p2, offset: 2, load: 2);

      LoadEnd(p1d1, offset: 4, load: 1, elapMin: 4);
      ExpectPalletStart(t, pal: 3, offset: 4, mats: [p1d1]);
      MovePallet(t, offset: 5, load: 1, pal: 3);

      LoadEnd(p2, offset: 6, load: 2, elapMin: 4);
      ExpectPalletStart(t, pal: 6, offset: 6, mats: [p2]);
      MovePallet(t, offset: 6, load: 2, pal: 6);

      MachStart(p1d1, offset: 10, mach: 1);
      MachStart(p2, offset: 12, mach: 3);

      MachEnd(p1d1, offset: 20, mach: 1, elapMin: 10);

      LoadStart(p1d2, offset: 21, load: 3);
      UnloadStart(p1d1, offset: 21, load: 3);
      LoadStart(p3d1, offset: 21, load: 3);
      LoadEnd(p3d1, offset: 24, load: 3, elapMin: 3 / 3);
      UnloadEnd(p1d1, offset: 24, load: 3, elapMin: 3 / 3);
      LoadEnd(p1d2, offset: 24, load: 3, elapMin: 3 / 3);
      ExpectPalletCycle(t, pal: 3, offset: 24, elapMin: 24 - 4, mats: [p1d1]);
      ExpectPalletStart(t, pal: 3, offset: 24, mats: [p3d1, p1d2]);
      MovePallet(t, offset: 24, load: 3, pal: 3);

      MachStart(p1d2, offset: 30, mach: 4);
      MachEnd(p2, offset: 33, mach: 3, elapMin: 21);

      UnloadStart(p2, offset: 40, load: 4);

      MachEnd(p1d2, offset: 42, mach: 4, elapMin: 12);
      MachStart(p3d1, offset: 43, mach: 4);

      UnloadEnd(p2, offset: 44, load: 4, elapMin: 4);
      ExpectPalletCycle(t, pal: 6, offset: 44, elapMin: 44 - 6, mats: [p2]);
      MovePallet(t, offset: 45, load: 4, pal: 6);

      MachEnd(p3d1, offset: 50, mach: 4, elapMin: 7);

      UnloadStart(p3d1, offset: 52, load: 1);
      UnloadStart(p1d2, offset: 52, load: 1);
      UnloadEnd(p3d1, offset: 54, load: 1, elapMin: 2 / 2);
      UnloadEnd(p1d2, offset: 54, load: 1, elapMin: 2 / 2);
      ExpectPalletCycle(t, pal: 3, offset: 54, elapMin: 54 - 24, mats: [p3d1, p1d2]);
      MovePallet(t, offset: 55, load: 1, pal: 3);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void ActiveTime()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu1", part: "pppp", numProc: 2);
      AddTestPart(unique: "uuuu2", part: "pppp", numProc: 2);

      var proc1path1 = BuildMaterial(
        t,
        pal: 2,
        unique: "uuuu1",
        part: "pppp",
        proc: 1,
        numProc: 2,
        partIdx: 1,
        matID: 1
      );
      var proc2path1 = BuildMaterial(
        t,
        pal: 2,
        unique: "uuuu1",
        part: "pppp",
        proc: 2,
        numProc: 2,
        partIdx: 1,
        matID: 1
      );
      var proc1path2 = BuildMaterial(
        t,
        pal: 4,
        unique: "uuuu2",
        part: "pppp",
        proc: 1,
        numProc: 2,
        partIdx: 2,
        matID: 2
      );
      var proc2path2 = BuildMaterial(
        t,
        pal: 4,
        unique: "uuuu2",
        part: "pppp",
        proc: 2,
        numProc: 2,
        partIdx: 2,
        matID: 2
      );

      var j1 = new Job()
      {
        UniqueStr = "uuuu1",
        PartName = "pppp",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                ExpectedLoadTime = TimeSpan.FromMinutes(11),
                ExpectedUnloadTime = TimeSpan.FromMinutes(711),
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    StationGroup = "machinespec",
                    ExpectedCycleTime = TimeSpan.FromMinutes(33),
                    Stations = [],
                  }
                ),
              }
            ),
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                ExpectedLoadTime = TimeSpan.FromMinutes(21),
                ExpectedUnloadTime = TimeSpan.FromMinutes(721),
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    StationGroup = "machinespec",
                    ExpectedCycleTime = TimeSpan.FromMinutes(44),
                    Stations = [],
                  }
                ),
              }
            ),
          }
        ),
      };
      var j2 = new Job()
      {
        UniqueStr = "uuuu2",
        PartName = "pppp",
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Cycles = 0,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                ExpectedLoadTime = TimeSpan.FromMinutes(12),
                ExpectedUnloadTime = TimeSpan.FromMinutes(712),
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    StationGroup = "machinespec",
                    ExpectedCycleTime = TimeSpan.FromMinutes(33),
                    Stations = [],
                  }
                ),
              }
            ),
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                ExpectedLoadTime = TimeSpan.FromMinutes(22),
                ExpectedUnloadTime = TimeSpan.FromMinutes(722),
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    StationGroup = "machinespec",
                    ExpectedCycleTime = TimeSpan.FromMinutes(44),
                    Stations = [],
                  }
                ),
              }
            ),
          }
        ),
      };
      var newJobs = new NewJobs() { Jobs = ImmutableList.Create<Job>(j1, j2), ScheduleId = "activeTimeSch" };
      jobLog.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      LoadStart(proc1path1, offset: 0, load: 1);
      LoadStart(proc1path2, offset: 1, load: 2);

      LoadEnd(proc1path1, offset: 2, load: 1, elapMin: 2, activeMin: 11);
      ExpectPalletStart(t, pal: 2, offset: 2, mats: [proc1path1]);
      MovePallet(t, offset: 5, pal: 2, load: 1);

      LoadEnd(proc1path2, offset: 7, load: 2, elapMin: 6, activeMin: 12);
      ExpectPalletStart(t, pal: 4, offset: 7, mats: [proc1path2]);
      MovePallet(t, offset: 8, pal: 4, load: 2);

      MachStart(proc1path1, offset: 10, mach: 1);
      MachStart(proc1path2, offset: 11, mach: 2);

      MachEnd(proc1path1, offset: 20, mach: 1, elapMin: 10, activeMin: 33);
      MachEnd(proc1path2, offset: 22, mach: 2, elapMin: 11, activeMin: 33);

      UnloadStart(proc1path1, offset: 24, load: 1);
      LoadStart(proc2path1, offset: 24, load: 1);

      UnloadStart(proc1path2, offset: 27, load: 2);
      LoadStart(proc2path2, offset: 27, load: 2);

      UnloadEnd(proc1path1, offset: 28, load: 1, elapMin: 28 - 24, activeMin: 711, totalActiveMin: 21 + 711);
      LoadEnd(proc2path1, offset: 28, load: 1, elapMin: 28 - 24, activeMin: 21, totalActiveMin: 21 + 711);
      ExpectPalletCycle(t, pal: 2, offset: 28, elapMin: 28 - 2, mats: [proc1path1]);
      ExpectPalletStart(t, pal: 2, offset: 28, mats: [proc2path1]);
      MovePallet(t, offset: 29, pal: 2, load: 1);

      UnloadEnd(proc1path2, offset: 30, load: 2, elapMin: 30 - 27, activeMin: 712, totalActiveMin: 712 + 22);
      LoadEnd(proc2path2, offset: 30, load: 2, elapMin: 30 - 27, activeMin: 22, totalActiveMin: 712 + 22);
      ExpectPalletCycle(t, pal: 4, offset: 30, elapMin: 30 - 7, mats: [proc1path2]);
      ExpectPalletStart(t, pal: 4, offset: 30, mats: [proc2path2]);
      MovePallet(t, offset: 33, pal: 4, load: 2);

      MachStart(proc2path1, offset: 40, mach: 1);
      MachStart(proc2path2, offset: 41, mach: 2);

      MachEnd(proc2path1, offset: 50, mach: 1, elapMin: 10, activeMin: 44);
      MachEnd(proc2path2, offset: 52, mach: 2, elapMin: 11, activeMin: 44);

      UnloadStart(proc2path1, offset: 60, load: 2);
      UnloadEnd(proc2path1, offset: 61, load: 2, elapMin: 1, activeMin: 721);
      ExpectPalletCycle(t, pal: 2, offset: 61, elapMin: 61 - 28, mats: [proc2path1]);

      MovePallet(t, offset: 62, pal: 2, load: 2);

      UnloadStart(proc2path2, offset: 65, load: 1);
      UnloadEnd(proc2path2, offset: 67, load: 1, elapMin: 2, activeMin: 722);
      ExpectPalletCycle(t, pal: 4, offset: 67, elapMin: 67 - 30, mats: [proc2path2]);

      MovePallet(t, offset: 70, pal: 4, load: 1);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void LargeFixedQuantites()
    {
      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Cycles = 0,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) },
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "largeFixedSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", numProc: 2);

      var proc1 = BuildMaterial(
        t,
        pal: 1,
        unique: "uuuu",
        part: "pppp",
        proc: 1,
        numProc: 2,
        matIDs: new long[] { 1, 2, 3 }
      );
      var proc2 = BuildMaterial(
        t,
        pal: 1,
        unique: "uuuu",
        part: "pppp",
        proc: 2,
        numProc: 2,
        matIDs: new long[] { 1, 2, 3 }
      );
      var proc1snd = BuildMaterial(
        t,
        pal: 1,
        unique: "uuuu",
        part: "pppp",
        proc: 1,
        numProc: 2,
        matIDs: new long[] { 4, 5, 6 }
      );
      var proc2snd = BuildMaterial(
        t,
        pal: 1,
        unique: "uuuu",
        part: "pppp",
        proc: 2,
        numProc: 2,
        matIDs: new long[] { 4, 5, 6 }
      );
      var proc1thrd = BuildMaterial(
        t,
        pal: 1,
        unique: "uuuu",
        part: "pppp",
        proc: 1,
        numProc: 2,
        matIDs: new long[] { 7, 8, 9 }
      );

      LoadStart(proc1, offset: 0, load: 1);
      LoadEnd(proc1, offset: 5, load: 1, elapMin: 5);
      ExpectPalletStart(t, pal: 1, offset: 5, mats: proc1);
      MovePallet(t, pal: 1, offset: 6, load: 1);

      MachStart(proc1, offset: 10, mach: 5);
      MachEnd(proc1, offset: 15, mach: 5, elapMin: 5);

      UnloadStart(proc1, offset: 20, load: 2);
      LoadStart(proc2, offset: 20, load: 2);
      LoadStart(proc1snd, offset: 20, load: 2);

      UnloadEnd(proc1, offset: 24, load: 2, elapMin: 4, totalMatCnt: 3 * 3);
      LoadEnd(proc2, offset: 24, load: 2, elapMin: 4, totalMatCnt: 3 * 3);
      LoadEnd(proc1snd, offset: 24, load: 2, elapMin: 4, totalMatCnt: 3 * 3);
      ExpectPalletCycle(t, pal: 1, offset: 24, elapMin: 24 - 5, mats: proc1);
      ExpectPalletStart(t, pal: 1, offset: 24, mats: [.. proc2, .. proc1snd]);
      MovePallet(t, pal: 1, offset: 25, load: 2);

      MachStart(proc2, offset: 30, mach: 6);
      MachEnd(proc2, offset: 33, mach: 6, elapMin: 3);
      MachStart(proc1snd, offset: 35, mach: 6);
      MachEnd(proc1snd, offset: 37, mach: 6, elapMin: 2);

      UnloadStart(proc2, offset: 40, load: 1);
      UnloadStart(proc1snd, offset: 40, load: 1);
      LoadStart(proc2snd, offset: 40, load: 1);
      LoadStart(proc1thrd, offset: 40, load: 1);

      UnloadEnd(proc2, offset: 45, load: 1, elapMin: 5, totalMatCnt: 4 * 3);
      UnloadEnd(proc1snd, offset: 45, load: 1, elapMin: 5, totalMatCnt: 4 * 3);
      LoadEnd(proc2snd, offset: 45, load: 1, elapMin: 5, totalMatCnt: 4 * 3);
      LoadEnd(proc1thrd, offset: 45, load: 1, elapMin: 5, totalMatCnt: 4 * 3);
      ExpectPalletCycle(t, pal: 1, offset: 45, elapMin: 45 - 24, mats: [.. proc2, .. proc1snd]);
      ExpectPalletStart(t, pal: 1, offset: 45, mats: [.. proc2snd, .. proc1thrd]);
      MovePallet(t, pal: 1, offset: 50, load: 1);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void ProvisionalWorkorder()
    {
      var j = new Job()
      {
        UniqueStr = "uniqqq",
        PartName = "pppart",
        Processes =
        [
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) },
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) },
        ],
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        ProvisionalWorkorderId = "provWork",
      };
      jobLog.AddJobs(new NewJobs() { Jobs = [j], ScheduleId = "schId" }, null, addAsCopiedToSystem: true);

      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "uniqqq", part: "pppart", numProc: 2);

      var p1 = BuildMaterial(
        t,
        pal: 3,
        unique: "uniqqq",
        part: "pppart",
        proc: 1,
        numProc: 2,
        matID: 1,
        workorder: "provWork"
      );

      LoadStart(p1, offset: 0, load: 1);
      LoadEnd(p1, offset: 4, load: 1, elapMin: 4, expectWork: "provWork");
      ExpectPalletStart(t, pal: 3, offset: 4, mats: [p1]);
      MovePallet(t, offset: 4, load: 1, pal: 3);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void SkipShortMachineCycle()
    {
      var j = new Job()
      {
        UniqueStr = "unique",
        PartName = "part1",
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Cycles = 0,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "skipMachSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", numProc: 1);

      var p1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, matID: 1);

      LoadStart(p1, offset: 0, load: 1);
      LoadEnd(p1, offset: 2, load: 1, elapMin: 2);
      ExpectPalletStart(t, pal: 3, offset: 2, mats: [p1]);
      MovePallet(t, offset: 2, load: 1, pal: 3);

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
      HandleEvent(bad);
      // don't add to expected, since it should be skipped

      MachStart(p1, offset: 15, mach: 3);
      MachEnd(p1, offset: 22, mach: 3, elapMin: 7);

      UnloadStart(p1, offset: 30, load: 1);
      UnloadEnd(p1, offset: 33, load: 1, elapMin: 3);
      ExpectPalletCycle(t, pal: 3, offset: 33, elapMin: 33 - 2, mats: [p1]);
      MovePallet(t, offset: 33, load: 1, pal: 3);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void Remachining()
    {
      var j = new Job()
      {
        UniqueStr = "unique",
        PartName = "part1",
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Cycles = 0,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "remachSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", numProc: 1);

      var p1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, matID: 1);
      var p2 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, matID: 2);

      LoadStart(p1, offset: 0, load: 5);
      LoadEnd(p1, offset: 2, load: 5, elapMin: 2);
      ExpectPalletStart(t, pal: 3, offset: 2, mats: [p1]);
      MovePallet(t, offset: 3, load: 1, pal: 3);

      MachStart(p1, offset: 4, mach: 2);
      MachEnd(p1, offset: 20, mach: 2, elapMin: 16);

      UnloadStart(p1, offset: 22, load: 1);
      LoadStart(p2, offset: 23, load: 1);
      //No unload or load ends since this is a remachining
      MovePallet(t, offset: 26, load: 1, pal: 3);

      MachStart(p1, offset: 30, mach: 1);
      MachEnd(p1, offset: 43, mach: 1, elapMin: 13);

      UnloadStart(p1, offset: 45, load: 2);
      LoadStart(p2, offset: 45, load: 2);
      UnloadEnd(p1, offset: 47, load: 2, elapMin: 2, totalMatCnt: 2);
      LoadEnd(p2, offset: 47, load: 2, elapMin: 2, totalMatCnt: 2);
      ExpectPalletCycle(t, pal: 3, offset: 47, elapMin: 47 - 2, mats: [p1]);
      ExpectPalletStart(t, pal: 3, offset: 47, mats: [p2]);
      MovePallet(t, offset: 48, load: 2, pal: 3);

      MachStart(p2, offset: 50, mach: 1);
      MachEnd(p2, offset: 57, mach: 1, elapMin: 7);

      UnloadStart(p2, offset: 60, load: 1);
      UnloadEnd(p2, offset: 66, load: 1, elapMin: 6);
      ExpectPalletCycle(t, pal: 3, offset: 66, elapMin: 66 - 47, mats: [p2]);
      MovePallet(t, offset: 66, load: 1, pal: 3);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void Inspections()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", numProc: 2);

      var proc1 = BuildMaterial(t, pal: 2, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 1);
      var proc2 = BuildMaterial(t, pal: 2, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, matID: 1);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Cycles = 0,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                Inspections =
                [
                  new PathInspection()
                  {
                    InspectionType = "insp_proc1",
                    Counter = "counter1",
                    MaxVal = 10,
                    RandomFreq = 0,
                    TimeInterval = TimeSpan.FromMinutes(1000),
                  },
                ],
              }
            ),
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                Inspections =
                [
                  new PathInspection()
                  {
                    InspectionType = "insp_whole",
                    Counter = "counter2",
                    MaxVal = 15,
                    RandomFreq = 0,
                    TimeInterval = TimeSpan.FromMinutes(1500),
                  },
                ],
              }
            ),
          }
        ),
      };
      var newJobs = new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "schinspections" };
      jobLog.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      LoadStart(proc1, offset: 0, load: 6);
      LoadEnd(proc1, offset: 2, load: 6, elapMin: 2);
      ExpectPalletStart(t, pal: 2, offset: 2, mats: [proc1]);
      MovePallet(t, offset: 5, pal: 2, load: 1);

      MachStart(proc1, offset: 10, mach: 4);

      MachEnd(proc1, offset: 20, mach: 4, elapMin: 10);
      ExpectInspection(
        proc1,
        inspTy: "insp_proc1",
        counter: "counter1",
        offset: 20,
        result: false,
        path: new[]
        {
          new MaterialProcessActualPath()
          {
            MaterialID = proc1.MaterialID,
            Process = 1,
            Pallet = 2,
            LoadStation = 6,
            Stops = ImmutableList.Create(
              new MaterialProcessActualPath.Stop() { StationName = "machinespec", StationNum = 4 }
            ),
            UnloadStation = -1,
          },
        }
      );

      UnloadStart(proc1, offset: 24, load: 1);
      LoadStart(proc2, offset: 24, load: 1);

      UnloadEnd(proc1, offset: 28, load: 1, elapMin: 28 - 24, totalMatCnt: 2);
      LoadEnd(proc2, offset: 28, load: 1, elapMin: 28 - 24, totalMatCnt: 2);
      ExpectPalletCycle(t, pal: 2, offset: 28, elapMin: 28 - 2, mats: [proc1]);
      ExpectPalletStart(t, pal: 2, offset: 28, mats: [proc2]);
      MovePallet(t, offset: 29, pal: 2, load: 1);

      MachStart(proc2, offset: 40, mach: 7);
      MachEnd(proc2, offset: 50, mach: 7, elapMin: 10);
      ExpectInspection(
        proc2,
        inspTy: "insp_whole",
        counter: "counter2",
        result: false,
        offset: 50,
        path: new[]
        {
          new MaterialProcessActualPath()
          {
            MaterialID = proc1.MaterialID,
            Process = 1,
            Pallet = 2,
            LoadStation = 6,
            Stops = ImmutableList.Create(
              new MaterialProcessActualPath.Stop() { StationName = "machinespec", StationNum = 4 }
            ),
            UnloadStation = 1,
          },
          new MaterialProcessActualPath()
          {
            MaterialID = proc1.MaterialID,
            Process = 2,
            Pallet = 2,
            LoadStation = 1,
            Stops = ImmutableList.Create(
              new MaterialProcessActualPath.Stop() { StationName = "machinespec", StationNum = 7 }
            ),
            UnloadStation = -1,
          },
        }
      );

      UnloadStart(proc2, offset: 60, load: 2);
      UnloadEnd(proc2, offset: 61, load: 2, elapMin: 1);
      ExpectPalletCycle(t, pal: 2, offset: 61, elapMin: 61 - 28, mats: [proc2]);
      MovePallet(t, offset: 62, pal: 2, load: 2);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void TranslatesProgramsFromJob()
    {
      var j1 = new Job()
      {
        UniqueStr = "unique1",
        PartName = "part1",
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Cycles = 0,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    Program = "the-log-prog",
                    ProgramRevision = 15,
                    StationGroup = "",
                    Stations = [],
                    ExpectedCycleTime = TimeSpan.Zero,
                  }
                ),
              }
            ),
          }
        ),
      };
      var j2 = new Job()
      {
        UniqueStr = "unique2",
        PartName = "part1",
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Cycles = 0,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                Stops = ImmutableList.Create(
                  new MachiningStop()
                  {
                    Program = "other-log-prog",
                    ProgramRevision = 12,
                    StationGroup = "",
                    Stations = [],
                    ExpectedCycleTime = TimeSpan.Zero,
                  }
                ),
              }
            ),
          }
        ),
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j1, j2), ScheduleId = "progsSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);

      var schIdPath1 = AddTestPart(unique: "unique1", part: "part1", numProc: 1);
      var schIdPath2 = AddTestPart(unique: "unique2", part: "part1", numProc: 1);
      AddTestPartPrograms(schIdPath1, insightPart: true, new[] { "is-ignored" });
      AddTestPartPrograms(schIdPath2, insightPart: true, new[] { "is-ignored2" });

      var path1 = BuildMaterial(
        t,
        pal: 3,
        unique: "unique1",
        part: "part1",
        proc: 1,
        partIdx: 1,
        numProc: 1,
        matID: 1
      );

      LoadStart(path1, offset: 0, load: 5);
      LoadEnd(path1, offset: 2, load: 5, elapMin: 2);
      ExpectPalletStart(t, pal: 3, offset: 2, mats: [path1]);
      MovePallet(t, offset: 3, load: 1, pal: 3);

      MachStart(path1, offset: 4, mach: 2, mazakProg: "the-mazak-prog", logProg: "the-log-prog", progRev: 15);
      MachEnd(
        path1,
        offset: 20,
        mach: 2,
        elapMin: 16,
        mazakProg: "the-mazak-prog",
        logProg: "the-log-prog",
        progRev: 15
      );

      var path2 = BuildMaterial(
        t,
        pal: 4,
        unique: "unique2",
        part: "part1",
        proc: 1,
        partIdx: 2,
        numProc: 1,
        matID: 2
      );

      LoadStart(path2, offset: 100, load: 5);
      LoadEnd(path2, offset: 102, load: 5, elapMin: 2);
      ExpectPalletStart(t, pal: 4, offset: 102, mats: [path2]);
      MovePallet(t, offset: 103, load: 1, pal: 4);

      MachStart(
        path2,
        offset: 104,
        mach: 2,
        mazakProg: "the-mazak-prog2",
        logProg: "other-log-prog",
        progRev: 12
      );
      MachEnd(
        path2,
        offset: 120,
        mach: 2,
        elapMin: 16,
        mazakProg: "the-mazak-prog2",
        logProg: "other-log-prog",
        progRev: 12
      );

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void TranslatesProgramsFromMazakDb()
    {
      var j = new Job()
      {
        UniqueStr = "unique",
        PartName = "part1",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) },
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "translatesProgsSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);

      var schId = AddTestPart(unique: "unique", part: "part1", numProc: 2);
      AddTestPartPrograms(schId, insightPart: false, new[] { "the-log-prog", "the-log-prog-proc2" });

      var p = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 2, matID: 1);

      LoadStart(p, offset: 0, load: 5);
      LoadEnd(p, offset: 2, load: 5, elapMin: 2);
      ExpectPalletStart(t, pal: 3, offset: 2, mats: [p]);
      MovePallet(t, offset: 3, load: 1, pal: 3);

      MachStart(p, offset: 4, mach: 2, mazakProg: "the-mazak-prog", logProg: "the-log-prog");
      MachEnd(p, offset: 20, mach: 2, elapMin: 16, mazakProg: "the-mazak-prog", logProg: "the-log-prog");

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void Queues()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", numProc: 2);

      var proc1 = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 1);
      var proc1snd = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 2);

      var proc2 = BuildMaterial(t, pal: 9, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, matID: 1);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(JobLogTest.EmptyPath with { OutputQueue = "thequeue" }),
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(JobLogTest.EmptyPath with { InputQueue = "thequeue" }),
          }
        ),
      };
      var newJobs = new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "queueSch" };
      jobLog.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      LoadStart(proc1, offset: 0, load: 1);
      LoadEnd(proc1, offset: 5, load: 1, elapMin: 5);
      ExpectPalletStart(t, pal: 8, offset: 5, mats: [proc1]);
      MovePallet(t, pal: 8, offset: 6, load: 1);

      MachStart(proc1, offset: 10, mach: 5);
      MachEnd(proc1, offset: 15, mach: 5, elapMin: 5);

      UnloadStart(proc1, offset: 20, load: 2);
      LoadStart(proc1snd, offset: 20, load: 2);
      UnloadEnd(proc1, offset: 24, load: 2, elapMin: 4, totalMatCnt: 2);
      ExpectAddToQueue(proc1, offset: 24, queue: "thequeue", pos: 0);
      LoadEnd(proc1snd, offset: 24, load: 2, elapMin: 4, totalMatCnt: 2);
      ExpectPalletCycle(t, pal: 8, offset: 24, elapMin: 24 - 5, mats: [proc1]);
      ExpectPalletStart(t, pal: 8, offset: 24, mats: [proc1snd]);
      MovePallet(t, pal: 8, offset: 25, load: 2);

      LoadStart(proc2, offset: 28, load: 1);
      LoadEnd(proc2, offset: 29, load: 1, elapMin: 1);
      ExpectPalletStart(t, pal: 9, offset: 29, mats: [proc2]);
      MovePallet(t, pal: 9, offset: 30, load: 1);
      ExpectRemoveFromQueue(
        proc1,
        offset: 29,
        queue: "thequeue",
        startingPos: 0,
        reason: "LoadedToPallet",
        elapMin: 29 - 24
      );

      MachStart(proc2, offset: 30, mach: 6);
      MachStart(proc1snd, offset: 35, mach: 3);
      MachEnd(proc1snd, offset: 37, mach: 3, elapMin: 2);
      MachEnd(proc2, offset: 39, mach: 6, elapMin: 9);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public async Task QueuesFirstInFirstOut()
    {
      // run multiple process 1s on multiple paths.  Also have multiple parts on a face.
      using var server = WireMockServer.Start();
      settings.ExternalQueues["externalq"] = server.Url;

      server
        .Given(Request.Create().WithPath(path => path.StartsWith("/api/v1/jobs/casting/")))
        .RespondWith(Response.Create().WithStatusCode(200));

      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu1", part: "pppp", numProc: 2);
      AddTestPart(unique: "uuuu2", part: "pppp", numProc: 2);

      var proc1path1 = BuildMaterial(
        t,
        pal: 4,
        unique: "uuuu1",
        part: "pppp",
        proc: 1,
        numProc: 2,
        partIdx: 1,
        matIDs: new long[] { 1, 2, 3 }
      );
      var proc2path1 = BuildMaterial(
        t,
        pal: 5,
        unique: "uuuu1",
        part: "pppp",
        proc: 2,
        numProc: 2,
        partIdx: 1,
        matIDs: new long[] { 1, 2, 3 }
      );

      var proc1path2 = BuildMaterial(
        t,
        pal: 6,
        unique: "uuuu2",
        part: "pppp",
        proc: 1,
        numProc: 2,
        partIdx: 2,
        matIDs: new long[] { 4, 5, 6 }
      );
      var proc2path2 = BuildMaterial(
        t,
        pal: 7,
        unique: "uuuu2",
        part: "pppp",
        proc: 2,
        numProc: 2,
        partIdx: 2,
        matIDs: new long[] { 4, 5, 6 }
      );

      var proc1path1snd = BuildMaterial(
        t,
        pal: 4,
        unique: "uuuu1",
        part: "pppp",
        proc: 1,
        numProc: 2,
        partIdx: 1,
        matIDs: new long[] { 7, 8, 9 }
      );
      var proc2path1snd = BuildMaterial(
        t,
        pal: 5,
        unique: "uuuu1",
        part: "pppp",
        proc: 2,
        numProc: 2,
        partIdx: 1,
        matIDs: new long[] { 7, 8, 9 }
      );

      var proc1path1thrd = BuildMaterial(
        t,
        pal: 4,
        unique: "uuuu1",
        part: "pppp",
        proc: 1,
        numProc: 2,
        partIdx: 1,
        matIDs: new long[] { 10, 11, 12 }
      );

      var j1 = new Job()
      {
        UniqueStr = "uuuu1",
        PartName = "pppp",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(JobLogTest.EmptyPath with { OutputQueue = "thequeue" }),
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                InputQueue = "thequeue",
                OutputQueue = "externalq",
              }
            ),
          }
        ),
      };
      var j2 = new Job()
      {
        UniqueStr = "uuuu2",
        PartName = "pppp",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(JobLogTest.EmptyPath with { OutputQueue = "thequeue" }),
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                InputQueue = "thequeue",
                OutputQueue = "externalq",
              }
            ),
          }
        ),
      };
      var newJobs = new NewJobs() { Jobs = ImmutableList.Create<Job>(j1, j2), ScheduleId = "queueSch" };
      jobLog.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      LoadStart(proc1path1, offset: 0, load: 10);
      LoadEnd(proc1path1, offset: 2, load: 10, elapMin: 2);
      ExpectPalletStart(t, pal: 4, offset: 2, mats: proc1path1);
      MovePallet(t, offset: 3, pal: 4, load: 10);

      MachStart(proc1path1, offset: 5, mach: 7);
      MachEnd(proc1path1, offset: 10, mach: 7, elapMin: 5);

      LoadStart(proc1path2, offset: 10, load: 9);
      LoadEnd(proc1path2, offset: 11, load: 9, elapMin: 1);
      ExpectPalletStart(t, pal: 6, offset: 11, mats: proc1path2);
      MovePallet(t, offset: 11, pal: 6, load: 9);

      UnloadStart(proc1path1, offset: 12, load: 3);
      LoadStart(proc1path1snd, offset: 12, load: 3);
      UnloadEnd(proc1path1, offset: 15, load: 3, elapMin: 3, totalMatCnt: 6);
      ExpectAddToQueue(proc1path1, offset: 15, queue: "thequeue", startPos: 0);
      LoadEnd(proc1path1snd, offset: 15, load: 3, elapMin: 3, totalMatCnt: 6);
      ExpectPalletCycle(t, pal: 4, offset: 15, elapMin: 15 - 2, mats: proc1path1);
      ExpectPalletStart(t, pal: 4, offset: 15, mats: proc1path1snd);
      MovePallet(t, offset: 16, pal: 4, load: 3);

      MachStart(proc1path2, offset: 18, mach: 1);
      MachEnd(proc1path2, offset: 19, mach: 1, elapMin: 1);

      MachStart(proc1path1snd, offset: 20, mach: 2);
      MachEnd(proc1path1snd, offset: 25, mach: 2, elapMin: 5);

      UnloadStart(proc1path2, offset: 26, load: 3);
      UnloadEnd(proc1path2, offset: 27, load: 3, elapMin: 1);
      ExpectPalletCycle(t, pal: 6, offset: 27, elapMin: 27 - 11, mats: proc1path2);
      ExpectAddToQueue(proc1path2, offset: 27, queue: "thequeue", startPos: 3);

      UnloadStart(proc1path1snd, offset: 30, load: 1);
      LoadStart(proc1path1thrd, offset: 30, load: 1);
      UnloadEnd(proc1path1snd, offset: 33, load: 1, elapMin: 3, totalMatCnt: 6);
      ExpectAddToQueue(proc1path1snd, offset: 33, queue: "thequeue", startPos: 6);
      LoadEnd(proc1path1thrd, offset: 33, load: 1, elapMin: 3, totalMatCnt: 6);
      ExpectPalletCycle(t, pal: 4, offset: 33, elapMin: 33 - 15, mats: proc1path1snd);
      ExpectPalletStart(t, pal: 4, offset: 33, mats: proc1path1thrd);
      MovePallet(t, offset: 34, pal: 4, load: 1);

      //queue now has 9 elements
      jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          Enumerable
            .Range(1, 9)
            .Select(
              (i, idx) =>
                new QueuedMaterial()
                {
                  MaterialID = i,
                  Queue = "thequeue",
                  Position = idx,
                  Unique = proc1path2.Concat(proc2path2).Any(m => m.MaterialID == i) ? "uuuu2" : "uuuu1",
                  PartNameOrCasting = "pppp",
                  NumProcesses = 2,
                  NextProcess = 2,
                  Serial = SerialSettings.ConvertToBase62(i).PadLeft(10, '0'),
                  Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1),
                  AddTimeUTC = t.AddMinutes(
                    i <= 3 ? 15
                    : i <= 6 ? 27
                    : 33
                  ),
                }
            )
            .ToList()
        );

      //first load should pull in mat ids 1, 2, 3

      LoadStart(proc2path1, offset: 40, load: 2);
      LoadEnd(proc2path1, offset: 44, load: 2, elapMin: 4);
      ExpectPalletStart(t, pal: 5, offset: 44, mats: proc2path1);
      MovePallet(t, offset: 45, pal: 5, load: 2);
      ExpectRemoveFromQueue(
        proc1path1,
        offset: 44,
        queue: "thequeue",
        startingPos: 0,
        reason: "LoadedToPallet",
        elapMin: 44 - 15
      );

      jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          (new[] { 4, 5, 6, 7, 8, 9 })
            .Select(
              (i, idx) =>
                new QueuedMaterial()
                {
                  MaterialID = i,
                  Queue = "thequeue",
                  Position = idx,
                  Unique = proc1path2.Concat(proc2path2).Any(m => m.MaterialID == i) ? "uuuu2" : "uuuu1",
                  PartNameOrCasting = "pppp",
                  NumProcesses = 2,
                  NextProcess = 2,
                  Serial = SerialSettings.ConvertToBase62(i).PadLeft(10, '0'),
                  Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1),
                  AddTimeUTC = t.AddMinutes(i <= 6 ? 27 : 33),
                }
            )
            .ToList()
        );

      MachStart(proc2path1, offset: 50, mach: 100);
      MachEnd(proc2path1, offset: 55, mach: 100, elapMin: 5);

      // second load should pull mat ids 7, 8, 9 because of path matching

      UnloadStart(proc2path1, offset: 60, load: 1);
      LoadStart(proc2path1snd, offset: 60, load: 1);
      UnloadEnd(proc2path1, offset: 65, load: 1, elapMin: 5, totalMatCnt: 6);
      LoadEnd(proc2path1snd, offset: 65, load: 1, elapMin: 5, totalMatCnt: 6);
      ExpectPalletCycle(t, pal: 5, offset: 65, elapMin: 65 - 44, mats: proc2path1);
      ExpectPalletStart(t, pal: 5, offset: 65, mats: proc2path1snd);
      MovePallet(t, offset: 66, pal: 5, load: 1);
      ExpectRemoveFromQueue(
        proc1path1snd,
        offset: 65,
        queue: "thequeue",
        startingPos: 3,
        reason: "LoadedToPallet",
        elapMin: 65 - 33
      );

      jobLog
        .GetMaterialInAllQueues()
        .ShouldBeEquivalentTo(
          (new[] { 4, 5, 6 })
            .Select(
              (i, idx) =>
                new QueuedMaterial()
                {
                  MaterialID = i,
                  Queue = "thequeue",
                  Position = idx,
                  Unique = proc1path2.Concat(proc2path2).Any(m => m.MaterialID == i) ? "uuuu2" : "uuuu1",
                  PartNameOrCasting = "pppp",
                  NumProcesses = 2,
                  NextProcess = 2,
                  Serial = SerialSettings.ConvertToBase62(i).PadLeft(10, '0'),
                  Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1),
                  AddTimeUTC = t.AddMinutes(27),
                }
            )
            .ToList()
        );

      // finally, load of path2 pulls remaining 4, 5, 6
      LoadStart(proc2path2, offset: 70, load: 2);
      LoadEnd(proc2path2, offset: 73, load: 2, elapMin: 3);
      ExpectPalletStart(t, pal: 7, offset: 73, mats: proc2path2);
      MovePallet(t, offset: 74, pal: 7, load: 2);
      ExpectRemoveFromQueue(
        proc1path2,
        offset: 73,
        queue: "thequeue",
        startingPos: 0,
        reason: "LoadedToPallet",
        elapMin: 73 - 27
      );

      jobLog.GetMaterialInAllQueues().ShouldBeEmpty();

      var details = jobLog.GetMaterialDetails(proc1path2.First().MaterialID);

      CheckExpected(t.AddHours(-1), t.AddHours(10));

      // The sends to external queues happen on a new thread so need to wait
      int numWaits = 0;
      while (server.LogEntries.Count() < 3 && numWaits < 30)
      {
        await Task.Delay(100, TestContext.Current.CancellationToken);
        numWaits++;
      }

      server.LogEntries.Count.ShouldBe(3);
      foreach (var e in server.LogEntries)
      {
        e.RequestMessage.Path.ShouldBe("/api/v1/jobs/casting/pppp");
        e.RequestMessage.Query.ShouldHaveSingleItem();
        e.RequestMessage.Query["queue"].ShouldBe(["externalq"]);
      }
      server
        .LogEntries.Select(e => e.RequestMessage.Body)
        .ShouldBe(["[\"0000000001\"]", "[\"0000000002\"]", "[\"0000000003\"]"]);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void QueuesSignalQuarantine(bool signalDuringUnload)
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", numProc: 2);

      var proc1 = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 1);
      var proc1snd = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 2);

      var proc2 = BuildMaterial(t, pal: 9, unique: "uuuu", part: "pppp", proc: 2, numProc: 2, matID: 1);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(JobLogTest.EmptyPath with { OutputQueue = "thequeue" }),
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(JobLogTest.EmptyPath with { InputQueue = "thequeue" }),
          }
        ),
      };
      var newJobs = new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "queueSch" };
      jobLog.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      LoadStart(proc1, offset: 0, load: 1);
      LoadEnd(proc1, offset: 5, load: 1, elapMin: 5);
      ExpectPalletStart(t, pal: 8, offset: 5, mats: [proc1]);
      MovePallet(t, pal: 8, offset: 6, load: 1);

      MachStart(proc1, offset: 10, mach: 5);
      if (!signalDuringUnload)
      {
        SignalForQuarantine(proc1, offset: 12, queue: "QuarantineQ");
      }
      MachEnd(proc1, offset: 15, mach: 5, elapMin: 5);

      UnloadStart(proc1, offset: 20, load: 2);
      LoadStart(proc1snd, offset: 20, load: 2);

      if (signalDuringUnload)
      {
        SignalForQuarantine(proc1, offset: 22, queue: "QuarantineQ");
      }

      UnloadEnd(proc1, offset: 24, load: 2, elapMin: 4, totalMatCnt: 2);
      ExpectAddToQueue(proc1, offset: 24, queue: "QuarantineQ", pos: 0);
      LoadEnd(proc1snd, offset: 24, load: 2, elapMin: 4, totalMatCnt: 2);
      ExpectPalletCycle(t, pal: 8, offset: 24, elapMin: 24 - 5, mats: [proc1]);
      ExpectPalletStart(t, pal: 8, offset: 24, mats: [proc1snd]);
      MovePallet(t, pal: 8, offset: 25, load: 2);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void LoadsAssignedRawMaterial()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", numProc: 2);

      var casting = BuildMaterial(t, pal: 8, unique: "", part: "xxxx", proc: 1, numProc: 1, matID: 1);
      var mat1 = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 2);
      var mat2 = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 3);
      var mat3 = BuildMaterial(t, pal: 4, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 4);
      AddMaterialToQueue(casting, proc: 0, queue: "rawmat", offset: 0, allocate: AllocateTy.Casting);
      AddMaterialToQueue(mat1, proc: 0, queue: "rawmat", offset: 1, allocate: AllocateTy.Assigned);
      AddMaterialToQueue(mat2, proc: 0, queue: "rawmat", offset: 2, allocate: AllocateTy.Assigned);
      AddMaterialToQueue(mat3, proc: 0, queue: "rawmat", offset: 3, allocate: AllocateTy.Assigned);

      // Add job with 2 fix qty on process 1
      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              JobLogTest.EmptyPath with
              {
                PartsPerPallet = 2,
                InputQueue = "rawmat",
                OutputQueue = "thequeue",
              },
            ],
          },
          new ProcessInfo() { Paths = [JobLogTest.EmptyPath with { InputQueue = "thequeue" }] },
        ],
      };
      var newJobs = new NewJobs() { Jobs = ImmutableList.Create<Job>(j), ScheduleId = "swapSch" };
      jobLog.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      CheckMatInQueue("rawmat", new[] { casting, mat1, mat2, mat3 });

      // load of pallet 8 should take both mat1 and mat2
      LoadStart([mat1, mat2], offset: 4, load: 1);
      LoadEnd([mat1, mat2], offset: 5, load: 1, elapMin: 1, expectMark: false);
      ExpectPalletStart(t, pal: 8, offset: 5, mats: [mat1, mat2]);
      MovePallet(t, pal: 8, offset: 6, load: 1);
      ExpectRemoveFromQueue(
        AdjProcess(mat1, 0),
        offset: 5,
        queue: "rawmat",
        startingPos: 1,
        reason: "LoadedToPallet",
        elapMin: 5 - 1
      );
      ExpectRemoveFromQueue(
        AdjProcess(mat2, 0),
        offset: 5,
        queue: "rawmat",
        startingPos: 1,
        reason: "LoadedToPallet",
        elapMin: 5 - 2
      );

      CheckMatInQueue("rawmat", new[] { casting, mat3 });
      CheckExpected(t.AddHours(-1), t.AddHours(10));

      // now a load of pallet 4, should take mat3 and create a new piece of material
      var newMat = BuildMaterial(t, pal: 4, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 5);

      LoadStart([mat3, newMat], offset: 8, load: 1);
      LoadEnd(
        [mat3, newMat],
        offset: 10,
        load: 1,
        elapMin: 2,
        // newMat should still have a mark at this time
        matsToSkipMark: new HashSet<long>([mat3.MaterialID])
      );
      ExpectPalletStart(t, pal: 4, offset: 10, mats: [mat3, newMat]);
      MovePallet(t, pal: 4, offset: 11, load: 1);
      ExpectRemoveFromQueue(
        AdjProcess(mat3, 0),
        offset: 10,
        queue: "rawmat",
        startingPos: 1,
        reason: "LoadedToPallet",
        elapMin: 10 - 3
      );

      CheckMatInQueue("rawmat", new[] { casting });
      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void AssignsMaterialFromQueueDuringLoad()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", numProc: 2);

      // one assigned, two castings
      var mat1 = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 1);
      var mat2 = BuildMaterial(t, pal: 8, unique: "", part: "cccc", proc: 1, numProc: 1, matID: 2);
      var mat3 = BuildMaterial(t, pal: 8, unique: "", part: "cccc", proc: 1, numProc: 1, matID: 3);
      AddMaterialToQueue(mat1, proc: 0, queue: "rawmat", offset: 1, allocate: AllocateTy.Assigned);
      AddMaterialToQueue(mat2, proc: 0, queue: "rawmat", offset: 2, allocate: AllocateTy.Casting);
      AddMaterialToQueue(mat3, proc: 0, queue: "rawmat", offset: 3, allocate: AllocateTy.Casting);

      // Add job with 3 fix qty on process 1
      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              JobLogTest.EmptyPath with
              {
                PartsPerPallet = 3,
                Casting = "cccc",
                InputQueue = "rawmat",
                OutputQueue = "thequeue",
              },
            ],
          },
          new ProcessInfo() { Paths = [JobLogTest.EmptyPath with { InputQueue = "thequeue" }] },
        ],
      };
      var newJobs = new NewJobs() { Jobs = [j], ScheduleId = "swapSch" };
      jobLog.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      CheckMatInQueue("rawmat", [mat1, mat2, mat3]);

      // load of pallet 8 should take them all
      LoadStart([mat1, mat2, mat3], offset: 4, load: 1);
      LoadEnd([mat1, mat2, mat3], offset: 5, load: 1, elapMin: 1, expectMark: false);
      ExpectPalletStart(t, pal: 8, offset: 5, mats: [mat1, mat2, mat3]);
      MovePallet(t, pal: 8, offset: 6, load: 1);
      ExpectRemoveFromQueue(
        AdjProcess(mat1, 0),
        offset: 5,
        queue: "rawmat",
        startingPos: 0,
        reason: "LoadedToPallet",
        elapMin: 5 - 1
      );
      ExpectRemoveFromQueue(
        AdjProcess(mat2, 0),
        offset: 5,
        queue: "rawmat",
        startingPos: 0,
        reason: "LoadedToPallet",
        elapMin: 5 - 2
      );
      ExpectRemoveFromQueue(
        AdjProcess(mat3, 0),
        offset: 5,
        queue: "rawmat",
        startingPos: 0,
        reason: "LoadedToPallet",
        elapMin: 5 - 3
      );

      CheckMatInQueue("rawmat", []);

      AdjustLogMaterial(mat2, m => m with { JobUniqueStr = "uuuu", PartName = "pppp", NumProcesses = 2 });
      AdjustLogMaterial(mat3, m => m with { JobUniqueStr = "uuuu", PartName = "pppp", NumProcesses = 2 });

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void SwapsRawMaterial()
    {
      var t = DateTime.UtcNow.AddHours(-5);
      AddTestPart(unique: "uuuu", part: "pppp", numProc: 2);

      var mat1 = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 1);
      var mat2 = BuildMaterial(t, pal: 8, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 2);
      var mat3 = BuildMaterial(t, pal: 4, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 3);
      var mat4 = BuildMaterial(t, pal: 4, unique: "uuuu", part: "pppp", proc: 1, numProc: 2, matID: 4);

      var j = new Job()
      {
        UniqueStr = "uuuu",
        PartName = "pppp",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(
              JobLogTest.EmptyPath with
              {
                InputQueue = "rawmat",
                OutputQueue = "thequeue",
              }
            ),
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(JobLogTest.EmptyPath with { InputQueue = "thequeue" }),
          }
        ),
      };
      var newJobs = new NewJobs() { Jobs = ImmutableList.Create<Job>(j), ScheduleId = "swapSch" };
      jobLog.AddJobs(newJobs, null, addAsCopiedToSystem: true);

      AddMaterialToQueue(mat1, proc: 0, queue: "rawmat", offset: 0, allocate: AllocateTy.Assigned);
      AddMaterialToQueue(mat2, proc: 0, queue: "rawmat", offset: 1, allocate: AllocateTy.Assigned);
      AddMaterialToQueue(mat3, proc: 0, queue: "rawmat", offset: 2, allocate: AllocateTy.Assigned);
      AddMaterialToQueue(mat4, proc: 0, queue: "rawmat", offset: 3, allocate: AllocateTy.Casting);

      LoadStart(mat1, offset: 4, load: 1);
      LoadEnd(mat1, offset: 5, load: 1, elapMin: 5 - 4, expectMark: false);
      ExpectPalletStart(t, pal: 8, offset: 5, mats: [mat1]);
      MovePallet(t, pal: 8, offset: 6, load: 1);
      ExpectRemoveFromQueue(
        AdjProcess(mat1, 0),
        offset: 5,
        queue: "rawmat",
        startingPos: 0,
        reason: "LoadedToPallet",
        elapMin: 5 - 0
      );

      StockerStart(mat1, offset: 8, stocker: 8, waitForMachine: true);

      CheckMatInQueue("rawmat", new[] { mat2, mat3, AdjUnique(mat4, "", 1) });

      SwapMaterial(mat1, mat2, offset: 10, unassigned: false);

      CheckMatInQueueIgnoreAddTime(
        "rawmat",
        new List<QueuedMaterial>
        {
          new QueuedMaterial()
          {
            MaterialID = mat3.MaterialID,
            Queue = "rawmat",
            Position = 0,
            Unique = mat3.Unique,
            NextProcess = 1,
            Serial = SerialSettings.ConvertToBase62(mat3.MaterialID).PadLeft(10, '0'),
            Paths = ImmutableDictionary<int, int>.Empty,
            PartNameOrCasting = mat3.JobPartName,
            NumProcesses = mat3.NumProcess,
          },
          new QueuedMaterial()
          {
            MaterialID = mat4.MaterialID,
            Queue = "rawmat",
            Position = 1,
            Unique = "", // unique is cleared
            NextProcess = 1,
            Serial = SerialSettings.ConvertToBase62(mat4.MaterialID).PadLeft(10, '0'),
            Paths = ImmutableDictionary<int, int>.Empty,
            PartNameOrCasting = mat4.JobPartName,
            NumProcesses = 1, // num processes is reset
          },
          new QueuedMaterial()
          {
            MaterialID = mat1.MaterialID,
            Queue = "rawmat",
            Position = 2,
            Unique = mat1.Unique,
            NextProcess = 1,
            Serial = SerialSettings.ConvertToBase62(mat1.MaterialID).PadLeft(10, '0'),
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1),
            PartNameOrCasting = mat1.JobPartName,
            NumProcesses = mat1.NumProcess,
          },
        }
      );

      // continue with mat2
      MachStart(mat2, offset: 15, mach: 3);
      MachEnd(mat2, offset: 20, mach: 3, elapMin: 20 - 15);

      RemoveFromAllQueues(mat1, proc: 0, offset: 22);

      //----- Now Unassigned
      LoadStart(mat3, offset: 25, load: 2);
      LoadEnd(mat3, offset: 30, load: 2, elapMin: 30 - 25, expectMark: false);
      ExpectPalletStart(t, pal: 4, offset: 30, mats: [mat3]);
      MovePallet(t, pal: 4, offset: 30, load: 2);
      ExpectRemoveFromQueue(
        AdjProcess(mat3, 0),
        offset: 30,
        queue: "rawmat",
        startingPos: 0,
        reason: "LoadedToPallet",
        elapMin: 30 - 2
      );

      CheckMatInQueue("rawmat", new[] { AdjUnique(mat4, "", 1) });

      SwapMaterial(mat3, mat4, offset: 33, unassigned: true);

      CheckMatInQueue("rawmat", new[] { AdjUnique(mat3, "", 2) });

      // continue with mat4
      MachStart(mat4, offset: 35, mach: 3);
      MachEnd(mat4, offset: 38, mach: 3, elapMin: 38 - 35);

      jobLog
        .GetMaterialDetails(mat3.MaterialID)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = mat3.MaterialID,
            JobUnique = null,
            PartName = mat3.JobPartName,
            NumProcesses = mat3.NumProcess,
            Workorder = null,
            Serial = SerialSettings.ConvertToBase62(mat3.MaterialID).PadLeft(10, '0'),
          }
        );

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void Tools()
    {
      var j = new Job()
      {
        UniqueStr = "unique",
        PartName = "part1",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "toolsSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow;

      AddTestPart(unique: "unique", part: "part1", numProc: 1);

      var p = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 1, matID: 1);

      LoadStart(p, offset: 0, load: 5);
      LoadEnd(p, offset: 2, load: 5, elapMin: 2);
      ExpectPalletStart(t, pal: 3, offset: 2, mats: [p]);
      MovePallet(t, offset: 3, load: 1, pal: 3);

      // some basic snapshots.  More complicated scenarios are tested as part of the Repository spec

      mazakDataTools.AddRange(
        new[]
        {
          new ToolPocketRow()
          {
            MachineNumber = 1,
            PocketNumber = 10,
            GroupNo = "ignored",
            IsToolDataValid = true,
            LifeUsed = 20,
            LifeSpan = 101,
          },
          new ToolPocketRow()
          {
            MachineNumber = 2,
            PocketNumber = 10,
            GroupNo = "tool1",
            IsToolDataValid = true,
            LifeUsed = 30,
            LifeSpan = 102,
          },
          new ToolPocketRow()
          {
            MachineNumber = 2,
            PocketNumber = 20,
            GroupNo = "tool2",
            IsToolDataValid = true,
            LifeUsed = 40,
            LifeSpan = 103,
          },
          new ToolPocketRow()
          {
            MachineNumber = 2,
            PocketNumber = 30,
            GroupNo = "ignored",
            IsToolDataValid = false,
            LifeUsed = 50,
            LifeSpan = 104,
          },
          new ToolPocketRow()
          {
            MachineNumber = 2,
            PocketNumber = 40,
            GroupNo = null,
            IsToolDataValid = false,
            LifeUsed = 60,
            LifeSpan = 105,
          },
          new ToolPocketRow()
          {
            MachineNumber = 2,
            PocketNumber = null,
            GroupNo = "ignored",
            IsToolDataValid = false,
            LifeUsed = 70,
            LifeSpan = 106,
          },
        }
      );
      MachStart(p, offset: 4, mach: 2);

      mazakDataTools.Clear();
      mazakDataTools.AddRange(
        new[]
        {
          new ToolPocketRow()
          {
            MachineNumber = 1,
            PocketNumber = 10,
            GroupNo = "ignored",
            IsToolDataValid = true,
            LifeUsed = 22,
            LifeSpan = 101,
          },
          new ToolPocketRow()
          {
            MachineNumber = 2,
            PocketNumber = 10,
            GroupNo = "tool1",
            IsToolDataValid = true,
            LifeUsed = 33,
            LifeSpan = 102,
          },
          new ToolPocketRow()
          {
            MachineNumber = 2,
            PocketNumber = 20,
            GroupNo = "tool2",
            IsToolDataValid = true,
            LifeUsed = 44,
            LifeSpan = 103,
          },
          new ToolPocketRow()
          {
            MachineNumber = 2,
            PocketNumber = 30,
            GroupNo = "ignored",
            IsToolDataValid = false,
            LifeUsed = 55,
            LifeSpan = 104,
          },
          new ToolPocketRow()
          {
            MachineNumber = 2,
            PocketNumber = 40,
            GroupNo = null,
            IsToolDataValid = false,
            LifeUsed = 66,
            LifeSpan = 105,
          },
          new ToolPocketRow()
          {
            MachineNumber = 2,
            PocketNumber = null,
            GroupNo = "ignored",
            IsToolDataValid = false,
            LifeUsed = 77,
            LifeSpan = 106,
          },
        }
      );
      MachEnd(
        p,
        offset: 20,
        mach: 2,
        elapMin: 16,
        tools: new List<ToolUse>()
        {
          new ToolUse()
          {
            Tool = "tool1",
            Pocket = 10,
            ToolUseDuringCycle = TimeSpan.FromSeconds(33 - 30),
            TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(33),
            ConfiguredToolLife = TimeSpan.FromSeconds(102),
          },
          new ToolUse()
          {
            Tool = "tool2",
            Pocket = 20,
            ToolUseDuringCycle = TimeSpan.FromSeconds(44 - 40),
            TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(44),
            ConfiguredToolLife = TimeSpan.FromSeconds(103),
          },
        }
      );

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void StockerAndRotaryTable()
    {
      var j = new Job()
      {
        UniqueStr = "unique",
        PartName = "part1",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "stockerSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", numProc: 1);

      var p = BuildMaterial(t, pal: 7, unique: "unique", part: "part1", proc: 1, numProc: 1, matID: 1);

      LoadStart(p, offset: 0, load: 5);
      LoadEnd(p, offset: 2, load: 5, elapMin: 2);
      ExpectPalletStart(t, pal: 7, offset: 2, mats: [p]);
      MovePallet(t, offset: 3, load: 1, pal: 7);

      StockerStart(p, offset: 4, stocker: 7, waitForMachine: true);
      StockerEnd(p, offset: 6, stocker: 7, elapMin: 2, waitForMachine: true);

      RotaryQueueStart(p, offset: 7, mc: 2);
      RotateIntoWorktable(p, offset: 10, mc: 2, elapMin: 3);

      MachStart(p, offset: 11, mach: 2);
      MachEnd(p, offset: 20, mach: 2, elapMin: 9);

      MoveFromOutboundRotaryTable(p, offset: 21, mc: 2);

      UnloadStart(p, offset: 22, load: 1);
      UnloadEnd(p, offset: 23, load: 1, elapMin: 1);
      ExpectPalletCycle(t, pal: 7, offset: 23, elapMin: 23 - 2, mats: [p]);
      MovePallet(t, offset: 24, load: 0, pal: 7);

      CheckExpected(t.AddHours(-1), t.AddHours(10));

      jobLog
        .GetMaterialDetails(p.MaterialID)
        .ShouldBeEquivalentTo(
          new MaterialDetails()
          {
            MaterialID = p.MaterialID,
            JobUnique = "unique",
            PartName = "part1",
            NumProcesses = 1,
            Serial = SerialSettings.ConvertToBase62(p.MaterialID).PadLeft(10, '0'),
            Workorder = null,
            Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1),
          }
        );
    }

    [Test]
    public void LeaveInboundRotary()
    {
      var j = new Job()
      {
        UniqueStr = "unique",
        PartName = "part1",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "rotarySch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);

      AddTestPart(unique: "unique", part: "part1", numProc: 1);

      var p = BuildMaterial(t, pal: 7, unique: "unique", part: "part1", proc: 1, numProc: 1, matID: 1);

      LoadStart(p, offset: 0, load: 5);
      LoadEnd(p, offset: 2, load: 5, elapMin: 2);
      ExpectPalletStart(t, pal: 7, offset: 2, mats: [p]);
      MovePallet(t, offset: 3, load: 1, pal: 7);

      RotaryQueueStart(p, offset: 7, mc: 2);
      MoveFromInboundRotaryTable(p, offset: 13, mc: 2, elapMin: 6);

      RotaryQueueStart(p, offset: 15, mc: 3);
      RotateIntoWorktable(p, offset: 16, mc: 3, elapMin: 1);

      MachStart(p, offset: 20, mach: 3);
      MachEnd(p, offset: 30, mach: 3, elapMin: 10);

      StockerStart(p, offset: 35, stocker: 3, waitForMachine: false);
      StockerEnd(p, offset: 37, stocker: 3, elapMin: 2, waitForMachine: false);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void QuarantinesMaterial()
    {
      var j = new Job()
      {
        UniqueStr = "unique",
        PartName = "part1",
        Cycles = 0,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes = ImmutableList.Create(
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) },
          new ProcessInfo() { Paths = ImmutableList.Create(JobLogTest.EmptyPath) }
        ),
      };
      jobLog.AddJobs(
        new NewJobs() { Jobs = ImmutableList.Create(j), ScheduleId = "quarantineSch" },
        null,
        addAsCopiedToSystem: true
      );

      var t = DateTime.UtcNow.AddHours(-5);

      var schId = AddTestPart(unique: "unique", part: "part1", numProc: 2);

      var m1proc1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 2, matID: 1);
      var m1proc2 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 2, numProc: 2, matID: 1);
      var m2proc1 = BuildMaterial(t, pal: 3, unique: "unique", part: "part1", proc: 1, numProc: 2, matID: 2);

      SetPallet(pal: 3, atLoadStation: true);
      LoadStart(m1proc1, offset: 0, load: 1);
      LoadEnd(m1proc1, offset: 4, load: 1, elapMin: 4);
      ExpectPalletStart(t, pal: 3, offset: 4, mats: [m1proc1]);
      MovePallet(t, offset: 5, load: 1, pal: 3);
      SetPallet(pal: 3, atLoadStation: false);
      SetPalletFace(pal: 3, schId: schId, proc: 1, fixQty: 1);

      MachStart(m1proc1, offset: 10, mach: 4);

      // shouldn't do anything here, statuses match
      CheckPalletStatusMatchesLogs().PalletStatusChanged.ShouldBeFalse();
      jobLog.GetMaterialInAllQueues().ShouldBeEmpty();

      MachEnd(m1proc1, offset: 15, mach: 4, elapMin: 5);

      UnloadStart(m1proc1, offset: 20, load: 2);
      LoadStart(m1proc2, offset: 20, load: 2);
      LoadStart(m2proc1, offset: 20, load: 2);

      SetPallet(pal: 3, atLoadStation: true);
      ClearPalletFace(pal: 3, proc: 1);

      // shouldn't do anything, pallet at load station
      CheckPalletStatusMatchesLogs().PalletStatusChanged.ShouldBeFalse();
      jobLog.GetMaterialInAllQueues().ShouldBeEmpty();

      UnloadEnd(m1proc1, offset: 22, load: 2, elapMin: 2, totalMatCnt: 3);
      LoadEnd(m1proc2, offset: 22, load: 2, elapMin: 2, totalMatCnt: 3);
      LoadEnd(m2proc1, offset: 22, load: 2, elapMin: 2, totalMatCnt: 3);
      ExpectPalletCycle(t, pal: 3, offset: 22, elapMin: 22 - 4, mats: [m1proc1]);
      ExpectPalletStart(t, pal: 3, offset: 22, mats: [m1proc2, m2proc1]);
      MovePallet(t, offset: 23, pal: 3, load: 2);
      SetPallet(pal: 3, atLoadStation: false);
      SetPalletFace(pal: 3, schId: schId, proc: 1, fixQty: 1);
      SetPalletFace(pal: 3, schId: schId, proc: 2, fixQty: 1);

      StockerStart(new[] { m1proc2, m2proc1 }, offset: 25, stocker: 3, waitForMachine: true);

      // shouldn't do anything, statuses match
      CheckPalletStatusMatchesLogs().PalletStatusChanged.ShouldBeFalse();
      jobLog.GetMaterialInAllQueues().ShouldBeEmpty();

      // remove process 1
      ClearPalletFace(pal: 3, proc: 1);

      CheckPalletStatusMatchesLogs().PalletStatusChanged.ShouldBeTrue();

      var actualQuarantinedMat = jobLog.GetMaterialInAllQueues().ShouldHaveSingleItem();
      actualQuarantinedMat
        .AddTimeUTC.ShouldNotBeNull()
        .ShouldBe(DateTime.UtcNow, tolerance: TimeSpan.FromSeconds(3));
      actualQuarantinedMat.ShouldBeEquivalentTo(
        new QueuedMaterial()
        {
          MaterialID = m2proc1.MaterialID,
          Queue = "quarantineQ",
          Position = 0,
          Unique = "unique",
          PartNameOrCasting = "part1",
          NumProcesses = 2,
          NextProcess = 2,
          Serial = SerialSettings.ConvertToBase62(m2proc1.MaterialID).PadLeft(10, '0'),
          Paths = ImmutableDictionary<int, int>.Empty.Add(1, 1),
          AddTimeUTC = actualQuarantinedMat.AddTimeUTC,
        }
      );

      expected.Add(
        new BlackMaple.MachineFramework.LogEntry(
          cntr: -1,
          mat: [m2proc1.ToLogMat() with { Face = 0 }],
          pal: 0,
          ty: LogType.AddToQueue,
          locName: "quarantineQ",
          locNum: 0,
          prog: "MaterialMissingOnPallet",
          start: false,
          endTime: actualQuarantinedMat.AddTimeUTC.ShouldNotBeNull(),
          result: ""
        )
      );

      // need to reload material in queues

      CheckPalletStatusMatchesLogs().PalletStatusChanged.ShouldBeFalse();

      // now future events don't have the material
      StockerEnd(new[] { m1proc2 }, offset: 30, stocker: 3, waitForMachine: true, elapMin: 5);

      CheckExpected(t.AddHours(-1), t.AddHours(10));
    }

    [Test]
    public void SkipsRecentMachineEnd()
    {
      var e = new MazakMachineInterface.LogEntry()
      {
        TimeUTC = DateTime.UtcNow.AddSeconds(-5),
        Code = LogCode.MachineCycleEnd,
        ForeignID = "",
        StationNumber = 4,
        Pallet = 3,
        FullPartName = "apart",
        JobPartName = "apart",
        Process = 2,
        FixedQuantity = 5,
        Program = "aprogram",
        TargetPosition = "",
        FromPosition = "",
      };

      HandleEvent(e, expectedMachineEnd: true);
      raisedByEvent.ShouldBeEmpty();
      jobLog.GetLogEntries(DateTime.UtcNow.AddHours(-10), DateTime.UtcNow.AddHours(10)).ShouldBeEmpty();
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
