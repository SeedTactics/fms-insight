/* Copyright (c) 2018, John Lenz

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

  public class BuildMazakSchedules
  {

    public static (MazakWriteData, ISet<string>)
      RemoveCompletedAndDecrementSchedules(
        MazakSchedules mazakData,
        bool DecrementPriorityOnDownload)
    {
      //remove all completed production
      var transSet = new MazakWriteData();
      var savedParts = new HashSet<string>();
      foreach (var schRow in mazakData.Schedules)
      {
        var newSchRow = schRow.Clone();
        if (schRow.PlanQuantity == schRow.CompleteQuantity)
        {
          newSchRow.Command = MazakWriteCommand.Delete;
          transSet.Schedules.Add(newSchRow);
        }
        else
        {
          savedParts.Add(schRow.PartName);

          if (DecrementPriorityOnDownload)
          {
            newSchRow.Command = MazakWriteCommand.ScheduleSafeEdit;
            newSchRow.Priority = Math.Max(newSchRow.Priority - 10, 1);
            newSchRow.Processes.Clear();  // no edit to processes, just priority
            transSet.Schedules.Add(newSchRow);
          }
        }
      }
      return (transSet, savedParts);
    }

    public static MazakWriteData AddSchedules(
      MazakSchedulesPartsPallets mazakData,
      IEnumerable<JobPlan> jobs,
      bool UseStartingOffsetForDueDate)
    {
      var transSet = new MazakWriteData();

      var usedScheduleIDs = new HashSet<int>();
      var scheduledParts = new HashSet<string>();
      foreach (var schRow in mazakData.Schedules)
      {
        usedScheduleIDs.Add(schRow.Id);
        scheduledParts.Add(schRow.PartName);
      }

      //now add the new schedule
      foreach (JobPlan part in jobs)
      {
        for (int proc1path = 1; proc1path <= part.GetNumPaths(1); proc1path++)
        {
          if (part.GetPlannedCyclesOnFirstProcess(proc1path) <= 0) continue;

          //check if part exists downloaded
          int downloadUid = -1;
          string mazakPartName = "";
          string mazakComment = "";
          foreach (var partRow in mazakData.Parts)
          {
            if (MazakPart.IsSailPart(partRow.PartName))
            {
              MazakPart.ParseComment(partRow.Comment, out string u, out var ps, out bool m);
              if (u == part.UniqueStr && ps.PathForProc(proc: 1) == proc1path)
              {
                downloadUid = MazakPart.ParseUID(partRow.PartName);
                mazakPartName = partRow.PartName;
                mazakComment = partRow.Comment;
                break;
              }
            }
          }
          if (downloadUid < 0)
          {
            throw new BlackMaple.MachineFramework.BadRequestException(
              "Attempting to create schedule for " + part.UniqueStr + " but a part does not exist");
          }

          if (!scheduledParts.Contains(mazakPartName))
          {
            int schid = FindNextScheduleId(usedScheduleIDs);
            int earlierConflicts = CountEarlierConflicts(part, proc1path, jobs);
            SchedulePart(transSet, schid, mazakPartName, mazakComment, part.NumProcesses, part, proc1path, earlierConflicts, UseStartingOffsetForDueDate);
          }
        }
      }

      if (UseStartingOffsetForDueDate)
        SortSchedulesByDate(transSet);

      return transSet;
    }

    private static void SchedulePart(
      MazakWriteData transSet, int SchID, string mazakPartName, string mazakComment, int numProcess,
      JobPlan part, int proc1path, int earlierConflicts, bool UseStartingOffsetForDueDate)
    {
      var newSchRow = new MazakScheduleRow();
      newSchRow.Command = MazakWriteCommand.Add;
      newSchRow.Id = SchID;
      newSchRow.PartName = mazakPartName;
      newSchRow.PlanQuantity = part.GetPlannedCyclesOnFirstProcess(proc1path);
      newSchRow.CompleteQuantity = 0;
      newSchRow.FixForMachine = 0;
      newSchRow.MissingFixture = 0;
      newSchRow.MissingProgram = 0;
      newSchRow.MissingTool = 0;
      newSchRow.MixScheduleID = 0;
      newSchRow.ProcessingPriority = 0;
      newSchRow.Comment = mazakComment;

      if (UseStartingOffsetForDueDate)
      {
        if (part.GetSimulatedStartingTimeUTC(1, proc1path) != DateTime.MinValue)
        {
          var start = part.GetSimulatedStartingTimeUTC(1, proc1path);
          newSchRow.DueDate = start.ToLocalTime().Date;
          newSchRow.Priority = 91 + Math.Min(earlierConflicts, 9);
        }
        else
        {
          newSchRow.DueDate = DateTime.Today;
          newSchRow.Priority = 91;
        }
      }
      else
      {
        newSchRow.Priority = 75;
        newSchRow.DueDate = DateTime.Parse("1/1/2008 12:00:00 AM");
      }

      bool entireHold = false;
      if (part.HoldEntireJob != null) entireHold = part.HoldEntireJob.IsJobOnHold;
      bool machiningHold = false;
      if (part.HoldMachining(1, proc1path) != null) machiningHold = part.HoldMachining(1, proc1path).IsJobOnHold;
      newSchRow.HoldMode = (int)HoldPattern.CalculateHoldMode(entireHold, machiningHold);

      int matQty = newSchRow.PlanQuantity;

      if (!string.IsNullOrEmpty(part.GetInputQueue(process: 1, path: proc1path)))
      {
        matQty = 0;
      }

      //need to add all the ScheduleProcess rows
      for (int i = 1; i <= numProcess; i++)
      {
        var newSchProcRow = new MazakScheduleProcessRow();
        newSchProcRow.MazakScheduleRowId = SchID;
        newSchProcRow.ProcessNumber = i;
        if (i == 1)
        {
          newSchProcRow.ProcessMaterialQuantity = matQty;
        }
        else
        {
          newSchProcRow.ProcessMaterialQuantity = 0;
        }
        newSchProcRow.ProcessBadQuantity = 0;
        newSchProcRow.ProcessExecuteQuantity = 0;
        newSchProcRow.ProcessMachine = 0;

        newSchRow.Processes.Add(newSchProcRow);
      }

      transSet.Schedules.Add(newSchRow);
    }

    /// Count up how many JobPaths have an earlier simulation start time and also share a fixture/face with the current job
    private static int CountEarlierConflicts(JobPlan jobToCheck, int proc1path, IEnumerable<JobPlan> jobs)
    {
      var startT = jobToCheck.GetSimulatedStartingTimeUTC(process: 1, path: proc1path);
      if (startT == DateTime.MinValue) return 0;

      // first, calculate the fixtures and faces used by the job to check
      var group = jobToCheck.GetPathGroup(process: 1, path: proc1path);
      var usedFixtureFaces = new HashSet<ValueTuple<string, string>>();
      for (int proc = 1; proc <= jobToCheck.NumProcesses; proc++)
      {
        for (int path = 1; path <= jobToCheck.GetNumPaths(proc); path++)
        {
          if (jobToCheck.GetPathGroup(proc, path) != group) continue;
          var (plannedFix, plannedFace) = jobToCheck.PlannedFixture(proc, path);
          if (string.IsNullOrEmpty(plannedFix)) continue;
          usedFixtureFaces.Add((plannedFix, plannedFace.ToString()));
        }
      }

      int earlierConflicts = 0;
      // go through each other job and process 1 path
      foreach (var otherJob in jobs)
      {
        for (var otherProc1Path = 1; otherProc1Path <= otherJob.GetNumPaths(process: 1); otherProc1Path++)
        {
          if (otherJob.UniqueStr == jobToCheck.UniqueStr && proc1path == otherProc1Path) continue;

          // see if the process 1 starting time is later and if so skip the remaining checks
          var otherStart = otherJob.GetSimulatedStartingTimeUTC(process: 1, path: otherProc1Path);
          if (otherStart == DateTime.MinValue) goto checkNextPath;
          if (otherStart >= startT) goto checkNextPath;
          var otherGroup = otherJob.GetPathGroup(process: 1, path: otherProc1Path);

          //the job-path combo starts earlier than the job-path to check, but need to see if it conflicts.

          // go through all processes matching the path group and if a fixture face matches,
          // count it as a conflict.
          for (var otherProc = 1; otherProc <= otherJob.NumProcesses; otherProc++)
          {
            for (var otherPath = 1; otherPath <= otherJob.GetNumPaths(otherProc); otherPath++)
            {
              if (otherJob.GetPathGroup(otherProc, otherPath) != otherGroup) continue;
              var (otherFix, otherFace) = otherJob.PlannedFixture(otherProc, otherPath);
              if (usedFixtureFaces.Contains((otherFix, otherFace.ToString())))
              {
                earlierConflicts += 1;
                goto checkNextPath;
              }
            }
          }

        checkNextPath:;
        }
      }

      return earlierConflicts;
    }

    private static void SortSchedulesByDate(MazakWriteData transSet)
    {
      transSet.Schedules =
        transSet.Schedules
        .OrderBy(x => x.DueDate)
        .ThenBy(x => -x.Priority)
        .ToList();
    }

    private static int FindNextScheduleId(HashSet<int> usedScheduleIds)
    {
      for (int i = 1; i <= 9999; i++)
      {
        if (!usedScheduleIds.Contains(i))
        {
          usedScheduleIds.Add(i);
          return i;
        }
      }
      throw new Exception("All Schedule Ids are currently being used");
    }

  }
}