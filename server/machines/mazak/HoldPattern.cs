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
    void SaveHoldMode(int schID, JobPlan job, int proc1path);
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

    public HoldPattern(string dbPath, IWriteData d, IReadDataAccess readDb, bool createThread)
    {
      database = d;
      readDatabase = readDb;
      OpenDB(System.IO.Path.Combine(dbPath, "hold.db"));

      if (createThread)
        _thread = new TransitionThread(this);
    }

    public HoldPattern(SqliteConnection conn, IWriteData d, IReadDataAccess readDb, bool createThread)
    {
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

      public void ChangeHoldMode(HoldMode newHold)
      {
        TransactionDataSet transSet = new TransactionDataSet();

        TransactionDataSet.Schedule_tRow newSchRow = transSet.Schedule_t.NewSchedule_tRow();
        OpenDatabaseKitTransactionDB.BuildScheduleEditRow(newSchRow, _schRow, false);
        newSchRow.HoldMode = (int)newHold;
        transSet.Schedule_t.AddSchedule_tRow(newSchRow);

        var logMessages = new List<string>();

        _parent.database.SaveTransaction(transSet, logMessages, "Hold Mode", 10);

        Log.Error("Error updating holds. {msgs}", logMessages);
      }

      public MazakSchedule(HoldPattern parent, MazakScheduleRow s)
      {
        _parent = parent;
        _schRow = s;
      }

      private HoldPattern _parent;
      private MazakScheduleRow _schRow;
    }

    private IDictionary<int, MazakSchedule> LoadMazakSchedules()
    {
      var ret = new Dictionary<int, MazakSchedule>();
      var mazakData = readDatabase.LoadSchedules();

      foreach (var schRow in mazakData.Schedules)
      {
        ret.Add(schRow.Id, new MazakSchedule(this, schRow));
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
          database.ClearTransactionDatabase();

          //Store the current time, this is the time we use to calculate the hold pattern.
          //It is important that this time be fixed for all schedule calculations, otherwise
          //we might miss a transition if it occurs while we are running this function.
          var nowUTC = DateTime.UtcNow;

          var mazakSch = LoadMazakSchedules();
          var holds = new Dictionary<int, JobHold>(GetHoldPatterns());

          Log.Debug("Checking for hold transitions at {time} ", nowUTC);

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

            holds.Remove(pair.Key);
          }

          //Any leftover holds for which there is no schedule in mazak can be deleted
          foreach (var key in holds.Keys)
          {
            Log.Debug("Deleting hold for schedule {key}", key);
            DeleteHold(key);
          }

          Log.Debug("Next hold transition {next}", nextTimeUTC);

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

    #region Database
    private SqliteConnection _connection;

    private object _dbLock = new object();
    private void OpenDB(string filename)
    {
      if (System.IO.File.Exists(filename))
      {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + filename);
        _connection.Open();
        UpdateTables();
      }
      else
      {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + filename);
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
      using (var cmd = _connection.CreateCommand()) {

      cmd.CommandText = "CREATE TABLE version(ver INTEGER)";
      cmd.ExecuteNonQuery();
      cmd.CommandText = "INSERT INTO version VALUES(" + Version.ToString() + ")";
      cmd.ExecuteNonQuery();

      cmd.CommandText = "CREATE TABLE holds(SchID INTEGER, EntireJob INTEGER, UniqueStr TEXT, HoldPatternStartUTC INTEGER, HoldPatternRepeats INTEGER, PRIMARY KEY(SchId, EntireJob))";
      cmd.ExecuteNonQuery();

      cmd.CommandText = "CREATE TABLE hold_pattern(SchID INTEGER, EntireJob INTEGER, Idx INTEGER, Span INTEGER, PRIMARY KEY(SchId, Idx, EntireJob))";
      cmd.ExecuteNonQuery();
      }
    }


    private void UpdateTables()
    {
      using (var cmd = _connection.CreateCommand()) {

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
        cmd.CommandText = "UPDATE version SET ver = $ver";
        cmd.Parameters.Add("ver", SqliteType.Integer).Value = Version;
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
    }

    private void Ver0ToVer1(IDbTransaction trans)
    {
      using (IDbCommand cmd = _connection.CreateCommand()) {
      cmd.Transaction = trans;

      cmd.CommandText = "ALTER TABLE holds ADD UniqueStr TEXT";
      cmd.ExecuteNonQuery();
      }
    }
    #endregion

    public void SaveHoldMode(int schID, JobPlan job, int proc1path)
    {
      lock (_dbLock)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          //Schedule ids can be reused, so make sure the old schedule hold data is gone.
          DeleteHoldTrans(schID, trans);

          if (job.HoldEntireJob != null)
            InsertHold(schID, true, job.UniqueStr, job.HoldEntireJob, trans);
          if (job.HoldMachining(1, proc1path) != null)
            InsertHold(schID, false, job.UniqueStr, job.HoldMachining(1, proc1path), trans);
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
      using (var cmd = _connection.CreateCommand()) {
      ((IDbCommand)cmd).Transaction = trans;

      //Use insert or replace to allow saving more than once.
      cmd.CommandText = "INSERT INTO holds(SchId,EntireJob,UniqueStr,HoldPatternStartUTC,HoldPatternRepeats)" +
                " VALUES ($schid,$job,$uniq,$hs,$hr)";
      cmd.Parameters.Add("schid", SqliteType.Integer).Value = schId;
      cmd.Parameters.Add("job", SqliteType.Integer).Value = entire;
      cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
      cmd.Parameters.Add("hs", SqliteType.Integer).Value = newHold.HoldUnholdPatternStartUTC.Ticks;
      cmd.Parameters.Add("hr", SqliteType.Integer).Value = newHold.HoldUnholdPatternRepeats;
      cmd.ExecuteNonQuery();

      cmd.CommandText = "INSERT INTO hold_pattern(SchId,EntireJob,Idx,Span) " +
                "VALUES ($schid,$job,$idx,$span)";
      cmd.Parameters.Clear();
      cmd.Parameters.Add("schid", SqliteType.Integer).Value = schId;
      cmd.Parameters.Add("job", SqliteType.Integer).Value = entire;
      cmd.Parameters.Add("idx", SqliteType.Integer);
      cmd.Parameters.Add("span", SqliteType.Integer);
      for (int i = 0; i < newHold.HoldUnholdPattern.Count; i++)
      {
        cmd.Parameters[2].Value = i;
        cmd.Parameters[3].Value = newHold.HoldUnholdPattern[i].Ticks;
        cmd.ExecuteNonQuery();
      }
      }
    }

    // internal for testing only!
    internal void DeleteHold(int schId)
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
      using (var cmd = _connection.CreateCommand()) {
      ((IDbCommand)cmd).Transaction = trans;

      cmd.CommandText = "DELETE FROM holds WHERE SchId = $schid";
      cmd.Parameters.Add("schid", SqliteType.Integer).Value = schId;
      cmd.ExecuteNonQuery();

      cmd.CommandText = "DELETE FROM hold_pattern WHERE SchId = $schid";
      cmd.ExecuteNonQuery();
      }
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

    public void LoadHoldIntoJob(int schID, JobPlan plan, int proc1path)
    {
      lock (_dbLock)
      {
        var trans = _connection.BeginTransaction();

        try
        {
          using (var cmd = _connection.CreateCommand()) {
          cmd.Transaction = trans;

                    cmd.CommandText = "SELECT EntireJob, HoldPatternStartUTC, HoldPatternRepeats,UniqueStr FROM holds " +
                                  "WHERE SchID = $schid";
          cmd.Parameters.Add("schid", SqliteType.Integer).Value = schID;

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
                hold = plan.HoldMachining(1, proc1path);

              hold.HoldUnholdPatternStartUTC = new DateTime(reader.GetInt64(1), DateTimeKind.Utc);
              hold.HoldUnholdPatternRepeats = reader.GetBoolean(2);

            }
          }

          //clear existing hold pattern
          plan.HoldEntireJob.HoldUnholdPattern.Clear();
          plan.HoldMachining(1, proc1path).HoldUnholdPattern.Clear();

          //hold pattern
          cmd.CommandText = "SELECT EntireJob, Span FROM hold_pattern " +
                        "WHERE SchId = $schid ORDER BY Idx ASC";

          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {

              JobHoldPattern hold;
              if (reader.GetBoolean(0))
                hold = plan.HoldEntireJob;
              else
                hold = plan.HoldMachining(1, proc1path);

              hold.HoldUnholdPattern.Add(TimeSpan.FromTicks(reader.GetInt64(1)));
            }
          }

        skip_load:
          trans.Commit();
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    // internal for testing only
    internal IDictionary<int, JobHold> GetHoldPatterns()
    {
      lock (_dbLock)
      {
        var ret = new Dictionary<int, JobHold>();

        using (var cmd = _connection.CreateCommand()) {

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

            hold.HoldUnholdPatternStartUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc);
            hold.HoldUnholdPatternRepeats = reader.GetBoolean(3);
          }
        }

        //hold pattern
        cmd.CommandText = "SELECT EntireJob, Span FROM hold_pattern " +
                    "WHERE SchId = $schid ORDER BY Idx ASC";
        cmd.Parameters.Add("schid", SqliteType.Integer);

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
    }
    #endregion
  }
}