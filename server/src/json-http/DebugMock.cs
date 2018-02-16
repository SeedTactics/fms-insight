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
using System.Runtime.Serialization;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;
using System.Runtime.Loader;
using System.Reflection;
using Microsoft.Extensions.DependencyModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

#if DEBUG
namespace MachineWatchApiServer
{
    public class MockBackend : IServerBackend, IJobControl
    {
        public JobLogDB LogDB {get;private set;}
        public JobDB JobDB {get; private set;}
        public InspectionDB InspectionDB {get; private set;}

        public void Init(string path)
        {
            string dbFile(string f) => System.IO.Path.Combine(path, f + ".db");

            if (path != null)
            {
                if (System.IO.File.Exists(dbFile("log"))) System.IO.File.Delete(dbFile("log"));
                LogDB = new JobLogDB();
                LogDB.Open(dbFile("log"));

                if (System.IO.File.Exists(dbFile("insp"))) System.IO.File.Delete(dbFile("insp"));
                InspectionDB = new InspectionDB(LogDB);
                InspectionDB.Open(dbFile("insp"));

                if (System.IO.File.Exists(dbFile("job"))) System.IO.File.Delete(dbFile("job"));
                JobDB = new JobDB();
                JobDB.Open(dbFile("job"));
            }
            else
            {
                var conn = SqliteExtensions.ConnectMemory();
                conn.Open();
                LogDB = new JobLogDB(conn);
                LogDB.CreateTables();

                conn = SqliteExtensions.ConnectMemory();
                conn.Open();
                InspectionDB = new InspectionDB(LogDB, conn);
                InspectionDB.CreateTables();

                conn = SqliteExtensions.ConnectMemory();
                conn.Open();
                JobDB = new JobDB(conn);
                JobDB.CreateTables();
            }

            var sample = new LogEntryGenerator(LogDB);
            DateTime today = DateTime.Today;
            DateTime month = new DateTime(today.Year, today.Month, 1);
            sample.AddMonthOfCycles(month, "uniq1", "part1", "pal1", 1, 40, 70);
            sample.AddMonthOfCycles(month, "uniq2", "part2", "pal2", 2, 80, 110);
            sample.AddMonthOfCycles(month, "uniq1", "part1", "pal2", 3, 72, 72);
            sample.AddEntriesToDatabase();

            var mockPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "../../../mock-jobs.json"
            );
            using (var file = System.IO.File.OpenRead(mockPath))
            {
                var settings = new DataContractJsonSerializerSettings();
                settings.DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ssZ");
                var s = new DataContractJsonSerializer(typeof(BlackMaple.MachineWatchInterface.NewJobs), settings);
                var newJobs = (BlackMaple.MachineWatchInterface.NewJobs)s.ReadObject(file);
                JobDB.AddJobs(newJobs, null);
            }
        }

        public IEnumerable<System.Diagnostics.TraceSource> TraceSources()
        {
            return new System.Diagnostics.TraceSource[] {};
        }

        public void Halt()
        {
            JobDB.Close();
            InspectionDB.Close();
            LogDB.Close();
        }

        public IInspectionControl InspectionControl()
        {
            return InspectionDB;
        }

        public IJobControl JobControl()
        {
            return this;
        }

        public ILogDatabase LogDatabase()
        {
            return LogDB;
        }

        public IJobDatabase JobDatabase()
        {
            return JobDB;
        }

        public CurrentStatus GetCurrentStatus()
        {
            return SampleJobData.SampleCurrentStatus();
        }

        public List<string> CheckValidRoutes(IEnumerable<JobPlan> newJobs)
        {
            return new List<string>();
        }

        public void AddJobs(NewJobs jobs, string expectedPreviousScheduleId)
        {
            JobDB.AddJobs(jobs, expectedPreviousScheduleId);
        }

        public List<JobAndDecrementQuantity> DecrementJobQuantites(string loadDecrementsStrictlyAfterDecrementId)
        {
            throw new NotImplementedException();
        }

        public List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
        {
            throw new NotImplementedException();
        }

        public IOldJobDecrement OldJobDecrement()
        {
            throw new NotImplementedException();
        }

        public List<string> GetQueueNames()
        {
            return new List<string> {
                "queueaaaa",
                "queuebbbb"
            };
        }

        private List<long> aaaQueue = new List<long> {
            10,
            11,
            12
        };
        private List<long> bbbQueue = new List<long> {
            20,
            21,
        };

        public List<long> GetSerialsInQueue(string queue)
        {
            if (queue == "queueaaa") {
                return aaaQueue.ToList();
            } else if (queue == "queuebbb") {
                return bbbQueue.ToList();
            } else {
                return new List<long>();
            }
        }

        public void SetMaterialInQueue(long materialId, string queue)
        {
            aaaQueue.Remove(materialId);
            bbbQueue.Remove(materialId);
            if (queue == "queueaaa") {
                aaaQueue.Add(materialId);
            } else if (queue == "queuebbb") {
                bbbQueue.Add(materialId);
            }
        }

        public void RemoveMaterialFromAllQueues(long materialId)
        {
            aaaQueue.Remove(materialId);
            bbbQueue.Remove(materialId);
        }
    }

    public class LogEntryGenerator
    {
        private Random rand = new Random();
        private JobLogDB db;
        private List<LogEntry> NewEntries = new List<LogEntry>();

        public LogEntryGenerator(JobLogDB d) => db = d;

        ///Take all the created log entries and add them to the database in sorted order
        public void AddEntriesToDatabase()
        {
            foreach (var e in NewEntries.OrderBy(x => x.EndTimeUTC))
            {
                if (e.LogType == LogType.PartMark)
                    db.RecordSerialForMaterialID(e.Material.FirstOrDefault(), e.Result, e.EndTimeUTC);
                else if (e.LogType == LogType.OrderAssignment)
                    db.RecordWorkorderForMaterialID(e.Material.FirstOrDefault(), e.Result, e.EndTimeUTC);
                else if (e.LogType == LogType.FinalizeWorkorder)
                    db.RecordFinalizedWorkorder(e.Result, e.EndTimeUTC);
                else
                    db.AddLogEntry(e);
            }
        }

        public void AddMonthOfCycles(DateTime month, string uniq, string part, string pal, int machine, double active, double time)
        {
            var workPrefix = "work" + part;
            var workCounter = 1;
            var workRemaining = rand.Next(3, 20);

            DateTime cur = month.AddMinutes(RandomCycleTime(time));
            while (cur < month.AddMonths(1))
            {
                LogMaterial mat;
                (mat, cur) = AddSinglePartCycle(cur, uniq, part, pal, machine, active, time);

                AddWorkorder(workPrefix + "-" + workCounter.ToString(), mat, cur);
                cur = cur.AddSeconds(5);
                workRemaining -= 1;
                if (workRemaining == 0)
                {
                    FinalizeWorkorder(workPrefix + "-" + workCounter.ToString(), cur);
                    cur = cur.AddSeconds(5);
                    workRemaining = rand.Next(3, 30);
                    workCounter += 1;
                }

                AddInspection(part, machine, mat, cur);
                cur = cur.AddMinutes(1);
            }
        }

        private (LogMaterial, DateTime) AddSinglePartCycle(DateTime cur, string uniq, string part, string pal, int machine, double active, double time)
        {
            DateTime start = cur;
            var mat = new LogMaterial(matID: GetMatId(uniq), uniq: uniq, proc:1, part:part, numProc:1);
            AddLoad(mat, 5, pal, 1, ref cur);
            //5 minutes for transfer
            cur = cur.AddMinutes(RandomCycleTime(5));
            AddMachine(mat, time, pal, machine, active, ref cur);
            cur = cur.AddMinutes(RandomCycleTime(5));
            AddUnload(mat, 5, pal, 1, ref cur);
            AddPallet(pal, cur.Subtract(start), cur);
            cur = cur.AddSeconds(5);
            AddSerial(part, mat, cur);
            cur = cur.AddSeconds(5);
            return (mat, cur);
        }

        private double RandomCycleTime(double mean)
        {
            double u1 = 1.0 - rand.NextDouble(); //uniform(0,1] random doubles
            double u2 = 1.0 - rand.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                         Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
            double randNormal =
                         mean + 5 * randStdNormal; //random normal(mean,stdDev^2)
            return randNormal > 1 ? randNormal : 1;
        }

        private string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[rand.Next(s.Length)]).ToArray());
        }


        private int GetMatId(string uniq)
        {
            return (int)db.AllocateMaterialID(uniq);
        }

        private void AddLoad(LogMaterial mat, double cycle, string pal, int stat, ref DateTime cur)
        {
            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] { mat },
                pal: pal,
                ty: LogType.LoadUnloadCycle,
                locName: "Load",
                locNum: stat,
                prog: "prog1",
                start: true,
                endTime: cur,
                result: "LOAD",
                endOfRoute: false));

            var elap = RandomCycleTime(cycle);
            cur = cur.AddMinutes(elap);

            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] {mat},
                pal: pal,
                ty: LogType.LoadUnloadCycle,
                locName: "Load",
                locNum: stat,
                prog: "prog1",
                start: false,
                endTime: cur,
                result: "LOAD",
                endOfRoute: false,
                elapsed: TimeSpan.FromMinutes(elap),
                active: TimeSpan.FromMinutes(3.5)
                ));
        }

        private void AddUnload(LogMaterial mat, double cycle, string pal, int stat, ref DateTime cur)
        {
            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] { mat },
                pal: pal,
                ty: LogType.LoadUnloadCycle,
                locName: "Load",
                locNum: stat,
                prog: "prog1",
                start: true,
                endTime: cur,
                result: "UNLOAD",
                endOfRoute: false));

            var elap = RandomCycleTime(cycle);
            cur = cur.AddMinutes(elap);

            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] { mat },
                pal: pal,
                ty: LogType.LoadUnloadCycle,
                locName: "Load",
                locNum: stat,
                prog: "prog1",
                start: false,
                endTime: cur,
                result: "UNLOAD",
                endOfRoute: true,
                elapsed: TimeSpan.FromMinutes(elap),
                active: TimeSpan.Zero
                ));
        }

        private void AddMachine(LogMaterial mat, double cycle, string pal, int stat, double active, ref DateTime cur)
        {
            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] { mat },
                pal: pal,
                ty: LogType.MachineCycle,
                locName: "MC",
                locNum: stat,
                prog: "prog1",
                start: true,
                endTime: cur,
                result: "",
                endOfRoute: false));

            var elap = RandomCycleTime(cycle);
            cur = cur.AddMinutes(elap);

            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] { mat },
                pal: pal,
                ty: LogType.MachineCycle,
                locName: "MC",
                locNum: stat,
                prog: "prog1",
                start: false,
                endTime: cur,
                result: "",
                endOfRoute: false,
                elapsed: TimeSpan.FromMinutes(elap),
                active: TimeSpan.FromMinutes(active)
                ));
        }

        private void AddPallet(string pal, TimeSpan elapsed, DateTime cur)
        {
            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] { },
                pal: pal,
                ty: LogType.PalletCycle,
                locName: "Pallet Cycle",
                locNum: 1,
                prog: "PalletCycle",
                start: false,
                endTime: cur,
                result: "",
                endOfRoute: false,
                elapsed: elapsed,
                active: TimeSpan.Zero
                ));
        }

        private void AddInspection(string part, int machine, LogMaterial mat, DateTime cur)
        {
            bool result = rand.NextDouble() < 0.2;

            var ty = new InspectionType
            {
                Name = "MyInspection",
                TrackPartName = true,
                TrackPalletName = false,
                TrackStationName = true,
                DefaultCountToTriggerInspection = 10,
                DefaultDeadline = TimeSpan.Zero,
                DefaultRandomFreq = -1,
                Overrides = new List<InspectionFrequencyOverride>()
            };
            var insp = ty.ConvertToJobInspection(part, 1);

            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] { mat },
                pal: "",
                ty: LogType.Inspection,
                locName: "Inspect",
                locNum: 1,
                prog: insp.Counter
                    .Replace(JobInspectionData.StationFormatFlag(1, 1), machine.ToString()),
                start: false,
                endTime: cur,
                result: result.ToString(),
                endOfRoute: false
                ));
        }

        private void AddSerial(string pal, LogMaterial mat, DateTime cur)
        {
            var result = JobLogDB.ConvertToBase62(mat.MaterialID);
            result = result.PadLeft(5, '0');
            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] { mat },
                pal: pal,
                ty: LogType.PartMark,
                locName: "Mark",
                locNum: 1,
                prog: "MARK",
                start: false,
                endTime: cur,
                result: result,
                endOfRoute: false
                ));
        }

        private void AddWorkorder(string work, LogMaterial mat, DateTime cur)
        {
            if (work == null) return;
            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] { mat },
                pal: "",
                ty: LogType.OrderAssignment,
                locName: "Order",
                locNum: 1,
                prog: "Order",
                start: false,
                endTime: cur,
                result: work,
                endOfRoute: false
                ));
        }

        private void FinalizeWorkorder(string work, DateTime cur)
        {
            NewEntries.Add(new LogEntry(
                cntr: 0,
                mat: new LogMaterial[] { },
                pal: "",
                ty: LogType.FinalizeWorkorder,
                locName: "FinalizeWorkorder",
                locNum: 1,
                prog: "FinalizeWorkorder",
                start: false,
                endTime: cur,
                result: work,
                endOfRoute: false
                ));
        }
    }

    public static class SampleJobData
    {
        public static CurrentStatus SampleCurrentStatus()
        {
            var st = new CurrentStatus();

            var uniqs = new[] {
                new {name = "part1", uniq="uniq1a"},
                new {name = "part1", uniq="uniq1b"},
                new {name = "part2", uniq="uniq2"},
                new {name = "part3", uniq="uniq3"},
                new {name = "part4", uniq="uniq4"},
                new {name = "part5", uniq="uniq5"},
            };

            foreach (var u in uniqs) {
                var job1 = new InProcessJob(RandomJob(u.uniq, j => {
                    j.PartName = u.name;
                    j.SetPlannedCyclesOnFirstProcess(1, rng.Next(0, 30));
                    j.SetPlannedCyclesOnFirstProcess(2, rng.Next(0, 30));
                }));
                job1.SetCompleted(1, 1, 5);
                job1.SetCompleted(1, 2, 4);
                job1.SetCompleted(2, 1, 3);
                job1.SetCompleted(2, 2, 2);
                job1.SetCompleted(2, 3, 1);
                st.Jobs.Add(job1.UniqueStr, job1);
            }

            st.LatestScheduleId = st.Jobs.FirstOrDefault().Value.ScheduleId;

            st.Pallets.Add("1", CreatePallet1Data());

            return st;
        }

        public static JobPlan AddJobToHistory(JobDB db, string uniq, Action<JobPlan> modify = null)
        {
            var j = RandomJob(uniq, modify);
            db.AddJob(j);
            return j;
        }

        private static void AddLoad(JobPlan job, int proc, int path)
        {
            var stat = rng.Next(0, 1000);
            foreach (var s in job.LoadStations(proc, path))
                if (s == stat)
                    return;
            job.AddLoadStation(proc, path, stat);
        }

        private static void AddUnload(JobPlan job, int proc, int path)
        {
            var stat = rng.Next(0, 1000);
            foreach (var s in job.UnloadStations(proc, path))
                if (s == stat)
                    return;
            job.AddUnloadStation(proc, path, stat);
        }

        public static JobPlan RandomJob(string uniq, Action<JobPlan> modify = null)
        {
            var job1 = new JobPlan(uniq, 2, new int[] {2, 3});

            job1.PartName = "part" + RandomString(5);
            job1.SetPlannedCyclesOnFirstProcess(1, rng.Next(10, 1000));
            job1.SetPlannedCyclesOnFirstProcess(2, rng.Next(10, 1000));
            job1.RouteStartingTimeUTC = DateTime.UtcNow.AddMinutes(-rng.Next(40, 1000));
            job1.RouteEndingTimeUTC = DateTime.UtcNow.AddMinutes(rng.Next(0, 1000));
            job1.Archived = false;
            job1.JobCopiedToSystem = true;
            job1.Priority = rng.Next(10, 100);
            job1.Comment = "comment" + RandomString(5);
            job1.CreateMarkerData = true;
            for (int i = 0; i < 3; i++)
            {
                job1.ScheduledBookingIds.Add("booking" + RandomString(10));
            }

            job1.SetPartsPerPallet(1, 1, rng.Next(1, 10));
            job1.SetPartsPerPallet(1, 2, rng.Next(1, 10));
            job1.SetPartsPerPallet(2, 1, rng.Next(1, 10));
            job1.SetPartsPerPallet(2, 2, rng.Next(1, 10));
            job1.SetPartsPerPallet(2, 3, rng.Next(1, 10));

            job1.SetPathGroup(1, 1, 1);
            job1.SetPathGroup(1, 2, 2);
            job1.SetPathGroup(2, 1, 1);
            job1.SetPathGroup(2, 2, 1);
            job1.SetPathGroup(2, 3, 2);

            job1.SetSimulatedStartingTimeUTC(1, 1, DateTime.UtcNow.AddMinutes(rng.Next(10, 300)));
            job1.SetSimulatedStartingTimeUTC(1, 2, DateTime.UtcNow.AddMinutes(rng.Next(10, 300)));
            job1.SetSimulatedStartingTimeUTC(2, 1, DateTime.UtcNow.AddMinutes(rng.Next(10, 300)));
            job1.SetSimulatedStartingTimeUTC(2, 2, DateTime.UtcNow.AddMinutes(rng.Next(10, 300)));
            job1.SetSimulatedStartingTimeUTC(2, 3, DateTime.UtcNow.AddMinutes(rng.Next(10, 300)));

            job1.SetSimulatedProduction(1, 1, RandSimProduction());
            job1.SetSimulatedProduction(1, 2, RandSimProduction());
            job1.SetSimulatedProduction(2, 1, RandSimProduction());
            job1.SetSimulatedProduction(2, 2, RandSimProduction());
            job1.SetSimulatedProduction(2, 3, RandSimProduction());

            job1.SetSimulatedAverageFlowTime(1, 1, TimeSpan.FromMinutes(rng.Next(10, 100)));
            job1.SetSimulatedAverageFlowTime(1, 2, TimeSpan.FromMinutes(rng.Next(10, 100)));
            job1.SetSimulatedAverageFlowTime(2, 1, TimeSpan.FromMinutes(rng.Next(10, 100)));
            job1.SetSimulatedAverageFlowTime(2, 2, TimeSpan.FromMinutes(rng.Next(10, 100)));
            job1.SetSimulatedAverageFlowTime(2, 3, TimeSpan.FromMinutes(rng.Next(10, 100)));

            job1.AddProcessOnPallet(1, 1, "pal" + RandomString(5));
            job1.AddProcessOnPallet(1, 1, "pal" + RandomString(5));
            job1.AddProcessOnPallet(1, 2, "pal" + RandomString(5));
            job1.AddProcessOnPallet(1, 2, "pal" + RandomString(5));
            job1.AddProcessOnPallet(2, 1, "pal" + RandomString(5));
            job1.AddProcessOnPallet(2, 1, "pal" + RandomString(5));
            job1.AddProcessOnPallet(2, 2, "pal" + RandomString(5));
            job1.AddProcessOnPallet(2, 2, "pal" + RandomString(5));
            job1.AddProcessOnPallet(2, 3, "pal" + RandomString(5));
            job1.AddProcessOnPallet(2, 3, "pal" + RandomString(5));

            AddLoad(job1, 1, 1);
            AddLoad(job1, 1, 1);
            AddLoad(job1, 1, 2);
            AddLoad(job1, 1, 2);
            AddLoad(job1, 2, 1);
            AddLoad(job1, 2, 1);
            AddLoad(job1, 2, 2);
            AddLoad(job1, 2, 2);
            AddLoad(job1, 2, 3);
            AddUnload(job1, 1, 1);
            AddUnload(job1, 1, 1);
            AddUnload(job1, 1, 2);
            AddUnload(job1, 2, 1);
            AddUnload(job1, 2, 2);
            AddUnload(job1, 2, 2);
            AddUnload(job1, 2, 3);

            var route = new JobMachiningStop("Machine");
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.ExpectedCycleTime = TimeSpan.FromMinutes(rng.Next(10, 200));
            job1.AddMachiningStop(1, 1, route);

            route = new JobMachiningStop("Other Machine");
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.ExpectedCycleTime = TimeSpan.FromMinutes(rng.Next(10, 200));
            job1.AddMachiningStop(1, 2, route);

            route = new JobMachiningStop("Test");
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.ExpectedCycleTime = TimeSpan.FromMinutes(rng.Next(10, 200));
            job1.AddMachiningStop(2, 1, route);

            route = new JobMachiningStop("Test");
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.ExpectedCycleTime = TimeSpan.FromMinutes(rng.Next(10, 200));
            job1.AddMachiningStop(2, 2, route);

            route = new JobMachiningStop("Test");
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.ExpectedCycleTime = TimeSpan.FromMinutes(rng.Next(10, 200));
            job1.AddMachiningStop(2, 1, route);

            route = new JobMachiningStop("Test");
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.AddProgram(rng.Next(1, 10), "prog" + RandomString(5));
            route.ExpectedCycleTime = TimeSpan.FromMinutes(rng.Next(10, 200));
            job1.AddMachiningStop(2, 2, route);

            job1.AddInspection(new JobInspectionData("Insp1", "counter1", 53, TimeSpan.FromMinutes(100), 12));
            job1.AddInspection(new JobInspectionData("Insp2", "counter1", 12, TimeSpan.FromMinutes(64)));
            job1.AddInspection(new JobInspectionData("Insp3", "abcdef", 175, TimeSpan.FromMinutes(121), 2));
            job1.AddInspection(new JobInspectionData("Insp4", "counter2", 16.12, TimeSpan.FromMinutes(33)));
            job1.AddInspection(new JobInspectionData("Insp5", "counter3", 0.544, TimeSpan.FromMinutes(44)));

            if (modify != null) modify(job1);
            return job1;
        }

        private static IEnumerable<JobPlan.SimulatedProduction> RandSimProduction()
        {
            var ret = new List<JobPlan.SimulatedProduction>();
            for (int i = 0; i < 3; i++)
            {
                var prod = default(JobPlan.SimulatedProduction);
                prod.TimeUTC = DateTime.UtcNow.AddHours(-100 + i*10);
                prod.Quantity = rng.Next(0, 100);
                ret.Add(prod);
            }

            return ret;
        }

        public static IEnumerable<SimulatedStationUtilization> RandStationUtilization()
        {
            var ret = new List<SimulatedStationUtilization>();
            for (int i = 0; i < 10; i++)
            {
                ret.Add(new SimulatedStationUtilization(
                    id: "id" + RandomString(10),
                    group: "group" + RandomString(10),
                    num: rng.Next(1, 1000),
                    start: DateTime.UtcNow.AddMinutes(10 + i*5),
                    endT: DateTime.UtcNow.AddMinutes(10 + (i+1)*5),
                    u: TimeSpan.FromSeconds(rng.Next(1, 5*60)),
                    d: TimeSpan.FromSeconds(rng.Next(1, 5*60))));
            }
            return ret;
        }

        private static PalletStatus CreatePallet1Data()
        {
            return new PalletStatus() {
                Pallet = "1",
                FixtureOnPallet = "fix1",
                CurrentPalletLocation = new PalletLocation(PalletLocationEnum.LoadUnload, "Load", 1)
            };
        }

        private static IEnumerable<InProcessMaterial> InProcMaterial()
        {
            var ret = new List<InProcessMaterial>();

            ret.Add(new InProcessMaterial() {
                MaterialID = 1,
                JobUnique = "job1",
                PartName = "part1",
                Process = 1,
                Path = 1,

                Location = new InProcessMaterialLocation() {
                    Type = InProcessMaterialLocation.LocType.OnPallet,
                    Pallet = "1",
                    Fixture = "fix1",
                    Face = 1
                },

                Action = new InProcessMaterialAction() {
                    Type = InProcessMaterialAction.ActionType.Unloading,
                }
            });

            ret.Add(new InProcessMaterial() {
                MaterialID = -1,
                JobUnique = "job1",
                PartName = "part1",
                Process = 1,
                Path = 1,

                Location = new InProcessMaterialLocation() {
                    Type = InProcessMaterialLocation.LocType.Free
                },

                Action = new InProcessMaterialAction() {
                    Type = InProcessMaterialAction.ActionType.Loading,
                    LoadOntoPallet = "1",
                    LoadOntoFace = 1
                }
            });

            return ret;
        }

        private static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[rng.Next(s.Length)]).ToArray());
        }

        private static Random rng = new Random();
    }
}
#endif