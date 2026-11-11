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
#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

public record MazakState : ICellState
{
  public required bool StateUpdated { get; init; }
  public required TimeSpan TimeUntilNextRefresh { get; init; }
  public required bool StoppedBecauseRecentLogEvent { get; init; }
  public required CurrentStatus CurrentStatus { get; init; }
  public required MazakAllDataAndLogs AllData { get; init; }

  // Insight pallet number whose final load/unload batch is incomplete. Retain this context
  // alongside AllData when rebuilding status against newer repository facts without replaying logs.
  public int? PalletWithUnprocessedUnloads { get; init; }
}

// Acquisition owns no repository connection. Its interval describes observation provenance,
// not an atomic controller snapshot. Reconciliation combines it with current repository facts.
public sealed record MazakAcquisition
{
  public required DateTime StartedAtUtc { get; init; }

  // The settling reference is captured after controller rows/logs finish loading, before tools.
  public required DateTime DataLoadedAtUtc { get; init; }
  public required DateTime CompletedAtUtc { get; init; }
  public required MazakAllDataAndLogs Data { get; init; }

  // Null means unobserved; an empty list means a successful observation of no tools.
  public required ImmutableList<ToolPocketRow>? Tools { get; init; }
  public bool ToolReadFailed { get; init; }
}

// Retain this receipt across acknowledgment failure. Its watermark is captured at reconciliation,
// so acknowledging it later cannot consume subsequently committed evidence.
public sealed record MazakReconciliation
{
  public required MazakState State { get; init; }
  public required string CommittedLogWatermark { get; init; }
}

public delegate void MazakLogEventDel(LogEntry e, IRepository jobDB);

public interface INotifyMazakLogEvent
{
  event MazakLogEventDel? MazakLogEvent;
}

public sealed class MazakSync : ISynchronizeCellState<MazakState>, INotifyMazakLogEvent, IDisposable
{
  private const string MazakCsvForeignIDLowerBound = "LG";
  private const string MazakCsvForeignIDUpperBound = "LH";

  public static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MazakSync>();
  public event Action? NewCellState;
  public event MazakLogEventDel? MazakLogEvent;

  private readonly IMazakDB mazakDB;
  private readonly FMSSettings settings;
  private readonly MazakConfig mazakConfig;
  private readonly TimeProvider timeProvider;

  // Last accepted acquisition's tool attempt, independently of manufacturing/status success.
  private bool lastToolReadFailed;
  private ImmutableList<CurrentStatusAlarm> ingestionDiagnostics = [];

  public MazakSync(IMazakDB mazakDb, FMSSettings settings, MazakConfig mazakConfig)
    : this(mazakDb, settings, mazakConfig, TimeProvider.System) { }

  public MazakSync(
    IMazakDB mazakDb,
    FMSSettings settings,
    MazakConfig mazakConfig,
    TimeProvider timeProvider
  )
  {
    this.mazakDB = mazakDb;
    this.settings = settings;
    this.mazakConfig = mazakConfig;
    this.timeProvider = timeProvider;
    mazakDB.OnNewEvent += RaiseNewCellState;
  }

  public void Dispose()
  {
    mazakDB.OnNewEvent -= RaiseNewCellState;
  }

  private void RaiseNewCellState()
  {
    NewCellState?.Invoke();
  }

  public bool AddJobsAsCopiedToSystem => false;

  public IEnumerable<string> CheckNewJobs(IRepository db, NewJobs jobs)
  {
    var logMessages = new List<string>();
    MazakAllData mazakData = mazakDB.LoadAllData();

    try
    {
      ProgramRevision lookupProg(string prog, long? rev)
      {
        // while checking jobs, a newly downloaded program might not yet
        // be in the database.  Therefore, we need to check if the program
        // is going to be included as part of the download
        NewProgramContent? newlyAdded = jobs.Programs?.FirstOrDefault(p =>
        {
          if (rev.HasValue && rev.Value != 0)
          {
            return p.ProgramName == prog && p.Revision == rev.Value;
          }
          else
          {
            // 0 revision just means most recent
            return p.ProgramName == prog;
          }
        });

        if (newlyAdded != null)
        {
          return new ProgramRevision()
          {
            ProgramName = prog,
            Comment = newlyAdded.Comment,
            CellControllerProgramName = "", // not yet in the cell controller
            // This revision isn't correct since a 0 or negative revision will be
            // assigned as part of the transaction adding the jobs and programs
            // to the database.
            Revision = newlyAdded.Revision,
          };
        }

        if (rev.HasValue)
        {
          return db.LoadProgram(prog, rev.Value);
        }
        else
        {
          return db.LoadMostRecentProgram(prog);
        }
      }

      //The reason we create the clsPalletPartMapping is to see if it throws any exceptions.  We therefore
      //need to ignore the warning that palletPartMap is not used.
#pragma warning disable 168, 219
      var mazakJobs = ConvertJobsToMazakParts.JobsToMazak(
        jobs: jobs.Jobs,
        downloadUID: 1,
        mazakData: mazakData,
        savedParts: new HashSet<string>(),
        mazakCfg: mazakConfig,
        fmsSettings: settings,
        lookupProgram: lookupProg,
        errors: logMessages
      );
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

    return logMessages;
  }

  // CalculateCellState composes acquisition, reconciliation and acknowledgment; staged callers can
  // release the repository mutation lock during controller I/O so other work and status can progress.
  // Hold one controller lock across the staged sequence, taking it before the repository lock,
  // and hold the repository lock only for watermark capture and reconciliation.
  // Each controller stream needs its own watermark domain; the repository-wide LG cursor does not
  // distinguish controllers.
  // The host owns source-health publication and decides which actions require fresh observations.
  public MazakState CalculateCellState(IRepository db)
  {
    var acquisition = AcquireCellState(CaptureLogWatermark(db));
    var reconciled = ReconcileCellState(db, acquisition);
    AcknowledgeLogs(reconciled);
    return reconciled.State;
  }

  // Capture under the caller's repository/mutation owner, then release that owner before acquiring.
  public string CaptureLogWatermark(IRepository db) =>
    // Web/Smooth CSV names begin with LG; exclude other event producers.
    db.MaxForeignIDInRange(MazakCsvForeignIDLowerBound, MazakCsvForeignIDUpperBound);

  // Serialize controller acquisition and acknowledgment with the same controller owner. All
  // enumerables, including tools and nested process rows, finish reading before this returns.
  public MazakAcquisition AcquireCellState(string afterLogId)
  {
    ArgumentNullException.ThrowIfNull(afterLogId);
    var started = timeProvider.GetUtcNow().UtcDateTime;
    var data = mazakDB.LoadAllDataAndLogs(afterLogId);
    data = data with
    {
      Logs = data.Logs.ToImmutableList(),
      Schedules = data
        .Schedules?.Select(s => s with { Processes = s.Processes.ToImmutableList() })
        .ToImmutableList(),
      Parts = data
        .Parts?.Select(p => p with { Processes = p.Processes.ToImmutableList() })
        .ToImmutableList(),
      LoadActions = data
        .LoadActions?.Select(action =>
          action with
          {
            StationPallet = action.StationPallet is { } pallet
              ? pallet with
              {
                Material = pallet.Material?.ToImmutableList(),
              }
              : null,
          }
        )
        .ToImmutableList(),
      PalletStatuses = data.PalletStatuses?.ToImmutableList(),
      PalletSubStatuses = data.PalletSubStatuses?.ToImmutableList(),
      PalletPositions = data.PalletPositions?.ToImmutableList(),
      Alarms = data.Alarms?.ToImmutableList(),
      Pallets = data.Pallets?.ToImmutableList(),
      MainPrograms = data.MainPrograms?.ToImmutableList(),
      Fixtures = data.Fixtures?.ToImmutableList(),
    };
    var dataLoaded = timeProvider.GetUtcNow().UtcDateTime;
    ImmutableList<ToolPocketRow>? tools = null;
    var toolReadFailed = false;
    if (data.Logs.Any(e => LogTranslation.NeedsToolSnapshot(e, dataLoaded)))
      try
      {
        tools = mazakDB.LoadTools().ToImmutableList();
      }
      catch (Exception error)
      {
        Log.Warning(
          error,
          "Unable to observe Mazak tools; continuing manufacturing ingestion without tool measurements"
        );
        toolReadFailed = true;
      }
    return new MazakAcquisition
    {
      StartedAtUtc = started,
      DataLoadedAtUtc = dataLoaded,
      CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
      Data = data,
      Tools = tools,
      ToolReadFailed = toolReadFailed,
    };
  }

  // No built-in controller I/O. The caller keeps source observations ordered and serializes
  // resolver/repository effects. Already committed events are omitted, including after a
  // partial reconciliation failure, while the available observation still contributes status.
  public MazakReconciliation ReconcileCellState(IRepository db, MazakAcquisition acquisition)
  {
    var committed = CaptureLogWatermark(db);
    if (acquisition.ToolReadFailed || acquisition.Tools is not null)
      lastToolReadFailed = acquisition.ToolReadFailed;
    var mazakData = acquisition.Data with
    {
      Logs = acquisition
        .Data.Logs.Where(entry => StringComparer.Ordinal.Compare(entry.ForeignID, committed) > 0)
        .ToImmutableList(),
    };
    var machineGroupName = BuildCurrentStatus.FindMachineGroupName(db);

    var evtResults = LogTranslation.HandleEvents(
      db,
      mazakData,
      machGroupName: machineGroupName,
      settings,
      chunk =>
      {
        if (chunk.LulEndChunk != null)
        {
          foreach (var le in chunk.LulEndChunk)
          {
            MazakLogEvent?.Invoke(le, db);
          }
        }
        else if (chunk.NonLulEndEvt != null)
        {
          MazakLogEvent?.Invoke(chunk.NonLulEndEvt, db);
        }
      },
      mazakConfig: mazakConfig,
      tools: acquisition.Tools,
      observationTimeUtc: acquisition.DataLoadedAtUtc
    );

    var palletWithUnprocessedUnloads = evtResults.PalletWithMostRecentEventAsLoadUnloadEnd
      is { } rawPallet
      ? mazakConfig.TranslatePalletNumber(rawPallet)
      : (int?)null;
    var st = BuildCurrentStatus.Build(
      db,
      settings,
      mazakConfig,
      mazakData,
      machineGroupName: machineGroupName,
      palletWithUnprocessedUnloads,
      acquisition.StartedAtUtc
    );

    if (mazakConfig != null && mazakConfig.AdjustCurrentStatus != null)
    {
      st = mazakConfig.AdjustCurrentStatus(db, st);
    }
    var diagnostics = ImmutableList<CurrentStatusAlarm>.Empty;
    if (lastToolReadFailed)
      diagnostics = diagnostics.Add(
        new CurrentStatusAlarm
        {
          Code = "mazak-tools-unavailable",
          Message =
            "The last Mazak tool read failed; manufacturing status continues without tool measurements until a successful tool observation.",
          Impact = AlarmImpact.Informational,
        }
      );

    foreach (var diagnostic in diagnostics)
      st = st.AddAlarm(diagnostic);
    var result = new MazakReconciliation
    {
      CommittedLogWatermark = CaptureLogWatermark(db),
      State = new MazakState()
      {
        StateUpdated =
          mazakData.Logs.Count > 0
          || evtResults.PalletStatusChanged
          || !ingestionDiagnostics.SequenceEqual(diagnostics),
        TimeUntilNextRefresh =
          evtResults.StoppedBecauseRecentMachineEvent
          || evtResults.PalletWithMostRecentEventAsLoadUnloadEnd.HasValue
            ? TimeSpan.FromSeconds(15)
            : TimeSpan.FromMinutes(2),
        StoppedBecauseRecentLogEvent =
          evtResults.StoppedBecauseRecentMachineEvent
          || evtResults.PalletWithMostRecentEventAsLoadUnloadEnd.HasValue,
        CurrentStatus = st,
        AllData = mazakData,
        PalletWithUnprocessedUnloads = palletWithUnprocessedUnloads,
      },
    };
    ingestionDiagnostics = diagnostics;
    return result;
  }

  // Call outside the repository/mutation owner, using its captured progress even if isolated
  // source records were logged and skipped. Never substitute a later repository watermark.
  public void AcknowledgeLogs(MazakReconciliation reconciliation) =>
    mazakDB.DeleteLogs(reconciliation.CommittedLogWatermark);

  public bool ApplyActions(IRepository db, MazakState st)
  {
    if (st.StoppedBecauseRecentLogEvent)
    {
      return false;
    }

    // jobs
    bool jobsCopied = WriteJobs.SyncFromDatabase(
      st.AllData,
      db,
      mazakDB,
      settings,
      mazakConfig,
      st.CurrentStatus.TimeOfCurrentStatusUTC
    );

    // queues
    bool queuesChanged = false;
    if (!jobsCopied)
    {
      var transSet = MazakQueues.CalculateScheduleChanges(db, st.AllData, mazakConfig);

      if (transSet != null && transSet.Schedules.Count > 0)
      {
        mazakDB.Save(transSet);
        queuesChanged = true;
      }
    }

    // TODO: holds

    return jobsCopied || queuesChanged;
  }

  public bool DecrementJobs(IRepository db, MazakState st)
  {
    if (
      db.LoadUnarchivedJobs()
        .Any(j => !j.ManuallyCreated && mazakConfig.UsesInsightJobQuantities(j))
    )
      throw new BadRequestException(
        "This backend manages job quantities. Use its backout command instead of the Mazak decrement command."
      );

    if (st.StoppedBecauseRecentLogEvent)
      throw new ConflictRequestException(
        "Cannot back out jobs while Mazak events are still settling. Wait for synchronization to finish and retry."
      );

    return DecrementPlanQty.Decrement(mazakDB, db, st.AllData);
  }
}
