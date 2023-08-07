/* Copyright (c) 2023, John Lenz

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

// Currently Missing:
// Adjusting Jobs before they are added: do this in ICheckJobs?
// want more control over archiving and decrement of jobs
// everything else from RoutingInfo matches with JobsAndQueuesFromDb

public record MazakState : ICellState
{
  public required bool StateUpdated { get; init; }
  public required TimeSpan TimeUntilNextRefresh { get; init; }
  public required bool StoppedBecauseRecentMachineEnd { get; init; }
  public required CurrentStatus CurrentStatus { get; init; }
  public required MazakAllData AllData { get; init; }
}

public class MazakSync : ISynchronizeCellState<MazakState>, IDisposable
{
  public static Serilog.ILogger Log = Serilog.Log.ForContext<MazakSync>();
  public event Action? NewCellState;
  public event MazakLogEventDel? MazakLogEvent;

  private readonly IMachineGroupName machineGroupName;
  private readonly IReadDataAccess readDatabase;
  private readonly FMSSettings settings;
  private readonly MazakDbType dbType;
  private readonly string logPath;
  private readonly MazakConfig mazakConfig;
  private readonly MazakQueues queues;
  private readonly IWriteJobs writeJobs;

  private readonly FileSystemWatcher logWatcher;

  public MazakSync(
    IMachineGroupName machineGroupName,
    IReadDataAccess readDb,
    FMSSettings settings,
    MazakDbType dbType,
    string logPath,
    MazakConfig mazakConfig,
    MazakQueues queues,
    IWriteJobs writeJobs
  )
  {
    this.machineGroupName = machineGroupName;
    this.readDatabase = readDb;
    this.settings = settings;
    this.dbType = dbType;
    this.logPath = logPath;
    this.mazakConfig = mazakConfig;
    this.queues = queues;
    this.writeJobs = writeJobs;

    logWatcher = new FileSystemWatcher(logPath);
    logWatcher.Filter = "*.csv";
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

  public MazakState CalculateCellState(IRepository db)
  {
    var now = DateTime.UtcNow;
    var mazakData = readDatabase.LoadAllData();

    var logs = LogDataWeb.LoadLog(db.MaxForeignID(), logPath);

    var trans = new LogTranslation(
      db,
      mazakData,
      machineGroupName,
      settings,
      le => MazakLogEvent?.Invoke(le, db),
      mazakConfig: mazakConfig,
      loadTools: readDatabase.LoadTools
    );
    var sendToExternal = new List<BlackMaple.MachineFramework.MaterialToSendToExternalQueue>();

    var stoppedBecauseRecentMachineEnd = false;
    foreach (var ev in logs)
    {
      try
      {
        var result = trans.HandleEvent(ev);
        if (result.StoppedBecauseRecentMachineEnd)
        {
          stoppedBecauseRecentMachineEnd = true;
          break;
        }
        sendToExternal.AddRange(result.MatsToSendToExternal);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error translating log event at time " + ev.TimeUTC.ToLocalTime().ToString());
      }
    }

    LogDataWeb.DeleteLog(db.MaxForeignID(), logPath);

    bool palStChanged = false;
    if (!stoppedBecauseRecentMachineEnd)
    {
      palStChanged = trans.CheckPalletStatusMatchesLogs();
    }

    if (sendToExternal.Count > 0)
    {
      BlackMaple.MachineFramework.SendMaterialToExternalQueue
        .Post(sendToExternal)
        .Wait(TimeSpan.FromSeconds(30));
    }

    var st = BuildCurrentStatus.Build(db, settings, machineGroupName, dbType, mazakData, now);
    if (queues.CurrentQueueMismatch)
    {
      st = st with { Alarms = st.Alarms.Add("Queue contents and Mazak schedule quantity mismatch.") };
    }
    if (mazakConfig != null && mazakConfig.AdjustCurrentStatus != null)
    {
      st = mazakConfig.AdjustCurrentStatus(db, st);
    }

    return new MazakState()
    {
      StateUpdated = logs.Count > 0 || palStChanged,
      TimeUntilNextRefresh = stoppedBecauseRecentMachineEnd
        ? TimeSpan.FromSeconds(10)
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

    writeJobs.SyncFromDatabase(st.AllData, db);

    var queuesChanged = queues.CheckQueues(db, st.AllData);
    if (queuesChanged)
    {
      return true;
    }

    // TODO: holds


    return false;
  }

  public bool DecrementJobs(IRepository db, MazakState st)
  {
    throw new NotImplementedException();
  }
}
