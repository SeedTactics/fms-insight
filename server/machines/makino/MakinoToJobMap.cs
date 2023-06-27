/* Copyright (c) 2018, John Lenz

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
using System.Linq;
using System.Collections.Immutable;
using BlackMaple.MachineFramework;

namespace Makino
{
  internal class MakinoToJobMap
  {
    /*Maps we have:
     *
     * part id => JobPlan
     *
     * process id => index/process number
     *
     * process id => part id
     *
     * jobid => index/job number
     *
     * job id => process id
     *
     * job id => list of programs
     *
     * job id => machining stop
     *
     * fixture id => list of process ids
     *
     */
    private Dictionary<int, Job> _byPartID = new Dictionary<int, Job>();
    private Dictionary<int, int> _procIDToProcNum = new Dictionary<int, int>();
    private Dictionary<int, int> _procIDToPartID = new Dictionary<int, int>();
    private Dictionary<int, int> _jobIDToNum = new Dictionary<int, int>();
    private Dictionary<int, int> _jobIDToProcID = new Dictionary<int, int>();
    private Dictionary<int, string> _programs = new Dictionary<int, string>();
    private Dictionary<int, MachiningStop> _stops = new Dictionary<int, MachiningStop>();
    private Dictionary<int, ActiveJob> _byOrderID = new Dictionary<int, ActiveJob>();

    private IRepository _logDb;

    public MakinoToJobMap(IRepository log)
    {
      _logDb = log;
    }

    public IEnumerable<ActiveJob> Jobs
    {
      get { return _byOrderID.Values; }
    }

    public void AddProcess(int partID, int processNum, int processID)
    {
      _procIDToProcNum.Add(processID, processNum);
      _procIDToPartID.Add(processID, partID);
    }

    public void CreateJob(string unique, int partID, string partName, string comment)
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
        new BlackMaple.MachineFramework.Job()
        {
          UniqueStr = unique,
          PartName = partName,
          Comment = comment,
          ManuallyCreated = false,
          RouteStartUTC = DateTime.MinValue,
          RouteEndUTC = DateTime.MinValue,
          Archived = false,
          Processes = ImmutableList<ProcessInfo>.Empty,
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
        var jobNum = _jobIDToNum[jobID];
        var proc = _procIDToProcNum[_jobIDToProcID[jobID]];
        var job = _byPartID[_procIDToPartID[_jobIDToProcID[jobID]]];

        if (jobNum == 1)
        {
          _byPartID[_procIDToPartID[_jobIDToProcID[jobID]]] = job.AdjustPath(
            proc,
            1,
            p => p with { Load = p.Load.Add(loc.Num) }
          );
        }
        else
        {
          _byPartID[_procIDToPartID[_jobIDToProcID[jobID]]] = job.AdjustPath(
            proc,
            1,
            p => p with { Unload = p.Unload.Add(loc.Num) }
          );
        }
      }
      else
      {
        if (!_stops.ContainsKey(jobID))
        {
          _stops.Add(
            jobID,
            new MachiningStop
            {
              StationGroup = "MC",
              Program = _programs[jobID],
              Stations = ImmutableList.Create(loc.Num),
              ExpectedCycleTime = TimeSpan.Zero
            }
          );
        }
        else
        {
          var s = _stops[jobID];
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
          _byPartID[proc.Value] = _byPartID[proc.Value].AdjustPath(
            procNum,
            1,
            d => d with { Stops = d.Stops.AddRange(stops.Values) }
          );
      }
    }

    public void AddFixtureToProcess(int processID, int fixtureID, IEnumerable<int> pals)
    {
      var procNum = _procIDToProcNum[processID];

      if (pals == null)
        return;

      _byPartID[_procIDToPartID[processID]] = _byPartID[_procIDToPartID[processID]].AdjustPath(
        procNum,
        1,
        p => p with { PalletNums = p.PalletNums.AddRange(pals) }
      );
    }

    public BlackMaple.MachineFramework.ActiveJob DuplicateForOrder(int orderID, string order, int partID)
    {
      var job = _byPartID[partID];
      var newJob = job.CloneToDerived<ActiveJob, Job>() with
      {
        UniqueStr = order,
        Completed = job.Processes.Select(_ => ImmutableList.Create(0)).ToImmutableList()
      };
      _byOrderID.Add(orderID, newJob);
      return newJob;
    }

    public void AddQuantityToProcess(int orderID, int processID, int completed)
    {
      var job = _byOrderID[orderID];
      var procNum = _procIDToProcNum[processID];

      _byOrderID[orderID] = job with
      {
        Completed = job.Completed.SetItem(procNum - 1, job.Completed[procNum - 1])
      };
    }

    public InProcessMaterial CreateMaterial(
      int orderID,
      int processID,
      int jobID,
      int palletNum,
      int face,
      long matID
    )
    {
      var job = _byOrderID[orderID];
      var program = "";
      if (_programs.ContainsKey(jobID))
        program = _programs[jobID];

      var matDetails = _logDb.GetMaterialDetails(matID);
      return new InProcessMaterial()
      {
        MaterialID = matID,
        JobUnique = job.UniqueStr,
        Process = _procIDToProcNum[processID],
        Path = 1,
        PartName = job.PartName,
        Serial = matDetails?.Serial,
        WorkorderId = matDetails?.Workorder,
        SignaledInspections = _logDb
          .LookupInspectionDecisions(matID)
          .Where(x => x.Inspect)
          .Select(x => x.InspType)
          .Distinct()
          .ToImmutableList(),
        Action = new InProcessMaterialAction()
        {
          Type = InProcessMaterialAction.ActionType.Waiting,
          Program = program
        },
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.OnPallet,
          PalletNum = palletNum,
          Face = face
        }
      };
    }

    public BlackMaple.MachineFramework.ActiveJob JobForOrder(int orderID)
    {
      if (_byOrderID.ContainsKey(orderID))
        return _byOrderID[orderID];
      else
        return null;
    }

    public int ProcessForJobID(int jobID)
    {
      var procID = _jobIDToProcID[jobID];
      return _procIDToProcNum[procID];
    }
  }

  internal class MakinoToPalletMap
  {
    /* Maps
     *
     * fixturePalletId => index/fixture num on the pallet
     *
     * fixturePalletID => fixtureID
     *
     * fixturePalletId => PalletNum
         *
         * fixturePalletId => list of material
     *
     * fixtureID => list of pallets
     *
     * pallet => PalletStatus
     */

    private Dictionary<int, int> _fixPalIDToFixNum = new Dictionary<int, int>();
    private Dictionary<int, int> _fixPalIDToFixID = new Dictionary<int, int>();
    private Dictionary<int, int> _fixPalIDToPalNum = new Dictionary<int, int>();
    private Dictionary<int, List<InProcessMaterial>> _fixPalIDToMaterial =
      new Dictionary<int, List<InProcessMaterial>>();
    private Dictionary<int, List<int>> _fixIDToPallets = new Dictionary<int, List<int>>();
    private Dictionary<int, PalletStatus> _pallets = new Dictionary<int, PalletStatus>();

    public IDictionary<int, PalletStatus> Pallets
    {
      get { return _pallets; }
    }

    public IEnumerable<InProcessMaterial> Material
    {
      get { return _fixPalIDToMaterial.SelectMany(x => x.Value); }
    }

    public void AddPalletInfo(
      int fixutrePalletID,
      int fixtureNum,
      int fixtureID,
      int palletNum,
      PalletLocation loc
    )
    {
      _fixPalIDToFixNum.Add(fixutrePalletID, fixtureNum);
      _fixPalIDToFixID.Add(fixutrePalletID, fixtureID);
      _fixPalIDToPalNum.Add(fixutrePalletID, palletNum);
      if (!_fixIDToPallets.ContainsKey(fixtureID))
        _fixIDToPallets.Add(fixtureID, new List<int>());
      _fixIDToPallets[fixtureID].Add(palletNum);

      if (_pallets.ContainsKey(palletNum))
      {
        _pallets[palletNum] %= p => p.NumFaces = Math.Max(p.NumFaces, fixtureNum);
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

    public IEnumerable<int> PalletsForFixture(int fixtureID)
    {
      if (_fixIDToPallets.ContainsKey(fixtureID))
        return _fixIDToPallets[fixtureID];
      else
        return new int[] { };
    }

    public void PalletLocInfo(int fixturePalletID, out int palletNum, out int fixNum)
    {
      palletNum = _fixPalIDToPalNum[fixturePalletID];
      fixNum = _fixPalIDToFixNum[fixturePalletID];
    }

    public void AddMaterial(int fixturePalletID, InProcessMaterial mat)
    {
      List<InProcessMaterial> ms;
      if (_fixPalIDToMaterial.ContainsKey(fixturePalletID))
        ms = _fixPalIDToMaterial[fixturePalletID];
      else
      {
        ms = new List<InProcessMaterial>();
        _fixPalIDToMaterial.Add(fixturePalletID, ms);
      }

      var palletNum = _fixPalIDToPalNum[fixturePalletID];
      var pal = _pallets[palletNum];

      if (pal.CurrentPalletLocation.Location == PalletLocationEnum.Machine && mat.Action.Program != "")
      {
        mat %= m => m.Action.Type = InProcessMaterialAction.ActionType.Machining;
      }
      else
      {
        mat %= m => m.Action.Program = "";
      }

      ms.Add(mat);
    }

    public void SetMaterialAsUnload(int fixturePalletID, bool completed)
    {
      var palletNum = _fixPalIDToPalNum[fixturePalletID];
      var pal = _pallets[palletNum];
      var face = _fixPalIDToFixNum[fixturePalletID].ToString();

      if (_fixPalIDToMaterial.ContainsKey(fixturePalletID))
      {
        _fixPalIDToMaterial[fixturePalletID] = _fixPalIDToMaterial[fixturePalletID]
          .Select(
            mat =>
              mat
              % (
                draft =>
                  draft.SetAction(
                    new InProcessMaterialAction()
                    {
                      Type = completed
                        ? InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
                        : InProcessMaterialAction.ActionType.UnloadToInProcess
                    }
                  )
              )
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
      if (_fixPalIDToMaterial.ContainsKey(fixturePalletID))
        ms = _fixPalIDToMaterial[fixturePalletID];
      else
      {
        ms = new List<InProcessMaterial>();
        _fixPalIDToMaterial.Add(fixturePalletID, ms);
      }

      var mat = new InProcessMaterial()
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
        SignaledInspections = ImmutableList<string>.Empty
      };

      ms.Add(mat);
    }
  }
}
