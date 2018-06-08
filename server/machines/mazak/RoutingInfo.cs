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
using System.Linq;
using System.Data;
using System.Diagnostics;
using BlackMaple.MachineWatchInterface;

namespace MazakMachineInterface
{
  public class RoutingInfo : IJobControl, IOldJobDecrement
  {
    private TransactionDatabaseAccess database;
    private IReadDataAccess readDatabase;
    private HoldPattern hold;
    private BlackMaple.MachineFramework.JobDB jobDB;
    private BlackMaple.MachineFramework.JobLogDB log;
    private BlackMaple.MachineFramework.InspectionDB inspDb;
    private LoadOperations loadOper;
    private System.Timers.Timer _copySchedulesTimer;

    public bool UseStartingOffsetForDueDate;
    public bool DecrementPriorityOnDownload;
    public bool CheckPalletsUsedOnce;

    public const int JobLookbackHours = 2 * 24;

    private readonly System.Diagnostics.TraceSource trace;

    public event NewCurrentStatus OnNewCurrentStatus;
    public void RaiseNewCurrentStatus(CurrentStatus s) => OnNewCurrentStatus?.Invoke(s);

    public RoutingInfo(TransactionDatabaseAccess d, IReadDataAccess readDb, HoldPattern h,
        BlackMaple.MachineFramework.JobDB jDB, BlackMaple.MachineFramework.JobLogDB jLog,
        BlackMaple.MachineFramework.InspectionDB iDb, LoadOperations lOper,
    bool check, bool useStarting, bool decrPriority, System.Diagnostics.TraceSource t)
    {
      database = d;
      readDatabase = readDb;
      hold = h;
      jobDB = jDB;
      log = jLog;
      inspDb = iDb;
      loadOper = lOper;
      CheckPalletsUsedOnce = check;
      UseStartingOffsetForDueDate = useStarting;
      DecrementPriorityOnDownload = decrPriority;
      trace = t;

      _copySchedulesTimer = new System.Timers.Timer(TimeSpan.FromMinutes(4.5).TotalMilliseconds);
      _copySchedulesTimer.Elapsed += (sender, args) => RecopyJobsToSystem();
      _copySchedulesTimer.Start();
    }

    public void Halt()
    {
      _copySchedulesTimer.Stop();
    }

    #region Reading
    private static void CalculateMaxProcAndPath(ReadOnlyDataSet mazakSet, TraceSource trace,
                                               out Dictionary<string, int> uniqueToMaxPath,
                                               out Dictionary<string, int> uniqueToMaxProcess)
    {
      uniqueToMaxPath = new Dictionary<string, int>();
      uniqueToMaxProcess = new Dictionary<string, int>();
      foreach (ReadOnlyDataSet.PartRow partRow in mazakSet.Part.Rows)
      {
        if (MazakPart.IsSailPart(partRow.PartName) && !partRow.IsCommentNull())
        {
          string jobUnique = "";
          int path = 1;
          bool manual = false;
          int numProc = partRow.GetPartProcessRows().Length;

          MazakPart.ParseComment(partRow.Comment, out jobUnique, out path, out manual);

          if (uniqueToMaxPath.ContainsKey(jobUnique))
          {
            uniqueToMaxPath[jobUnique] = Math.Max(uniqueToMaxPath[jobUnique], path);
          }
          else
          {
            uniqueToMaxPath.Add(jobUnique, path);
          }

          if (uniqueToMaxProcess.ContainsKey(jobUnique))
          {
            if (trace != null && numProc != uniqueToMaxProcess[jobUnique])
            {
              trace.TraceEvent(TraceEventType.Warning, 0, "Paths for " + jobUnique + " have a different number of processes");
            }
          }
          else
          {
            uniqueToMaxProcess.Add(jobUnique, numProc);
          }
        }
      }
    }

    private static void AddRoutingToJob(ReadOnlyDataSet mazakSet, ReadOnlyDataSet.PartRow partRow, JobPlan job, int path, DatabaseAccess.MazakDbType mazakTy)
    {
      //Add routing and pallets
      foreach (ReadOnlyDataSet.PartProcessRow partProcRow in partRow.GetPartProcessRows())
      {
        job.SetPartsPerPallet(partProcRow.ProcessNumber, path, partProcRow.FixQuantity);
        job.SetPathGroup(partProcRow.ProcessNumber, path, path);

        //Routing
        string fixStr = partProcRow.FixLDS;
        string cutStr = partProcRow.CutMc;
        string removeStr = partProcRow.RemoveLDS;

        if (mazakTy != DatabaseAccess.MazakDbType.MazakVersionE)
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
              routeStop = new JobMachiningStop("MC");
              job.AddMachiningStop(partProcRow.ProcessNumber, path, routeStop);
            }
            routeStop.AddProgram(int.Parse(c.ToString()), "");
          }
        }

        //Planned Pallets
        foreach (ReadOnlyDataSet.PalletRow palRow in mazakSet.Pallet.Rows)
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

    private void AddDataFromJobDB(JobPlan jobFromMazak)
    {
      var jobFromDb = jobDB.LoadJob(jobFromMazak.UniqueStr);
      if (jobFromDb == null) return;

      jobFromMazak.RouteStartingTimeUTC = jobFromDb.RouteStartingTimeUTC;
      jobFromMazak.RouteEndingTimeUTC = jobFromDb.RouteEndingTimeUTC;
      jobFromMazak.ScheduleId = jobFromDb.ScheduleId;
      foreach (var b in jobFromDb.ScheduledBookingIds)
        jobFromMazak.ScheduledBookingIds.Add(b);
      for (int proc = 1; proc <= jobFromMazak.NumProcesses; proc++) {
        for (int path = 1; path <= jobFromMazak.GetNumPaths(proc); path++) {
          if (proc > jobFromDb.NumProcesses || path > jobFromDb.GetNumPaths(proc))
            continue;

          jobFromMazak.SetSimulatedStartingTimeUTC(proc, path,
            jobFromDb.GetSimulatedStartingTimeUTC(proc, path));
          jobFromMazak.SetSimulatedAverageFlowTime(proc, path,
            jobFromDb.GetSimulatedAverageFlowTime(proc, path));
          jobFromMazak.SetSimulatedProduction(proc, path,
            jobFromDb.GetSimulatedProduction(proc, path));

          var mazakStops = jobFromMazak.GetMachiningStop(proc, path).ToList();
          var dbStops = jobFromDb.GetMachiningStop(proc, path).ToList();
          for (int i = 0; i < Math.Min(mazakStops.Count, dbStops.Count); i++) {
            mazakStops[i].StationGroup = dbStops[i].StationGroup;
            mazakStops[i].ExpectedCycleTime = dbStops[i].ExpectedCycleTime;
          }
        }
      }

    }

    /* This loads a JobPlan which contains the routing for the given unique string.  Not all fields in the plan are filled in,
		 * just the routing.  This is useful for inspection decisions, since when translating inspection counters we only need the routing */
    public static JobPlan RoutingForUnique(ReadOnlyDataSet mazakSet, string unique, DatabaseAccess.MazakDbType mazakTy)
    {
      Dictionary<string, int> uniqueToMaxPath;
      Dictionary<string, int> uniqueToMaxProcess;
      CalculateMaxProcAndPath(mazakSet, null, out uniqueToMaxPath, out uniqueToMaxProcess);

      if (!uniqueToMaxProcess.ContainsKey(unique))
        return null;
      if (!uniqueToMaxPath.ContainsKey(unique))
        return null;

      int numProc = uniqueToMaxProcess[unique];
      int maxPath = uniqueToMaxPath[unique];

      var paths = new int[numProc];
      for (int i = 0; i < numProc; i++)
        paths[i] = maxPath;
      var job = new JobPlan(unique, numProc, paths);

      foreach (ReadOnlyDataSet.PartRow partRow in mazakSet.Part.Rows)
      {
        if (partRow.IsCommentNull())
          continue;

        string jobUnique = "";
        int path = 1;
        bool manual = false;
        MazakPart.ParseComment(partRow.Comment, out jobUnique, out path, out manual);

        if (unique == jobUnique)
          AddRoutingToJob(mazakSet, partRow, job, path, mazakTy);
      }

      return job;
    }

    public CurrentStatus GetCurrentStatus()
    {
      ReadOnlyDataSet mazakSet = null;
      if (!database.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      try
      {
        mazakSet = readDatabase.LoadReadOnly();
      }
      finally
      {
        database.MazakTransactionLock.ReleaseMutex();
      }

      return GetCurrentStatus(mazakSet);
    }

    public CurrentStatus GetCurrentStatus(ReadOnlyDataSet mazakSet)
    {

      //Load process and path numbers
      Dictionary<string, int> uniqueToMaxPath;
      Dictionary<string, int> uniqueToMaxProcess;
      CalculateMaxProcAndPath(mazakSet, trace, out uniqueToMaxPath, out uniqueToMaxProcess);

      var currentLoads = new List<LoadAction>(loadOper.CurrentLoadActions());

      var curStatus = new CurrentStatus();

      var jobsBySchID = new Dictionary<long, InProcessJob>();
      var pathBySchID = new Dictionary<long, int>();

      foreach (ReadOnlyDataSet.ScheduleRow schRow in mazakSet.Schedule.Rows)
      {
        if (!MazakPart.IsSailPart(schRow.PartName))
          continue;

        ReadOnlyDataSet.PartRow partRow = null;
        foreach (ReadOnlyDataSet.PartRow p in mazakSet.Part.Rows)
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
        int path = 1;
        bool manual = false;
        if (!partRow.IsCommentNull())
          MazakPart.ParseComment(partRow.Comment, out jobUnique, out path, out manual);

        if (!uniqueToMaxProcess.ContainsKey(jobUnique))
          continue;

        int numProc = uniqueToMaxProcess[jobUnique];
        int maxPath = uniqueToMaxPath[jobUnique];

        InProcessJob job;

        //Create or lookup the job
        if (curStatus.Jobs.ContainsKey(jobUnique))
        {
          job = curStatus.Jobs[jobUnique];
        }
        else
        {
          var paths = new int[numProc];
          for (int i = 0; i < numProc; i++)
            paths[i] = maxPath;
          job = new InProcessJob(jobUnique, numProc, paths);
          job.PartName = partName;
          job.JobCopiedToSystem = true;
          job.AddInspections(jobDB.LoadInspections(job.UniqueStr));
          curStatus.Jobs.Add(jobUnique, job);
        }
        jobsBySchID.Add(schRow.ScheduleID, job);
        pathBySchID.Add(schRow.ScheduleID, path);

        //Job Basics
        job.SetPlannedCyclesOnFirstProcess(path, schRow.PlanQuantity);
        job.SetCompleted(numProc, path, schRow.CompleteQuantity);
        job.Priority = schRow.Priority;

        if (((HoldPattern.HoldMode)schRow.HoldMode) == HoldPattern.HoldMode.FullHold)
          job.HoldEntireJob.UserHold = true;
        else
          job.HoldEntireJob.UserHold = false;
        hold.LoadHoldIntoJob(schRow.ScheduleID, job, path);

        AddRoutingToJob(mazakSet, partRow, job, path, database.MazakType);
      }

      foreach (var j in jobsBySchID.Values)
        AddDataFromJobDB(j);

      //Now add pallets

      foreach (ReadOnlyDataSet.PalletRow palRow in mazakSet.Pallet.Rows)
      {
        if (palRow.PalletNumber > 0 && !curStatus.Pallets.ContainsKey(palRow.PalletNumber.ToString()))
        {

          var palName = palRow.PalletNumber.ToString();
          var palLoc = FindPalletLocation(mazakSet, palRow.PalletNumber);

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
          foreach (ReadOnlyDataSet.PalletSubStatusRow palSub in mazakSet.PalletSubStatus.Rows)
          {
            if (palSub.PalletNumber != palRow.PalletNumber)
              continue;
            if (palSub.FixQuantity <= 0)
              continue;
            if (!jobsBySchID.ContainsKey(palSub.ScheduleID))
              continue;

            status.NumFaces = Math.Max(status.NumFaces, palSub.PartProcessNumber);

            var job = jobsBySchID[palSub.ScheduleID];
            var path = pathBySchID[palSub.ScheduleID];

            var matIDs = new Queue<long>(FindMatIDsFromOldCycles(oldCycles, job.UniqueStr, palSub.PartProcessNumber));

            for (int i = 1; i <= palSub.FixQuantity; i++)
            {
              int face = palSub.PartProcessNumber;
              long matID = -1;
              if (matIDs.Count > 0)
                matID = matIDs.Dequeue();

              var inProcMat = new InProcessMaterial()
              {
                MaterialID = matID,
                JobUnique = job.UniqueStr,
                PartName = job.PartName,
                Process = palSub.PartProcessNumber,
                Path = path,
                Serial = log.SerialForMaterialID(matID),
                WorkorderId = log.WorkorderForMaterialID(matID),
                SignaledInspections =
                      inspDb.LookupInspectionDecisions(matID)
                      .Where(x => x.Inspect)
                      .Select(x => x.InspType)
                      .ToList(),
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
                inProcMat.Action = new InProcessMaterialAction()
                {
                  Type = InProcessMaterialAction.ActionType.Loading,
                  LoadOntoFace = palSub.PartProcessNumber + 1,
                  LoadOntoPallet = status.Pallet,
                  ProcessAfterLoad = palSub.PartProcessNumber + 1,
                  PathAfterLoad = path,
                };
              }
              else if (unload != null)
              {
                inProcMat.Action = new InProcessMaterialAction()
                {
                  Type =
                        palSub.PartProcessNumber == job.NumProcesses
                            ? InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
                            : InProcessMaterialAction.ActionType.UnloadToInProcess
                };
              }
            }
          }

          if (palLoc.Location == PalletLocationEnum.LoadUnload)
          {
            AddLoads(currentLoads, status.Pallet, palLoc, curStatus);
            AddUnloads(currentLoads, status, oldCycles, curStatus);
          }
        }
      }



      //now lookup the global string
      foreach (ReadOnlyDataSet.FixtureRow fixRow in mazakSet.Fixture.Rows)
      {
        if (fixRow.FixtureName.ToLower() == "fixture:uniquestr")
        {
          if (!fixRow.IsCommentNull())
            curStatus.LatestScheduleId = fixRow.Comment;
          break;
        }
      }

      var notCopied = jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddHours(-JobLookbackHours), DateTime.UtcNow);
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

      return curStatus;
    }

    private LoadAction CheckLoadOfNextProcess(List<LoadAction> currentLoads, string unique, int process, PalletLocation loc)
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
          if (act.Qty == 1)  {
            currentLoads.Remove(act);
          } else {
            act.Qty -= 1;
          }
          return act;
        }
      }
      return null;
    }

    private LoadAction CheckUnload(List<LoadAction> currentLoads, string unique, int process, PalletLocation loc)
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
          if (act.Qty == 1) {
            currentLoads.Remove(act);
          } else {
            act.Qty -= 1;
          }
          return act;
        }
      }
      return null;
    }

    private void AddLoads(IEnumerable<LoadAction> currentLoads, string pallet, PalletLocation palLoc, CurrentStatus curStatus)
    {
      //process remaining loads/unloads (already processed ones have been removed from currentLoads)
      foreach (var operation in currentLoads)
      {
        if (!operation.LoadEvent || operation.LoadStation != palLoc.Num) continue;
        for (int i = 0; i < operation.Qty; i++)
        {
          var inProcMat = new InProcessMaterial()
          {
            MaterialID = -1,
            JobUnique = operation.Unique,
            PartName = operation.Part,
            Process = operation.Process,
            Path = operation.Path,
            Location = new InProcessMaterialLocation()
            {
              Type = InProcessMaterialLocation.LocType.Free,
            },
            Action = new InProcessMaterialAction()
            {
              Type = InProcessMaterialAction.ActionType.Loading,
              LoadOntoPallet = pallet,
              LoadOntoFace = operation.Process,
              ProcessAfterLoad = operation.Process,
              PathAfterLoad = operation.Path
            }
          };
          curStatus.Material.Add(inProcMat);
        }
      }
    }

    private void AddUnloads(IEnumerable<LoadAction> currentActions, PalletStatus pallet, List<BlackMaple.MachineWatchInterface.LogEntry> oldCycles, CurrentStatus status)
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


          var inProcMat = new InProcessMaterial()
          {
            MaterialID = matID,
            JobUnique = unload.Unique,
            PartName = unload.Part,
            Process = unload.Process,
            Path = unload.Path,
            Serial = log.SerialForMaterialID(matID),
            WorkorderId = log.WorkorderForMaterialID(matID),
            SignaledInspections =
                          inspDb.LookupInspectionDecisions(matID)
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
              Type =
                      job != null && unload.Process == job.NumProcesses
                          ? InProcessMaterialAction.ActionType.UnloadToCompletedMaterial
                          : InProcessMaterialAction.ActionType.UnloadToInProcess
            }
          };
          status.Material.Add(inProcMat);
        }
      }
    }

    private IEnumerable<long> FindMatIDsFromOldCycles(IEnumerable<BlackMaple.MachineWatchInterface.LogEntry> oldCycles, string unique, int proc)
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

    private PalletLocation FindPalletLocation(ReadOnlyDataSet readSet, int palletNum)
    {
      foreach (ReadOnlyDataSet.PalletPositionRow palLocRow in readSet.PalletPosition.Rows)
      {
        if (palLocRow.PalletNumber == palletNum)
        {
          if (database.MazakType != DatabaseAccess.MazakDbType.MazakVersionE)
          {
            return ParseStatNameWeb(palLocRow.PalletPosition);
          }
          else
          {
            return ParseStatNameVerE(palLocRow.PalletPosition);
          }
        }
      }

      return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 0);
    }

    private PalletLocation ParseStatNameVerE(string pos)
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
        ret.StationGroup = "MC";
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

    private PalletLocation ParseStatNameWeb(string pos)
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
        ret.StationGroup = "MC";
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
    #endregion

    #region "Write Routing Info"

    public List<string> CheckValidRoutes(IEnumerable<JobPlan> jobs)
    {
      var logMessages = new List<string>();
      var traceMessages = new List<string>();
      ReadOnlyDataSet currentSet = null;

      if (!database.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      try
      {
        currentSet = readDatabase.LoadReadOnly();
      }
      finally
      {
        database.MazakTransactionLock.ReleaseMutex();
      }

      try
      {
        trace.TraceEvent(TraceEventType.Information, 0, "Check valid routing info");

        //The reason we create the clsPalletPartMapping is to see if it throws any exceptions.  We therefore
        //need to ignore the warning that palletPartMap is not used.
#pragma warning disable 168, 219
        var palletPartMap = new clsPalletPartMapping(jobs, currentSet, 1,
                                                     new List<string>(), logMessages, traceMessages, false, "",
                                                     CheckPalletsUsedOnce, database.MazakType);
#pragma warning restore 168, 219

      }
      catch (Exception ex)
      {
        if (ex.Message.StartsWith("Invalid pallet->part mapping"))
        {

          logMessages.Add(ex.Message);
        }
        else
        {
          throw;
        }
      }

      trace.Flush();

      return logMessages;
    }

    public void AddJobs(NewJobs newJ, string expectedPreviousScheduleId)
    {
      if (!database.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      try
      {
        database.ClearTransactionDatabase();

        ReadOnlyDataSet currentSet = readDatabase.LoadReadOnly();
        List<string> logMessages = new List<string>();

        //check for an old schedule that has not yet been copied
        var oldJobs = jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddHours(1));
        if (oldJobs.Jobs.Count > 0)
        {
          //there are jobs to copy
          trace.TraceEvent(TraceEventType.Warning, 0, "Resuming copy of job schedules into mazak: "
              + String.Join(",", oldJobs.Jobs.Select(j => j.UniqueStr).ToArray()));

          //find UID.  This relies on their being only one schedule to copy at a time
          int UID = 0;
          foreach (ReadOnlyDataSet.FixtureRow fixRow in currentSet.Fixture.Rows)
          {
            if (!fixRow.IsCommentNull() && fixRow.Comment == oldJobs.LatestScheduleId)
            {
              UID = MazakPart.ParseUID(fixRow.FixtureName);
              if (UID >= 0)
              {
                break;
              }
            }
          }
          var usedScheduleIDs = new List<int>();
          foreach (ReadOnlyDataSet.ScheduleRow schRow in currentSet.Schedule.Rows)
          {
            usedScheduleIDs.Add(schRow.ScheduleID);
          }
          AddSchedules(oldJobs.Jobs, usedScheduleIDs, UID, currentSet, logMessages,
              p => FindNumberProcesses(currentSet, p));
          UpdateGlobalUniqueStr(currentSet, logMessages, oldJobs.LatestScheduleId);
          hold.SignalNewSchedules();

          if (logMessages.Count > 0)
          {
            throw new ApplicationException(
                 "Error copying job schedules to mazak " +
                 String.Join(Environment.NewLine, logMessages.ToArray()));
          }

          //reload current set, because schedules have been added
          currentSet = readDatabase.LoadReadOnly();
        }

        if (!string.IsNullOrEmpty(newJ.ScheduleId))
        {
          //check for already downloaded scheduleId.
          var recentDbSchedule = jobDB.LoadMostRecentSchedule();
          if (string.Compare(recentDbSchedule.LatestScheduleId, newJ.ScheduleId) >= 0)
          {
            //this schedule has been already downloaded
            return;
          }
          if (!string.IsNullOrEmpty(expectedPreviousScheduleId) &&
              expectedPreviousScheduleId != recentDbSchedule.LatestScheduleId)
          {
            throw new ApplicationException("Expected previous schedule id does not match current schedule id");
          }
        }


        //add fixtures, pallets, parts.  If this fails, just throw an exception,
        //they will be deleted during the next download.
        var palPartMap = AddFixturesPalletsParts(newJ, currentSet, logMessages, newJ.ScheduleId);

        if (logMessages.Count > 0)
        {
          throw BuildTransactionException("Error downloading routing info", logMessages);
        }

        //Now that the parts have been added and we are confident that there no problems with the jobs,
        //add them to the database.  Once this occurrs, the timer will pick up and eventually
        //copy them to the system
        AddJobsToDB(newJ);

        {
          var usedScheduleIDs = new List<int>();
          foreach (ReadOnlyDataSet.ScheduleRow schRow in currentSet.Schedule.Rows)
          {
            usedScheduleIDs.Add(schRow.ScheduleID);
          }
          AddSchedules(newJ.Jobs, usedScheduleIDs, palPartMap.Uid, currentSet, logMessages,
              p => palPartMap.GetNumberProcesses(p));
          UpdateGlobalUniqueStr(currentSet, logMessages, newJ.ScheduleId);
          hold.SignalNewSchedules();
        }
      }
      finally
      {
        try
        {
          database.ClearTransactionDatabase();
        }
        catch
        {
        }
        database.MazakTransactionLock.ReleaseMutex();
      }
    }

    public void RecopyJobsToSystem()
    {
      if (!database.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }
      try
      {
        var jobs = jobDB.LoadJobsNotCopiedToSystem(DateTime.UtcNow.AddHours(-JobLookbackHours), DateTime.UtcNow.AddHours(1));
        if (jobs.Jobs.Count == 0) return;

        //there are jobs to copy
        trace.TraceEvent(TraceEventType.Warning, 0, "Resuming copy of job schedules into mazak: "
            + String.Join(",", jobs.Jobs.Select(j => j.UniqueStr).ToArray()));

        database.ClearTransactionDatabase();

        ReadOnlyDataSet currentSet = readDatabase.LoadReadOnly();
        List<string> logMessages = new List<string>();

        try
        {
          var usedScheduleIDs = new List<int>();
          foreach (ReadOnlyDataSet.ScheduleRow schRow in currentSet.Schedule.Rows)
          {
            usedScheduleIDs.Add(schRow.ScheduleID);
          }

          int UID = 0;
          foreach (ReadOnlyDataSet.FixtureRow fixRow in currentSet.Fixture.Rows)
          {
            if (!fixRow.IsCommentNull() && fixRow.Comment == jobs.LatestScheduleId)
            {
              //found a download that was in progress...
              UID = MazakPart.ParseUID(fixRow.FixtureName);
              if (UID >= 0)
              {
                break;
              }
            }
          }
          AddSchedules(jobs.Jobs, usedScheduleIDs, UID, currentSet, logMessages,
              p => FindNumberProcesses(currentSet, p));

          UpdateGlobalUniqueStr(currentSet, logMessages, jobs.LatestScheduleId);

          hold.SignalNewSchedules();

          if (logMessages.Count > 0)
          {
            trace.TraceEvent(TraceEventType.Error, 0,
                "Error copying job schedules to mazak " +
                String.Join(Environment.NewLine, logMessages.ToArray()));
          }
        }
        catch (Exception ex)
        {
          trace.TraceEvent(TraceEventType.Error, 0,
              "Error copying job schedules to mazak " + ex.ToString());
        }

      }
      finally
      {
        try
        {
          database.ClearTransactionDatabase();
        }
        catch
        {
        }
        database.MazakTransactionLock.ReleaseMutex();
      }
    }

    private clsPalletPartMapping AddFixturesPalletsParts(
            NewJobs newJ,
        ReadOnlyDataSet currentSet,
            IList<string> logMessages,
        string newGlobal)
    {
      TransactionDataSet transSet = new TransactionDataSet();

      int UID = 0;
      var savedParts = new List<string>();

      //first allocate a UID to use for this download
      UID = 0;
      while (UID < int.MaxValue)
      {
        //check schedule rows for UID
        foreach (ReadOnlyDataSet.ScheduleRow schRow in currentSet.Schedule.Rows)
        {
          if (MazakPart.ParseUID(schRow.PartName) == UID)
            goto found;
        }

        //check fixture rows for UID
        foreach (ReadOnlyDataSet.FixtureRow fixRow in currentSet.Fixture.Rows)
        {
          if (MazakPart.ParseUID(fixRow.FixtureName) == UID)
            goto found;
        }

        break;
      found:
        UID += 1;
      }
      if (UID == int.MaxValue)
      {
        throw new Exception("Unable to find unused UID");
      }

      //remove all completed production
      foreach (ReadOnlyDataSet.ScheduleRow schRow in currentSet.Schedule.Rows)
      {
        TransactionDataSet.Schedule_tRow newSchRow = transSet.Schedule_t.NewSchedule_tRow();
        if (schRow.PlanQuantity == schRow.CompleteQuantity)
        {
          newSchRow.Command = TransactionDatabaseAccess.DeleteCommand;
          newSchRow.ScheduleID = schRow.ScheduleID;
          newSchRow.PartName = schRow.PartName;

          transSet.Schedule_t.AddSchedule_tRow(newSchRow);

          foreach (ReadOnlyDataSet.ScheduleProcessRow schProcRow in schRow.GetScheduleProcessRows())
          {
            var newSchProcRow = transSet.ScheduleProcess_t.NewScheduleProcess_tRow();
            newSchProcRow.ScheduleID = schProcRow.ScheduleID;
            newSchProcRow.ProcessNumber = schProcRow.ProcessNumber;
            transSet.ScheduleProcess_t.AddScheduleProcess_tRow(newSchProcRow);
          }

          string unique;
          int path;
          bool manual;
          MazakPart.ParseComment(schRow.Comment, out unique, out path, out manual);
          if (unique != null && unique != "")
            jobDB.ArchiveJob(unique);

        }
        else
        {
          savedParts.Add(schRow.PartName);

          if (DecrementPriorityOnDownload)
          {
            TransactionDatabaseAccess.BuildScheduleEditRow(newSchRow, schRow, false);
            newSchRow.Priority = Math.Max(newSchRow.Priority - 1, 1);
            transSet.Schedule_t.AddSchedule_tRow(newSchRow);
          }
        }
      }

      trace.TraceEvent(TraceEventType.Information, 0, "Creating new schedule with UID = " + UID.ToString());
      trace.TraceEvent(TraceEventType.Information, 0, "Saved Parts: " + DatabaseAccess.Join(savedParts, ", "));

      var traceMessages = new List<string>();

      //build the pallet->part mapping
      var palletPartMap = new clsPalletPartMapping(newJ.Jobs, currentSet, UID, savedParts, logMessages, traceMessages,
                                                   !string.IsNullOrEmpty(newGlobal), newGlobal,
                                                   CheckPalletsUsedOnce, database.MazakType);

      trace.TraceEvent(TraceEventType.Information, 0, "Finished mapping with messages: " +
        DatabaseAccess.Join(traceMessages, Environment.NewLine));

      //delete everything
      palletPartMap.DeletePartPallets(transSet);
      database.SaveTransaction(transSet, logMessages, "Delete Parts Pallets");

      trace.TraceEvent(TraceEventType.Information, 0, "Completed deletion of parts and pallets with messages: " +
        DatabaseAccess.Join(logMessages, Environment.NewLine));

      //have to delete fixtures after schedule, parts, and pallets are already deleted
      //also, add new fixtures
      transSet = new TransactionDataSet();
      palletPartMap.DeleteFixtures(transSet);
      palletPartMap.AddFixtures(transSet);
      database.SaveTransaction(transSet, logMessages, "Fixtures");

      trace.TraceEvent(TraceEventType.Information, 0, "Deleted fixtures with messages: " +
        DatabaseAccess.Join(logMessages, Environment.NewLine));

      //now save the pallets and parts
      transSet = new TransactionDataSet();
      palletPartMap.CreateRows(transSet);
      database.SaveTransaction(transSet, logMessages, "Add Parts");

      trace.TraceEvent(TraceEventType.Information, 0, "Added parts and pallets with messages: " +
        DatabaseAccess.Join(logMessages, Environment.NewLine));


      if (logMessages.Count > 0)
      {
        trace.TraceEvent(TraceEventType.Warning, 0, "Aborting schedule creation during download because" +
          " mazak returned an error while creating parts and pallets");
        trace.Flush();

        throw BuildTransactionException("Error creating parts and pallets", logMessages);
      }

      trace.Flush();

      return palletPartMap;
    }

    private delegate int FindProcDel(JobPlan part);

    private void AddSchedules(IEnumerable<JobPlan> routes,
                              List<int> usedScheduleIDs,
                              int UID,
                              ReadOnlyDataSet currentSet,
                              IList<string> logMessages,
                              FindProcDel numProcess)
    {
      var transSet = new TransactionDataSet();
      var scheduledPart = new List<string>();
      var now = DateTime.Now;

      //add all parts that have already been scheduled
      foreach (ReadOnlyDataSet.ScheduleRow schRow in currentSet.Schedule.Rows)
      {
        if (MazakPart.ParseUID(schRow.PartName) == UID)
        {
          string partName = schRow.PartName.Substring(0, schRow.PartName.IndexOf(':'));
          scheduledPart.Add(partName + "--" + MazakPart.ParsePathFromPart(schRow.PartName).ToString());
        }
      }

      //now add the new schedule
      int scheduleCount = 0;
      foreach (JobPlan part in routes)
      {
        for (int path = 1; path <= part.GetNumPaths(1); path++)
        {
          if (!scheduledPart.Contains(part.PartName + "--" + path.ToString()) & part.GetPlannedCyclesOnFirstProcess(path) > 0)
          {
            scheduledPart.Add(part.PartName + "--" + path.ToString());

            int schid = FindNextScheduleId(usedScheduleIDs);

            SchedulePart(transSet, schid, UID, numProcess, part, path, now, scheduleCount);

            hold.SaveHoldMode(schid, part, path);

            scheduleCount += 1;
          }
        }
      }

      if (transSet.Schedule_t.Rows.Count > 0)
      {

        if (UseStartingOffsetForDueDate)
          SortSchedulesByDate(transSet);

        database.SaveTransaction(transSet, logMessages, "Add Schedules");

        trace.TraceEvent(TraceEventType.Information, 0, "Completed adding schedules with messages " +
            DatabaseAccess.Join(logMessages, Environment.NewLine));

        foreach (var j in routes)
        {
          jobDB.MarkJobCopiedToSystem(j.UniqueStr);
        }
      }
    }

    private void UpdateGlobalUniqueStr(
        ReadOnlyDataSet currentSet,
        IList<string> logMessages,
        string newGlobalTag)
    {
      //once a successful transaction has occured, save the uniqueStr into the fixutre comment
      if (!string.IsNullOrEmpty(newGlobalTag))
      {
        string oldFixName = "";
        foreach (ReadOnlyDataSet.FixtureRow fixRow in currentSet.Fixture.Rows)
        {
          if (fixRow.FixtureName.ToLower() == "fixture:uniquestr")
          {
            oldFixName = fixRow.FixtureName;
            break;
          }
        }
        var transSet = new TransactionDataSet();
        var newFixRow = transSet.Fixture_t.NewFixture_tRow();
        if (!string.IsNullOrEmpty(oldFixName))
        {
          newFixRow.Command = TransactionDatabaseAccess.EditCommand;
          newFixRow.FixtureName = oldFixName;
          newFixRow.Comment = newGlobalTag;
          transSet.Fixture_t.AddFixture_tRow(newFixRow);
          database.SaveTransaction(transSet, logMessages, "UniqueStr", 3);
        }
        else
        {
          newFixRow.Command = TransactionDatabaseAccess.AddCommand;
          newFixRow.FixtureName = "Fixture:UniqueStr";
          newFixRow.Comment = newGlobalTag;
          transSet.Fixture_t.AddFixture_tRow(newFixRow);
          database.SaveTransaction(transSet, logMessages, "UniqueStr", 3);
        }
      }
    }

    private int FindFixQty(string part, ReadOnlyDataSet currentSet)
    {
      foreach (ReadOnlyDataSet.PartProcessRow partProcRow in currentSet.PartProcess.Rows)
      {
        if (partProcRow.PartName == part && partProcRow.ProcessNumber == 1)
        {
          return partProcRow.FixQuantity;
        }
      }
      return 1;
    }

    private void SchedulePart(TransactionDataSet transSet, int SchID, int UID, FindProcDel numProcess,
                              JobPlan part, int path, DateTime now, int scheduleCount)
    {
      var tempMazakPart = new MazakPart(part, path, UID);

      var newSchRow = transSet.Schedule_t.NewSchedule_tRow();
      newSchRow.Command = TransactionDatabaseAccess.AddCommand;
      newSchRow.ScheduleID = SchID;
      newSchRow.PartName = tempMazakPart.PartName;
      newSchRow.PlanQuantity = part.GetPlannedCyclesOnFirstProcess(path);
      newSchRow.CompleteQuantity = 0;
      newSchRow.FixForMachine = 0;
      newSchRow.MissingFixture = 0;
      newSchRow.MissingProgram = 0;
      newSchRow.MissingTool = 0;
      newSchRow.MixScheduleID = 0;
      newSchRow.ProcessingPriority = 0;
      newSchRow.Priority = 75;
      newSchRow.Comment = tempMazakPart.Comment;

      if (UseStartingOffsetForDueDate)
      {
        if (part.GetSimulatedStartingTimeUTC(1, path) != DateTime.MinValue)
          newSchRow.DueDate = part.GetSimulatedStartingTimeUTC(1, path);
        else
          newSchRow.DueDate = DateTime.Today;
        newSchRow.DueDate = newSchRow.DueDate.AddSeconds(5 * scheduleCount);
      }
      else
      {
        newSchRow.DueDate = DateTime.Parse("1/1/2008 12:00:00 AM");
      }

      bool entireHold = false;
      if (part.HoldEntireJob != null) entireHold = part.HoldEntireJob.IsJobOnHold;
      bool machiningHold = false;
      if (part.HoldMachining(1, path) != null) machiningHold = part.HoldMachining(1, path).IsJobOnHold;
      newSchRow.HoldMode = (int)HoldPattern.CalculateHoldMode(entireHold, machiningHold);

      //need to add all the ScheduleProcess rows
      for (int i = 1; i <= numProcess(part); i++)
      {
        var newSchProcRow = transSet.ScheduleProcess_t.NewScheduleProcess_tRow();
        newSchProcRow.ScheduleID = SchID;
        newSchProcRow.ProcessNumber = i;
        if (i == 1)
        {
          newSchProcRow.ProcessMaterialQuantity = newSchRow.PlanQuantity;
        }
        else
        {
          newSchProcRow.ProcessMaterialQuantity = 0;
        }
        newSchProcRow.ProcessBadQuantity = 0;
        newSchProcRow.ProcessExecuteQuantity = 0;
        newSchProcRow.ProcessMachine = 0;

        transSet.ScheduleProcess_t.AddScheduleProcess_tRow(newSchProcRow);
      }

      transSet.Schedule_t.AddSchedule_tRow(newSchRow);
    }

    private static void SortSchedulesByDate(TransactionDataSet transSet)
    {
      transSet.EnforceConstraints = false;

      var scheduleCopy = (TransactionDataSet.Schedule_tDataTable)transSet.Schedule_t.Copy();
      var rows = new List<TransactionDataSet.Schedule_tRow>();
      foreach (TransactionDataSet.Schedule_tRow r in scheduleCopy.Rows)
        rows.Add(r);
      rows.Sort((x, y) => x.DueDate.CompareTo(y.DueDate));
      transSet.Schedule_t.Rows.Clear();
      foreach (var r in rows)
      {
        //ImportRow has a really bad "feature" that it won't import
        //a detached row, so we must copy the entire table. Actually,
        //we must have three copies of the rows: the copy of the original table,
        //the list so we can call sort, and the filled output table. GAH!
        //Mono imports detached rows just fine....
        transSet.Schedule_t.ImportRow(r);
      }

      transSet.EnforceConstraints = true;
    }

    private void AddJobsToDB(NewJobs newJ)
    {
      foreach (var j in newJ.Jobs)
      {
        j.Archived = true;
        j.JobCopiedToSystem = false;
        if (!jobDB.DoesJobExist(j.UniqueStr))
        {
          for (int proc = 1; proc <= j.NumProcesses; proc++)
          {
            for (int path = 1; path <= j.GetNumPaths(proc); path++)
            {
              foreach (var stop in j.GetMachiningStop(proc, path))
              {
                //The station group name on the job and the LocationName from the
                //generated log entries must match.  Rather than store and try and lookup
                //the station name when creating log entries, since we only support a single
                //machine group, just set the group name to MC here during storage and
                //always create log entries with MC.
                stop.StationGroup = "MC";
              }
            }
          }
        }
      }
      jobDB.AddJobs(newJ, null);
    }
    #endregion

    #region "Decrement Plan Quantity"
    public List<JobAndDecrementQuantity> DecrementJobQuantites(string loadDecrementsStrictlyAfterDecrementId)
    {
      return new List<JobAndDecrementQuantity>();
    }
    public List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
    {
      return new List<JobAndDecrementQuantity>();
    }

    public Dictionary<JobAndPath, int> OldDecrementJobQuantites()
    {
      if (!database.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }

      try
      {
        database.ClearTransactionDatabase();
        return modDecrementPlanQty.DecrementPlanQty(database, readDatabase);
      }
      finally
      {
        try
        {
          database.ClearTransactionDatabase();
        }
        catch
        {
        }
        database.MazakTransactionLock.ReleaseMutex();
      }
    }

    public void OldFinalizeDecrement()
    {
      if (!database.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(2), true))
      {
        throw new Exception("Unable to obtain mazak database lock");
      }

      try
      {
        modDecrementPlanQty.FinalizeDecement(database, readDatabase);
      }
      finally
      {
        try
        {
          database.ClearTransactionDatabase();
        }
        catch
        {
        }
        database.MazakTransactionLock.ReleaseMutex();
      }
    }


    #endregion

    #region "Helpers"
    internal static int CountMaterial(ReadOnlyDataSet.ScheduleRow schRow)
    {
      int cnt = schRow.CompleteQuantity;
      foreach (ReadOnlyDataSet.ScheduleProcessRow schProcRow in schRow.GetScheduleProcessRows())
      {
        cnt += schProcRow.ProcessMaterialQuantity;
        cnt += schProcRow.ProcessExecuteQuantity;
        cnt += schProcRow.ProcessBadQuantity;
      }

      return cnt;
    }

    private static int CountInExecution(ReadOnlyDataSet.ScheduleRow schRow)
    {
      int cnt = 0;
      foreach (ReadOnlyDataSet.ScheduleProcessRow schProcRow in schRow.GetScheduleProcessRows())
      {
        cnt += schProcRow.ProcessExecuteQuantity;
      }

      return cnt;
    }

    private static int FindNextScheduleId(IList<int> usedScheduleIds)
    {
      for (int i = 1; i <= 9999; i++)
      {
        if (!usedScheduleIds.Contains(i))
        {
          usedScheduleIds.Add(i);
          return i;
        }
      }
      throw new ApplicationException("All Schedule Ids are currently being used");
    }


    public static Exception BuildTransactionException(string msg, IList<string> log)
    {
      string s = msg;
      foreach (string r in log)
      {
        s += Environment.NewLine + r;
      }
      return new Exception(s);
    }

    internal static int FindNumberProcesses(ReadOnlyDataSet dset, JobPlan part)
    {
      foreach (ReadOnlyDataSet.PartRow pRow in dset.Part.Rows)
      {
        if (pRow.PartName == part.PartName)
        {

          int procNum = 0;
          foreach (ReadOnlyDataSet.PartProcessRow proc in pRow.GetPartProcessRows())
          {
            procNum = Math.Max(procNum, proc.ProcessNumber);
          }
          return procNum;
        }
      }

      return 0;
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
    #endregion

    #region Queues

    public void AddUnprocessedMaterialToQueue(string jobUnique, int process, string queue, int position, string serial)
    {
      //do nothing
    }

    public void SetMaterialInQueue(long materialId, string queue, int position)
    {
      //do nothing
    }

    public void RemoveMaterialFromAllQueues(long materialId)
    {
      //do nothing
    }
    #endregion
  }
}
