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
using System.Collections.Generic;
using System.Linq;
using System.Data;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework
{
    //database backend for the job db
    public class JobDB : BlackMaple.MachineWatchInterface.IJobDatabase
    {

        #region Database Open/Update
        private SqliteConnection _connection;
        private object _lock = new object();

        public event MachineWatchInterface.NewJobsDelegate OnNewJobs;

        public JobDB() { }
        public JobDB(SqliteConnection c)
        {
            _connection = c;
        }

        public void Open(string filename)
        {
            if (System.IO.File.Exists(filename))
            {
                _connection = new SqliteConnection("Data Source=" + filename);
                _connection.Open();
                UpdateTables();
            }
            else
            {
                _connection = new SqliteConnection("Data Source=" + filename);
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

        public void Close()
        {
            _connection.Close();
        }

        private const int Version = 17;

        public void CreateTables()
        {
            using (var cmd = _connection.CreateCommand()) {

            cmd.CommandText = "CREATE TABLE version(ver INTEGER)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO version VALUES(" + Version.ToString() + ")";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE jobs(UniqueStr TEXT PRIMARY KEY, Part TEXT NOT NULL, NumProcess INTEGER NOT NULL, Priority INTEGER NOT NULL, Comment TEXT, CreateMarker INTEGER NOT NULL, StartUTC INTEGER NOT NULL, EndUTC INTEGER NOT NULL, Archived INTEGER NOT NULL, CopiedToSystem INTEGER NOT NULL, ScheduleId TEXT)";
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

            cmd.CommandText = "CREATE TABLE pathdata(UniqueStr TEXT, Process INTEGER, Path INTEGER, StartingUTC INTEGER, PartsPerPallet INTEGER, PathGroup INTEGER, SimAverageFlowTime INTEGER, InputQueue TEXT, OutputQueue TEXT, LoadTime INTEGER, UnloadTime INTEGER, PRIMARY KEY(UniqueStr,Process,Path))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE pallets(UniqueStr TEXT, Process INTEGER, Path INTEGER, Pallet TEXT, PRIMARY KEY(UniqueStr,Process,Path,Pallet))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE fixtures(UniqueStr TEXT, Process INTEGER, Path INTEGER, Fixture TEXT, Face TEXT, PRIMARY KEY(UniqueStr,Process,Path,Fixture,Face))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE stops(UniqueStr TEXT, Process INTEGER, Path INTEGER, RouteNum INTEGER, StatGroup STRING, ExpectedCycleTime INTEGER, Program TEXT, ProgramRevision INTEGER, PRIMARY KEY(UniqueStr, Process, Path, RouteNum))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE stops_stations(UniqueStr TEXT, Process INTEGER, Path INTEGER, RouteNum INTEGER, StatNum INTEGER, PRIMARY KEY(UniqueStr, Process, Path, RouteNum, StatNum))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE tools(UniqueStr TEXT, Process INTEGER, Path INTEGER, RouteNum INTEGER, Tool STRING, ExpectedUse INTEGER, PRIMARY KEY(UniqueStr,Process,Path,RouteNum,Tool))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE loadunload(UniqueStr TEXT, Process INTEGER, Path INTEGER, StatNum INTEGER, Load INTEGER, PRIMARY KEY(UniqueStr,Process,Path,StatNum,Load))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE TABLE inspections(UniqueStr TEXT, InspType STRING, Counter STRING, MaxVal INTEGER, TimeInterval INTEGER, RandomFreq NUMERIC, InspProc INTEGER, PRIMARY KEY (UniqueStr, InspType))";
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
            }
        }


        private void UpdateTables()
        {
            using (var cmd = _connection.CreateCommand()) {

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
        }

        private void Ver0ToVer1(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = "CREATE TABLE fixtures(UniqueStr TEXT, Process INTEGER, Path INTEGER, Fixture TEXT, Face TEXT, PRIMARY KEY(UniqueStr,Process,Path,Fixture,Face))";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver1ToVer2(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = "ALTER TABLE pathdata ADD PartsPerPallet INTEGER";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver2ToVer3(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = "ALTER TABLE pathdata ADD PathGroup INTEGER";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver3ToVer4(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = "ALTER TABLE inspections ADD RandomFreq NUMERIC";
                cmd.ExecuteNonQuery();
            }

        }

        private void Ver4ToVer5(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = "ALTER TABLE inspections ADD InspProc INTEGER";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver5ToVer6(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
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

        private void Ver6ToVer7(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = "CREATE TABLE scheduled_bookings(UniqueStr TEXT NOT NULL, BookingId TEXT NOT NULL, PRIMARY KEY(UniqueStr, BookingId))";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "CREATE TABLE extra_parts(UniqueStr TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER, PRIMARY KEY(UniqueStr, Part))";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver7ToVer8(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
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

        private void Ver8ToVer9(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
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

        private void Ver9ToVer10(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = "ALTER TABLE sim_station_use ADD PlanDownTime INTEGER";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver10ToVer11(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = "CREATE TABLE schedule_debug(ScheduleId TEXT PRIMARY KEY, DebugMessage BLOB)";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver11ToVer12(IDbTransaction transaction)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "CREATE TABLE unfilled_workorders(ScheduleId TEXT NOT NULL, Workorder TEXT NOT NULL, Part TEXT NOT NULL, Quantity INTEGER NOT NULL, DueDate INTEGER NOT NULL, Priority INTEGER NOT NULL, PRIMARY KEY(ScheduleId, Part, Workorder))";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver12ToVer13(IDbTransaction transaction)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "ALTER TABLE pathdata ADD InputQueue TEXT";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "ALTER TABLE pathdata ADD OutputQueue TEXT";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver13ToVer14(IDbTransaction transaction)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "ALTER TABLE pathdata ADD LoadTime INTEGER";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "ALTER TABLE pathdata ADD UnloadTime INTEGER";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver14ToVer15(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
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

        private void Ver15ToVer16(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
            {
                cmd.Transaction = trans;
                cmd.CommandText = "DROP TABLE material";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "DROP TABLE first_proc_comp";
                cmd.ExecuteNonQuery();
            }
        }

        private void Ver16ToVer17(IDbTransaction trans)
        {
            using (IDbCommand cmd = _connection.CreateCommand())
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
            using (var cmd = _connection.CreateCommand()) {
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

            //read fixtures
            cmd.CommandText = "SELECT Process, Path, Fixture, Face FROM fixtures WHERE UniqueStr = $uniq";
            using (IDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    job.AddProcessOnFixture(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3));
                }
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
            cmd.CommandText = "SELECT Process, Path, StartingUTC, PartsPerPallet, PathGroup, SimAverageFlowTime, InputQueue, OutputQueue, LoadTime, UnloadTime FROM pathdata WHERE UniqueStr = $uniq";
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
            cmd.CommandText = "SELECT InspType, Counter, MaxVal, TimeInterval, RandomFreq, InspProc FROM inspections WHERE UniqueStr = $uniq";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    MachineWatchInterface.JobInspectionData insp;
                    int inspProc = -1;
                    if (!reader.IsDBNull(5))
                    {
                        inspProc = reader.GetInt32(5);
                    }
                    if (reader.IsDBNull(4) || reader.GetInt32(2) >= 0)
                    {
                        insp = new MachineWatchInterface.JobInspectionData(reader.GetString(0),
                                                                          reader.GetString(1),
                                                                          reader.GetInt32(2),
                                                                          TimeSpan.FromTicks(reader.GetInt64(3)),
                                                                          inspProc);
                    }
                    else
                    {
                        insp = new MachineWatchInterface.JobInspectionData(reader.GetString(0),
                                                                           reader.GetString(1),
                                                                           reader.GetDouble(4),
                                                                           TimeSpan.FromTicks(reader.GetInt64(3)),
                                                                           inspProc);
                    }
                    job.AddInspection(insp);
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
			using (var cmd2 = _connection.CreateCommand()) {
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
                    job.Priority = reader.GetInt32(3);

                    if (!reader.IsDBNull(4))
                        job.Comment = reader.GetString(4);

                    job.CreateMarkerData = reader.GetBoolean(5);
                    if (!reader.IsDBNull(6))
                        job.RouteStartingTimeUTC = new DateTime(reader.GetInt64(6), DateTimeKind.Utc);
                    if (!reader.IsDBNull(7))
                        job.RouteEndingTimeUTC = new DateTime(reader.GetInt64(7), DateTimeKind.Utc);
                    job.Archived = reader.GetBoolean(8);
                    if (!reader.IsDBNull(9))
                        job.JobCopiedToSystem = reader.GetBoolean(9);
                    if (!reader.IsDBNull(10))
                        job.ScheduleId = reader.GetString(10);

                    ret.Add(job);

                }
            }
            }

            foreach (var job in ret)
                LoadJobData(job, trans);

			return ret;
        }

        private Dictionary<string, int> LoadMostRecentExtraParts(IDbTransaction trans)
        {
            using (var cmd = _connection.CreateCommand()) {
            ((IDbCommand)cmd).Transaction = trans;

            var ret = new Dictionary<string, int>();
            cmd.CommandText = "SELECT Part, Quantity FROM scheduled_parts WHERE ScheduleId IN (SELECT MAX(ScheduleId) FROM jobs WHERE ScheduleId IS NOT NULL)";
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

        private List<MachineWatchInterface.PartWorkorder> LoadMostRecentUnfilledWorkorders(IDbTransaction trans)
        {
            using (var cmd = _connection.CreateCommand()) {
            ((IDbCommand)cmd).Transaction = trans;

            var ret = new List<MachineWatchInterface.PartWorkorder>();
            cmd.CommandText = "SELECT Workorder, Part, Quantity, DueDate, Priority FROM unfilled_workorders WHERE ScheduleId IN (SELECT MAX(ScheduleId) FROM jobs WHERE ScheduleId IS NOT NULL)";
            using (IDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    ret.Add(new MachineWatchInterface.PartWorkorder() {
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
            using (var cmd = _connection.CreateCommand()) {
            ((IDbCommand)cmd).Transaction = trans;

            cmd.CommandText = "SELECT MAX(ScheduleId) FROM jobs WHERE ScheduleId IS NOT NULL";

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

        private MachineWatchInterface.PlannedSchedule LoadPlannedSchedule(IDbCommand cmd)
        {
            lock (_lock)
            {
                var ret = default(MachineWatchInterface.PlannedSchedule);
                ret.Jobs = new List<MachineWatchInterface.JobPlan>();
                ret.ExtraParts = new Dictionary<string, int>();

                var trans = _connection.BeginTransaction();
                try
                {
                    cmd.Transaction = trans;

					ret.Jobs = LoadJobsHelper(cmd, trans);
                    ret.ExtraParts = LoadMostRecentExtraParts(trans);
                    ret.CurrentUnfilledWorkorders = LoadMostRecentUnfilledWorkorders(trans);
                    ret.LatestScheduleId = LatestScheduleId(trans);

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

        private MachineWatchInterface.HistoricData LoadHistory(IDbCommand jobCmd, IDbCommand simCmd)
        {
            lock (_lock)
            {
                var ret = default(BlackMaple.MachineWatchInterface.HistoricData);
                ret.Jobs = new Dictionary<string, MachineWatchInterface.JobPlan>();
                ret.StationUse = new List<MachineWatchInterface.SimulatedStationUtilization>();

                var trans = _connection.BeginTransaction();
                try
                {
                    jobCmd.Transaction = trans;
                    simCmd.Transaction = trans;

					ret.Jobs = LoadJobsHelper(jobCmd, trans).ToDictionary(j => j.UniqueStr, j => j);
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

        public MachineWatchInterface.PlannedSchedule LoadUnarchivedJobs()
        {
            using (var cmd = _connection.CreateCommand()) {
            cmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Priority, Comment, CreateMarker, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId FROM jobs WHERE Archived = 0";
            return LoadPlannedSchedule(cmd);
            }
        }

        public MachineWatchInterface.PlannedSchedule LoadJobsNotCopiedToSystem(DateTime startUTC, DateTime endUTC)
        {
            using (var cmd = _connection.CreateCommand()) {
            cmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Priority, Comment, CreateMarker, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId " +
                " FROM jobs WHERE StartUTC <= $end AND EndUTC >= $start AND CopiedToSystem = 0";
            cmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
            cmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;
            return LoadPlannedSchedule(cmd);
            }
        }

        public BlackMaple.MachineWatchInterface.HistoricData LoadJobHistory(DateTime startUTC, DateTime endUTC)
        {
            using (var jobCmd = _connection.CreateCommand())
            using (var simCmd = _connection.CreateCommand()) {
            jobCmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Priority, Comment, CreateMarker, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId " +
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
            using (var simCmd = _connection.CreateCommand()) {
            jobCmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Priority, Comment, CreateMarker, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId " +
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
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand()) {

                var ret = new List<MachineWatchInterface.PartWorkorder>();
                cmd.CommandText = "SELECT Workorder, Part, Quantity, DueDate, Priority FROM unfilled_workorders " +
                  "WHERE ScheduleId IN (SELECT MAX(ScheduleId) FROM jobs WHERE ScheduleId IS NOT NULL) AND Part = $part";
                cmd.Parameters.Add("part", SqliteType.Text).Value = part;

                using (IDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ret.Add(new MachineWatchInterface.PartWorkorder() {
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
            using (var cmd = _connection.CreateCommand()) {
			cmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Priority, Comment, CreateMarker, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId " +
                " FROM jobs WHERE ScheduleId = $sid";

            lock (_lock)
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
                    ret.ExtraParts = LoadMostRecentExtraParts(trans);
                    ret.CurrentUnfilledWorkorders = LoadMostRecentUnfilledWorkorders(trans);

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
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand())
                using (var cmd2 = _connection.CreateCommand()) {

                MachineWatchInterface.JobPlan job = null;

                cmd.CommandText = "SELECT Part, NumProcess, Priority, Comment, CreateMarker, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId FROM jobs WHERE UniqueStr = $uniq";
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
                            job.Priority = reader.GetInt32(2);

                            if (!reader.IsDBNull(3))
                                job.Comment = reader.GetString(3);

                            job.CreateMarkerData = reader.GetBoolean(4);
                            if (!reader.IsDBNull(5))
                                job.RouteStartingTimeUTC = new DateTime(reader.GetInt64(5), DateTimeKind.Utc);
                            if (!reader.IsDBNull(6))
                                job.RouteEndingTimeUTC = new DateTime(reader.GetInt64(6), DateTimeKind.Utc);
                            job.Archived = reader.GetBoolean(7);
                            if (!reader.IsDBNull(8))
                                job.JobCopiedToSystem = reader.GetBoolean(8);
                            if (!reader.IsDBNull(9))
                                job.ScheduleId = reader.GetString(9);

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
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand()) {
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

        public IList<MachineWatchInterface.JobInspectionData> LoadInspections(string unique)
        {
            var ret = new List<MachineWatchInterface.JobInspectionData>();

            lock (_lock)
            {
                var trans = _connection.BeginTransaction();
                try
                {
                    using (var cmd = _connection.CreateCommand()) {
                    ((IDbCommand)cmd).Transaction = trans;
                    cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
                    cmd.CommandText = "SELECT InspType, Counter, MaxVal, TimeInterval, RandomFreq, InspProc FROM inspections WHERE UniqueStr = $uniq";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            MachineWatchInterface.JobInspectionData insp;
                            int inspProc = -1;
                            if (!reader.IsDBNull(5))
                            {
                                inspProc = reader.GetInt32(5);
                            }
                            if (reader.IsDBNull(4) || reader.GetInt32(2) >= 0)
                            {
                                insp = new MachineWatchInterface.JobInspectionData(
                                        reader.GetString(0),
                                        reader.GetString(1),
                                        reader.GetInt32(2),
                                        TimeSpan.FromTicks(reader.GetInt64(3)),
                                        inspProc);
                            }
                            else
                            {
                                insp = new MachineWatchInterface.JobInspectionData(
                                        reader.GetString(0),
                                        reader.GetString(1),
                                        reader.GetDouble(4),
                                        TimeSpan.FromTicks(reader.GetInt64(3)),
                                        inspProc);
                            }
                            ret.Add(insp);
                        }
                    }

                    trans.Commit();
                    }
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }

            return ret;
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
            lock (_lock)
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
                    foreach (var job in newJobs.Jobs)
                    {
                        AddJob(trans, job);
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

            if (OnNewJobs != null) {
                OnNewJobs(newJobs);
            }
        }
        private void AddJob(IDbTransaction trans, MachineWatchInterface.JobPlan job)
        {
            using (var cmd = _connection.CreateCommand()) {
            ((IDbCommand)cmd).Transaction = trans;

            cmd.CommandText = "INSERT INTO jobs(UniqueStr, Part, NumProcess, Priority, Comment, CreateMarker, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId) " +
          "VALUES($uniq,$part,$proc,$pri,$comment,$marker,$start,$end,$archived,$copied,$sid)";
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
            cmd.Parameters.Add("part", SqliteType.Text).Value = job.PartName;
            cmd.Parameters.Add("proc", SqliteType.Integer).Value = job.NumProcesses;
            cmd.Parameters.Add("pri", SqliteType.Integer).Value = job.Priority;
            if (string.IsNullOrEmpty(job.Comment))
                cmd.Parameters.Add("comment", SqliteType.Text).Value = DBNull.Value;
            else
                cmd.Parameters.Add("comment", SqliteType.Text).Value = job.Comment;
            cmd.Parameters.Add("marker", SqliteType.Integer).Value = job.CreateMarkerData;
            cmd.Parameters.Add("start", SqliteType.Integer).Value = job.RouteStartingTimeUTC.Ticks;
            cmd.Parameters.Add("end", SqliteType.Integer).Value = job.RouteEndingTimeUTC.Ticks;
            cmd.Parameters.Add("archived", SqliteType.Integer).Value = job.Archived;
            cmd.Parameters.Add("copied", SqliteType.Integer).Value = job.JobCopiedToSystem;
            if (string.IsNullOrEmpty(job.ScheduleId))
                cmd.Parameters.Add("sid", SqliteType.Text).Value = DBNull.Value;
            else
                cmd.Parameters.Add("sid", SqliteType.Text).Value = job.ScheduleId;

            cmd.ExecuteNonQuery();

            if (job.ScheduledBookingIds != null) {
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

            cmd.CommandText = "INSERT INTO fixtures(UniqueStr, Process, Path, Fixture,Face) VALUES ($uniq,$proc,$path,$fix,$face)";
            cmd.Parameters.Clear();
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
            cmd.Parameters.Add("proc", SqliteType.Integer);
            cmd.Parameters.Add("path", SqliteType.Integer);
            cmd.Parameters.Add("fix", SqliteType.Text);
            cmd.Parameters.Add("face", SqliteType.Text);

            for (int i = 1; i <= job.NumProcesses; i++)
            {
                for (int j = 1; j <= job.GetNumPaths(i); j++)
                {
                    if (job.PlannedFixtures(i, j) == null) continue;
                    foreach (var fix in job.PlannedFixtures(i, j))
                    {
                        cmd.Parameters[1].Value = i;
                        cmd.Parameters[2].Value = j;
                        cmd.Parameters[3].Value = fix.Fixture;
                        cmd.Parameters[4].Value = fix.Face;
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            cmd.CommandText = "INSERT INTO pathdata(UniqueStr, Process, Path, StartingUTC, PartsPerPallet, PathGroup,SimAverageFlowTime,InputQueue,OutputQueue,LoadTime,UnloadTime) " +
          "VALUES ($uniq,$proc,$path,$start,$ppp,$group,$flow,$iq,$oq,$lt,$ul)";
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
                        cmd.Parameters[1].Value = i;
                        cmd.Parameters[2].Value = j;
                        cmd.Parameters[3].Value = routeNum;
                        cmd.Parameters[4].Value = entry.StationGroup;
                        cmd.Parameters[5].Value = entry.ExpectedCycleTime.Ticks;
                        cmd.Parameters[6].Value = string.IsNullOrEmpty(entry.ProgramName) ? DBNull.Value : (object)entry.ProgramName;
                        cmd.Parameters[7].Value = entry.ProgramRevision;
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

            AddJobInspection(trans, job);
        }

        private void AddJobInspection(IDbTransaction trans, MachineWatchInterface.JobPlan job)
        {
            if (job.GetInspections() == null) return;

            using (var cmd = _connection.CreateCommand()) {
            ((IDbCommand)cmd).Transaction = trans;

            cmd.CommandText = "INSERT OR REPLACE INTO inspections(UniqueStr,InspType,Counter,MaxVal,TimeInterval,RandomFreq,InspProc) "
          + "VALUES ($uniq,$insp,$cnt,$max,$time,$freq,$proc)";
            cmd.Parameters.Clear();
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
            cmd.Parameters.Add("insp", SqliteType.Text);
            cmd.Parameters.Add("cnt", SqliteType.Text);
            cmd.Parameters.Add("max", SqliteType.Integer);
            cmd.Parameters.Add("time", SqliteType.Integer);
            cmd.Parameters.Add("freq", SqliteType.Real);
            cmd.Parameters.Add("proc", SqliteType.Integer);
            foreach (var insp in job.GetInspections())
            {
                cmd.Parameters[1].Value = insp.InspectionType;
                cmd.Parameters[2].Value = insp.Counter;
                cmd.Parameters[3].Value = insp.MaxVal;
                cmd.Parameters[4].Value = insp.TimeInterval.Ticks;
                cmd.Parameters[5].Value = insp.RandomFreq;
                cmd.Parameters[6].Value = insp.InspectSingleProcess;
                cmd.ExecuteNonQuery();
            }
            }
        }

        private void AddSimulatedStations(IDbTransaction trans, IEnumerable<MachineWatchInterface.SimulatedStationUtilization> simStats)
        {
            if (simStats == null) return;

            using (var cmd = _connection.CreateCommand()) {
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
            using (var cmd = _connection.CreateCommand()) {
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
            using (var cmd = _connection.CreateCommand()) {
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

            using (var cmd = _connection.CreateCommand()) {
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
            lock (_lock)
            {
                var trans = _connection.BeginTransaction();
                try
                {
                    ArchiveJob(trans, UniqueStr);
                    trans.Commit();
                }
                catch
                {
                    trans.Rollback();
                    throw;
                }
            }
        }

        private void ArchiveJob(IDbTransaction trans, string UniqueStr)
        {
            using (var cmd = _connection.CreateCommand()) {
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = UniqueStr;
            ((IDbCommand)cmd).Transaction = trans;
            cmd.CommandText = "UPDATE jobs SET Archived = 1 WHERE UniqueStr = $uniq";
            cmd.ExecuteNonQuery();
            }
        }

        public void MarkJobCopiedToSystem(string UniqueStr)
        {
            lock (_lock)
            {
                var trans = _connection.BeginTransaction();
                try
                {
                    using (var cmd = _connection.CreateCommand()) {
                    cmd.CommandText = "UPDATE jobs SET CopiedToSystem = 1 WHERE UniqueStr = $uniq";
                    cmd.Parameters.Add("uniq", SqliteType.Text).Value = UniqueStr;
                    ((IDbCommand)cmd).Transaction = trans;
                    cmd.ExecuteNonQuery();
                    trans.Commit();
                    }
                } catch {
                    trans.Rollback();
                    throw;
                }
            }
        }
        #endregion

        #region "Modification of Jobs"
        public void UpdateJob(string unique, int newPriority, string comment)
        {
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand()) {

                var trans = _connection.BeginTransaction();

                try
                {
                    cmd.Transaction = trans;

                    cmd.CommandText = "UPDATE jobs SET Comment = $comment, Priority = $pri WHERE UniqueStr = $uniq";
                    cmd.Parameters.Add("comment", SqliteType.Text).Value = comment;
                    cmd.Parameters.Add("pri", SqliteType.Integer).Value = newPriority;
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
            lock (_lock)
            {
                var trans = _connection.BeginTransaction();

                try
                {

                    using (var cmd = _connection.CreateCommand()) {
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
        public void AddNewDecrement(IEnumerable<NewDecrementQuantity> counts, DateTime? nowUTC = null)
        {
            lock (_lock)
            {
                var now = nowUTC ?? DateTime.UtcNow;
                var trans = _connection.BeginTransaction();
                try
                {
                    long decrementId = 0;
                    using (var cmd = _connection.CreateCommand()) {
                        cmd.Transaction = trans;
                        cmd.CommandText = "SELECT MAX(DecrementId) FROM decrements";
                        var lastDecId = cmd.ExecuteScalar();
                        if (lastDecId != null && lastDecId != DBNull.Value) {
                            decrementId = Convert.ToInt64(lastDecId) + 1;
                        }
                    }

                    using (var cmd = _connection.CreateCommand()) {
                    cmd.Transaction = trans;

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

        public List<MachineWatchInterface.InProcessJobDecrement> LoadDecrementsForJob(string unique)
        {
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand()) {
                    cmd.CommandText = "SELECT DecrementId,TimeUTC,Quantity FROM decrements WHERE JobUnique = $uniq";
                    cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
                    var ret = new List<MachineWatchInterface.InProcessJobDecrement>();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var j = new MachineWatchInterface.InProcessJobDecrement();
                            j.DecrementId = reader.GetInt64(0);
                            j.TimeUTC = new DateTime(reader.GetInt64(1), DateTimeKind.Utc);
                            j.Quantity = reader.GetInt32(2);
                            ret.Add(j);
                        }
                        return ret;
                    }
                }
            }
        }

        public List<MachineWatchInterface.JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(long afterId)
        {
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand()) {
                cmd.CommandText = "SELECT DecrementId,JobUnique,TimeUTC,Part,Quantity FROM decrements WHERE DecrementId > $after";
                cmd.Parameters.Add("after", SqliteType.Integer).Value = afterId;
                return LoadDecrementQuantitiesHelper(cmd);
                }
            }
        }

        public List<MachineWatchInterface.JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(DateTime afterUTC)
        {
            lock (_lock)
            {
                using (var cmd = _connection.CreateCommand()) {
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
    }
}
