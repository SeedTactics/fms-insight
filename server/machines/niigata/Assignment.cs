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
using System.Collections.Generic;
using System.Linq;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata
{
  public interface IAssignPallets
  {
    NiigataAction NewPalletChange(CellState materialStatus);
  }

  public class AssignPallets : IAssignPallets
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<AssignPallets>();
    private IRecordFacesForPallet _recordFaces;

    public AssignPallets(IRecordFacesForPallet r)
    {
      _recordFaces = r;
    }

    public const int LoadStepNum = 1;
    public const int MachineStepNum = 2;
    public const int UnloadStepNum = 3;

    public NiigataAction NewPalletChange(CellState cellSt)
    {
      // only need to decide on a single change, SyncPallets will call in a loop until no changes are needed.

      //TODO: check if pallet in BeforeUnload just needs a simple increment before it reaches the load station?

      // first, check if pallet at load station being unloaded needs something loaded
      foreach (var pal in cellSt.Pallets)
      {
        if (pal.Status.CurStation.Location.Location != PalletLocationEnum.LoadUnload) continue;
        if (!(pal.Status.Tracking.BeforeCurrentStep && pal.Status.Tracking.CurrentStepNum == UnloadStepNum)) continue;
        if (pal.Faces.SelectMany(ms => ms.Material).Any(m => m.Action.Type == InProcessMaterialAction.ActionType.Loading)) continue;

        var pathsToLoad = FindMaterialToLoad(cellSt, pal.Status.Master.PalletNum, pal.Status.CurStation.Location.Num, pal.Faces.SelectMany(ms => ms.Material).ToList(), queuedMats: cellSt.QueuedMaterial);
        if (pathsToLoad != null && pathsToLoad.Count > 0)
        {
          return SetNewRoute(pal.Status, pathsToLoad, cellSt.Status.TimeOfStatusUTC);
        }
      }

      // next, check empty stuff in buffer
      foreach (var pal in cellSt.Pallets)
      {
        if (pal.Status.CurStation.Location.Location != PalletLocationEnum.Buffer) continue;
        if (!pal.Status.Master.NoWork) continue;
        if (pal.Faces.Any())
        {
          Log.Error("State mismatch! no work on pallet but it has material {@pal}", pal);
          continue;
        }

        var pathsToLoad = FindMaterialToLoad(cellSt, pal.Status.Master.PalletNum, loadStation: null, matCurrentlyOnPal: Enumerable.Empty<InProcessMaterial>(), queuedMats: cellSt.QueuedMaterial);
        if (pathsToLoad != null && pathsToLoad.Count > 0)
        {
          return SetNewRoute(pal.Status, pathsToLoad, cellSt.Status.TimeOfStatusUTC);
        }
      }

      return null;
    }

    #region Calculate Paths
    private /* record */ class JobPath
    {
      public JobPlan Job { get; set; }
      public int Process { get; set; }
      public int Path { get; set; }
    }

    private IList<JobPath> FindPathsForPallet(CellState cellSt, int pallet, int? loadStation)
    {
      return
        cellSt.Schedule.Jobs
        .Where(j =>
          cellSt.JobQtyStarted[j.UniqueStr] < Enumerable.Range(1, j.GetNumPaths(process: 1)).Sum(path => j.GetPlannedCyclesOnFirstProcess(path))
        )
        .SelectMany(job => Enumerable.Range(1, job.NumProcesses).Select(proc => new { job, proc }))
        .SelectMany(j => Enumerable.Range(1, j.job.GetNumPaths(j.proc)).Select(path => new JobPath { Job = j.job, Process = j.proc, Path = path }))
        .Where(j => loadStation == null || j.Job.LoadStations(j.Process, j.Path).Contains(loadStation.Value))
        .Where(j => j.Job.PlannedPallets(j.Process, j.Path).Contains(pallet.ToString()))
        .OrderBy(j => j.Job.RouteStartingTimeUTC)
        .ThenByDescending(j => j.Job.Priority)
        .ThenBy(j => j.Job.GetSimulatedStartingTimeUTC(j.Process, j.Path))
        .ToList()
        ;
    }

    private bool PathAllowedOnPallet(IEnumerable<JobPath> alreadyLoading, JobPath potentialNewPath)
    {
      var seenFaces = new HashSet<string>();
      var newFixture = potentialNewPath.Job.PlannedFixtures(potentialNewPath.Process, potentialNewPath.Path).FirstOrDefault();
      foreach (var otherPath in alreadyLoading)
      {
        var otherFixFace = otherPath.Job.PlannedFixtures(otherPath.Process, otherPath.Path).FirstOrDefault();
        if (!string.IsNullOrEmpty(newFixture.Fixture) && newFixture.Fixture != otherFixFace.Fixture)
          return false;
        seenFaces.Add(otherFixFace.Face);
      }

      if (seenFaces.Contains(newFixture.Face))
        return false;

      return true;
    }

    private static HashSet<T> ToHashSet<T>(IEnumerable<T> ts)
    {
      var s = new HashSet<T>();
      foreach (var t in ts) s.Add(t);
      return s;
    }

    private (bool, HashSet<long>) CheckMaterialForPathExists(HashSet<long> currentlyLoading, IReadOnlyDictionary<long, InProcessMaterial> unusedMatsOnPal, JobPath path, IEnumerable<InProcessMaterial> queuedMats)
    {
      var fixFace = path.Job.PlannedFixtures(path.Process, path.Path).FirstOrDefault();
      int.TryParse(fixFace.Face, out int faceNum);

      var inputQueue = path.Job.GetInputQueue(path.Process, path.Path);
      if (path.Process == 1 && string.IsNullOrEmpty(inputQueue))
      {
        // no input queue, just new parts
        return (true, new HashSet<long>());
      }
      else if (path.Process == 1 && !string.IsNullOrEmpty(inputQueue))
      {
        // load castings
        var castings =
          queuedMats
          .Where(m => m.Location.CurrentQueue == inputQueue
                    && m.Process == 0
                    && ((string.IsNullOrEmpty(m.JobUnique) && m.PartName == path.Job.PartName)
                       || (!string.IsNullOrEmpty(m.JobUnique) && m.JobUnique == path.Job.UniqueStr)
                       )
                    && !currentlyLoading.Contains(m.MaterialID)
          )
          .OrderBy(m => m.Location.QueuePosition)
          .ToList();

        if (castings.Count >= path.Job.PartsPerPallet(path.Process, path.Path))
        {
          return (true, ToHashSet(castings.Take(path.Job.PartsPerPallet(path.Process, path.Path)).Select(m => m.MaterialID)));
        }
        else
        {
          return (false, null);
        }
      }
      else if (path.Process > 1)
      {
        var countToLoad = path.Job.PartsPerPallet(path.Process, path.Path);
        // first check mat on pal
        var availMatIds = new HashSet<long>();
        foreach (var mat in unusedMatsOnPal.Values)
        {
          if (mat.JobUnique == path.Job.UniqueStr
            && mat.Process + 1 == path.Process
            && path.Job.GetPathGroup(mat.Process, mat.Path) == path.Job.GetPathGroup(path.Process, path.Path)
            && !currentlyLoading.Contains(mat.MaterialID)
          )
          {
            availMatIds.Add(mat.MaterialID);
            if (availMatIds.Count == countToLoad)
            {
              return (true, availMatIds);
            }
          }
        }

        // now check queue
        if (!string.IsNullOrEmpty(inputQueue))
        {
          foreach (var mat in queuedMats)
          {
            if (mat.Location.CurrentQueue != inputQueue) continue;
            if (mat.JobUnique != path.Job.UniqueStr) continue;
            if (mat.Process + 1 != path.Process) continue;
            if (mat.Path != -1 && path.Job.GetPathGroup(mat.Process, mat.Path) != path.Job.GetPathGroup(path.Process, path.Path))
              continue;
            if (currentlyLoading.Contains(mat.MaterialID)) continue;

            availMatIds.Add(mat.MaterialID);
            if (availMatIds.Count == countToLoad)
            {
              return (true, availMatIds);
            }
          }
        }
      }

      return (false, null);
    }

    private IReadOnlyList<JobPath> FindMaterialToLoad(CellState cellSt, int pallet, int? loadStation, IEnumerable<InProcessMaterial> matCurrentlyOnPal, IEnumerable<InProcessMaterial> queuedMats)
    {
      List<JobPath> paths = null;
      var allPaths = FindPathsForPallet(cellSt, pallet, loadStation);
      var unusedMatsOnPal = matCurrentlyOnPal.ToDictionary(m => m.MaterialID);
      var currentlyLoading = new HashSet<long>();
      foreach (var path in allPaths)
      {
        var (hasMat, usedMatIds) = CheckMaterialForPathExists(currentlyLoading, unusedMatsOnPal, path, queuedMats);
        if (!hasMat) continue;

        if (paths == null)
        {
          foreach (var matId in usedMatIds)
          {
            unusedMatsOnPal.Remove(matId);
            currentlyLoading.Add(matId);
          }
          // first path with material gets set
          paths = new List<JobPath> { path };
        }
        else
        {
          // later paths need to make sure they are compatible.
          if (PathAllowedOnPallet(paths, path))
          {
            foreach (var matId in usedMatIds)
            {
              unusedMatsOnPal.Remove(matId);
              currentlyLoading.Add(matId);
            }
            paths.Add(path);
          }
        }
      }
      return paths;
    }
    #endregion

    #region Set New Route
    private NiigataAction SetNewRoute(PalletStatus oldPallet, IReadOnlyList<JobPath> newPaths, DateTime nowUtc)
    {
      var newMaster = NewPalletMaster(oldPallet.Master.PalletNum, newPaths);
      if (SimpleQuantityChange(oldPallet.Master, newMaster))
      {
        int remaining = 1;
        if (oldPallet.Tracking.CurrentStepNum == UnloadStepNum)
        {
          remaining = 2;
        }
        return new UpdatePalletQuantities()
        {
          Pallet = oldPallet.Master.PalletNum,
          Priority = 0,
          Cycles = remaining,
          NoWork = false,
          Skip = false,
          ForLongTool = false
        };
      }
      else
      {
        long pendingId = DateTime.UtcNow.Ticks;
        newMaster.Comment = pendingId.ToString();
        _recordFaces.Save(newMaster.PalletNum, pendingId.ToString(), nowUtc, newPaths.Select((path, idx) =>
          new AssignedJobAndPathForFace()
          {
            Face = idx + 1,
            Unique = path.Job.UniqueStr,
            Proc = path.Process,
            Path = path.Path
          }
        ));
        return new NewPalletRoute()
        {
          PendingID = pendingId,
          NewMaster = newMaster
        };
      }

    }

    private PalletMaster NewPalletMaster(int pallet, IReadOnlyList<JobPath> newPaths)
    {
      var orderedPaths = newPaths.OrderBy(p => p.Job.UniqueStr).ThenBy(p => p.Process).ThenBy(p => p.Path).ToList();
      var machineStops = orderedPaths.Select(p => p.Job.GetMachiningStop(p.Process, p.Path).First());

      var firstPath = orderedPaths.First();
      var firstStop = machineStops.First();

      return new PalletMaster()
      {
        PalletNum = pallet,
        Comment = "",
        RemainingPalletCycles = 1,
        Priority = 0,
        NoWork = false,
        Skip = false,
        ForLongToolMaintenance = false,
        Routes = new List<RouteStep>() {
          new LoadStep() {
            LoadStations = firstPath.Job.LoadStations(firstPath.Process, firstPath.Path).ToList(),
          },
          new MachiningStep() {
            Machines = firstStop.Stations().ToList(),
            ProgramNumsToRun = newPaths.Select(p => p.Job.GetMachiningStop(p.Process, p.Path).First().AllPrograms().First().Program).Select(int.Parse).ToList(),
          },
          new UnloadStep() {
            UnloadStations = firstPath.Job.UnloadStations(firstPath.Process, firstPath.Path).ToList(),
            CompletedPartCount = 1
          }
        }
      };
    }

    private bool SimpleQuantityChange(PalletMaster existing, PalletMaster newPal)
    {
      if (existing.Routes.Count != 3) return false;
      if (newPal.Routes.Count != 3) return false;

      if (!(existing.Routes[0] is LoadStep)) return false;
      if (!(newPal.Routes[0] is LoadStep)) return false;
      if (!((LoadStep)existing.Routes[0]).LoadStations.SequenceEqual(((LoadStep)newPal.Routes[0]).LoadStations)) return false;

      if (!(existing.Routes[1] is MachiningStep)) return false;
      if (!(newPal.Routes[1] is MachiningStep)) return false;
      var eM = (MachiningStep)existing.Routes[1];
      var nM = (MachiningStep)newPal.Routes[1];
      if (!eM.Machines.SequenceEqual(nM.Machines)) return false;
      if (!eM.ProgramNumsToRun.SequenceEqual(nM.ProgramNumsToRun)) return false;

      if (!(existing.Routes[2] is UnloadStep)) return false;
      if (!(newPal.Routes[2] is UnloadStep)) return false;
      if (!((UnloadStep)existing.Routes[2]).UnloadStations.SequenceEqual(((UnloadStep)newPal.Routes[2]).UnloadStations)) return false;

      return true;
    }
    #endregion
  }
}