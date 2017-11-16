/* Copyright (c) 2017, John Lenz

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
using System.Data;
using System.Collections.Generic;
#if MICROSOFT_DATA_SQLITE
using Microsoft.Data.Sqlite;
#endif

namespace BlackMaple.MachineFramework
{
    public class JobLogDB : MachineWatchInterface.ILogServerV2
    {

        #region Database Create/Update
        private SqliteConnection _connection;
        private object _lock;

        public JobLogDB()
        {
            _lock = new object();
        }
        public JobLogDB(SqliteConnection c) : this()
        {
            _connection = c;
        }

        public void Open(string filename)
        {
            if (System.IO.File.Exists(filename))
            {
                _connection = SqliteExtensions.Connect(filename, newFile: false);
                _connection.Open();
                UpdateTables();
            }
            else
            {
                _connection = SqliteExtensions.Connect(filename, newFile: true);
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
            LoadSettings();
        }

        public void Close()
        {
            _connection.Close();
        }


        private const int Version = 15;

        public void CreateTables()
        {
            var cmd = _connection.CreateCommand();

            cmd.CommandText = "CREATE TABLE version(ver INTEGER)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO version VALUES(" + Version.ToString() + ")";
            cmd.ExecuteNonQuery();

            //StationLoc, StationName and StationNum columns should be named
            //LogType, LocationName, and LocationNum but the column names are kept for backwards
            //compatibility
            cmd.CommandText = "CREATE TABLE stations(Counter INTEGER PRIMARY KEY AUTOINCREMENT,  Pallet TEXT," +
                 "StationLoc INTEGER, StationName TEXT, StationNum INTEGER, Program TEXT, Start INTEGER, TimeUTC INTEGER," +
                 "Result TEXT, EndOfRoute INTEGER, Elapsed INTEGER, ActiveTime INTEGER, ForeignID TEXT, OriginalMessage TEXT)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE material(Counter INTEGER, MaterialID INTEGER, UniqueStr TEXT," +
                 "Process INTEGER, Part TEXT, NumProcess INTEGER, Face TEXT, PRIMARY KEY(Counter,MaterialID,Process))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX stations_idx ON stations(TimeUTC)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX stations_pal ON stations(Pallet, Result)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX stations_foreign ON stations(ForeignID)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX material_idx ON material(MaterialID)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE materialid(MaterialID INTEGER PRIMARY KEY AUTOINCREMENT," +
                 "UniqueStr TEXT NOT NULL, Serial TEXT, Workorder TEXT)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX materialid_serial ON materialid(Serial) WHERE Serial IS NOT NULL";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX materialid_workorder ON materialid(Workorder) WHERE Workorder IS NOT NULL";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX materialid_uniq ON materialid(UniqueStr)";
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

            cmd.CommandText = "CREATE TABLE sersettings(ID INTEGER PRIMARY KEY, SerialType INTEGER, SerialLength INTEGER, DepositProc INTEGER, FilenameTemplate TEXT, ProgramTemplate TEXT)";
            cmd.ExecuteNonQuery();
        }


        private void UpdateTables()
        {
            var cmd = _connection.CreateCommand();

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
                throw new Exception("This input file was created with a newer version of machine watch.  Please upgrade machine watch");
            }

            if (curVersion == Version) return;


            var trans = _connection.BeginTransaction();

            try
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
        }

        private void Ver0ToVer1(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "ALTER TABLE material ADD Part TEXT";
            cmd.ExecuteNonQuery();
        }

        private void Ver1ToVer2(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "ALTER TABLE material ADD NumProcess INTEGER";
            cmd.ExecuteNonQuery();
        }

        private void Ver2ToVer3(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "ALTER TABLE stations ADD EndOfRoute INTEGER";
            cmd.ExecuteNonQuery();
        }

        private void Ver3ToVer4(IDbTransaction trans)
        {
            // This version added columns which have since been removed.
        }

        private void Ver4ToVer5(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "ALTER TABLE stations ADD Elapsed INTEGER";
            cmd.ExecuteNonQuery();
        }

        private void Ver5ToVer6(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "ALTER TABLE material ADD Face TEXT";
            cmd.ExecuteNonQuery();
        }

        private void Ver6ToVer7(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "ALTER TABLE stations ADD ForeignID TEXT";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX stations_pal ON stations(Pallet, EndOfRoute);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX stations_foreign ON stations(ForeignID)";
            cmd.ExecuteNonQuery();
        }

        private void Ver7ToVer8(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
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

        private void Ver8ToVer9(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "CREATE TABLE program_details(Counter INTEGER, Key TEXT, Value TEXT, " +
                "PRIMARY KEY(Counter, Key))";
            cmd.ExecuteNonQuery();
        }

        private void Ver9ToVer10(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
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

        private void Ver10ToVer11(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "ALTER TABLE stations ADD OriginalMessage TEXT";
            cmd.ExecuteNonQuery();
        }

        private void Ver11ToVer12(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "ALTER TABLE materialid ADD Workorder TEXT";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE INDEX materialid_workorder ON materialid(Workorder) WHERE Workorder IS NOT NULL";
            cmd.ExecuteNonQuery();
        }

        private void Ver12ToVer13(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "ALTER TABLE stations ADD StationName TEXT";
            cmd.ExecuteNonQuery();
        }

        private void Ver13ToVer14(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "CREATE TABLE sersettings(ID INTEGER PRIMARY KEY, SerialType INTEGER, SerialLength INTEGER, DepositProc INTEGER, FilenameTemplate TEXT, ProgramTemplate TEXT)";
            cmd.ExecuteNonQuery();
        }

        private void Ver14ToVer15(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = "CREATE INDEX materialid_uniq ON materialid(UniqueStr)";
            cmd.ExecuteNonQuery();
        }
        #endregion

        #region Event
        public delegate void NewStationCycleHandler(MachineWatchInterface.LogEntry log, string foreignID);
        public event NewStationCycleHandler NewStationCycle;
        #endregion

        #region Loading
        private List<MachineWatchInterface.LogEntry> LoadLog(IDataReader reader)
        {
            var matCmd = _connection.CreateCommand();
            matCmd.CommandText = "SELECT MaterialID, UniqueStr, Process, Part, NumProcess, Face FROM material WHERE Counter = $cntr ORDER BY Counter ASC";
            matCmd.Parameters.Add("cntr", SqliteType.Integer);

            var detailCmd = _connection.CreateCommand();
            detailCmd.CommandText = "SELECT Key, Value FROM program_details WHERE Counter = $cntr";
            detailCmd.Parameters.Add("cntr", SqliteType.Integer);

            var lst = new List<MachineWatchInterface.LogEntry>();

            while (reader.Read())
            {
                long ctr = reader.GetInt64(0);
                string pal = reader.GetString(1);
                int logType = reader.GetInt32(2);
                int locNum = reader.GetInt32(3);
                string prog = reader.GetString(4);
                bool start = reader.GetBoolean(5);
                System.DateTime timeUTC = new DateTime(reader.GetInt64(6), DateTimeKind.Utc);
                string result = reader.GetString(7);
                bool endOfRoute = false;
                if (!reader.IsDBNull(8))
                    endOfRoute = reader.GetBoolean(8);
                TimeSpan elapsed = TimeSpan.FromMinutes(-1);
                if (!reader.IsDBNull(9))
                    elapsed = TimeSpan.FromTicks(reader.GetInt64(9));
                TimeSpan active = TimeSpan.Zero;
                if (!reader.IsDBNull(10))
                    active = TimeSpan.FromTicks(reader.GetInt64(10));
                string locName = null;
                if (!reader.IsDBNull(11))
                    locName = reader.GetString(11);

                MachineWatchInterface.LogType ty;
                if (Enum.IsDefined(typeof(MachineWatchInterface.LogType), logType))
                {
                    ty = (MachineWatchInterface.LogType)logType;
                    if (locName == null)
                    {
                        //For compatibility with old logs
                        switch (ty)
                        {
                            case MachineWatchInterface.LogType.GeneralMessage:
                                locName = "General";
                                break;
                            case MachineWatchInterface.LogType.Inspection:
                                locName = "Inspect";
                                break;
                            case MachineWatchInterface.LogType.LoadUnloadCycle:
                                locName = "Load";
                                break;
                            case MachineWatchInterface.LogType.MachineCycle:
                                locName = "MC";
                                break;
                            case MachineWatchInterface.LogType.OrderAssignment:
                                locName = "Order";
                                break;
                            case MachineWatchInterface.LogType.PartMark:
                                locName = "Mark";
                                break;
                            case MachineWatchInterface.LogType.PalletCycle:
                                locName = "Pallet Cycle";
                                break;
                        }
                    }
                }
                else
                {
                    ty = MachineWatchInterface.LogType.GeneralMessage;
                    locName = ((MachineWatchInterface.PalletLocationTypeEnum)logType).ToString();
                }

                var matLst = new List<MachineWatchInterface.LogMaterial>();
                matCmd.Parameters[0].Value = ctr;
                using (var matReader = matCmd.ExecuteReader())
                {
                    while (matReader.Read())
                    {
                        string part = "";
                        int numProc = -1;
                        string face = "";
                        if (!matReader.IsDBNull(3))
                            part = matReader.GetString(3);
                        if (!matReader.IsDBNull(4))
                            numProc = matReader.GetInt32(4);
                        if (!matReader.IsDBNull(5))
                            face = matReader.GetString(5);
                        matLst.Add(new MachineWatchInterface.LogMaterial(matReader.GetInt64(0),
                                                                         matReader.GetString(1),
                                                                         matReader.GetInt32(2),
                                                                         part, numProc, face));
                    }
                }

                var logRow = new MachineWatchInterface.LogEntry(ctr, matLst, pal,
                      ty, locName, locNum,
                    prog, start, timeUTC, result, endOfRoute, elapsed, active);

                detailCmd.Parameters[0].Value = ctr;
                using (var detailReader = detailCmd.ExecuteReader())
                {
                    while (detailReader.Read())
                    {
                        logRow.ProgramDetails[detailReader.GetString(0)] = detailReader.GetString(1);
                    }
                }

                lst.Add(logRow);
            }

            return lst;
        }

        public List<MachineWatchInterface.LogEntry> GetLogEntries(System.DateTime startUTC, System.DateTime endUTC)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
                     " FROM stations WHERE TimeUTC >= $start AND TimeUTC <= $end ORDER BY Counter ASC";

                cmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
                cmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;

                using (var reader = cmd.ExecuteReader())
                {
                    return LoadLog(reader);
                }
            }
        }


        public List<MachineWatchInterface.LogEntry> GetLog(long counter)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
                     " FROM stations WHERE Counter > $cntr ORDER BY Counter ASC";
                cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;

                using (var reader = cmd.ExecuteReader())
                {
                    return LoadLog(reader);
                }
            }
        }

        public List<MachineWatchInterface.LogEntry> StationLogByForeignID(string foreignID)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
                     " FROM stations WHERE ForeignID = $foreign ORDER BY Counter ASC";
                cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;

                using (var reader = cmd.ExecuteReader())
                {
                    return LoadLog(reader);
                }
            }
        }

        public string OriginalMessageByForeignID(string foreignID)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT OriginalMessage " +
                     " FROM stations WHERE ForeignID = $foreign ORDER BY Counter DESC LIMIT 1";
                cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(0))
                        {
                            return "";
                        }
                        else
                        {
                            return reader.GetString(0);
                        }
                    }
                }
            }
            return "";
        }

        public List<MachineWatchInterface.LogEntry> GetLogForMaterial(long materialID)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
                     " FROM stations WHERE Counter IN (SELECT Counter FROM material WHERE MaterialID = $mat) ORDER BY Counter ASC";
                cmd.Parameters.Add("mat", SqliteType.Integer).Value = materialID;

                using (var reader = cmd.ExecuteReader())
                {
                    return LoadLog(reader);
                }
            }
        }

        public List<MachineWatchInterface.LogEntry> GetLogForSerial(string serial)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
                    " FROM stations WHERE Counter IN (SELECT material.Counter FROM materialid INNER JOIN material ON material.MaterialID = materialid.MaterialID WHERE materialid.Serial = $ser) ORDER BY Counter ASC";
                cmd.Parameters.Add("ser", SqliteType.Text).Value = serial;

                using (var reader = cmd.ExecuteReader())
                {
                    return LoadLog(reader);
                }
            }
        }

        public List<MachineWatchInterface.LogEntry> GetLogForJobUnique(string jobUnique)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
                    " FROM stations WHERE Counter IN (SELECT material.Counter FROM materialid INNER JOIN material ON material.MaterialID = materialid.MaterialID WHERE materialid.UniqueStr = $uniq) ORDER BY Counter ASC";
                cmd.Parameters.Add("uniq", SqliteType.Text).Value = jobUnique;

                using (var reader = cmd.ExecuteReader())
                {
                    return LoadLog(reader);
                }
            }
        }

        public List<MachineWatchInterface.LogEntry> GetLogForWorkorder(string workorder)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
                    " FROM stations WHERE Counter IN (SELECT material.Counter FROM materialid INNER JOIN material ON material.MaterialID = materialid.MaterialID WHERE materialid.Workorder = $work) ORDER BY Counter ASC";
                cmd.Parameters.Add("work", SqliteType.Text).Value = workorder;

                using (var reader = cmd.ExecuteReader())
                {
                    return LoadLog(reader);
                }
            }
        }

        public List<MachineWatchInterface.LogEntry> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC)
        {
            var searchCompleted = @"
                SELECT Counter FROM material
                    WHERE MaterialId IN
                        (SELECT material.MaterialId FROM stations, material
                            WHERE
                                stations.Counter = material.Counter
                                AND
                                stations.EndOfRoute = 1
                                AND
                                stations.TimeUTC <= $endUTC
                                AND
                                stations.TimeUTC >= $startUTC
                                AND
                                material.Process = material.NumProcess
                        )";

            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
                    " FROM stations WHERE Counter IN (" + searchCompleted + ") ORDER BY Counter ASC";
                cmd.Parameters.Add("endUTC", SqliteType.Integer).Value = endUTC.Ticks;
                cmd.Parameters.Add("startUTC", SqliteType.Integer).Value = startUTC.Ticks;

                using (var reader = cmd.ExecuteReader())
                {
                    return LoadLog(reader);
                }
            }
        }

        public DateTime LastPalletCycleTime(string pallet)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT TimeUTC FROM stations where Pallet = $pal AND Result = 'PalletCycle' " +
                                     "ORDER BY Counter DESC LIMIT 1";
                cmd.Parameters.Add("pal", SqliteType.Text).Value = pallet;
                
                var date = cmd.ExecuteScalar();
                if (date == null || date == DBNull.Value)
                    return DateTime.MinValue;
                else
                    return new DateTime((long)date, DateTimeKind.Utc);
            }
        }

        //Loads the log for the current pallet cycle, which is all events from the last Result = "PalletCycle"
        public List<MachineWatchInterface.LogEntry> CurrentPalletLog(string pallet)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT MAX(Counter) FROM stations where Pallet = $pal AND Result = 'PalletCycle'";
                cmd.Parameters.Add("pal", SqliteType.Text).Value = pallet;

                var counter = cmd.ExecuteScalar();

                if (counter == DBNull.Value)
                {

                    cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
                        " FROM stations WHERE Pallet = $pal ORDER BY Counter ASC";
                    using (var reader = cmd.ExecuteReader())
                    {
                        return LoadLog(reader);
                    }

                }
                else
                {

                    cmd.CommandText = "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName " +
                        " FROM stations WHERE Pallet = $pal AND Counter > $cntr ORDER BY Counter ASC";
                    cmd.Parameters.Add("cntr", SqliteType.Integer).Value = (long)counter;

                    using (var reader = cmd.ExecuteReader())
                    {
                        return LoadLog(reader);
                    }
                }
            }
        }

        public System.DateTime MaxLogDate()
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT MAX(TimeUTC) FROM stations";

                System.DateTime ret = DateTime.MinValue;

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            ret = new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
                        }
                    }
                }

                return ret;
            }
        }

        public string MaxForeignID()
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();

                cmd.CommandText = "SELECT MAX(ForeignID) FROM stations";
                var maxStat = cmd.ExecuteScalar();

                cmd.CommandText = "SELECT MAX(ForeignID) FROM pendingloads";
                var maxLoad = cmd.ExecuteScalar();

                if (maxStat == DBNull.Value && maxLoad == DBNull.Value)
                    return "";
                else if (maxStat != DBNull.Value && maxLoad == DBNull.Value)
                    return (string)maxStat;
                else if (maxStat == DBNull.Value && maxLoad != DBNull.Value)
                    return (string)maxLoad;
                else
                {
                    var s = (string)maxStat;
                    var l = (string)maxLoad;
                    if (s.CompareTo(l) > 0)
                        return s;
                    else
                        return l;
                }
            }
        }

        public string ForeignIDForCounter(long counter)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT ForeignID FROM stations WHERE Counter = $cntr";
                cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
                var ret = cmd.ExecuteScalar();
                if (ret == DBNull.Value)
                    return "";
                else if (ret == null)
                    return "";
                else
                    return (string)ret;
            }
        }

        public bool CycleExists(MachineWatchInterface.LogEntry cycle)
        {
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM stations WHERE " +
                        "TimeUTC = $time AND Pallet = $pal AND StationLoc = $loc AND StationNum = $locnum AND StationName = $locname";
                    cmd.Parameters.Add("time", SqliteType.Integer).Value = cycle.EndTimeUTC.Ticks;
                    cmd.Parameters.Add("pal", SqliteType.Text).Value = cycle.Pallet;
                    cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)cycle.LogType;
                    cmd.Parameters.Add("locnum", SqliteType.Integer).Value = cycle.LocationNum;
                    cmd.Parameters.Add("locname", SqliteType.Text).Value = cycle.LocationName;

                    var ret = cmd.ExecuteScalar();
                    if (ret == null || Convert.ToInt32(ret) <= 0)
                        return false;
                    else
                        return true;
                }
            }
        }

		public List<MachineWatchInterface.WorkorderSummary> GetWorkorderSummaries(IEnumerable<string> workorders)
		{
			var countQry = @"
				SELECT material.Part, COUNT(material.MaterialID) FROM stations, material, materialid
  					WHERE
   						stations.Counter = material.Counter
   						AND
   						stations.EndOfRoute = 1
   						AND
   						material.MaterialID = materialid.MaterialID
   						AND
   						materialid.Workorder = $workid
                        AND
                        material.Process == material.NumProcess
                    GROUP BY
                        material.Part";

			var serialQry = @"
				SELECT DISTINCT Serial FROM materialid
				    WHERE
					    materialid.Workorder = $workid";

            var finalizedQry = @"
                SELECT MAX(TimeUTC) FROM stations
                    WHERE
                        Pallet = ''
                        AND
                        Result = $workid
                        AND
                        StationLoc = $workloc"; //use the (Pallet, Result) index

            var timeQry = @"
                SELECT Part, StationName, SUM(Elapsed / totcount), SUM(ActiveTime / totcount)
                    FROM
                        (
                            SELECT s.StationName, m.Part, s.Elapsed, s.ActiveTime,
                                 (SELECT COUNT(*) FROM material AS m2 WHERE m2.Counter = s.Counter) totcount
                              FROM stations AS s, material AS m, materialid
                              WHERE
                                s.Counter = m.Counter
                                AND
                                m.MaterialID = materialid.MaterialID
                                AND
                                materialid.Workorder = $workid
                                AND
                                s.Start = 0
                        )
                    GROUP BY Part, StationName";

			using (var countCmd = _connection.CreateCommand())
			using (var serialCmd = _connection.CreateCommand())
            using (var finalizedCmd = _connection.CreateCommand())
            using (var timeCmd = _connection.CreateCommand())
			{
				countCmd.CommandText = countQry;
				countCmd.Parameters.Add("workid", SqliteType.Text);
				serialCmd.CommandText = serialQry;
				serialCmd.Parameters.Add("workid", SqliteType.Text);
                finalizedCmd.CommandText = finalizedQry;
                finalizedCmd.Parameters.Add("workid", SqliteType.Text);
                finalizedCmd.Parameters.Add("workloc", SqliteType.Integer)
                    .Value = (int)MachineWatchInterface.LogType.FinalizeWorkorder;
                timeCmd.CommandText = timeQry;
                timeCmd.Parameters.Add("workid", SqliteType.Text);

				var trans = _connection.BeginTransaction();
				try
				{
					countCmd.Transaction = trans;
					serialCmd.Transaction = trans;
                    finalizedCmd.Transaction = trans;
                    timeCmd.Transaction = trans;

					var ret = new List<MachineWatchInterface.WorkorderSummary>();
                    var partMap = new Dictionary<string, MachineWatchInterface.WorkorderPartSummary>();
					foreach (var w in workorders)
					{
						var summary = new MachineWatchInterface.WorkorderSummary();
						summary.WorkorderId = w;
                        partMap.Clear();

						countCmd.Parameters[0].Value = w;
                        using (var reader = countCmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var wPart = new MachineWatchInterface.WorkorderPartSummary
                                {
                                    Part = reader.GetString(0),
                                    PartsCompleted = reader.GetInt32(1)
                                };
                                summary.Parts.Add(wPart);
                                partMap.Add(wPart.Part, wPart);
                            }
                        }

						serialCmd.Parameters[0].Value = w;
						using (var reader = serialCmd.ExecuteReader())
						{
							while (reader.Read())
								summary.Serials.Add(reader.GetString(0));
						}

                        finalizedCmd.Parameters[0].Value = w;
                        using (var reader = finalizedCmd.ExecuteReader())
                        {
                            if (reader.Read() && !reader.IsDBNull(0))
                            {
                                summary.FinalizedTimeUTC =
                                  new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
                            }
                        }

                        timeCmd.Parameters[0].Value = w;
                        using (var reader = timeCmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var partName = reader.GetString(0);
                                var stat = reader.GetString(1);
                                //part name should exist because material query should return it
                                if (partMap.ContainsKey(partName))
                                {
                                    var detail = partMap[partName];
                                    if (!reader.IsDBNull(2))
                                        detail.ElapsedStationTime[stat] = TimeSpan.FromTicks((long)reader.GetDecimal(2));
                                    if (!reader.IsDBNull(3))
                                        detail.ActiveStationTime[stat] = TimeSpan.FromTicks((long)reader.GetDecimal(3));
                                }
                            }
                        }

						ret.Add(summary);
					}

					trans.Commit();
					return ret;
				}
				catch
				{
					trans.Rollback();
					throw;
				}
			}
		}

        #endregion

        #region Inspection Translate
        //This function is used by clsInspection
        internal string TranslateInspectionCounter(long matID, MachineWatchInterface.JobPlan job, string counter)
        {
            lock (_lock)
            {
                //first, load the counters which contain this MaterialID
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Counter, Process FROM material WHERE MaterialID = $int";
                cmd.Parameters.Add("int", SqliteType.Integer).Value = matID;

                Dictionary<int, List<long>> counters = new Dictionary<int, List<long>>();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        List<long> lst = null;
                        int proc = reader.GetInt32(1);
                        if (counters.ContainsKey(proc))
                        {
                            lst = counters[proc];
                        }
                        else
                        {
                            lst = new List<long>();
                            counters[proc] = lst;
                        }
                        lst.Add(reader.GetInt64(0));
                    }
                }

                cmd.CommandText = "SELECT Pallet, StationLoc, StationNum FROM stations WHERE Counter = $int AND Start = 0";

                string newCounter = counter;

                //now we go through each coutner/each row in the log and try and use the log row
                //to translate some of the wildcards in newProg
                foreach (int proc in counters.Keys)
                {
                    List<long> lst = counters[proc];
                    lst.Sort();

                    bool foundLoad = false;
                    bool foundMach = false;
                    var stops = new StationsOnPath[job.GetNumPaths(proc)];
                    for (int path = 1; path <= job.GetNumPaths(proc); path++)
                        stops[path - 1] = new StationsOnPath(job, proc, path);

                    foreach (long ctr in lst)
                    {
                        cmd.Parameters[0].Value = ctr;
                        using (var reader = cmd.ExecuteReader())
                        {

                            if (reader.Read())
                            {
                                //for each log entry, we search for a matching route stop in the job
                                //if we find one, we replace the counter in the program
                                string pal = reader.GetString(0);
                                MachineWatchInterface.PalletLocationTypeEnum palLoc = (MachineWatchInterface.PalletLocationTypeEnum)reader.GetInt32(1);
                                int statNum = reader.GetInt32(2);

                                newCounter = newCounter.Replace(MachineWatchInterface.JobInspectionData.PalletFormatFlag(proc), pal);

                                if (palLoc == MachineWatchInterface.PalletLocationTypeEnum.LoadUnload)
                                {
                                    if (!foundLoad && !foundMach)
                                    {
                                        newCounter = newCounter.Replace(MachineWatchInterface.JobInspectionData.LoadFormatFlag(proc),
                                                                        statNum.ToString());
                                        foundLoad = true;
                                    }
                                    else
                                    {
                                        newCounter = newCounter.Replace(MachineWatchInterface.JobInspectionData.UnloadFormatFlag(proc),
                                                                        statNum.ToString());
                                    }

                                }
                                else if (palLoc == MachineWatchInterface.PalletLocationTypeEnum.Machine)
                                {
                                    foundMach = true;
                                    for (int i = 0; i < stops.Length; i++)
                                        stops[i].VisitStation(statNum);
                                }
                            }
                        }
                    }

                    for (int path = 1; path <= job.GetNumPaths(proc); path++)
                    {
                        if (stops[path - 1].MatchingRoute)
                        {
                            newCounter = stops[path - 1].ReplaceCounter(newCounter);
                        }
                    }
                }

                return newCounter;
            }
        }

        private class StationsOnPath
        {
            public StationsOnPath(MachineWatchInterface.JobPlan job, int proc, int path)
            {
                _job = job;
                _proc = proc;
                _stopEnum = _job.GetMachiningStop(proc, path).GetEnumerator();
                _stopIndex = 0;
                _matchingRoute = true;
                _replacements = new List<Replacement>();
            }

            public bool MatchingRoute
            {
                get { return _matchingRoute; }
            }

            public void VisitStation(int stat)
            {
                while (_stopEnum.MoveNext())
                {
                    _stopIndex += 1;

                    if (CollContains(_stopEnum.Current.Stations(), stat))
                    {
                        _replacements.Add(new Replacement(_stopIndex, stat));
                        return;
                    }
                }

                //we ran out of stops
                _matchingRoute = false;
            }

            public string ReplaceCounter(string counter)
            {
                if (!_matchingRoute) return counter;

                foreach (var s in _replacements)
                    counter = counter.Replace(MachineWatchInterface.JobInspectionData.StationFormatFlag(_proc, s.stopNum),
                                              s.stat.ToString());
                return counter;
            }

            private bool CollContains(IEnumerable<int> coll, int val)
            {
                foreach (int v in coll)
                {
                    if (v.Equals(val))
                    {
                        return true;
                    }
                }
                return false;
            }

            private MachineWatchInterface.JobPlan _job;
            private int _proc;

            private IEnumerator<MachineWatchInterface.JobMachiningStop> _stopEnum;
            private int _stopIndex;
            private bool _matchingRoute;

            private struct Replacement
            {
                public int stopNum;
                public int stat;
                public Replacement(int st, int sta)
                {
                    stopNum = st;
                    stat = sta;
                }
            }
            private List<Replacement> _replacements;
        }
        #endregion

        #region Adding
        public void AddLogEntry(MachineWatchInterface.LogEntry log)
        {
            AddStationCycle(log, null, null);
        }

        public void AddStationCycle(MachineWatchInterface.LogEntry log, string foreignID)
        {
            AddStationCycle(log, foreignID, null);
        }

        public void AddStationCycle(MachineWatchInterface.LogEntry log, string foreignID, string origMessage)
        {
            lock (_lock)
            {
                var trans = _connection.BeginTransaction();

                try
                {
                    AddStationCycle(trans, log, foreignID, origMessage);
                    trans.Commit();
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }

            if (NewStationCycle != null)
                NewStationCycle(log, foreignID);
        }

        public void AddStationCycles(IEnumerable<MachineWatchInterface.LogEntry> logs, string foreignID, string origMessage)
        {
            lock (_lock)
            {
                var trans = _connection.BeginTransaction();

                try
                {
                    foreach (var log in logs)
                        AddStationCycle(trans, log, foreignID, origMessage);
                    trans.Commit();
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }

            if (NewStationCycle != null)
                foreach (var log in logs)
                    NewStationCycle(log, foreignID);
        }

        public void AddStationCycle(IDbTransaction trans, MachineWatchInterface.LogEntry log, string foreignID, string origMessage)
        {
            var cmd = _connection.CreateCommand();
            ((IDbCommand)cmd).Transaction = trans;

            cmd.CommandText = "INSERT INTO stations(Pallet, StationLoc, StationName, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, ForeignID,OriginalMessage)" +
                "VALUES ($pal,$loc,$locname,$locnum,$prog,$start,$time,$result,$end,$elapsed,$active,$foreign,$orig)";

            cmd.Parameters.Add("pal", SqliteType.Text).Value = log.Pallet;
            cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)log.LogType;
            cmd.Parameters.Add("locname", SqliteType.Text).Value = log.LocationName;
            cmd.Parameters.Add("locnum", SqliteType.Integer).Value = log.LocationNum;
            cmd.Parameters.Add("prog", SqliteType.Text).Value = log.Program;
            cmd.Parameters.Add("start", SqliteType.Integer).Value = log.StartOfCycle;
            cmd.Parameters.Add("time", SqliteType.Integer).Value = log.EndTimeUTC.Ticks;
            cmd.Parameters.Add("result", SqliteType.Text).Value = log.Result;
            cmd.Parameters.Add("end", SqliteType.Integer).Value = log.EndOfRoute;
            if (log.ElapsedTime.Ticks >= 0)
                cmd.Parameters.Add("elapsed", SqliteType.Integer).Value = log.ElapsedTime.Ticks;
            else
                cmd.Parameters.Add("elapsed", SqliteType.Integer).Value = DBNull.Value;
            if (log.ActiveOperationTime.Ticks > 0)
                cmd.Parameters.Add("active", SqliteType.Integer).Value = log.ActiveOperationTime.Ticks;
            else
                cmd.Parameters.Add("active", SqliteType.Integer).Value = DBNull.Value;
            if (foreignID == null || foreignID == "")
                cmd.Parameters.Add("foreign", SqliteType.Text).Value = DBNull.Value;
            else
                cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;
            if (origMessage == null || origMessage == "")
                cmd.Parameters.Add("orig", SqliteType.Text).Value = DBNull.Value;
            else
                cmd.Parameters.Add("orig", SqliteType.Text).Value = origMessage;

            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid()";
            cmd.Parameters.Clear();
            long ctr = (long)cmd.ExecuteScalar();

            AddMaterial(ctr, log.Material, trans);
            AddProgramDetail(ctr, log.ProgramDetails, trans);

        }

        private void AddMaterial(long counter, IEnumerable<MachineWatchInterface.LogMaterial> mat, IDbTransaction trans)
        {
            var cmd = _connection.CreateCommand();
            ((IDbCommand)cmd).Transaction = trans;

            cmd.CommandText = "INSERT INTO material(Counter,MaterialID,UniqueStr,Process,Part,NumProcess,Face)" +
           "VALUES($cntr,$mat,$unique,$proc,$part,$numproc,$face)";
            cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
            cmd.Parameters.Add("mat", SqliteType.Integer);
            cmd.Parameters.Add("unique", SqliteType.Text);
            cmd.Parameters.Add("proc", SqliteType.Integer);
            cmd.Parameters.Add("part", SqliteType.Text);
            cmd.Parameters.Add("numproc", SqliteType.Integer);
            cmd.Parameters.Add("face", SqliteType.Text);

            foreach (var m in mat)
            {
                cmd.Parameters[1].Value = m.MaterialID;
                cmd.Parameters[2].Value = m.JobUniqueStr;
                cmd.Parameters[3].Value = m.Process;
                cmd.Parameters[4].Value = m.PartName;
                cmd.Parameters[5].Value = m.NumProcesses;
                cmd.Parameters[6].Value = m.Face;
                cmd.ExecuteNonQuery();
            }
        }

        private void AddProgramDetail(long counter, IDictionary<string, string> details, IDbTransaction trans)
        {
            var cmd = _connection.CreateCommand();
            ((IDbCommand)cmd).Transaction = trans;

            cmd.CommandText = "INSERT INTO program_details(Counter,Key,Value) VALUES($cntr,$key,$val)";
            cmd.Parameters.Add("cntr", SqliteType.Integer).Value = counter;
            cmd.Parameters.Add("key", SqliteType.Text);
            cmd.Parameters.Add("val", SqliteType.Text);

            foreach (var pair in details)
            {
                cmd.Parameters[1].Value = pair.Key;
                cmd.Parameters[2].Value = pair.Value;
                cmd.ExecuteNonQuery();
            }
        }
        #endregion

        #region Material Tags

        public long AllocateMaterialID(string unique)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = "INSERT INTO materialid(UniqueStr) VALUES ($uniq)";
                cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
                cmd.ExecuteNonQuery();
                cmd.CommandText = "SELECT last_insert_rowid()";
                cmd.Parameters.Clear();
                return (long)cmd.ExecuteScalar();
            }
        }

        public string JobUniqueStrFromMaterialID(long matID)
        {
            lock (_lock)
            {
                string unique = "";

                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT UniqueStr FROM materialid WHERE MaterialID = $mat";
                cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        unique = reader.GetString(0);
                    }
                }

                return unique;
            }
        }


        public MachineWatchInterface.LogEntry RecordSerialForMaterialID(MachineWatchInterface.LogMaterial mat, string serial)
        {
            return RecordSerialForMaterialID(mat, serial, DateTime.UtcNow);
        }

        public MachineWatchInterface.LogEntry RecordSerialForMaterialID(MachineWatchInterface.LogMaterial mat, string serial, DateTime endTimeUTC)
        {
            var log = new MachineWatchInterface.LogEntry(-1,
                                                             new MachineWatchInterface.LogMaterial[] { mat },
                                                             "",
                                                             MachineWatchInterface.LogType.PartMark, "Mark", 1,
                                                             "MARK",
                                                             false,
                                                             endTimeUTC,
                                                             serial,
                                                             false);
            lock (_lock)
            {
                var trans = _connection.BeginTransaction();
                try
                {
                    RecordSerialForMaterialID(trans, mat.MaterialID, serial);
                    AddStationCycle(trans, log, "", "");
                    trans.Commit();
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }
            if (NewStationCycle != null)
                NewStationCycle(log, "");
            return log;
        }

        private void RecordSerialForMaterialID(IDbTransaction trans, long matID, string serial)
        {
            var cmd = _connection.CreateCommand();
            ((IDbCommand)cmd).Transaction = trans;
            cmd.CommandText = "UPDATE materialid SET Serial = $ser WHERE MaterialID = $mat";
            if (string.IsNullOrEmpty(serial))
                cmd.Parameters.Add("ser", SqliteType.Text).Value = DBNull.Value;
            else
                cmd.Parameters.Add("ser", SqliteType.Text).Value = serial;
            cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
            cmd.ExecuteNonQuery();
        }

        public string SerialForMaterialID(long matID)
        {
            lock (_lock)
            {
                string ser = "";

                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Serial FROM materialid WHERE MaterialID = $mat";
                cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                            ser = reader.GetString(0);
                    }
                }

                return ser;
            }
        }

        public MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(MachineWatchInterface.LogMaterial mat, string workorder)
        {
            return RecordWorkorderForMaterialID(mat, workorder, DateTime.UtcNow);
        }

        public MachineWatchInterface.LogEntry RecordWorkorderForMaterialID(MachineWatchInterface.LogMaterial mat, string workorder, DateTime recordUtc)
        {
                var log = new MachineWatchInterface.LogEntry(-1,
                                                             new MachineWatchInterface.LogMaterial[] { mat },
                                                             "",
                                                             MachineWatchInterface.LogType.OrderAssignment, "Order", 1,
                                                             "",
                                                             false,
                                                             recordUtc,
                                                             workorder,
                                                             false);
            lock (_lock)
            {
                var trans = _connection.BeginTransaction();
                try
                {
                    RecordWorkorderForMaterialID(trans, mat.MaterialID, workorder);
                    AddStationCycle(trans, log, "", "");
                    trans.Commit();
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }
            if (NewStationCycle != null)
                NewStationCycle(log, "");
            return log;
        }

        private void RecordWorkorderForMaterialID(IDbTransaction trans, long matID, string workorder)
        {
            var cmd = _connection.CreateCommand();
            ((IDbCommand)cmd).Transaction = trans;
            cmd.CommandText = "UPDATE materialid SET Workorder = $work WHERE MaterialID = $mat";
            if (string.IsNullOrEmpty(workorder))
                cmd.Parameters.Add("work", SqliteType.Text).Value = DBNull.Value;
            else
                cmd.Parameters.Add("work", SqliteType.Text).Value = workorder;
            cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
            cmd.ExecuteNonQuery();
        }

        public MachineWatchInterface.LogEntry RecordFinalizedWorkorder(string workorder)
        {
            return RecordFinalizedWorkorder(workorder, DateTime.UtcNow);
        }

        public MachineWatchInterface.LogEntry RecordFinalizedWorkorder(string workorder, DateTime finalizedUTC)
        {
            var log = new MachineWatchInterface.LogEntry(
                cntr: -1,
                mat: new MachineWatchInterface.LogMaterial[] {},
                pal: "",
                ty: MachineWatchInterface.LogType.FinalizeWorkorder,
                locName: "FinalizeWorkorder",
                locNum: 1,
                prog: "",
                start: false,
                endTime: finalizedUTC,
                result: workorder,
                endOfRoute: false);
            lock (_lock)
            {
                var trans = _connection.BeginTransaction();
                try
                {
                    AddStationCycle(trans, log, "", "");
                    trans.Commit();
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }
            if (NewStationCycle != null)
                NewStationCycle(log, "");
            return log;
        }

        #endregion

        #region Pending Loads

        public void AddPendingLoad(string pal, string key, int load, TimeSpan elapsed, TimeSpan active, string foreignID)
        {
            lock (_lock)
            {
                var trans = _connection.BeginTransaction();

                try
                {
                    var cmd = _connection.CreateCommand();
                    cmd.Transaction = trans;

                    cmd.CommandText = "INSERT INTO pendingloads(Pallet, Key, LoadStation, Elapsed, ActiveTime, ForeignID)" +
                        "VALUES ($pal,$key,$load,$elapsed,$active,$foreign)";

                    cmd.Parameters.Add("pal", SqliteType.Text).Value = pal;
                    cmd.Parameters.Add("key", SqliteType.Text).Value = key;
                    cmd.Parameters.Add("load", SqliteType.Integer).Value = load;
                    cmd.Parameters.Add("elapsed", SqliteType.Integer).Value = elapsed.Ticks;
                    cmd.Parameters.Add("active", SqliteType.Integer).Value = active.Ticks;
                    cmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;

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

        public struct PendingLoad
        {
            public string Pallet;
            public string Key;
            public int LoadStation;
            public TimeSpan Elapsed;
            public TimeSpan ActiveOperationTime;
            public string ForeignID;
        }

        public List<PendingLoad> PendingLoads(string pallet)
        {
            lock (_lock)
            {
                var ret = new List<PendingLoad>();

                var trans = _connection.BeginTransaction();
                try
                {
                    var cmd = _connection.CreateCommand();
                    cmd.Transaction = trans;

                    cmd.CommandText = "SELECT Key, LoadStation, Elapsed, ActiveTime, ForeignID FROM pendingloads WHERE Pallet = $pal";
                    cmd.Parameters.Add("pal", SqliteType.Text).Value = pallet;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var p = default(PendingLoad);
                            p.Pallet = pallet;
                            p.Key = reader.GetString(0);
                            p.LoadStation = reader.GetInt32(1);
                            p.Elapsed = new TimeSpan(reader.GetInt64(2));
                            if (!reader.IsDBNull(3))
                                p.ActiveOperationTime = TimeSpan.FromTicks(reader.GetInt64(3));
                            p.ForeignID = reader.GetString(4);
                            ret.Add(p);
                        }
                    }

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

        private MachineWatchInterface.SerialSettings _serialSettings =
            new MachineWatchInterface.SerialSettings(MachineWatchInterface.SerialType.NoSerials, 10);

        public void CompletePalletCycle(string pal, DateTime timeUTC, string foreignID)
        {
            CompletePalletCycle(pal, timeUTC, foreignID, null);
        }

        public void CompletePalletCycle(string pal, DateTime timeUTC, string foreignID,
                                        IDictionary<string, IEnumerable<MachineWatchInterface.LogMaterial>> mat)
        {
            lock (_lock)
            {
                var trans = _connection.BeginTransaction();

                try
                {

                    var lastTimeCmd = _connection.CreateCommand();
                    lastTimeCmd.CommandText = "SELECT TimeUTC FROM stations where Pallet = $pal AND Result = 'PalletCycle' " +
                                            "ORDER BY Counter DESC LIMIT 1";
                    lastTimeCmd.Parameters.Add("pal", SqliteType.Text).Value = pal;
                
                    var elapsedTime = TimeSpan.Zero;
                    var lastCycleTime = lastTimeCmd.ExecuteScalar();
                    if (lastCycleTime != null && lastCycleTime != DBNull.Value)
                        elapsedTime = timeUTC.Subtract(new DateTime((long)lastCycleTime, DateTimeKind.Utc));

                    // Add the pallet cycle
                    var addCmd = _connection.CreateCommand();
                    addCmd.Transaction = trans;
                    addCmd.CommandText = "INSERT INTO stations(Pallet, StationLoc, StationName, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, ForeignID)" +
                        "VALUES ($pal,$loc,'Pallet Cycle',1,'',0,$time,'PalletCycle',0,$elapsed,NULL,$foreign)";
                    addCmd.Parameters.Add("pal", SqliteType.Text).Value = pal;
                    addCmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)MachineWatchInterface.LogType.PalletCycle;
                    addCmd.Parameters.Add("time", SqliteType.Integer).Value = timeUTC.Ticks;
                    addCmd.Parameters.Add("foreign", SqliteType.Text).Value = foreignID;
                    addCmd.Parameters.Add("elapsed", SqliteType.Integer).Value = elapsedTime.Ticks;
                    addCmd.ExecuteNonQuery();

                    if (mat == null)
                    {
                        trans.Commit();
                        return;
                    }

                    // Copy over pending loads
                    var loadPending = _connection.CreateCommand();
                    loadPending.Transaction = trans;
                    loadPending.CommandText = "SELECT Key, LoadStation, Elapsed, ActiveTime, ForeignID FROM pendingloads WHERE Pallet = $pal";
                    loadPending.Parameters.Add("pal", SqliteType.Text).Value = pal;

                    addCmd.CommandText = "INSERT INTO stations(Pallet, StationLoc, StationName, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, ForeignID)" +
                        "VALUES ($pal," + ((int)MachineWatchInterface.LogType.LoadUnloadCycle).ToString() +
                                ",'Load',$load,'',0,$time,'LOAD',0,$elapsed,$active,$foreign)";
                    addCmd.Parameters.Clear();
                    addCmd.Parameters.Add("pal", SqliteType.Text).Value = pal;
                    addCmd.Parameters.Add("load", SqliteType.Integer);
                    addCmd.Parameters.Add("time", SqliteType.Integer).Value = timeUTC.AddSeconds(1).Ticks;
                    addCmd.Parameters.Add("elapsed", SqliteType.Integer);
                    addCmd.Parameters.Add("active", SqliteType.Integer);
                    addCmd.Parameters.Add("foreign", SqliteType.Text);

                    var addSerial = _connection.CreateCommand();
                    addSerial.Transaction = trans;
                    addSerial.CommandText = "INSERT INTO stations(Pallet, StationLoc, StationName, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, ForeignID)" +
                        "VALUES ($pal," + ((int)MachineWatchInterface.LogType.PartMark).ToString() +
                                ",'Mark',1,'MARK',0,$time,$result,0,NULL,NULL,NULL)";
                    addSerial.Parameters.Add("pal", SqliteType.Text).Value = pal;
                    addSerial.Parameters.Add("time", SqliteType.Integer).Value = timeUTC.AddSeconds(2).Ticks;
                    addSerial.Parameters.Add("result", SqliteType.Text); // result

                    var lastRowid = _connection.CreateCommand();
                    lastRowid.Transaction = trans;
                    lastRowid.CommandText = "SELECT last_insert_rowid()";


                    using (var reader = loadPending.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var key = reader.GetString(0);
                            if (mat.ContainsKey(key))
                            {
                                addCmd.Parameters[1].Value = reader.GetInt32(1); // load
                                addCmd.Parameters[3].Value = reader.GetInt64(2); //elapsed
                                if (reader.IsDBNull(3))
                                    addCmd.Parameters[4].Value = DBNull.Value; //active
                                else
                                    addCmd.Parameters[4].Value = reader.GetInt64(3); //active
                                addCmd.Parameters[5].Value = reader.GetString(4); //foreign
                                addCmd.ExecuteNonQuery();

                                long ctr = (long)lastRowid.ExecuteScalar();

                                AddMaterial(ctr, mat[key], trans);

                                if (_serialSettings.SerialType == MachineWatchInterface.SerialType.OneSerialPerCycle)
                                {

                                    // find a material id to use to create the serial
                                    long matID = -1;
                                    foreach (var m in mat[key])
                                    {
                                        if (m.MaterialID >= 0)
                                        {
                                            matID = m.MaterialID;
                                            break;
                                        }
                                    }
                                    if (matID >= 0)
                                    {
                                        // add the serial
                                        var serial = ConvertToBase62(matID);
                                        serial = serial.Substring(0, Math.Min(_serialSettings.SerialLength, serial.Length));
                                        serial = serial.PadLeft(_serialSettings.SerialLength, '0');
                                        addSerial.Parameters[2].Value = serial;
                                        addSerial.ExecuteNonQuery();
                                        foreach (var m in mat[key])
                                        {
                                            if (m.MaterialID >= 0)
                                                RecordSerialForMaterialID(trans, m.MaterialID, serial);
                                        }
                                        ctr = (long)lastRowid.ExecuteScalar();
                                        AddMaterial(ctr, mat[key], trans);
                                    }

                                }
                                else if (_serialSettings.SerialType == MachineWatchInterface.SerialType.OneSerialPerMaterial)
                                {
                                    foreach (var m in mat[key])
                                    {
                                        if (m.MaterialID < 0) continue;
                                        var serial = ConvertToBase62(m.MaterialID);
                                        serial = serial.Substring(0, Math.Min(_serialSettings.SerialLength, serial.Length));
                                        serial = serial.PadLeft(_serialSettings.SerialLength, '0');
                                        addSerial.Parameters[2].Value = serial;
                                        addSerial.ExecuteNonQuery();
                                        RecordSerialForMaterialID(trans, m.MaterialID, serial);
                                        ctr = (long)lastRowid.ExecuteScalar();
                                        AddMaterial(ctr, new MachineWatchInterface.LogMaterial[] { m }, trans);
                                    }

                                }
                            }
                        }
                    }

                    addCmd.CommandText = "DELETE FROM pendingloads WHERE Pallet = $pal";
                    addCmd.Parameters.Clear();
                    addCmd.Parameters.Add("pal", SqliteType.Text).Value = pal;
                    addCmd.ExecuteNonQuery();

                    trans.Commit();
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }
        }

        private void LoadSettings()
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT SerialType, SerialLength, DepositProc, FilenameTemplate, ProgramTemplate FROM sersettings WHERE ID = 0";
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    var serialType = (MachineWatchInterface.SerialType)reader.GetInt32(0);
                    var serialLength = reader.GetInt32(1);
                    if (serialType == MachineWatchInterface.SerialType.SerialDeposit)
                    {
                        _serialSettings = new MachineWatchInterface.SerialSettings(
                            serialLength,
                            reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            reader.IsDBNull(3) ? null : reader.GetString(3),
                            reader.IsDBNull(4) ? null : reader.GetString(4)
                        );
                    } else {
                        _serialSettings = new MachineWatchInterface.SerialSettings(serialType, serialLength);
                    }
                }
            }
        }

        public MachineWatchInterface.SerialSettings GetSerialSettings()
        {
            lock (_lock)
            {
                return _serialSettings;
            }
        }

        public void SetSerialSettings(MachineWatchInterface.SerialSettings s)
        {
            lock(_lock)
            {
                var trans = _connection.BeginTransaction();
                try
                {
                    var cmd = _connection.CreateCommand();
                    cmd.Transaction = trans;
                    cmd.CommandText = "INSERT OR REPLACE INTO sersettings(ID, SerialType, SerialLength,DepositProc,FilenameTemplate,ProgramTemplate) VALUES (0,$ty,$len,$proc,$file,$prog)";
                    cmd.Parameters.Add("ty", SqliteType.Integer).Value = (int)s.SerialType;
                    cmd.Parameters.Add("len", SqliteType.Integer).Value = s.SerialLength;
                    cmd.Parameters.Add("proc", SqliteType.Integer).Value = s.DepositOnProcess;
                    if (string.IsNullOrEmpty(s.FilenameTemplate))
                        cmd.Parameters.Add("file", SqliteType.Text).Value = DBNull.Value;
                    else
                        cmd.Parameters.Add("file", SqliteType.Text).Value = s.FilenameTemplate;
                    if (string.IsNullOrEmpty(s.ProgramTemplate))
                        cmd.Parameters.Add("prog", SqliteType.Text).Value = DBNull.Value;
                    else
                        cmd.Parameters.Add("prog", SqliteType.Text).Value = s.ProgramTemplate;
                    cmd.ExecuteNonQuery();
                    trans.Commit();

                    _serialSettings = s;
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }
        }

        public static string ConvertToBase62(long num)
        {
            string baseChars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

            string res = "";
            long cur = num;

            while (cur > 0)
            {
                long quotient = cur / 62;
                int remainder = (int)cur % 62;

                res = baseChars[remainder] + res;
                cur = quotient;
            }

            return res;
        }
        #endregion
    }
}
