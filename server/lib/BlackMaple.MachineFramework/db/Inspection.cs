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
using System.Collections;
using Generic = System.Collections.Generic;
using IO = System.IO;
using System.Data;
using Microsoft.Data.Sqlite;

using BlackMaple.MachineWatchInterface;

namespace BlackMaple.MachineFramework
{

    public class InspectionDB : IInspectionControl
    {
        //This class holds helper code to implement the inspection counter table
        //to implement the running of Inspection programs.  The counts are stored in a sqlite db.

        #region Database
        private SqliteConnection _connection;
        private Random _rand = new Random();
        private object _lock = new object();
        private JobLogDB jobLog;

        public InspectionDB(JobLogDB log)
        {
            jobLog = log;
        }

        public InspectionDB(JobLogDB log, SqliteConnection conn)
            : this(log)
        {
            _connection = conn;
        }

        public void Open(string filename)
        {
            if (IO.File.Exists(filename))
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
                    IO.File.Delete(filename);
                    throw;
                }
            }
        }

        public void Close()
        {
            _connection.Close();
        }


        private const int Version = 5;
        public void CreateTables()
        {
            var cmd = _connection.CreateCommand();

            cmd.CommandText = "CREATE TABLE version(ver INTEGER)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO version VALUES(" + Version.ToString() + ")";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE counters(Counter TEXT PRIMARY KEY, Val INTEGER, LastUTC INTEGER)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE forced(MaterialID INTEGER, InspType TEXT, PRIMARY KEY(MaterialID, InspType))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE decisions(MaterialID INTEGER, InspType TEXT, Counter TEXT, Inspect INTEGER, CreateUTC INTEGER, PRIMARY KEY(MaterialID, InspType))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE next_piece(StatType INTEGER, StatNum INTEGER, InspType TEXT, PRIMARY KEY(StatType,StatNum, InspType))";
            cmd.ExecuteNonQuery();
        }

        private void UpdateTables()
        {
            var cmd = _connection.CreateCommand();

            cmd.CommandText = "SELECT ver FROM version";
            long ver = (long)cmd.ExecuteScalar();

            if (ver == Version) return;

            var trans = _connection.BeginTransaction();

            try
            {

                if (ver < 1)
                    Ver0ToVer1(trans);

                if (ver < 2)
                    Ver1ToVer2(trans);

                if (ver < 3)
                    Ver2ToVer3(trans);

                if (ver < 4)
                    Ver3ToVer4(trans);

                if (ver < 5) Ver4ToVer5(trans);

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

        private void Ver0ToVer1(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;

            cmd.CommandText = "CREATE TABLE next_piece(StatType INTEGER, StatNum INTEGER, InspType TEXT, PRIMARY KEY(StatType,StatNum, InspType))";
            cmd.ExecuteNonQuery();
        }

        private void Ver1ToVer2(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;

            // delete all counters because we changed the counter format in the download and also added
            // code to treat empty counters as a random value between 0 and maxVal
            cmd.CommandText = "DELETE FROM counters";
            cmd.ExecuteNonQuery();
        }

        private void Ver2ToVer3(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;

            cmd.CommandText = "ALTER TABLE counters ADD LastUTC INTEGER";
            cmd.ExecuteNonQuery();
        }

        private void Ver3ToVer4(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;

            cmd.CommandText = "CREATE TABLE global_types(InspType TEXT PRIMARY KEY," +
              " TrackPart INTEGER, TrackPallet INTEGER, TrackStation INTEGER, SingleProc INTEGER, " +
              " MaxCount INTEGER, RandomFreq INTEGER, Deadline INTEGER)";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE global_override(InspType TEXT, Part TEXT, " +
              " MaxCount INTEGER, RandomFreq INTEGER, Deadline INTEGER, " +
              " PRIMARY KEY (InspType, Part))";
            cmd.ExecuteNonQuery();
        }

        private void Ver4ToVer5(IDbTransaction trans)
        {
            IDbCommand cmd = _connection.CreateCommand();
            cmd.Transaction = trans;

            cmd.CommandText = "DROP TABLE global_types";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "DROP TABLE global_override";
            cmd.ExecuteNonQuery();
        }
        #endregion

        #region Inspection Decisions
        //This function returns true if the material should be inspected.
        public bool MakeInspectionDecision(
            long matID,
            JobPlan job,
            JobInspectionData iProg,
            DateTime? mutcNow = null)
        {
            var counter = jobLog.TranslateInspectionCounter(matID, job, iProg.Counter);

            return MakeInspectionDecision(matID,
                                          job.UniqueStr,
                                          job.PartName,
                                          job.NumProcesses,
                                          iProg,
                                          counter,
                                          mutcNow);
        }
        public bool MakeInspectionDecision(long matID,
                                           string unique,
                                           string partName,
                                           int numProcesses,
                                           JobInspectionData iProg,
                                           string counter,
                                           DateTime? mutcNow = null)
        {
            var utcNow = mutcNow.HasValue ? mutcNow.Value : DateTime.UtcNow;
            LogEntry log = null;
            bool inspect = false;

            lock (_lock)
            {
                foreach (Decision d in LookupInspectionDecisions(matID))
                {
                    if (d.InspType == iProg.InspectionType)
                    {
                        return d.Inspect;
                    }
                }

                var currentCount = QueryCount(counter, iProg.MaxVal);

                if (iProg.MaxVal > 0)
                {
                    currentCount.Value += 1;

                    if (currentCount.Value >= iProg.MaxVal)
                    {
                        currentCount.Value = 0;
                        inspect = true;
                    }
                }
                else if (iProg.RandomFreq > 0)
                {
                    if (_rand.NextDouble() < iProg.RandomFreq)
                        inspect = true;
                }

                //now check lastutc
                if (iProg.TimeInterval > TimeSpan.Zero &&
                    currentCount.LastUTC != DateTime.MaxValue &&
                    currentCount.LastUTC.Add(iProg.TimeInterval) < utcNow)
                {
                    inspect = true;
                }

                //lastly, check forced inspection
                foreach (string iType in LookupForcedInspection(matID))
                {
                    if (iProg.InspectionType == iType)
                    {
                        inspect = true;
                    }
                }

                //update lastutc if there is an inspection
                if (inspect)
                    currentCount.LastUTC = utcNow;

                //if no lastutc has been recoreded, record the current time.
                if (currentCount.LastUTC == DateTime.MaxValue)
                    currentCount.LastUTC = utcNow;

                LogMaterial mat =
                    new LogMaterial(matID, unique, numProcesses, partName, -1);

                log = new LogEntry(1,
                    new LogMaterial[] { mat },
                    "", //pallet
                    LogType.Inspection, "Inspect", 1,
                    counter,
                    false, utcNow, inspect.ToString(), false);

                StoreInspectionDecision(counter, currentCount.Value, currentCount.LastUTC,
                                        matID, iProg.InspectionType, inspect, utcNow);

            }

            //do this outside the lock
            if (log != null)
            {
                jobLog.AddLogEntry(log);
            }

            return inspect;
        }

        public struct Decision
        {
            public long MaterialID;
            public string InspType;
            public string Counter;
            public bool Inspect;

            public System.DateTime CreateUTC;
        }
        public Generic.IList<Decision> LookupInspectionDecisions(long matID)
        {
            lock (_lock)
            {
                Generic.List<Decision> ret = new Generic.List<Decision>();

                var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT InspType, Counter, Inspect, CreateUTC FROM decisions " +
                    "WHERE MaterialID = $mat";
                cmd.Parameters.Add("$mat", SqliteType.Integer).Value = matID;

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0) && !reader.IsDBNull(1) &&
                            !reader.IsDBNull(2) && !reader.IsDBNull(3))
                        {
                            Decision d = new Decision();
                            d.MaterialID = matID;
                            d.InspType = reader.GetString(0);
                            d.Counter = reader.GetString(1);
                            d.Inspect = reader.GetBoolean(2);
                            d.CreateUTC = new DateTime(reader.GetInt64(3), DateTimeKind.Utc);
                            ret.Add(d);
                        }
                    }
                }

                return ret;
            }
        }

        public void ForceInspection(long matID, string inspType)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();

                cmd.CommandText = "INSERT OR REPLACE INTO forced(MaterialID,InspType) VALUES ($mat,$insp)";
                cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
                cmd.Parameters.Add("insp", SqliteType.Text).Value = inspType;

                cmd.ExecuteNonQuery();
            }
        }

        public void NextPieceInspection(PalletLocation palLoc, string inspType)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();

                cmd.CommandText = "INSERT OR REPLACE INTO next_piece(StatType, StatNum, InspType)" +
                    " VALUES ($loc,$locnum,$insp)";
                cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)palLoc.Location;
                cmd.Parameters.Add("locnum", SqliteType.Integer).Value = palLoc.Num;
                cmd.Parameters.Add("insp", SqliteType.Text).Value = inspType;

                cmd.ExecuteNonQuery();
            }
        }

        public void CheckMaterialForNextPeiceInspection(PalletLocation palLoc, long matID)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();
                var cmd2 = _connection.CreateCommand();

                cmd.CommandText = "SELECT InspType FROM next_piece WHERE StatType = $loc AND StatNum = $locnum";
                cmd.Parameters.Add("loc", SqliteType.Integer).Value = (int)palLoc.Location;
                cmd.Parameters.Add("locnum", SqliteType.Integer).Value = palLoc.Num;

                cmd2.CommandText = "INSERT OR REPLACE INTO forced(MaterialID,InspType) VALUES ($mat,$insp)";
                cmd2.Parameters.Add("mat", SqliteType.Integer);
                cmd2.Parameters.Add("insp", SqliteType.Text);


                var trans = _connection.BeginTransaction();
                try
                {
                    cmd.Transaction = trans;
                    cmd2.Transaction = trans;

                    IDataReader reader = cmd.ExecuteReader();
                    try
                    {

                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                cmd2.Parameters[0].Value = matID;
                                cmd2.Parameters[1].Value = reader.GetString(0);
                                cmd2.ExecuteNonQuery();
                            }
                        }

                    }
                    finally
                    {
                        reader.Close();
                    }

                    cmd.CommandText = "DELETE FROM next_piece WHERE StatType = $loc AND StatNum = $locnum";
                    //keep the same parameters as above
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


        private InspectCount QueryCount(string counter, int maxVal)
        {
            InspectCount cnt = new InspectCount();
            cnt.Counter = counter;

            var cmd = _connection.CreateCommand();

            cmd.CommandText = "SELECT Val, LastUTC FROM counters WHERE Counter = $cntr";
            cmd.Parameters.Add("cntr", SqliteType.Text).Value = counter;

            using (IDataReader reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    cnt.Value = reader.GetInt32(0);
                    if (reader.IsDBNull(1))
                        cnt.LastUTC = DateTime.MaxValue;
                    else
                        cnt.LastUTC = new DateTime(reader.GetInt64(1), DateTimeKind.Utc);

                }
                else
                {
                    if (maxVal <= 1)
                        cnt.Value = 0;
                    else
                        cnt.Value = _rand.Next(0, maxVal - 1);

                    cnt.LastUTC = DateTime.MaxValue;
                }
            }

            return cnt;
        }

        private Generic.IList<string> LookupForcedInspection(long matID)
        {
            Generic.List<string> ret = new Generic.List<string>();

            var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT InspType FROM forced WHERE MaterialID = $mat";
            cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                    {
                        ret.Add(reader.GetString(0));
                    }
                }
            }

            return ret;
        }

        private void StoreInspectionDecision(string counter, int newCounterVal, DateTime newLastUTC,
                                             long matID, string inspType, bool status, DateTime utcNow)
        {
            var cmd = _connection.CreateCommand();

            var trans = _connection.BeginTransaction();
            try
            {
                cmd.Transaction = trans;
                cmd.CommandText = "INSERT OR REPLACE INTO counters(Counter,Val,LastUTC) VALUES ($cntr,$val,$time)";
                cmd.Parameters.Add("cntr", SqliteType.Text).Value = counter;
                cmd.Parameters.Add("val", SqliteType.Integer).Value = newCounterVal;
                cmd.Parameters.Add("time", SqliteType.Integer).Value = newLastUTC.Ticks;
                cmd.ExecuteNonQuery();

                cmd.CommandText = "INSERT INTO decisions(MaterialID, InspType, Counter, Inspect, CreateUTC) VALUES " +
            "($mat,$insp,$cntr,$status,$create)";
                cmd.Parameters.Clear();
                cmd.Parameters.Add("mat", SqliteType.Integer).Value = matID;
                cmd.Parameters.Add("insp", SqliteType.Text).Value = inspType;
                cmd.Parameters.Add("cntr", SqliteType.Text).Value = counter;
                cmd.Parameters.Add("status", SqliteType.Integer).Value = status;
                cmd.Parameters.Add("create", SqliteType.Integer).Value = utcNow.Ticks;
                cmd.ExecuteNonQuery();

                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }
        #endregion

        #region Inspection Counts
        public Generic.List<InspectCount> LoadInspectCounts()
        {
            lock (_lock)
            {
                Generic.List<InspectCount> ret = new Generic.List<InspectCount>();

                var cmd = _connection.CreateCommand();

                cmd.CommandText = "SELECT Counter, Val, LastUTC FROM counters";

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        InspectCount insp = default(InspectCount);
                        insp.Counter = reader.GetString(0);
                        insp.Value = reader.GetInt32(1);
                        if (reader.IsDBNull(2))
                            insp.LastUTC = DateTime.MaxValue;
                        else
                            insp.LastUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc);
                        ret.Add(insp);
                    }
                }

                return ret;
            }
        }

        public void SetInspectCounts(Generic.IEnumerable<InspectCount> counts)
        {
            lock (_lock)
            {
                var cmd = _connection.CreateCommand();

                cmd.CommandText = "INSERT OR REPLACE INTO counters(Counter, Val, LastUTC) VALUES ($cntr,$val,$last)";
                cmd.Parameters.Add("cntr", SqliteType.Text);
                cmd.Parameters.Add("val", SqliteType.Integer);
                cmd.Parameters.Add("last", SqliteType.Integer);

                var trans = _connection.BeginTransaction();
                try
                {
                    cmd.Transaction = trans;

                    foreach (var insp in counts)
                    {
                        cmd.Parameters[0].Value = insp.Counter;
                        cmd.Parameters[1].Value = insp.Value;
                        cmd.Parameters[2].Value = insp.LastUTC.Ticks;
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
