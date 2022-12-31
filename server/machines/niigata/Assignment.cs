/* Copyright (c) 2021, John Lenz

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

    public delegate bool ExtraPartFilterDelegate(
      MachineFramework.Job job,
      int procNum,
      int pathNum,
      MachineFramework.ProcPathInfo pathInfo
    );

    public delegate int PathToPriorityDelegate(
      IReadOnlyList<(MachineFramework.HistoricJob job, int process, int path)> newPaths
    );

    private readonly ExtraPartFilterDelegate _extraPathFilter;
    private readonly PathToPriorityDelegate _pathToPriority;

    public AssignNewRoutesOnPallets(
      NiigataStationNames n,
      ExtraPartFilterDelegate extraPathFilter = null,
      PathToPriorityDelegate pathToPriority = null
    )
    {
      _statNames = n;
      _extraPathFilter = extraPathFilter;
      _pathToPriority = pathToPriority ?? PathsToPriority;
    }

    public NiigataAction NewPalletChange(CellState cellSt)
    {
      // only need to decide on a single change, SyncPallets will call in a loop until no changes are needed.

      // first, check if any programs are needed
      foreach (var prog in cellSt.ProgramsInUse)
      {
        if (string.IsNullOrEmpty(prog.Value.CellControllerProgramName))
        {
          return AddProgram(cellSt: cellSt, prog: prog.Value);
        }
      }

      // next, check if any pallet needs to be adjusted because a face is missing material
      foreach (
        var pal in cellSt.Pallets.Where(pal => pal.CurrentOrLoadingFaces.Any(f => f.FaceIsMissingMaterial))
      )
      {
        if (pal.CurrentOrLoadingFaces.All(f => f.FaceIsMissingMaterial))
        {
          return SetNoWork(pal);
        }
        else
        {
          // can't do anything until at the load station
          if (pal.Status.CurStation.Location.Location != PalletLocationEnum.LoadUnload)
            continue;
          if (
            !(
              pal.Status.Tracking.BeforeCurrentStep
              && (pal.Status.CurrentStep is UnloadStep || pal.Status.CurrentStep is LoadStep)
            )
          )
            continue;

          // FindMaterialToLoad should correctly (re)detect the faces missing material.
          var pathsToLoad = FindMaterialToLoad(
            cellSt,
            pal.Status.Master.PalletNum,
            pal.Status.CurStation.Location.Num,
            pal.Material,
            queuedMats: cellSt.QueuedMaterial
          );
          if (pathsToLoad != null && pathsToLoad.Count > 0)
          {
            if (
              SetNewRoute(
                pal,
                pathsToLoad,
                cellSt.Status.TimeOfStatusUTC,
                cellSt.ProgramsInUse,
                out var newAction
              )
            )
              return newAction;
          }
        }
      }

      // next, check if pallet at load station being unloaded needs something loaded
      foreach (var pal in cellSt.Pallets)
      {
        if (pal.ManualControl)
          continue;
        if (pal.Status.Master.Skip)
          continue;
        if (pal.Status.CurStation.Location.Location != PalletLocationEnum.LoadUnload)
          continue;
        if (!(pal.Status.Tracking.BeforeCurrentStep && pal.Status.CurrentStep is UnloadStep))
          continue;
        if (pal.Material.Any(m => m.Mat.Action.Type == InProcessMaterialAction.ActionType.Loading))
          continue;

        var pathsToLoad = FindMaterialToLoad(
          cellSt,
          pal.Status.Master.PalletNum,
          pal.Status.CurStation.Location.Num,
          pal.Material,
          queuedMats: cellSt.QueuedMaterial
        );
        if (pathsToLoad != null && pathsToLoad.Count > 0)
        {
          if (
            SetNewRoute(
              pal,
              pathsToLoad,
              cellSt.Status.TimeOfStatusUTC,
              cellSt.ProgramsInUse,
              out var newAction
            )
          )
            return newAction;
        }
      }

      // next, check empty stuff in buffer
      foreach (var pal in cellSt.Pallets)
      {
        if (pal.ManualControl)
          continue;
        if (pal.Status.Master.Skip)
          continue;
        if (pal.Status.CurStation.Location.Location != PalletLocationEnum.Buffer)
          continue;
        if (pal.Status.HasWork)
          continue;

        // use empty matCurrentlyOnPal because if FMS Insight thinks there is material, the operator aborted it by setting no-work, overriding the route.
        // thus want to record the material as unloaded when it arrives at load station (may already have been unloaded, Insight just don't know it)
        var pathsToLoad = FindMaterialToLoad(
          cellSt,
          pal.Status.Master.PalletNum,
          loadStation: null,
          matCurrentlyOnPal: Enumerable.Empty<InProcessMaterialAndJob>(),
          queuedMats: cellSt.QueuedMaterial
        );
        if (pathsToLoad != null && pathsToLoad.Count > 0)
        {
          if (
            SetNewRoute(
              pal,
              pathsToLoad,
              cellSt.Status.TimeOfStatusUTC,
              cellSt.ProgramsInUse,
              out var newAction
            )
          )
            return newAction;
        }
      }

      // delete old programs
      return CheckForOldPrograms(cellSt);
    }

    #region Calculate Paths
    private record JobPath
    {
      public MachineFramework.HistoricJob Job { get; init; }
      public MachineFramework.ProcPathInfo PathInfo { get; init; }
      public int Process { get; init; }
      public int Path { get; init; }
      public long Precedence { get; init; }

      // RemainingProc1ToRun is only set to true on Process = 1 paths for which we have not yet
      // started all the planned quantity from the job.  The reason the path is not just filtered
      // out is in the case that there is an input raw material queue, we still want the chance to
      // run material assigned to the job.  But in this case, unassigned material will not be
      // assigned to the job, effectively preventing any new material from running.
      public bool RemainingProc1ToRun { get; init; }
    }

    private record JobPathAndPrograms : JobPath
    {
      public ImmutableList<ProgramsForProcess> Programs { get; init; }
      public bool ProgramsOverrideJob { get; init; }
    }

    private ImmutableList<JobPath> FindPathsForPallet(CellState cellSt, int pallet, int? loadStation)
    {
      return cellSt.CurrentStatus.Jobs.Values
        .SelectMany(
          job =>
            job.Processes.Select(
              (proc, procIdx) =>
                new
                {
                  job,
                  proc,
                  procNum = procIdx + 1
                }
            )
        )
        .SelectMany(
          j =>
            j.proc.Paths.Select(
              (path, pathIdx) =>
                new JobPath
                {
                  Job = j.job,
                  PathInfo = path,
                  Process = j.procNum,
                  Path = pathIdx + 1,
                  Precedence = j.job.Precedence[j.procNum - 1][pathIdx],
                  RemainingProc1ToRun = j.procNum == 1 ? j.job.RemainingToStart > 0 : false,
                }
            )
        )
        .Where(j => j.Process > 1 || !string.IsNullOrEmpty(j.PathInfo.InputQueue) || j.RemainingProc1ToRun)
        .Where(j => loadStation == null || j.PathInfo.Load.Contains(loadStation.Value))
        .Where(j => j.PathInfo.Pallets.Contains(pallet.ToString()))
        .Where(
          j =>
            _extraPathFilter == null
            || _extraPathFilter(job: j.Job, procNum: j.Process, pathNum: j.Path, pathInfo: j.PathInfo)
        )
        .OrderBy(j => j.Precedence)
        .ToImmutableList();
    }

    private bool PathAllowedOnPallet(IEnumerable<JobPath> alreadyLoading, JobPath potentialNewPath)
    {
      var seenFaces = new HashSet<int>();
      var newFixture = potentialNewPath.PathInfo.Fixture;
      var newFace = potentialNewPath.PathInfo.Face ?? 0;
      foreach (var otherPath in alreadyLoading)
      {
        if (!string.IsNullOrEmpty(newFixture) && newFixture != otherPath.PathInfo.Fixture)
          return false;
        seenFaces.Add(otherPath.PathInfo.Face ?? 0);
      }

      if (seenFaces.Contains(newFace))
        return false;

      return true;
    }

    private bool CanMaterialLoadOntoPath(
      InProcessMaterialAndJob mat,
      JobPath path,
      ImmutableList<ProgramsForProcess> programs
    )
    {
      var m = mat.Mat;
      return CreateCellState.FilterMaterialAvailableToLoadOntoFace(
        new CreateCellState.QueuedMaterialWithDetails()
        {
          MaterialID = m.MaterialID,
          Unique = m.JobUnique,
          Serial = m.Serial,
          Workorder = m.WorkorderId,
          PartNameOrCasting = m.PartName,
          NextProcess = m.Process + 1,
          QueuePosition = m.Location.QueuePosition ?? 0,
          Paths = new Dictionary<int, int>() { { m.Process, m.Path } },
          Workorders = mat.Workorders,
        },
        new PalletFace()
        {
          Job = path.Job,
          PathInfo = path.PathInfo,
          Process = path.Process,
          Path = path.Path,
          Face = 1,
          FaceIsMissingMaterial = false,
          Programs = programs
        },
        _statNames
      );
    }

    private ImmutableList<ProgramsForProcess> ProgramsForMaterial(
      InProcessMaterialAndJob mat,
      JobPath path,
      out bool programsOverrideJob
    )
    {
      var matWorkProgs = mat.Workorders?.Where(w => w.Part == path.Job.PartName).FirstOrDefault()?.Programs;
      if (matWorkProgs == null)
      {
        // use from job
        programsOverrideJob = false;
        return CreateCellState.JobProgramsFromStops(path.PathInfo.Stops, _statNames).ToImmutableList();
      }
      else
      {
        // use from workorder
        programsOverrideJob = true;
        return CreateCellState.WorkorderProgramsForProcess(path.Process, matWorkProgs).ToImmutableList();
      }
    }

    private record MatForPath
    {
      public ImmutableHashSet<long> MatIds { get; init; }
      public ImmutableList<ProgramsForProcess> Programs { get; init; }
      public bool ProgramsOverrideJob { get; init; }
    }

    private MatForPath CheckMaterialForPathExists(
      HashSet<long> currentlyLoading,
      IReadOnlyDictionary<long, InProcessMaterialAndJob> unusedMatsOnPal,
      JobPath path,
      IEnumerable<InProcessMaterialAndJob> queuedMats
    )
    {
      // This logic must be identical to the eventual assignment in CreateCellState.MaterialToLoadOnFace and SizedQueues.AvailablePalletForPickup

      var inputQueue = path.PathInfo.InputQueue;
      if (path.Process == 1 && string.IsNullOrEmpty(inputQueue))
      {
        // no input queue, just new parts
        return new MatForPath() { MatIds = ImmutableHashSet<long>.Empty };
      }

      var countToLoad = path.PathInfo.PartsPerPallet;
      var availMatIds = ImmutableHashSet.CreateBuilder<long>();
      bool programsOverrideJob = false;
      ImmutableList<ProgramsForProcess> programs = null;

      if (path.Process > 1)
      {
        // check material on pallets
        foreach (var mat in unusedMatsOnPal.Values)
        {
          if (
            mat.Mat.JobUnique == path.Job.UniqueStr
            && mat.Mat.Process + 1 == path.Process
            && !currentlyLoading.Contains(mat.Mat.MaterialID)
          )
          {
            if (programs == null)
            {
              programs = ProgramsForMaterial(mat, path, out programsOverrideJob);
            }
            availMatIds.Add(mat.Mat.MaterialID);
            if (availMatIds.Count == countToLoad)
            {
              return new MatForPath()
              {
                MatIds = availMatIds.ToImmutable(),
                ProgramsOverrideJob = programsOverrideJob,
                Programs = programs
              };
            }
          }
        }
      }

      if (!string.IsNullOrEmpty(inputQueue))
      {
        // load mat in queue
        var matInQueue = queuedMats
          .Where(
            m => m.Mat.Location.CurrentQueue == inputQueue && !currentlyLoading.Contains(m.Mat.MaterialID)
          )
          .OrderBy(m => m.Mat.Location.QueuePosition);

        foreach (var mat in matInQueue)
        {
          if (!CanMaterialLoadOntoPath(mat, path, programs))
            continue;

          // don't load from casting if no more parts are needed and material not yet assigned to the job
          if (!path.RemainingProc1ToRun && string.IsNullOrEmpty(mat.Mat.JobUnique))
            continue;

          if (programs == null)
          {
            programs = ProgramsForMaterial(mat, path, out programsOverrideJob);
          }
          availMatIds.Add(mat.Mat.MaterialID);
          if (availMatIds.Count == countToLoad)
          {
            return new MatForPath()
            {
              MatIds = availMatIds.ToImmutable(),
              ProgramsOverrideJob = programsOverrideJob,
              Programs = programs
            };
          }
        }
      }

      return null;
    }

    private ImmutableList<JobPathAndPrograms> FindMaterialToLoad(
      CellState cellSt,
      int pallet,
      int? loadStation,
      IEnumerable<InProcessMaterialAndJob> matCurrentlyOnPal,
      IEnumerable<InProcessMaterialAndJob> queuedMats
    )
    {
      ImmutableList<JobPathAndPrograms>.Builder paths = null;
      var allPaths = FindPathsForPallet(cellSt, pallet, loadStation);
      var unusedMatsOnPal = matCurrentlyOnPal.ToDictionary(m => m.Mat.MaterialID);
      var currentlyLoading = new HashSet<long>();
      foreach (var path in allPaths)
      {
        var matForPath = CheckMaterialForPathExists(currentlyLoading, unusedMatsOnPal, path, queuedMats);
        if (matForPath == null)
          continue;

        if (paths == null)
        {
          foreach (var matId in matForPath.MatIds)
          {
            unusedMatsOnPal.Remove(matId);
            currentlyLoading.Add(matId);
          }
          // first path with material gets set
          paths = ImmutableList.CreateBuilder<JobPathAndPrograms>();
          paths.Add(
            new JobPathAndPrograms()
            {
              Job = path.Job,
              PathInfo = path.PathInfo,
              Process = path.Process,
              Path = path.Path,
              Programs = matForPath.Programs,
              ProgramsOverrideJob = matForPath.ProgramsOverrideJob
            }
          );
        }
        else
        {
          // later paths need to make sure they are compatible.
          if (PathAllowedOnPallet(paths, path))
          {
            foreach (var matId in matForPath.MatIds)
            {
              unusedMatsOnPal.Remove(matId);
              currentlyLoading.Add(matId);
            }
            paths.Add(
              new JobPathAndPrograms()
              {
                Job = path.Job,
                PathInfo = path.PathInfo,
                Process = path.Process,
                Path = path.Path,
                Programs = matForPath.Programs,
                ProgramsOverrideJob = matForPath.ProgramsOverrideJob
              }
            );
          }
        }
      }
      return paths?.ToImmutable();
    }
    #endregion

    #region Set New Route
    public static int PathsToPriority(
      IReadOnlyList<(MachineFramework.HistoricJob job, int process, int path)> newPaths
    )
    {
      if (newPaths.Count == 0)
        return 7;
      var proc = newPaths.Select(p => p.process).Max();
      var numProc = newPaths.Select(p => p.job.Processes.Count).Max();
      return Math.Max(1, Math.Min(9, numProc - proc + 1));
    }

    private bool SetNewRoute(
      PalletAndMaterial oldPallet,
      IReadOnlyList<JobPathAndPrograms> newPaths,
      DateTime nowUtc,
      IReadOnlyDictionary<(string progName, long revision), ProgramRevision> progs,
      out NiigataAction newAction
    )
    {
      var newMaster = NewPalletMaster(oldPallet.Status.Master.PalletNum, newPaths, progs);
      var newFaces = newPaths
        .Select(
          path =>
            new AssignedJobAndPathForFace()
            {
              Face = path.PathInfo.Face ?? 0,
              Unique = path.Job.UniqueStr,
              Proc = path.Process,
              Path = path.Path,
              ProgOverride = path.ProgramsOverrideJob ? path.Programs : null
            }
        )
        .ToList();

      if (SimpleQuantityChange(oldPallet.Status, oldPallet.CurrentOrLoadingFaces, newMaster, newFaces))
      {
        int remaining = 1;
        if (oldPallet.Status.CurrentStep is UnloadStep)
        {
          remaining = 2;
        }
        newAction = new UpdatePalletQuantities()
        {
          Pallet = oldPallet.Status.Master.PalletNum,
          Priority = _pathToPriority(newPaths.Select(j => (j.Job, j.Process, j.Path)).ToList()),
          Cycles = remaining,
          NoWork = false,
          Skip = false,
          LongToolMachine = 0
        };
        return true;
      }
      else if (AllowOverrideRoute(oldPallet.Status, newMaster))
      {
        newAction = new NewPalletRoute() { NewMaster = newMaster, NewFaces = newFaces };
        return true;
      }
      else if (oldPallet.Status.CurStation.Location.Location == PalletLocationEnum.Buffer)
      {
        // need to delete first
        newAction = new DeletePalletRoute() { PalletNum = newMaster.PalletNum };
        return true;
      }
      else
      {
        // pallet is at load station, must wait until it gets to the buffer
        newAction = null;
        return false;
      }
    }

    private List<RouteStep> MiddleStepsForPath(
      JobPathAndPrograms path,
      IReadOnlyDictionary<(string progNum, long revision), ProgramRevision> progs
    )
    {
      var steps = new List<RouteStep>();
      int machStopIdx = -1;
      foreach (var stop in path.PathInfo.Stops)
      {
        if (_statNames != null && _statNames.ReclampGroupNames.Contains(stop.StationGroup))
        {
          steps.Add(new ReclampStep() { Reclamp = stop.Stations.ToList() });
        }
        else
        {
          machStopIdx += 1;

          int? iccProgram = null;

          var workorderProg = path.Programs?.FirstOrDefault(p => p.MachineStopIndex == machStopIdx);

          if (workorderProg != null && workorderProg.Revision.HasValue)
          {
            if (progs.TryGetValue((workorderProg.ProgramName, workorderProg.Revision.Value), out var program))
            {
              if (
                program.CellControllerProgramName != null
                && int.TryParse(program.CellControllerProgramName, out int p)
              )
              {
                iccProgram = p;
              }
              else
              {
                Log.Error("Unable to find program {prog}", workorderProg.ProgramName);
              }
            }
            else
            {
              Log.Error("Unable to find program {prog}", workorderProg.ProgramName);
            }
          }

          if (
            !iccProgram.HasValue
            && stop.ProgramRevision.HasValue
            && progs.TryGetValue((stop.Program, stop.ProgramRevision.Value), out var prog)
          )
          {
            if (
              prog.CellControllerProgramName != null
              && int.TryParse(prog.CellControllerProgramName, out int p)
            )
            {
              iccProgram = p;
            }
            else
            {
              Log.Error(
                "Unable to find program for job {uniq} part {part} program {name}",
                path.Job.UniqueStr,
                path.Job.PartName,
                stop.Program
              );
            }
          }
          else if (!iccProgram.HasValue)
          {
            if (int.TryParse(stop.Program, out int p))
            {
              iccProgram = p;
            }
            else
            {
              Log.Error(
                "Program for job {uniq} part {part} program {prog} is not an integer",
                path.Job.UniqueStr,
                path.Job.PartName,
                stop.Program
              );
            }
          }

          if (iccProgram.HasValue)
          {
            steps.Add(
              new MachiningStep()
              {
                Machines = stop.Stations
                  .Select(s =>
                  {
                    if (_statNames != null)
                    {
                      return _statNames.JobMachToIcc(stop.StationGroup, s);
                    }
                    else
                    {
                      return s;
                    }
                  })
                  .ToList(),
                ProgramNumsToRun = new List<int> { iccProgram.Value }
              }
            );
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

    private PalletMaster NewPalletMaster(
      int pallet,
      IReadOnlyList<JobPathAndPrograms> newPaths,
      IReadOnlyDictionary<(string progNum, long revision), ProgramRevision> progs
    )
    {
      var orderedPaths = newPaths
        .OrderBy(p => p.Job.UniqueStr)
        .ThenBy(p => p.Process)
        .ThenBy(p => p.Path)
        .ToList();

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
        Priority = _pathToPriority(newPaths.Select(j => (j.Job, j.Process, j.Path)).ToList()),
        NoWork = false,
        Skip = false,
        ForLongToolMaintenance = false,
        PerformProgramDownload = true,
        Routes = (new RouteStep[] { new LoadStep() { LoadStations = firstPath.PathInfo.Load.ToList() } })
          .Concat(middleSteps)
          .Concat(
            new[]
            {
              new UnloadStep() { UnloadStations = firstPath.PathInfo.Unload.ToList(), CompletedPartCount = 1 }
            }
          )
          .ToList()
      };
    }

    private bool SimpleQuantityChange(
      PalletStatus existingStatus,
      IReadOnlyList<PalletFace> existingFaces,
      PalletMaster newPal,
      IReadOnlyList<AssignedJobAndPathForFace> newFaces
    )
    {
      var existingMaster = existingStatus.Master;
      if (existingStatus.Tracking.RouteInvalid)
        return false;
      if (existingFaces.Count != newFaces.Count)
        return false;
      if (existingMaster.Routes.Count != newPal.Routes.Count)
        return false;

      if (
        newFaces.Any(
          newFace =>
            existingFaces.FirstOrDefault(
              existingFace =>
                newFace.Unique == existingFace.Job.UniqueStr
                && newFace.Proc == existingFace.Process
                && newFace.Path == existingFace.Path
                && newFace.Face == existingFace.Face
            ) == null
        )
      )
      {
        return false;
      }

      for (int i = 0; i < existingMaster.Routes.Count - 1; i++)
      {
        if (existingMaster.Routes[i] is LoadStep && newPal.Routes[i] is LoadStep)
        {
          if (
            !((LoadStep)existingMaster.Routes[i]).LoadStations.SequenceEqual(
              ((LoadStep)newPal.Routes[i]).LoadStations
            )
          )
            return false;
        }
        else if (existingMaster.Routes[i] is MachiningStep && newPal.Routes[i] is MachiningStep)
        {
          var eM = (MachiningStep)existingMaster.Routes[i];
          var nM = (MachiningStep)newPal.Routes[i];
          if (!eM.Machines.SequenceEqual(nM.Machines))
            return false;
          if (!eM.ProgramNumsToRun.SequenceEqual(nM.ProgramNumsToRun))
            return false;
        }
        else if (existingMaster.Routes[i] is ReclampStep && newPal.Routes[i] is ReclampStep)
        {
          if (
            !((ReclampStep)existingMaster.Routes[i]).Reclamp.SequenceEqual(
              ((ReclampStep)newPal.Routes[i]).Reclamp
            )
          )
            return false;
        }
        else if (existingMaster.Routes[i] is UnloadStep && newPal.Routes[i] is UnloadStep)
        {
          if (
            !((UnloadStep)existingMaster.Routes[i]).UnloadStations.SequenceEqual(
              ((UnloadStep)newPal.Routes[i]).UnloadStations
            )
          )
            return false;
        }
        else
        {
          return false;
        }
      }

      return true;
    }

    private bool AllowOverrideRoute(PalletStatus existingStatus, PalletMaster newPal)
    {
      var existingMaster = existingStatus.Master;
      if (existingStatus.Tracking.RouteInvalid)
        return true;

      // The ICC can only replace a route if the sequence of steps matches, otherwise the old route needs to be deleted first
      if (existingMaster.Routes.Count != newPal.Routes.Count)
        return false;

      for (int i = 0; i < existingMaster.Routes.Count - 1; i++)
      {
        if (
          (existingMaster.Routes[i] is LoadStep && newPal.Routes[i] is LoadStep)
          || (existingMaster.Routes[i] is MachiningStep && newPal.Routes[i] is MachiningStep)
          || (existingMaster.Routes[i] is ReclampStep && newPal.Routes[i] is ReclampStep)
          || (existingMaster.Routes[i] is UnloadStep && newPal.Routes[i] is UnloadStep)
        )
        {
          // OK
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

    private NewProgram AddProgram(CellState cellSt, ProgramRevision prog)
    {
      int process = 0;
      TimeSpan elapsed = TimeSpan.Zero;

      // search for program in job
      var procAndStopFromJob = cellSt.CurrentStatus.Jobs.Values
        .SelectMany(j => j.Processes.Select((p, idx) => new { proc = p, procNum = idx + 1 }))
        .SelectMany(p => p.proc.Paths.Select(path => new { path, procNum = p.procNum }))
        .SelectMany(p => p.path.Stops.Select(stop => new { stop, procNum = p.procNum }))
        .FirstOrDefault(s => s.stop.Program == prog.ProgramName && s.stop.ProgramRevision == prog.Revision);

      if (procAndStopFromJob != null)
      {
        process = procAndStopFromJob.procNum;
        elapsed = procAndStopFromJob.stop.ExpectedCycleTime;
      }
      else
      {
        // search in workorders
        foreach (
          var work in cellSt.QueuedMaterial
            .Concat(cellSt.Pallets.SelectMany(p => p.Material))
            .SelectMany(m => m.Workorders ?? Enumerable.Empty<Workorder>())
            .Where(w => w.Programs != null)
        )
        {
          var workProg = work.Programs.FirstOrDefault(
            p => p.ProgramName == prog.ProgramName && p.Revision == prog.Revision
          );
          if (workProg != null)
          {
            process = workProg.ProcessNumber;
            var job = cellSt.CurrentStatus.Jobs.Values.Where(j => j.PartName == work.Part).FirstOrDefault();
            if (job != null && process >= 1 && process <= job.Processes.Count)
            {
              elapsed =
                job.Processes[process - 1].Paths[0].Stops
                  .Where(s => _statNames == null || !_statNames.ReclampGroupNames.Contains(s.StationGroup))
                  .ElementAtOrDefault(workProg.StopIndex ?? 0)
                  ?.ExpectedCycleTime ?? TimeSpan.Zero;
            }
          }
        }
      }

      var existing = new HashSet<int>(
        cellSt.Status.Programs.Keys
          .Concat(
            cellSt.OldUnusedPrograms.Select(
              p => int.TryParse(p.CellControllerProgramName, out var num) ? num : 0
            )
          )
          .Concat(cellSt.Status.BadProgramNumbers ?? Enumerable.Empty<int>())
      );

      int progNum = 0;
      if (process >= 1 && process <= 9)
      {
        // start at the max existing number and wrap around, checking for available
        int maxExisting =
          Enumerable.Range(2000 + process * 100, 99).LastOrDefault(p => existing.Contains(p)) % 100;
        if (maxExisting > 90)
          maxExisting = 0;

        for (int i = 0; i < 99; i++)
        {
          var offset = (maxExisting + i) % 100;
          var toCheck = 2000 + process * 100 + offset;
          if (!existing.Contains(toCheck))
          {
            progNum = toCheck;
            break;
          }
        }
      }
      if (progNum == 0)
      {
        progNum = Enumerable.Range(3000, 9999 - 3000).FirstOrDefault(p => !existing.Contains(p));
      }
      if (progNum == 0)
      {
        throw new Exception(
          "Unable to find unused program number for program "
            + prog.ProgramName
            + " rev"
            + prog.Revision.ToString()
        );
      }
      return new NewProgram()
      {
        ProgramNum = progNum,
        IccProgramComment = CreateProgramComment(prog.ProgramName, prog.Revision),
        ProgramName = prog.ProgramName,
        ProgramRevision = prog.Revision,
        ExpectedCuttingTime = elapsed
      };
    }

    private DeleteProgram CheckForOldPrograms(CellState cellSt)
    {
      foreach (var prog in cellSt.OldUnusedPrograms)
      {
        if (!int.TryParse(prog.CellControllerProgramName, out int progNum))
          continue;

        return new DeleteProgram()
        {
          ProgramNum = progNum,
          ProgramName = prog.ProgramName,
          ProgramRevision = prog.Revision
        };
      }

      return null;
    }
    #endregion
  }
}
