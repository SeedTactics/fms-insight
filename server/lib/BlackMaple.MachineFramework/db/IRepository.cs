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

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace BlackMaple.MachineFramework
{
  public interface IRepository : IDisposable
  {
    RepositoryConfig RepoConfig { get; }

    // --------------------------------------------------------------------------------
    // Loading Events
    // --------------------------------------------------------------------------------
    IEnumerable<LogEntry> GetRecentLog(
      long lastSeenCounter,
      DateTime? expectedEndUTCofLastSeen = null
    );
    IEnumerable<LogEntry> GetLogEntries(DateTime startUTC, DateTime endUTC);
    IEnumerable<LogEntry> GetLogOfAllCompletedParts(DateTime startUTC, DateTime endUTC);
    IEnumerable<LogEntry> GetLogForJobUnique(string jobUnique);
    IEnumerable<LogEntry> CompletedUnloadsSince(long counter);
    IEnumerable<LogEntry> GetLogForMaterial(long materialID, bool includeInvalidatedCycles = true);
    IEnumerable<LogEntry> GetLogForMaterial(
      IEnumerable<long> materialIDs,
      bool includeInvalidatedCycles = true
    );
    IEnumerable<LogEntry> GetLogForSerial(string serial);
    IEnumerable<LogEntry> GetLogForWorkorder(string workorder);
    List<LogEntry> CurrentPalletLog(int pallet, bool includeLastPalletCycleEvt = false);
    List<LogEntry> CurrentBasketLog(int basketId, bool includeLastCycleEvt = false);
    ImmutableList<LogEntry> CurrentBasketLog(
      ContainerIdentity basketIdentity,
      bool includeLastCycleEvt = false
    );
    ImmutableList<CurrentBasketIdentityHint> GetCurrentBasketIdentityHints(int? basketNum = null);
    ImmutableList<Guid> GetUnresolvedOpenBasketContainerIds();
    ImmutableDictionary<Guid, CurrentBasketIdentityHint> ReconstructBasketIdentityHints();
    IEnumerable<ToolSnapshot> ToolPocketSnapshotForCycle(long counter);
    bool CycleExists(DateTime endUTC, int pal, LogType logTy, string locName, int locNum);
    ImmutableList<ActiveWorkorder> GetActiveWorkorder(string workorder);
    ImmutableList<ActiveWorkorder> GetActiveWorkorders(
      IReadOnlySet<string> additionalWorkorders = null
    );
    ImmutableSortedSet<string> GetWorkordersForUnique(string jobUnique);
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
    // A foreignId is optional correlation or source metadata stored on each event. It is not unique
    // and does not make an event write idempotent. Callers may deliberately use one foreignId for
    // several related events. Methods with an idempotencyKey instead define an atomic retry
    // contract: identical retries return the original result and changed durable input conflicts.
    LogEntry RecordLoadStart(
      IEnumerable<EventLogMaterial> mats,
      int pallet,
      int lulNum,
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
    LogEntry RecordBasketLoadBegin(
      IEnumerable<EventLogMaterial> mats,
      int basketId,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordBasketLoadBegin(
      IEnumerable<EventLogMaterial> mats,
      ContainerIdentity basketIdentity,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordBasketUnloadBegin(
      IEnumerable<EventLogMaterial> mats,
      int basketId,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordBasketUnloadBegin(
      IEnumerable<EventLogMaterial> mats,
      ContainerIdentity basketIdentity,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );

    // RecordPartialLoadUnload is for partial pallet <-> queue or pallet <-> basket events.
    // The phrases load and unload in this method refer to the pallet, so toLoad is material
    // being loaded onto the pallet.  Material here must later be passed to `RecordLoadUnloadComplete`.
    IEnumerable<LogEntry> RecordPartialLoadUnload(
      IReadOnlyList<MaterialToLoadOntoFace> toLoad,
      IReadOnlyList<MaterialToUnloadFromFace> toUnload,
      int lulNum,
      int pallet,
      TimeSpan totalElapsed,
      DateTime timeUTC,
      IReadOnlyDictionary<string, string> externalQueues,
      PalletBasketLoadUnloadCompletion palletBasketCompletion = null
    );

    // The main method for recording a completed pallet load/unload, which combines
    // pallet <-> queue and pallet <-> basket operations along with any previously
    // recorded partial options in calls to `RecordPartialLoadUnload`. This emits pallet cycle
    // events. Basket transfer evidence and basket cycle boundaries are emitted only from the
    // optional explicit basket completion.
    IEnumerable<LogEntry> RecordLoadUnloadComplete(
      IReadOnlyList<MaterialToLoadOntoFace> toLoad,
      IReadOnlyList<EventLogMaterial> previouslyLoaded,
      IReadOnlyList<MaterialToUnloadFromFace> toUnload,
      IReadOnlyList<EventLogMaterial> previouslyUnloaded,
      int lulNum,
      int pallet,
      TimeSpan totalElapsed,
      DateTime timeUTC,
      IReadOnlyDictionary<string, string> externalQueues,
      PalletBasketLoadUnloadCompletion palletBasketCompletion = null
    );

    // Atomically records one completed basket-station operation. Each transfer owns its basket
    // identity so a physical turnover can unload one UUID episode and load another. Queue changes,
    // operation timing, transfer evidence, and complete-content cycle boundaries are committed
    // under one idempotency key. An identical retry returns the original event group; changed
    // durable input throws ConflictRequestException. timeUTC is intentionally excluded from retry
    // comparison. Cycle-boundary events record lulNum as their location number. foreignId remains
    // optional event correlation metadata.
    //
    // totalElapsed is apportioned among transfers in proportion to ActiveOperationTime when every
    // transfer has a positive expected time. Otherwise, it is apportioned by material count.
    //
    // Local queue changes are part of the database transaction. Delivery to a configured external
    // queue is best-effort after commit and is not covered by the atomic retry contract.
    IEnumerable<LogEntry> RecordBasketStationOperation(
      BasketStationOperation operation,
      int lulNum,
      TimeSpan totalElapsed,
      DateTime timeUTC,
      IReadOnlyDictionary<string, string> externalQueues,
      string idempotencyKey,
      string foreignId = null,
      string originalMessage = null
    );

    IEnumerable<LogEntry> RecordEmptyPallet(
      int pallet,
      DateTime timeUTC,
      string foreignId = null,
      bool palletEnd = false
    );
    IEnumerable<LogEntry> RecordEmptyBasket(
      int basketId,
      int lulNum,
      DateTime timeUTC,
      string foreignId = null,
      bool basketEnd = false
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
    LogEntry RecordBasketArriveLocation(
      IEnumerable<EventLogMaterial> mats,
      int basketId,
      string locationName,
      int locationPosition,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordBasketArriveLocation(
      IEnumerable<EventLogMaterial> mats,
      ContainerIdentity basketIdentity,
      string locationName,
      int locationPosition,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordBasketDepartLocation(
      IEnumerable<EventLogMaterial> mats,
      int basketId,
      string locationName,
      int locationPosition,
      DateTime timeUTC,
      TimeSpan elapsed,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordBasketDepartLocation(
      IEnumerable<EventLogMaterial> mats,
      ContainerIdentity basketIdentity,
      string locationName,
      int locationPosition,
      DateTime timeUTC,
      TimeSpan elapsed,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordBasketContentSnapshot(
      IEnumerable<EventLogMaterial> mats,
      ContainerIdentity basketIdentity,
      DateTime timeUTC,
      string foreignId = null,
      string originalMessage = null
    );
    LogEntry RecordBasketIdentityHint(
      Guid containerId,
      int basketNum,
      DateTime timeUTC,
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
    LogEntry RecordWorkorderForMaterialID(
      EventLogMaterial mat,
      string workorder,
      DateTime recordUtc
    );
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
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active
    );
    LogEntry RecordCloseoutCompleted(
      EventLogMaterial mat,
      int locNum,
      string closeoutType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active
    );
    LogEntry RecordCloseoutCompleted(
      EventLogMaterial mat,
      int locNum,
      string closeoutType,
      bool success,
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
    LogEntry RecordGeneralMessage(
      IEnumerable<EventLogMaterial> mats,
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
      string operatorName,
      DateTime? timeUTC = null
    );
    IEnumerable<LogEntry> InvalidateAndChangeAssignment(
      long matId,
      string operatorName,
      string changeJobUniqueTo,
      string changePartNameTo,
      int changeNumProcessesTo,
      DateTime? timeUTC = null
    );
    LogEntry CreateRebooking(
      string bookingId,
      string partName,
      int qty = 1,
      string notes = null,
      int? priority = null,
      string workorder = null,
      DateTime? timeUTC = null
    );
    LogEntry CancelRebooking(string bookingId, DateTime? timeUTC = null);
    Rebooking LookupRebooking(string bookingId);

    // --------------------------------------------------------------------------------
    // Material IDs
    // --------------------------------------------------------------------------------
    long AllocateMaterialID(string unique, string part, int numProc);

    /// <summary>
    /// Allocates a batch of ordinary sequential material IDs exactly once. An identical retry
    /// returns the original allocation in request order; changed material details throw
    /// <see cref="ConflictRequestException"/>.
    /// </summary>
    ImmutableList<MaterialDetails> AllocateMaterialIDs(
      ImmutableList<MaterialToAllocate> material,
      string idempotencyKey
    );
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
    IEnumerable<QueuedMaterial> GetUnallocatedMaterialInQueue(
      string queue,
      string partNameOrCasting
    );
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
    // Inspections
    // --------------------------------------------------------------------------------
    List<InspectCount> LoadInspectCounts();
    void SetInspectCounts(IEnumerable<InspectCount> counts);
    IReadOnlyList<Decision> LookupInspectionDecisions(long matID);
    ImmutableDictionary<long, IReadOnlyList<Decision>> LookupInspectionDecisions(
      IEnumerable<long> matID
    );
    IEnumerable<LogEntry> MakeInspectionDecisions(
      long matID,
      int process,
      IEnumerable<PathInspection> inspections,
      DateTime? mutcNow = null
    );
    LogEntry StoreInspectionDecision(
      long matID,
      int proc,
      PathInspection insp,
      bool inspect,
      DateTime? utcNow = null
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
    RecentHistoricData LoadRecentJobHistory(
      DateTime startUTC,
      IEnumerable<string> alreadyKnownSchIds = null
    );
    MostRecentSchedule LoadMostRecentSchedule();
    IEnumerable<string> StationGroupsOnMostRecentSchedule();
    (
      ImmutableHashSet<string> rawMatQ,
      ImmutableHashSet<string> inProcQ
    ) QueuesOnMostRecentSchedule();
    ImmutableList<Workorder> WorkordersById(string workorderId);
    ImmutableDictionary<string, ImmutableList<Workorder>> WorkordersById(
      IReadOnlySet<string> workorderId
    );
    ImmutableList<Rebooking> LoadUnscheduledRebookings();

    // --------------------------------------------------------------------------------
    // Adding and Updating Jobs
    // --------------------------------------------------------------------------------
    void AddJobs(NewJobs newJobs, string expectedPreviousScheduleId, bool addAsCopiedToSystem);
    void AddPrograms(IEnumerable<NewProgramContent> programs, DateTime startingUtc);
    void UpdateCachedWorkorders(IEnumerable<Workorder> workorders);
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
        Face = m.Face,
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
    public string ForeignID { get; init; } = null;
    public string OriginalMessage { get; init; } = null;
  }

  public record UnloadDestination
  {
    public string Queue { get; init; }
  }

  /// <summary>
  /// Basket-side evidence for material transferred during a pallet load/unload. Timing is recorded
  /// on the corresponding pallet event.
  /// </summary>
  public abstract record PalletBasketTransfer
  {
    private PalletBasketTransfer() { }

    public required ContainerIdentity BasketIdentity { get; init; }
    public required ImmutableList<EventLogMaterial> Material { get; init; }

    public sealed record LoadOntoBasket : PalletBasketTransfer;

    public sealed record UnloadFromBasket : PalletBasketTransfer;
  }

  public abstract record BasketCycleBoundary
  {
    private BasketCycleBoundary() { }

    public required ContainerIdentity BasketIdentity { get; init; }

    /// <summary>
    /// Slot-aware material associated with the boundary. An end declares the complete material
    /// carried during the ending cycle. A start declares the complete basket contents at the
    /// boundary; later basket loads may add material before that cycle ends.
    /// </summary>
    public required ImmutableList<EventLogMaterial> Material { get; init; }

    public sealed record End : BasketCycleBoundary
    {
      /// <summary>
      /// Durable UUID basket identities reconciled into this numbered cycle. Leave empty when every
      /// event in the cycle was already recorded with the numbered identity.
      /// </summary>
      public required ImmutableHashSet<Guid> ReconciledBasketIdentities { get; init; }
    }

    public sealed record Start : BasketCycleBoundary
    {
      /// <summary>
      /// Atomically records the current numbered-basket association for a UUID cycle start. Leave
      /// null for a numbered start or when the numbered identity is not known.
      /// </summary>
      public int? AssociatedBasketNum { get; init; }
    }
  }

  public sealed record PalletBasketLoadUnloadCompletion
  {
    public required ImmutableList<PalletBasketTransfer> Transfers { get; init; }
    public required ImmutableList<BasketCycleBoundary> CycleBoundaries { get; init; }
  }

  public abstract record BasketStationTransfer
  {
    private BasketStationTransfer() { }

    public required ContainerIdentity BasketIdentity { get; init; }
    public required ImmutableList<EventLogMaterial> Material { get; init; }

    /// <summary>
    /// Expected accounting time for this entire transfer, including every material in
    /// <see cref="Material"/>.
    /// </summary>
    public required TimeSpan ActiveOperationTime { get; init; }

    public sealed record LoadOntoBasket : BasketStationTransfer;

    public sealed record UnloadFromBasket : BasketStationTransfer
    {
      /// <summary>
      /// The local or configured external destination queue, or null when the material is
      /// transferred directly without entering a queue.
      /// </summary>
      public string DestinationQueue { get; init; }
    }
  }

  public sealed record BasketStationOperation
  {
    public required ImmutableList<BasketStationTransfer> Transfers { get; init; }
    public required ImmutableList<BasketCycleBoundary> CycleBoundaries { get; init; }
  }

  public record MaterialToUnloadFromFace
  {
    public required ImmutableDictionary<
      long,
      UnloadDestination
    > MaterialIDToDestination { get; init; }
    public required int FaceNum { get; init; }
    public required int Process { get; init; }
    public required TimeSpan ActiveOperationTime { get; init; }
    public string ForeignID { get; init; } = null;
    public string OriginalMessage { get; init; } = null;
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
