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
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Niigata
{
  public record PalletFace
  {
    public Job Job { get; init; }
    public int Process { get; init; }
    public int Path { get; init; }
    public ProcPathInfo PathInfo { get; init; }
    public int Face { get; init; }
    public bool FaceIsMissingMaterial { get; set; }
    public ImmutableList<ProgramsForProcess> Programs { get; init; }
  }

  public record InProcessMaterialAndJob
  {
    public InProcessMaterial Mat { get; set; }
    public Job Job { get; init; }
    public ImmutableList<Workorder> Workorders { get; init; }
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

  public class CellState : ICellState
  {
    public NiigataStatus Status { get; set; }
    public bool StateUpdated { get; set; }
    public required TimeSpan TimeUntilNextRefresh { get; init; }
    public List<PalletAndMaterial> Pallets { get; set; }
    public ImmutableList<InProcessMaterialAndJob> QueuedMaterial { get; set; }
    public Dictionary<(string progName, long revision), ProgramRevision> ProgramsInUse { get; set; }
    public List<ProgramRevision> OldUnusedPrograms { get; set; }
    public CurrentStatus CurrentStatus { get; init; }
  }

  public class CreateCellState
  {
    private readonly FMSSettings _settings;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<CreateCellState>();
    private readonly NiigataSettings _niigataSettings;
    private readonly ICncMachineConnection _machConnection;

    public static CellState BuildCellState(
      FMSSettings s,
      NiigataSettings nSt,
      ICncMachineConnection machConn,
      IRepository logDB,
      NiigataStatus status
    )
    {
      return new CreateCellState(s, nSt, machConn).Build(logDB, status);
    }

    private CreateCellState(FMSSettings s, NiigataSettings nSt, ICncMachineConnection machConn)
    {
      _settings = s;
      _niigataSettings = nSt;
      _machConnection = machConn;
    }

    private CellState Build(IRepository logDB, NiigataStatus status)
    {
      var palletStateUpdated = false;

      var jobCache = _niigataSettings.CreateJobCache(logDB);
      var pals = status
        .Pallets.Select(p => BuildCurrentPallet(status.Machines, jobCache, p, logDB))
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
        LoadedPallet(pal, status.TimeOfStatusUTC, currentlyLoading, logDB, ref palletStateUpdated);
      }

      // next, go through pallets currently being loaded
      foreach (var pal in pals.Where(p => p.IsLoading && p.Status.HasWork))
      {
        CurrentlyLoadingPallet(pal, status.TimeOfStatusUTC, currentlyLoading, logDB, ref palletStateUpdated);
      }

      foreach (var pal in pals.Where(p => !p.Status.HasWork))
      {
        // need to check if an unload with no load happened and if so record unload end
        LoadedPallet(pal, status.TimeOfStatusUTC, currentlyLoading, logDB, ref palletStateUpdated);
      }

      pals.Sort((p1, p2) => p1.Status.Master.PalletNum.CompareTo(p2.Status.Master.PalletNum));

      // must calculate QueuedMaterial before loading programs, because QueuedMaterial might unarchive a job.
      var queuedMats = QueuedMaterial(
        new HashSet<long>(pals.SelectMany(p => p.Material).Select(m => m.Mat.MaterialID)),
        logDB,
        jobCache
      );

      var progsInUse = FindProgramNums(
        logDB,
        jobCache.AllJobs,
        pals.SelectMany(p => p.Material).Concat(queuedMats),
        status
      );

      return new CellState()
      {
        Status = status,
        TimeUntilNextRefresh = TimeSpan.FromMinutes(5),
        StateUpdated = palletStateUpdated,
        Pallets = pals,
        QueuedMaterial = queuedMats,
        ProgramsInUse = progsInUse,
        OldUnusedPrograms = OldUnusedPrograms(logDB, status, progsInUse),
        CurrentStatus = new CurrentStatus()
        {
          TimeOfCurrentStatusUTC = status.TimeOfStatusUTC,
          Jobs = jobCache.BuildActiveJobs(
            allMaterial: pals.SelectMany(p => p.Material).Concat(queuedMats).Select(m => m.Mat),
            db: logDB,
            archiveCompletedBefore: status.TimeOfStatusUTC.AddHours(-12)
          ),
          Pallets = pals.ToImmutableDictionary(
            pal => pal.Status.Master.PalletNum,
            pal => new MachineFramework.PalletStatus()
            {
              PalletNum = pal.Status.Master.PalletNum,
              FixtureOnPallet = "",
              OnHold = pal.Status.Master.Skip,
              CurrentPalletLocation = pal.Status.CurStation.Location,
              NumFaces = pal.Material.Count > 0 ? pal.Material.Max(m => m.Mat.Location.Face ?? 1) : 0,
            }
          ),
          Material = pals.SelectMany(pal => pal.Material)
            .Concat(queuedMats)
            .Select(m => m.Mat)
            .Concat(SetLongTool(pals))
            .ToImmutableList(),
          Queues = BlackMaple.MachineFramework.BuildCellState.CalcQueueRoles(jobCache.AllJobs, _settings),
          Alarms = pals.Where(pal => pal.Status.Tracking.Alarm)
            .Select(pal => AlarmCodeToString(pal.Status.Master.PalletNum, pal.Status.Tracking.AlarmCode))
            .Concat(
              status
                .Machines.Values.Where(mc => mc.Alarm)
                .Select(mc => "Machine " + mc.MachineNumber.ToString() + " has an alarm")
            )
            .Concat(status.Alarm ? new[] { "ICC has an alarm" } : new string[] { })
            .ToImmutableList(),
          Workorders = logDB.GetActiveWorkorders(
            additionalWorkorders: pals.SelectMany(pal => pal.Material)
              .Concat(queuedMats)
              .Select(m => m.Mat.WorkorderId)
              .Where(w => !string.IsNullOrEmpty(w))
              .ToHashSet()
          ),
        },
      };
    }

    private PalletAndMaterial BuildCurrentPallet(
      IReadOnlyDictionary<int, MachineStatus> machines,
      IJobCache jobCache,
      PalletStatus pallet,
      IRepository logDB
    )
    {
      MachineStatus machStatus = null;
      if (pallet.CurStation.Location.Location == PalletLocationEnum.Machine)
      {
        var iccMc = pallet.CurStation.Location.Num;
        if (_niigataSettings.StationNames != null)
        {
          iccMc = _niigataSettings.StationNames.JobMachToIcc(
            pallet.CurStation.Location.StationGroup,
            pallet.CurStation.Location.Num
          );
        }
        machines.TryGetValue(iccMc, out machStatus);
      }

      var currentOrLoadingFaces = RecordFacesForPallet
        .Load(pallet.Master.Comment, logDB)
        .Select(m =>
        {
          var job = jobCache.Lookup(m.Unique);
          var pathInfo = job.Processes[m.Proc - 1].Paths[m.Path - 1];
          return new PalletFace()
          {
            Job = job,
            Process = m.Proc,
            Path = m.Path,
            PathInfo = pathInfo,
            Face = m.Face,
            FaceIsMissingMaterial = false,
            Programs =
              m.ProgOverride?.ToImmutableList()
              ?? JobProgramsFromStops(pathInfo.Stops, _niigataSettings.StationNames).ToImmutableList(),
          };
        })
        .ToList();

      var mats = new List<InProcessMaterialAndJob>();

      var log = logDB.CurrentPalletLog(pallet.Master.PalletNum);
      foreach (
        var m in log.Where(e => e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && !e.StartOfCycle)
          .SelectMany(e => e.Material)
      )
      {
        var inProcMat = new InProcessMaterial()
        {
          MaterialID = m.MaterialID,
          JobUnique = m.JobUniqueStr,
          PartName = m.PartName,
          Process = m.Process,
          Path = m.Path ?? 1,
          Serial = m.Serial == "" ? null : m.Serial,
          WorkorderId = m.Workorder == "" ? null : m.Workorder,
          SignaledInspections = logDB
            .LookupInspectionDecisions(m.MaterialID)
            .Where(x => x.Inspect)
            .Select(x => x.InspType)
            .ToImmutableList(),
          QuarantineAfterUnload = log.Any(e =>
            e.LogType == LogType.SignalQuarantine && e.Material.Any(m => m.MaterialID == m.MaterialID)
          )
            ? true
            : null,
          LastCompletedMachiningRouteStopIndex = null,
          Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.OnPallet,
            PalletNum = pallet.Master.PalletNum,
            Face = m.Face,
          },
          Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
        };
        var job = jobCache.Lookup(m.JobUniqueStr);
        if (job != null)
        {
          var stops = job.Processes[inProcMat.Process - 1].Paths[inProcMat.Path - 1].Stops.ToList();
          var lastCompletedIccIdx = pallet.Tracking.CurrentStepNum - 1;
          if (
            pallet.Tracking.BeforeCurrentStep == false
            && pallet.Tracking.Alarm == false
            && (machStatus == null || (machStatus.FMSLinkMode && machStatus.Alarm == false))
          )
          {
            lastCompletedIccIdx += 1;
          }
          var completedMachineSteps = Math.Min(
            pallet
              .Master.Routes.Take(lastCompletedIccIdx)
              .Where(r => r is MachiningStep || r is ReclampStep)
              .Count(),
            // Should never be hit, but if the user edits the pallet and forgets to set "Manual" in the comment field,
            // it can be higher than the configured steps.  Add a bound just in case.
            stops.Count
          );

          if (completedMachineSteps > 0)
          {
            inProcMat = inProcMat with { LastCompletedMachiningRouteStopIndex = completedMachineSteps - 1 };
          }
        }

        mats.Add(
          new InProcessMaterialAndJob()
          {
            Mat = inProcMat,
            Job = job,
            Workorders = string.IsNullOrEmpty(inProcMat.WorkorderId)
              ? null
              : logDB.WorkordersById(inProcMat.WorkorderId).ToImmutableList(),
          }
        );
      }

      bool manualControl = (pallet.Master.Comment ?? "").ToLower().Contains("manual");

      return new PalletAndMaterial()
      {
        Status = pallet,
        CurrentOrLoadingFaces = currentOrLoadingFaces,
        Material = mats,
        Log = log,
        IsLoading =
          !manualControl
          && (
            (pallet.CurrentStep is LoadStep && pallet.Tracking.BeforeCurrentStep)
            || (pallet.CurrentStep is UnloadStep && pallet.Tracking.BeforeCurrentStep)
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
      IRepository logDB
    )
    {
      foreach (var face in pallet.CurrentOrLoadingFaces)
      {
        var inputQueue = face.PathInfo.InputQueue;
        int loadedMatCnt = 0;

        if (face.Process == 1 && string.IsNullOrEmpty(inputQueue))
        {
          // castings
          for (int i = 1; i <= face.PathInfo.PartsPerPallet; i++)
          {
            long mid;
            string serial = null;
            if (allocateNew)
            {
              mid = logDB.AllocateMaterialIDAndGenerateSerial(
                unique: face.Job.UniqueStr,
                part: face.Job.PartName,
                numProc: face.Job.Processes.Count,
                timeUTC: nowUtc,
                out var serialLogEntry
              );
              serial = serialLogEntry?.Result;
            }
            else
            {
              mid = -1;
            }
            pallet.Material.Add(
              new InProcessMaterialAndJob()
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
                  SignaledInspections = ImmutableList<string>.Empty,
                  QuarantineAfterUnload = null,
                  Location = actionIsLoading
                    ? new InProcessMaterialLocation() { Type = InProcessMaterialLocation.LocType.Free }
                    : new InProcessMaterialLocation()
                    {
                      Type = InProcessMaterialLocation.LocType.OnPallet,
                      PalletNum = pallet.Status.Master.PalletNum,
                      Face = face.Face,
                    },
                  Action = actionIsLoading
                    ? new InProcessMaterialAction()
                    {
                      Type = InProcessMaterialAction.ActionType.Loading,
                      LoadOntoPalletNum = pallet.Status.Master.PalletNum,
                      ProcessAfterLoad = face.Process,
                      PathAfterLoad = face.Path,
                      LoadOntoFace = face.Face,
                    }
                    : new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
                },
              }
            );
            loadedMatCnt += 1;
          }
        }
        else if (face.Process == 1)
        {
          // load castings from queue
          var castings = logDB
            .GetUnallocatedMaterialInQueue(
              inputQueue,
              string.IsNullOrEmpty(face.PathInfo.Casting) ? face.Job.PartName : face.PathInfo.Casting
            )
            .Concat(logDB.GetMaterialInQueueByUnique(inputQueue, face.Job.UniqueStr))
            .Where(m => !currentlyLoading.Contains(m.MaterialID))
            .OrderBy(m => m.Position)
            .Select(m =>
            {
              return new QueuedMaterialWithDetails()
              {
                MaterialID = m.MaterialID,
                Unique = m.Unique,
                Serial = m.Serial,
                Workorder = m.Workorder,
                PartNameOrCasting = m.PartNameOrCasting,
                NextProcess = m.NextProcess ?? 1,
                QueuePosition = m.Position,
                Paths = m.Paths?.ToDictionary(k => k.Key, k => k.Value),
                Workorders = string.IsNullOrEmpty(m.Workorder)
                  ? null
                  : logDB.WorkordersById(m.Workorder).ToImmutableList(),
              };
            })
            .Where(m => FilterMaterialAvailableToLoadOntoFace(m, face, _niigataSettings.StationNames))
            .ToList();

          foreach (var mat in castings.Take(face.PathInfo.PartsPerPallet))
          {
            if (allocateNew)
            {
              logDB.SetDetailsForMaterialID(
                mat.MaterialID,
                face.Job.UniqueStr,
                face.Job.PartName,
                face.Job.Processes.Count
              );
            }
            pallet.Material.Add(
              new InProcessMaterialAndJob()
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
                  SignaledInspections = ImmutableList<string>.Empty,
                  QuarantineAfterUnload = null,
                  Path = actionIsLoading
                    ? (mat.Paths != null && mat.Paths.TryGetValue(1, out var path) ? path : 1)
                    : face.Path,
                  Location = actionIsLoading
                    ? new InProcessMaterialLocation()
                    {
                      Type = InProcessMaterialLocation.LocType.InQueue,
                      CurrentQueue = inputQueue,
                      QueuePosition = mat.QueuePosition,
                    }
                    : new InProcessMaterialLocation()
                    {
                      Type = InProcessMaterialLocation.LocType.OnPallet,
                      PalletNum = pallet.Status.Master.PalletNum,
                      Face = face.Face,
                    },
                  Action = actionIsLoading
                    ? new InProcessMaterialAction()
                    {
                      Type = InProcessMaterialAction.ActionType.Loading,
                      LoadOntoPalletNum = pallet.Status.Master.PalletNum,
                      ProcessAfterLoad = face.Process,
                      PathAfterLoad = face.Path,
                      LoadOntoFace = face.Face,
                    }
                    : new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
                },
                Workorders = mat.Workorders,
              }
            );
            currentlyLoading.Add(mat.MaterialID);
            loadedMatCnt += 1;
          }
        }
        else
        {
          // first check mat on pal
          foreach (var mat in unusedMatsOnPal.Values.ToList().Select(m => m.Mat))
          {
            if (
              mat.JobUnique == face.Job.UniqueStr
              && mat.Process + 1 == face.Process
              && !currentlyLoading.Contains(mat.MaterialID)
            )
            {
              pallet.Material.Add(
                new InProcessMaterialAndJob()
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
                    QuarantineAfterUnload = null,
                    Process = actionIsLoading ? mat.Process : face.Process,
                    Path = actionIsLoading ? mat.Path : face.Path,
                    Location = actionIsLoading
                      ? new InProcessMaterialLocation()
                      {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        PalletNum = pallet.Status.Master.PalletNum,
                        Face = mat.Location.Face,
                      }
                      : new InProcessMaterialLocation()
                      {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        PalletNum = pallet.Status.Master.PalletNum,
                        Face = face.Face,
                      },
                    Action = actionIsLoading
                      ? new InProcessMaterialAction()
                      {
                        Type = InProcessMaterialAction.ActionType.Loading,
                        LoadOntoPalletNum = pallet.Status.Master.PalletNum,
                        ProcessAfterLoad = face.Process,
                        PathAfterLoad = face.Path,
                        LoadOntoFace = face.Face,
                      }
                      : new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
                  },
                  Workorders = string.IsNullOrEmpty(mat.WorkorderId)
                    ? null
                    : logDB.WorkordersById(mat.WorkorderId).ToImmutableList(),
                }
              );
              unusedMatsOnPal.Remove(mat.MaterialID);
              currentlyLoading.Add(mat.MaterialID);
              loadedMatCnt += 1;

              if (loadedMatCnt >= face.PathInfo.PartsPerPallet)
              {
                break;
              }
            }
          }

          // now check queue
          if (!string.IsNullOrEmpty(inputQueue))
          {
            var availableMaterial = logDB
              .GetMaterialInQueueByUnique(inputQueue, face.Job.UniqueStr)
              .Where(m => !currentlyLoading.Contains(m.MaterialID))
              .Select(m =>
              {
                return new QueuedMaterialWithDetails()
                {
                  MaterialID = m.MaterialID,
                  Unique = m.Unique,
                  Serial = m.Serial,
                  Workorder = m.Workorder,
                  QueuePosition = m.Position,
                  PartNameOrCasting = m.PartNameOrCasting,
                  NextProcess = m.NextProcess ?? 1,
                  Paths = m.Paths?.ToDictionary(k => k.Key, k => k.Value),
                  Workorders = string.IsNullOrEmpty(m.Workorder)
                    ? null
                    : logDB.WorkordersById(m.Workorder).ToImmutableList(),
                };
              })
              .Where(m => FilterMaterialAvailableToLoadOntoFace(m, face, _niigataSettings.StationNames))
              .ToList();
            var inspections = logDB.LookupInspectionDecisions(availableMaterial.Select(m => m.MaterialID));
            foreach (var mat in availableMaterial)
            {
              pallet.Material.Add(
                new InProcessMaterialAndJob()
                {
                  Job = face.Job,
                  Mat = new InProcessMaterial()
                  {
                    MaterialID = mat.MaterialID,
                    JobUnique = face.Job.UniqueStr,
                    PartName = face.Job.PartName,
                    Serial = mat.Serial,
                    WorkorderId = mat.Workorder,
                    SignaledInspections = inspections[mat.MaterialID]
                      .Where(x => x.Inspect)
                      .Select(x => x.InspType)
                      .Distinct()
                      .ToImmutableList(),
                    QuarantineAfterUnload = null,
                    Process = actionIsLoading ? face.Process - 1 : face.Process,
                    Path = actionIsLoading
                      ? (
                        mat.Paths != null
                        && mat.Paths.TryGetValue(Math.Max(face.Process - 1, 1), out var path)
                          ? path
                          : 1
                      )
                      : face.Path,
                    Location = actionIsLoading
                      ? new InProcessMaterialLocation()
                      {
                        Type = InProcessMaterialLocation.LocType.InQueue,
                        CurrentQueue = inputQueue,
                        QueuePosition = mat.QueuePosition,
                      }
                      : new InProcessMaterialLocation()
                      {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        PalletNum = pallet.Status.Master.PalletNum,
                        Face = face.Face,
                      },
                    Action = actionIsLoading
                      ? new InProcessMaterialAction()
                      {
                        Type = InProcessMaterialAction.ActionType.Loading,
                        LoadOntoPalletNum = pallet.Status.Master.PalletNum,
                        ProcessAfterLoad = face.Process,
                        PathAfterLoad = face.Path,
                        LoadOntoFace = face.Face,
                      }
                      : new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
                  },
                  Workorders = mat.Workorders,
                }
              );

              currentlyLoading.Add(mat.MaterialID);
              loadedMatCnt += 1;

              if (loadedMatCnt >= face.PathInfo.PartsPerPallet)
              {
                break;
              }
            }
          }
        }

        // if not enough, give error and allocate more
        if (loadedMatCnt < face.PathInfo.PartsPerPallet)
        {
          Log.Debug(
            "Unable to find enough in-process parts for {@pallet} and {@face} with {@currentlyLoading}",
            pallet,
            face,
            currentlyLoading
          );
          if (!allocateNew)
          {
            face.FaceIsMissingMaterial = true;
          }
          else
          {
            Log.Error(
              "Unable to find enough in-process parts for {@job}-{@proc} on pallet {@pallet}",
              face.Job.UniqueStr,
              face.Process,
              pallet.Status.Master.PalletNum
            );
            for (int i = loadedMatCnt; i < face.PathInfo.PartsPerPallet; i++)
            {
              string serial = null;
              long mid = logDB.AllocateMaterialIDAndGenerateSerial(
                unique: face.Job.UniqueStr,
                part: face.Job.PartName,
                numProc: face.Job.Processes.Count,
                timeUTC: nowUtc,
                out var serialLogEntry
              );
              serial = serialLogEntry?.Result;
              pallet.Material.Add(
                new InProcessMaterialAndJob()
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
                    SignaledInspections = ImmutableList<string>.Empty,
                    QuarantineAfterUnload = null,
                    Location = actionIsLoading
                      ? new InProcessMaterialLocation() { Type = InProcessMaterialLocation.LocType.Free }
                      : new InProcessMaterialLocation()
                      {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        PalletNum = pallet.Status.Master.PalletNum,
                        Face = face.Face,
                      },
                    Action = actionIsLoading
                      ? new InProcessMaterialAction()
                      {
                        Type = InProcessMaterialAction.ActionType.Loading,
                        LoadOntoPalletNum = pallet.Status.Master.PalletNum,
                        ProcessAfterLoad = face.Process,
                        PathAfterLoad = face.Path,
                        LoadOntoFace = face.Face,
                      }
                      : new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
                  },
                }
              );
            }
          }
        }
      }
    }

    private void LoadedPallet(
      PalletAndMaterial pallet,
      DateTime nowUtc,
      HashSet<long> currentlyLoading,
      IRepository jobDB,
      ref bool palletStateUpdated
    )
    {
      if (pallet.ManualControl)
      {
        QuarantineMatOnPal(pallet, "PalletToManualControl", jobDB, ref palletStateUpdated, nowUtc);
      }
      else
      {
        // first, check if this is the first time seeing pallet as loaded since a load began
        var loadBegin = pallet.Log.FirstOrDefault(e =>
          e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.StartOfCycle
        );
        if (loadBegin != null)
        {
          // first time seeing pallet after it loaded, record the pallet cycle and adjust the material on the faces
          palletStateUpdated = true;
          AddPalletCycle(pallet, loadBegin, currentlyLoading, nowUtc, jobDB);
        }

        if (pallet.Status.HasWork)
        {
          EnsureAllNonloadStopsHaveEvents(pallet, nowUtc, jobDB, ref palletStateUpdated);
        }

        EnsurePalletRotaryEvents(pallet, nowUtc, jobDB);
        EnsurePalletStockerEvents(pallet, nowUtc, jobDB);

        if (pallet.Status.HasWork == false && pallet.Material.Any())
        {
          QuarantineMatOnPal(pallet, "MaterialMissingOnPallet", jobDB, ref palletStateUpdated, nowUtc);
        }
      }
    }

    private void QuarantineMatOnPal(
      PalletAndMaterial pallet,
      string reason,
      IRepository logDB,
      ref bool palletStateUpdated,
      DateTime nowUtc
    )
    {
      // move all material to quarantine queue
      if (!string.IsNullOrEmpty(_settings.QuarantineQueue))
      {
        foreach (var m in pallet.Material)
        {
          logDB.RecordAddMaterialToQueue(
            mat: new EventLogMaterial()
            {
              MaterialID = m.Mat.MaterialID,
              Process = m.Mat.Process,
              Face = 0,
            },
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
        Log.Debug(
          "Removing material {@mats} from pallet {pal} because it was switched to manual control",
          pallet.Material,
          pallet.Status.Master.PalletNum
        );
        logDB.RecordEmptyPallet(pallet.Status.Master.PalletNum, nowUtc);
        pallet.Material.Clear();
        palletStateUpdated = true;
      }
    }

    private void CurrentlyLoadingPallet(
      PalletAndMaterial pallet,
      DateTime nowUtc,
      HashSet<long> currentlyLoading,
      IRepository jobDB,
      ref bool palletStateUpdated
    )
    {
      TimeSpan? elapsedLoadTime = null;
      if (pallet.Status.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
      {
        // ensure load-begin so that we know the starting time of load
        var seenLoadBegin = pallet.Log.FirstOrDefault(e =>
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
          jobDB.RecordLoadStart(
            mats: new EventLogMaterial[] { },
            pallet: pallet.Status.Master.PalletNum,
            lulNum: pallet.Status.CurStation.Location.Num,
            timeUTC: nowUtc
          );
        }
      }

      EnsureAllNonloadStopsHaveEvents(pallet, nowUtc, jobDB, ref palletStateUpdated);
      EnsurePalletStockerEvents(pallet, nowUtc, jobDB);

      // clear the material and recalculte what will be on the pallet after the load ends
      var unusedMatsOnPal = pallet.Material.ToDictionary(m => m.Mat.MaterialID, m => m);
      pallet.Material.Clear();

      // check for things to load
      var loadingIds = new HashSet<long>();
      if (
        pallet.Status.CurrentStep is LoadStep
        || (pallet.Status.CurrentStep is UnloadStep && pallet.Status.Master.RemainingPalletCycles > 1)
      )
      {
        SetMaterialToLoadOnFace(
          pallet,
          allocateNew: false,
          actionIsLoading: true,
          nowUtc: nowUtc,
          currentlyLoading: currentlyLoading,
          unusedMatsOnPal: unusedMatsOnPal,
          logDB: jobDB
        );
        foreach (var mat in pallet.Material)
        {
          mat.Mat = mat.Mat with { Action = mat.Mat.Action with { ElapsedLoadUnloadTime = elapsedLoadTime } };
          loadingIds.Add(mat.Mat.MaterialID);
        }
      }

      // now material to unload
      foreach (var mat in unusedMatsOnPal.Values.Where(m => !loadingIds.Contains(m.Mat.MaterialID)))
      {
        mat.Mat = mat.Mat with
        {
          Action = new InProcessMaterialAction()
          {
            Type =
              mat.Mat.Process == mat.Job.Processes.Count
                ? InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
                : InProcessMaterialAction.ActionType.UnloadToInProcess,
            UnloadIntoQueue = OutputQueueForMaterial(mat, pallet.Log, defaultToScrap: true),
            ElapsedLoadUnloadTime = elapsedLoadTime,
          },
        };
        pallet.Material.Add(mat);
      }
    }

    private void AddPalletCycle(
      PalletAndMaterial pallet,
      LogEntry loadBegin,
      HashSet<long> currentlyLoading,
      DateTime nowUtc,
      IRepository logDB
    )
    {
      var toUnload = new List<MaterialToUnloadFromFace>();

      // record unload-end
      foreach (var face in pallet.Material.GroupBy(m => m.Mat.Location.Face))
      {
        // everything on a face shares the job, proc, and path
        var job = face.First().Job;
        var proc = face.First().Mat.Process;
        var path = face.First().Mat.Path;
        var pathInfo = job.Processes[proc - 1].Paths[path - 1];

        toUnload.Add(
          new MaterialToUnloadFromFace()
          {
            MaterialIDToQueue = face.ToImmutableDictionary(
              m => m.Mat.MaterialID,
              m => OutputQueueForMaterial(m, pallet.Log, defaultToScrap: false)
            ),
            FaceNum = face.Key ?? 0,
            Process = proc,
            ActiveOperationTime = TimeSpan.FromTicks(pathInfo.ExpectedUnloadTime.Ticks * face.Count()),
          }
        );
      }

      var unusedMatsOnPal = pallet.Material.ToDictionary(m => m.Mat.MaterialID, m => m);
      pallet.Material.Clear();

      // add load-end for material put onto
      var toLoad = new List<MaterialToLoadOntoFace>();

      if (pallet.Status.HasWork)
      {
        SetMaterialToLoadOnFace(
          pallet,
          allocateNew: true,
          actionIsLoading: false,
          nowUtc: nowUtc,
          currentlyLoading: currentlyLoading,
          unusedMatsOnPal: unusedMatsOnPal,
          logDB: logDB
        );

        toLoad = pallet
          .Material.GroupBy(p => p.Mat.Location.Face ?? 1)
          .OrderBy(face => face.Key)
          .Select(face =>
          {
            var job = face.First().Job;
            var proc = face.First().Mat.Process;
            var path = face.First().Mat.Path;
            var pathInfo = job.Processes[proc - 1].Paths[path - 1];

            return new MaterialToLoadOntoFace()
            {
              MaterialIDs = face.Select(m => m.Mat.MaterialID).ToImmutableList(),
              FaceNum = face.Key,
              Process = proc,
              Path = path,
              ActiveOperationTime = TimeSpan.FromTicks(pathInfo.ExpectedLoadTime.Ticks * face.Count()),
            };
          })
          .ToList();
      }

      if (toLoad.Count > 0 || toUnload.Count > 0)
      {
        var lulEvts = logDB.RecordLoadUnloadComplete(
          toLoad: toLoad,
          previouslyLoaded: null,
          toUnload: toUnload,
          previouslyUnloaded: null,
          lulNum: loadBegin.LocationNum,
          pallet: pallet.Status.Master.PalletNum,
          totalElapsed: nowUtc.Subtract(loadBegin.EndTimeUTC),
          timeUTC: nowUtc,
          externalQueues: _settings.ExternalQueues
        );

        pallet.Log = lulEvts.Where(e => e.EndTimeUTC > nowUtc).ToList();
      }
      else
      {
        logDB.RecordEmptyPallet(pallet: pallet.Status.Master.PalletNum, timeUTC: nowUtc);
        pallet.Log = [];
      }
    }

    private void EnsureAllNonloadStopsHaveEvents(
      PalletAndMaterial pallet,
      DateTime nowUtc,
      IRepository jobDB,
      ref bool palletStateUpdated
    )
    {
      var unusedLogs = pallet.Log.ToList();

      foreach (var face in pallet.CurrentOrLoadingFaces)
      {
        var matOnFace = pallet
          .Material.Where(m =>
            m.Mat.JobUnique == face.Job.UniqueStr
            && m.Mat.Process == face.Process
            && m.Mat.Path == face.Path
            && m.Mat.Location.Face == face.Face
          )
          .ToList();
        var matIdsOnFace = new HashSet<long>(matOnFace.Select(m => m.Mat.MaterialID));

        foreach (var ss in MatchStopsAndSteps(jobDB, pallet, face))
        {
          switch (ss.IccStep)
          {
            case MachiningStep step:
              // find log events if they exist
              var machStart = unusedLogs
                .Where(e =>
                  e.LogType == LogType.MachineCycle
                  && e.StartOfCycle
                  && e.Program == ss.ProgramName
                  && e.Material.Any(m => matIdsOnFace.Contains(m.MaterialID))
                )
                .FirstOrDefault();
              var machEnd = unusedLogs
                .Where(e =>
                  e.LogType == LogType.MachineCycle
                  && !e.StartOfCycle
                  && e.Program == ss.ProgramName
                  && e.Material.Any(m => matIdsOnFace.Contains(m.MaterialID))
                )
                .FirstOrDefault();
              if (machStart != null)
                unusedLogs.Remove(machStart);
              if (machEnd != null)
                unusedLogs.Remove(machEnd);

              if (ss.StopCompleted)
              {
                if (machEnd == null)
                {
                  RecordMachineEnd(
                    pallet,
                    face,
                    matOnFace,
                    ss,
                    machStart,
                    nowUtc,
                    jobDB,
                    ref palletStateUpdated
                  );
                }
              }
              else
              {
                if (
                  pallet.Status.CurStation.Location.Location == PalletLocationEnum.Machine
                  && ss.JobStop.StationGroup == pallet.Status.CurStation.Location.StationGroup
                  && ss.JobStop.Stations.Contains(pallet.Status.CurStation.Location.Num)
                )
                {
                  RecordPalletAtMachine(
                    pallet,
                    face,
                    matOnFace,
                    ss,
                    machStart,
                    machEnd,
                    nowUtc,
                    jobDB,
                    ref palletStateUpdated
                  );
                }
              }
              break;

            case ReclampStep step:
              // find log events if they exist
              var reclampStart = unusedLogs
                .Where(e =>
                  e.LogType == LogType.LoadUnloadCycle
                  && e.StartOfCycle
                  && e.Result == ss.JobStop.StationGroup
                  && e.Material.Any(m => matIdsOnFace.Contains(m.MaterialID))
                )
                .FirstOrDefault();
              var reclampEnd = unusedLogs
                .Where(e =>
                  e.LogType == LogType.LoadUnloadCycle
                  && !e.StartOfCycle
                  && e.Result == ss.JobStop.StationGroup
                  && e.Material.Any(m => matIdsOnFace.Contains(m.MaterialID))
                )
                .FirstOrDefault();
              if (reclampStart != null)
                unusedLogs.Remove(reclampStart);
              if (reclampEnd != null)
                unusedLogs.Remove(reclampEnd);

              if (ss.StopCompleted)
              {
                if (reclampEnd == null)
                {
                  RecordReclampEnd(
                    pallet,
                    face,
                    matOnFace,
                    ss,
                    reclampStart,
                    nowUtc,
                    jobDB,
                    ref palletStateUpdated
                  );
                }
              }
              else
              {
                if (
                  pallet.Status.CurStation.Location.Location == PalletLocationEnum.LoadUnload
                  && step.Reclamp.Contains(pallet.Status.CurStation.Location.Num)
                )
                {
                  MarkReclampInProgress(
                    pallet,
                    face,
                    matOnFace,
                    ss,
                    reclampStart,
                    nowUtc,
                    jobDB,
                    ref palletStateUpdated
                  );
                }
              }
              break;
          }
        }
      }

      if (pallet.Status.CurrentStep is UnloadStep && pallet.Status.Tracking.BeforeCurrentStep)
      {
        MakeInspectionDecisions(pallet, nowUtc, jobDB, ref palletStateUpdated);
      }
    }

    public class StopAndStep
    {
      public MachiningStop JobStop { get; set; }
      public int IccStepNum { get; set; } // 1-indexed
      public RouteStep IccStep { get; set; }
      public bool StopCompleted { get; set; }
      public string ProgramName { get; set; }
      public long? Revision { get; set; }
      public string IccMachineProgram { get; set; }
    }

    private IReadOnlyList<StopAndStep> MatchStopsAndSteps(
      IRepository jobDB,
      PalletAndMaterial pallet,
      PalletFace face
    )
    {
      var ret = new List<StopAndStep>();

      int stepIdx = 1; // 0-indexed, first step is load so skip it
      int machineStopIdx = -1;

      foreach (var stop in face.PathInfo.Stops)
      {
        // find out if job stop is machine or reclamp and which program
        string programName = null;
        long? revision = null;
        string iccMachineProg = null;
        bool isReclamp = false;
        if (
          _niigataSettings.StationNames != null
          && _niigataSettings.StationNames.ReclampGroupNames.Contains(stop.StationGroup)
        )
        {
          isReclamp = true;
        }
        else
        {
          machineStopIdx += 1;
          var faceProg = face.Programs.FirstOrDefault(p => p.MachineStopIndex == machineStopIdx);
          if (faceProg != null && faceProg.Revision.HasValue)
          {
            programName = faceProg.ProgramName;
            revision = faceProg.Revision;
            iccMachineProg = jobDB
              .LoadProgram(faceProg.ProgramName, faceProg.Revision.Value)
              ?.CellControllerProgramName;
          }
          else if (faceProg != null)
          {
            programName = faceProg.ProgramName;
            revision = faceProg.Revision;
            iccMachineProg = faceProg.ProgramName;
          }
          else if (stop.ProgramRevision.HasValue)
          {
            programName = stop.Program;
            revision = stop.ProgramRevision;
            iccMachineProg = jobDB
              .LoadProgram(stop.Program, stop.ProgramRevision.Value)
              ?.CellControllerProgramName;
          }
          else
          {
            programName = stop.Program;
            revision = null;
            iccMachineProg = stop.Program;
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
              if (
                !string.IsNullOrEmpty(iccMachineProg)
                && stop.Stations.Any(s =>
                {
                  var iccMcNum = s;
                  if (_niigataSettings.StationNames != null)
                  {
                    iccMcNum = _niigataSettings.StationNames.JobMachToIcc(stop.StationGroup, s);
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

        matchingStep:
        ;

        if (stepIdx + 1 > pallet.Status.Tracking.CurrentStepNum)
        {
          break;
        }

        // add the matching stop and step
        ret.Add(
          new StopAndStep()
          {
            JobStop = stop,
            IccStepNum = stepIdx + 1,
            IccStep = step,
            ProgramName = programName,
            Revision = revision,
            IccMachineProgram = iccMachineProg,
            StopCompleted =
              (stepIdx + 1 < pallet.Status.Tracking.CurrentStepNum)
              || (
                stepIdx + 1 == pallet.Status.Tracking.CurrentStepNum
                && pallet.Status.Tracking.BeforeCurrentStep == false
                && pallet.Status.Tracking.Alarm == false
                && (
                  pallet.MachineStatus == null
                  || (pallet.MachineStatus.FMSLinkMode && pallet.MachineStatus.Alarm == false)
                )
              ),
          }
        );

        stepIdx += 1;

        if (stepIdx + 1 > pallet.Status.Tracking.CurrentStepNum)
        {
          break;
        }
      }
      return ret;
    }

    private void RecordPalletAtMachine(
      PalletAndMaterial pallet,
      PalletFace face,
      IEnumerable<InProcessMaterialAndJob> matOnFace,
      StopAndStep ss,
      LogEntry machineStart,
      LogEntry machineEnd,
      DateTime nowUtc,
      IRepository jobDB,
      ref bool palletStateUpdated
    )
    {
      bool runningProgram = false;
      bool runningAnyProgram = false;
      if (pallet.MachineStatus != null && pallet.MachineStatus.CurrentlyExecutingProgram > 0)
      {
        runningAnyProgram = true;
        runningProgram = pallet.MachineStatus.CurrentlyExecutingProgram.ToString() == ss.IccMachineProgram;
      }

      if (runningProgram && machineStart == null)
      {
        // record start of new cycle
        Log.Debug("Recording machine start for {@pallet} and face {@face} for stop {@ss}", pallet, face, ss);

        palletStateUpdated = true;

        var tools = _machConnection.ToolsForMachine(
          _niigataSettings.StationNames.JobMachToIcc(
            pallet.Status.CurStation.Location.StationGroup,
            pallet.Status.CurStation.Location.Num
          )
        );

        jobDB.RecordMachineStart(
          mats: matOnFace.Select(m => new EventLogMaterial()
          {
            MaterialID = m.Mat.MaterialID,
            Process = m.Mat.Process,
            Face = face.Face,
          }),
          pallet: pallet.Status.Master.PalletNum,
          statName: pallet.Status.CurStation.Location.StationGroup,
          statNum: pallet.Status.CurStation.Location.Num,
          program: ss.ProgramName,
          timeUTC: nowUtc,
          extraData: !ss.Revision.HasValue
            ? null
            : new Dictionary<string, string> { { "ProgramRevision", ss.Revision.Value.ToString() } },
          pockets: tools?.Select(t =>
            t.ToToolInMachine(
              machineGroup: pallet.Status.CurStation.Location.StationGroup,
              machineNum: pallet.Status.CurStation.Location.Num
            )
          )
        );

        foreach (var m in matOnFace)
        {
          m.Mat = m.Mat with
          {
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = ss.Revision.HasValue
                ? ss.ProgramName + " rev" + ss.Revision.Value.ToString()
                : ss.ProgramName,
              ElapsedMachiningTime = TimeSpan.Zero,
              ExpectedRemainingMachiningTime = ss.JobStop.ExpectedCycleTime,
            },
          };
        }
      }
      else if (runningProgram || (!runningAnyProgram && machineStart != null && machineEnd == null))
      {
        // update material to be running, since either program is running or paused with nothing running
        var elapsed = machineStart == null ? TimeSpan.Zero : nowUtc.Subtract(machineStart.EndTimeUTC);
        foreach (var m in matOnFace)
        {
          m.Mat = m.Mat with
          {
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              Program = ss.Revision.HasValue
                ? ss.ProgramName + " rev" + ss.Revision.Value.ToString()
                : ss.ProgramName,
              ElapsedMachiningTime = elapsed,
              ExpectedRemainingMachiningTime = ss.JobStop.ExpectedCycleTime.Subtract(elapsed),
            },
          };
        }
      }
      else if (!runningProgram && machineStart != null && machineEnd == null)
      {
        // a different program started, record machine end
        RecordMachineEnd(pallet, face, matOnFace, ss, machineStart, nowUtc, jobDB, ref palletStateUpdated);
      }
    }

    private void RecordMachineEnd(
      PalletAndMaterial pallet,
      PalletFace face,
      IEnumerable<InProcessMaterialAndJob> matOnFace,
      StopAndStep ss,
      LogEntry machStart,
      DateTime nowUtc,
      IRepository logDB,
      ref bool palletStateUpdated
    )
    {
      Log.Debug(
        "Recording machine end for {@pallet} and face {@face} for stop {@ss} with logs {@machStart}",
        pallet,
        face,
        ss,
        machStart
      );
      palletStateUpdated = true;

      string statName;
      int statNum;
      IEnumerable<ToolSnapshot> toolsAtStart;
      if (machStart != null)
      {
        statName = machStart.LocationName;
        statNum = machStart.LocationNum;
        toolsAtStart =
          logDB.ToolPocketSnapshotForCycle(machStart.Counter) ?? Enumerable.Empty<ToolSnapshot>();
      }
      else if (ss.IccStepNum <= pallet.Status.Tracking.ExecutedStationNumber.Count)
      {
        statName = ss.JobStop.StationGroup;
        var stat = pallet.Status.Tracking.ExecutedStationNumber[ss.IccStepNum - 1];
        if (stat != null)
        {
          if (
            (
              stat.Location.Location != PalletLocationEnum.Machine
              && stat.Location.Location != PalletLocationEnum.MachineQueue
            )
            || stat.Location.StationGroup != ss.JobStop.StationGroup
          )
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
        toolsAtEnd = _machConnection.ToolsForMachine(
          _niigataSettings.StationNames.JobMachToIcc(statName, statNum)
        );
      }

      logDB.RecordMachineEnd(
        mats: matOnFace.Select(m => new EventLogMaterial()
        {
          MaterialID = m.Mat.MaterialID,
          Process = m.Mat.Process,
          Face = face.Face,
        }),
        pallet: pallet.Status.Master.PalletNum,
        statName: statName,
        statNum: statNum,
        program: ss.ProgramName,
        result: "",
        timeUTC: nowUtc,
        elapsed: machStart != null ? nowUtc.Subtract(machStart.EndTimeUTC) : TimeSpan.Zero, // TODO: lookup start from SQL?
        active: ss.JobStop.ExpectedCycleTime,
        extraData: !ss.Revision.HasValue
          ? null
          : new Dictionary<string, string> { { "ProgramRevision", ss.Revision.Value.ToString() } },
        tools: toolsAtStart == null || toolsAtEnd == null
          ? null
          : ToolSnapshotDiff.Diff(
            toolsAtStart,
            toolsAtEnd.Select(t => t.ToToolInMachine(machineGroup: statName, machineNum: statNum))
          ),
        deleteToolSnapshotsFromCntr: machStart?.Counter
      );
    }

    private void MarkReclampInProgress(
      PalletAndMaterial pallet,
      PalletFace face,
      IEnumerable<InProcessMaterialAndJob> matOnFace,
      StopAndStep ss,
      LogEntry reclampStart,
      DateTime nowUtc,
      IRepository logDB,
      ref bool palletStateUpdated
    )
    {
      if (reclampStart == null)
      {
        Log.Debug("Recording reclamp start for {@pallet} and face {@face} for stop {@ss}", pallet, face, ss);
        palletStateUpdated = true;
        logDB.RecordManualWorkAtLULStart(
          mats: matOnFace.Select(m => new EventLogMaterial()
          {
            MaterialID = m.Mat.MaterialID,
            Process = m.Mat.Process,
            Face = face.Face,
          }),
          pallet: pallet.Status.Master.PalletNum,
          lulNum: pallet.Status.CurStation.Location.Num,
          operationName: ss.JobStop.StationGroup,
          timeUTC: nowUtc
        );

        foreach (var m in matOnFace)
        {
          m.Mat = m.Mat with
          {
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Loading,
              ElapsedLoadUnloadTime = TimeSpan.Zero,
            },
          };
        }
      }
      else
      {
        foreach (var m in matOnFace)
        {
          m.Mat = m.Mat with
          {
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Loading,
              ElapsedLoadUnloadTime = nowUtc.Subtract(reclampStart.EndTimeUTC),
            },
          };
        }
      }
    }

    private void RecordReclampEnd(
      PalletAndMaterial pallet,
      PalletFace face,
      IEnumerable<InProcessMaterialAndJob> matOnFace,
      StopAndStep ss,
      LogEntry reclampStart,
      DateTime nowUtc,
      IRepository logDB,
      ref bool palletStateUpdated
    )
    {
      Log.Debug(
        "Recording reclamp end for {@pallet} and face {@face} for stop {@ss} with logs {@machStart}",
        pallet,
        face,
        ss,
        reclampStart
      );
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
        mats: matOnFace.Select(m => new EventLogMaterial()
        {
          MaterialID = m.Mat.MaterialID,
          Process = m.Mat.Process,
          Face = face.Face,
        }),
        pallet: pallet.Status.Master.PalletNum,
        lulNum: statNum,
        operationName: ss.JobStop.StationGroup,
        timeUTC: nowUtc,
        elapsed: reclampStart != null ? nowUtc.Subtract(reclampStart.EndTimeUTC) : TimeSpan.Zero, // TODO: lookup start from SQL?
        active: ss.JobStop.ExpectedCycleTime
      );
    }

    private void MakeInspectionDecisions(
      PalletAndMaterial pallet,
      DateTime timeUTC,
      IRepository logDB,
      ref bool palletStateUpdated
    )
    {
      foreach (var mat in pallet.Material)
      {
        var pathInfo = mat.Job.Processes[mat.Mat.Process - 1].Paths[mat.Mat.Path - 1];
        var inspections = pathInfo.Inspections;
        if (inspections != null && inspections.Count > 0)
        {
          var decisions = logDB.MakeInspectionDecisions(
            mat.Mat.MaterialID,
            mat.Mat.Process,
            inspections,
            timeUTC
          );
          if (decisions.Any())
          {
            foreach (var log in decisions.Where(e => e.Result.ToLower() == "true"))
            {
              if (
                log.ProgramDetails != null
                && log.ProgramDetails.TryGetValue("InspectionType", out var iType)
              )
              {
                mat.Mat = mat.Mat with { SignaledInspections = [.. mat.Mat.SignaledInspections, iType] };
              }
            }
            palletStateUpdated = true;
          }
        }
      }
    }

    private void EnsurePalletRotaryEvents(PalletAndMaterial pallet, DateTime nowUtc, IRepository logDB)
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
          mats: pallet.Material.Select(m => new EventLogMaterial()
          {
            MaterialID = m.Mat.MaterialID,
            Process = m.Mat.Process,
            Face = m.Mat.Location.Face ?? 0,
          }),
          pallet: pallet.Status.Master.PalletNum,
          statName: pallet.Status.CurStation.Location.StationGroup,
          statNum: pallet.Status.CurStation.Location.Num,
          timeUTC: nowUtc
        );
      }
      else if (start != null && !currentlyOnRotary)
      {
        logDB.RecordPalletDepartRotaryInbound(
          mats: pallet.Material.Select(m => new EventLogMaterial()
          {
            MaterialID = m.Mat.MaterialID,
            Process = m.Mat.Process,
            Face = m.Mat.Location.Face ?? 0,
          }),
          pallet: pallet.Status.Master.PalletNum,
          statName: start.LocationName,
          statNum: start.LocationNum,
          timeUTC: nowUtc,
          elapsed: nowUtc.Subtract(start.EndTimeUTC),
          rotateIntoWorktable: pallet.Status.CurStation.Location.Location == PalletLocationEnum.Machine
        );
      }
    }

    private void EnsurePalletStockerEvents(PalletAndMaterial pallet, DateTime nowUtc, IRepository logDB)
    {
      if (!pallet.Material.Any())
        return;

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
      for (
        int stepNum = pallet.Status.Tracking.CurrentStepNum + 1;
        stepNum <= pallet.Status.Master.Routes.Count;
        stepNum += 1
      )
      {
        if (pallet.Status.Master.Routes[stepNum - 1] is MachiningStep)
        {
          waitForMachine = true;
        }
      }

      if (start == null && currentlyAtStocker)
      {
        logDB.RecordPalletArriveStocker(
          mats: pallet.Material.Select(m => new EventLogMaterial()
          {
            MaterialID = m.Mat.MaterialID,
            Process = m.Mat.Process,
            Face = m.Mat.Location.Face ?? 0,
          }),
          pallet: pallet.Status.Master.PalletNum,
          stockerNum: pallet.Status.CurStation.Location.Num,
          timeUTC: nowUtc,
          waitForMachine: waitForMachine
        );
      }
      else if (start != null && !currentlyAtStocker)
      {
        logDB.RecordPalletDepartStocker(
          mats: pallet.Material.Select(m => new EventLogMaterial()
          {
            MaterialID = m.Mat.MaterialID,
            Process = m.Mat.Process,
            Face = m.Mat.Location.Face ?? 0,
          }),
          pallet: pallet.Status.Master.PalletNum,
          stockerNum: start.LocationNum,
          timeUTC: nowUtc,
          elapsed: nowUtc.Subtract(start.EndTimeUTC),
          waitForMachine: waitForMachine
        );
      }
    }

    #region Material Computations
    private ImmutableList<InProcessMaterialAndJob> QueuedMaterial(
      HashSet<long> matsOnPallets,
      IRepository logDB,
      IJobCache loadJob
    )
    {
      var allMats = MachineFramework
        .BuildCellState.AllQueuedMaterial(logDB, loadJob)
        .Where(m => !matsOnPallets.Contains(m.InProc.MaterialID));

      var works = logDB.WorkordersById(
        allMats
          .Where(m => !string.IsNullOrEmpty(m.InProc.WorkorderId))
          .Select(m => m.InProc.WorkorderId)
          .ToHashSet()
      );

      return allMats
        .Select(m => new InProcessMaterialAndJob()
        {
          Job = m.Job,
          Mat = m.InProc,
          Workorders = string.IsNullOrEmpty(m.InProc.WorkorderId) ? null : works[m.InProc.WorkorderId],
        })
        .ToImmutableList();
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
      public ImmutableList<Workorder> Workorders { get; set; }
    }

    public static bool FilterMaterialAvailableToLoadOntoFace(
      QueuedMaterialWithDetails mat,
      PalletFace face,
      NiigataStationNames statNames
    )
    {
      if (face.Process == 1 && mat.NextProcess == 1 && string.IsNullOrEmpty(mat.Unique))
      {
        // check for casting on process 1
        var casting = face.PathInfo.Casting;
        if (string.IsNullOrEmpty(casting))
          casting = face.Job.PartName;

        if (mat.PartNameOrCasting != casting)
          return false;

        // check workorders
        var matWorkProgs = WorkorderProgramsForPart(face.Job.PartName, mat.Workorders);
        if (matWorkProgs == null)
        {
          // material will just inherit programs on pallet
          return true;
        }
        else if (face.Programs == null)
        {
          return CheckProgramsMatchJobSteps(
            mat.Workorder,
            face,
            WorkorderProgramsForProcess(1, matWorkProgs),
            statNames
          );
        }
        else
        {
          return CheckProgramsMatch(face.Programs, WorkorderProgramsForProcess(1, matWorkProgs));
        }
      }
      else
      {
        // check unique and process match
        if (mat.Unique != face.Job.UniqueStr)
          return false;
        if (mat.NextProcess != face.Process)
          return false;

        // finally, check workorders
        var matWorkProgs = WorkorderProgramsForPart(face.Job.PartName, mat.Workorders);
        if (face.Programs == null && matWorkProgs == null)
        {
          return true;
        }
        else if (face.Programs == null && matWorkProgs != null)
        {
          return CheckProgramsMatchJobSteps(
            mat.Workorder,
            face,
            WorkorderProgramsForProcess(face.Process, matWorkProgs),
            statNames
          );
        }
        else if (face.Programs != null && matWorkProgs == null)
        {
          return CheckProgramsMatch(face.Programs, JobProgramsFromStops(face.PathInfo.Stops, statNames));
        }
        else
        {
          return CheckProgramsMatch(face.Programs, WorkorderProgramsForProcess(face.Process, matWorkProgs));
        }
      }
    }

    private static bool CheckProgramsMatch(
      IEnumerable<ProgramsForProcess> ps1,
      IEnumerable<ProgramsForProcess> ps2
    )
    {
      // can use Enumerable.SequenceEqual when ProgramsForProcess is converted to a record
      var byStop = ps1.GroupBy(p => p.MachineStopIndex).ToDictionary(p => p.Key, p => p.First());

      foreach (var p2 in ps2)
      {
        if (byStop.TryGetValue(p2.MachineStopIndex, out var p1))
        {
          if (p1.ProgramName != p2.ProgramName || p1.Revision != p2.Revision)
          {
            return false;
          }
          byStop.Remove(p2.MachineStopIndex);
        }
        else
        {
          return false;
        }
      }

      return byStop.Count == 0;
    }

    private static bool CheckProgramsMatchJobSteps(
      string workorder,
      PalletFace face,
      IEnumerable<ProgramsForProcess> ps,
      NiigataStationNames statNames
    )
    {
      var byStop = ps.GroupBy(p => p.MachineStopIndex).ToDictionary(p => p.Key, p => p.First());

      int machStopIdx = -1;
      foreach (
        var stop in face.PathInfo.Stops.Where(s => !statNames.ReclampGroupNames.Contains(s.StationGroup))
      )
      {
        machStopIdx += 1;

        if (byStop.TryGetValue(machStopIdx, out var p))
        {
          byStop.Remove(machStopIdx);
        }
        else
        {
          Log.Warning(
            "Workorder {workorder} programs for process {proc} do not match job machining steps for job {uniq}",
            workorder,
            face.Process,
            face.Job.UniqueStr
          );
          return false;
        }
      }

      if (byStop.Count == 0)
      {
        return true;
      }
      else
      {
        Log.Warning(
          "Workorder {workorder} programs for process {proc} do not match job machining steps for job {uniq}",
          workorder,
          face.Process,
          face.Job.UniqueStr
        );
        return false;
      }
    }

    private static IEnumerable<ProgramForJobStep> WorkorderProgramsForPart(
      string part,
      IEnumerable<Workorder> works
    )
    {
      return works?.Where(w => w.Part == part).FirstOrDefault()?.Programs;
    }

    public static IEnumerable<ProgramsForProcess> JobProgramsFromStops(
      IEnumerable<MachiningStop> stops,
      NiigataStationNames statNames
    )
    {
      return stops
        .Where(s => statNames == null || !statNames.ReclampGroupNames.Contains(s.StationGroup))
        .Select(
          (stop, stopIdx) =>
            new ProgramsForProcess()
            {
              MachineStopIndex = stopIdx,
              ProgramName = stop.Program,
              Revision = stop.ProgramRevision,
            }
        );
    }

    public static IEnumerable<ProgramsForProcess> WorkorderProgramsForProcess(
      int proc,
      IEnumerable<ProgramForJobStep> works
    )
    {
      return works
        .Where(p => p.ProcessNumber == proc)
        .Select(p => new ProgramsForProcess()
        {
          MachineStopIndex = p.StopIndex ?? 0,
          ProgramName = p.ProgramName,
          Revision = p.Revision,
        });
    }

    private string OutputQueueForMaterial(
      InProcessMaterialAndJob mat,
      IReadOnlyList<LogEntry> log,
      bool defaultToScrap
    )
    {
      var signalQuarantine = log.LastOrDefault(e =>
        e.LogType == LogType.SignalQuarantine && e.Material.Any(m => m.MaterialID == mat.Mat.MaterialID)
      );

      if (signalQuarantine != null)
      {
        var queue = signalQuarantine.LocationName ?? _settings.QuarantineQueue;
        if (defaultToScrap)
        {
          return queue ?? "Scrap";
        }
        else
        {
          return queue;
        }
      }
      else
      {
        return mat.Job.Processes[mat.Mat.Process - 1].Paths[mat.Mat.Path - 1].OutputQueue;
      }
    }

    private Dictionary<(string progName, long revision), ProgramRevision> FindProgramNums(
      IRepository jobDB,
      IEnumerable<Job> unarchivedJobs,
      IEnumerable<InProcessMaterialAndJob> mats,
      NiigataStatus status
    )
    {
      var progs = new Dictionary<(string progName, long revision), ProgramRevision>();

      var stops = unarchivedJobs
        .SelectMany(j => j.Processes)
        .SelectMany(p => p.Paths)
        .SelectMany(p => p.Stops);
      foreach (var stop in stops)
      {
        if (stop.ProgramRevision.HasValue)
        {
          var prog = jobDB.LoadProgram(stop.Program, stop.ProgramRevision.Value);
          if (
            !string.IsNullOrEmpty(prog.CellControllerProgramName)
            && int.TryParse(prog.CellControllerProgramName, out var progNum)
          )
          {
            // operator might have deleted the program
            if (
              !status.Programs.ContainsKey(progNum)
              || !AssignNewRoutesOnPallets.IsInsightProgram(status.Programs[progNum])
            )
            {
              // clear it, so that the Assignment adds the program back as a new number
              jobDB.SetCellControllerProgramForProgram(prog.ProgramName, prog.Revision, null);
              prog = prog with { CellControllerProgramName = null };
            }
          }
          progs[(stop.Program, stop.ProgramRevision.Value)] = prog;
        }
      }

      foreach (var mat in mats)
      {
        if (mat.Workorders != null)
        {
          var nextOrCurProc =
            mat.Mat.Location.Type == InProcessMaterialLocation.LocType.OnPallet
              ? mat.Mat.Process
              : mat.Mat.Process + 1;
          foreach (
            var workProg in mat.Workorders.SelectMany(w =>
              w.Programs ?? Enumerable.Empty<ProgramForJobStep>()
            )
          )
          {
            if (workProg.Revision.HasValue && workProg.ProcessNumber >= nextOrCurProc)
            {
              var prog = jobDB.LoadProgram(workProg.ProgramName, workProg.Revision.Value);
              if (
                !string.IsNullOrEmpty(prog.CellControllerProgramName)
                && int.TryParse(prog.CellControllerProgramName, out var progNum)
              )
              {
                // operator might have deleted the program
                if (
                  !status.Programs.ContainsKey(progNum)
                  || !AssignNewRoutesOnPallets.IsInsightProgram(status.Programs[progNum])
                )
                {
                  // clear it, so that the Assignment adds the program back as a new number
                  jobDB.SetCellControllerProgramForProgram(prog.ProgramName, prog.Revision, null);
                  prog = prog with { CellControllerProgramName = null };
                }
              }
              progs[(workProg.ProgramName, workProg.Revision.Value)] = prog;
            }
          }
        }
      }

      return progs;
    }

    private List<ProgramRevision> OldUnusedPrograms(
      IRepository jobDB,
      NiigataStatus status,
      Dictionary<(string progName, long revision), ProgramRevision> usedProgs
    )
    {
      // the icc program numbers currently used by schedules
      var usedIccProgs = new HashSet<string>(
        usedProgs
          .Values.Select(p => p.CellControllerProgramName)
          .Concat(
            status
              .Pallets.SelectMany(p => p.Master.Routes)
              .SelectMany(r =>
                r is MachiningStep
                  ? ((MachiningStep)r).ProgramNumsToRun.Select(p => p.ToString())
                  : Enumerable.Empty<string>()
              )
          )
      );

      // we want to keep around the latest revision for each program just so that we don't delete it as soon as a schedule
      // completes in anticipation of a new schedule being downloaded.
      var maxRevForProg = status
        .Programs.Select(p =>
          AssignNewRoutesOnPallets.TryParseProgramComment(p.Value, out string pName, out long rev)
            ? new { pName, rev }
            : null
        )
        .Where(p => p != null)
        .ToLookup(p => p.pName, p => p.rev)
        .ToDictionary(ps => ps.Key, ps => ps.Max());

      var progsToDelete = new Dictionary<string, ProgramRevision>();

      // ICC has a bug where it occasionally (about 1 out of 20 time) only partially deletes the program.  It is gone from
      // the status_program PostgreSQL table but still exists.  So first calculate the programs to delete from our own
      // database.

      foreach (var prog in jobDB.LoadProgramsInCellController())
      {
        if (string.IsNullOrEmpty(prog.CellControllerProgramName))
          continue;
        if (usedIccProgs.Contains(prog.CellControllerProgramName))
          continue;
        if (maxRevForProg.ContainsKey(prog.ProgramName) && prog.Revision >= maxRevForProg[prog.ProgramName])
          continue;
        progsToDelete.Add(prog.CellControllerProgramName, prog);
      }

      // next, check programs in the ICC itself (perhaps the databases were deleted)
      foreach (var prog in status.Programs)
      {
        if (!AssignNewRoutesOnPallets.IsInsightProgram(prog.Value))
          continue;
        if (usedIccProgs.Contains(prog.Key.ToString()))
          continue;
        if (progsToDelete.ContainsKey(prog.Key.ToString()))
          continue;
        if (!AssignNewRoutesOnPallets.TryParseProgramComment(prog.Value, out string pName, out long rev))
          continue;
        if (rev >= maxRevForProg[pName])
          continue;

        progsToDelete.Add(
          prog.Key.ToString(),
          new ProgramRevision()
          {
            ProgramName = pName,
            Revision = rev,
            Comment = "",
            CellControllerProgramName = prog.Key.ToString(),
          }
        );
      }

      return progsToDelete.Values.ToList();
    }

    private static IEnumerable<InProcessMaterial> SetLongTool(IEnumerable<PalletAndMaterial> pallets)
    {
      // tool loads/unloads
      foreach (var pal in pallets.Where(p => p.Status.Master.ForLongToolMaintenance && p.Status.HasWork))
      {
        if (pal.Status.CurrentStep == null)
          continue;
        switch (pal.Status.CurrentStep)
        {
          case LoadStep load:
            if (pal.Status.Tracking.BeforeCurrentStep)
            {
              yield return new InProcessMaterial()
              {
                MaterialID = -1,
                JobUnique = "",
                PartName = "LongTool",
                Process = 1,
                Path = 1,
                Location = new InProcessMaterialLocation() { Type = InProcessMaterialLocation.LocType.Free },
                SignaledInspections = ImmutableList<string>.Empty,
                QuarantineAfterUnload = null,
                Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.Loading,
                  LoadOntoPalletNum = pal.Status.Master.PalletNum,
                  LoadOntoFace = 1,
                  ProcessAfterLoad = 1,
                  PathAfterLoad = 1,
                },
              };
            }
            break;
          case UnloadStep unload:
            if (pal.Status.Tracking.BeforeCurrentStep)
            {
              yield return new InProcessMaterial()
              {
                MaterialID = -1,
                JobUnique = "",
                PartName = "LongTool",
                Process = 1,
                Path = 1,
                SignaledInspections = ImmutableList<string>.Empty,
                QuarantineAfterUnload = null,
                Location = new InProcessMaterialLocation()
                {
                  Type = InProcessMaterialLocation.LocType.OnPallet,
                  PalletNum = pal.Status.Master.PalletNum,
                  Face = 1,
                },
                Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
                },
              };
            }
            break;
        }
      }
    }

    private static string AlarmCodeToString(int palletNum, PalletAlarmCode code)
    {
      string msg = " has alarm code " + code.ToString();
      switch (code)
      {
        case PalletAlarmCode.AlarmSetOnScreen:
          msg = " has alarm set by operator";
          break;
        case PalletAlarmCode.M165:
          msg = " has alarm M165";
          break;
        case PalletAlarmCode.RoutingFault:
          msg = " has routing fault";
          break;
        case PalletAlarmCode.LoadingFromAutoLULStation:
          msg = " is loading from auto L/UL station";
          break;
        case PalletAlarmCode.ProgramRequestAlarm:
          msg = " has program request alarm";
          break;
        case PalletAlarmCode.ProgramRespondingAlarm:
          msg = " has program responding alarm";
          break;
        case PalletAlarmCode.ProgramTransferAlarm:
          msg = " has program transfer alarm";
          break;
        case PalletAlarmCode.ProgramTransferFinAlarm:
          msg = " has program transfer alarm";
          break;
        case PalletAlarmCode.MachineAutoOff:
          msg = " at machine with auto off";
          break;
        case PalletAlarmCode.MachinePowerOff:
          msg = " at machine which is powered off";
          break;
        case PalletAlarmCode.IccExited:
          msg = " can't communicate with ICC";
          break;
      }
      return "Pallet " + palletNum.ToString() + msg;
    }
    #endregion
  }
}
