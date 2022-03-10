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
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using System.Collections.Immutable;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class NiigataDecrementSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _jobDB;

    public NiigataDecrementSpec()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _jobDB = _repoCfg.OpenConnection();
    }

    void IDisposable.Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    [Fact]
    public void Decrements()
    {
      var qtyStarted = new Dictionary<string, int>();

      var j1 = new HistoricJob()
      {
        UniqueStr = "u1",
        Cycles = 12,
        Processes = ImmutableList.Create(
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(new ProcPathInfo(), new ProcPathInfo())
          },
          new ProcessInfo()
          {
            Paths = ImmutableList.Create(new ProcPathInfo())
          }
        )
      };
      qtyStarted["u1"] = 5;

      var j2 = new HistoricJob()
      {
        UniqueStr = "u2",
        Cycles = 22,
        Processes = ImmutableList.Create(new ProcessInfo() { Paths = ImmutableList.Create(new ProcPathInfo()) }),
      };
      qtyStarted["u2"] = 22;

      var d = new DecrementNotYetStartedJobs();

      for (int i = 0; i < 2; i++)
      {
        // decrement twice, the second time keeps everything unchanged

        d.DecrementJobs(_jobDB,
          new CellState()
          {
            UnarchivedJobs = new[] { j1, j2 },
            CyclesStartedOnProc1 = qtyStarted
          }
        ).Should().Be(i == 0); // only something changed first time

        j1 = j1 with
        {
          Decrements = ImmutableList.Create(
            new[] {
              new DecrementQuantity() {
                DecrementId = 0, TimeUTC = DateTime.UtcNow, Quantity = 12 - 5
              }
            }
          )
        };

        _jobDB.LoadDecrementsForJob("u1").Should().BeEquivalentTo(
          j1.Decrements,
          options => options
            .ComparingByMembers<DecrementQuantity>()
            .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: TimeSpan.FromSeconds(4)))
            .WhenTypeIs<DateTime>()
        );

        _jobDB.LoadDecrementsForJob("u2").Should().BeEmpty();
      }
    }
  }
}