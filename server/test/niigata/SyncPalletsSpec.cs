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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using BlackMaple.MachineFramework;
using Shouldly;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class SyncPalletsSpec : IDisposable
  {
    private SerialSettings _serialSt;
    private FMSSettings _fmsSt;
    private RepositoryConfig _logDBCfg;
    private IccSimulator _sim;
    private SyncNiigataPallets _sync;
    private JsonSerializerOptions jsonSettings;

    public SyncPalletsSpec()
    {
      _serialSt = new SerialSettings()
      {
        ConvertMaterialIDToSerial = (m) => SerialSettings.ConvertToBase62(m, 10),
      };
      _fmsSt = new FMSSettings();
      _fmsSt.Queues.Add("Transfer", new QueueInfo() { MaxSizeBeforeStopUnloading = -1 });
      _fmsSt.Queues.Add("sizedQ", new QueueInfo() { MaxSizeBeforeStopUnloading = 1 });

      _logDBCfg = RepositoryConfig.InitializeMemoryDB(_serialSt);

      jsonSettings = new JsonSerializerOptions();
      FMSInsightWebHost.JsonSettings(jsonSettings);
      jsonSettings.WriteIndented = true;
    }

    private void InitSim(
      NiigataStationNames statNames,
      int numPals = 10,
      int numMachines = 6,
      int numLoads = 2,
      Func<IRepository, IJobCache> BuildJobCache = null
    )
    {
      var machConn = NSubstitute.Substitute.For<ICncMachineConnection>();

      var assign = new MultiPalletAssign(
        new IAssignPallets[] { new AssignNewRoutesOnPallets(statNames), new SizedQueues(_fmsSt.Queues) }
      );

      _sim = new IccSimulator(
        numPals: numPals,
        numMachines: numMachines,
        numLoads: numLoads,
        statNames: statNames
      );

      var settings = new NiigataSettings()
      {
        ProgramDirectory = "",
        SQLConnectionString = "",
        RequireProgramsInJobs = false,
        StationNames = statNames,
        MachineIPs = [],
        CreateJobCache = BuildJobCache ?? (db => new JobCache(db)),
      };

      _sync = new SyncNiigataPallets(_fmsSt, settings, _sim, machConn, assign);

      _sim.OnNewProgram += (newprog) =>
      {
        using var db = _logDBCfg.OpenConnection();
        db.SetCellControllerProgramForProgram(
          newprog.ProgramName,
          newprog.ProgramRevision,
          newprog.ProgramNum.ToString()
        );
      };
    }

    public void Dispose()
    {
      _logDBCfg.Dispose();
    }

    private void Synchronize()
    {
      using (var db = _logDBCfg.OpenConnection())
      {
        bool applyAction = false;
        do
        {
          var st = _sync.CalculateCellState(db);
          applyAction = _sync.ApplyActions(db, st);
        } while (applyAction);
      }
    }

    private IEnumerable<LogEntry> Step()
    {
      if (!_sim.Step())
      {
        return null;
      }
      var lst = new List<LogEntry>();
      Action<LogEntry, string, IRepository> addLog = (e, _, _) => lst.Add(e);
      _logDBCfg.NewLogEntry += addLog;
      try
      {
        Synchronize();
        return lst;
      }
      finally
      {
        _logDBCfg.NewLogEntry -= addLog;
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
      }
      return logs;
    }

    private void WriteLogs(IEnumerable<MachineFramework.LogEntry> es)
    {
      var output = new System.Text.StringBuilder();
      foreach (var e in es)
      {
        Action writeMat = () =>
          output.AppendJoin(
            ',',
            e.Material.Select(m =>
              m.PartName + "-" + m.Process.ToString() + "[" + m.MaterialID.ToString() + "]"
            )
          );
        switch (e.LogType)
        {
          case LogType.LoadUnloadCycle:
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

          case LogType.MachineCycle:
            if (e.StartOfCycle)
            {
              output.AppendFormat(
                "Machine-Start on {0} at MC{1} of {2} for ",
                e.Pallet,
                e.LocationNum,
                e.Program
              );
              writeMat();
              output.AppendLine();
            }
            else
            {
              output.AppendFormat(
                "Machine-End on {0} at MC{1} of {2} for ",
                e.Pallet,
                e.LocationNum,
                e.Program
              );
              writeMat();
              output.AppendLine();
            }
            break;

          case LogType.PartMark:
            output.AppendFormat("Assign {0} to ", e.Result);
            writeMat();
            output.AppendLine();
            break;

          case LogType.PalletCycle:
            output.AppendFormat("Pallet cycle for {0}", e.Pallet);
            output.AppendLine();
            break;
        }
      }
      System.Diagnostics.Debug.WriteLine(output.ToString());
    }

    private void AddJobs(IEnumerable<Job> jobs, IEnumerable<(string prog, long rev)> progs)
    {
      AddJobs(jobs.ToImmutableList(), progs);
    }

    private void AddJobs(ImmutableList<Job> jobs, IEnumerable<(string prog, long rev)> progs)
    {
      AddJobs(
        new NewJobs()
        {
          Jobs = jobs,
          ScheduleId = "theschId",
          Programs = progs
            .Select(p => new MachineFramework.NewProgramContent()
            {
              ProgramName = p.prog,
              Revision = p.rev,
              Comment = "Comment " + p.prog + " rev" + p.rev.ToString(),
              ProgramContent = "ProgramCt " + p.prog + " rev" + p.rev.ToString(),
            })
            .ToImmutableList(),
        }
      );
    }

    private void ExpectNewRoute()
    {
      bool seenNewRoute = false;

      Action<LogEntry, string, IRepository> addLog = (e, _, _) =>
      {
        if (e.Result == "New Niigata Route")
        {
          seenNewRoute = true;
        }
      };

      _logDBCfg.NewLogEntry += addLog;
      try
      {
        Synchronize();
      }
      finally
      {
        _logDBCfg.NewLogEntry -= addLog;
      }

      seenNewRoute.ShouldBeTrue();
    }

    private void AddJobs(NewJobs jobs, bool expectNewRoute = true)
    {
      using (var db = _logDBCfg.OpenConnection())
      {
        db.AddJobs(jobs, null, addAsCopiedToSystem: true);
      }

      bool seenNewRoute = false;

      Action<LogEntry, string, IRepository> addLog = (e, _, _) =>
      {
        if (e.Result == "New Niigata Route")
        {
          seenNewRoute = true;
        }
      };

      _logDBCfg.NewLogEntry += addLog;
      try
      {
        Synchronize();
      }
      finally
      {
        _logDBCfg.NewLogEntry -= addLog;
      }

      if (expectNewRoute)
      {
        seenNewRoute.ShouldBeTrue();
      }
    }

    private void CheckSingleMaterial(
      IEnumerable<LogEntry> logs,
      long matId,
      string uniq,
      string part,
      int numProc,
      int[][] pals,
      string queue = null,
      bool[] reclamp = null,
      string[][] machGroups = null,
      string castingQueue = null,
      (string prog, long rev)[][] progs = null
    )
    {
      var matLogs = logs.Where(e =>
          e.LogType != LogType.PalletInStocker
          && e.LogType != LogType.PalletOnRotaryInbound
          && e.LogType != LogType.PalletCycle
        )
        .ToList();
      matLogs.Select(e => e.EndTimeUTC).ShouldBeInOrder();

      machGroups = machGroups ?? Enumerable.Range(1, numProc).Select(_ => new string[] { "MC" }).ToArray();

      var expected = new List<Action<LogEntry>>();
      for (int procNum = 1; procNum <= numProc; procNum++)
      {
        int proc = procNum; // so lambdas capture constant proc
        if (proc == 1)
        {
          if (castingQueue != null)
          {
            expected.Add(e =>
            {
              e.LogType.ShouldBe(LogType.RemoveFromQueue);
              e.Material.ShouldAllBe(m => m.PartName == part && m.JobUniqueStr == uniq);
              e.LocationName.ShouldBe(castingQueue);
            });
          }
          else
          {
            expected.Add(mark =>
            {
              mark.LogType.ShouldBe(LogType.PartMark);
              mark.Material.ShouldAllBe(m => m.PartName == part && m.JobUniqueStr == uniq);
              mark.Result.ShouldBe(_serialSt.ConvertMaterialIDToSerial(matId));
            });
          }
        }

        if (proc > 1 && queue != null)
        {
          expected.Add(e =>
          {
            e.LogType.ShouldBe(LogType.RemoveFromQueue);
            e.Material.ShouldAllBe(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LocationName.ShouldBe(queue);
          });
        }

        expected.Add(e =>
        {
          e.Pallet.ShouldBeOneOf(pals[proc - 1]);
          e.Material.ShouldAllBe(m => m.PartName == part && m.JobUniqueStr == uniq);
          e.LogType.ShouldBe(LogType.LoadUnloadCycle);
          e.StartOfCycle.ShouldBeFalse();
          e.Result.ShouldBe("LOAD");
        });

        foreach (var (group, grpIdx) in machGroups[proc - 1].Select((g, idx) => (g, idx)))
        {
          expected.Add(e =>
          {
            e.Pallet.ShouldBeOneOf(pals[proc - 1]);
            e.Material.ShouldAllBe(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LogType.ShouldBe(LogType.MachineCycle);
            e.LocationName.ShouldBe(group);
            e.StartOfCycle.ShouldBeTrue();

            if (progs != null && proc - 1 < progs.Length && grpIdx < progs[proc - 1].Length)
            {
              var p = progs[proc - 1][grpIdx];
              e.Program.ShouldBe(p.prog);
              e.ProgramDetails["ProgramRevision"].ShouldBe(p.rev.ToString());
            }
          });
          expected.Add(e =>
          {
            e.Pallet.ShouldBeOneOf(pals[proc - 1]);
            e.Material.ShouldAllBe(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LogType.ShouldBe(LogType.MachineCycle);
            e.LocationName.ShouldBe(group);
            e.StartOfCycle.ShouldBeFalse();

            if (progs != null && proc - 1 < progs.Length && grpIdx < progs[proc - 1].Length)
            {
              var p = progs[proc - 1][grpIdx];
              e.Program.ShouldBe(p.prog);
              e.ProgramDetails["ProgramRevision"].ShouldBe(p.rev.ToString());
            }
          });
        }

        if (reclamp != null && reclamp[proc - 1])
        {
          expected.Add(e =>
          {
            e.Pallet.ShouldBeOneOf(pals[proc - 1]);
            e.Material.ShouldAllBe(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LogType.ShouldBe(LogType.LoadUnloadCycle);
            e.Program.ShouldBe("TestReclamp");
            e.Result.ShouldBe("TestReclamp");
            e.StartOfCycle.ShouldBeTrue();
          });
          expected.Add(e =>
          {
            e.Pallet.ShouldBeOneOf(pals[proc - 1]);
            e.Material.ShouldAllBe(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LogType.ShouldBe(LogType.LoadUnloadCycle);
            e.Program.ShouldBe("TestReclamp");
            e.Result.ShouldBe("TestReclamp");
            e.StartOfCycle.ShouldBeFalse();
          });
        }

        if (proc < numProc && queue != null)
        {
          expected.Add(e =>
          {
            e.LogType.ShouldBe(LogType.AddToQueue);
            e.Material.ShouldAllBe(m => m.PartName == part && m.JobUniqueStr == uniq);
            e.LocationName.ShouldBe(queue);
          });
        }

        expected.Add(e =>
        {
          e.Pallet.ShouldBeOneOf(pals[proc - 1]);
          e.LogType.ShouldBe(LogType.LoadUnloadCycle);
          e.StartOfCycle.ShouldBeFalse();
          e.Result.ShouldBe("UNLOAD");
        });
      }

      matLogs.Count.ShouldBe(expected.Count);
      for (int i = 0; i < matLogs.Count; i++)
      {
        expected[i](matLogs[i]);
      }
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

      cnt.ShouldBe(0);
      max.ShouldBe(expectedMax);
    }

    [Test]
    public void OneProcJob()
    {
      InitSim(
        new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
          IccMachineToJobMachNames = Enumerable
            .Range(1, 6)
            .ToDictionary(mc => mc, mc => (group: "TestMC", num: mc + 100)),
        }
      );

      var j = FakeIccDsl
        .CreateOneProcOnePathJob(
          unique: "uniq1",
          part: "part1",
          qty: 3,
          priority: 1,
          partsPerPal: 1,
          pals: new[] { 1, 2 },
          fixture: "fix1",
          face: 1,
          luls: new[] { 1 },
          loadMins: 8,
          machs: new[] { 5, 6 },
          machMins: 14,
          prog: "prog111",
          progRev: null,
          unloadMins: 5
        )
        .Item1;

      AddJobs(new[] { j }, new[] { (prog: "prog111", rev: 5L) });

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.ShouldBe(3);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(
          m,
          m.Key,
          "uniq1",
          "part1",
          1,
          pals: new[] { new[] { 1, 2 } },
          machGroups: new[] { new[] { "TestMC" } }
        );
      }
    }

    [Test]
    public void MultpleProcsMultiplePathsSeparatePallets()
    {
      InitSim(
        new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
          IccMachineToJobMachNames = Enumerable
            .Range(1, 6)
            .ToDictionary(mc => mc, mc => (group: "MC", num: mc)),
        }
      );

      var j = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        Cycles = 7,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                Load = [1],
                Unload = [1],
                ExpectedLoadTime = TimeSpan.FromMinutes(8),
                ExpectedUnloadTime = TimeSpan.FromMinutes(5),
                PartsPerPallet = 1,
                PalletNums = [1, 2],
                Fixture = "fix1",
                Face = 1,
                OutputQueue = "transQ",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(14),
                    Program = "1111",
                  },
                ],
              },
              new ProcPathInfo()
              {
                Load = [2],
                Unload = [2],
                ExpectedLoadTime = TimeSpan.FromMinutes(7),
                ExpectedUnloadTime = TimeSpan.FromMinutes(4),
                PartsPerPallet = 1,
                PalletNums = [5, 6],
                Fixture = "fix1",
                Face = 1,
                OutputQueue = "transQ",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [3, 4],
                    ExpectedCycleTime = TimeSpan.FromMinutes(12),
                    Program = "3333",
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                Load = [1],
                Unload = [1],
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(6),
                PartsPerPallet = 1,
                PalletNums = [3, 4],
                Fixture = "fix2",
                Face = 1,
                InputQueue = "transQ",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [1, 2],
                    ExpectedCycleTime = TimeSpan.FromMinutes(19),
                    Program = "2222",
                  },
                ],
              },
              new ProcPathInfo()
              {
                Load = [2],
                Unload = [2],
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(3),
                PartsPerPallet = 1,
                PalletNums = [7, 8],
                Fixture = "fix2",
                Face = 1,
                InputQueue = "transQ",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [1, 2],
                    ExpectedCycleTime = TimeSpan.FromMinutes(16),
                    Program = "4444",
                  },
                ],
              },
            ],
          },
        ],
      };

      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);
      byMat.Count.ShouldBe(7);

      var byPath = byMat.ToLookup(mat => (new[] { 1, 2 }).Contains(mat.Skip(1).First().Pallet));
      byPath[true].Count().ShouldBeGreaterThan(0); // true is pallets 1 or 2, path 1
      byPath[false].Count().ShouldBeGreaterThan(0); // false is pallets 5 or 6, path 2

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
            pals: path.Key
              ? new[] { new[] { 1, 2 }, new[] { 3, 4, 7, 8 } }
              : new[] { new[] { 5, 6 }, new[] { 3, 4, 7, 8 } }
          );
        }
      }
    }

    [Test]
    public void MultipleProcsMultiplePathsSamePallet()
    {
      InitSim(
        new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
          IccMachineToJobMachNames = Enumerable
            .Range(1, 6)
            .ToDictionary(mc => mc, mc => (group: "MC", num: mc)),
        }
      );

      var j = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        Cycles = 14,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                Load = [1],
                Unload = [1],
                ExpectedLoadTime = TimeSpan.FromMinutes(8),
                ExpectedUnloadTime = TimeSpan.FromMinutes(5),
                PartsPerPallet = 2,
                PalletNums = [1, 2],
                Fixture = "fix1",
                Face = 1,
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(14),
                    Program = "1111",
                  },
                ],
              },
              new ProcPathInfo()
              {
                Load = [2],
                Unload = [2],
                ExpectedLoadTime = TimeSpan.FromMinutes(7),
                ExpectedUnloadTime = TimeSpan.FromMinutes(4),
                PartsPerPallet = 2,
                PalletNums = [3, 4],
                Fixture = "fix1",
                Face = 1,
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(12),
                    Program = "3333",
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                Load = [1],
                Unload = [1],
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(6),
                PartsPerPallet = 2,
                PalletNums = [1, 2],
                Fixture = "fix1",
                Face = 2,
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(19),
                    Program = "2222",
                  },
                ],
              },
              new ProcPathInfo()
              {
                Load = [2],
                Unload = [2],
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(3),
                PartsPerPallet = 2,
                PalletNums = [3, 4],
                Fixture = "fix1",
                Face = 2,
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(16),
                    Program = "4444",
                  },
                ],
              },
            ],
          },
        ],
      };

      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();

      var matIds = logs.SelectMany(e => e.Material).Select(m => m.MaterialID).ToHashSet();
      matIds.Count.ShouldBe(14);

      var byPath = matIds
        .Select(matId => new { matId, logs = logs.Where(e => e.Material.Any(m => m.MaterialID == matId)) })
        .ToLookup(mat => (new[] { 1, 2 }).Contains(mat.logs.Skip(1).First().Pallet));
      byPath[true].Count().ShouldBeGreaterThan(0); // true is pallets 1 or 2, path 1
      byPath[false].Count().ShouldBeGreaterThan(0); // false is pallets 3 or 4, path 2

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
            pals: path.Key
              ? new[] { new[] { 1, 2 }, new[] { 1, 2 } }
              : new[] { new[] { 3, 4 }, new[] { 3, 4 } }
          );
        }
      }
    }

    [Test, Skip("Holding at machine not yet supported by Niigata")]
    public void SizedQueue()
    {
      InitSim(
        new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
          IccMachineToJobMachNames = Enumerable
            .Range(1, 6)
            .ToDictionary(mc => mc, mc => (group: "MC", num: mc)),
        }
      );

      var j = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        Cycles = 8,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                Load = [1],
                Unload = [1],
                ExpectedLoadTime = TimeSpan.FromMinutes(2),
                ExpectedUnloadTime = TimeSpan.FromMinutes(3),
                PartsPerPallet = 1,
                PalletNums = [1, 2, 3, 4],
                Fixture = "fix1",
                Face = 1,
                OutputQueue = "sizedQ",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [1, 2, 3, 4, 5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(5),
                    Program = "1111",
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                Load = [1],
                Unload = [1],
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(4),
                PartsPerPallet = 1,
                PalletNums = [5, 6],
                Fixture = "fix2",
                Face = 1,
                InputQueue = "sizedQ",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [1, 2, 3, 4, 5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(20),
                    Program = "2222",
                  },
                ],
              },
            ],
          },
        ],
      };

      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.ShouldBe(8);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(
          m,
          m.Key,
          "uniq1",
          "part1",
          2,
          pals: new[] { new[] { 1, 2, 3, 4 }, new[] { 5, 6 } },
          queue: "sizedQ"
        );
      }

      CheckMaxQueueSize(logs, "sizedQ", 1);
    }

    [Test]
    public void SizedQueueWithReclamp()
    {
      InitSim(
        new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
          IccMachineToJobMachNames = Enumerable
            .Range(1, 6)
            .ToDictionary(mc => mc, mc => (group: "MC", num: mc)),
        }
      );

      var j = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        Cycles = 4,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                Load = [1],
                Unload = [1],
                ExpectedLoadTime = TimeSpan.FromMinutes(2),
                ExpectedUnloadTime = TimeSpan.FromMinutes(3),
                PartsPerPallet = 1,
                PalletNums = [1, 2, 3, 4],
                Fixture = "fix1",
                Face = 1,
                OutputQueue = "sizedQ",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [1, 2, 3, 4, 5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(5),
                    Program = "1111",
                  },
                  new MachiningStop()
                  {
                    StationGroup = "TestReclamp",
                    Stations = [2],
                    ExpectedCycleTime = TimeSpan.FromMinutes(3),
                  },
                ],
              },
            ],
          },
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                Load = [1],
                Unload = [1],
                ExpectedLoadTime = TimeSpan.FromMinutes(3),
                ExpectedUnloadTime = TimeSpan.FromMinutes(4),
                PartsPerPallet = 1,
                PalletNums = [5, 6],
                Fixture = "fix2",
                Face = 1,
                InputQueue = "sizedQ",
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [1, 2, 3, 4, 5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(20),
                    Program = "2222",
                  },
                ],
              },
            ],
          },
        ],
      };

      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.ShouldBe(4);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(
          m,
          m.Key,
          "uniq1",
          "part1",
          2,
          pals: new[] { new[] { 1, 2, 3, 4 }, new[] { 5, 6 } },
          queue: "sizedQ",
          reclamp: new[] { true, false }
        );
      }

      CheckMaxQueueSize(logs, "sizedQ", 1);
    }

    [Test]
    public void TwoMachineStops()
    {
      InitSim(
        new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>() { },
          IccMachineToJobMachNames = new Dictionary<int, (string group, int num)>
          {
            { 1, (group: "RO", num: 1) },
            { 2, (group: "RO", num: 2) },
            { 3, (group: "RO", num: 3) },
            { 4, (group: "RO", num: 4) },
            { 5, (group: "FC", num: 1) },
            { 6, (group: "FC", num: 2) },
            { 7, (group: "FC", num: 3) },
            { 8, (group: "FC", num: 4) },
          },
        },
        numPals: 16,
        numLoads: 4,
        numMachines: 8
      );

      AddJobs(
        JsonSerializer.Deserialize<NewJobs>(
          System.IO.File.ReadAllText("../../../sample-newjobs/two-stops.json"),
          jsonSettings
        )
      );

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.ShouldBe(9);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(
          m,
          matId: m.Key,
          uniq: "aaa-0NovSzGF9VZGS00b_",
          part: "aaa",
          numProc: 4,
          queue: "Transfer",
          pals: new[]
          {
            new[] { 2, 3, 4 },
            new[] { 5, 6, 7, 8, 9, 10 },
            new[] { 12, 13, 14, 15, 16 },
            new[] { 11 },
          },
          machGroups: new[] { new[] { "RO" }, new[] { "RO", "FC" }, new[] { "FC" }, new[] { "RO" } },
          progs: new[]
          {
            new[] { (prog: "aaa1RO", rev: 1L) },
            new[] { (prog: "aaa2RO", rev: 1L), (prog: "aaa2FC", rev: 1L) },
            new[] { (prog: "aaa3FC", rev: 1L) },
            new[] { (prog: "aaa4RO", rev: 1L) },
          }
        );
      }
    }

    private static HistoricJob AdjustLoad3To2(HistoricJob j)
    {
      return j.AdjustAllPaths(
        (proc, pathNum, path) =>
        {
          if (path.Load.Contains(3))
          {
            path = path with { Load = path.Load.Remove(3).Add(2) };
          }
          if (path.Unload.Contains(3))
          {
            path = path with { Unload = path.Unload.Remove(3).Add(2) };
          }
          return path;
        }
      );
    }

    [Test]
    public void ChangePathOfJob()
    {
      InitSim(
        new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
          IccMachineToJobMachNames = Enumerable
            .Range(1, 6)
            .ToDictionary(mc => mc, mc => (group: "TestMC", num: mc + 100)),
        },
        BuildJobCache: db => new JobCache(db, AdjustLoad3To2),
        numLoads: 4,
        numMachines: 6
      );

      var j = FakeIccDsl
        .CreateMultiProcSeparatePalletJob(
          unique: "uniq1",
          part: "part1",
          qty: 3,
          priority: 1,
          fixture: "fix1",
          partsPerPal: 1,
          pals1: [5],
          pals2: [7],
          load1: [1],
          unload1: [3],
          load2: [3],
          unload2: [4],
          loadMins1: 8,
          loadMins2: 9,
          unloadMins1: 10,
          unloadMins2: 11,
          machs: new[] { 5, 6 },
          machMins1: 14,
          machMins2: 15,
          prog1: "prog111",
          prog1Rev: null,
          prog2: "prog222",
          prog2Rev: null,
          transQ: "FLIPPER"
        )
        .Item1;

      AddJobs(new[] { j }, new[] { (prog: "prog111", rev: 5L), (prog: "prog222", rev: 8L) });

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.ShouldBe(3);
      byMat
        .SelectMany(e => e)
        .Where(e => e.LogType == LogType.LoadUnloadCycle && e.Program == "LOAD")
        .ShouldAllBe(e => e.LocationNum == (e.Material[0].Process == 1 ? 1 : 2));
      byMat
        .SelectMany(e => e)
        .Where(e => e.LogType == LogType.LoadUnloadCycle && e.Program == "UNLOAD")
        .ShouldAllBe(e => e.LocationNum == (e.Material[0].Process == 1 ? 2 : 4));

      foreach (var m in byMat)
      {
        CheckSingleMaterial(
          logs: m,
          matId: m.Key,
          uniq: "uniq1",
          part: "part1",
          numProc: 2,
          pals:
          [
            [5],
            [7],
          ],
          queue: "FLIPPER",
          machGroups:
          [
            ["TestMC"],
            ["TestMC"],
          ]
        );
      }
    }

    private static HistoricJob RemoveFirstPath(HistoricJob j)
    {
      return j with
      {
        Processes = j
          .Processes.Select(proc => proc with { Paths = proc.Paths.Skip(1).ToImmutableList() })
          .ToImmutableList(),
      };
    }

    [Test]
    public void FilterOutPath()
    {
      InitSim(
        new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>() { "TestReclamp" },
          IccMachineToJobMachNames = Enumerable
            .Range(1, 6)
            .ToDictionary(mc => mc, mc => (group: "MC", num: mc)),
        },
        BuildJobCache: db => new JobCache(db, RemoveFirstPath)
      );

      var j = new Job()
      {
        UniqueStr = "uniq1",
        PartName = "part1",
        Cycles = 14,
        RouteStartUTC = DateTime.MinValue,
        RouteEndUTC = DateTime.MinValue,
        Archived = false,
        Processes =
        [
          new ProcessInfo()
          {
            Paths =
            [
              new ProcPathInfo()
              {
                Load = [1],
                Unload = [1],
                ExpectedLoadTime = TimeSpan.FromMinutes(8),
                ExpectedUnloadTime = TimeSpan.FromMinutes(5),
                PartsPerPallet = 2,
                PalletNums = [1, 2],
                Fixture = "fix1",
                Face = 1,
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(14),
                    Program = "1111",
                  },
                ],
              },
              new ProcPathInfo()
              {
                Load = [2],
                Unload = [2],
                ExpectedLoadTime = TimeSpan.FromMinutes(7),
                ExpectedUnloadTime = TimeSpan.FromMinutes(4),
                PartsPerPallet = 2,
                PalletNums = [3, 4],
                Fixture = "fix1",
                Face = 1,
                SimulatedStartingUTC = DateTime.MinValue,
                SimulatedAverageFlowTime = TimeSpan.Zero,
                Stops =
                [
                  new MachiningStop()
                  {
                    StationGroup = "MC",
                    Stations = [5, 6],
                    ExpectedCycleTime = TimeSpan.FromMinutes(12),
                    Program = "3333",
                  },
                ],
              },
            ],
          },
        ],
      };

      AddJobs(new[] { j }, Enumerable.Empty<(string prog, long rev)>());

      var logs = Run();

      logs.Where(e => e.LogType == LogType.LoadUnloadCycle && e.Program == "LOAD")
        .ShouldAllBe(e =>
          e.LocationNum == 2 // path 1 filtered out
        );

      logs.ShouldAllBe(e => e.Pallet == 0 || e.Pallet == 3 || e.Pallet == 4);
    }

    [Test]
    public void PerMaterialWorkorderPrograms()
    {
      InitSim(
        new NiigataStationNames()
        {
          ReclampGroupNames = new HashSet<string>() { },
          IccMachineToJobMachNames = new Dictionary<int, (string group, int num)>
          {
            { 1, (group: "RO", num: 1) },
            { 2, (group: "RO", num: 2) },
            { 3, (group: "RO", num: 3) },
            { 4, (group: "RO", num: 4) },
            { 5, (group: "FC", num: 1) },
            { 6, (group: "FC", num: 2) },
            { 7, (group: "FC", num: 3) },
            { 8, (group: "FC", num: 4) },
          },
        },
        numPals: 16,
        numLoads: 4,
        numMachines: 8
      );

      var newJobs = JsonSerializer.Deserialize<NewJobs>(
        System.IO.File.ReadAllText("../../../sample-newjobs/two-stops.json"),
        jsonSettings
      );
      newJobs = newJobs with
      {
        Jobs = newJobs
          .Jobs.Select(j =>
            j.AdjustAllPaths(
              (proc, path, draftPath) =>
                draftPath with
                {
                  InputQueue = proc == 1 ? "castingQ" : draftPath.InputQueue,
                  Stops = draftPath
                    .Stops.Select(d => d with { Program = null, ProgramRevision = null })
                    .ToImmutableList(),
                }
            )
          )
          .ToImmutableList(),
        CurrentUnfilledWorkorders = newJobs.CurrentUnfilledWorkorders.AddRange(
          new[]
          {
            new Workorder()
            {
              WorkorderId = "work1",
              Part = "aaa",
              Quantity = 0,
              DueDate = DateTime.MinValue,
              Priority = 0,
              Programs =
              [
                new ProgramForJobStep()
                {
                  ProcessNumber = 1,
                  StopIndex = 0,
                  ProgramName = "aaa1RO",
                  Revision = -1,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 2,
                  StopIndex = 0,
                  ProgramName = "aaa2RO",
                  Revision = -1,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 2,
                  StopIndex = 1,
                  ProgramName = "aaa2FC",
                  Revision = -1,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 3,
                  ProgramName = "aaa3FC",
                  Revision = -1,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 4,
                  ProgramName = "aaa4RO",
                  Revision = -1,
                },
              ],
            },
            new Workorder()
            {
              WorkorderId = "work2",
              Part = "aaa",
              Quantity = 0,
              DueDate = DateTime.MinValue,
              Priority = 0,
              Programs =
              [
                new ProgramForJobStep()
                {
                  ProcessNumber = 1,
                  StopIndex = 0,
                  ProgramName = "aaa1RO",
                  Revision = -2,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 2,
                  StopIndex = 0,
                  ProgramName = "aaa2RO",
                  Revision = -2,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 2,
                  StopIndex = 1,
                  ProgramName = "aaa2FC",
                  Revision = -2,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 3,
                  ProgramName = "zzz3FC",
                  Revision = -1,
                },
                new ProgramForJobStep()
                {
                  ProcessNumber = 4,
                  ProgramName = "zzz4RO",
                  Revision = -1,
                },
              ],
            },
          }
        ),
        Programs = newJobs.Programs.AddRange(
          new[]
          {
            new MachineFramework.NewProgramContent()
            {
              ProgramName = "aaa1RO",
              Revision = -2,
              Comment = "a 1 RO rev -2",
              ProgramContent = "aa 1 RO rev-2",
            },
            new MachineFramework.NewProgramContent()
            {
              ProgramName = "aaa2RO",
              Revision = -2,
              Comment = "a 2 RO rev -2",
              ProgramContent = "aa 2 RO rev-2",
            },
            new MachineFramework.NewProgramContent()
            {
              ProgramName = "aaa2FC",
              Revision = -2,
              Comment = "a 2 FC rev -2",
              ProgramContent = "aa 2 FC rev-2",
            },
            new MachineFramework.NewProgramContent()
            {
              ProgramName = "zzz3FC",
              Revision = -1,
              Comment = "z 3 RO rev -1",
              ProgramContent = "zz 3 FC rev-1",
            },
            new MachineFramework.NewProgramContent()
            {
              ProgramName = "zzz4RO",
              Revision = -1,
              Comment = "z 4 RO rev -1",
              ProgramContent = "zz 4 RO rev-1",
            },
          }
        ),
      };

      AddJobs(newJobs, expectNewRoute: false); // no route yet because no material

      // plan quantity of 9, but add 10 parts
      var addTime = DateTime.UtcNow;
      using (var db = _logDBCfg.OpenConnection())
      {
        for (int i = 0; i < 10; i++)
        {
          var m = db.AllocateMaterialIDForCasting("aaa");
          db.RecordWorkorderForMaterialID(m, 0, i % 2 == 0 ? "work1" : "work2");
          db.RecordAddMaterialToQueue(
            new EventLogMaterial()
            {
              MaterialID = m,
              Process = 0,
              Face = 0,
            },
            "castingQ",
            -1,
            "theoperator",
            "testsuite",
            addTime
          );
        }
      }

      ExpectNewRoute();

      var logs = Run();
      var byMat = logs.Where(e => e.Material.Any()).ToLookup(e => e.Material.First().MaterialID);

      byMat.Count.ShouldBe(9);
      foreach (var m in byMat)
      {
        CheckSingleMaterial(
          m,
          matId: m.Key,
          uniq: "aaa-0NovSzGF9VZGS00b_",
          part: "aaa",
          numProc: 4,
          castingQueue: "castingQ",
          queue: "Transfer",
          pals: new[]
          {
            new[] { 2, 3, 4 },
            new[] { 5, 6, 7, 8, 9, 10 },
            new[] { 12, 13, 14, 15, 16 },
            new[] { 11 },
          },
          machGroups: new[] { new[] { "RO" }, new[] { "RO", "FC" }, new[] { "FC" }, new[] { "RO" } },
          progs: m.Key % 2 == 1 // material ids start at 1, so odd is first
            ? new[]
            {
              new[] { (prog: "aaa1RO", rev: 1L) },
              new[] { (prog: "aaa2RO", rev: 1L), (prog: "aaa2FC", rev: 1L) },
              new[] { (prog: "aaa3FC", rev: 1L) },
              new[] { (prog: "aaa4RO", rev: 1L) },
            }
            : new[]
            {
              new[] { (prog: "aaa1RO", rev: 2L) },
              new[] { (prog: "aaa2RO", rev: 2L), (prog: "aaa2FC", rev: 2L) },
              new[] { (prog: "zzz3FC", rev: 1L) },
              new[] { (prog: "zzz4RO", rev: 1L) },
            }
        );
      }

      using (var db = _logDBCfg.OpenConnection())
      {
        db.GetMaterialInAllQueues()
          .ToArray()
          .ShouldBeEquivalentTo(
            new[]
            {
              new QueuedMaterial()
              {
                MaterialID = 10,
                Queue = "castingQ",
                Position = 0,
                Unique = "",
                Workorder = "work2",
                NextProcess = 1,
                Paths = ImmutableDictionary<int, int>.Empty,
                PartNameOrCasting = "aaa",
                NumProcesses = 1,
                AddTimeUTC = addTime,
              },
            }
          );
      }
    }
  }
}
