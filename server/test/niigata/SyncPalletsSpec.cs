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
    private AssignPallets _assign;
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

      _assign = new AssignPallets(record, null);
      _createLog = new CreateCellState(_logDB, _jobDB, record, _settings, null);

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
        if (evts.Any())
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

    private void CheckSingleMaterial(IEnumerable<LogEntry> logs, long matId, string uniq, string part, int numProc)
    {
      logs.Should().BeInAscendingOrder(e => e.EndTimeUTC);

      var expected = new List<Action<LogEntry>>();
      for (int proc = 1; proc <= numProc; proc++)
      {
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

        expected.Add(e =>
        {
          e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
          e.LogType.Should().Be(LogType.LoadUnloadCycle);
          e.StartOfCycle.Should().BeFalse();
          e.Result.Should().Be("LOAD");
        });
        expected.Add(e =>
        {
          e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
          e.LogType.Should().Be(LogType.MachineCycle);
          e.StartOfCycle.Should().BeTrue();
        });
        expected.Add(e =>
        {
          e.Material.Should().OnlyContain(m => m.PartName == part && m.JobUniqueStr == uniq);
          e.LogType.Should().Be(LogType.MachineCycle);
          e.StartOfCycle.Should().BeFalse();
        });
        expected.Add(e =>
        {
          e.LogType.Should().Be(LogType.LoadUnloadCycle);
          e.StartOfCycle.Should().BeFalse();
          e.Result.Should().Be("UNLOAD");
        });
      }

      logs.Should().SatisfyRespectively(expected);
    }


    [Fact]
    public void OneProcJob()
    {
      var j = new JobPlan("uniq1", 1);
      j.PartName = "part1";
      j.SetPlannedCyclesOnFirstProcess(path: 1, numCycles: 3);
      j.AddLoadStation(1, 1, statNum: 1);
      j.AddUnloadStation(1, 1, statNum: 1);
      j.AddLoadStation(1, 1, statNum: 2);
      j.AddUnloadStation(1, 1, statNum: 2);
      j.SetExpectedLoadTime(1, 1, TimeSpan.FromMinutes(8));
      j.SetExpectedUnloadTime(1, 1, TimeSpan.FromMinutes(9));
      j.SetPartsPerPallet(1, 1, partsPerPallet: 1);

      var s = new JobMachiningStop("MC");
      s.ProgramName = "prog111";
      s.ProgramRevision = null;
      s.ExpectedCycleTime = TimeSpan.FromMinutes(14);
      s.Stations.Add(5);
      s.Stations.Add(6);
      j.AddMachiningStop(1, 1, s);
      j.AddProcessOnPallet(1, 1, "1");
      j.AddProcessOnPallet(1, 1, "2");
      j.SetFixtureFace(1, 1, "fix1", 1);

      AddJobs(new[] { j }, new[] { (prog: "prog111", rev: 5L) });

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.Should().Be(3);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(m, m.Key, "uniq1", "part1", 1);
      }
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