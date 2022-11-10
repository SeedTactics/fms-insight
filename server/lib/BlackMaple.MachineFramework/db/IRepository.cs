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
using System.Collections.Immutable;
using Germinate;

namespace BlackMaple.MachineFramework
{
  public interface IRepository : IDisposable
  {
    RepositoryConfig RepoConfig { get; }

    // --------------------------------------------------------------------------------
    // Loading Events
    // --------------------------------------------------------------------------------
    IEnumerable<LogEntry> GetRecentLog(long lastSeenCounter, DateTime? expectedEndUTCofLastSeen = null);
    IEnumerable<LogEntry> GetLogEntries(DateTime startUTC, DateTime endUTC);
    IEnumerable<LogEntry> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC);
    IEnumerable<LogEntry> GetLogForJobUnique(string jobUnique);
    List<LogEntry> GetLogForMaterial(long materialID);
    List<LogEntry> GetLogForMaterial(IEnumerable<long> materialIDs);
    IEnumerable<LogEntry> GetLogForSerial(string serial);
    IEnumerable<LogEntry> GetLogForWorkorder(string workorder);
    List<LogEntry> StationLogByForeignID(string foreignID);
    List<LogEntry> CurrentPalletLog(string pallet);
    string OriginalMessageByForeignID(string foreignID);
    DateTime LastPalletCycleTime(string pallet);
    IEnumerable<ToolPocketSnapshot> ToolPocketSnapshotForCycle(long counter);
    string MaxForeignID();
    DateTime MaxLogDate();
    string ForeignIDForCounter(long counter);
    bool CycleExists(DateTime endUTC, string pal, LogType logTy, string locName, int locNum);
    List<WorkorderSummary> GetWorkorderSummaries(IEnumerable<string> workorders);
    ImmutableList<string> GetWorkordersForUnique(string jobUnique);

    // --------------------------------------------------------------------------------
    // Adding Events
    // --------------------------------------------------------------------------------
    LogEntry RecordLoadStart(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, string foreignId = null, string originalMessage = null);
    IEnumerable<LogEntry> RecordLoadEnd(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, string foreignId = null, string originalMessage = null);
    LogEntry RecordUnloadStart(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, string foreignId = null, string originalMessage = null);
    IEnumerable<LogEntry> RecordUnloadEnd(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, Dictionary<long, string> unloadIntoQueues = null, string foreignId = null, string originalMessage = null);
    LogEntry RecordManualWorkAtLULStart(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, string operationName, string foreignId = null, string originalMessage = null);
    LogEntry RecordManualWorkAtLULEnd(IEnumerable<EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, string operationName, string foreignId = null, string originalMessage = null);
    LogEntry RecordMachineStart(IEnumerable<EventLogMaterial> mats, string pallet, string statName, int statNum, string program, DateTime timeUTC, IDictionary<string, string> extraData = null, IEnumerable<ToolPocketSnapshot> pockets = null, string foreignId = null, string originalMessage = null);
    LogEntry RecordMachineEnd(IEnumerable<EventLogMaterial> mats, string pallet, string statName, int statNum, string program, string result, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, IDictionary<string, string> extraData = null, ImmutableList<ToolUse> tools = null, IEnumerable<ToolPocketSnapshot> pockets = null, string foreignId = null, string originalMessage = null);
    LogEntry RecordPalletArriveRotaryInbound(IEnumerable<EventLogMaterial> mats, string pallet, string statName, int statNum, DateTime timeUTC, string foreignId = null, string originalMessage = null);
    LogEntry RecordPalletDepartRotaryInbound(IEnumerable<EventLogMaterial> mats, string pallet, string statName, int statNum, DateTime timeUTC, TimeSpan elapsed, bool rotateIntoWorktable, string foreignId = null, string originalMessage = null);
    LogEntry RecordPalletArriveStocker(IEnumerable<EventLogMaterial> mats, string pallet, int stockerNum, DateTime timeUTC, bool waitForMachine, string foreignId = null, string originalMessage = null);
    LogEntry RecordPalletDepartStocker(IEnumerable<EventLogMaterial> mats, string pallet, int stockerNum, DateTime timeUTC, bool waitForMachine, TimeSpan elapsed, string foreignId = null, string originalMessage = null);
    LogEntry RecordSerialForMaterialID(EventLogMaterial mat, string serial, DateTime endTimeUTC);
    LogEntry RecordSerialForMaterialID(EventLogMaterial mat, string serial);
    LogEntry RecordSerialForMaterialID(long materialID, int proc, string serial);
    LogEntry RecordWorkorderForMaterialID(long materialID, int proc, string workorder);
    LogEntry RecordWorkorderForMaterialID(EventLogMaterial mat, string workorder);
    LogEntry RecordWorkorderForMaterialID(EventLogMaterial mat, string workorder, DateTime recordUtc);
    LogEntry RecordInspectionCompleted(EventLogMaterial mat, int inspectionLocNum, string inspectionType, bool success, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    LogEntry RecordInspectionCompleted(EventLogMaterial mat, int inspectionLocNum, string inspectionType, bool success, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active, DateTime inspectTimeUTC);
    LogEntry RecordInspectionCompleted(long materialID, int process, int inspectionLocNum, string inspectionType, bool success, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    LogEntry RecordWashCompleted(long materialID, int process, int washLocNum, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    LogEntry RecordWashCompleted(EventLogMaterial mat, int washLocNum, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    LogEntry RecordWashCompleted(EventLogMaterial mat, int washLocNum, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active, DateTime completeTimeUTC);
    LogEntry RecordFinalizedWorkorder(string workorder);
    LogEntry RecordFinalizedWorkorder(string workorder, DateTime finalizedUTC);
    IEnumerable<LogEntry> RecordAddMaterialToQueue(EventLogMaterial mat, string queue, int position, string operatorName, string reason, DateTime? timeUTC = null);
    IEnumerable<LogEntry> RecordAddMaterialToQueue(long matID, int process, string queue, int position, string operatorName, string reason, DateTime? timeUTC = null);
    IEnumerable<LogEntry> RecordRemoveMaterialFromAllQueues(EventLogMaterial mat, string operatorName = null, DateTime? timeUTC = null);
    IEnumerable<LogEntry> RecordRemoveMaterialFromAllQueues(long matID, int process, string operatorName = null, DateTime? timeUTC = null);
    IEnumerable<LogEntry> BulkRemoveMaterialFromAllQueues(IEnumerable<long> matIds, string operatorName = null, DateTime? timeUTC = null);
    LogEntry RecordGeneralMessage(EventLogMaterial mat, string program, string result, string pallet = "", DateTime? timeUTC = null, string foreignId = null, string originalMessage = null, IDictionary<string, string> extraData = null);
    LogEntry RecordOperatorNotes(long materialId, int process, string notes, string operatorName);
    LogEntry RecordOperatorNotes(long materialId, int process, string notes, string operatorName, DateTime? timeUtc);
    LogEntry SignalMaterialForQuarantine(EventLogMaterial mat, string pallet, string queue, DateTime? timeUTC = null, string operatorName = null, string foreignId = null, string originalMessage = null);
    SwapMaterialResult SwapMaterialInCurrentPalletCycle(string pallet, long oldMatId, long newMatId, string operatorName, string quarantineQueue, DateTime? timeUTC = null);
    IEnumerable<LogEntry> InvalidatePalletCycle(long matId, int process, string oldMatPutInQueue, string operatorName, DateTime? timeUTC = null);


    // --------------------------------------------------------------------------------
    // Material IDs
    // --------------------------------------------------------------------------------
    long AllocateMaterialID(string unique, string part, int numProc);
    long AllocateMaterialIDAndGenerateSerial(string unique, string part, int proc, int numProc, DateTime timeUTC, out LogEntry serialLogEntry);
    long AllocateMaterialIDForCasting(string casting);
    void SetDetailsForMaterialID(long matID, string unique, string part, int? numProc);
    void RecordPathForProcess(long matID, int process, int path);
    void CreateMaterialID(long matID, string unique, string part, int numProc);
    MaterialDetails GetMaterialDetails(long matID);
    IReadOnlyList<MaterialDetails> GetMaterialDetailsForSerial(string serial);
    List<MaterialDetails> GetMaterialForWorkorder(string workorder);
    List<MaterialDetails> GetMaterialForJobUnique(string jobUnique);


    // --------------------------------------------------------------------------------
    // Queues
    // --------------------------------------------------------------------------------
    IReadOnlyList<long> AllocateCastingsInQueue(string queue, string casting, string unique, string part, int proc1Path, int numProcesses, int count);
    void MarkCastingsAsUnallocated(IEnumerable<long> matIds, string casting);
    bool IsMaterialInQueue(long matId);
    IEnumerable<QueuedMaterial> GetMaterialInQueueByUnique(string queue, string jobUnique);
    IEnumerable<QueuedMaterial> GetUnallocatedMaterialInQueue(string queue, string partNameOrCasting);
    IEnumerable<QueuedMaterial> GetMaterialInAllQueues();
    int? NextProcessForQueuedMaterial(long matId);
    BulkAddCastingResult BulkAddNewCastingsInQueue(string casting, int qty, string queue, IList<string> serials, string operatorName, string reason = null, DateTime? timeUTC = null);


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
    IReadOnlyList<Decision> LookupInspectionDecisions(long matID);
    ImmutableDictionary<long, IReadOnlyList<Decision>> LookupInspectionDecisions(IEnumerable<long> matID);
    IEnumerable<LogEntry> MakeInspectionDecisions(long matID, int process, IEnumerable<PathInspection> inspections, DateTime? mutcNow = null);
    LogEntry ForceInspection(long matID, string inspType);
    LogEntry ForceInspection(long materialID, int process, string inspType, bool inspect);
    LogEntry ForceInspection(EventLogMaterial mat, string inspType, bool inspect);
    LogEntry ForceInspection(EventLogMaterial mat, string inspType, bool inspect, DateTime utcNow);
    void NextPieceInspection(PalletLocation palLoc, string inspType);
    void CheckMaterialForNextPeiceInspection(PalletLocation palLoc, long matID);


    // --------------------------------------------------------------------------------
    // Loading Jobs
    // --------------------------------------------------------------------------------
    HistoricJob LoadJob(string UniqueStr);
    bool DoesJobExist(string unique);
    IReadOnlyList<HistoricJob> LoadUnarchivedJobs();
    IReadOnlyList<HistoricJob> LoadJobsNotCopiedToSystem(DateTime startUTC, DateTime endUTC, bool includeDecremented = true);
    HistoricData LoadJobHistory(DateTime startUTC, DateTime endUTC, IEnumerable<string> alreadyKnownSchIds = null);
    HistoricData LoadJobsAfterScheduleId(string schId);
    PlannedSchedule LoadMostRecentSchedule();
    IReadOnlyList<Workorder> MostRecentWorkorders();
    List<Workorder> MostRecentUnfilledWorkordersForPart(string part);
    List<Workorder> WorkordersById(string workorderId);


    // --------------------------------------------------------------------------------
    // Adding and Updating Jobs
    // --------------------------------------------------------------------------------
    void AddJobs(NewJobs newJobs, string expectedPreviousScheduleId, bool addAsCopiedToSystem);
    void AddPrograms(IEnumerable<NewProgramContent> programs, DateTime startingUtc);
    void ArchiveJob(string UniqueStr);
    void ArchiveJobs(IEnumerable<string> uniqueStrs, IEnumerable<NewDecrementQuantity> newDecrements = null, DateTime? nowUTC = null);
    void UnarchiveJob(string UniqueStr);
    void UnarchiveJobs(IEnumerable<string> uniqueStrs, DateTime? nowUTC = null);
    void MarkJobCopiedToSystem(string UniqueStr);
    void SetJobComment(string unique, string comment);
    void UpdateJobHold(string unique, HoldPattern newHold);
    void UpdateJobLoadUnloadHold(string unique, int proc, int path, HoldPattern newHold);
    void UpdateJobMachiningHold(string unique, int proc, int path, HoldPattern newHold);
    void ReplaceWorkordersForSchedule(string scheduleId, IEnumerable<Workorder> newWorkorders, IEnumerable<NewProgramContent> programs, DateTime? nowUtc = null);


    // --------------------------------------------------------------------------------
    // Decrements
    // --------------------------------------------------------------------------------
    void AddNewDecrement(IEnumerable<NewDecrementQuantity> counts, DateTime? nowUTC = null, IEnumerable<RemovedBooking> removedBookings = null);
    ImmutableList<DecrementQuantity> LoadDecrementsForJob(string unique);
    List<JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(long afterId);
    List<JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(DateTime afterUTC);


    // --------------------------------------------------------------------------------
    // Programs
    // --------------------------------------------------------------------------------
    ProgramRevision LoadProgram(string program, long revision);
    ProgramRevision LoadMostRecentProgram(string program);
    string LoadProgramContent(string program, long revision);
    List<ProgramRevision> LoadProgramRevisionsInDescendingOrderOfRevision(string program, int count, long? startRevision);
    List<ProgramRevision> LoadProgramsInCellController();
    ProgramRevision ProgramFromCellControllerProgram(string cellCtProgName);
    void SetCellControllerProgramForProgram(string program, long revision, string cellCtProgName);
  }

  public record InspectCount
  {
    public string Counter { get; init; }
    public int Value { get; init; }
    public DateTime LastUTC { get; init; }
  }

  [Draftable]
  public record EventLogMaterial
  {
    public long MaterialID { get; init; }
    public int Process { get; init; }
    public string Face { get; init; }

    public static EventLogMaterial FromLogMat(LogMaterial m)
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
    public IEnumerable<LogEntry> ChangedLogEntries { get; init; }
    public IEnumerable<LogEntry> NewLogEntries { get; init; }
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
    public string Serial { get; init; }
    public string Workorder { get; init; }
    public ImmutableDictionary<int, int> Paths { get; init; } // key is process, value is path
    public DateTime? AddTimeUTC { get; init; }
    public int? NextProcess { get; init; }
  }

  [Draftable]
  public record NewDecrementQuantity
  {
    public string JobUnique { get; init; }
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

  public record BulkAddCastingResult
  {
    public HashSet<long> MaterialIds { get; init; }
    public IReadOnlyList<LogEntry> Logs { get; init; }
  }

}