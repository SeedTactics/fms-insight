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
using System.Collections.Generic;
using BlackMaple.MachineWatchInterface;

namespace MazakMachineInterface
{
  public static class modDecrementPlanQty
  {
    //Because we want to lower the Material count, we have a possible loss of data.
    //If we update the mazak schedule entries when the pallet is at the load station,
    //we might overwrite the Mazak system updating those tables.  We would then
    //lose a part because the mazak would update the Complete field, but we would
    //overwrite it.  But the mazak does not update any schedule data until the ready signal/switch
    //is thrown by the operator at the load station, which happens at the end of loading.
    //
    //Mazak has provided another schedule edit command, command 8, which updates the material
    //and bad qtys.  This command will give an error in the following situations:
    //  * There is any pallet with this schedule at the load station.
    //  * Any fields beside the material quantities do not match the current database.  Crucially,
    //    if the execute and complete quantities do not match we get an error.
    //
    //This protects agains the following two sequences of events:
    //
    //  Issue 1:
    //     * Pallet travels to the load station with the expectation of loading a new part, 101, on the pallet.
    //     * Machine Watch loads the read database which has the status before the unload, which has 101 as zero
    //       execute quantites.
    //     * Machine watch sets the material quantities of 101 to zero.
    //
    //  This is an error because mazak told the operator or robot to load a 101 part and we took all the material off.
    //  So mazak will generate an error when we try and edit the schedule for 101.  Note, without mazak we
    //  have no way of knowing that schedule 101 is connected to the pallet at the load station because 101 is
    //  a new part on the pallet.
    //
    //
    //  Issue 2:
    //    * Pallet travels to the load station.
    //    * Machine Watch loads the database which has the status before the unload.
    //    * Load/Unload finished and the pallet leaves the load station.
    //    * Machine Watch tries to write the old data, overwritting what was changed by mazak.
    //
    //  This problem will be detected when we try and write because either the execute quantites will not match,
    //  or if the pallet still has the types of parts after the load, the complete quantity will not match.
    //
    //
    //When we get this error from the database, we don't know if it was Issue 1 or Issue 2.  Issue 2
    //can be solved by reloading the database and retrying right away.  But for Issue 1, we must put the
    //schedule on hold (to prevent more pallets going to the load station) and wait for the pallet to leave.
    //Therefore, we sleep a little bit and try again.

    private const int MaxRetryTimes = 25;

    internal static Dictionary<JobAndPath, int> DecrementPlanQty(DatabaseAccess database)
    {
      var logMessages = new List<string>();
      var currentSet = database.LoadReadOnly();
      var IdsLeftToDecrement = LoadIdsToDecrement(currentSet);

      //If nothing is left, just return the stored decrement values.
      if (IdsLeftToDecrement.Count == 0)
      {
        return LoadDecrementOffsets(currentSet);
      }

      for (int retryCount = 0; retryCount < MaxRetryTimes; retryCount++)
      {

        var idsCopy = new List<ScheduleId>(IdsLeftToDecrement);
        idsCopy.Sort((a, b) =>
        {
          return a.DownloadAttempts.CompareTo(b.DownloadAttempts);
        });

        foreach (var schID in idsCopy)
        {
          if (DecrementSingleSchedule(currentSet, schID.SchId, logMessages, database))
          {
            IdsLeftToDecrement.Remove(schID);
          }
          else
          {
            schID.DownloadAttempts += 1;
          }
        }

        if (IdsLeftToDecrement.Count == 0)
          break;

        HoldSchedules(currentSet, IdsLeftToDecrement, logMessages, database);

        //we run a garbage collection here, so that hopefully it won't be triggered during the decrement.
        //The further in time we are between the load of the readonly set and the current time, the better
        //chance we have of hitting one of the issues and getting an error.
        GC.Collect();

        if (retryCount >= 2)
        {
          System.Threading.Thread.Sleep(TimeSpan.FromSeconds(15));
        }

        currentSet = database.LoadReadOnly();
      }

      if (logMessages.Count > 0)
      {
        throw RoutingInfo.BuildTransactionException("Error decrementing schedule", logMessages);
      }

      return LoadDecrementOffsets(database.LoadReadOnly());
    }

    private class ScheduleId
    {
      public int SchId;
      public int DownloadAttempts;
      public bool PutOnHold;

      public ScheduleId(int s)
      {
        SchId = s;
        DownloadAttempts = 0;
        PutOnHold = false;
      }
    }

    //Load the schedule ids to decrement
    private static List<ScheduleId> LoadIdsToDecrement(ReadOnlyDataSet currentSet)
    {
      //load the list of Schedule IDs that need to be decremented
      var IdsLeftToDecrement = new List<ScheduleId>();

      foreach (ReadOnlyDataSet.ScheduleRow schRow in currentSet.Schedule.Rows)
      {
        //only deal with parts we created
        if (MazakPart.IsSailPart(schRow.PartName))
        {

          //Check if the schedule is manually created, since we don't decrement those
          string unique;
          int path;
          bool manual = false;
          if (!schRow.IsCommentNull())
            MazakPart.ParseComment(schRow.Comment, out unique, out path, out manual);

          if (manual) continue;

          //find the fixQuantity
          int fix = 1;
          foreach (ReadOnlyDataSet.PartProcessRow partProcRow in currentSet.PartProcess.Rows)
          {
            if (partProcRow.PartName == schRow.PartName && partProcRow.ProcessNumber == 1)
            {
              fix = partProcRow.FixQuantity;
              break;
            }
          }

          foreach (ReadOnlyDataSet.ScheduleProcessRow schProcRow in schRow.GetScheduleProcessRows())
          {
            if (schProcRow.ProcessNumber == 1 && schProcRow.ProcessMaterialQuantity >= fix)
            {
              IdsLeftToDecrement.Add(new ScheduleId(schRow.ScheduleID));
            }
          }
        }
      }

      return IdsLeftToDecrement;
    }

    //Try and decrement a single schedule, returning true if we were successful or an unrecoverable error
    //occured and false if we should retry this decrement again.
    private static bool DecrementSingleSchedule(ReadOnlyDataSet currentSet, int schID,
                                                IList<string> logMessages,
                                                DatabaseAccess database)
    {
      //Find the schedule row.
      ReadOnlyDataSet.ScheduleRow schRow = null;
      foreach (ReadOnlyDataSet.ScheduleRow schRow2 in currentSet.Schedule.Rows)
      {
        if (schRow2.ScheduleID == schID)
        {
          schRow = schRow2;
          break;
        }
      }

      if (schRow == null) return true;

      var transSet = new TransactionDataSet();
      TransactionDataSet.Schedule_tRow newSchRow = transSet.Schedule_t.NewSchedule_tRow();
      DatabaseAccess.BuildScheduleEditRow(newSchRow, schRow, true);
      transSet.Schedule_t.AddSchedule_tRow(newSchRow);

      //Clear any holds made from a previous error.
      newSchRow.HoldMode = 0;

      int numRows = 0;

      foreach (ReadOnlyDataSet.ScheduleProcessRow schProcRow in schRow.GetScheduleProcessRows())
      {
        var newSchProcRow = transSet.ScheduleProcess_t.NewScheduleProcess_tRow();
        DatabaseAccess.BuildScheduleProcEditRow(newSchProcRow, schProcRow);
        transSet.ScheduleProcess_t.AddScheduleProcess_tRow(newSchProcRow);

        newSchProcRow.ProcessBadQuantity = 0;

        if (schProcRow.ProcessNumber == 1)
        {
          newSchProcRow.ProcessMaterialQuantity = 0;
        }

        numRows += 1;
      }

      var log = new List<string>();
      database.SaveTransaction(transSet, log, "Decrement", numRows + 1);
      if (log.Count > 0)
      {
        if (log.Count == 1 && log[0].ToString().StartsWith("Mazak transaction returned error 8"))
        {
          return false;
        }
        else
        {
          foreach (string s in log)
          {
            logMessages.Add(s);
          }
          //we return that true because we don't want to retry this because we got some other error.
          //Eventually, an exception will be thrown with the values in logMessages.
          return true;
        }
      }
      else
      {
        return true;
      }
    }


    private static void HoldSchedules(ReadOnlyDataSet currentSet, IList<ScheduleId> schIds,
                                      IList<string> logMessages,
                                      DatabaseAccess database)
    {
      TransactionDataSet transSet = new TransactionDataSet();

      var schIdsPutOnHold = new List<ScheduleId>();

      foreach (var schId in schIds)
      {
        //If we have tried two or more times, put the schedule on hold.  This is so that
        //if we get Issue 1 the first time we don't put the schedule on hold.
        if (schId.DownloadAttempts >= 2 && !schId.PutOnHold)
        {

          //Find the schedule row.
          ReadOnlyDataSet.ScheduleRow schRow = null;
          foreach (ReadOnlyDataSet.ScheduleRow schRow2 in currentSet.Schedule.Rows)
          {
            if (schRow2.ScheduleID == schId.SchId)
            {
              schRow = schRow2;
              break;
            }
          }

          if (schRow == null) continue;

          if (schRow.HoldMode != (int)HoldPattern.HoldMode.FullHold)
          {
            TransactionDataSet.Schedule_tRow newSchRow = transSet.Schedule_t.NewSchedule_tRow();
            DatabaseAccess.BuildScheduleEditRow(newSchRow, schRow, false);
            newSchRow.HoldMode = (int)HoldPattern.HoldMode.FullHold;
            transSet.Schedule_t.AddSchedule_tRow(newSchRow);
          }

          schIdsPutOnHold.Add(schId);
        }
      }

      if (transSet.Schedule_t.Rows.Count > 0)
      {
        database.SaveTransaction(transSet, logMessages, "Decrement Unhold", 10);
      }

      foreach (var schId in schIdsPutOnHold)
        schId.PutOnHold = true;
    }

    private static Dictionary<JobAndPath, int> LoadDecrementOffsets(ReadOnlyDataSet currentSet)
    {
      var parts = new Dictionary<JobAndPath, int>();

      foreach (ReadOnlyDataSet.ScheduleRow schRow in currentSet.Schedule.Rows)
      {
        if (MazakPart.IsSailPart(schRow.PartName))
        {

          int cnt = schRow.CompleteQuantity;
          foreach (ReadOnlyDataSet.ScheduleProcessRow schProcRow in schRow.GetScheduleProcessRows())
          {
            cnt += schProcRow.ProcessMaterialQuantity;
            cnt += schProcRow.ProcessExecuteQuantity;
            cnt += schProcRow.ProcessBadQuantity;
          }

          if (cnt < schRow.PlanQuantity)
          {
            int numRemove = schRow.PlanQuantity - cnt;

            string jobUnique = "";
            int path = 1;
            bool manual = false;
            if (!schRow.IsCommentNull())
            {
              MazakPart.ParseComment(schRow.Comment, out jobUnique, out path, out manual);
            }
            var decrKey = new JobAndPath(jobUnique, path);

            if (parts.ContainsKey(decrKey))
            {
              parts[decrKey] = parts[decrKey] + numRemove;
            }
            else
            {
              parts.Add(decrKey, numRemove);
            }
          }

        }
      }

      return parts;
    }

    public static void FinalizeDecement(DatabaseAccess database)
    {
      //We  just lower the plan count (which we can do with the SafeEditCommand), so this is much
      //easier than the above.

      ReadOnlyDataSet currentSet = database.LoadReadOnly();
      TransactionDataSet transSet = new TransactionDataSet();
      TransactionDataSet.Schedule_tRow newSchRow = null;
      List<string> log = new List<string>();

      database.ClearTransactionDatabase();

      //now we update all the plan quantites to match
      foreach (ReadOnlyDataSet.ScheduleRow schRow in currentSet.Schedule.Rows)
      {
        if (MazakPart.IsSailPart(schRow.PartName))
        {
          //only deal with schedules we have created
          int cnt = RoutingInfo.CountMaterial(schRow);
          if (schRow.PlanQuantity != cnt)
          {
            newSchRow = transSet.Schedule_t.NewSchedule_tRow();
            DatabaseAccess.BuildScheduleEditRow(newSchRow, schRow, false);
            newSchRow.PlanQuantity = cnt;
            newSchRow.HoldMode = 0;
            transSet.Schedule_t.AddSchedule_tRow(newSchRow);
          }
        }
      }

      if (transSet.Schedule_t.Rows.Count > 0)
      {
        database.SaveTransaction(transSet, log, "Decrement Finalize");
      }

      if (log.Count > 0)
      {
        throw RoutingInfo.BuildTransactionException("Error finalizing decrement", log);
      }
    }
  }
}
