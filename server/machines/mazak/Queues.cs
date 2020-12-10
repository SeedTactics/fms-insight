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
using Serilog;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface
{
  public interface IQueueSyncFault
  {
    bool CurrentQueueMismatch { get; }
  }

  public class MazakQueues : IQueueSyncFault
  {
    private static ILogger log = Serilog.Log.ForContext<MazakQueues>();

    private IWriteData _transDB;
    private bool _waitForAllCastings;

    public bool CurrentQueueMismatch { get; private set; }

    public MazakQueues(IWriteData trans, bool waitForAllCastings)
    {
      _transDB = trans;
      _waitForAllCastings = waitForAllCastings;
      CurrentQueueMismatch = false;
    }

    public bool CheckQueues(JobDB jobDB, EventLogDB logDB, MazakSchedulesAndLoadActions mazakData)
    {
      if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(3), true))
      {
        log.Debug("Unable to obtain mazak db lock, trying again soon.");
        return false;
      }
      try
      {
        var transSet = CalculateScheduleChanges(jobDB, logDB, mazakData);

        bool changed = false;
        if (transSet != null && transSet.Schedules.Count() > 0)
        {
          _transDB.Save(transSet, "Setting material from queues");
          changed = true;
        }
        CurrentQueueMismatch = false;
        return changed;
      }
      catch (Exception ex)
      {
        CurrentQueueMismatch = true;
        log.Error(ex, "Error checking for new material");
        return true;
      }
      finally
      {
        OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
      }
    }

    public MazakWriteData CalculateScheduleChanges(JobDB jdb, EventLogDB logDb, MazakSchedulesAndLoadActions mazakData)
    {
      IEnumerable<ScheduleWithQueues> schs;
      schs = LoadSchedules(jdb, logDb, mazakData);
      if (!schs.Any()) return null;

      CalculateTargetMatQty(logDb, mazakData, schs);
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
      public JobPlan Job { get; set; }
      public bool LowerPriorityScheduleMatchingCastingSkipped { get; set; }
      public Dictionary<int, ScheduleWithQueuesProcess> Procs { get; set; }
      public DateTime? NewDueDate { get; set; }
      public int? NewPriority { get; set; }
    }

    private IEnumerable<ScheduleWithQueues> LoadSchedules(JobDB jdb, EventLogDB logDB, MazakSchedulesAndLoadActions mazakData)
    {
      var loadOpers = mazakData.LoadActions;
      var schs = new List<ScheduleWithQueues>();
      var pending = logDB.AllPendingLoads();
      var skippedCastings = new HashSet<string>();
      foreach (var schRow in mazakData.Schedules.OrderBy(s => s.DueDate).ThenBy(s => s.Priority))
      {
        if (!MazakPart.IsSailPart(schRow.PartName, schRow.Comment)) continue;

        MazakPart.ParseComment(schRow.Comment, out string unique, out var procToPath, out bool manual);

        var job = jdb.LoadJob(unique);
        if (job == null) continue;

        var casting = job.GetCasting(procToPath.PathForProc(1));
        if (string.IsNullOrEmpty(casting))
        {
          casting = job.PartName;
        }

        // only if no load or unload action is in process
        bool foundJobAtLoad = false;
        foreach (var action in loadOpers)
        {
          if (action.Unique == job.UniqueStr && action.Path == procToPath.PathForProc(action.Process))
          {
            foundJobAtLoad = true;
            skippedCastings.Add(casting);
            break;
          }
        }
        foreach (var pendingLoad in pending)
        {
          var s = pendingLoad.Key.Split(',');
          if (schRow.PartName == s[0])
          {
            skippedCastings.Add(casting);
            foundJobAtLoad = true;
            break;
          }
        }
        if (foundJobAtLoad) continue;

        // start building the schedule
        var sch = new ScheduleWithQueues()
        {
          SchRow = schRow,
          Unique = unique,
          Job = job,
          // when we are waiting for all castings to assign, we can assume that any running schedule
          // has all of its material so no need to prevent assignment.
          LowerPriorityScheduleMatchingCastingSkipped = !_waitForAllCastings && skippedCastings.Contains(casting),
          Procs = new Dictionary<int, ScheduleWithQueuesProcess>(),
          NewDueDate = null,
          NewPriority = null
        };
        bool missingProc = false;
        for (int proc = 1; proc <= job.NumProcesses; proc++)
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
            log.Error("Unable to find process {proc} for job {uniq} and schedule {schid}", proc, job.UniqueStr, schRow.Id);
            missingProc = true;
            break;
          }
          var path = procToPath.PathForProc(proc);
          sch.Procs.Add(proc, new ScheduleWithQueuesProcess()
          {
            SchProcRow = schProcRow,
            Path = path,
            InputQueue = job.GetInputQueue(process: proc, path: path),
            Casting = proc == 1 ? casting : null,
          });
        }
        if (!missingProc)
        {
          schs.Add(sch);
        }
      }
      return schs;
    }

    private void CalculateTargetMatQty(EventLogDB logDb, MazakSchedulesAndLoadActions mazakData, IEnumerable<ScheduleWithQueues> schs)
    {
      // go through each job and process, and distribute the queued material among the various paths
      // for the job and process.
      foreach (var schsForJob in schs.GroupBy(s => s.Unique))
      {
        var job = schsForJob.First().Job;
        for (int proc = job.NumProcesses; proc >= 1; proc--)
        {
          CheckAssignedMaterial(job, logDb, schsForJob, proc);
        }
      }
      AssignCastings(logDb, schs);
    }

    private void CheckAssignedMaterial(JobPlan jobPlan, EventLogDB logDb, IEnumerable<ScheduleWithQueues> schsForJob, int proc)
    {
      foreach (var sch in schsForJob.Where(s => !string.IsNullOrEmpty(s.Procs[proc].InputQueue)))
      {
        var schProc = sch.Procs[proc];
        if (string.IsNullOrEmpty(schProc.InputQueue)) continue;

        var matInQueue = QueuedMaterialForLoading(jobPlan, logDb.GetMaterialInQueue(schProc.InputQueue), proc, schProc.Path, logDb);
        var numMatInQueue = matInQueue.Count;

        if (proc == 1)
        {
          if (_waitForAllCastings)
          {
            // update FMS Insight queue to match schedule
            if (numMatInQueue < schProc.SchProcRow.ProcessMaterialQuantity)
            {
              // add some new material to queues
              for (int i = numMatInQueue + 1; i <= schProc.SchProcRow.ProcessMaterialQuantity; i++)
              {
                var m = logDb.AllocateMaterialID(sch.Job.UniqueStr, sch.Job.PartName, sch.Job.NumProcesses);
                logDb.RecordPathForProcess(m, 1, schProc.Path);
                logDb.RecordAddMaterialToQueue(
                  mat: new EventLogDB.EventLogMaterial() { MaterialID = m, Process = 0, Face = "" },
                  queue: schProc.InputQueue,
                  position: -1
                );
              }
            }
            else if (numMatInQueue > schProc.SchProcRow.ProcessMaterialQuantity)
            {
              // remove material from queues
              foreach (var m in matInQueue.Skip(schProc.SchProcRow.ProcessMaterialQuantity))
              {
                logDb.RecordRemoveMaterialFromAllQueues(
                  new EventLogDB.EventLogMaterial() { MaterialID = m.MaterialID, Process = 1, Face = "" }
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
                matInQueue
                  .Skip(Math.Max(0, sch.SchRow.PlanQuantity - started))
                  .Select(m => m.MaterialID),
                schProc.Casting);
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
        foreach (var sch in schsForJob.Where(s => string.IsNullOrEmpty(s.Procs[proc].InputQueue)))
        {
          if (sch.SchRow.PlanQuantity <= CountCompletedOrMachiningStarted(sch) && sch.Procs[1].SchProcRow.ProcessMaterialQuantity > 0)
          {
            sch.Procs[1].TargetMaterialCount = 0;
          }
        }
      }
    }

    private void AssignCastings(EventLogDB logDb, IEnumerable<ScheduleWithQueues> allSchs)
    {
      var schsToAssign =
        allSchs
        .Where(s => !s.LowerPriorityScheduleMatchingCastingSkipped && !string.IsNullOrEmpty(s.Procs[1].InputQueue))
        .OrderBy(s => s.SchRow.DueDate).ThenBy(s => s.SchRow.Priority);

      var skippedCastings = new HashSet<string>();

      foreach (var sch in schsToAssign)
      {
        var schProc1 = sch.Procs[1];
        var started = CountCompletedOrMachiningStarted(sch);
        var curCastings = (schProc1.TargetMaterialCount ?? schProc1.SchProcRow.ProcessMaterialQuantity);

        if (skippedCastings.Contains(schProc1.Casting)) continue;

        if (started + curCastings < sch.SchRow.PlanQuantity && curCastings < schProc1.SchProcRow.FixQuantity)
        {
          // find some new castings
          var unassignedCastings =
            logDb.GetMaterialInQueue(schProc1.InputQueue)
            .Where(m => string.IsNullOrEmpty(m.Unique) && (m.PartNameOrCasting == schProc1.Casting || m.PartNameOrCasting == sch.Job.PartName))
            .Count();

          var toAdd =
            _waitForAllCastings
            ? sch.SchRow.PlanQuantity - started - curCastings
            : schProc1.SchProcRow.FixQuantity - curCastings;

          if (toAdd > 0 && unassignedCastings >= toAdd)
          {
            var allocated = logDb.AllocateCastingsInQueue(
              queue: schProc1.InputQueue,
              casting: schProc1.Casting,
              unique: sch.Unique,
              part: sch.Job.PartName,
              proc1Path: schProc1.Path,
              numProcesses: sch.Job.NumProcesses,
              count: toAdd);
            if (allocated.Count != toAdd)
            {
              Log.Error("Did not allocate {toAdd} parts from queue! {sch}, {queue}", toAdd, sch, unassignedCastings);
            }
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
                allSchs.Where(s => s.SchRow.DueDate == dueDateOfRunningSch.Value).Max(s => s.SchRow.Priority)
                + 1;
            }
          }

          if (toAdd > 0 && _waitForAllCastings && unassignedCastings < toAdd)
          {
            // if we tried to add but didn't have enough, dont let schedules with higher priority
            // snatch up these castings.  If the user wants to run it, they can edit priority
            // in mazak.
            skippedCastings.Add(schProc1.Casting);
          }
        }
      }
    }

    private MazakWriteData UpdateMazakMaterialCounts(IEnumerable<ScheduleWithQueues> schs)
    {
      var newSchs = new List<MazakScheduleRow>();
      foreach (var sch in schs)
      {
        if (!sch.Procs.Values.Any(p => p.TargetMaterialCount.HasValue)) continue;
        log.Debug("Updating material on schedule {schId} for job {uniq} to {@sch}", sch.SchRow.Id, sch.Unique, sch);

        var newSch = sch.SchRow.Clone();
        newSch.Command = MazakWriteCommand.ScheduleMaterialEdit;
        newSchs.Add(newSch);

        if (sch.NewDueDate.HasValue)
        {
          newSch.DueDate = sch.NewDueDate.Value;
        }
        if (sch.NewPriority.HasValue)
        {
          newSch.Priority = sch.NewPriority.Value;
        }

        foreach (var newProc in newSch.Processes)
        {
          var oldProc = sch.Procs[newProc.ProcessNumber];
          if (oldProc.TargetMaterialCount.HasValue)
          {
            newProc.ProcessMaterialQuantity = oldProc.TargetMaterialCount.Value;
          }
        }
      }

      return new MazakWriteData()
      {
        Schedules = newSchs
      };
    }

    private static int? FindPathGroup(EventLogDB log, JobPlan job, long matId)
    {
      var details = log.GetMaterialDetails(matId);
      if (details.Paths.Count > 0)
      {
        var path = details.Paths.Aggregate((max, v) => max.Key > v.Key ? max : v);
        return job.GetPathGroup(process: path.Key, path: path.Value);
      }
      else
      {
        Log.Warning("Material {matId} has no path groups! {@details}", matId, details);
        return null;
      }
    }

    public static List<EventLogDB.QueuedMaterial> QueuedMaterialForLoading(JobPlan job, IEnumerable<EventLogDB.QueuedMaterial> materialToSearch, int proc, int path, EventLogDB log)
    {
      var mats = new List<EventLogDB.QueuedMaterial>();
      foreach (var m in materialToSearch)
      {
        if (m.Unique != job.UniqueStr) continue;
        if ((log.NextProcessForQueuedMaterial(m.MaterialID) ?? 1) != proc) continue;
        if (job.GetNumPaths(proc) > 1)
        {
          if (FindPathGroup(log, job, m.MaterialID) != job.GetPathGroup(proc, path)) continue;
        }

        mats.Add(m);
      }
      return mats;
    }

    private static int CountCompletedOrMachiningStarted(ScheduleWithQueues sch)
    {
      var cnt = sch.SchRow.CompleteQuantity;
      foreach (var schProcRow in sch.Procs.Values)
      {
        cnt += schProcRow.SchProcRow.ProcessBadQuantity + schProcRow.SchProcRow.ProcessExecuteQuantity;
        if (schProcRow.SchProcRow.ProcessNumber > 1)
          cnt += schProcRow.TargetMaterialCount ?? schProcRow.SchProcRow.ProcessMaterialQuantity;
      }
      return cnt;
    }

    private static DateTime? IsLaterPriorityCurrentlyRunning(ScheduleWithQueues currentSch, IEnumerable<ScheduleWithQueues> allSchedules)
    {
      var usedFixtureFaces = new HashSet<(string fixture, int face)>();
      var usedPallets = new HashSet<string>();
      foreach (var proc in currentSch.Procs)
      {
        var (plannedFix, plannedFace) = currentSch.Job.PlannedFixture(proc.Key, proc.Value.Path);
        if (string.IsNullOrEmpty(plannedFix))
        {
          foreach (var p in currentSch.Job.PlannedPallets(proc.Key, proc.Value.Path))
          {
            usedPallets.Add(p);
          }
        }
        else
        {
          usedFixtureFaces.Add((fixture: plannedFix, face: plannedFace));
        }
      }


      return
        allSchedules
        .LastOrDefault(s =>
          (s.SchRow.DueDate > currentSch.SchRow.DueDate || (s.SchRow.DueDate == currentSch.SchRow.DueDate && s.SchRow.Priority > currentSch.SchRow.Priority))
          &&
          s.Procs.Any(p => p.Value.SchProcRow.ProcessExecuteQuantity > 0)
          &&
          s.Procs.Any(p =>
            usedFixtureFaces.Contains(s.Job.PlannedFixture(p.Key, p.Value.Path))
            ||
            s.Job.PlannedPallets(p.Key, p.Value.Path).Any(usedPallets.Contains)
          )
        )
        ?.SchRow.DueDate;
    }

  }
}