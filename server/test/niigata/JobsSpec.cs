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
  public class NiigataJobSpec
  {
    private JobDB _jobDB;
    private JobLogDB _logDB;
    private NiigataJobs _jobs;
    private ISyncPallets _syncMock;
    private IBuildCurrentStatus _curStMock;

    public NiigataJobSpec()
    {
      var logConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      logConn.Open();
      _logDB = new JobLogDB(new FMSSettings(), logConn);
      _logDB.CreateTables(firstSerialOnEmpty: null);

      var jobConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      jobConn.Open();
      _jobDB = new JobDB(jobConn);
      _jobDB.CreateTables();

      _syncMock = Substitute.For<ISyncPallets>();
      _curStMock = Substitute.For<IBuildCurrentStatus>();

      var settings = new FMSSettings();
      settings.Queues.Add("q1", new QueueSize());

      _jobs = new NiigataJobs(_jobDB, _logDB, settings, _syncMock, _curStMock);
    }

    [Fact]
    public void LoadsCurrentSt()
    {
      var c = new CurrentStatus();
      c.Pallets.Add("1", new PalletStatus());
      _curStMock.GetCurrentStatus().Returns(c);
      _jobs.GetCurrentStatus().Should().Be(c);
      _curStMock.Received().GetCurrentStatus();
    }

    [Fact]
    public void AddsBasicJobs()
    {
      var c = new CurrentStatus();
      c.Pallets.Add("1", new PalletStatus());
      _curStMock.GetCurrentStatus().Returns(c);

      //add a completed job
      var completedJob = new JobPlan("old", 1);
      completedJob.PartName = "oldpart";
      _jobDB.AddJobs(new NewJobs() { ScheduleId = "old", Jobs = new List<JobPlan> { completedJob } }, null);
      _curStMock.IsJobCompleted(Arg.Is<JobPlan>(j => j.UniqueStr == "old")).Returns(true);

      //some new jobs
      var newJob1 = new JobPlan("uu1", 2);
      newJob1.PartName = "p1";
      newJob1.SetPlannedCyclesOnFirstProcess(1, 10);
      var newJob2 = new JobPlan("uu2", 1);
      newJob2.PartName = "p2";
      newJob2.AddProcessOnPallet(process: 1, path: 1, pallet: "4");

      var newJobs = new NewJobs()
      {
        ScheduleId = "abcd",
        ArchiveCompletedJobs = true,
        Jobs = new List<JobPlan> { newJob1, newJob2 }
      };


      using (var monitor = _jobs.Monitor())
      {
        _jobs.AddJobs(newJobs, null);

        _jobDB.LoadUnarchivedJobs().Jobs.Should().BeEquivalentTo(new[] { newJob1, newJob2 });
        _jobDB.LoadJob("old").Archived.Should().BeTrue();

        _syncMock.Received().RecheckPallets();
        monitor.Should().Raise("OnNewCurrentStatus").WithArgs<CurrentStatus>(r => r == c);
      }
    }

    [Fact]
    public void Decrement()
    {
      var now = DateTime.UtcNow.AddHours(-1);

      _jobDB.AddNewDecrement(new[] {
        new JobDB.NewDecrementQuantity()
        {
          JobUnique = "u1",
          Part = "p1",
          Quantity = 10
        },
        new JobDB.NewDecrementQuantity()
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

      _jobs.DecrementJobQuantites(-1).Should().BeEquivalentTo(expected);
      _syncMock.Received().DecrementPlannedButNotStartedQty();

      _syncMock.ClearReceivedCalls();

      _jobs.DecrementJobQuantites(DateTime.UtcNow.AddHours(-5)).Should().BeEquivalentTo(expected);
      _syncMock.Received().DecrementPlannedButNotStartedQty();
    }

    [Fact]
    public void UnallocatedQueues()
    {
      var c = new CurrentStatus();
      c.Pallets.Add("1", new PalletStatus());
      _curStMock.GetCurrentStatus().Returns(c);

      //add a casting
      using (var monitor = _jobs.Monitor())
      {
        _jobs.AddUnallocatedCastingToQueue("p1", "q1", position: 0, serial: "aaa");
        _logDB.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
        {
          MaterialID = 1,
          PartName = "p1",
          NumProcesses = 1,
          Serial = "aaa"
        });

        _logDB.GetMaterialInQueue("q1").Should().BeEquivalentTo(new[] {
          new JobLogDB.QueuedMaterial()
          { MaterialID = 1, NumProcesses = 1, PartName = "p1", Position = 0, Queue = "q1", Unique = ""}
        });

        _syncMock.Received().RecheckPallets();
        monitor.Should().Raise("OnNewCurrentStatus").WithArgs<CurrentStatus>(r => r == c);
        monitor.Clear();
        _syncMock.ClearReceivedCalls();

        _jobs.RemoveMaterialFromAllQueues(1);
        _logDB.GetMaterialInQueue("q1").Should().BeEmpty();

        _syncMock.Received().RecheckPallets();
        monitor.Should().Raise("OnNewCurrentStatus").WithArgs<CurrentStatus>(r => r == c);
        monitor.Clear();
        _syncMock.ClearReceivedCalls();
      }
    }

    [Fact]
    public void AllocatedQueues()
    {
      var c = new CurrentStatus();
      c.Pallets.Add("1", new PalletStatus());
      _curStMock.GetCurrentStatus().Returns(c);

      var job = new JobPlan("uuu1", 2);
      job.PartName = "p1";
      _jobDB.AddJobs(new NewJobs() { ScheduleId = "abcd", Jobs = new List<JobPlan> { job } }, null);

      //add an allocated material
      using (var monitor = _jobs.Monitor())
      {
        _jobs.AddUnprocessedMaterialToQueue("uuu1", 1, "q1", 0, "aaa");
        _logDB.GetMaterialDetails(1).Should().BeEquivalentTo(new MaterialDetails()
        {
          MaterialID = 1,
          PartName = "p1",
          NumProcesses = 2,
          Serial = "aaa",
          JobUnique = "uuu1"
        });

        _logDB.GetMaterialInQueue("q1").Should().BeEquivalentTo(new[] {
          new JobLogDB.QueuedMaterial()
          { MaterialID = 1, NumProcesses = 2, PartName = "p1", Position = 0, Queue = "q1", Unique = "uuu1"}
        });

        _syncMock.Received().RecheckPallets();
        monitor.Should().Raise("OnNewCurrentStatus").WithArgs<CurrentStatus>(r => r == c);
        monitor.Clear();
        _syncMock.ClearReceivedCalls();

        //remove it
        _jobs.RemoveMaterialFromAllQueues(1);
        _logDB.GetMaterialInQueue("q1").Should().BeEmpty();

        _syncMock.Received().RecheckPallets();
        monitor.Should().Raise("OnNewCurrentStatus").WithArgs<CurrentStatus>(r => r == c);
        monitor.Clear();
        _syncMock.ClearReceivedCalls();

        //add it back in
        _jobs.SetMaterialInQueue(1, "q1", 0);
        _logDB.GetMaterialInQueue("q1").Should().BeEquivalentTo(new[] {
          new JobLogDB.QueuedMaterial()
          { MaterialID = 1, NumProcesses = 2, PartName = "p1", Position = 0, Queue = "q1", Unique = "uuu1"}
        });

        _syncMock.Received().RecheckPallets();
        monitor.Should().Raise("OnNewCurrentStatus").WithArgs<CurrentStatus>(r => r == c);
        monitor.Clear();
        _syncMock.ClearReceivedCalls();
      }

    }
  }
}