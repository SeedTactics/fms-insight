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
  internal static class DatabaseSchema
  {
    private const int Version = 25;

    #region Create
    public static void CreateTables(SqliteConnection connection, FMSSettings settings)
    {
      using (var trans = connection.BeginTransaction())
      {
        using (var cmd = connection.CreateCommand())
        {
          cmd.Transaction = trans;
          cmd.CommandText = "CREATE TABLE version(ver INTEGER)";
          cmd.ExecuteNonQuery();
          cmd.CommandText = "INSERT INTO version VALUES(" + Version.ToString() + ")";
          cmd.ExecuteNonQuery();
        }

        CreateLogTables(connection, trans, settings);
        CreateJobTables(connection, trans, settings);

        trans.Commit();
      }
    }

    private static void CreateLogTables(SqliteConnection connection, SqliteTransaction transaction, FMSSettings settings)
    {
      using (var cmd = connection.CreateCommand())
      {
        cmd.Transaction = transaction;

        //StationLoc, StationName and StationNum columns should be named
        //LogType, LocationName, and LocationNum but the column names are kept for backwards
        //compatibility
        cmd.CommandText = "CREATE TABLE stations(Counter INTEGER PRIMARY KEY AUTOINCREMENT,  Pallet TEXT," +
             "StationLoc INTEGER, StationName TEXT, StationNum INTEGER, Program TEXT, Start INTEGER, TimeUTC INTEGER," +
             "Result TEXT, EndOfRoute INTEGER, Elapsed INTEGER, ActiveTime INTEGER, ForeignID TEXT, OriginalMessage TEXT)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE stations_mat(Counter INTEGER, MaterialID INTEGER, " +
             "Process INTEGER, Face TEXT, PRIMARY KEY(Counter,MaterialID,Process))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX stations_idx ON stations(TimeUTC)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX stations_pal ON stations(Pallet, Result)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX stations_foreign ON stations(ForeignID)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX stations_material_idx ON stations_mat(MaterialID)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE matdetails(" +
            "MaterialID INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "UniqueStr TEXT, " +
            "PartName TEXT NOT NULL, " +
            "NumProcesses INTEGER NOT NULL, " +
            "Serial TEXT, " +
            "Workorder TEXT " +
            ")";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX matdetails_serial ON matdetails(Serial) WHERE Serial IS NOT NULL";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX matdetails_workorder ON matdetails(Workorder) WHERE Workorder IS NOT NULL";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX matdetails_uniq ON matdetails(UniqueStr)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE mat_path_details(MaterialID INTEGER, Process INTEGER, Path INTEGER, PRIMARY KEY(MaterialID, Process))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE pendingloads(Pallet TEXT, Key TEXT, LoadStation INTEGER, Elapsed INTEGER, ActiveTime INTEGER, ForeignID TEXT)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX pending_pal on pendingloads(Pallet)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX pending_foreign on pendingloads(ForeignID)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE program_details(Counter INTEGER, Key TEXT, Value TEXT, " +
            "PRIMARY KEY(Counter, Key))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE station_tools(Counter INTEGER, Tool TEXT, UseInCycle INTEGER, UseAtEndOfCycle INTEGER, ToolLife INTEGER, ToolChange INTEGER, " +
            "PRIMARY KEY(Counter, Tool))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE inspection_counters(Counter TEXT PRIMARY KEY, Val INTEGER, LastUTC INTEGER)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE inspection_next_piece(StatType INTEGER, StatNum INTEGER, InspType TEXT, PRIMARY KEY(StatType,StatNum, InspType))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE queues(MaterialID INTEGER, Queue TEXT, Position INTEGER, AddTimeUTC INTEGER, PRIMARY KEY(MaterialID, Queue))";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX queues_idx ON queues(Queue, Position)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE tool_snapshots(Counter INTEGER, PocketNumber INTEGER, Tool TEXT, CurrentUse INTEGER, ToolLife INTEGER, " +
            "PRIMARY KEY(Counter, PocketNumber, Tool))";
        cmd.ExecuteNonQuery();

        if (!string.IsNullOrEmpty(settings.StartingSerial))
        {
          long matId = settings.ConvertSerialToMaterialID(settings.StartingSerial) - 1;
          if (matId > 9007199254740991) // 2^53 - 1, the max size in a javascript 64-bit precision double
            throw new Exception("Serial " + settings.StartingSerial + " is too large");
          cmd.CommandText = "INSERT INTO sqlite_sequence(name, seq) VALUES ('matdetails',$v)";
          cmd.Parameters.Add("v", SqliteType.Integer).Value = matId;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private static void CreateJobTables(SqliteConnection connection, SqliteTransaction transaction, FMSSettings settings)
    {
      using (var cmd = connection.CreateCommand())
      {
        cmd.Transaction = transaction;

        cmd.CommandText = "CREATE TABLE jobs(UniqueStr TEXT PRIMARY KEY, Part TEXT NOT NULL, NumProcess INTEGER NOT NULL, Comment TEXT, AllocateAlg TEXT, StartUTC INTEGER NOT NULL, EndUTC INTEGER NOT NULL, Archived INTEGER NOT NULL, CopiedToSystem INTEGER NOT NULL, ScheduleId TEXT, Manual INTEGER)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX jobs_time_idx ON jobs(EndUTC, StartUTC)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX jobs_archived_idx ON jobs(Archived) WHERE Archived = 0";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX jobs_schedule_id ON jobs(ScheduleId) WHERE ScheduleId IS NOT NULL";
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

        cmd.CommandText = "CREATE TABLE job_decrements(DecrementId INTEGER NOT NULL, JobUnique TEXT NOT NULL, Proc1Path INTEGER NOT NULL, TimeUTC TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER, PRIMARY KEY(DecrementId, JobUnique, Proc1Path))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX job_decrements_time ON job_decrements(TimeUTC)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX job_decrements_unique ON job_decrements(JobUnique)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE schedule_debug(ScheduleId TEXT PRIMARY KEY, DebugMessage BLOB)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE unfilled_workorders(ScheduleId TEXT NOT NULL, Workorder TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER NOT NULL, DueDate INTEGER NOT NULL, Priority INTEGER NOT NULL, Archived INTEGER, PRIMARY KEY(ScheduleId, Part, Workorder))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX workorder_id_idx ON unfilled_workorders(Workorder, ScheduleId)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE workorder_programs(ScheduleId TEXT NOT NULL, Workorder TEXT NOT NULL, Part TEXT NOT NULL, ProcessNumber INTEGER NOT NULL, StopIndex INTEGER, ProgramName TEXT NOT NULL, Revision INTEGER, PRIMARY KEY(ScheduleId, Workorder, Part, ProcessNumber, StopIndex))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE program_revisions(ProgramName TEXT NOT NULL, ProgramRevision INTEGER NOT NULL, CellControllerProgramName TEXT, RevisionTimeUTC INTEGER NOT NULL, RevisionComment TEXT, ProgramContent TEXT, PRIMARY KEY(ProgramName, ProgramRevision))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX program_rev_cell_prog_name ON program_revisions(CellControllerProgramName, ProgramRevision) WHERE CellControllerProgramName IS NOT NULL";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX program_comment_idx ON program_revisions(ProgramName, RevisionComment, ProgramRevision) WHERE RevisionComment IS NOT NULL";
        cmd.ExecuteNonQuery();
      }
    }
    #endregion

    #region Upgrade
    public static void UpgradeTables(SqliteConnection connection, FMSSettings settings, string oldInspDbFile, string oldJobDbFile)
    {
      using (var cmd = connection.CreateCommand())
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

        if (curVersion > Version)
        {
          throw new Exception("This input file was created with a newer version of machine watch.  Please upgrade FMS Insight");
        }

        if (curVersion == Version)
        {
          using (var trans = connection.BeginTransaction())
          {
            AdjustStartingSerial(connection, trans, settings);
            trans.Commit();
          }
          return;
        }

        bool detachInsp = false;
        bool detachOldJob = false;
        using (var trans = connection.BeginTransaction())
        {
          //add upgrade code here, in seperate functions
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

          if (curVersion < 17)
            detachInsp = Ver16ToVer17(trans, oldInspDbFile);

          if (curVersion < 18) Ver17ToVer18(trans);

          if (curVersion < 19) Ver18ToVer19(trans);

          if (curVersion < 20) Ver19ToVer20(trans);

          if (curVersion < 21) Ver20ToVer21(trans);

          if (curVersion < 22) Ver21ToVer22(trans);

          if (curVersion < 23) Ver22ToVer23(trans);

          if (curVersion < 24) Ver23ToVer24(trans, settings, oldJobDbFile, out detachOldJob);

          bool updateJobsTables = curVersion >= 24; // Ver23ToVer24 creates fresh job tables, so no updates needed if coming from before then

          if (curVersion < 25) Ver24ToVer25(trans, updateJobsTables);


          //update the version in the database
          cmd.Transaction = trans;
          cmd.CommandText = "UPDATE version SET ver = " + Version.ToString();
          cmd.ExecuteNonQuery();

          AdjustStartingSerial(connection, trans, settings);

          trans.Commit();
        }

        //detaching must be outside the transaction which attached
        if (detachInsp) DetachInspectionDb(connection);
        if (detachOldJob) DetachJobsDb(connection);

        //only vacuum if we did some updating
        cmd.Transaction = null;
        cmd.CommandText = "VACUUM";
        cmd.ExecuteNonQuery();

        try
        {
          if (!string.IsNullOrEmpty(oldInspDbFile) && System.IO.File.Exists(oldInspDbFile))
          {
            System.IO.File.Delete(oldInspDbFile);
          }
          if (!string.IsNullOrEmpty(oldJobDbFile) && System.IO.File.Exists(oldJobDbFile))
          {
            System.IO.File.Delete(oldJobDbFile);
          }
        }
        catch (Exception ex)
        {
          Serilog.Log.Error(ex, "Error deleting old database files");
        }
      }
    }

    private static void AdjustStartingSerial(SqliteConnection conn, SqliteTransaction trans, FMSSettings settings)
    {
      if (string.IsNullOrEmpty(settings.StartingSerial)) return;
      long startingMatId = settings.ConvertSerialToMaterialID(settings.StartingSerial) - 1;
      if (startingMatId > 9007199254740991) // 2^53 - 1, the max size in a javascript 64-bit precision double
        throw new Exception("Serial " + settings.StartingSerial + " is too large");

      using (var cmd = conn.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "SELECT seq FROM sqlite_sequence WHERE name == 'matdetails'";
        var last = cmd.ExecuteScalar();
        if (last != null && last != DBNull.Value)
        {
          var lastId = (long)last;
          // if starting mat id is moving forward, update it
          if (lastId + 1000 < startingMatId)
          {
            cmd.CommandText = "UPDATE sqlite_sequence SET seq = $v WHERE name == 'matdetails'";
            cmd.Parameters.Add("v", SqliteType.Integer).Value = startingMatId;
            cmd.ExecuteNonQuery();
          }
        }
      }
    }

    private static void Ver0ToVer1(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE material ADD Part TEXT";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver1ToVer2(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE material ADD NumProcess INTEGER";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver2ToVer3(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE stations ADD EndOfRoute INTEGER";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver3ToVer4(IDbTransaction trans)
    {
      // This version added columns which have since been removed.
    }

    private static void Ver4ToVer5(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE stations ADD Elapsed INTEGER";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver5ToVer6(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE material ADD Face TEXT";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver6ToVer7(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE stations ADD ForeignID TEXT";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX stations_pal ON stations(Pallet, EndOfRoute);";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX stations_foreign ON stations(ForeignID)";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver7ToVer8(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE TABLE pendingloads(Pallet TEXT, Key TEXT, LoadStation INTEGER, Elapsed INTEGER, ForeignID TEXT)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX pending_pal on pendingloads(Pallet)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX pending_foreign on pendingloads(ForeignID)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP INDEX stations_pal";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX stations_pal ON stations(Pallet, Result)";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver8ToVer9(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE TABLE program_details(Counter INTEGER, Key TEXT, Value TEXT, " +
            "PRIMARY KEY(Counter, Key))";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver9ToVer10(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE stations ADD ActiveTime INTEGER";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE pendingloads ADD ActiveTime INTEGER";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "ALTER TABLE materialid ADD Serial TEXT";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX materialid_serial ON materialid(Serial) WHERE Serial IS NOT NULL";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver10ToVer11(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE stations ADD OriginalMessage TEXT";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver11ToVer12(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE materialid ADD Workorder TEXT";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX materialid_workorder ON materialid(Workorder) WHERE Workorder IS NOT NULL";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver12ToVer13(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "ALTER TABLE stations ADD StationName TEXT";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver13ToVer14(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE TABLE sersettings(ID INTEGER PRIMARY KEY, SerialType INTEGER, SerialLength INTEGER, DepositProc INTEGER, FilenameTemplate TEXT, ProgramTemplate TEXT)";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver14ToVer15(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE INDEX materialid_uniq ON materialid(UniqueStr)";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver15ToVer16(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "DROP TABLE sersettings";
        cmd.ExecuteNonQuery();
      }
    }

    private static bool Ver16ToVer17(SqliteTransaction trans, string inspDbFile)
    {
      using (var cmd = trans.Connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "CREATE TABLE inspection_counters(Counter TEXT PRIMARY KEY, Val INTEGER, LastUTC INTEGER)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE inspection_next_piece(StatType INTEGER, StatNum INTEGER, InspType TEXT, PRIMARY KEY(StatType,StatNum, InspType))";
        cmd.ExecuteNonQuery();

        if (string.IsNullOrEmpty(inspDbFile)) return false;
        if (!System.IO.File.Exists(inspDbFile)) return false;

        cmd.CommandText = "ATTACH DATABASE $db AS insp";
        cmd.Parameters.Add("db", SqliteType.Text).Value = inspDbFile;
        cmd.ExecuteNonQuery();
        cmd.Parameters.Clear();

        cmd.CommandText = "INSERT INTO main.inspection_counters SELECT * FROM insp.counters";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO main.inspection_next_piece SELECT * FROM insp.next_piece";
        cmd.ExecuteNonQuery();
      }

      return true;
    }

    private static void DetachInspectionDb(SqliteConnection conn)
    {
      using (var cmd = conn.CreateCommand())
      {
        cmd.CommandText = "DETACH DATABASE insp";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver17ToVer18(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE TABLE queues(MaterialID INTEGER, Queue TEXT, Position INTEGER, PRIMARY KEY(MaterialID, Queue))";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX queues_idx ON queues(Queue, Position)";
        cmd.ExecuteNonQuery();

        // new stations_mat table equals old material table but without some columns
        cmd.CommandText = "CREATE TABLE stations_mat(Counter INTEGER, MaterialID INTEGER, " +
             "Process INTEGER, Face TEXT, PRIMARY KEY(Counter,MaterialID,Process))";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX stations_material_idx ON stations_mat(MaterialID)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO stations_mat(Counter, MaterialID, Process, Face) " +
          " SELECT Counter, MaterialID, Process, Face FROM material";
        cmd.ExecuteNonQuery();


        // new matdetails table
        cmd.CommandText = "CREATE TABLE matdetails(" +
            "MaterialID INTEGER PRIMARY KEY AUTOINCREMENT, " +
            "UniqueStr TEXT, " +
            "PartName TEXT," +
            "NumProcesses INTEGER, " +
            "Serial TEXT, " +
            "Workorder TEXT " +
            ")";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX matdetails_serial ON matdetails(Serial) WHERE Serial IS NOT NULL";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX matdetails_workorder ON matdetails(Workorder) WHERE Workorder IS NOT NULL";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "CREATE INDEX matdetails_uniq ON matdetails(UniqueStr)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO matdetails(MaterialID, UniqueStr, Serial, Workorder, PartName, NumProcesses) " +
          " SELECT " +
          "   MaterialID, UniqueStr, Serial, Workorder, " +
          "   (SELECT Part FROM material WHERE material.MaterialID = materialid.MaterialID LIMIT 1) as PartName, " +
          "   (SELECT NumProcess FROM material WHERE material.MaterialID = materialid.MaterialID LIMIT 1) as NumProcesses " +
          " FROM materialid";
        cmd.ExecuteNonQuery();


        cmd.CommandText = "DROP TABLE material";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DROP TABLE materialid";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver18ToVer19(IDbTransaction trans)
    {
      using (IDbCommand cmd = trans.Connection.CreateCommand())
      {
        cmd.Transaction = trans;
        cmd.CommandText = "CREATE TABLE station_tools(Counter INTEGER, Tool TEXT, UseInCycle INTEGER, UseAtEndOfCycle INTEGER, ToolLife INTEGER, " +
            "PRIMARY KEY(Counter, Tool))";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver19ToVer20(IDbTransaction transaction)
    {
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;
        cmd.CommandText = "CREATE TABLE mat_path_details(MaterialID INTEGER, Process INTEGER, Path INTEGER, PRIMARY KEY(MaterialID, Process))";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver20ToVer21(IDbTransaction transaction)
    {
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;
        cmd.CommandText = "CREATE TABLE tool_snapshots(Counter INTEGER, PocketNumber INTEGER, Tool TEXT, CurrentUse INTEGER, ToolLife INTEGER, " +
            "PRIMARY KEY(Counter, PocketNumber))";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver21ToVer22(IDbTransaction transaction)
    {
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;

        cmd.CommandText = "DROP TABLE tool_snapshots";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE tool_snapshots(Counter INTEGER, PocketNumber INTEGER, Tool TEXT, CurrentUse INTEGER, ToolLife INTEGER, " +
            "PRIMARY KEY(Counter, PocketNumber, Tool))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "ALTER TABLE queues ADD AddTimeUTC INTEGER";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver22ToVer23(IDbTransaction transaction)
    {
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;

        cmd.CommandText = "ALTER TABLE station_tools ADD ToolChange INTEGER";
        cmd.ExecuteNonQuery();
      }
    }

    private static void DetachJobsDb(SqliteConnection conn)
    {
      using (var cmd = conn.CreateCommand())
      {
        cmd.CommandText = "DETACH DATABASE jobs";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver23ToVer24(SqliteTransaction transaction, FMSSettings settings, string oldJobDb, out bool attachedOldJobDb)
    {
      CreateJobTables(transaction.Connection, transaction, settings);

      if (!string.IsNullOrEmpty(oldJobDb) && System.IO.File.Exists(oldJobDb))
      {
        OldJobDBSchema.CheckOldJobDbUpgraded(oldJobDb);

        using (var cmd = transaction.Connection.CreateCommand())
        {
          cmd.Transaction = transaction;

          cmd.CommandText = "ATTACH DATABASE $db AS jobs";
          cmd.Parameters.Add("db", SqliteType.Text).Value = oldJobDb;
          cmd.ExecuteNonQuery();
          cmd.Parameters.Clear();

          var jobTableNames = new[] {
            "planqty",
            "pathdata",
            "pallets",
            "stops",
            "stops_stations",
            "tools",
            "loadunload",
            "path_inspections",
            "holds",
            "hold_pattern",
            "simulated_production",
            "sim_station_use",
            "scheduled_bookings",
            "scheduled_parts",
            "job_decrements",
            "schedule_debug",
            "unfilled_workorders",
            "workorder_programs",
            "program_revisions"
          };

          foreach (var tableName in jobTableNames)
          {
            cmd.CommandText = $"INSERT INTO main.{tableName} SELECT * FROM jobs.{tableName}";
            cmd.ExecuteNonQuery();
          }

          cmd.CommandText = "INSERT INTO main.jobs(UniqueStr,Part,NumProcess,Comment,StartUTC,EndUTC,Archived,CopiedToSystem,ScheduleId,Manual) SELECT UniqueStr,Part,NumProcess,Comment,StartUTC,EndUTC,Archived,CopiedToSystem,ScheduleId,Manual FROM jobs.jobs";
          cmd.ExecuteNonQuery();

          attachedOldJobDb = true;
        }
      }
      else
      {
        attachedOldJobDb = false;
      }
    }

    private static void Ver24ToVer25(IDbTransaction transaction, bool updateJobsTables)
    {
      if (!updateJobsTables) return;
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;
        cmd.CommandText = "ALTER TABLE jobs ADD AllocateAlg TEXT";
        cmd.ExecuteNonQuery();
      }
    }
    #endregion
  }

  internal static class OldJobDBSchema
  {
    internal static void CheckOldJobDbUpgraded(string oldJobDbFile)
    {
      using (var conn = new SqliteConnection("Data Source=" + oldJobDbFile))
      {
        conn.Open();
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

          if (curVersion > 23)
          {
            throw new Exception("This input file was created with a newer version of machine watch.  Please upgrade machine watch");
          }

          if (curVersion == 23)
          {
            return;
          }


          using (var trans = conn.BeginTransaction())
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
            if (curVersion < 22) Ver20ToVer22(trans);
            if (curVersion < 23) Ver22ToVer23(trans);

            //update the version in the database
            cmd.Transaction = trans;
            cmd.CommandText = "UPDATE version SET ver = " + 23.ToString();
            cmd.ExecuteNonQuery();

            trans.Commit();

          }

          return;
        }
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

    private static void Ver20ToVer22(IDbTransaction transaction)
    {
      // There was an error in the upgrade, so version 21 and 22 are the same.
      // Also, must check if job_decrements table already exists and don't create it twice.
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;

        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='job_decrements'";
        var cnt = cmd.ExecuteScalar();
        if (cnt != null && cnt != DBNull.Value && (long)cnt > 0) return;

        cmd.CommandText = "CREATE TABLE job_decrements(DecrementId INTEGER NOT NULL, JobUnique TEXT NOT NULL, Proc1Path INTEGER NOT NULL, TimeUTC TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER, PRIMARY KEY(DecrementId, JobUnique, Proc1Path))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX job_decrements_time ON job_decrements(TimeUTC)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX job_decrements_unique ON job_decrements(JobUnique)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO job_decrements(DecrementId, JobUnique, Proc1Path, TimeUTC, Part, Quantity) " +
          " SELECT DecrementId, JobUnique, 0, TimeUTC, Part, Quantity FROM decrements";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE decrements";
        cmd.ExecuteNonQuery();
      }
    }

    private static void Ver22ToVer23(IDbTransaction transaction)
    {
      using (IDbCommand cmd = transaction.Connection.CreateCommand())
      {
        cmd.Transaction = transaction;

        cmd.CommandText = "ALTER TABLE unfilled_workorders ADD Archived INTEGER";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE TABLE workorder_programs(ScheduleId TEXT NOT NULL, Workorder TEXT NOT NULL, Part TEXT NOT NULL, ProcessNumber INTEGER NOT NULL, StopIndex INTEGER, ProgramName TEXT NOT NULL, Revision INTEGER, PRIMARY KEY(ScheduleId, Workorder, Part, ProcessNumber, StopIndex))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX workorder_id_idx ON unfilled_workorders(Workorder, ScheduleId)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX program_comment_idx ON program_revisions(ProgramName, RevisionComment, ProgramRevision) WHERE RevisionComment IS NOT NULL";
        cmd.ExecuteNonQuery();
      }
    }
  }
}