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
      return db.StationGroupsOnMostRecentSchedule().FirstOrDefault(s => !string.IsNullOrEmpty(s))
        ?? "MC";
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
      machineGroupName = mazakCfg.MachineGroupName ?? machineGroupName;

      //Load process and path numbers
      CalculateMaxProcAndPath(
        mazakData,
        jobDB,
        out var uniqueToMaxProcess,
        out var partNameToNumProc
      );

      // Raw controller data remains unchanged; all status comparisons use Insight coordinates.
      var currentLoads = mazakData
        .LoadActions.Select(a =>
          a with
          {
            LoadStation = mazakCfg.TranslateLoadStationNumber(a.LoadStation),
          }
        )
        .ToList();

      var jobUniqBySchID = new Dictionary<long, string>();
      var jobsByUniq = new Dictionary<string, CurrentJob>();
      // For split schedules, the Mazak PartProcessNumber on the pallet sub-status is always 1,
      // but the FMS job process can be 2+ for later split processes. Track it here.
      var splitFmsProcBySchID = new Dictionary<long, int>();

      long precedence = 0;
      foreach (var schRow in mazakData.Schedules.OrderBy(s => s.DueDate).ThenBy(s => s.Priority))
      {
        precedence += 1;

        var route = MazakRouteHistory.Find(jobDB, mazakCfg, schRow.PartName, 1);
        if (route != null && !MazakRouteHistory.AgreesWithObservation(route, mazakData))
          continue;
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
        var partComment = MazakPart.ParseCommentInfo(partRow.Comment);
        var jobUnique = partComment.Unique;

        if (!uniqueToMaxProcess.ContainsKey(jobUnique))
          continue;

        int numProc = uniqueToMaxProcess[jobUnique];
        var scheduleComment = MazakPart.ParseCommentInfo(schRow.Comment);
        var fmsProcSch = scheduleComment.FmsProcess;
        if (scheduleComment.IsSplit && (fmsProcSch < 1 || fmsProcSch > numProc))
        {
          Log.Warning(
            "Ignoring split schedule for {uniq} with out-of-range process {proc}; repository job has {numProc} processes",
            jobUnique,
            fmsProcSch,
            numProc
          );
          continue;
        }

        //Create or lookup the job
        CurrentJob job;
        if (!jobsByUniq.TryGetValue(jobUnique, out job))
        {
          var dbJob = jobDB.LoadJob(jobUnique);
          job = NewCurrentJob(jobUnique, partName, numProc, dbJob);
          jobsByUniq.Add(jobUnique, job);
        }
        jobUniqBySchID.Add(schRow.Id, jobUnique);

        if (scheduleComment.IsSplit)
          splitFmsProcBySchID.Add(schRow.Id, fmsProcSch);

        //Job Basics
        // Split schedules repeat the same PlanQuantity on every per-process schedule, and
        // Mazak can delete an earlier process schedule before later ones finish. Recover the
        // shared job count from whichever split schedule still exists.
        if (scheduleComment.IsSplit)
          job.Cycles = Math.Max(job.Cycles, schRow.PlanQuantity);
        else
          job.Cycles += schRow.PlanQuantity;
        AddCompletedToJob(schRow, job, scheduleComment.IsSplit, fmsProcSch);
        AddMachiningOrCompletedToStarted(schRow, job, scheduleComment.IsSplit);
        job.UserHold =
          job.UserHold || ((HoldPattern.HoldMode)schRow.HoldMode) == HoldPattern.HoldMode.FullHold;

        AddRoutingToJob(mazakData, partRow, job, machineGroupName, mazakCfg, precedence);
      }

      //Now add pallets
      var palletsByName = ImmutableDictionary.CreateBuilder<int, PalletStatus>();
      var material = ImmutableList.CreateBuilder<InProcessMaterial>();
      var placementAlarms = ImmutableList.CreateBuilder<CurrentStatusAlarm>();
      var palOnHold = mazakData
        .PalletStatuses?.GroupBy(p => p.PalletNumber)
        .ToDictionary(g => mazakCfg.TranslatePalletNumber(g.Key), g => g.Any(p => p.IsOnHold > 0));
      var palletRows = mazakCfg.UseDurableRouteIdentity
        ? mazakData.Pallets.Concat(
          mazakData
            .PalletPositions.Select(p => p.PalletNumber)
            .Concat((mazakData.PalletStatuses ?? []).Select(p => p.PalletNumber))
            .Select(number => new MazakPalletRow { PalletNumber = number, Fixture = "" })
        )
        : mazakData.Pallets;
      foreach (var palRow in palletRows)
      {
        if (
          palRow.PalletNumber > 0
          && !palletsByName.ContainsKey(mazakCfg.TranslatePalletNumber(palRow.PalletNumber))
        )
        {
          var palName = mazakCfg.TranslatePalletNumber(palRow.PalletNumber);
          var palLoc = FindPalletLocation(
            machineGroupName,
            mazakData,
            mazakCfg,
            palRow.PalletNumber
          );

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

          var oldCycles = MazakRouteHistory.CurrentPlacement(jobDB, mazakCfg, palName);
          var removals = MazakMaterialHistory.LoadRemovalCounters(
            jobDB,
            oldCycles.SelectMany(e => e.Material).Select(m => m.MaterialID)
          );

          var ambiguous = MazakRouteHistory.AmbiguousPlacements(jobDB, mazakCfg, oldCycles);
          var departed = ambiguous.Values.SelectMany(p => p.Departed).ToImmutableHashSet();

          var confirmed = mazakCfg.UseDurableRouteIdentity
            ? MazakMaterialHistory
              .CurrentPalletMaterial(jobDB, oldCycles, removals)
              .RemoveAll(m => departed.Contains(m.MaterialID))
            : [];

          //Add the material currently on the pallet
          foreach (var palSub in mazakData.PalletSubStatuses)
          {
            if (palSub.PalletNumber != palRow.PalletNumber || palSub.FixQuantity <= 0)
              continue;
            var route = MazakRouteHistory.Find(
              jobDB,
              mazakCfg,
              palSub.PartName,
              palSub.PartProcessNumber
            );
            var conflictingRoute = false;
            CurrentJob job;
            int palFmsProc;
            if (route != null)
            {
              conflictingRoute = !MazakRouteHistory.AgreesWithObservation(route, mazakData);
              palFmsProc = route.JobProcess;
              if (!jobsByUniq.TryGetValue(route.JobUnique, out job))
              {
                var accepted = jobDB.LoadJob(route.JobUnique);
                if (accepted == null || accepted.Processes.Count < palFmsProc)
                  continue;
                job = NewCurrentJob(
                  accepted.UniqueStr,
                  accepted.PartName,
                  accepted.Processes.Count,
                  accepted
                );
              }
            }
            else
            {
              if (!jobUniqBySchID.TryGetValue(palSub.ScheduleID, out var unique))
                continue;
              if (
                mazakCfg.UseDurableRouteIdentity
                && !string.IsNullOrEmpty(palSub.PartName)
                && !mazakData.Schedules.Any(s =>
                  s.Id == palSub.ScheduleID && s.PartName == palSub.PartName
                )
              )
                continue;
              job = jobsByUniq[unique];
              palFmsProc = splitFmsProcBySchID.TryGetValue(palSub.ScheduleID, out var splitFP)
                ? splitFP
                : palSub.PartProcessNumber;
            }

            palletsByName[palName] = palletsByName[palName] with
            {
              NumFaces = Math.Max(palletsByName[palName].NumFaces, palSub.PartProcessNumber),
            };

            var face = palSub.PartProcessNumber;
            var definitions =
              route == null
                ? []
                : jobDB
                  .LoadMazakRouteIdentities(mazakCfg.ControllerSource, route.JobUnique)
                  .Where(r => r.JobProcess == route.JobProcess && r.Path == route.Path)
                  .ToImmutableList();
            var matIDs =
              route != null
                ? confirmed
                  .Where(m =>
                    m.JobUniqueStr == job.UniqueStr
                    && m.Process == palFmsProc
                    && m.Face == face
                    && MazakRouteHistory.MatchesRecordedPlacement(oldCycles, definitions, m, route)
                  )
                  .Select(m => m.MaterialID)
                  .ToImmutableList()
                : FindMatIDsFromOldCycles(
                    oldCycles,
                    palletWithUnprocessedUnloads == palName,
                    job,
                    palFmsProc,
                    jobDB,
                    removals
                  )
                  .Take(palSub.FixQuantity)
                  .ToImmutableList();
            if (route != null)
            {
              var candidates = matIDs;
              var unresolved = ambiguous.GetValueOrDefault(route.PartName)?.Candidates;
              if (conflictingRoute || matIDs.Count > palSub.FixQuantity)
                matIDs = [];
              else if (unresolved != null)
                matIDs = matIDs.Where(id => !unresolved.Contains(id)).ToImmutableList();
              var unidentified = palSub.FixQuantity - matIDs.Count;
              if (unidentified > 0)
                ReportUncertainPlacement(
                  face,
                  palSub.FixQuantity,
                  unidentified,
                  candidates,
                  conflictingRoute
                );
            }

            for (var i = 0; i < palSub.FixQuantity; i++)
            {
              var matID = i < matIDs.Count ? matIDs[i] : -1;
              var currentProcessBeforeLoad =
                palFmsProc > palSub.PartProcessNumber ? palFmsProc - 1 : palSub.PartProcessNumber;

              //check for unloading or transfer
              // The pallet substatus reports Mazak face/process 1 for every split schedule. For
              // split transfers beyond proc-1, recover the pre-load FMS process from the schedule.
              // Managed processes load independently. A request for P2 can select a different
              // piece from the one unloading P1 at the same station.
              var loadNext =
                route != null
                  ? null
                  : CheckLoadOfNextProcess(
                    currentLoads,
                    job.UniqueStr,
                    currentProcessBeforeLoad,
                    palLoc
                  );
              var unload = CheckUnload(
                currentLoads,
                job.UniqueStr,
                palSub.PartProcessNumber,
                palLoc,
                route
              );

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
                  LoadOntoFace = currentProcessBeforeLoad + 1,
                  LoadOntoPalletNum = palName,
                  ProcessAfterLoad = currentProcessBeforeLoad + 1,
                  PathAfterLoad = 1,
                  ElapsedLoadUnloadTime =
                    start != null ? (TimeSpan?)utcNow.Subtract(start.EndTimeUTC) : null,
                };
              }
              else if (unload != null)
              {
                var start = FindLoadStartFromOldCycles(oldCycles, matID);
                action = new InProcessMaterialAction()
                {
                  Type =
                    palFmsProc == job.Processes.Count
                      ? InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
                      : InProcessMaterialAction.ActionType.UnloadToInProcess,
                  UnloadIntoQueue = OutputQueueForMaterial(
                    matID,
                    job,
                    palFmsProc,
                    oldCycles,
                    fmsSettings
                  ),
                  ElapsedLoadUnloadTime =
                    start != null ? (TimeSpan?)utcNow.Subtract(start.EndTimeUTC) : null,
                };
              }
              else
              {
                action = MachiningOrWaiting(matID, palFmsProc, job.Processes[palFmsProc - 1][0]);
              }

              AddPalletMaterial(
                matID,
                job.UniqueStr,
                job.PartName,
                palFmsProc,
                route?.Path ?? 1,
                face,
                action
              );
            }
          }

          if (mazakCfg.UseDurableRouteIdentity)
          {
            foreach (var placement in ambiguous.Values.Where(p => !p.Candidates.IsEmpty))
            {
              var serials = DescribeCandidates(placement.Candidates.Order());
              ReportPlacementAlarm(
                $"Pallet {palName} face {placement.Face} has unloads with unresolved serial attribution.{serials}"
              );
            }
            if (palletWithUnprocessedUnloads == palName)
              ReportPlacementAlarm(
                $"Pallet {palName} has a load or unload event that is still settling."
              );
          }

          if (palLoc.Location == PalletLocationEnum.LoadUnload)
          {
            if (mazakCfg.UseDurableRouteIdentity)
            {
              var unassigned = currentLoads.RemoveAll(a =>
                !a.LoadEvent && a.LoadStation == palLoc.Num && IsManagedUnload(a)
              );
              if (unassigned > 0)
                ReportPlacementAlarm(
                  $"Pallet {palName} has a Mazak unload request without a matching pallet assignment."
                );
            }

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
              removals,
              partNameToNumProc,
              fmsSettings,
              mazakCfg
            );
          }
          InProcessMaterialAction MachiningOrWaiting(
            long materialId,
            int process,
            ProcPathInfo path
          )
          {
            var start =
              materialId >= 0
                ? FindMachineStartFromOldCycles(oldCycles, materialId, process)
                : null;
            if (start == null)
              return new() { Type = InProcessMaterialAction.ActionType.Waiting };
            var elapsed = utcNow.Subtract(start.EndTimeUTC);
            var stop = path?.Stops.FirstOrDefault();
            return new()
            {
              Type = InProcessMaterialAction.ActionType.Machining,
              ElapsedMachiningTime = elapsed,
              ExpectedRemainingMachiningTime = stop?.ExpectedCycleTime.Subtract(elapsed)
                ?? TimeSpan.Zero,
            };
          }

          bool IsManagedUnload(LoadAction action)
          {
            var comment = MazakPart.ParseCommentInfo(action.Comment);
            return comment.HasUnique
              && (
                mazakCfg.UsesInsightJobQuantities(jobDB.LoadJob(comment.Unique))
                || jobDB
                  .LoadMazakRouteIdentities(mazakCfg.ControllerSource, comment.Unique)
                  .Any(r =>
                    r.JobProcess == comment.JobProcessForMazakProcess(action.Process)
                    && r.NativeProcess == action.Process
                    && MazakPart.ExtractPartNameFromMazakPartName(r.PartName) == action.Part
                  )
              );
          }

          void ReportUncertainPlacement(
            int face,
            int nativeQuantity,
            int unidentifiedQuantity,
            ImmutableList<long> candidates,
            bool conflictingRoute
          )
          {
            var conflict = conflictingRoute
              ? " The observed route conflicts with its reserved identity."
              : "";
            ReportPlacementAlarm(
              $"Pallet {palName} face {face} has unresolved material placement. Mazak reports {nativeQuantity} piece(s), with {unidentifiedQuantity} unidentified piece(s).{conflict}{DescribeCandidates(candidates)}"
            );
          }

          string DescribeCandidates(IEnumerable<long> candidates)
          {
            var serials = string.Join(
              ", ",
              candidates.Select(id =>
                jobDB.GetMaterialDetails(id)?.Serial is { Length: > 0 } serial
                  ? serial
                  : $"material {id}"
              )
            );
            return serials.Length == 0 ? "" : $" Candidate identities are {serials}.";
          }

          void ReportPlacementAlarm(string message) =>
            placementAlarms.Add(
              new CurrentStatusAlarm
              {
                Code = MazakRouteHistory.PlacementAlarmCode,
                Message = message,
                Impact = AlarmImpact.Informational,
              }
            );

          void AddPalletMaterial(
            long matID,
            string unique,
            string part,
            int process,
            int path,
            int face,
            InProcessMaterialAction action
          )
          {
            var matDetails = jobDB.GetMaterialDetails(matID);
            material.Add(
              new InProcessMaterial()
              {
                MaterialID = matID,
                JobUnique = unique,
                PartName = part,
                Process = process,
                Path = path,
                Serial = matDetails?.Serial,
                WorkorderId = matDetails?.Workorder,
                SignaledInspections = jobDB
                  .LookupInspectionDecisions(matID)
                  .Where(x => x.Inspect)
                  .Select(x => x.InspType)
                  .Distinct()
                  .ToImmutableList(),
                QuarantineAfterUnload =
                  FindSignalQuarantineForMaterial(matID, oldCycles) != null ? true : null,
                LastCompletedMachiningRouteStopIndex = oldCycles.Any(c =>
                  c.LogType == LogType.MachineCycle
                  && !c.StartOfCycle
                  && c.Material.Any(m => m.MaterialID == matID && m.Process == process)
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
        .LoadJobsNotCopiedToSystem(
          DateTime.UtcNow.AddHours(-WriteJobs.JobLookbackHours),
          DateTime.UtcNow
        )
        .Where(j => !j.Archived || !mazakCfg.UsesInsightJobQuantities(j))
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
            Cycles = Math.Max(0, j.Cycles - (j.Decrements?.Sum(d => d.Quantity) ?? 0)),
            Completed = j
              .Processes.Select(p => ImmutableList.Create(new int[p.Paths.Count]))
              .ToImmutableList(),
            RemainingToStart = j.Decrements?.Any(d => d.Quantity > 0) == true ? 0 : j.Cycles,
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
        .Values.Where(job =>
          job.DbJob?.Archived != true || !mazakCfg.UsesInsightJobQuantities(job.DbJob)
        )
        .Select(job => new ActiveJob()
        {
          UniqueStr = job.UniqueStr,
          RouteStartUTC = job.DbJob?.RouteStartUTC ?? DateTime.Today,
          RouteEndUTC = job.DbJob?.RouteEndUTC ?? DateTime.Today.AddDays(1),
          Archived = false,
          PartName = job.PartName,
          Comment = job.DbJob?.Comment,
          ScheduleId = job.DbJob?.ScheduleId,
          BookingIds = job.DbJob?.BookingIds,
          ArtifactRunDate = job.DbJob?.ArtifactRunDate,
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
            .Processes.Select(
              (paths, processIndex) =>
                processIndex < (job.DbJob?.Processes.Count ?? 0)
                  ? job.DbJob.Processes[processIndex] with
                  {
                    Paths = paths.ToImmutable(),
                  }
                  : new ProcessInfo { Paths = paths.ToImmutable() }
            )
            .ToImmutableList(),
          CopiedToSystem = true,
          Completed = job.Completed.Select(c => c.ToImmutable()).ToImmutableList(),
          // Controller machining starts plus active process-1 loads remain a lower bound when
          // Insight history is incomplete.
          RemainingToStart = Math.Max(
            job.Cycles
              - (
                job.DbJob == null
                  ? job.Started
                  : Math.Max(
                    job.Started,
                    JobHelpers.CountCommittedToAutomation(
                      job.DbJob,
                      jobDB.GetJobProductionSummary(job.UniqueStr).AutomationEntryMaterialIds,
                      material
                    )
                  )
              ),
            0
          ),
          Decrements = EmptyToNull(jobDB.LoadDecrementsForJob(job.UniqueStr)),
          AssignedWorkorders = EmptyToNull(jobDB.GetWorkordersForUnique(job.UniqueStr)),
          Precedence = job.Precedence.Select(p => p.ToImmutable()).ToImmutableList(),
        })
        .Concat(notCopied)
        .ToImmutableDictionary(j => j.UniqueStr);

      if (mazakCfg.UseInsightJobQuantities != null)
      {
        // Repository accounting survives controller plan increases, split-route deletion and
        // canceled instructions. The backend projects any additional private reservations via
        // AdjustCurrentStatus and owns the decision to archive these jobs.
        var localJobs = new JobCache(jobDB).BuildActiveJobs(
          mazakCfg.UseDurableRouteIdentity ? [] : material,
          jobDB
        );
        foreach (var local in localJobs.Values.Where(mazakCfg.UsesInsightJobQuantities))
        {
          var remainingToStart = local.RemainingToStart;
          // Actual execution still bounds remaining demand when entry history is incomplete.
          // Use the locally authorized quantity, not the controller's execution ceiling.
          if (
            !mazakCfg.UseDurableRouteIdentity
            && remainingToStart is { } remaining
            && jobsByUniq.TryGetValue(local.UniqueStr, out var controller)
          )
            remainingToStart = Math.Min(remaining, Math.Max(0, local.Cycles - controller.Started));
          allJobs = allJobs.SetItem(
            local.UniqueStr,
            allJobs.TryGetValue(local.UniqueStr, out var installed)
              ? installed with
              {
                Cycles = local.Cycles,
                RemainingToStart = remainingToStart,
                // Keep execution evidence from a controller batch whose logs are still
                // settling, and history for split routes that have already been removed.
                Completed = mazakCfg.UseDurableRouteIdentity
                  ? local.Completed
                  : local
                    .Completed.Select(
                      (paths, process) =>
                        paths
                          .Select(
                            (count, path) => Math.Max(count, installed.Completed[process][path])
                          )
                          .ToImmutableList()
                    )
                    .ToImmutableList(),
                Decrements = EmptyToNull(local.Decrements),
              }
              : local with
              {
                Decrements = EmptyToNull(local.Decrements),
              }
          );
        }
      }

      return new CurrentStatus()
      {
        Revision = 0,
        TimeOfCurrentStatusUTC = utcNow,
        Jobs = allJobs,
        Pallets = palletsByName.ToImmutable(),
        Material = material.ToImmutable(),
        Queues = BuildCellState.CalcQueueRoles(allJobs.Values, fmsSettings, jobDB),
        Alarms = (mazakData.Alarms ?? Enumerable.Empty<MazakAlarmRow>())
          .Where(a => !string.IsNullOrEmpty(a.AlarmMessage))
          .Select(a => a.AlarmMessage)
          .Concat(placementAlarms.Select(a => a.Message))
          .ToImmutableList(),
        AlarmDetails = placementAlarms.Count > 0 ? placementAlarms.ToImmutable() : null,
        Workorders = jobDB.GetActiveWorkorders(
          additionalWorkorders: material
            .Select(m => m.WorkorderId)
            .Where(w => !string.IsNullOrEmpty(w))
            .ToHashSet()
        ),
      };
    }

    private static CurrentJob NewCurrentJob(
      string jobUnique,
      string partName,
      int numProc,
      HistoricJob dbJob
    ) =>
      new CurrentJob()
      {
        UniqueStr = jobUnique,
        PartName = partName,
        Cycles = 0,
        DbJob = dbJob,
        Processes = Enumerable
          .Range(1, numProc)
          .Select(proc =>
          {
            var b = ImmutableList.CreateBuilder<ProcPathInfo>();
            b.Add(
              dbJob != null
              && proc <= dbJob.Processes.Count
              && dbJob.Processes[proc - 1].Paths.Count > 0
                ? dbJob.Processes[proc - 1].Paths[0]
                : null
            );
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
        var partComment = MazakPart.ParseCommentInfo(partRow.Comment);
        var numProc = partRow.Processes.Count();
        numProc = partComment.TotalProcesses(jobDB, numProc);

        partNameToNumProc[MazakPart.ExtractPartNameFromMazakPartName(partRow.PartName)] = numProc;

        if (MazakPart.IsSailPart(partRow.PartName, partRow.Comment) && partComment.HasUnique)
        {
          var jobUnique = partComment.Unique;

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
      var partComment = MazakPart.ParseCommentInfo(partRow.Comment);

      //Add routing and pallets
      foreach (var partProcRow in partRow.Processes)
      {
        var jobProc = partComment.JobProcessForMazakProcess(partProcRow.ProcessNumber);
        var dbPath = job.DbProcPath(jobProc, 1);

        job.Precedence[jobProc - 1][0] = precedence;

        //Routing
        string fixStr = partProcRow.FixLDS;
        string cutStr = partProcRow.CutMc;
        string removeStr = partProcRow.RemoveLDS;

        fixStr = ConvertStatIntV2ToV1(Convert.ToInt32(fixStr));
        cutStr = ConvertStatIntV2ToV1(Convert.ToInt32(cutStr));
        removeStr = ConvertStatIntV2ToV1(Convert.ToInt32(removeStr));

        var loads = ImmutableSortedSet.CreateBuilder<int>();
        var unloads = ImmutableSortedSet.CreateBuilder<int>();
        var machines = ImmutableSortedSet.CreateBuilder<int>();
        for (var i = 0; i < fixStr.Length; i++)
          if (fixStr[i] != '0')
            loads.Add(mazakCfg.TranslateLoadStationNumber(i + 1));
        for (var i = 0; i < removeStr.Length; i++)
          if (removeStr[i] != '0')
            unloads.Add(mazakCfg.TranslateLoadStationNumber(i + 1));
        foreach (char c in cutStr)
        {
          if (c != '0')
          {
            machines.Add(mazakCfg.TranslateMachineNumber(int.Parse(c.ToString())));
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

        job.Processes[jobProc - 1][0] = new ProcPathInfo()
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

    private static void AddCompletedToJob(
      MazakScheduleRow schRow,
      CurrentJob job,
      bool isSplit,
      int fmsProc
    )
    {
      if (isSplit)
      {
        // Each split schedule owns exactly one process slot. The proc-1 schedule
        // CompleteQuantity represents pieces that finished proc-1 (and may still be
        // in the queue, running proc-2, or fully done). The proc-N schedule
        // CompleteQuantity represents pieces that finished proc-N. Each slot is
        // set independently; no cross-schedule summing is needed or correct.
        job.Completed[fmsProc - 1][0] = schRow.CompleteQuantity;
        return;
      }

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

    private static void AddMachiningOrCompletedToStarted(
      MazakScheduleRow schRow,
      CurrentJob job,
      bool isSplit
    )
    {
      var started = (long)DecrementPlanQty.CountCompletedOrMachiningStarted(schRow);
      if (isSplit)
        job.Started = Math.Max(job.Started, started);
      else
        job.Started += started;
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
        var actComment = MazakPart.ParseCommentInfo(act.Comment);
        var actProcess = actComment.IsSplit ? actComment.FmsProcess : act.Process;
        if (
          act.LoadEvent == true
          && loc.Num == act.LoadStation
          && !string.IsNullOrEmpty(act.Comment)
          && unique == actComment.Unique
          && process + 1 == actProcess
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
      PalletLocation loc,
      MazakRouteIdentity route
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
          && (
            route == null
            || act.Comment == MazakPart.SplitComment(route.JobUnique, route.JobProcess, route.Path)
          )
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
      IReadOnlyDictionary<long, long> removals,
      IReadOnlyDictionary<string, int> partNameToNumProc,
      FMSSettings fmsSettings,
      MazakConfig config
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
        var unloadComment = MazakPart.ParseCommentInfo(unload.Comment);
        if (unloadComment.HasUnique)
        {
          jobsByUniq.TryGetValue(unloadComment.Unique, out job);
        }

        // For split schedules, unload.Process is always 1 (the Mazak process); the actual FMS
        // job process is encoded in the comment.
        var fmsUnloadProc = unloadComment.JobProcessForMazakProcess(unload.Process);

        var matIDs = new Queue<long>(
          FindMatIDsFromOldCycles(oldCycles, false, job, fmsUnloadProc, log, removals)
        );

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
                  fmsUnloadProc < job.Processes.Count
                    ? InProcessMaterialAction.ActionType.UnloadToInProcess
                    : InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
                ElapsedLoadUnloadTime = elapsedLoadTime,
                UnloadIntoQueue = OutputQueueForMaterial(
                  matID,
                  job,
                  fmsUnloadProc,
                  oldCycles,
                  fmsSettings
                ),
              };
            }
            else
            {
              bool unloadToInProc = false;
              if (partNameToNumProc.TryGetValue(unload.Part, out var numProc))
              {
                if (fmsUnloadProc < numProc)
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
              JobUnique = unloadComment.Unique,
              PartName = unload.Part,
              Process = fmsUnloadProc,
              Path = 1,
              Serial = matDetails?.Serial,
              WorkorderId = matDetails?.Workorder,
              SignaledInspections = log.LookupInspectionDecisions(matID)
                .Where(x => x.Inspect)
                .Select(x => x.InspType)
                .ToImmutableList(),
              QuarantineAfterUnload =
                FindSignalQuarantineForMaterial(matID, oldCycles) != null ? true : null,
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
          var loc = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.Free,
          };

          var operationComment = MazakPart.ParseCommentInfo(operation.Comment);
          var uniq = operationComment.Unique;

          // For split schedules, operation.Process is always 1 (the Mazak process); the actual
          // FMS job process is encoded in the comment.
          var fmsOperationProc = operationComment.JobProcessForMazakProcess(operation.Process);

          // Check for queued material
          List<BlackMaple.MachineFramework.QueuedMaterial> queuedMat = null;
          jobsByUniq.TryGetValue(uniq, out var job);
          var managed =
            config.UseDurableRouteIdentity && config.UsesInsightJobQuantities(job?.DbJob);
          if (job != null)
          {
            // add loads on process 1 into the job started.  Loads on other
            // processes are calculated during AddMachiningOrCompletedToStarted
            if (fmsOperationProc == 1)
            {
              job.Started += 1;
            }

            var queue = job.Processes[fmsOperationProc - 1][0].InputQueue;
            if (!managed && !string.IsNullOrEmpty(queue))
            {
              var key = (uniq: uniq, proc: fmsOperationProc);
              if (queuedMats.ContainsKey(key))
                queuedMat = queuedMats[key];
              else
              {
                queuedMat = QueuedMaterialForLoading(
                  job,
                  log.GetMaterialInQueueByUnique(queue, job.UniqueStr),
                  fmsOperationProc,
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
              mat.Paths != null
              && mat.Paths.TryGetValue(Math.Max(fmsOperationProc - 1, 1), out var path)
                ? path
                : 1;
          }
          else if (
            fmsOperationProc == 1
            && !string.IsNullOrEmpty(job?.DbJob?.ProvisionalWorkorderId)
          )
          {
            workId = job.DbJob.ProvisionalWorkorderId;
          }

          material.Add(
            new InProcessMaterial()
            {
              MaterialID = matId,
              JobUnique = operationComment.Unique,
              PartName = operation.Part,
              Process = fmsOperationProc - 1,
              Path = prevProcPath,
              Serial = serial,
              WorkorderId = workId,
              Location = loc,
              Action = new InProcessMaterialAction()
              {
                Type = InProcessMaterialAction.ActionType.Loading,
                LoadOntoPalletNum = palletName,
                LoadOntoFace = managed ? operation.Process : fmsOperationProc,
                ProcessAfterLoad = fmsOperationProc,
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

    private static BlackMaple.MachineFramework.LogEntry FindSignalQuarantineForMaterial(
      long matId,
      IReadOnlyList<BlackMaple.MachineFramework.LogEntry> log
    )
    {
      return log.LastOrDefault(e =>
        e.LogType == LogType.SignalQuarantine && e.Material.Any(m => m.MaterialID == matId)
      );
    }

    private static string OutputQueueForMaterial(
      long matId,
      CurrentJob job,
      int proc,
      IReadOnlyList<BlackMaple.MachineFramework.LogEntry> log,
      FMSSettings fmsSettings
    )
    {
      var signalQuarantine = FindSignalQuarantineForMaterial(matId, log);

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
      IRepository log,
      IReadOnlyDictionary<long, long> removals
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
            .SelectMany(c =>
              (c.Material ?? Enumerable.Empty<LogMaterial>()).Where(m =>
                !MazakMaterialHistory.WasRemovedAfter(removals, m.MaterialID, c.Counter)
              )
            )
            .Where(m =>
              m != null
              && m.MaterialID >= 0
              && m.JobUniqueStr == job.UniqueStr
              && m.Process == proc - 1
            )
            .Select(m => m.MaterialID)
            .Distinct()
            .OrderBy(m => m)
            .ToList();
        }
      }
      else
      {
        // With no pending load, a completed unload ends the older assignment even if
        // its machine/load history remains in this pallet cycle. A later assignment wins.
        var unloadedAt = oldCycles
          .Where(c =>
            c.LogType == LogType.LoadUnloadCycle && !c.StartOfCycle && c.Result == "UNLOAD"
          )
          .SelectMany(c => c.Material.Select(m => (m.MaterialID, c.Counter)))
          .GroupBy(m => m.MaterialID)
          .ToDictionary(g => g.Key, g => g.Max(m => m.Counter));
        return oldCycles
          .SelectMany(c =>
            (c.Material ?? Enumerable.Empty<LogMaterial>()).Where(m =>
              (!unloadedAt.TryGetValue(m.MaterialID, out var counter) || c.Counter > counter)
              && !MazakMaterialHistory.WasRemovedAfter(removals, m.MaterialID, c.Counter)
            )
          )
          .Where(m =>
            m != null && m.MaterialID >= 0 && m.JobUniqueStr == job.UniqueStr && m.Process == proc
          )
          .Select(m => m.MaterialID)
          .Distinct()
          .OrderBy(m => m)
          .ToList();
      }
    }

    private static BlackMaple.MachineFramework.LogEntry FindMachineStartFromOldCycles(
      IEnumerable<BlackMaple.MachineFramework.LogEntry> oldCycles,
      long matId,
      int process
    )
    {
      BlackMaple.MachineFramework.LogEntry start = null;

      foreach (var s in oldCycles.Where(e => e.Material.Any(m => m.MaterialID == matId)))
      {
        if (s.LogType == LogType.LoadUnloadCycle && !s.StartOfCycle)
        {
          start = null;
        }
        else if (
          s.LogType == LogType.MachineCycle
          && s.Material.Any(m => m.MaterialID == matId && m.Process == process)
        )
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
          return ParseStatNameWeb(machName, palLocRow.PalletPosition, mazakCfg);
        }
      }

      return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 0);
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
        return new PalletLocation()
        {
          Location = pos[3] == '3' ? PalletLocationEnum.Machine : PalletLocationEnum.MachineQueue,
          StationGroup = machName,
          Num = cfg.TranslateMachineNumber(Convert.ToInt32(pos[2].ToString())),
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
