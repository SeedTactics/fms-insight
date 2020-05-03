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
    public IReadOnlyList<PalletFace> Faces { get; set; }
    public IReadOnlyList<LogEntry> Log { get; set; }
    public bool IsLoading { get; set; }
    public MachineStatus MachineStatus { get; set; } // non-null if pallet is at machine
  }

  public class CellState
  {
    public NiigataStatus Status { get; set; }
    public PlannedSchedule Schedule { get; set; }
    public bool PalletStateUpdated { get; set; }
    public List<PalletAndMaterial> Pallets { get; set; }
    public List<InProcessMaterial> QueuedMaterial { get; set; }
    public Dictionary<(string uniq, int proc1path), int> JobQtyRemainingOnProc1 { get; set; }
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
    private HashSet<string> _reclampGroupNames;

    public CreateCellState(JobLogDB l, JobDB jobs, IRecordFacesForPallet r, FMSSettings s, HashSet<string> reclampGrpNames)
    {
      _log = l;
      _jobs = jobs;
      _recordFaces = r;
      _settings = s;
      _reclampGroupNames = reclampGrpNames;
    }

    public CellState BuildCellState(NiigataStatus status, PlannedSchedule sch)
    {
      var palletStateUpdated = false;

      // sort pallets by loadBegin so that the assignment of material from queues to pallets is consistent
      var pals = status.Pallets.Select(p => BuildCurrentPallet(status.Machines, p))
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
      foreach (var pal in pals.Where(p => !p.IsLoading && !p.Status.Master.NoWork))
      {
        LoadedPallet(pal, status.TimeOfStatusUTC, currentlyLoading, ref palletStateUpdated);
      }

      // next, go through pallets currently being loaded
      foreach (var pal in pals.Where(p => p.IsLoading && !p.Status.Master.NoWork))
      {
        CurrentlyLoadingPallet(pal, status.TimeOfStatusUTC, currentlyLoading, ref palletStateUpdated);
      }

      foreach (var pal in pals.Where(p => p.Status.Master.NoWork))
      {
        // need to check if an unload with no load happened and if so record unload end
        LoadedPallet(pal, status.TimeOfStatusUTC, currentlyLoading, ref palletStateUpdated);
      }

      pals.Sort((p1, p2) => p1.Status.Master.PalletNum.CompareTo(p2.Status.Master.PalletNum));
      return new CellState()
      {
        Status = status,
        Schedule = sch,
        PalletStateUpdated = palletStateUpdated,
        Pallets = pals,
        QueuedMaterial = QueuedMaterial(new HashSet<long>(pals.SelectMany(p => p.Faces).SelectMany(p => p.Material).Select(m => m.MaterialID))),
        JobQtyRemainingOnProc1 = CountRemainingQuantity(sch, pals),
        ProgramNums = FindProgramNums(sch),
      };
    }

    private PalletAndMaterial BuildCurrentPallet(IReadOnlyDictionary<int, MachineStatus> machines, PalletStatus pallet)
    {
      Dictionary<int, PalletFace> faces;
      if (pallet.Master.NoWork)
      {
        faces = new Dictionary<int, PalletFace>();
      }
      else
      {
        faces =
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

      var log = _log.CurrentPalletLog(pallet.Master.PalletNum.ToString());
      foreach (var m in log
        .Where(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && !e.StartOfCycle)
        .SelectMany(e => e.Material))
      {
        var details = _log.GetMaterialDetails(m.MaterialID);
        int? lastCompletedRouteStopIdx = null;
        if (faces.TryGetValue(int.Parse(m.Face), out var face))
        {
          var stops = face.Job.GetMachiningStop(face.Process, face.Path).ToList();
          var completedMachineSteps =
            pallet.Master.Routes
            .Take(pallet.Tracking.BeforeCurrentStep ? pallet.Tracking.CurrentStepNum - 1 : pallet.Tracking.CurrentStepNum)
            .Where(r => r is MachiningStep || r is ReclampStep)
            .Count();

          if (completedMachineSteps > 0)
          {
            lastCompletedRouteStopIdx = completedMachineSteps - 1;
          }
          face.Material.Add(
            new InProcessMaterial()
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
            }
          );
        }
      }

      MachineStatus machStatus = null;
      if (pallet.CurStation.Location.Location == PalletLocationEnum.Machine)
      {
        machines.TryGetValue(pallet.CurStation.Location.Num, out machStatus);
      }


      return new PalletAndMaterial()
      {
        Status = pallet,
        Faces = faces.Values.ToList(),
        Log = log,
        IsLoading = (pallet.CurrentStep is LoadStep && pallet.Tracking.BeforeCurrentStep)
                    ||
                    (pallet.CurrentStep is UnloadStep && pallet.Tracking.BeforeCurrentStep),
        MachineStatus = machStatus
      };
    }

    private void SetMaterialToLoadOnFace(
          PalletStatus pallet,
          PalletFace face,
          bool allocateNew,
          DateTime nowUtc,
          HashSet<long> currentlyLoading,
          Dictionary<long, InProcessMaterial> unusedMatsOnPal
    )
    {
      face.Material.Clear();

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
          face.Material.Add(new InProcessMaterial()
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
          face.Material.Add(new InProcessMaterial()
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
            face.Material.Add(new InProcessMaterial()
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

            if (face.Material.Count >= face.Job.PartsPerPallet(face.Process, face.Path))
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
            face.Material.Add(new InProcessMaterial()
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

            if (face.Material.Count >= face.Job.PartsPerPallet(face.Process, face.Path))
            {
              break;
            }
          }
        }
      }

      // if not enough, give error and allocate more
      if (face.Material.Count < face.Job.PartsPerPallet(face.Process, face.Path))
      {
        Log.Debug("Unable to find enough in-process parts for {@pallet} and {@face} with {@currentlyLoading}", pallet, face, currentlyLoading);
        if (allocateNew)
        {
          Log.Warning("Unable to find enough in-process parts for {@job}-{@proc} on pallet {@pallet}", face.Job.UniqueStr, face.Process, pallet.Master.PalletNum);
        }
        for (int i = face.Material.Count; i < face.Job.PartsPerPallet(face.Process, face.Path); i++)
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
          face.Material.Add(new InProcessMaterial()
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
    }

    private void LoadedPallet(PalletAndMaterial pallet, DateTime nowUtc, HashSet<long> currentlyLoading, ref bool palletStateUpdated)
    {
      // first, check if this is the first time seeing pallet as loaded since a load began
      var loadBegin =
        pallet.Log.FirstOrDefault(e =>
          e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.StartOfCycle
        );
      if (loadBegin != null)
      {
        // first time seeing pallet after it loaded, record the pallet cycle and adjust the material on the faces
        palletStateUpdated = true;
        AddPalletCycle(pallet, loadBegin, currentlyLoading, nowUtc);
      }

      EnsureAllNonloadStopsHaveEvents(pallet, nowUtc, ref palletStateUpdated);
    }

    private void CurrentlyLoadingPallet(PalletAndMaterial pallet, DateTime nowUtc, HashSet<long> currentlyLoading, ref bool palletStateUpdated)
    {
      TimeSpan? elapsedLoadTime = null;
      if (pallet.Status.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
      {
        // ensure load-begin so that we know the starting time of load
        var seenLoadBegin =
          pallet.Log.FirstOrDefault(e =>
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
            pallet: pallet.Status.Master.PalletNum.ToString(),
            lulNum: pallet.Status.CurStation.Location.Num,
            timeUTC: nowUtc
          );
        }
      }

      // first find current state of pallet before the pending load completes
      EnsureAllNonloadStopsHaveEvents(pallet, nowUtc, ref palletStateUpdated);

      // now clear the faces of material and calculate what will be on the pallet after the load ends
      var unusedMatsOnPal = pallet.Faces.SelectMany(f => f.Material).ToDictionary(m => m.MaterialID, m => m);

      // check for things to load
      var loadingIds = new HashSet<long>();
      if (pallet.Status.CurrentStep is LoadStep
          ||
             (pallet.Status.CurrentStep is UnloadStep && pallet.Status.Master.RemainingPalletCycles > 1)
         )
      {
        foreach (var face in pallet.Faces)
        {
          // find material to load
          SetMaterialToLoadOnFace(pallet.Status, face, allocateNew: false, nowUtc: nowUtc, currentlyLoading: currentlyLoading, unusedMatsOnPal: unusedMatsOnPal);
          foreach (var mat in face.Material)
          {
            mat.Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Loading,
              LoadOntoPallet = pallet.Status.Master.PalletNum.ToString(),
              LoadOntoFace = face.Face,
              ProcessAfterLoad = face.Process,
              PathAfterLoad = face.Path,
              ElapsedLoadUnloadTime = elapsedLoadTime
            };
            loadingIds.Add(mat.MaterialID);
          }
        }
      }

      var jobCache = new Dictionary<string, JobPlan>();
      foreach (var f in pallet.Faces)
        jobCache[f.Job.UniqueStr] = f.Job;

      // now material to unload or transfer
      foreach (var mat in unusedMatsOnPal.Values)
      {
        if (loadingIds.Contains(mat.MaterialID)) continue; // transfer

        if (!jobCache.ContainsKey(mat.JobUnique))
        {
          jobCache.Add(mat.JobUnique, _jobs.LoadJob(mat.JobUnique));
        }
        var job = jobCache[mat.JobUnique];

        if (mat.Process == job.NumProcesses)
        {
          mat.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = elapsedLoadTime
          };
        }
        else
        {
          mat.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = job.GetOutputQueue(mat.Process, mat.Path),
            ElapsedLoadUnloadTime = elapsedLoadTime
          };
        }
      }
    }

    private void AddPalletCycle(PalletAndMaterial pallet, LogEntry loadBegin, HashSet<long> currentlyLoading, DateTime nowUtc)
    {
      // record unload-end
      foreach (var face in pallet.Faces)
      {
        // everything on a face shares the job, proc, and path
        var queues = new Dictionary<long, string>();
        var queue = face.Job.GetOutputQueue(face.Process, face.Path);
        if (!string.IsNullOrEmpty(queue))
        {
          foreach (var mat in face.Material)
          {
            queues[mat.MaterialID] = queue;
          }
        }

        _log.RecordUnloadEnd(
          mats: face.Material.Select(m => new JobLogDB.EventLogMaterial() { MaterialID = m.MaterialID, Process = m.Process, Face = face.Face.ToString() }),
          pallet: pallet.Status.Master.PalletNum.ToString(),
          lulNum: loadBegin.LocationNum,
          timeUTC: nowUtc,
          elapsed: nowUtc.Subtract(loadBegin.EndTimeUTC),
          active: face.Job.GetExpectedUnloadTime(face.Process, face.Path) * face.Material.Count,
          unloadIntoQueues: queues
        );
      }

      // complete the pallet cycle so new cycle starts with the below Load end
      _log.CompletePalletCycle(pallet.Status.Master.PalletNum.ToString(), nowUtc, foreignID: null);

      // add load-end for material put onto
      var unusedMatsOnPal = pallet.Faces.SelectMany(f => f.Material).ToDictionary(m => m.MaterialID, m => m);
      var newLoadEvents = new List<LogEntry>();
      foreach (var face in pallet.Faces)
      {
        // add 1 seconds to now so the marking of serials and load end happens after the pallet cycle
        SetMaterialToLoadOnFace(
          pallet.Status,
          face,
          allocateNew: true,
          nowUtc: nowUtc.AddSeconds(1),
          currentlyLoading: currentlyLoading,
          unusedMatsOnPal: unusedMatsOnPal);

        newLoadEvents.AddRange(_log.RecordLoadEnd(
          mats: face.Material.Select(m => new JobLogDB.EventLogMaterial() { MaterialID = m.MaterialID, Process = face.Process, Face = face.ToString() }),
          pallet: pallet.Status.Master.PalletNum.ToString(),
          lulNum: loadBegin.LocationNum,
          timeUTC: nowUtc.AddSeconds(1),
          elapsed: nowUtc.Subtract(loadBegin.EndTimeUTC),
          active: face.Job.GetExpectedLoadTime(face.Process, face.Path) * face.Material.Count
        ));

        foreach (var mat in face.Material)
        {
          _log.RecordPathForProcess(mat.MaterialID, mat.Process, mat.Path);
          mat.Process = face.Process;
          mat.Path = face.Path;
          mat.Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.OnPallet,
            Pallet = pallet.Status.Master.PalletNum.ToString(),
            Face = face.Face
          };
          mat.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Waiting
          };
        }
      }

      pallet.Log = newLoadEvents;
    }

    private void EnsureAllNonloadStopsHaveEvents(PalletAndMaterial pallet, DateTime nowUtc, ref bool palletStateUpdated)
    {
      var unusedLogs = pallet.Log.ToList();

      foreach (var face in pallet.Faces)
      {
        var matIds = new HashSet<long>(face.Material.Select(m => m.MaterialID));

        foreach (var ss in MatchStopsAndSteps(pallet, face))
        {
          switch (ss.IccStep)
          {
            case MachiningStep step:
              // find log events if they exist
              var machStart = unusedLogs
                .Where(e => e.LogType == LogType.MachineCycle && e.StartOfCycle && e.Program == ss.JobStop.ProgramName && e.Material.Any(m => matIds.Contains(m.MaterialID)))
                .FirstOrDefault()
                ;
              var machEnd = unusedLogs
                .Where(e => e.LogType == LogType.MachineCycle && !e.StartOfCycle && e.Program == ss.JobStop.ProgramName && e.Material.Any(m => matIds.Contains(m.MaterialID)))
                .FirstOrDefault()
                ;
              if (machStart != null) unusedLogs.Remove(machStart);
              if (machEnd != null) unusedLogs.Remove(machEnd);

              if (ss.StopCompleted)
              {
                if (machEnd == null)
                {
                  RecordMachineEnd(pallet, face, ss, machStart, nowUtc, ref palletStateUpdated);
                }
              }
              else
              {
                if (pallet.Status.CurStation.Location.Location == PalletLocationEnum.Machine && step.Machines.Contains(pallet.Status.CurStation.Location.Num))
                {
                  RecordPalletAtMachine(pallet, face, ss, machStart, machEnd, nowUtc, ref palletStateUpdated);
                }
              }
              break;

            case ReclampStep step:
              // find log events if they exist
              var reclampStart = unusedLogs
                .Where(e => e.LogType == LogType.LoadUnloadCycle && e.StartOfCycle && e.Result == ss.JobStop.StationGroup && e.Material.Any(m => matIds.Contains(m.MaterialID)))
                .FirstOrDefault()
                ;
              var reclampEnd = unusedLogs
                .Where(e => e.LogType == LogType.LoadUnloadCycle && !e.StartOfCycle && e.Result == ss.JobStop.StationGroup && e.Material.Any(m => matIds.Contains(m.MaterialID)))
                .FirstOrDefault()
                ;
              if (reclampStart != null) unusedLogs.Remove(reclampStart);
              if (reclampEnd != null) unusedLogs.Remove(reclampEnd);

              if (ss.StopCompleted)
              {
                if (reclampEnd == null)
                {
                  RecordReclampEnd(pallet, face, ss, reclampStart, nowUtc, ref palletStateUpdated);
                }
              }
              else
              {
                if (pallet.Status.CurStation.Location.Location == PalletLocationEnum.LoadUnload && step.Reclamp.Contains(pallet.Status.CurStation.Location.Num))
                {
                  MarkReclampInProgress(pallet, face, ss, reclampStart, nowUtc, ref palletStateUpdated);
                }
              }
              break;

          }
        }
      }

      if (pallet.Status.CurrentStep is UnloadStep && pallet.Status.Tracking.BeforeCurrentStep)
      {
        foreach (var face in pallet.Faces)
        {
          MakeInspectionDecisions(face, nowUtc);
        }
      }
    }

    public class StopAndStep
    {
      public JobMachiningStop JobStop { get; set; }
      public int IccStepNum { get; set; } // 1-indexed
      public RouteStep IccStep { get; set; }
      public bool StopCompleted { get; set; }
    }

    private IReadOnlyList<StopAndStep> MatchStopsAndSteps(PalletAndMaterial pallet, PalletFace face)
    {
      var ret = new List<StopAndStep>();

      int stepIdx = 0;

      foreach (var stop in face.Job.GetMachiningStop(face.Process, face.Path))
      {
        // find out if job stop is machine or reclamp and which program
        string iccMachineProg = null;
        bool isReclamp = false;
        if (_reclampGroupNames.Contains(stop.StationGroup))
        {
          isReclamp = true;
        }
        else
        {
          if (stop.ProgramRevision.HasValue)
          {
            iccMachineProg = _jobs.LoadProgram(stop.ProgramName, stop.ProgramRevision.Value)?.CellControllerProgramName;
          }
          else
          {
            iccMachineProg = stop.ProgramName;
          }
        }


        // advance the pallet steps until we find a match
        RouteStep step;
        while (stepIdx < pallet.Status.Master.Routes.Count)
        {
          step = pallet.Status.Master.Routes[stepIdx];

          switch (step)
          {
            case MachiningStep machStep:
              if (!string.IsNullOrEmpty(iccMachineProg)
                  && stop.Stations.All(s => machStep.Machines.Contains(s))
                  && machStep.ProgramNumsToRun.Select(p => p.ToString()).Contains(iccMachineProg)
                 )
              {
                goto matchingStep;
              }
              break;

            case ReclampStep reclampStep:
              if (isReclamp && stop.Stations.All(s => reclampStep.Reclamp.Contains(s)))
              {
                goto matchingStep;
              }
              break;
          }

          stepIdx += 1;
        }

        Log.Warning("Unable to match job stops to route steps for {@pallet} and {@face}", pallet, face);
        return ret;

      matchingStep:;

        // add the matching stop and step
        ret.Add(new StopAndStep()
        {
          JobStop = stop,
          IccStepNum = stepIdx + 1,
          StopCompleted =
            (stepIdx + 1 < pallet.Status.Tracking.CurrentStepNum)
            ||
            (stepIdx + 1 == pallet.Status.Tracking.CurrentStepNum && !pallet.Status.Tracking.BeforeCurrentStep)
        });

        if (stepIdx + 1 == pallet.Status.Tracking.CurrentStepNum)
        {
          break;
        }

      }
      return ret;
    }

    private void RecordPalletAtMachine(PalletAndMaterial pallet, PalletFace face, StopAndStep ss, LogEntry machineStart, LogEntry machineEnd, DateTime nowUtc, ref bool palletStateUpdated)
    {
      bool runningProgram = false;
      if (pallet.MachineStatus != null && pallet.MachineStatus.Machining)
      {
        if (ss.JobStop.ProgramRevision.HasValue)
        {
          runningProgram =
            pallet.MachineStatus.CurrentlyExecutingProgram.ToString()
            ==
            _jobs.LoadProgram(ss.JobStop.ProgramName, ss.JobStop.ProgramRevision.Value).CellControllerProgramName;
        }
        else
        {
          runningProgram = pallet.MachineStatus.CurrentlyExecutingProgram.ToString() == ss.JobStop.ProgramName;
        }
      }

      if (runningProgram && machineStart == null)
      {
        // record start of new cycle
        Log.Debug("Recording machine start for {@pallet} and face {@face} for stop {@ss}", pallet, face, ss);

        palletStateUpdated = true;

        _log.RecordMachineStart(
          mats: face.Material.Select(m => new JobLogDB.EventLogMaterial()
          {
            MaterialID = m.MaterialID,
            Process = m.Process,
            Face = face.Face.ToString(),
          }),
          pallet: pallet.Status.Master.PalletNum.ToString(),
          statName: pallet.Status.CurStation.Location.StationGroup,
          statNum: pallet.Status.CurStation.Location.Num,
          program: ss.JobStop.ProgramName,
          timeUTC: nowUtc,
          extraData: !ss.JobStop.ProgramRevision.HasValue ? null : new Dictionary<string, string> {
              {"ProgramRevision", ss.JobStop.ProgramRevision.Value.ToString()}
          }
        );

        var elapsed = TimeSpan.Zero;
        foreach (var m in face.Material)
        {
          m.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = ss.JobStop.ProgramRevision.HasValue ? ss.JobStop.ProgramName + " rev" + ss.JobStop.ProgramRevision.Value.ToString() : ss.JobStop.ProgramName,
            ElapsedMachiningTime = elapsed,
            ExpectedRemainingMachiningTime = ss.JobStop.ExpectedCycleTime.Subtract(elapsed)
          };
        }
      }
      else if (runningProgram)
      {
        // update material to be running
        foreach (var m in face.Material)
        {
          m.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Machining,
            Program = ss.JobStop.ProgramRevision.HasValue ? ss.JobStop.ProgramName + " rev" + ss.JobStop.ProgramRevision.Value.ToString() : ss.JobStop.ProgramName,
            ElapsedMachiningTime = TimeSpan.Zero,
            ExpectedRemainingMachiningTime = ss.JobStop.ExpectedCycleTime
          };
        }
      }
      else if (!runningProgram && machineStart != null && machineEnd == null)
      {
        // a different program started, record machine end
        RecordMachineEnd(pallet, face, ss, machineStart, nowUtc, ref palletStateUpdated);
      }
    }

    private void RecordMachineEnd(PalletAndMaterial pallet, PalletFace face, StopAndStep ss, LogEntry machStart, DateTime nowUtc, ref bool palletStateUpdated)
    {
      Log.Debug("Recording machine end for {@pallet} and face {@face} for stop {@ss} with logs {@machStart}", pallet, face, ss, machStart);
      palletStateUpdated = true;

      _log.RecordMachineEnd(
        mats: face.Material.Select(m => new JobLogDB.EventLogMaterial()
        {
          MaterialID = m.MaterialID,
          Process = m.Process,
          Face = face.Face.ToString()
        }),
        pallet: pallet.Status.Master.PalletNum.ToString(),
        statName: machStart != null ? machStart.LocationName : pallet.Status.CurStation.Location.StationGroup,
        statNum: machStart != null ? machStart.LocationNum : pallet.Status.CurStation.Location.Num,
        program: ss.JobStop.ProgramName,
        result: "",
        timeUTC: nowUtc,
        elapsed: machStart != null ? nowUtc.Subtract(machStart.EndTimeUTC) : TimeSpan.Zero, // TODO: lookup start from SQL?
        active: ss.JobStop.ExpectedCycleTime,
        extraData: !ss.JobStop.ProgramRevision.HasValue ? null : new Dictionary<string, string> {
                {"ProgramRevision", ss.JobStop.ProgramRevision.Value.ToString()}
        }
      );
    }

    private void MarkReclampInProgress(PalletAndMaterial pallet, PalletFace face, StopAndStep ss, LogEntry reclampStart, DateTime nowUtc, ref bool palletStateUpdated)
    {
      if (reclampStart == null)
      {
        Log.Debug("Recording reclamp start for {@pallet} and face {@face} for stop {@ss}", pallet, face, ss);
        palletStateUpdated = true;
        _log.RecordManualWorkAtLULStart(
          mats: face.Material.Select(m => new JobLogDB.EventLogMaterial()
          {
            MaterialID = m.MaterialID,
            Process = m.Process,
            Face = face.Face.ToString()
          }),
          pallet: pallet.Status.Master.PalletNum.ToString(),
          lulNum: pallet.Status.CurStation.Location.Num,
          operationName: ss.JobStop.StationGroup,
          timeUTC: nowUtc
        );

        foreach (var m in face.Material)
        {
          m.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            ElapsedLoadUnloadTime = TimeSpan.Zero
          };
        }
      }
      else
      {
        foreach (var m in face.Material)
        {
          m.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            ElapsedLoadUnloadTime = nowUtc.Subtract(reclampStart.EndTimeUTC)
          };
        }
      }
    }

    private void RecordReclampEnd(PalletAndMaterial pallet, PalletFace face, StopAndStep ss, LogEntry reclampStart, DateTime nowUtc, ref bool palletStateUpdated)
    {
      Log.Debug("Recording machine end for {@pallet} and face {@face} for stop {@ss} with logs {@machStart}", pallet, face, ss, reclampStart);
      palletStateUpdated = true;

      _log.RecordManualWorkAtLULEnd(
        mats: face.Material.Select(m => new JobLogDB.EventLogMaterial()
        {
          MaterialID = m.MaterialID,
          Process = m.Process,
          Face = face.Face.ToString()
        }),
        pallet: pallet.Status.Master.PalletNum.ToString(),
        lulNum: reclampStart != null ? reclampStart.LocationNum : pallet.Status.CurStation.Location.Num,
        operationName: ss.JobStop.StationGroup,
        timeUTC: nowUtc,
        elapsed: reclampStart != null ? nowUtc.Subtract(reclampStart.EndTimeUTC) : TimeSpan.Zero, // TODO: lookup start from SQL?
        active: ss.JobStop.ExpectedCycleTime
      );
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


    #region Material Computations
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

    private Dictionary<(string uniq, int proc1path), int> CountRemainingQuantity(PlannedSchedule schedule, IEnumerable<PalletAndMaterial> pals)
    {
      var cnts = new Dictionary<(string uniq, int proc1path), int>();
      foreach (var job in schedule.Jobs)
      {
        if (_jobs.LoadDecrementsForJob(job.UniqueStr).Count == 0)
        {
          var loadedCnt =
            _log.GetLogForJobUnique(job.UniqueStr)
              .Where(e => (e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD") || e.LogType == LogType.MachineCycle)
              .SelectMany(e => e.Material)
              .Where(m => m.JobUniqueStr == job.UniqueStr)
              .Select(m => m.MaterialID)
              .Distinct()
              .Select(m =>
              {
                var details = _log.GetMaterialDetails(m);
                if (details != null && details.Paths != null && details.Paths.TryGetValue(1, out var path))
                {
                  return new { MatId = m, Path = path };
                }
                else
                {
                  return new { MatId = m, Path = 1 };
                }
              })
              .GroupBy(m => m.Path)
              .ToDictionary(g => g.Key, g => g.Count());

          var loadingCnt =
            pals
              .SelectMany(p => p.Faces)
              .SelectMany(f => f.Material)
              .Where(m => m.JobUnique == job.UniqueStr &&
                          m.Action.Type == InProcessMaterialAction.ActionType.Loading &&
                          m.Action.ProcessAfterLoad == 1
              )
              .GroupBy(m => m.Action.PathAfterLoad ?? 1)
              .ToDictionary(g => g.Key, g => g.Count());

          for (int path = 1; path <= job.GetNumPaths(process: 1); path += 1)
          {
            var started = loadedCnt.TryGetValue(path, out var cnt) ? cnt : 0;
            var loading = loadingCnt.TryGetValue(path, out var cnt2) ? cnt2 : 0;
            cnts.Add((uniq: job.UniqueStr, proc1path: path), job.GetPlannedCyclesOnFirstProcess(path) - started - loading);
          }
        }
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
      }
      return progs;
    }
    #endregion
  }
}