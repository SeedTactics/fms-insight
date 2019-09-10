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
using System.Linq;
using System.Collections.Generic;
using FluentAssertions;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class FakeIccDsl : IDisposable
  {
    private JobLogDB _logDB;
    private JobDB _jobDB;
    private AssignPallets _assign;
    private CreateLogEntries _createLog;
    private NiigataStatus _status;
    private FMSSettings _settings;

    private List<InProcessMaterial> _expectedLoadCastings = new List<InProcessMaterial>();
    private Dictionary<long, InProcessMaterial> _expectedMaterial = new Dictionary<long, InProcessMaterial>(); //key is matId
    private Dictionary<int, List<(int face, string unique, int proc, int path)>> _expectedFaces = new Dictionary<int, List<(int face, string unique, int proc, int path)>>(); // key is pallet

    public FakeIccDsl(int numPals, int numMachines)
    {
      _settings = new FMSSettings()
      {
        SerialType = SerialType.AssignOneSerialPerMaterial,
        ConvertMaterialIDToSerial = FMSSettings.ConvertToBase62,
        ConvertSerialToMaterialID = FMSSettings.ConvertFromBase62
      };

      var logConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      logConn.Open();
      _logDB = new JobLogDB(_settings, logConn);
      _logDB.CreateTables(firstSerialOnEmpty: null);

      var jobConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      jobConn.Open();
      _jobDB = new JobDB(jobConn);
      _jobDB.CreateTables();

      var record = new RecordFacesForPallet(_logDB);

      _assign = new AssignPallets(record);
      _createLog = new CreateLogEntries(_logDB, _jobDB, record, _settings);

      _status = new NiigataStatus();
      _status.TimeOfStatusUTC = DateTime.UtcNow.AddDays(-1);

      _status.Machines = new Dictionary<int, MachineStatus>();
      for (int mach = 1; mach <= numMachines; mach++)
      {
        _status.Machines.Add(mach, new MachineStatus()
        {
          MachineNumber = mach,
          Machining = false,
          CurrentlyExecutingProgram = 0
        });
      }

      _status.Pallets = new List<PalletStatus>();
      for (int pal = 1; pal <= numPals; pal++)
      {
        _status.Pallets.Add(new PalletStatus()
        {
          Master = new PalletMaster()
          {
            PalletNum = pal,
            NoWork = true
          },
          CurStation = NiigataStationNum.Buffer(pal),
          Tracking = new TrackingInfo()
        });
        _expectedFaces[pal] = new List<(int face, string unique, int proc, int path)>();
      }
    }

    public void Dispose()
    {
      _logDB.Close();
      _jobDB.Close();
    }

    #region Niigata Status
    public FakeIccDsl MoveToBuffer(int pal, int buff)
    {
      _status.Pallets[pal - 1].CurStation = NiigataStationNum.Buffer(buff);
      return this;
    }
    public FakeIccDsl MoveToMachineQueue(int pal, int mach)
    {
      _status.Pallets[pal - 1].CurStation = NiigataStationNum.MachineQueue(mach);
      return this;
    }
    public FakeIccDsl MoveToMachine(int pal, int mach)
    {
      _status.Pallets[pal - 1].CurStation = NiigataStationNum.Machine(mach);
      return this;
    }

    public FakeIccDsl MoveToLoad(int pal, int lul)
    {
      _status.Pallets[pal - 1].CurStation = NiigataStationNum.LoadStation(lul);
      return this;
    }

    public FakeIccDsl SetBeforeLoad(int pal)
    {
      var p = _status.Pallets[pal - 1];
      p.Tracking.CurrentControlNum = AssignPallets.LoadStepNum * 2 - 1;
      p.Tracking.CurrentStepNum = AssignPallets.LoadStepNum;
      return this;
    }

    public FakeIccDsl SetAfterLoad(int pal)
    {
      var p = _status.Pallets[pal - 1];
      p.Tracking.CurrentControlNum = AssignPallets.LoadStepNum * 2;
      p.Tracking.CurrentStepNum = AssignPallets.LoadStepNum;
      return this;
    }

    public FakeIccDsl SetBeforeMC(int pal)
    {
      var p = _status.Pallets[pal - 1];
      p.Tracking.CurrentControlNum = AssignPallets.MachineStepNum * 2 - 1;
      p.Tracking.CurrentStepNum = AssignPallets.MachineStepNum;
      return this;
    }

    public FakeIccDsl SetAfterMC(int pal)
    {
      var p = _status.Pallets[pal - 1];
      p.Tracking.CurrentControlNum = AssignPallets.MachineStepNum * 2;
      p.Tracking.CurrentStepNum = AssignPallets.MachineStepNum;
      return this;
    }

    public FakeIccDsl SetBeforeUnload(int pal)
    {
      var p = _status.Pallets[pal - 1];
      p.Tracking.CurrentControlNum = AssignPallets.UnloadStepNum * 2 - 1;
      p.Tracking.CurrentStepNum = AssignPallets.UnloadStepNum;
      return this;
    }

    public FakeIccDsl SetAfterUnload(int pal)
    {
      var p = _status.Pallets[pal - 1];
      p.Tracking.CurrentControlNum = AssignPallets.UnloadStepNum * 2;
      p.Tracking.CurrentStepNum = AssignPallets.UnloadStepNum;
      return this;
    }

    public FakeIccDsl StartMachine(int mach, int program)
    {
      _status.Machines[mach].Machining = true;
      _status.Machines[mach].CurrentlyExecutingProgram = program;
      return this;
    }

    public FakeIccDsl EndMachine(int mach)
    {
      _status.Machines[mach].Machining = false;
      _status.Machines[mach].CurrentlyExecutingProgram = 0;
      return this;
    }

    public FakeIccDsl AdvanceMinutes(int min)
    {
      _status.TimeOfStatusUTC = _status.TimeOfStatusUTC.AddMinutes(min);
      return this;
    }

    public FakeIccDsl OverrideRoute(int pal, string comment, bool noWork, IEnumerable<int> luls, IEnumerable<int> machs, IEnumerable<int> progs, IEnumerable<(int face, string unique, int proc, int path)> faces = null)
    {
      _status.Pallets[pal - 1].Master = new PalletMaster()
      {
        PalletNum = pal,
        Comment = comment,
        RemainingPalletCycles = 1,
        NoWork = noWork,
        Routes = new List<RouteStep> {
          new LoadStep() {
            LoadStations = luls.ToList()
          },
          new MachiningStep() {
            Machines = machs.ToList(),
            ProgramNumsToRun = progs.ToList()
          },
          new UnloadStep() {
            UnloadStations = luls.ToList()
          }
        }
      };
      _expectedFaces[pal] = faces == null ? new List<(int face, string unique, int proc, int path)>() : faces.ToList();

      return this;
    }
    #endregion

    #region Material
    public FakeIccDsl AddUnallocatedCasting(string queue, string part, int numProc, out LogMaterial mat)
    {
      var matId = _logDB.AllocateMaterialIDForCasting(part, numProc);
      if (_settings.SerialType == SerialType.AssignOneSerialPerMaterial)
      {
        _logDB.RecordSerialForMaterialID(
          new JobLogDB.EventLogMaterial() { MaterialID = matId, Process = 0, Face = "" },
          _settings.ConvertMaterialIDToSerial(matId),
          _status.TimeOfStatusUTC
        );
      }
      var addLog = _logDB.RecordAddMaterialToQueue(
        new JobLogDB.EventLogMaterial()
        {
          MaterialID = matId,
          Process = 0,
          Face = ""
        },
        queue,
        -1,
        _status.TimeOfStatusUTC
      );
      _expectedMaterial[matId] = new InProcessMaterial()
      {
        MaterialID = matId,
        JobUnique = "",
        PartName = part,
        Process = 0,
        Path = 1,
        Serial = _settings.ConvertMaterialIDToSerial(matId),
        Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.Waiting
        },
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.InQueue,
          CurrentQueue = queue,
          QueuePosition = addLog.First().LocationNum
        }
      };
      mat = new LogMaterial(
        matID: matId,
        uniq: "",
        proc: 0,
        part: part,
        numProc: numProc,
        serial: _settings.ConvertMaterialIDToSerial(matId),
        workorder: "",
        face: ""
      );
      return this;
    }

    public FakeIccDsl SetExpectedLoadCastings(IEnumerable<(string unique, string part, int pal, int path, int face)> castings)
    {
      _expectedLoadCastings = castings.Select(c => new InProcessMaterial()
      {
        MaterialID = -1,
        JobUnique = c.unique,
        PartName = c.part,
        Process = 0,
        Path = c.path,
        Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.Loading,
          LoadOntoPallet = c.pal.ToString(),
          LoadOntoFace = c.face,
          ProcessAfterLoad = 1,
          PathAfterLoad = c.path,
        },
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.Free
        }
      }).ToList();
      return this;
    }

    public FakeIccDsl ClearExpectedLoadCastings()
    {
      _expectedLoadCastings = new List<InProcessMaterial>();
      return this;
    }

    public FakeIccDsl UpdateExpectedMaterial(long matId, Action<InProcessMaterial> f)
    {
      f(_expectedMaterial[matId]);
      return this;
    }

    public FakeIccDsl UpdateExpectedMaterial(IEnumerable<long> matIds, Action<InProcessMaterial> f)
    {
      foreach (var matId in matIds)
        f(_expectedMaterial[matId]);
      return this;
    }

    public FakeIccDsl RemoveExpectedMaterial(long matId)
    {
      _expectedMaterial.Remove(matId);
      return this;
    }

    public FakeIccDsl RemoveExpectedMaterial(IEnumerable<long> matIds)
    {
      foreach (var matId in matIds)
        _expectedMaterial.Remove(matId);
      return this;
    }
    #endregion

    #region Jobs
    public FakeIccDsl AddOneProcOnePathJob(string unique, string part, int qty, int priority, int partsPerPal, int[] pals, int[] luls, int[] machs, int prog, int loadMins, int machMins, int unloadMins, string fixture, int face, string queue = null)
    {
      var j = new JobPlan(unique, 1);
      j.PartName = part;
      j.Priority = priority;
      foreach (var i in luls)
      {
        j.AddLoadStation(1, 1, i);
        j.AddUnloadStation(1, 1, i);
      }
      j.SetExpectedLoadTime(1, 1, TimeSpan.FromMinutes(loadMins));
      j.SetExpectedUnloadTime(1, 1, TimeSpan.FromMinutes(unloadMins));
      j.SetPartsPerPallet(1, 1, partsPerPal);
      var s = new JobMachiningStop("MC");
      foreach (var m in machs)
      {
        s.AddProgram(m, prog.ToString());
        s.ExpectedCycleTime = TimeSpan.FromMinutes(machMins);
      }
      j.AddMachiningStop(1, 1, s);
      foreach (var p in pals)
      {
        j.AddProcessOnPallet(1, 1, p.ToString());
      }
      j.AddProcessOnFixture(1, 1, fixture, face.ToString());
      if (!string.IsNullOrEmpty(queue))
      {
        j.SetInputQueue(1, 1, queue);
      }
      _jobDB.AddJobs(new NewJobs() { Jobs = new List<JobPlan> { j } }, null);

      return this;
    }
    #endregion

    #region Steps
    private void CheckStatusMatchesExpected(NiigataMaterialStatus actualStatus)
    {
      actualStatus.Status.Should().Be(_status);
      actualStatus.Pallets.Count.Should().Be(_status.Pallets.Count);
      for (int palNum = 1; palNum <= actualStatus.Pallets.Count; palNum++)
      {
        var current = actualStatus.Pallets[palNum - 1];
        current.Status.Should().Be(_status.Pallets[palNum - 1]);
        current.Faces.Should().BeEquivalentTo(_expectedFaces[palNum].Select(face =>
          new PalletFace()
          {
            Job = _jobDB.LoadJob(face.unique),
            Process = face.proc,
            Path = face.path,
            Face = face.face,
            Material = _expectedMaterial.Values.Concat(_expectedLoadCastings).Where(
              m => (m.Location.Type == InProcessMaterialLocation.LocType.OnPallet && m.Location.Pallet == palNum.ToString() && m.Location.Face == face.face)
                || (m.Action.Type == InProcessMaterialAction.ActionType.Loading && m.Action.LoadOntoPallet == palNum.ToString() && m.Action.LoadOntoFace == face.face)
            ).ToList()
          }
        ));
      }
      actualStatus.QueuedMaterial.Should().BeEquivalentTo(_expectedMaterial.Values.Where(
        m => m.Location.Type == InProcessMaterialLocation.LocType.InQueue && m.Action.Type == InProcessMaterialAction.ActionType.Waiting
      ));
    }

    public FakeIccDsl ExpectNoChanges()
    {
      var sch = _jobDB.LoadUnarchivedJobs();

      using (var logMonitor = _logDB.Monitor())
      {
        var status = _createLog.CheckForNewLogEntries(_status, sch, out bool palletStateUpdated);
        palletStateUpdated.Should().BeFalse();
        CheckStatusMatchesExpected(status);
        _assign.NewPalletChange(status, sch).Should().BeNull();
        logMonitor.Should().NotRaise("NewLogEntry");
      }
      return this;
    }

    public FakeIccDsl ExpectNewRoute(int pal, int[] luls, int[] machs, int[] progs, IEnumerable<(int face, string unique, int proc, int path)> faces)
    {
      var sch = _jobDB.LoadUnarchivedJobs();
      var expectedMaster = new PalletMaster()
      {
        PalletNum = pal,
        Comment = "",
        RemainingPalletCycles = 1,
        Priority = 0,
        NoWork = false,
        Skip = false,
        ForLongToolMaintenance = false,
        PerformProgramDownload = false,
        Routes = new List<RouteStep> {
          new LoadStep() {
            LoadStations = luls.ToList()
          },
          new MachiningStep() {
            Machines = machs.ToList(),
            ProgramNumsToRun = progs.ToList()
          },
          new UnloadStep() {
            UnloadStations = luls.ToList(),
            CompletedPartCount = 1
          }
        },
      };

      using (var logMonitor = _logDB.Monitor())
      {
        var matStatus = _createLog.CheckForNewLogEntries(_status, sch, out bool palletStateUpdated);
        palletStateUpdated.Should().BeFalse();
        CheckStatusMatchesExpected(matStatus);

        logMonitor.Should().NotRaise("NewLogEntry");

        var action = _assign.NewPalletChange(matStatus, sch);
        action.Should().BeEquivalentTo<NewPalletRoute>(new NewPalletRoute()
        {
          NewMaster = expectedMaster
        }, options => options.Excluding(e => e.PendingID).Excluding(e => e.NewMaster.Comment));
        _status.Pallets[pal - 1].Master = ((NewPalletRoute)action).NewMaster;
        _status.Pallets[pal - 1].Tracking.CurrentControlNum = AssignPallets.LoadStepNum * 2 - 1;
        _status.Pallets[pal - 1].Tracking.CurrentStepNum = AssignPallets.LoadStepNum;
        _expectedFaces[pal] = faces.ToList();

        logMonitor.Should().Raise("NewLogEntry");

        logMonitor.OccurredEvents.Length.Should().Be(1);
        var log = (LogEntry)logMonitor.OccurredEvents[0].Parameters[0];
        log.LogType.Should().Be(LogType.GeneralMessage);
        log.Result.Should().Be("New Niigata Route");
      }
      return this;
    }

    public FakeIccDsl ExpectRouteIncrement(int pal, int newCycleCnt)
    {
      var sch = _jobDB.LoadUnarchivedJobs();
      using (var logMonitor = _logDB.Monitor())
      {
        var pals = _createLog.CheckForNewLogEntries(_status, sch, out bool palletStateUpdated);
        palletStateUpdated.Should().BeFalse();

        var action = _assign.NewPalletChange(pals, sch);
        action.Should().BeEquivalentTo<UpdatePalletQuantities>(new UpdatePalletQuantities()
        {
          Pallet = pal,
          Priority = _status.Pallets[pal - 1].Master.Priority,
          Cycles = newCycleCnt,
          NoWork = false,
          Skip = false,
          ForLongTool = false
        });
        _status.Pallets[pal - 1].Master.NoWork = false;
        _status.Pallets[pal - 1].Master.RemainingPalletCycles = newCycleCnt;
        _status.Pallets[pal - 1].Tracking.CurrentControlNum = AssignPallets.LoadStepNum * 2 - 1;
        _status.Pallets[pal - 1].Tracking.CurrentStepNum = AssignPallets.LoadStepNum;

        logMonitor.Should().NotRaise("NewLogEntry");
      }

      return ExpectNoChanges();
    }

    public FakeIccDsl ExpectRouteIncrementAndLoadBegin(int pal, int lul)
    {
      var sch = _jobDB.LoadUnarchivedJobs();
      using (var logMonitor = _logDB.Monitor())
      {
        var pals = _createLog.CheckForNewLogEntries(_status, sch, out bool palletStateUpdated);
        palletStateUpdated.Should().BeTrue();

        logMonitor.Should().Raise("NewLogEntry");
        logMonitor.OccurredEvents.Select(e => e.Parameters[0]).Should().BeEquivalentTo(new[] {
          new LogEntry(
            cntr: -1,
            mat: Enumerable.Empty<LogMaterial>(),
            pal: pal.ToString(),
            ty: LogType.LoadUnloadCycle,
            locName: "L/U",
            locNum: lul,
            prog: "LOAD",
            start: true,
            endTime: _status.TimeOfStatusUTC,
            result: "LOAD",
            endOfRoute: false
          )
        }, options => options.Excluding(e => e.Counter));

        var action = _assign.NewPalletChange(pals, sch);
        action.Should().BeEquivalentTo<UpdatePalletQuantities>(new UpdatePalletQuantities()
        {
          Pallet = pal,
          Priority = _status.Pallets[pal - 1].Master.Priority,
          Cycles = 2,
          NoWork = false,
          Skip = false,
          ForLongTool = false
        });
        _status.Pallets[pal - 1].Master.RemainingPalletCycles = 2;
        _status.Pallets[pal - 1].Tracking.CurrentStepNum = AssignPallets.LoadStepNum;
        _status.Pallets[pal - 1].Tracking.CurrentControlNum = AssignPallets.LoadStepNum * 2 - 1;
      }

      return ExpectNoChanges();
    }
    #endregion

    #region Logs

    private FakeIccDsl ExpectNewLogs(IEnumerable<LogEntry> log)
    {
      var sch = _jobDB.LoadUnarchivedJobs();

      using (var logMonitor = _logDB.Monitor())
      {
        var matStatus = _createLog.CheckForNewLogEntries(_status, sch, out bool palletStateUpdated);
        palletStateUpdated.Should().BeTrue();
        CheckStatusMatchesExpected(matStatus);

        _assign.NewPalletChange(matStatus, sch).Should().BeNull();

        logMonitor.Should().Raise("NewLogEntry");

        logMonitor.OccurredEvents.Select(e => e.Parameters[0]).Should().BeEquivalentTo(log, options => options.Excluding(e => e.Counter));
      }

      return ExpectNoChanges();
    }

    public FakeIccDsl ExpectLoadBeginEvt(int pal, int lul)
    {
      return ExpectNewLogs(new[] {
        new LogEntry(
          cntr: -1,
          mat: Enumerable.Empty<LogMaterial>(),
          pal: pal.ToString(),
          ty: LogType.LoadUnloadCycle,
          locName: "L/U",
          locNum: lul,
          prog: "LOAD",
          start: true,
          endTime: _status.TimeOfStatusUTC,
          result: "LOAD",
          endOfRoute: false
      )});
    }

    public FakeIccDsl ExpectMachineBegin(int pal, int mach, int program, IEnumerable<LogMaterial> mats)
    {
      return ExpectNewLogs(new[] {
        new LogEntry(
          cntr: -1,
          mat: mats,
          pal: pal.ToString(),
          ty: LogType.MachineCycle,
          locName: "MC",
          locNum: mach,
          prog: program.ToString(),
          start: true,
          endTime: _status.TimeOfStatusUTC,
          result: "",
          endOfRoute: false
      )});
    }

    public FakeIccDsl ExpectMachineEnd(int pal, int mach, int program, int proc, int path, int elapsedMin, int activeMin, IEnumerable<LogMaterial> mats)
    {
      return ExpectNewLogs(new[] {
        new LogEntry(
          cntr: -1,
          mat: mats,
          pal: pal.ToString(),
          ty: LogType.MachineCycle,
          locName: "MC",
          locNum: mach,
          prog: program.ToString(),
          start: false,
          endTime: _status.TimeOfStatusUTC,
          result: "",
          endOfRoute: false,
          elapsed: TimeSpan.FromMinutes(elapsedMin),
          active: TimeSpan.FromMinutes(activeMin)
      )});
    }

    public abstract class ExpectedLoadEndEvt { }

    private class ExpectedLoadCastingEvt : ExpectedLoadEndEvt
    {
      public int Face { get; set; }
      public int Count { get; set; }
      public string Unique { get; set; }
      public int Path { get; set; }
      public int ActiveMins { get; set; }
      public List<LogMaterial> OutMaterial { get; set; }
    }

    public static ExpectedLoadEndEvt LoadCastingToFace(int face, string unique, int path, int cnt, int activeMins, out IEnumerable<LogMaterial> mats)
    {
      var e = new ExpectedLoadCastingEvt()
      {
        Face = face,
        Count = cnt,
        Unique = unique,
        Path = path,
        ActiveMins = activeMins,
        OutMaterial = new List<LogMaterial>(),
      };
      mats = e.OutMaterial;
      return e;
    }

    private class ExpectedLoadMatsEvt : ExpectedLoadEndEvt
    {
      public IEnumerable<LogMaterial> Material { get; set; }
      public int ActiveMins { get; set; }
      public string FromQueue { get; set; } = null;
    }

    public ExpectedLoadEndEvt LoadToFace(int face, string unique, int activeMins, string fromQueue, IEnumerable<LogMaterial> loadingMats, out IEnumerable<LogMaterial> loadedMats)
    {
      loadedMats = loadingMats.Select(m =>
        new LogMaterial(
          matID: m.MaterialID,
          uniq: unique,
          proc: m.Process + 1,
          part: m.PartName,
          numProc: m.NumProcesses,
          serial: m.Serial,
          workorder: m.Workorder,
          face: face.ToString()
        )
      );
      return new ExpectedLoadMatsEvt()
      {
        Material = loadedMats,
        ActiveMins = activeMins,
        FromQueue = fromQueue,
      };
    }


    public class ExpectedUnloadEvt : ExpectedLoadEndEvt
    {
      public IEnumerable<LogMaterial> Material { get; set; }
      public int ActiveMins { get; set; }
      public string ToQueue { get; set; } = null;
    }

    public static ExpectedLoadEndEvt UnloadFromFace(int activeMins, string toQueue, IEnumerable<LogMaterial> mats)
    {
      return new ExpectedUnloadEvt()
      {
        Material = mats,
        ActiveMins = activeMins,
        ToQueue = toQueue,
      };
    }

    public FakeIccDsl ExpectLoadEndEvt(int pal, int lul, int elapsedMin, int palMins, IEnumerable<ExpectedLoadEndEvt> expectedEvts)
    {
      var sch = _jobDB.LoadUnarchivedJobs();

      using (var logMonitor = _logDB.Monitor())
      {
        var pals = _createLog.CheckForNewLogEntries(_status, sch, out bool palletStateUpdated);
        palletStateUpdated.Should().BeTrue();

        _assign.NewPalletChange(pals, sch).Should().BeNull();

        logMonitor.Should().Raise("NewLogEntry");
        var evts = logMonitor.OccurredEvents.Select(e => e.Parameters[0]).Cast<LogEntry>();


        // start computing the expected events
        var expectedLogs = new List<LogEntry> {
          new LogEntry(
            cntr: -1,
            mat: Enumerable.Empty<LogMaterial>(),
            pal: pal.ToString(),
            ty: LogType.PalletCycle,
            locName: "Pallet Cycle",
            locNum: 1,
            prog: "",
            start: false,
            endTime: _status.TimeOfStatusUTC,
            result: "PalletCycle",
            endOfRoute: false,
            elapsed: TimeSpan.FromMinutes(palMins),
            active: TimeSpan.Zero
          ),
        };

        foreach (var expected in expectedEvts)
        {
          switch (expected)
          {
            case ExpectedLoadCastingEvt load:

              // first, extract the newly created material
              var evt = evts.First(
                e => e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.Material.Any(m => m.Face == load.Face.ToString())
              );
              var matIds = evt.Material.Select(m => m.MaterialID);

              matIds.Count().Should().Be(load.Count);

              load.OutMaterial.AddRange(evt.Material.Select(origMat => new LogMaterial(
                matID: origMat.MaterialID, uniq: load.Unique, proc: 1, part: origMat.PartName, numProc: origMat.NumProcesses,
                serial: _settings.ConvertMaterialIDToSerial(origMat.MaterialID), workorder: "", face: load.Face.ToString())
              ));

              // now the expected events
              expectedLogs.Add(new LogEntry(
                cntr: -1,
                mat: load.OutMaterial,
                pal: pal.ToString(),
                ty: LogType.LoadUnloadCycle,
                locName: "L/U",
                locNum: lul,
                prog: "LOAD",
                start: false,
                endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                result: "LOAD",
                endOfRoute: false,
                elapsed: TimeSpan.FromMinutes(elapsedMin),
                active: TimeSpan.FromMinutes(load.ActiveMins)
              ));
              expectedLogs.AddRange(load.OutMaterial.Select(m => new LogEntry(
                cntr: -1,
                mat: new[] { new LogMaterial(matID: m.MaterialID, uniq: m.JobUniqueStr, proc: m.Process, part: m.PartName,
                                            numProc: m.NumProcesses, serial: m.Serial, workorder: "", face: "") },
                pal: "",
                ty: LogType.PartMark,
                locName: "Mark",
                locNum: 1,
                prog: "MARK",
                start: false,
                endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                result: _settings.ConvertMaterialIDToSerial(m.MaterialID),
                endOfRoute: false
              )));

              // finally, add the material to the expected material
              foreach (var m in load.OutMaterial)
              {
                _expectedMaterial[m.MaterialID] = new InProcessMaterial()
                {
                  MaterialID = m.MaterialID,
                  JobUnique = m.JobUniqueStr,
                  Process = 1,
                  Path = load.Path,
                  PartName = m.PartName,
                  Serial = _settings.ConvertMaterialIDToSerial(m.MaterialID),
                  Location = new InProcessMaterialLocation()
                  {
                    Type = InProcessMaterialLocation.LocType.OnPallet,
                    Pallet = pal.ToString(),
                    Face = load.Face
                  },
                  Action = new InProcessMaterialAction()
                  {
                    Type = InProcessMaterialAction.ActionType.Waiting
                  }
                };
              }

              break;


            case ExpectedLoadMatsEvt load:

              expectedLogs.Add(new LogEntry(
                cntr: -1,
                mat: load.Material,
                pal: pal.ToString(),
                ty: LogType.LoadUnloadCycle,
                locName: "L/U",
                locNum: lul,
                prog: "LOAD",
                start: false,
                endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                result: "LOAD",
                endOfRoute: false,
                elapsed: TimeSpan.FromMinutes(elapsedMin),
                active: TimeSpan.FromMinutes(load.ActiveMins)
              ));

              if (load.FromQueue != null)
              {
                expectedLogs.AddRange(load.Material.Select(m => new LogEntry(
                  cntr: -1,
                  mat: new[] { new LogMaterial(matID: m.MaterialID, uniq: m.JobUniqueStr, proc: m.Process, part: m.PartName,
                                              numProc: m.NumProcesses, serial: m.Serial, workorder: "", face: m.Face) },
                  pal: "",
                  ty: LogType.RemoveFromQueue,
                  locName: load.FromQueue,
                  locNum: 1,
                  prog: "",
                  start: false,
                  endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                  result: "",
                  endOfRoute: false

                )));
              }
              break;


            case ExpectedUnloadEvt unload:
              expectedLogs.Add(new LogEntry(
                cntr: -1,
                mat: unload.Material,
                pal: pal.ToString(),
                ty: LogType.LoadUnloadCycle,
                locName: "L/U",
                locNum: lul,
                prog: "UNLOAD",
                start: false,
                endTime: _status.TimeOfStatusUTC,
                result: "UNLOAD",
                endOfRoute: true,
                elapsed: TimeSpan.FromMinutes(elapsedMin),
                active: TimeSpan.FromMinutes(unload.ActiveMins)
              ));

              if (unload.ToQueue != null)
              {
                expectedLogs.AddRange(unload.Material.Select(m => new LogEntry(
                  cntr: -1,
                  mat: new[] { new LogMaterial(matID: m.MaterialID, uniq: m.JobUniqueStr, proc: m.Process, part: m.PartName,
                                              numProc: m.NumProcesses, serial: m.Serial, workorder: "", face: m.Face) },
                  pal: "",
                  ty: LogType.AddToQueue,
                  locName: unload.ToQueue,
                  locNum: 1,
                  prog: "",
                  start: false,
                  endTime: _status.TimeOfStatusUTC,
                  result: "",
                  endOfRoute: false

                )));
              }

              break;
          }
        }

        evts.Should().BeEquivalentTo(expectedLogs,
          options => options.Excluding(e => e.Counter)
        );
      }

      return ExpectNoChanges();
    }
    #endregion
  }
}