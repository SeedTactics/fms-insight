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
using System.Data;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework
{
  public interface IRepository : IDisposable
  {
    void AddPendingLoad(string pal, string key, int load, TimeSpan elapsed, TimeSpan active, string foreignID);
    IReadOnlyList<long> AllocateCastingsInQueue(string queue, string casting, string unique, string part, int proc1Path, int numProcesses, int count);
    long AllocateMaterialID(string unique, string part, int numProc);
    long AllocateMaterialIDForCasting(string casting);
    List<Repository.PendingLoad> AllPendingLoads();
    void CheckMaterialForNextPeiceInspection(MachineWatchInterface.PalletLocation palLoc, long matID);
    void CompletePalletCycle(string pal, DateTime timeUTC, string foreignID);
    void CompletePalletCycle(string pal, DateTime timeUTC, string foreignID, IDictionary<string, IEnumerable<Repository.EventLogMaterial>> mat, bool generateSerials);
    void CreateMaterialID(long matID, string unique, string part, int numProc);
    List<MachineWatchInterface.LogEntry> CurrentPalletLog(string pallet);
    bool CycleExists(DateTime endUTC, string pal, MachineWatchInterface.LogType logTy, string locName, int locNum);
    void ForceInspection(long matID, string inspType);
    MachineWatchInterface.LogEntry ForceInspection(long materialID, int process, string inspType, bool inspect);
    MachineWatchInterface.LogEntry ForceInspection(Repository.EventLogMaterial mat, string inspType, bool inspect);
    MachineWatchInterface.LogEntry ForceInspection(Repository.EventLogMaterial mat, string inspType, bool inspect, DateTime utcNow);
    string ForeignIDForCounter(long counter);
    List<MachineWatchInterface.LogEntry> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC);
    List<MachineWatchInterface.LogEntry> GetLog(long counter);
    List<MachineWatchInterface.LogEntry> GetLogEntries(DateTime startUTC, DateTime endUTC);
    List<MachineWatchInterface.LogEntry> GetLogForJobUnique(string jobUnique);
    List<MachineWatchInterface.LogEntry> GetLogForMaterial(long materialID);
    List<MachineWatchInterface.LogEntry> GetLogForMaterial(IEnumerable<long> materialIDs);
    List<MachineWatchInterface.LogEntry> GetLogForSerial(string serial);
    List<MachineWatchInterface.LogEntry> GetLogForWorkorder(string workorder);
    MachineWatchInterface.MaterialDetails GetMaterialDetails(long matID);
    IReadOnlyList<MachineWatchInterface.MaterialDetails> GetMaterialDetailsForSerial(string serial);
    List<MachineWatchInterface.MaterialDetails> GetMaterialForJobUnique(string jobUnique);
    List<MachineWatchInterface.MaterialDetails> GetMaterialForWorkorder(string workorder);
    IEnumerable<Repository.QueuedMaterial> GetMaterialInAllQueues();
    IEnumerable<Repository.QueuedMaterial> GetMaterialInQueue(string queue);
    List<string> GetWorkordersForUnique(string jobUnique);
    List<MachineWatchInterface.WorkorderSummary> GetWorkorderSummaries(IEnumerable<string> workorders);
    IEnumerable<MachineWatchInterface.LogEntry> InvalidatePalletCycle(long matId, int process, string oldMatPutInQueue, string operatorName, DateTime? timeUTC = null);
    DateTime LastPalletCycleTime(string pallet);
    List<InspectCount> LoadInspectCounts();
    IList<Repository.Decision> LookupInspectionDecisions(long matID);
    IEnumerable<MachineWatchInterface.LogEntry> MakeInspectionDecisions(long matID, int process, IEnumerable<MachineWatchInterface.PathInspection> inspections, DateTime? mutcNow = null);
    void MarkCastingsAsUnallocated(IEnumerable<long> matIds, string casting);
    string MaxForeignID();
    DateTime MaxLogDate();
    void NextPieceInspection(MachineWatchInterface.PalletLocation palLoc, string inspType);
    int? NextProcessForQueuedMaterial(long matId);
    string OriginalMessageByForeignID(string foreignID);
    List<Repository.PendingLoad> PendingLoads(string pallet);
    IEnumerable<MachineWatchInterface.LogEntry> RawAddLogEntries(IEnumerable<Repository.NewEventLogEntry> logs, string foreignId = null, string origMessage = null);
    IEnumerable<MachineWatchInterface.LogEntry> RecordAddMaterialToQueue(Repository.EventLogMaterial mat, string queue, int position, string operatorName, string reason, DateTime? timeUTC = null);
    IEnumerable<MachineWatchInterface.LogEntry> RecordAddMaterialToQueue(long matID, int process, string queue, int position, string operatorName, string reason, DateTime? timeUTC = null);
    MachineWatchInterface.LogEntry RecordFinalizedWorkorder(string workorder);
    MachineWatchInterface.LogEntry RecordFinalizedWorkorder(string workorder, DateTime finalizedUTC);
    MachineWatchInterface.LogEntry RecordGeneralMessage(Repository.EventLogMaterial mat, string program, string result, string pallet = "", DateTime? timeUTC = null, string foreignId = null, string originalMessage = null, IDictionary<string, string> extraData = null);
    MachineWatchInterface.LogEntry RecordInspectionCompleted(long materialID, int process, int inspectionLocNum, string inspectionType, bool success, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    MachineWatchInterface.LogEntry RecordInspectionCompleted(Repository.EventLogMaterial mat, int inspectionLocNum, string inspectionType, bool success, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    MachineWatchInterface.LogEntry RecordInspectionCompleted(Repository.EventLogMaterial mat, int inspectionLocNum, string inspectionType, bool success, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active, DateTime inspectTimeUTC);
    IEnumerable<MachineWatchInterface.LogEntry> RecordLoadEnd(IEnumerable<Repository.EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordLoadStart(IEnumerable<Repository.EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordMachineEnd(IEnumerable<Repository.EventLogMaterial> mats, string pallet, string statName, int statNum, string program, string result, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, IDictionary<string, string> extraData = null, IDictionary<string, MachineWatchInterface.ToolUse> tools = null, IEnumerable<Repository.ToolPocketSnapshot> pockets = null, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordMachineStart(IEnumerable<Repository.EventLogMaterial> mats, string pallet, string statName, int statNum, string program, DateTime timeUTC, IDictionary<string, string> extraData = null, IEnumerable<Repository.ToolPocketSnapshot> pockets = null, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordManualWorkAtLULEnd(IEnumerable<Repository.EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, string operationName, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordManualWorkAtLULStart(IEnumerable<Repository.EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, string operationName, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordOperatorNotes(long materialId, int process, string notes, string operatorName);
    MachineWatchInterface.LogEntry RecordOperatorNotes(long materialId, int process, string notes, string operatorName, DateTime? timeUtc);
    MachineWatchInterface.LogEntry RecordPalletArriveRotaryInbound(IEnumerable<Repository.EventLogMaterial> mats, string pallet, string statName, int statNum, DateTime timeUTC, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordPalletArriveStocker(IEnumerable<Repository.EventLogMaterial> mats, string pallet, int stockerNum, DateTime timeUTC, bool waitForMachine, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordPalletDepartRotaryInbound(IEnumerable<Repository.EventLogMaterial> mats, string pallet, string statName, int statNum, DateTime timeUTC, TimeSpan elapsed, bool rotateIntoWorktable, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordPalletDepartStocker(IEnumerable<Repository.EventLogMaterial> mats, string pallet, int stockerNum, DateTime timeUTC, bool waitForMachine, TimeSpan elapsed, string foreignId = null, string originalMessage = null);
    void RecordPathForProcess(long matID, int process, int path);
    IEnumerable<MachineWatchInterface.LogEntry> RecordRemoveMaterialFromAllQueues(Repository.EventLogMaterial mat, string operatorName = null, DateTime? timeUTC = null);
    IEnumerable<MachineWatchInterface.LogEntry> RecordRemoveMaterialFromAllQueues(long matID, int process, string operatorName = null, DateTime? timeUTC = null);
    MachineWatchInterface.LogEntry RecordSerialForMaterialID(long materialID, int proc, string serial);
    MachineWatchInterface.LogEntry RecordSerialForMaterialID(Repository.EventLogMaterial mat, string serial);
    MachineWatchInterface.LogEntry RecordSerialForMaterialID(Repository.EventLogMaterial mat, string serial, DateTime endTimeUTC);
    IEnumerable<MachineWatchInterface.LogEntry> RecordUnloadEnd(IEnumerable<Repository.EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, TimeSpan elapsed, TimeSpan active, Dictionary<long, string> unloadIntoQueues = null, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordUnloadStart(IEnumerable<Repository.EventLogMaterial> mats, string pallet, int lulNum, DateTime timeUTC, string foreignId = null, string originalMessage = null);
    MachineWatchInterface.LogEntry RecordWashCompleted(long materialID, int process, int washLocNum, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    MachineWatchInterface.LogEntry RecordWashCompleted(Repository.EventLogMaterial mat, int washLocNum, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active);
    MachineWatchInterface.LogEntry RecordWashCompleted(Repository.EventLogMaterial mat, int washLocNum, IDictionary<string, string> extraData, TimeSpan elapsed, TimeSpan active, DateTime completeTimeUTC);
    MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(long materialID, int proc, string workorder);
    MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(Repository.EventLogMaterial mat, string workorder);
    MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(Repository.EventLogMaterial mat, string workorder, DateTime recordUtc);
    void SetDetailsForMaterialID(long matID, string unique, string part, int? numProc);
    void SetInspectCounts(IEnumerable<InspectCount> counts);
    MachineWatchInterface.LogEntry SignalMaterialForQuarantine(Repository.EventLogMaterial mat, string pallet, string queue, DateTime? timeUTC = null, string operatorName = null, string foreignId = null, string originalMessage = null);
    List<MachineWatchInterface.LogEntry> StationLogByForeignID(string foreignID);
    Repository.SwapMaterialResult SwapMaterialInCurrentPalletCycle(string pallet, long oldMatId, long newMatId, string operatorName, DateTime? timeUTC = null);
    IEnumerable<Repository.ToolPocketSnapshot> ToolPocketSnapshotForCycle(long counter);



    void AddJobs(MachineWatchInterface.NewJobs newJobs, string expectedPreviousScheduleId);
    void AddNewDecrement(IEnumerable<Repository.NewDecrementQuantity> counts, DateTime? nowUTC = null, IEnumerable<Repository.RemovedBooking> removedBookings = null);
    void AddPrograms(IEnumerable<MachineWatchInterface.ProgramEntry> programs, DateTime startingUtc);
    void ArchiveJob(string UniqueStr);
    void ArchiveJobs(IEnumerable<string> uniqueStrs, IEnumerable<Repository.NewDecrementQuantity> newDecrements = null, DateTime? nowUTC = null);
    bool DoesJobExist(string unique);
    List<MachineWatchInterface.JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(long afterId);
    List<MachineWatchInterface.JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(DateTime afterUTC);
    List<MachineWatchInterface.DecrementQuantity> LoadDecrementsForJob(string unique);
    MachineWatchInterface.JobPlan LoadJob(string UniqueStr);
    MachineWatchInterface.HistoricData LoadJobHistory(DateTime startUTC, DateTime endUTC);
    MachineWatchInterface.HistoricData LoadJobsAfterScheduleId(string schId);
    List<MachineWatchInterface.JobPlan> LoadJobsNotCopiedToSystem(DateTime startUTC, DateTime endUTC, bool includeDecremented = true);
    MachineWatchInterface.ProgramRevision LoadMostRecentProgram(string program);
    MachineWatchInterface.PlannedSchedule LoadMostRecentSchedule();
    MachineWatchInterface.ProgramRevision LoadProgram(string program, long revision);
    string LoadProgramContent(string program, long revision);
    List<MachineWatchInterface.ProgramRevision> LoadProgramRevisionsInDescendingOrderOfRevision(string program, int count, long? startRevision);
    List<MachineWatchInterface.ProgramRevision> LoadProgramsInCellController();
    List<MachineWatchInterface.JobPlan> LoadUnarchivedJobs();
    void MarkJobCopiedToSystem(string UniqueStr);
    List<MachineWatchInterface.PartWorkorder> MostRecentUnfilledWorkordersForPart(string part);
    List<MachineWatchInterface.PartWorkorder> MostRecentWorkorders();
    MachineWatchInterface.ProgramRevision ProgramFromCellControllerProgram(string cellCtProgName);
    void ReplaceWorkordersForSchedule(string scheduleId, IEnumerable<MachineWatchInterface.PartWorkorder> newWorkorders, IEnumerable<MachineWatchInterface.ProgramEntry> programs, DateTime? nowUtc = null);
    void SetCellControllerProgramForProgram(string program, long revision, string cellCtProgName);
    void SetJobComment(string unique, string comment);
    void UnarchiveJob(string UniqueStr);
    void UnarchiveJobs(IEnumerable<string> uniqueStrs, DateTime? nowUTC = null);
    void UpdateJobHold(string unique, MachineWatchInterface.JobHoldPattern newHold);
    void UpdateJobLoadUnloadHold(string unique, int proc, int path, MachineWatchInterface.JobHoldPattern newHold);
    void UpdateJobMachiningHold(string unique, int proc, int path, MachineWatchInterface.JobHoldPattern newHold);
    List<MachineWatchInterface.PartWorkorder> WorkordersById(string workorderId);

  }

  public struct InspectCount
  {
    public string Counter;
    public int Value;
    public DateTime LastUTC;
  }
}