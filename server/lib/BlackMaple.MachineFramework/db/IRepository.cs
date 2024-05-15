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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
    List<LogEntry> GetLogForMaterial(long materialID, bool includeInvalidatedCycles = true);
    List<LogEntry> GetLogForMaterial(IEnumerable<long> materialIDs);
    IEnumerable<LogEntry> GetLogForSerial(string serial);
    IEnumerable<LogEntry> GetLogForWorkorder(string workorder);
    List<LogEntry> CurrentPalletLog(int pallet, bool includeLastPalletCycleEvt = false);
    DateTime LastPalletCycleTime(int pallet);
    IEnumerable<ToolSnapshot> ToolPocketSnapshotForCycle(long counter);
    bool CycleExists(DateTime endUTC, int pal, LogType logTy, string locName, int locNum);
    ImmutableList<ActiveWorkorder> GetActiveWorkorders(string partToFilter = null);
    ImmutableList<string> GetWorkordersForUnique(string jobUnique);
    DateTime MaxLogDate();
    string MaxForeignID(); // WARNING: uses sqlite default string collate (binary), not lexicographic
    string ForeignIDForCounter(long counter);
    string OriginalMessageByForeignID(string foreignID);

    // Most recent refers to event Counter, not time
    LogEntry MostRecentLogEntryForForeignID(string foreignID);
    LogEntry MostRecentLogEntryLessOrEqualToForeignID(string foreignID); // WARNING: uses sqlite default string collate (binary), not lexicographic

    // --------------------------------------------------------------------------------
    // Adding Events
    // --------------------------------------------------------------------------------
    LogEntry RecordLoadStart(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );
    IEnumerable<LogEntry> RecordLoadEnd(
      IEnumerable<MaterialToLoadOntoPallet> toLoad,
      int pallet,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordUnloadStart(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );
    IEnumerable<LogEntry> RecordUnloadEnd(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int lulNum,
      DateTime timeUTC,
      TimeSpan elapsed,
      TimeSpan active,
      Dictionary<long, string> unloadIntoQueues = null,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordManualWorkAtLULStart(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int lulNum,
      DateTime timeUTC,
      string operationName,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordManualWorkAtLULEnd(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int lulNum,
      DateTime timeUTC,
      TimeSpan elapsed,
      TimeSpan active,
      string operationName,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordMachineStart(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      string statName,
      int statNum,
      string program,
      DateTime timeUTC,
      IDictionary<string, string> extraData = null,
      IEnumerable<ToolSnapshot> pockets = null,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordMachineEnd(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      string statName,
      int statNum,
      string program,
      string result,
      DateTime timeUTC,
      TimeSpan elapsed,
      TimeSpan active,
      IDictionary<string, string> extraData = null,
      ImmutableList<ToolUse> tools = null,
      IEnumerable<ToolSnapshot> pockets = null,
      long? deleteToolSnapshotsFromCntr = null,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordPalletArriveRotaryInbound(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      string statName,
      int statNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordPalletDepartRotaryInbound(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      string statName,
      int statNum,
      DateTime timeUTC,
      TimeSpan elapsed,
      bool rotateIntoWorktable,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordPalletArriveStocker(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int stockerNum,
      DateTime timeUTC,
      bool waitForMachine,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordPalletDepartStocker(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int stockerNum,
      DateTime timeUTC,
      bool waitForMachine,
      TimeSpan elapsed,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordSerialForMaterialID(
      EventLogMaterial mat,
      string serial,
      DateTime timeUTC,
      string foreignID = null,
      string originalMessage = null
    );
    LogEntry RecordSerialForMaterialID(
      long materialID,
      int proc,
      string serial,
      DateTime timeUTC,
      string foreignID = null,
      string originalMessage = null
    );
    LogEntry RecordWorkorderForMaterialID(long materialID, int proc, string workorder);
    LogEntry RecordWorkorderForMaterialID(EventLogMaterial mat, string workorder);
    LogEntry RecordWorkorderForMaterialID(EventLogMaterial mat, string workorder, DateTime recordUtc);
    LogEntry RecordInspectionCompleted(
      EventLogMaterial mat,
      int inspectionLocNum,
      string inspectionType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active
    );
    LogEntry RecordInspectionCompleted(
      EventLogMaterial mat,
      int inspectionLocNum,
      string inspectionType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active,
      DateTime inspectTimeUTC
    );
    LogEntry RecordInspectionCompleted(
      long materialID,
      int process,
      int inspectionLocNum,
      string inspectionType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active
    );
    LogEntry RecordCloseoutCompleted(
      long materialID,
      int process,
      int locNum,
      string closeoutType,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active
    );
    LogEntry RecordCloseoutCompleted(
      EventLogMaterial mat,
      int locNum,
      string closeoutType,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active
    );
    LogEntry RecordCloseoutCompleted(
      EventLogMaterial mat,
      int locNum,
      string closeoutType,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active,
      DateTime completeTimeUTC
    );
    LogEntry RecordWorkorderComment(
      string workorder,
      string comment,
      string operName,
      DateTime? timeUTC = null
    );
    IEnumerable<LogEntry> RecordAddMaterialToQueue(
      EventLogMaterial mat,
      string queue,
      int position,
      string operatorName,
      string reason,
      DateTime? timeUTC = null
    );
    IEnumerable<LogEntry> RecordAddMaterialToQueue(
      long matID,
      int process,
      string queue,
      int position,
      string operatorName,
      string reason,
      DateTime? timeUTC = null
    );
    IEnumerable<LogEntry> RecordRemoveMaterialFromAllQueues(
      EventLogMaterial mat,
      string operatorName = null,
      DateTime? timeUTC = null
    );
    IEnumerable<LogEntry> RecordRemoveMaterialFromAllQueues(
      long matID,
      int process,
      string operatorName = null,
      DateTime? timeUTC = null
    );
    IEnumerable<LogEntry> BulkRemoveMaterialFromAllQueues(
      IEnumerable<long> matIds,
      string operatorName = null,
      string reason = null,
      DateTime? timeUTC = null
    );
    LogEntry RecordGeneralMessage(
      EventLogMaterial mat,
      string program,
      string result,
      int pallet = 0,
      DateTime? timeUTC = null,
      string foreignId = null,
      string originalMessage = null,
      IDictionary<string, string> extraData = null
    );
    LogEntry RecordOperatorNotes(long materialId, int process, string notes, string operatorName);
    LogEntry RecordOperatorNotes(
      long materialId,
      int process,
      string notes,
      string operatorName,
      DateTime? timeUtc
    );
    LogEntry SignalMaterialForQuarantine(
      EventLogMaterial mat,
      int pallet,
      string queue,
      string operatorName,
      string reason,
      DateTime? timeUTC = null,
      string foreignId = null,
      string originalMessage = null
    );
    SwapMaterialResult SwapMaterialInCurrentPalletCycle(
      int pallet,
      long oldMatId,
      long newMatId,
      string operatorName,
      string quarantineQueue,
      DateTime? timeUTC = null
    );
    IEnumerable<LogEntry> InvalidatePalletCycle(
      long matId,
      int process,
      string oldMatPutInQueue,
      string operatorName,
      DateTime? timeUTC = null
    );

    // --------------------------------------------------------------------------------
    // Material IDs
    // --------------------------------------------------------------------------------
    long AllocateMaterialID(string unique, string part, int numProc);
    long AllocateMaterialIDAndGenerateSerial(
      string unique,
      string part,
      int numProc,
      DateTime timeUTC,
      out LogEntry serialLogEntry,
      string foreignID = null,
      string originalMessage = null
    );
    long AllocateMaterialIDForCasting(string casting);
    MaterialDetails AllocateMaterialIDWithSerialAndWorkorder(
      string unique,
      string part,
      int numProc,
      string serial,
      string workorder,
      out IEnumerable<LogEntry> newLogEntries,
      DateTime? timeUTC = null
    );
    void SetDetailsForMaterialID(long matID, string unique, string part, int? numProc);
    void RecordPathForProcess(long matID, int process, int path);
    void CreateMaterialID(long matID, string unique, string part, int numProc);
    MaterialDetails GetMaterialDetails(long matID);
    IReadOnlyList<MaterialDetails> GetMaterialDetailsForSerial(string serial);
    List<MaterialDetails> GetMaterialForWorkorder(string workorder);
    long CountMaterialForWorkorder(string workorder, string part = null);
    List<MaterialDetails> GetMaterialForJobUnique(string jobUnique);
    long CountMaterialForJobUnique(string jobUnique);

    // --------------------------------------------------------------------------------
    // Queues
    // --------------------------------------------------------------------------------
    IReadOnlyList<long> AllocateCastingsInQueue(
      string queue,
      string casting,
      string unique,
      string part,
      int proc1Path,
      int numProcesses,
      int count
    );
    void MarkCastingsAsUnallocated(IEnumerable<long> matIds, string casting);
    bool IsMaterialInQueue(long matId);
    IEnumerable<QueuedMaterial> GetMaterialInQueueByUnique(string queue, string jobUnique);
    IEnumerable<QueuedMaterial> GetUnallocatedMaterialInQueue(string queue, string partNameOrCasting);
    IEnumerable<QueuedMaterial> GetMaterialInAllQueues();
    int? NextProcessForQueuedMaterial(long matId);
    BulkAddCastingResult BulkAddNewCastingsInQueue(
      string casting,
      int qty,
      string queue,
      IList<string> serials,
      string workorder,
      string operatorName,
      string reason = null,
      DateTime? timeUTC = null,
      bool throwOnExistingSerial = false
    );

    // --------------------------------------------------------------------------------
    // Pending Loads
    // --------------------------------------------------------------------------------
    void AddPendingLoad(int pal, string key, int load, TimeSpan elapsed, TimeSpan active, string foreignID);
    List<PendingLoad> PendingLoads(int pallet);
    List<PendingLoad> AllPendingLoads();
    void CancelPendingLoads(string foreignID);
    LogEntry CompletePalletCycle(int pal, DateTime timeUTC, string foreignID = null);
    (LogEntry, IEnumerable<LogEntry>) CompletePalletCycle(
      int pal,
      DateTime timeUTC,
      IReadOnlyDictionary<string, IEnumerable<EventLogMaterial>> matFromPendingLoads,
      IEnumerable<MaterialToLoadOntoPallet> additionalLoads,
      bool generateSerials = false,
      string foreignID = null
    );

    // --------------------------------------------------------------------------------
    // Inspections
    // --------------------------------------------------------------------------------
    List<InspectCount> LoadInspectCounts();
    void SetInspectCounts(IEnumerable<InspectCount> counts);
    IReadOnlyList<Decision> LookupInspectionDecisions(long matID);
    ImmutableDictionary<long, IReadOnlyList<Decision>> LookupInspectionDecisions(IEnumerable<long> matID);
    IEnumerable<LogEntry> MakeInspectionDecisions(
      long matID,
      int process,
      IEnumerable<PathInspection> inspections,
      DateTime? mutcNow = null
    );
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
    ImmutableList<HistoricJob> LoadJobsBetween(string startingUniqueStr, string endingUniqueStr);
    bool DoesJobExist(string unique);
    ImmutableList<HistoricJob> LoadUnarchivedJobs();
    ImmutableList<HistoricJob> LoadJobsNotCopiedToSystem(
      DateTime startUTC,
      DateTime endUTC,
      bool includeDecremented = true
    );
    HistoricData LoadJobHistory(
      DateTime startUTC,
      DateTime endUTC,
      IEnumerable<string> alreadyKnownSchIds = null
    );
    RecentHistoricData LoadRecentJobHistory(DateTime startUTC, IEnumerable<string> alreadyKnownSchIds = null);
    PlannedSchedule LoadMostRecentSchedule();
    ImmutableList<Workorder> WorkordersById(string workorderId);
    ImmutableDictionary<string, ImmutableList<Workorder>> WorkordersById(IReadOnlySet<string> workorderId);

    // --------------------------------------------------------------------------------
    // Adding and Updating Jobs
    // --------------------------------------------------------------------------------
    void AddJobs(NewJobs newJobs, string expectedPreviousScheduleId, bool addAsCopiedToSystem);
    void AddPrograms(IEnumerable<NewProgramContent> programs, DateTime startingUtc);
    void ArchiveJob(string UniqueStr);
    void ArchiveJobs(
      IEnumerable<string> uniqueStrs,
      IEnumerable<NewDecrementQuantity> newDecrements = null,
      DateTime? nowUTC = null
    );
    void UnarchiveJob(string UniqueStr);
    void UnarchiveJobs(IEnumerable<string> uniqueStrs, DateTime? nowUTC = null);
    void MarkJobCopiedToSystem(string UniqueStr);
    void SetJobComment(string unique, string comment);
    void UpdateJobHold(string unique, HoldPattern newHold);
    void UpdateJobLoadUnloadHold(string unique, int proc, int path, HoldPattern newHold);
    void UpdateJobMachiningHold(string unique, int proc, int path, HoldPattern newHold);

    // --------------------------------------------------------------------------------
    // Decrements
    // --------------------------------------------------------------------------------
    void AddNewDecrement(
      IEnumerable<NewDecrementQuantity> counts,
      DateTime? nowUTC = null,
      IEnumerable<RemovedBooking> removedBookings = null
    );
    ImmutableList<DecrementQuantity> LoadDecrementsForJob(string unique);
    List<JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(long afterId);
    List<JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(DateTime afterUTC);

    // --------------------------------------------------------------------------------
    // Programs
    // --------------------------------------------------------------------------------
    ProgramRevision LoadProgram(string program, long revision);
    ProgramRevision LoadMostRecentProgram(string program);
    string LoadProgramContent(string program, long revision);
    ImmutableList<ProgramRevision> LoadProgramRevisionsInDescendingOrderOfRevision(
      string program,
      int count,
      long? startRevision
    );
    List<ProgramRevision> LoadProgramsInCellController();
    ProgramRevision ProgramFromCellControllerProgram(string cellCtProgName);
    void SetCellControllerProgramForProgram(string program, long revision, string cellCtProgName);
  }

  public record InspectCount
  {
    public required string Counter { get; init; }
    public required int Value { get; init; }
    public required DateTime LastUTC { get; init; }
  }

  [Draftable]
  public record EventLogMaterial
  {
    public required long MaterialID { get; init; }
    public required int Process { get; init; }
    public required int Face { get; init; }

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
    public required IEnumerable<LogEntry> ChangedLogEntries { get; init; }
    public required IEnumerable<LogEntry> NewLogEntries { get; init; }
  }

  public record Decision
  {
    public required long MaterialID { get; init; }
    public required string InspType { get; init; }
    public required string Counter { get; init; }
    public required bool Inspect { get; init; }
    public required bool Forced { get; init; }
    public required System.DateTime CreateUTC { get; init; }
  }

  public record PendingLoad
  {
    public required int Pallet { get; init; }
    public required string Key { get; init; }
    public required int LoadStation { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required TimeSpan ActiveOperationTime { get; init; }
    public required string ForeignID { get; init; }
  }

  public record MaterialToLoadOntoFace
  {
    public required ImmutableList<long> MaterialIDs { get; init; }
    public required int FaceNum { get; init; }
    public required int Process { get; init; }
    public required int? Path { get; init; }
    public required TimeSpan ActiveOperationTime { get; init; }
  }

  public record MaterialToLoadOntoPallet
  {
    public required int LoadStation { get; init; }
    public TimeSpan Elapsed { get; init; }
    public ImmutableList<MaterialToLoadOntoFace> Faces { get; init; }
  }

  public record QueuedMaterial
  {
    public required long MaterialID { get; init; }
    public required string Queue { get; init; }
    public required int Position { get; init; }
    public required string Unique { get; init; }
    public required string PartNameOrCasting { get; init; }
    public required int NumProcesses { get; init; }
    public string Serial { get; init; }
    public string Workorder { get; init; }
    public required ImmutableDictionary<int, int> Paths { get; init; } // key is process, value is path
    public DateTime? AddTimeUTC { get; init; }
    public int? NextProcess { get; init; }
  }

  [Draftable]
  public record NewDecrementQuantity
  {
    public required string JobUnique { get; init; }
    public required string Part { get; init; }
    public required int Quantity { get; init; }
  }

  public record RemovedBooking
  {
    public required string JobUnique { get; init; }
    public required string BookingId { get; init; }
  }

  public record BulkAddCastingResult
  {
    public required HashSet<long> MaterialIds { get; init; }
    public required IReadOnlyList<LogEntry> Logs { get; init; }
  }
}
