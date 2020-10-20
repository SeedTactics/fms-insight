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
using System.Linq;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata
{
  public interface IAssignPallets
  {
    NiigataAction NewPalletChange(CellState materialStatus);
  }

  public class MultiPalletAssign : IAssignPallets
  {
    private readonly IEnumerable<IAssignPallets> _assignments;
    public MultiPalletAssign(IEnumerable<IAssignPallets> a) => _assignments = a;
    public NiigataAction NewPalletChange(CellState materialStatus)
    {
      foreach (var a in _assignments)
      {
        var action = a.NewPalletChange(materialStatus);
        if (action != null)
        {
          return action;
        }
      }
      return null;
    }
  }

  public class AssignNewRoutesOnPallets : IAssignPallets
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<AssignNewRoutesOnPallets>();
    private readonly NiigataStationNames _statNames;

    public AssignNewRoutesOnPallets(NiigataStationNames n)
    {
      _statNames = n;
    }

    public NiigataAction NewPalletChange(CellState cellSt)
    {
      // only need to decide on a single change, SyncPallets will call in a loop until no changes are needed.

      // first, check if any programs are needed
      foreach (var prog in cellSt.ProgramNums)
      {
        if (string.IsNullOrEmpty(prog.Value.CellControllerProgramName))
        {
          return AddProgram(
            existing: cellSt.Status.Programs,
            prog: prog.Value,
            allJobs: cellSt.UnarchivedJobs
          );
        }
      }

      // next, check if any pallet needs to be adjusted because a face is missing material
      foreach (var pal in cellSt.Pallets.Where(pal => pal.CurrentOrLoadingFaces.Any(f => f.FaceIsMissingMaterial)))
      {
        if (pal.CurrentOrLoadingFaces.All(f => f.FaceIsMissingMaterial))
        {
          return SetNoWork(pal);
        }
        else
        {
          // can't do anything until at the load station
          if (pal.Status.CurStation.Location.Location != PalletLocationEnum.LoadUnload) continue;
          if (!(pal.Status.Tracking.BeforeCurrentStep && (pal.Status.CurrentStep is UnloadStep || pal.Status.CurrentStep is LoadStep))) continue;

          // FindMaterialToLoad should correctly (re)detect the faces missing material.
          var pathsToLoad = FindMaterialToLoad(cellSt, pal.Status.Master.PalletNum, pal.Status.CurStation.Location.Num, pal.Material.Select(ms => ms.Mat).ToList(), queuedMats: cellSt.QueuedMaterial);
          if (pathsToLoad != null && pathsToLoad.Count > 0)
          {
            return SetNewRoute(pal, pathsToLoad, cellSt.Status.TimeOfStatusUTC, cellSt.ProgramNums);
          }
        }
      }

      // next, check if pallet at load station being unloaded needs something loaded
      foreach (var pal in cellSt.Pallets)
      {
        if (pal.ManualControl) continue;
        if (pal.Status.Master.Skip) continue;
        if (pal.Status.CurStation.Location.Location != PalletLocationEnum.LoadUnload) continue;
        if (!(pal.Status.Tracking.BeforeCurrentStep && pal.Status.CurrentStep is UnloadStep)) continue;
        if (pal.Material.Any(m => m.Mat.Action.Type == InProcessMaterialAction.ActionType.Loading)) continue;

        var pathsToLoad = FindMaterialToLoad(cellSt, pal.Status.Master.PalletNum, pal.Status.CurStation.Location.Num, pal.Material.Select(ms => ms.Mat).ToList(), queuedMats: cellSt.QueuedMaterial);
        if (pathsToLoad != null && pathsToLoad.Count > 0)
        {
          return SetNewRoute(pal, pathsToLoad, cellSt.Status.TimeOfStatusUTC, cellSt.ProgramNums);
        }
      }

      // next, check empty stuff in buffer
      foreach (var pal in cellSt.Pallets)
      {
        if (pal.ManualControl) continue;
        if (pal.Status.Master.Skip) continue;
        if (pal.Status.CurStation.Location.Location != PalletLocationEnum.Buffer) continue;
        if (pal.Status.HasWork) continue;

        // use empty matCurrentlyOnPal because if FMS Insight thinks there is material, the operator aborted it by setting no-work, overriding the route.
        // thus want to record the material as unloaded when it arrives at load station (may already have been unloaded, Insight just don't know it)
        var pathsToLoad = FindMaterialToLoad(cellSt, pal.Status.Master.PalletNum, loadStation: null, matCurrentlyOnPal: Enumerable.Empty<InProcessMaterial>(), queuedMats: cellSt.QueuedMaterial);
        if (pathsToLoad != null && pathsToLoad.Count > 0)
        {
          return SetNewRoute(pal, pathsToLoad, cellSt.Status.TimeOfStatusUTC, cellSt.ProgramNums);
        }
      }

      // delete old programs
      return CheckForOldPrograms(cellSt);
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
        cellSt.UnarchivedJobs
        .SelectMany(job => Enumerable.Range(1, job.NumProcesses).Select(proc => new { job, proc }))
        .SelectMany(j => Enumerable.Range(1, j.job.GetNumPaths(j.proc)).Select(path => new JobPath { Job = j.job, Process = j.proc, Path = path }))
        .Where(j =>
          j.Process > 1
          ||
          !string.IsNullOrEmpty(j.Job.GetInputQueue(j.Process, j.Path))
          ||
          (cellSt.JobQtyRemainingOnProc1.TryGetValue((uniq: j.Job.UniqueStr, proc1path: j.Path), out int qty) && qty > 0)
        )
        .Where(j => loadStation == null || j.Job.LoadStations(j.Process, j.Path).Contains(loadStation.Value))
        .Where(j => j.Job.PlannedPallets(j.Process, j.Path).Contains(pallet.ToString()))
        .OrderBy(j => j.Job.RouteStartingTimeUTC)
        .ThenBy(j => j.Job.GetSimulatedStartingTimeUTC(j.Process, j.Path))
        .ToList()
        ;
    }

    private bool PathAllowedOnPallet(IEnumerable<JobPath> alreadyLoading, JobPath potentialNewPath)
    {
      var seenFaces = new HashSet<int>();
      var (newFixture, newFace) = potentialNewPath.Job.PlannedFixture(potentialNewPath.Process, potentialNewPath.Path);
      foreach (var otherPath in alreadyLoading)
      {
        var (otherFix, otherFace) = otherPath.Job.PlannedFixture(otherPath.Process, otherPath.Path);
        if (!string.IsNullOrEmpty(newFixture) && newFixture != otherFix)
          return false;
        seenFaces.Add(otherFace);
      }

      if (seenFaces.Contains(newFace))
        return false;

      return true;
    }

    private static HashSet<T> ToHashSet<T>(IEnumerable<T> ts)
    {
      // constructor with parameter doesn't exist in NET461
      var s = new HashSet<T>();
      foreach (var t in ts) s.Add(t);
      return s;
    }

    private (bool, HashSet<long>) CheckMaterialForPathExists(HashSet<long> currentlyLoading, IReadOnlyDictionary<long, InProcessMaterial> unusedMatsOnPal, JobPath path, IEnumerable<InProcessMaterial> queuedMats)
    {
      // This logic must be identical to the eventual assignment in CreateCellState.MaterialToLoadOnFace and SizedQueues.AvailablePalletForPickup

      var (fixture, faceNum) = path.Job.PlannedFixture(path.Process, path.Path);

      var inputQueue = path.Job.GetInputQueue(path.Process, path.Path);
      if (path.Process == 1 && string.IsNullOrEmpty(inputQueue))
      {
        // no input queue, just new parts
        return (true, new HashSet<long>());
      }
      else if (path.Process == 1 && !string.IsNullOrEmpty(inputQueue))
      {
        var casting = path.Job.GetCasting(path.Path);
        if (string.IsNullOrEmpty(casting)) casting = path.Job.PartName;

        // load castings.
        var castings =
          queuedMats
          .Where(m => m.Location.CurrentQueue == inputQueue
                    && !currentlyLoading.Contains(m.MaterialID)
                    && ((string.IsNullOrEmpty(m.JobUnique) && m.PartName == casting)
                       || (!string.IsNullOrEmpty(m.JobUnique)
                            && m.JobUnique == path.Job.UniqueStr
                            && m.Process == 0
                            && path.Job.GetPathGroup(1, m.Path) == path.Job.GetPathGroup(path.Process, path.Path)
                          )
                       )
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
            if (path.Job.GetPathGroup(mat.Process, mat.Path) != path.Job.GetPathGroup(path.Process, path.Path))
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
    private static int PathsToPriority(IReadOnlyList<JobPath> newPaths)
    {
      if (newPaths.Count == 0) return 7;
      var proc = newPaths.Select(p => p.Process).Max();
      var numProc = newPaths.Select(p => p.Job.NumProcesses).Max();
      return Math.Max(1, Math.Min(9, numProc - proc + 1));
    }

    private NiigataAction SetNewRoute(PalletAndMaterial oldPallet, IReadOnlyList<JobPath> newPaths, DateTime nowUtc, IReadOnlyDictionary<(string progName, long revision), ProgramRevision> progs)
    {
      var newMaster = NewPalletMaster(oldPallet.Status.Master.PalletNum, newPaths, progs);
      var newFaces = newPaths.Select(path =>
        new AssignedJobAndPathForFace()
        {
          Face = path.Job.PlannedFixture(process: path.Process, path: path.Path).face,
          Unique = path.Job.UniqueStr,
          Proc = path.Process,
          Path = path.Path
        }).ToList();

      if (SimpleQuantityChange(oldPallet.Status, oldPallet.CurrentOrLoadingFaces, newMaster, newFaces))
      {
        int remaining = 1;
        if (oldPallet.Status.CurrentStep is UnloadStep)
        {
          remaining = 2;
        }
        return new UpdatePalletQuantities()
        {
          Pallet = oldPallet.Status.Master.PalletNum,
          Priority = PathsToPriority(newPaths),
          Cycles = remaining,
          NoWork = false,
          Skip = false,
          LongToolMachine = 0
        };
      }
      else
      {
        return new NewPalletRoute()
        {
          NewMaster = newMaster,
          NewFaces = newFaces
        };
      }
    }

    private List<RouteStep> MiddleStepsForPath(JobPath path, IReadOnlyDictionary<(string progNum, long revision), ProgramRevision> progs)
    {
      var steps = new List<RouteStep>();
      foreach (var stop in path.Job.GetMachiningStop(path.Process, path.Path))
      {
        if (_statNames != null && _statNames.ReclampGroupNames.Contains(stop.StationGroup))
        {
          steps.Add(new ReclampStep()
          {
            Reclamp = stop.Stations.ToList()
          });
        }
        else
        {

          int? iccProgram = null;
          if (stop.ProgramRevision.HasValue && progs.TryGetValue((stop.ProgramName, stop.ProgramRevision.Value), out var prog))
          {
            if (prog.CellControllerProgramName != null && int.TryParse(prog.CellControllerProgramName, out int p))
            {
              iccProgram = p;
            }
            else
            {
              Log.Error("Unable to find program for job {uniq} part {part} program {name}", path.Job.UniqueStr, path.Job.PartName, stop.ProgramName);
            }
          }
          else
          {
            if (int.TryParse(stop.ProgramName, out int p))
            {
              iccProgram = p;
            }
            else
            {
              Log.Error("Program for job {uniq} part {part} program {prog} is not an integer", path.Job.UniqueStr, path.Job.PartName, stop.ProgramName);
            }
          }

          if (iccProgram.HasValue)
          {
            steps.Add(new MachiningStep()
            {
              Machines = stop.Stations.Select(s =>
              {
                if (_statNames != null)
                {
                  return _statNames.JobMachToIcc(stop.StationGroup, s);
                }
                else
                {
                  return s;
                }
              }).ToList(),
              ProgramNumsToRun = new List<int> { iccProgram.Value }
            });
          }
        }
      }
      return steps;
    }

    private void MergeSteps(List<RouteStep> steps, IEnumerable<RouteStep> newSteps)
    {
      var idx = 0;
      foreach (var newStep in newSteps)
      {
        while (idx < steps.Count)
        {
          var curStep = steps[idx];
          // check if newStep matches
          if (curStep is ReclampStep && newStep is ReclampStep)
          {
            if (((ReclampStep)curStep).Reclamp.SequenceEqual(((ReclampStep)newStep).Reclamp))
            {
              break;
            }
            else
            {
              idx += 1;
            }
          }
          else if (curStep is MachiningStep && newStep is MachiningStep)
          {
            if (((MachiningStep)curStep).Machines.SequenceEqual(((MachiningStep)newStep).Machines))
            {
              foreach (var prog in ((MachiningStep)newStep).ProgramNumsToRun)
              {
                ((MachiningStep)curStep).ProgramNumsToRun.Add(prog);
              }
              break;
            }
            else
            {
              idx += 1;
            }
          }
          else
          {
            idx += 1;
          }
        }

        if (idx == steps.Count)
        {
          steps.Add(newStep);
          idx += 1;
        }
      }

    }

    private PalletMaster NewPalletMaster(int pallet, IReadOnlyList<JobPath> newPaths, IReadOnlyDictionary<(string progNum, long revision), ProgramRevision> progs)
    {
      var orderedPaths = newPaths.OrderBy(p => p.Job.UniqueStr).ThenBy(p => p.Process).ThenBy(p => p.Path).ToList();

      List<RouteStep> middleSteps = null;
      foreach (var path in orderedPaths)
      {
        var newSteps = MiddleStepsForPath(path, progs);
        if (middleSteps == null || middleSteps.Count == 0)
        {
          middleSteps = newSteps;
        }
        else
        {
          MergeSteps(middleSteps, newSteps);
        }
      }

      var firstPath = orderedPaths.First();

      return new PalletMaster()
      {
        PalletNum = pallet,
        Comment = "",
        RemainingPalletCycles = 1,
        Priority = PathsToPriority(newPaths),
        NoWork = false,
        Skip = false,
        ForLongToolMaintenance = false,
        PerformProgramDownload = true,
        Routes =
          (new RouteStep[] { new LoadStep() { LoadStations = firstPath.Job.LoadStations(firstPath.Process, firstPath.Path).ToList() } })
          .Concat(middleSteps)
          .Concat(new[] {
            new UnloadStep()
            {
              UnloadStations = firstPath.Job.UnloadStations(firstPath.Process, firstPath.Path).ToList(),
              CompletedPartCount = 1
            }
          })
          .ToList()
      };
    }

    private bool SimpleQuantityChange(PalletStatus existingStatus, IReadOnlyList<PalletFace> existingFaces,
                                      PalletMaster newPal, IReadOnlyList<AssignedJobAndPathForFace> newFaces
    )
    {
      var existingMaster = existingStatus.Master;
      if (existingStatus.Tracking.RouteInvalid) return false;
      if (existingFaces.Count != newFaces.Count) return false;
      if (existingMaster.Routes.Count != newPal.Routes.Count) return false;

      if (newFaces.Any(newFace =>
            existingFaces.FirstOrDefault(existingFace =>
              newFace.Unique == existingFace.Job.UniqueStr &&
              newFace.Proc == existingFace.Process &&
              newFace.Path == existingFace.Path &&
              newFace.Face == existingFace.Face
            ) == null)
         )
      {
        return false;
      }

      for (int i = 0; i < existingMaster.Routes.Count - 1; i++)
      {
        if (existingMaster.Routes[i] is LoadStep && newPal.Routes[i] is LoadStep)
        {
          if (!((LoadStep)existingMaster.Routes[i]).LoadStations.SequenceEqual(((LoadStep)newPal.Routes[i]).LoadStations)) return false;
        }
        else if (existingMaster.Routes[i] is MachiningStep && newPal.Routes[i] is MachiningStep)
        {
          var eM = (MachiningStep)existingMaster.Routes[i];
          var nM = (MachiningStep)newPal.Routes[i];
          if (!eM.Machines.SequenceEqual(nM.Machines)) return false;
          if (!eM.ProgramNumsToRun.SequenceEqual(nM.ProgramNumsToRun)) return false;
        }
        else if (existingMaster.Routes[i] is ReclampStep && newPal.Routes[i] is ReclampStep)
        {
          if (!((ReclampStep)existingMaster.Routes[i]).Reclamp.SequenceEqual(((ReclampStep)newPal.Routes[i]).Reclamp)) return false;
        }
        else if (existingMaster.Routes[i] is UnloadStep && newPal.Routes[i] is UnloadStep)
        {
          if (!((UnloadStep)existingMaster.Routes[i]).UnloadStations.SequenceEqual(((UnloadStep)newPal.Routes[i]).UnloadStations)) return false;
        }
        else
        {
          return false;
        }
      }

      return true;
    }

    private NiigataAction SetNoWork(PalletAndMaterial pal)
    {
      return new UpdatePalletQuantities()
      {
        Pallet = pal.Status.Master.PalletNum,
        Priority = pal.Status.Master.Priority,
        Cycles = pal.Status.Master.RemainingPalletCycles,
        NoWork = true,
        Skip = false,
        LongToolMachine = 0
      };
    }
    #endregion

    #region Programs
    public static string CreateProgramComment(string program, long revision)
    {
      return "Insight:" + revision.ToString() + ":" + program;
    }
    public static bool IsInsightProgram(ProgramEntry prog)
    {
      return prog.Comment.StartsWith("Insight:");
    }
    public static bool TryParseProgramComment(ProgramEntry prog, out string program, out long rev)
    {
      var comment = prog.Comment;
      if (comment.StartsWith("Insight:"))
      {
        comment = comment.Substring(8);
        var idx = comment.IndexOf(':');
        if (idx > 0)
        {
          if (long.TryParse(comment.Substring(0, idx), out rev))
          {
            program = comment.Substring(idx + 1);
            return true;
          }
        }
      }

      program = null;
      rev = 0;
      return false;
    }

    private NewProgram AddProgram(IReadOnlyDictionary<int, ProgramEntry> existing, ProgramRevision prog, IEnumerable<JobPlan> allJobs)
    {
      var procAndStop =
        allJobs
          .SelectMany(j => Enumerable.Range(1, j.NumProcesses).Select(proc => new { j, proc }))
          .SelectMany(j => Enumerable.Range(1, j.j.GetNumPaths(j.proc)).Select(path => new { j = j.j, proc = j.proc, path }))
          .SelectMany(j => j.j.GetMachiningStop(j.proc, j.path).Select(stop => new { stop, proc = j.proc }))
          .FirstOrDefault(s => s.stop.ProgramName == prog.ProgramName && s.stop.ProgramRevision == prog.Revision);

      int progNum = 0;
      if (procAndStop != null && procAndStop.proc >= 1 && procAndStop.proc <= 9)
      {
        progNum = Enumerable.Range(2000 + procAndStop.proc * 100, 999).FirstOrDefault(p => !existing.ContainsKey(p));
      }
      if (progNum == 0)
      {
        progNum = Enumerable.Range(3000, 9999 - 3000).FirstOrDefault(p => !existing.ContainsKey(p));
      }
      if (progNum == 0)
      {
        throw new Exception("Unable to find unused program number for program " + prog.ProgramName + " rev" + prog.Revision.ToString());
      }
      return new NewProgram()
      {
        ProgramNum = progNum,
        IccProgramComment = CreateProgramComment(prog.ProgramName, prog.Revision),
        ProgramName = prog.ProgramName,
        ProgramRevision = prog.Revision,
        ExpectedCuttingTime = procAndStop == null ? TimeSpan.Zero : procAndStop.stop.ExpectedCycleTime
      };
    }

    private DeleteProgram CheckForOldPrograms(CellState cellSt)
    {
      // the icc program numbers currently used by schedules
      var usedIccProgs = new HashSet<string>(
        cellSt.ProgramNums.Values.Select(p => p.CellControllerProgramName)
        .Concat(
          cellSt.Status.Pallets
          .SelectMany(p => p.Master.Routes)
          .SelectMany(r =>
            r is MachiningStep
              ? ((MachiningStep)r).ProgramNumsToRun.Select(p => p.ToString())
              : Enumerable.Empty<string>()
          )
        )
      );

      // we want to keep around the latest revision for each program just so that we don't delete it as soon as a schedule
      // completes in anticipation of a new schedule being downloaded.
      var maxRevForProg =
        cellSt.Status.Programs
          .Select(p => TryParseProgramComment(p.Value, out string pName, out long rev) ? new { pName, rev } : null)
          .Where(p => p != null)
          .ToLookup(p => p.pName, p => p.rev)
          .ToDictionary(ps => ps.Key, ps => ps.Max());

      foreach (var prog in cellSt.Status.Programs)
      {
        if (!IsInsightProgram(prog.Value)) continue;
        if (usedIccProgs.Contains(prog.Key.ToString())) continue;
        if (!TryParseProgramComment(prog.Value, out string pName, out long rev)) continue;
        if (rev >= maxRevForProg[pName]) continue;

        return new DeleteProgram()
        {
          ProgramNum = prog.Key,
          ProgramName = pName,
          ProgramRevision = rev
        };
      }

      return null;
    }
    #endregion
  }
}