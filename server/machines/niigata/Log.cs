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
    public List<PalletFace> Faces { get; set; } // key is the face
  }

  public interface ICreateLog
  {
    List<PalletAndMaterial> CheckForNewLogEntries(NiigataStatus status, PlannedSchedule sch, out bool palletStateUpdated, DateTime? nowUtc = null);
  }

  public class CreateLogEntries : ICreateLog
  {
    private JobLogDB _log;
    private JobDB _jobs;
    private static Serilog.ILogger Log = Serilog.Log.ForContext<CreateLogEntries>();

    public CreateLogEntries(JobLogDB l, JobDB jobs)
    {
      _log = l;
      _jobs = jobs;
    }

    public List<PalletAndMaterial> CheckForNewLogEntries(NiigataStatus status, PlannedSchedule sch, out bool palletStateUpdated, DateTime? nowUtc = null)
    {
      var now = nowUtc ?? DateTime.UtcNow;
      palletStateUpdated = false;
      var pals = new List<PalletAndMaterial>();

      foreach (var pal in status.Pallets)
      {
        var log = _log.CurrentPalletLog(pal.Master.PalletNum.ToString());
        if (pal.Master.NoWork)
        {
          pals.Add(new PalletAndMaterial()
          {
            Status = pal,
            Faces = new List<PalletFace>()
          });
        }
        else if (pal.Tracking.CurrentStepNum == AssignPallets.LoadStepNum && pal.Tracking.BeforeCurrentStep)
        {
          // Before load
          pals.Add(CurrentlyLoadingPallet(pal, log, now, ref palletStateUpdated));
        }
        else if (pal.Tracking.CurrentStepNum == AssignPallets.LoadStepNum && !pal.Tracking.BeforeCurrentStep)
        {
          // After load
          pals.Add(LoadedPallet(pal, log, now, ref palletStateUpdated));
        }
        else if (pal.Tracking.CurrentStepNum == AssignPallets.MachineStepNum && pal.Tracking.BeforeCurrentStep)
        {
          // before machine
          pals.Add(LoadedPallet(pal, log, now, ref palletStateUpdated));
        }
        else if (pal.Tracking.CurrentStepNum == AssignPallets.MachineStepNum && !pal.Tracking.BeforeCurrentStep)
        {
          // after machine
          var palAndMat = LoadedPallet(pal, log, now, ref palletStateUpdated);
          EnsureAllMachineEnds(palAndMat, log, now, ref palletStateUpdated);
          pals.Add(palAndMat);
        }
        else if (pal.Tracking.CurrentStepNum == AssignPallets.UnloadStepNum && pal.Tracking.BeforeCurrentStep)
        {
          // unload-begin
          var palAndMat = CurrentlyUnloadingPallet(pal, log, now, ref palletStateUpdated);
          EnsureAllMachineEnds(palAndMat, log, now, ref palletStateUpdated);
          pals.Add(palAndMat);
        }
        else
        {
          Log.Error("Invalid pallet state {@pallet}", pal);
        }
      }

      return pals;
    }

    private IDictionary<int, PalletFace> GetFaces(PalletStatus pallet)
    {
      return
        AssignPallets.AssignedJobAndPathForFace.FacesForPallet(_log, pallet.Master.Comment)
        .ToDictionary(m => m.Face, m => new PalletFace()
        {
          Job = _jobs.LoadJob(m.Unique),
          Process = m.Proc,
          Path = m.Path,
          Face = m.Face,
          Material = new List<InProcessMaterial>()
        });
    }

    private List<InProcessMaterial> MaterialCurrentlyOnFace(PalletStatus pallet, PalletFace face, IEnumerable<LogEntry> log, bool allocateIfMissing)
    {
      var mats = new List<InProcessMaterial>();

      if (pallet.Master.NoWork)
      {
        return mats;
      }

      foreach (var e in log)
      {
        if (e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && !e.StartOfCycle)
        {
          foreach (var m in e.Material)
          {
            if (m.Face == face.Face.ToString())
            {
              var details = _log.GetMaterialDetails(m.MaterialID);
              mats.Add(new InProcessMaterial()
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
                Location = new InProcessMaterialLocation()
                {
                  Type = InProcessMaterialLocation.LocType.OnPallet,
                  Pallet = pallet.Master.PalletNum.ToString()
                },
                Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.Waiting
                }
              });
            }
          }
        }
      }

      return mats;
    }

    private List<InProcessMaterial> MaterialToLoadOnFace(PalletStatus pallet, PalletFace face, bool allocateNew, HashSet<long> currentlyLoading, Dictionary<long, InProcessMaterial> unusedMatsOnPal)
    {
      var mats = new List<InProcessMaterial>();

      var inputQueue = face.Job.GetInputQueue(face.Process, face.Path);

      if (face.Process == 1 && string.IsNullOrEmpty(inputQueue))
      {
        // castings
        for (int i = 1; i <= face.Job.PartsPerPallet(face.Process, face.Path); i++)
        {
          long mid;
          if (allocateNew)
          {
            mid = _log.AllocateMaterialID(face.Job.UniqueStr, face.Job.PartName, face.Job.NumProcesses);
            _log.RecordPathForProcess(mid, face.Process, face.Path);
            // TODO: serial
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
          .Where(m => (string.IsNullOrEmpty(m.Unique) && m.PartName == face.Job.PartName)
                    || (!string.IsNullOrEmpty(m.Unique) && m.Unique == face.Job.UniqueStr)
          )
          .Where(m => !currentlyLoading.Contains(m.MaterialID))
          .Where(m => ProcessAndPathForMatID(m.MaterialID, face.Job).proc == 0)
          .ToList();

        foreach (var mat in castings.Take(face.Job.PartsPerPallet(face.Process, face.Path)))
        {
          mats.Add(new InProcessMaterial()
          {
            MaterialID = mat.MaterialID,
            JobUnique = face.Job.UniqueStr,
            PartName = face.Job.PartName,
            Process = 0,
            Path = 1,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.InQueue,
              CurrentQueue = inputQueue,
              QueuePosition = mat.Position,
            },
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Waiting
            }
          });
          currentlyLoading.Add(mat.MaterialID);
        }


      }
      else
      {
        // loading in-proc from pallet then queue
        var countToLoad = face.Job.PartsPerPallet(face.Process, face.Path);

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
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                Pallet = pallet.Master.PalletNum.ToString()
              },
              Action = new InProcessMaterialAction()
              {
                Type = InProcessMaterialAction.ActionType.Waiting
              }
            });
            unusedMatsOnPal.Remove(mat.MaterialID);

            if (mats.Count >= face.Job.PartsPerPallet(face.Process, face.Path))
            {
              break;
            }
          }
        }

        // now check queue
        if (!string.IsNullOrEmpty(inputQueue))
        {
          foreach (var mat in _log.GetMaterialInQueue(inputQueue))
          {
            if (mat.Unique != face.Job.UniqueStr) continue;
            var (mProc, mPath) = ProcessAndPathForMatID(mat.MaterialID, face.Job);
            if (mProc + 1 != face.Process) continue;
            if (mPath != -1 && face.Job.GetPathGroup(mProc, mPath) != face.Job.GetPathGroup(face.Process, face.Path))
              continue;
            if (currentlyLoading.Contains(mat.MaterialID)) continue;


            mats.Add(new InProcessMaterial()
            {
              MaterialID = mat.MaterialID,
              JobUnique = face.Job.UniqueStr,
              PartName = face.Job.PartName,
              Process = mProc,
              Path = mPath,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.InQueue,
                CurrentQueue = inputQueue,
                QueuePosition = mat.Position
              },
              Action = new InProcessMaterialAction()
              {
                Type = InProcessMaterialAction.ActionType.Waiting
              }
            });

            if (mats.Count >= face.Job.PartsPerPallet(face.Process, face.Path))
            {
              break;
            }
          }
        }
      }

      // TODO: if not enough, give error and add/allocate more

      return mats;
    }

    private PalletAndMaterial CurrentlyLoadingPallet(PalletStatus pallet, IEnumerable<LogEntry> log, DateTime nowUtc, ref bool palletStateUpdated)
    {
      var faces = GetFaces(pallet);

      if (pallet.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
      {
        // ensure load-begin so that we know the starting time of load
        bool seenLoadBegin =
          log.Any(e =>
            e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.StartOfCycle
          );
        if (!seenLoadBegin)
        {
          palletStateUpdated = true;
          _log.RecordLoadStart(
            mats: new JobLogDB.EventLogMaterial[] { },
            pallet: pallet.Master.PalletNum.ToString(),
            lulNum: pallet.CurStation.Location.Num,
            timeUTC: nowUtc
          );
        }
      }

      // first unloading material
      var unusedMatsOnPal = new Dictionary<long, InProcessMaterial>();
      foreach (var face in faces)
      {

      }

      foreach (var face in faces)
      {
        var loadMat = MaterialToLoadOnFace(pallet, face.Value, allocateNew: false);
        faces[face.Key].Material.AddRange(loadMat);
        foreach (var mat in loadMat)
        {
          mat.Action = new InProcessMaterialAction()
          {
            Type = InProcessMaterialAction.ActionType.Loading,
            LoadOntoPallet = pallet.Master.ToString(),
            LoadOntoFace = face.Key,
            ProcessAfterLoad = face.Value.Process,
            PathAfterLoad = face.Value.Path
          };
        }
        var loadingIds = new HashSet<long>(loadMat.Select(m => m.MaterialID));

        var unloadMat = MaterialCurrentlyOnFace(pallet, face.Value, log, allocateIfMissing: false);
        foreach (var mat in unloadMat)
        {
          if (loadingIds.Contains(mat.MaterialID)) continue; // transfer

          if (face.Value.Process == face.Value.Job.NumProcesses)
          {
            mat.Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
            };
          }
          else
          {
            mat.Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
              UnloadIntoQueue = face.Value.Job.GetOutputQueue(face.Value.Process, face.Value.Path)
            };
          }

          faces[face.Key].Material.Add(mat);
        }
      }

      return new PalletAndMaterial()
      {
        Status = pallet,
        Faces = faces.Values.ToList()
      };
    }

    private PalletAndMaterial CurrentlyUnloadingPallet(PalletStatus pallet, IEnumerable<LogEntry> log, DateTime nowUtc, ref bool palletStateUpdated)
    {
      var faces = GetFaces(pallet);

      if (pallet.CurStation.Location.Location == PalletLocationEnum.LoadUnload)
      {
        // ensure load-begin so that we know the starting time of load
        bool seenLoadBegin =
          log.Any(e =>
            e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.StartOfCycle
          );
        if (!seenLoadBegin)
        {
          palletStateUpdated = true;
          _log.RecordLoadStart(
            mats: new JobLogDB.EventLogMaterial[] { },
            pallet: pallet.Master.PalletNum.ToString(),
            lulNum: pallet.CurStation.Location.Num,
            timeUTC: nowUtc
          );
        }
      }

      // now find the material for the pallet
      foreach (var face in faces)
      {
        var unloadMat = MaterialCurrentlyOnFace(pallet, face.Value, log, allocateIfMissing: false);
        foreach (var mat in unloadMat)
        {
          if (face.Value.Process == face.Value.Job.NumProcesses)
          {
            mat.Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
            };
          }
          else
          {
            mat.Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.UnloadToInProcess,
              UnloadIntoQueue = face.Value.Job.GetOutputQueue(face.Value.Process, face.Value.Path)
            };
          }

          faces[face.Key].Material.Add(mat);
        }
      }

      return new PalletAndMaterial()
      {
        Status = pallet,
        Faces = faces.Values.ToList()
      };
    }

    private PalletAndMaterial LoadedPallet(PalletStatus pallet, IEnumerable<LogEntry> log, DateTime nowUtc, ref bool palletStateUpdated)
    {
      var faces = GetFaces(pallet);

      var loadBegin =
        log.FirstOrDefault(e =>
          e.LogType == LogType.LoadUnloadCycle && e.Result == "LOAD" && e.StartOfCycle
        );
      if (loadBegin != null)
      {
        palletStateUpdated = true;

        // first time seeing loaded-pallet, need to record load and unload end, and create pallet cycle
        foreach (var face in faces)
        {
          var oldMatOnPal = MaterialCurrentlyOnFace(pallet, face.Value, log, allocateIfMissing: false);

          var queues = new Dictionary<long, string>();
          var queue = face.Value.Job.GetOutputQueue(face.Value.Process, face.Value.Path);
          if (!string.IsNullOrEmpty(queue))
          {
            foreach (var mat in oldMatOnPal)
            {
              queues[mat.MaterialID] = queue;
            }
          }

          _log.RecordUnloadEnd(
            mats: oldMatOnPal.Select(m => new JobLogDB.EventLogMaterial() { MaterialID = m.MaterialID, Process = m.Process, Face = face.Key.ToString() }),
            pallet: pallet.Master.PalletNum.ToString(),
            lulNum: loadBegin.LocationNum,
            timeUTC: nowUtc,
            elapsed: nowUtc.Subtract(loadBegin.EndTimeUTC),
            active: face.Value.Job.GetExpectedUnloadTime(face.Value.Process, face.Value.Path),
            unloadIntoQueues: queues
          );

          _log.CompletePalletCycle(pallet.Master.PalletNum.ToString(), nowUtc, foreignID: null);

          var matToLoad = MaterialToLoadOnFace(pallet, face.Value, allocateNew: true);

          _log.RecordLoadEnd(
            mats: matToLoad.Select(m => new JobLogDB.EventLogMaterial() { MaterialID = m.MaterialID, Process = m.Process, Face = face.Key.ToString() }),
            pallet: pallet.Master.PalletNum.ToString(),
            lulNum: loadBegin.LocationNum,
            timeUTC: nowUtc.AddSeconds(1),
            elapsed: nowUtc.Subtract(loadBegin.EndTimeUTC),
            active: face.Value.Job.GetExpectedLoadTime(face.Value.Process, face.Value.Path)
          );

          foreach (var mat in matToLoad)
          {
            mat.Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.OnPallet,
              Pallet = pallet.Master.PalletNum.ToString()
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
        foreach (var face in faces)
        {
          face.Value.Material = MaterialCurrentlyOnFace(pallet, face.Value, log, allocateIfMissing: true);
        }
      }

      return new PalletAndMaterial()
      {
        Status = pallet,
        Faces = faces.Values.ToList()
      };
    }

    private List<JobLogDB.EventLogMaterial> MaterialForProgram(PalletAndMaterial pallet, int program)
    {
      var mats = new List<JobLogDB.EventLogMaterial>();
      var machStop = (MachiningStep)(pallet.Status.Master.Routes[1]);
      foreach (var face in pallet.Faces)
      {
        var faceNum = face.Face;
        if (program == machStop.ProgramNumsToRun[faceNum - 1])
        {
          foreach (var mat in face.Material)
          {
            if (mat.Location.Type == InProcessMaterialLocation.LocType.OnPallet
                && mat.Location.Pallet == pallet.Status.Master.PalletNum.ToString()
                && mat.Process == face.Process)
            {
              mats.Add(new JobLogDB.EventLogMaterial()
              {
                MaterialID = mat.MaterialID,
                Process = mat.Process,
                Face = faceNum.ToString()
              });
            }
          }
        }
      }
      return mats;
    }

    private void EnsureMachineStart(PalletAndMaterial pallet, IEnumerable<LogEntry> log, int program, DateTime nowUtc)
    {
      var mats = MaterialForProgram(pallet, program);
      if (!mats.Any())
      {
        return;
      }
      var matIds = new HashSet<long>(mats.Select(m => m.MaterialID));

      foreach (var e in log)
      {
        if (e.LogType == LogType.MachineCycle && e.StartOfCycle && e.Material.Any(m => matIds.Contains(m.MaterialID)))
        {
          var elapsed = nowUtc.Subtract(e.EndTimeUTC);
          foreach (var face in pallet.Faces)
          {
            var machStop = face.Job.GetMachiningStop(face.Process, face.Path).First();

            foreach (var mat in face.Material)
            {
              if (matIds.Contains(mat.MaterialID))
              {
                mat.Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.Machining,
                  Program = program.ToString(),
                  ElapsedMachiningTime = elapsed,
                  ExpectedRemainingMachiningTime = machStop.ExpectedCycleTime.Subtract(elapsed)
                };
              }
            }
          }
          return;
        }
      }

      Log.Debug("Recording machine start for {@pallet} from logs {@log} and {program} with material {@mats}", pallet, log, program, mats);

      _log.RecordMachineStart(
        mats: mats,
        pallet: pallet.Status.Master.PalletNum.ToString(),
        statName: pallet.Status.CurStation.Location.StationGroup,
        statNum: pallet.Status.CurStation.Location.Num,
        program: program.ToString(),
        timeUTC: nowUtc
      );
    }

    private void EnsureMachineEnd(PalletAndMaterial pallet, IEnumerable<LogEntry> log, int program, DateTime nowUtc, ref bool palletStateUpdated)
    {
      var mats = MaterialForProgram(pallet, program);
      if (!mats.Any())
      {
        Log.Debug("EnsureMachineEnd for program {program} did not find any material {@pallet}", program, pallet);
        return;
      }
      var matIds = new HashSet<long>(mats.Select(m => m.MaterialID));
      DateTime? startUtc = null;

      foreach (var e in log)
      {
        if (e.LogType == LogType.MachineCycle && !e.StartOfCycle && e.Material.Any(m => matIds.Contains(m.MaterialID)))
        {
          return;
        }
        if (e.LogType == LogType.MachineCycle && e.StartOfCycle && e.Material.Any(m => matIds.Contains(m.MaterialID)))
        {
          startUtc = e.EndTimeUTC;
        }
      }
      if (startUtc == null)
      {
        Log.Error("EnsureMachineEnd did not find machine start! {@pallet} {@logs} {program}", pallet, log, program);
      }

      // a single face can have only one (job, proc, path) combo
      var mat = mats.First();
      var matDetails = _log.GetMaterialDetails(mat.MaterialID);
      var path = matDetails.Paths.ContainsKey(mat.Process) ? matDetails.Paths[mat.Process] : 1;
      var job = _jobs.LoadJob(matDetails.JobUnique);
      TimeSpan active = TimeSpan.Zero;
      if (job != null)
      {
        var stop = job.GetMachiningStop(mat.Process, path).First();
        active = stop.ExpectedCycleTime;
      }
      else
      {
        Log.Error("Unable to find job for material {mat}", mat);
      }

      Log.Debug("Recording machine end for {@pallet} from logs {@log} and {program} with material {@mats}", pallet, log, program, mats);

      palletStateUpdated = true;

      _log.RecordMachineEnd(
        mats: mats,
        pallet: pallet.Status.Master.PalletNum.ToString(),
        statName: pallet.Status.CurStation.Location.StationGroup,
        statNum: pallet.Status.CurStation.Location.Num,
        program: program.ToString(),
        result: "",
        timeUTC: nowUtc,
        elapsed: startUtc == null ? active : nowUtc.Subtract(startUtc.Value),
        active: active
      );
    }

    private void EnsureAllMachineEnds(PalletAndMaterial pallet, IEnumerable<LogEntry> log, DateTime nowUtc, ref bool palletStateUpdated)
    {
      var machStop = (MachiningStep)(pallet.Status.Master.Routes[1]);
      foreach (var prog in machStop.ProgramNumsToRun)
      {
        EnsureMachineEnd(pallet, log, prog, nowUtc, ref palletStateUpdated);
      }
    }

    private (int proc, int path) ProcessAndPathForMatID(long matID, JobPlan job)
    {
      var log = _log.GetLogForMaterial(matID);
      var proc = log
        .SelectMany(m => m.Material)
        .Where(m => m.MaterialID == matID)
        .Max(m => m.Process);

      var details = _log.GetMaterialDetails(matID);
      if (details.Paths != null && details.Paths.ContainsKey(proc))
      {
        return (proc, details.Paths[proc]);
      }
      else
      {
        return (proc, -1);
      }
    }
  }
}