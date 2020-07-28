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
using System.Data;
using System.Threading;
using BlackMaple.MachineWatchInterface;
using Microsoft.Data.Sqlite;

namespace MazakMachineInterface
{
  public interface IHoldManagement
  {
    void SignalNewSchedules();
  }

  public class HoldPattern : IHoldManagement
  {
    #region Public API
    //Each schedule can be in one of four states: Shift1, Shift2, Preperation, or FullHold.
    //Then the mazak cell controller has two settings which can be changed on the main screen:
    // - Machining:  "Shift1", "Shift2", or "Shift1&2"
    // - LoadUnload: "Shift1", "Shift2", or "Shift1&2"
    //These settings determine which schedules to run.  Preperation means only load events happen.
    //
    //If we want to hold machining we will put the schedule in the Preperation state.  Once the hold
    //passes, we use a thread to switch the schedule to the Shift1 state so that it starts running.
    //Note that we do not currently support holding load unload.  If we wanted, we could use the Shift2
    //to hold LoadUnload.  For example, put Machining in "Shift1&2" and LUL in Shift1.  Then
    //schedules in the Shift2 state would not be loaded.

    public enum HoldMode
    {
      Shift1 = 0,
      FullHold = 1,
      Preperation = 2,
      Shift2 = 3
    }

    public static HoldMode CalculateHoldMode(bool entireHold, bool machineHold)
    {
      if (entireHold)
        return HoldMode.FullHold;
      else if (machineHold)
        return HoldMode.Preperation;
      else
        return HoldMode.Shift1;
    }

    public void SignalNewSchedules()
    {
      _thread.SignalNewSchedules();
    }

    private static Serilog.ILogger Log = Serilog.Log.ForContext<HoldPattern>();
    private TransitionThread _thread;
    private IWriteData database;
    private IReadDataAccess readDatabase;
    private BlackMaple.MachineFramework.JobDB.Config jobDBConfig;

    public HoldPattern(IWriteData d, IReadDataAccess readDb, BlackMaple.MachineFramework.JobDB.Config jdb, bool createThread)
    {
      database = d;
      readDatabase = readDb;
      jobDBConfig = jdb;

      if (createThread)
        _thread = new TransitionThread(this);
    }

    public void Shutdown()
    {
      if (_thread != null)
        _thread.SignalShutdown();
    }
    #endregion

    #region Checking for transition thread

    //Instead of using a timer to periodically check for hold transitions,
    //we spawn a thread which will just sleep and check for transitions.  Mainly we use a thread
    //so that we can wake the thread when a new download occurs.
    //
    //The thread code looks like:
    //  for (;;) {
    //     Check for any hold transitions.
    //     Sleep until there is some transition or a new download occurs.
    //  }
    //
    // We use two AutoResetEvents: one to signal new jobs have appeared and one to shut down.

    private class TransitionThread
    {
      public void SignalNewSchedules()
      {
        _newSchedules.Set();
      }

      public void SignalShutdown()
      {
        _shutdown.Set();

        if (!_thread.Join(TimeSpan.FromSeconds(15)))
          _thread.Abort();
      }

      public TransitionThread(HoldPattern parent)
      {
        _parent = parent;
        _shutdown = new AutoResetEvent(false);
        _newSchedules = new AutoResetEvent(false);

        _thread = new Thread(new ThreadStart(ThreadFunc));
        _thread.Start();
      }

#if DEBUG
      private const int MaxSleepMinutes = 2;
#else
			private const int MaxSleepMinutes = 15;
#endif

      public void ThreadFunc()
      {
        for (; ; )
        {

          var sleepTime = _parent.CheckForTransition();

          if (sleepTime == TimeSpan.MaxValue)
          {
            // -1 milliseconds means infinite wait.
            sleepTime = TimeSpan.FromMilliseconds(-1);

          }
          else
          {

            //sleep 5 seconds longer than the time to the next transition, just so that
            //we are sure to be after the transition when we wake up.
            sleepTime = sleepTime.Add(TimeSpan.FromSeconds(5));

            if (sleepTime < TimeSpan.Zero)
              sleepTime = TimeSpan.FromSeconds(10);

            // The max sleep time is 15 minutes, after which we recalculate.
            // We could have some time drift or the user could have changed the clock time
            // so we don't want to sleep too long.
            if (sleepTime > TimeSpan.FromMinutes(MaxSleepMinutes))
              sleepTime = TimeSpan.FromMinutes(MaxSleepMinutes);
          }

          Log.Debug("Sleeping for {time}", sleepTime);

          var ret = WaitHandle.WaitAny(new WaitHandle[] { _shutdown, _newSchedules }, sleepTime, false);

          if (ret == 0)
          {
            //Shutdown was fired.
            Log.Debug("Hold shutdown");
            return;
          }
        }
      }

      private HoldPattern _parent;
      private Thread _thread;
      private AutoResetEvent _shutdown;
      private AutoResetEvent _newSchedules;
    }
    #endregion

    #region Mazak Schedules

    private class MazakSchedule
    {
      public int SchId
      {
        get { return _schRow.Id; }
      }

      public string UniqueStr
      {
        get
        {
          MazakPart.ParseComment(_schRow.Comment, out string unique, out var paths, out var manual);
          return unique;
        }
      }

      public HoldMode Hold
      {
        get { return (HoldMode)_schRow.HoldMode; }
      }

      public JobHoldPattern HoldEntireJob { get; }
      public JobHoldPattern HoldMachining { get; }

      public void ChangeHoldMode(HoldMode newHold)
      {
        var transSet = new MazakWriteData();
        var newSchRow = _schRow.Clone();
        newSchRow.Command = MazakWriteCommand.ScheduleSafeEdit;
        newSchRow.HoldMode = (int)newHold;
        transSet.Schedules.Add(newSchRow);

        _parent.database.Save(transSet, "Hold Mode");
      }

      public MazakSchedule(BlackMaple.MachineFramework.JobDB jdb, HoldPattern parent, MazakScheduleRow s)
      {
        _parent = parent;
        _schRow = s;

        if (MazakPart.IsSailPart(_schRow.PartName))
        {
          MazakPart.ParseComment(_schRow.Comment, out string unique, out var paths, out var manual);
          var job = jdb.LoadJob(unique);
          if (job != null)
          {
            HoldEntireJob = job.HoldEntireJob;
            HoldMachining = job.HoldMachining(process: 1, path: paths.PathForProc(proc: 1));
          }
        }
      }

      private HoldPattern _parent;
      private MazakScheduleRow _schRow;
    }

    private IDictionary<int, MazakSchedule> LoadMazakSchedules(BlackMaple.MachineFramework.JobDB jdb)
    {
      var ret = new Dictionary<int, MazakSchedule>();
      var mazakData = readDatabase.LoadSchedules();

      foreach (var schRow in mazakData.Schedules)
      {
        ret.Add(schRow.Id, new MazakSchedule(jdb, this, schRow));
      }

      return ret;
    }

    private TimeSpan CheckForTransition()
    {
      try
      {
        if (!OpenDatabaseKitDB.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(3), true))
        {
          //Downloads usually take a long time and they hold the db lock,
          //so we will probably hit this timeout during the download.
          //For this reason, we wait 3 minutes for the db lock and retry again after only 20 seconds.
          //Thus during the download most of the waiting will be here for the db lock.

          Log.Debug("Unable to obtain mazak db lock, trying again in 20 seconds.");
          return TimeSpan.FromSeconds(20);
        }

        try
        {
          //Store the current time, this is the time we use to calculate the hold pattern.
          //It is important that this time be fixed for all schedule calculations, otherwise
          //we might miss a transition if it occurs while we are running this function.
          var nowUTC = DateTime.UtcNow;

          IDictionary<int, MazakSchedule> mazakSch;
          using (var jdb = jobDBConfig.OpenConnection())
          {
            mazakSch = LoadMazakSchedules(jdb);
          }

          Log.Debug("Checking for hold transitions at {time} ", nowUTC);

          var nextTimeUTC = DateTime.MaxValue;

          foreach (var pair in mazakSch)
          {
            bool allHold = false;
            DateTime allNext = DateTime.MaxValue;
            bool machHold = false;
            DateTime machNext = DateTime.MaxValue;

            if (pair.Value.HoldEntireJob != null)
              pair.Value.HoldEntireJob.HoldInformation(nowUTC, out allHold, out allNext);
            if (pair.Value.HoldMachining != null)
              pair.Value.HoldMachining.HoldInformation(nowUTC, out machHold, out machNext);

            HoldMode currentHoldMode = CalculateHoldMode(allHold, machHold);

            Log.Debug("Checking schedule {sch}, mode {mode}, target {targetMode}, next {allNext}, mach {machNext}",
              pair.Key, pair.Value.Hold, currentHoldMode, allNext, machNext
            );

            if (currentHoldMode != pair.Value.Hold)
            {
              pair.Value.ChangeHoldMode(currentHoldMode);
            }

            if (allNext < nextTimeUTC)
              nextTimeUTC = allNext;
            if (machNext < nextTimeUTC)
              nextTimeUTC = machNext;
          }

          Log.Debug("Next hold transition {next}", nextTimeUTC);

          if (nextTimeUTC == DateTime.MaxValue)
            return TimeSpan.MaxValue;
          else
            return nextTimeUTC.Subtract(DateTime.UtcNow);

        }
        finally
        {
          OpenDatabaseKitDB.MazakTransactionLock.ReleaseMutex();
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Unhanlded error checking for hold transition");

        //Try again in three minutes.
        return TimeSpan.FromMinutes(3);
      }
    }

    #endregion
  }
}