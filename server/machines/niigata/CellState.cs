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
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace BlackMaple.FMSInsight.Niigata
{
  public class PalletFace
  {
    public JobPlan Job { get; set; }
    public int Process { get; set; }
    public int Path { get; set; }
    public int Face { get; set; }
    public List<InProcessMaterial> Material { get; set; }
  }

  public class PalletAndMaterial
  {
    public PalletStatus Status { get; set; }
    public List<PalletFace> Faces { get; set; }
  }

  public class CellState
  {
    public NiigataStatus Status { get; set; }
    public PlannedSchedule Schedule { get; set; }
    public bool PalletStateUpdated { get; set; }
    public List<PalletAndMaterial> Pallets { get; set; }
    public List<InProcessMaterial> QueuedMaterial { get; set; }
    public Dictionary<string, int> JobQtyStarted { get; set; }
    public Dictionary<(string progName, long revision), JobDB.ProgramRevision> ProgramNums { get; set; }
  }

  public interface IBuildCellState
  {
    CellState BuildCellState(NiigataStatus status, PlannedSchedule sch);
  }

  public class CreateCellState : IBuildCellState
  {
    private JobLogDB _log;
    private JobDB _jobs;
    private IRecordFacesForPallet _recordFaces;
    private FMSSettings _settings;
    private static Serilog.ILogger Log = Serilog.Log.ForContext<CreateCellState>();

    public CreateCellState(JobLogDB l, JobDB jobs, IRecordFacesForPallet r, FMSSettings s)
    {
      _log = l;
      _jobs = jobs;
      _recordFaces = r;
      _settings = s;
    }

    public CellState BuildCellState(NiigataStatus status, PlannedSchedule sch)
    {
      var palletStateUpdated = false;
      var palsWithMat = new List<PalletAndMaterial>();
      var programs = FindProgramNums(sch);

      // sort pallets by loadBegin so that the assignment of material from queues to pallets is consistent
      var pals = status.Pallets
        .Select(pal =>
        {
          var log = _log.CurrentPalletLog(pal.Master.PalletNum.ToString());
          return new { Status = pal, Log = log };
        })
        .OrderBy(p =>
        {
          var loadBegin = p.Log.FirstOrDefault(e =>
            e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.StartOfCycle
          );
          return loadBegin?.EndTimeUTC ?? DateTime.MaxValue;
        })
        .ToList();

      // first, go through loaded pallets because might need to allocate material from queue
      var currentlyLoading = new HashSet<long>();
      foreach (var pal in pals.Where(p => !p.Status.Master.NoWork))
      {
        if (pal.Status.CurrentStep is LoadStep && !pal.Status.Tracking.BeforeCurrentStep)
        {
          // After load
          palsWithMat.Add(LoadedPallet(pal.Status, pal.Log, status.TimeOfStatusUTC, programs, currentlyLoading, ref palletStateUpdated));
        }
        else if (pal.Status.CurrentStep is UnloadStep && !pal.Status.Tracking.BeforeCurrentStep)
        {
          // after unload
          palsWithMat.Add(LoadedPallet(pal.Status, pal.Log, status.TimeOfStatusUTC, programs, currentlyLoading, ref palletStateUpdated));
        }
        else if (pal.Status.CurrentStep is MachiningStep && pal.Status.Tracking.BeforeCurrentStep)
        {
          // before machine
          var palAndMat = LoadedPallet(pal.Status, pal.Log, status.TimeOfStatusUTC, programs, currentlyLoading, ref palletStateUpdated);

          if (pal.Status.CurStation.Location.Location == PalletLocationEnum.Machine && status.Machines.ContainsKey(pal.Status.CurStation.Location.Num))
          {
            var mach = status.Machines[pal.Status.CurStation.Location.Num];
            if (mach.Machining)
            {
              MarkProgramRunning(palAndMat, pal.Log, mach.CurrentlyExecutingProgram, ref palletStateUpdated, status.TimeOfStatusUTC);
            }
          }

          palsWithMat.Add(palAndMat);
        }
        else if (pal.Status.CurrentStep is MachiningStep && !pal.Status.Tracking.BeforeCurrentStep)
        {
          // after machine
          var palAndMat = LoadedPallet(pal.Status, pal.Log, status.TimeOfStatusUTC, programs, currentlyLoading, ref palletStateUpdated);
          var step = (MachiningStep)pal.Status.CurrentStep;
          EnsureAllMachineEnds(palAndMat, step.ProgramNumsToRun, pal.Log, status.TimeOfStatusUTC, ref palletStateUpdated);
          palsWithMat.Add(palAndMat);
        }
      }

      // next, go through pallets currently being loaded
      foreach (var pal in pals.Where(p => !p.Status.Master.NoWork))
      {
        if (pal.Status.CurrentStep is LoadStep && pal.Status.Tracking.BeforeCurrentStep)
        {
          // Before load
          palsWithMat.Add(CurrentlyLoadingPallet(pal.Status, pal.Log, status.TimeOfStatusUTC, programs, currentlyLoading, ref palletStateUpdated));
        }
        else if (pal.Status.CurrentStep is UnloadStep && pal.Status.Tracking.BeforeCurrentStep)
        {
          // unload-begin
          var palAndMat = CurrentlyLoadingPallet(pal.Status, pal.Log, status.TimeOfStatusUTC, programs, currentlyLoading, ref palletStateUpdated);
          var allProgs = pal.Status.Master.Routes.SelectMany(r =>
          {
            switch (r)
            {
              case MachiningStep step:
                return step.ProgramNumsToRun;
              default:
                return Enumerable.Empty<int>();
            }
          });
          EnsureAllMachineEnds(palAndMat, allProgs, pal.Log, status.TimeOfStatusUTC, ref palletStateUpdated);
          palsWithMat.Add(palAndMat);
        }
      }

      foreach (var pal in pals.Where(p => p.Status.Master.NoWork))
      {
        // need to check if an unload with no load happened and if so record unload end
        palsWithMat.Add(LoadedPallet(pal.Status, pal.Log, status.TimeOfStatusUTC, programs, currentlyLoading, ref palletStateUpdated));
      }

      palsWithMat.Sort((p1, p2) => p1.Status.Master.PalletNum.CompareTo(p2.Status.Master.PalletNum));
      return new CellState()
      {
        Status = status,
        Schedule = sch,
        PalletStateUpdated = palletStateUpdated,
        Pallets = palsWithMat,
        QueuedMaterial = QueuedMaterial(new HashSet<long>(palsWithMat.SelectMany(p => p.Faces).SelectMany(p => p.Material).Select(m => m.MaterialID))),
        JobQtyStarted = CountStartedMaterial(sch, palsWithMat),
        ProgramNums = programs
      };
    }

    private IDictionary<int, PalletFace> GetFaces(PalletStatus pallet)
    {
      if (pallet.Master.NoWork)
      {
        return new Dictionary<int, PalletFace>();
      }

      return
        _recordFaces.Load(pallet.Master.Comment)
        .ToDictionary(m => m.Face, m => new PalletFace()
        {
          Job = _jobs.LoadJob(m.Unique),
          Process = m.Proc,
          Path = m.Path,
          Face = m.Face,
          Material = new List<InProcessMaterial>()
        });
    }

    private class InProcessMaterialWithDetails
    {
      public InProcessMaterial InProc { get; set; }
      public MaterialDetails Details { get; set; }
    }

    private List<InProcessMaterialWithDetails> MaterialCurrentlyOnPallet(
            PalletStatus pallet,
            IDictionary<int, PalletFace> faces,
            IReadOnlyDictionary<(string progName, long revision), JobDB.ProgramRevision> programs,
            IEnumerable<LogEntry> log)
    {
      return log
        .Where(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && !e.StartOfCycle)
        .SelectMany(e => e.Material)
        .Select(m =>
        {
          var details = _log.GetMaterialDetails(m.MaterialID);
          int? lastCompletedRouteStopIdx = null;
          if (faces.TryGetValue(int.Parse(m.Face), out var face))
          {
            var stops = face.Job.GetMachiningStop(face.Process, face.Path).ToList();
            var completedMachineSteps =
              pallet.Master.Routes
              .Take(pallet.Tracking.BeforeCurrentStep ? pallet.Tracking.CurrentStepNum - 1 : pallet.Tracking.CurrentStepNum)
              .Where(r => r is MachiningStep)
              .Cast<MachiningStep>()
              .ToList();

            int stopIdx = 0;
            foreach (var step in completedMachineSteps)
            {
              if (stopIdx >= stops.Count) break;
              var stop = stops[stopIdx];
              if (step.Machines.SequenceEqual(stop.Stations)
                  && stop.ProgramRevision.HasValue
                  && programs.TryGetValue((stop.ProgramName, stop.ProgramRevision.Value), out var prog)
                  && int.TryParse(prog.CellControllerProgramName, out var cellProgNum)
                  && step.ProgramNumsToRun.Contains(cellProgNum)
                 )
              {
                lastCompletedRouteStopIdx = stopIdx;
                stopIdx += 1;
              }
            }
          }
          return new InProcessMaterialWithDetails()
          {
            InProc = new InProcessMaterial()
            {
              MaterialID = m.MaterialID,
              JobUnique = m.JobUniqueStr,
              PartName = m.PartName,
              Process = m.Process,
              Path = details.Paths.ContainsKey(m.Process) ? details.Paths[m.Process] : 1,
              Serial = details.Serial,
              WorkorderId = details.Workorder,
              SignaledInspections =
                _log.LookupInspectionDecisions(m.MaterialID)
                .Where(x => x.Inspect)
                .Select(x => x.InspType)
                .ToList(),
              LastCompletedMachiningRouteStopIndex = lastCompletedRouteStopIdx,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                Pallet = pallet.Master.PalletNum.ToString(),
                Face = int.Parse(m.Face)
              },
              Action = new InProcessMaterialAction()
              {
                Type = InProcessMaterialAction.ActionType.Waiting
              },
            },
            Details = details
          };
        })
        .ToList();
    }

    private List<InProcessMaterial> MaterialToLoadOnFace(PalletStatus pallet, PalletFace face, bool allocateNew, DateTime nowUtc, HashSet<long> currentlyLoading, Dictionary<long, InProcessMaterial> unusedMatsOnPal)
    {
      var mats = new List<InProcessMaterial>();

      var inputQueue = face.Job.GetInputQueue(face.Process, face.Path);

      if (face.Process == 1 && string.IsNullOrEmpty(inputQueue))
      {
        // castings
        for (int i = 1; i <= face.Job.PartsPerPallet(face.Process, face.Path); i++)
        {
          long mid;
          string serial = null;
          if (allocateNew)
          {
            mid = _log.AllocateMaterialID(face.Job.UniqueStr, face.Job.PartName, face.Job.NumProcesses);
            if (_settings.SerialType == SerialType.AssignOneSerialPerMaterial)
            {
              serial = _settings.ConvertMaterialIDToSerial(mid);
              _log.RecordSerialForMaterialID(
                new JobLogDB.EventLogMaterial() { MaterialID = mid, Process = face.Process, Face = "" },
                serial,
                nowUtc
                );
            }
          }
          else
          {
            mid = -1;
          }
          mats.Add(new InProcessMaterial()
          {
            MaterialID = mid,
            JobUnique = face.Job.UniqueStr,
            PartName = face.Job.PartName,
            Process = 0,
            Path = 1,
            Serial = serial,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.Free,
            },
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Waiting
            }
          });
        }
      }
      else if (face.Process == 1)
      {
        // load castings from queue
        var casting = face.Job.GetCasting(face.Path);
        if (string.IsNullOrEmpty(casting)) casting = face.Job.PartName;

        var castings =
          _log.GetMaterialInQueue(inputQueue)
          .Where(m => !currentlyLoading.Contains(m.MaterialID))
          .Select(m => FilterMaterialAvailableToLoadOntoFace(m, face))
          .Where(m => m != null)
          .ToList();

        foreach (var mat in castings.Take(face.Job.PartsPerPallet(face.Process, face.Path)))
        {
          if (allocateNew)
          {
            _log.SetDetailsForMaterialID(mat.Material.MaterialID, face.Job.UniqueStr, face.Job.PartName, face.Job.NumProcesses);
          }
          mats.Add(new InProcessMaterial()
          {
            MaterialID = mat.Material.MaterialID,
            JobUnique = face.Job.UniqueStr,
            PartName = face.Job.PartName,
            Process = 0,
            Path = mat.Details.Paths != null && mat.Details.Paths.TryGetValue(1, out var path) ? path : 1,
            Serial = mat.Details?.Serial,
            WorkorderId = mat.Details?.Workorder,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.InQueue,
              CurrentQueue = inputQueue,
              QueuePosition = mat.Material.Position,
            },
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Waiting
            }
          });
          currentlyLoading.Add(mat.Material.MaterialID);
        }


      }
      else
      {
        // first check mat on pal
        foreach (var mat in unusedMatsOnPal.Values)
        {
          if (mat.JobUnique == face.Job.UniqueStr
            && mat.Process + 1 == face.Process
            && face.Job.GetPathGroup(mat.Process, mat.Path) == face.Job.GetPathGroup(face.Process, face.Path)
            && !currentlyLoading.Contains(mat.MaterialID)
          )
          {
            mats.Add(new InProcessMaterial()
            {
              MaterialID = mat.MaterialID,
              JobUnique = face.Job.UniqueStr,
              PartName = face.Job.PartName,
              Process = mat.Process,
              Path = mat.Path,
              Serial = mat.Serial,
              WorkorderId = mat.WorkorderId,
              SignaledInspections = mat.SignaledInspections,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                Pallet = pallet.Master.PalletNum.ToString(),
                Face = mat.Location.Face
              },
              Action = new InProcessMaterialAction()
              {
                Type = InProcessMaterialAction.ActionType.Waiting
              }
            });
            unusedMatsOnPal.Remove(mat.MaterialID);
            currentlyLoading.Add(mat.MaterialID);

            if (mats.Count >= face.Job.PartsPerPallet(face.Process, face.Path))
            {
              break;
            }
          }
        }

        // now check queue
        if (!string.IsNullOrEmpty(inputQueue))
        {
          var availableMaterial =
            _log.GetMaterialInQueue(inputQueue)
            .Where(m => !currentlyLoading.Contains(m.MaterialID))
            .Select(m => FilterMaterialAvailableToLoadOntoFace(m, face))
            .Where(m => m != null)
            .ToList();
          foreach (var mat in availableMaterial)
          {
            mats.Add(new InProcessMaterial()
            {
              MaterialID = mat.Material.MaterialID,
              JobUnique = face.Job.UniqueStr,
              PartName = face.Job.PartName,
              Process = face.Process - 1,
              Path = mat.Details.Paths != null && mat.Details.Paths.TryGetValue(Math.Max(face.Process - 1, 1), out var path) ? path : 1,
              Serial = mat.Details?.Serial,
              WorkorderId = mat.Details?.Workorder,
              SignaledInspections =
                    _log.LookupInspectionDecisions(mat.Material.MaterialID)
                    .Where(x => x.Inspect)
                    .Select(x => x.InspType)
                    .Distinct()
                    .ToList(),
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = inputQueue,
                QueuePosition = mat.Material.Position
              },
              Action = new InProcessMaterialAction()
              {
                Type = InProcessMaterialAction.ActionType.Waiting
              }
            });

            currentlyLoading.Add(mat.Material.MaterialID);

            if (mats.Count >= face.Job.PartsPerPallet(face.Process, face.Path))
            {
              break;
            }
          }
        }
      }

      // if not enough, give error and allocate more
      if (mats.Count < face.Job.PartsPerPallet(face.Process, face.Path))
      {
        Log.Debug("Unable to find enough in-process parts for {@pallet} and {@face} with {@currentlyLoading}", pallet, face, currentlyLoading);
        if (allocateNew)
        {
          Log.Warning("Unable to find enough in-process parts for {@job}-{@proc} on pallet {@pallet}", face.Job.UniqueStr, face.Process, pallet.Master.PalletNum);
        }
        for (int i = mats.Count; i < face.Job.PartsPerPallet(face.Process, face.Path); i++)
        {
          long mid = -1;
          string serial = null;
          if (allocateNew)
          {
            mid = _log.AllocateMaterialID(face.Job.UniqueStr, face.Job.PartName, face.Job.NumProcesses);
            if (_settings.SerialType == SerialType.AssignOneSerialPerMaterial)
            {
              serial = _settings.ConvertMaterialIDToSerial(mid);
              _log.RecordSerialForMaterialID(
                new JobLogDB.EventLogMaterial() { MaterialID = mid, Process = face.Process, Face = "" },
                serial,
                nowUtc
                );
            }
          }
          mats.Add(new InProcessMaterial()
          {
            MaterialID = mid,
            JobUnique = face.Job.UniqueStr,
            PartName = face.Job.PartName,
            Process = face.Process - 1,
            Path = 1,
            Serial = serial,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.Free,
            },
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Waiting
            }
          });
        }
      }

      return mats;
    }

    private PalletAndMaterial CurrentlyLoadingPallet(PalletStatus pallet, IEnumerable<LogEntry> log, DateTime nowUtc, IReadOnlyDictionary<(string progName, long revision), JobDB.ProgramRevision> programs, HashSet<long> currentlyLoading, ref bool palletStateUpdated)
    {
      var faces = GetFaces(pallet);

      TimeSpan? elapsedLoadTime = null;
      if (pallet.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
      {
        // ensure load-begin so that we know the starting time of load
        var seenLoadBegin =
          log.FirstOrDefault(e =>
            e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.StartOfCycle
          );
        if (seenLoadBegin != null)
        {
          elapsedLoadTime = nowUtc.Subtract(seenLoadBegin.EndTimeUTC);
        }
        else
        {
          elapsedLoadTime = TimeSpan.Zero;
          palletStateUpdated = true;
          _log.RecordLoadStart(
            mats: new JobLogDB.EventLogMaterial[] { },
            pallet: pallet.Master.PalletNum.ToString(),
            lulNum: pallet.CurStation.Location.Num,
            timeUTC: nowUtc
          );
        }
      }

      // first find all material being unloaded
      var matToUnload = MaterialCurrentlyOnPallet(pallet, faces, programs, log);
      var unusedMatsOnPal = matToUnload.ToDictionary(m => m.InProc.MaterialID, m => m.InProc);

      // now material to load
      var loadingIds = new HashSet<long>();
      if (
               pallet.CurrentStep is LoadStep
            ||
               (pallet.CurrentStep is UnloadStep && pallet.Master.RemainingPalletCycles > 1)
         )
      {
        foreach (var face in faces)
        {
          // find material to load
          var loadMat = MaterialToLoadOnFace(pallet, face.Value, allocateNew: false, nowUtc: nowUtc, currentlyLoading: currentlyLoading, unusedMatsOnPal: unusedMatsOnPal);
          faces[face.Key].Material.AddRange(loadMat);
          foreach (var mat in loadMat)
          {
            mat.Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Loading,
              LoadOntoPallet = pallet.Master.PalletNum.ToString(),
              LoadOntoFace = face.Key,
              ProcessAfterLoad = face.Value.Process,
              PathAfterLoad = face.Value.Path,
              ElapsedLoadUnloadTime = elapsedLoadTime
            };
            loadingIds.Add(mat.MaterialID);
          }
        }
      }

      var jobCache = new Dictionary<string, JobPlan>();
      foreach (var f in faces.Values)
        jobCache[f.Job.UniqueStr] = f.Job;

      // now material to unload or transfer
      foreach (var mat in matToUnload)
      {
        if (loadingIds.Contains(mat.InProc.MaterialID)) continue; // transfer

        if (mat.InProc.Process == mat.Details.NumProcesses)
        {
          mat.InProc.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = elapsedLoadTime
          };
        }
        else
        {
          if (!jobCache.ContainsKey(mat.InProc.JobUnique))
          {
            jobCache.Add(mat.InProc.JobUnique, _jobs.LoadJob(mat.InProc.JobUnique));
          }
          var job = jobCache[mat.InProc.JobUnique];
          mat.InProc.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = job.GetOutputQueue(mat.InProc.Process, mat.Details.Paths.ContainsKey(mat.InProc.Process) ? mat.Details.Paths[mat.InProc.Process] : 1),
            ElapsedLoadUnloadTime = elapsedLoadTime
          };
        }
        faces[mat.InProc.Location.Face ?? 1].Material.Add(mat.InProc);
      }

      return new PalletAndMaterial()
      {
        Status = pallet,
        Faces = faces.Values.ToList()
      };
    }

    private PalletAndMaterial LoadedPallet(PalletStatus pallet, IEnumerable<LogEntry> log, DateTime nowUtc, IReadOnlyDictionary<(string progName, long revision), JobDB.ProgramRevision> programs, HashSet<long> currentlyLoading, ref bool palletStateUpdated)
    {
      var faces = GetFaces(pallet);

      var loadBegin =
        log.FirstOrDefault(e =>
          e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.StartOfCycle
        );
      // check if this is the first time seeing loaded-pallet, need to record load and unload end, and create pallet cycle
      if (loadBegin != null)
      {
        palletStateUpdated = true;

        // record unload-end
        var oldMatOnPal = MaterialCurrentlyOnPallet(pallet, faces, programs, log);
        var jobCache = new Dictionary<string, JobPlan>();
        foreach (var face in faces.Values)
          jobCache[face.Job.UniqueStr] = face.Job;
        foreach (var face in oldMatOnPal.ToLookup(m => m.InProc.Location.Face))
        {
          // everything on a face shares the job, proc, and path
          var unique = face.First().InProc.JobUnique;
          var proc = face.First().InProc.Process;
          var path = face.First().InProc.Path;

          if (!jobCache.ContainsKey(unique))
          {
            jobCache.Add(unique, _jobs.LoadJob(unique));
          }
          var job = jobCache[unique];
          var queues = new Dictionary<long, string>();
          var queue = job.GetOutputQueue(proc, path);
          if (!string.IsNullOrEmpty(queue))
          {
            foreach (var mat in oldMatOnPal)
            {
              queues[mat.InProc.MaterialID] = queue;
            }
          }

          _log.RecordUnloadEnd(
            mats: face.Select(m => new JobLogDB.EventLogMaterial() { MaterialID = m.InProc.MaterialID, Process = m.InProc.Process, Face = face.Key.ToString() }),
            pallet: pallet.Master.PalletNum.ToString(),
            lulNum: loadBegin.LocationNum,
            timeUTC: nowUtc,
            elapsed: nowUtc.Subtract(loadBegin.EndTimeUTC),
            active: job.GetExpectedUnloadTime(proc, path) * face.Count(),
            unloadIntoQueues: queues
          );
        }

        // complete the pallet cycle so new cycle starts with the below Load end
        _log.CompletePalletCycle(pallet.Master.PalletNum.ToString(), nowUtc, foreignID: null);

        // add load-end for material put onto
        var unusedMatsOnPal = oldMatOnPal.ToDictionary(m => m.InProc.MaterialID, m => m.InProc);
        foreach (var face in faces)
        {
          // add 1 seconds to now so the marking of serials and load end happens after the pallet cycle
          var matToLoad = MaterialToLoadOnFace(pallet, face.Value, allocateNew: true, nowUtc: nowUtc.AddSeconds(1), currentlyLoading: currentlyLoading, unusedMatsOnPal: unusedMatsOnPal);

          _log.RecordLoadEnd(
            mats: matToLoad.Select(m => new JobLogDB.EventLogMaterial() { MaterialID = m.MaterialID, Process = face.Value.Process, Face = face.Key.ToString() }),
            pallet: pallet.Master.PalletNum.ToString(),
            lulNum: loadBegin.LocationNum,
            timeUTC: nowUtc.AddSeconds(1),
            elapsed: nowUtc.Subtract(loadBegin.EndTimeUTC),
            active: face.Value.Job.GetExpectedLoadTime(face.Value.Process, face.Value.Path) * matToLoad.Count
          );

          foreach (var mat in matToLoad)
          {
            _log.RecordPathForProcess(mat.MaterialID, mat.Process, mat.Path);
            mat.Process = face.Value.Process;
            mat.Path = face.Value.Path;
            mat.Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.OnPallet,
              Pallet = pallet.Master.PalletNum.ToString(),
              Face = face.Key
            };
            mat.Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Waiting
            };
          }

          face.Value.Material = matToLoad;
        }
      }
      else
      {
        var mats = MaterialCurrentlyOnPallet(pallet, faces, programs, log).ToLookup(m => m.InProc.Location.Face ?? 1);
        foreach (var face in faces)
        {
          face.Value.Material = mats.Contains(face.Key) ? mats[face.Key].Select(m => m.InProc).ToList() : new List<InProcessMaterial>();
        }
      }

      return new PalletAndMaterial()
      {
        Status = pallet,
        Faces = faces.Values.ToList()
      };
    }

    private void MarkProgramRunning(PalletAndMaterial pallet, IEnumerable<LogEntry> log, int iccProgram, ref bool palletStateUpdated, DateTime nowUtc)
    {
      var jobProgram = _jobs.ProgramFromCellControllerProgram(iccProgram.ToString());
      if (jobProgram == null)
      {
        Log.Error("Detected program {program} run on ICC, but Insight has no knowlede of this program", iccProgram);
        return;
      }

      foreach (var face in pallet.Faces)
      {
        var currentlyRunningStop = face.Job.GetMachiningStop(face.Process, face.Path).FirstOrDefault(stop => stop.ProgramName == jobProgram.ProgramName);

        var matIds = new HashSet<long>(face.Material.Select(m => m.MaterialID));
        var machStarts = log
          .Where(e => e.LogType == LogType.MachineCycle && e.StartOfCycle && e.Material.Any(m => matIds.Contains(m.MaterialID)))
          .ToList()
          ;
        var machEnds = log
          .Where(e => e.LogType == LogType.MachineCycle && !e.StartOfCycle && e.Material.Any(m => matIds.Contains(m.MaterialID)))
          .ToList()
          ;

        // first, check if there is a machine-start for the currently running program
        if (currentlyRunningStop != null && !machStarts.Any(e => e.Program == jobProgram.ProgramName))
        {

          if (face.Material.Count == 0)
          {
            // we must have missed the load, recreate material here as a backup
            Log.Warning("Detected program {program} without a cooresponding load event, creating new material!", jobProgram.ProgramName);
            Log.Debug("Program {program} on pallet {@pallet} and log {@log} run without a seen load event, creating material", jobProgram.ProgramName, pallet, log);

            for (int i = 1; i <= face.Job.PartsPerPallet(face.Process, face.Path); i++)
            {
              long mid = _log.AllocateMaterialID(face.Job.UniqueStr, face.Job.PartName, face.Job.NumProcesses);
              if (_settings.SerialType == SerialType.AssignOneSerialPerMaterial)
              {
                _log.RecordSerialForMaterialID(
                  new JobLogDB.EventLogMaterial() { MaterialID = mid, Process = face.Process, Face = "" },
                  _settings.ConvertMaterialIDToSerial(mid),
                  nowUtc
                  );
              }
              _log.RecordPathForProcess(mid, face.Process, face.Path);
              face.Material.Add(new InProcessMaterial()
              {
                MaterialID = mid,
                JobUnique = face.Job.UniqueStr,
                PartName = face.Job.PartName,
                Process = face.Process,
                Path = face.Path,
                Location = new InProcessMaterialLocation()
                {
                  Type = InProcessMaterialLocation.LocType.OnPallet,
                  Pallet = pallet.Status.Master.PalletNum.ToString(),
                  Face = face.Face,
                },
                Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.Waiting
                }
              });
            }
          }

          // record start of new cycle
          Log.Debug("Recording machine start for {@pallet} from logs {@log} and {program} with face {@face}", pallet, log, jobProgram.ProgramName, face);

          palletStateUpdated = true;

          machStarts.Add(_log.RecordMachineStart(
            mats: face.Material.Select(m => new JobLogDB.EventLogMaterial()
            {
              MaterialID = m.MaterialID,
              Process = m.Process,
              Face = face.Face.ToString(),
            }),
            pallet: pallet.Status.Master.PalletNum.ToString(),
            statName: pallet.Status.CurStation.Location.StationGroup,
            statNum: pallet.Status.CurStation.Location.Num,
            program: jobProgram.ProgramName,
            timeUTC: nowUtc,
            extraData: new Dictionary<string, string> {
              {"ProgramRevision", jobProgram.Revision.ToString()}
            }
          ));
        }

        // ensure machine-ends for everything not currently running
        foreach (var machStart in machStarts)
        {
          if (currentlyRunningStop != null && machStart.Program == jobProgram.ProgramName) continue;
          if (machEnds.Any(e => e.Program == machStart.Program)) continue;
          var machStop = face.Job.GetMachiningStop(face.Process, face.Path).FirstOrDefault(stop => stop.ProgramName == machStart.Program);
          if (machStop == null)
          {
            Log.Warning("Unable to find machining stop for machine cycle {@machStart} on {@face}", machStart, face);
            continue;
          }

          Log.Debug("Program changed, recording machine end for {@pallet} from start {@machStart} and {@stop} with face {@face}", pallet, machStart, machStop, face);

          palletStateUpdated = true;

          _log.RecordMachineEnd(
            mats: face.Material.Select(m => new JobLogDB.EventLogMaterial()
            {
              MaterialID = m.MaterialID,
              Process = m.Process,
              Face = face.Face.ToString()
            }),
            pallet: pallet.Status.Master.PalletNum.ToString(),
            statName: pallet.Status.CurStation.Location.StationGroup,
            statNum: pallet.Status.CurStation.Location.Num,
            program: machStart.Program,
            result: "",
            timeUTC: nowUtc,
            elapsed: nowUtc.Subtract(machStart.EndTimeUTC),
            active: machStop.ExpectedCycleTime,
            extraData: new Dictionary<string, string> {
              {"ProgramRevision", machStart.ProgramDetails["ProgramRevision"] }
            }
          );

          foreach (var mat in face.Material)
          {
            _log.RecordPathForProcess(mat.MaterialID, mat.Process, mat.Path);
          }
          MakeInspectionDecisions(face, nowUtc);
        }

        if (currentlyRunningStop != null)
        {
          var machStart = machStarts.First(e => e.Program == jobProgram.ProgramName);
          var elapsed = nowUtc.Subtract(machStart.EndTimeUTC);
          foreach (var mat in face.Material)
          {
            mat.Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = jobProgram.ProgramName + " rev" + jobProgram.Revision.ToString(),
              ElapsedMachiningTime = elapsed,
              ExpectedRemainingMachiningTime = currentlyRunningStop.ExpectedCycleTime.Subtract(elapsed)
            };
          }
        }

      }

    }

    private void EnsureAllMachineEnds(PalletAndMaterial pallet, IEnumerable<int> iccPrograms, IEnumerable<LogEntry> log, DateTime nowUtc, ref bool palletStateUpdated)
    {
      var progsToCheck = new Dictionary<string, JobDB.ProgramRevision>();
      foreach (var iccProg in iccPrograms)
      {
        var jobProgram = _jobs.ProgramFromCellControllerProgram(iccProg.ToString());
        if (jobProgram != null)
        {
          progsToCheck[jobProgram.ProgramName] = jobProgram;
        }
      }
      foreach (var face in pallet.Faces)
      {
        foreach (var machStop in face.Job.GetMachiningStop(face.Process, face.Path))
        {
          var machProg = machStop.ProgramName;
          if (!progsToCheck.ContainsKey(machProg)) continue;

          var matIds = new HashSet<long>(face.Material.Select(m => m.MaterialID));
          var machStart = log
            .Where(e => e.LogType == LogType.MachineCycle && e.StartOfCycle && e.Program == machProg && e.Material.Any(m => matIds.Contains(m.MaterialID)))
            .FirstOrDefault()
            ;
          var machEnd = log
            .Where(e => e.LogType == LogType.MachineCycle && !e.StartOfCycle && e.Program == machProg && e.Material.Any(m => matIds.Contains(m.MaterialID)))
            .FirstOrDefault()
            ;

          if (machStart != null && machEnd == null)
          {
            // program changed, record end of previous program
            Log.Debug("Recording machine end for {@pallet} from logs {@log} with face {@face}", pallet, log, face);
            palletStateUpdated = true;

            _log.RecordMachineEnd(
              mats: face.Material.Select(m => new JobLogDB.EventLogMaterial()
              {
                MaterialID = m.MaterialID,
                Process = m.Process,
                Face = face.Face.ToString()
              }),
              pallet: pallet.Status.Master.PalletNum.ToString(),
              statName: pallet.Status.CurStation.Location.StationGroup,
              statNum: pallet.Status.CurStation.Location.Num,
              program: machStart.Program,
              result: "",
              timeUTC: nowUtc,
              elapsed: nowUtc.Subtract(machStart.EndTimeUTC),
              active: machStop.ExpectedCycleTime,
              extraData: new Dictionary<string, string> {
                {"ProgramRevision", progsToCheck[machProg].Revision.ToString()}
              }
            );
            foreach (var mat in face.Material)
            {
              _log.RecordPathForProcess(mat.MaterialID, mat.Process, mat.Path);
            }
            MakeInspectionDecisions(face, nowUtc);
          }
          else if (machStart == null && machEnd == null)
          {
            Log.Warning("Missed machine cycle for {part} process {process} on machines {@machs} with program {prog}",
              face.Job.PartName, face.Process, machStop.Stations, machProg);
          }
        }
      }
    }

    private List<InProcessMaterial> QueuedMaterial(HashSet<long> matsOnPallets)
    {
      var mats = new List<InProcessMaterial>();

      foreach (var mat in _log.GetMaterialInAllQueues())
      {
        if (matsOnPallets.Contains(mat.MaterialID)) continue;

        var lastProc = _log.GetLogForMaterial(mat.MaterialID)
          .SelectMany(m => m.Material)
          .Where(m => m.MaterialID == mat.MaterialID)
          .Max(m => m.Process);

        var matDetails = _log.GetMaterialDetails(mat.MaterialID);
        mats.Add(new InProcessMaterial()
        {
          MaterialID = mat.MaterialID,
          JobUnique = mat.Unique,
          PartName = mat.PartNameOrCasting,
          Process = lastProc,
          Path = matDetails?.Paths != null && matDetails.Paths.TryGetValue(Math.Max(1, lastProc), out var path) ? path : 1,
          Serial = matDetails?.Serial,
          WorkorderId = matDetails?.Workorder,
          SignaledInspections =
                _log.LookupInspectionDecisions(mat.MaterialID)
                .Where(x => x.Inspect)
                .Select(x => x.InspType)
                .Distinct()
                .ToList(),
          Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = mat.Queue,
            QueuePosition = mat.Position,
          },
          Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Waiting
          }
        });
      }

      return mats;
    }

    private void MakeInspectionDecisions(PalletFace face, DateTime timeUTC)
    {
      foreach (var mat in face.Material)
      {
        var inspections = face.Job.PathInspections(face.Process, face.Path);
        if (inspections.Count > 0)
        {
          _log.MakeInspectionDecisions(mat.MaterialID, mat.Process, inspections, timeUTC);
          Log.Debug("Making inspection decision for " + string.Join(",", inspections.Select(x => x.InspectionType)) + " material " + mat.MaterialID.ToString() +
                    " completed at time " + timeUTC.ToLocalTime().ToString() +
                    " part " + mat.PartName);
        }
      }
    }

    private Dictionary<string, int> CountStartedMaterial(PlannedSchedule schedule, IEnumerable<PalletAndMaterial> pals)
    {
      var cnts = new Dictionary<string, int>();
      foreach (var uniq in schedule.Jobs.Select(j => j.UniqueStr))
      {
        var loadedCnt =
          _log.GetLogForJobUnique(uniq)
            .Where(e => (e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD") || e.LogType == LogType.MachineCycle)
            .SelectMany(e => e.Material)
            .Where(m => m.JobUniqueStr == uniq)
            .Select(m => m.MaterialID)
            .Distinct()
            .Count();

        var loadingCnt =
          pals
            .SelectMany(p => p.Faces)
            .SelectMany(f => f.Material)
            .Where(m => m.JobUnique == uniq &&
                        m.Action.Type == InProcessMaterialAction.ActionType.Loading &&
                        m.Action.ProcessAfterLoad == 1
            )
            .Count();

        cnts.Add(uniq, loadedCnt + loadingCnt);
      }
      return cnts;
    }

    private class QueuedMaterialWithDetails
    {
      public JobLogDB.QueuedMaterial Material { get; set; }
      public MaterialDetails Details { get; set; }
    }

    private QueuedMaterialWithDetails FilterMaterialAvailableToLoadOntoFace(JobLogDB.QueuedMaterial mat, PalletFace face)
    {
      if (face.Process == 1)
      {
        // check for casting on process 1
        var casting = face.Job.GetCasting(face.Path);
        if (string.IsNullOrEmpty(casting)) casting = face.Job.PartName;

        if (string.IsNullOrEmpty(mat.Unique) && mat.PartNameOrCasting == casting)
        {
          return new QueuedMaterialWithDetails()
          {
            Material = mat,
            Details = _log.GetMaterialDetails(mat.MaterialID)
          };
        }
      }

      // now check unique, process, and path group match
      if (mat.Unique != face.Job.UniqueStr) return null;

      var proc = _log.GetLogForMaterial(mat.MaterialID)
        .SelectMany(m => m.Material)
        .Where(m => m.MaterialID == mat.MaterialID)
        .Max(m => m.Process);

      if (proc + 1 != face.Process) return null;

      // now path group
      var details = _log.GetMaterialDetails(mat.MaterialID);
      if (details.Paths != null && details.Paths.Count > 0)
      {
        var path = details.Paths.Aggregate((max, v) => max.Key > v.Key ? max : v);
        var group = face.Job.GetPathGroup(process: path.Key, path: path.Value);
        if (group == face.Job.GetPathGroup(face.Process, face.Path))
        {
          return new QueuedMaterialWithDetails()
          {
            Material = mat,
            Details = details
          };
        }
      }
      else
      {
        Log.Warning("Material {matId} has no path groups! {@details}", mat.MaterialID, details);
      }

      return null;
    }

    private Dictionary<(string progName, long revision), JobDB.ProgramRevision> FindProgramNums(PlannedSchedule schedule)
    {
      var stops =
        schedule.Jobs
          .SelectMany(j => Enumerable.Range(1, j.NumProcesses).SelectMany(proc =>
            Enumerable.Range(1, j.GetNumPaths(proc)).SelectMany(path =>
              j.GetMachiningStop(proc, path)
            )
          ));

      var progs = new Dictionary<(string progName, long revision), JobDB.ProgramRevision>();
      foreach (var stop in stops)
      {
        if (stop.ProgramRevision.HasValue)
        {
          progs[(stop.ProgramName, stop.ProgramRevision.Value)] = _jobs.LoadProgram(stop.ProgramName, stop.ProgramRevision.Value);
        }
        else
        {
          Log.Error("Job stop {@stop} does not have a program revision!", stop);
        }
      }
      return progs;
    }
  }
}