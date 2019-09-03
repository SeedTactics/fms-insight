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
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class NiigataAssignmentSpec : IDisposable
  {
    private FakeIccDsl _dsl;
    public NiigataAssignmentSpec()
    {
      _dsl = new FakeIccDsl(numPals: 5, numMachines: 3);
    }

    void IDisposable.Dispose()
    {
      _dsl.Dispose();
    }

    [Fact]
    public void OneProcOnePath()
    {
      _dsl
        .AddOneProcOnePathJob(
          unique: "uniq1",
          part: "part1",
          qty: 3,
          priority: 5,
          partsPerPal: 1,
          pals: new[] { 1, 2 },
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          prog: 1234,
          machMins: 14,
          fixture: "fix1",
          face: 1
        )
        .ExpectNewRoute(
          pal: 1,
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          progs: new[] { 1234 },
          faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
        )
        .ExpectNewRoute(
          pal: 2,
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          progs: new[] { 1234 },
          faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
        )
        .ExpectNoChanges()
        .MoveToLoad(pal: 1, lul: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .ExpectLoadBeginEvt(pal: 1, lul: 1)
        .AdvanceMinutes(4) // =4
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectLoadEndEvt(pal: 1, lul: 1, elapsedMin: 4, palMins: 0, expectedEvts: new[] {
          FakeIccDsl.LoadCastingToFace(face: 1, unique: "uniq1", proc: 1, path: 1, cnt: 1, mats: out var fstMats)
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectNoChanges()
        .MoveToMachineQueue(pal: 1, mach: 3)
        .AdvanceMinutes(6) // =10
        .SetBeforeMC(pal: 1)
        .ExpectNoChanges()
        .MoveToMachine(pal: 1, mach: 3)
        .ExpectNoChanges()
        .StartMachine(mach: 3, program: 1234)
        .UpdateExpectedMaterial(
          fstMats.Select(m => m.MaterialID), im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Machining;
            im.Action.Program = "1234";
            im.Action.ElapsedMachiningTime = TimeSpan.Zero;
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
          }
        )
        .ExpectMachineBegin(pal: 1, mach: 3, program: 1234, mats: fstMats)
        .AdvanceMinutes(10) // =20
        .UpdateExpectedMaterial(
          fstMats.Select(m => m.MaterialID), im =>
          {
            im.Action.ElapsedMachiningTime = TimeSpan.FromMinutes(10);
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(4);
          }
        )
        .ExpectNoChanges()
        .AdvanceMinutes(5) // =25
        .EndMachine(mach: 3)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          fstMats.Select(m => m.MaterialID), im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Waiting;
            im.Action.Program = null;
            im.Action.ElapsedMachiningTime = null;
            im.Action.ExpectedRemainingMachiningTime = null;
          }
        )
        .ExpectMachineEnd(pal: 1, mach: 3, program: 1234, proc: 1, path: 1, elapsedMin: 15, activeMin: 14, mats: fstMats)
        .MoveToMachineQueue(pal: 1, mach: 3)
        .ExpectNoChanges()
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(
          fstMats.Select(m => m.MaterialID), im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial;
          }
        )
        .ExpectNoChanges()
        .AdvanceMinutes(3) //=28
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
        })
        .ExpectRouteIncrementAndLoadBegin(pal: 1, lul: 4)
        .AdvanceMinutes(2) // =30
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .RemoveExpectedMaterial(fstMats.Select(m => m.MaterialID))
        .ExpectLoadEndEvt(pal: 1, lul: 4, elapsedMin: 2, palMins: 30 - 4, expectedEvts: new[] {
          FakeIccDsl.UnloadFromFace(face: 1, unique: "uniq1", proc: 1, path: 1, mats: fstMats),
          FakeIccDsl.LoadCastingToFace(face: 1, unique: "uniq1", proc: 1, path: 1, cnt: 1, mats: out var sndMats)
        })
      ;
    }

    /*
      [Fact]
      public void IgnoresPalInMachine()
      {
        _dsl
          .AddOneProcOnePathJob(
            unique: "uniq1",
            part: "part1",
            qty: 3,
            priority: 5,
            partsPerPal: 1,
            pals: new[] { 1, 2 },
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            prog: 1234,
            fixture: "fix1",
            face: 1
          )
          .SetEmptyInBuffer(pal: 1).MoveToMachine(pal: 1, mach: 3)
          .NextShouldBeNull();
      }

      [Fact]
      public void IgnoresPalletWithMaterialInBuffer()
      {
        _dsl
          .AddOneProcOnePathJob(
            unique: "uniq1",
            part: "part1",
            qty: 3,
            priority: 5,
            partsPerPal: 1,
            pals: new[] { 1, 2 },
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            prog: 1234,
            fixture: "fix1",
            face: 1
          )
          .SetEmptyInBuffer(pal: 1).AddMaterial(pal: 1, face: 1, matId: 100, jobUnique: "uniq1", part: "part1", process: 1, path: 1)
          .NextShouldBeNull();

      }

      [Fact]
      public void ApplysNewQtyAtUnload()
      {
        _dsl
          .AddOneProcOnePathJob(
            unique: "uniq1",
            part: "part1",
            qty: 3,
            priority: 5,
            partsPerPal: 1,
            pals: new[] { 1, 2 },
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            prog: 1234,
            fixture: "fix1",
            face: 1
          )
          .SetEmptyInBuffer(pal: 1).MoveToUnload(pal: 1, load: 3)
          .SetRoute(pal: 1, comment: "abc", luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 })
          .AddMaterial(pal: 1, face: 1, matId: 100, jobUnique: "uniq1", part: "part1", process: 1, path: 1)
          .NextShouldBeRouteIncrement(pal: 1)

          //now should set new route if loads, machines, or progs differ
          .SetRoute(pal: 1, comment: "abc", luls: new[] { 100, 200 }, machs: new[] { 5, 6 }, progs: new[] { 1234 })
          .NextShouldBeNewRoute(pal: 1, comment: "part1-1", luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 })
          .SetRoute(pal: 1, comment: "abc", luls: new[] { 3, 4 }, machs: new[] { 500, 600 }, progs: new[] { 1234 })
          .NextShouldBeNewRoute(pal: 1, comment: "part1-1", luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 })
          .SetRoute(pal: 1, comment: "abc", luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 12345 })
          .NextShouldBeNewRoute(pal: 1, comment: "part1-1", luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 });
      }

      [Fact(Skip = "Pending")]
      public void CountsCompletedFromLog()
      {

      }

      [Fact]
      public void CastingsFromQueue()
      {
        _dsl
          .AddOneProcOnePathJob(
            unique: "uniq1",
            part: "part1",
            qty: 3,
            priority: 5,
            partsPerPal: 1,
            pals: new[] { 1, 2 },
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            prog: 1234,
            fixture: "fix1",
            face: 1,
            queue: "thequeue"
          )
          .SetEmptyInBuffer(pal: 1)
          .NextShouldBeNull()

          .AddUnallocatedCasting("thequeue", "part4", 1, out long unusedMatId)
          .NextShouldBeNull()

          .AddUnallocatedCasting("thequeue", "part1", 1, out long matId)
          .NextShouldBeNewRoute(pal: 1, comment: "part1-1", luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 })
          .AddLoadingMaterial(pal: 1, face: 1, matId: matId, jobUnique: "uniq1", part: "part1", process: 1, path: 1)

          .SetEmptyInBuffer(pal: 2)
          .NextShouldBeNull() // already allocated to pallet 1

          .AllocateMaterial("uniq1", "part1", 1, out long mid2)
          .AddMaterialToQueue("thequeue", mid2)
          .NextShouldBeNewRoute(pal: 2, comment: "part1-1", luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 })
          ;
      }


      [Fact(Skip = "Pending")]
      public void MultipleJobPriority()
      {

      }

      [Fact(Skip = "Pending")]
      public void MultipleProcessSeparatePallets()
      {

      }

      [Fact(Skip = "Pending")]
      public void MultipleProcessSamePallet()
      {

      }

      [Fact(Skip = "pending")]
      public void MultipleFixtures()
      {

      }

      [Fact(Skip = "Pending")]
      public void MultpleProcsMultiplePathsSeparatePallets()
      {

      }

      [Fact(Skip = "Pending")]
      public void MultipleProcsMultiplePathsSamePallet()
      {

      }
    */
  }

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

      _assign = new AssignPallets(_logDB);
      _createLog = new CreateLogEntries(_logDB, _jobDB, _settings);

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
    #endregion

    #region Material
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
    public FakeIccDsl AddOneProcOnePathJob(string unique, string part, int qty, int priority, int partsPerPal, int[] pals, int[] luls, int[] machs, int prog, int machMins, string fixture, int face, string queue = null)
    {
      var j = new JobPlan(unique, 1);
      j.PartName = part;
      j.Priority = priority;
      foreach (var i in luls)
      {
        j.AddLoadStation(1, 1, i);
        j.AddUnloadStation(1, 1, i);
      }
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
    private void CheckPals(List<PalletAndMaterial> pals)
    {
      pals.Count.Should().Be(_status.Pallets.Count);
      for (int palNum = 1; palNum <= pals.Count; palNum++)
      {
        var current = pals[palNum - 1];
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
    }

    public FakeIccDsl ExpectNoChanges()
    {
      var sch = _jobDB.LoadUnarchivedJobs();

      using (var logMonitor = _logDB.Monitor())
      {
        var pals = _createLog.CheckForNewLogEntries(_status, sch, out bool palletStateUpdated);
        palletStateUpdated.Should().BeFalse();
        CheckPals(pals);
        _assign.NewPalletChange(pals, sch).Should().BeNull();
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
            UnloadStations = luls.ToList()
          }
        },
      };

      using (var logMonitor = _logDB.Monitor())
      {
        var pals = _createLog.CheckForNewLogEntries(_status, sch, out bool palletStateUpdated);
        palletStateUpdated.Should().BeFalse();
        CheckPals(pals);

        logMonitor.Should().NotRaise("NewLogEntry");

        var action = _assign.NewPalletChange(pals, sch);
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

    private FakeIccDsl ExpectNewLogs(IEnumerable<LogEntry> log)
    {
      var sch = _jobDB.LoadUnarchivedJobs();

      using (var logMonitor = _logDB.Monitor())
      {
        var pals = _createLog.CheckForNewLogEntries(_status, sch, out bool palletStateUpdated);
        palletStateUpdated.Should().BeTrue();
        CheckPals(pals);

        _assign.NewPalletChange(pals, sch).Should().BeNull();

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

    public class ExpectedEndLoadEvt
    {
      public int Face { get; set; }
      public bool Load { get; set; }
      public IEnumerable<LogMaterial> ExpectedMaterial { get; set; }

      // if expected is false, the following are filled in
      public string Unique { get; set; }
      public int Process { get; set; }
      public int Path { get; set; }
      public int CastingCount { get; set; }
      public List<LogMaterial> OutMaterial { get; set; }
    }

    public static ExpectedEndLoadEvt LoadCastingToFace(int face, string unique, int proc, int path, int cnt, out IEnumerable<LogMaterial> mats)
    {
      var e = new ExpectedEndLoadEvt()
      {
        Face = face,
        Load = true,
        Unique = unique,
        Process = proc,
        Path = path,
        CastingCount = cnt,
        OutMaterial = new List<LogMaterial>()
      };
      mats = e.OutMaterial;
      return e;
    }

    public static ExpectedEndLoadEvt UnloadFromFace(int face, string unique, int proc, int path, IEnumerable<LogMaterial> mats)
    {
      return new ExpectedEndLoadEvt()
      {
        Face = face,
        Load = false,
        Unique = unique,
        Process = proc,
        Path = path,
        ExpectedMaterial = mats
      };
    }

    public FakeIccDsl ExpectLoadEndEvt(int pal, int lul,
                                       int elapsedMin,
                                       int palMins,
                                       IEnumerable<ExpectedEndLoadEvt> expectedEvts
                                      )
    {
      var sch = _jobDB.LoadUnarchivedJobs();

      using (var logMonitor = _logDB.Monitor())
      {
        var pals = _createLog.CheckForNewLogEntries(_status, sch, out bool palletStateUpdated);
        palletStateUpdated.Should().BeTrue();

        _assign.NewPalletChange(pals, sch).Should().BeNull();

        logMonitor.Should().Raise("NewLogEntry");
        var evts = logMonitor.OccurredEvents.Select(e => e.Parameters[0]).Cast<LogEntry>();

        // first, any unknown materials, extract them
        foreach (var expected in expectedEvts)
        {
          if (expected.ExpectedMaterial != null) continue;
          var evt = evts.First(
            e => e.LogType == LogType.LoadUnloadCycle && e.Result == (expected.Load ? "LOAD" : "UNLOAD") && e.Material.Any(m => m.Face == expected.Face.ToString())
          );
          var matIds = evt.Material.Select(m => m.MaterialID);

          matIds.Count().Should().Be(expected.CastingCount);

          var job = _jobDB.LoadJob(expected.Unique);
          expected.ExpectedMaterial = matIds.Select(mid => new LogMaterial(
            matID: mid, uniq: expected.Unique, proc: expected.Process, part: job.PartName, numProc: job.NumProcesses,
            serial: _settings.ConvertMaterialIDToSerial(mid), workorder: "", face: expected.Face.ToString())
          ).ToList();

          expected.OutMaterial.AddRange(expected.ExpectedMaterial);


        }

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
          var job = _jobDB.LoadJob(expected.Unique);
          expectedLogs.Add(new LogEntry(
            cntr: -1,
            mat: expected.ExpectedMaterial,
            pal: pal.ToString(),
            ty: LogType.LoadUnloadCycle,
            locName: "L/U",
            locNum: lul,
            prog: expected.Load ? "LOAD" : "UNLOAD",
            start: false,
            endTime: expected.Load ? _status.TimeOfStatusUTC.AddSeconds(1) : _status.TimeOfStatusUTC,
            result: expected.Load ? "LOAD" : "UNLOAD",
            endOfRoute: !expected.Load,
            elapsed: TimeSpan.FromMinutes(elapsedMin),
            active: job.GetExpectedLoadTime(expected.Process, expected.Path)
          ));

          if (expected.Load && expected.Process == 1)
          {
            expectedLogs.AddRange(expected.ExpectedMaterial.Select(m => new LogEntry(
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
          }

          if (expected.Load)
          {
            foreach (var m in expected.ExpectedMaterial)
            {
              _expectedMaterial[m.MaterialID] = new InProcessMaterial()
              {
                MaterialID = m.MaterialID,
                JobUnique = m.JobUniqueStr,
                Process = expected.Process,
                Path = expected.Path,
                PartName = job.PartName,
                Serial = _settings.ConvertMaterialIDToSerial(m.MaterialID),
                Location = new InProcessMaterialLocation()
                {
                  Type = InProcessMaterialLocation.LocType.OnPallet,
                  Pallet = pal.ToString(),
                  Face = expected.Face
                },
                Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.Waiting
                }
              };
            }
          }
        }

        evts.Should().BeEquivalentTo(expectedLogs,
          options => options.Excluding(e => e.Counter)
        );
      }

      return ExpectNoChanges();
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

    #endregion

    /*
        #region Material
        public FakeIccDsl AllocateMaterial(string unique, string part, int numProc, out long matId)
        {
          matId = _logDB.AllocateMaterialID(unique, part, numProc);
          return this;
        }

        public FakeIccDsl AddUnallocatedCasting(string queue, string part, int numProc, out long matId)
        {
          matId = _logDB.AllocateMaterialIDForCasting(part, numProc);
          _logDB.RecordAddMaterialToQueue(new JobLogDB.EventLogMaterial()
          {
            MaterialID = matId,
            Process = 0,
            Face = ""
          }, queue, -1);
          return this;
        }

        public FakeIccDsl AddMaterialToQueue(string queue, long matId)
        {
          _logDB.RecordAddMaterialToQueue(new JobLogDB.EventLogMaterial()
          {
            MaterialID = matId,
            Process = 0,
            Face = ""
          }, queue, -1);
          return this;
        }


        public FakeIccDsl AddMaterial(int pal, int face, long matId, string jobUnique, string part, int process, int path)
        {
          var m = new InProcessMaterial()
          {
            MaterialID = matId,
            JobUnique = jobUnique,
            PartName = part,
            Process = process,
            Path = path,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.OnPallet,
              Pallet = pal.ToString()
            },
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Waiting
            }
          };
          if (_pals[pal].Material.ContainsKey(face))
          {
            _pals[pal].Material[face] = _pals[pal].Material[face].Append(m).ToList();
          }
          else
          {
            _pals[pal].Material[face] = new[] { m };
          }
          return this;
        }

        public FakeIccDsl AddLoadingMaterial(int pal, int face, long matId, string jobUnique, string part, int process, int path)
        {
          var m = new InProcessMaterial()
          {
            MaterialID = matId,
            JobUnique = jobUnique,
            PartName = part,
            Process = process,
            Path = path,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.OnPallet,
              Pallet = pal.ToString()
            },
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Loading
            }
          };
          if (_pals[pal].Material.ContainsKey(face))
          {
            _pals[pal].Material[face] = _pals[pal].Material[face].Append(m).ToList();
          }
          else
          {
            _pals[pal].Material[face] = new[] { m };
          }
          return this;
        }
        #endregion

        #region Actions
        public FakeIccDsl NextShouldBeNewRoute(int pal, string comment, int[] luls, int[] machs, int[] progs)
        {
          var expectedMaster = new PalletMaster()
          {
            PalletNum = pal,
            Comment = comment,
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
                    UnloadStations = luls.ToList()
                  }
                },
          };
          var action = _assign.NewPalletChange(_pals.Values, _sch);
          action.Should().BeEquivalentTo<NewPalletRoute>(new NewPalletRoute()
          {
            NewMaster = expectedMaster
          });
          _pals[pal].Status.Master = expectedMaster;
          return this;
        }

        public FakeIccDsl NextShouldBeNull()
        {
          _assign.NewPalletChange(_pals.Values, _sch).Should().BeNull();
          return this;
        }

        public FakeIccDsl NextShouldBeRouteIncrement(int pal)
        {
          _assign.NewPalletChange(_pals.Values, _sch).Should().BeEquivalentTo<UpdatePalletQuantities>(new UpdatePalletQuantities()
          {
            Pallet = pal,
            Priority = _pals[pal].Status.Master.Priority,
            Cycles = 2,
            NoWork = false,
            Skip = false,
            ForLongTool = false
          });
          _pals[pal].Status.Master.RemainingPalletCycles = 2;
          return this;
        }
        #endregion
        */
  }
}