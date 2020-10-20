/* Copyright (c) 2020, John Lenz

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
using System.Linq;
using System.Data;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework
{
  //database backend for the job db
  public class JobDB : BlackMaple.MachineWatchInterface.IJobDatabase, IDisposable
  {
    #region Database Open/Update
    public class OldDbColumns
    {
      public bool JobTableHasOldPriorityColumn { get; set; } = false;
      public bool JobTableHasOldCreateMarkerColumn { get; set; } = false;
    }

    public class Config
    {
      public OldDbColumns OldDbCols { get; set; }

      public static Config InitializeJobDatabase(string filename)
      {
        var connStr = "Data Source=" + filename;
        if (System.IO.File.Exists(filename))
        {
          using (var conn = new SqliteConnection(connStr))
          {
            conn.Open();
            var oldPriCol = UpdateTables(conn);
            return new Config(connStr, oldPriCol);
          }
        }
        else
        {
          using (var conn = new SqliteConnection(connStr))
          {
            conn.Open();
            try
            {
              CreateTables(conn);
              return new Config(connStr);
            }
            catch
            {
              conn.Close();
              System.IO.File.Delete(filename);
              throw;
            }
          }
        }
      }

      public static Config InitializeSingleThreadedMemoryDB()
      {
        var memConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        memConn.Open();
        CreateTables(memConn);
        return new Config(memConn);
      }

      public JobDB OpenConnection()
      {
        if (_memoryConnection != null)
        {
          return new JobDB(this, _memoryConnection, closeOnDispose: false);
        }
        else
        {
          var conn = new SqliteConnection(_connStr);
          conn.Open();
          return new JobDB(this, conn, closeOnDispose: true);
        }
      }

      private string _connStr { get; }
      private SqliteConnection _memoryConnection { get; }

      private Config(string connStr, OldDbColumns oldDbColumns = null)
      {
        _connStr = connStr;
        OldDbCols = oldDbColumns ?? new OldDbColumns();
      }

      private Config(SqliteConnection memConn)
      {
        _memoryConnection = memConn;
        OldDbCols = new OldDbColumns();
      }

    }

    private SqliteConnection _connection;
    private bool _closeConnectionOnDispose;
    private Config _cfg;

    private JobDB(Config cfg, SqliteConnection c, bool closeOnDispose)
    {
      _connection = c;
      _closeConnectionOnDispose = closeOnDispose;
      _cfg = cfg;
    }

    public void Close()
    {
      _connection.Close();
    }

    public void Dispose()
    {
      if (_closeConnectionOnDispose && _connection != null)
      {
        _connection.Close();
        _connection.Dispose();
        _connection = null;
      }
    }

    private const int Version = 20;

    // the Priority and CreateMarkingData field was removed from the job but old tables had it marked as NOT NULL.  So rather than try and copy
    // all the jobs, we just fill it in with 0 for old job databases.

    private static void CreateTables(SqliteConnection conn)
    {
      using (var cmd = conn.CreateCommand())
      {

        cmd.CommandText = "CREATE TABLE version(ver INTEGER)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO version VALUES(" + Version.ToString() + ")";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE jobs(UniqueStr TEXT PRIMARY KEY, Part TEXT NOT NULL, NumProcess INTEGER NOT NULL, Comment TEXT, StartUTC INTEGER NOT NULL, EndUTC INTEGER NOT NULL, Archived INTEGER NOT NULL, CopiedToSystem INTEGER NOT NULL, ScheduleId TEXT, Manual INTEGER)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX jobs_time_idx ON jobs(EndUTC, StartUTC)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX jobs_archived_idx ON jobs(Archived) WHERE Archived = 0";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX jobs_schedule_id ON jobs(ScheduleId) WHERE ScheduleId IS NOT NULL";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE numpaths(UniqueStr TEXT, Process INTEGER, NumPaths INTEGER NOT NULL, PRIMARY KEY(UniqueStr, Process))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE planqty(UniqueStr TEXT, Path INTEGER, PlanQty INTEGER NOT NULL, PRIMARY KEY(UniqueStr, Path))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE pathdata(UniqueStr TEXT, Process INTEGER, Path INTEGER, StartingUTC INTEGER, PartsPerPallet INTEGER, PathGroup INTEGER, SimAverageFlowTime INTEGER, InputQueue TEXT, OutputQueue TEXT, LoadTime INTEGER, UnloadTime INTEGER, Fixture TEXT, Face INTEGER, Casting TEXT, PRIMARY KEY(UniqueStr,Process,Path))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE pallets(UniqueStr TEXT, Process INTEGER, Path INTEGER, Pallet TEXT, PRIMARY KEY(UniqueStr,Process,Path,Pallet))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE stops(UniqueStr TEXT, Process INTEGER, Path INTEGER, RouteNum INTEGER, StatGroup STRING, ExpectedCycleTime INTEGER, Program TEXT, ProgramRevision INTEGER, PRIMARY KEY(UniqueStr, Process, Path, RouteNum))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE stops_stations(UniqueStr TEXT, Process INTEGER, Path INTEGER, RouteNum INTEGER, StatNum INTEGER, PRIMARY KEY(UniqueStr, Process, Path, RouteNum, StatNum))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE tools(UniqueStr TEXT, Process INTEGER, Path INTEGER, RouteNum INTEGER, Tool STRING, ExpectedUse INTEGER, PRIMARY KEY(UniqueStr,Process,Path,RouteNum,Tool))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE loadunload(UniqueStr TEXT, Process INTEGER, Path INTEGER, StatNum INTEGER, Load INTEGER, PRIMARY KEY(UniqueStr,Process,Path,StatNum,Load))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE path_inspections(UniqueStr TEXT, Process INTEGER, Path INTEGER, InspType STRING, Counter STRING, MaxVal INTEGER, TimeInterval INTEGER, RandomFreq NUMERIC, ExpectedTime INTEGER, PRIMARY KEY (UniqueStr, Process, Path, InspType))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE holds(UniqueStr TEXT, Process INTEGER, Path INTEGER, LoadUnload INTEGER, UserHold INTEGER, UserHoldReason TEXT, HoldPatternStartUTC INTEGER, HoldPatternRepeats INTEGER, PRIMARY KEY(UniqueStr, Process, Path, LoadUnload))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE hold_pattern(UniqueStr TEXT, Process INTEGER, Path INTEGER, LoadUnload INTEGER, Idx INTEGER, Span INTEGER, PRIMARY KEY(UniqueStr, Process, Path, LoadUnload, Idx))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE simulated_production(UniqueStr TEXT, Process INTEGER, Path INTEGER, TimeUTC INTEGER, Quantity INTEGER, PRIMARY KEY(UniqueStr,Process,Path,TimeUTC))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE sim_station_use(SimId TEXT, StationGroup TEXT, StationNum INTEGER, StartUTC INTEGER, EndUTC INTEGER, UtilizationTime INTEGER, PlanDownTime INTEGER, PRIMARY KEY(SimId, StationGroup, StationNum, StartUTC, EndUTC))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX sim_station_time_idx ON sim_station_use(EndUTC, StartUTC)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE scheduled_bookings(UniqueStr TEXT NOT NULL, BookingId TEXT NOT NULL, PRIMARY KEY(UniqueStr, BookingId))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE scheduled_parts(ScheduleId TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER, PRIMARY KEY(ScheduleId, Part))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE decrements(DecrementId INTEGER NOT NULL, JobUnique TEXT NOT NULL, TimeUTC TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER, PRIMARY KEY(DecrementId, JobUnique))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX decrements_time ON decrements(TimeUTC)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX decrements_unique ON decrements(JobUnique)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE schedule_debug(ScheduleId TEXT PRIMARY KEY, DebugMessage BLOB)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE unfilled_workorders(ScheduleId TEXT NOT NULL, Workorder TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER NOT NULL, DueDate INTEGER NOT NULL, Priority INTEGER NOT NULL, PRIMARY KEY(ScheduleId, Part, Workorder))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE program_revisions(ProgramName TEXT NOT NULL, ProgramRevision INTEGER NOT NULL, CellControllerProgramName TEXT, RevisionTimeUTC INTEGER NOT NULL, RevisionComment TEXT, ProgramContent TEXT, PRIMARY KEY(ProgramName, ProgramRevision))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX program_rev_cell_prog_name ON program_revisions(CellControllerProgramName, ProgramRevision) WHERE CellControllerProgramName IS NOT NULL";
        cmd.ExecuteNonQuery();
      }
    }

    private static OldDbColumns UpdateTables(SqliteConnection conn)
    {
      using (var cmd = conn.CreateCommand())
      {

        cmd.CommandText = "SELECT ver FROM version";

        int curVersion = 0;

        try
        {
          using (var reader = cmd.ExecuteReader())
          {
            if (reader.Read())
            {
              curVersion = (int)reader.GetInt32(0);
            }
            else
            {
              curVersion = 0;
            }
          }

        }
        catch (Exception ex)
        {
          if (ex.Message.IndexOf("no such table") >= 0)
          {
            curVersion = 0;
          }
          else
          {
            throw;
          }
        }

        var oldDbCols = new OldDbColumns();

        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('jobs') WHERE name='Priority'";
        var priColCnt = cmd.ExecuteScalar();
        if (priColCnt != null && priColCnt != DBNull.Value && (long)priColCnt > 0)
          oldDbCols.JobTableHasOldPriorityColumn = true;

        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('jobs') WHERE name='CreateMarker'";
        var markerColCnt = cmd.ExecuteScalar();
        if (markerColCnt != null && markerColCnt != DBNull.Value && (long)markerColCnt > 0)
          oldDbCols.JobTableHasOldCreateMarkerColumn = true;

        if (curVersion > Version)
        {
          throw new Exception("This input file was created with a newer version of machine watch.  Please upgrade machine watch");
        }

        if (curVersion == Version)
        {
          return oldDbCols;
        }


        var trans = conn.BeginTransaction();

        try
        {
          //add upgrade code here, in separate functions

          if (curVersion < 1) Ver0ToVer1(trans);
          if (curVersion < 2) Ver1ToVer2(trans);
          if (curVersion < 3) Ver2ToVer3(trans);
          if (curVersion < 4) Ver3ToVer4(trans);
          if (curVersion < 5) Ver4ToVer5(trans);
          if (curVersion < 6) Ver5ToVer6(trans);
          if (curVersion < 7) Ver6ToVer7(trans);
          if (curVersion < 8) Ver7ToVer8(trans);
          if (curVersion < 9) Ver8ToVer9(trans);
          if (curVersion < 10) Ver9ToVer10(trans);
          if (curVersion < 11) Ver10ToVer11(trans);
          if (curVersion < 12) Ver11ToVer12(trans);
          if (curVersion < 13) Ver12ToVer13(trans);
          if (curVersion < 14) Ver13ToVer14(trans);
          if (curVersion < 15) Ver14ToVer15(trans);
          if (curVersion < 16) Ver15ToVer16(trans);
          if (curVersion < 17) Ver16ToVer17(trans);
          if (curVersion < 18) Ver17ToVer18(trans);
          if (curVersion < 19) Ver18ToVer19(trans);
          if (curVersion < 20) Ver19ToVer20(trans);

          //update the version in the database
          cmd.Transaction = trans;
          cmd.CommandText = "UPDATE version SET ver = " + Version.ToString();
          cmd.ExecuteNonQuery();

          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }

        //only vacuum if we did some updating
        cmd.Transaction = null;
        cmd.CommandText = "VACUUM";
        cmd.ExecuteNonQuery();

        return oldDbCols;
      }
    }

    private static void Ver0ToVer1(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE TABLE fixtures(UniqueStr TEXT, Process INTEGER, Path INTEGER, Fixture TEXT, Face TEXT, PRIMARY KEY(UniqueStr,Process,Path,Fixture,Face))";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver1ToVer2(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE pathdata ADD PartsPerPallet INTEGER";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver2ToVer3(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE pathdata ADD PathGroup INTEGER";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver3ToVer4(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE inspections ADD RandomFreq NUMERIC";
        cmd.ExecuteNonQuery();
      }

    }

    private static void Ver4ToVer5(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE inspections ADD InspProc INTEGER";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver5ToVer6(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE jobs ADD StartUTC INTEGER";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE jobs ADD EndUTC INTEGER";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE jobs ADD Archived INTEGER NOT NULL DEFAULT 0";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE jobs ADD CopiedToSystem INTEGER";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX jobs_time_idx ON jobs(EndUTC, StartUTC)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX jobs_archived_idx ON jobs(Archived) WHERE Archived = 0";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE pathdata ADD SimAverageFlowTime INTEGER";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE TABLE simulated_production(UniqueStr TEXT, Process INTEGER, Path INTEGER, TimeUTC INTEGER, Quantity INTEGER, PRIMARY KEY(UniqueStr,Process,Path,TimeUTC))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE stops(UniqueStr TEXT, Process INTEGER, Path INTEGER, RouteNum INTEGER, StatGroup STRING, ExpectedCycleTime INTEGER, PRIMARY KEY(UniqueStr, Process, Path, RouteNum))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE programs(UniqueStr TEXT, Process INTEGER, Path INTEGER, RouteNum INTEGER, StatNum INTEGER, Program TEXT NOT NULL, PRIMARY KEY(UniqueStr, Process, Path, RouteNum, StatNum))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE tools(UniqueStr TEXT, Process INTEGER, Path INTEGER, RouteNum INTEGER, Tool STRING, ExpectedUse INTEGER, PRIMARY KEY(UniqueStr,Process,Path,RouteNum,Tool))";
        cmd.ExecuteNonQuery();

        //copy data from the old routes table to stops and programs table
        cmd.CommandText = "INSERT OR REPLACE INTO programs SELECT UniqueStr, Process, Path, RouteNum, StatNum, Program FROM routes";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT OR REPLACE INTO stops(UniqueStr, Process, Path, RouteNum, StatGroup) " +
            "SELECT UniqueStr, Process, Path, RouteNum, StatGroup FROM routes";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE routes";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE sim_station_use(SimId TEXT, StationGroup TEXT, StationNum INTEGER, StartUTC INTEGER, EndUTC INTEGER, UtilizationTime INTEGER, PRIMARY KEY(SimId, StationGroup, StationNum, StartUTC, EndUTC))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX sim_station_time_idx ON sim_station_use(EndUTC, StartUTC)";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver6ToVer7(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE TABLE scheduled_bookings(UniqueStr TEXT NOT NULL, BookingId TEXT NOT NULL, PRIMARY KEY(UniqueStr, BookingId))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE extra_parts(UniqueStr TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER, PRIMARY KEY(UniqueStr, Part))";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver7ToVer8(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE jobs ADD ScheduleId TEXT";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX jobs_schedule_id ON jobs(ScheduleId) WHERE ScheduleId IS NOT NULL";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE extra_parts";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE scheduled_parts(ScheduleId TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER, PRIMARY KEY(ScheduleId, Part))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE global_tag";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver8ToVer9(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "DROP TABLE decrement_counts";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE decrement_snapshots(DecrementId TEXT NOT NULL, JobUnique TEXT NOT NULL, TimeUTC TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER, PRIMARY KEY(DecrementId, JobUnique))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX decrement_snapshot_time ON decrement_snapshots(TimeUTC)";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver9ToVer10(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE sim_station_use ADD PlanDownTime INTEGER";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver10ToVer11(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE TABLE schedule_debug(ScheduleId TEXT PRIMARY KEY, DebugMessage BLOB)";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver11ToVer12(IDbTransaction transaction)
    {
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;
        cmd.CommandText = "CREATE TABLE unfilled_workorders(ScheduleId TEXT NOT NULL, Workorder TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER NOT NULL, DueDate INTEGER NOT NULL, Priority INTEGER NOT NULL, PRIMARY KEY(ScheduleId, Part, Workorder))";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver12ToVer13(IDbTransaction transaction)
    {
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;
        cmd.CommandText = "ALTER TABLE pathdata ADD InputQueue TEXT";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE pathdata ADD OutputQueue TEXT";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver13ToVer14(IDbTransaction transaction)
    {
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;
        cmd.CommandText = "ALTER TABLE pathdata ADD LoadTime INTEGER";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE pathdata ADD UnloadTime INTEGER";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver14ToVer15(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        // at the time of conversion, none of the plugins had yet used the decrement snapshot table.
        // main change is to switch DecrementId from TEXT to INTEGER and add an index
        cmd.CommandText = "DROP TABLE decrement_snapshots";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE decrements(DecrementId INTEGER NOT NULL, JobUnique TEXT NOT NULL, TimeUTC TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER, PRIMARY KEY(DecrementId, JobUnique))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX decrements_time ON decrements(TimeUTC)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX decrements_unique ON decrements(JobUnique)";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver15ToVer16(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "DROP TABLE material";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DROP TABLE first_proc_comp";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver16ToVer17(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.CommandText = "CREATE TABLE stops_stations(UniqueStr TEXT, Process INTEGER, Path INTEGER, RouteNum INTEGER, StatNum INTEGER, PRIMARY KEY(UniqueStr, Process, Path, RouteNum, StatNum))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO stops_stations(UniqueStr, Process, Path, RouteNum, StatNum) SELECT UniqueStr, Process, Path, RouteNum, StatNum FROM programs";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "ALTER TABLE stops ADD Program TEXT";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "ALTER TABLE stops ADD ProgramRevision INTEGER";
        cmd.ExecuteNonQuery();

        cmd.CommandText =
            "UPDATE stops SET Program = " +
            " (SELECT programs.Program FROM programs WHERE " +
            "  programs.UniqueStr = stops.UniqueStr AND " +
            "  programs.Process = stops.Process AND " +
            "  programs.Path = stops.Path AND " +
            "  programs.RouteNum = stops.RouteNum " +
            "  ORDER BY programs.StatNum ASC " +
            "  LIMIT 1 " +
            " )";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE programs";
        cmd.ExecuteNonQuery();

        // now fixture/face
        cmd.CommandText = "ALTER TABLE pathdata ADD Fixture TEXT";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "ALTER TABLE pathdata ADD Face INTEGER";
        cmd.ExecuteNonQuery();

        cmd.CommandText =
            "UPDATE pathdata SET " +
            "  Fixture = (SELECT fixtures.Fixture FROM fixtures WHERE " +
            "   fixtures.UniqueStr = pathdata.UniqueStr AND fixtures.Process = pathdata.Process AND fixtures.Path = pathdata.Path ORDER BY fixtures.Fixture ASC LIMIT 1)," +
            "  Face = (SELECT fixtures.Face FROM fixtures WHERE " +
            "   fixtures.UniqueStr = pathdata.UniqueStr AND fixtures.Process = pathdata.Process AND fixtures.Path = pathdata.Path ORDER BY fixtures.Fixture ASC LIMIT 1)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE fixtures";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE program_revisions(ProgramName TEXT NOT NULL, ProgramRevision INTEGER NOT NULL, CellControllerProgramName TEXT, RevisionTimeUTC INTEGER NOT NULL, RevisionComment TEXT, ProgramContent TEXT, PRIMARY KEY(ProgramName, ProgramRevision))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX program_rev_cell_prog_name ON program_revisions(CellControllerProgramName, ProgramRevision) WHERE CellControllerProgramName IS NOT NULL";
        cmd.ExecuteNonQuery();
      }

    }

    private static void Ver17ToVer18(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE TABLE path_inspections(UniqueStr TEXT, Process INTEGER, Path INTEGER, InspType STRING, Counter STRING, MaxVal INTEGER, TimeInterval INTEGER, RandomFreq NUMERIC, ExpectedTime INTEGER, PRIMARY KEY (UniqueStr, Process, Path, InspType))";
        cmd.ExecuteNonQuery();

        cmd.CommandText =
          "INSERT INTO path_inspections(UniqueStr, Process, Path, InspType, Counter, MaxVal, TimeInterval, RandomFreq) " +
          "  SELECT inspections.UniqueStr, " +
          "          COALESCE(CASE WHEN InspProc < 0 THEN NULL ELSE InspProc END, NumProcess), " +
          "         -1, InspType, Counter, MaxVal, TimeInterval, RandomFreq " +
          "   FROM inspections, jobs where inspections.UniqueStr = jobs.UniqueStr";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE inspections";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver18ToVer19(IDbTransaction transaction)
    {
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;
        cmd.CommandText = "ALTER TABLE pathdata ADD Casting TEXT";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver19ToVer20(IDbTransaction transaction)
    {
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;
        cmd.CommandText = "ALTER TABLE jobs ADD Manual INTEGER";
        cmd.ExecuteNonQuery();
      }
    }
    #endregion

    #region "Loading Jobs"
    private struct JobPath
    {
      public string Unique;
      public int Process;
      public int Path;
    }

    private void LoadJobData(MachineWatchInterface.JobPlan job, IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.Parameters.Add("$uniq", SqliteType.Text).Value = job.UniqueStr;

        //read plan quantity
        cmd.CommandText = "SELECT Path, PlanQty FROM planqty WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            job.SetPlannedCyclesOnFirstProcess(reader.GetInt32(0), reader.GetInt32(1));
          }
        }

        //read pallets
        cmd.CommandText = "SELECT Process, Path, Pallet FROM pallets WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            job.AddProcessOnPallet(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2));
          }
        }

        //simulated production
        cmd.CommandText = "SELECT Process, Path, TimeUTC, Quantity FROM simulated_production WHERE UniqueStr = $uniq ORDER BY Process,Path,TimeUTC";
        var simProd = new Dictionary<JobPath, List<MachineWatchInterface.JobPlan.SimulatedProduction>>();
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = new JobPath();
            key.Unique = job.UniqueStr;
            key.Process = reader.GetInt32(0);
            key.Path = reader.GetInt32(1);
            List<MachineWatchInterface.JobPlan.SimulatedProduction> prodL;
            if (simProd.ContainsKey(key))
            {
              prodL = simProd[key];
            }
            else
            {
              prodL = new List<MachineWatchInterface.JobPlan.SimulatedProduction>();
              simProd.Add(key, prodL);
            }
            var prod = default(MachineWatchInterface.JobPlan.SimulatedProduction);
            prod.TimeUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc);
            prod.Quantity = reader.GetInt32(3);
            prodL.Add(prod);
          }
        }
        foreach (var entry in simProd)
        {
          job.SetSimulatedProduction(entry.Key.Process, entry.Key.Path, entry.Value);
        }

        //scheduled bookings
        cmd.CommandText = "SELECT BookingId FROM scheduled_bookings WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            job.ScheduledBookingIds.Add(reader.GetString(0));
          }
        }

        //path data
        cmd.CommandText = "SELECT Process, Path, StartingUTC, PartsPerPallet, PathGroup, SimAverageFlowTime, InputQueue, OutputQueue, LoadTime, UnloadTime, Fixture, Face, Casting FROM pathdata WHERE UniqueStr = $uniq";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var proc = reader.GetInt32(0);
            var path = reader.GetInt32(1);
            job.SetSimulatedStartingTimeUTC(proc,
                                            path,
                                            new DateTime(reader.GetInt64(2), DateTimeKind.Utc));
            job.SetPartsPerPallet(proc, path, reader.GetInt32(3));
            job.SetPathGroup(proc, path, reader.GetInt32(4));
            if (!reader.IsDBNull(5))
            {
              job.SetSimulatedAverageFlowTime(proc, path, TimeSpan.FromTicks(reader.GetInt64(5)));
            }
            if (!reader.IsDBNull(6))
              job.SetInputQueue(proc, path, reader.GetString(6));
            if (!reader.IsDBNull(7))
              job.SetOutputQueue(proc, path, reader.GetString(7));
            if (!reader.IsDBNull(8))
              job.SetExpectedLoadTime(proc, path, TimeSpan.FromTicks(reader.GetInt64(8)));
            if (!reader.IsDBNull(9))
              job.SetExpectedUnloadTime(proc, path, TimeSpan.FromTicks(reader.GetInt64(9)));

            if (!reader.IsDBNull(10) && !reader.IsDBNull(11))
            {
              var faceTy = reader.GetFieldType(11);
              if (faceTy == typeof(string))
              {
                if (int.TryParse(reader.GetString(11), out int face))
                {
                  job.SetFixtureFace(proc, path, reader.GetString(10), face);
                }
              }
              else
              {
                job.SetFixtureFace(proc, path, reader.GetString(10), reader.GetInt32(11));
              }
            }

            if (!reader.IsDBNull(12) && proc == 1)
            {
              job.SetCasting(path, reader.GetString(12));
            }
          }
        }

        var routes = new Dictionary<JobPath, SortedList<int, MachineWatchInterface.JobMachiningStop>>();

        //now add routes
        cmd.CommandText = "SELECT Process, Path, RouteNum, StatGroup, ExpectedCycleTime, Program, ProgramRevision FROM stops WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            JobPath key = new JobPath();
            key.Unique = job.UniqueStr;
            key.Process = reader.GetInt32(0);
            key.Path = reader.GetInt32(1);
            int routeNum = reader.GetInt32(2);

            SortedList<int, MachineWatchInterface.JobMachiningStop> rList = null;
            if (routes.ContainsKey(key))
            {
              rList = routes[key];
            }
            else
            {
              rList = new SortedList<int, MachineWatchInterface.JobMachiningStop>();
              routes.Add(key, rList);
            }

            var stop = new MachineWatchInterface.JobMachiningStop(reader.GetString(3));
            if (!reader.IsDBNull(4))
              stop.ExpectedCycleTime = TimeSpan.FromTicks(reader.GetInt64(4));

            if (!reader.IsDBNull(5))
              stop.ProgramName = reader.GetString(5);
            if (!reader.IsDBNull(6))
              stop.ProgramRevision = reader.GetInt64(6);

            rList[routeNum] = stop;
          }
        }

        foreach (var key in routes.Keys)
        {
          foreach (var r in routes[key].Values)
          {
            job.AddMachiningStop(key.Process, key.Path, r);
          }
        }

        //programs for routes
        cmd.CommandText = "SELECT Process, Path, RouteNum, StatNum FROM stops_stations WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            JobPath key = new JobPath();
            key.Unique = job.UniqueStr;
            key.Process = reader.GetInt32(0);
            key.Path = reader.GetInt32(1);
            int routeNum = reader.GetInt32(2);
            if (routes.ContainsKey(key))
            {
              var stops = routes[key];
              if (stops.ContainsKey(routeNum))
              {
                stops[routeNum].Stations.Add(reader.GetInt32(3));
              }
            }
          }
        }

        //tools for routes
        cmd.CommandText = "SELECT Process, Path, RouteNum, Tool, ExpectedUse FROM tools WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            JobPath key = new JobPath();
            key.Unique = job.UniqueStr;
            key.Process = reader.GetInt32(0);
            key.Path = reader.GetInt32(1);
            int routeNum = reader.GetInt32(2);
            if (routes.ContainsKey(key))
            {
              var stops = routes[key];
              if (stops.ContainsKey(routeNum))
              {
                stops[routeNum].Tools[reader.GetString(3)] = TimeSpan.FromTicks(reader.GetInt64(4));
              }
            }
          }
        }

        //now add load/unload
        cmd.CommandText = "SELECT Process, Path, StatNum, Load FROM loadunload WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            if (reader.GetBoolean(3))
            {
              job.AddLoadStation(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
            }
            else
            {
              job.AddUnloadStation(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
            }
          }
        }

        //now inspections
        cmd.CommandText = "SELECT Process, Path, InspType, Counter, MaxVal, TimeInterval, RandomFreq, ExpectedTime FROM path_inspections WHERE UniqueStr = $uniq";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var insp = new MachineWatchInterface.PathInspection()
            {
              InspectionType = reader.GetString(2),
              Counter = reader.GetString(3),
              MaxVal = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
              TimeInterval = reader.IsDBNull(5) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(5)),
              RandomFreq = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
              ExpectedInspectionTime = reader.IsDBNull(7) ? null : (TimeSpan?)TimeSpan.FromTicks(reader.GetInt64(7))
            };

            var proc = reader.GetInt32(0);
            var path = reader.GetInt32(1);

            if (path < 1)
            {
              // all paths
              for (path = 1; path <= job.GetNumPaths(proc); path++)
              {
                job.PathInspections(proc, path).Add(insp);
              }
            }
            else
            {
              // single path
              job.PathInspections(proc, path).Add(insp);
            }
          }
        }

        //hold
        cmd.CommandText = "SELECT Process, Path, LoadUnload, UserHold, UserHoldReason, HoldPatternStartUTC, HoldPatternRepeats FROM holds WHERE UniqueStr = $uniq";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            int proc = reader.GetInt32(0);
            int path = reader.GetInt32(1);
            bool load = reader.GetBoolean(2);

            MachineWatchInterface.JobHoldPattern hold;
            if (proc < 0)
              hold = job.HoldEntireJob;
            else if (load)
              hold = job.HoldLoadUnload(proc, path);
            else
              hold = job.HoldMachining(proc, path);

            hold.UserHold = reader.GetBoolean(3);
            hold.ReasonForUserHold = reader.GetString(4);
            hold.HoldUnholdPatternStartUTC = new DateTime(reader.GetInt64(5), DateTimeKind.Utc);
            hold.HoldUnholdPatternRepeats = reader.GetBoolean(6);
          }
        }

        //hold pattern
        cmd.CommandText = "SELECT Process, Path, LoadUnload, Span FROM hold_pattern WHERE UniqueStr = $uniq ORDER BY Idx ASC";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            int proc = reader.GetInt32(0);
            int path = reader.GetInt32(1);
            bool load = reader.GetBoolean(2);

            MachineWatchInterface.JobHoldPattern hold;
            if (proc < 0)
              hold = job.HoldEntireJob;
            else if (load)
              hold = job.HoldLoadUnload(proc, path);
            else
              hold = job.HoldMachining(proc, path);

            hold.HoldUnholdPattern.Add(TimeSpan.FromTicks(reader.GetInt64(3)));
          }
        }
      }
    }

    private List<MachineWatchInterface.JobPlan> LoadJobsHelper(IDbCommand cmd, IDbTransaction trans)
    {
      var ret = new List<MachineWatchInterface.JobPlan>();
      using (var cmd2 = _connection.CreateCommand())
      {
        cmd2.CommandText = "SELECT Process, NumPaths FROM numpaths WHERE UniqueStr = $uniq";
        cmd2.Parameters.Add("uniq", SqliteType.Text);
        ((IDbCommand)cmd2).Transaction = trans;

        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {

            string unique = reader.GetString(0);

            //load the list of number of paths
            int[] numPaths = new int[reader.GetInt32(2)];
            for (int i = 0; i < numPaths.Length; i++)
              numPaths[i] = 1;
            cmd2.Parameters[0].Value = unique;
            using (IDataReader reader2 = cmd2.ExecuteReader())
            {
              while (reader2.Read())
              {
                int proc = reader2.GetInt32(0);
                if (proc >= 1 && proc <= numPaths.Length)
                  numPaths[proc - 1] = reader2.GetInt32(1);
              }
            }

            var job = new MachineWatchInterface.JobPlan(unique, reader.GetInt32(2), numPaths);
            job.PartName = reader.GetString(1);

            if (!reader.IsDBNull(3))
              job.Comment = reader.GetString(3);

            if (!reader.IsDBNull(4))
              job.RouteStartingTimeUTC = new DateTime(reader.GetInt64(4), DateTimeKind.Utc);
            if (!reader.IsDBNull(5))
              job.RouteEndingTimeUTC = new DateTime(reader.GetInt64(5), DateTimeKind.Utc);
            job.Archived = reader.GetBoolean(6);
            if (!reader.IsDBNull(7))
              job.JobCopiedToSystem = reader.GetBoolean(7);
            if (!reader.IsDBNull(8))
              job.ScheduleId = reader.GetString(8);
            job.ManuallyCreatedJob = !reader.IsDBNull(9) && reader.GetBoolean(9);

            ret.Add(job);

          }
        }
      }

      foreach (var job in ret)
        LoadJobData(job, trans);

      return ret;
    }

    private Dictionary<string, int> LoadExtraParts(IDbTransaction trans, string schId)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        var ret = new Dictionary<string, int>();
        cmd.CommandText = "SELECT Part, Quantity FROM scheduled_parts WHERE ScheduleId == $sid";
        cmd.Parameters.Add("sid", SqliteType.Text).Value = schId;
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            ret.Add(reader.GetString(0), reader.GetInt32(1));
          }
        }
        return ret;
      }
    }

    private List<MachineWatchInterface.PartWorkorder> LoadUnfilledWorkorders(IDbTransaction trans, string schId)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        var ret = new List<MachineWatchInterface.PartWorkorder>();
        cmd.CommandText = "SELECT Workorder, Part, Quantity, DueDate, Priority FROM unfilled_workorders WHERE ScheduleId == $sid";
        cmd.Parameters.Add("sid", SqliteType.Integer).Value = schId;
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            ret.Add(new MachineWatchInterface.PartWorkorder()
            {
              WorkorderId = reader.GetString(0),
              Part = reader.GetString(1),
              Quantity = reader.GetInt32(2),
              DueDate = new DateTime(reader.GetInt64(3)),
              Priority = reader.GetInt32(4)
            });
          }
        }
        return ret;
      }
    }

    private string LatestScheduleId(IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "SELECT MAX(ScheduleId) FROM jobs WHERE ScheduleId IS NOT NULL AND (Manual IS NULL OR Manual == 0)";

        string tag = "";

        object val = cmd.ExecuteScalar();
        if ((val != null))
        {
          tag = val.ToString();
        }

        return tag;
      }
    }

    private IList<MachineWatchInterface.SimulatedStationUtilization> LoadSimulatedStationUse(
        IDbCommand cmd, IDbTransaction trans)
    {
      var ret = new List<MachineWatchInterface.SimulatedStationUtilization>();

      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          var sim = new MachineWatchInterface.SimulatedStationUtilization(
                  reader.GetString(0),
                  reader.GetString(1),
                  reader.GetInt32(2),
                  new DateTime(reader.GetInt64(3), DateTimeKind.Utc),
                  new DateTime(reader.GetInt64(4), DateTimeKind.Utc),
                  TimeSpan.FromTicks(reader.GetInt64(5)),
                  reader.IsDBNull(6) ? TimeSpan.Zero :
                  TimeSpan.FromTicks(reader.GetInt64(6)));
          ret.Add(sim);
        }
      }

      return ret;
    }

    private MachineWatchInterface.HistoricData LoadHistory(IDbCommand jobCmd, IDbCommand simCmd)
    {
      lock (_cfg)
      {
        var ret = default(BlackMaple.MachineWatchInterface.HistoricData);
        ret.Jobs = new Dictionary<string, MachineWatchInterface.HistoricJob>();
        ret.StationUse = new List<MachineWatchInterface.SimulatedStationUtilization>();

        var trans = _connection.BeginTransaction();
        try
        {
          jobCmd.Transaction = trans;
          simCmd.Transaction = trans;

          ret.Jobs = LoadJobsHelper(jobCmd, trans).ToDictionary(j => j.UniqueStr, j => new MachineWatchInterface.HistoricJob(j));
          foreach (var j in ret.Jobs)
          {
            j.Value.Decrements = LoadDecrementsForJob(trans, j.Key);
          }
          ret.StationUse = LoadSimulatedStationUse(simCmd, trans);

          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }

        return ret;
      }
    }

    // --------------------------------------------------------------------------------
    // Public Loading API
    // --------------------------------------------------------------------------------

    public List<MachineWatchInterface.JobPlan> LoadUnarchivedJobs()
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual FROM jobs WHERE Archived = 0";
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          return LoadJobsHelper(cmd, trans);
        }
      }
    }

    public List<MachineWatchInterface.JobPlan> LoadJobsNotCopiedToSystem(DateTime startUTC, DateTime endUTC, bool includeDecremented = true)
    {
      var cmdTxt = "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual " +
                  " FROM jobs WHERE StartUTC <= $end AND EndUTC >= $start AND CopiedToSystem = 0";
      if (!includeDecremented)
      {
        cmdTxt += " AND NOT EXISTS(SELECT 1 FROM decrements WHERE decrements.JobUnique = jobs.UniqueStr)";
      }
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = cmdTxt;
        cmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
        cmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          return LoadJobsHelper(cmd, trans);
        }
      }
    }

    public BlackMaple.MachineWatchInterface.HistoricData LoadJobHistory(DateTime startUTC, DateTime endUTC)
    {
      using (var jobCmd = _connection.CreateCommand())
      using (var simCmd = _connection.CreateCommand())
      {
        jobCmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual " +
            " FROM jobs WHERE StartUTC <= $end AND EndUTC >= $start";
        jobCmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
        jobCmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;

        simCmd.CommandText = "SELECT SimId, StationGroup, StationNum, StartUTC, EndUTC, UtilizationTime, PlanDownTime FROM sim_station_use " +
            " WHERE EndUTC >= $start AND StartUTC <= $end";
        simCmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
        simCmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;

        return LoadHistory(jobCmd, simCmd);
      }
    }

    public BlackMaple.MachineWatchInterface.HistoricData LoadJobsAfterScheduleId(string schId)
    {
      using (var jobCmd = _connection.CreateCommand())
      using (var simCmd = _connection.CreateCommand())
      {
        jobCmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual " +
            " FROM jobs WHERE ScheduleId > $sid";
        jobCmd.Parameters.Add("sid", SqliteType.Text).Value = schId;

        simCmd.CommandText = "SELECT SimId, StationGroup, StationNum, StartUTC, EndUTC, UtilizationTime, PlanDownTime FROM sim_station_use " +
            " WHERE SimId > $sid";
        simCmd.Parameters.Add("sid", SqliteType.Text).Value = schId;

        return LoadHistory(jobCmd, simCmd);
      }
    }

    public List<MachineWatchInterface.PartWorkorder> MostRecentUnfilledWorkordersForPart(string part)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {

          var ret = new List<MachineWatchInterface.PartWorkorder>();
          cmd.CommandText = "SELECT Workorder, Part, Quantity, DueDate, Priority FROM unfilled_workorders " +
            "WHERE ScheduleId IN (SELECT MAX(ScheduleId) FROM jobs WHERE ScheduleId IS NOT NULL) AND Part = $part";
          cmd.Parameters.Add("part", SqliteType.Text).Value = part;

          using (IDataReader reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              ret.Add(new MachineWatchInterface.PartWorkorder()
              {
                WorkorderId = reader.GetString(0),
                Part = reader.GetString(1),
                Quantity = reader.GetInt32(2),
                DueDate = new DateTime(reader.GetInt64(3)),
                Priority = reader.GetInt32(4)
              });
            }
          }
          return ret;
        }
      }
    }

    public List<MachineWatchInterface.PartWorkorder> UnfilledWorkordersForJob(string unique)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {

          var ret = new List<MachineWatchInterface.PartWorkorder>();
          cmd.CommandText = "SELECT a.Workorder, a.Part, a.Quantity, a.DueDate, a.Priority " +
                             " FROM unfilled_workorders a INNER JOIN jobs b ON a.ScheduleId = b.ScheduleId AND a.Part = b.Part" +
                             " WHERE b.UniqueStr = $uniq";
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;

          using (IDataReader reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              ret.Add(new MachineWatchInterface.PartWorkorder()
              {
                WorkorderId = reader.GetString(0),
                Part = reader.GetString(1),
                Quantity = reader.GetInt32(2),
                DueDate = new DateTime(reader.GetInt64(3)),
                Priority = reader.GetInt32(4)
              });
            }
          }
          return ret;
        }
      }

    }

    public BlackMaple.MachineWatchInterface.PlannedSchedule LoadMostRecentSchedule()
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual " +
                  " FROM jobs WHERE ScheduleId = $sid";

        lock (_cfg)
        {
          var ret = default(BlackMaple.MachineWatchInterface.PlannedSchedule);
          ret.Jobs = new List<BlackMaple.MachineWatchInterface.JobPlan>();
          ret.ExtraParts = new Dictionary<string, int>();

          var trans = _connection.BeginTransaction();
          try
          {
            ret.LatestScheduleId = LatestScheduleId(trans);
            cmd.Parameters.Add("sid", SqliteType.Text).Value = ret.LatestScheduleId;
            cmd.Transaction = trans;
            ret.Jobs = LoadJobsHelper(cmd, trans);
            ret.ExtraParts = LoadExtraParts(trans, ret.LatestScheduleId);
            ret.CurrentUnfilledWorkorders = LoadUnfilledWorkorders(trans, ret.LatestScheduleId);

            trans.Commit();
          }
          catch
          {
            trans.Rollback();
            throw;
          }

          return ret;
        }
      }
    }

    public MachineWatchInterface.JobPlan LoadJob(string UniqueStr)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        using (var cmd2 = _connection.CreateCommand())
        {

          MachineWatchInterface.JobPlan job = null;

          cmd.CommandText = "SELECT Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual FROM jobs WHERE UniqueStr = $uniq";
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = UniqueStr;
          cmd2.CommandText = "SELECT Process, NumPaths FROM numpaths WHERE UniqueStr = $uniq";
          cmd2.Parameters.Add("uniq", SqliteType.Text).Value = UniqueStr;


          var trans = _connection.BeginTransaction();
          try
          {
            cmd.Transaction = trans;
            cmd2.Transaction = trans;

            using (IDataReader reader = cmd.ExecuteReader())
            {
              if (reader.Read())
              {

                //load the list of number of paths
                int[] numPaths = new int[reader.GetInt32(1)];
                for (int i = 0; i < numPaths.Length; i++)
                  numPaths[i] = 1;
                using (IDataReader reader2 = cmd2.ExecuteReader())
                {
                  while (reader2.Read())
                  {
                    int proc = reader2.GetInt32(0);
                    if (proc >= 1 && proc <= numPaths.Length)
                      numPaths[proc - 1] = reader2.GetInt32(1);
                  }
                }

                job = new MachineWatchInterface.JobPlan(UniqueStr, reader.GetInt32(1), numPaths);
                job.PartName = reader.GetString(0);

                if (!reader.IsDBNull(2))
                  job.Comment = reader.GetString(2);

                if (!reader.IsDBNull(3))
                  job.RouteStartingTimeUTC = new DateTime(reader.GetInt64(3), DateTimeKind.Utc);
                if (!reader.IsDBNull(4))
                  job.RouteEndingTimeUTC = new DateTime(reader.GetInt64(4), DateTimeKind.Utc);
                job.Archived = reader.GetBoolean(5);
                if (!reader.IsDBNull(6))
                  job.JobCopiedToSystem = reader.GetBoolean(6);
                if (!reader.IsDBNull(7))
                  job.ScheduleId = reader.GetString(7);
                job.ManuallyCreatedJob = !reader.IsDBNull(8) && reader.GetBoolean(8);

              }
            }

            if (job != null)
            {
              LoadJobData(job, trans);
            }

            trans.Commit();
          }
          catch
          {
            trans.Rollback();
            throw;
          }

          return job;
        }
      }
    }

    public bool DoesJobExist(string unique)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT COUNT(*) FROM jobs WHERE UniqueStr = $uniq";
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;

          object cnt = cmd.ExecuteScalar();
          if (cnt != null & Convert.ToInt32(cnt) > 0)
            return true;
          else
            return false;
        }
      }
    }

    #endregion

    #region "Adding and deleting"

    public void AddJobs(BlackMaple.MachineWatchInterface.NewJobs newJobs, string expectedPreviousScheduleId)
    {
      foreach (var j in newJobs.Jobs)
      {
        if (string.IsNullOrEmpty(j.ScheduleId))
          j.ScheduleId = newJobs.ScheduleId;
      }
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          if (!string.IsNullOrEmpty(expectedPreviousScheduleId))
          {
            var last = LatestScheduleId(trans);
            if (last != expectedPreviousScheduleId)
            {
              throw new BadRequestException(string.Format("Mismatch in previous schedule: expected '{0}' but got '{1}'", expectedPreviousScheduleId, last));
            }
          }

          // add programs first so that the lookup of latest program revision will use newest programs
          var startingUtc = DateTime.UtcNow;
          if (newJobs.Jobs.Any())
          {
            startingUtc = newJobs.Jobs[0].RouteStartingTimeUTC;
          }

          var negRevisionMap = AddPrograms(trans, newJobs.Programs, startingUtc);

          foreach (var job in newJobs.Jobs)
          {
            AddJob(trans, job, negRevisionMap);
          }

          AddSimulatedStations(trans, newJobs.StationUse);

          if (!string.IsNullOrEmpty(newJobs.ScheduleId) && newJobs.ExtraParts != null)
          {
            AddExtraParts(trans, newJobs.ScheduleId, newJobs.ExtraParts);
          }

          if (!string.IsNullOrEmpty(newJobs.ScheduleId) && newJobs.CurrentUnfilledWorkorders != null)
          {
            AddUnfilledWorkorders(trans, newJobs.ScheduleId, newJobs.CurrentUnfilledWorkorders);
          }

          if (!string.IsNullOrEmpty(newJobs.ScheduleId) && newJobs.DebugMessage != null)
          {
            using (var cmd = _connection.CreateCommand())
            {
              cmd.Transaction = trans;
              cmd.CommandText = "INSERT OR REPLACE INTO schedule_debug(ScheduleId, DebugMessage) VALUES ($sid,$debug)";
              cmd.Parameters.Add("sid", SqliteType.Text).Value = newJobs.ScheduleId;
              cmd.Parameters.Add("debug", SqliteType.Blob).Value = newJobs.DebugMessage;
              cmd.ExecuteNonQuery();
            }
          }

          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void AddPrograms(IEnumerable<MachineWatchInterface.ProgramEntry> programs, DateTime startingUtc)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          AddPrograms(trans, programs, startingUtc);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private void AddJob(IDbTransaction trans, MachineWatchInterface.JobPlan job, Dictionary<(string prog, long rev), long> negativeRevisionMap)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        string extraCols = "";
        string extraNames = "";

        if (_cfg.OldDbCols.JobTableHasOldPriorityColumn)
        {
          extraCols += ", Priority";
          extraNames += ",$pri";
          cmd.Parameters.Add("pri", SqliteType.Integer).Value = 0;
        }
        if (_cfg.OldDbCols.JobTableHasOldCreateMarkerColumn)
        {
          extraCols += ", CreateMarker";
          extraNames += ",$marker";
          cmd.Parameters.Add("marker", SqliteType.Integer).Value = 0;
        }

        cmd.CommandText =
          "INSERT INTO jobs(UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual " + extraCols + ") " +
            "VALUES($uniq,$part,$proc,$comment,$start,$end,$archived,$copied,$sid,$manual" + extraNames + ")";

        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("part", SqliteType.Text).Value = job.PartName;
        cmd.Parameters.Add("proc", SqliteType.Integer).Value = job.NumProcesses;
        if (string.IsNullOrEmpty(job.Comment))
          cmd.Parameters.Add("comment", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("comment", SqliteType.Text).Value = job.Comment;
        cmd.Parameters.Add("start", SqliteType.Integer).Value = job.RouteStartingTimeUTC.Ticks;
        cmd.Parameters.Add("end", SqliteType.Integer).Value = job.RouteEndingTimeUTC.Ticks;
        cmd.Parameters.Add("archived", SqliteType.Integer).Value = job.Archived;
        cmd.Parameters.Add("copied", SqliteType.Integer).Value = job.JobCopiedToSystem;
        if (string.IsNullOrEmpty(job.ScheduleId))
          cmd.Parameters.Add("sid", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("sid", SqliteType.Text).Value = job.ScheduleId;
        cmd.Parameters.Add("manual", SqliteType.Integer).Value = job.ManuallyCreatedJob;

        cmd.ExecuteNonQuery();

        if (job.ScheduledBookingIds != null)
        {
          cmd.CommandText = "INSERT INTO scheduled_bookings(UniqueStr, BookingId) VALUES ($uniq,$booking)";
          cmd.Parameters.Clear();
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
          cmd.Parameters.Add("booking", SqliteType.Text);
          foreach (var b in job.ScheduledBookingIds)
          {
            cmd.Parameters[1].Value = b;
            cmd.ExecuteNonQuery();
          }
        }

        cmd.CommandText = "INSERT INTO numpaths(UniqueStr, Process, NumPaths) VALUES ($uniq,$proc,$path)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);

        for (int i = 1; i <= job.NumProcesses; i++)
        {
          cmd.Parameters[1].Value = i;
          cmd.Parameters[2].Value = job.GetNumPaths(i);
          cmd.ExecuteNonQuery();
        }

        cmd.CommandText = "INSERT INTO planqty(UniqueStr, Path, PlanQty) VALUES ($uniq,$path,$plan)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("plan", SqliteType.Integer);

        for (int i = 1; i <= job.GetNumPaths(1); i++)
        {
          cmd.Parameters[1].Value = i;
          cmd.Parameters[2].Value = job.GetPlannedCyclesOnFirstProcess(i);
          cmd.ExecuteNonQuery();
        }

        cmd.CommandText = "INSERT OR REPLACE INTO simulated_production(UniqueStr, Process, Path, TimeUTC, Quantity) VALUES ($uniq,$proc,$path,$time,$qty)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("time", SqliteType.Integer);
        cmd.Parameters.Add("qty", SqliteType.Integer);

        for (int i = 1; i <= job.NumProcesses; i++)
        {
          for (int j = 1; j <= job.GetNumPaths(i); j++)
          {
            if (job.GetSimulatedProduction(i, j) == null) continue;
            foreach (var prod in job.GetSimulatedProduction(i, j))
            {
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;
              cmd.Parameters[3].Value = prod.TimeUTC.Ticks;
              cmd.Parameters[4].Value = prod.Quantity;
              cmd.ExecuteNonQuery();
            }
          }
        }

        cmd.CommandText = "INSERT INTO pallets(UniqueStr, Process, Path, Pallet) VALUES ($uniq,$proc,$path,$pal)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("pal", SqliteType.Text);

        for (int i = 1; i <= job.NumProcesses; i++)
        {
          for (int j = 1; j <= job.GetNumPaths(i); j++)
          {
            foreach (string pal in job.PlannedPallets(i, j))
            {
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;
              cmd.Parameters[3].Value = pal;
              cmd.ExecuteNonQuery();
            }
          }
        }

        cmd.CommandText = "INSERT INTO pathdata(UniqueStr, Process, Path, StartingUTC, PartsPerPallet, PathGroup,SimAverageFlowTime,InputQueue,OutputQueue,LoadTime,UnloadTime,Fixture,Face,Casting) " +
      "VALUES ($uniq,$proc,$path,$start,$ppp,$group,$flow,$iq,$oq,$lt,$ul,$fix,$face,$casting)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("start", SqliteType.Integer);
        cmd.Parameters.Add("ppp", SqliteType.Integer);
        cmd.Parameters.Add("group", SqliteType.Integer);
        cmd.Parameters.Add("flow", SqliteType.Integer);
        cmd.Parameters.Add("iq", SqliteType.Text);
        cmd.Parameters.Add("oq", SqliteType.Text);
        cmd.Parameters.Add("lt", SqliteType.Integer);
        cmd.Parameters.Add("ul", SqliteType.Integer);
        cmd.Parameters.Add("fix", SqliteType.Text);
        cmd.Parameters.Add("face", SqliteType.Integer);
        cmd.Parameters.Add("casting", SqliteType.Text);
        for (int i = 1; i <= job.NumProcesses; i++)
        {
          for (int j = 1; j <= job.GetNumPaths(i); j++)
          {
            cmd.Parameters[1].Value = i;
            cmd.Parameters[2].Value = j;
            cmd.Parameters[3].Value = job.GetSimulatedStartingTimeUTC(i, j).Ticks;
            cmd.Parameters[4].Value = job.PartsPerPallet(i, j);
            cmd.Parameters[5].Value = job.GetPathGroup(i, j);
            cmd.Parameters[6].Value = job.GetSimulatedAverageFlowTime(i, j).Ticks;
            var iq = job.GetInputQueue(i, j);
            if (string.IsNullOrEmpty(iq))
              cmd.Parameters[7].Value = DBNull.Value;
            else
              cmd.Parameters[7].Value = iq;
            var oq = job.GetOutputQueue(i, j);
            if (string.IsNullOrEmpty(oq))
              cmd.Parameters[8].Value = DBNull.Value;
            else
              cmd.Parameters[8].Value = oq;
            cmd.Parameters[9].Value = job.GetExpectedLoadTime(i, j).Ticks;
            cmd.Parameters[10].Value = job.GetExpectedUnloadTime(i, j).Ticks;
            var (fix, face) = job.PlannedFixture(i, j);
            cmd.Parameters[11].Value = string.IsNullOrEmpty(fix) ? DBNull.Value : (object)fix;
            cmd.Parameters[12].Value = face;
            if (i == 1)
            {
              var casting = job.GetCasting(j);
              cmd.Parameters[13].Value = string.IsNullOrEmpty(casting) ? DBNull.Value : (object)casting;
            }
            else
            {
              cmd.Parameters[13].Value = DBNull.Value;
            }
            cmd.ExecuteNonQuery();
          }
        }

        cmd.CommandText = "INSERT INTO stops(UniqueStr, Process, Path, RouteNum, StatGroup, ExpectedCycleTime, Program, ProgramRevision) " +
      "VALUES ($uniq,$proc,$path,$route,$group,$cycle,$prog,$rev)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("route", SqliteType.Integer);
        cmd.Parameters.Add("group", SqliteType.Text);
        cmd.Parameters.Add("cycle", SqliteType.Integer);
        cmd.Parameters.Add("prog", SqliteType.Text);
        cmd.Parameters.Add("rev", SqliteType.Integer);


        for (int i = 1; i <= job.NumProcesses; i++)
        {
          for (int j = 1; j <= job.GetNumPaths(i); j++)
          {
            int routeNum = 0;
            foreach (var entry in job.GetMachiningStop(i, j))
            {
              long? rev = null;
              if (!entry.ProgramRevision.HasValue || entry.ProgramRevision.Value == 0)
              {
                if (!string.IsNullOrEmpty(entry.ProgramName))
                {
                  rev = LatestRevisionForProgram(trans, entry.ProgramName);
                }
              }
              else if (entry.ProgramRevision.Value > 0)
              {
                rev = entry.ProgramRevision.Value;
              }
              else if (negativeRevisionMap.TryGetValue((prog: entry.ProgramName, rev: entry.ProgramRevision.Value), out long convertedRev))
              {
                rev = convertedRev;
              }
              else
              {
                throw new BadRequestException($"Part {job.PartName}, process {i}, path {j}, stop {routeNum}, " +
                  "has a negative program revision but no matching negative program revision exists in the downloaded ProgramEntry list");
              }
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;
              cmd.Parameters[3].Value = routeNum;
              cmd.Parameters[4].Value = entry.StationGroup;
              cmd.Parameters[5].Value = entry.ExpectedCycleTime.Ticks;
              cmd.Parameters[6].Value = string.IsNullOrEmpty(entry.ProgramName) ? DBNull.Value : (object)entry.ProgramName;
              cmd.Parameters[7].Value = rev != null ? (object)rev : DBNull.Value;
              cmd.ExecuteNonQuery();
              routeNum += 1;
            }
          }
        }

        cmd.CommandText = "INSERT INTO stops_stations(UniqueStr, Process, Path, RouteNum, StatNum) " +
      "VALUES ($uniq,$proc,$path,$route,$num)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("route", SqliteType.Integer);
        cmd.Parameters.Add("num", SqliteType.Integer);

        for (int i = 1; i <= job.NumProcesses; i++)
        {
          for (int j = 1; j <= job.GetNumPaths(i); j++)
          {
            int routeNum = 0;
            foreach (var entry in job.GetMachiningStop(i, j))
            {
              foreach (var stat in entry.Stations)
              {
                cmd.Parameters[1].Value = i;
                cmd.Parameters[2].Value = j;
                cmd.Parameters[3].Value = routeNum;
                cmd.Parameters[4].Value = stat;

                cmd.ExecuteNonQuery();
              }
              routeNum += 1;
            }
          }
        }

        cmd.CommandText = "INSERT INTO tools(UniqueStr, Process, Path, RouteNum, Tool, ExpectedUse) " +
      "VALUES ($uniq,$proc,$path,$route,$tool,$use)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("route", SqliteType.Integer);
        cmd.Parameters.Add("tool", SqliteType.Text);
        cmd.Parameters.Add("use", SqliteType.Integer);

        for (int i = 1; i <= job.NumProcesses; i++)
        {
          for (int j = 1; j <= job.GetNumPaths(i); j++)
          {
            int routeNum = 0;
            foreach (var entry in job.GetMachiningStop(i, j))
            {
              foreach (var tool in entry.Tools)
              {
                cmd.Parameters[1].Value = i;
                cmd.Parameters[2].Value = j;
                cmd.Parameters[3].Value = routeNum;
                cmd.Parameters[4].Value = tool.Key;
                cmd.Parameters[5].Value = tool.Value.Ticks;
                cmd.ExecuteNonQuery();
              }
              routeNum += 1;
            }
          }
        }

        cmd.CommandText = "INSERT INTO loadunload(UniqueStr,Process,Path,StatNum,Load) VALUES ($uniq,$proc,$path,$stat,$load)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("stat", SqliteType.Integer);
        cmd.Parameters.Add("load", SqliteType.Integer);

        for (int proc = 1; proc <= job.NumProcesses; proc++)
        {
          for (int path = 1; path <= job.GetNumPaths(proc); path++)
          {
            cmd.Parameters[1].Value = proc;
            cmd.Parameters[2].Value = path;
            cmd.Parameters[4].Value = true;
            foreach (int statNum in job.LoadStations(proc, path))
            {
              cmd.Parameters[3].Value = statNum;
              cmd.ExecuteNonQuery();
            }
            cmd.Parameters[4].Value = false;
            foreach (int statNum in job.UnloadStations(proc, path))
            {
              cmd.Parameters[3].Value = statNum;
              cmd.ExecuteNonQuery();
            }
          }
        }


        cmd.CommandText = "INSERT INTO path_inspections(UniqueStr,Process,Path,InspType,Counter,MaxVal,TimeInterval,RandomFreq,ExpectedTime) "
          + "VALUES ($uniq,$proc,$path,$insp,$cnt,$max,$time,$freq,$expected)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("insp", SqliteType.Text);
        cmd.Parameters.Add("cnt", SqliteType.Text);
        cmd.Parameters.Add("max", SqliteType.Integer);
        cmd.Parameters.Add("time", SqliteType.Integer);
        cmd.Parameters.Add("freq", SqliteType.Real);
        cmd.Parameters.Add("expected", SqliteType.Integer);
        for (int proc = 1; proc <= job.NumProcesses; proc++)
        {
          for (int path = 1; path <= job.GetNumPaths(proc); path++)
          {
            cmd.Parameters[1].Value = proc;
            cmd.Parameters[2].Value = path;

            foreach (var insp in job.PathInspections(proc, path))
            {
              cmd.Parameters[3].Value = insp.InspectionType;
              cmd.Parameters[4].Value = insp.Counter;
              cmd.Parameters[5].Value = insp.MaxVal > 0 ? (object)insp.MaxVal : DBNull.Value;
              cmd.Parameters[6].Value = insp.TimeInterval.Ticks > 0 ? (object)insp.TimeInterval.Ticks : DBNull.Value;
              cmd.Parameters[7].Value = insp.RandomFreq > 0 ? (object)insp.RandomFreq : DBNull.Value;
              cmd.Parameters[8].Value = insp.ExpectedInspectionTime.HasValue && insp.ExpectedInspectionTime.Value.Ticks > 0 ?
                (object)insp.ExpectedInspectionTime.Value.Ticks : DBNull.Value;
              cmd.ExecuteNonQuery();
            }
          }
        }
        foreach (var insp in job.GetOldObsoleteInspections() ?? Enumerable.Empty<MachineWatchInterface.JobInspectionData>())
        {
          cmd.Parameters[1].Value = insp.InspectSingleProcess > 0 ? Math.Min(insp.InspectSingleProcess, job.NumProcesses) : job.NumProcesses;
          cmd.Parameters[2].Value = -1; // Path = -1 is loaded above for all paths
          cmd.Parameters[3].Value = insp.InspectionType;
          cmd.Parameters[4].Value = insp.Counter;
          cmd.Parameters[5].Value = insp.MaxVal > 0 ? (object)insp.MaxVal : DBNull.Value;
          cmd.Parameters[6].Value = insp.TimeInterval.Ticks > 0 ? (object)insp.TimeInterval.Ticks : DBNull.Value;
          cmd.Parameters[7].Value = insp.RandomFreq > 0 ? (object)insp.RandomFreq : DBNull.Value;
          cmd.Parameters[8].Value = DBNull.Value;
          cmd.ExecuteNonQuery();
        }
      }

      InsertHold(job.UniqueStr, -1, -1, false, job.HoldEntireJob, trans);
      for (int proc = 1; proc <= job.NumProcesses; proc++)
      {
        for (int path = 1; path <= job.GetNumPaths(proc); path++)
        {
          InsertHold(job.UniqueStr, proc, path, true, job.HoldLoadUnload(proc, path), trans);
          InsertHold(job.UniqueStr, proc, path, false, job.HoldMachining(proc, path), trans);
        }
      }
    }

    private void AddSimulatedStations(IDbTransaction trans, IEnumerable<MachineWatchInterface.SimulatedStationUtilization> simStats)
    {
      if (simStats == null) return;

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT OR REPLACE INTO sim_station_use(SimId, StationGroup, StationNum, StartUTC, EndUTC, UtilizationTime, PlanDownTime) " +
            " VALUES($simid,$group,$num,$start,$end,$utilization,$plandown)";
        cmd.Parameters.Add("simid", SqliteType.Text);
        cmd.Parameters.Add("group", SqliteType.Text);
        cmd.Parameters.Add("num", SqliteType.Integer);
        cmd.Parameters.Add("start", SqliteType.Integer);
        cmd.Parameters.Add("end", SqliteType.Integer);
        cmd.Parameters.Add("utilization", SqliteType.Integer);
        cmd.Parameters.Add("plandown", SqliteType.Integer);

        foreach (var sim in simStats)
        {
          cmd.Parameters[0].Value = sim.ScheduleId;
          cmd.Parameters[1].Value = sim.StationGroup;
          cmd.Parameters[2].Value = sim.StationNum;
          cmd.Parameters[3].Value = sim.StartUTC.Ticks;
          cmd.Parameters[4].Value = sim.EndUTC.Ticks;
          cmd.Parameters[5].Value = sim.UtilizationTime.Ticks;
          cmd.Parameters[6].Value = sim.PlannedDownTime.Ticks;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddExtraParts(IDbTransaction trans, string scheduleId, IDictionary<string, int> extraParts)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;


        cmd.CommandText = "INSERT OR REPLACE INTO scheduled_parts(ScheduleId, Part, Quantity) VALUES ($sid,$part,$qty)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;
        cmd.Parameters.Add("part", SqliteType.Text);
        cmd.Parameters.Add("qty", SqliteType.Integer);
        foreach (var p in extraParts)
        {
          cmd.Parameters[1].Value = p.Key;
          cmd.Parameters[2].Value = p.Value;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddUnfilledWorkorders(IDbTransaction trans, string scheduleId, IEnumerable<MachineWatchInterface.PartWorkorder> workorders)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT OR REPLACE INTO unfilled_workorders(ScheduleId, Workorder, Part, Quantity, DueDate, Priority) VALUES ($sid,$work,$part,$qty,$due,$pri)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;
        cmd.Parameters.Add("work", SqliteType.Text);
        cmd.Parameters.Add("part", SqliteType.Text);
        cmd.Parameters.Add("qty", SqliteType.Integer);
        cmd.Parameters.Add("due", SqliteType.Integer);
        cmd.Parameters.Add("pri", SqliteType.Integer);
        foreach (var w in workorders)
        {
          cmd.Parameters[1].Value = w.WorkorderId;
          cmd.Parameters[2].Value = w.Part;
          cmd.Parameters[3].Value = w.Quantity;
          cmd.Parameters[4].Value = w.DueDate.Ticks;
          cmd.Parameters[5].Value = w.Priority;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void InsertHold(string unique, int proc, int path, bool load, MachineWatchInterface.JobHoldPattern newHold,
                            IDbTransaction trans)
    {
      if (newHold == null) return;

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT INTO holds(UniqueStr,Process,Path,LoadUnload,UserHold,UserHoldReason,HoldPatternStartUTC,HoldPatternRepeats) " +
      "VALUES ($uniq,$proc,$path,$load,$hold,$holdR,$holdT,$holdP)";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
        cmd.Parameters.Add("proc", SqliteType.Integer).Value = proc;
        cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
        cmd.Parameters.Add("load", SqliteType.Integer).Value = load;
        cmd.Parameters.Add("hold", SqliteType.Integer).Value = newHold.UserHold;
        cmd.Parameters.Add("holdR", SqliteType.Text).Value = newHold.ReasonForUserHold;
        cmd.Parameters.Add("holdT", SqliteType.Integer).Value = newHold.HoldUnholdPatternStartUTC.Ticks;
        cmd.Parameters.Add("holdP", SqliteType.Integer).Value = newHold.HoldUnholdPatternRepeats;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO hold_pattern(UniqueStr,Process,Path,LoadUnload,Idx,Span) " +
      "VALUES ($uniq,$proc,$path,$stat,$idx,$span)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
        cmd.Parameters.Add("proc", SqliteType.Integer).Value = proc;
        cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
        cmd.Parameters.Add("stat", SqliteType.Integer).Value = load;
        cmd.Parameters.Add("idx", SqliteType.Integer);
        cmd.Parameters.Add("span", SqliteType.Integer);
        for (int i = 0; i < newHold.HoldUnholdPattern.Count; i++)
        {
          cmd.Parameters[4].Value = i;
          cmd.Parameters[5].Value = newHold.HoldUnholdPattern[i].Ticks;
          cmd.ExecuteNonQuery();
        }
      }
    }

    public void ArchiveJob(string UniqueStr)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          SetArchived(trans, new[] { UniqueStr }, archived: true);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void ArchiveJobs(IEnumerable<string> uniqueStrs, IEnumerable<NewDecrementQuantity> newDecrements = null, DateTime? nowUTC = null)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          SetArchived(trans, uniqueStrs, archived: true);
          if (newDecrements != null)
          {
            AddNewDecrement(
              trans: trans,
              counts: newDecrements,
              removedBookings: null,
              nowUTC: nowUTC);
          }
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void UnarchiveJob(string UniqueStr)
    {
      UnarchiveJobs(new[] { UniqueStr });
    }

    public void UnarchiveJobs(IEnumerable<string> uniqueStrs, DateTime? nowUTC = null)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          SetArchived(trans, uniqueStrs, archived: false);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private void SetArchived(IDbTransaction trans, IEnumerable<string> uniqs, bool archived)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "UPDATE jobs SET Archived = $archived WHERE UniqueStr = $uniq";
        var param = cmd.Parameters.Add("uniq", SqliteType.Text);
        cmd.Parameters.Add("archived", SqliteType.Integer).Value = archived ? 1 : 0;
        foreach (var uniqStr in uniqs)
        {
          param.Value = uniqStr;
          cmd.ExecuteNonQuery();
        }
      }
    }

    public void MarkJobCopiedToSystem(string UniqueStr)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.CommandText = "UPDATE jobs SET CopiedToSystem = 1 WHERE UniqueStr = $uniq";
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = UniqueStr;
            ((IDbCommand)cmd).Transaction = trans;
            cmd.ExecuteNonQuery();
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
    #endregion

    #region "Modification of Jobs"
    public void SetJobComment(string unique, string comment)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {

          var trans = _connection.BeginTransaction();

          try
          {
            cmd.Transaction = trans;

            cmd.CommandText = "UPDATE jobs SET Comment = $comment WHERE UniqueStr = $uniq";
            cmd.Parameters.Add("comment", SqliteType.Text).Value = comment;
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
            cmd.ExecuteNonQuery();
            trans.Commit();
          }
          catch
          {
            trans.Rollback();
            throw;
          }
        }
      }
    }
    public void UpdateJobHold(string unique, MachineWatchInterface.JobHoldPattern newHold)
    {
      UpdateJobHoldHelper(unique, -1, -1, false, newHold);
    }

    public void UpdateJobMachiningHold(string unique, int proc, int path, MachineWatchInterface.JobHoldPattern newHold)
    {
      UpdateJobHoldHelper(unique, proc, path, false, newHold);
    }

    public void UpdateJobLoadUnloadHold(string unique, int proc, int path, MachineWatchInterface.JobHoldPattern newHold)
    {
      UpdateJobHoldHelper(unique, proc, path, true, newHold);
    }

    private void UpdateJobHoldHelper(string unique, int proc, int path, bool load, MachineWatchInterface.JobHoldPattern newHold)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();

        try
        {

          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;

            cmd.CommandText = "DELETE FROM holds WHERE UniqueStr = $uniq AND Process = $proc AND Path = $path AND LoadUnload = $load";
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
            cmd.Parameters.Add("proc", SqliteType.Integer).Value = proc;
            cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
            cmd.Parameters.Add("load", SqliteType.Integer).Value = load;
            cmd.ExecuteNonQuery();

            cmd.CommandText = "DELETE FROM hold_pattern WHERE UniqueStr = $uniq AND Process = $proc AND Path = $path AND LoadUnload = $load";
            cmd.ExecuteNonQuery();

            InsertHold(unique, proc, path, load, newHold, trans);

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
    #endregion

    #region Decrement Counts
    public class NewDecrementQuantity
    {
      public string JobUnique { get; set; }
      public string Part { get; set; }
      public int Quantity { get; set; }
    }

    public class RemovedBooking
    {
      public string JobUnique { get; set; }
      public string BookingId { get; set; }
    }

    public void AddNewDecrement(IEnumerable<NewDecrementQuantity> counts, DateTime? nowUTC = null, IEnumerable<RemovedBooking> removedBookings = null)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          AddNewDecrement(
            trans: trans,
            counts: counts,
            removedBookings: removedBookings,
            nowUTC: nowUTC
          );
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }

    }

    private void AddNewDecrement(IDbTransaction trans, IEnumerable<NewDecrementQuantity> counts, IEnumerable<RemovedBooking> removedBookings, DateTime? nowUTC)
    {
      var now = nowUTC ?? DateTime.UtcNow;
      long decrementId = 0;
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT MAX(DecrementId) FROM decrements";
        var lastDecId = cmd.ExecuteScalar();
        if (lastDecId != null && lastDecId != DBNull.Value)
        {
          decrementId = Convert.ToInt64(lastDecId) + 1;
        }
      }

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT INTO decrements(DecrementId,JobUnique,TimeUTC,Part,Quantity) VALUES ($id,$uniq,$now,$part,$qty)";
        cmd.Parameters.Add("id", SqliteType.Integer);
        cmd.Parameters.Add("uniq", SqliteType.Text);
        cmd.Parameters.Add("now", SqliteType.Integer);
        cmd.Parameters.Add("part", SqliteType.Text);
        cmd.Parameters.Add("qty", SqliteType.Integer);

        foreach (var q in counts)
        {
          cmd.Parameters[0].Value = decrementId;
          cmd.Parameters[1].Value = q.JobUnique;
          cmd.Parameters[2].Value = now.Ticks;
          cmd.Parameters[3].Value = q.Part;
          cmd.Parameters[4].Value = q.Quantity;
          cmd.ExecuteNonQuery();
        }
      }

      if (removedBookings != null)
      {
        using (var cmd = _connection.CreateCommand())
        {
          ((IDbCommand)cmd).Transaction = trans;

          cmd.CommandText = "DELETE FROM scheduled_bookings WHERE UniqueStr = $u AND BookingId = $b";
          cmd.Parameters.Add("u", SqliteType.Text);
          cmd.Parameters.Add("b", SqliteType.Text);

          foreach (var b in removedBookings)
          {
            cmd.Parameters[0].Value = b.JobUnique;
            cmd.Parameters[1].Value = b.BookingId;
            cmd.ExecuteNonQuery();
          }
        }
      }

    }

    public List<MachineWatchInterface.DecrementQuantity> LoadDecrementsForJob(string unique)
    {
      lock (_cfg)
      {
        return LoadDecrementsForJob(trans: null, unique: unique);
      }
    }

    private List<MachineWatchInterface.DecrementQuantity> LoadDecrementsForJob(IDbTransaction trans, string unique)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT DecrementId,TimeUTC,Quantity FROM decrements WHERE JobUnique = $uniq";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
        var ret = new List<MachineWatchInterface.DecrementQuantity>();
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var j = new MachineWatchInterface.DecrementQuantity();
            j.DecrementId = reader.GetInt64(0);
            j.TimeUTC = new DateTime(reader.GetInt64(1), DateTimeKind.Utc);
            j.Quantity = reader.GetInt32(2);
            ret.Add(j);
          }
          return ret;
        }
      }
    }

    public List<MachineWatchInterface.JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(long afterId)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT DecrementId,JobUnique,TimeUTC,Part,Quantity FROM decrements WHERE DecrementId > $after";
          cmd.Parameters.Add("after", SqliteType.Integer).Value = afterId;
          return LoadDecrementQuantitiesHelper(cmd);
        }
      }
    }

    public List<MachineWatchInterface.JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(DateTime afterUTC)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT DecrementId,JobUnique,TimeUTC,Part,Quantity FROM decrements WHERE TimeUTC > $after";
          cmd.Parameters.Add("after", SqliteType.Integer).Value = afterUTC.Ticks;
          return LoadDecrementQuantitiesHelper(cmd);
        }
      }
    }

    private List<MachineWatchInterface.JobAndDecrementQuantity> LoadDecrementQuantitiesHelper(IDbCommand cmd)
    {
      var ret = new List<MachineWatchInterface.JobAndDecrementQuantity>();
      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          var j = default(MachineWatchInterface.JobAndDecrementQuantity);
          j.DecrementId = reader.GetInt64(0);
          j.JobUnique = reader.GetString(1);
          j.TimeUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc);
          j.Part = reader.GetString(3);
          j.Quantity = reader.GetInt32(4);
          ret.Add(j);
        }
        return ret;
      }
    }
    #endregion

    #region Programs
    private Dictionary<(string prog, long rev), long> AddPrograms(IDbTransaction transaction, IEnumerable<MachineWatchInterface.ProgramEntry> programs, DateTime nowUtc)
    {
      if (programs == null || !programs.Any()) return new Dictionary<(string prog, long rev), long>();

      var negativeRevisionMap = new Dictionary<(string prog, long rev), long>();

      using (var checkCmd = _connection.CreateCommand())
      using (var checkMaxCmd = _connection.CreateCommand())
      using (var addProgCmd = _connection.CreateCommand())
      {
        ((IDbCommand)checkCmd).Transaction = transaction;
        checkCmd.CommandText = "SELECT ProgramContent FROM program_revisions WHERE ProgramName = $name AND ProgramRevision = $rev";
        checkCmd.Parameters.Add("name", SqliteType.Text);
        checkCmd.Parameters.Add("rev", SqliteType.Integer);

        ((IDbCommand)checkMaxCmd).Transaction = transaction;
        checkMaxCmd.CommandText = "SELECT ProgramRevision, ProgramContent FROM program_revisions WHERE ProgramName = $prog ORDER BY ProgramRevision DESC LIMIT 1";
        checkMaxCmd.Parameters.Add("prog", SqliteType.Text);

        ((IDbCommand)addProgCmd).Transaction = transaction;
        addProgCmd.CommandText = "INSERT INTO program_revisions(ProgramName, ProgramRevision, RevisionTimeUTC, RevisionComment, ProgramContent) " +
                            " VALUES($name,$rev,$time,$comment,$prog)";
        addProgCmd.Parameters.Add("name", SqliteType.Text);
        addProgCmd.Parameters.Add("rev", SqliteType.Integer);
        addProgCmd.Parameters.Add("time", SqliteType.Integer).Value = nowUtc.Ticks;
        addProgCmd.Parameters.Add("comment", SqliteType.Text);
        addProgCmd.Parameters.Add("prog", SqliteType.Text);

        // positive revisions are either added or checked for match
        foreach (var prog in programs.Where(p => p.Revision > 0))
        {
          checkCmd.Parameters[0].Value = prog.ProgramName;
          checkCmd.Parameters[1].Value = prog.Revision;
          var content = checkCmd.ExecuteScalar();
          if (content != null && content != DBNull.Value)
          {
            if ((string)content != prog.ProgramContent)
            {
              throw new BadRequestException("Program " + prog.ProgramName + " rev" + prog.Revision.ToString() + " has already been used and the program contents do not match.");
            }
            // if match, do nothing
          }
          else
          {
            addProgCmd.Parameters[0].Value = prog.ProgramName;
            addProgCmd.Parameters[1].Value = prog.Revision;
            addProgCmd.Parameters[3].Value = string.IsNullOrEmpty(prog.Comment) ? DBNull.Value : (object)prog.Comment;
            addProgCmd.Parameters[4].Value = string.IsNullOrEmpty(prog.ProgramContent) ? DBNull.Value : (object)prog.ProgramContent;
            addProgCmd.ExecuteNonQuery();
          }
        }

        // zero and negative revisions are allocated a new number
        foreach (var prog in programs.Where(p => p.Revision <= 0).OrderByDescending(p => p.Revision))
        {
          long lastRev;
          checkMaxCmd.Parameters[0].Value = prog.ProgramName;
          using (var reader = checkMaxCmd.ExecuteReader())
          {
            if (reader.Read())
            {
              lastRev = reader.GetInt64(0);
              var lastContent = reader.GetString(1);
              if (lastContent == prog.ProgramContent)
              {
                if (prog.Revision < 0) negativeRevisionMap[(prog: prog.ProgramName, rev: prog.Revision)] = lastRev;
                continue;
              }
            }
            else
            {
              lastRev = 0;
            }
          }

          addProgCmd.Parameters[0].Value = prog.ProgramName;
          addProgCmd.Parameters[1].Value = lastRev + 1;
          addProgCmd.Parameters[3].Value = string.IsNullOrEmpty(prog.Comment) ? DBNull.Value : (object)prog.Comment;
          addProgCmd.Parameters[4].Value = string.IsNullOrEmpty(prog.ProgramContent) ? DBNull.Value : (object)prog.ProgramContent;
          addProgCmd.ExecuteNonQuery();

          if (prog.Revision < 0) negativeRevisionMap[(prog: prog.ProgramName, rev: prog.Revision)] = lastRev + 1;
        }
      }

      return negativeRevisionMap;
    }

    private long? LatestRevisionForProgram(IDbTransaction trans, string program)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT MAX(ProgramRevision) FROM program_revisions WHERE ProgramName = $prog";
        cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
        var rev = cmd.ExecuteScalar();
        if (rev == null || rev == DBNull.Value)
        {
          return null;
        }
        else
        {
          return (long)rev;
        }
      }
    }

    public MachineWatchInterface.ProgramRevision ProgramFromCellControllerProgram(string cellCtProgName)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          MachineWatchInterface.ProgramRevision prog = null;
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT ProgramName, ProgramRevision, RevisionComment FROM program_revisions WHERE CellControllerProgramName = $prog LIMIT 1";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = cellCtProgName;
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                prog = new MachineWatchInterface.ProgramRevision
                {
                  ProgramName = reader.GetString(0),
                  Revision = reader.GetInt64(1),
                  Comment = reader.IsDBNull(2) ? null : reader.GetString(2),
                  CellControllerProgramName = cellCtProgName
                };
                break;
              }
            }
          }
          trans.Commit();
          return prog;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public MachineWatchInterface.ProgramRevision LoadProgram(string program, long revision)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          MachineWatchInterface.ProgramRevision prog = null;
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT RevisionComment, CellControllerProgramName FROM program_revisions WHERE ProgramName = $prog AND ProgramRevision = $rev";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
            cmd.Parameters.Add("rev", SqliteType.Integer).Value = revision;
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                prog = new MachineWatchInterface.ProgramRevision
                {
                  ProgramName = program,
                  Revision = revision,
                  Comment = reader.IsDBNull(0) ? null : reader.GetString(0),
                  CellControllerProgramName = reader.IsDBNull(1) ? null : reader.GetString(1)
                };
                break;
              }
            }
          }
          trans.Commit();
          return prog;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public List<MachineWatchInterface.ProgramRevision> LoadProgramRevisionsInDescendingOrderOfRevision(string program, int count, long? startRevision)
    {
      count = Math.Min(count, 100);
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          if (startRevision.HasValue)
          {
            cmd.CommandText = "SELECT ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions " +
                                " WHERE ProgramName = $prog AND ProgramRevision <= $rev " +
                                " ORDER BY ProgramRevision DESC " +
                                " LIMIT $cnt";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
            cmd.Parameters.Add("rev", SqliteType.Integer).Value = startRevision.Value;
            cmd.Parameters.Add("cnt", SqliteType.Integer).Value = count;
          }
          else
          {
            cmd.CommandText = "SELECT ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions " +
                                " WHERE ProgramName = $prog " +
                                " ORDER BY ProgramRevision DESC " +
                                " LIMIT $cnt";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
            cmd.Parameters.Add("cnt", SqliteType.Integer).Value = count;
          }

          using (var reader = cmd.ExecuteReader())
          {
            var ret = new List<MachineWatchInterface.ProgramRevision>();
            while (reader.Read())
            {
              ret.Add(new MachineWatchInterface.ProgramRevision
              {
                ProgramName = program,
                Revision = reader.GetInt64(0),
                Comment = reader.IsDBNull(1) ? null : reader.GetString(1),
                CellControllerProgramName = reader.IsDBNull(2) ? null : reader.GetString(2)
              });
            }
            return ret;
          }
        }
      }
    }

    public MachineWatchInterface.ProgramRevision LoadMostRecentProgram(string program)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          MachineWatchInterface.ProgramRevision prog = null;
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions WHERE ProgramName = $prog ORDER BY ProgramRevision DESC LIMIT 1";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                prog = new MachineWatchInterface.ProgramRevision
                {
                  ProgramName = program,
                  Revision = reader.GetInt64(0),
                  Comment = reader.IsDBNull(1) ? null : reader.GetString(1),
                  CellControllerProgramName = reader.IsDBNull(2) ? null : reader.GetString(2)
                };
                break;
              }
            }
          }
          trans.Commit();
          return prog;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public string LoadProgramContent(string program, long revision)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          string content = null;
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT ProgramContent FROM program_revisions WHERE ProgramName = $prog AND ProgramRevision = $rev";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
            cmd.Parameters.Add("rev", SqliteType.Integer).Value = revision;
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                if (!reader.IsDBNull(0))
                {
                  content = reader.GetString(0);
                }
                break;
              }
            }
          }
          trans.Commit();
          return content;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void SetCellControllerProgramForProgram(string program, long revision, string cellCtProgName)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          using (var cmd = _connection.CreateCommand())
          using (var checkCmd = _connection.CreateCommand())
          {
            if (!string.IsNullOrEmpty(cellCtProgName))
            {
              checkCmd.Transaction = trans;
              checkCmd.CommandText = "SELECT COUNT(*) FROM program_revisions WHERE CellControllerProgramName = $cell";
              checkCmd.Parameters.Add("cell", SqliteType.Text).Value = cellCtProgName;
              if ((long)checkCmd.ExecuteScalar() > 0)
              {
                throw new Exception("Cell program name " + cellCtProgName + " already in use");
              }
            }

            cmd.Transaction = trans;
            cmd.CommandText = "UPDATE program_revisions SET CellControllerProgramName = $cell WHERE ProgramName = $name AND ProgramRevision = $rev";
            cmd.Parameters.Add("cell", SqliteType.Text).Value = string.IsNullOrEmpty(cellCtProgName) ? DBNull.Value : (object)cellCtProgName;
            cmd.Parameters.Add("name", SqliteType.Text).Value = program;
            cmd.Parameters.Add("rev", SqliteType.Text).Value = revision;
            cmd.ExecuteNonQuery();
          }
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    #endregion
  }
}
