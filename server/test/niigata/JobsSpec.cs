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
using NSubstitute;
using System.Collections.Immutable;
using Germinate;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class NiigataJobSpec : IDisposable
  {
    private RepositoryConfig _repoCfg;
    private IRepository _logDB;
    private NiigataJobs _jobs;
    private ISyncPallets _syncMock;

    public NiigataJobSpec()
    {
      _repoCfg = RepositoryConfig.InitializeSingleThreadedMemoryDB(new SerialSettings());
      _logDB = _repoCfg.OpenConnection();

      _syncMock = Substitute.For<ISyncPallets>();

      var settings = new FMSSettings();
      settings.Queues.Add("q1", new QueueSize());
      settings.Queues.Add("q2", new QueueSize());

      _jobs = new NiigataJobs(_repoCfg, settings, _syncMock, null, true, null);
    }

    void IDisposable.Dispose()
    {
      _repoCfg.CloseMemoryConnection();
    }

    [Fact]
    public void AddsBasicJobs()
    {
      //add some existing jobs
      var completedJob = FakeIccDsl.CreateMultiProcSamePalletJob(
        unique: "old",
        part: "oldpart",
        qty: 3, // has quantity 3!
        priority: 4,
        partsPerPal: 1,
        pals: new[] { 6000 },
        luls: new[] { 1 },
        machs: new[] { 1 },
        prog1: "oldprog1",
        prog1Rev: null,
        prog2: "oldprog2",
        prog2Rev: null,
        loadMins1: 56,
        machMins1: 53,
        unloadMins1: 4,
        loadMins2: 4,
        machMins2: 2,
        unloadMins2: 2566,
        fixture: "oldfixture",
        face1: 1,
        face2: 2
      ).Item1;

      var completedActive = completedJob.CloneToDerived<ActiveJob, Job>() with { RemainingToStart = 0 };
      var toKeepJob = completedJob.CloneToDerived<ActiveJob, Job>() with { UniqueStr = "tokeep", Cycles = 10, RemainingToStart = 5 };

      _logDB.AddJobs(new NewJobs() { ScheduleId = "old", Jobs = ImmutableList.Create<Job>(completedJob, toKeepJob) }, null, addAsCopiedToSystem: true);

      _syncMock.CurrentCellState().Returns(new CellState()
      {
        Pallets = new List<PalletAndMaterial>(),
        QueuedMaterial = new List<InProcessMaterialAndJob>(),
        CurrentStatus = new CurrentStatus()
        {
          Jobs = ImmutableDictionary<string, ActiveJob>.Empty.Add(completedActive.UniqueStr, completedActive).Add(toKeepJob.UniqueStr, toKeepJob)
        }
      });

      //some new jobs
      var newJob1 = FakeIccDsl.CreateOneProcOnePathJob(
        unique: "uu1",
        part: "p1",
        qty: 12,
        priority: 7,
        partsPerPal: 1,
        pals: new[] { 777 },
        luls: new[] { 2 },
        machs: new[] { 5 },
        prog: "prog1",
        progRev: null,
        loadMins: 54,
        machMins: 64,
        unloadMins: 5,
        fixture: "xxxx",
        face: 1
      ).Item1;
      var newJob2 = FakeIccDsl.CreateOneProcOnePathJob(
        unique: "uu2",
        part: "p2",
        qty: 35,
        priority: 7,
        partsPerPal: 1,
        pals: new[] { 87 },
        luls: new[] { 2 },
        machs: new[] { 5 },
        prog: "prog2",
        progRev: null,
        loadMins: 632,
        machMins: 676,
        unloadMins: 5,
        fixture: "fix",
        face: 1
      ).Item1;
      var newJobs = new NewJobs()
      {
        ScheduleId = "abcd",
        Jobs = ImmutableList.Create<Job>(newJob1, newJob2),
        Programs = ImmutableList.Create(
          new NewProgramContent()
          {
            ProgramName = "prog1",
            ProgramContent = "content1"
          },
          new NewProgramContent()
          {
            ProgramName = "prog2",
            ProgramContent = "content2"
          })
      };

      ((IJobControl)_jobs).AddJobs(newJobs, null, false);

      _logDB.LoadUnarchivedJobs().Select(j => j.UniqueStr).Should().BeEquivalentTo(new[] { toKeepJob.UniqueStr, newJob1.UniqueStr, newJob2.UniqueStr });
      _logDB.LoadJob("old").Archived.Should().BeTrue();

      _syncMock.Received().JobsOrQueuesChanged();
    }

    [Fact]
    public void Decrement()
    {
      var now = DateTime.UtcNow.AddHours(-1);

      _logDB.AddNewDecrement(new[] {
        new NewDecrementQuantity()
        {
          JobUnique = "u1",
          Part = "p1",
          Quantity = 10
        },
        new NewDecrementQuantity()
        {
          JobUnique = "u2",
          Part = "p2",
          Quantity = 5
        }
      }, now);

      var expected = new[] {
        new JobAndDecrementQuantity()
        {
          DecrementId = 0,
          JobUnique = "u1",
          Part = "p1",
          Quantity = 10,
          TimeUTC = now
        },
        new JobAndDecrementQuantity()
        {
          DecrementId = 0,
          JobUnique = "u2",
          Part = "p2",
          Quantity = 5,
          TimeUTC = now
        }
      };

      ((IJobControl)_jobs).DecrementJobQuantites(-1).Should().BeEquivalentTo(expected);
      _syncMock.Received().DecrementPlannedButNotStartedQty(Arg.Any<IRepository>());

      _syncMock.ClearReceivedCalls();

      ((IJobControl)_jobs).DecrementJobQuantites(DateTime.UtcNow.AddHours(-5)).Should().BeEquivalentTo(expected);
      _syncMock.Received().DecrementPlannedButNotStartedQty(Arg.Any<IRepository>());
    }

  }
}