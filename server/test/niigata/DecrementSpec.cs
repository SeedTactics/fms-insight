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
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class NiigataDecrementSpec : IDisposable
  {
    private JobDB _jobDB;
    private EventLogDB _logDB;

    public NiigataDecrementSpec()
    {
      var logCfg = EventLogDB.Config.InitializeSingleThreadedMemoryDB(new FMSSettings());
      _logDB = logCfg.OpenConnection();

      var jobDbCfg = JobDB.Config.InitializeSingleThreadedMemoryDB();
      _jobDB = jobDbCfg.OpenConnection();
    }

    void IDisposable.Dispose()
    {
      _logDB.Close();
      _jobDB.Close();
    }

    [Fact]
    public void Decrements()
    {
      var qtyRemaining = new Dictionary<(string uniq, int proc1path), int>();

      var j1 = new JobPlan("u1", numProcess: 2, numPaths: new[] { 2, 1 });
      qtyRemaining[(uniq: "u1", proc1path: 1)] = 5;
      qtyRemaining[(uniq: "u1", proc1path: 2)] = 7;

      var j2 = new JobPlan("u2", numProcess: 1);
      qtyRemaining[(uniq: "u2", proc1path: 1)] = 0;

      var d = new DecrementNotYetStartedJobs();

      for (int i = 0; i < 2; i++)
      {
        // decrement twice, the second time keeps everything unchanged

        d.DecrementJobs(_jobDB, _logDB,
          new CellState()
          {
            UnarchivedJobs = new[] { j1, j2 },
            JobQtyRemainingOnProc1 = qtyRemaining
          }
        ).Should().Be(i == 0); // only something changed first time

        _jobDB.LoadDecrementsForJob("u1").Should().BeEquivalentTo(
          new[] {
          new DecrementQuantity() {
            DecrementId = 0, TimeUTC = DateTime.UtcNow, Quantity = 5 + 7
          }
          },
          options => options
            .Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, precision: 4000))
            .WhenTypeIs<DateTime>()
        );

        _jobDB.LoadDecrementsForJob("u2").Should().BeEmpty();
      }
    }

  }
}
