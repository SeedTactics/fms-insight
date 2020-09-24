/* Copyright (c) 2020, John Lenz

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
using Xunit;
using BlackMaple.MachineWatchInterface;
using System.Collections.Generic;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class IccControlSpec : IDisposable
  {
    private FakeIccDsl _dsl;
    public IccControlSpec()
    {
      _dsl = new FakeIccDsl(numPals: 5, numMachines: 6);
    }

    void IDisposable.Dispose()
    {
      _dsl.Dispose();
    }

    [Fact]
    public void OneProcOnePath()
    {
      _dsl
        .AddJobs(new[] {
          FakeIccDsl.CreateOneProcOnePathJob(
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
          .AddInsp(proc: 1, path: 1, inspTy: "InspTy", cntr: "Thecounter", max: 2)
          },
          new[] { (prog: "prog111", rev: 5L) }
        )
        .MoveToMachineQueue(pal: 2, mach: 3)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 3, 5, 6 },
            progs: new[] { 2100 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })
        .MoveToBuffer(pal: 2, buff: 2)
        .DecrJobRemainCnt("uniq1", path: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
          })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[]{
          FakeIccDsl.ExpectNewRoute(
            pal: 2,
            luls: new[] { 3, 4 },
            machs: new[] { 3, 5, 6 },
            progs: new[] { 2100 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })
        .ExpectNoChanges()
        .MoveToLoad(pal: 1, lul: 1)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 1) })
        .AdvanceMinutes(4) // =4
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 4)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
          })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 1, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 4, activeMins: 8, mats: out var fstMats)
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: fstMats)
        })
        .AdvanceMinutes(1)
        .ExpectNoChanges()

        .MoveToMachineQueue(pal: 1, mach: 3)
        .AdvanceMinutes(2)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectRotaryStart(pal: 1, mach: 3, mats: fstMats),
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, waitForMach: true, elapMin: 3, mats: fstMats),
        })
        .AdvanceMinutes(3)
        .MoveToBuffer(pal: 1, buff: 6)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectRotaryEnd(pal: 1, mach: 3, rotate: false, elapMin: 3, mats: fstMats),
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 6, waitForMach: true, mats: fstMats)
        })
        .AdvanceMinutes(2)
        .MoveToMachineQueue(pal: 1, mach: 3)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectRotaryStart(pal: 1, mach: 3, mats: fstMats),
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 6, waitForMach: true, elapMin: 2, mats: fstMats),
        })
        .MoveToMachine(pal: 1, mach: 3)
        .AdvanceMinutes(1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectRotaryEnd(pal: 1, mach: 3, rotate: true, elapMin: 1, mats: fstMats)
        })
        .StartMachine(mach: 3, program: 2100)
        .UpdateExpectedMaterial(fstMats, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Machining;
            im.Action.Program = "prog111 rev5";
            im.Action.ElapsedMachiningTime = TimeSpan.Zero;
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 3, program: "prog111", rev: 5, mat: fstMats)
        })
        .AdvanceMinutes(10)
        .UpdateExpectedMaterial(fstMats, im =>
          {
            im.Action.ElapsedMachiningTime = TimeSpan.FromMinutes(10);
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(4);
          }
        )
        .ExpectNoChanges()
        .AdvanceMinutes(5)
        .EndMachine(mach: 3)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(fstMats, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Waiting;
            im.Action.Program = null;
            im.Action.ElapsedMachiningTime = null;
            im.Action.ExpectedRemainingMachiningTime = null;
            im.LastCompletedMachiningRouteStopIndex = 0;
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 3, program: "prog111", rev: 5, elapsedMin: 15, activeMin: 14, mats: fstMats),
        })
        .MoveToMachineQueue(pal: 1, mach: 3)
        .ExpectNoChanges()
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(fstMats, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial;
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectInspection(mat: fstMats, cntr: "Thecounter", inspTy: "InspTy", inspect: false, path: new[] {
            new MaterialProcessActualPath() {
              Process = 1, Pallet = "1", LoadStation = 1, UnloadStation = -1, Stops = new List<MaterialProcessActualPath.Stop> {
                new MaterialProcessActualPath.Stop() {
                  StationName = "TestMC", StationNum = 103
                }
              }
            }
          }),
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: fstMats)
        })
        .AdvanceMinutes(3)
        .ExpectNoChanges()
        .MoveToLoad(pal: 1, lul: 4)
        .DecrJobRemainCnt("uniq1", path: 1)
        .SetExpectedLoadCastings(new[] {
         (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
        })
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, waitForMach: false, elapMin: 3, mats: fstMats),
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
          FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2)
        })
        .AdvanceMinutes(2) // =33
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action.ElapsedLoadUnloadTime = TimeSpan.FromMinutes(2);
        })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 2)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .SetExpectedLoadCastings(new[] {
         (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
        })
        .RemoveExpectedMaterial(fstMats.Select(m => m.MaterialID))
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 33 - 4),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 2, activeMins: 9, mats: fstMats),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 4, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 2, activeMins: 8, mats: out var sndMats)
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: sndMats)
        })
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(sndMats, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Machining;
            im.Action.Program = "prog111 rev5";
            im.Action.ElapsedMachiningTime = TimeSpan.Zero;
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, waitForMach: true, elapMin: 0, mats: sndMats),
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: sndMats)
        })
        .AdvanceMinutes(15) // =45
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(sndMats, im =>
          {
            im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
            im.LastCompletedMachiningRouteStopIndex = 0;
          }
        )
        .ExpectTransition(new[] {
         FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: "prog111", rev: 5, elapsedMin: 15, activeMin: 14, mats: sndMats),
        })

        .MoveToLoad(pal: 1, lul: 3)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(sndMats, m =>
        {
          m.Action.Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial;
          m.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
          m.SignaledInspections.Add("InspTy");
        })
        // no load of new, since qty is 3 and have produced 2 on pallet 1 and there is still a pending load assigned to pallet 2
        .ExpectTransition(new[] {
         FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3),
         FakeIccDsl.ExpectInspection(mat: sndMats, cntr: "Thecounter", inspTy: "InspTy", inspect: true, path: new[] {
           new MaterialProcessActualPath() {
             Process = 1, Pallet = "1", LoadStation = 4, UnloadStation = -1, Stops = new List<MaterialProcessActualPath.Stop> {
               new MaterialProcessActualPath.Stop() {
                 StationName = "TestMC", StationNum = 106
               }
             }
           }
         })
        })
      .AdvanceMinutes(5) // = 50 min
      .SetNoWork(pal: 1)
      .RemoveExpectedMaterial(sndMats)
      .ExpectTransition(new[] {
         FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 50 - 30),
         FakeIccDsl.UnloadFromFace(pal: 1, lul: 3, elapsedMin: 5, activeMins: 9, mats: sndMats),
      })
      .MoveToBuffer(pal: 1, buff: 1)
      .ExpectNoChanges()
      ;
    }

    [Fact]
    public void IgnoresDecrementedJob()
    {
      _dsl
        .AddJobs(new[] {
          FakeIccDsl.CreateOneProcOnePathJob(
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
          .AddInsp(proc: 1, path: 1, inspTy: "InspTy", cntr: "Thecounter", max: 2)
          }
        )
        .MoveToMachineQueue(pal: 2, mach: 3)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 345 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
        )})
        .MoveToBuffer(pal: 2, buff: 2)

        //normally pal 2 should get a new route, but add a decrement
        .AddJobDecrement("uniq1")
        .ExpectNoChanges();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mycasting")]
    public void CastingsFromQueue(string casting)
    {
      _dsl
        .AddJobs(new[] {
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
          )},
          new[] { (prog: "prog111", rev: 6L) }
        )
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 6, mcMin: 14)
        });

      if (string.IsNullOrEmpty(casting))
      {
        casting = "part1";
      }

      _dsl
        .AddUnallocatedCasting(queue: "thequeue", rawMatName: "part4", mat: out var unusedMat)
        .ExpectNoChanges()

        .AddUnallocatedCasting(queue: "thequeue", rawMatName: casting, mat: out var queuedMat)
        .UpdateExpectedMaterial(queuedMat.MaterialID, m =>
        {
          m.JobUnique = "uniq1";
          m.PartName = "part1";
          m.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPallet = 1.ToString(),
            LoadOntoFace = 1,
            ProcessAfterLoad = 1,
            PathAfterLoad = 1
          };
        })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectNewRoute(pal: 1, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 2100 }, faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) })
        })
        .MoveToLoad(pal: 1, lul: 3)
        .UpdateExpectedMaterial(queuedMat.MaterialID, m =>
        {
          m.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3)
        })
        .AdvanceMinutes(3) // = 3min
        .UpdateExpectedMaterial(queuedMat.MaterialID, m =>
        {
          m.Action.ElapsedLoadUnloadTime = TimeSpan.FromMinutes(3);
        })
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .UpdateExpectedMaterial(queuedMat.MaterialID, m =>
        {
          m.Process = 1;
          m.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          m.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.OnPallet,
            Pallet = "1",
            Face = 1
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
          _dsl.LoadToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", elapsedMin: 3, activeMins: 8, loadingMats: new[] {queuedMat}, loadedMats: out var mat1, part: "part1"),
          FakeIccDsl.RemoveFromQueue("thequeue", pos: 1, elapMin: 3, mat: mat1)
        })
        ;
    }

    [Fact]
    public void MultiProcSamePallet()
    {
      _dsl
        .AddJobs(new[] {
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
            face2: 2
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
            face2: 2
          )
          },
          new[] {
            (prog: "prog111", rev: 4L),
            (prog: "prog111", rev: 5L),
            (prog: "prog222", rev: 6L),
            (prog: "prog222", rev: 7L),
            (prog: "prog333", rev: 8L),
            (prog: "prog444", rev: 9L),
          }
        )

        // process 1 only cycle
        .SetExpectedLoadCastings(new[] {
              (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          FakeIccDsl.ExpectAddNewProgram(progNum: 2200, name: "prog222", rev: 6, mcMin: 15),
          FakeIccDsl.ExpectAddNewProgram(progNum: 2101, name: "prog333", rev: 8, mcMin: 18),
          FakeIccDsl.ExpectAddNewProgram(progNum: 2201, name: "prog444", rev: 9, mcMin: 21),
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 2100 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .MoveToLoad(pal: 1, lul: 3)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(4) // =4
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 4)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 4, activeMins: 8, mats: out var AAAproc1)
        })
        .MoveToBuffer(pal: 1, buff: 7)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 7, waitForMach: true, mats: AAAproc1)
        })
        .MoveToMachineQueue(pal: 1, mach: 6)
        .AdvanceMinutes(5) // = 9
        .SetBeforeMC(pal: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 7, waitForMach: true, elapMin: 5, mats: AAAproc1),
          FakeIccDsl.ExpectRotaryStart(pal: 1, mach: 6, mats: AAAproc1)
        })
        .AdvanceMinutes(1) // = 10
        .MoveToMachine(pal: 1, mach: 6)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectRotaryEnd(pal: 1, mach: 6, rotate: true, elapMin: 1, mats: AAAproc1)
        })
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(AAAproc1, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Machining;
            im.Action.Program = "prog111 rev5";
            im.Action.ElapsedMachiningTime = TimeSpan.Zero;
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
          }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1) })
        .AdvanceMinutes(15) // =25
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(AAAproc1, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Waiting;
            im.Action.Program = null;
            im.Action.ElapsedMachiningTime = null;
            im.Action.ExpectedRemainingMachiningTime = null;
            im.LastCompletedMachiningRouteStopIndex = 0;
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: "prog111", rev: 5, elapsedMin: 15, activeMin: 14, mats: AAAproc1)
        })

        // now a cycle with process 1 and 2
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .DecrJobRemainCnt("uniq1", path: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
         .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
         .UpdateExpectedMaterial(AAAproc1, im =>
         {
           im.Action.Type = InProcessMaterialAction.ActionType.Loading;
           im.Action.LoadOntoPallet = "1";
           im.Action.ProcessAfterLoad = 2;
           im.Action.PathAfterLoad = 1;
           im.Action.LoadOntoFace = 2;
           im.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
           im.LastCompletedMachiningRouteStopIndex = null;
         })
         .ExpectTransition(new[] {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 2100, 2200 },
              faces: new[] {
                (face: 1, unique: "uniq1", proc: 1, path: 1),
                (face: 2, unique: "uniq1", proc: 2, path: 1)
              }
            ),
            FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4)
         })
        .AdvanceMinutes(20) // =45
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Process = 2;
          im.Path = 1;
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.Location.Face = 2;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 45 - 4),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 20, activeMins: 9, mats: AAAproc1),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 4, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 20, activeMins: 8, mats: out var BBBproc1),
          _dsl.LoadToFace(pal: 1, lul: 4, face: 2, unique: "uniq1", elapsedMin: 20, activeMins: 10, loadingMats: AAAproc1, loadedMats: out var AAAproc2)
        })

        .AdvanceMinutes(2) // = 47min
        .MoveToMachine(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(BBBproc1, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Machining;
            im.Action.Program = "prog111 rev5";
            im.Action.ElapsedMachiningTime = TimeSpan.Zero;
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
          }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: BBBproc1) })
        .AdvanceMinutes(20) // = 67min
        .StartMachine(mach: 5, program: 2200)
        .UpdateExpectedMaterial(BBBproc1, im =>
          {
            im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          }
        )
        .UpdateExpectedMaterial(AAAproc2, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog222 rev6";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15);
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: "prog111", rev:5, elapsedMin: 20, activeMin: 14, mats: BBBproc1),
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog222", rev: 6, mat: AAAproc2)
        })
        .AdvanceMinutes(30) // = 97min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(BBBproc1, im =>
          {
            im.LastCompletedMachiningRouteStopIndex = 0;
          }
        )
        .UpdateExpectedMaterial(AAAproc2, im =>
          {
            im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
            im.LastCompletedMachiningRouteStopIndex = 0;
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: "prog222", rev: 6, elapsedMin: 30, activeMin: 15, mats: AAAproc2)
        })

        .MoveToLoad(pal: 1, lul: 4)
        .SetBeforeUnload(pal: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .DecrJobRemainCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(BBBproc1, im =>
         {
           im.Action = new InProcessMaterialAction()
           {
             Type = InProcessMaterialAction.ActionType.Loading,
             LoadOntoFace = 2,
             LoadOntoPallet = "1",
             ProcessAfterLoad = 2,
             PathAfterLoad = 1,
             ElapsedLoadUnloadTime = TimeSpan.Zero
           };
           im.LastCompletedMachiningRouteStopIndex = null;
         })
        .UpdateExpectedMaterial(AAAproc2, im =>
         {
           im.Action = new InProcessMaterialAction()
           {
             Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
             ElapsedLoadUnloadTime = TimeSpan.Zero
           };
           im.LastCompletedMachiningRouteStopIndex = null;
         })
        .ExpectTransition(new[] {
           FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2),
           FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4)
         })
        .AdvanceMinutes(10) //= 107 min
        .RemoveExpectedMaterial(AAAproc2.Select(m => m.MaterialID))
        .UpdateExpectedMaterial(BBBproc1.Select(m => m.MaterialID), im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.Location.Face = 2;
          im.Process = 2;
          im.Path = 1;
        })
        .ClearExpectedLoadCastings()
        .SetAfterLoad(pal: 1)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 107-45),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 10, activeMins: 9, mats: BBBproc1),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 10, activeMins: 11, mats: AAAproc2),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 4, elapsedMin: 10, face: 1, unique: "uniq1", path: 1, cnt: 1, activeMins: 8, mats: out var CCCproc1),
          _dsl.LoadToFace(pal: 1, lul: 4, face: 2, unique: "uniq1", elapsedMin: 10, activeMins: 10, loadingMats: BBBproc1, loadedMats: out var BBBproc2)
        })

        //a full cycle
        .MoveToMachine(pal: 1, mach: 6)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 6, program: 2200)
        .UpdateExpectedMaterial(BBBproc2, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog222 rev6";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15);
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog222", rev: 6, mat: BBBproc2)
        })
        .AdvanceMinutes(5) // = 112 min
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(BBBproc2, im =>
          {
            im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          }
        )
        .UpdateExpectedMaterial(CCCproc1, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog111 rev5";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: "prog222", rev: 6, elapsedMin: 5, activeMin: 15, mats: BBBproc2),
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: CCCproc1)
        })
        .AdvanceMinutes(100) // 212 min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(BBBproc2, im =>
        {
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .UpdateExpectedMaterial(CCCproc1, im =>
          {
            im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
            im.LastCompletedMachiningRouteStopIndex = 0;
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: "prog111", rev: 5, elapsedMin: 100, activeMin: 14, mats: CCCproc1),
        })

        // load of new job part2
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 3)
        .UpdateExpectedMaterial(CCCproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoFace = 2,
            LoadOntoPallet = "1",
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .UpdateExpectedMaterial(BBBproc2, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .SetExpectedLoadCastings(new[] {
          (unique: "uniq2", part: "part2", pal: 1, path: 1, face: 1)
        })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .DecrJobRemainCnt(unique: "uniq2", path: 1)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 2200, 2101 },
            faces: new[] {
              (face: 1, unique: "uniq2", proc: 1, path: 1),
              (face: 2, unique: "uniq1", proc: 2, path: 1)
            }
          ),
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3)
        })

        .AdvanceMinutes(10) // =mins 222
        .RemoveExpectedMaterial(BBBproc2)
        .UpdateExpectedMaterial(CCCproc1, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.Location.Face = 2;
          im.Process = 2;
          im.Path = 1;
        })
        .ClearExpectedLoadCastings()
        .SetAfterLoad(pal: 1)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 222 - 107),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 3, elapsedMin: 10, activeMins: 9, mats: CCCproc1),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 3, elapsedMin: 10, activeMins: 11, mats: BBBproc2),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, elapsedMin: 10, face: 1, unique: "uniq2", cnt: 1, path: 1, activeMins: 16, mats: out var DDDproc1),
          _dsl.LoadToFace(pal: 1, lul: 3, face: 2, unique: "uniq1", elapsedMin: 10, activeMins: 10, loadingMats: CCCproc1, loadedMats: out var CCCproc2)
        })

        .MoveToMachine(pal: 1, mach: 6)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 6, program: 2101)
        .UpdateExpectedMaterial(DDDproc1, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog333 rev8";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(18);
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog333", rev: 8, mat: DDDproc1)
        })
        .AdvanceMinutes(4) // = 226min
        .StartMachine(mach: 6, program: 2200)
        .UpdateExpectedMaterial(DDDproc1, im =>
        {
          im.Action = new InProcessMaterialAction { Type = InProcessMaterialAction.ActionType.Waiting };
        })
        .UpdateExpectedMaterial(CCCproc2, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog222 rev6";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15);
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: "prog333", rev: 8, elapsedMin: 4, activeMin: 18, mats: DDDproc1),
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog222", rev: 6, mat: CCCproc2)
        })
        .AdvanceMinutes(4) // = 230min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(DDDproc1, im =>
        {
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .UpdateExpectedMaterial(CCCproc2, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: "prog222", rev: 6, elapsedMin: 4, activeMin: 15, mats: CCCproc2)
        })

        // no new load, since quantity of 3 reached
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 3)
        .UpdateExpectedMaterial(DDDproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoFace = 2,
            LoadOntoPallet = "1",
            ProcessAfterLoad = 2,
            PathAfterLoad = 1,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .UpdateExpectedMaterial(CCCproc2, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 2201 },
            faces: new[] {
              (face: 2, unique: "uniq2", proc: 2, path: 1)
            }
          ),
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3)
        })

        .AdvanceMinutes(2) // = 232 min
        .RemoveExpectedMaterial(CCCproc2.Select(m => m.MaterialID))
        .UpdateExpectedMaterial(DDDproc1.Select(m => m.MaterialID), im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.Location.Face = 2;
          im.Process = 2;
          im.Path = 1;
        })
        .SetAfterLoad(pal: 1)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 232 - 222),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 3, elapsedMin: 2, activeMins: 17, mats: DDDproc1),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 3, elapsedMin: 2, activeMins: 11, mats: CCCproc2),
          _dsl.LoadToFace(pal: 1, lul: 3, face: 2, unique: "uniq2", elapsedMin: 2, activeMins: 19, loadingMats: DDDproc1, loadedMats: out var DDDproc2)
        })

        // a cycle with only proc2
        .MoveToMachine(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 5, program: 2201)
        .UpdateExpectedMaterial(DDDproc2, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog444 rev9";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(21);
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog444", rev: 9, mat: DDDproc2)
        })

        .AdvanceMinutes(20) // = 252 min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(DDDproc2, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: "prog444", rev: 9, elapsedMin: 20, activeMin: 21, mats: DDDproc2)
        })
        .MoveToLoad(pal: 1, lul: 3)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(DDDproc2, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
        })
        // nothing new loaded
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3)
        })
        .AdvanceMinutes(10) // = 262min
        .SetNoWork(pal: 1)
        .RemoveExpectedMaterial(DDDproc2.Select(m => m.MaterialID))
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 262 -  232),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 3, elapsedMin: 10, activeMins: 20, mats: DDDproc2)
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectNoChanges()

        ;

    }

    [Fact]
    public void MultipleProcessSeparatePallets()
    {
      _dsl
        .AddJobs(new[] {
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
            queue: "qqq"
          )},
          new[] {
            (prog: "prog111", rev: 5L),
          }
        )
        .SetExpectedLoadCastings(new[] {
              (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
        })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 2100 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })

        // first process on pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2) // = 2min
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 2)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 2, activeMins: 8, mats: out var AAAproc1)
        })
        .MoveToMachineQueue(pal: 1, mach: 6)
        .ExpectNoChanges()
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog111 rev5";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
        })
        .ExpectTransition(new[] { FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1) })
        .AdvanceMinutes(10) // = 12min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Waiting;
          im.Action.Program = null;
          im.Action.ElapsedMachiningTime = null;
          im.Action.ExpectedRemainingMachiningTime = null;
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: "prog111", rev: 5, elapsedMin: 10, activeMin: 14, mats: AAAproc1)
        })
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] {
              (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
        })
        .DecrJobRemainCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
          FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2)
        })
        .AdvanceMinutes(15) // 27min
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPallet = "2",
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1
          };
          im.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = "qqq",
            QueuePosition = 0
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 27 - 2),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 15, activeMins: 9, mats: AAAproc1),
          FakeIccDsl.AddToQueue("qqq", 0, AAAproc1),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 4, elapsedMin: 15, face: 1, unique: "uniq1", path: 1, cnt: 1, activeMins: 8, mats: out var BBBproc1),
          FakeIccDsl.ExpectNewRoute(
            pal: 2,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 654 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
          )
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: BBBproc1)
        })

        // load process on pallet 2
        .MoveToLoad(pal: 2, lul: 3)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3)
        })
        .AdvanceMinutes(7) // =34 min
        .SetAfterLoad(pal: 2)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Process = 2;
          im.Path = 1;
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Waiting,
          };
          im.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.OnPallet,
            Pallet = "2",
            Face = 1
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 2, mins: 0),
          _dsl.LoadToFace(pal: 2, face: 1, unique: "uniq1", lul: 3, elapsedMin: 7, activeMins: 11, loadingMats: AAAproc1, loadedMats: out var AAAproc2),
          FakeIccDsl.RemoveFromQueue("qqq", pos: 0, elapMin: 7, mat: AAAproc2)
        })

        // machine both pallets 1 and 2
        .MoveToMachine(pal: 2, mach: 6)
        .SetBeforeMC(pal: 2)
        .StartMachine(mach: 6, program: 654)
        .UpdateExpectedMaterial(AAAproc2, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "654",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(10)
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineBegin(pal: 2, machine: 6, program: "654", mat: AAAproc2)
        })
        .AdvanceMinutes(4) // = 38min
        .MoveToMachine(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(AAAproc2, im =>
        {
          im.Action.ElapsedMachiningTime = TimeSpan.FromMinutes(4);
          im.Action.ExpectedRemainingMachiningTime -= TimeSpan.FromMinutes(4);
        })
        .UpdateExpectedMaterial(BBBproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14)
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 7 + 4, waitForMach: true, mats: BBBproc1),
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: BBBproc1)
        })
        .AdvanceMinutes(4) // = 42min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(BBBproc1, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .UpdateExpectedMaterial(AAAproc2, im =>
        {
          im.Action.ElapsedMachiningTime = TimeSpan.FromMinutes(4 + 4);
          im.Action.ExpectedRemainingMachiningTime -= TimeSpan.FromMinutes(4);
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: "prog111", rev: 5, elapsedMin: 4, activeMin: 14, mats: BBBproc1)
        })
        .AdvanceMinutes(1) // =43min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 2)
        .UpdateExpectedMaterial(AAAproc2, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 2, mach: 6, program: "654", elapsedMin: 4 + 4 + 1, activeMin: 10, mats: AAAproc2)
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .MoveToBuffer(pal: 2, buff: 2)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: BBBproc1),
          FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: false, mats: AAAproc2),
        })

        // unload process 1 first into queue
        .MoveToLoad(pal: 1, lul: 4)
        .SetBeforeUnload(pal: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
        })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .DecrJobRemainCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(BBBproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 0, waitForMach: false, mats: BBBproc1),
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
          FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2)
        })
        .AdvanceMinutes(5) // = 48min
        .SetAfterLoad(pal: 1)
        .UpdateExpectedMaterial(BBBproc1, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = "qqq",
            QueuePosition = 0
          };
        })
        .ClearExpectedLoadCastings()
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 48 - 27),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 5, activeMins: 9, mats: BBBproc1),
          FakeIccDsl.AddToQueue("qqq", 0, mat: BBBproc1),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 4, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 5, activeMins: 8, mats: out var CCCproc1),
        })
        .MoveToBuffer(pal: 1, buff: 1)

        // now unload and load pallet 2
        .MoveToLoad(pal: 2, lul: 3)
        .SetBeforeUnload(pal: 2)
        .UpdateExpectedMaterial(AAAproc2, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .UpdateExpectedMaterial(BBBproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoFace = 1,
            LoadOntoPallet = "2",
            PathAfterLoad = 1,
            ProcessAfterLoad = 2,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: CCCproc1),
          FakeIccDsl.ExpectStockerEnd(pal: 2, stocker: 2, elapMin: 5, waitForMach: false, mats: AAAproc2),
          FakeIccDsl.ExpectRouteIncrement(pal: 2, newCycleCnt: 2),
          FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3)
        })
        .AdvanceMinutes(12) // = 60min
        .SetAfterLoad(pal: 2)
        .RemoveExpectedMaterial(AAAproc2)
        .UpdateExpectedMaterial(BBBproc1, im =>
        {
          im.Process = 2;
          im.Path = 1;
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.OnPallet,
            Pallet = "2",
            Face = 1
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 2, mins: 60 - 34),
          FakeIccDsl.UnloadFromFace(pal: 2, lul: 3, elapsedMin: 12, activeMins: 12, mats: AAAproc2),
          _dsl.LoadToFace(pal: 2, face: 1, unique: "uniq1", lul: 3, elapsedMin: 12, activeMins: 11, loadingMats: BBBproc1, loadedMats: out var BBBproc2),
          FakeIccDsl.RemoveFromQueue(queue: "qqq", pos: 0, elapMin: 12, mat: BBBproc2)
        })
        .MoveToBuffer(pal: 2, buff: 2)

        // run machine cycles for both pallets
        .MoveToMachine(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(CCCproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14)
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, waitForMach: true, elapMin: 12, mats: CCCproc1),
          FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: true, mats: BBBproc2),
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: CCCproc1)
        })
        .AdvanceMinutes(1) // = 61 min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(CCCproc1, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: "prog111", rev: 5, elapsedMin: 1, activeMin: 14, mats: CCCproc1)
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .MoveToMachine(pal: 2, mach: 5)
        .SetBeforeMC(pal: 2)
        .StartMachine(mach: 5, program: 654)
        .UpdateExpectedMaterial(BBBproc2, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "654",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(10)
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 2, stocker: 2, waitForMach: true, elapMin: 1, mats: BBBproc2),
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: CCCproc1),
          FakeIccDsl.ExpectMachineBegin(pal: 2, machine: 5, program: "654", mat: BBBproc2)
        })
        .AdvanceMinutes(2) // = 63 min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 2)
        .UpdateExpectedMaterial(BBBproc2, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 2, mach: 5, program: "654", elapsedMin: 2, activeMin: 10, mats: BBBproc2)
        })
        .MoveToBuffer(pal: 2, buff: 2)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: false, mats: BBBproc2)
        })

        //now unload pallet 2 first
        .MoveToLoad(pal: 2, lul: 4)
        .SetBeforeUnload(pal: 2)
        .UpdateExpectedMaterial(BBBproc2, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 2, stocker: 2, waitForMach: false, elapMin: 0, mats: BBBproc2),
          FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 4)
        })
        .AdvanceMinutes(3) // = 66 min
        .SetNoWork(pal: 2)
        .RemoveExpectedMaterial(BBBproc2)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 2, mins: 66 - 60),
          FakeIccDsl.UnloadFromFace(pal: 2, lul: 4, elapsedMin: 3, activeMins: 12, mats: BBBproc2)
        })
        .MoveToBuffer(pal: 2, buff: 2)

        // now unload pal 1 which should trigger pallet 2.  Nothing new should be loaded since quantity of 3 reached
        .MoveToLoad(pal: 1, lul: 4)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(CCCproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 5, waitForMach: false, mats: CCCproc1),
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4)
        })
        .AdvanceMinutes(3) //= 69 min
        .SetNoWork(pal: 1)
        .UpdateExpectedMaterial(CCCproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPallet = "2",
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1
          };
          im.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = "qqq",
            QueuePosition = 0
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 69 - 48),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 3, activeMins: 9, mats: CCCproc1),
          FakeIccDsl.AddToQueue("qqq", 0, mat: CCCproc1),
          FakeIccDsl.ExpectRouteIncrement(pal: 2, newCycleCnt: 1, faces: new[] {
            (face: 1, unique: "uniq1", proc: 2, path: 1)
          })
        })
        .MoveToBuffer(pal: 1, buff: 1)

        // now load pal 2
        .MoveToLoad(pal: 2, lul: 4)
        .SetBeforeLoad(pal: 2)
        .UpdateExpectedMaterial(CCCproc1, im =>
        {
          im.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 4)
        })
        .AdvanceMinutes(22) // = 91 min
        .SetAfterLoad(pal: 2)
        .UpdateExpectedMaterial(CCCproc1, im =>
        {
          im.Process = 2;
          im.Path = 1;
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.OnPallet,
            Pallet = "2",
            Face = 1
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 2, mins: 91 - 66),
        _dsl.LoadToFace(pal: 2, face: 1, unique: "uniq1", lul: 4, elapsedMin: 22, activeMins: 11, loadingMats: CCCproc1, loadedMats: out var CCCproc2),
          FakeIccDsl.RemoveFromQueue(queue: "qqq", pos: 0, elapMin: 22, mat: CCCproc2)
        })

        ;

    }

    [Fact]
    public void SeparateLoadUnloadStations()
    {
      _dsl
      .AddJobs(new[] {
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
          queue: "qqq"
        )},
        new[] {
          (prog: "prog111", rev: 5L),
          (prog: "prog222", rev: 6L)
        }
      )
      .SetExpectedLoadCastings(new[] {
            (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
      })
      .DecrJobRemainCnt("uniq1", path: 1)
      .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
        FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
        FakeIccDsl.ExpectAddNewProgram(progNum: 2200, name: "prog222", rev: 6, mcMin: 10),
        FakeIccDsl.ExpectNewRoute(
          pal: 1,
          luls: new[] { 3 },
          unloads: new[] { 4 },
          machs: new[] { 5, 6 },
          progs: new[] { 2100 },
          faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
        )
      })

      .MoveToLoad(pal: 1, lul: 3)
      .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
      .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
      .AdvanceMinutes(1) // = 1min
      .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 1)
      .ExpectNoChanges()
      .SetAfterLoad(pal: 1)
      .ClearExpectedLoadCastings()
      .ExpectTransition(new[] {
        FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
        FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 1, activeMins: 8, mats: out var AAAproc1)
      })
      .MoveToMachine(pal: 1, mach: 6)
      .SetBeforeMC(pal: 1)
      .StartMachine(mach: 6, program: 2100)
      .UpdateExpectedMaterial(AAAproc1, im =>
      {
        im.Action.Type = InProcessMaterialAction.ActionType.Machining;
        im.Action.Program = "prog111 rev5";
        im.Action.ElapsedMachiningTime = TimeSpan.Zero;
        im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
      })
      .ExpectTransition(new[] {
        FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1)
      })
      .AdvanceMinutes(10) // = 11min
      .EndMachine(mach: 6)
      .SetAfterMC(pal: 1)
      .UpdateExpectedMaterial(AAAproc1, im =>
      {
        im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
        im.LastCompletedMachiningRouteStopIndex = 0;
      })
      .ExpectTransition(new[] {
        FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: "prog111", rev: 5, elapsedMin: 10, activeMin: 14, mats: AAAproc1)
      })

      // when moving to load station, should just unload and not load

      .SetBeforeUnload(pal: 1)
      .MoveToLoad(pal: 1, lul: 4)
      .UpdateExpectedMaterial(AAAproc1, im =>
      {
        im.Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
          UnloadIntoQueue = "qqq",
          ElapsedLoadUnloadTime = TimeSpan.Zero
        };
      })
      .ExpectTransition(new[] {
        FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4)
      })
      .AdvanceMinutes(4) // = 15min
      .SetAfterUnload(pal: 1)
      .SetNoWork(pal: 1)
      .UpdateExpectedMaterial(AAAproc1, im =>
      {
        im.Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.Loading,
          LoadOntoPallet = "2",
          LoadOntoFace = 1,
          ProcessAfterLoad = 2,
          PathAfterLoad = 1
        };
        im.Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.InQueue,
          CurrentQueue = "qqq",
          QueuePosition = 0
        };
        im.LastCompletedMachiningRouteStopIndex = null;
      })
      .ExpectTransition(new[] {
        FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 15 - 1),
        FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 4, activeMins: 9, mats: AAAproc1),
        FakeIccDsl.AddToQueue("qqq", 0, AAAproc1),
        FakeIccDsl.ExpectNewRoute(
          pal: 2,
          luls: new[] { 4 },
          unloads: new[] { 5 },
          machs: new[] { 5, 6},
          progs: new[] { 2200 },
          faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1)}
        )
      })
      .MoveToBuffer(pal: 1, buff: 1)
      .SetExpectedLoadCastings(new[] {
        (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
      })
      .DecrJobRemainCnt("uniq1", path: 1)
      .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
        FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 1, faces: new[] {
          (face: 1, unique: "uniq1", proc: 1, path: 1)
        })
      })

      // now both pal 1 and pal 2 are in buffer but have routes to load
      .MoveToLoad(pal: 2, lul: 4)
      .SetBeforeLoad(pal: 2)
      .UpdateExpectedMaterial(AAAproc1, im =>
      {
        im.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
      })
      .ExpectTransition(new[] {
        FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 4)
      })
      .AdvanceMinutes(1) // =16min
      .UpdateExpectedMaterial(AAAproc1, im =>
      {
        im.Action.ElapsedLoadUnloadTime = TimeSpan.FromMinutes(1);
      })
      .MoveToLoad(pal: 1, lul: 3)
      .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
      .SetBeforeLoad(pal: 1)
      .ExpectTransition(new[] {
        FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3)
      })
      .AdvanceMinutes(4) // = 20min
      .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 4)

      .SetAfterLoad(pal: 2)
      .UpdateExpectedMaterial(AAAproc1, im =>
      {
        im.Process = 2;
        im.Path = 1;
        im.Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.Waiting,
        };
        im.Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.OnPallet,
          Pallet = "2",
          Face = 1
        };
      })
      .ExpectTransition(new[] {
        FakeIccDsl.ExpectPalletCycle(pal: 2, mins: 0),
        _dsl.LoadToFace(pal: 2, face: 1, unique: "uniq1", lul: 4, elapsedMin: 5, activeMins: 11, loadingMats: AAAproc1, loadedMats: out var AAAproc2),
        FakeIccDsl.RemoveFromQueue("qqq", pos: 0, elapMin: 5, mat: AAAproc2)
      })
      ;

    }

    [Fact]
    public void MultipleMachineStops()
    {
      _dsl
        .AddJobs(new[] {
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
          )},
          new[] {
            (prog: "prog111", rev: 5L),
            (prog: "prog222", rev: 6L)
          }
        )
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          FakeIccDsl.ExpectAddNewProgram(progNum: 2101, name: "prog222", rev: 6, mcMin: 15),
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            loads: new[] { 3, 4 },
            machs1: new[] { 5, 6 },
            progs1: new[] { 2100 },
            machs2: new[] { 1, 2 },
            progs2: new[] { 2101 },
            reclamp: new[] { 2 },
            unloads: new[] { 3, 4 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
        )})
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(4) // =4
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 4)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 4, activeMins: 8, mats: out var fstMats)
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: fstMats)
        })

        .SetBeforeMC(pal: 1, machStepOffset: 0)
        .MoveToMachine(pal: 1, mach: 5)
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog111 rev5";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 0, waitForMach: true, mats: fstMats),
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: fstMats)
        })
        .AdvanceMinutes(5) // = 9min
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action.ElapsedMachiningTime = TimeSpan.FromMinutes(5);
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14 - 5);
        })
        .ExpectNoChanges()
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1, machStepOffset: 0)
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: "prog111", rev: 5, elapsedMin: 5, activeMin: 14, mats: fstMats)
        })

        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: fstMats)
        })
        .MoveToMachine(pal: 1, mach: 2)
        .SetBeforeMC(pal: 1, machStepOffset: 1)
        .StartMachine(mach: 2, program: 2101)
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog222 rev6";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15);
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, waitForMach: true, elapMin: 0, mats: fstMats),
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 2, program: "prog222", rev: 6, mat: fstMats)
        })
        .AdvanceMinutes(10) // = 19min
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog222 rev6";
          im.Action.ElapsedMachiningTime = TimeSpan.FromMinutes(10);
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15 - 10);
        })
        .ExpectNoChanges()
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1, machStepOffset: 1)
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.LastCompletedMachiningRouteStopIndex = 1;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 2, program: "prog222", rev: 6, elapsedMin: 10, activeMin: 15, mats: fstMats)
        })
        .MoveToBuffer(pal: 1, buff: 2)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 2, waitForMach: false, mats: fstMats)
        })

        .SetBeforeReclamp(pal: 1, reclampStepOffset: 0)
        .ExpectNoChanges()
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
        })
        .MoveToLoad(pal: 1, lul: 2)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 2, waitForMach: false, elapMin: 0, mats: fstMats),
          FakeIccDsl.ExpectReclampBegin(pal: 1, lul: 2, mats: fstMats)
        })
        .AdvanceMinutes(3) // = 22min
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action.ElapsedLoadUnloadTime = TimeSpan.FromMinutes(3);
        })
        .AdvanceMinutes(3) // = 26min
        .SetAfterReclamp(pal: 1, reclampStepOffset: 0)
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.LastCompletedMachiningRouteStopIndex = 2;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectReclampEnd(pal: 1, lul: 2, elapsedMin: 6, activeMin: 10, mats: fstMats)
        })
        .MoveToBuffer(pal: 1, buff: 4)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 4, waitForMach: false, mats: fstMats)
        })

        .MoveToLoad(pal: 1, lul: 4)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
         .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 4, elapMin: 0, waitForMach: false, mats: fstMats),
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
          FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2)
        })
        .AdvanceMinutes(10) // = 35min
        .SetAfterLoad(pal: 1)
        .RemoveExpectedMaterial(fstMats)
        .ClearExpectedLoadCastings()
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 35 - 4),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 10, activeMins: 9, mats: fstMats),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 4, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 10, activeMins: 8, mats: out var sndMats)
        })
        ;

    }

    [Fact]
    public void MissEvents()
    {
      _dsl
        .AddJobs(new[] {
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
          )},
          new[] { (prog: "prog111", rev: 5L) }
        )
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 2100 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
        )})
        .MoveToLoad(pal: 1, lul: 1)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 1) })
        .AdvanceMinutes(4) // =4

        // skip unload end and send straight to machine
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 5)
        .StartMachine(mach: 5, program: 2100)
        .ClearExpectedLoadCastings()
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 1, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 4, activeMins: 8, mats: out var fstMats,
            adj: im => {
              im.Action.Type = InProcessMaterialAction.ActionType.Machining;
              im.Action.Program = "prog111 rev5";
              im.Action.ElapsedMachiningTime = TimeSpan.Zero;
              im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
            }
          ),
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", fstMats, rev: 5)
        })

        .AdvanceMinutes(min: 10)

        // now skip after machine and go straight to unload
        .EndMachine(mach: 5)
        .MoveToLoad(pal: 1, lul: 3)
        .SetBeforeUnload(pal: 1)
        .UpdateExpectedMaterial(fstMats, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
        })
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .DecrJobRemainCnt(unique: "uniq1", path: 1)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: "prog111", rev: 5, elapsedMin: 10, activeMin: 14, mats: fstMats),
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3),
          FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2)
        })

        .AdvanceMinutes(min: 6) //= 20 min

        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .RemoveExpectedMaterial(fstMats)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 20 - 4),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 3, elapsedMin: 6, activeMins: 9, mats: fstMats),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 6, activeMins: 8, mats: out var sndMats)
        })

        .AdvanceMinutes(20)

        // go straight to after mc
        .MoveToBuffer(pal: 1, buff: 1)
        .SetAfterMC(pal: 1)
        .SetExecutedStationNum(pal: 1, new[] { NiigataStationNum.LoadStation(3), NiigataStationNum.Machine(5, _dsl.StatNames) }) // load 3, machine 5
        .UpdateExpectedMaterial(sndMats, im =>
        {
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: sndMats),
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: "prog111", rev: 5, elapsedMin: 0, activeMin: 14, mats: sndMats)
        });


    }

    [Fact]
    public void SizedQueues()
    {
      _dsl
        .AddJobs(new[] {
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
            queue: "sizedQ"
          )},
          new[] {
            (prog: "prog111", rev: 5L),
          }
        )
        .MoveToMachineQueue(pal: 2, mach: 2)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
        })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3 },
            machs: new[] { 5, 6 },
            progs: new[] { 2100 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })
        .MoveToBuffer(pal: 2, buff: 2)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1)
        })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectNewRoute(
            pal: 2,
            luls: new[] { 3 },
            machs: new[] { 5, 6 },
            progs: new[] { 2100 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })

        // load pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2)
        .SetAfterLoad(pal: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1)
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 2, activeMins: 8, mats: out var AAAProc1)
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: AAAProc1)
        })

        // load pallet 2
        .MoveToLoad(pal: 2, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 2, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3) })
        .AdvanceMinutes(3)
        .SetAfterLoad(pal: 2)
        .ClearExpectedLoadCastings()
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 2, mins: 0),
          FakeIccDsl.LoadCastingToFace(pal: 2, lul: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 3, activeMins: 8, mats: out var BBBproc1)
        })
        .MoveToBuffer(pal: 2, buff: 2)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: true, mats: BBBproc1)
        })

        // start machining pallet 1
        .MoveToMachineQueue(pal: 1, mach: 5)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 3, waitForMach: true, mats: AAAProc1),
          FakeIccDsl.ExpectRotaryStart(pal: 1, mach: 5, mats: AAAProc1)
        })
        .MoveToMachine(pal: 1, mach: 5)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectRotaryEnd(pal: 1, mach: 5, rotate: true, elapMin: 0, mats: AAAProc1)
        })
        .StartMachine(mach: 5, program: 2100)
        .UpdateExpectedMaterial(AAAProc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14)
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: "prog111", rev: 5, mat: AAAProc1),
          FakeIccDsl.ExpectPalletHold(pal: 1, hold: true)
        })

        // start machining pallet 2
        .AdvanceMinutes(5)
        .UpdateExpectedMaterial(AAAProc1, im =>
        {
          im.Action.ElapsedMachiningTime = TimeSpan.FromMinutes(5);
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14 - 5);
        })
        .MoveToMachine(pal: 2, mach: 6)
        .SetBeforeMC(pal: 2)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(BBBproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "prog111 rev5",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14)
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 2, stocker: 2, elapMin: 5, waitForMach: true, mats: BBBproc1),
          FakeIccDsl.ExpectMachineBegin(pal: 2, machine: 6, program: "prog111", rev: 5, mat: BBBproc1),
          FakeIccDsl.ExpectPalletHold(pal: 2, hold: true)
        })

        // end pallet 1 cycle
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(AAAProc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Waiting
          };
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: "prog111", rev: 5, elapsedMin: 5, activeMin: 14, mats: AAAProc1),
          FakeIccDsl.ExpectPalletHold(pal: 1, hold: false) // queue and pallet 4 is available
        })

        // end pallet 2 cycle
        .AdvanceMinutes(3)
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 2)
        .UpdateExpectedMaterial(BBBproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Waiting
          };
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 2, mach: 6, program: "prog111", rev: 5, elapsedMin: 3, activeMin: 14, mats: BBBproc1)
          // kept on hold!
        })

        // move pallet 1 to buffer
        .AdvanceMinutes(2)
        .SetBeforeUnload(pal: 1)
        .MoveToBuffer(pal: 1, buff: 1)
        .UpdateExpectedMaterial(AAAProc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "sizedQ",
          };
        })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          // no unhold of pallet 2
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: false, mats: AAAProc1)
        })

        // start unloading pal 1
        .AdvanceMinutes(6)
        .MoveToLoad(pal: 1, lul: 3)
        .DecrJobRemainCnt(unique: "uniq1", path: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
        })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .UpdateExpectedMaterial(AAAProc1, im =>
        {
          im.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerEnd(pal: 1, stocker: 1, elapMin: 6, waitForMach: false, mats: AAAProc1),
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3),
          FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2)
        })

        // move pallet 2 to buffer
        .AdvanceMinutes(3)
        .UpdateExpectedMaterial(AAAProc1, im =>
        {
          im.Action.ElapsedLoadUnloadTime = TimeSpan.FromMinutes(3);
        })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 3)
        .SetBeforeUnload(pal: 2)
        .MoveToBuffer(pal: 2, buff: 2)
        .UpdateExpectedMaterial(BBBproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "sizedQ",
          };
        })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 2, stocker: 2, waitForMach: false, mats: BBBproc1)
        })

        // finish load of pallet 1
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(AAAProc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPallet = "4",
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1
          };
          im.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = "sizedQ",
            QueuePosition = 0
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 24 - 2),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 3, elapsedMin: 3, activeMins: 9, mats: AAAProc1),
          FakeIccDsl.AddToQueue("sizedQ", 0, AAAProc1),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, elapsedMin: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, activeMins: 8, mats: out var CCCproc1),
          FakeIccDsl.ExpectNewRoute(
            pal: 4,
            luls: new[] { 3 },
            machs: new[] { 5, 6 },
            progs: new[] { 654 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
          )
          // pal 2 on hold since pallet 4 not available
        })

        // load pallet 4
        .MoveToBuffer(pal: 1, buff: 1)
        .MoveToLoad(pal: 4, lul: 3)
        .UpdateExpectedMaterial(AAAProc1, im =>
        {
          im.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: CCCproc1),
          FakeIccDsl.ExpectLoadBegin(pal: 4, lul: 3)
        })
        .AdvanceMinutes(5)
        .SetAfterLoad(pal: 4)
        .UpdateExpectedMaterial(AAAProc1, im =>
        {
          im.Process = 2;
          im.Path = 1;
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Waiting
          };
          im.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.OnPallet,
            Pallet = "4",
            Face = 1
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 4, mins: 0),
          _dsl.LoadToFace(pal: 4, face: 1, unique: "uniq1", lul: 3, elapsedMin: 5, activeMins: 11, loadingMats: AAAProc1, loadedMats: out var AAAproc2),
          FakeIccDsl.RemoveFromQueue("sizedQ", pos: 0, elapMin: 5, mat: AAAproc2)
        })

        // machine pallet 4
        .SetBeforeMC(pal: 4)
        .MoveToMachine(pal: 4, mach: 6)
        .StartMachine(mach: 6, program: 654)
        .UpdateExpectedMaterial(AAAproc2, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = "654",
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(10)
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineBegin(pal: 4, machine: 6, program: "654", mat: AAAproc2)
        })
        .AdvanceMinutes(2)
        .SetAfterMC(pal: 4)
        .EndMachine(mach: 6)
        .UpdateExpectedMaterial(AAAproc2, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 4, mach: 6, program: "654", elapsedMin: 2, activeMin: 10, mats: AAAproc2)
        })

        // unload 4
        .MoveToLoad(pal: 4, lul: 3)
        .SetBeforeUnload(pal: 4)
        .UpdateExpectedMaterial(AAAproc2, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 4, lul: 3)
        })
        .AdvanceMinutes(4)
        .SetNoWork(pal: 4)
        .RemoveExpectedMaterial(AAAproc2)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 4, mins: 6),
          FakeIccDsl.UnloadFromFace(pal: 4, lul: 3, elapsedMin: 4, activeMins: 12, mats: AAAproc2),
          FakeIccDsl.ExpectPalletHold(pal: 2, hold: false) // finally, unhold pallet 2!
        })
        ;

    }

    [Fact]
    public void DeletePrograms()
    {
      _dsl
        .AddJobs(new[] {
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
          )},
          new[] {
            (prog: "prog111", rev: 4L),
            (prog: "prog111", rev: 5L),
            (prog: "prog222", rev: 6L),
            (prog: "prog222", rev: 7L),
          }
        )

        // process 1 only cycle
        .SetExpectedLoadCastings(new[] {
              (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          FakeIccDsl.ExpectAddNewProgram(progNum: 2200, name: "prog222", rev: 6, mcMin: 15),
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 2100 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })
        .SetIccProgram(4000, "non-insight")
        .SetIccProgram(4001, "Insight:4:prog111") // has newer revision 5, should be deleted
        .SetIccProgram(4002, "Insight:7:prog222") // shouldn't be deleted since latest revision, even though not used
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectDeleteProgram(4001, "prog111", 4)
        })
        ;
    }

    [Fact]
    public void RemoveFromQueueDuringLoad()
    {
      _dsl
        .AddJobs(new[] {
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
            queue: "qqq"
          )},
          new[] {
            (prog: "prog111", rev: 5L),
          }
        )
        .SetExpectedLoadCastings(new[] {
              (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
        })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 2100 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })

        // first process on pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2) // = 2min
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 2)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 2, activeMins: 8, mats: out var AAAproc1)
        })
        .MoveToMachineQueue(pal: 1, mach: 6)
        .ExpectNoChanges()
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog111 rev5";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
        })
        .ExpectTransition(new[] { FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1) })
        .AdvanceMinutes(10) // = 12min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Waiting;
          im.Action.Program = null;
          im.Action.ElapsedMachiningTime = null;
          im.Action.ExpectedRemainingMachiningTime = null;
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: "prog111", rev: 5, elapsedMin: 10, activeMin: 14, mats: AAAproc1)
        })
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] {
              (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
        })
        .DecrJobRemainCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
          FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2)
        })
        .AdvanceMinutes(15) // 27min
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPallet = "2",
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1
          };
          im.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = "qqq",
            QueuePosition = 0
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 27 - 2),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 15, activeMins: 9, mats: AAAproc1),
          FakeIccDsl.AddToQueue("qqq", 0, AAAproc1),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 4, elapsedMin: 15, face: 1, unique: "uniq1", path: 1, cnt: 1, activeMins: 8, mats: out var BBBproc1),
          FakeIccDsl.ExpectNewRoute(
            pal: 2,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 654 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
          )
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: BBBproc1)
        })

        // move pallet 2 to the load station
        .MoveToLoad(pal: 2, lul: 3)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3)
        })

        // now pretend the part was scrapped or quarantined
        .RemoveFromQueue(AAAproc1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
            FakeIccDsl.ExpectNoWork(pal: 2, noWork: true),
            FakeIccDsl.ExpectPalletCycle(pal: 2, mins: 0)
        })
        .MoveToBuffer(pal: 2, buff: 2)
        .ExpectNoChanges()

        ;
    }

    [Fact]
    public void OperatorPressesUnloadToSetNoWork()
    {
      _dsl
        .AddJobs(new[] {
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
            queue: "qqq"
          )},
          new[] {
            (prog: "prog111", rev: 5L),
          }
        )
        .SetExpectedLoadCastings(new[] {
              (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
        })
        .DecrJobRemainCnt("uniq1", path: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectAddNewProgram(progNum: 2100, name: "prog111", rev: 5, mcMin: 14),
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 2100 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })

        // first process on pallet 1
        .MoveToLoad(pal: 1, lul: 3)
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(2) // = 2min
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 2)
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 2, activeMins: 8, mats: out var AAAproc1)
        })
        .MoveToMachineQueue(pal: 1, mach: 6)
        .ExpectNoChanges()
        .SetBeforeMC(pal: 1)
        .MoveToMachine(pal: 1, mach: 6)
        .StartMachine(mach: 6, program: 2100)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "prog111 rev5";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
        })
        .ExpectTransition(new[] { FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: "prog111", rev: 5, mat: AAAproc1) })
        .AdvanceMinutes(10) // = 12min
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Waiting;
          im.Action.Program = null;
          im.Action.ElapsedMachiningTime = null;
          im.Action.ExpectedRemainingMachiningTime = null;
          im.LastCompletedMachiningRouteStopIndex = 0;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: "prog111", rev: 5, elapsedMin: 10, activeMin: 14, mats: AAAproc1)
        })
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] {
              (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
        })
        .DecrJobRemainCnt("uniq1", path: 1)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = "qqq",
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
          im.LastCompletedMachiningRouteStopIndex = null;
        })
        .SetExpectedCastingElapsedLoadUnloadTime(pal: 1, mins: 0)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
          FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2)
        })
        .AdvanceMinutes(15) // 27min
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPallet = "2",
            LoadOntoFace = 1,
            ProcessAfterLoad = 2,
            PathAfterLoad = 1
          };
          im.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = "qqq",
            QueuePosition = 0
          };
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 27 - 2),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 15, activeMins: 9, mats: AAAproc1),
          FakeIccDsl.AddToQueue("qqq", 0, AAAproc1),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 4, elapsedMin: 15, face: 1, unique: "uniq1", path: 1, cnt: 1, activeMins: 8, mats: out var BBBproc1),
          FakeIccDsl.ExpectNewRoute(
            pal: 2,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 654 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
          )
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .SetBeforeMC(pal: 1)
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectStockerStart(pal: 1, stocker: 1, waitForMach: true, mats: BBBproc1)
        })

        // move pallet 2 to the load station
        .MoveToLoad(pal: 2, lul: 3)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action.ElapsedLoadUnloadTime = TimeSpan.Zero;
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 2, lul: 3)
        })

        // now assume the operator presses unload button to set nowork
        .SetNoWork(pal: 2)
        .UpdateExpectedMaterial(AAAproc1, im =>
        {
          im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
        })
        .ExpectTransition(new[] {
            FakeIccDsl.ExpectPalletCycle(pal: 2, mins: 0)
        })

        ;
    }
  }
}