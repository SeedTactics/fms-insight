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
using NSubstitute;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class NiigataAssignmentSpec : IDisposable
  {
    private FakeIccDsl _dsl;
    public NiigataAssignmentSpec()
    {
      _dsl = new FakeIccDsl();
    }

    void IDisposable.Dispose()
    {
      _dsl.Dispose();
    }

    [Fact]
    public void OneJob()
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
        .SetEmptyInBuffer(pal: 1)
        .SetEmptyInBuffer(pal: 2)
        .SetEmptyInBuffer(pal: 3)
        .NextShouldBeNewRoute(
          pal: 1,
          comment: "part1-1",
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          progs: new[] { 1234 }
        )
        .NextShouldBeNewRoute(
          pal: 2,
          comment: "part1-1",
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          progs: new[] { 1234 }
        )
        .NextShouldBeNull();
    }

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
  }

  public class FakeIccDsl : IDisposable
  {
    private JobLogDB _logDB;
    private AssignPallets _assign;
    private PlannedSchedule _sch = new PlannedSchedule() { Jobs = new List<JobPlan>() };
    private Dictionary<int, PalletAndMaterial> _pals = new Dictionary<int, PalletAndMaterial>();

    public FakeIccDsl()
    {
      var logConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      logConn.Open();
      _logDB = new JobLogDB(new FMSSettings(), logConn);
      _logDB.CreateTables(firstSerialOnEmpty: null);

      _assign = new AssignPallets(_logDB);
    }

    public void Dispose()
    {
      _logDB.Close();
    }

    #region Pallets
    public FakeIccDsl SetEmptyInBuffer(int pal)
    {
      _pals[pal] = new PalletAndMaterial()
      {
        Status = new PalletStatus()
        {
          Master = new PalletMaster()
          {
            PalletNum = pal,
            NoWork = true
          },
          CurStation = NiigataStationNum.Buffer(pal),
          Tracking = new TrackingInfo()
        },
        Material = new Dictionary<int, IReadOnlyList<InProcessMaterial>>()
      };

      return this;
    }

    public FakeIccDsl MoveToMachine(int pal, int mach)
    {
      _pals[pal].Status.CurStation = NiigataStationNum.Machine(mach);
      return this;
    }

    public FakeIccDsl MoveToUnload(int pal, int load)
    {
      _pals[pal].Status.CurStation = NiigataStationNum.LoadStation(load);
      _pals[pal].Status.Tracking.CurrentControlNum = AssignPallets.UnloadStepNum * 2;
      _pals[pal].Status.Tracking.CurrentStepNum = AssignPallets.UnloadStepNum;
      return this;
    }

    public FakeIccDsl SetRoute(int pal, string comment, int[] luls, int[] machs, int[] progs)
    {
      _pals[pal].Status.Master = new PalletMaster()
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
      return this;
    }
    #endregion

    #region Jobs
    public FakeIccDsl AddOneProcOnePathJob(string unique, string part, int qty, int priority, int partsPerPal, int[] pals, int[] luls, int[] machs, int prog, string fixture, int face, string queue = null)
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
      _sch.Jobs.Add(j);

      return this;
    }

    #endregion

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
  }
}