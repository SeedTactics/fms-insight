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
    private JobLogDB _logDB;
    private AssignPallets _assign;

    public NiigataAssignmentSpec()
    {
      var logConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      logConn.Open();
      _logDB = new JobLogDB(new FMSSettings(), logConn);
      _logDB.CreateTables(firstSerialOnEmpty: null);

      _assign = new AssignPallets(_logDB);
    }

    void IDisposable.Dispose()
    {
      _logDB.Close();
    }

    [Fact]
    public void OneJob()
    {
      var curSch = NewSch(new[] {
        OneProcOnePathJob(
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
      });

      var existingPals = new List<PalletAndMaterial> {
        EmptyPalletInBuffer(pal: 1),
        EmptyPalletInBuffer(pal: 2),
      };

      _assign.NewPalletChange(existingPals, curSch).Should().BeEquivalentTo<NewPalletRoute>(
        NewRoute(
          pal: 1,
          comment: "part1-1",
          luls: new[] { 3, 4 },
          machs: new[] { 5, 6 },
          progs: new[] { 1234 }
        )
      );
    }

    #region Existing Pallets
    private static PalletAndMaterial EmptyPalletInBuffer(int pal)
    {
      return new PalletAndMaterial()
      {
        Pallet = new PalletStatus()
        {
          Master = new PalletMaster()
          {
            PalletNum = pal,
            NoWork = true
          },
          CurStation = NiigataStationNum.Buffer(pal)
        },
        Material = new Dictionary<int, IReadOnlyList<InProcessMaterial>>()
      };
    }
    #endregion

    #region Expected actions
    private static NewPalletRoute NewRoute(int pal, string comment, int[] luls, int[] machs, int[] progs)
    {
      return new NewPalletRoute()
      {
        NewMaster = new PalletMaster()
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
        }

      };
    }
    #endregion

    #region Job Definitions
    private static JobPlan OneProcOnePathJob(string unique, string part, int qty, int priority, int partsPerPal, int[] pals, int[] luls, int[] machs, int prog, string fixture, int face)
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
      return j;
    }

    private static PlannedSchedule NewSch(IEnumerable<JobPlan> jobs)
    {
      return new PlannedSchedule()
      {
        Jobs = jobs.ToList()
      };
    }
    #endregion

  }
}