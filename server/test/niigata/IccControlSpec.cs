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
          loadMins: 8,
          unloadMins: 9,
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
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
         })
        .ExpectNewRoute(
          pal: 2,
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          progs: new[] { 1234 },
          faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
        )
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 1, path: 1, face: 1),
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
         })
        .ExpectNoChanges()
        .MoveToLoad(pal: 1, lul: 1)
        .ExpectLoadBeginEvt(pal: 1, lul: 1)
        .AdvanceMinutes(4) // =4
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
         })
        .ExpectLoadEndEvt(pal: 1, lul: 1, elapsedMin: 4, palMins: 0, expectedEvts: new[] {
          FakeIccDsl.LoadCastingToFace(face: 1, unique: "uniq1", proc: 1, path: 1, cnt: 1, activeMins: 8, mats: out var fstMats)
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
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
        })
        .ExpectRouteIncrementAndLoadBegin(pal: 1, lul: 4)
        .AdvanceMinutes(2) // =30
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .SetExpectedLoadCastings(new[] {
          (uniq: "uniq1", part: "part1", pal: 2, path: 1, face: 1),
        })
        .RemoveExpectedMaterial(fstMats.Select(m => m.MaterialID))
        .ExpectLoadEndEvt(pal: 1, lul: 4, elapsedMin: 2, palMins: 30 - 4, expectedEvts: new[] {
          FakeIccDsl.UnloadFromFace(face: 1, unique: "uniq1", proc: 1, path: 1, activeMins: 9, mats: fstMats),
          FakeIccDsl.LoadCastingToFace(face: 1, unique: "uniq1", proc: 1, path: 1, cnt: 1, activeMins: 8, mats: out var sndMats)
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
        .ExpectNewRoute(
          pal: 1,
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          progs: new[] { 1234 },
          faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) }
        )

        //should set new route if loads, machines, or progs differ
        .OverrideRoute(pal: 1, comment: "abc", noWork: true, luls: new[] { 100, 200 }, machs: new[] { 5, 6 }, progs: new[] { 1234 })
        .ExpectNewRoute(pal: 1, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 }, faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) })
        .OverrideRoute(pal: 1, comment: "abc", noWork: true, luls: new[] { 3, 4 }, machs: new[] { 500, 600 }, progs: new[] { 1234 })
        .ExpectNewRoute(pal: 1, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 }, faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) })
        .OverrideRoute(pal: 1, comment: "abc", noWork: true, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 12345 })
        .ExpectNewRoute(pal: 1, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 }, faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) })

        // back to correct, just increment
        .OverrideRoute(pal: 1, comment: "abc", noWork: true, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 })
        .ExpectRouteIncrement(pal: 1, newCycleCnt: 1)
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

        .AddUnallocatedCasting(queue: "thequeue", part: "part4", numProc: 1, matId: out long unusedMat)
        .ExpectNoChanges()

        .AddUnallocatedCasting(queue: "thequeue", part: "part1", numProc: 1, matId: out long matId1)
        .ExpectNewRoute(pal: 1, luls: new[] { 3, 4 }, machs: new[] { 5, 6 }, progs: new[] { 1234 }, faces: new[] { (face: 1, unique: "uniq1", proc: 1, path: 1) })
        .UpdateExpectedMaterial(matId1, m =>
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
        .ExpectNoChanges() // should not set route on pal 2 since already allocated to pallet 1

        .MoveToLoad(pal: 1, lul: 3)
        .ExpectLoadBeginEvt(pal: 1, lul: 3)
        .AdvanceMinutes(3) // = 3min
        .ExpectNoChanges()
        .SetAfterLoad(pal: 1)
        .ExpectLoadEndEvt(pal: 1, lul: 3, elapsedMin: 3, palMins: 0, expectedEvts: new[] {
          _dsl.LoadToFace(face: 1, unique: "uniq1", part: "part1", numProc: 1, proc: 1, path: 1, activeMins: 8, fromQueue: true, matIds: new[] {matId1}, mats: out var mat1)
        })
        ;
    }

    /*
      [Fact]
      public void MultipleAvailablePallets()
      {
        //Currently, if multiple pallets can satisfy some queued material, currently don't check
        //that pallet moving to load station has "acquired" that material.
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
}