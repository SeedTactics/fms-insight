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
    public bool FaceIsMissingMaterial { get; set; }
    public IEnumerable<ProgramsForProcess> Programs { get; set; }
  }

  public class InProcessMaterialAndJob
  {
    public InProcessMaterial Mat { get; set; }
    public JobPlan Job { get; set; }
    public IEnumerable<WorkorderProgram> WorkorderPrograms { get; set; }
  }

  public class PalletAndMaterial
  {
    public PalletStatus Status { get; set; }
    public IReadOnlyList<PalletFace> CurrentOrLoadingFaces { get; set; }
    public List<InProcessMaterialAndJob> Material { get; set; }
    public IReadOnlyList<LogEntry> Log { get; set; }
    public bool IsLoading { get; set; }
    public bool ManualControl { get; set; }
    public MachineStatus MachineStatus { get; set; } // non-null if pallet is at machine
  }

  public class CellState
  {
    public NiigataStatus Status { get; set; }
    public ICollection<JobPlan> UnarchivedJobs { get; set; }
    public bool PalletStateUpdated { get; set; }
    public List<PalletAndMaterial> Pallets { get; set; }
    public List<InProcessMaterialAndJob> QueuedMaterial { get; set; }
    public Dictionary<(string uniq, int proc1path), int> JobQtyRemainingOnProc1 { get; set; }
    public Dictionary<(string progName, long revision), ProgramRevision> ProgramsInUse { get; set; }
    public List<ProgramRevision> OldUnusedPrograms { get; set; }
  }

  public interface IBuildCellState
  {
    CellState BuildCellState(JobDB jobDB, EventLogDB logDB, NiigataStatus status, List<JobPlan> unarchivedJobs);
  }

  public class CreateCellState : IBuildCellState
  {
    private FMSSettings _settings;
    private static Serilog.ILogger Log = Serilog.Log.ForContext<CreateCellState>();
    private NiigataStationNames _stationNames;
    private ICncMachineConnection _machConnection;

    public CreateCellState(FMSSettings s, NiigataStationNames n, ICncMachineConnection machConn)
    {
      _settings = s;
      _stationNames = n;
      _machConnection = machConn;
    }

    public CellState BuildCellState(JobDB jobDB, EventLogDB logDB, NiigataStatus status, List<JobPlan> unarchivedJobs)
    {
      var palletStateUpdated = false;

      var jobCache = Memoize<string, JobPlan>(jobDB.LoadJob);
      var pals = status.Pallets
        .Select(p => BuildCurrentPallet(status.Machines, jobCache, p, logDB, jobDB))
        .OrderBy(p =>
        {
          // sort pallets by loadBegin so that the assignment of material from queues to pallets is consistent
          var loadBegin = p.Log.FirstOrDefault(e =>
            e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.StartOfCycle
          );
          return loadBegin?.EndTimeUTC ?? DateTime.MaxValue;
        })
        .ToList();

      // first, go through loaded pallets because might need to allocate material from queue
      var currentlyLoading = new HashSet<long>();
      foreach (var pal in pals.Where(p => !p.IsLoading && p.Status.HasWork))
      {
        LoadedPallet(pal, status.TimeOfStatusUTC, currentlyLoading, jobDB, logDB, ref palletStateUpdated);
      }

      // next, go through pallets currently being loaded
      foreach (var pal in pals.Where(p => p.IsLoading && p.Status.HasWork))
      {
        CurrentlyLoadingPallet(pal, status.TimeOfStatusUTC, currentlyLoading, jobDB, logDB, ref palletStateUpdated);
      }

      foreach (var pal in pals.Where(p => !p.Status.HasWork))
      {
        // need to check if an unload with no load happened and if so record unload end
        LoadedPallet(pal, status.TimeOfStatusUTC, currentlyLoading, jobDB, logDB, ref palletStateUpdated);
      }

      pals.Sort((p1, p2) => p1.Status.Master.PalletNum.CompareTo(p2.Status.Master.PalletNum));

      // must calculate QueuedMaterial before loading programs, because QueuedMaterial might unarchive a job.
      var queuedMats = QueuedMaterial(new HashSet<long>(
          pals.SelectMany(p => p.Material).Select(m => m.Mat.MaterialID)
        ), logDB, unarchivedJobs, jobCache, jobDB);
      var progsInUse = FindProgramNums(jobDB, unarchivedJobs, status);

      return new CellState()
      {
        Status = status,
        UnarchivedJobs = unarchivedJobs,
        PalletStateUpdated = palletStateUpdated,
        Pallets = pals,
        QueuedMaterial = queuedMats,
        JobQtyRemainingOnProc1 = CountRemainingQuantity(jobDB, logDB, unarchivedJobs, pals),
        ProgramsInUse = progsInUse,
        OldUnusedPrograms = OldUnusedPrograms(jobDB, status, progsInUse)
      };
    }

    private PalletAndMaterial BuildCurrentPallet(IReadOnlyDictionary<int, MachineStatus> machines, Func<string, JobPlan> jobCache, PalletStatus pallet, EventLogDB logDB, JobDB jobDB)
    {
      MachineStatus machStatus = null;
      if (pallet.CurStation.Location.Location == PalletLocationEnum.Machine)
      {
        var iccMc = pallet.CurStation.Location.Num;
        if (_stationNames != null)
        {
          iccMc = _stationNames.JobMachToIcc(pallet.CurStation.Location.StationGroup, pallet.CurStation.Location.Num);
        }
        machines.TryGetValue(iccMc, out machStatus);
      }

      var currentOrLoadingFaces =
        RecordFacesForPallet
        .Load(pallet.Master.Comment, logDB)
        .Select(m =>
        {
          var job = jobCache(m.Unique);
          return new PalletFace()
          {
            Job = job,
            Process = m.Proc,
            Path = m.Path,
            Face = m.Face,
            FaceIsMissingMaterial = false,
            Programs = m.ProgOverride ?? job.GetMachiningStop(m.Proc, m.Path).Select((stop, stopIdx) => new ProgramsForProcess()
            {
              StopIndex = stopIdx,
              ProgramName = stop.ProgramName,
              Revision = stop.ProgramRevision
            })
          };
        })
        .ToList();

      var mats = new List<InProcessMaterialAndJob>();

      var log = logDB.CurrentPalletLog(pallet.Master.PalletNum.ToString());
      foreach (var m in log
        .Where(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && !e.StartOfCycle)
        .SelectMany(e => e.Material))
      {
        var details = logDB.GetMaterialDetails(m.MaterialID);
        var inProcMat =
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
              logDB.LookupInspectionDecisions(m.MaterialID)
              .Where(x => x.Inspect)
              .Select(x => x.InspType)
              .ToList(),
            LastCompletedMachiningRouteStopIndex = null,
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
          };
        var job = jobCache(m.JobUniqueStr);
        if (job != null)
        {
          var stops = job.GetMachiningStop(inProcMat.Process, inProcMat.Path).ToList();
          var lastCompletedIccIdx = pallet.Tracking.CurrentStepNum - 1;
          if (
              pallet.Tracking.BeforeCurrentStep == false
              && pallet.Tracking.Alarm == false
              && (
                  machStatus == null
                  ||
                  (machStatus.FMSLinkMode && machStatus.Alarm == false)
                 )
            )
          {
            lastCompletedIccIdx += 1;
          }
          var completedMachineSteps =
            pallet.Master.Routes
            .Take(lastCompletedIccIdx)
            .Where(r => r is MachiningStep || r is ReclampStep)
            .Count();

          if (completedMachineSteps > 0)
          {
            inProcMat.LastCompletedMachiningRouteStopIndex = completedMachineSteps - 1;
          }
        }

        mats.Add(new InProcessMaterialAndJob()
        {
          Mat = inProcMat,
          Job = job,
          WorkorderPrograms = string.IsNullOrEmpty(inProcMat.WorkorderId) ? null : jobDB.WorkordersById(inProcMat.WorkorderId).Where(w => w.Part == inProcMat.PartName).FirstOrDefault()?.Programs
        });
      }

      bool manualControl = (pallet.Master.Comment ?? "").ToLower().Contains("manual");

      return new PalletAndMaterial()
      {
        Status = pallet,
        CurrentOrLoadingFaces = currentOrLoadingFaces,
        Material = mats,
        Log = log,
        IsLoading = !manualControl &&
                    (
                      (pallet.CurrentStep is LoadStep && pallet.Tracking.BeforeCurrentStep)
                      ||
                      (pallet.CurrentStep is UnloadStep && pallet.Tracking.BeforeCurrentStep)
                    ),
        ManualControl = manualControl,
        MachineStatus = machStatus,
      };
    }

    private void SetMaterialToLoadOnFace(
          PalletAndMaterial pallet,
          bool allocateNew,
          bool actionIsLoading,
          DateTime nowUtc,
          HashSet<long> currentlyLoading,
          Dictionary<long, InProcessMaterialAndJob> unusedMatsOnPal,
          EventLogDB logDB,
          JobDB jobDB
    )
    {
      foreach (var face in pallet.CurrentOrLoadingFaces)
      {
        var inputQueue = face.Job.GetInputQueue(face.Process, face.Path);
        int loadedMatCnt = 0;

        if (face.Process == 1 && string.IsNullOrEmpty(inputQueue))
        {
          // castings
          for (int i = 1; i <= face.Job.PartsPerPallet(face.Process, face.Path); i++)
          {
            long mid;
            string serial = null;
            if (allocateNew)
            {
              mid = logDB.AllocateMaterialID(face.Job.UniqueStr, face.Job.PartName, face.Job.NumProcesses);
              if (_settings.SerialType == SerialType.AssignOneSerialPerMaterial)
              {
                serial = _settings.ConvertMaterialIDToSerial(mid);
                logDB.RecordSerialForMaterialID(
                  new EventLogDB.EventLogMaterial() { MaterialID = mid, Process = face.Process, Face = "" },
                  serial,
                  nowUtc
                  );
              }
            }
            else
            {
              mid = -1;
            }
            pallet.Material.Add(new InProcessMaterialAndJob()
            {
              Job = face.Job,
              Mat = new InProcessMaterial()
              {
                MaterialID = mid,
                JobUnique = face.Job.UniqueStr,
                PartName = face.Job.PartName,
                Process = actionIsLoading ? 0 : face.Process,
                Path = actionIsLoading ? 1 : face.Path,
                Serial = serial,
                Location = actionIsLoading ?
                  new InProcessMaterialLocation()
                  {
                    Type = InProcessMaterialLocation.LocType.Free,
                  }
                  : new InProcessMaterialLocation()
                  {
                    Type = InProcessMaterialLocation.LocType.OnPallet,
                    Pallet = pallet.Status.Master.PalletNum.ToString(),
                    Face = face.Face
                  },
                Action = actionIsLoading ?
                  new InProcessMaterialAction()
                  {
                    Type = InProcessMaterialAction.ActionType.Loading,
                    LoadOntoPallet = pallet.Status.Master.PalletNum.ToString(),
                    ProcessAfterLoad = face.Process,
                    PathAfterLoad = face.Path,
                    LoadOntoFace = face.Face,
                  }
                :
                 new InProcessMaterialAction()
                 {
                   Type = InProcessMaterialAction.ActionType.Waiting
                 }
              }
            });
            loadedMatCnt += 1;
          }
        }
        else if (face.Process == 1)
        {
          // load castings from queue
          var castings =
            logDB.GetMaterialInQueue(inputQueue)
            .Where(m => !currentlyLoading.Contains(m.MaterialID))
            .Select(m =>
            {
              var details = logDB.GetMaterialDetails(m.MaterialID);
              return new QueuedMaterialWithDetails()
              {
                MaterialID = m.MaterialID,
                Unique = m.Unique,
                Serial = details?.Serial,
                Workorder = details?.Workorder,
                PartNameOrCasting = m.PartNameOrCasting,
                NextProcess = logDB.NextProcessForQueuedMaterial(m.MaterialID) ?? 1,
                QueuePosition = m.Position,
                Paths = details?.Paths,
                WorkorderPrograms = string.IsNullOrEmpty(details?.Workorder) ? null : jobDB.WorkordersById(details?.Workorder).Where(w => w.Part == m.PartNameOrCasting).FirstOrDefault()?.Programs
              };
            })
            .Where(m => FilterMaterialAvailableToLoadOntoFace(m, face))
            .ToList();

          foreach (var mat in castings.Take(face.Job.PartsPerPallet(face.Process, face.Path)))
          {
            if (allocateNew)
            {
              logDB.SetDetailsForMaterialID(mat.MaterialID, face.Job.UniqueStr, face.Job.PartName, face.Job.NumProcesses);
            }
            pallet.Material.Add(new InProcessMaterialAndJob()
            {
              Job = face.Job,
              Mat = new InProcessMaterial()
              {
                MaterialID = mat.MaterialID,
                JobUnique = face.Job.UniqueStr,
                PartName = face.Job.PartName,
                Serial = mat.Serial,
                WorkorderId = mat.Workorder,
                Process = actionIsLoading ? 0 : face.Process,
                Path = actionIsLoading ? (mat.Paths != null && mat.Paths.TryGetValue(1, out var path) ? path : 1) : face.Path,
                Location = actionIsLoading ?
                  new InProcessMaterialLocation()
                  {
                    Type = InProcessMaterialLocation.LocType.InQueue,
                    CurrentQueue = inputQueue,
                    QueuePosition = mat.QueuePosition,
                  }
                  : new InProcessMaterialLocation()
                  {
                    Type = InProcessMaterialLocation.LocType.OnPallet,
                    Pallet = pallet.Status.Master.PalletNum.ToString(),
                    Face = face.Face
                  },
                Action = actionIsLoading ?
                  new InProcessMaterialAction()
                  {
                    Type = InProcessMaterialAction.ActionType.Loading,
                    LoadOntoPallet = pallet.Status.Master.PalletNum.ToString(),
                    ProcessAfterLoad = face.Process,
                    PathAfterLoad = face.Path,
                    LoadOntoFace = face.Face,
                  }
                :
                 new InProcessMaterialAction()
                 {
                   Type = InProcessMaterialAction.ActionType.Waiting
                 }
              },
              WorkorderPrograms = mat.WorkorderPrograms
            });
            currentlyLoading.Add(mat.MaterialID);
            loadedMatCnt += 1;
          }


        }
        else
        {
          // first check mat on pal
          foreach (var mat in unusedMatsOnPal.Values.ToList().Select(m => m.Mat))
          {
            if (mat.JobUnique == face.Job.UniqueStr
              && mat.Process + 1 == face.Process
              && face.Job.GetPathGroup(mat.Process, mat.Path) == face.Job.GetPathGroup(face.Process, face.Path)
              && !currentlyLoading.Contains(mat.MaterialID)
            )
            {
              pallet.Material.Add(new InProcessMaterialAndJob()
              {
                Job = face.Job,
                Mat = new InProcessMaterial()
                {
                  MaterialID = mat.MaterialID,
                  JobUnique = face.Job.UniqueStr,
                  PartName = face.Job.PartName,
                  Serial = mat.Serial,
                  WorkorderId = mat.WorkorderId,
                  SignaledInspections = mat.SignaledInspections,
                  Process = actionIsLoading ? mat.Process : face.Process,
                  Path = actionIsLoading ? mat.Path : face.Path,
                  Location = actionIsLoading ?
                    new InProcessMaterialLocation()
                    {
                      Type = InProcessMaterialLocation.LocType.OnPallet,
                      Pallet = pallet.Status.Master.PalletNum.ToString(),
                      Face = mat.Location.Face
                    }
                    : new InProcessMaterialLocation()
                    {
                      Type = InProcessMaterialLocation.LocType.OnPallet,
                      Pallet = pallet.Status.Master.PalletNum.ToString(),
                      Face = face.Face
                    },
                  Action = actionIsLoading ?
                    new InProcessMaterialAction()
                    {
                      Type = InProcessMaterialAction.ActionType.Loading,
                      LoadOntoPallet = pallet.Status.Master.PalletNum.ToString(),
                      ProcessAfterLoad = face.Process,
                      PathAfterLoad = face.Path,
                      LoadOntoFace = face.Face,
                    }
                    :
                    new InProcessMaterialAction()
                    {
                      Type = InProcessMaterialAction.ActionType.Waiting
                    }
                },
                WorkorderPrograms = string.IsNullOrEmpty(mat.WorkorderId) ? null : jobDB.WorkordersById(mat.WorkorderId).Where(w => w.Part == face.Job.PartName).FirstOrDefault()?.Programs
              });
              unusedMatsOnPal.Remove(mat.MaterialID);
              currentlyLoading.Add(mat.MaterialID);
              loadedMatCnt += 1;

              if (loadedMatCnt >= face.Job.PartsPerPallet(face.Process, face.Path))
              {
                break;
              }
            }
          }

          // now check queue
          if (!string.IsNullOrEmpty(inputQueue))
          {
            var availableMaterial =
              logDB.GetMaterialInQueue(inputQueue)
              .Where(m => !currentlyLoading.Contains(m.MaterialID))
              .Select(m =>
              {
                var details = logDB.GetMaterialDetails(m.MaterialID);
                return new QueuedMaterialWithDetails()
                {
                  MaterialID = m.MaterialID,
                  Unique = m.Unique,
                  Serial = details?.Serial,
                  Workorder = details?.Workorder,
                  QueuePosition = m.Position,
                  PartNameOrCasting = m.PartNameOrCasting,
                  NextProcess = logDB.NextProcessForQueuedMaterial(m.MaterialID) ?? 1,
                  Paths = details?.Paths,
                  WorkorderPrograms = string.IsNullOrEmpty(details?.Workorder) ? null : jobDB.WorkordersById(details?.Workorder).Where(w => w.Part == m.PartNameOrCasting).FirstOrDefault()?.Programs
                };
              })
              .Where(m => FilterMaterialAvailableToLoadOntoFace(m, face))
              .ToList();
            foreach (var mat in availableMaterial)
            {
              pallet.Material.Add(new InProcessMaterialAndJob()
              {
                Job = face.Job,
                Mat = new InProcessMaterial()
                {
                  MaterialID = mat.MaterialID,
                  JobUnique = face.Job.UniqueStr,
                  PartName = face.Job.PartName,
                  Serial = mat.Serial,
                  WorkorderId = mat.Workorder,
                  SignaledInspections =
                      logDB.LookupInspectionDecisions(mat.MaterialID)
                      .Where(x => x.Inspect)
                      .Select(x => x.InspType)
                      .Distinct()
                      .ToList(),
                  Process = actionIsLoading ? face.Process - 1 : face.Process,
                  Path = actionIsLoading ? (mat.Paths != null && mat.Paths.TryGetValue(Math.Max(face.Process - 1, 1), out var path) ? path : 1) : face.Path,
                  Location = actionIsLoading ?
                    new InProcessMaterialLocation()
                    {
                      Type = InProcessMaterialLocation.LocType.InQueue,
                      CurrentQueue = inputQueue,
                      QueuePosition = mat.QueuePosition
                    }
                    : new InProcessMaterialLocation()
                    {
                      Type = InProcessMaterialLocation.LocType.OnPallet,
                      Pallet = pallet.Status.Master.PalletNum.ToString(),
                      Face = face.Face
                    },
                  Action = actionIsLoading ?
                    new InProcessMaterialAction()
                    {
                      Type = InProcessMaterialAction.ActionType.Loading,
                      LoadOntoPallet = pallet.Status.Master.PalletNum.ToString(),
                      ProcessAfterLoad = face.Process,
                      PathAfterLoad = face.Path,
                      LoadOntoFace = face.Face,
                    }
                    :
                    new InProcessMaterialAction()
                    {
                      Type = InProcessMaterialAction.ActionType.Waiting
                    }
                },
                WorkorderPrograms = mat.WorkorderPrograms
              });

              currentlyLoading.Add(mat.MaterialID);
              loadedMatCnt += 1;

              if (loadedMatCnt >= face.Job.PartsPerPallet(face.Process, face.Path))
              {
                break;
              }
            }
          }
        }

        // if not enough, give error and allocate more
        if (loadedMatCnt < face.Job.PartsPerPallet(face.Process, face.Path))
        {
          Log.Debug("Unable to find enough in-process parts for {@pallet} and {@face} with {@currentlyLoading}", pallet, face, currentlyLoading);
          if (!allocateNew)
          {
            face.FaceIsMissingMaterial = true;
          }
          else
          {
            Log.Error("Unable to find enough in-process parts for {@job}-{@proc} on pallet {@pallet}", face.Job.UniqueStr, face.Process, pallet.Status.Master.PalletNum);
            for (int i = loadedMatCnt; i < face.Job.PartsPerPallet(face.Process, face.Path); i++)
            {
              string serial = null;
              long mid = logDB.AllocateMaterialID(face.Job.UniqueStr, face.Job.PartName, face.Job.NumProcesses);
              if (_settings.SerialType == SerialType.AssignOneSerialPerMaterial)
              {
                serial = _settings.ConvertMaterialIDToSerial(mid);
                logDB.RecordSerialForMaterialID(
                  new EventLogDB.EventLogMaterial() { MaterialID = mid, Process = face.Process, Face = "" },
                  serial,
                  nowUtc
                  );
              }
              pallet.Material.Add(new InProcessMaterialAndJob()
              {
                Job = face.Job,
                Mat = new InProcessMaterial()
                {
                  MaterialID = mid,
                  JobUnique = face.Job.UniqueStr,
                  PartName = face.Job.PartName,
                  Serial = serial,
                  Process = actionIsLoading ? face.Process - 1 : face.Process,
                  Path = actionIsLoading ? 1 : face.Path,
                  Location = actionIsLoading ?
                      new InProcessMaterialLocation()
                      {
                        Type = InProcessMaterialLocation.LocType.Free,
                      }
                      : new InProcessMaterialLocation()
                      {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = pallet.Status.Master.PalletNum.ToString(),
                        Face = face.Face
                      },
                  Action = actionIsLoading ?
                    new InProcessMaterialAction()
                    {
                      Type = InProcessMaterialAction.ActionType.Loading,
                      LoadOntoPallet = pallet.Status.Master.PalletNum.ToString(),
                      ProcessAfterLoad = face.Process,
                      PathAfterLoad = face.Path,
                      LoadOntoFace = face.Face,
                    }
                    :
                    new InProcessMaterialAction()
                    {
                      Type = InProcessMaterialAction.ActionType.Waiting
                    }
                },
              });
            }
          }
        }
      }
    }

    private void LoadedPallet(PalletAndMaterial pallet, DateTime nowUtc, HashSet<long> currentlyLoading, JobDB jobDB, EventLogDB logDB, ref bool palletStateUpdated)
    {
      if (pallet.ManualControl)
      {
        QuarantineMatOnPal(pallet, "PalletToManualControl", logDB, ref palletStateUpdated, nowUtc);
      }
      else
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
          AddPalletCycle(pallet, loadBegin, currentlyLoading, nowUtc, logDB, jobDB);
        }

        if (pallet.Status.HasWork)
        {
          EnsureAllNonloadStopsHaveEvents(pallet, nowUtc, jobDB, logDB, ref palletStateUpdated);
        }

        EnsurePalletRotaryEvents(pallet, nowUtc, logDB);
        EnsurePalletStockerEvents(pallet, nowUtc, logDB);

        if (pallet.Status.HasWork == false && pallet.Material.Any())
        {
          QuarantineMatOnPal(pallet, "MaterialMissingOnPallet", logDB, ref palletStateUpdated, nowUtc);
        }

      }
    }

    private void QuarantineMatOnPal(PalletAndMaterial pallet, string reason, EventLogDB logDB, ref bool palletStateUpdated, DateTime nowUtc)
    {
      // move all material to quarantine queue
      if (!string.IsNullOrEmpty(_settings.QuarantineQueue))
      {
        foreach (var m in pallet.Material)
        {
          logDB.RecordAddMaterialToQueue(
            mat: new EventLogDB.EventLogMaterial() { MaterialID = m.Mat.MaterialID, Process = m.Mat.Process, Face = "" },
            queue: _settings.QuarantineQueue,
            position: -1,
            timeUTC: nowUtc,
            operatorName: null,
            reason: reason
          );
        }
      }

      // record pallet cycle so any material is removed from pallet
      if (pallet.Material.Count > 0)
      {
        Log.Debug("Removing material {@mats} from pallet {pal} because it was switched to manual control", pallet.Material, pallet.Status.Master.PalletNum);
        logDB.CompletePalletCycle(pallet.Status.Master.PalletNum.ToString(), nowUtc, foreignID: null);
        pallet.Material.Clear();
        palletStateUpdated = true;
      }
    }

    private void CurrentlyLoadingPallet(PalletAndMaterial pallet, DateTime nowUtc, HashSet<long> currentlyLoading, JobDB jobDB, EventLogDB logDB, ref bool palletStateUpdated)
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
          logDB.RecordLoadStart(
            mats: new EventLogDB.EventLogMaterial[] { },
            pallet: pallet.Status.Master.PalletNum.ToString(),
            lulNum: pallet.Status.CurStation.Location.Num,
            timeUTC: nowUtc
          );
        }
      }

      EnsureAllNonloadStopsHaveEvents(pallet, nowUtc, jobDB, logDB, ref palletStateUpdated);
      EnsurePalletStockerEvents(pallet, nowUtc, logDB);

      // clear the material and recalculte what will be on the pallet after the load ends
      var unusedMatsOnPal = pallet.Material.ToDictionary(m => m.Mat.MaterialID, m => m);
      pallet.Material.Clear();

      // check for things to load
      var loadingIds = new HashSet<long>();
      if (pallet.Status.CurrentStep is LoadStep
          ||
          (pallet.Status.CurrentStep is UnloadStep && pallet.Status.Master.RemainingPalletCycles > 1)
         )
      {
        SetMaterialToLoadOnFace(pallet, allocateNew: false, actionIsLoading: true, nowUtc: nowUtc, currentlyLoading: currentlyLoading, unusedMatsOnPal: unusedMatsOnPal, logDB: logDB, jobDB: jobDB);
        foreach (var mat in pallet.Material)
        {
          mat.Mat.Action.ElapsedLoadUnloadTime = elapsedLoadTime;
          loadingIds.Add(mat.Mat.MaterialID);
        }
      }

      // now material to unload
      foreach (var mat in unusedMatsOnPal.Values.Where(m => !loadingIds.Contains(m.Mat.MaterialID)))
      {
        if (mat.Mat.Process == mat.Job.NumProcesses)
        {
          mat.Mat.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
            ElapsedLoadUnloadTime = elapsedLoadTime
          };
        }
        else
        {
          mat.Mat.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = OutputQueueForMaterial(mat, pallet.Log),
            ElapsedLoadUnloadTime = elapsedLoadTime
          };
        }
        pallet.Material.Add(mat);
      }
    }

    private void AddPalletCycle(PalletAndMaterial pallet, LogEntry loadBegin, HashSet<long> currentlyLoading, DateTime nowUtc, EventLogDB logDB, JobDB jobDB)
    {
      // record unload-end
      foreach (var face in pallet.Material.GroupBy(m => m.Mat.Location.Face))
      {
        // everything on a face shares the job, proc, and path
        var job = face.First().Job;
        var proc = face.First().Mat.Process;
        var path = face.First().Mat.Path;
        var queues = new Dictionary<long, string>();
        foreach (var mat in face)
        {
          var q = OutputQueueForMaterial(mat, pallet.Log);
          if (!string.IsNullOrEmpty(q))
          {
            queues[mat.Mat.MaterialID] = q;
          }
        }

        logDB.RecordUnloadEnd(
          mats: face.Select(m => new EventLogDB.EventLogMaterial() { MaterialID = m.Mat.MaterialID, Process = proc, Face = face.Key.ToString() }),
          pallet: pallet.Status.Master.PalletNum.ToString(),
          lulNum: loadBegin.LocationNum,
          timeUTC: nowUtc,
          elapsed: nowUtc.Subtract(loadBegin.EndTimeUTC),
          active: TimeSpan.FromTicks(job.GetExpectedUnloadTime(proc, path).Ticks * face.Count()),
          unloadIntoQueues: queues
        );
      }

      // complete the pallet cycle so new cycle starts with the below Load end
      logDB.CompletePalletCycle(pallet.Status.Master.PalletNum.ToString(), nowUtc, foreignID: null);

      var unusedMatsOnPal = pallet.Material.ToDictionary(m => m.Mat.MaterialID, m => m);
      pallet.Material.Clear();

      // add load-end for material put onto
      var newLoadEvents = new List<LogEntry>();

      if (pallet.Status.HasWork)
      {
        // add 1 seconds to now so the marking of serials and load end happens after the pallet cycle
        SetMaterialToLoadOnFace(
          pallet,
          allocateNew: true,
          actionIsLoading: false,
          nowUtc: nowUtc.AddSeconds(1),
          currentlyLoading: currentlyLoading,
          unusedMatsOnPal: unusedMatsOnPal,
          logDB: logDB,
          jobDB: jobDB);

        foreach (var face in pallet.Material.GroupBy(p => p.Mat.Location.Face))
        {
          var job = face.First().Job;
          var proc = face.First().Mat.Process;
          var path = face.First().Mat.Path;
          newLoadEvents.AddRange(logDB.RecordLoadEnd(
            mats: face.Select(m => new EventLogDB.EventLogMaterial() { MaterialID = m.Mat.MaterialID, Process = m.Mat.Process, Face = face.Key.ToString() }),
            pallet: pallet.Status.Master.PalletNum.ToString(),
            lulNum: loadBegin.LocationNum,
            timeUTC: nowUtc.AddSeconds(1),
            elapsed: nowUtc.Subtract(loadBegin.EndTimeUTC),
            active: TimeSpan.FromTicks(job.GetExpectedLoadTime(proc, path).Ticks * face.Count())
          ));
        }

        foreach (var mat in pallet.Material)
        {
          logDB.RecordPathForProcess(mat.Mat.MaterialID, mat.Mat.Process, mat.Mat.Path);
        }
      }

      pallet.Log = newLoadEvents;
    }

    private void EnsureAllNonloadStopsHaveEvents(PalletAndMaterial pallet, DateTime nowUtc, JobDB jobDB, EventLogDB logDB, ref bool palletStateUpdated)
    {
      var unusedLogs = pallet.Log.ToList();

      foreach (var face in pallet.CurrentOrLoadingFaces)
      {
        var matOnFace = pallet.Material.Where(
          m => m.Mat.JobUnique == face.Job.UniqueStr && m.Mat.Process == face.Process && m.Mat.Path == face.Path && m.Mat.Location.Face == face.Face
        )
        .Select(m => m.Mat)
        .ToList();
        var matIdsOnFace = new HashSet<long>(matOnFace.Select(m => m.MaterialID));

        foreach (var ss in MatchStopsAndSteps(jobDB, pallet, face))
        {
          switch (ss.IccStep)
          {
            case MachiningStep step:
              // find log events if they exist
              var machStart = unusedLogs
                .Where(e => e.LogType == LogType.MachineCycle && e.StartOfCycle && e.Program == ss.JobStop.ProgramName && e.Material.Any(m => matIdsOnFace.Contains(m.MaterialID)))
                .FirstOrDefault()
                ;
              var machEnd = unusedLogs
                .Where(e => e.LogType == LogType.MachineCycle && !e.StartOfCycle && e.Program == ss.JobStop.ProgramName && e.Material.Any(m => matIdsOnFace.Contains(m.MaterialID)))
                .FirstOrDefault()
                ;
              if (machStart != null) unusedLogs.Remove(machStart);
              if (machEnd != null) unusedLogs.Remove(machEnd);

              if (ss.StopCompleted)
              {
                if (machEnd == null)
                {
                  RecordMachineEnd(pallet, face, matOnFace, ss, machStart, nowUtc, logDB, ref palletStateUpdated);
                }
              }
              else
              {
                if (pallet.Status.CurStation.Location.Location == PalletLocationEnum.Machine &&
                    ss.JobStop.StationGroup == pallet.Status.CurStation.Location.StationGroup &&
                    ss.JobStop.Stations.Contains(pallet.Status.CurStation.Location.Num)
                   )
                {
                  RecordPalletAtMachine(pallet, face, matOnFace, ss, machStart, machEnd, nowUtc, jobDB, logDB, ref palletStateUpdated);
                }
              }
              break;

            case ReclampStep step:
              // find log events if they exist
              var reclampStart = unusedLogs
                .Where(e => e.LogType == LogType.LoadUnloadCycle && e.StartOfCycle && e.Result == ss.JobStop.StationGroup && e.Material.Any(m => matIdsOnFace.Contains(m.MaterialID)))
                .FirstOrDefault()
                ;
              var reclampEnd = unusedLogs
                .Where(e => e.LogType == LogType.LoadUnloadCycle && !e.StartOfCycle && e.Result == ss.JobStop.StationGroup && e.Material.Any(m => matIdsOnFace.Contains(m.MaterialID)))
                .FirstOrDefault()
                ;
              if (reclampStart != null) unusedLogs.Remove(reclampStart);
              if (reclampEnd != null) unusedLogs.Remove(reclampEnd);

              if (ss.StopCompleted)
              {
                if (reclampEnd == null)
                {
                  RecordReclampEnd(pallet, face, matOnFace, ss, reclampStart, nowUtc, logDB, ref palletStateUpdated);
                }
              }
              else
              {
                if (pallet.Status.CurStation.Location.Location == PalletLocationEnum.LoadUnload && step.Reclamp.Contains(pallet.Status.CurStation.Location.Num))
                {
                  MarkReclampInProgress(pallet, face, matOnFace, ss, reclampStart, nowUtc, logDB, ref palletStateUpdated);
                }
              }
              break;

          }
        }
      }

      if (pallet.Status.CurrentStep is UnloadStep && pallet.Status.Tracking.BeforeCurrentStep)
      {
        MakeInspectionDecisions(pallet, nowUtc, logDB, ref palletStateUpdated);
      }
    }

    public class StopAndStep
    {
      public JobMachiningStop JobStop { get; set; }
      public int IccStepNum { get; set; } // 1-indexed
      public RouteStep IccStep { get; set; }
      public bool StopCompleted { get; set; }
    }

    private IReadOnlyList<StopAndStep> MatchStopsAndSteps(JobDB jobDB, PalletAndMaterial pallet, PalletFace face)
    {
      var ret = new List<StopAndStep>();

      int stepIdx = 1; // 0-indexed, first step is load so skip it

      foreach (var stop in face.Job.GetMachiningStop(face.Process, face.Path))
      {
        // find out if job stop is machine or reclamp and which program
        string iccMachineProg = null;
        bool isReclamp = false;
        if (_stationNames != null && _stationNames.ReclampGroupNames.Contains(stop.StationGroup))
        {
          isReclamp = true;
        }
        else
        {
          if (stop.ProgramRevision.HasValue)
          {
            iccMachineProg = jobDB.LoadProgram(stop.ProgramName, stop.ProgramRevision.Value)?.CellControllerProgramName;
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
                  && stop.Stations.Any(s =>
                  {
                    var iccMcNum = s;
                    if (_stationNames != null)
                    {
                      iccMcNum = _stationNames.JobMachToIcc(stop.StationGroup, s);
                    }
                    return machStep.Machines.Contains(iccMcNum);
                  })
                  && machStep.ProgramNumsToRun.Select(p => p.ToString()).Contains(iccMachineProg)
                 )
              {
                goto matchingStep;
              }
              break;

            case ReclampStep reclampStep:
              if (isReclamp && stop.Stations.Any(s => reclampStep.Reclamp.Contains(s)))
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

        if (stepIdx + 1 > pallet.Status.Tracking.CurrentStepNum)
        {
          break;
        }

        // add the matching stop and step
        ret.Add(new StopAndStep()
        {
          JobStop = stop,
          IccStepNum = stepIdx + 1,
          IccStep = step,
          StopCompleted =
            (stepIdx + 1 < pallet.Status.Tracking.CurrentStepNum)
            ||
            (stepIdx + 1 == pallet.Status.Tracking.CurrentStepNum
              && pallet.Status.Tracking.BeforeCurrentStep == false
              && pallet.Status.Tracking.Alarm == false
              && (
                  pallet.MachineStatus == null
                  ||
                  (pallet.MachineStatus.FMSLinkMode && pallet.MachineStatus.Alarm == false)
                 )
            )
        });

        stepIdx += 1;

        if (stepIdx + 1 > pallet.Status.Tracking.CurrentStepNum)
        {
          break;
        }
      }
      return ret;
    }

    private void RecordPalletAtMachine(PalletAndMaterial pallet, PalletFace face, IEnumerable<InProcessMaterial> matOnFace, StopAndStep ss, LogEntry machineStart, LogEntry machineEnd, DateTime nowUtc, JobDB jobDB, EventLogDB logDB, ref bool palletStateUpdated)
    {
      bool runningProgram = false;
      bool runningAnyProgram = false;
      if (pallet.MachineStatus != null && pallet.MachineStatus.CurrentlyExecutingProgram > 0)
      {
        runningAnyProgram = true;
        if (ss.JobStop.ProgramRevision.HasValue)
        {
          runningProgram =
            pallet.MachineStatus.CurrentlyExecutingProgram.ToString()
            ==
            jobDB.LoadProgram(ss.JobStop.ProgramName, ss.JobStop.ProgramRevision.Value).CellControllerProgramName;
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

        var tools = _machConnection.ToolsForMachine(
          _stationNames.JobMachToIcc(pallet.Status.CurStation.Location.StationGroup, pallet.Status.CurStation.Location.Num)
        );

        logDB.RecordMachineStart(
          mats: matOnFace.Select(m => new EventLogDB.EventLogMaterial()
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
          },
          pockets: tools?.Select(t => t.ToEventDBToolSnapshot())
        );

        foreach (var m in matOnFace)
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
      else if (runningProgram || (!runningAnyProgram && machineStart != null && machineEnd == null))
      {
        // update material to be running, since either program is running or paused with nothing running
        var elapsed = machineStart == null ? TimeSpan.Zero : nowUtc.Subtract(machineStart.EndTimeUTC);
        foreach (var m in matOnFace)
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
      else if (!runningProgram && machineStart != null && machineEnd == null)
      {
        // a different program started, record machine end
        RecordMachineEnd(pallet, face, matOnFace, ss, machineStart, nowUtc, logDB, ref palletStateUpdated);
      }
    }

    private void RecordMachineEnd(PalletAndMaterial pallet, PalletFace face, IEnumerable<InProcessMaterial> matOnFace, StopAndStep ss, LogEntry machStart, DateTime nowUtc, EventLogDB logDB, ref bool palletStateUpdated)
    {
      Log.Debug("Recording machine end for {@pallet} and face {@face} for stop {@ss} with logs {@machStart}", pallet, face, ss, machStart);
      palletStateUpdated = true;

      string statName;
      int statNum;
      IEnumerable<EventLogDB.ToolPocketSnapshot> toolsAtStart;
      if (machStart != null)
      {
        statName = machStart.LocationName;
        statNum = machStart.LocationNum;
        toolsAtStart = logDB.ToolPocketSnapshotForCycle(machStart.Counter) ?? Enumerable.Empty<EventLogDB.ToolPocketSnapshot>();
      }
      else if (ss.IccStepNum <= pallet.Status.Tracking.ExecutedStationNumber.Count)
      {
        statName = ss.JobStop.StationGroup;
        var stat = pallet.Status.Tracking.ExecutedStationNumber[ss.IccStepNum - 1];
        if (stat != null)
        {
          if (stat.Location.Location != PalletLocationEnum.Machine || stat.Location.StationGroup != ss.JobStop.StationGroup)
          {
            Log.Warning("Mismatch between executed station number and job step for {@pal}", pallet);
          }
          statNum = stat.Location.Num;
        }
        else
        {
          statNum = 0;
        }
        toolsAtStart = null;
      }
      else
      {
        statName = ss.JobStop.StationGroup;
        statNum = 0;
        toolsAtStart = null;
      }

      IEnumerable<NiigataToolData> toolsAtEnd = null;
      if (toolsAtStart != null)
      {
        toolsAtEnd = _machConnection.ToolsForMachine(_stationNames.JobMachToIcc(statName, statNum));
      }

      logDB.RecordMachineEnd(
        mats: matOnFace.Select(m => new EventLogDB.EventLogMaterial()
        {
          MaterialID = m.MaterialID,
          Process = m.Process,
          Face = face.Face.ToString()
        }),
        pallet: pallet.Status.Master.PalletNum.ToString(),
        statName: statName,
        statNum: statNum,
        program: ss.JobStop.ProgramName,
        result: "",
        timeUTC: nowUtc,
        elapsed: machStart != null ? nowUtc.Subtract(machStart.EndTimeUTC) : TimeSpan.Zero, // TODO: lookup start from SQL?
        active: ss.JobStop.ExpectedCycleTime,
        extraData: !ss.JobStop.ProgramRevision.HasValue ? null : new Dictionary<string, string> {
                {"ProgramRevision", ss.JobStop.ProgramRevision.Value.ToString()}
        },
        tools: toolsAtStart == null || toolsAtEnd == null ? null :
          EventLogDB.ToolPocketSnapshot.DiffSnapshots(toolsAtStart, toolsAtEnd.Select(t => t.ToEventDBToolSnapshot()))
      );
    }

    private void MarkReclampInProgress(PalletAndMaterial pallet, PalletFace face, IEnumerable<InProcessMaterial> matOnFace, StopAndStep ss, LogEntry reclampStart, DateTime nowUtc, EventLogDB logDB, ref bool palletStateUpdated)
    {
      if (reclampStart == null)
      {
        Log.Debug("Recording reclamp start for {@pallet} and face {@face} for stop {@ss}", pallet, face, ss);
        palletStateUpdated = true;
        logDB.RecordManualWorkAtLULStart(
          mats: matOnFace.Select(m => new EventLogDB.EventLogMaterial()
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

        foreach (var m in matOnFace)
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
        foreach (var m in matOnFace)
        {
          m.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            ElapsedLoadUnloadTime = nowUtc.Subtract(reclampStart.EndTimeUTC)
          };
        }
      }
    }

    private void RecordReclampEnd(PalletAndMaterial pallet, PalletFace face, IEnumerable<InProcessMaterial> matOnFace, StopAndStep ss, LogEntry reclampStart, DateTime nowUtc, EventLogDB logDB, ref bool palletStateUpdated)
    {
      Log.Debug("Recording reclamp end for {@pallet} and face {@face} for stop {@ss} with logs {@machStart}", pallet, face, ss, reclampStart);
      palletStateUpdated = true;

      int statNum;
      if (reclampStart != null)
      {
        statNum = reclampStart.LocationNum;
      }
      else if (ss.IccStepNum <= pallet.Status.Tracking.ExecutedStationNumber.Count)
      {
        var niigataStat = pallet.Status.Tracking.ExecutedStationNumber[ss.IccStepNum - 1];
        if (niigataStat != null)
        {
          if (niigataStat.Location.Location != PalletLocationEnum.LoadUnload)
          {
            Log.Warning("Mismatch between executed station number and job reclamp step for {@pal}", pallet);
          }
          statNum = niigataStat.StatNum;
        }
        else
        {
          statNum = 0;
        }
      }
      else
      {
        statNum = 0;
      }

      logDB.RecordManualWorkAtLULEnd(
        mats: matOnFace.Select(m => new EventLogDB.EventLogMaterial()
        {
          MaterialID = m.MaterialID,
          Process = m.Process,
          Face = face.Face.ToString()
        }),
        pallet: pallet.Status.Master.PalletNum.ToString(),
        lulNum: statNum,
        operationName: ss.JobStop.StationGroup,
        timeUTC: nowUtc,
        elapsed: reclampStart != null ? nowUtc.Subtract(reclampStart.EndTimeUTC) : TimeSpan.Zero, // TODO: lookup start from SQL?
        active: ss.JobStop.ExpectedCycleTime
      );
    }


    private void MakeInspectionDecisions(PalletAndMaterial pallet, DateTime timeUTC, EventLogDB logDB, ref bool palletStateUpdated)
    {
      foreach (var mat in pallet.Material)
      {
        var inspections = mat.Job.PathInspections(mat.Mat.Process, mat.Mat.Path);
        if (inspections.Count > 0)
        {
          var decisions = logDB.MakeInspectionDecisions(mat.Mat.MaterialID, mat.Mat.Process, inspections, timeUTC);
          if (decisions.Any())
          {
            foreach (var log in decisions.Where(e => e.Result.ToLower() == "true"))
            {
              if (log.ProgramDetails != null && log.ProgramDetails.TryGetValue("InspectionType", out var iType))
              {
                mat.Mat.SignaledInspections.Add(iType);
              }
            }
            palletStateUpdated = true;
          }
        }
      }
    }

    private void EnsurePalletRotaryEvents(PalletAndMaterial pallet, DateTime nowUtc, EventLogDB logDB)
    {
      LogEntry start = null;
      foreach (var e in pallet.Log)
      {
        if (e.LogType == LogType.PalletOnRotaryInbound)
        {
          if (e.StartOfCycle)
          {
            start = e;
          }
          else
          {
            start = null;
          }
        }
      }

      bool currentlyOnRotary =
          pallet.Status.CurStation.Location.Location == PalletLocationEnum.MachineQueue
        && pallet.Status.CurStation.IsOutboundMachineQueue == false
        && pallet.Status.CurrentStep is MachiningStep
        && pallet.Status.Tracking.BeforeCurrentStep;

      if (start == null && currentlyOnRotary && !pallet.Status.Tracking.Alarm)
      {
        logDB.RecordPalletArriveRotaryInbound(
          mats: pallet.Material.Select(m => new EventLogDB.EventLogMaterial()
          {
            MaterialID = m.Mat.MaterialID,
            Process = m.Mat.Process,
            Face = m.Mat.Location.Face.HasValue ? m.Mat.Location.Face.Value.ToString() : "",
          }),
          pallet: pallet.Status.Master.PalletNum.ToString(),
          statName: pallet.Status.CurStation.Location.StationGroup,
          statNum: pallet.Status.CurStation.Location.Num,
          timeUTC: nowUtc
        );
      }
      else if (start != null && !currentlyOnRotary)
      {
        logDB.RecordPalletDepartRotaryInbound(
          mats: pallet.Material.Select(m => new EventLogDB.EventLogMaterial()
          {
            MaterialID = m.Mat.MaterialID,
            Process = m.Mat.Process,
            Face = m.Mat.Location.Face.HasValue ? m.Mat.Location.Face.Value.ToString() : "",
          }),
          pallet: pallet.Status.Master.PalletNum.ToString(),
          statName: start.LocationName,
          statNum: start.LocationNum,
          timeUTC: nowUtc,
          elapsed: nowUtc.Subtract(start.EndTimeUTC),
          rotateIntoWorktable: pallet.Status.CurStation.Location.Location == PalletLocationEnum.Machine
        );
      }
    }

    private void EnsurePalletStockerEvents(PalletAndMaterial pallet, DateTime nowUtc, EventLogDB logDB)
    {
      if (!pallet.Material.Any()) return;

      LogEntry start = null;
      foreach (var e in pallet.Log)
      {
        if (e.LogType == LogType.PalletInStocker)
        {
          if (e.StartOfCycle)
          {
            start = e;
          }
          else
          {
            start = null;
          }
        }
      }

      bool currentlyAtStocker = pallet.Status.CurStation.Location.Location == PalletLocationEnum.Buffer;
      bool waitForMachine = false;
      if (pallet.Status.CurrentStep is MachiningStep && pallet.Status.Tracking.BeforeCurrentStep)
      {
        waitForMachine = true;
      }
      for (int stepNum = pallet.Status.Tracking.CurrentStepNum + 1; stepNum <= pallet.Status.Master.Routes.Count; stepNum += 1)
      {
        if (pallet.Status.Master.Routes[stepNum - 1] is MachiningStep)
        {
          waitForMachine = true;
        }
      }

      if (start == null && currentlyAtStocker)
      {
        logDB.RecordPalletArriveStocker(
          mats: pallet.Material.Select(m => new EventLogDB.EventLogMaterial()
          {
            MaterialID = m.Mat.MaterialID,
            Process = m.Mat.Process,
            Face = m.Mat.Location.Face.HasValue ? m.Mat.Location.Face.Value.ToString() : "",
          }),
          pallet: pallet.Status.Master.PalletNum.ToString(),
          stockerNum: pallet.Status.CurStation.Location.Num,
          timeUTC: nowUtc,
          waitForMachine: waitForMachine
        );
      }
      else if (start != null && !currentlyAtStocker)
      {
        logDB.RecordPalletDepartStocker(
          mats: pallet.Material.Select(m => new EventLogDB.EventLogMaterial()
          {
            MaterialID = m.Mat.MaterialID,
            Process = m.Mat.Process,
            Face = m.Mat.Location.Face.HasValue ? m.Mat.Location.Face.Value.ToString() : "",
          }),
          pallet: pallet.Status.Master.PalletNum.ToString(),
          stockerNum: start.LocationNum,
          timeUTC: nowUtc,
          elapsed: nowUtc.Subtract(start.EndTimeUTC),
          waitForMachine: waitForMachine
        );
      }
    }

    #region Material Computations
    private List<InProcessMaterialAndJob> QueuedMaterial(HashSet<long> matsOnPallets, EventLogDB logDB, List<JobPlan> unarchivedJobs, Func<string, JobPlan> loadJob, JobDB jobDB)
    {
      var mats = new List<InProcessMaterialAndJob>();
      var jobUniqs = new HashSet<string>(unarchivedJobs.Select(j => j.UniqueStr));

      foreach (var mat in logDB.GetMaterialInAllQueues())
      {
        if (matsOnPallets.Contains(mat.MaterialID)) continue;


        var nextProc = logDB.NextProcessForQueuedMaterial(mat.MaterialID);
        var lastProc = (nextProc ?? 1) - 1;

        var matDetails = logDB.GetMaterialDetails(mat.MaterialID);

        JobPlan job = null;
        if (!string.IsNullOrEmpty(matDetails.JobUnique) && !jobUniqs.Contains(matDetails.JobUnique))
        {
          job = loadJob(matDetails.JobUnique);
          if (job.Archived)
          {
            jobDB.UnarchiveJob(matDetails.JobUnique);
          }
          jobUniqs.Add(matDetails.JobUnique);
          unarchivedJobs.Add(job);
        }

        mats.Add(new InProcessMaterialAndJob()
        {
          Job = job,
          Mat = new InProcessMaterial()
          {
            MaterialID = mat.MaterialID,
            JobUnique = mat.Unique,
            PartName = mat.PartNameOrCasting,
            Process = lastProc,
            Path = matDetails?.Paths != null && matDetails.Paths.TryGetValue(Math.Max(1, lastProc), out var path) ? path : 1,
            Serial = matDetails?.Serial,
            WorkorderId = matDetails?.Workorder,
            SignaledInspections =
                logDB.LookupInspectionDecisions(mat.MaterialID)
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
          },
          WorkorderPrograms = string.IsNullOrEmpty(matDetails?.Workorder) ? null : jobDB.WorkordersById(matDetails?.Workorder).Where(w => w.Part == mat.PartNameOrCasting).FirstOrDefault()?.Programs
        });
      }

      return mats;
    }

    private Dictionary<(string uniq, int proc1path), int> CountRemainingQuantity(JobDB jobDB, EventLogDB logDB, IEnumerable<JobPlan> unarchivedJobs, IEnumerable<PalletAndMaterial> pals)
    {
      var cnts = new Dictionary<(string uniq, int proc1path), int>();
      foreach (var job in unarchivedJobs)
      {
        if (jobDB.LoadDecrementsForJob(job.UniqueStr).Count == 0)
        {
          var loadedCnt =
            logDB.GetLogForJobUnique(job.UniqueStr)
              .Where(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD")
              .SelectMany(e => e.Material)
              .Where(m => m.JobUniqueStr == job.UniqueStr)
              .Select(m => m.MaterialID)
              .Distinct()
              .Select(m =>
              {
                var details = logDB.GetMaterialDetails(m);
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
              .SelectMany(p => p.Material)
              .Select(m => m.Mat)
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

    public class QueuedMaterialWithDetails
    {
      public long MaterialID { get; set; }
      public string Unique { get; set; }
      public string Serial { get; set; }
      public string Workorder { get; set; }
      public string PartNameOrCasting { get; set; }
      public int QueuePosition { get; set; }
      public Dictionary<int, int> Paths { get; set; }
      public int NextProcess { get; set; }
      public IEnumerable<WorkorderProgram> WorkorderPrograms { get; set; }
    }

    public static bool FilterMaterialAvailableToLoadOntoFace(QueuedMaterialWithDetails mat, PalletFace face)
    {
      if (face.Process == 1 && mat.NextProcess == 1 && string.IsNullOrEmpty(mat.Unique))
      {
        // check for casting on process 1
        var casting = face.Job.GetCasting(face.Path);
        if (string.IsNullOrEmpty(casting)) casting = face.Job.PartName;

        if (mat.PartNameOrCasting != casting) return false;

        // check workorders
        if (mat.WorkorderPrograms == null)
        {
          // material will just inherit programs on pallet
          return true;
        }
        else if (face.Programs == null)
        {
          return CheckProgramsMatchJobSteps(face.Job.GetMachiningStop(face.Process, face.Path), FilterProgramsToProcess(1, mat.WorkorderPrograms));
        }
        else
        {
          return CheckProgramsMatch(face.Programs, FilterProgramsToProcess(1, mat.WorkorderPrograms));
        }
      }
      else
      {
        // check unique, process, and path group match
        if (mat.Unique != face.Job.UniqueStr) return false;
        if (mat.NextProcess != face.Process) return false;

        // now path group
        if (mat.Paths != null && mat.Paths.Count > 0)
        {
          var path = mat.Paths.Aggregate((max, v) => max.Key > v.Key ? max : v);
          var group = face.Job.GetPathGroup(process: Math.Max(1, path.Key), path: path.Value);
          if (group != face.Job.GetPathGroup(face.Process, face.Path))
          {
            return false;
          }
        }
        else
        {
          Log.Warning("Material {matId} has no path groups! {@mat}", mat);
        }

        // finally, check workorders
        if (face.Programs == null && mat.WorkorderPrograms == null)
        {
          return true;
        }
        else if (face.Programs == null && mat.WorkorderPrograms != null)
        {
          return CheckProgramsMatchJobSteps(face.Job.GetMachiningStop(face.Process, face.Path), FilterProgramsToProcess(face.Process, mat.WorkorderPrograms));
        }
        else if (face.Programs != null && mat.WorkorderPrograms == null)
        {
          var progsForMat = face.Job.GetMachiningStop(face.Process, face.Path).Select((stop, stopIdx) =>
            new ProgramsForProcess()
            {
              StopIndex = stopIdx,
              ProgramName = stop.ProgramName,
              Revision = stop.ProgramRevision
            }
          );
          return CheckProgramsMatch(face.Programs, progsForMat);
        }
        else
        {
          return CheckProgramsMatch(face.Programs, FilterProgramsToProcess(face.Process, mat.WorkorderPrograms));
        }
      }
    }

    private static bool CheckProgramsMatch(IEnumerable<ProgramsForProcess> ps1, IEnumerable<ProgramsForProcess> ps2)
    {
      // can use Enumerable.SequenceEqual when ProgramsForProcess is converted to a record
      var byStop = ps1.GroupBy(p => p.StopIndex).ToDictionary(p => p.Key, p => p.First());

      foreach (var p2 in ps2)
      {
        if (byStop.TryGetValue(p2.StopIndex, out var p1))
        {
          if (p1.ProgramName != p2.ProgramName || p1.Revision != p2.Revision)
          {
            return false;
          }
          byStop.Remove(p2.StopIndex);
        }
        else
        {
          return false;
        }
      }

      return byStop.Count == 0;
    }

    private static bool CheckProgramsMatchJobSteps(IEnumerable<JobMachiningStop> stops, IEnumerable<ProgramsForProcess> ps)
    {
      var byStop = ps.GroupBy(p => p.StopIndex).ToDictionary(p => p.Key, p => p.First());

      int stopIdx = -1;
      foreach (var stop in stops)
      {
        stopIdx += 1;

        if (byStop.TryGetValue(stopIdx, out var p))
        {
          byStop.Remove(stopIdx);
        }
        else
        {
          return false;
        }
      }

      return byStop.Count == 0;
    }

    private static IEnumerable<ProgramsForProcess> FilterProgramsToProcess(int proc, IEnumerable<WorkorderProgram> works)
    {
      return works.Where(p => p.ProcessNumber == proc).Select(p => new ProgramsForProcess()
      {
        StopIndex = p.StopIndex ?? 0,
        ProgramName = p.ProgramName,
        Revision = p.Revision
      });
    }

    private string OutputQueueForMaterial(InProcessMaterialAndJob mat, IReadOnlyList<LogEntry> log)
    {
      var signalQuarantine = log.FirstOrDefault(e => e.LogType == LogType.SignalQuarantine && e.Material.Any(m => m.MaterialID == mat.Mat.MaterialID));

      if (signalQuarantine != null)
      {
        return signalQuarantine.LocationName ?? _settings.QuarantineQueue;
      }
      else
      {
        return mat.Job.GetOutputQueue(mat.Mat.Process, mat.Mat.Path);
      }
    }

    private Dictionary<(string progName, long revision), ProgramRevision> FindProgramNums(JobDB jobDB, IEnumerable<JobPlan> unarchivedJobs, NiigataStatus status)
    {
      var stops =
        unarchivedJobs
          .SelectMany(j => Enumerable.Range(1, j.NumProcesses).SelectMany(proc =>
            Enumerable.Range(1, j.GetNumPaths(proc)).SelectMany(path =>
              j.GetMachiningStop(proc, path)
            )
          ));

      var progs = new Dictionary<(string progName, long revision), ProgramRevision>();
      foreach (var stop in stops)
      {
        if (stop.ProgramRevision.HasValue)
        {
          var prog = jobDB.LoadProgram(stop.ProgramName, stop.ProgramRevision.Value);
          progs[(stop.ProgramName, stop.ProgramRevision.Value)] = prog;
          if (!string.IsNullOrEmpty(prog.CellControllerProgramName) && int.TryParse(prog.CellControllerProgramName, out var progNum))
          {
            // operator might have deleted the program
            if (!status.Programs.ContainsKey(progNum) || !AssignNewRoutesOnPallets.IsInsightProgram(status.Programs[progNum]))
            {
              // clear it, so that the Assignment adds the program back as a new number
              jobDB.SetCellControllerProgramForProgram(prog.ProgramName, prog.Revision, null);
              prog.CellControllerProgramName = null;
            }
          }
        }
      }
      return progs;
    }

    private List<ProgramRevision> OldUnusedPrograms(
      JobDB jobDB,
      NiigataStatus status,
      Dictionary<(string progName, long revision), ProgramRevision> usedProgs
    )
    {
      // the icc program numbers currently used by schedules
      var usedIccProgs = new HashSet<string>(
        usedProgs.Values.Select(p => p.CellControllerProgramName)
        .Concat(
          status.Pallets
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
        status.Programs
          .Select(p => AssignNewRoutesOnPallets.TryParseProgramComment(p.Value, out string pName, out long rev) ? new { pName, rev } : null)
          .Where(p => p != null)
          .ToLookup(p => p.pName, p => p.rev)
          .ToDictionary(ps => ps.Key, ps => ps.Max());

      var progsToDelete = new Dictionary<string, ProgramRevision>();

      // ICC has a bug where it occasionally (about 1 out of 20 time) only partially deletes the program.  It is gone from
      // the status_program PostgreSQL table but still exists.  So first calculate the programs to delete from our own
      // database.

      foreach (var prog in jobDB.LoadProgramsInCellController())
      {
        if (string.IsNullOrEmpty(prog.CellControllerProgramName)) continue;
        if (usedIccProgs.Contains(prog.CellControllerProgramName)) continue;
        if (maxRevForProg.ContainsKey(prog.ProgramName) && prog.Revision >= maxRevForProg[prog.ProgramName]) continue;
        progsToDelete.Add(prog.CellControllerProgramName, prog);
      }

      // next, check programs in the ICC itself (perhaps the databases were deleted)
      foreach (var prog in status.Programs)
      {
        if (!AssignNewRoutesOnPallets.IsInsightProgram(prog.Value)) continue;
        if (usedIccProgs.Contains(prog.Key.ToString())) continue;
        if (progsToDelete.ContainsKey(prog.Key.ToString())) continue;
        if (!AssignNewRoutesOnPallets.TryParseProgramComment(prog.Value, out string pName, out long rev)) continue;
        if (rev >= maxRevForProg[pName]) continue;

        progsToDelete.Add(prog.Key.ToString(), new ProgramRevision()
        {
          ProgramName = pName,
          Revision = rev,
          Comment = "",
          CellControllerProgramName = prog.Key.ToString(),
        });
      }

      return progsToDelete.Values.ToList();
    }

    private static Func<A, B> Memoize<A, B>(Func<A, B> f)
    {
      var cache = new Dictionary<A, B>();
      return a =>
      {
        if (cache.TryGetValue(a, out var b))
        {
          return b;
        }
        else
        {
          b = f(a);
          cache.Add(a, b);
          return b;
        }
      };
    }
    #endregion
  }
}