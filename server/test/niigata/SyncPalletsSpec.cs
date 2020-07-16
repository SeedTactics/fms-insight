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
using BlackMaple.MachineFramework;
using FluentAssertions;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class SyncPalletsSpec : IDisposable
  {
    private FMSSettings _settings;
    private JobLogDB _logDB;
    private JobDB _jobDB;
    private IAssignPallets _assign;
    private CreateCellState _createLog;
    private IccSimulator _sim;
    private SyncPallets _sync;
    private Xunit.Abstractions.ITestOutputHelper _output;
    private bool _debugLogEnabled = false;

    public SyncPalletsSpec(Xunit.Abstractions.ITestOutputHelper o)
    {
      _output = o;
      _settings = new FMSSettings()
      {
        SerialType = SerialType.AssignOneSerialPerMaterial,
        ConvertMaterialIDToSerial = FMSSettings.ConvertToBase62,
        ConvertSerialToMaterialID = FMSSettings.ConvertFromBase62
      };

      var logConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      logConn.Open();
      _logDB = new JobLogDB(_settings, logConn);
      _logDB.CreateTables(firstSerialOnEmpty: null);

      var jobConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      jobConn.Open();
      _jobDB = new JobDB(jobConn);
      _jobDB.CreateTables();

      var record = new RecordFacesForPallet(_logDB);

      var machConn = NSubstitute.Substitute.For<ICncMachineConnection>();

      var statNames = new NiigataStationNames()
      {
        ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
        IccMachineToJobMachNames = Enumerable.Range(1, 6).ToDictionary(mc => mc, mc => (group: "MC", num: mc))
      };

      _assign = new MultiPalletAssign(new IAssignPallets[] {
        new AssignNewRoutesOnPallets(record, statNames),
        new SizedQueues(new Dictionary<string, QueueSize>() {
          {"sizedQ", new QueueSize() { MaxSizeBeforeStopUnloading = 1}}
        })
      });
      _createLog = new CreateCellState(_logDB, _jobDB, record, _settings, statNames, machConn);

      _sim = new IccSimulator(numPals: 10, numMachines: 6, numLoads: 2);
      _sync = new SyncPallets(_jobDB, _logDB, _sim, _assign, _createLog);

      _sim.OnNewProgram += (newprog) =>
        _jobDB.SetCellControllerProgramForProgram(newprog.ProgramName, newprog.ProgramRevision, newprog.ProgramNum.ToString());
    }

    public void Dispose()
    {
      _sync.Dispose();
      _logDB.Close();
      _jobDB.Close();
    }

    private IEnumerable<LogEntry> Step()
    {
      if (!_sim.Step())
      {
        return null;
      }
      using (var syncMonitor = _sync.Monitor())
      using (var logMonitor = _logDB.Monitor())
      {
        _sync.SynchronizePallets(false);
        var evts = logMonitor.OccurredEvents.Where(e => e.EventName == "NewLogEntry").Select(e => e.Parameters[0]).Cast<LogEntry>();
        if (evts.Any(e => e.LogType != LogType.PalletInStocker && e.LogType != LogType.PalletOnRotaryInbound))
        {
          syncMonitor.Should().Raise("OnPalletsChanged");
        }
        return evts;
      }
    }

    private IEnumerable<LogEntry> Run()
    {
      var logs = new List<LogEntry>();
      while (true)
      {
        var newLogs = Step();
        if (newLogs != null)
        {
          logs.AddRange(newLogs);
        }
        else
        {
          break;
        }
        if (_debugLogEnabled)
        {
          if (newLogs.Any())
          {
            _output.WriteLine("");
            _output.WriteLine("*************** Events *********************************");
            _output.WriteLine("Events: ");
            WriteLogs(newLogs);
          }
          _output.WriteLine("");
          _output.WriteLine("--------------- Status ---------------------------------");
          _output.WriteLine(_sim.DebugPrintStatus());
        }
      }
      return logs;
    }

    private void WriteLogs(IEnumerable<MachineWatchInterface.LogEntry> es)
    {
      var output = new System.Text.StringBuilder();
      foreach (var e in es)
      {
        Action writeMat = () => output.AppendJoin(',', e.Material.Select(m => m.PartName + "-" + m.Process.ToString() + "[" + m.MaterialID.ToString() + "]"));
        switch (e.LogType)
        {
          case MachineWatchInterface.LogType.LoadUnloadCycle:
            if (e.StartOfCycle)
            {
              output.AppendFormat("{0}-Start on {1} at L/U{2} for ", e.Result, e.Pallet, e.LocationNum);
              writeMat();
              output.AppendLine();
            }
            else
            {
              output.AppendFormat("{0}-End on {1} at L/U{2} for ", e.Result, e.Pallet, e.LocationNum);
              writeMat();
              output.AppendLine();
            }
            break;

          case MachineWatchInterface.LogType.MachineCycle:
            if (e.StartOfCycle)
            {
              output.AppendFormat("Machine-Start on {0} at MC{1} of {2} for ", e.Pallet, e.LocationNum, e.Program);
              writeMat();
              output.AppendLine();
            }
            else
            {
              output.AppendFormat("Machine-End on {0} at MC{1} of {2} for ", e.Pallet, e.LocationNum, e.Program);
              writeMat();
              output.AppendLine();
            }
            break;

          case MachineWatchInterface.LogType.PartMark:
            output.AppendFormat("Assign {0} to ", e.Result);
            writeMat();
            output.AppendLine();
            break;

          case MachineWatchInterface.LogType.PalletCycle:
            output.AppendFormat("Pallet cycle for {0}", e.Pallet);
            output.AppendLine();
            break;
        }
      }
      _output.WriteLine(output.ToString());
    }

    private void AddJobs(IEnumerable<JobPlan> jobs, IEnumerable<(string prog, long rev)> progs)
    {
      _jobDB.AddJobs(new NewJobs()
      {
        Jobs = jobs.ToList(),
        Programs =
            progs.Select(p =>
            new MachineWatchInterface.ProgramEntry()
            {
              ProgramName = p.prog,
              Revision = p.rev,
              Comment = "Comment " + p.prog + " rev" + p.rev.ToString(),
              ProgramContent = "ProgramCt " + p.prog + " rev" + p.rev.ToString()
            }).ToList()
      }, null);
      using (var logMonitor = _logDB.Monitor())
      {
        _sync.SynchronizePallets(false);
        var evts = logMonitor.OccurredEvents.Where(e => e.EventName == "NewLogEntry").Select(e => e.Parameters[0]).Cast<LogEntry>();
        evts.Count(e => e.Result == "New Niigata Route").Should().BePositive();
      }
    }

    private void CheckSingleMaterial(IEnumerable<LogEntry> logs,
        long matId, string uniq, string part, int numProc, int[][] pals, string queue = null, bool[] reclamp = null
    )
    {
      var matLogs = logs.Where(e =>
        e.LogType != LogType.PalletInStocker && e.LogType != LogType.PalletOnRotaryInbound
      ).ToList();
      matLogs.Should().BeInAscendingOrder(e => e.EndTimeUTC);

      var expected = new List<Action<LogEntry>>();
      for (int procNum = 1; procNum <= numProc; procNum++)
      {
        int proc = procNum; // so lambdas capture constant proc
        if (proc == 1)
        {
          expected.Add(mark =>
          {
            mark.LogType.Should().Be(LogType.PartMark);
            mark.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            mark.Result.Should().Be(_settings.ConvertMaterialIDToSerial(matId));
          }
          );
        }

        if (proc > 1 && queue != null)
        {
          expected.Add(e =>
          {
            e.LogType.Should().Be(LogType.RemoveFromQueue);
            e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LocationName.Should().Be(queue);
          });
        }

        expected.Add(e =>
        {
          e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
          e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
          e.LogType.Should().Be(LogType.LoadUnloadCycle);
          e.StartOfCycle.Should().BeFalse();
          e.Result.Should().Be("LOAD");
        });
        expected.Add(e =>
        {
          e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
          e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
          e.LogType.Should().Be(LogType.MachineCycle);
          e.StartOfCycle.Should().BeTrue();
        });
        expected.Add(e =>
        {
          e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
          e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
          e.LogType.Should().Be(LogType.MachineCycle);
          e.StartOfCycle.Should().BeFalse();
        });

        if (reclamp != null && reclamp[proc - 1])
        {
          expected.Add(e =>
          {
            e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
            e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LogType.Should().Be(LogType.LoadUnloadCycle);
            e.Program.Should().Be("TestReclamp");
            e.Result.Should().Be("TestReclamp");
            e.StartOfCycle.Should().BeTrue();
          });
          expected.Add(e =>
          {
            e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
            e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LogType.Should().Be(LogType.LoadUnloadCycle);
            e.Program.Should().Be("TestReclamp");
            e.Result.Should().Be("TestReclamp");
            e.StartOfCycle.Should().BeFalse();
          });
        }

        if (proc < numProc && queue != null)
        {
          expected.Add(e =>
          {
            e.LogType.Should().Be(LogType.AddToQueue);
            e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LocationName.Should().Be(queue);
          });
        }

        expected.Add(e =>
        {
          e.Pallet.Should().BeOneOf(pals[proc - 1].Select(p => p.ToString()));
          e.LogType.Should().Be(LogType.LoadUnloadCycle);
          e.StartOfCycle.Should().BeFalse();
          e.Result.Should().Be("UNLOAD");
        });
      }

      matLogs.Should().SatisfyRespectively(expected);
    }

    private void CheckMaxQueueSize(IEnumerable<LogEntry> logs, string queue, int expectedMax)
    {
      int cnt = 0;
      int max = 0;

      foreach (var e in logs)
      {
        if (e.LogType == LogType.AddToQueue && e.LocationName == queue)
        {
          cnt += 1;
          max = Math.Max(cnt, max);
        }
        else if (e.LogType == LogType.RemoveFromQueue && e.LocationName == queue)
        {
          cnt -= 1;
        }
      }

      cnt.Should().Be(0);
      max.Should().Be(expectedMax);
    }

    private void SetPath(
      JobPlan j,
      int proc,
      int path,
      int group,
      int[] pals,
      string fixture,
      int face,
      int[] loads,
      int loadMins,
      int[] machines,
      int machMins,
      string program,
      int[] unloads,
      int unloadMins,
      int partsPerPal = 1,
      string outQueue = null,
      string inQueue = null,
      int[] reclamp = null,
      int? reclampMins = null
    )
    {
      j.SetPathGroup(proc, path, group);
      foreach (var pal in pals)
      {
        j.AddProcessOnPallet(proc, path, pal.ToString());
      }
      j.SetFixtureFace(proc, path, fixture, face);
      foreach (var lul in loads)
      {
        j.AddLoadStation(proc, path, lul);
      }
      j.SetExpectedLoadTime(proc, path, TimeSpan.FromMinutes(loadMins));

      var s = new JobMachiningStop("MC");
      s.ProgramName = program;
      s.ProgramRevision = null;
      s.ExpectedCycleTime = TimeSpan.FromMinutes(machMins);
      foreach (var m in machines)
      {
        s.Stations.Add(m);
      }
      j.AddMachiningStop(proc, path, s);

      if (reclamp != null && reclampMins.HasValue)
      {
        s = new JobMachiningStop("TestReclamp");
        s.ExpectedCycleTime = TimeSpan.FromMinutes(reclampMins.Value);
        foreach (var r in reclamp)
        {
          s.Stations.Add(r);
        }
        j.AddMachiningStop(proc, path, s);
      }

      foreach (var lul in unloads)
      {
        j.AddUnloadStation(proc, path, lul);
      }
      j.SetExpectedUnloadTime(proc, path, TimeSpan.FromMinutes(unloadMins));

      j.SetPartsPerPallet(proc, path, partsPerPal);
      j.SetOutputQueue(proc, path, outQueue);
      j.SetInputQueue(proc, path, inQueue);
    }


    [Fact]
    public void OneProcJob()
    {
      var j = new JobPlan("uniq1", 1);
      j.PartName = "part1";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 3);
      SetPath(j,
        proc: 1,
        path: 1,
        group: 0,
        pals: new[] { 1, 2 },
        fixture: "fix1",
        face: 1,
        loads: new[] { 1 },
        loadMins: 8,
        machines: new[] { 5, 6 },
        machMins: 14,
        program: "prog111",
        unloads: new[] { 1 },
        unloadMins: 5
      );

      AddJobs(new[] { j }, new[] { (prog: "prog111", rev: 5L) });

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.Should().Be(3);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(m, m.Key, "uniq1", "part1", 1, pals: new[] { new[] { 1, 2 } });
      }
    }

    [Fact]
    public void MultpleProcsMultiplePathsSeparatePallets()
    {
      var j = new JobPlan("uniq1", 2, new[] { 2, 2 });
      j.PartName = "part1";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 3);
      j.SetPlannedCyclesOnFirstProcess(path: 2, numCycles: 4);

      // path 1, group 0 on pallets 1, 2, 3, 4
      SetPath(j,
        proc: 1,
        path: 1,
        group: 0,
        pals: new[] { 1, 2 },
        fixture: "fix1",
        face: 1,
        loads: new[] { 1 },
        loadMins: 8,
        machines: new[] { 5, 6 },
        machMins: 14,
        program: "1111",
        unloads: new[] { 1 },
        unloadMins: 5,
        outQueue: "transQ"
      );
      SetPath(j,
        proc: 2,
        path: 1,
        group: 0,
        pals: new[] { 3, 4 },
        fixture: "fix2",
        face: 1,
        loads: new[] { 1 },
        loadMins: 3,
        machines: new[] { 1, 2 },
        machMins: 19,
        program: "2222",
        unloads: new[] { 1 },
        unloadMins: 6,
        inQueue: "transQ"
      );

      // path 2, group 1 on pallets 5, 6, 7, 8
      SetPath(j,
        proc: 1,
        path: 2,
        group: 1,
        pals: new[] { 5, 6 },
        fixture: "fix1",
        face: 1,
        loads: new[] { 2 },
        loadMins: 7,
        machines: new[] { 3, 4 },
        machMins: 12,
        program: "3333",
        unloads: new[] { 2 },
        unloadMins: 4,
        outQueue: "transQ"
      );
      SetPath(j,
        proc: 2,
        path: 2,
        group: 1,
        pals: new[] { 7, 8 },
        fixture: "fix2",
        face: 1,
        loads: new[] { 2 },
        loadMins: 3,
        machines: new[] { 1, 2 },
        machMins: 16,
        program: "4444",
        unloads: new[] { 2 },
        unloadMins: 3,
        inQueue: "transQ"
      );


      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);
      byMat.Count.Should().Be(7);

      var byPath = byMat.ToLookup(mat => (new[] { "1", "2" }).Contains(mat.Skip(1).First().Pallet));
      byPath[true].Count().Should().Be(3); // true is pallets 1 or 2, path 1
      byPath[false].Count().Should().Be(4); // false is pallets 5 or 6, path 2

      foreach (var path in byPath)
      {
        foreach (var m in path)
        {
          CheckSingleMaterial(
            logs: m,
            matId: m.Key,
            uniq: "uniq1",
            part: "part1",
            numProc: 2,
            queue: "transQ",
            pals:
              path.Key
                ? new[] { new[] { 1, 2 }, new[] { 3, 4 } }
                : new[] { new[] { 5, 6 }, new[] { 7, 8 } }
          );
        }
      }

    }

    [Fact]
    public void MultipleProcsMultiplePathsSamePallet()
    {
      var j = new JobPlan("uniq1", 2, new[] { 2, 2 });
      j.PartName = "part1";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 8);
      j.SetPlannedCyclesOnFirstProcess(path: 2, numCycles: 6);

      // path 1, group 0 on pallets 1, 2
      SetPath(j,
        proc: 1,
        path: 1,
        group: 0,
        pals: new[] { 1, 2 },
        fixture: "fix1",
        face: 1,
        loads: new[] { 1 },
        loadMins: 8,
        machines: new[] { 5, 6 },
        machMins: 14,
        program: "1111",
        unloads: new[] { 1 },
        unloadMins: 5,
        partsPerPal: 2
      );
      SetPath(j,
        proc: 2,
        path: 1,
        group: 0,
        pals: new[] { 1, 2 },
        fixture: "fix1",
        face: 2,
        loads: new[] { 1 },
        loadMins: 3,
        machines: new[] { 5, 6 },
        machMins: 19,
        program: "2222",
        unloads: new[] { 1 },
        unloadMins: 6,
        partsPerPal: 2
      );

      // path 2, group 1 on pallets 3, 4
      SetPath(j,
        proc: 1,
        path: 2,
        group: 1,
        pals: new[] { 3, 4 },
        fixture: "fix1",
        face: 1,
        loads: new[] { 2 },
        loadMins: 7,
        machines: new[] { 5, 6 },
        machMins: 12,
        program: "3333",
        unloads: new[] { 2 },
        unloadMins: 4,
        partsPerPal: 2
      );
      SetPath(j,
        proc: 2,
        path: 2,
        group: 1,
        pals: new[] { 3, 4 },
        fixture: "fix1",
        face: 2,
        loads: new[] { 2 },
        loadMins: 3,
        machines: new[] { 5, 6 },
        machMins: 16,
        program: "4444",
        unloads: new[] { 2 },
        unloadMins: 3,
        partsPerPal: 2
      );


      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();

      var matIds = logs.SelectMany(e => e.Material).Select(m => m.MaterialID).ToHashSet();
      matIds.Count.Should().Be(14);

      var byPath =
        matIds
          .Select(matId => new { matId, logs = logs.Where(e => e.Material.Any(m => m.MaterialID == matId)) })
          .ToLookup(mat => (new[] { "1", "2" }).Contains(mat.logs.Skip(1).First().Pallet));
      byPath[true].Count().Should().Be(8); // true is pallets 1 or 2, path 1
      byPath[false].Count().Should().Be(6); // false is pallets 3 or 4, path 2

      foreach (var path in byPath)
      {
        foreach (var m in path)
        {
          CheckSingleMaterial(
            logs: m.logs,
            matId: m.matId,
            uniq: "uniq1",
            part: "part1",
            numProc: 2,
            pals:
              path.Key
                ? new[] { new[] { 1, 2 }, new[] { 1, 2 } }
                : new[] { new[] { 3, 4 }, new[] { 3, 4 } }
          );
        }
      }


    }

    [Fact]
    public void SizedQueue()
    {
      var j = new JobPlan("uniq1", 2);
      j.PartName = "part1";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 8);

      // process 1 on 4 pallets with short times
      SetPath(j,
        proc: 1,
        path: 1,
        group: 0,
        pals: new[] { 1, 2, 3, 4 },
        fixture: "fix1",
        face: 1,
        loads: new[] { 1 },
        loadMins: 2,
        machines: new[] { 1, 2, 3, 4, 5, 6 },
        machMins: 5,
        program: "1111",
        unloads: new[] { 1 },
        unloadMins: 3,
        outQueue: "sizedQ"
      );

      //process 2 on only 2 pallets with longer times
      SetPath(j,
        proc: 2,
        path: 1,
        group: 0,
        pals: new[] { 5, 6 },
        fixture: "fix2",
        face: 1,
        loads: new[] { 1 },
        loadMins: 3,
        machines: new[] { 1, 2, 3, 4, 5, 6 },
        machMins: 20,
        program: "2222",
        unloads: new[] { 1 },
        unloadMins: 4,
        inQueue: "sizedQ"
      );


      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.Should().Be(8);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(m, m.Key, "uniq1", "part1", 2, pals: new[] { new[] { 1, 2, 3, 4 }, new[] { 5, 6 } }, queue: "sizedQ");
      }

      CheckMaxQueueSize(logs, "sizedQ", 1);
    }

    [Fact]
    public void SizedQueueWithReclamp()
    {
      var j = new JobPlan("uniq1", 2);
      j.PartName = "part1";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 4);

      // process 1 on 4 pallets with short times
      SetPath(j,
        proc: 1,
        path: 1,
        group: 0,
        pals: new[] { 1, 2, 3, 4 },
        fixture: "fix1",
        face: 1,
        loads: new[] { 1 },
        loadMins: 2,
        machines: new[] { 1, 2, 3, 4, 5, 6 },
        machMins: 5,
        program: "1111",
        reclamp: new[] { 2 },
        reclampMins: 3,
        unloads: new[] { 1 },
        unloadMins: 3,
        outQueue: "sizedQ"
      );

      //process 2 on only 2 pallets with longer times
      SetPath(j,
        proc: 2,
        path: 1,
        group: 0,
        pals: new[] { 5, 6 },
        fixture: "fix2",
        face: 1,
        loads: new[] { 1 },
        loadMins: 3,
        machines: new[] { 1, 2, 3, 4, 5, 6 },
        machMins: 20,
        program: "2222",
        unloads: new[] { 1 },
        unloadMins: 4,
        inQueue: "sizedQ"
      );


      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.Should().Be(4);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(m, m.Key, "uniq1", "part1", 2,
          pals: new[] { new[] { 1, 2, 3, 4 }, new[] { 5, 6 } },
          queue: "sizedQ",
          reclamp: new[] { true, false });
      }

      CheckMaxQueueSize(logs, "sizedQ", 1);
    }

  }
}