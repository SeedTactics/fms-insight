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
  public required bool StoppedBecauseRecentMachineEnd { get; init; }
  public required CurrentStatus CurrentStatus { get; init; }
  public required MazakAllData AllData { get; init; }
}

public delegate void MazakLogEventDel(LogEntry e, IRepository jobDB);

public interface INotifyMazakLogEvent
{
  event MazakLogEventDel? MazakLogEvent;
}

public sealed class MazakSync : ISynchronizeCellState<MazakState>, INotifyMazakLogEvent, IDisposable
{
  public static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MazakSync>();
  public event Action? NewCellState;
  public event MazakLogEventDel? MazakLogEvent;

  private readonly IReadDataAccess readDatabase;
  private readonly IWriteData writeDatabase;
  private readonly FMSSettings settings;
  private readonly MazakConfig mazakConfig;

  private readonly FileSystemWatcher logWatcher;

  public MazakSync(IReadDataAccess readDb, IWriteData writeDb, FMSSettings settings, MazakConfig mazakConfig)
  {
    this.readDatabase = readDb;
    this.writeDatabase = writeDb;
    this.settings = settings;
    this.mazakConfig = mazakConfig;

    logWatcher = new FileSystemWatcher(mazakConfig.LogCSVPath) { Filter = "*.csv" };
    logWatcher.Created += LogFileCreated;
    logWatcher.EnableRaisingEvents = true;
  }

  public void Dispose()
  {
    logWatcher.EnableRaisingEvents = false;
    logWatcher.Created -= LogFileCreated;
    logWatcher.Dispose();
  }

  private void LogFileCreated(object sender, FileSystemEventArgs e)
  {
    NewCellState?.Invoke();
  }

  public bool AllowQuarantineToCancelLoad => false;
  public bool AddJobsAsCopiedToSystem => false;

  public IEnumerable<string> CheckNewJobs(IRepository db, NewJobs jobs)
  {
    var logMessages = new List<string>();
    MazakAllData mazakData = readDatabase.LoadAllData();

    try
    {
      ProgramRevision lookupProg(string prog, long? rev)
      {
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
        MazakType: mazakConfig.DBType,
        useStartingOffsetForDueDate: mazakConfig.UseStartingOffsetForDueDate,
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

  public MazakState CalculateCellState(IRepository db)
  {
    var now = DateTime.UtcNow;
    var mazakData = readDatabase.LoadAllData();
    var machineGroupName = BuildCurrentStatus.FindMachineGroupName(db);

    var logs = LogCSVParsing.LoadLog(db.MaxForeignID(), mazakConfig.LogCSVPath);

    var trans = new LogTranslation(
      db,
      mazakData,
      machGroupName: machineGroupName,
      settings,
      le => MazakLogEvent?.Invoke(le, db),
      mazakConfig: mazakConfig,
      loadTools: readDatabase.LoadTools
    );
    var sendToExternal = new List<MaterialToSendToExternalQueue>();

    var stoppedBecauseRecentMachineEnd = false;
    foreach (var ev in logs)
    {
      try
      {
        var result = trans.HandleEvent(ev);
        sendToExternal.AddRange(result.MatsToSendToExternal);
        if (result.StoppedBecauseRecentMachineEnd)
        {
          stoppedBecauseRecentMachineEnd = true;
          break;
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error translating log event at time " + ev.TimeUTC.ToLocalTime().ToString());
      }
    }

    LogCSVParsing.DeleteLog(db.MaxForeignID(), mazakConfig.LogCSVPath);

    bool palStChanged = false;
    if (!stoppedBecauseRecentMachineEnd)
    {
      palStChanged = trans.CheckPalletStatusMatchesLogs();
    }

    if (sendToExternal.Count > 0)
    {
      SendMaterialToExternalQueue.Post(sendToExternal).Wait(TimeSpan.FromSeconds(30));
    }

    var st = BuildCurrentStatus.Build(
      db,
      settings,
      mazakConfig.DBType,
      mazakData,
      machineGroupName: machineGroupName,
      now
    );

    if (mazakConfig != null && mazakConfig.AdjustCurrentStatus != null)
    {
      st = mazakConfig.AdjustCurrentStatus(db, st);
    }

    return new MazakState()
    {
      StateUpdated = logs.Count > 0 || palStChanged,
      TimeUntilNextRefresh = stoppedBecauseRecentMachineEnd
        ? TimeSpan.FromSeconds(15)
        : TimeSpan.FromMinutes(2),
      StoppedBecauseRecentMachineEnd = stoppedBecauseRecentMachineEnd,
      CurrentStatus = st,
      AllData = mazakData
    };
  }

  public bool ApplyActions(IRepository db, MazakState st)
  {
    if (st.StoppedBecauseRecentMachineEnd)
    {
      return false;
    }

    // jobs
    bool jobsCopied = WriteJobs.SyncFromDatabase(
      st.AllData,
      db,
      writeDatabase,
      readDatabase,
      settings,
      mazakConfig,
      st.CurrentStatus.TimeOfCurrentStatusUTC
    );

    // queues
    bool queuesChanged = false;
    if (!jobsCopied)
    {
      var transSet = MazakQueues.CalculateScheduleChanges(
        db,
        st.AllData,
        waitForAllCastings: mazakConfig.WaitForAllCastings
      );

      if (transSet != null && transSet.Schedules.Count > 0)
      {
        writeDatabase.Save(transSet, "Setting material from queues");
        queuesChanged = true;
      }
    }

    // TODO: holds

    return jobsCopied || queuesChanged;
  }

  public bool DecrementJobs(IRepository db, MazakState st)
  {
    // TODO: reload AllData to make sure it is up to date?
    return DecrementPlanQty.Decrement(writeDatabase, db, st.AllData);
  }
}
