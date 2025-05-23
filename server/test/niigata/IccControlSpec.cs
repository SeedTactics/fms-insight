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
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;
using WireMock.Matchers;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class IccControlSpec : IDisposable
  {
    private FakeIccDsl _dsl;

    public IccControlSpec()
    {
      _dsl = new FakeIccDsl(numPals: 5, numMachines: 6);
    }

    public void Dispose()
    {
      _dsl.Dispose();
    }

    [Test]
    public void OneProcOnePath()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl
              .CreateOneProcOnePathJob(
                unique: "uniq1",
                part: "part1",
                qty: 3,
                priority: 5,
                partsPerPal: 1,
                pals: new[] { 1, 2 },
                luls: new[] { 3, 4 },
                machs: new[] { 3, 5, 6 },
                prog: "prog111",
                progRev: null,
                loadMins: 8,
                unloadMins: 9,
                machMins: 14,
                fixture: "fix1",
                face: 1
              )
              .AddInsp(proc: 1, path: 1, inspTy: "InspTy", cntr: "Thecounter", max: 2),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .MoveToMachineQueue(pal: 2, mach: 3)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        .IncrJobStartedCnt("uniq1", path: 1)
        .SetExpectedLoadCastings(
          new[]
          {
            (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
            (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
          }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 2,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .ExpectNoChanges()
        .MoveToLoad(pal: 1, lul: 1)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 1) })
        .AdvanceMinutes(4) // =4
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 4)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1) })
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 1,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 4,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var fstMats
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: fstMats),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: fstMats),
          }
        )
        .AdvanceMinutes(1)
        .ExpectNoChanges()
        .MoveToMachineQueue(pal: 1, mach: 3)
        .AdvanceMinutes(2)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectRotaryStart(pal: 1, mach: 3, mats: fstMats),
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, waitForMach: true, elapMin: 3, mats: fstMats),
          }
        )
        .AdvanceMinutes(3)
        .MoveToBuffer(pal: 1, buff: 6)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectRotaryEnd(pal: 1, mach: 3, rotate: false, elapMin: 3, mats: fstMats),
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 6, waitForMach: true, mats: fstMats),
          }
        )
        .AdvanceMinutes(2)
        .MoveToMachineQueue(pal: 1, mach: 3)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectRotaryStart(pal: 1, mach: 3, mats: fstMats),
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 6, waitForMach: true, elapMin: 2, mats: fstMats),
          }
        )
        .MoveToMachine(pal: 1, mach: 3)
        .AdvanceMinutes(1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectRotaryEnd(pal: 1, mach: 3, rotate: true, elapMin: 1, mats: fstMats),
          }
        )
        .StartMachine(mach: 3, program: 2100)
        .UpdateExpectedMaterial(
          fstMats,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 3, program: "prog111", rev: 5, mat: fstMats),
          }
        )
        .AdvanceMinutes(10)
        .UpdateExpectedMaterial(
          fstMats,
          a =>
            a with
            {
              ElapsedMachiningTime = TimeSpan.FromMinutes(10),
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(4),
            }
        )
        .ExpectNoChanges()
        .EndMachine(mach: 3)
        .ExpectNoChanges() // pausing machining without going to AfterMC does nothing
        .StartMachine(mach: 3, program: 2100) // restart machine
        .ExpectNoChanges()
        .AdvanceMinutes(5)
        .EndMachine(mach: 3)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          fstMats,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im => im with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 3,
              program: "prog111",
              rev: 5,
              elapsedMin: 15,
              activeMin: 14,
              mats: fstMats
            ),
          }
        )
        .MoveToMachineQueue(pal: 1, mach: 3)
        .ExpectNoChanges()
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(
          fstMats,
          a => new() { Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectInspection(
              mat: fstMats,
              cntr: "Thecounter",
              inspTy: "InspTy",
              inspect: false,
              path: new[]
              {
                new MaterialProcessActualPath()
                {
                  MaterialID = 0,
                  Process = 1,
                  Pallet = 1,
                  LoadStation = 1,
                  UnloadStation = -1,
                  Stops = ImmutableList.Create(
                    new MaterialProcessActualPath.Stop() { StationName = "TestMC", StationNum = 103 }
                  ),
                },
              }
            ),
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: fstMats),
          }
        )
        .AdvanceMinutes(3)
        .ExpectNoChanges()
        .MoveToLoad(pal: 1, lul: 4)
        .IncrJobStartedCnt("uniq1", path: 1)
        .SetExpectedLoadCastings(
          new[]
          {
            (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
            (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
          }
        )
        .UpdateExpectedMaterial(
          fstMats,
          a => a with { ElapsedLoadUnloadTime = TimeSpan.Zero },
          im => im with { LastCompletedMachiningRouteStopIndex = null }
        )
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, waitForMach: false, elapMin: 3, mats: fstMats),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
          }
        )
        .AdvanceMinutes(2) // =33
        .UpdateExpectedMaterial(fstMats, a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(2) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 2)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1) })
        .RemoveExpectedMaterial(fstMats.Select(m => m.MaterialID))
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 2,
              activeMins: 9,
              totalActiveMins: 9 + 8,
              mats: fstMats
            ),
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 33 - 4, mats: fstMats),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 2,
              activeMins: 8,
              totalActiveMins: 9 + 8,
              mats: out var sndMats
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: sndMats),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: sndMats),
          }
        )
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          sndMats,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, waitForMach: true, elapMin: 0, mats: sndMats),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: sndMats),
          }
        )
        .AdvanceMinutes(15) // =45
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          sndMats,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im => im with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 15,
              activeMin: 14,
              mats: sndMats
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 3)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(
          sndMats,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
              ElapsedLoadUnloadTime = TimeSpan.Zero,
            },
          m => m with { SignaledInspections = m.SignaledInspections.Add("InspTy") }
        )
        // no load of new, since qty is 3 and have produced 2 on pallet 1 and there is still a pending load assigned to pallet 2
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3),
            FakeIccDsl.ExpectInspection(
              mat: sndMats,
              cntr: "Thecounter",
              inspTy: "InspTy",
              inspect: true,
              path: new[]
              {
                new MaterialProcessActualPath()
                {
                  MaterialID = 0,
                  Process = 1,
                  Pallet = 1,
                  LoadStation = 4,
                  UnloadStation = -1,
                  Stops = ImmutableList.Create(
                    new MaterialProcessActualPath.Stop() { StationName = "TestMC", StationNum = 106 }
                  ),
                },
              }
            ),
          }
        )
        .AdvanceMinutes(5) // = 50 min
        .SetNoWork(pal: 1)
        .RemoveExpectedMaterial(sndMats)
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 50 - 30, mats: sndMats),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 3,
              elapsedMin: 5,
              activeMins: 9,
              totalActiveMins: 9,
              mats: sndMats
            ),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectNoChanges();
    }

    [Test]
    public void IgnoresDecrementedJob()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl
              .CreateOneProcOnePathJob(
                unique: "uniq1",
                part: "part1",
                qty: 3,
                priority: 5,
                partsPerPal: 1,
                pals: new[] { 1, 2 },
                luls: new[] { 3, 4 },
                machs: new[] { 5, 6 },
                prog: "345",
                progRev: null,
                loadMins: 8,
                unloadMins: 9,
                machMins: 14,
                fixture: "fix1",
                face: 1
              )
              .AddInsp(proc: 1, path: 1, inspTy: "InspTy", cntr: "Thecounter", max: 2),
          }
        )
        .MoveToMachineQueue(pal: 2, mach: 3)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 345 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        //normally pal 2 should get a new route, but add a decrement
        .AddJobDecrement("uniq1")
        .ExpectNoChanges();
    }

    [Test]
    [Arguments(null)]
    [Arguments("mycasting")]
    public void CastingsFromQueue(string casting)
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateOneProcOnePathJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals: new[] { 1, 2 },
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog: "prog111",
              progRev: 6,
              loadMins: 8,
              unloadMins: 9,
              machMins: 14,
              fixture: "fix1",
              face: 1,
              queue: "thequeue",
              casting: casting
            ),
          },
          new[] { (prog: "prog111", rev: 6L) }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 6, mcMin: 14),
          }
        );

      if (string.IsNullOrEmpty(casting))
      {
        casting = "part1";
      }

      _dsl.AddUnallocatedCasting(queue: "thequeue", rawMatName: "part4", mat: out var unusedMat)
        .ExpectNoChanges()
        .AddUnallocatedCasting(queue: "thequeue", rawMatName: casting, mat: out var queuedMat)
        .UpdateExpectedMaterial(
          queuedMat.MaterialID,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 1,
            LoadOntoFace = 1,
            ProcessAfterLoad = 1,
            PathAfterLoad = 1,
          },
          m => m with { JobUnique = "uniq1", PartName = "part1" }
        )
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              pri: 1,
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 3)
        .UpdateExpectedMaterial(queuedMat.MaterialID, a => a with { ElapsedLoadUnloadTime = TimeSpan.Zero })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(3) // = 3min
        .UpdateExpectedMaterial(
          queuedMat.MaterialID,
          a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(3) }
        )
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .UpdateExpectedMaterial(
          queuedMat.MaterialID,
          a => new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
          m =>
            m with
            {
              Process = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 1,
                Face = 1,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 1,
              lul: 3,
              newPath: 1,
              face: 1,
              unique: "uniq1",
              elapsedMin: 3,
              activeMins: 8,
              loadingMats: new[] { queuedMat },
              totalActiveMins: 8,
              loadedMats: out var mat1,
              part: "part1"
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: mat1),
            FakeIccDsl.RemoveFromQueue(
              "thequeue",
              pos: 1,
              reason: "LoadedToPallet",
              elapMin: 3,
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(proc: 0, path: null, mat1))
            ),
          }
        );
    }

    [Test]
    public void MultiProcSamePallet()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSamePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 100,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "prog222",
              prog2Rev: 6L,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              loadMins2: 10,
              unloadMins2: 11,
              machMins2: 15,
              fixture: "fix1",
              face1: 1,
              face2: 2,
              prec1: 0,
              prec2: 1
            ),
            FakeIccDsl.CreateMultiProcSamePalletJob(
              unique: "uniq2",
              part: "part2",
              qty: 1,
              priority: 50,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog333",
              prog1Rev: null,
              prog2: "prog444",
              prog2Rev: null,
              loadMins1: 16,
              unloadMins1: 17,
              machMins1: 18,
              loadMins2: 19,
              unloadMins2: 20,
              machMins2: 21,
              fixture: "fix1",
              face1: 1,
              face2: 2,
              prec1: 2,
              prec2: 3
            ),
          },
          new[]
          {
            (prog: "prog111", rev: 4L),
            (prog: "prog111", rev: 5L),
            (prog: "prog222", rev: 6L),
            (prog: "prog222", rev: 7L),
            (prog: "prog333", rev: 8L),
            (prog: "prog444", rev: 9L),
          }
        )
        // process 1 only cycle
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2200, name: "prog222", rev: 6, mcMin: 15),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2101, name: "prog333", rev: 8, mcMin: 18),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2201, name: "prog444", rev: 9, mcMin: 21),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .MoveToLoad(pal: 1, lul: 3)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(4) // =4
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 4)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 4,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var AAAproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: AAAproc1),
          }
        )
        .MoveToBuffer(pal: 1, buff: 7)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 7, waitForMach: true, mats: AAAproc1),
          }
        )
        .MoveToMachineQueue(pal: 1, mach: 6)
        .AdvanceMinutes(5) // = 9
        .SetBeforeMC(pal: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 7, waitForMach: true, elapMin: 5, mats: AAAproc1),
            FakeIccDsl.ExpectRotaryStart(pal: 1, mach: 6, mats: AAAproc1),
          }
        )
        .AdvanceMinutes(1) // = 10
        .MoveToMachine(pal: 1, mach: 6)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectRotaryEnd(pal: 1, mach: 6, rotate: true, elapMin: 1, mats: AAAproc1),
          }
        )
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          AAAproc1,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1),
          }
        )
        .AdvanceMinutes(15) // =25
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 15,
              activeMin: 14,
              mats: AAAproc1
            ),
          }
        )
        // now a cycle with process 1 and 2
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .IncrJobStartedCnt("uniq1", path: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .UpdateExpectedMaterial(
          AAAproc1,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Loading,
              LoadOntoPalletNum = 1,
              ProcessAfterLoad = 2,
              PathAfterLoad = 1,
              LoadOntoFace = 2,
              ElapsedLoadUnloadTime = TimeSpan.Zero,
            },
          im => im with { LastCompletedMachiningRouteStopIndex = null }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100, 2200 },
              faces: new[]
              {
                (face: 1, unique: "uniq1", proc: 1, path: 1),
                (face: 2, unique: "uniq1", proc: 2, path: 1),
              }
            ),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
          }
        )
        .AdvanceMinutes(20) // =45
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { Process = 2, Path = 1, Location = m.Location with { Face = 2 } }
        )
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: AAAproc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 20,
              activeMins: 9,
              totalActiveMins: 9 + 8 + 10,
              mats: AAAproc1
            ),
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 45 - 4, mats: AAAproc1),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 20,
              activeMins: 8,
              totalActiveMins: 9 + 8 + 10,
              mats: out var BBBproc1
            ),
            _dsl.LoadToFace(
              pal: 1,
              lul: 4,
              newPath: 1,
              face: 2,
              unique: "uniq1",
              elapsedMin: 20,
              activeMins: 10,
              totalActiveMins: 9 + 8 + 10,
              loadingMats: AAAproc1,
              loadedMats: out var AAAproc2
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: AAAproc2.Concat(BBBproc1)),
          }
        )
        .AdvanceMinutes(2) // = 47min
        .MoveToMachine(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(
          BBBproc1,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: BBBproc1),
          }
        )
        .AdvanceMinutes(20) // = 67min
        .StartMachine(mach: 5, program: 2200)
        .UpdateExpectedMaterial(BBBproc1, a => new() { Type = InProcessMaterialAction.ActionType.Waiting })
        .UpdateExpectedMaterial(
          AAAproc2,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog222 rev6",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 5,
              program: "prog111",
              rev: 5,
              elapsedMin: 20,
              activeMin: 14,
              mats: BBBproc1
            ),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog222", rev: 6, mat: AAAproc2),
          }
        )
        .AdvanceMinutes(30) // = 97min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(BBBproc1, a => a, m => m with { LastCompletedMachiningRouteStopIndex = 0 })
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 5,
              program: "prog222",
              rev: 6,
              elapsedMin: 30,
              activeMin: 15,
              mats: AAAproc2
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 4)
        .SetBeforeUnload(pal: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .IncrJobStartedCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoFace = 2,
            LoadOntoPalletNum = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
          }
        )
        .AdvanceMinutes(10) //= 107 min
        .RemoveExpectedMaterial(AAAproc2.Select(m => m.MaterialID))
        .UpdateExpectedMaterial(
          BBBproc1.Select(m => m.MaterialID),
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im => im with { Location = im.Location with { Face = 2 }, Process = 2, Path = 1 }
        )
        .ClearExpectedLoadCastings()
        .SetAfterLoad(pal: 1)
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: BBBproc1.Count())
        .IncrJobCompletedCnt("uniq1", proc: 2, path: 1, cnt: AAAproc2.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 107 - 45, mats: BBBproc1.Concat(AAAproc2)),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 10,
              activeMins: 9,
              totalActiveMins: 9 + 11 + 8 + 10,
              mats: BBBproc1
            ),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 10,
              activeMins: 11,
              totalActiveMins: 9 + 11 + 8 + 10,
              mats: AAAproc2
            ),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              elapsedMin: 10,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              totalActiveMins: 9 + 11 + 8 + 10,
              activeMins: 8,
              mats: out var CCCproc1
            ),
            _dsl.LoadToFace(
              pal: 1,
              lul: 4,
              newPath: 1,
              face: 2,
              unique: "uniq1",
              elapsedMin: 10,
              activeMins: 10,
              totalActiveMins: 9 + 11 + 8 + 10,
              loadingMats: BBBproc1,
              loadedMats: out var BBBproc2
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: BBBproc2.Concat(CCCproc1)),
          }
        )
        //a full cycle
        .MoveToMachine(pal: 1, mach: 6)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 6, program: 2200)
        .UpdateExpectedMaterial(
          BBBproc2,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog222 rev6",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog222", rev: 6, mat: BBBproc2),
          }
        )
        .AdvanceMinutes(5) // = 112 min
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(BBBproc2, a => new() { Type = InProcessMaterialAction.ActionType.Waiting })
        .UpdateExpectedMaterial(
          CCCproc1,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: CCCproc1),
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog222",
              rev: 6,
              elapsedMin: 5,
              activeMin: 15,
              mats: BBBproc2
            ),
          }
        )
        .AdvanceMinutes(100) // 212 min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(BBBproc2, a => a, m => m with { LastCompletedMachiningRouteStopIndex = 0 })
        .UpdateExpectedMaterial(
          CCCproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 100,
              activeMin: 14,
              mats: CCCproc1
            ),
          }
        )
        // load of new job part2
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 3)
        .UpdateExpectedMaterial(
          CCCproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoFace = 2,
            LoadOntoPalletNum = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .UpdateExpectedMaterial(
          BBBproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .SetExpectedLoadCastings(new[] { (unique: "uniq2", part: "part2", pal: 1, path: 1, face: 1) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .IncrJobStartedCnt(unique: "uniq2", path: 1)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2200, 2101 },
              faces: new[]
              {
                (face: 1, unique: "uniq2", proc: 1, path: 1),
                (face: 2, unique: "uniq1", proc: 2, path: 1),
              }
            ),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3),
          }
        )
        .AdvanceMinutes(10) // =mins 222
        .RemoveExpectedMaterial(BBBproc2)
        .UpdateExpectedMaterial(
          CCCproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im => im with { Location = im.Location with { Face = 2 }, Process = 2, Path = 1 }
        )
        .ClearExpectedLoadCastings()
        .SetAfterLoad(pal: 1)
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: CCCproc1.Count())
        .IncrJobCompletedCnt("uniq1", proc: 2, path: 1, cnt: BBBproc2.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 222 - 107, mats: CCCproc1.Concat(BBBproc2)),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 3,
              elapsedMin: 10,
              activeMins: 9,
              totalActiveMins: 9 + 11 + 16 + 10,
              mats: CCCproc1
            ),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 3,
              elapsedMin: 10,
              activeMins: 11,
              totalActiveMins: 9 + 11 + 16 + 10,
              mats: BBBproc2
            ),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              elapsedMin: 10,
              face: 1,
              unique: "uniq2",
              cnt: 1,
              path: 1,
              activeMins: 16,
              totalActiveMins: 9 + 11 + 16 + 10,
              mats: out var DDDproc1
            ),
            _dsl.LoadToFace(
              pal: 1,
              lul: 3,
              newPath: 1,
              face: 2,
              unique: "uniq1",
              elapsedMin: 10,
              activeMins: 10,
              totalActiveMins: 9 + 11 + 16 + 10,
              loadingMats: CCCproc1,
              loadedMats: out var CCCproc2
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: CCCproc2.Concat(DDDproc1)),
          }
        )
        .MoveToMachine(pal: 1, mach: 6)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 6, program: 2101)
        .UpdateExpectedMaterial(
          DDDproc1,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog333 rev8",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(18),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog333", rev: 8, mat: DDDproc1),
          }
        )
        .AdvanceMinutes(4) // = 226min
        .StartMachine(mach: 6, program: 2200)
        .UpdateExpectedMaterial(DDDproc1, a => new() { Type = InProcessMaterialAction.ActionType.Waiting })
        .UpdateExpectedMaterial(
          CCCproc2,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog222 rev6",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog222", rev: 6, mat: CCCproc2),
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog333",
              rev: 8,
              elapsedMin: 4,
              activeMin: 18,
              mats: DDDproc1
            ),
          }
        )
        .AdvanceMinutes(4) // = 230min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(DDDproc1, a => a, m => m with { LastCompletedMachiningRouteStopIndex = 0 })
        .UpdateExpectedMaterial(
          CCCproc2,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog222",
              rev: 6,
              elapsedMin: 4,
              activeMin: 15,
              mats: CCCproc2
            ),
          }
        )
        // no new load, since quantity of 3 reached
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 3)
        .UpdateExpectedMaterial(
          DDDproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoFace = 2,
            LoadOntoPalletNum = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .UpdateExpectedMaterial(
          CCCproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2201 },
              faces: new[] { (face: 2, unique: "uniq2", proc: 2, path: 1) }
            ),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3),
          }
        )
        .AdvanceMinutes(2) // = 232 min
        .RemoveExpectedMaterial(CCCproc2.Select(m => m.MaterialID))
        .UpdateExpectedMaterial(
          DDDproc1.Select(m => m.MaterialID),
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im => im with { Location = im.Location with { Face = 2 }, Process = 2, Path = 1 }
        )
        .SetAfterLoad(pal: 1)
        .IncrJobCompletedCnt("uniq2", proc: 1, path: 1, cnt: DDDproc1.Count())
        .IncrJobCompletedCnt("uniq1", proc: 2, path: 1, cnt: CCCproc2.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 232 - 222, mats: DDDproc1.Concat(CCCproc2)),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 3,
              elapsedMin: 2,
              activeMins: 17,
              totalActiveMins: 17 + 11 + 19,
              mats: DDDproc1
            ),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 3,
              elapsedMin: 2,
              activeMins: 11,
              totalActiveMins: 17 + 11 + 19,
              mats: CCCproc2
            ),
            _dsl.LoadToFace(
              pal: 1,
              lul: 3,
              newPath: 1,
              face: 2,
              unique: "uniq2",
              elapsedMin: 2,
              activeMins: 19,
              totalActiveMins: 17 + 11 + 19,
              loadingMats: DDDproc1,
              loadedMats: out var DDDproc2
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: DDDproc2),
          }
        )
        // a cycle with only proc2
        .MoveToMachine(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 5, program: 2201)
        .UpdateExpectedMaterial(
          DDDproc2,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog444 rev9",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(21),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog444", rev: 9, mat: DDDproc2),
          }
        )
        .AdvanceMinutes(20) // = 252 min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          DDDproc2,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 5,
              program: "prog444",
              rev: 9,
              elapsedMin: 20,
              activeMin: 21,
              mats: DDDproc2
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 3)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(
          DDDproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        // nothing new loaded
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(10) // = 262min
        .SetNoWork(pal: 1)
        .RemoveExpectedMaterial(DDDproc2.Select(m => m.MaterialID))
        .IncrJobCompletedCnt("uniq2", proc: 2, path: 1, cnt: DDDproc2.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 262 - 232, mats: DDDproc2),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 3,
              elapsedMin: 10,
              activeMins: 20,
              totalActiveMins: 20,
              mats: DDDproc2
            ),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectNoChanges()
        .AdvanceMinutes(11 * 60) // advance 11 hours, should not yet be archived
        .ExpectNoChanges()
        .ExpectJobArchived(uniq: "uniq1", isArchived: false)
        .AdvanceMinutes(60) // after 12 hours from completed, should be archived
        .ExpectNoChanges()
        .ExpectJobArchived(uniq: "uniq1", isArchived: true);
    }

    [Test]
    public void MultipleProcessSeparatePallets()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSeparatePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals1: new[] { 1 },
              pals2: new[] { 2 },
              load1: new[] { 3, 4 },
              unload1: new[] { 3, 4 },
              load2: new[] { 3, 4 },
              unload2: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "654",
              prog2Rev: null,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              machMins2: 10,
              loadMins2: 11,
              unloadMins2: 12,
              fixture: "fix1",
              transQ: "qqq"
            ),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // first process on pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2) // = 2min
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 2)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 2,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var AAAproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: AAAproc1),
          }
        )
        .MoveToMachineQueue(pal: 1, mach: 6)
        .ExpectNoChanges()
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          AAAproc1,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1),
          }
        )
        .AdvanceMinutes(10) // = 12min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 10,
              activeMin: 14,
              mats: AAAproc1
            ),
          }
        )
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
          }
        )
        .AdvanceMinutes(15) // 27min
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 2,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          },
          m =>
            m with
            {
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "qqq",
                QueuePosition = 0,
              },
            }
        )
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: AAAproc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 27 - 2, mats: AAAproc1),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 15,
              activeMins: 9,
              totalActiveMins: 9 + 8,
              mats: AAAproc1
            ),
            FakeIccDsl.AddToQueue("qqq", 0, reason: "Unloaded", AAAproc1),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              elapsedMin: 15,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              activeMins: 8,
              totalActiveMins: 9 + 8,
              mats: out var BBBproc1
            ),
            FakeIccDsl.ExpectNewRoute(
              pal: 2,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 654 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: BBBproc1),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: BBBproc1),
          }
        )
        // load process on pallet 2
        .MoveToLoad(pal: 2, lul: 3)
        .UpdateExpectedMaterial(AAAproc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.Zero })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3) })
        .AdvanceMinutes(7) // =34 min
        .SetAfterLoad(pal: 2)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im =>
            im with
            {
              Process = 2,
              Path = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 2,
                Face = 1,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 2,
              face: 1,
              newPath: 1,
              unique: "uniq1",
              lul: 3,
              elapsedMin: 7,
              activeMins: 11,
              totalActiveMins: 11,
              loadingMats: AAAproc1,
              loadedMats: out var AAAproc2
            ),
            FakeIccDsl.ExpectPalletStart(pal: 2, mats: AAAproc2),
            FakeIccDsl.RemoveFromQueue(
              "qqq",
              pos: 0,
              reason: "LoadedToPallet",
              elapMin: 7,
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(1, 1, AAAproc2))
            ),
          }
        )
        // machine both pallets 1 and 2
        .MoveToMachine(pal: 2, mach: 6)
        .SetBeforeMC(pal: 2)
        .StartMachine(mach: 6, program: 654)
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "654",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(10),
          }
        )
        .ExpectTransition(
          new[] { FakeIccDsl.ExpectMachineBegin(pal: 2, machine: 6, program: "654", mat: AAAproc2) }
        )
        .AdvanceMinutes(4) // = 38min
        .MoveToMachine(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(
          AAAproc2,
          a =>
            a with
            {
              ElapsedMachiningTime = TimeSpan.FromMinutes(4),
              ExpectedRemainingMachiningTime = a.ExpectedRemainingMachiningTime - TimeSpan.FromMinutes(4),
            }
        )
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(
              pal: 1,
              stocker: 1,
              elapMin: 7 + 4,
              waitForMach: true,
              mats: BBBproc1
            ),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: BBBproc1),
          }
        )
        // pallet can be moved out of machine for operator to fix fault.  Might go to after MC temporarily
        .AdvanceMinutes(2) // = 40min
        .EndMachine(mach: 5)
        .SetPalletAlarm(pal: 1, alarm: true, code: PalletAlarmCode.RoutingFault, "Pallet 1 has routing fault")
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          BBBproc1,
          a =>
            a with
            {
              ElapsedMachiningTime = TimeSpan.FromMinutes(2),
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14 - 2),
            }
        )
        .UpdateExpectedMaterial(
          AAAproc2,
          a =>
            a with
            {
              ElapsedMachiningTime = a.ElapsedMachiningTime + TimeSpan.FromMinutes(2),
              ExpectedRemainingMachiningTime = a.ExpectedRemainingMachiningTime - TimeSpan.FromMinutes(2),
            }
        )
        .ExpectNoChanges()
        .MoveToMachineQueue(pal: 1, mach: 5)
        .UpdateExpectedMaterial(BBBproc1, a => new() { Type = InProcessMaterialAction.ActionType.Waiting })
        .ExpectNoChanges()
        // now return back to machine
        .MoveToMachine(pal: 1, mach: 5)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.FromMinutes(2),
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14 - 2),
          }
        )
        .ExpectNoChanges()
        .StartMachine(mach: 5, program: 2100)
        .ExpectNoChanges()
        // various combinations of alarms/statuses are ignored
        .SetPalletAlarm(pal: 1, alarm: false)
        .SetMachAlarm(mc: 5, link: false, alarm: false)
        .ExpectNoChanges()
        .SetMachAlarm(mc: 5, link: true, alarm: true)
        .ExpectNoChanges()
        // program returns to active w/ no alarms
        .SetPalletAlarm(pal: 1, alarm: false)
        .SetBeforeMC(pal: 1)
        .SetMachAlarm(mc: 5, link: true, alarm: false)
        .ExpectNoChanges()
        // program finishes normally
        .AdvanceMinutes(2) // = 42min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .UpdateExpectedMaterial(
          AAAproc2,
          a =>
            a with
            {
              ElapsedMachiningTime = TimeSpan.FromMinutes(4 + 4),
              ExpectedRemainingMachiningTime = a.ExpectedRemainingMachiningTime - TimeSpan.FromMinutes(2),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 5,
              program: "prog111",
              rev: 5,
              elapsedMin: 4,
              activeMin: 14,
              mats: BBBproc1
            ),
          }
        )
        .AdvanceMinutes(1) // =43min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 2)
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 2,
              mach: 6,
              program: "654",
              elapsedMin: 4 + 4 + 1,
              activeMin: 10,
              mats: AAAproc2
            ),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .MoveToBuffer(pal: 2, buff: 2)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: BBBproc1),
            FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: false, mats: AAAproc2),
          }
        )
        // unload process 1 first into queue
        .MoveToLoad(pal: 1, lul: 4)
        .SetBeforeUnload(pal: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .IncrJobStartedCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 0, waitForMach: false, mats: BBBproc1),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
          }
        )
        .AdvanceMinutes(5) // = 48min
        .SetAfterLoad(pal: 1)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im =>
            im with
            {
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "qqq",
                QueuePosition = 0,
              },
            }
        )
        .ClearExpectedLoadCastings()
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: BBBproc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 48 - 27, mats: BBBproc1),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 5,
              activeMins: 9,
              totalActiveMins: 9 + 8,
              mats: BBBproc1
            ),
            FakeIccDsl.AddToQueue("qqq", 0, reason: "Unloaded", mat: BBBproc1),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 5,
              activeMins: 8,
              totalActiveMins: 9 + 8,
              mats: out var CCCproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: CCCproc1),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        // now unload and load pallet 2
        .MoveToLoad(pal: 2, lul: 3)
        .SetBeforeUnload(pal: 2)
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoFace = 1,
            LoadOntoPalletNum = 2,
            PathAfterLoad = 1,
            ProcessAfterLoad = 2,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: CCCproc1),
            FakeIccDsl.ExpectStockerEnd(pal: 2, stocker: 2, elapMin: 5, waitForMach: false, mats: AAAproc2),
            FakeIccDsl.ExpectRouteIncrement(pal: 2, newCycleCnt: 2),
            FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3),
          }
        )
        .AdvanceMinutes(12) // = 60min
        .SetAfterLoad(pal: 2)
        .RemoveExpectedMaterial(AAAproc2)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im =>
            im with
            {
              Process = 2,
              Path = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 2,
                Face = 1,
              },
            }
        )
        .IncrJobCompletedCnt("uniq1", proc: 2, path: 1, cnt: AAAproc2.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 2, mins: 60 - 34, mats: AAAproc2),
            FakeIccDsl.UnloadFromFace(
              pal: 2,
              lul: 3,
              elapsedMin: 12,
              activeMins: 12,
              totalActiveMins: 12 + 11,
              mats: AAAproc2
            ),
            _dsl.LoadToFace(
              pal: 2,
              face: 1,
              newPath: 1,
              unique: "uniq1",
              lul: 3,
              elapsedMin: 12,
              activeMins: 11,
              totalActiveMins: 12 + 11,
              loadingMats: BBBproc1,
              loadedMats: out var BBBproc2
            ),
            FakeIccDsl.RemoveFromQueue(
              queue: "qqq",
              pos: 0,
              reason: "LoadedToPallet",
              elapMin: 12,
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(1, 1, BBBproc2))
            ),
            FakeIccDsl.ExpectPalletStart(pal: 2, mats: BBBproc2),
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        // run machine cycles for both pallets
        .MoveToMachine(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(
          CCCproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, waitForMach: true, elapMin: 12, mats: CCCproc1),
            FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: true, mats: BBBproc2),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: CCCproc1),
          }
        )
        .AdvanceMinutes(1) // = 61 min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          CCCproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 5,
              program: "prog111",
              rev: 5,
              elapsedMin: 1,
              activeMin: 14,
              mats: CCCproc1
            ),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .MoveToMachine(pal: 2, mach: 5)
        .SetBeforeMC(pal: 2)
        .StartMachine(mach: 5, program: 654)
        .UpdateExpectedMaterial(
          BBBproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "654",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(10),
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 2, stocker: 2, waitForMach: true, elapMin: 1, mats: BBBproc2),
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: CCCproc1),
            FakeIccDsl.ExpectMachineBegin(pal: 2, machine: 5, program: "654", mat: BBBproc2),
          }
        )
        .AdvanceMinutes(2) // = 63 min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 2)
        .UpdateExpectedMaterial(
          BBBproc2,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 2,
              mach: 5,
              program: "654",
              elapsedMin: 2,
              activeMin: 10,
              mats: BBBproc2
            ),
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: false, mats: BBBproc2),
          }
        )
        //now unload pallet 2 first
        .MoveToLoad(pal: 2, lul: 4)
        .SetBeforeUnload(pal: 2)
        .UpdateExpectedMaterial(
          BBBproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 2, stocker: 2, waitForMach: false, elapMin: 0, mats: BBBproc2),
            FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 4),
          }
        )
        .AdvanceMinutes(3) // = 66 min
        .SetNoWork(pal: 2)
        .RemoveExpectedMaterial(BBBproc2)
        .IncrJobCompletedCnt("uniq1", proc: 2, path: 1, cnt: BBBproc2.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 2, mins: 66 - 60, mats: BBBproc2),
            FakeIccDsl.UnloadFromFace(
              pal: 2,
              lul: 4,
              elapsedMin: 3,
              activeMins: 12,
              totalActiveMins: 12,
              mats: BBBproc2
            ),
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        // now unload pal 1 which should trigger pallet 2.  Nothing new should be loaded since quantity of 3 reached
        .MoveToLoad(pal: 1, lul: 4)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(
          CCCproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 5, waitForMach: false, mats: CCCproc1),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
          }
        )
        .AdvanceMinutes(3) //= 69 min
        .SetNoWork(pal: 1)
        .UpdateExpectedMaterial(
          CCCproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 2,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          },
          im =>
            im with
            {
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "qqq",
                QueuePosition = 0,
              },
              LastCompletedMachiningRouteStopIndex = null,
            }
        )
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: CCCproc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 69 - 48, mats: CCCproc1),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 3,
              activeMins: 9,
              totalActiveMins: 9,
              mats: CCCproc1
            ),
            FakeIccDsl.AddToQueue("qqq", 0, reason: "Unloaded", mat: CCCproc1),
            FakeIccDsl.ExpectRouteIncrement(
              pal: 2,
              newCycleCnt: 1,
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
            ),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        // now load pal 2
        .MoveToLoad(pal: 2, lul: 4)
        .SetBeforeLoad(pal: 2)
        .UpdateExpectedMaterial(CCCproc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.Zero })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 4) })
        .AdvanceMinutes(22) // = 91 min
        .SetAfterLoad(pal: 2)
        .UpdateExpectedMaterial(
          CCCproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im =>
            im with
            {
              Process = 2,
              Path = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 2,
                Face = 1,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 2,
              face: 1,
              newPath: 1,
              unique: "uniq1",
              lul: 4,
              elapsedMin: 22,
              activeMins: 11,
              totalActiveMins: 11,
              loadingMats: CCCproc1,
              loadedMats: out var CCCproc2
            ),
            FakeIccDsl.ExpectPalletStart(pal: 2, mats: CCCproc2),
            FakeIccDsl.RemoveFromQueue(
              queue: "qqq",
              pos: 0,
              reason: "LoadedToPallet",
              elapMin: 22,
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(1, 1, CCCproc2))
            ),
          }
        );
    }

    [Test]
    public void SeparateLoadUnloadStations()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSeparatePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals1: new[] { 1 },
              load1: new[] { 3 },
              unload1: new[] { 4 },
              pals2: new[] { 2 },
              load2: new[] { 4 },
              unload2: new[] { 5 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: 5,
              prog2: "prog222",
              prog2Rev: null,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              machMins2: 10,
              loadMins2: 11,
              unloadMins2: 12,
              fixture: "fix1",
              transQ: "qqq"
            ),
          },
          new[] { (prog: "prog111", rev: 5L), (prog: "prog222", rev: 6L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2200, name: "prog222", rev: 6, mcMin: 10),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3 },
              unloads: new[] { 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(1) // = 1min
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 1)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 1,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var AAAproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: AAAproc1),
          }
        )
        .MoveToMachine(pal: 1, mach: 6)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          AAAproc1,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1),
          }
        )
        .AdvanceMinutes(10) // = 11min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 10,
              activeMin: 14,
              mats: AAAproc1
            ),
          }
        )
        // when moving to load station, should just unload and not load

        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4) })
        .AdvanceMinutes(4) // = 15min
        .SetAfterUnload(pal: 1)
        .SetNoWork(pal: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 2,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          },
          im =>
            im with
            {
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "qqq",
                QueuePosition = 0,
              },
              LastCompletedMachiningRouteStopIndex = null,
            }
        )
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: AAAproc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 15 - 1, mats: AAAproc1),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 4,
              activeMins: 9,
              totalActiveMins: 9,
              mats: AAAproc1
            ),
            FakeIccDsl.AddToQueue("qqq", 0, reason: "Unloaded", AAAproc1),
            FakeIccDsl.ExpectNewRoute(
              pal: 2,
              pri: 1,
              luls: new[] { 4 },
              unloads: new[] { 5 },
              machs: new[] { 5, 6 },
              progs: new[] { 2200 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
            ),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectRouteIncrement(
              pal: 1,
              newCycleCnt: 1,
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // now both pal 1 and pal 2 are in buffer but have routes to load
        .MoveToLoad(pal: 2, lul: 4)
        .SetBeforeLoad(pal: 2)
        .UpdateExpectedMaterial(AAAproc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.Zero })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 4) })
        .AdvanceMinutes(1) // =16min
        .UpdateExpectedMaterial(AAAproc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(1) })
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .SetBeforeLoad(pal: 1)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(4) // = 20min
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 4)
        .SetAfterLoad(pal: 2)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im =>
            im with
            {
              Process = 2,
              Path = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 2,
                Face = 1,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 2,
              face: 1,
              newPath: 1,
              unique: "uniq1",
              lul: 4,
              elapsedMin: 5,
              activeMins: 11,
              totalActiveMins: 11,
              loadingMats: AAAproc1,
              loadedMats: out var AAAproc2
            ),
            FakeIccDsl.ExpectPalletStart(pal: 2, mats: AAAproc2),
            FakeIccDsl.RemoveFromQueue(
              "qqq",
              pos: 0,
              elapMin: 5,
              reason: "LoadedToPallet",
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(1, 1, AAAproc2))
            ),
          }
        );
    }

    [Test]
    public void MultipleMachineStops()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateOneProcOnePathMultiStepJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs1: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              machs2: new[] { 1, 2 },
              prog2: "prog222",
              prog2Rev: null,
              reclamp: new[] { 2 },
              reclampMins: 10,
              loadMins: 8,
              unloadMins: 9,
              machMins1: 14,
              machMins2: 15,
              fixture: "fix1",
              face: 1
            ),
          },
          new[] { (prog: "prog111", rev: 5L), (prog: "prog222", rev: 6L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2101, name: "prog222", rev: 6, mcMin: 15),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              loads: new[] { 3, 4 },
              machs1: new[] { 5, 6 },
              progs1: new[] { 2100 },
              machs2: new[] { 1, 2 },
              progs2: new[] { 2101 },
              reclamp: new[] { 2 },
              unloads: new[] { 3, 4 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(4) // =4
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 4)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 4,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var fstMats
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: fstMats),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: fstMats),
          }
        )
        .SetBeforeMC(pal: 1, machStepOffset: 0)
        .MoveToMachine(pal: 1, mach: 5)
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(
          fstMats,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 0, waitForMach: true, mats: fstMats),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: fstMats),
          }
        )
        .AdvanceMinutes(5) // = 9min
        .UpdateExpectedMaterial(
          fstMats,
          a =>
            a with
            {
              ElapsedMachiningTime = TimeSpan.FromMinutes(5),
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14 - 5),
            }
        )
        .ExpectNoChanges()
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1, machStepOffset: 0)
        .UpdateExpectedMaterial(
          fstMats,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 5,
              program: "prog111",
              rev: 5,
              elapsedMin: 5,
              activeMin: 14,
              mats: fstMats
            ),
          }
        )
        // I don't know exactly when the pallet goes to BeforeMC on the second step.
        // Does the pallet always go to the buffer between machining and the transition happens at that time?
        // Just in case, CellState correctly handles setting BeforeMC as soon as it is on the outbound
        .SetBeforeMC(pal: 1, machStepOffset: 1)
        .MoveToMachineOutboundQueue(pal: 1, mach: 5)
        .ExpectNoChanges()
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: fstMats),
          }
        )
        .MoveToMachine(pal: 1, mach: 2)
        .StartMachine(mach: 2, program: 2101)
        .UpdateExpectedMaterial(
          fstMats,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog222 rev6",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, waitForMach: true, elapMin: 0, mats: fstMats),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 2, program: "prog222", rev: 6, mat: fstMats),
          }
        )
        .AdvanceMinutes(10) // = 19min
        .UpdateExpectedMaterial(
          fstMats,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog222 rev6",
              ElapsedMachiningTime = TimeSpan.FromMinutes(10),
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15 - 10),
            }
        )
        .ExpectNoChanges()
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1, machStepOffset: 1)
        .UpdateExpectedMaterial(
          fstMats,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 1 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 2,
              program: "prog222",
              rev: 6,
              elapsedMin: 10,
              activeMin: 15,
              mats: fstMats
            ),
          }
        )
        .MoveToBuffer(pal: 1, buff: 2)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 2, waitForMach: false, mats: fstMats),
          }
        )
        .SetBeforeReclamp(pal: 1, reclampStepOffset: 0)
        .ExpectNoChanges()
        .UpdateExpectedMaterial(
          fstMats,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .MoveToLoad(pal: 1, lul: 2)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 2, waitForMach: false, elapMin: 0, mats: fstMats),
            FakeIccDsl.ExpectReclampBegin(pal: 1, lul: 2, mats: fstMats),
          }
        )
        .AdvanceMinutes(3) // = 22min
        .UpdateExpectedMaterial(fstMats, a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(3) })
        .AdvanceMinutes(3) // = 26min
        .SetAfterReclamp(pal: 1, reclampStepOffset: 0)
        .UpdateExpectedMaterial(
          fstMats,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 2 }
        )
        .ExpectTransition(
          new[] { FakeIccDsl.ExpectReclampEnd(pal: 1, lul: 2, elapsedMin: 6, activeMin: 10, mats: fstMats) }
        )
        .MoveToBuffer(pal: 1, buff: 4)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 4, waitForMach: false, mats: fstMats),
          }
        )
        .MoveToLoad(pal: 1, lul: 4)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(
          fstMats,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 4, elapMin: 0, waitForMach: false, mats: fstMats),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
          }
        )
        .AdvanceMinutes(10) // = 35min
        .SetAfterLoad(pal: 1)
        .RemoveExpectedMaterial(fstMats)
        .ClearExpectedLoadCastings()
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: fstMats.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 35 - 4, mats: fstMats),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 10,
              activeMins: 9,
              totalActiveMins: 9 + 8,
              mats: fstMats
            ),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 10,
              activeMins: 8,
              totalActiveMins: 9 + 8,
              mats: out var sndMats
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: sndMats),
          }
        );
    }

    [Test]
    public void MissEvents()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateOneProcOnePathJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog: "prog111",
              progRev: null,
              loadMins: 8,
              unloadMins: 9,
              machMins: 14,
              fixture: "fix1",
              face: 1
            ),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 1)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 1) })
        .AdvanceMinutes(4) // =4
        // skip unload end and send straight to machine
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 5)
        .StartMachine(mach: 5, program: 2100)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 1,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 4,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var fstMats,
              adj: im =>
                im with
                {
                  Action = new()
                  {
                    Type = InProcessMaterialAction.ActionType.Machining,
                    Program = "prog111 rev5",
                    ElapsedMachiningTime = TimeSpan.Zero,
                    ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
                  },
                }
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: fstMats),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", fstMats, rev: 5),
          }
        )
        .AdvanceMinutes(min: 10)
        // now skip after machine and go straight to unload
        .EndMachine(mach: 5)
        .MoveToLoad(pal: 1, lul: 3)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(
          fstMats,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .IncrJobStartedCnt(unique: "uniq1", path: 1)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 5,
              program: "prog111",
              rev: 5,
              elapsedMin: 10,
              activeMin: 14,
              mats: fstMats
            ),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
          }
        )
        .AdvanceMinutes(min: 6) //= 20 min
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .RemoveExpectedMaterial(fstMats)
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: fstMats.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 20 - 4, mats: fstMats),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 3,
              elapsedMin: 6,
              activeMins: 9,
              totalActiveMins: 9 + 8,
              mats: fstMats
            ),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 6,
              activeMins: 8,
              totalActiveMins: 9 + 8,
              mats: out var sndMats
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: sndMats),
          }
        )
        .AdvanceMinutes(20)
        // go straight to after mc
        .MoveToBuffer(pal: 1, buff: 1)
        .SetAfterMC(pal: 1)
        .SetExecutedStationNum(
          pal: 1,
          new[] { NiigataStationNum.LoadStation(3), NiigataStationNum.Machine(5, _dsl.StatNames) }
        ) // load 3, machine 5
        .UpdateExpectedMaterial(sndMats, a => a, m => m with { LastCompletedMachiningRouteStopIndex = 0 })
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: sndMats),
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 5,
              program: "prog111",
              rev: 5,
              elapsedMin: 0,
              activeMin: 14,
              mats: sndMats
            ),
          }
        );
    }

    [Test]
    [Arguments(null)]
    [Arguments("rawmat")]
    public void ProgramsInWorkorders(string rawMatName)
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSeparatePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 5,
              priority: 5,
              partsPerPal: 1,
              pals1: new[] { 1, 2, 3 },
              pals2: new[] { 4, 5 },
              load1: new[] { 3, 4 },
              unload1: new[] { 3, 4 },
              load2: new[] { 3, 4 },
              unload2: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: null,
              prog1Rev: null,
              prog2: null,
              prog2Rev: null,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              machMins2: 10,
              reclamp1: new[] { 2 },
              reclamp1Mins: 4,
              reclamp2: new[] { 2 },
              reclamp2Min: 5,
              loadMins2: 11,
              unloadMins2: 12,
              fixture: "fix1",
              rawMatName: rawMatName,
              castingQ: "castingQ",
              transQ: "qqq"
            ),
          },
          progs: new[]
          {
            (prog: "prog111", rev: 4L),
            (prog: "prog111", rev: 5L),
            (prog: "prog222", rev: 6L),
            (prog: "prog333", rev: 7L),
          },
          workorders: new[]
          {
            new Workorder()
            {
              WorkorderId = "work1",
              Part = "part1",
              Quantity = 0,
              DueDate = DateTime.UtcNow,
              Priority = 0,
              Programs = ImmutableList.Create(
                new ProgramForJobStep()
                {
                  ProcessNumber = 1,
                  StopIndex = 0,
                  ProgramName = "prog111",
                  Revision = null,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 2,
                  ProgramName = "prog222",
                  Revision = 6,
                }
              ),
            },
            new Workorder()
            {
              WorkorderId = "work2",
              Part = "part1",
              Quantity = 0,
              DueDate = DateTime.UtcNow,
              Priority = 0,
              Programs = ImmutableList.Create(
                new ProgramForJobStep()
                {
                  ProcessNumber = 1,
                  StopIndex = 0,
                  ProgramName = "prog111",
                  Revision = 4,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 2,
                  ProgramName = "prog333",
                  Revision = 7,
                }
              ),
            },
          }
        )
        .ExpectNoChanges()
        .MoveToMachineOutboundQueue(pal: 2, mach: 1)
        .MoveToMachineOutboundQueue(pal: 3, mach: 2)
        .AddUnallocatedCasting(
          queue: "castingQ",
          rawMatName: rawMatName ?? "part1",
          mat: out var queuedMat1,
          workorder: "work1",
          numProc: 2
        )
        .AddUnallocatedCasting(
          queue: "castingQ",
          rawMatName: rawMatName ?? "part1",
          mat: out var queuedMat2,
          workorder: "work2",
          numProc: 2
        )
        .IncrJobStartedCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(
          queuedMat1.MaterialID,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 1,
            LoadOntoFace = 1,
            ProcessAfterLoad = 1,
            PathAfterLoad = 1,
          },
          m => m with { JobUnique = "uniq1", PartName = "part1" }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2200, name: "prog222", rev: 6, mcMin: 10),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2101, name: "prog111", rev: 4, mcMin: 14),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2201, name: "prog333", rev: 7, mcMin: 10),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) },
              reclamp: new[] { 2 },
              reclampFirst: false,
              progOverride: new[]
              {
                (
                  face: 1,
                  progs: new[]
                  {
                    new ProgramsForProcess()
                    {
                      MachineStopIndex = 0,
                      ProgramName = "prog111",
                      Revision = 5,
                    },
                  }
                ),
              }
            ),
          }
        )
        // load pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .UpdateExpectedMaterial(queuedMat1.MaterialID, a => a with { ElapsedLoadUnloadTime = TimeSpan.Zero })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2)
        .SetInQueue(queuedMat2, queue: "castingQ", pos: 0)
        .UpdateExpectedMaterial(
          queuedMat1.MaterialID,
          a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(2) },
          m => m with { Location = m.Location with { QueuePosition = 1 } }
        )
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .UpdateExpectedMaterial(
          queuedMat1.MaterialID,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m =>
            m with
            {
              Process = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 1,
                Face = 1,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 1,
              lul: 3,
              face: 1,
              newPath: 1,
              unique: "uniq1",
              elapsedMin: 2,
              activeMins: 8,
              totalActiveMins: 8,
              loadingMats: new[] { queuedMat1 },
              loadedMats: out var mat1,
              part: "part1"
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: mat1),
            FakeIccDsl.RemoveFromQueue(
              "castingQ",
              pos: 1,
              elapMin: 2,
              reason: "LoadedToPallet",
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(0, null, mat1))
            ),
          }
        )
        .SetBeforeMC(pal: 1)
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: mat1),
          }
        )
        .AdvanceMinutes(2)
        /// update work1 process 1 to have a new program, old already running program should not be changed because part is loaded already
        .ReplaceWorkorders(
          new[]
          {
            new Workorder()
            {
              WorkorderId = "work1",
              Part = "part1",
              Quantity = 0,
              DueDate = DateTime.UtcNow,
              Priority = 0,
              Programs = ImmutableList.Create(
                new ProgramForJobStep()
                {
                  ProcessNumber = 1,
                  StopIndex = 0,
                  ProgramName = "prog111",
                  Revision = 10,
                }, // this is the only one that changes
                new ProgramForJobStep()
                {
                  ProcessNumber = 2,
                  ProgramName = "prog222",
                  Revision = 6,
                }
              ),
            },
            new Workorder()
            {
              WorkorderId = "work2",
              Part = "part1",
              Quantity = 0,
              DueDate = DateTime.UtcNow,
              Priority = 0,
              Programs = ImmutableList.Create(
                new ProgramForJobStep()
                {
                  ProcessNumber = 1,
                  StopIndex = 0,
                  ProgramName = "prog111",
                  Revision = 4,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 2,
                  ProgramName = "prog333",
                  Revision = 7,
                }
              ),
            },
          },
          new[]
          {
            new MachineFramework.NewProgramContent()
            {
              ProgramName = "prog111",
              ProgramContent = "prog111 rev 10 ct",
              Revision = 10,
            },
          }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2102, name: "prog111", rev: 10, mcMin: 14),
          }
        )
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          mat1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 2, waitForMach: true, mats: mat1),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: mat1),
          }
        )
        .AdvanceMinutes(4)
        .SetAfterMC(pal: 1)
        .EndMachine(mach: 6)
        .UpdateExpectedMaterial(
          mat1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 4,
              activeMin: 14,
              mats: mat1
            ),
          }
        )
        .SetBeforeReclamp(pal: 1)
        .MoveToLoad(pal: 1, lul: 2)
        .UpdateExpectedMaterial(
          mat1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectReclampBegin(pal: 1, lul: 2, mats: mat1) })
        .AdvanceMinutes(2)
        .SetAfterReclamp(pal: 1)
        .UpdateExpectedMaterial(
          mat1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 1 }
        )
        .ExpectTransition(
          new[] { FakeIccDsl.ExpectReclampEnd(pal: 1, lul: 2, mats: mat1, elapsedMin: 2, activeMin: 4) }
        )
        // second one goes on a pallet with different programs (2101 and revision 4)
        .MoveToBuffer(pal: 2, buff: 2)
        .IncrJobStartedCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(
          queuedMat2.MaterialID,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 2,
            LoadOntoFace = 1,
            ProcessAfterLoad = 1,
            PathAfterLoad = 1,
          },
          m => m with { JobUnique = "uniq1", PartName = "part1" }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 2,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2101 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) },
              reclamp: new[] { 2 },
              reclampFirst: false,
              progOverride: new[]
              {
                (
                  face: 1,
                  progs: new[]
                  {
                    new ProgramsForProcess()
                    {
                      MachineStopIndex = 0,
                      ProgramName = "prog111",
                      Revision = 4,
                    },
                  }
                ),
              }
            ),
          }
        )
        .MoveToLoad(pal: 2, lul: 4)
        .UpdateExpectedMaterial(queuedMat2.MaterialID, a => a with { ElapsedLoadUnloadTime = TimeSpan.Zero })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 4) })
        .AdvanceMinutes(5)
        .SetAfterLoad(pal: 2)
        .UpdateExpectedMaterial(
          queuedMat2.MaterialID,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m =>
            m with
            {
              Process = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 2,
                Face = 1,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 2,
              lul: 4,
              face: 1,
              newPath: 1,
              unique: "uniq1",
              elapsedMin: 5,
              activeMins: 8,
              totalActiveMins: 8,
              loadingMats: new[] { queuedMat2 },
              loadedMats: out var mat2,
              part: "part1"
            ),
            FakeIccDsl.ExpectPalletStart(pal: 2, mats: mat2),
            FakeIccDsl.RemoveFromQueue(
              "castingQ",
              pos: 0,
              elapMin: 13,
              reason: "LoadedToPallet",
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(0, null, mat2))
            ),
          }
        )
        .SetBeforeMC(pal: 2)
        .MoveToMachine(pal: 2, mach: 5)
        .StartMachine(mach: 5, program: 2101)
        .UpdateExpectedMaterial(
          mat2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev4",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
          }
        )
        .ExpectTransition(
          new[] { FakeIccDsl.ExpectMachineBegin(pal: 2, machine: 5, program: "prog111", rev: 4, mat: mat2) }
        )
        .AdvanceMinutes(3)
        .SetAfterMC(pal: 2)
        .EndMachine(mach: 5)
        .UpdateExpectedMaterial(
          mat2,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 2,
              mach: 5,
              program: "prog111",
              rev: 4,
              elapsedMin: 3,
              activeMin: 14,
              mats: mat2
            ),
          }
        )
        // new material on work1 uses updated program
        .AddUnallocatedCasting(
          queue: "castingQ",
          rawMatName: rawMatName ?? "part1",
          mat: out var queuedMat5,
          workorder: "work1",
          numProc: 2
        )
        .MoveToBuffer(pal: 3, buff: 3)
        .IncrJobStartedCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(
          queuedMat5.MaterialID,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 3,
            LoadOntoFace = 1,
            ProcessAfterLoad = 1,
            PathAfterLoad = 1,
          },
          m => m with { JobUnique = "uniq1", PartName = "part1" }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 3,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2102 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) },
              reclamp: new[] { 2 },
              reclampFirst: false,
              progOverride: new[]
              {
                (
                  face: 1,
                  progs: new[]
                  {
                    new ProgramsForProcess()
                    {
                      MachineStopIndex = 0,
                      ProgramName = "prog111",
                      Revision = 10,
                    },
                  }
                ),
              }
            ),
          }
        )
        .MoveToLoad(pal: 3, lul: 4)
        .UpdateExpectedMaterial(queuedMat5.MaterialID, a => a with { ElapsedLoadUnloadTime = TimeSpan.Zero })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 3, lul: 4) })
        .AdvanceMinutes(5)
        .UpdateExpectedMaterial(
          queuedMat5.MaterialID,
          a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(5) }
        )
        // replace workorders again, just updating work1 process 1 program.  Everything else unchanged.
        .ReplaceWorkorders(
          new[]
          {
            new Workorder()
            {
              WorkorderId = "work1",
              Part = "part1",
              Quantity = 0,
              DueDate = DateTime.UtcNow,
              Priority = 0,
              Programs = ImmutableList.Create(
                new ProgramForJobStep()
                {
                  ProcessNumber = 1,
                  StopIndex = 0,
                  ProgramName = "prog111",
                  Revision = 11,
                }, // this is the only one that changes
                new ProgramForJobStep()
                {
                  ProcessNumber = 2,
                  ProgramName = "prog222",
                  Revision = 6,
                }
              ),
            },
            new Workorder()
            {
              WorkorderId = "work2",
              Part = "part1",
              Quantity = 0,
              DueDate = DateTime.UtcNow,
              Priority = 0,
              Programs = ImmutableList.Create(
                new ProgramForJobStep()
                {
                  ProcessNumber = 1,
                  StopIndex = 0,
                  ProgramName = "prog111",
                  Revision = 4,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 2,
                  ProgramName = "prog333",
                  Revision = 7,
                }
              ),
            },
          },
          new[]
          {
            new MachineFramework.NewProgramContent()
            {
              ProgramName = "prog111",
              ProgramContent = "prog111 rev 11 ct",
              Revision = 11,
            },
          }
        )
        .UpdateExpectedMaterial(
          queuedMat5.MaterialID,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { JobUnique = "", PartName = rawMatName ?? "part1" }
        )
        .IncrJobStartedCnt("uniq1", path: 1, cnt: -1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2103, name: "prog111", rev: 11, mcMin: 14),
            FakeIccDsl.ExpectNoWork(pal: 3, noWork: true),
            FakeIccDsl.ExpectPalletStart(pal: 3, mats: []),
          }
        )
        // now try process 1 -> 2
        .MoveToMachineQueue(pal: 5, mach: 1)
        .AddAllocatedMaterial(
          queue: "qqq",
          uniq: "uniq1",
          part: "part1",
          workorder: "work2",
          proc: 1,
          path: 1,
          numProc: 2,
          mat: out var queuedMat3
        )
        .AddAllocatedMaterial(
          queue: "qqq",
          uniq: "uniq1",
          part: "part1",
          workorder: "work1",
          proc: 1,
          path: 1,
          numProc: 2,
          mat: out var queuedMat4
        )
        .UpdateExpectedMaterial(
          queuedMat3.MaterialID,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 4,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 4,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2201 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) },
              reclamp: new[] { 2 },
              reclampFirst: true,
              progOverride: new[]
              {
                (
                  face: 1,
                  progs: new[]
                  {
                    new ProgramsForProcess()
                    {
                      MachineStopIndex = 0,
                      ProgramName = "prog333",
                      Revision = 7,
                    },
                  }
                ),
              }
            ),
          }
        )
        .MoveToLoad(pal: 4, lul: 3)
        .UpdateExpectedMaterial(queuedMat3.MaterialID, a => a with { ElapsedLoadUnloadTime = TimeSpan.Zero })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 4, lul: 3) })
        .AdvanceMinutes(6)
        // reorder queue, should not change anything since programs don't match
        .SetInQueue(queuedMat4, queue: "qqq", pos: 0)
        .UpdateExpectedMaterial(
          queuedMat3.MaterialID,
          a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(6) },
          m => m with { Location = m.Location with { QueuePosition = 1 } }
        )
        .SetAfterLoad(pal: 4)
        .UpdateExpectedMaterial(
          queuedMat3.MaterialID,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m =>
            m with
            {
              Process = 2,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 4,
                Face = 1,
              },
            }
        )
        .IncrJobStartedCnt("uniq1", 1)
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 4,
              lul: 3,
              newPath: 1,
              face: 1,
              unique: "uniq1",
              elapsedMin: 6,
              activeMins: 11,
              totalActiveMins: 11,
              loadingMats: new[] { queuedMat3 },
              loadedMats: out var mat3,
              part: "part1"
            ),
            FakeIccDsl.ExpectPalletStart(pal: 4, mats: mat3),
            FakeIccDsl.RemoveFromQueue(
              "qqq",
              pos: 1,
              elapMin: 6,
              reason: "LoadedToPallet",
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(1, null, mat3))
            ),
          }
        )
        .SetBeforeReclamp(pal: 4)
        .MoveToLoad(pal: 4, lul: 2)
        .UpdateExpectedMaterial(
          mat3,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectReclampBegin(pal: 4, lul: 2, mats: mat3) })
        .AdvanceMinutes(7)
        .SetAfterReclamp(pal: 4)
        .UpdateExpectedMaterial(
          mat3,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[] { FakeIccDsl.ExpectReclampEnd(pal: 4, lul: 2, mats: mat3, elapsedMin: 7, activeMin: 5) }
        )
        .SetBeforeMC(pal: 4)
        .MoveToMachine(pal: 4, mach: 5)
        .StartMachine(mach: 5, program: 2201)
        .UpdateExpectedMaterial(
          mat3,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog333 rev7",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(10),
          }
        )
        .ExpectTransition(
          new[] { FakeIccDsl.ExpectMachineBegin(pal: 4, machine: 5, program: "prog333", rev: 7, mat: mat3) }
        )
        .AdvanceMinutes(4)
        .SetAfterMC(pal: 4)
        .EndMachine(mach: 5)
        .UpdateExpectedMaterial(
          mat3,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 1 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 4,
              mach: 5,
              program: "prog333",
              rev: 7,
              mats: mat3,
              elapsedMin: 4,
              activeMin: 10
            ),
          }
        );
    }

    [Test, Skip("Holding at machine not yet supported by Niigata")]
    public void SizedQueues()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSeparatePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals1: new[] { 1, 2 },
              pals2: new[] { 4 },
              load1: new[] { 3 },
              unload1: new[] { 3 },
              load2: new[] { 3 },
              unload2: new[] { 3 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "654",
              prog2Rev: null,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              machMins2: 10,
              loadMins2: 11,
              unloadMins2: 12,
              fixture: "fix1",
              transQ: "sizedQ"
            ),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .MoveToMachineQueue(pal: 2, mach: 2)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        .SetExpectedLoadCastings(
          new[]
          {
            (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
            (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
          }
        )
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 2,
              pri: 2,
              luls: new[] { 3 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // load pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2)
        .SetAfterLoad(pal: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1) })
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 2,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var AAAProc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: AAAProc1),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: AAAProc1),
          }
        )
        // load pallet 2
        .MoveToLoad(pal: 2, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 2, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3) })
        .AdvanceMinutes(3)
        .SetAfterLoad(pal: 2)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 2,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 3,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var BBBproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 2, mats: BBBproc1),
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: true, mats: BBBproc1),
          }
        )
        // start machining pallet 1
        .MoveToMachineQueue(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 3, waitForMach: true, mats: AAAProc1),
            FakeIccDsl.ExpectRotaryStart(pal: 1, mach: 5, mats: AAAProc1),
          }
        )
        .MoveToMachine(pal: 1, mach: 5)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectRotaryEnd(pal: 1, mach: 5, rotate: true, elapMin: 0, mats: AAAProc1),
          }
        )
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: AAAProc1),
            FakeIccDsl.ExpectPalletHold(pal: 1, hold: true),
          }
        )
        // start machining pallet 2
        .AdvanceMinutes(5)
        .UpdateExpectedMaterial(
          AAAProc1,
          a =>
            a with
            {
              ElapsedMachiningTime = TimeSpan.FromMinutes(5),
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14 - 5),
            }
        )
        .MoveToMachine(pal: 2, mach: 6)
        .SetBeforeMC(pal: 2)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 2, stocker: 2, elapMin: 5, waitForMach: true, mats: BBBproc1),
            FakeIccDsl.ExpectMachineBegin(pal: 2, machine: 6, program: "prog111", rev: 5, mat: BBBproc1),
            FakeIccDsl.ExpectPalletHold(pal: 2, hold: true),
          }
        )
        // end pallet 1 cycle
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 5,
              program: "prog111",
              rev: 5,
              elapsedMin: 5,
              activeMin: 14,
              mats: AAAProc1
            ),
            FakeIccDsl.ExpectPalletHold(
              pal: 1,
              hold: false
            ) // queue and pallet 4 is available
            ,
          }
        )
        // end pallet 2 cycle
        .AdvanceMinutes(3)
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 2)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 2,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 3,
              activeMin: 14,
              mats: BBBproc1
            ),
            // kept on hold!
          }
        )
        // move pallet 1 to buffer
        .AdvanceMinutes(2)
        .SetBeforeUnload(pal: 1)
        .MoveToBuffer(pal: 1, buff: 1)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "sizedQ",
          }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            // no unhold of pallet 2
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: AAAProc1),
          }
        )
        // start unloading pal 1
        .AdvanceMinutes(6)
        .MoveToLoad(pal: 1, lul: 3)
        .IncrJobStartedCnt(unique: "uniq1", path: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(0) },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 6, waitForMach: false, mats: AAAProc1),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
          }
        )
        // move pallet 2 to buffer
        .AdvanceMinutes(3)
        .UpdateExpectedMaterial(AAAProc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(3) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 3)
        .SetBeforeUnload(pal: 2)
        .MoveToBuffer(pal: 2, buff: 2)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "sizedQ",
          }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: false, mats: BBBproc1),
          }
        )
        // finish load of pallet 1
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 4,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          },
          m =>
            m with
            {
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "sizedQ",
                QueuePosition = 0,
              },
            }
        )
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: AAAProc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 24 - 2, mats: AAAProc1),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 3,
              elapsedMin: 3,
              activeMins: 9,
              totalActiveMins: 9 + 8,
              mats: AAAProc1
            ),
            FakeIccDsl.AddToQueue("sizedQ", 0, reason: "Unloaded", AAAProc1),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              elapsedMin: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              activeMins: 8,
              totalActiveMins: 9 + 8,
              mats: out var CCCproc1
            ),
            FakeIccDsl.ExpectNewRoute(
              pal: 4,
              pri: 1,
              luls: new[] { 3 },
              machs: new[] { 5, 6 },
              progs: new[] { 654 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
            ),
            // pal 2 on hold since pallet 4 not available
          }
        )
        // load pallet 4
        .MoveToBuffer(pal: 1, buff: 1)
        .MoveToLoad(pal: 4, lul: 3)
        .UpdateExpectedMaterial(AAAProc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.Zero })
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: CCCproc1),
            FakeIccDsl.ExpectLoadBegin(pal: 4, lul: 3),
          }
        )
        .AdvanceMinutes(5)
        .SetAfterLoad(pal: 4)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im =>
            im with
            {
              Process = 2,
              Path = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 4,
                Face = 1,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 4,
              face: 1,
              newPath: 1,
              unique: "uniq1",
              lul: 3,
              elapsedMin: 5,
              activeMins: 11,
              totalActiveMins: 11,
              loadingMats: AAAProc1,
              loadedMats: out var AAAproc2
            ),
            FakeIccDsl.ExpectPalletStart(pal: 4, mats: AAAproc2),
            FakeIccDsl.RemoveFromQueue(
              "sizedQ",
              pos: 0,
              elapMin: 5,
              reason: "LoadedToPallet",
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(1, 1, AAAproc2))
            ),
          }
        )
        // machine pallet 4
        .SetBeforeMC(pal: 4)
        .MoveToMachine(pal: 4, mach: 6)
        .StartMachine(mach: 6, program: 654)
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "654",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(10),
          }
        )
        .ExpectTransition(
          new[] { FakeIccDsl.ExpectMachineBegin(pal: 4, machine: 6, program: "654", mat: AAAproc2) }
        )
        .AdvanceMinutes(2)
        .SetAfterMC(pal: 4)
        .EndMachine(mach: 6)
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 4,
              mach: 6,
              program: "654",
              elapsedMin: 2,
              activeMin: 10,
              mats: AAAproc2
            ),
          }
        )
        // unload 4
        .MoveToLoad(pal: 4, lul: 3)
        .SetBeforeUnload(pal: 4)
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 4, lul: 3) })
        .AdvanceMinutes(4)
        .SetNoWork(pal: 4)
        .RemoveExpectedMaterial(AAAproc2)
        .IncrJobCompletedCnt("uniq1", proc: 2, path: 1, cnt: AAAproc2.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 4, mins: 6, mats: AAAproc2),
            FakeIccDsl.UnloadFromFace(
              pal: 4,
              lul: 3,
              elapsedMin: 4,
              activeMins: 12,
              totalActiveMins: 12,
              mats: AAAproc2
            ),
            FakeIccDsl.ExpectPalletHold(
              pal: 2,
              hold: false
            ) // finally, unhold pallet 2!
            ,
          }
        );
    }

    [Test]
    public void SizedQueuesWithReclamp()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSeparatePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals1: new[] { 1, 2 },
              pals2: new[] { 4, 5 },
              load1: new[] { 3 },
              unload1: new[] { 3 },
              load2: new[] { 3 },
              unload2: new[] { 3 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "654",
              prog2Rev: null,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              machMins2: 10,
              loadMins2: 11,
              unloadMins2: 12,
              reclamp1: new[] { 1 },
              reclamp1Mins: 15,
              fixture: "fix1",
              transQ: "sizedQ"
            ),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .MoveToMachineQueue(pal: 2, mach: 2)
        .SetManualControl(pal: 5, manual: true)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              reclamp: new[] { 1 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        .SetExpectedLoadCastings(
          new[]
          {
            (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
            (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
          }
        )
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 2,
              pri: 2,
              luls: new[] { 3 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              reclamp: new[] { 1 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // load pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2)
        .SetAfterLoad(pal: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1) })
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 2,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var AAAProc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: AAAProc1),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: AAAProc1),
          }
        )
        // load pallet 2
        .MoveToLoad(pal: 2, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 2, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3) })
        .AdvanceMinutes(3)
        .SetAfterLoad(pal: 2)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 2,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 3,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var BBBproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 2, mats: BBBproc1),
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: true, mats: BBBproc1),
          }
        )
        // machine both pallets
        .MoveToMachine(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 3, waitForMach: true, mats: AAAProc1),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: AAAProc1),
          }
        )
        .AdvanceMinutes(4)
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 5,
              program: "prog111",
              rev: 5,
              elapsedMin: 4,
              activeMin: 14,
              mats: AAAProc1
            ),
          }
        )
        .MoveToMachine(pal: 2, mach: 6)
        .SetBeforeMC(pal: 2)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 2, stocker: 2, elapMin: 4, waitForMach: true, mats: BBBproc1),
            FakeIccDsl.ExpectMachineBegin(pal: 2, machine: 6, program: "prog111", rev: 5, mat: BBBproc1),
          }
        )
        .AdvanceMinutes(2)
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 2)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 2,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 2,
              activeMin: 14,
              mats: BBBproc1
            ),
          }
        )
        // reclamp of pallet 1
        .SetBeforeReclamp(pal: 1)
        .MoveToLoad(pal: 1, lul: 1)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectReclampBegin(pal: 1, lul: 1, mats: AAAProc1),
            FakeIccDsl.ExpectPalletHold(
              pal: 1,
              hold: true
            ) // hold because of sized queue
            ,
          }
        )
        .AdvanceMinutes(1)
        .SetAfterReclamp(pal: 1)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 1 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectReclampEnd(pal: 1, lul: 1, elapsedMin: 1, activeMin: 15, mats: AAAProc1),
            FakeIccDsl.ExpectPalletHold(
              pal: 1,
              hold: false
            ) // unhold because of queue and pallet 4 avail
            ,
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "sizedQ",
          }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: AAAProc1),
          }
        )
        // reclamp of pallet 2
        .SetBeforeReclamp(pal: 2)
        .MoveToLoad(pal: 2, lul: 1)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectReclampBegin(pal: 2, lul: 1, mats: BBBproc1),
            FakeIccDsl.ExpectPalletHold(
              pal: 2,
              hold: true
            ) // hold because of sized queue
            ,
          }
        )
        .AdvanceMinutes(1)
        .SetAfterReclamp(pal: 2)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im => im with { LastCompletedMachiningRouteStopIndex = 1 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectReclampEnd(pal: 2, lul: 1, elapsedMin: 1, activeMin: 15, mats: BBBproc1),
            // no unhold because material on pallet 1 is about to use queue
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        .SetBeforeUnload(pal: 2)
        .UpdateExpectedMaterial(
          BBBproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "sizedQ",
          }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: false, mats: BBBproc1),
          }
        )
        // start unloading pal 1
        .AdvanceMinutes(6)
        .MoveToLoad(pal: 1, lul: 3)
        .IncrJobStartedCnt(unique: "uniq1", path: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(0) },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 7, waitForMach: false, mats: AAAProc1),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
          }
        )
        .AdvanceMinutes(3)
        // unload pallet 1
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 4,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          },
          m =>
            m with
            {
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "sizedQ",
                QueuePosition = 0,
              },
            }
        )
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: AAAProc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 20, mats: AAAProc1),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 3,
              elapsedMin: 3,
              activeMins: 9,
              totalActiveMins: 9 + 8,
              mats: AAAProc1
            ),
            FakeIccDsl.AddToQueue("sizedQ", 0, reason: "Unloaded", AAAProc1),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              elapsedMin: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              activeMins: 8,
              totalActiveMins: 9 + 8,
              mats: out var CCCproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: CCCproc1),
            FakeIccDsl.ExpectNewRoute(
              pal: 4,
              pri: 1,
              luls: new[] { 3 },
              machs: new[] { 5, 6 },
              progs: new[] { 654 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
            ),
            // pal 2 on hold since pallet 4 not available and 5 is in manual control
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: CCCproc1),
          }
        )
        // pallet 4 is in the buffer and waiting to move to the load station, so pallet 2 should not be unheld yet even if pallet 5 becomes available
        .SetManualControl(pal: 5, manual: false)
        .ExpectNoChanges()
        // start load pallet 4
        .MoveToLoad(pal: 4, lul: 3)
        .UpdateExpectedMaterial(AAAProc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(0) })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 4, lul: 3) })
        .AdvanceMinutes(3)
        // make pallet 5 no longer available (it would trigger pallet 2 unhold as soon as pallet 4 finishes)
        .UpdateExpectedMaterial(AAAProc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(3) })
        .SetManualControl(pal: 5, manual: true)
        .ExpectNoChanges()
        // finish load of pallet 4
        .AdvanceMinutes(2)
        .SetAfterLoad(pal: 4)
        .UpdateExpectedMaterial(
          AAAProc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im =>
            im with
            {
              Process = 2,
              Path = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 4,
                Face = 1,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 4,
              face: 1,
              unique: "uniq1",
              newPath: 1,
              lul: 3,
              elapsedMin: 5,
              activeMins: 11,
              totalActiveMins: 11,
              loadingMats: AAAProc1,
              loadedMats: out var AAAproc2
            ),
            FakeIccDsl.ExpectPalletStart(pal: 4, mats: AAAproc2),
            FakeIccDsl.RemoveFromQueue(
              "sizedQ",
              pos: 0,
              elapMin: 5,
              reason: "LoadedToPallet",
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(1, 1, AAAproc2))
            ),
          }
        )
        // machine pallet 4
        .SetBeforeMC(pal: 4)
        .MoveToMachine(pal: 4, mach: 6)
        .StartMachine(mach: 6, program: 654)
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "654",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(10),
          }
        )
        .ExpectTransition(
          new[] { FakeIccDsl.ExpectMachineBegin(pal: 4, machine: 6, program: "654", mat: AAAproc2) }
        )
        .AdvanceMinutes(2)
        .SetAfterMC(pal: 4)
        .EndMachine(mach: 6)
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 4,
              mach: 6,
              program: "654",
              elapsedMin: 2,
              activeMin: 10,
              mats: AAAproc2
            ),
          }
        )
        // unload 4
        .MoveToLoad(pal: 4, lul: 3)
        .SetBeforeUnload(pal: 4)
        .UpdateExpectedMaterial(
          AAAproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 4, lul: 3) })
        .AdvanceMinutes(4)
        .SetNoWork(pal: 4)
        .RemoveExpectedMaterial(AAAproc2)
        .IncrJobCompletedCnt("uniq1", proc: 2, path: 1, cnt: AAAproc2.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 4, mins: 6, mats: AAAproc2),
            FakeIccDsl.UnloadFromFace(
              pal: 4,
              lul: 3,
              elapsedMin: 4,
              activeMins: 12,
              totalActiveMins: 12,
              mats: AAAproc2
            ),
            FakeIccDsl.ExpectPalletHold(
              pal: 2,
              hold: false
            ) // finally, unhold pallet 2!
            ,
          }
        );
    }

    [Test]
    public void DeletePrograms()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSamePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "prog222",
              prog2Rev: 6L,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              loadMins2: 10,
              unloadMins2: 11,
              machMins2: 15,
              fixture: "fix1",
              face1: 1,
              face2: 2
            ),
          },
          new[]
          {
            (prog: "prog111", rev: 4L),
            (prog: "prog111", rev: 5L),
            (prog: "prog222", rev: 6L),
            (prog: "prog222", rev: 7L),
          }
        )
        // process 1 only cycle
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2200, name: "prog222", rev: 6, mcMin: 15),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .OverrideRoute(
          pal: 5,
          comment: "manual control",
          noWork: false,
          luls: new[] { 1 },
          machs: new[] { 2, 3 },
          progs: new[] { 4001 }
        )
        .SetIccProgram(4000, "non-insight")
        .SetIccProgram(4001, "Insight:3:prog111") // has newer revision, but a custom manual pallet uses it so don't delete
        .SetIccProgram(4002, "Insight:4:prog111") // has newer revision 5, should be deleted
        .SetIccProgram(4003, "Insight:7:prog222") // shouldn't be deleted since it is the latest revision, even though not used
        .ExpectOldProgram(name: "prog111", rev: 4, num: 4002)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[] { FakeIccDsl.ExpectDeleteProgram(4002, "prog111", 4) }
        )
        // add a second job with higher revisions
        .AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSamePalletJob(
              unique: "uniq2",
              part: "part1",
              qty: 3,
              priority: 10,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: 100L,
              prog2: "prog222",
              prog2Rev: 200L,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              loadMins2: 10,
              unloadMins2: 11,
              machMins2: 15,
              fixture: "fix1",
              face1: 1,
              face2: 2,
              prec1: 0,
              prec2: 1
            ),
          },
          new[] { (prog: "prog111", rev: 100L), (prog: "prog222", rev: 200L) }
        )
        .SetJobPrecedence("uniq1", new[] { new[] { 2 }, new[] { 3 } })
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2101, name: "prog111", rev: 100, mcMin: 14),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2201, name: "prog222", rev: 200, mcMin: 15),
            FakeIccDsl.ExpectDeleteProgram(
              4003,
              "prog222",
              7
            ) // 4003 now outdated
            ,
          }
        )
        // archive the first job, so old programs
        .OverrideRoute(
          pal: 1,
          comment: "aaa",
          noWork: false,
          luls: new int[] { },
          machs: new int[] { },
          progs: new int[] { }
        )
        .ClearExpectedLoadCastings()
        .RemoveJobStartedCnt("uniq1")
        .ArchiveJob("uniq1")
        .ExpectOldProgram("prog111", rev: 5, num: 2100)
        .ExpectOldProgram("prog222", rev: 6, num: 2200, comment: "Comment prog222 rev6")
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectDeleteProgram(2100, "prog111", rev: 5),
            FakeIccDsl.ExpectDeleteProgram(2200, "prog222", rev: 6, fail: true),
            FakeIccDsl.ExpectDeleteProgram(2200, "prog222", rev: 6),
          }
        );
    }

    [Test]
    public void UsesNextLargestProgram()
    {
      _dsl.SetIccProgram(2133, "non-insight")
        .AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSamePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "prog222",
              prog2Rev: 6L,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              loadMins2: 10,
              unloadMins2: 11,
              machMins2: 15,
              fixture: "fix1",
              face1: 1,
              face2: 2
            ),
          },
          new[]
          {
            (prog: "prog111", rev: 4L),
            (prog: "prog111", rev: 5L),
            (prog: "prog222", rev: 6L),
            (prog: "prog222", rev: 7L),
          }
        )
        // process 1 only cycle
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2134, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2200, name: "prog222", rev: 6, mcMin: 15),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2134 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        );
    }

    [Test]
    public void OperatorDeletesPrograms()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSamePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "prog222",
              prog2Rev: 6L,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              loadMins2: 10,
              unloadMins2: 11,
              machMins2: 15,
              fixture: "fix1",
              face1: 1,
              face2: 2
            ),
          },
          new[]
          {
            (prog: "prog111", rev: 4L),
            (prog: "prog111", rev: 5L),
            (prog: "prog222", rev: 6L),
            (prog: "prog222", rev: 7L),
          }
        )
        // process 1 only cycle
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectAddNewProgram(progNum: 2200, name: "prog222", rev: 6, mcMin: 15),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // operator deletes, Insight puts it back
        .SetNoWork(pal: 1)
        .RemoveIccProgram(iccProg: 2100)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 1),
          }
        )
        // operator changes it to a non-insight program, Insight creates a new program 2101
        .SetNoWork(pal: 1)
        .SetIccProgram(iccProg: 2100, comment: "thecustom program")
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2101, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2101 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        );
    }

    [Test]
    public void RemoveFromQueueDuringLoad()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSeparatePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals1: new[] { 1 },
              pals2: new[] { 2 },
              load1: new[] { 3, 4 },
              unload1: new[] { 3, 4 },
              load2: new[] { 3, 4 },
              unload2: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "654",
              prog2Rev: null,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              machMins2: 10,
              loadMins2: 11,
              unloadMins2: 12,
              fixture: "fix1",
              transQ: "qqq"
            ),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // first process on pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2) // = 2min
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 2)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 2,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var AAAproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: AAAproc1),
          }
        )
        .MoveToMachineQueue(pal: 1, mach: 6)
        .ExpectNoChanges()
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          AAAproc1,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1),
          }
        )
        .AdvanceMinutes(10) // = 12min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 10,
              activeMin: 14,
              mats: AAAproc1
            ),
          }
        )
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
          }
        )
        .AdvanceMinutes(15) // 27min
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 2,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          },
          m =>
            m with
            {
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "qqq",
                QueuePosition = 0,
              },
            }
        )
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: AAAproc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 27 - 2, mats: AAAproc1),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 15,
              activeMins: 9,
              totalActiveMins: 9 + 8,
              mats: AAAproc1
            ),
            FakeIccDsl.AddToQueue("qqq", 0, reason: "Unloaded", AAAproc1),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              elapsedMin: 15,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              activeMins: 8,
              totalActiveMins: 9 + 8,
              mats: out var BBBproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: BBBproc1),
            FakeIccDsl.ExpectNewRoute(
              pal: 2,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 654 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
            ),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: BBBproc1),
          }
        )
        // move pallet 2 to the load station
        .MoveToLoad(pal: 2, lul: 3)
        .UpdateExpectedMaterial(AAAproc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(0) })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3) })
        // now pretend the part was scrapped or quarantined
        .RemoveFromQueue(AAAproc1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNoWork(pal: 2, noWork: true),
            FakeIccDsl.ExpectPalletStart(pal: 2, mats: []),
          }
        )
        .MoveToBuffer(pal: 2, buff: 2)
        .ExpectNoChanges();
    }

    [Test]
    public void OperatorPressesUnloadToSetNoWork()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSeparatePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals1: new[] { 1 },
              pals2: new[] { 2 },
              load1: new[] { 3, 4 },
              unload1: new[] { 3, 4 },
              load2: new[] { 3, 4 },
              unload2: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "654",
              prog2Rev: null,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              machMins2: 10,
              loadMins2: 11,
              unloadMins2: 12,
              fixture: "fix1",
              transQ: "qqq"
            ),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // first process on pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2) // = 2min
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 2)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 2,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var AAAproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: AAAproc1),
          }
        )
        .MoveToMachineQueue(pal: 1, mach: 6)
        .ExpectNoChanges()
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          AAAproc1,
          im =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1),
          }
        )
        .AdvanceMinutes(10) // = 12min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 10,
              activeMin: 14,
              mats: AAAproc1
            ),
          }
        )
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
          }
        )
        .AdvanceMinutes(15) // 27min
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 2,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          },
          m =>
            m with
            {
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "qqq",
                QueuePosition = 0,
              },
            }
        )
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: AAAproc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 27 - 2, mats: AAAproc1),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 15,
              activeMins: 9,
              totalActiveMins: 9 + 8,
              mats: AAAproc1
            ),
            FakeIccDsl.AddToQueue("qqq", 0, reason: "Unloaded", AAAproc1),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              elapsedMin: 15,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              totalActiveMins: 9 + 8,
              activeMins: 8,
              mats: out var BBBproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: BBBproc1),
            FakeIccDsl.ExpectNewRoute(
              pal: 2,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 654 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
            ),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: BBBproc1),
          }
        )
        // move pallet 2 to the load station
        .MoveToLoad(pal: 2, lul: 3)
        .UpdateExpectedMaterial(AAAproc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(0) })
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3) })
        // now assume the operator presses unload button to set nowork
        .SetNoWork(pal: 2)
        .UpdateExpectedMaterial(AAAproc1, a => new() { Type = InProcessMaterialAction.ActionType.Waiting })
        .ExpectTransition(new[] { FakeIccDsl.ExpectPalletStart(pal: 2, mats: []) });
    }

    [Test]
    public void OperatorManuallyOverridesWithNoWork()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl
              .CreateOneProcOnePathJob(
                unique: "uniq1",
                part: "part1",
                qty: 3,
                priority: 5,
                partsPerPal: 1,
                pals: new[] { 1 },
                luls: new[] { 3, 4 },
                machs: new[] { 3, 5, 6 },
                prog: "prog111",
                progRev: null,
                loadMins: 8,
                unloadMins: 9,
                machMins: 14,
                fixture: "fix1",
                face: 1
              )
              .AddInsp(proc: 1, path: 1, inspTy: "InspTy", cntr: "Thecounter", max: 2),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .ExpectNoChanges()
        .MoveToLoad(pal: 1, lul: 1)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 1) })
        .AdvanceMinutes(4) // =4
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 4)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 1,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 4,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var fstMats
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: fstMats),
          }
        )
        .MoveToMachineQueue(pal: 1, mach: 3)
        .AdvanceMinutes(2)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[] { FakeIccDsl.ExpectRotaryStart(pal: 1, mach: 3, mats: fstMats) }
        )
        .AdvanceMinutes(3)
        // operator aborts
        .SetNoWork(pal: 1)
        .SetBeforeLoad(pal: 1)
        .MoveToCart(pal: 1)
        .UpdateExpectedMaterial(
          fstMats,
          a => a,
          im =>
            im with
            {
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "Quarantine",
                QueuePosition = 0,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectRotaryEnd(pal: 1, mach: 3, rotate: false, elapMin: 3, mats: fstMats),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: []),
            FakeIccDsl.AddToQueue(
              "Quarantine",
              0,
              reason: "MaterialMissingOnPallet",
              FakeIccDsl.ClearFaces(fstMats)
            ),
          }
        )
        // when get to buffer, sets a new route
        .MoveToBuffer(pal: 1, buff: 6)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectRouteIncrement(
              pal: 1,
              newCycleCnt: 1,
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .AdvanceMinutes(2)
        .ExpectNoChanges()
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4) })
        .AdvanceMinutes(2)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 2)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 2,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var sndMats
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: sndMats),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: sndMats),
          }
        );
    }

    [Test]
    public void UnarchivesJobWithMaterial()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl
              .CreateOneProcOnePathJob(
                unique: "uniq1",
                part: "part1",
                qty: 1,
                priority: 5,
                partsPerPal: 1,
                pals: new[] { 1 },
                luls: new[] { 3, 4 },
                machs: new[] { 3, 5, 6 },
                prog: "prog111",
                progRev: null,
                loadMins: 8,
                unloadMins: 9,
                machMins: 14,
                fixture: "fix1",
                face: 1,
                queue: "rawmat"
              )
              .AddInsp(proc: 1, path: 1, inspTy: "InspTy", cntr: "Thecounter", max: 2),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          }
        )
        .ArchiveJob("uniq1")
        .RemoveJobStartedCnt(unique: "uniq1")
        .ExpectNoChanges()
        .AddAllocatedMaterial(
          queue: "rawmat",
          uniq: "uniq1",
          part: "part1",
          proc: 0,
          path: 1,
          numProc: 1,
          out var mat1
        )
        .UpdateExpectedMaterial(
          mat1.MaterialID,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 1,
            LoadOntoFace = 1,
            ProcessAfterLoad = 1,
            PathAfterLoad = 1,
          }
        )
        .SetJobStartedCnt(unique: "uniq1", cnt: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // first process on pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .UpdateExpectedMaterial(
          mat1.MaterialID,
          a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(0) }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2) // = 2min
        .SetAfterLoad(pal: 1)
        .UpdateExpectedMaterial(
          mat1.MaterialID,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m =>
            m with
            {
              Process = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 1,
                Face = 1,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 1,
              lul: 3,
              newPath: 1,
              face: 1,
              unique: "uniq1",
              elapsedMin: 2,
              activeMins: 8,
              totalActiveMins: 8,
              loadingMats: new[] { mat1 },
              loadedMats: out var loadedMat1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: loadedMat1),
            FakeIccDsl.RemoveFromQueue(
              queue: "rawmat",
              pos: 0,
              elapMin: 2,
              reason: "LoadedToPallet",
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(0, null, loadedMat1))
            ),
          }
        )
        .MoveToMachineQueue(pal: 1, mach: 6)
        .ExpectNoChanges();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void SignalForQuarantine(bool signalDuringUnload)
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSeparatePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals1: new[] { 1 },
              pals2: new[] { 2 },
              load1: new[] { 3, 4 },
              unload1: new[] { 3, 4 },
              load2: new[] { 3, 4 },
              unload2: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "654",
              prog2Rev: null,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              machMins2: 10,
              loadMins2: 11,
              unloadMins2: 12,
              fixture: "fix1",
              transQ: "qqq"
            ),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // first process on pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2) // = 2min
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 2)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 3,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 2,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var AAAproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: AAAproc1),
          }
        )
        .MoveToMachineQueue(pal: 1, mach: 6)
        .ExpectNoChanges()
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          AAAproc1,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1),
          }
        )
        .AdvanceMinutes(10); // = 12min

      if (!signalDuringUnload)
      {
        _dsl.SignalForQuarantine(AAAproc1, pal: 1, q: "Quarantine");
        _dsl.UpdateExpectedMaterial(AAAproc1, a => a, im => im with { QuarantineAfterUnload = true });
      }

      _dsl.EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 10,
              activeMin: 14,
              mats: AAAproc1
            ),
          }
        )
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = signalDuringUnload ? "qqq" : "Quarantine",
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          },
          m => m with { LastCompletedMachiningRouteStopIndex = null }
        )
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
            FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
          }
        )
        .AdvanceMinutes(5)
        .UpdateExpectedMaterial(AAAproc1, a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(5) })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 5)
        .ExpectNoChanges();

      if (signalDuringUnload)
      {
        // signal for quarantine
        _dsl.SignalForQuarantine(AAAproc1, pal: 1, q: "Quarantine")
          .UpdateExpectedMaterial(
            AAAproc1,
            a => a with { UnloadIntoQueue = "Quarantine" },
            im => im with { QuarantineAfterUnload = true }
          );
      }

      _dsl.ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im =>
            im with
            {
              QuarantineAfterUnload = null,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "Quarantine",
                QueuePosition = 0,
              },
            }
        )
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: AAAproc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 17 - 2, mats: AAAproc1),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 4,
              elapsedMin: 5,
              activeMins: 9,
              totalActiveMins: 9 + 8,
              mats: AAAproc1
            ),
            FakeIccDsl.AddToQueue("Quarantine", 0, reason: "Unloaded", AAAproc1),
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              elapsedMin: 5,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              activeMins: 8,
              totalActiveMins: 9 + 8,
              mats: out var BBBproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: BBBproc1),
          }
        );
    }

    [Test]
    public void SwapRawMaterialOnPal()
    {
      // TODO: raw material name
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl
              .CreateOneProcOnePathJob(
                unique: "uniq1",
                part: "part1",
                qty: 3,
                priority: 5,
                partsPerPal: 1,
                pals: new[] { 1 },
                luls: new[] { 3, 4 },
                machs: new[] { 3, 5, 6 },
                prog: "prog111",
                progRev: null,
                loadMins: 8,
                unloadMins: 9,
                machMins: 14,
                fixture: "fix1",
                face: 1,
                queue: "rawmat"
              )
              .AddInsp(proc: 1, path: 1, inspTy: "InspTy", cntr: "Thecounter", max: 2),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          }
        )
        .AddUnallocatedCasting(queue: "rawmat", rawMatName: "part1", mat: out var qmat1)
        .AddUnallocatedCasting(queue: "rawmat", rawMatName: "part1", mat: out var qmat2)
        .UpdateExpectedMaterial(
          qmat1.MaterialID,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 1,
            LoadOntoFace = 1,
            ProcessAfterLoad = 1,
            PathAfterLoad = 1,
          },
          m => m with { JobUnique = "uniq1", PartName = "part1" }
        )
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 1)
        .UpdateExpectedMaterial(
          qmat1.MaterialID,
          a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(0) }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 1) })
        .AdvanceMinutes(4) // =4
        .SetAfterLoad(pal: 1)
        .UpdateExpectedMaterial(
          qmat1.MaterialID,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m =>
            m with
            {
              Process = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 1,
                Face = 1,
              },
            }
        )
        .UpdateExpectedMaterial(
          qmat2.MaterialID,
          a => a,
          m => m with { Location = m.Location with { QueuePosition = 0 } }
        )
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 1,
              lul: 1,
              newPath: 1,
              face: 1,
              unique: "uniq1",
              elapsedMin: 4,
              activeMins: 8,
              totalActiveMins: 8,
              loadingMats: new[] { qmat1 },
              loadedMats: out var lmat1,
              part: "part1"
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: lmat1),
            FakeIccDsl.RemoveFromQueue(
              "rawmat",
              pos: 0,
              elapMin: 4,
              reason: "LoadedToPallet",
              mat: FakeIccDsl.ClearFaces(FakeIccDsl.SetProc(0, null, lmat1))
            ),
          }
        )
        .SetBeforeMC(pal: 1)
        .MoveToBuffer(pal: 1, buff: 3)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 3, waitForMach: true, mats: lmat1),
          }
        )
        .SwapMaterial(pal: 1, matOnPalId: qmat1.MaterialID, matToAddId: qmat2.MaterialID, out var lmat2)
        .ExpectNoChanges()
        .AdvanceMinutes(4)
        .MoveToMachine(pal: 1, mach: 3)
        .StartMachine(mach: 3, program: 2100)
        .UpdateExpectedMaterial(
          lmat2,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 3, elapMin: 4, waitForMach: true, mats: lmat2),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 3, program: "prog111", rev: 5, mat: lmat2),
          }
        );
    }

    [Test]
    public void SwapInProcessMat()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSeparatePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 5,
              priority: 5,
              partsPerPal: 1,
              pals1: new[] { 1 },
              pals2: new[] { 2 },
              load1: new[] { 3, 4 },
              unload1: new[] { 3, 4 },
              load2: new[] { 3, 4 },
              unload2: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "654",
              prog2Rev: null,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              machMins2: 10,
              loadMins2: 11,
              unloadMins2: 12,
              fixture: "fix1",
              transQ: "qqq"
            ),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // directly add some material to the queue
        .AddAllocatedMaterial(
          queue: "qqq",
          uniq: "uniq1",
          part: "part1",
          proc: 1,
          path: 1,
          numProc: 2,
          out var AAAproc1
        )
        .AddAllocatedMaterial(
          queue: "qqq",
          uniq: "uniq1",
          part: "part1",
          proc: 1,
          path: 1,
          numProc: 2,
          out var Bproc1
        )
        .UpdateExpectedMaterial(
          AAAproc1.MaterialID,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 2,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 2,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 654 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
            ),
          }
        )
        .MoveToLoad(pal: 2, lul: 3)
        .SetBeforeLoad(pal: 2)
        .UpdateExpectedMaterial(
          AAAproc1.MaterialID,
          a => a with { ElapsedLoadUnloadTime = TimeSpan.FromMinutes(0) }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3) })
        .AdvanceMinutes(7) // =34 min
        .SetAfterLoad(pal: 2)
        .UpdateExpectedMaterial(
          AAAproc1.MaterialID,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          im =>
            im with
            {
              Process = 2,
              Path = 1,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = 2,
                Face = 1,
              },
            }
        )
        .UpdateExpectedMaterial(
          Bproc1.MaterialID,
          a => a,
          im => im with { Location = im.Location with { QueuePosition = im.Location.QueuePosition - 1 } }
        )
        .IncrJobStartedCnt("uniq1", path: 1, cnt: 1)
        .ExpectTransition(
          new[]
          {
            _dsl.LoadToFace(
              pal: 2,
              face: 1,
              newPath: 1,
              unique: "uniq1",
              lul: 3,
              elapsedMin: 7,
              activeMins: 11,
              totalActiveMins: 11,
              loadingMats: new[] { AAAproc1 },
              loadedMats: out var AAAproc2
            ),
            FakeIccDsl.ExpectPalletStart(pal: 2, mats: AAAproc2),
            FakeIccDsl.RemoveFromQueue(
              "qqq",
              pos: 0,
              elapMin: 7,
              reason: "LoadedToPallet",
              mat: FakeIccDsl.ClearFaces([AAAproc1 with { Path = null }])
            ),
          }
        )
        .SetBeforeMC(pal: 2)
        .MoveToBuffer(pal: 2, buff: 2)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: true, mats: AAAproc2),
          }
        )
        .SwapMaterial(
          pal: 2,
          matOnPalId: AAAproc2.First().MaterialID,
          matToAddId: Bproc1.MaterialID,
          out var BBBproc2
        )
        .ExpectNoChanges()
        .AdvanceMinutes(4)
        .MoveToMachine(pal: 2, mach: 5)
        .StartMachine(mach: 5, program: 654)
        .UpdateExpectedMaterial(
          BBBproc2,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "654",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(10),
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 2, stocker: 2, elapMin: 4, waitForMach: true, mats: BBBproc2),
            FakeIccDsl.ExpectMachineBegin(pal: 2, machine: 5, program: "654", rev: null, mat: BBBproc2),
          }
        );
    }

    [Test]
    public void PalletMarkedAsManualControl()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl
              .CreateOneProcOnePathJob(
                unique: "uniq1",
                part: "part1",
                qty: 3,
                priority: 5,
                partsPerPal: 1,
                pals: new[] { 1 },
                luls: new[] { 3, 4 },
                machs: new[] { 3, 5, 6 },
                prog: "prog111",
                progRev: null,
                loadMins: 8,
                unloadMins: 9,
                machMins: 14,
                fixture: "fix1",
                face: 1
              )
              .AddInsp(proc: 1, path: 1, inspTy: "InspTy", cntr: "Thecounter", max: 2),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 1)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 1) })
        .AdvanceMinutes(4) // =4
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 1,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 4,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var fstMats
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: fstMats),
          }
        )
        .MoveToMachine(pal: 1, mach: 3)
        .SetBeforeMC(pal: 1)
        .AdvanceMinutes(1)
        .StartMachine(mach: 3, program: 2100)
        .UpdateExpectedMaterial(
          fstMats,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 3, program: "prog111", rev: 5, mat: fstMats),
          }
        )
        .AdvanceMinutes(10)
        .UpdateExpectedMaterial(
          fstMats,
          a =>
            a with
            {
              ElapsedMachiningTime = TimeSpan.FromMinutes(10),
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(4),
            }
        )
        .ExpectNoChanges()
        // pallet goes to manual
        .SetManualControl(pal: 1, manual: true)
        .UpdateExpectedMaterial(
          fstMats,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m =>
            m with
            {
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "Quarantine",
                QueuePosition = 0,
              },
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.AddToQueue(
              queue: "Quarantine",
              pos: 0,
              reason: "PalletToManualControl",
              mat: FakeIccDsl.ClearFaces(fstMats)
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: []),
          }
        )
        // no longer logs anything
        .EndMachine(mach: 3)
        .SetAfterMC(pal: 1)
        .ExpectNoChanges()
        .MoveToLoad(pal: 1, lul: 3)
        .SetBeforeUnload(pal: 1)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ExpectNoChanges()
        // doesnt set route
        .SetNoWork(pal: 1)
        .SetBeforeLoad(pal: 1)
        .MoveToBuffer(pal: 1, buff: 1)
        // returns pallet to normal
        .SetManualControl(pal: 1, manual: false)
        .IncrJobStartedCnt("uniq1", path: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4) });
    }

    [Test]
    public void DeletesPalletRoute()
    {
      _dsl.OverrideRoute(
          pal: 1,
          comment: "abcdef",
          noWork: true,
          luls: new[] { 3 },
          machs: new[] { 1, 2, 3, 4 },
          progs: new[] { 2222 },
          machs2: new[] { 5, 6, 7, 8 },
          progs2: new[] { 3333 }
        )
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 3)
        .AddJobs(
          new[]
          {
            FakeIccDsl.CreateOneProcOnePathJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              prog: "1111",
              progRev: null,
              loadMins: 8,
              unloadMins: 9,
              machMins: 14,
              fixture: "fix1",
              face: 1
            ),
          }
        )
        // no delete when pallet at load station
        .ExpectNoChanges()
        .MoveToBuffer(pal: 1, buff: 1)
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectRouteDelete(pal: 1),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              progs: new[] { 1111 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        );
    }

    [Test]
    public void AllowsSubsetsOfMachines()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateOneProcOnePathJob(
              unique: "uniq1",
              part: "part1",
              qty: 3,
              priority: 5,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              prog: "1111",
              progRev: null,
              loadMins: 8,
              unloadMins: 9,
              machMins: 14,
              fixture: "fix1",
              face: 1
            ),
          }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 3, 5, 6 },
              progs: new[] { 1111 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4) })
        .AdvanceMinutes(4) // =4
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 4,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 4,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var fstMats
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: fstMats),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: fstMats),
          }
        )
        // now override route to only have mach 3, not 3, 5, and 6
        .AdvanceMinutes(5)
        .OverrideRouteMachines(pal: 1, stepIdx: 1, machs: new[] { 3 })
        .ExpectNoChanges()
        .MoveToMachine(pal: 1, mach: 3)
        .StartMachine(mach: 3, program: 1111)
        .UpdateExpectedMaterial(
          fstMats,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "1111",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
          }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 5, waitForMach: true, mats: fstMats),
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 3, program: "1111", mat: fstMats),
          }
        );
    }

    [Test]
    public void ManualJobPriority()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateOneProcOnePathJob(
              unique: "uniq1",
              part: "part1",
              // higher priority but non-manual so will run second.
              manual: false,
              priority: 100,
              qty: 3,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              prog: "123",
              progRev: null,
              loadMins: 8,
              unloadMins: 9,
              machMins: 14,
              fixture: "fix1",
              face: 1,
              precedence: 1
            ),
            FakeIccDsl.CreateOneProcOnePathJob(
              unique: "uniq2",
              part: "part1",
              // has lower priority but manual is true so should run first
              manual: true,
              priority: 50,
              qty: 3,
              partsPerPal: 1,
              pals: new[] { 1 },
              luls: new[] { 3, 4 },
              machs: new[] { 2, 3 },
              prog: "345",
              progRev: null,
              loadMins: 8,
              unloadMins: 9,
              machMins: 14,
              fixture: "fix1",
              face: 1,
              precedence: 0
            ),
          }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq2", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq2", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 2, 3 },
              progs: new[] { 345 },
              faces: new[] { (face: 1, unique: "uniq2", proc: 1, path: 1) }
            ),
          }
        );
    }

    [Test]
    public void UnarchivesJobs()
    {
      _dsl.AddJobs(
          new[]
          {
            FakeIccDsl.CreateMultiProcSeparatePalletJob(
              unique: "uniq1",
              part: "part1",
              qty: 1,
              priority: 5,
              partsPerPal: 1,
              pals1: new[] { 1 },
              pals2: new[] { 2 },
              load1: new[] { 1 },
              unload1: new[] { 2 },
              load2: new[] { 3 },
              unload2: new[] { 4 },
              machs: new[] { 5, 6 },
              prog1: "prog111",
              prog1Rev: null,
              prog2: "654",
              prog2Rev: null,
              loadMins1: 8,
              unloadMins1: 9,
              machMins1: 14,
              machMins2: 10,
              loadMins2: 11,
              unloadMins2: 12,
              fixture: "fix1",
              transQ: "qqq"
            ),
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .SetExpectedLoadCastings(new[] { (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1) })
        .IncrJobStartedCnt("uniq1", path: 1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              pri: 2,
              luls: new[] { 1 },
              unloads: new[] { 2 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
            ),
          }
        )
        // first process on pallet 1
        .MoveToLoad(pal: 1, lul: 1)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 1) })
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.LoadCastingToFace(
              pal: 1,
              lul: 1,
              face: 1,
              unique: "uniq1",
              path: 1,
              cnt: 1,
              elapsedMin: 0,
              activeMins: 8,
              totalActiveMins: 8,
              mats: out var AAAproc1
            ),
            FakeIccDsl.ExpectPalletStart(pal: 1, mats: AAAproc1),
          }
        )
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(
          AAAproc1,
          a =>
            new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = "prog111 rev5",
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14),
            }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1),
          }
        )
        .AdvanceMinutes(10) // = 10min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new() { Type = InProcessMaterialAction.ActionType.Waiting },
          m => m with { LastCompletedMachiningRouteStopIndex = 0 }
        )
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectMachineEnd(
              pal: 1,
              mach: 6,
              program: "prog111",
              rev: 5,
              elapsedMin: 10,
              activeMin: 14,
              mats: AAAproc1
            ),
          }
        )
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 2)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero,
          }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 2) })
        .AdvanceMinutes(4) // 9min
        .SetAfterUnload(pal: 1)
        .SetNoWork(pal: 1)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 2,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          },
          m =>
            m with
            {
              LastCompletedMachiningRouteStopIndex = null,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = "qqq",
                QueuePosition = 0,
              },
            }
        )
        .IncrJobCompletedCnt("uniq1", proc: 1, path: 1, cnt: AAAproc1.Count())
        .ExpectTransition(
          new[]
          {
            FakeIccDsl.ExpectPalletEnd(pal: 1, mins: 14, mats: AAAproc1),
            FakeIccDsl.UnloadFromFace(
              pal: 1,
              lul: 2,
              elapsedMin: 4,
              activeMins: 9,
              totalActiveMins: 9,
              mats: AAAproc1
            ),
            FakeIccDsl.AddToQueue("qqq", 0, reason: "Unloaded", AAAproc1),
            FakeIccDsl.ExpectNewRoute(
              pal: 2,
              pri: 1,
              luls: new[] { 3 },
              unloads: new[] { 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 654 },
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
            ),
          }
        )
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectNoChanges();

      // Material qqq is in the queue and pallet 2 is set for loading but still in the stocker.
      // Instead, remove it from the queue
      _dsl.RemoveFromQueue(AAAproc1)
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[] { FakeIccDsl.ExpectNoWork(pal: 2, noWork: true, expectedUpdated: false) }
        );

      // After 12 hours it should be archived, so first advance 10
      _dsl.AdvanceMinutes(10 * 60)
        .ExpectNoChanges()
        .AdvanceMinutes(3 * 60) // and then 3 more
        .ExpectNoChanges() // ExpectNoChanges loads unarchived jobs to compare
        .ExpectJobArchived("uniq1", isArchived: true);

      // Adding AAAproc1 back to the queue should unarchive the job
      _dsl.AddToQueue(AAAproc1, "qqq", 0)
        .UpdateExpectedMaterial(
          AAAproc1,
          a => new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPalletNum = 2,
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
          }
        )
        .ExpectTransition(
          expectedUpdates: false,
          expectedChanges: new[]
          {
            FakeIccDsl.ExpectRouteIncrement(
              pal: 2,
              newCycleCnt: 1,
              faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
            ),
          }
        )
        .ExpectJobArchived("uniq1", isArchived: false);
    }
  }
}
