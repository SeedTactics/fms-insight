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
using System.Linq;
using System.Collections.Generic;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace MazakMachineInterface
{
  public class BuildCurrentStatus
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<BuildCurrentStatus>();

    public static CurrentStatus Build(JobDB jobDB, JobLogDB log, FMSSettings fmsSettings, IMachineGroupName machineGroupName, IQueueSyncFault queueSyncFault, MazakDbType dbType, MazakAllData mazakData, DateTime utcNow)
    {
      //Load process and path numbers
      Dictionary<string, int> uniqueToMaxPath;
      Dictionary<string, int> uniqueToMaxProcess;
      CalculateMaxProcAndPath(mazakData, out uniqueToMaxPath, out uniqueToMaxProcess);

      var currentLoads = new List<LoadAction>(mazakData.LoadActions);

      var curStatus = new CurrentStatus();
      foreach (var k in fmsSettings.Queues) curStatus.QueueSizes[k.Key] = k.Value;
      if (mazakData.Alarms != null)
      {
        foreach (var alarm in mazakData.Alarms)
        {
          if (!string.IsNullOrEmpty(alarm.AlarmMessage))
          {
            curStatus.Alarms.Add(alarm.AlarmMessage);
          }
        }
      }
      if (queueSyncFault.CurrentQueueMismatch)
      {
        curStatus.Alarms.Add("Queue contents and Mazak schedule quantity mismatch.");
      }

      var jobsBySchID = new Dictionary<long, InProcessJob>();
      var pathBySchID = new Dictionary<long, MazakPart.IProcToPath>();

      foreach (var schRow in mazakData.Schedules)
      {
        if (!MazakPart.IsSailPart(schRow.PartName))
          continue;

        MazakPartRow partRow = null;
        foreach (var p in mazakData.Parts)
        {
          if (p.PartName == schRow.PartName)
          {
            partRow = p;
            break;
          }
        }
        if (partRow == null)
          continue;

        //Parse data from the database
        var partName = partRow.PartName;
        int loc = partName.IndexOf(':');
        if (loc >= 0) partName = partName.Substring(0, loc);
        string jobUnique = "";
        MazakPart.IProcToPath procToPath = null;
        bool manual = false;
        if (!string.IsNullOrEmpty(partRow.Comment))
          MazakPart.ParseComment(partRow.Comment, out jobUnique, out procToPath, out manual);

        if (!uniqueToMaxProcess.ContainsKey(jobUnique))
          continue;

        int numProc = uniqueToMaxProcess[jobUnique];
        int maxProc1Path = uniqueToMaxPath[jobUnique];

        InProcessJob job;

        //Create or lookup the job
        if (curStatus.Jobs.ContainsKey(jobUnique))
        {
          job = curStatus.Jobs[jobUnique];
        }
        else
        {
          var jobPaths = new int[numProc];
          for (int i = 0; i < numProc; i++)
            jobPaths[i] = maxProc1Path;
          job = new InProcessJob(jobUnique, numProc, jobPaths);
          job.PartName = partName;
          job.JobCopiedToSystem = true;
          curStatus.Jobs.Add(jobUnique, job);
        }
        jobsBySchID.Add(schRow.Id, job);
        pathBySchID.Add(schRow.Id, procToPath);

        //Job Basics
        job.SetPlannedCyclesOnFirstProcess(procToPath.PathForProc(proc: 1), schRow.PlanQuantity);
        AddCompletedToJob(schRow, job, procToPath);
        job.Priority = schRow.Priority;
        if (((HoldPattern.HoldMode)schRow.HoldMode) == HoldPattern.HoldMode.FullHold)
          job.HoldEntireJob.UserHold = true;
        else
          job.HoldEntireJob.UserHold = false;

        AddRoutingToJob(mazakData, partRow, job, machineGroupName, procToPath, dbType);
      }

      var loadedJobs = new HashSet<string>();
      foreach (var j in jobsBySchID.Values)
      {
        if (loadedJobs.Contains(j.UniqueStr)) continue;
        loadedJobs.Add(j.UniqueStr);
        AddDataFromJobDB(jobDB, j);
      }

      //Now add pallets

      foreach (var palRow in mazakData.Pallets)
      {
        if (palRow.PalletNumber > 0 && !curStatus.Pallets.ContainsKey(palRow.PalletNumber.ToString()))
        {

          var palName = palRow.PalletNumber.ToString();
          var palLoc = FindPalletLocation(machineGroupName, mazakData, dbType, palRow.PalletNumber);

          //Create the pallet
          PalletStatus status = new PalletStatus()
          {
            Pallet = palName,
            CurrentPalletLocation = palLoc,
            FixtureOnPallet = palRow.Fixture,
            NumFaces = 1,
            OnHold = false
          };
          curStatus.Pallets.Add(status.Pallet, status);

          var oldCycles = log.CurrentPalletLog(palName);

          //Add the material currently on the pallet
          foreach (var palSub in mazakData.PalletSubStatuses)
          {
            if (palSub.PalletNumber != palRow.PalletNumber)
              continue;
            if (palSub.FixQuantity <= 0)
              continue;
            if (!jobsBySchID.ContainsKey(palSub.ScheduleID))
              continue;

            status.NumFaces = Math.Max(status.NumFaces, palSub.PartProcessNumber);

            var job = jobsBySchID[palSub.ScheduleID];
            var procToPath = pathBySchID[palSub.ScheduleID];

            var matIDs = new Queue<long>(FindMatIDsFromOldCycles(oldCycles, job.UniqueStr, palSub.PartProcessNumber));

            for (int i = 1; i <= palSub.FixQuantity; i++)
            {
              int face = palSub.PartProcessNumber;
              long matID = -1;
              if (matIDs.Count > 0)
                matID = matIDs.Dequeue();

              var matDetails = log.GetMaterialDetails(matID);
              var inProcMat = new InProcessMaterial()
              {
                MaterialID = matID,
                JobUnique = job.UniqueStr,
                PartName = job.PartName,
                Process = palSub.PartProcessNumber,
                Path = procToPath.PathForProc(palSub.PartProcessNumber),
                Serial = matDetails?.Serial,
                WorkorderId = matDetails?.Workorder,
                SignaledInspections =
                      log.LookupInspectionDecisions(matID)
                      .Where(x => x.Inspect)
                      .Select(x => x.InspType)
                      .Distinct()
                      .ToList(),
                LastCompletedMachiningRouteStopIndex =
                  oldCycles.Any(
                      c => c.LogType == LogType.MachineCycle
                      && !c.StartOfCycle
                      && c.Material.Any(m => m.MaterialID == matID && m.Process == palSub.PartProcessNumber)
                    )
                    ? (int?)0
                    : null,
                Location = new InProcessMaterialLocation()
                {
                  Type = InProcessMaterialLocation.LocType.OnPallet,
                  Pallet = status.Pallet,
                  Face = face
                },
                Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.Waiting
                }
              };
              curStatus.Material.Add(inProcMat);

              //check for unloading or transfer
              var loadNext = CheckLoadOfNextProcess(currentLoads, job.UniqueStr, palSub.PartProcessNumber, palLoc);
              var unload = CheckUnload(currentLoads, job.UniqueStr, palSub.PartProcessNumber, palLoc);

              if (loadNext != null)
              {
                var start = FindLoadStartFromOldCycles(oldCycles, matID);
                inProcMat.Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.Loading,
                  LoadOntoFace = palSub.PartProcessNumber + 1,
                  LoadOntoPallet = status.Pallet,
                  ProcessAfterLoad = palSub.PartProcessNumber + 1,
                  PathAfterLoad = procToPath.PathForProc(palSub.PartProcessNumber + 1),
                  ElapsedLoadUnloadTime = start != null ? (TimeSpan?)utcNow.Subtract(start.EndTimeUTC) : null
                };
              }
              else if (unload != null)
              {
                var start = FindLoadStartFromOldCycles(oldCycles, matID);
                inProcMat.Action = new InProcessMaterialAction()
                {
                  Type =
                        palSub.PartProcessNumber == job.NumProcesses
                            ? InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
                            : InProcessMaterialAction.ActionType.UnloadToInProcess,
                  UnloadIntoQueue = job.GetOutputQueue(
                    process: palSub.PartProcessNumber,
                    path: procToPath.PathForProc(palSub.PartProcessNumber)),
                  ElapsedLoadUnloadTime = start != null ? (TimeSpan?)utcNow.Subtract(start.EndTimeUTC) : null
                };
              }
              else
              {
                // detect if machining
                var start = FindMachineStartFromOldCycles(oldCycles, matID);
                if (start != null)
                {
                  var machStop = job.GetMachiningStop(inProcMat.Process, inProcMat.Path).FirstOrDefault();
                  var elapsedTime = utcNow.Subtract(start.EndTimeUTC);
                  inProcMat.Action = new InProcessMaterialAction()
                  {
                    Type = InProcessMaterialAction.ActionType.Machining,
                    ElapsedMachiningTime = elapsedTime,
                    ExpectedRemainingMachiningTime =
                      machStop != null ? machStop.ExpectedCycleTime.Subtract(elapsedTime) : TimeSpan.Zero
                  };
                }
              }
            }
          }

          if (palLoc.Location == PalletLocationEnum.LoadUnload)
          {
            var start = FindLoadStartFromOldCycles(oldCycles);
            var elapsedLoad = start != null ? (TimeSpan?)utcNow.Subtract(start.EndTimeUTC) : null;
            AddLoads(log, currentLoads, status.Pallet, palLoc, elapsedLoad, curStatus);
            AddUnloads(log, currentLoads, status, elapsedLoad, oldCycles, curStatus);
          }
        }
      }

      //now queued
      var seenMatIds = new HashSet<long>(curStatus.Material.Select(m => m.MaterialID));
      foreach (var mat in log.GetMaterialInAllQueues())
      {
        // material could be in the process of being loaded
        if (seenMatIds.Contains(mat.MaterialID)) continue;
        var matLogs = log.GetLogForMaterial(mat.MaterialID);
        int lastProc = 0;
        foreach (var entry in log.GetLogForMaterial(mat.MaterialID))
        {
          foreach (var entryMat in entry.Material)
          {
            if (entryMat.MaterialID == mat.MaterialID)
            {
              lastProc = Math.Max(lastProc, entryMat.Process);
            }
          }
        }
        var matDetails = log.GetMaterialDetails(mat.MaterialID);
        curStatus.Material.Add(new InProcessMaterial()
        {
          MaterialID = mat.MaterialID,
          JobUnique = mat.Unique,
          PartName = mat.PartNameOrCasting,
          Process = lastProc,
          Path = 1,
          Serial = matDetails?.Serial,
          WorkorderId = matDetails?.Workorder,
          SignaledInspections =
                log.LookupInspectionDecisions(mat.MaterialID)
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


      var notCopied = jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddHours(-WriteJobs.JobLookbackHours), DateTime.UtcNow);
      foreach (var j in notCopied.Jobs)
      {
        if (curStatus.Jobs.ContainsKey(j.UniqueStr))
        {
          //The copy to the cell succeeded but the DB has not yet been updated.
          //The thread which copies jobs will soon notice and update the database
          //so we can ignore it for now.
        }
        else
        {
          curStatus.Jobs.Add(j.UniqueStr, new InProcessJob(j));
        }
      }

      foreach (var j in curStatus.Jobs)
      {
        foreach (var d in jobDB.LoadDecrementsForJob(j.Value.UniqueStr))
          j.Value.Decrements.Add(d);
      }

      return curStatus;
    }

    private static void CalculateMaxProcAndPath(MazakSchedulesPartsPallets mazakData,
                                               out Dictionary<string, int> uniqueToMaxProc1Path,
                                               out Dictionary<string, int> uniqueToMaxProcess)
    {
      uniqueToMaxProc1Path = new Dictionary<string, int>();
      uniqueToMaxProcess = new Dictionary<string, int>();
      foreach (var partRow in mazakData.Parts)
      {
        if (MazakPart.IsSailPart(partRow.PartName) && !string.IsNullOrEmpty(partRow.Comment))
        {
          string jobUnique = "";
          bool manual = false;
          int numProc = partRow.Processes.Count;

          MazakPart.ParseComment(partRow.Comment, out jobUnique, out var paths, out manual);

          if (uniqueToMaxProc1Path.ContainsKey(jobUnique))
          {
            uniqueToMaxProc1Path[jobUnique] = Math.Max(uniqueToMaxProc1Path[jobUnique], paths.PathForProc(proc: 1));
          }
          else
          {
            uniqueToMaxProc1Path.Add(jobUnique, paths.PathForProc(proc: 1));
          }

          if (uniqueToMaxProcess.ContainsKey(jobUnique))
          {
            if (numProc != uniqueToMaxProcess[jobUnique])
            {
              Log.Warning("Paths for {uniq} have a different number of processes", jobUnique);
            }
          }
          else
          {
            uniqueToMaxProcess.Add(jobUnique, numProc);
          }
        }
      }
    }

    private static void AddRoutingToJob(MazakSchedulesPartsPallets mazakData, MazakPartRow partRow, JobPlan job, IMachineGroupName machineGroupName, MazakPart.IProcToPath procToPath, MazakDbType mazakTy)
    {
      //Add routing and pallets
      foreach (var partProcRow in partRow.Processes)
      {
        var path = procToPath.PathForProc(partProcRow.ProcessNumber);
        job.SetPartsPerPallet(partProcRow.ProcessNumber, path, partProcRow.FixQuantity);
        job.SetPathGroup(partProcRow.ProcessNumber, path, path);
        job.SetHoldMachining(partProcRow.ProcessNumber, path, job.HoldMachining(partProcRow.ProcessNumber, path));
        job.SetHoldLoadUnload(partProcRow.ProcessNumber, path, job.HoldLoadUnload(partProcRow.ProcessNumber, path));

        //Routing
        string fixStr = partProcRow.FixLDS;
        string cutStr = partProcRow.CutMc;
        string removeStr = partProcRow.RemoveLDS;

        if (mazakTy != MazakDbType.MazakVersionE)
        {
          fixStr = ConvertStatIntV2ToV1(Convert.ToInt32(fixStr));
          cutStr = ConvertStatIntV2ToV1(Convert.ToInt32(cutStr));
          removeStr = ConvertStatIntV2ToV1(Convert.ToInt32(removeStr));
        }

        foreach (char c in fixStr)
          if (c != '0')
            job.AddLoadStation(partProcRow.ProcessNumber, path, int.Parse(c.ToString()));
        foreach (char c in removeStr)
          if (c != '0')
            job.AddUnloadStation(partProcRow.ProcessNumber, path, int.Parse(c.ToString()));

        JobMachiningStop routeStop = null;
        foreach (char c in cutStr)
        {
          if (c != '0')
          {
            if (routeStop == null)
            {
              routeStop = new JobMachiningStop(machineGroupName.MachineGroupName);
              job.AddMachiningStop(partProcRow.ProcessNumber, path, routeStop);
            }
            routeStop.Stations.Add(int.Parse(c.ToString()));
          }
        }

        if (routeStop != null)
        {
          routeStop.ProgramName = partProcRow.MainProgram;
        }

        //Planned Pallets
        foreach (var palRow in mazakData.Pallets)
        {
          if (palRow.PalletNumber > 0
              && palRow.Fixture == partProcRow.Fixture
              && !job.HasPallet(partProcRow.ProcessNumber, path, palRow.PalletNumber.ToString()))
          {

            job.AddProcessOnPallet(partProcRow.ProcessNumber, path, palRow.PalletNumber.ToString());
          }
        }
      }
    }

    private static void AddCompletedToJob(MazakScheduleRow schRow, InProcessJob job, MazakPart.IProcToPath procToPath)
    {
      job.SetCompleted(job.NumProcesses, procToPath.PathForProc(job.NumProcesses), schRow.CompleteQuantity);

      //in-proc and material for each process
      var counts = new Dictionary<int, int>(); //key is process, value is in-proc + mat
      foreach (var schProcRow in schRow.Processes)
      {
        counts[schProcRow.ProcessNumber] =
          schProcRow.ProcessBadQuantity + schProcRow.ProcessExecuteQuantity + schProcRow.ProcessMaterialQuantity;
      }

      for (int proc = 1; proc < job.NumProcesses; proc++)
      {
        var cnt =
          counts
          .Where(x => x.Key > proc)
          .Select(x => x.Value)
          .Sum();
        job.SetCompleted(proc, procToPath.PathForProc(proc), cnt + schRow.CompleteQuantity);
      }
    }

    private static void AddDataFromJobDB(JobDB jobDB, JobPlan jobFromMazak)
    {
      var jobFromDb = jobDB.LoadJob(jobFromMazak.UniqueStr);
      if (jobFromDb == null) return;

      jobFromMazak.RouteStartingTimeUTC = jobFromDb.RouteStartingTimeUTC;
      jobFromMazak.RouteEndingTimeUTC = jobFromDb.RouteEndingTimeUTC;
      jobFromMazak.ScheduleId = jobFromDb.ScheduleId;
      jobFromMazak.HoldEntireJob = jobFromDb.HoldEntireJob;
      foreach (var b in jobFromDb.ScheduledBookingIds)
        jobFromMazak.ScheduledBookingIds.Add(b);
      for (int proc = 1; proc <= jobFromMazak.NumProcesses; proc++)
      {
        for (int path = 1; path <= jobFromMazak.GetNumPaths(proc); path++)
        {
          if (proc > jobFromDb.NumProcesses || path > jobFromDb.GetNumPaths(proc))
            continue;

          foreach (var insp in jobFromDb.PathInspections(proc, path))
            jobFromMazak.PathInspections(proc, path).Add(insp);
          jobFromMazak.SetSimulatedStartingTimeUTC(proc, path,
            jobFromDb.GetSimulatedStartingTimeUTC(proc, path));
          jobFromMazak.SetSimulatedAverageFlowTime(proc, path,
            jobFromDb.GetSimulatedAverageFlowTime(proc, path));
          jobFromMazak.SetSimulatedProduction(proc, path,
            jobFromDb.GetSimulatedProduction(proc, path));
          jobFromMazak.SetExpectedLoadTime(proc, path,
            jobFromDb.GetExpectedLoadTime(proc, path));
          jobFromMazak.SetExpectedUnloadTime(proc, path,
            jobFromDb.GetExpectedUnloadTime(proc, path));
          jobFromMazak.SetInputQueue(proc, path,
            jobFromDb.GetInputQueue(proc, path));
          jobFromMazak.SetOutputQueue(proc, path,
            jobFromDb.GetOutputQueue(proc, path));
          jobFromMazak.SetPathGroup(proc, path,
            jobFromDb.GetPathGroup(proc, path)
          );

          var mazakStops = jobFromMazak.GetMachiningStop(proc, path).ToList();
          var dbStops = jobFromDb.GetMachiningStop(proc, path).ToList();
          for (int i = 0; i < Math.Min(mazakStops.Count, dbStops.Count); i++)
          {
            mazakStops[i].StationGroup = dbStops[i].StationGroup;
            mazakStops[i].ExpectedCycleTime = dbStops[i].ExpectedCycleTime;
          }
        }
      }

    }

    private static LoadAction CheckLoadOfNextProcess(List<LoadAction> currentLoads, string unique, int process, PalletLocation loc)
    {
      if (loc.Location != PalletLocationEnum.LoadUnload)
        return null;
      foreach (var act in currentLoads)
      {
        if (act.LoadEvent == true
            && loc.Num == act.LoadStation
            && unique == act.Unique
            && process + 1 == act.Process
            && act.Qty >= 1)
        {
          if (act.Qty == 1)
          {
            currentLoads.Remove(act);
          }
          else
          {
            act.Qty -= 1;
          }
          return act;
        }
      }
      return null;
    }

    private static LoadAction CheckUnload(List<LoadAction> currentLoads, string unique, int process, PalletLocation loc)
    {
      if (loc.Location != PalletLocationEnum.LoadUnload)
        return null;
      foreach (var act in currentLoads)
      {
        if (act.LoadEvent == false
            && loc.Num == act.LoadStation
            && unique == act.Unique
            && process == act.Process
            && act.Qty >= 1)
        {
          if (act.Qty == 1)
          {
            currentLoads.Remove(act);
          }
          else
          {
            act.Qty -= 1;
          }
          return act;
        }
      }
      return null;
    }

    private static void AddLoads(JobLogDB log, IEnumerable<LoadAction> currentLoads, string pallet, PalletLocation palLoc, TimeSpan? elapsedLoadTime, CurrentStatus curStatus)
    {
      var queuedMats = new Dictionary<(string uniq, int proc, int path), List<BlackMaple.MachineFramework.JobLogDB.QueuedMaterial>>();
      //process remaining loads/unloads (already processed ones have been removed from currentLoads)
      foreach (var operation in currentLoads)
      {
        if (!operation.LoadEvent || operation.LoadStation != palLoc.Num) continue;
        for (int i = 0; i < operation.Qty; i++)
        {

          // first, calculate material id, serial, workorder, and location
          long matId = -1;
          string serial = null;
          string workId = null;
          var loc = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.Free
          };

          // load queued material
          List<BlackMaple.MachineFramework.JobLogDB.QueuedMaterial> queuedMat = null;
          if (curStatus.Jobs.ContainsKey(operation.Unique))
          {
            var job = curStatus.Jobs[operation.Unique];
            var queue = job.GetInputQueue(process: operation.Process, path: operation.Path);
            if (!string.IsNullOrEmpty(queue))
            {
              var key = (uniq: operation.Unique, proc: operation.Process, path: operation.Path);
              if (queuedMats.ContainsKey(key))
                queuedMat = queuedMats[key];
              else
              {
                queuedMat = MazakQueues.QueuedMaterialForLoading(job, log.GetMaterialInQueue(queue), operation.Process, operation.Path, log);
                queuedMats.Add(key, queuedMat);
              }
            }
          }

          // extract matid and details from queued material if available
          if (queuedMat != null && queuedMat.Count > 0)
          {
            var mat = queuedMat[0];
            queuedMat.RemoveAt(0);
            matId = mat.MaterialID;
            loc = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.InQueue,
              CurrentQueue = mat.Queue,
              QueuePosition = mat.Position,
            };
            var matDetails = log.GetMaterialDetails(matId);
            serial = matDetails?.Serial;
            workId = matDetails?.Workorder;
          }

          var inProcMat = new InProcessMaterial()
          {
            MaterialID = matId,
            JobUnique = operation.Unique,
            PartName = operation.Part,
            Process = operation.Process,
            Path = operation.Path,
            Serial = serial,
            WorkorderId = workId,
            Location = loc,
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Loading,
              LoadOntoPallet = pallet,
              LoadOntoFace = operation.Process,
              ProcessAfterLoad = operation.Process,
              PathAfterLoad = operation.Path,
              ElapsedLoadUnloadTime = elapsedLoadTime
            }
          };
          curStatus.Material.Add(inProcMat);
        }
      }
    }

    private static void AddUnloads(JobLogDB log, IEnumerable<LoadAction> currentActions, PalletStatus pallet, TimeSpan? elapsedLoadTime, List<BlackMaple.MachineWatchInterface.LogEntry> oldCycles, CurrentStatus status)
    {
      // For some reason, sometimes parts to unload don't show up in PalletSubStatus table.
      // So create them here if that happens

      foreach (var unload in currentActions)
      {
        if (unload.LoadEvent)
          continue;
        if (unload.LoadStation != pallet.CurrentPalletLocation.Num)
          continue;

        var matIDs = new Queue<long>(FindMatIDsFromOldCycles(oldCycles, unload.Unique, unload.Process));
        status.Jobs.TryGetValue(unload.Unique, out InProcessJob job);

        for (int i = 0; i < unload.Qty; i += 1)
        {
          string face = unload.Process.ToString();
          long matID = -1;
          if (matIDs.Count > 0)
            matID = matIDs.Dequeue();

          var matDetails = log.GetMaterialDetails(matID);
          var inProcMat = new InProcessMaterial()
          {
            MaterialID = matID,
            JobUnique = unload.Unique,
            PartName = unload.Part,
            Process = unload.Process,
            Path = unload.Path,
            Serial = matDetails?.Serial,
            WorkorderId = matDetails?.Workorder,
            SignaledInspections =
                          log.LookupInspectionDecisions(matID)
                          .Where(x => x.Inspect)
                          .Select(x => x.InspType)
                          .ToList(),
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.OnPallet,
              Pallet = pallet.Pallet,
              Face = unload.Process
            },
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
              ElapsedLoadUnloadTime = elapsedLoadTime
            }
          };
          if (job != null)
          {
            if (unload.Process == job.NumProcesses)
              inProcMat.Action.Type = InProcessMaterialAction.ActionType.UnloadToInProcess;
            var queue = job.GetOutputQueue(process: unload.Process, path: unload.Path);
            if (!string.IsNullOrEmpty(queue))
            {
              inProcMat.Action.UnloadIntoQueue = queue;
            }
          }
          status.Material.Add(inProcMat);
        }
      }
    }

    private static IEnumerable<long> FindMatIDsFromOldCycles(IEnumerable<BlackMaple.MachineWatchInterface.LogEntry> oldCycles, string unique, int proc)
    {
      var ret = new Dictionary<long, bool>();

      foreach (var s in oldCycles)
      {
        foreach (LogMaterial mat in s.Material)
        {
          if (mat.MaterialID >= 0 && mat.JobUniqueStr == unique && mat.Process == proc)
          {
            ret[mat.MaterialID] = true;
          }
        }
      }

      return ret.Keys;
    }

    private static BlackMaple.MachineWatchInterface.LogEntry FindMachineStartFromOldCycles(IEnumerable<BlackMaple.MachineWatchInterface.LogEntry> oldCycles, long matId)
    {
      BlackMaple.MachineWatchInterface.LogEntry start = null;

      foreach (var s in oldCycles.Where(e => e.Material.Any(m => m.MaterialID == matId)))
      {
        if (s.LogType == LogType.MachineCycle)
        {
          if (s.StartOfCycle)
          {
            start = s;
          }
          else
          {
            start = null; // clear when completed
          }
        }
      }

      return start;
    }

    private static BlackMaple.MachineWatchInterface.LogEntry FindLoadStartFromOldCycles(IEnumerable<BlackMaple.MachineWatchInterface.LogEntry> oldCycles, long? matId = null)
    {
      BlackMaple.MachineWatchInterface.LogEntry start = null;

      var cyclesToSearch =
        matId != null
          ? oldCycles.Where(e => e.Material.Any(m => m.MaterialID == matId))
          : oldCycles;

      foreach (var s in cyclesToSearch)
      {
        if (s.LogType == LogType.LoadUnloadCycle)
        {
          if (s.StartOfCycle)
          {
            start = s;
          }
          else
          {
            start = null; // clear when completed
          }
        }
      }

      return start;
    }

    private static PalletLocation FindPalletLocation(IMachineGroupName machName, MazakSchedulesPartsPallets mazakData, MazakDbType dbType, int palletNum)
    {
      foreach (var palLocRow in mazakData.PalletPositions)
      {
        if (palLocRow.PalletNumber == palletNum)
        {
          if (dbType != MazakDbType.MazakVersionE)
          {
            return ParseStatNameWeb(machName, palLocRow.PalletPosition);
          }
          else
          {
            return ParseStatNameVerE(machName, palLocRow.PalletPosition);
          }
        }
      }

      return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 0);
    }

    private static PalletLocation ParseStatNameVerE(IMachineGroupName machName, string pos)
    {
      PalletLocation ret = new PalletLocation();
      ret.Location = PalletLocationEnum.Buffer;
      ret.StationGroup = "Unknown";
      ret.Num = 0;

      if (pos.StartsWith("LS"))
      {
        //load station
        ret.Location = PalletLocationEnum.LoadUnload;
        ret.StationGroup = "L/U";
        ret.Num = Convert.ToInt32(pos.Substring(3));
      }

      if (pos.StartsWith("M"))
      {
        //M23 means machine 2, on the table (M21 is is the input pos, M22 is the output pos)

        if (pos[2] == '3')
        {
          ret.Location = PalletLocationEnum.Machine;
        }
        else
        {
          ret.Location = PalletLocationEnum.MachineQueue;
        }
        ret.StationGroup = machName.MachineGroupName;
        ret.Num = Convert.ToInt32(pos[1].ToString());
      }

      if (pos.StartsWith("S"))
      {
        if (pos == "STA")
        {
          ret.Location = PalletLocationEnum.Cart;
          ret.StationGroup = "Cart";
          ret.Num = 1;
        }
        else
        {
          ret.Location = PalletLocationEnum.Buffer;
          ret.StationGroup = "Buffer";
          ret.Num = Convert.ToInt32(pos.Substring(1));
        }
      }

      return ret;

    }

    private static PalletLocation ParseStatNameWeb(IMachineGroupName machName, string pos)
    {
      PalletLocation ret = new PalletLocation();
      ret.Location = PalletLocationEnum.Buffer;
      ret.StationGroup = "Unknown";
      ret.Num = 0;

      if (pos.StartsWith("LS"))
      {
        //load station
        ret.Location = PalletLocationEnum.LoadUnload;
        ret.StationGroup = "L/U";
        ret.Num = Convert.ToInt32(pos.Substring(3, 1));
      }

      if (pos.StartsWith("M"))
      {
        //M023 means machine 2, on the table (M021 is is the input pos, M022 is the output pos)

        if (pos[3] == '3')
        {
          ret.Location = PalletLocationEnum.Machine;
        }
        else
        {
          ret.Location = PalletLocationEnum.MachineQueue;
        }
        ret.StationGroup = machName.MachineGroupName;
        ret.Num = Convert.ToInt32(pos[2].ToString());
      }

      if (pos.StartsWith("S"))
      {
        if (pos == "STA")
        {
          ret.Location = PalletLocationEnum.Cart;
          ret.StationGroup = "Cart";
          ret.Num = 1;
        }
        else
        {
          ret.Location = PalletLocationEnum.Buffer;
          ret.StationGroup = "Buffer";
          ret.Num = Convert.ToInt32(pos.Substring(1));
        }
      }

      return ret;
    }

    private static string ConvertStatIntV2ToV1(int statNum)
    {
      char[] ret = {
        '0',
        '0',
        '0',
        '0',
        '0',
        '0',
        '0',
        '0',
        '0',
        '0'
      };

      for (int i = 0; i <= ret.Length - 1; i++)
      {
        if ((statNum & (1 << i)) != 0)
        {
          ret[i] = (i + 1).ToString()[0];
        }
      }

      return new string(ret);
    }
  }

}