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

namespace MazakMachineInterface
{
  public static class BuildCurrentStatus
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<CurrentStatus>();

    private record CurrentJob
    {
      public string UniqueStr { get; init; }
      public string PartName { get; init; }
      public HistoricJob DbJob { get; init; }
      public int Cycles { get; set; }
      public long Started { get; set; }
      public IReadOnlyList<ImmutableList<int>.Builder> Completed { get; init; }
      public IReadOnlyList<ImmutableList<ProcPathInfo>.Builder> Processes { get; init; }
      public IReadOnlyList<ImmutableList<long>.Builder> Precedence { get; init; }
      public bool UserHold { get; set; }

      public ProcPathInfo DbProcPath(int proc, int path)
      {
        if (DbJob == null)
          return null;
        if (proc >= 1 && proc <= DbJob.Processes.Count)
        {
          if (path >= 1 && path <= DbJob.Processes[proc - 1].Paths.Count)
          {
            return DbJob.Processes[proc - 1].Paths[path - 1];
          }
        }
        return null;
      }
    }

    public static string FindMachineGroupName(IRepository db)
    {
      return db.StationGroupsOnMostRecentSchedule().FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? "MC";
    }

    public static CurrentStatus Build(
      IRepository jobDB,
      FMSSettings fmsSettings,
      MazakConfig mazakCfg,
      MazakAllData mazakData,
      string machineGroupName,
      int? palletWithUnprocessedUnloads,
      DateTime utcNow
    )
    {
      //Load process and path numbers
      CalculateMaxProcAndPath(mazakData, jobDB, out var uniqueToMaxProcess, out var partNameToNumProc);

      var currentLoads = new List<LoadAction>(mazakData.LoadActions);

      var jobUniqBySchID = new Dictionary<long, string>();
      var jobsByUniq = new Dictionary<string, CurrentJob>();

      long precedence = 0;
      foreach (var schRow in mazakData.Schedules.OrderBy(s => s.DueDate).ThenBy(s => s.Priority))
      {
        precedence += 1;

        if (!MazakPart.IsSailPart(schRow.PartName, schRow.Comment))
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
        if (loc >= 0)
          partName = partName.Substring(0, loc);
        string jobUnique = "";
        if (!string.IsNullOrEmpty(partRow.Comment))
          jobUnique = MazakPart.UniqueFromComment(partRow.Comment);

        if (!uniqueToMaxProcess.ContainsKey(jobUnique))
          continue;

        int numProc = uniqueToMaxProcess[jobUnique];

        //Create or lookup the job
        CurrentJob job;
        if (!jobsByUniq.TryGetValue(jobUnique, out job))
        {
          job = new CurrentJob()
          {
            UniqueStr = jobUnique,
            PartName = partName,
            Cycles = 0,
            DbJob = jobDB.LoadJob(jobUnique),
            Processes = Enumerable
              .Range(1, numProc)
              .Select(_ =>
              {
                var b = ImmutableList.CreateBuilder<ProcPathInfo>();
                b.Add(null);
                return b;
              })
              .ToList(),
            Completed = Enumerable
              .Range(1, numProc)
              .Select(_ =>
              {
                var b = ImmutableList.CreateBuilder<int>();
                b.Add(0);
                return b;
              })
              .ToList(),
            Precedence = Enumerable
              .Range(1, numProc)
              .Select(_ =>
              {
                var b = ImmutableList.CreateBuilder<long>();
                b.Add(0);
                return b;
              })
              .ToList(),
          };
          jobsByUniq.Add(jobUnique, job);
        }
        jobUniqBySchID.Add(schRow.Id, jobUnique);

        //Job Basics
        job.Cycles += schRow.PlanQuantity;
        AddCompletedToJob(schRow, job);
        AddMachiningOrCompletedToStarted(schRow, job);
        if (((HoldPattern.HoldMode)schRow.HoldMode) == HoldPattern.HoldMode.FullHold)
          job.UserHold = true;
        else
          job.UserHold = false;

        AddRoutingToJob(mazakData, partRow, job, machineGroupName, mazakCfg, precedence);
      }

      //Now add pallets
      var palletsByName = ImmutableDictionary.CreateBuilder<int, PalletStatus>();
      var material = ImmutableList.CreateBuilder<InProcessMaterial>();
      var palOnHold = mazakData
        .PalletStatuses?.GroupBy(p => p.PalletNumber)
        .ToDictionary(g => mazakCfg.TranslatePalletNumber(g.Key), g => g.Any(p => p.IsOnHold > 0));
      foreach (var palRow in mazakData.Pallets)
      {
        if (
          palRow.PalletNumber > 0
          && !palletsByName.ContainsKey(mazakCfg.TranslatePalletNumber(palRow.PalletNumber))
        )
        {
          var palName = mazakCfg.TranslatePalletNumber(palRow.PalletNumber);
          var palLoc = FindPalletLocation(machineGroupName, mazakData, mazakCfg, palRow.PalletNumber);

          //Create the pallet
          palletsByName.Add(
            palName,
            new PalletStatus()
            {
              PalletNum = palName,
              CurrentPalletLocation = palLoc,
              FixtureOnPallet = palRow.Fixture,
              NumFaces = 1,
              OnHold = palOnHold != null && palOnHold.TryGetValue(palName, out var h) && h,
            }
          );

          var oldCycles = jobDB.CurrentPalletLog(palName);

          //Add the material currently on the pallet
          foreach (var palSub in mazakData.PalletSubStatuses)
          {
            if (palSub.PalletNumber != palRow.PalletNumber)
              continue;
            if (palSub.FixQuantity <= 0)
              continue;
            if (!jobUniqBySchID.ContainsKey(palSub.ScheduleID))
              continue;

            palletsByName[palName] = palletsByName[palName] with
            {
              NumFaces = Math.Max(palletsByName[palName].NumFaces, palSub.PartProcessNumber),
            };

            var job = jobsByUniq[jobUniqBySchID[palSub.ScheduleID]];

            var matIDs = new Queue<long>(
              FindMatIDsFromOldCycles(
                oldCycles,
                palletWithUnprocessedUnloads == palName,
                job,
                palSub.PartProcessNumber,
                jobDB
              )
            );

            for (int i = 1; i <= palSub.FixQuantity; i++)
            {
              int face = palSub.PartProcessNumber;
              long matID = -1;
              if (matIDs.Count > 0)
                matID = matIDs.Dequeue();

              var matDetails = jobDB.GetMaterialDetails(matID);

              //check for unloading or transfer
              var loadNext = CheckLoadOfNextProcess(
                currentLoads,
                job.UniqueStr,
                palSub.PartProcessNumber,
                palLoc
              );
              var unload = CheckUnload(currentLoads, job.UniqueStr, palSub.PartProcessNumber, palLoc);

              var action = new InProcessMaterialAction()
              {
                Type = InProcessMaterialAction.ActionType.Waiting,
              };

              if (loadNext != null)
              {
                var start = FindLoadStartFromOldCycles(oldCycles, matID);
                action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.Loading,
                  LoadOntoFace = palSub.PartProcessNumber + 1,
                  LoadOntoPalletNum = palName,
                  ProcessAfterLoad = palSub.PartProcessNumber + 1,
                  PathAfterLoad = 1,
                  ElapsedLoadUnloadTime = start != null ? (TimeSpan?)utcNow.Subtract(start.EndTimeUTC) : null,
                };
              }
              else if (unload != null)
              {
                var start = FindLoadStartFromOldCycles(oldCycles, matID);
                action = new InProcessMaterialAction()
                {
                  Type =
                    palSub.PartProcessNumber == job.Processes.Count
                      ? InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
                      : InProcessMaterialAction.ActionType.UnloadToInProcess,
                  UnloadIntoQueue = OutputQueueForMaterial(
                    matID,
                    job,
                    palSub.PartProcessNumber,
                    oldCycles,
                    fmsSettings
                  ),
                  ElapsedLoadUnloadTime = start != null ? (TimeSpan?)utcNow.Subtract(start.EndTimeUTC) : null,
                };
              }
              else
              {
                // detect if machining
                var start = FindMachineStartFromOldCycles(oldCycles, matID);
                if (start != null)
                {
                  var machStop = job.Processes[palSub.PartProcessNumber - 1][0].Stops.FirstOrDefault();
                  var elapsedTime = utcNow.Subtract(start.EndTimeUTC);
                  action = new InProcessMaterialAction()
                  {
                    Type = InProcessMaterialAction.ActionType.Machining,
                    ElapsedMachiningTime = elapsedTime,
                    ExpectedRemainingMachiningTime =
                      machStop != null ? machStop.ExpectedCycleTime.Subtract(elapsedTime) : TimeSpan.Zero,
                  };
                }
              }

              material.Add(
                new InProcessMaterial()
                {
                  MaterialID = matID,
                  JobUnique = job.UniqueStr,
                  PartName = job.PartName,
                  Process = palSub.PartProcessNumber,
                  Path = 1,
                  Serial = matDetails?.Serial,
                  WorkorderId = matDetails?.Workorder,
                  SignaledInspections = jobDB
                    .LookupInspectionDecisions(matID)
                    .Where(x => x.Inspect)
                    .Select(x => x.InspType)
                    .Distinct()
                    .ToImmutableList(),
                  QuarantineAfterUnload = oldCycles.Any(e =>
                    e.LogType == LogType.SignalQuarantine && e.Material.Any(m => m.MaterialID == m.MaterialID)
                  )
                    ? true
                    : null,
                  LastCompletedMachiningRouteStopIndex = oldCycles.Any(c =>
                    c.LogType == LogType.MachineCycle
                    && !c.StartOfCycle
                    && c.Material.Any(m => m.MaterialID == matID && m.Process == palSub.PartProcessNumber)
                  )
                    ? (int?)0
                    : null,
                  Location = new InProcessMaterialLocation()
                  {
                    Type = InProcessMaterialLocation.LocType.OnPallet,
                    PalletNum = palName,
                    Face = face,
                  },
                  Action = action,
                }
              );
            }
          }

          if (palLoc.Location == PalletLocationEnum.LoadUnload)
          {
            var start = FindLoadStartFromOldCycles(oldCycles);
            var elapsedLoad = start != null ? (TimeSpan?)utcNow.Subtract(start.EndTimeUTC) : null;
            AddRemainingLoadsAndUnloads(
              jobDB,
              currentLoads,
              palName,
              palLoc,
              elapsedLoad,
              material,
              jobsByUniq,
              oldCycles,
              partNameToNumProc,
              fmsSettings
            );
          }
        }
      }

      // add any pallets on hold that are not currently used in any parts
      foreach (var pal in palOnHold?.Where(p => p.Value) ?? [])
      {
        if (!palletsByName.ContainsKey(pal.Key))
        {
          palletsByName.Add(
            pal.Key,
            new PalletStatus()
            {
              PalletNum = pal.Key,
              CurrentPalletLocation = FindPalletLocation(
                machineGroupName,
                mazakData,
                mazakCfg,
                mazakCfg.InversePalletNumber(pal.Key)
              ),
              FixtureOnPallet = "",
              NumFaces = 1,
              OnHold = true,
            }
          );
        }
      }

      //now queued
      var seenMatIds = new HashSet<long>(material.Select(m => m.MaterialID));
      material.AddRange(
        BuildCellState
          .AllQueuedMaterial(jobDB, jobCache: null)
          .Where(m => !seenMatIds.Contains(m.InProc.MaterialID))
          .Select(m => m.InProc)
      );

      var notCopied = jobDB
        .LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddHours(-WriteJobs.JobLookbackHours), DateTime.UtcNow)
        .Where(j =>
          //Check if the copy to the cell succeeded but the DB has not yet been updated.
          //The thread which copies jobs will soon notice and update the database
          //so we can ignore it for now.
          !jobsByUniq.ContainsKey(j.UniqueStr)
        )
        .Select(j =>
        {
          precedence += 1;
          return j.CloneToDerived<ActiveJob, Job>() with
          {
            Completed = j
              .Processes.Select(p => ImmutableList.Create(new int[p.Paths.Count]))
              .ToImmutableList(),
            RemainingToStart = j.Cycles,
            Decrements = j.Decrements,
            ScheduleId = j.ScheduleId,
            CopiedToSystem = false,
            Precedence = j
              .Processes.Select(p =>
                Enumerable.Range(1, p.Paths.Count).Select(x => precedence).ToImmutableList()
              )
              .ToImmutableList(),
            AssignedWorkorders = EmptyToNull(jobDB.GetWorkordersForUnique(j.UniqueStr)),
          };
        });

      var allJobs = jobsByUniq
        .Values.Select(job => new ActiveJob()
        {
          UniqueStr = job.UniqueStr,
          RouteStartUTC = job.DbJob?.RouteStartUTC ?? DateTime.Today,
          RouteEndUTC = job.DbJob?.RouteEndUTC ?? DateTime.Today.AddDays(1),
          Archived = false,
          PartName = job.PartName,
          Comment = job.DbJob?.Comment,
          ScheduleId = job.DbJob?.ScheduleId,
          BookingIds = job.DbJob?.BookingIds,
          ManuallyCreated = job.DbJob?.ManuallyCreated ?? false,
          HoldJob = job.UserHold
            ? new BlackMaple.MachineFramework.HoldPattern()
            {
              UserHold = true,
              ReasonForUserHold = "",
              HoldUnholdPattern = ImmutableList<TimeSpan>.Empty,
              HoldUnholdPatternStartUTC = DateTime.MinValue,
              HoldUnholdPatternRepeats = false,
            }
            : null,
          Cycles = job.Cycles,
          Processes = job
            .Processes.Select(paths => new ProcessInfo() { Paths = paths.ToImmutable() })
            .ToImmutableList(),
          CopiedToSystem = true,
          Completed = job.Completed.Select(c => c.ToImmutable()).ToImmutableList(),
          RemainingToStart = Math.Max(job.Cycles - job.Started, 0),
          Decrements = EmptyToNull(jobDB.LoadDecrementsForJob(job.UniqueStr)),
          AssignedWorkorders = EmptyToNull(jobDB.GetWorkordersForUnique(job.UniqueStr)),
          Precedence = job.Precedence.Select(p => p.ToImmutable()).ToImmutableList(),
        })
        .Concat(notCopied)
        .ToImmutableDictionary(j => j.UniqueStr);

      return new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = DateTime.UtcNow,
        Jobs = allJobs,
        Pallets = palletsByName.ToImmutable(),
        Material = material.ToImmutable(),
        Queues = BuildCellState.CalcQueueRoles(allJobs.Values, fmsSettings, jobDB),
        Alarms = (mazakData.Alarms ?? Enumerable.Empty<MazakAlarmRow>())
          .Where(a => !string.IsNullOrEmpty(a.AlarmMessage))
          .Select(a => a.AlarmMessage)
          .ToImmutableList(),
        Workorders = jobDB.GetActiveWorkorders(
          additionalWorkorders: material
            .Select(m => m.WorkorderId)
            .Where(w => !string.IsNullOrEmpty(w))
            .ToHashSet()
        ),
      };
    }

    private static void CalculateMaxProcAndPath(
      MazakAllData mazakData,
      IRepository jobDB,
      out Dictionary<string, int> uniqueToMaxProcess,
      out Dictionary<string, int> partNameToNumProc
    )
    {
      uniqueToMaxProcess = new Dictionary<string, int>();
      partNameToNumProc = new Dictionary<string, int>();
      foreach (var partRow in mazakData.Parts)
      {
        partNameToNumProc[MazakPart.ExtractPartNameFromMazakPartName(partRow.PartName)] =
          partRow.Processes.Count();

        if (MazakPart.IsSailPart(partRow.PartName, partRow.Comment) && !string.IsNullOrEmpty(partRow.Comment))
        {
          string jobUnique = "";
          int numProc = partRow.Processes.Count;

          jobUnique = MazakPart.UniqueFromComment(partRow.Comment);

          if (jobDB.LoadJob(jobUnique) == null)
            continue;

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

    private static void AddRoutingToJob(
      MazakAllData mazakData,
      MazakPartRow partRow,
      CurrentJob job,
      string machineGroupName,
      MazakConfig mazakCfg,
      long precedence
    )
    {
      //Add routing and pallets
      foreach (var partProcRow in partRow.Processes)
      {
        var dbPath = job.DbProcPath(partProcRow.ProcessNumber, 1);

        job.Precedence[partProcRow.ProcessNumber - 1][0] = precedence;

        //Routing
        string fixStr = partProcRow.FixLDS;
        string cutStr = partProcRow.CutMc;
        string removeStr = partProcRow.RemoveLDS;

        if (mazakCfg.DBType != MazakDbType.MazakVersionE)
        {
          fixStr = ConvertStatIntV2ToV1(Convert.ToInt32(fixStr));
          cutStr = ConvertStatIntV2ToV1(Convert.ToInt32(cutStr));
          removeStr = ConvertStatIntV2ToV1(Convert.ToInt32(removeStr));
        }

        var loads = ImmutableSortedSet.CreateBuilder<int>();
        var unloads = ImmutableSortedSet.CreateBuilder<int>();
        var machines = ImmutableSortedSet.CreateBuilder<int>();
        foreach (char c in fixStr)
          if (c != '0')
            loads.Add(int.Parse(c.ToString()));
        foreach (char c in removeStr)
          if (c != '0')
            unloads.Add(int.Parse(c.ToString()));
        foreach (char c in cutStr)
        {
          if (c != '0')
          {
            int num = int.Parse(c.ToString());
            if (mazakCfg.MachineNumbers != null && num > 0 && num <= mazakCfg.MachineNumbers.Count)
            {
              num = mazakCfg.MachineNumbers[num - 1];
            }
            machines.Add(num);
          }
        }

        var routeStop = new MachiningStop()
        {
          StationGroup = machineGroupName,
          Stations = machines.ToImmutable(),
          Program = partProcRow.MainProgram,
          ProgramRevision = null,
          ExpectedCycleTime = dbPath?.Stops?.FirstOrDefault()?.ExpectedCycleTime ?? TimeSpan.Zero,
        };

        //Planned Pallets
        var pals = ImmutableSortedSet.CreateBuilder<int>();
        foreach (var palRow in mazakData.Pallets)
        {
          if (palRow.PalletNumber > 0 && palRow.Fixture == partProcRow.Fixture)
          {
            pals.Add(mazakCfg.TranslatePalletNumber(palRow.PalletNumber));
          }
        }

        job.Processes[partProcRow.ProcessNumber - 1][0] = new ProcPathInfo()
        {
          PalletNums = pals.ToImmutable(),
          Fixture = dbPath?.Fixture,
          Face = dbPath?.Face,
          Load = loads.ToImmutable(),
          ExpectedLoadTime = dbPath?.ExpectedLoadTime ?? TimeSpan.Zero,
          Unload = unloads.ToImmutable(),
          ExpectedUnloadTime = dbPath?.ExpectedUnloadTime ?? TimeSpan.Zero,
          Stops = ImmutableList.Create(routeStop),
          SimulatedProduction = dbPath?.SimulatedProduction,
          SimulatedStartingUTC = dbPath?.SimulatedStartingUTC ?? DateTime.Today,
          SimulatedAverageFlowTime = dbPath?.SimulatedAverageFlowTime ?? TimeSpan.Zero,
          PartsPerPallet = partProcRow.FixQuantity,
          InputQueue = dbPath?.InputQueue,
          OutputQueue = dbPath?.OutputQueue,
          Inspections = dbPath?.Inspections,
          Casting = dbPath?.Casting,
        };
      }
    }

    private static void AddCompletedToJob(MazakScheduleRow schRow, CurrentJob job)
    {
      job.Completed[job.Processes.Count - 1][0] = schRow.CompleteQuantity;

      //in-proc and material for each process
      var counts = new Dictionary<int, int>(); //key is process, value is in-proc + mat
      foreach (var schProcRow in schRow.Processes)
      {
        counts[schProcRow.ProcessNumber] =
          schProcRow.ProcessBadQuantity
          + schProcRow.ProcessExecuteQuantity
          + schProcRow.ProcessMaterialQuantity;
      }

      for (int proc = 1; proc < job.Processes.Count; proc++)
      {
        var cnt = counts.Where(x => x.Key > proc).Select(x => x.Value).Sum();
        job.Completed[proc - 1][0] = cnt + schRow.CompleteQuantity;
      }
    }

    private static void AddMachiningOrCompletedToStarted(MazakScheduleRow schRow, CurrentJob job)
    {
      job.Started += schRow.CompleteQuantity;
      foreach (var schProcRow in schRow.Processes)
      {
        job.Started += schProcRow.ProcessBadQuantity + schProcRow.ProcessExecuteQuantity;
        if (schProcRow.ProcessNumber > 1)
          job.Started += schProcRow.ProcessMaterialQuantity;
      }
    }

    private static LoadAction CheckLoadOfNextProcess(
      List<LoadAction> currentLoads,
      string unique,
      int process,
      PalletLocation loc
    )
    {
      if (loc.Location != PalletLocationEnum.LoadUnload)
        return null;
      for (int i = 0; i < currentLoads.Count; i++)
      {
        var act = currentLoads[i];
        if (
          act.LoadEvent == true
          && loc.Num == act.LoadStation
          && !string.IsNullOrEmpty(act.Comment)
          && unique == MazakPart.UniqueFromComment(act.Comment)
          && process + 1 == act.Process
          && act.Qty >= 1
        )
        {
          if (act.Qty == 1)
          {
            currentLoads.Remove(act);
          }
          else
          {
            currentLoads[i] = act with { Qty = act.Qty - 1 };
          }
          return act;
        }
      }
      return null;
    }

    private static LoadAction CheckUnload(
      List<LoadAction> currentLoads,
      string unique,
      int process,
      PalletLocation loc
    )
    {
      if (loc.Location != PalletLocationEnum.LoadUnload)
        return null;
      for (int i = 0; i < currentLoads.Count; i++)
      {
        var act = currentLoads[i];
        if (
          act.LoadEvent == false
          && loc.Num == act.LoadStation
          && !string.IsNullOrEmpty(act.Comment)
          && unique == MazakPart.UniqueFromComment(act.Comment)
          && process == act.Process
          && act.Qty >= 1
        )
        {
          if (act.Qty == 1)
          {
            currentLoads.Remove(act);
          }
          else
          {
            currentLoads[i] = act with { Qty = act.Qty - 1 };
          }
          return act;
        }
      }
      return null;
    }

    private static void AddRemainingLoadsAndUnloads(
      IRepository log,
      List<LoadAction> currentActions,
      int palletName,
      PalletLocation palLoc,
      TimeSpan? elapsedLoadTime,
      IList<InProcessMaterial> material,
      IReadOnlyDictionary<string, CurrentJob> jobsByUniq,
      List<BlackMaple.MachineFramework.LogEntry> oldCycles,
      IReadOnlyDictionary<string, int> partNameToNumProc,
      FMSSettings fmsSettings
    )
    {
      var queuedMats = new Dictionary<(string uniq, int proc), List<QueuedMaterial>>();
      //process remaining loads/unloads (already processed ones have been removed from currentLoads)

      // loads from raw material or a queue have not yet been processed.
      // for unloads, occasionally a palsubstatus row doesn't exist so the unload must be processed here.
      // also, if the unload is from a user-entered schedule (not one of our jobs), it also will not yet have been processed.

      // first do the unloads
      foreach (var unload in currentActions.ToList())
      {
        if (unload.LoadEvent)
          continue;
        if (unload.LoadStation != palLoc.Num)
          continue;

        CurrentJob job = null;
        if (!string.IsNullOrEmpty(unload.Comment))
        {
          jobsByUniq.TryGetValue(MazakPart.UniqueFromComment(unload.Comment), out job);
        }

        var matIDs = new Queue<long>(FindMatIDsFromOldCycles(oldCycles, false, job, unload.Process, log));

        InProcessMaterialAction loadAction = null;
        if (job == null)
        {
          // if user-entered schedule, check for loading of next process
          foreach (var loadAct in currentActions)
          {
            if (
              loadAct.LoadEvent
              && loadAct.LoadStation == unload.LoadStation
              && loadAct.Qty == unload.Qty
              && loadAct.Part == unload.Part
              && loadAct.Process == unload.Process + 1
            )
            {
              loadAction = new InProcessMaterialAction()
              {
                Type = InProcessMaterialAction.ActionType.Loading,
                LoadOntoPalletNum = palletName,
                LoadOntoFace = loadAct.Process,
                ProcessAfterLoad = loadAct.Process,
                PathAfterLoad = 1,
                ElapsedLoadUnloadTime = elapsedLoadTime,
              };
              currentActions.Remove(loadAct);
              break;
            }
          }
        }

        for (int i = 0; i < unload.Qty; i += 1)
        {
          string face = unload.Process.ToString();
          long matID = -1;
          if (matIDs.Count > 0)
            matID = matIDs.Dequeue();

          var matDetails = log.GetMaterialDetails(matID);

          InProcessMaterialAction action = loadAction;
          if (action == null)
          {
            // no load found, just unload it
            if (job != null)
            {
              action = new InProcessMaterialAction()
              {
                Type =
                  unload.Process < job.Processes.Count
                    ? InProcessMaterialAction.ActionType.UnloadToInProcess
                    : InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
                ElapsedLoadUnloadTime = elapsedLoadTime,
                UnloadIntoQueue = OutputQueueForMaterial(matID, job, unload.Process, oldCycles, fmsSettings),
              };
            }
            else
            {
              bool unloadToInProc = false;
              if (partNameToNumProc.TryGetValue(unload.Part, out var numProc))
              {
                if (unload.Process < numProc)
                {
                  unloadToInProc = true;
                }
              }
              action = new InProcessMaterialAction()
              {
                Type = unloadToInProc
                  ? InProcessMaterialAction.ActionType.UnloadToInProcess
                  : InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
                ElapsedLoadUnloadTime = elapsedLoadTime,
              };
            }
          }

          material.Add(
            new InProcessMaterial()
            {
              MaterialID = matID,
              JobUnique = string.IsNullOrEmpty(unload.Comment)
                ? ""
                : MazakPart.UniqueFromComment(unload.Comment),
              PartName = unload.Part,
              Process = unload.Process,
              Path = 1,
              Serial = matDetails?.Serial,
              WorkorderId = matDetails?.Workorder,
              SignaledInspections = log.LookupInspectionDecisions(matID)
                .Where(x => x.Inspect)
                .Select(x => x.InspType)
                .ToImmutableList(),
              QuarantineAfterUnload = oldCycles.Any(e =>
                e.LogType == LogType.SignalQuarantine && e.Material.Any(m => m.MaterialID == m.MaterialID)
              )
                ? true
                : null,
              Location = new InProcessMaterialLocation()
              {
                Type = InProcessMaterialLocation.LocType.OnPallet,
                PalletNum = palletName,
                Face = unload.Process,
              },
              Action = action,
            }
          );
        }
      }

      // now remaining loads
      foreach (var operation in currentActions)
      {
        if (!operation.LoadEvent || operation.LoadStation != palLoc.Num)
          continue;
        for (int i = 0; i < operation.Qty; i++)
        {
          // first, calculate material id, serial, workorder, location, and previous proc path
          long matId = -1;
          string serial = null;
          string workId = null;
          int prevProcPath = 1;
          var loc = new InProcessMaterialLocation() { Type = InProcessMaterialLocation.LocType.Free };

          var uniq = "";
          if (!string.IsNullOrEmpty(operation.Comment))
            uniq = MazakPart.UniqueFromComment(operation.Comment);

          // Check for queued material
          List<BlackMaple.MachineFramework.QueuedMaterial> queuedMat = null;
          jobsByUniq.TryGetValue(uniq, out var job);
          if (job != null)
          {
            // add loads on process 1 into the job started.  Loads on other
            // processes are calculated during AddMachiningOrCompletedToStarted
            if (operation.Process == 1)
            {
              job.Started += 1;
            }

            var queue = job.Processes[operation.Process - 1][0].InputQueue;
            if (!string.IsNullOrEmpty(queue))
            {
              var key = (uniq: uniq, proc: operation.Process);
              if (queuedMats.ContainsKey(key))
                queuedMat = queuedMats[key];
              else
              {
                queuedMat = QueuedMaterialForLoading(
                  job,
                  log.GetMaterialInQueueByUnique(queue, job.UniqueStr),
                  operation.Process,
                  log
                );
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
            serial = mat.Serial;
            workId = mat.Workorder;
            prevProcPath =
              mat.Paths != null && mat.Paths.TryGetValue(Math.Max(operation.Process - 1, 1), out var path)
                ? path
                : 1;
          }
          else if (operation.Process == 1 && !string.IsNullOrEmpty(job?.DbJob?.ProvisionalWorkorderId))
          {
            workId = job.DbJob.ProvisionalWorkorderId;
          }

          material.Add(
            new InProcessMaterial()
            {
              MaterialID = matId,
              JobUnique = string.IsNullOrEmpty(operation.Comment)
                ? ""
                : MazakPart.UniqueFromComment(operation.Comment),
              PartName = operation.Part,
              Process = operation.Process - 1,
              Path = prevProcPath,
              Serial = serial,
              WorkorderId = workId,
              Location = loc,
              Action = new InProcessMaterialAction()
              {
                Type = InProcessMaterialAction.ActionType.Loading,
                LoadOntoPalletNum = palletName,
                LoadOntoFace = operation.Process,
                ProcessAfterLoad = operation.Process,
                PathAfterLoad = 1,
                ElapsedLoadUnloadTime = elapsedLoadTime,
              },
              SignaledInspections =
                matId >= 0
                  ? log.LookupInspectionDecisions(matId)
                    .Where(x => x.Inspect)
                    .Select(x => x.InspType)
                    .ToImmutableList()
                  : ImmutableList<string>.Empty,
              QuarantineAfterUnload = null,
            }
          );
        }
      }
    }

    private static List<QueuedMaterial> QueuedMaterialForLoading(
      CurrentJob job,
      IEnumerable<QueuedMaterial> materialToSearch,
      int proc,
      IRepository log
    )
    {
      return MazakQueues.QueuedMaterialForLoading(job.UniqueStr, materialToSearch, proc);
    }

    private static string OutputQueueForMaterial(
      long matId,
      CurrentJob job,
      int proc,
      IReadOnlyList<BlackMaple.MachineFramework.LogEntry> log,
      FMSSettings fmsSettings
    )
    {
      var signalQuarantine = log.LastOrDefault(e =>
        e.LogType == LogType.SignalQuarantine && e.Material.Any(m => m.MaterialID == matId)
      );

      if (signalQuarantine != null)
      {
        return signalQuarantine.LocationName ?? fmsSettings.QuarantineQueue ?? "Scrap";
      }
      else
      {
        return job.Processes[proc - 1][0].OutputQueue;
      }
    }

    private static IEnumerable<long> FindMatIDsFromOldCycles(
      IEnumerable<BlackMaple.MachineFramework.LogEntry> oldCycles,
      bool hasPendingLoads,
      CurrentJob job,
      int proc,
      IRepository log
    )
    {
      if (job == null)
      {
        return Enumerable.Empty<long>();
      }

      if (hasPendingLoads)
      {
        // there is a brief period between when Mazak ends the load/unload operation and thus updates
        // the palletsubstatus and load action rows, but before the pallet cycle completes.  During
        // this time, we need to take into account the material as it will exist once the
        // pending operations are completed.

        var inputQueue = job.Processes[proc - 1][0].InputQueue;

        if (!string.IsNullOrEmpty(inputQueue))
        {
          var qs = QueuedMaterialForLoading(
            job,
            log.GetMaterialInQueueByUnique(inputQueue, job.UniqueStr),
            proc,
            log
          );
          return qs.Select(m => m.MaterialID).ToList();
        }
        else if (proc == 1)
        {
          // create new material
          return Enumerable.Empty<long>();
        }
        else
        {
          // search on pallet for previous process
          return oldCycles
            .SelectMany(c => c.Material ?? Enumerable.Empty<LogMaterial>())
            .Where(m =>
              m != null && m.MaterialID >= 0 && m.JobUniqueStr == job.UniqueStr && m.Process == proc - 1
            )
            .Select(m => m.MaterialID)
            .Distinct()
            .OrderBy(m => m)
            .ToList();
        }
      }
      else
      {
        // no pending loads, search on pallet for current process
        return oldCycles
          .SelectMany(c => c.Material ?? Enumerable.Empty<LogMaterial>())
          .Where(m => m != null && m.MaterialID >= 0 && m.JobUniqueStr == job.UniqueStr && m.Process == proc)
          .Select(m => m.MaterialID)
          .Distinct()
          .OrderBy(m => m)
          .ToList();
      }
    }

    private static BlackMaple.MachineFramework.LogEntry FindMachineStartFromOldCycles(
      IEnumerable<BlackMaple.MachineFramework.LogEntry> oldCycles,
      long matId
    )
    {
      BlackMaple.MachineFramework.LogEntry start = null;

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

    private static BlackMaple.MachineFramework.LogEntry FindLoadStartFromOldCycles(
      IEnumerable<BlackMaple.MachineFramework.LogEntry> oldCycles,
      long? matId = null
    )
    {
      BlackMaple.MachineFramework.LogEntry start = null;

      var cyclesToSearch =
        matId != null ? oldCycles.Where(e => e.Material.Any(m => m.MaterialID == matId)) : oldCycles;

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

    private static PalletLocation FindPalletLocation(
      string machName,
      MazakAllData mazakData,
      MazakConfig mazakCfg,
      int palletNum
    )
    {
      foreach (var palLocRow in mazakData.PalletPositions)
      {
        if (palLocRow.PalletNumber == palletNum)
        {
          if (mazakCfg.DBType != MazakDbType.MazakVersionE)
          {
            return ParseStatNameWeb(machName, palLocRow.PalletPosition, mazakCfg);
          }
          else
          {
            return ParseStatNameVerE(machName, palLocRow.PalletPosition, mazakCfg);
          }
        }
      }

      return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 0);
    }

    private static PalletLocation ParseStatNameVerE(string machName, string pos, MazakConfig cfg)
    {
      if (pos.StartsWith("LS"))
      {
        //load station
        return new PalletLocation()
        {
          Location = PalletLocationEnum.LoadUnload,
          StationGroup = "L/U",
          Num = cfg.TranslateLoadStationNumber(Convert.ToInt32(pos.Substring(3))),
        };
      }
      else if (pos.StartsWith("M"))
      {
        //M23 means machine 2, on the table (M21 is is the input pos, M22 is the output pos)
        int num = Convert.ToInt32(pos[1].ToString());
        if (cfg.MachineNumbers != null && num > 0 && num <= cfg.MachineNumbers.Count)
        {
          num = cfg.MachineNumbers[num - 1];
        }
        return new PalletLocation()
        {
          Location = pos[2] == '3' ? PalletLocationEnum.Machine : PalletLocationEnum.MachineQueue,
          StationGroup = machName,
          Num = num,
        };
      }
      else if (pos.StartsWith("S"))
      {
        if (pos == "STA")
        {
          return new PalletLocation()
          {
            Location = PalletLocationEnum.Cart,
            StationGroup = "Cart",
            Num = 1,
          };
        }
        else
        {
          return new PalletLocation()
          {
            Location = PalletLocationEnum.Buffer,
            StationGroup = "Buffer",
            Num = Convert.ToInt32(pos.Substring(1)),
          };
        }
      }
      else
      {
        return new PalletLocation()
        {
          Location = PalletLocationEnum.Buffer,
          StationGroup = "Unknown",
          Num = 0,
        };
      }
    }

    private static PalletLocation ParseStatNameWeb(string machName, string pos, MazakConfig cfg)
    {
      if (pos.StartsWith("LS"))
      {
        //load station
        return new PalletLocation()
        {
          Location = PalletLocationEnum.LoadUnload,
          StationGroup = "L/U",
          Num = cfg.TranslateLoadStationNumber(Convert.ToInt32(pos.Substring(3, 1))),
        };
      }
      else if (pos.StartsWith("M"))
      {
        //M023 means machine 2, on the table (M021 is is the input pos, M022 is the output pos)
        int num = Convert.ToInt32(pos[2].ToString());
        if (cfg.MachineNumbers != null && num > 0 && num <= cfg.MachineNumbers.Count)
        {
          num = cfg.MachineNumbers[num - 1];
        }
        return new PalletLocation()
        {
          Location = pos[3] == '3' ? PalletLocationEnum.Machine : PalletLocationEnum.MachineQueue,
          StationGroup = machName,
          Num = num,
        };
      }
      else if (pos.StartsWith("S"))
      {
        if (pos == "STA")
        {
          return new PalletLocation()
          {
            Location = PalletLocationEnum.Cart,
            StationGroup = "Cart",
            Num = 1,
          };
        }
        else
        {
          return new PalletLocation()
          {
            Location = PalletLocationEnum.Buffer,
            StationGroup = "Buffer",
            Num = Convert.ToInt32(pos.Substring(1)),
          };
        }
      }
      else
      {
        return new PalletLocation()
        {
          Location = PalletLocationEnum.Buffer,
          StationGroup = "Unknown",
          Num = 0,
        };
      }
    }

    private static string ConvertStatIntV2ToV1(int statNum)
    {
      char[] ret = { '0', '0', '0', '0', '0', '0', '0', '0', '0', '0' };

      for (int i = 0; i <= ret.Length - 1; i++)
      {
        if ((statNum & (1 << i)) != 0)
        {
          ret[i] = (i + 1).ToString()[0];
        }
      }

      return new string(ret);
    }

    private static ImmutableList<T> EmptyToNull<T>(ImmutableList<T> x)
    {
      if (x == null || x.Count == 0)
        return null;
      else
        return x;
    }

    private static ImmutableSortedSet<T> EmptyToNull<T>(ImmutableSortedSet<T> x)
    {
      if (x == null || x.Count == 0)
        return null;
      else
        return x;
    }
  }
}
