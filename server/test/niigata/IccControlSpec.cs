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
using Xunit;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class NiigataAssignmentSpec : IDisposable
  {
    private FakeIccDsl _dsl;
    public NiigataAssignmentSpec()
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
          loadMins: 8,
          unloadMins: 9,
          machMins: 14,
          fixture: "fix1",
          face: 1
        )
        .MoveToMachineQueue(pal: 2, mach: 3)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 1234 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
        )})
        .MoveToBuffer(pal: 2, buff: 2)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
          })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[]{
          FakeIccDsl.ExpectNewRoute(
            pal: 2,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 1234 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })
        .ExpectNoChanges()
        .MoveToLoad(pal: 1, lul: 1)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 1) })
        .AdvanceMinutes(4) // =4
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
        .ExpectNoChanges()
        .MoveToMachineQueue(pal: 1, mach: 3)
        .AdvanceMinutes(6) // =10
        .SetBeforeMC(pal: 1)
        .ExpectNoChanges()
        .MoveToMachine(pal: 1, mach: 3)
        .ExpectNoChanges()
        .StartMachine(mach: 3, program: 1234)
        .UpdateExpectedMaterial(fstMats, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Machining;
            im.Action.Program = "1234";
            im.Action.ElapsedMachiningTime = TimeSpan.Zero;
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 3, program: 1234, mat: fstMats)
        })
        .AdvanceMinutes(10) // =20
        .UpdateExpectedMaterial(fstMats, im =>
          {
            im.Action.ElapsedMachiningTime = TimeSpan.FromMinutes(10);
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(4);
          }
        )
        .ExpectNoChanges()
        .AdvanceMinutes(5) // =25
        .EndMachine(mach: 3)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(fstMats, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Waiting;
            im.Action.Program = null;
            im.Action.ElapsedMachiningTime = null;
            im.Action.ExpectedRemainingMachiningTime = null;
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 3, program: 1234, elapsedMin: 15, activeMin: 14, mats: fstMats)
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
        .ExpectNoChanges()
        .AdvanceMinutes(3) //=28
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 4),
          FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 2)
        })
        .AdvanceMinutes(2) // =30
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
        })
        .RemoveExpectedMaterial(fstMats.Select(m => m.MaterialID))
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 30 - 4),
          FakeIccDsl.UnloadFromFace(pal: 1, lul: 4, elapsedMin: 2, activeMins: 9, mats: fstMats),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 4, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 2, activeMins: 8, mats: out var sndMats)
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectNoChanges()
      ;
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
          pals: new[] { 1 },
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          prog: 1234,
          loadMins: 8,
          unloadMins: 9,
          machMins: 14,
          fixture: "fix1",
          face: 1
        )
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
        })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 1234 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })

        //should set new route if loads, machines, or progs differ
        .OverrideRoute(pal: 1, comment: "abc", noWork: true, luls: new[] { 100, 200 }, machs: new[] { 5, 6 }, progs: new[] { 1234 })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectNewRoute(pal: 1, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 }, faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) })
        })
        .OverrideRoute(pal: 1, comment: "abc", noWork: true, luls: new[] { 3, 4 }, machs: new[] { 500, 600 }, progs: new[] { 1234 })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectNewRoute(pal: 1, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 }, faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) })
        })
        .OverrideRoute(pal: 1, comment: "abc", noWork: true, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 12345 })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectNewRoute(pal: 1, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 }, faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) })
        })

        // back to correct, just increment
        .OverrideRoute(pal: 1, comment: "abc", noWork: true, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectRouteIncrement(pal: 1, newCycleCnt: 1)
        })
        ;
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
          loadMins: 8,
          unloadMins: 9,
          machMins: 14,
          fixture: "fix1",
          face: 1,
          queue: "thequeue"
        )
        .ExpectNoChanges()

        .AddUnallocatedCasting(queue: "thequeue", part: "part4", numProc: 1, mat: out var unusedMat)
        .ExpectNoChanges()

        .AddUnallocatedCasting(queue: "thequeue", part: "part1", numProc: 1, mat: out var queuedMat)
        .UpdateExpectedMaterial(queuedMat.MaterialID, m =>
        {
          m.JobUnique = "uniq1";
          m.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPallet = 1.ToString(),
            LoadOntoFace = 1,
            ProcessAfterLoad = 1,
            PathAfterLoad = 1
          };
        })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectNewRoute(pal: 1, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 }, faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) })
        })
        .MoveToLoad(pal: 1, lul: 3)
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3)
        })
        .AdvanceMinutes(3) // = 3min
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
          _dsl.LoadToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", elapsedMin: 3, activeMins: 8, loadingMats: new[] {queuedMat}, loadedMats: out var mat1),
          FakeIccDsl.RemoveFromQueue("thequeue", 1, mat1)
        })
        ;
    }

    [Fact]
    public void MultiProcSamePallet()
    {
      _dsl
        .AddMultiProcSamePalletJob(
          unique: "uniq1",
          part: "part1",
          qty: 3,
          priority: 5,
          partsPerPal: 1,
          pals: new[] { 1 },
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          prog1: 1234,
          prog2: 9876,
          loadMins1: 8,
          unloadMins1: 9,
          machMins1: 14,
          loadMins2: 10,
          unloadMins2: 11,
          machMins2: 15,
          fixture: "fix1",
          face1: 1,
          face2: 2
        )

        // process 1 only cycle
        .SetExpectedLoadCastings(new[] {
              (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
          FakeIccDsl.ExpectNewRoute(
            pal: 1,
            luls: new[] { 3, 4 },
            machs: new[] { 5, 6 },
            progs: new[] { 1234 },
            faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
          )
        })
        .MoveToLoad(pal: 1, lul: 3)
        .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
        .AdvanceMinutes(4) // =4
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ClearExpectedLoadCastings()
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectPalletCycle(pal: 1, mins: 0),
          FakeIccDsl.LoadCastingToFace(pal: 1, lul: 3, face: 1, unique: "uniq1", path: 1, cnt: 1, elapsedMin: 4, activeMins: 8, mats: out var AAAproc1)
        })
        .MoveToBuffer(pal: 1, buff: 1)
        .ExpectNoChanges()
        .MoveToMachineQueue(pal: 1, mach: 3)
        .AdvanceMinutes(6) // =10
        .SetBeforeMC(pal: 1)
        .ExpectNoChanges()
        .MoveToMachine(pal: 1, mach: 6)
        .ExpectNoChanges()
        .StartMachine(mach: 6, program: 1234)
        .UpdateExpectedMaterial(AAAproc1, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Machining;
            im.Action.Program = "1234";
            im.Action.ElapsedMachiningTime = TimeSpan.Zero;
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
          }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: 1234, mat: AAAproc1) })
        .AdvanceMinutes(15) // =25
        .EndMachine(mach: 6)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(AAAproc1, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Waiting;
            im.Action.Program = null;
            im.Action.ElapsedMachiningTime = null;
            im.Action.ExpectedRemainingMachiningTime = null;
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: 1234, elapsedMin: 15, activeMin: 14, mats: AAAproc1)
        })

        // now a cycle with process 1 and 2
        .SetBeforeUnload(pal: 1)
        .MoveToLoad(pal: 1, lul: 4)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
         .UpdateExpectedMaterial(AAAproc1, im =>
         {
           im.Action.Type = InProcessMaterialAction.ActionType.Loading;
           im.Action.LoadOntoPallet = "1";
           im.Action.ProcessAfterLoad = 2;
           im.Action.PathAfterLoad = 1;
           im.Action.LoadOntoFace = 2;
         })
         .ExpectTransition(new[] {
            FakeIccDsl.ExpectNewRoute(
              pal: 1,
              luls: new[] { 3, 4 },
              machs: new[] { 5, 6 },
              progs: new[] { 1234, 9876 },
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
        .StartMachine(mach: 5, program: 1234)
        .UpdateExpectedMaterial(BBBproc1, im =>
          {
            im.Action.Type = InProcessMaterialAction.ActionType.Machining;
            im.Action.Program = "1234";
            im.Action.ElapsedMachiningTime = TimeSpan.Zero;
            im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
          }
        )
        .ExpectTransition(new[] { FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: 1234, mat: BBBproc1) })
        .AdvanceMinutes(20) // = 67min
        .StartMachine(mach: 5, program: 9876)
        .UpdateExpectedMaterial(BBBproc1, im =>
          {
            im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          }
        )
        .UpdateExpectedMaterial(AAAproc2, im =>
        {
          im.Action.Type = InProcessMaterialAction.ActionType.Machining;
          im.Action.Program = "9876";
          im.Action.ElapsedMachiningTime = TimeSpan.Zero;
          im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(15);
        })
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: 1234, elapsedMin: 20, activeMin: 14, mats: BBBproc1),
          FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 5, program: 9876, mat: AAAproc2)
        })
        .AdvanceMinutes(30) // = 97min
        .EndMachine(mach: 5)
        .SetAfterMC(pal: 1)
        .UpdateExpectedMaterial(AAAproc2, im =>
          {
            im.Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting };
          }
        )
        .ExpectTransition(new[] {
          FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 5, program: 9876, elapsedMin: 30, activeMin: 15, mats: AAAproc2)
        })

        .MoveToLoad(pal: 1, lul: 4)
        .SetBeforeUnload(pal: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
         .UpdateExpectedMaterial(BBBproc1, im =>
         {
           im.Action = new InProcessMaterialAction()
           {
             Type = InProcessMaterialAction.ActionType.Loading,
             LoadOntoFace = 2,
             LoadOntoPallet = "1",
             ProcessAfterLoad = 2,
             PathAfterLoad = 1
           };
         })
         .UpdateExpectedMaterial(AAAproc2, im =>
         {
           im.Action = new InProcessMaterialAction()
           {
             Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
           };
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

        ;

    }

    [Fact]
    public void MultipleProcessSeparatePallets()
    {
      _dsl.AddMultiProcSeparatePalletJob(
        unique: "uniq1",
        part: "part1",
        qty: 3,
        priority: 5,
        partsPerPal: 1,
        pals1: new[] { 1 },
        pals2: new[] { 2, 3 },
        luls: new[] { 3, 4 },
        machs: new[] { 5, 6 },
        prog1: 1234,
        prog2: 9876,
        loadMins1: 8,
        unloadMins1: 9,
        machMins1: 14,
        machMins2: 10,
        loadMins2: 11,
        unloadMins2: 12,
        fixture: "fix1",
        queue: "qqq"
      )
      .SetExpectedLoadCastings(new[] {
            (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
      })
      .ExpectTransition(expectedUpdates: false, expectedChanges: new[] {
        FakeIccDsl.ExpectNewRoute(
          pal: 1,
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          progs: new[] { 1234 },
          faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
        )
      })
      .MoveToLoad(pal: 1, lul: 3)
      .ExpectTransition(new[] { FakeIccDsl.ExpectLoadBegin(pal: 1, lul: 3) })
      .AdvanceMinutes(2) // = 2min
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
      .StartMachine(mach: 6, program: 1234)
      .UpdateExpectedMaterial(AAAproc1, im =>
      {
        im.Action.Type = InProcessMaterialAction.ActionType.Machining;
        im.Action.Program = "1234";
        im.Action.ElapsedMachiningTime = TimeSpan.Zero;
        im.Action.ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(14);
      })
      .ExpectTransition(new[] { FakeIccDsl.ExpectMachineBegin(pal: 1, machine: 6, program: 1234, mat: AAAproc1) })
      .AdvanceMinutes(10) // = 12min
      .EndMachine(mach: 6)
      .SetAfterMC(pal: 1)
      .UpdateExpectedMaterial(AAAproc1, im =>
      {
        im.Action.Type = InProcessMaterialAction.ActionType.Waiting;
        im.Action.Program = null;
        im.Action.ElapsedMachiningTime = null;
        im.Action.ExpectedRemainingMachiningTime = null;
      })
      .ExpectTransition(new[] {
        FakeIccDsl.ExpectMachineEnd(pal: 1, mach: 6, program: 1234, elapsedMin: 10, activeMin: 14, mats: AAAproc1)
      })
      .SetBeforeUnload(pal: 1)
      .MoveToLoad(pal: 1, lul: 4)
      .SetExpectedLoadCastings(new[] {
            (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1)
      })
      .UpdateExpectedMaterial(AAAproc1, im =>
      {
        im.Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
          UnloadIntoQueue = "qqq"
        };
      })
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
          progs: new[] { 9876 },
          faces: new[] { (face: 1, unique: "uniq1", proc: 2, path: 1) }
        )
      })
      .MoveToBuffer(pal: 1, buff: 1)
      .SetBeforeMC(pal: 1)
      .MoveToLoad(pal: 2, lul: 3)
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
        FakeIccDsl.RemoveFromQueue("qqq", 0, AAAproc2)
      })
      ;

    }

    [Fact(Skip = "Pending")]
    public void CountsCompletedFromLog()
    {

    }

    [Fact(Skip = "Pending")]
    public void JobPriority()
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
}