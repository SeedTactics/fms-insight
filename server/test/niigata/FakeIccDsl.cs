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
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using BlackMaple.MachineFramework;
using FluentAssertions;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class FakeIccDsl : IDisposable
  {
    private RepositoryConfig _logDBCfg;
    private IAssignPallets _assign;
    private CreateCellState _createLog;
    private NiigataStatus _status;
    private FMSSettings _settings;
    private SerialSettings _serialSt;
    private NiigataStationNames _statNames;
    public NiigataStationNames StatNames => _statNames;

    private List<InProcessMaterial> _expectedLoadCastings = new List<InProcessMaterial>();
    private Dictionary<long, InProcessMaterial> _expectedMaterial = new Dictionary<long, InProcessMaterial>(); //key is matId
    private Dictionary<
      int,
      List<(int face, string unique, int proc, int path, IEnumerable<ProgramsForProcess> progs)>
    > _expectedFaces =
      new Dictionary<
        int,
        List<(int face, string unique, int proc, int path, IEnumerable<ProgramsForProcess> progs)>
      >(); // key is pallet
    private Dictionary<string, int> _expectedStartedQty = new Dictionary<string, int>();
    private List<ProgramRevision> _expectedOldPrograms = new List<ProgramRevision>();
    private Dictionary<string, Dictionary<(int proc, int path), int>> _expectedCompleted =
      new Dictionary<string, Dictionary<(int proc, int path), int>>();
    private Dictionary<string, ImmutableList<ImmutableList<long>>> _expectedPrecedence =
      new Dictionary<string, ImmutableList<ImmutableList<long>>>();
    private Dictionary<int, string> _expectedPalletAlarms = new Dictionary<int, string>();
    private HashSet<int> _expectedMachineAlarms = new HashSet<int>();

    public FakeIccDsl(int numPals, int numMachines)
    {
      _serialSt = new SerialSettings()
      {
        SerialType = SerialType.AssignOneSerialPerMaterial,
        ConvertMaterialIDToSerial = (m) => SerialSettings.ConvertToBase62(m, 10)
      };
      _settings = new FMSSettings() { QuarantineQueue = "Quarantine" };
      _settings.Queues.Add("thequeue", new MachineFramework.QueueInfo());
      _settings.Queues.Add("qqq", new MachineFramework.QueueInfo());
      _settings.Queues.Add("Quarantine", new MachineFramework.QueueInfo());

      _logDBCfg = RepositoryConfig.InitializeMemoryDB(_serialSt);

      _statNames = new NiigataStationNames()
      {
        ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
        IccMachineToJobMachNames = Enumerable
          .Range(1, numMachines)
          .ToDictionary(mc => mc, mc => (group: "TestMC", num: 100 + mc))
      };

      var machConn = NSubstitute.Substitute.For<ICncMachineConnection>();

      _assign = new MultiPalletAssign(
        new IAssignPallets[]
        {
          new AssignNewRoutesOnPallets(_statNames),
          new SizedQueues(
            new Dictionary<string, QueueInfo>()
            {
              {
                "sizedQ",
                new QueueInfo() { MaxSizeBeforeStopUnloading = 1 }
              }
            }
          )
        }
      );
      _createLog = new CreateCellState(_settings, _statNames, machConn);

      _status = new NiigataStatus();
      _status.TimeOfStatusUTC = DateTime.UtcNow.AddDays(-1);
      _status.Programs = new Dictionary<int, ProgramEntry>();

      _status.Machines = new Dictionary<int, MachineStatus>();
      for (int mach = 1; mach <= numMachines; mach++)
      {
        _status.Machines.Add(
          mach,
          new MachineStatus()
          {
            MachineNumber = mach,
            Machining = false,
            CurrentlyExecutingProgram = 0,
            FMSLinkMode = true,
            Alarm = false
          }
        );
      }

      _status.Pallets = new List<PalletStatus>();
      var rng = new Random();
      for (int pal = 1; pal <= numPals; pal++)
      {
        _status.Pallets.Add(
          new PalletStatus()
          {
            Master = new PalletMaster()
            {
              PalletNum = pal,
              NoWork = rng.NextDouble() > 0.5 // RouteInvalid is true, so all pallets should be considered empty
            },
            CurStation = NiigataStationNum.Buffer(pal),
            Tracking = new TrackingInfo() { RouteInvalid = true, Alarm = false }
          }
        );
        _expectedFaces[pal] =
          new List<(int face, string unique, int proc, int path, IEnumerable<ProgramsForProcess> progs)>();
      }
    }

    public void Dispose()
    {
      _logDBCfg.CloseMemoryConnection();
    }

    #region Niigata Status
    public FakeIccDsl MoveToBuffer(int pal, int buff)
    {
      _status.Pallets[pal - 1].CurStation = NiigataStationNum.Buffer(buff);
      return this;
    }

    public FakeIccDsl MoveToMachineQueue(int pal, int mach)
    {
      _status.Pallets[pal - 1].CurStation = NiigataStationNum.MachineQueue(mach, _statNames);
      return this;
    }

    public FakeIccDsl MoveToMachineOutboundQueue(int pal, int mach)
    {
      _status.Pallets[pal - 1].CurStation = NiigataStationNum.MachineOutboundQueue(mach, _statNames);
      return this;
    }

    public FakeIccDsl MoveToMachine(int pal, int mach)
    {
      _status.Pallets[pal - 1].CurStation = NiigataStationNum.Machine(mach, _statNames);
      return this;
    }

    public FakeIccDsl MoveToLoad(int pal, int lul)
    {
      _status.Pallets[pal - 1].CurStation = NiigataStationNum.LoadStation(lul);
      return this;
    }

    public FakeIccDsl MoveToCart(int pal)
    {
      _status.Pallets[pal - 1].CurStation = NiigataStationNum.Cart();
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

    public FakeIccDsl SetExecutedStationNum(int pal, IEnumerable<NiigataStationNum> nums)
    {
      _status.Pallets[pal - 1].Tracking.ExecutedStationNumber = nums.ToList();
      return this;
    }

    public FakeIccDsl SetBeforeReclamp(int pal, int reclampStepOffset = 0)
    {
      var p = _status.Pallets[pal - 1];
      var step = 1;
      foreach (var s in p.Master.Routes)
      {
        if (s is ReclampStep)
        {
          if (reclampStepOffset == 0)
          {
            break;
          }
          else
          {
            reclampStepOffset -= 1;
          }
        }
        step += 1;
      }
      p.Tracking.CurrentStepNum = step;
      p.Tracking.CurrentControlNum = step * 2 - 1;
      return this;
    }

    public FakeIccDsl SetAfterReclamp(int pal, int reclampStepOffset = 0)
    {
      var p = _status.Pallets[pal - 1];
      var step = 1;
      foreach (var s in p.Master.Routes)
      {
        if (s is ReclampStep)
        {
          if (reclampStepOffset == 0)
          {
            break;
          }
          else
          {
            reclampStepOffset -= 1;
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

    public FakeIccDsl SetPalletAlarm(
      int pal,
      bool alarm,
      PalletAlarmCode code = PalletAlarmCode.NoAlarm,
      string alarmMsg = ""
    )
    {
      _status.Pallets[pal - 1].Tracking.Alarm = alarm;
      _status.Pallets[pal - 1].Tracking.AlarmCode = code;
      if (alarm)
      {
        _expectedPalletAlarms[pal] = alarmMsg;
      }
      else
      {
        _expectedPalletAlarms.Remove(pal);
      }
      return this;
    }

    public FakeIccDsl SetManualControl(int pal, bool manual)
    {
      if (manual)
      {
        _expectedFaces[pal] =
          new List<(int face, string unique, int proc, int path, IEnumerable<ProgramsForProcess> progs)>();
      }
      _status.Pallets[pal - 1].Master.Comment = manual ? "aaa Manual yyy" : "";
      return this;
    }

    public FakeIccDsl SetMachAlarm(int mc, bool link, bool alarm)
    {
      _status.Machines[mc].FMSLinkMode = link;
      _status.Machines[mc].Alarm = alarm;
      if (alarm)
      {
        _expectedMachineAlarms.Add(mc);
      }
      else
      {
        _expectedMachineAlarms.Remove(mc);
      }
      return this;
    }

    public FakeIccDsl StartMachine(int mach, int program)
    {
      // ICC sometimes sets Machining = true and sometimes not, so Insight should check only program is non-zero
      //_status.Machines[mach].Machining = true;
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

    public FakeIccDsl OverrideRoute(
      int pal,
      string comment,
      bool noWork,
      IEnumerable<int> luls,
      IEnumerable<int> machs,
      IEnumerable<int> progs,
      IEnumerable<int> machs2 = null,
      IEnumerable<int> progs2 = null,
      IEnumerable<(
        int face,
        string unique,
        int proc,
        int path,
        IEnumerable<ProgramsForProcess> progs
      )> faces = null
    )
    {
      var routes = new List<RouteStep>();
      routes.Add(new LoadStep() { LoadStations = luls.ToList() });
      routes.Add(new MachiningStep() { Machines = machs.ToList(), ProgramNumsToRun = progs.ToList() });

      if (machs2 != null && progs2 != null)
      {
        routes.Add(new MachiningStep() { Machines = machs2.ToList(), ProgramNumsToRun = progs2.ToList() });
      }

      routes.Add(new UnloadStep() { UnloadStations = luls.ToList() });

      _status.Pallets[pal - 1].Tracking.RouteInvalid = false;
      _status.Pallets[pal - 1].Master = new PalletMaster()
      {
        PalletNum = pal,
        Comment = comment,
        RemainingPalletCycles = 1,
        NoWork = noWork,
        Routes = routes
      };
      _expectedFaces[pal] =
        faces == null
          ? new List<(int face, string unique, int proc, int path, IEnumerable<ProgramsForProcess> progs)>()
          : faces.ToList();

      return this;
    }

    public FakeIccDsl OverrideRouteMachines(int pal, int stepIdx, IEnumerable<int> machs)
    {
      var mach = (MachiningStep)_status.Pallets[pal - 1].Master.Routes[stepIdx];
      mach.Machines = machs.ToList();
      return this;
    }

    public FakeIccDsl SetIccProgram(int iccProg, string comment)
    {
      _status.Programs[iccProg] = new ProgramEntry()
      {
        ProgramNum = iccProg,
        Comment = comment,
        CycleTime = TimeSpan.Zero,
        Tools = new List<int>()
      };
      return this;
    }

    public FakeIccDsl RemoveIccProgram(int iccProg)
    {
      _status.Programs.Remove(iccProg);
      return this;
    }
    #endregion

    #region Material
    public FakeIccDsl AddUnallocatedCasting(
      string queue,
      string rawMatName,
      out LogMaterial mat,
      string workorder = null,
      int numProc = 1
    )
    {
      using var _logDB = _logDBCfg.OpenConnection();
      var matId = _logDB.AllocateMaterialIDForCasting(rawMatName);
      if (_serialSt.SerialType == SerialType.AssignOneSerialPerMaterial)
      {
        _logDB.RecordSerialForMaterialID(
          new EventLogMaterial()
          {
            MaterialID = matId,
            Process = 0,
            Face = ""
          },
          _serialSt.ConvertMaterialIDToSerial(matId),
          _status.TimeOfStatusUTC
        );
      }
      if (!string.IsNullOrEmpty(workorder))
      {
        _logDB.RecordWorkorderForMaterialID(
          new EventLogMaterial()
          {
            MaterialID = matId,
            Process = 0,
            Face = ""
          },
          workorder,
          _status.TimeOfStatusUTC
        );
      }
      var addLog = _logDB.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = matId,
          Process = 0,
          Face = ""
        },
        queue,
        -1,
        operatorName: "theoperator",
        reason: "TestAddUnallocated",
        timeUTC: _status.TimeOfStatusUTC
      );
      _expectedMaterial[matId] = new InProcessMaterial()
      {
        MaterialID = matId,
        JobUnique = "",
        PartName = rawMatName,
        Process = 0,
        Path = 1,
        Serial = _serialSt.ConvertMaterialIDToSerial(matId),
        WorkorderId = workorder,
        SignaledInspections = ImmutableList<string>.Empty,
        QuarantineAfterUnload = null,
        Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
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
        part: rawMatName,
        numProc: numProc,
        serial: _serialSt.ConvertMaterialIDToSerial(matId),
        workorder: workorder ?? "",
        face: ""
      );
      return this;
    }

    public FakeIccDsl AddAllocatedMaterial(
      string queue,
      string uniq,
      string part,
      int proc,
      int path,
      int numProc,
      out LogMaterial mat,
      string workorder = null
    )
    {
      using var _logDB = _logDBCfg.OpenConnection();
      var matId = _logDB.AllocateMaterialID(unique: uniq, part: part, numProc: numProc);
      if (_serialSt.SerialType == SerialType.AssignOneSerialPerMaterial)
      {
        _logDB.RecordSerialForMaterialID(
          new EventLogMaterial()
          {
            MaterialID = matId,
            Process = proc,
            Face = ""
          },
          _serialSt.ConvertMaterialIDToSerial(matId),
          _status.TimeOfStatusUTC
        );
      }
      if (!string.IsNullOrEmpty(workorder))
      {
        _logDB.RecordWorkorderForMaterialID(
          new EventLogMaterial()
          {
            MaterialID = matId,
            Process = proc,
            Face = ""
          },
          workorder,
          _status.TimeOfStatusUTC
        );
      }
      var addLog = _logDB.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = matId,
          Process = proc,
          Face = ""
        },
        queue,
        -1,
        operatorName: "theoperator",
        reason: "TestAddCasting",
        timeUTC: _status.TimeOfStatusUTC
      );
      _expectedMaterial[matId] = new InProcessMaterial()
      {
        MaterialID = matId,
        JobUnique = uniq,
        PartName = part,
        Process = proc,
        Path = path,
        Serial = _serialSt.ConvertMaterialIDToSerial(matId),
        SignaledInspections = ImmutableList<string>.Empty,
        QuarantineAfterUnload = null,
        WorkorderId = workorder,
        Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.InQueue,
          CurrentQueue = queue,
          QueuePosition = addLog.First().LocationNum
        }
      };
      mat = new LogMaterial(
        matID: matId,
        uniq: uniq,
        proc: proc,
        part: part,
        numProc: numProc,
        serial: _serialSt.ConvertMaterialIDToSerial(matId),
        workorder: workorder ?? "",
        face: ""
      );
      return this;
    }

    public FakeIccDsl SetInQueue(LogMaterial mat, string queue, int pos)
    {
      using var _logDB = _logDBCfg.OpenConnection();
      _logDB.RecordAddMaterialToQueue(
        new EventLogMaterial()
        {
          MaterialID = mat.MaterialID,
          Process = mat.Process,
          Face = ""
        },
        queue,
        pos,
        operatorName: "theoperator",
        reason: "TestAddCasting",
        timeUTC: _status.TimeOfStatusUTC
      );
      _expectedMaterial[mat.MaterialID] %= m =>
        m.SetLocation(
          new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = queue,
            QueuePosition = pos
          }
        );
      return this;
    }

    public FakeIccDsl RemoveFromQueue(IEnumerable<LogMaterial> mats)
    {
      using var _logDB = _logDBCfg.OpenConnection();
      foreach (var mat in mats)
      {
        _logDB.RecordRemoveMaterialFromAllQueues(
          EventLogMaterial.FromLogMat(mat),
          operatorName: null,
          _status.TimeOfStatusUTC
        );
        _expectedMaterial.Remove(mat.MaterialID);
      }
      return this;
    }

    public FakeIccDsl SignalForQuarantine(IEnumerable<LogMaterial> mats, int pal, string q)
    {
      using var _logDB = _logDBCfg.OpenConnection();
      foreach (var mat in mats)
      {
        _logDB.SignalMaterialForQuarantine(
          EventLogMaterial.FromLogMat(mat),
          pallet: pal,
          queue: q,
          operatorName: null,
          reason: null,
          timeUTC: _status.TimeOfStatusUTC
        );
      }
      return this;
    }

    public FakeIccDsl SetExpectedLoadCastings(
      IEnumerable<(string unique, string part, int pal, int path, int face)> castings
    )
    {
      _expectedLoadCastings = castings
        .Select(c => new InProcessMaterial()
        {
          MaterialID = -1,
          JobUnique = c.unique,
          PartName = c.part,
          Process = 0,
          Path = c.path,
          SignaledInspections = ImmutableList<string>.Empty,
          QuarantineAfterUnload = null,
          Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = c.pal,
            LoadOntoFace = c.face,
            ProcessAfterLoad = 1,
            PathAfterLoad = c.path,
          },
          Location = new InProcessMaterialLocation() { Type = InProcessMaterialLocation.LocType.Free }
        })
        .ToList();
      return this;
    }

    public FakeIccDsl SetExpectedCastingElapsedLoadUnloadTime(int pal, int mins)
    {
      for (int i = 0; i < _expectedLoadCastings.Count; i++)
      {
        var m = _expectedLoadCastings[i];
        if (m.Action.LoadOntoPalletNum == pal)
        {
          _expectedLoadCastings[i] =
            m % (mat => mat.Action.ElapsedLoadUnloadTime = TimeSpan.FromMinutes(mins));
        }
      }
      return this;
    }

    public FakeIccDsl ClearExpectedLoadCastings()
    {
      _expectedLoadCastings = new List<InProcessMaterial>();
      return this;
    }

    public FakeIccDsl UpdateExpectedMaterial(long matId, Action<IInProcessMaterialDraft> f)
    {
      _expectedMaterial[matId] %= f;
      return this;
    }

    public FakeIccDsl UpdateExpectedMaterial(IEnumerable<long> matIds, Action<IInProcessMaterialDraft> f)
    {
      foreach (var matId in matIds)
      {
        _expectedMaterial[matId] %= f;
      }
      return this;
    }

    public FakeIccDsl UpdateExpectedMaterial(IEnumerable<LogMaterial> mats, Action<IInProcessMaterialDraft> f)
    {
      foreach (var mat in mats)
      {
        _expectedMaterial[mat.MaterialID] %= f;
      }
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

    public static IEnumerable<LogMaterial> ClearFaces(IEnumerable<LogMaterial> mats)
    {
      return mats.Select(m => new LogMaterial(
          matID: m.MaterialID,
          uniq: m.JobUniqueStr,
          proc: m.Process,
          part: m.PartName,
          numProc: m.NumProcesses,
          serial: m.Serial,
          workorder: m.Workorder,
          face: ""
        ))
        .ToList();
    }

    public static IEnumerable<LogMaterial> SetProc(int proc, IEnumerable<LogMaterial> mats)
    {
      return mats.Select(m => new LogMaterial(
          matID: m.MaterialID,
          uniq: m.JobUniqueStr,
          proc: proc,
          part: m.PartName,
          numProc: m.NumProcesses,
          serial: m.Serial,
          workorder: m.Workorder,
          face: m.Face
        ))
        .ToList();
    }

    public FakeIccDsl SwapMaterial(
      int pal,
      long matOnPalId,
      long matToAddId,
      out IEnumerable<LogMaterial> newMat
    )
    {
      using var _logDB = _logDBCfg.OpenConnection();
      var swap = _logDB.SwapMaterialInCurrentPalletCycle(
        pallet: pal,
        oldMatId: matOnPalId,
        newMatId: matToAddId,
        operatorName: null,
        quarantineQueue: _settings.QuarantineQueue,
        timeUTC: _status.TimeOfStatusUTC
      );

      var oldMatOnPal = _expectedMaterial[matOnPalId];
      var oldMatToAdd = _expectedMaterial[matToAddId];
      var queue = oldMatToAdd.Location.CurrentQueue;

      _expectedMaterial[matOnPalId] %= mat =>
      {
        mat.JobUnique = oldMatToAdd.JobUnique;
        mat.Process = oldMatToAdd.Process;
        mat.Path = oldMatToAdd.Path;
        mat.SetLocation(
          new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = queue,
            QueuePosition = _expectedMaterial
              .Where(n =>
                n.Value.Location.Type == InProcessMaterialLocation.LocType.InQueue
                && n.Value.Location.CurrentQueue == queue
              )
              .Select(n => n.Value.Location.QueuePosition)
              .Max()
          }
        );
      };

      _expectedMaterial[matToAddId] %= mat =>
      {
        mat.JobUnique = oldMatOnPal.JobUnique;
        mat.Process = oldMatOnPal.Process;
        mat.Path = oldMatOnPal.Path;
        mat.SetLocation(
          new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.OnPallet,
            PalletNum = pal,
            Face = oldMatOnPal.Location.Face
          }
        );
      };

      int numProc = _logDB.LoadJob(oldMatOnPal.JobUnique).Processes.Count;

      newMat = new[]
      {
        new LogMaterial(
          matID: matToAddId,
          uniq: oldMatOnPal.JobUnique,
          proc: oldMatOnPal.Process,
          part: oldMatOnPal.PartName,
          numProc: numProc,
          serial: oldMatToAdd.Serial,
          workorder: oldMatToAdd.WorkorderId ?? "",
          face: oldMatOnPal.Location.Face.ToString()
        )
      };

      return this;
    }
    #endregion

    #region Jobs
    public FakeIccDsl IncrJobStartedCnt(string unique, int path, int cnt = 1)
    {
      _expectedStartedQty[unique] += cnt;
      return this;
    }

    public FakeIccDsl RemoveJobStartedCnt(string unique)
    {
      _expectedStartedQty.Remove(unique);
      return this;
    }

    public FakeIccDsl SetJobStartedCnt(string unique, int cnt)
    {
      _expectedStartedQty[unique] = cnt;
      return this;
    }

    public FakeIccDsl IncrJobCompletedCnt(string unique, int proc, int path, int cnt = 1)
    {
      _expectedCompleted[unique][(proc: proc, path: path)] += cnt;
      return this;
    }

    public FakeIccDsl AddJobs(
      IEnumerable<(Job, int[][])> jobs,
      IEnumerable<(string prog, long rev)> progs = null,
      IEnumerable<Workorder> workorders = null
    )
    {
      using var _logDB = _logDBCfg.OpenConnection();
      if (progs == null)
        progs = Enumerable.Empty<(string prog, long rev)>();
      var newJ = new NewJobs()
      {
        ScheduleId = DateTime.UtcNow.Ticks.ToString(),
        Jobs = jobs.Select(j => j.Item1).ToImmutableList(),
        Programs = progs
          .Select(p => new MachineFramework.NewProgramContent()
          {
            ProgramName = p.prog,
            Revision = p.rev,
            Comment = "Comment " + p.prog + " rev" + p.rev.ToString(),
            ProgramContent = "ProgramCt " + p.prog + " rev" + p.rev.ToString()
          })
          .ToImmutableList(),
        CurrentUnfilledWorkorders = workorders?.ToImmutableList()
      };
      _logDB.AddJobs(newJ, null, addAsCopiedToSystem: true);
      foreach (var x in jobs)
      {
        var j = x.Item1;
        var precedence = x.Item2;
        _expectedStartedQty[j.UniqueStr] = 0;
        var completed = new Dictionary<(int proc, int path), int>();
        for (var proc = 1; proc <= j.Processes.Count; proc++)
        {
          for (var path = 1; path <= j.Processes[proc - 1].Paths.Count; path++)
          {
            completed[(proc, path)] = 0;
          }
        }
        _expectedCompleted[j.UniqueStr] = completed;
        _expectedPrecedence[j.UniqueStr] = precedence
          .Select(ps => ps.Select(p => (long)p).ToImmutableList())
          .ToImmutableList();
      }
      return this;
    }

    public FakeIccDsl SetJobPrecedence(string uniq, int[][] precs)
    {
      _expectedPrecedence[uniq] = precs
        .Select(ps => ps.Select(p => (long)p).ToImmutableList())
        .ToImmutableList();
      return this;
    }

    public FakeIccDsl AddJobDecrement(string uniq)
    {
      using var _logDB = _logDBCfg.OpenConnection();
      _logDB.AddNewDecrement(
        new[]
        {
          new NewDecrementQuantity()
          {
            JobUnique = uniq,
            Part = "part",
            Quantity = 1
          }
        }
      );
      return this;
    }

    public FakeIccDsl ArchiveJob(string uniq)
    {
      using var _logDB = _logDBCfg.OpenConnection();
      _logDB.ArchiveJob(uniq);
      return this;
    }

    public FakeIccDsl ReplaceWorkorders(
      IEnumerable<Workorder> workorders,
      IEnumerable<MachineFramework.NewProgramContent> programs
    )
    {
      using var _logDB = _logDBCfg.OpenConnection();
      _logDB.AddJobs(
        new NewJobs()
        {
          ScheduleId = DateTime.UtcNow.Ticks.ToString(),
          Jobs = ImmutableList<Job>.Empty,
          Programs = programs.ToImmutableList(),
          CurrentUnfilledWorkorders = workorders.ToImmutableList()
        },
        null,
        addAsCopiedToSystem: true
      );
      return this;
    }

    public static (Job, int[][]) CreateOneProcOnePathJob(
      string unique,
      string part,
      int qty,
      int priority,
      int partsPerPal,
      int[] pals,
      int[] luls,
      int[] machs,
      string prog,
      long? progRev,
      int loadMins,
      int machMins,
      int unloadMins,
      string fixture,
      int face,
      string queue = null,
      string casting = null,
      bool manual = false,
      int precedence = 0
    )
    {
      return (
        new Job()
        {
          UniqueStr = unique,
          PartName = part,
          ManuallyCreated = manual,
          RouteStartUTC = DateTime.UtcNow.AddHours(-priority),
          Cycles = qty,
          RouteEndUTC = DateTime.MinValue,
          Archived = false,
          Processes = ImmutableList.Create(
            new ProcessInfo()
            {
              Paths = ImmutableList.Create(
                new ProcPathInfo()
                {
                  Load = luls.ToImmutableList(),
                  Unload = luls.ToImmutableList(),
                  ExpectedLoadTime = TimeSpan.FromMinutes(loadMins),
                  ExpectedUnloadTime = TimeSpan.FromMinutes(unloadMins),
                  PartsPerPallet = partsPerPal,
                  PalletNums = pals.ToImmutableList(),
                  Fixture = fixture,
                  Face = face,
                  InputQueue = queue,
                  Casting = casting,
                  SimulatedStartingUTC = DateTime.MinValue,
                  SimulatedAverageFlowTime = TimeSpan.Zero,
                  Stops = ImmutableList.Create(
                    new MachiningStop()
                    {
                      StationGroup = "TestMC",
                      Program = prog,
                      ProgramRevision = progRev,
                      ExpectedCycleTime = TimeSpan.FromMinutes(machMins),
                      Stations = machs.Select(m => m + 100).ToImmutableList(),
                    }
                  )
                }
              )
            }
          )
        },
        new int[][] { new int[] { precedence } }
      );
    }

    public static (Job, int[][]) CreateOneProcOnePathMultiStepJob(
      string unique,
      string part,
      int qty,
      int priority,
      int partsPerPal,
      int[] pals,
      int[] luls,
      int[] machs1,
      string prog1,
      long? prog1Rev,
      int[] machs2,
      string prog2,
      long? prog2Rev,
      int[] reclamp,
      int loadMins,
      int machMins1,
      int machMins2,
      int reclampMins,
      int unloadMins,
      string fixture,
      int face,
      string queue = null,
      int precedence = 0
    )
    {
      var stops = ImmutableList.CreateBuilder<MachiningStop>();

      stops.Add(
        new MachiningStop()
        {
          StationGroup = "TestMC",
          Program = prog1,
          ProgramRevision = prog1Rev,
          ExpectedCycleTime = TimeSpan.FromMinutes(machMins1),
          Stations = machs1.Select(m => m + 100).ToImmutableList(),
        }
      );

      stops.Add(
        new MachiningStop()
        {
          StationGroup = "TestMC",
          Program = prog2,
          ProgramRevision = prog2Rev,
          ExpectedCycleTime = TimeSpan.FromMinutes(machMins2),
          Stations = machs2.Select(m => m + 100).ToImmutableList(),
        }
      );

      if (reclamp.Any())
      {
        stops.Add(
          new MachiningStop()
          {
            StationGroup = "TestReclamp",
            ExpectedCycleTime = TimeSpan.FromMinutes(reclampMins),
            Stations = reclamp.ToImmutableList(),
          }
        );
      }

      return (
        new Job()
        {
          UniqueStr = unique,
          PartName = part,
          RouteStartUTC = DateTime.UtcNow.AddHours(-priority),
          Cycles = qty,
          RouteEndUTC = DateTime.MinValue,
          Archived = false,
          Processes = ImmutableList.Create(
            new ProcessInfo()
            {
              Paths = ImmutableList.Create(
                new ProcPathInfo()
                {
                  Load = luls.ToImmutableList(),
                  Unload = luls.ToImmutableList(),
                  ExpectedLoadTime = TimeSpan.FromMinutes(loadMins),
                  ExpectedUnloadTime = TimeSpan.FromMinutes(unloadMins),
                  PartsPerPallet = partsPerPal,
                  PalletNums = pals.ToImmutableList(),
                  Fixture = fixture,
                  Face = face,
                  InputQueue = queue,
                  SimulatedStartingUTC = DateTime.MinValue,
                  SimulatedAverageFlowTime = TimeSpan.Zero,
                  Stops = stops.ToImmutable()
                }
              )
            }
          )
        },
        new int[][] { new[] { precedence } }
      );
    }

    public static (Job, int[][]) CreateMultiProcSamePalletJob(
      string unique,
      string part,
      int qty,
      int priority,
      int partsPerPal,
      int[] pals,
      int[] luls,
      int[] machs,
      string prog1,
      long? prog1Rev,
      string prog2,
      long? prog2Rev,
      int loadMins1,
      int machMins1,
      int unloadMins1,
      int loadMins2,
      int machMins2,
      int unloadMins2,
      string fixture,
      int face1,
      int face2,
      int prec1 = 0,
      int prec2 = 1
    )
    {
      return (
        new Job()
        {
          UniqueStr = unique,
          PartName = part,
          RouteStartUTC = DateTime.UtcNow.AddHours(-priority),
          Cycles = qty,
          RouteEndUTC = DateTime.MinValue,
          Archived = false,
          Processes = ImmutableList.Create(
            new ProcessInfo()
            {
              Paths = ImmutableList.Create(
                new ProcPathInfo()
                {
                  SimulatedStartingUTC = DateTime.UtcNow.AddHours(-priority).AddMinutes(10),
                  Load = luls.ToImmutableList(),
                  Unload = luls.ToImmutableList(),
                  ExpectedLoadTime = TimeSpan.FromMinutes(loadMins1),
                  ExpectedUnloadTime = TimeSpan.FromMinutes(unloadMins1),
                  PartsPerPallet = partsPerPal,
                  PalletNums = pals.ToImmutableList(),
                  Fixture = fixture,
                  Face = face1,
                  SimulatedAverageFlowTime = TimeSpan.Zero,
                  Stops = ImmutableList.Create(
                    new MachiningStop()
                    {
                      StationGroup = "TestMC",
                      Program = prog1,
                      ProgramRevision = prog1Rev,
                      ExpectedCycleTime = TimeSpan.FromMinutes(machMins1),
                      Stations = machs.Select(m => m + 100).ToImmutableList(),
                    }
                  )
                }
              )
            },
            new ProcessInfo()
            {
              Paths = ImmutableList.Create(
                new ProcPathInfo()
                {
                  SimulatedStartingUTC = DateTime.UtcNow.AddHours(-priority).AddMinutes(20),
                  Load = luls.ToImmutableList(),
                  Unload = luls.ToImmutableList(),
                  ExpectedLoadTime = TimeSpan.FromMinutes(loadMins2),
                  ExpectedUnloadTime = TimeSpan.FromMinutes(unloadMins2),
                  PartsPerPallet = partsPerPal,
                  SimulatedAverageFlowTime = TimeSpan.Zero,
                  PalletNums = pals.ToImmutableList(),
                  Fixture = fixture,
                  Face = face2,
                  Stops = ImmutableList.Create(
                    new MachiningStop()
                    {
                      StationGroup = "TestMC",
                      Program = prog2,
                      ProgramRevision = prog2Rev,
                      ExpectedCycleTime = TimeSpan.FromMinutes(machMins2),
                      Stations = machs.Select(m => m + 100).ToImmutableList(),
                    }
                  )
                }
              )
            }
          )
        },
        new[] { new[] { prec1 }, new[] { prec2 } }
      );
    }

    public static (Job, int[][]) CreateMultiProcSeparatePalletJob(
      string unique,
      string part,
      int qty,
      int priority,
      int partsPerPal,
      int[] pals1,
      int[] pals2,
      int[] load1,
      int[] load2,
      int[] unload1,
      int[] unload2,
      int[] machs,
      string prog1,
      long? prog1Rev,
      string prog2,
      long? prog2Rev,
      int loadMins1,
      int machMins1,
      int unloadMins1,
      int loadMins2,
      int machMins2,
      int unloadMins2,
      string fixture,
      string transQ,
      int[] reclamp1 = null,
      int reclamp1Mins = 1,
      int[] reclamp2 = null,
      int reclamp2Min = 1,
      string rawMatName = null,
      string castingQ = null,
      int prec1 = 0,
      int prec2 = 1
    )
    {
      var stops1 = ImmutableList.CreateBuilder<MachiningStop>();
      var stops2 = ImmutableList.CreateBuilder<MachiningStop>();

      stops1.Add(
        new MachiningStop()
        {
          StationGroup = "TestMC",
          Program = prog1,
          ProgramRevision = prog1Rev,
          ExpectedCycleTime = TimeSpan.FromMinutes(machMins1),
          Stations = machs.Select(m => m + 100).ToImmutableList(),
        }
      );

      if (reclamp1 != null && reclamp1.Any())
      {
        stops1.Add(
          new MachiningStop()
          {
            StationGroup = "TestReclamp",
            ExpectedCycleTime = TimeSpan.FromMinutes(reclamp1Mins),
            Stations = reclamp1.ToImmutableList(),
          }
        );
      }

      if (reclamp2 != null && reclamp2.Any())
      {
        stops2.Add(
          new MachiningStop()
          {
            StationGroup = "TestReclamp",
            ExpectedCycleTime = TimeSpan.FromMinutes(reclamp2Min),
            Stations = reclamp2.ToImmutableList(),
          }
        );
      }

      stops2.Add(
        new MachiningStop()
        {
          StationGroup = "TestMC",
          Program = prog2,
          ProgramRevision = prog2Rev,
          ExpectedCycleTime = TimeSpan.FromMinutes(machMins2),
          Stations = machs.Select(m => m + 100).ToImmutableList(),
        }
      );

      return (
        new Job()
        {
          UniqueStr = unique,
          PartName = part,
          RouteStartUTC = DateTime.UtcNow.AddHours(-priority),
          Cycles = qty,
          RouteEndUTC = DateTime.MinValue,
          Archived = false,
          Processes = ImmutableList.Create(
            new ProcessInfo()
            {
              Paths = ImmutableList.Create(
                new ProcPathInfo()
                {
                  Load = load1.ToImmutableList(),
                  Unload = unload1.ToImmutableList(),
                  ExpectedLoadTime = TimeSpan.FromMinutes(loadMins1),
                  ExpectedUnloadTime = TimeSpan.FromMinutes(unloadMins1),
                  PartsPerPallet = partsPerPal,
                  PalletNums = pals1.ToImmutableList(),
                  Fixture = fixture,
                  Face = 1,
                  SimulatedStartingUTC = DateTime.MinValue,
                  SimulatedAverageFlowTime = TimeSpan.Zero,
                  Casting = string.IsNullOrEmpty(castingQ) ? null : rawMatName,
                  InputQueue = string.IsNullOrEmpty(castingQ) ? null : castingQ,
                  OutputQueue = transQ,
                  Stops = stops1.ToImmutable()
                }
              )
            },
            new ProcessInfo()
            {
              Paths = ImmutableList.Create(
                new ProcPathInfo()
                {
                  Load = load2.ToImmutableList(),
                  Unload = unload2.ToImmutableList(),
                  ExpectedLoadTime = TimeSpan.FromMinutes(loadMins2),
                  ExpectedUnloadTime = TimeSpan.FromMinutes(unloadMins2),
                  PartsPerPallet = partsPerPal,
                  PalletNums = pals2.ToImmutableList(),
                  Fixture = fixture,
                  Face = 1,
                  SimulatedStartingUTC = DateTime.MinValue,
                  SimulatedAverageFlowTime = TimeSpan.Zero,
                  InputQueue = transQ,
                  Stops = stops2.ToImmutable()
                }
              )
            }
          )
        },
        new[] { new[] { prec1 }, new[] { prec2 } }
      );
    }

    public FakeIccDsl ExpectOldProgram(string name, long rev, int num)
    {
      _expectedOldPrograms.Add(
        new ProgramRevision()
        {
          ProgramName = name,
          Revision = rev,
          CellControllerProgramName = num.ToString()
        }
      );
      return this;
    }

    public FakeIccDsl ClearExpectedOldPrograms()
    {
      _expectedOldPrograms.Clear();
      return this;
    }
    #endregion

    #region Steps
    private void CheckCellStMatchesExpected(CellState actualSt)
    {
      using var _logDB = _logDBCfg.OpenConnection();
      actualSt.Status.Should().Be(_status);
      actualSt.Pallets.Count.Should().Be(_status.Pallets.Count);
      actualSt.CurrentStatus.Pallets.Count.Should().Be(_status.Pallets.Count);
      for (int palNum = 1; palNum <= actualSt.Pallets.Count; palNum++)
      {
        var current = actualSt.Pallets[palNum - 1];
        current.Status.Should().Be(_status.Pallets[palNum - 1]);
        current
          .CurrentOrLoadingFaces.Should()
          .BeEquivalentTo(
            _expectedFaces[palNum]
              .Select(face =>
              {
                var job = _logDB.LoadJob(face.unique);
                return new PalletFace()
                {
                  Job = job,
                  Process = face.proc,
                  Path = face.path,
                  Face = face.face,
                  PathInfo = job.Processes[face.proc - 1].Paths[face.path - 1],
                  FaceIsMissingMaterial = false,
                  Programs =
                    face.progs?.ToImmutableList()
                    ?? job.Processes[face.proc - 1]
                      .Paths[face.path - 1]
                      .Stops.Where(s =>
                        _statNames == null || !_statNames.ReclampGroupNames.Contains(s.StationGroup)
                      )
                      .Select(
                        (stop, stopIdx) =>
                          new ProgramsForProcess()
                          {
                            MachineStopIndex = stopIdx,
                            ProgramName = stop.Program,
                            Revision = stop.ProgramRevision
                          }
                      )
                      .ToImmutableList()
                };
              })
          );
        current
          .Material.Select(m => m.Mat)
          .Should()
          .BeEquivalentTo(
            _expectedMaterial
              .Values.Concat(_expectedLoadCastings)
              .Where(m =>
              {
                if (m.Action.Type == InProcessMaterialAction.ActionType.Loading)
                {
                  if (m.Action.LoadOntoPalletNum == null)
                  {
                    return m.Location.PalletNum == palNum;
                  }
                  else
                  {
                    return m.Action.LoadOntoPalletNum == palNum;
                  }
                }
                else if (m.Location.Type == InProcessMaterialLocation.LocType.OnPallet)
                {
                  return m.Location.PalletNum == palNum;
                }
                return false;
              }),
            options => options.ComparingByMembers<InProcessMaterial>()
          );

        actualSt
          .CurrentStatus.Pallets[palNum]
          .Should()
          .BeEquivalentTo(
            new MachineFramework.PalletStatus()
            {
              PalletNum = palNum,
              FixtureOnPallet = "",
              OnHold = _status.Pallets[palNum - 1].Master.Skip,
              CurrentPalletLocation = _status.Pallets[palNum - 1].CurStation.Location,
              NumFaces = current.Material.Select(m => m.Mat.Location.Face ?? 1).DefaultIfEmpty(0).Max()
            }
          );
      }
      actualSt
        .QueuedMaterial.Select(m => m.Mat)
        .Should()
        .BeEquivalentTo(
          _expectedMaterial.Values.Where(m =>
            m.Location.Type == InProcessMaterialLocation.LocType.InQueue
            && m.Action.Type == InProcessMaterialAction.ActionType.Waiting
          ),
          options => options.ComparingByMembers<InProcessMaterial>()
        );
      actualSt
        .OldUnusedPrograms.Should()
        .BeEquivalentTo(_expectedOldPrograms, options => options.Excluding(p => p.Comment));

      actualSt
        .CurrentStatus.Jobs.Values.Should()
        .BeEquivalentTo(
          _logDB
            .LoadUnarchivedJobs()
            .Select(j =>
              j.CloneToDerived<ActiveJob, HistoricJob>() with
              {
                Cycles = j.Decrements != null ? j.Cycles - j.Decrements.Sum(d => d.Quantity) : j.Cycles,
                RemainingToStart =
                  j.Decrements != null && j.Decrements.Count > 0
                    ? 0
                    : j.Cycles - _expectedStartedQty.GetValueOrDefault(j.UniqueStr, 0),
                Completed = _expectedCompleted[j.UniqueStr]
                  .GroupBy(x => x.Key.proc)
                  .OrderBy(x => x.Key)
                  .Select(x => x.OrderBy(y => y.Key.path).Select(y => y.Value).ToImmutableList())
                  .ToImmutableList(),
                Precedence = _expectedPrecedence[j.UniqueStr],
                AssignedWorkorders = _logDB.GetWorkordersForUnique(j.UniqueStr)
              }
            )
        );

      actualSt
        .CurrentStatus.Material.Should()
        .BeEquivalentTo(_expectedMaterial.Values.Concat(_expectedLoadCastings));

      actualSt.CurrentStatus.Queues.Should().BeEquivalentTo(_settings.Queues);

      actualSt
        .CurrentStatus.Alarms.Should()
        .BeEquivalentTo(
          _expectedPalletAlarms.Values.Concat(_expectedMachineAlarms.Select(m => $"Machine {m} has an alarm"))
        );
    }

    public FakeIccDsl ExpectNoChanges()
    {
      using var _logDB = _logDBCfg.OpenConnection();
      using (var logMonitor = _logDBCfg.Monitor())
      {
        var cellSt = _createLog.BuildCellState(_logDB, _status);
        cellSt.StateUpdated.Should().BeFalse();

        CheckCellStMatchesExpected(cellSt);
        _assign.NewPalletChange(cellSt).Should().BeNull();
        logMonitor.Should().NotRaise("NewLogEntry");
      }
      return this;
    }

    public FakeIccDsl ExpectJobArchived(string uniq, bool isArchived)
    {
      using var _logDB = _logDBCfg.OpenConnection();
      _logDB.LoadJob(uniq).Archived.Should().Be(isArchived);
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
      public Action<IInProcessMaterialDraft> MatAdjust { get; set; }
      public List<LogMaterial> OutMaterial { get; set; }
    }

    public static ExpectedChange LoadCastingToFace(
      int pal,
      int lul,
      int face,
      string unique,
      int path,
      int cnt,
      int elapsedMin,
      int activeMins,
      out IEnumerable<LogMaterial> mats,
      Action<IInProcessMaterialDraft> adj = null
    )
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
        MatAdjust = adj,
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

    public ExpectedChange LoadToFace(
      int pal,
      int face,
      string unique,
      int lul,
      int elapsedMin,
      int activeMins,
      IEnumerable<LogMaterial> loadingMats,
      out IEnumerable<LogMaterial> loadedMats,
      string part = null
    )
    {
      loadedMats = loadingMats.Select(m => new LogMaterial(
        matID: m.MaterialID,
        uniq: unique,
        proc: m.Process + 1,
        part: part == null ? m.PartName : part,
        numProc: m.NumProcesses,
        serial: m.Serial,
        workorder: m.Workorder,
        face: face.ToString()
      ));
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
      public string Reason { get; set; }
      public int ElapsedMins { get; set; }
    }

    public static ExpectedChange RemoveFromQueue(
      string queue,
      int pos,
      int elapMin,
      string reason,
      IEnumerable<LogMaterial> mat
    )
    {
      return new ExpectedRemoveFromQueueEvt()
      {
        Material = mat,
        FromQueue = queue,
        Position = pos,
        Reason = reason,
        ElapsedMins = elapMin
      };
    }

    private class ExpectedUnloadEvt : ExpectedChange
    {
      public int Pallet { get; set; }
      public int LoadStation { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
      public int ElapsedMin { get; set; }
      public int ActiveMins { get; set; }
    }

    public static ExpectedChange UnloadFromFace(
      int pal,
      int lul,
      int elapsedMin,
      int activeMins,
      IEnumerable<LogMaterial> mats
    )
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
      public string Reason { get; set; }
    }

    public static ExpectedChange AddToQueue(
      string queue,
      int pos,
      string reason,
      IEnumerable<LogMaterial> mat
    )
    {
      return new ExpectedAddToQueueEvt()
      {
        Material = mat,
        ToQueue = queue,
        Position = pos,
        Reason = reason
      };
    }

    private class ExpectNewRouteChange : ExpectedChange
    {
      public PalletMaster ExpectedMaster { get; set; }
      public IEnumerable<(
        int face,
        string unique,
        int proc,
        int path,
        IEnumerable<ProgramsForProcess> progs
      )> Faces { get; set; }
    }

    public static ExpectedChange ExpectNewRoute(
      int pal,
      int[] luls,
      int[] machs,
      int[] progs,
      int pri,
      IEnumerable<(int face, string unique, int proc, int path)> faces,
      int[] unloads = null,
      IEnumerable<(int face, ProgramsForProcess[] progs)> progOverride = null,
      int[] reclamp = null,
      bool reclampFirst = false
    )
    {
      var routes = new List<RouteStep>
      {
        new LoadStep() { LoadStations = luls.ToList() },
        new MachiningStep() { Machines = machs.ToList(), ProgramNumsToRun = progs.ToList() },
        new UnloadStep() { UnloadStations = (unloads ?? luls).ToList(), CompletedPartCount = 1 }
      };
      if (reclamp != null)
      {
        routes.Insert(reclampFirst ? 1 : 2, new ReclampStep() { Reclamp = reclamp.ToList() });
      }
      return new ExpectNewRouteChange()
      {
        ExpectedMaster = new PalletMaster()
        {
          PalletNum = pal,
          Comment = "",
          RemainingPalletCycles = 1,
          Priority = pri,
          NoWork = false,
          Skip = false,
          ForLongToolMaintenance = false,
          PerformProgramDownload = true,
          Routes = routes,
        },
        Faces = faces.Select(f =>
        {
          var overrideProgs =
            (IEnumerable<ProgramsForProcess>)progOverride?.FirstOrDefault(p => p.face == f.face).progs;
          return (face: f.face, unique: f.unique, proc: f.proc, path: f.path, progs: overrideProgs);
        })
      };
    }

    public static ExpectedChange ExpectNewRoute(
      int pal,
      int[] loads,
      int[] machs1,
      int[] progs1,
      int[] machs2,
      int[] progs2,
      int[] reclamp,
      int[] unloads,
      int pri,
      IEnumerable<(int face, string unique, int proc, int path)> faces
    )
    {
      return new ExpectNewRouteChange()
      {
        ExpectedMaster = new PalletMaster()
        {
          PalletNum = pal,
          Comment = "",
          RemainingPalletCycles = 1,
          Priority = pri,
          NoWork = false,
          Skip = false,
          ForLongToolMaintenance = false,
          PerformProgramDownload = true,
          Routes = new List<RouteStep>
          {
            new LoadStep() { LoadStations = loads.ToList() },
            new MachiningStep() { Machines = machs1.ToList(), ProgramNumsToRun = progs1.ToList() },
            new MachiningStep() { Machines = machs2.ToList(), ProgramNumsToRun = progs2.ToList() },
            new ReclampStep() { Reclamp = reclamp.ToList() },
            new UnloadStep() { UnloadStations = unloads.ToList(), CompletedPartCount = 1 }
          },
        },
        Faces = faces.Select(f =>
          (
            face: f.face,
            unique: f.unique,
            proc: f.proc,
            path: f.path,
            progs: (IEnumerable<ProgramsForProcess>)null
          )
        )
      };
    }

    private class ExpectRouteIncrementChange : ExpectedChange
    {
      public int Pallet { get; set; }
      public int NewCycleCount { get; set; }
      public IEnumerable<(
        int face,
        string unique,
        int proc,
        int path,
        IEnumerable<ProgramsForProcess> progs
      )> Faces { get; set; }
    }

    public static ExpectedChange ExpectRouteIncrement(
      int pal,
      int newCycleCnt,
      IEnumerable<(int face, string unique, int proc, int path)> faces = null
    )
    {
      return new ExpectRouteIncrementChange()
      {
        Pallet = pal,
        NewCycleCount = newCycleCnt,
        Faces = faces?.Select(f =>
          (
            face: f.face,
            unique: f.unique,
            proc: f.proc,
            path: f.path,
            progs: (IEnumerable<ProgramsForProcess>)null
          )
        )
      };
    }

    private class ExpectRouteDeleteChange : ExpectedChange
    {
      public int Pallet { get; set; }
    }

    public static ExpectedChange ExpectRouteDelete(int pal)
    {
      return new ExpectRouteDeleteChange() { Pallet = pal };
    }

    private class ExpectPalHold : ExpectedChange
    {
      public int Pallet { get; set; }
      public bool Hold { get; set; }
    }

    public static ExpectedChange ExpectPalletHold(int pal, bool hold)
    {
      return new ExpectPalHold() { Pallet = pal, Hold = hold };
    }

    private class ExpectPalNoWork : ExpectedChange
    {
      public int Pallet { get; set; }
      public bool NoWork { get; set; }
    }

    public static ExpectedChange ExpectNoWork(int pal, bool noWork)
    {
      return new ExpectPalNoWork() { Pallet = pal, NoWork = noWork };
    }

    private class ExpectMachineBeginEvent : ExpectedChange
    {
      public int Pallet { get; set; }
      public int Machine { get; set; }
      public string Program { get; set; }
      public long? Revision { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
    }

    public static ExpectedChange ExpectMachineBegin(
      int pal,
      int machine,
      string program,
      IEnumerable<LogMaterial> mat,
      long? rev = null
    )
    {
      return new ExpectMachineBeginEvent()
      {
        Pallet = pal,
        Machine = machine,
        Program = program,
        Revision = rev,
        Material = mat
      };
    }

    private class ExpectMachineEndEvent : ExpectedChange
    {
      public int Pallet { get; set; }
      public int Machine { get; set; }
      public string Program { get; set; }
      public long? Revision { get; set; }
      public int ElapsedMin { get; set; }
      public int ActiveMin { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
    }

    public static ExpectedChange ExpectMachineEnd(
      int pal,
      int mach,
      string program,
      int elapsedMin,
      int activeMin,
      IEnumerable<LogMaterial> mats,
      long? rev = null
    )
    {
      return new ExpectMachineEndEvent()
      {
        Pallet = pal,
        Machine = mach,
        Program = program,
        Revision = rev,
        ElapsedMin = elapsedMin,
        ActiveMin = activeMin,
        Material = mats
      };
    }

    private class ExpectReclampBeginEvent : ExpectedChange
    {
      public int Pallet { get; set; }
      public int LoadStation { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
    }

    public static ExpectedChange ExpectReclampBegin(int pal, int lul, IEnumerable<LogMaterial> mats)
    {
      return new ExpectReclampBeginEvent()
      {
        Pallet = pal,
        LoadStation = lul,
        Material = mats
      };
    }

    private class ExpectReclampEndEvent : ExpectedChange
    {
      public int Pallet { get; set; }
      public int LoadStation { get; set; }
      public int ElapsedMin { get; set; }
      public int ActiveMin { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
    }

    public static ExpectedChange ExpectReclampEnd(
      int pal,
      int lul,
      int elapsedMin,
      int activeMin,
      IEnumerable<LogMaterial> mats
    )
    {
      return new ExpectReclampEndEvent()
      {
        Pallet = pal,
        LoadStation = lul,
        ElapsedMin = elapsedMin,
        ActiveMin = activeMin,
        Material = mats
      };
    }

    private class ExpectStockerStartEvent : ExpectedChange
    {
      public int Pallet { get; set; }
      public int Stocker { get; set; }
      public bool WaitForMachine { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
    }

    public static ExpectedChange ExpectStockerStart(
      int pal,
      int stocker,
      bool waitForMach,
      IEnumerable<LogMaterial> mats
    )
    {
      return new ExpectStockerStartEvent()
      {
        Pallet = pal,
        Stocker = stocker,
        WaitForMachine = waitForMach,
        Material = mats
      };
    }

    private class ExpectStockerEndEvent : ExpectedChange
    {
      public int Pallet { get; set; }
      public int Stocker { get; set; }
      public bool WaitForMachine { get; set; }
      public int ElapsedMin { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
    }

    public static ExpectedChange ExpectStockerEnd(
      int pal,
      int stocker,
      int elapMin,
      bool waitForMach,
      IEnumerable<LogMaterial> mats
    )
    {
      return new ExpectStockerEndEvent()
      {
        Pallet = pal,
        Stocker = stocker,
        WaitForMachine = waitForMach,
        ElapsedMin = elapMin,
        Material = mats
      };
    }

    private class ExpectRotaryStartEvent : ExpectedChange
    {
      public int Pallet { get; set; }
      public int Machine { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
    }

    public static ExpectedChange ExpectRotaryStart(int pal, int mach, IEnumerable<LogMaterial> mats)
    {
      return new ExpectRotaryStartEvent()
      {
        Pallet = pal,
        Machine = mach,
        Material = mats
      };
    }

    private class ExpectRotaryEndEvent : ExpectedChange
    {
      public int Pallet { get; set; }
      public int Machine { get; set; }
      public bool RotateIntoMachine { get; set; }
      public int ElapsedMin { get; set; }
      public IEnumerable<LogMaterial> Material { get; set; }
    }

    public static ExpectedChange ExpectRotaryEnd(
      int pal,
      int mach,
      bool rotate,
      int elapMin,
      IEnumerable<LogMaterial> mats
    )
    {
      return new ExpectRotaryEndEvent()
      {
        Pallet = pal,
        Machine = mach,
        RotateIntoMachine = rotate,
        ElapsedMin = elapMin,
        Material = mats
      };
    }

    private class ExpectInspectionDecision : ExpectedChange
    {
      public IEnumerable<LogMaterial> Material { get; set; }
      public string Counter { get; set; }
      public bool Inspect { get; set; }
      public string InspType { get; set; }
      public IEnumerable<MaterialProcessActualPath> Path { get; set; }
    }

    public static ExpectedChange ExpectInspection(
      IEnumerable<LogMaterial> mat,
      string cntr,
      string inspTy,
      bool inspect,
      IEnumerable<MaterialProcessActualPath> path
    )
    {
      return new ExpectInspectionDecision()
      {
        Material = mat,
        Counter = cntr,
        InspType = inspTy,
        Inspect = inspect,
        Path = path
      };
    }

    private class ExpectPalletCycleChange : ExpectedChange
    {
      public int Pallet { get; set; }
      public int Minutes { get; set; }
    }

    public static ExpectedChange ExpectPalletCycle(int pal, int mins)
    {
      return new ExpectPalletCycleChange() { Pallet = pal, Minutes = mins };
    }

    private class ExpectAddProgram : ExpectedChange
    {
      public NewProgram Expected { get; set; }
    }

    public static ExpectedChange ExpectAddNewProgram(int progNum, string name, long rev, int mcMin)
    {
      return new ExpectAddProgram()
      {
        Expected = new NewProgram()
        {
          ProgramNum = progNum,
          ProgramName = name,
          ProgramRevision = rev,
          IccProgramComment = "Insight:" + rev.ToString() + ":" + name,
          ExpectedCuttingTime = TimeSpan.FromMinutes(mcMin)
        }
      };
    }

    private class ExpectedDeleteProgram : ExpectedChange
    {
      public DeleteProgram Expected { get; set; }
      public bool IccFailure { get; set; }
    }

    public static ExpectedChange ExpectDeleteProgram(int progNum, string name, long rev, bool fail = false)
    {
      return new ExpectedDeleteProgram()
      {
        Expected = new DeleteProgram()
        {
          ProgramNum = progNum,
          ProgramName = name,
          ProgramRevision = rev,
        },
        IccFailure = fail
      };
    }

    public FakeIccDsl ExpectTransition(
      IEnumerable<ExpectedChange> expectedChanges,
      bool expectedUpdates = true
    )
    {
      using var _logDB = _logDBCfg.OpenConnection();
      using (var logMonitor = _logDBCfg.Monitor())
      {
        var cellSt = _createLog.BuildCellState(_logDB, _status);

        cellSt.StateUpdated.Should().Be(expectedUpdates);

        var expectedLogs = new List<LogEntry>();

        // deal with new programs
        foreach (var expectAdd in expectedChanges.Where(e => e is ExpectAddProgram).Cast<ExpectAddProgram>())
        {
          var action = _assign.NewPalletChange(cellSt);
          action.Should().BeEquivalentTo<NewProgram>(expectAdd.Expected);
          _logDB.SetCellControllerProgramForProgram(
            expectAdd.Expected.ProgramName,
            expectAdd.Expected.ProgramRevision,
            expectAdd.Expected.ProgramNum.ToString()
          );
          _status.Programs[expectAdd.Expected.ProgramNum] = new ProgramEntry()
          {
            ProgramNum = expectAdd.Expected.ProgramNum,
            Comment = expectAdd.Expected.IccProgramComment,
            CycleTime = TimeSpan.FromMinutes(expectAdd.Expected.ProgramRevision),
            Tools = new List<int>()
          };

          // reload cell state
          cellSt = _createLog.BuildCellState(_logDB, _status);
          cellSt.StateUpdated.Should().Be(expectedUpdates);
        }

        var expectedDelRoute = (ExpectRouteDeleteChange)
          expectedChanges.FirstOrDefault(e => e is ExpectRouteDeleteChange);
        var expectedNewRoute = (ExpectNewRouteChange)
          expectedChanges.FirstOrDefault(e => e is ExpectNewRouteChange);
        var expectIncr = (ExpectRouteIncrementChange)
          expectedChanges.FirstOrDefault(e => e is ExpectRouteIncrementChange);
        var expectFirstDelete = (ExpectedDeleteProgram)
          expectedChanges.FirstOrDefault(e => e is ExpectedDeleteProgram);
        var expectHold = (ExpectPalHold)expectedChanges.FirstOrDefault(e => e is ExpectPalHold);
        var expectNoWork = (ExpectPalNoWork)expectedChanges.FirstOrDefault(e => e is ExpectPalNoWork);

        if (expectedDelRoute != null)
        {
          var action = _assign.NewPalletChange(cellSt);
          var pal = expectedDelRoute.Pallet;
          action.Should().BeEquivalentTo<DeletePalletRoute>(new DeletePalletRoute() { PalletNum = pal });
          _status.Pallets[pal - 1].Master.NoWork = true;
          _status.Pallets[pal - 1].Master.Comment = "";
          _status.Pallets[pal - 1].Master.Routes.Clear();
          _status.Pallets[pal - 1].Tracking.RouteInvalid = true;

          // reload
          cellSt = _createLog.BuildCellState(_logDB, _status);
          cellSt.StateUpdated.Should().Be(false);
        }

        if (expectedNewRoute != null)
        {
          var action = _assign.NewPalletChange(cellSt);
          var pal = expectedNewRoute.ExpectedMaster.PalletNum;
          action
            .Should()
            .BeEquivalentTo<NewPalletRoute>(
              new NewPalletRoute()
              {
                NewMaster = expectedNewRoute.ExpectedMaster,
                NewFaces = expectedNewRoute.Faces.Select(f => new AssignedJobAndPathForFace()
                {
                  Face = f.face,
                  Unique = f.unique,
                  Proc = f.proc,
                  Path = f.path,
                  ProgOverride = f.progs
                })
              },
              options => options.Excluding(e => e.NewMaster.Comment).RespectingRuntimeTypes()
            );
          ((NewPalletRoute)action).NewMaster.Comment = RecordFacesForPallet.Save(
            ((NewPalletRoute)action).NewMaster.PalletNum,
            _status.TimeOfStatusUTC,
            ((NewPalletRoute)action).NewFaces,
            _logDB
          );
          _status.Pallets[pal - 1].Master = ((NewPalletRoute)action).NewMaster;
          _status.Pallets[pal - 1].Tracking.RouteInvalid = false;
          _status.Pallets[pal - 1].Tracking.CurrentControlNum = 1;
          _status.Pallets[pal - 1].Tracking.CurrentStepNum = 1;
          _expectedFaces[pal] = expectedNewRoute.Faces.ToList();

          expectedLogs.Add(
            new LogEntry(
              cntr: -1,
              mat: Enumerable.Empty<LogMaterial>(),
              pal: pal,
              ty: LogType.GeneralMessage,
              locName: "Message",
              locNum: 1,
              prog: "Assign",
              start: false,
              endTime: _status.TimeOfStatusUTC,
              result: "New Niigata Route"
            )
          );
        }
        else if (expectIncr != null)
        {
          var action = _assign.NewPalletChange(cellSt);
          var pal = expectIncr.Pallet;
          action
            .Should()
            .BeEquivalentTo<UpdatePalletQuantities>(
              new UpdatePalletQuantities()
              {
                Pallet = pal,
                Priority = _status.Pallets[pal - 1].Master.Priority,
                Cycles = expectIncr.NewCycleCount,
                NoWork = false,
                Skip = false,
                LongToolMachine = 0
              }
            );
          _status.Pallets[pal - 1].Master.NoWork = false;
          _status.Pallets[pal - 1].Master.RemainingPalletCycles = expectIncr.NewCycleCount;
          _status.Pallets[pal - 1].Tracking.CurrentControlNum = 1;
          _status.Pallets[pal - 1].Tracking.CurrentStepNum = 1;
          if (expectIncr.Faces != null)
          {
            _expectedFaces[pal] = expectIncr.Faces.ToList();
          }
        }
        else if (expectFirstDelete != null)
        {
          foreach (
            var expectDelete in expectedChanges
              .Where(e => e is ExpectedDeleteProgram)
              .Cast<ExpectedDeleteProgram>()
          )
          {
            var action = _assign.NewPalletChange(cellSt);
            action.Should().BeEquivalentTo<DeleteProgram>(expectDelete.Expected);
            _status.Programs.Remove(expectDelete.Expected.ProgramNum);
            if (expectDelete.IccFailure == false)
            {
              _logDB.SetCellControllerProgramForProgram(
                expectDelete.Expected.ProgramName,
                expectDelete.Expected.ProgramRevision,
                null
              );
              _expectedOldPrograms.RemoveAll(p =>
                p.CellControllerProgramName == expectDelete.Expected.ProgramNum.ToString()
              );
            }
            // reload cell state
            cellSt = _createLog.BuildCellState(_logDB, _status);
            cellSt.StateUpdated.Should().Be(expectedUpdates);
            cellSt
              .OldUnusedPrograms.Should()
              .BeEquivalentTo(
                _expectedOldPrograms,
                options => options.ComparingByMembers<ProgramRevision>().Excluding(p => p.Comment)
              );
          }
        }
        else if (expectHold != null)
        {
          _status.Pallets[expectHold.Pallet - 1].Master.Skip.Should().Be(!expectHold.Hold);
          var action = _assign.NewPalletChange(cellSt);
          action
            .Should()
            .BeEquivalentTo<UpdatePalletQuantities>(
              new UpdatePalletQuantities()
              {
                Pallet = expectHold.Pallet,
                Priority = _status.Pallets[expectHold.Pallet - 1].Master.Priority,
                Cycles = _status.Pallets[expectHold.Pallet - 1].Master.RemainingPalletCycles,
                NoWork = false,
                Skip = expectHold.Hold,
                LongToolMachine = 0
              }
            );
          _status.Pallets[expectHold.Pallet - 1].Master.Skip = expectHold.Hold;
        }
        else if (expectNoWork != null)
        {
          _status.Pallets[expectNoWork.Pallet - 1].Master.NoWork.Should().Be(!expectNoWork.NoWork);
          var action = _assign.NewPalletChange(cellSt);
          action
            .Should()
            .BeEquivalentTo<UpdatePalletQuantities>(
              new UpdatePalletQuantities()
              {
                Pallet = expectNoWork.Pallet,
                Priority = _status.Pallets[expectNoWork.Pallet - 1].Master.Priority,
                Cycles = _status.Pallets[expectNoWork.Pallet - 1].Master.RemainingPalletCycles,
                NoWork = expectNoWork.NoWork,
                Skip = _status.Pallets[expectNoWork.Pallet - 1].Master.Skip,
                LongToolMachine = 0
              }
            );
          _status.Pallets[expectNoWork.Pallet - 1].Master.NoWork = expectNoWork.NoWork;

          // reload cell state
          cellSt = _createLog.BuildCellState(_logDB, _status);
          cellSt.StateUpdated.Should().Be(true);
        }
        else
        {
          _assign.NewPalletChange(cellSt).Should().BeNull();
        }

        var evts = logMonitor.OccurredEvents.Select(e => e.Parameters[0]).Cast<LogEntry>().ToList();

        foreach (var expected in expectedChanges)
        {
          switch (expected)
          {
            case ExpectPalletCycleChange palletCycleChange:
              expectedLogs.Add(
                new LogEntry(
                  cntr: -1,
                  mat: Enumerable.Empty<LogMaterial>(),
                  pal: palletCycleChange.Pallet,
                  ty: LogType.PalletCycle,
                  locName: "Pallet Cycle",
                  locNum: 1,
                  prog: "",
                  start: false,
                  endTime: _status.TimeOfStatusUTC,
                  result: "PalletCycle",
                  elapsed: TimeSpan.FromMinutes(palletCycleChange.Minutes),
                  active: TimeSpan.Zero
                )
              );
              _status.Pallets[palletCycleChange.Pallet - 1].Master.RemainingPalletCycles -= 1;
              break;

            case ExpectedLoadBegin loadBegin:
              expectedLogs.Add(
                new LogEntry(
                  cntr: -1,
                  mat: Enumerable.Empty<LogMaterial>(),
                  pal: loadBegin.Pallet,
                  ty: LogType.LoadUnloadCycle,
                  locName: "L/U",
                  locNum: loadBegin.LoadStation,
                  prog: "LOAD",
                  start: true,
                  endTime: _status.TimeOfStatusUTC,
                  result: "LOAD"
                )
              );
              break;

            case ExpectedLoadCastingEvt load:

              // first, extract the newly created material
              var evt = evts.First(e =>
                e.LogType == LogType.LoadUnloadCycle
                && e.Result == "LOAD"
                && e.Material.Any(m => m.Face == load.Face.ToString())
              );
              var matIds = evt.Material.Select(m => m.MaterialID);

              matIds.Count().Should().Be(load.Count);

              load.OutMaterial.AddRange(
                evt.Material.Select(origMat => new LogMaterial(
                  matID: origMat.MaterialID,
                  uniq: load.Unique,
                  proc: 1,
                  part: origMat.PartName,
                  numProc: origMat.NumProcesses,
                  serial: _serialSt.ConvertMaterialIDToSerial(origMat.MaterialID),
                  workorder: "",
                  face: load.Face.ToString()
                ))
              );

              // now the expected events
              expectedLogs.Add(
                new LogEntry(
                  cntr: -1,
                  mat: load.OutMaterial,
                  pal: load.Pallet,
                  ty: LogType.LoadUnloadCycle,
                  locName: "L/U",
                  locNum: load.LoadStation,
                  prog: "LOAD",
                  start: false,
                  endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                  result: "LOAD",
                  elapsed: TimeSpan.FromMinutes(load.ElapsedMin),
                  active: TimeSpan.FromMinutes(load.ActiveMins)
                )
              );
              expectedLogs.AddRange(
                load.OutMaterial.Select(m => new LogEntry(
                  cntr: -1,
                  mat: new[]
                  {
                    new LogMaterial(
                      matID: m.MaterialID,
                      uniq: m.JobUniqueStr,
                      proc: 0,
                      part: m.PartName,
                      numProc: m.NumProcesses,
                      serial: m.Serial,
                      workorder: "",
                      face: ""
                    )
                  },
                  pal: 0,
                  ty: LogType.PartMark,
                  locName: "Mark",
                  locNum: 1,
                  prog: "MARK",
                  start: false,
                  endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                  result: _serialSt.ConvertMaterialIDToSerial(m.MaterialID)
                ))
              );

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
                  Serial = _serialSt.ConvertMaterialIDToSerial(m.MaterialID),
                  SignaledInspections = ImmutableList<string>.Empty,
                  QuarantineAfterUnload = null,
                  Location = new InProcessMaterialLocation()
                  {
                    Type = InProcessMaterialLocation.LocType.OnPallet,
                    PalletNum = load.Pallet,
                    Face = load.Face
                  },
                  Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting }
                };
                if (load.MatAdjust != null)
                {
                  _expectedMaterial[m.MaterialID] %= load.MatAdjust;
                }
              }

              break;

            case ExpectedLoadMatsEvt load:

              expectedLogs.Add(
                new LogEntry(
                  cntr: -1,
                  mat: load.Material,
                  pal: load.Pallet,
                  ty: LogType.LoadUnloadCycle,
                  locName: "L/U",
                  locNum: load.LoadStation,
                  prog: "LOAD",
                  start: false,
                  endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                  result: "LOAD",
                  elapsed: TimeSpan.FromMinutes(load.ElapsedMin),
                  active: TimeSpan.FromMinutes(load.ActiveMins)
                )
              );
              break;

            case ExpectedRemoveFromQueueEvt removeFromQueueEvt:
              expectedLogs.AddRange(
                removeFromQueueEvt.Material.Select(m => new LogEntry(
                  cntr: -1,
                  mat: new[]
                  {
                    new LogMaterial(
                      matID: m.MaterialID,
                      uniq: m.JobUniqueStr,
                      proc: m.Process,
                      part: m.PartName,
                      numProc: m.NumProcesses,
                      serial: m.Serial,
                      workorder: m.Workorder ?? "",
                      face: m.Face
                    )
                  },
                  pal: 0,
                  ty: LogType.RemoveFromQueue,
                  locName: removeFromQueueEvt.FromQueue,
                  locNum: removeFromQueueEvt.Position,
                  prog: removeFromQueueEvt.Reason ?? "",
                  start: false,
                  endTime: _status.TimeOfStatusUTC.AddSeconds(1),
                  result: "",
                  // add 1 second because addtoqueue event is one-second after load end
                  elapsed: TimeSpan.FromMinutes(removeFromQueueEvt.ElapsedMins).Add(TimeSpan.FromSeconds(1)),
                  active: TimeSpan.Zero
                ))
              );
              break;

            case ExpectedUnloadEvt unload:
              expectedLogs.Add(
                new LogEntry(
                  cntr: -1,
                  mat: unload.Material,
                  pal: unload.Pallet,
                  ty: LogType.LoadUnloadCycle,
                  locName: "L/U",
                  locNum: unload.LoadStation,
                  prog: "UNLOAD",
                  start: false,
                  endTime: _status.TimeOfStatusUTC,
                  result: "UNLOAD",
                  elapsed: TimeSpan.FromMinutes(unload.ElapsedMin),
                  active: TimeSpan.FromMinutes(unload.ActiveMins)
                )
              );
              break;

            case ExpectedAddToQueueEvt addToQueueEvt:
              expectedLogs.AddRange(
                addToQueueEvt.Material.Select(m => new LogEntry(
                  cntr: -1,
                  mat: new[]
                  {
                    new LogMaterial(
                      matID: m.MaterialID,
                      uniq: m.JobUniqueStr,
                      proc: m.Process,
                      part: m.PartName,
                      numProc: m.NumProcesses,
                      serial: m.Serial,
                      workorder: "",
                      face: m.Face
                    )
                  },
                  pal: 0,
                  ty: LogType.AddToQueue,
                  locName: addToQueueEvt.ToQueue,
                  locNum: addToQueueEvt.Position,
                  prog: addToQueueEvt.Reason ?? "",
                  start: false,
                  endTime: _status.TimeOfStatusUTC,
                  result: ""
                ))
              );
              break;

            case ExpectMachineBeginEvent machBegin:
              {
                expectedLogs.Add(
                  new LogEntry()
                  {
                    Counter = -1,
                    Material = machBegin.Material.ToImmutableList(),
                    Pallet = machBegin.Pallet,
                    LogType = LogType.MachineCycle,
                    LocationName = "TestMC",
                    LocationNum = 100 + machBegin.Machine,
                    Program = machBegin.Program,
                    StartOfCycle = true,
                    EndTimeUTC = _status.TimeOfStatusUTC,
                    Result = "",
                    ElapsedTime = TimeSpan.FromMinutes(-1),
                    ActiveOperationTime = TimeSpan.Zero,
                    ProgramDetails = machBegin.Revision.HasValue
                      ? ImmutableDictionary<string, string>.Empty.Add(
                        "ProgramRevision",
                        machBegin.Revision.Value.ToString()
                      )
                      : null
                  }
                );
              }
              break;

            case ExpectMachineEndEvent machEnd:
              {
                expectedLogs.Add(
                  new LogEntry()
                  {
                    Counter = -1,
                    Material = machEnd.Material.ToImmutableList(),
                    Pallet = machEnd.Pallet,
                    LogType = LogType.MachineCycle,
                    LocationName = "TestMC",
                    LocationNum = 100 + machEnd.Machine,
                    Program = machEnd.Program,
                    StartOfCycle = false,
                    EndTimeUTC = _status.TimeOfStatusUTC,
                    Result = "",
                    ElapsedTime = TimeSpan.FromMinutes(machEnd.ElapsedMin),
                    ActiveOperationTime = TimeSpan.FromMinutes(machEnd.ActiveMin),
                    ProgramDetails = machEnd.Revision.HasValue
                      ? ImmutableDictionary<string, string>.Empty.Add(
                        "ProgramRevision",
                        machEnd.Revision.Value.ToString()
                      )
                      : null
                  }
                );
              }
              break;

            case ExpectReclampBeginEvent reclampBegin:
              {
                expectedLogs.Add(
                  new LogEntry(
                    cntr: -1,
                    mat: reclampBegin.Material,
                    pal: reclampBegin.Pallet,
                    ty: LogType.LoadUnloadCycle,
                    locName: "L/U",
                    locNum: reclampBegin.LoadStation,
                    prog: "TestReclamp",
                    start: true,
                    endTime: _status.TimeOfStatusUTC,
                    result: "TestReclamp"
                  )
                );
              }
              break;

            case ExpectReclampEndEvent reclampEnd:
              {
                expectedLogs.Add(
                  new LogEntry(
                    cntr: -1,
                    mat: reclampEnd.Material,
                    pal: reclampEnd.Pallet,
                    ty: LogType.LoadUnloadCycle,
                    locName: "L/U",
                    locNum: reclampEnd.LoadStation,
                    prog: "TestReclamp",
                    start: false,
                    endTime: _status.TimeOfStatusUTC,
                    result: "TestReclamp",
                    elapsed: TimeSpan.FromMinutes(reclampEnd.ElapsedMin),
                    active: TimeSpan.FromMinutes(reclampEnd.ActiveMin)
                  )
                );
              }
              break;

            case ExpectStockerStartEvent stockerStart:
              {
                expectedLogs.Add(
                  new LogEntry(
                    cntr: -1,
                    mat: stockerStart.Material,
                    pal: stockerStart.Pallet,
                    ty: LogType.PalletInStocker,
                    locName: "Stocker",
                    locNum: stockerStart.Stocker,
                    prog: "Arrive",
                    start: true,
                    endTime: _status.TimeOfStatusUTC,
                    result: stockerStart.WaitForMachine ? "WaitForMachine" : "WaitForUnload"
                  )
                );
              }
              break;

            case ExpectStockerEndEvent stockerEnd:
              {
                expectedLogs.Add(
                  new LogEntry(
                    cntr: -1,
                    mat: stockerEnd.Material,
                    pal: stockerEnd.Pallet,
                    ty: LogType.PalletInStocker,
                    locName: "Stocker",
                    locNum: stockerEnd.Stocker,
                    prog: "Depart",
                    start: false,
                    endTime: _status.TimeOfStatusUTC,
                    result: stockerEnd.WaitForMachine ? "WaitForMachine" : "WaitForUnload",
                    elapsed: TimeSpan.FromMinutes(stockerEnd.ElapsedMin),
                    active: TimeSpan.Zero
                  )
                );
              }
              break;

            case ExpectRotaryStartEvent rotaryStart:
              {
                expectedLogs.Add(
                  new LogEntry(
                    cntr: -1,
                    mat: rotaryStart.Material,
                    pal: rotaryStart.Pallet,
                    ty: LogType.PalletOnRotaryInbound,
                    locName: "TestMC",
                    locNum: 100 + rotaryStart.Machine,
                    prog: "Arrive",
                    start: true,
                    endTime: _status.TimeOfStatusUTC,
                    result: "Arrive"
                  )
                );
              }
              break;

            case ExpectRotaryEndEvent rotaryEnd:
              {
                expectedLogs.Add(
                  new LogEntry(
                    cntr: -1,
                    mat: rotaryEnd.Material,
                    pal: rotaryEnd.Pallet,
                    ty: LogType.PalletOnRotaryInbound,
                    locName: "TestMC",
                    locNum: 100 + rotaryEnd.Machine,
                    prog: "Depart",
                    start: false,
                    endTime: _status.TimeOfStatusUTC,
                    result: rotaryEnd.RotateIntoMachine ? "RotateIntoWorktable" : "LeaveMachine",
                    elapsed: TimeSpan.FromMinutes(rotaryEnd.ElapsedMin),
                    active: TimeSpan.Zero
                  )
                );
              }
              break;

            case ExpectInspectionDecision insp:
              foreach (var mat in insp.Material)
              {
                expectedLogs.Add(
                  new LogEntry()
                  {
                    Counter = -1,
                    Material = ImmutableList.Create(
                      new LogMaterial(
                        matID: mat.MaterialID,
                        uniq: mat.JobUniqueStr,
                        proc: mat.Process,
                        part: mat.PartName,
                        numProc: mat.NumProcesses,
                        serial: mat.Serial,
                        workorder: mat.Workorder,
                        face: ""
                      )
                    ),
                    Pallet = 0,
                    LogType = LogType.Inspection,
                    LocationName = "Inspect",
                    LocationNum = 1,
                    Program = insp.Counter,
                    StartOfCycle = false,
                    EndTimeUTC = _status.TimeOfStatusUTC,
                    Result = insp.Inspect.ToString(),
                    ElapsedTime = TimeSpan.FromMinutes(-1),
                    ActiveOperationTime = TimeSpan.Zero,
                    ProgramDetails = ImmutableDictionary<string, string>
                      .Empty.Add("InspectionType", insp.InspType)
                      .Add(
                        "ActualPath",
                        JsonSerializer.Serialize(
                          insp.Path.Select(p => p with { MaterialID = mat.MaterialID })
                        )
                      )
                  }
                );
              }

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
        evts.Should()
          .BeEquivalentTo(
            expectedLogs,
            options => options.Excluding(e => e.Counter).ComparingByMembers<LogEntry>()
          );
      }

      return ExpectNoChanges();
    }
    #endregion
  }

  public static class FakeIccDslJobHelpers
  {
    public static (Job, int[][]) AddInsp(
      this (Job, int[][]) job,
      int proc,
      int path,
      string inspTy,
      string cntr,
      int max
    )
    {
      return (
        job.Item1.AdjustPath(
          proc,
          path,
          p =>
            p with
            {
              Inspections = (p.Inspections ?? ImmutableList<PathInspection>.Empty).Add(
                new PathInspection()
                {
                  InspectionType = inspTy,
                  Counter = cntr,
                  MaxVal = max,
                  RandomFreq = 0,
                  TimeInterval = TimeSpan.Zero
                }
              )
            }
        ),
        job.Item2
      );
    }
  }
}
