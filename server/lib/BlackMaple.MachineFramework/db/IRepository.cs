/* Copyright (c) 2021, John Lenz

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

namespace BlackMaple.MachineFramework
{
  public interface IRepository : IDisposable
  {
    // --------------------------------------------------------------------------------
    // Loading Events
    // --------------------------------------------------------------------------------
    List<MachineWatchInterface.LogEntry> GetLog(long counter);
    List<MachineWatchInterface.LogEntry> GetLogEntries(DateTime startUTC, DateTime endUTC);
    List<MachineWatchInterface.LogEntry> GetLogForJobUnique(string jobUnique);
    List<MachineWatchInterface.LogEntry> GetLogForMaterial(long materialID);
    List<MachineWatchInterface.LogEntry> GetLogForMaterial(IEnumerable<long> materialIDs);
    List<MachineWatchInterface.LogEntry> GetLogForSerial(string serial);
    List<MachineWatchInterface.LogEntry> GetLogForWorkorder(string workorder);
    List<MachineWatchInterface.LogEntry> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC);
    List<MachineWatchInterface.LogEntry> StationLogByForeignID(string foreignID);
    List<MachineWatchInterface.LogEntry> CurrentPalletLog(string pallet);
    string OriginalMessageByForeignID(string foreignID);
    DateTime LastPalletCycleTime(string pallet);
    IEnumerable<ToolPocketSnapshot> ToolPocketSnapshotForCycle(long counter);
    string MaxForeignID();
    DateTime MaxLogDate();
    string ForeignIDForCounter(long counter);
    bool CycleExists(DateTime endUTC, string pal, MachineWatchInterface.LogType logTy, string locName, int locNum);
    List<MachineWatchInterface.WorkorderSummary> GetWorkorderSummaries(IEnumerable<string> workorders);
    List<string> GetWorkordersForUnique(string jobUnique);

    // --------------------------------------------------------------------------------
    // Adding Events
    // --------------------------------------------------------------------------------
    MachineWatchInterface.LogEntry RecordLoadStart(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, string foreignId = null, string originalMessage = null);
    IEnumerable<MachineWatchInterface.LogEntry> RecordLoadEnd(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordUnloadStart(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, string foreignId = null, string originalMessage = null);
    IEnumerable<MachineWatchInterface.LogEntry> RecordUnloadEnd(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, Dictionary<long, string> unloadIntoQueues = null, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordManualWorkAtLULStart(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, string operationName, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordManualWorkAtLULEnd(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, string operationName, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordMachineStart(IEnumerable<EventLogMaterial> mats, string pallet, string statName, int statNum, string program, DateTime timeUTC, IDictionary<string, string> extraData = null, IEnumerable<ToolPocketSnapshot> pockets = null, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordMachineEnd(IEnumerable<EventLogMaterial> mats, string pallet, string statName, int statNum, string program, string result, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, IDictionary<string, string> extraData = null, IDictionary<string, MachineWatchInterface.ToolUse> tools = null, IEnumerable<ToolPocketSnapshot> pockets = null, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordPalletArriveRotaryInbound(IEnumerable<EventLogMaterial> mats, string pallet, string statName, int statNum, DateTime timeUTC, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordPalletDepartRotaryInbound(IEnumerable<EventLogMaterial> mats, string pallet, string statName, int statNum, DateTime timeUTC, TimeSpan elapsed, bool rotateIntoWorktable, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordPalletArriveStocker(IEnumerable<EventLogMaterial> mats, string pallet, int stockerNum, DateTime timeUTC, bool waitForMachine, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordPalletDepartStocker(IEnumerable<EventLogMaterial> mats, string pallet, int stockerNum, DateTime timeUTC, bool waitForMachine, TimeSpan elapsed, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordSerialForMaterialID(EventLogMaterial mat, string serial, DateTime endTimeUTC);
    MachineWatchInterface.LogEntry RecordSerialForMaterialID(EventLogMaterial mat, string serial);
    MachineWatchInterface.LogEntry RecordSerialForMaterialID(long materialID, int proc, string serial);
    MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(long materialID, int proc, string workorder);
    MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(EventLogMaterial mat, string workorder);
    MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(EventLogMaterial mat, string workorder, DateTime recordUtc);
    MachineWatchInterface.LogEntry RecordInspectionCompleted(EventLogMaterial mat, int inspectionLocNum, string inspectionType, bool success, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    MachineWatchInterface.LogEntry RecordInspectionCompleted(EventLogMaterial mat, int inspectionLocNum, string inspectionType, bool success, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active, DateTime inspectTimeUTC);
    MachineWatchInterface.LogEntry RecordInspectionCompleted(long materialID, int process, int inspectionLocNum, string inspectionType, bool success, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    MachineWatchInterface.LogEntry RecordWashCompleted(long materialID, int process, int washLocNum, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    MachineWatchInterface.LogEntry RecordWashCompleted(EventLogMaterial mat, int washLocNum, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    MachineWatchInterface.LogEntry RecordWashCompleted(EventLogMaterial mat, int washLocNum, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active, DateTime completeTimeUTC);
    MachineWatchInterface.LogEntry RecordFinalizedWorkorder(string workorder);
    MachineWatchInterface.LogEntry RecordFinalizedWorkorder(string workorder, DateTime finalizedUTC);
    IEnumerable<MachineWatchInterface.LogEntry> RecordAddMaterialToQueue(EventLogMaterial mat, string queue, int position, string operatorName, string reason, DateTime? timeUTC = null);
    IEnumerable<MachineWatchInterface.LogEntry> RecordAddMaterialToQueue(long matID, int process, string queue, int position, string operatorName, string reason, DateTime? timeUTC = null);
    IEnumerable<MachineWatchInterface.LogEntry> RecordRemoveMaterialFromAllQueues(EventLogMaterial mat, string operatorName = null, DateTime? timeUTC = null);
    IEnumerable<MachineWatchInterface.LogEntry> RecordRemoveMaterialFromAllQueues(long matID, int process, string operatorName = null, DateTime? timeUTC = null);
    MachineWatchInterface.LogEntry RecordGeneralMessage(EventLogMaterial mat, string program, string result, string pallet = "", DateTime? timeUTC = null, string foreignId = null, string originalMessage = null, IDictionary<string, string> extraData = null);
    MachineWatchInterface.LogEntry RecordOperatorNotes(long materialId, int process, string notes, string operatorName);
    MachineWatchInterface.LogEntry RecordOperatorNotes(long materialId, int process, string notes, string operatorName, DateTime? timeUtc);
    MachineWatchInterface.LogEntry SignalMaterialForQuarantine(EventLogMaterial mat, string pallet, string queue, DateTime? timeUTC = null, string operatorName = null, string foreignId = null, string originalMessage = null);
    SwapMaterialResult SwapMaterialInCurrentPalletCycle(string pallet, long oldMatId, long newMatId, string operatorName, DateTime? timeUTC = null);
    IEnumerable<MachineWatchInterface.LogEntry> InvalidatePalletCycle(long matId, int process, string oldMatPutInQueue, string operatorName, DateTime? timeUTC = null);


    // --------------------------------------------------------------------------------
    // Material IDs
    // --------------------------------------------------------------------------------
    long AllocateMaterialID(string unique, string part, int numProc);
    long AllocateMaterialIDForCasting(string casting);
    void SetDetailsForMaterialID(long matID, string unique, string part, int? numProc);
    void RecordPathForProcess(long matID, int process, int path);
    void CreateMaterialID(long matID, string unique, string part, int numProc);
    MachineWatchInterface.MaterialDetails GetMaterialDetails(long matID);
    IReadOnlyList<MachineWatchInterface.MaterialDetails> GetMaterialDetailsForSerial(string serial);
    List<MachineWatchInterface.MaterialDetails> GetMaterialForWorkorder(string workorder);
    List<MachineWatchInterface.MaterialDetails> GetMaterialForJobUnique(string jobUnique);


    // --------------------------------------------------------------------------------
    // Queues
    // --------------------------------------------------------------------------------
    IReadOnlyList<long> AllocateCastingsInQueue(string queue, string casting, string unique, string part, int proc1Path, int numProcesses, int count);
    void MarkCastingsAsUnallocated(IEnumerable<long> matIds, string casting);
    IEnumerable<QueuedMaterial> GetMaterialInQueue(string queue);
    IEnumerable<QueuedMaterial> GetMaterialInAllQueues();
    int? NextProcessForQueuedMaterial(long matId);


    // --------------------------------------------------------------------------------
    // Pending Loads
    // --------------------------------------------------------------------------------
    void AddPendingLoad(string pal, string key, int load, TimeSpan elapsed, TimeSpan active, string foreignID);
    List<PendingLoad> PendingLoads(string pallet);
    List<PendingLoad> AllPendingLoads();
    void CompletePalletCycle(string pal, DateTime timeUTC, string foreignID);
    void CompletePalletCycle(string pal, DateTime timeUTC, string foreignID, IDictionary<string, IEnumerable<EventLogMaterial>> mat, bool generateSerials);


    // --------------------------------------------------------------------------------
    // Inspections
    // --------------------------------------------------------------------------------
    List<InspectCount> LoadInspectCounts();
    void SetInspectCounts(IEnumerable<InspectCount> counts);
    IList<Decision> LookupInspectionDecisions(long matID);
    IEnumerable<MachineWatchInterface.LogEntry> MakeInspectionDecisions(long matID, int process, IEnumerable<MachineWatchInterface.PathInspection> inspections, DateTime? mutcNow = null);
    MachineWatchInterface.LogEntry ForceInspection(long matID, string inspType);
    MachineWatchInterface.LogEntry ForceInspection(long materialID, int process, string inspType, bool inspect);
    MachineWatchInterface.LogEntry ForceInspection(EventLogMaterial mat, string inspType, bool inspect);
    MachineWatchInterface.LogEntry ForceInspection(EventLogMaterial mat, string inspType, bool inspect, DateTime utcNow);
    void NextPieceInspection(MachineWatchInterface.PalletLocation palLoc, string inspType);
    void CheckMaterialForNextPeiceInspection(MachineWatchInterface.PalletLocation palLoc, long matID);


    // --------------------------------------------------------------------------------
    // Loading Jobs
    // --------------------------------------------------------------------------------
    MachineWatchInterface.JobPlan LoadJob(string UniqueStr);
    bool DoesJobExist(string unique);
    List<MachineWatchInterface.JobPlan> LoadUnarchivedJobs();
    List<MachineWatchInterface.JobPlan> LoadJobsNotCopiedToSystem(DateTime startUTC, DateTime endUTC, bool includeDecremented = true);
    MachineWatchInterface.HistoricData LoadJobHistory(DateTime startUTC, DateTime endUTC);
    MachineWatchInterface.HistoricData LoadJobsAfterScheduleId(string schId);
    MachineWatchInterface.PlannedSchedule LoadMostRecentSchedule();
    List<MachineWatchInterface.PartWorkorder> MostRecentWorkorders();
    List<MachineWatchInterface.PartWorkorder> MostRecentUnfilledWorkordersForPart(string part);
    List<MachineWatchInterface.PartWorkorder> WorkordersById(string workorderId);


    // --------------------------------------------------------------------------------
    // Adding and Updating Jobs
    // --------------------------------------------------------------------------------
    void AddJobs(MachineWatchInterface.NewJobs newJobs, string expectedPreviousScheduleId);
    void AddPrograms(IEnumerable<MachineWatchInterface.ProgramEntry> programs, DateTime startingUtc);
    void ArchiveJob(string UniqueStr);
    void ArchiveJobs(IEnumerable<string> uniqueStrs, IEnumerable<NewDecrementQuantity> newDecrements = null, DateTime? nowUTC = null);
    void UnarchiveJob(string UniqueStr);
    void UnarchiveJobs(IEnumerable<string> uniqueStrs, DateTime? nowUTC = null);
    void MarkJobCopiedToSystem(string UniqueStr);
    void SetJobComment(string unique, string comment);
    void UpdateJobHold(string unique, MachineWatchInterface.JobHoldPattern newHold);
    void UpdateJobLoadUnloadHold(string unique, int proc, int path, MachineWatchInterface.JobHoldPattern newHold);
    void UpdateJobMachiningHold(string unique, int proc, int path, MachineWatchInterface.JobHoldPattern newHold);
    void ReplaceWorkordersForSchedule(string scheduleId, IEnumerable<MachineWatchInterface.PartWorkorder> newWorkorders, IEnumerable<MachineWatchInterface.ProgramEntry> programs, DateTime? nowUtc = null);


    // --------------------------------------------------------------------------------
    // Decrements
    // --------------------------------------------------------------------------------
    void AddNewDecrement(IEnumerable<NewDecrementQuantity> counts, DateTime? nowUTC = null, IEnumerable<RemovedBooking> removedBookings = null);
    List<MachineWatchInterface.DecrementQuantity> LoadDecrementsForJob(string unique);
    List<MachineWatchInterface.JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(long afterId);
    List<MachineWatchInterface.JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(DateTime afterUTC);


    // --------------------------------------------------------------------------------
    // Programs
    // --------------------------------------------------------------------------------
    MachineWatchInterface.ProgramRevision LoadProgram(string program, long revision);
    MachineWatchInterface.ProgramRevision LoadMostRecentProgram(string program);
    string LoadProgramContent(string program, long revision);
    List<MachineWatchInterface.ProgramRevision> LoadProgramRevisionsInDescendingOrderOfRevision(string program, int count, long? startRevision);
    List<MachineWatchInterface.ProgramRevision> LoadProgramsInCellController();
    MachineWatchInterface.ProgramRevision ProgramFromCellControllerProgram(string cellCtProgName);
    void SetCellControllerProgramForProgram(string program, long revision, string cellCtProgName);
  }

  public record InspectCount
  {
    public string Counter { get; init; }
    public int Value { get; init; }
    public DateTime LastUTC { get; init; }
  }

  public record EventLogMaterial
  {
    public long MaterialID { get; init; }
    public int Process { get; init; }
    public string Face { get; init; }

    public static EventLogMaterial FromLogMat(MachineWatchInterface.LogMaterial m)
    {
      return new EventLogMaterial()
      {
        MaterialID = m.MaterialID,
        Process = m.Process,
        Face = m.Face
      };
    }
  }

  public record SwapMaterialResult
  {
    public IEnumerable<MachineWatchInterface.LogEntry> ChangedLogEntries { get; init; }
    public IEnumerable<MachineWatchInterface.LogEntry> NewLogEntries { get; init; }
  }

  public record Decision
  {
    public long MaterialID { get; init; }
    public string InspType { get; init; }
    public string Counter { get; init; }
    public bool Inspect { get; init; }
    public bool Forced { get; init; }
    public System.DateTime CreateUTC { get; init; }
  }

  public record PendingLoad
  {
    public string Pallet { get; init; }
    public string Key { get; init; }
    public int LoadStation { get; init; }
    public TimeSpan Elapsed { get; init; }
    public TimeSpan ActiveOperationTime { get; init; }
    public string ForeignID { get; init; }
  }

  public record QueuedMaterial
  {
    public long MaterialID { get; init; }
    public string Queue { get; init; }
    public int Position { get; init; }
    public string Unique { get; init; }
    public string PartNameOrCasting { get; init; }
    public int NumProcesses { get; init; }
    public DateTime? AddTimeUTC { get; init; }
  }

  public record NewDecrementQuantity
  {
    public string JobUnique { get; init; }
    public int Proc1Path { get; init; }
    public string Part { get; init; }
    public int Quantity { get; init; }
  }

  public record RemovedBooking
  {
    public string JobUnique { get; init; }
    public string BookingId { get; init; }
  }

  public record ToolPocketSnapshot
  {
    public int PocketNumber { get; init; }
    public string Tool { get; init; }
    public TimeSpan CurrentUse { get; init; }
    public TimeSpan ToolLife { get; init; }
  }


}