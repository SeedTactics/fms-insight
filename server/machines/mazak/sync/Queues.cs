/* Copyright (c) 2024, John Lenz

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
using Serilog;

namespace MazakMachineInterface
{
  public static class MazakQueues
  {
    private static readonly ILogger log = Serilog.Log.ForContext<ScheduleWithQueues>();

    public static MazakWriteData CalculateScheduleChanges(
      IRepository jdb,
      MazakCurrentStatus mazakData,
      MazakConfig cfg
    )
    {
      IEnumerable<ScheduleWithQueues> schs;
      schs = LoadSchedules(jdb, mazakData, cfg);
      if (!schs.Any())
        return null;

      CalculateTargetMatQty(jdb, schs, cfg);
      return UpdateMazakMaterialCounts(schs);
    }

    private class ScheduleWithQueuesProcess
    {
      public MazakScheduleProcessRow SchProcRow { get; set; }
      public string InputQueue { get; set; }
      public string Casting { get; set; }
      public int Path { get; set; }
      public int? TargetMaterialCount { get; set; }
    }

    private class ScheduleWithQueues
    {
      public MazakScheduleRow SchRow { get; set; }
      public string Unique { get; set; }
      public Job Job { get; set; }
      public bool LowerPriorityScheduleMatchingCastingSkipped { get; set; }
      public Dictionary<int, ScheduleWithQueuesProcess> Procs { get; set; }
      public DateTime? NewDueDate { get; set; }
      public int? NewPriority { get; set; }
    }

    private static IReadOnlyList<ScheduleWithQueues> LoadSchedules(
      IRepository jdb,
      MazakCurrentStatus mazakData,
      MazakConfig cfg
    )
    {
      var loadOpers = mazakData.LoadActions;
      var schs = new List<ScheduleWithQueues>();
      var skippedCastings = new HashSet<string>();
      foreach (var schRow in mazakData.Schedules.OrderBy(s => s.DueDate).ThenBy(s => s.Priority))
      {
        if (!MazakPart.IsSailPart(schRow.PartName, schRow.Comment))
          continue;

        var unique = MazakPart.UniqueFromComment(schRow.Comment);

        var job = jdb.LoadJob(unique);
        if (job == null)
          continue;

        var casting = job.Processes[0].Paths[0].Casting;
        if (string.IsNullOrEmpty(casting))
        {
          casting = job.PartName;
        }

        // only if no load or unload action is in process
        bool foundJobAtLoad = false;
        foreach (var action in loadOpers)
        {
          if (
            !string.IsNullOrEmpty(action.Comment)
            && MazakPart.UniqueFromComment(action.Comment) == job.UniqueStr
          )
          {
            foundJobAtLoad = true;
            skippedCastings.Add(casting);
            break;
          }
        }
        if (foundJobAtLoad)
          continue;

        // start building the schedule
        var sch = new ScheduleWithQueues()
        {
          SchRow = schRow,
          Unique = unique,
          Job = job,
          // when we are waiting for all castings to assign, we can assume that any running schedule
          // has all of its material so no need to prevent assignment.
          LowerPriorityScheduleMatchingCastingSkipped =
            !cfg.WaitForAllCastings && skippedCastings.Contains(casting),
          Procs = new Dictionary<int, ScheduleWithQueuesProcess>(),
          NewDueDate = null,
          NewPriority = null,
        };
        bool missingProc = false;
        for (int proc = 1; proc <= job.Processes.Count; proc++)
        {
          MazakScheduleProcessRow schProcRow = null;
          foreach (var row in schRow.Processes)
          {
            if (row.ProcessNumber == proc)
            {
              schProcRow = row;
              break;
            }
          }
          if (schProcRow == null)
          {
            log.Error(
              "Unable to find process {proc} for job {uniq} and schedule {schid}",
              proc,
              job.UniqueStr,
              schRow.Id
            );
            missingProc = true;
            break;
          }
          sch.Procs.Add(
            proc,
            new ScheduleWithQueuesProcess()
            {
              SchProcRow = schProcRow,
              Path = 1,
              InputQueue = job.Processes[proc - 1].Paths[0].InputQueue,
              Casting = proc == 1 ? casting : null,
            }
          );
        }
        if (!missingProc)
        {
          schs.Add(sch);
        }
      }
      return schs;
    }

    private static void CalculateTargetMatQty(
      IRepository logDb,
      IEnumerable<ScheduleWithQueues> schs,
      MazakConfig cfg
    )
    {
      // go through each job and process, and distribute the queued material among the various paths
      // for the job and process.
      foreach (var schsForJob in schs.GroupBy(s => s.Unique))
      {
        var job = schsForJob.First().Job;
        for (int proc = job.Processes.Count; proc >= 1; proc--)
        {
          CheckAssignedMaterial(job, logDb, schsForJob, proc, cfg);
        }
      }
      AssignCastings(logDb, schs, cfg);
    }

    private static void CheckAssignedMaterial(
      Job jobPlan,
      IRepository logDb,
      IEnumerable<ScheduleWithQueues> schsForJob,
      int proc,
      MazakConfig cfg
    )
    {
      // The goal here is to keep the contents of the queue syncronized with the count of material
      // inside the schedule.

      foreach (var sch in schsForJob)
      {
        var schProc = sch.Procs[proc];
        if (!ShouldSyncronizeJobProcess(sch.Job, proc: proc, cfg))
          continue;

        var matInQueue = QueuedMaterialForLoading(
          jobPlan.UniqueStr,
          logDb.GetMaterialInQueueByUnique(schProc.InputQueue, jobPlan.UniqueStr),
          proc
        );
        var numMatInQueue = matInQueue.Count;

        if (proc == 1)
        {
          if (cfg.WaitForAllCastings)
          {
            // update FMS Insight queue to match schedule
            if (numMatInQueue < schProc.SchProcRow.ProcessMaterialQuantity)
            {
              // add some new material to queues
              for (int i = numMatInQueue + 1; i <= schProc.SchProcRow.ProcessMaterialQuantity; i++)
              {
                var m = logDb.AllocateMaterialID(
                  sch.Job.UniqueStr,
                  sch.Job.PartName,
                  sch.Job.Processes.Count
                );
                logDb.RecordPathForProcess(m, 1, schProc.Path);
                logDb.RecordAddMaterialToQueue(
                  mat: new EventLogMaterial()
                  {
                    MaterialID = m,
                    Process = 0,
                    Face = 0,
                  },
                  queue: schProc.InputQueue,
                  position: -1,
                  operatorName: null,
                  reason: "CreatedToMatchMazakQuantities"
                );
              }
            }
            else if (numMatInQueue > schProc.SchProcRow.ProcessMaterialQuantity)
            {
              // remove material from queues
              foreach (var m in matInQueue.Skip(schProc.SchProcRow.ProcessMaterialQuantity))
              {
                logDb.RecordRemoveMaterialFromAllQueues(
                  new EventLogMaterial()
                  {
                    MaterialID = m.MaterialID,
                    Process = 1,
                    Face = 0,
                  }
                );
              }
            }
          }
          else
          {
            // check for too many assigned
            var started = CountCompletedOrMachiningStarted(sch);
            if (started + numMatInQueue > sch.SchRow.PlanQuantity)
            {
              logDb.MarkCastingsAsUnallocated(
                matInQueue.Skip(Math.Max(0, sch.SchRow.PlanQuantity - started)).Select(m => m.MaterialID),
                schProc.Casting
              );
              numMatInQueue = Math.Max(0, sch.SchRow.PlanQuantity - started);
            }

            // update schedule to match FMS Insight queue
            if (numMatInQueue != schProc.SchProcRow.ProcessMaterialQuantity)
            {
              schProc.TargetMaterialCount = numMatInQueue;
            }
          }
        }
        else
        {
          // for larger proc, update schedule to match FMS Insight queue
          if (numMatInQueue != schProc.SchProcRow.ProcessMaterialQuantity)
          {
            schProc.TargetMaterialCount = numMatInQueue;
          }
        }
      }

      if (proc == 1)
      {
        // now deal with the non-input-queue raw material. They could have larger material than planned quantity
        // if the schedule has been decremented
        foreach (var sch in schsForJob.Where(s => string.IsNullOrEmpty(s.Procs[1].InputQueue)))
        {
          if (
            sch.SchRow.PlanQuantity <= CountCompletedOrMachiningStarted(sch)
            && sch.Procs[1].SchProcRow.ProcessMaterialQuantity > 0
          )
          {
            sch.Procs[1].TargetMaterialCount = 0;
          }
        }
      }
    }

    private static void AssignCastings(
      IRepository logDb,
      IEnumerable<ScheduleWithQueues> allSchs,
      MazakConfig cfg
    )
    {
      var schsToAssign = allSchs
        .Where(s =>
          !s.LowerPriorityScheduleMatchingCastingSkipped && ShouldSyncronizeJobProcess(s.Job, proc: 1, cfg)
        )
        .OrderBy(s => s.SchRow.DueDate)
        .ThenBy(s => s.SchRow.Priority);

      var skippedCastings = new HashSet<string>();

      foreach (var sch in schsToAssign)
      {
        var schProc1 = sch.Procs[1];
        var started = CountCompletedOrMachiningStarted(sch);
        var curCastings = (schProc1.TargetMaterialCount ?? schProc1.SchProcRow.ProcessMaterialQuantity);

        if (skippedCastings.Contains(schProc1.Casting))
          continue;

        if (started + curCastings < sch.SchRow.PlanQuantity && curCastings < schProc1.SchProcRow.FixQuantity)
        {
          // find some new castings
          var toAdd = cfg.WaitForAllCastings
            ? sch.SchRow.PlanQuantity - started - curCastings
            : schProc1.SchProcRow.FixQuantity - curCastings;

          bool foundEnough = false;
          if (toAdd > 0)
          {
            var allocated = logDb.AllocateCastingsInQueue(
              queue: schProc1.InputQueue,
              casting: schProc1.Casting,
              unique: sch.Unique,
              part: sch.Job.PartName,
              proc1Path: schProc1.Path,
              numProcesses: sch.Job.Processes.Count,
              count: toAdd
            );
            // if not enough material, AllocateCastingsInQueue does nothing and returns an empty list
            if (allocated.Count == toAdd)
            {
              foundEnough = true;
              schProc1.TargetMaterialCount = allocated.Count;

              // if another schedule with a later priority is currently running, adding material to this
              // schedule will interrupt that one.  Instead, increase priority
              var dueDateOfRunningSch = IsLaterPriorityCurrentlyRunning(sch, allSchs);
              if (dueDateOfRunningSch.HasValue)
              {
                if (sch.SchRow.DueDate != dueDateOfRunningSch.Value)
                {
                  sch.NewDueDate = dueDateOfRunningSch.Value;
                }
                sch.NewPriority =
                  allSchs
                    .Where(s => s.SchRow.DueDate == dueDateOfRunningSch.Value)
                    .Max(s => s.SchRow.Priority) + 1;
              }
            }
          }

          if (toAdd > 0 && cfg.WaitForAllCastings && !foundEnough)
          {
            // if we tried to add but didn't have enough, dont let schedules with higher priority
            // snatch up these castings.  If the user wants to run it, they can edit priority
            // in mazak.
            skippedCastings.Add(schProc1.Casting);
          }
        }
      }
    }

    private static MazakWriteData UpdateMazakMaterialCounts(IEnumerable<ScheduleWithQueues> schs)
    {
      var newSchs = new List<MazakScheduleRow>();
      foreach (var sch in schs)
      {
        if (!sch.Procs.Values.Any(p => p.TargetMaterialCount.HasValue))
          continue;
        log.Debug(
          "Updating material on schedule {schId} for job {uniq} to {@sch}",
          sch.SchRow.Id,
          sch.Unique,
          sch
        );

        var newSch = sch.SchRow with
        {
          Command = MazakWriteCommand.ScheduleMaterialEdit,
          Processes = sch.SchRow.Processes.ToList(),
        };

        if (sch.NewDueDate.HasValue)
        {
          newSch = newSch with { DueDate = sch.NewDueDate.Value };
        }
        if (sch.NewPriority.HasValue)
        {
          newSch = newSch with { Priority = sch.NewPriority.Value };
        }

        for (int i = 0; i < newSch.Processes.Count; i++)
        {
          var newProc = newSch.Processes[i];
          var oldProc = sch.Procs[newProc.ProcessNumber];
          if (oldProc.TargetMaterialCount.HasValue)
          {
            newSch.Processes[i] = newProc with
            {
              ProcessMaterialQuantity = oldProc.TargetMaterialCount.Value,
            };
          }
        }

        newSchs.Add(newSch);
      }

      return new MazakWriteData() { Prefix = "Setting material from queues", Schedules = newSchs };
    }

    public static bool ShouldSyncronizeJobProcess(Job j, int proc, MazakConfig cfg)
    {
      if (proc == 1 && !cfg.SynchronizeRawMaterialInQueues)
      {
        return false;
      }

      return !string.IsNullOrEmpty(j.Processes[proc - 1].Paths[0].InputQueue);
    }

    public static List<QueuedMaterial> QueuedMaterialForLoading(
      string jobUniq,
      IEnumerable<QueuedMaterial> materialToSearch,
      int proc
    )
    {
      var mats = new List<QueuedMaterial>();
      foreach (var m in materialToSearch)
      {
        if (m.Unique != jobUniq)
          continue;
        if ((m.NextProcess ?? 1) != proc)
          continue;
        mats.Add(m);
      }
      return mats;
    }

    private static int CountCompletedOrMachiningStarted(ScheduleWithQueues sch)
    {
      // the logic here should match the calculation of RemainingToRun when creating the CurrentStatus ActiveJobs
      var cnt = sch.SchRow.CompleteQuantity;
      foreach (var schProcRow in sch.Procs.Values)
      {
        cnt += schProcRow.SchProcRow.ProcessBadQuantity + schProcRow.SchProcRow.ProcessExecuteQuantity;
        if (schProcRow.SchProcRow.ProcessNumber > 1)
          cnt += schProcRow.TargetMaterialCount ?? schProcRow.SchProcRow.ProcessMaterialQuantity;
      }
      return cnt;
    }

    private static DateTime? IsLaterPriorityCurrentlyRunning(
      ScheduleWithQueues currentSch,
      IEnumerable<ScheduleWithQueues> allSchedules
    )
    {
      var usedFixtureFaces = new HashSet<(string fixture, int face)>();
      var usedPallets = new HashSet<int>();
      foreach (var proc in currentSch.Procs)
      {
        var plannedInfo = currentSch.Job.Processes[proc.Key - 1].Paths[proc.Value.Path - 1];
        if (string.IsNullOrEmpty(plannedInfo.Fixture))
        {
          foreach (var p in plannedInfo.PalletNums)
          {
            usedPallets.Add(p);
          }
        }
        else
        {
          usedFixtureFaces.Add((fixture: plannedInfo.Fixture, face: plannedInfo.Face ?? 1));
        }
      }

      return allSchedules
        .LastOrDefault(s =>
          (
            s.SchRow.DueDate > currentSch.SchRow.DueDate
            || (
              s.SchRow.DueDate == currentSch.SchRow.DueDate && s.SchRow.Priority > currentSch.SchRow.Priority
            )
          )
          && s.Procs.Any(p => p.Value.SchProcRow.ProcessExecuteQuantity > 0)
          && s.Procs.Any(p =>
          {
            var info = s.Job.Processes[p.Key - 1].Paths[p.Value.Path - 1];
            return usedFixtureFaces.Contains((fixture: info.Fixture, face: info.Face ?? 1))
              || info.PalletNums.Any(usedPallets.Contains);
          })
        )
        ?.SchRow.DueDate;
    }
  }
}
