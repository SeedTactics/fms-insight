/* Copyright (c) 2024, John Lenz

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
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Makino
{
  internal class MakinoToJobMap(IRepository db)
  {
    #region Jobs
    private readonly Dictionary<int, Job> _byPartID = []; // part id => JobPlan
    private readonly Dictionary<int, int> _procIDToProcNum = []; // process id => index/process number
    private readonly Dictionary<int, int> _procIDToPartID = []; // process id => part id
    private readonly Dictionary<int, int> _jobIDToNum = []; // jobid => index/job number
    private readonly Dictionary<int, int> _jobIDToProcID = []; // job id => process id
    private readonly Dictionary<int, string> _programs = []; // job id => list of programs
    private readonly Dictionary<int, MachiningStop> _stops = []; // job id => machining stop
    private readonly Dictionary<int, ActiveJob> _byOrderID = []; // order id => insightjob

    private readonly Dictionary<int, int> _fixPalIDToFixNum = []; // fixture pallet id => fixture num
    private readonly Dictionary<int, int> _fixPalIDToFixID = []; // fixture pallet id => fixture id
    private readonly Dictionary<int, int> _fixPalIDToPalNum = []; // fixture pallet id => pallet num
    private readonly Dictionary<int, List<InProcessMaterial>> _fixPalIDToMaterial = []; // fixture pallet id => list of material
    private readonly Dictionary<int, List<int>> _fixIDToPallets = []; // fixture id => list of pallets
    private readonly Dictionary<int, int> _palletIdToPalletNum = []; // pallet id => pallet num
    private readonly Dictionary<int, PalletStatus> _pallets = []; // pallet num => pallet status

    public IEnumerable<ActiveJob> Jobs
    {
      get { return _byOrderID.Values; }
    }

    public void AddProcess(int partID, int processNum, int processID)
    {
      _procIDToProcNum.Add(processID, processNum);
      _procIDToPartID.Add(processID, partID);
    }

    public void CreateJob(string unique, int partID, string partName, string? comment)
    {
      int numProc = 1;
      foreach (var p in _procIDToPartID)
      {
        if (p.Value == partID)
        {
          numProc = Math.Max(numProc, _procIDToProcNum[p.Key]);
        }
      }
      _byPartID.Add(
        partID,
        new Job()
        {
          UniqueStr = unique,
          PartName = partName,
          Comment = comment,
          ManuallyCreated = false,
          RouteStartUTC = DateTime.MinValue,
          RouteEndUTC = DateTime.MinValue,
          Archived = false,
          Processes = Enumerable
            .Range(1, numProc)
            .Select(procNum => new ProcessInfo()
            {
              Paths =
              [
                new ProcPathInfo()
                {
                  PalletNums = [],
                  Load = [],
                  Unload = [],
                  Stops = [],
                  PartsPerPallet = 1,
                  SimulatedStartingUTC = DateTime.MinValue,
                  SimulatedAverageFlowTime = TimeSpan.Zero,
                  ExpectedLoadTime = TimeSpan.Zero,
                  ExpectedUnloadTime = TimeSpan.Zero,
                }
              ]
            })
            .ToImmutableList(),
          Cycles = 0
        }
      );
    }

    public void AddJobToProcess(int processID, int jobNumber, int jobID)
    {
      _jobIDToNum.Add(jobID, jobNumber);
      _jobIDToProcID.Add(jobID, processID);
    }

    public void AddProgramToJob(int jobID, string program)
    {
      _programs[jobID] = program;
    }

    public void AddAllowedStationToJob(int jobID, PalletLocation loc)
    {
      if (loc.Location == PalletLocationEnum.LoadUnload)
      {
        var procID = _jobIDToProcID[jobID];
        var proc = _procIDToProcNum[procID];
        var job = _byPartID[_procIDToPartID[procID]];

        if (_jobIDToNum[jobID] == 1)
        {
          _byPartID[_procIDToPartID[procID]] = job.AdjustPath(
            proc,
            1,
            p => p with { Load = p.Load.Add(loc.Num) }
          );
        }
        else
        {
          _byPartID[_procIDToPartID[procID]] = job.AdjustPath(
            proc,
            1,
            p => p with { Unload = p.Unload.Add(loc.Num) }
          );
        }
      }
      else
      {
        if (!_stops.TryGetValue(jobID, out var value))
        {
          _stops.Add(
            jobID,
            new MachiningStop
            {
              StationGroup = loc.StationGroup,
              Program = _programs[jobID],
              Stations = [loc.Num],
              ExpectedCycleTime = TimeSpan.Zero
            }
          );
        }
        else
        {
          var s = value;
          _stops[jobID] = s with { Stations = s.Stations.Add(loc.Num) };
        }
      }
    }

    public void CompleteStations()
    {
      foreach (var proc in _procIDToPartID)
      {
        var procNum = _procIDToProcNum[proc.Key];
        var stops = new SortedList<int, MachiningStop>();

        foreach (var jobStop in _stops)
        {
          if (_jobIDToProcID[jobStop.Key] == proc.Key)
          { //Filter only the jobs on this processID
            stops.Add(_jobIDToNum[jobStop.Key], jobStop.Value);
          }
        }

        if (stops.Count > 0)
        {
          _byPartID[proc.Value] = _byPartID[proc.Value]
            .AdjustPath(procNum, 1, d => d with { Stops = stops.Values.ToImmutableList() });
        }
      }
    }

    public void AddFixtureToProcess(int processID, int fixtureID)
    {
      IEnumerable<int> pals;
      if (_fixIDToPallets.TryGetValue(fixtureID, out var value))
        pals = value;
      else
        return;

      var procNum = _procIDToProcNum[processID];

      _byPartID[_procIDToPartID[processID]] = _byPartID[_procIDToPartID[processID]]
        .AdjustPath(procNum, 1, p => p with { PalletNums = p.PalletNums.AddRange(pals) });
    }

    public void AddOperationToProcess(int processID, int clampQty)
    {
      var procNum = _procIDToProcNum[processID];

      _byPartID[_procIDToPartID[processID]] = _byPartID[_procIDToPartID[processID]]
        .AdjustPath(procNum, 1, p => p with { PartsPerPallet = clampQty });
    }

    public void DuplicateForOrder(int orderID, string order, int partID, Func<ActiveJob, ActiveJob> extra)
    {
      var job = _byPartID[partID];
      var historic = db.LoadJob(order);
      var newJob = job.CloneToDerived<ActiveJob, Job>() with
      {
        UniqueStr = order,
        RouteStartUTC = historic?.RouteStartUTC ?? DateTime.Today,
        RouteEndUTC = historic?.RouteEndUTC ?? DateTime.Today,
        Comment = historic?.Comment ?? job.Comment,
        ScheduleId = historic?.ScheduleId ?? null,
        BookingIds = historic?.BookingIds ?? null,
        ManuallyCreated = historic?.ManuallyCreated ?? false,
        CopiedToSystem = historic?.CopiedToSystem ?? true,
        Decrements = db.LoadDecrementsForJob(order),
        AssignedWorkorders = db.GetWorkordersForUnique(order),
        Completed = job.Processes.Select(_ => ImmutableList.Create(0)).ToImmutableList(),

        Processes =
          historic != null && historic.Processes.Count == job.Processes.Count
            ? Enumerable
              .Zip(
                historic.Processes,
                job.Processes,
                (h, j) =>
                  new ProcessInfo()
                  {
                    Paths = j
                      .Paths.Select(
                        (jpath, pathIdx) =>
                        {
                          if (pathIdx >= h.Paths.Count)
                          {
                            return jpath;
                          }
                          var hpath = h.Paths[pathIdx];
                          return jpath with
                          {
                            Stops = jpath
                              .Stops.Select(
                                (jstop, stopIdx) =>
                                {
                                  if (stopIdx >= hpath.Stops.Count)
                                  {
                                    return jstop;
                                  }
                                  else
                                  {
                                    return jstop with
                                    {
                                      ExpectedCycleTime = hpath.Stops[stopIdx].ExpectedCycleTime
                                    };
                                  }
                                }
                              )
                              .ToImmutableList(),
                            SimulatedStartingUTC = hpath.SimulatedStartingUTC,
                            SimulatedProduction = hpath.SimulatedProduction,
                            SimulatedAverageFlowTime = hpath.SimulatedAverageFlowTime,
                            ExpectedLoadTime = hpath.ExpectedLoadTime,
                            ExpectedUnloadTime = hpath.ExpectedUnloadTime,
                            Inspections = hpath.Inspections,
                            Casting = hpath.Casting,
                            InputQueue = hpath.InputQueue,
                            OutputQueue = hpath.OutputQueue,
                          };
                        }
                      )
                      .ToImmutableList()
                  }
              )
              .ToImmutableList()
            : job.Processes
      };

      _byOrderID.Add(orderID, extra(newJob));
    }

    public void AddQuantityToProcess(int orderID, int processID, int remaining, int completed, int scrap)
    {
      var job = _byOrderID[orderID];
      var procNum = _procIDToProcNum[processID];

      _byOrderID[orderID] = job with
      {
        RemainingToStart = procNum == 1 ? remaining : job.RemainingToStart,
        Completed = job.Completed!.SetItem(procNum - 1, [completed + scrap])
      };
    }

    public ActiveJob? JobForOrder(int orderID)
    {
      if (_byOrderID.TryGetValue(orderID, out var value))
        return value;
      else
        return null;
    }

    public int ProcessForJobID(int jobID)
    {
      var procID = _jobIDToProcID[jobID];
      return _procIDToProcNum[procID];
    }
    #endregion

    #region Pallets
    public IDictionary<int, PalletStatus> Pallets
    {
      get { return _pallets; }
    }

    public IEnumerable<InProcessMaterial> Material
    {
      get { return _fixPalIDToMaterial.SelectMany(x => x.Value); }
    }

    public void AddPalletInfo(
      int palletID,
      int fixutrePalletID,
      int fixtureNum,
      int fixtureID,
      int palletNum,
      PalletLocation loc
    )
    {
      _palletIdToPalletNum[palletID] = palletNum;
      _fixPalIDToFixNum.Add(fixutrePalletID, fixtureNum);
      _fixPalIDToFixID.Add(fixutrePalletID, fixtureID);
      _fixPalIDToPalNum.Add(fixutrePalletID, palletNum);
      if (!_fixIDToPallets.ContainsKey(fixtureID))
        _fixIDToPallets.Add(fixtureID, []);
      _fixIDToPallets[fixtureID].Add(palletNum);

      if (_pallets.TryGetValue(palletNum, out PalletStatus? value) && value != null)
      {
        _pallets[palletNum] = value with { NumFaces = Math.Max(value.NumFaces, fixtureNum) };
        return;
      }

      PalletStatus pal;

      pal = new PalletStatus()
      {
        PalletNum = palletNum,
        CurrentPalletLocation = loc,
        NumFaces = fixtureNum,
        FixtureOnPallet = "",
        OnHold = false,
      };

      _pallets.Add(palletNum, pal);
    }

    public void PalletLocInfo(int fixturePalletID, out int palletNum, out int fixNum)
    {
      palletNum = _fixPalIDToPalNum[fixturePalletID];
      fixNum = _fixPalIDToFixNum[fixturePalletID];
    }

    public void AddMaterial(
      int fixturePalletID,
      int orderID,
      int processID,
      int palletNum,
      int fixtureNum,
      int? curJobID,
      int? lastMachiningJobID,
      int? tablePalletID,
      int? machineAction,
      int? runJob,
      long matID,
      string? serial,
      string? workorder
    )
    {
      var job = _byOrderID[orderID];

      var action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting, };

      if (
        curJobID.HasValue
        && machineAction == 1 // machine action = 1 means machine running
        && runJob == 1
        && _stops.TryGetValue(curJobID.Value, out var stop)
        && tablePalletID.HasValue
        && _palletIdToPalletNum[tablePalletID.Value] == palletNum
      )
      {
        action = action with { Type = InProcessMaterialAction.ActionType.Machining, Program = stop.Program, };
      }

      var mat = new InProcessMaterial()
      {
        MaterialID = matID,
        JobUnique = job.UniqueStr,
        Process = _procIDToProcNum[processID],
        Path = 1,
        PartName = job.PartName,
        Serial = serial,
        WorkorderId = workorder,
        SignaledInspections = db.LookupInspectionDecisions(matID)
          .Where(x => x.Inspect)
          .Select(x => x.InspType)
          .Distinct()
          .ToImmutableList(),
        QuarantineAfterUnload = null,
        LastCompletedMachiningRouteStopIndex =
          lastMachiningJobID.HasValue && lastMachiningJobID.Value > 0
            // job num 1 is load, 2 is machining so subtract 2 to get the index
            ? _jobIDToNum[lastMachiningJobID.Value] - 2
            : null,
        Action = action,
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.OnPallet,
          PalletNum = palletNum,
          Face = fixtureNum
        }
      };

      List<InProcessMaterial> ms;
      if (_fixPalIDToMaterial.TryGetValue(fixturePalletID, out var value))
        ms = value;
      else
      {
        ms = [];
        _fixPalIDToMaterial.Add(fixturePalletID, ms);
      }

      ms.Add(mat);
    }

    public void SetMaterialAsUnload(int fixturePalletID, bool completed)
    {
      var palletNum = _fixPalIDToPalNum[fixturePalletID];
      var pal = _pallets[palletNum];
      var face = _fixPalIDToFixNum[fixturePalletID].ToString();

      if (_fixPalIDToMaterial.TryGetValue(fixturePalletID, out var value))
      {
        _fixPalIDToMaterial[fixturePalletID] = value
          .Select(mat =>
            mat with
            {
              Action = new InProcessMaterialAction()
              {
                Type = completed
                  ? InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
                  : InProcessMaterialAction.ActionType.UnloadToInProcess
              }
            }
          )
          .ToList();
      }
    }

    public void AddMaterialToLoad(int fixturePalletID, string unique, string partName, int procNum, int qty)
    {
      var palletNum = _fixPalIDToPalNum[fixturePalletID];
      var pal = _pallets[palletNum];
      var face = _fixPalIDToFixNum[fixturePalletID];

      List<InProcessMaterial> ms;
      if (_fixPalIDToMaterial.TryGetValue(fixturePalletID, out var value))
        ms = value;
      else
      {
        ms = [];
        _fixPalIDToMaterial.Add(fixturePalletID, ms);
      }

      for (var i = 0; i < qty; i++)
      {
        ms.Add(
          new InProcessMaterial()
          {
            MaterialID = -1,
            JobUnique = unique,
            PartName = partName,
            Process = procNum,
            Path = 1,
            Location = new InProcessMaterialLocation() { Type = InProcessMaterialLocation.LocType.Free, },
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Loading,
              ProcessAfterLoad = procNum,
              PathAfterLoad = 1,
              LoadOntoFace = face,
              LoadOntoPalletNum = pal.PalletNum
            },
            SignaledInspections = [],
            QuarantineAfterUnload = null,
          }
        );
      }
    }

    public void SetPalletOnRotary(int machine, int palletID)
    {
      if (
        _palletIdToPalletNum.TryGetValue(palletID, out var palletNum)
        && _pallets.TryGetValue(palletNum, out var value)
      )
      {
        if (
          value.CurrentPalletLocation.Location == PalletLocationEnum.Machine
          && value.CurrentPalletLocation.Num == machine
        )
        {
          _pallets[palletNum] = value with
          {
            CurrentPalletLocation = new PalletLocation(
              PalletLocationEnum.MachineQueue,
              value.CurrentPalletLocation.StationGroup,
              value.CurrentPalletLocation.Num
            )
          };
        }
        else
        {
          Serilog.Log.Debug(
            "Mismatch in setting pallet on rotary, pallet {palletID} is not on machine {machine}, old {@pal}",
            palletID,
            machine,
            value
          );
        }
      }
    }
    #endregion
  }
}
