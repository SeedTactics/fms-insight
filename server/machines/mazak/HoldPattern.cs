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
  public class HoldPattern
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

    private TransitionThread _thread;
    internal System.Diagnostics.TraceSource Trace;
    private TransactionDatabaseAccess database;
    private IReadDataAccess readDatabase;

    public HoldPattern(string dbPath, TransactionDatabaseAccess d, IReadDataAccess readDb, System.Diagnostics.TraceSource t, bool createThread)
    {
      Trace = t;
      database = d;
      readDatabase = readDb;
      OpenDB(System.IO.Path.Combine(dbPath, "hold.db"));

      if (createThread)
        _thread = new TransitionThread(this);
    }

    public HoldPattern(SqliteConnection conn, TransactionDatabaseAccess d, IReadDataAccess readDb, System.Diagnostics.TraceSource t, bool createThread)
    {
      Trace = t;
      database = d;
      readDatabase = readDb;
      _connection = conn;

      if (createThread)
        _thread = new TransitionThread(this);
    }

    public void Shutdown()
    {
      if (_thread != null)
        _thread.SignalShutdown();
      CloseDB();
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

          _parent.Trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                            "Sleeping for " + sleepTime.TotalMinutes.ToString() + " minutes");

          var ret = WaitHandle.WaitAny(new WaitHandle[] { _shutdown, _newSchedules }, sleepTime, false);

          if (ret == 0)
          {
            //Shutdown was fired.
            _parent.Trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                              "Shutdown");
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
        get { return _schRow.ScheduleID; }
      }

      public string UniqueStr
      {
        get
        {
          string unique;
          int path;
          bool manual;

          MazakPart.ParseComment(_schRow.Comment, out unique, out path, out manual);

          return unique;
        }
      }

      public HoldMode Hold
      {
        get { return (HoldMode)_schRow.HoldMode; }
      }

      public void ChangeHoldMode(HoldMode newHold)
      {
        TransactionDataSet transSet = new TransactionDataSet();

        TransactionDataSet.Schedule_tRow newSchRow = transSet.Schedule_t.NewSchedule_tRow();
        TransactionDatabaseAccess.BuildScheduleEditRow(newSchRow, _schRow, false);
        newSchRow.HoldMode = (int)newHold;
        transSet.Schedule_t.AddSchedule_tRow(newSchRow);

        var logMessages = new List<string>();

        _parent.database.SaveTransaction(transSet, logMessages, "Hold Mode", 10);

        foreach (var s in logMessages)
        {
          _parent.Trace.TraceEvent(System.Diagnostics.TraceEventType.Error, 0, s);
        }
      }

      public MazakSchedule(HoldPattern parent, ReadOnlyDataSet.ScheduleRow s)
      {
        _parent = parent;
        _schRow = s;
      }

      private HoldPattern _parent;
      private ReadOnlyDataSet.ScheduleRow _schRow;
    }

    private IDictionary<int, MazakSchedule> LoadMazakSchedules()
    {
      var ret = new Dictionary<int, MazakSchedule>();
      ReadOnlyDataSet currentSet = readDatabase.LoadReadOnly();

      foreach (ReadOnlyDataSet.ScheduleRow schRow in currentSet.Schedule.Rows)
      {
        ret.Add(schRow.ScheduleID, new MazakSchedule(this, schRow));
      }

      return ret;
    }

    private TimeSpan CheckForTransition()
    {
      try
      {
        if (!database.MazakTransactionLock.WaitOne(TimeSpan.FromMinutes(3), true))
        {
          //Downloads usually take a long time and they hold the db lock,
          //so we will probably hit this timeout during the download.
          //For this reason, we wait 3 minutes for the db lock and retry again after only 20 seconds.
          //Thus during the download most of the waiting will be here for the db lock.

          Trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                           "Unable to obtain mazak db lock, trying again in 20 seconds.");
          return TimeSpan.FromSeconds(20);
        }

        try
        {
          database.ClearTransactionDatabase();

          //Store the current time, this is the time we use to calculate the hold pattern.
          //It is important that this time be fixed for all schedule calculations, otherwise
          //we might miss a transition if it occurs while we are running this function.
          var nowUTC = DateTime.UtcNow;

          var mazakSch = LoadMazakSchedules();
          var holds = new Dictionary<int, JobHold>(GetHoldPatterns());

          Trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                           "Checking for hold transitions at UTC " + nowUTC.ToString());

          var nextTimeUTC = DateTime.MaxValue;

          foreach (var pair in mazakSch)
          {
            if (!holds.ContainsKey(pair.Key))
              continue;

            var hold = holds[pair.Key];

            //Sometimes the schedule id is reused, so check that the unique strings match
            if (hold.UniqueStr != pair.Value.UniqueStr)
              continue;

            bool allHold;
            DateTime allNext;
            bool machHold;
            DateTime machNext;

            hold.HoldEntireJob.HoldInformation(nowUTC, out allHold, out allNext);
            hold.HoldMachining.HoldInformation(nowUTC, out machHold, out machNext);

            HoldMode currentHoldMode = CalculateHoldMode(allHold, machHold);

            Trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                             "Checking sch " + pair.Key.ToString() + ":" +
                             " current mode = " + pair.Value.Hold.ToString() +
                             " target mode = " + currentHoldMode.ToString() +
                             " next all UTC = " + allNext.ToString() +
                             " next mach UTC = " + machNext.ToString());

            if (currentHoldMode != pair.Value.Hold)
            {
              pair.Value.ChangeHoldMode(currentHoldMode);
            }

            if (allNext < nextTimeUTC)
              nextTimeUTC = allNext;
            if (machNext < nextTimeUTC)
              nextTimeUTC = machNext;

            holds.Remove(pair.Key);
          }

          //Any leftover holds for which there is no schedule in mazak can be deleted
          foreach (var key in holds.Keys)
          {
            Trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                             "Deleting hold info for schedule " + key.ToString());
            DeleteHold(key);
          }

          Trace.TraceEvent(System.Diagnostics.TraceEventType.Information, 0,
                           "Next hold transition UTC: " + nextTimeUTC.ToString());

          if (nextTimeUTC == DateTime.MaxValue)
            return TimeSpan.MaxValue;
          else
            return nextTimeUTC.Subtract(DateTime.UtcNow);

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
      catch (Exception ex)
      {
        Trace.TraceEvent(System.Diagnostics.TraceEventType.Error, 0,
                         "Unhandled error checking for hold transition" + Environment.NewLine +
                         ex.ToString());

        //Try again in three minutes.
        return TimeSpan.FromMinutes(3);
      }
    }

    #endregion

    #region Database
    private SqliteConnection _connection;

    private object _dbLock = new object();
    private void OpenDB(string filename)
    {
      if (System.IO.File.Exists(filename))
      {
        _connection = BlackMaple.MachineFramework.SqliteExtensions.Connect(filename, false);
        _connection.Open();
        UpdateTables();
      }
      else
      {
        _connection = BlackMaple.MachineFramework.SqliteExtensions.Connect(filename, true);
        _connection.Open();
        try
        {
          CreateTables();
        }
        catch
        {
          _connection.Close();
          System.IO.File.Delete(filename);
          throw;
        }
      }
    }

    private void CloseDB()
    {
      _connection.Close();
    }

    #region Tables
    private const int Version = 1;

    public void CreateTables()
    {
      var cmd = _connection.CreateCommand();

      cmd.CommandText = "CREATE TABLE version(ver INTEGER)";
      cmd.ExecuteNonQuery();
      cmd.CommandText = "INSERT INTO version VALUES(" + Version.ToString() + ")";
      cmd.ExecuteNonQuery();

      cmd.CommandText = "CREATE TABLE holds(SchID INTEGER, EntireJob INTEGER, UniqueStr TEXT, HoldPatternStartUTC INTEGER, HoldPatternRepeats INTEGER, PRIMARY KEY(SchId, EntireJob))";
      cmd.ExecuteNonQuery();

      cmd.CommandText = "CREATE TABLE hold_pattern(SchID INTEGER, EntireJob INTEGER, Idx INTEGER, Span INTEGER, PRIMARY KEY(SchId, Idx, EntireJob))";
      cmd.ExecuteNonQuery();
    }


    private void UpdateTables()
    {
      var cmd = _connection.CreateCommand();

      cmd.CommandText = "SELECT ver FROM version";
      long ver = (long)cmd.ExecuteScalar();

      if (ver == Version)
        return;

      var trans = _connection.BeginTransaction();

      try
      {

        if (ver < 1)
          Ver0ToVer1(trans);

        cmd.Transaction = trans;
        cmd.CommandText = "UPDATE version SET ver = ?";
        cmd.Parameters.Add("", SqliteType.Integer).Value = Version;
        cmd.ExecuteNonQuery();

        trans.Commit();
      }
      catch
      {
        trans.Rollback();
        throw;
      }

      cmd.Transaction = null;
      cmd.CommandText = "VACUUM";
      cmd.Parameters.Clear();
      cmd.ExecuteNonQuery();
    }

    private void Ver0ToVer1(IDbTransaction trans)
    {
      IDbCommand cmd = _connection.CreateCommand();
      cmd.Transaction = trans;

      cmd.CommandText = "ALTER TABLE holds ADD UniqueStr TEXT";
      cmd.ExecuteNonQuery();
    }
    #endregion

    public void SaveHoldMode(int schID, JobPlan job, int path)
    {
      lock (_dbLock)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          //Schedule ids can be reused, so make sure the old schedule hold data is gone.
          DeleteHoldTrans(schID, trans);

          InsertHold(schID, true, job.UniqueStr, job.HoldEntireJob, trans);
          InsertHold(schID, false, job.UniqueStr, job.HoldMachining(1, path), trans);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private void InsertHold(int schId, bool entire, string unique, JobHoldPattern newHold, IDbTransaction trans)
    {
      var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;

      //Use insert or replace to allow saving more than once.
      cmd.CommandText = "INSERT INTO holds(SchId,EntireJob,UniqueStr,HoldPatternStartUTC,HoldPatternRepeats) VALUES (?,?,?,?,?)";
      cmd.Parameters.Add("", SqliteType.Integer).Value = schId;
      cmd.Parameters.Add("", SqliteType.Integer).Value = entire;
      cmd.Parameters.Add("", SqliteType.Text).Value = unique;
      cmd.Parameters.Add("", SqliteType.Integer).Value = newHold.HoldUnholdPatternStartUTC.Ticks;
      cmd.Parameters.Add("", SqliteType.Integer).Value = newHold.HoldUnholdPatternRepeats;
      cmd.ExecuteNonQuery();

      cmd.CommandText = "INSERT INTO hold_pattern(SchId,EntireJob,Idx,Span) VALUES (?,?,?,?)";
      cmd.Parameters.Clear();
      cmd.Parameters.Add("", SqliteType.Integer).Value = schId;
      cmd.Parameters.Add("", SqliteType.Integer).Value = entire;
      cmd.Parameters.Add("", SqliteType.Integer);
      cmd.Parameters.Add("", SqliteType.Integer);
      for (int i = 0; i < newHold.HoldUnholdPattern.Count; i++)
      {
        cmd.Parameters[2].Value = i;
        cmd.Parameters[3].Value = newHold.HoldUnholdPattern[i].Ticks;
        cmd.ExecuteNonQuery();
      }
    }

    private void DeleteHold(int schId)
    {
      lock (_dbLock)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          DeleteHoldTrans(schId, trans);

          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private void DeleteHoldTrans(int schId, IDbTransaction trans)
    {
      var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;

      cmd.CommandText = "DELETE FROM holds WHERE SchId = ?";
      cmd.Parameters.Add("", SqliteType.Integer).Value = schId;
      cmd.ExecuteNonQuery();

      cmd.CommandText = "DELETE FROM hold_pattern WHERE SchId = ?";
      cmd.ExecuteNonQuery();
    }

    public class JobHold
    {
      public int SchId;
      public string UniqueStr;
      public JobHoldPattern HoldEntireJob;
      public JobHoldPattern HoldMachining;

      public JobHold(int s, string u)
      {
        SchId = s;
        UniqueStr = u;
        HoldEntireJob = new JobHoldPattern();
        HoldMachining = new JobHoldPattern();
      }
    }

    public void LoadHoldIntoJob(int schID, JobPlan plan, int path)
    {
      lock (_dbLock)
      {
        var trans = _connection.BeginTransaction();

        try
        {
          var cmd = _connection.CreateCommand();
          cmd.Transaction = trans;

          cmd.CommandText = "SELECT EntireJob, HoldPatternStartUTC, HoldPatternRepeats,UniqueStr FROM holds WHERE SchID = ?";
          cmd.Parameters.Add("", SqliteType.Integer).Value = schID;

          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {

              //Sometimes schedule ids are reused before we can delete the data, so check
              //that the unique string matches before loading.
              if (reader.IsDBNull(3) || plan.UniqueStr != reader.GetString(3))
                goto skip_load;

              JobHoldPattern hold;
              if (reader.GetBoolean(0))
                hold = plan.HoldEntireJob;
              else
                hold = plan.HoldMachining(1, path);

              hold.HoldUnholdPatternStartUTC = reader.GetDateTime(1);
              hold.HoldUnholdPatternRepeats = reader.GetBoolean(2);

            }
          }

          //clear existing hold pattern
          plan.HoldEntireJob.HoldUnholdPattern.Clear();
          plan.HoldMachining(1, path).HoldUnholdPattern.Clear();

          //hold pattern
          cmd.CommandText = "SELECT EntireJob, Span FROM hold_pattern WHERE SchId = ? ORDER BY Idx ASC";

          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {

              JobHoldPattern hold;
              if (reader.GetBoolean(0))
                hold = plan.HoldEntireJob;
              else
                hold = plan.HoldMachining(1, path);

              hold.HoldUnholdPattern.Add(TimeSpan.FromTicks(reader.GetInt64(1)));
            }
          }

        skip_load:
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private IDictionary<int, JobHold> GetHoldPatterns()
    {
      lock (_dbLock)
      {
        var ret = new Dictionary<int, JobHold>();

        var cmd = _connection.CreateCommand();

        //hold
        cmd.CommandText = "SELECT SchId, EntireJob, HoldPatternStartUTC, HoldPatternRepeats, UniqueStr FROM holds";

        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            int schid = reader.GetInt32(0);
            string unique;

            if (reader.IsDBNull(4))
              unique = "";
            else
              unique = reader.GetString(4);

            JobHold h;
            if (ret.ContainsKey(schid))
              h = ret[schid];
            else
            {
              h = new JobHold(schid, unique);
              ret.Add(schid, h);
            }

            JobHoldPattern hold;
            if (reader.GetBoolean(1))
              hold = h.HoldEntireJob;
            else
              hold = h.HoldMachining;

            hold.HoldUnholdPatternStartUTC = reader.GetDateTime(2);
            hold.HoldUnholdPatternRepeats = reader.GetBoolean(3);
          }
        }

        //hold pattern
        cmd.CommandText = "SELECT EntireJob, Span FROM hold_pattern WHERE SchId = ? ORDER BY Idx ASC";
        cmd.Parameters.Add("", SqliteType.Integer);

        foreach (var pair in ret)
        {
          cmd.Parameters[0].Value = pair.Key;

          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {

              JobHoldPattern hold;
              if (reader.GetBoolean(0))
                hold = pair.Value.HoldEntireJob;
              else
                hold = pair.Value.HoldMachining;

              hold.HoldUnholdPattern.Add(TimeSpan.FromTicks(reader.GetInt64(1)));
            }
          }
        }

        return ret;
      }
    }
    #endregion
  }
}