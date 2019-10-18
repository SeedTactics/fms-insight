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
    private CreateCellState _createLog;
    private NiigataStatus _status;
    private FMSSettings _settings;

    private List<InProcessMaterial> _expectedLoadCastings = new List<InProcessMaterial>();
    private Dictionary<long, InProcessMaterial> _expectedMaterial = new Dictionary<long, InProcessMaterial>(); //key is matId
    private Dictionary<int, List<(int face, string unique, int proc, int path)>> _expectedFaces = new Dictionary<int, List<(int face, string unique, int proc, int path)>>(); // key is pallet
    private Dictionary<string, int> _expectedJobStartedCount = new Dictionary<string, int>();

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
      _createLog = new CreateCellState(_logDB, _jobDB, record, _settings);

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
      p.Tracking.CurrentStepNum = 1;
      p.Tracking.CurrentControlNum = 1;
      return this;
    }

    public FakeIccDsl SetAfterLoad(int pal)
    {
      var p = _status.Pallets[pal - 1];
      p.Tracking.CurrentStepNum = 1;
      p.Tracking.CurrentControlNum = 2;
      return this;
    }

    public FakeIccDsl SetNoWork(int pal)
    {
      var p = _status.Pallets[pal - 1];
      p.Master.NoWork = true;
      p.Tracking.CurrentStepNum = 1;
      p.Tracking.CurrentControlNum = 2;
      _expectedFaces[pal].Clear();
      return this;
    }

    public FakeIccDsl SetBeforeMC(int pal, int machStepOffset = 0)
    {
      var p = _status.Pallets[pal - 1];
      var step = 1;
      foreach (var s in p.Master.Routes)
      {
        if (s is MachiningStep)
        {
          if (machStepOffset == 0)
          {
            break;
          }
          else
          {
            machStepOffset -= 1;
          }
        }
        step += 1;
      }
      p.Tracking.CurrentStepNum = step;
      p.Tracking.CurrentControlNum = step * 2 - 1;
      return this;
    }

    public FakeIccDsl SetAfterMC(int pal, int machStepOffset = 0)
    {
      var p = _status.Pallets[pal - 1];
      var step = 1;
      foreach (var s in p.Master.Routes)
      {
        if (s is MachiningStep)
        {
          if (machStepOffset == 0)
          {
            break;
          }
          else
          {
            machStepOffset -= 1;
          }
        }
        step += 1;
      }
      p.Tracking.CurrentStepNum = step;
      p.Tracking.CurrentControlNum = step * 2;
      return this;
    }

    public FakeIccDsl SetBeforeUnload(int pal)
    {
      var p = _status.Pallets[pal - 1];
      var stepIdx = p.Master.Routes.FindIndex(r => r is UnloadStep);
      var unloadStep = stepIdx + 1;
      p.Tracking.CurrentStepNum = unloadStep;
      p.Tracking.CurrentControlNum = unloadStep * 2 - 1;
      return this;
    }

    public FakeIccDsl SetAfterUnload(int pal)
    {
      var p = _status.Pallets[pal - 1];
      var stepIdx = p.Master.Routes.FindIndex(r => r is UnloadStep);
      var unloadStep = stepIdx + 1;
      p.Tracking.CurrentStepNum = unloadStep;
      p.Tracking.CurrentControlNum = unloadStep * 2;
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

    public FakeIccDsl UpdateExpectedMaterial(IEnumerable<LogMaterial> mats, Action<InProcessMaterial> f)
    {
      foreach (var mat in mats)
        f(_expectedMaterial[mat.MaterialID]);
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

    public FakeIccDsl RemoveExpectedMaterial(IEnumerable<LogMaterial> mats)
    {
      foreach (var mat in mats)
        _expectedMaterial.Remove(mat.MaterialID);
      return this;
    }
    #endregion

    #region Jobs
    public FakeIccDsl IncrJobStartedCnt(string unique, int cnt = 1)
    {
      _expectedJobStartedCount[unique] += cnt;
      return this;
    }
    public FakeIccDsl AddOneProcOnePathJob(string unique, string part, int qty, int priority, int partsPerPal, int[] pals, int[] luls, int[] machs, int prog, int loadMins, int machMins, int unloadMins, string fixture, int face, string queue = null)
    {
      var j = new JobPlan(unique, 1);
      j.PartName = part;
      j.Priority = priority;
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: qty);
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
      _expectedJobStartedCount[unique] = 0;

      return this;
    }

    public FakeIccDsl AddOneProcOnePathMultiStepJob(string unique, string part, int qty, int priority, int partsPerPal, int[] pals, int[] luls, int[] machs1, int prog1, int[] machs2, int prog2, int loadMins, int machMins1, int machMins2, int unloadMins, string fixture, int face, string queue = null)
    {
      var j = new JobPlan(unique, 1);
      j.PartName = part;
      j.Priority = priority;
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: qty);
      foreach (var i in luls)
      {
        j.AddLoadStation(1, 1, i);
        j.AddUnloadStation(1, 1, i);
      }
      j.SetExpectedLoadTime(1, 1, TimeSpan.FromMinutes(loadMins));
      j.SetExpectedUnloadTime(1, 1, TimeSpan.FromMinutes(unloadMins));
      j.SetPartsPerPallet(1, 1, partsPerPal);

      var s = new JobMachiningStop("MC");
      foreach (var m in machs1)
      {
        s.AddProgram(m, prog1.ToString());
        s.ExpectedCycleTime = TimeSpan.FromMinutes(machMins1);
      }
      j.AddMachiningStop(1, 1, s);

      s = new JobMachiningStop("MC");
      foreach (var m in machs2)
      {
        s.AddProgram(m, prog2.ToString());
        s.ExpectedCycleTime = TimeSpan.FromMinutes(machMins2);
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
      _expectedJobStartedCount[unique] = 0;

      return this;
    }

    public FakeIccDsl AddMultiProcSamePalletJob(string unique, string part, int qty, int priority, int partsPerPal, int[] pals, int[] luls, int[] machs, int prog1, int prog2, int loadMins1, int machMins1, int unloadMins1, int loadMins2, int machMins2, int unloadMins2, string fixture, int face1, int face2)
    {
      var j = new JobPlan(unique, 2);
      j.PartName = part;
      j.Priority = priority;
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: qty);
      foreach (var i in luls)
      {
        j.AddLoadStation(1, 1, i);
        j.AddUnloadStation(1, 1, i);
        j.AddLoadStation(2, 1, i);
        j.AddUnloadStation(2, 1, i);
      }
      j.SetExpectedLoadTime(1, 1, TimeSpan.FromMinutes(loadMins1));
      j.SetExpectedUnloadTime(1, 1, TimeSpan.FromMinutes(unloadMins1));
      j.SetExpectedLoadTime(2, 1, TimeSpan.FromMinutes(loadMins2));
      j.SetExpectedUnloadTime(2, 1, TimeSpan.FromMinutes(unloadMins2));
      j.SetPartsPerPallet(1, 1, partsPerPal);
      j.SetPartsPerPallet(2, 1, partsPerPal);
      var s = new JobMachiningStop("MC");
      foreach (var m in machs)
      {
        s.AddProgram(m, prog1.ToString());
        s.ExpectedCycleTime = TimeSpan.FromMinutes(machMins1);
      }
      j.AddMachiningStop(1, 1, s);
      s = new JobMachiningStop("MC");
      foreach (var m in machs)
      {
        s.AddProgram(m, prog2.ToString());
        s.ExpectedCycleTime = TimeSpan.FromMinutes(machMins2);
      }
      j.AddMachiningStop(2, 1, s);
      foreach (var p in pals)
      {
        j.AddProcessOnPallet(1, 1, p.ToString());
        j.AddProcessOnPallet(2, 1, p.ToString());
      }
      j.AddProcessOnFixture(1, 1, fixture, face1.ToString());
      j.AddProcessOnFixture(2, 1, fixture, face2.ToString());
      _jobDB.AddJobs(new NewJobs() { Jobs = new List<JobPlan> { j } }, null);
      _expectedJobStartedCount[unique] = 0;

      return this;
    }
    public FakeIccDsl AddMultiProcSeparatePalletJob(string unique, string part, int qty, int priority, int partsPerPal, int[] pals1, int[] pals2, int[] load1, int[] load2, int[] unload1, int[] unload2, int[] machs, int prog1, int prog2, int loadMins1, int machMins1, int unloadMins1, int loadMins2, int machMins2, int unloadMins2, string fixture, string queue)
    {
      var j = new JobPlan(unique, 2);
      j.PartName = part;
      j.Priority = priority;
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: qty);
      foreach (var i in load1)
      {
        j.AddLoadStation(1, 1, i);
      }
      foreach (var i in unload1)
      {
        j.AddUnloadStation(1, 1, i);
      }
      foreach (var i in load2)
      {
        j.AddLoadStation(2, 1, i);
      }
      foreach (var i in unload2)
      {
        j.AddUnloadStation(2, 1, i);
      }
      j.SetExpectedLoadTime(1, 1, TimeSpan.FromMinutes(loadMins1));
      j.SetExpectedUnloadTime(1, 1, TimeSpan.FromMinutes(unloadMins1));
      j.SetExpectedLoadTime(2, 1, TimeSpan.FromMinutes(loadMins2));
      j.SetExpectedUnloadTime(2, 1, TimeSpan.FromMinutes(unloadMins2));
      j.SetPartsPerPallet(1, 1, partsPerPal);
      j.SetPartsPerPallet(2, 1, partsPerPal);
      var s = new JobMachiningStop("MC");
      foreach (var m in machs)
      {
        s.AddProgram(m, prog1.ToString());
        s.ExpectedCycleTime = TimeSpan.FromMinutes(machMins1);
      }
      j.AddMachiningStop(1, 1, s);
      s = new JobMachiningStop("MC");
      foreach (var m in machs)
      {
        s.AddProgram(m, prog2.ToString());
        s.ExpectedCycleTime = TimeSpan.FromMinutes(machMins2);
      }
      j.AddMachiningStop(2, 1, s);
      foreach (var p in pals1)
      {
        j.AddProcessOnPallet(1, 1, p.ToString());
      }
      foreach (var p in pals2)
      {
        j.AddProcessOnPallet(2, 1, p.ToString());
      }
      j.AddProcessOnFixture(1, 1, fixture, "1");
      j.AddProcessOnFixture(2, 1, fixture, "1");

      j.SetOutputQueue(1, 1, queue);
      j.SetInputQueue(2, 1, queue);
      _jobDB.AddJobs(new NewJobs() { Jobs = new List<JobPlan> { j } }, null);
      _expectedJobStartedCount[unique] = 0;

      return this;
    }
    #endregion

    #region Steps
    private void CheckCellStMatchesExpected(CellState actualSt)
    {
      actualSt.Status.Should().Be(_status);
      actualSt.Pallets.Count.Should().Be(_status.Pallets.Count);
      for (int palNum = 1; palNum <= actualSt.Pallets.Count; palNum++)
      {
        var current = actualSt.Pallets[palNum - 1];
        current.Status.Should().Be(_status.Pallets[palNum - 1]);
        current.Faces.Should().BeEquivalentTo(_expectedFaces[palNum].Select(face =>
          new PalletFace()
          {
            Job = _jobDB.LoadJob(face.unique),
            Process = face.proc,
            Path = face.path,
            Face = face.face,
            Material = _expectedMaterial.Values.Concat(_expectedLoadCastings).Where(m =>
            {
              if (m.Action.Type == InProcessMaterialAction.ActionType.Loading)
              {
                return m.Action.LoadOntoPallet == palNum.ToString() && m.Action.LoadOntoFace == face.face;
              }
              else if (m.Location.Type == InProcessMaterialLocation.LocType.OnPallet)
              {
                return m.Location.Pallet == palNum.ToString() && m.Location.Face == face.face;
              }
              return false;
            }).ToList()
          }
        ));
      }
      actualSt.QueuedMaterial.Should().BeEquivalentTo(_expectedMaterial.Values.Where(
        m => m.Location.Type == InProcessMaterialLocation.LocType.InQueue && m.Action.Type == InProcessMaterialAction.ActionType.Waiting
      ));
      actualSt.JobQtyStarted.Should().BeEquivalentTo(_expectedJobStartedCount);
    }

    public FakeIccDsl ExpectNoChanges()
    {
      var sch = _jobDB.LoadUnarchivedJobs();

      using (var logMonitor = _logDB.Monitor())
      {
        var cellSt = _createLog.BuildCellState(_status, sch);
        cellSt.PalletStateUpdated.Should().BeFalse();
        cellSt.Schedule.Should().Be(sch);
        CheckCellStMatchesExpected(cellSt);
        _assign.NewPalletChange(cellSt).Should().BeNull();
        logMonitor.Should().NotRaise("NewLogEntry");
      }
      return this;
    }

    public abstract class ExpectedChange { }

    private class ExpectedLoadBegin : ExpectedChange
    {
      public int Pallet { get; set; }
      public int LoadStation { get; set; }
    }

    public static ExpectedChange ExpectLoadBegin(int pal, int lul)
    {
      return new ExpectedLoadBegin() { Pallet = pal, LoadStation = lul };
    }

    private class ExpectedLoadCastingEvt : ExpectedChange
    {
      public int Pallet { get; set; }
      public int LoadStation { get; set; }
      public int Face { get; set; }
      public int Count { get; set; }
      public string Unique { get; set; }
      public int Path { get; set; }
      public int ElapsedMin { get; set; }
      public int ActiveMins { get; set; }
      public List<LogMaterial> OutMaterial { get; set; }
    }

    public static ExpectedChange LoadCastingToFace(int pal, int lul, int face, string unique, int path, int cnt, int elapsedMin, int activeMins, out IEnumerable<LogMaterial> mats)
    {
      var e = new ExpectedLoadCastingEvt()
      {
        Pallet = pal,
        LoadStation = lul,
        Face = face,
        Count = cnt,
        Unique = unique,
        Path = path,
        ElapsedMin = elapsedMin,
        ActiveMins = activeMins,
        OutMaterial = new List<LogMaterial>(),
      };
      mats = e.OutMaterial;
      return e;
    }

    private class ExpectedLoadMatsEvt : ExpectedChange
    {
      public int Pallet { get; set; }
      public int LoadStation { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
      public int ElapsedMin { get; set; }
      public int ActiveMins { get; set; }
    }

    public ExpectedChange LoadToFace(int pal, int face, string unique, int lul, int elapsedMin, int activeMins, IEnumerable<LogMaterial> loadingMats, out IEnumerable<LogMaterial> loadedMats)
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
        Pallet = pal,
        LoadStation = lul,
        Material = loadedMats,
        ElapsedMin = elapsedMin,
        ActiveMins = activeMins,
      };
    }

    private class ExpectedRemoveFromQueueEvt : ExpectedChange
    {
      public IEnumerable<LogMaterial> Material { get; set; }
      public string FromQueue { get; set; }
      public int Position { get; set; }
    }

    public static ExpectedChange RemoveFromQueue(string queue, int pos, IEnumerable<LogMaterial> mat)
    {
      return new ExpectedRemoveFromQueueEvt() { Material = mat, FromQueue = queue, Position = pos };
    }

    private class ExpectedUnloadEvt : ExpectedChange
    {
      public int Pallet { get; set; }
      public int LoadStation { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
      public int ElapsedMin { get; set; }
      public int ActiveMins { get; set; }
    }

    public static ExpectedChange UnloadFromFace(int pal, int lul, int elapsedMin, int activeMins, IEnumerable<LogMaterial> mats)
    {
      return new ExpectedUnloadEvt()
      {
        Pallet = pal,
        LoadStation = lul,
        Material = mats,
        ElapsedMin = elapsedMin,
        ActiveMins = activeMins,
      };
    }

    private class ExpectedAddToQueueEvt : ExpectedChange
    {
      public IEnumerable<LogMaterial> Material { get; set; }
      public string ToQueue { get; set; }
      public int Position { get; set; }
    }

    public static ExpectedChange AddToQueue(string queue, int pos, IEnumerable<LogMaterial> mat)
    {
      return new ExpectedAddToQueueEvt()
      {
        Material = mat,
        ToQueue = queue,
        Position = pos
      };
    }

    private class ExpectNewRouteChange : ExpectedChange
    {
      public PalletMaster ExpectedMaster { get; set; }
      public IEnumerable<(int face, string unique, int proc, int path)> Faces { get; set; }
    }

    public static ExpectedChange ExpectNewRoute(int pal, int[] luls, int[] machs, int[] progs, IEnumerable<(int face, string unique, int proc, int path)> faces, int[] unloads = null)
    {
      return new ExpectNewRouteChange()
      {
        ExpectedMaster = new PalletMaster()
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
              UnloadStations = (unloads ?? luls).ToList(),
              CompletedPartCount = 1
            }
          },
        },
        Faces = faces
      };
    }

    public static ExpectedChange ExpectNewRoute(int pal, int[] loads, int[] machs1, int[] progs1, int[] machs2, int[] progs2, int[] unloads, IEnumerable<(int face, string unique, int proc, int path)> faces)
    {
      return new ExpectNewRouteChange()
      {
        ExpectedMaster = new PalletMaster()
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
              LoadStations = loads.ToList()
            },
            new MachiningStep() {
              Machines = machs1.ToList(),
              ProgramNumsToRun = progs1.ToList()
            },
            new MachiningStep() {
              Machines = machs2.ToList(),
              ProgramNumsToRun = progs2.ToList()
            },
            new UnloadStep() {
              UnloadStations = unloads.ToList(),
              CompletedPartCount = 1
            }
          },
        },
        Faces = faces
      };
    }

    private class ExpectRouteIncrementChange : ExpectedChange
    {
      public int Pallet { get; set; }
      public int NewCycleCount { get; set; }
      public IEnumerable<(int face, string unique, int proc, int path)> Faces { get; set; }
    }

    public static ExpectedChange ExpectRouteIncrement(int pal, int newCycleCnt, IEnumerable<(int face, string unique, int proc, int path)> faces = null)
    {
      return new ExpectRouteIncrementChange() { Pallet = pal, NewCycleCount = newCycleCnt, Faces = faces };
    }

    private class ExpectMachineBeginEvent : ExpectedChange
    {
      public int Pallet { get; set; }
      public int Machine { get; set; }
      public int Program { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
    }

    public static ExpectedChange ExpectMachineBegin(int pal, int machine, int program, IEnumerable<LogMaterial> mat)
    {
      return new ExpectMachineBeginEvent()
      {
        Pallet = pal,
        Machine = machine,
        Program = program,
        Material = mat
      };
    }

    private class ExpectMachineEndEvent : ExpectedChange
    {
      public int Pallet { get; set; }
      public int Machine { get; set; }
      public int Program { get; set; }
      public int ElapsedMin { get; set; }
      public int ActiveMin { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
    }

    public static ExpectedChange ExpectMachineEnd(int pal, int mach, int program, int elapsedMin, int activeMin, IEnumerable<LogMaterial> mats)
    {
      return new ExpectMachineEndEvent()
      {
        Pallet = pal,
        Machine = mach,
        Program = program,
        ElapsedMin = elapsedMin,
        ActiveMin = activeMin,
        Material = mats
      };
    }

    private class ExpectPalletCycleChange : ExpectedChange
    {
      public int Pallet { get; set; }
      public int Minutes { get; set; }
    }

    public static ExpectedChange ExpectPalletCycle(int pal, int mins)
    {
      return new ExpectPalletCycleChange()
      {
        Pallet = pal,
        Minutes = mins
      };
    }

    public FakeIccDsl ExpectTransition(IEnumerable<ExpectedChange> expectedChanges, bool expectedUpdates = true)
    {
      var sch = _jobDB.LoadUnarchivedJobs();

      using (var logMonitor = _logDB.Monitor())
      {
        var cellSt = _createLog.BuildCellState(_status, sch);
        cellSt.PalletStateUpdated.Should().Be(expectedUpdates);
        cellSt.Schedule.Should().Be(sch);

        var expectedLogs = new List<LogEntry>();

        var expectedNewRoute = (ExpectNewRouteChange)expectedChanges.FirstOrDefault(e => e is ExpectNewRouteChange);
        var expectIncr = (ExpectRouteIncrementChange)expectedChanges.FirstOrDefault(e => e is ExpectRouteIncrementChange);
        if (expectedNewRoute != null)
        {
          var action = _assign.NewPalletChange(cellSt);
          var pal = expectedNewRoute.ExpectedMaster.PalletNum;
          action.Should().BeEquivalentTo<NewPalletRoute>(new NewPalletRoute()
          {
            NewMaster = expectedNewRoute.ExpectedMaster
          }, options => options
              .Excluding(e => e.PendingID)
              .Excluding(e => e.NewMaster.Comment)
              .RespectingRuntimeTypes()
          );
          _status.Pallets[pal - 1].Master = ((NewPalletRoute)action).NewMaster;
          _status.Pallets[pal - 1].Tracking.CurrentControlNum = 1;
          _status.Pallets[pal - 1].Tracking.CurrentStepNum = 1;
          _expectedFaces[pal] = expectedNewRoute.Faces.ToList();

          expectedLogs.Add(new LogEntry(
            cntr: -1,
            mat: Enumerable.Empty<LogMaterial>(),
            pal: pal.ToString(),
            ty: LogType.GeneralMessage,
            locName: "Message",
            locNum: 1,
            prog: "Assign",
            start: false,
            endTime: _status.TimeOfStatusUTC,
            result: "New Niigata Route",
            endOfRoute: false
          ));
        }
        else if (expectIncr != null)
        {
          var action = _assign.NewPalletChange(cellSt);
          var pal = expectIncr.Pallet;
          action.Should().BeEquivalentTo<UpdatePalletQuantities>(new UpdatePalletQuantities()
          {
            Pallet = pal,
            Priority = _status.Pallets[pal - 1].Master.Priority,
            Cycles = expectIncr.NewCycleCount,
            NoWork = false,
            Skip = false,
            ForLongTool = false
          });
          _status.Pallets[pal - 1].Master.NoWork = false;
          _status.Pallets[pal - 1].Master.RemainingPalletCycles = expectIncr.NewCycleCount;
          _status.Pallets[pal - 1].Tracking.CurrentControlNum = 1;
          _status.Pallets[pal - 1].Tracking.CurrentStepNum = 1;
          if (expectIncr.Faces != null)
          {
            _expectedFaces[pal] = expectIncr.Faces.ToList();
          }
        }
        else
        {
          _assign.NewPalletChange(cellSt).Should().BeNull();
        }

        var evts = logMonitor.OccurredEvents.Select(e => e.Parameters[0]).Cast<LogEntry>();

        foreach (var expected in expectedChanges)
        {
          switch (expected)
          {
            case ExpectPalletCycleChange palletCycleChange:
              expectedLogs.Add(new LogEntry(
                cntr: -1,
                mat: Enumerable.Empty<LogMaterial>(),
                pal: palletCycleChange.Pallet.ToString(),
                ty: LogType.PalletCycle,
                locName: "Pallet Cycle",
                locNum: 1,
                prog: "",
                start: false,
                endTime: _status.TimeOfStatusUTC,
                result: "PalletCycle",
                endOfRoute: false,
                elapsed: TimeSpan.FromMinutes(palletCycleChange.Minutes),
                active: TimeSpan.Zero
              ));
              _status.Pallets[palletCycleChange.Pallet - 1].Master.RemainingPalletCycles -= 1;
              break;

            case ExpectedLoadBegin loadBegin:
              expectedLogs.Add(
                new LogEntry(
                  cntr: -1,
                  mat: Enumerable.Empty<LogMaterial>(),
                  pal: loadBegin.Pallet.ToString(),
                  ty: LogType.LoadUnloadCycle,
                  locName: "L/U",
                  locNum: loadBegin.LoadStation,
                  prog: "LOAD",
                  start: true,
                  endTime: _status.TimeOfStatusUTC,
                  result: "LOAD",
                  endOfRoute: false
              ));
              break;

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
                pal: load.Pallet.ToString(),
                ty: LogType.LoadUnloadCycle,
                locName: "L/U",
                locNum: load.LoadStation,
                prog: "LOAD",
                start: false,
                endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                result: "LOAD",
                endOfRoute: false,
                elapsed: TimeSpan.FromMinutes(load.ElapsedMin),
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
                    Pallet = load.Pallet.ToString(),
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
                pal: load.Pallet.ToString(),
                ty: LogType.LoadUnloadCycle,
                locName: "L/U",
                locNum: load.LoadStation,
                prog: "LOAD",
                start: false,
                endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                result: "LOAD",
                endOfRoute: false,
                elapsed: TimeSpan.FromMinutes(load.ElapsedMin),
                active: TimeSpan.FromMinutes(load.ActiveMins)
              ));
              break;

            case ExpectedRemoveFromQueueEvt removeFromQueueEvt:
              expectedLogs.AddRange(removeFromQueueEvt.Material.Select(m => new LogEntry(
                cntr: -1,
                mat: new[] { new LogMaterial(matID: m.MaterialID, uniq: m.JobUniqueStr, proc: m.Process, part: m.PartName,
                                            numProc: m.NumProcesses, serial: m.Serial, workorder: "", face: m.Face) },
                pal: "",
                ty: LogType.RemoveFromQueue,
                locName: removeFromQueueEvt.FromQueue,
                locNum: removeFromQueueEvt.Position,
                prog: "",
                start: false,
                endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                result: "",
                endOfRoute: false

              )));
              break;


            case ExpectedUnloadEvt unload:
              expectedLogs.Add(new LogEntry(
                cntr: -1,
                mat: unload.Material,
                pal: unload.Pallet.ToString(),
                ty: LogType.LoadUnloadCycle,
                locName: "L/U",
                locNum: unload.LoadStation,
                prog: "UNLOAD",
                start: false,
                endTime: _status.TimeOfStatusUTC,
                result: "UNLOAD",
                endOfRoute: true,
                elapsed: TimeSpan.FromMinutes(unload.ElapsedMin),
                active: TimeSpan.FromMinutes(unload.ActiveMins)
              ));
              break;

            case ExpectedAddToQueueEvt addToQueueEvt:
              expectedLogs.AddRange(addToQueueEvt.Material.Select(m => new LogEntry(
                cntr: -1,
                mat: new[] { new LogMaterial(matID: m.MaterialID, uniq: m.JobUniqueStr, proc: m.Process, part: m.PartName,
                                            numProc: m.NumProcesses, serial: m.Serial, workorder: "", face: m.Face) },
                pal: "",
                ty: LogType.AddToQueue,
                locName: addToQueueEvt.ToQueue,
                locNum: addToQueueEvt.Position,
                prog: "",
                start: false,
                endTime: _status.TimeOfStatusUTC,
                result: "",
                endOfRoute: false

              )));
              break;

            case ExpectMachineBeginEvent machBegin:
              expectedLogs.Add(
                new LogEntry(
                  cntr: -1,
                  mat: machBegin.Material,
                  pal: machBegin.Pallet.ToString(),
                  ty: LogType.MachineCycle,
                  locName: "MC",
                  locNum: machBegin.Machine,
                  prog: machBegin.Program.ToString(),
                  start: true,
                  endTime: _status.TimeOfStatusUTC,
                  result: "",
                  endOfRoute: false
              ));
              break;

            case ExpectMachineEndEvent machEnd:
              expectedLogs.Add(
                new LogEntry(
                  cntr: -1,
                  mat: machEnd.Material,
                  pal: machEnd.Pallet.ToString(),
                  ty: LogType.MachineCycle,
                  locName: "MC",
                  locNum: machEnd.Machine,
                  prog: machEnd.Program.ToString(),
                  start: false,
                  endTime: _status.TimeOfStatusUTC,
                  result: "",
                  endOfRoute: false,
                  elapsed: TimeSpan.FromMinutes(machEnd.ElapsedMin),
                  active: TimeSpan.FromMinutes(machEnd.ActiveMin)
              ));
              break;
          }
        }

        if (expectedLogs.Any())
        {
          logMonitor.Should().Raise("NewLogEntry");
        }
        else
        {
          logMonitor.Should().NotRaise("NewLogEntry");
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