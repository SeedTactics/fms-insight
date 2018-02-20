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
using Xunit;
using Microsoft.Data.Sqlite;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace MachineWatchTest
{
    public class JobLogTest : IDisposable
    {
        private JobLogDB _jobLog;

        public JobLogTest()
        {
            var connection = BlackMaple.MachineFramework.SqliteExtensions.ConnectMemory();
            connection.Open();
            _jobLog = new JobLogDB(connection);
            _jobLog.CreateTables();
        }

        public void Dispose()
        {
            _jobLog.Close();
        }

        /*
        public class IgnoreEmptyCollection : Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            public static IgnoreEmptyCollection Instance = new IgnoreEmptyCollection();
            protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                Predicate<object> checkEmpty = obj =>
                {
                    var collection = property.ValueProvider.GetValue(obj) as System.Collections.ICollection;
                    return collection == null || collection.Count != 0;
                };
                if (property.ShouldSerialize == null)
                {
                    property.ShouldSerialize = checkEmpty;
                }
                else
                {
                    Predicate<object> oldShouldSerialize = property.ShouldSerialize;
                    property.ShouldSerialize = o => oldShouldSerialize(o) && checkEmpty(o);
                }
                return property;
            }
        }
        public void PrintLog(LogEntry log)
        {
            var str = JsonConvert.SerializeObject(log,
                                                  Formatting.Indented,
                                                  new Newtonsoft.Json.JsonSerializerSettings
                                                  { //DefaultValueHandling = DefaultValueHandling.Ignore,
                                                    Converters = new JsonConverter[] { new Newtonsoft.Json.Converters.StringEnumConverter() }
                                                    //ContractResolver = IgnoreEmptyCollection.Instance
                                                });
            Console.WriteLine("XXXXX " + str);

            //var settings = new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings {
            //    DateTimeFormat = new System.Runtime.Serialization.DateTimeFormat("yyyy-MM-ddTHH:mm:ssZ"),
            //};
            //var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(log.GetType(), settings);
            //System.IO.MemoryStream ms = new System.IO.MemoryStream();
            //serializer.WriteObject(ms, log);
            //Console.WriteLine("YYYYYY" + ": " + System.Text.Encoding.UTF8.GetString(ms.ToArray()));
            var stat = JsonConvert.DeserializeObject<LogEntry>(str);
            var str2 = JsonConvert.SerializeObject(stat, Formatting.Indented);
            Console.WriteLine("XXXXX " + str2);
    }*/

        [Fact]
        public void MaterialIDs()
        {
            long m1 = _jobLog.AllocateMaterialID("U1");
            long m2 = _jobLog.AllocateMaterialID("U2");
            long m3 = _jobLog.AllocateMaterialID("U3");

            Assert.Equal("U1", _jobLog.JobUniqueStrFromMaterialID(m1));
            Assert.Equal("U2", _jobLog.JobUniqueStrFromMaterialID(m2));
            Assert.Equal("U3", _jobLog.JobUniqueStrFromMaterialID(m3));
        }

        [Fact]
        public void AddLog()
        {
            Assert.Equal(DateTime.MinValue, _jobLog.MaxLogDate());

            System.DateTime start = DateTime.UtcNow.AddHours(-10);
            System.DateTime last = DateTime.MinValue;

            List<LogEntry> logs = new List<LogEntry>();
            var logsForMat1 = new List<LogEntry>();

            LogMaterial mat1 = new LogMaterial(19, "unique", 2, "pp1", 53, "face55");
            LogMaterial mat2 = new LogMaterial(1, "grgaegr", 7, "pp2", 23, "face22");

            var log = new LogEntry(12, new LogMaterial[] { mat1, mat2 }, "pal",
                                LogType.LoadUnloadCycle, "Load", 2,
                                "pppe", false, start.AddHours(1), "ress", true);
            logs.Add(log);
            logsForMat1.Add(log);

            mat1 = new LogMaterial(15, "qghr4e", 3, "ppp532", 14);
            mat2 = new LogMaterial(2, "ahre", 1, "gewoiweg", 13);

            logs.Add(new LogEntry(1166, new LogMaterial[] { mat1, mat2 }, "gherr",
                                LogType.MachineCycle, "MC", 16,
                                "ehr", true, start.AddHours(3), "geehr", false, TimeSpan.FromMinutes(55), TimeSpan.Zero));

            mat1 = new LogMaterial(1, "ehet", 1, "gweg", 12);

            log = new LogEntry(1622, new LogMaterial[] { mat1 }, "sht", LogType.GeneralMessage, "ABC", 1,
                             "heert", false, start.AddHours(5).AddMinutes(10), "sthrt", false, TimeSpan.FromMinutes(19), TimeSpan.FromMinutes(12));
            logs.Add(log);
            logsForMat1.Add(log);

            mat1 = new LogMaterial(1562, "hree", 6, "gaherher", 32);

            log = new LogEntry(1234, new LogMaterial[] { mat1 }, "jtryr", LogType.PartMark, "Mark", 8,
                                 "gh4rer", true, start.AddHours(7), "weferg", true);
            log.ProgramDetails["1"] = "one";
            log.ProgramDetails["2"] = "two";
            log.ProgramDetails["3"] = "three";
            logs.Add(log);

            last = AddLog(logs);

            Assert.Equal(last, _jobLog.MaxLogDate());

            IList<LogEntry> otherLogs = null;

            otherLogs = _jobLog.GetLogEntries(start, DateTime.UtcNow);
            long maxCtr = CheckLog(logs, otherLogs, start);

            otherLogs = _jobLog.GetLogEntries(start.AddHours(5), DateTime.UtcNow);
            CheckLog(logs, otherLogs, start.AddHours(5));

            otherLogs = _jobLog.GetLog(maxCtr - 2);
            CheckLog(logs, otherLogs, start.AddHours(5));

            otherLogs = _jobLog.GetLog(maxCtr - 1);
            CheckLog(logs, otherLogs, start.AddHours(6.5));

            otherLogs = _jobLog.GetLog(maxCtr);
            Assert.Equal(0, otherLogs.Count);

            foreach (var c in logs)
                Assert.True(_jobLog.CycleExists(c), "Checking " + c.EndTimeUTC.ToString());

            Assert.False(_jobLog.CycleExists(new LogEntry(
        -1, new LogMaterial[] { }, "Palll", LogType.MachineCycle, "MC", 3,
                "Program", false, DateTime.Parse("4/6/2011"), "", false)));

            CheckLog(logsForMat1, _jobLog.GetLogForMaterial(1), start);
            Assert.Equal(_jobLog.GetLogForMaterial(18).Count, 0);

            _jobLog.AllocateMaterialID("uniqformat1");
            mat1 = new LogMaterial(1, "thrwe", 9, "adsf", 644, "face842");
            var markLog = _jobLog.RecordSerialForMaterialID(mat1, "ser1");
            logsForMat1.Add(new LogEntry(-1, new LogMaterial[] { mat1 }, "",
                                             LogType.PartMark, "Mark", 1,
                                             "MARK", false, markLog.EndTimeUTC, "ser1", false));
            CheckLog(logsForMat1, _jobLog.GetLogForSerial("ser1"), start);
            Assert.Equal(_jobLog.GetLogForSerial("ser2").Count, 0);

            var orderLog = _jobLog.RecordWorkorderForMaterialID(mat1, "work1");
            logsForMat1.Add(new LogEntry(-1, new LogMaterial[] { mat1 }, "",
                                             LogType.OrderAssignment, "Order", 1,
                                             "", false, orderLog.EndTimeUTC, "work1", false));
            CheckLog(logsForMat1, _jobLog.GetLogForWorkorder("work1"), start);
            Assert.Equal(_jobLog.GetLogForWorkorder("work2").Count, 0);

            CheckLog(logsForMat1, _jobLog.GetLogForJobUnique("uniqformat1"), start);
            Assert.Equal(_jobLog.GetLogForJobUnique("sofusadouf").Count, 0);

            //inspection and wash
            var inspCompLog = _jobLog.RecordInspectionCompleted(
                mat1, 5, "insptype1", new Dictionary<string, string> {{"a", "aaa"}, {"b", "bbb"}},
                TimeSpan.FromMinutes(100), TimeSpan.FromMinutes(5));
            var expectedInspLog = new LogEntry(-1, new LogMaterial[] { mat1 }, "",
                LogType.InspectionResult, "Inspection", 5, "insptype1", false, inspCompLog.EndTimeUTC, "", false,
                TimeSpan.FromMinutes(100), TimeSpan.FromMinutes(5));
            expectedInspLog.ProgramDetails.Add("a", "aaa");
            expectedInspLog.ProgramDetails.Add("b", "bbb");
            logsForMat1.Add(expectedInspLog);

            var washLog = _jobLog.RecordWashCompleted(
                mat1, 7, new Dictionary<string, string> {{"z", "zzz"}, {"y", "yyy"}},
                TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(9));
            var expectedWashLog = new LogEntry(-1, new LogMaterial[] { mat1 }, "",
                LogType.Wash, "Wash", 7, "", false, washLog.EndTimeUTC, "", false,
                TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(9));
            expectedWashLog.ProgramDetails.Add("z", "zzz");
            expectedWashLog.ProgramDetails.Add("y", "yyy");
            logsForMat1.Add(expectedWashLog);

            CheckLog(logsForMat1, _jobLog.GetLogForJobUnique("uniqformat1"), start);
        }

        /*
          [Fact]
          public void Serialization()
          {
              System.DateTime start = DateTime.UtcNow.AddHours(-10);

              LogMaterial mat1 = new LogMaterial(1, "unique", 2, "pp1", 53, "face111");
              LogMaterial mat2 = new LogMaterial(17, "grgaegr", 7, "pp2", 23, "face4423");

              var c = new LogEntry(12, new LogMaterial[] { mat1, mat2 }, "pal",
                                 LogType.LoadUnloadCycle, "Load", 2,
                                 "pppe", false, start.AddHours(1), "ress", true);
              c.ProgramDetails["a"] = "z";
              c.ProgramDetails["b"] = "y";

              var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();

              var stream = new System.IO.MemoryStream();

              formatter.Serialize(stream, c);

              stream.Seek(0, System.IO.SeekOrigin.Begin);

              LogEntry cload =
                  (LogEntry) formatter.Deserialize(stream);

              CheckEqual(c, cload);
          }*/

        [Fact]
        public void LookupByPallet()
        {
            Assert.Equal(0, _jobLog.CurrentPalletLog("pal1").Count);
            Assert.Equal(0, _jobLog.CurrentPalletLog("pal2").Count);
            Assert.Equal(DateTime.MinValue, _jobLog.LastPalletCycleTime("pal1"));

            var pal1Initial = new List<LogEntry>();
            var pal1Cycle = new List<LogEntry>();
            var pal2Cycle = new List<LogEntry>();

            var mat1 = new LogMaterial(1, "unique", 1, "part1", 2, "face1");
            var mat2 = new LogMaterial(17, "unique2", 2, "part2", 2, "face2");

            DateTime pal1InitialTime = DateTime.UtcNow.AddHours(-4);

            // *********** Add load cycle on pal1
            pal1Initial.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                "pal1", LogType.LoadUnloadCycle, "Load", 2,
                "prog1",
                true, //start of event
                pal1InitialTime,
                "result",
                false)); //end of route
            pal1Initial.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                "pal1", LogType.LoadUnloadCycle, "Load", 2,
                "prog1",
                false, //start of event
                pal1InitialTime.AddMinutes(5),
                "result",
                false)); //end of route

            // *********** Add machine cycle on pal1
            pal1Initial.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                "pal1", LogType.MachineCycle, "MC", 2,
                "prog1",
                true, //start of event
                pal1InitialTime.AddMinutes(10),
                "result",
                false)); //end of route
            pal1Initial.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                "pal1", LogType.MachineCycle, "MC", 2,
                "prog1",
                false, //start of event
                pal1InitialTime.AddMinutes(20),
                "result",
                true)); //end of route
                        // ***********  End of Route for pal1

            AddLog(pal1Initial);

            CheckLog(pal1Initial, _jobLog.CurrentPalletLog("pal1"), DateTime.UtcNow.AddHours(-10));
            Assert.Equal(0, _jobLog.CurrentPalletLog("pal2").Count);

            _jobLog.CompletePalletCycle("pal1", pal1InitialTime.AddMinutes(25), "");

            pal1Initial.Add(new LogEntry(0, new LogMaterial[] {}, "pal1", LogType.PalletCycle, "Pallet Cycle", 1,
                "", false, pal1InitialTime.AddMinutes(25), "PalletCycle", false, TimeSpan.Zero, TimeSpan.Zero));

            Assert.Equal(pal1InitialTime.AddMinutes(25), _jobLog.LastPalletCycleTime("pal1"));
            CheckLog(pal1Initial, _jobLog.GetLogEntries(DateTime.UtcNow.AddHours(-10), DateTime.UtcNow), DateTime.UtcNow.AddHours(-50));
            Assert.Equal(0, _jobLog.CurrentPalletLog("pal1").Count);
            Assert.Equal(0, _jobLog.CurrentPalletLog("pal2").Count);

            DateTime pal2CycleTime = DateTime.UtcNow.AddHours(-3);

            // *********** Add pal2 load event
            pal2Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                "pal2", LogType.LoadUnloadCycle, "Load", 2,
                "prog1",
                true, //start of event
                pal2CycleTime,
                "result",
                false)); //end of route
            pal2Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                "pal2", LogType.LoadUnloadCycle, "Load", 2,
                "prog1",
                false, //start of event
                pal2CycleTime.AddMinutes(10),
                "result",
                false)); //end of route

            AddLog(pal2Cycle);

            Assert.Equal(0, _jobLog.CurrentPalletLog("pal1").Count);
            CheckLog(pal2Cycle, _jobLog.CurrentPalletLog("pal2"), DateTime.UtcNow.AddHours(-10));

            DateTime pal1CycleTime = DateTime.UtcNow.AddHours(-2);

            // ********** Add pal1 load event
            pal1Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                "pal1", LogType.LoadUnloadCycle, "Load", 2,
                "prog1",
                true, //start of event
                pal1CycleTime,
                "result",
                false)); //end of route
            pal1Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                "pal1", LogType.LoadUnloadCycle, "Load", 2,
                "prog1",
                false, //start of event
                pal1CycleTime.AddMinutes(15),
                "result",
                false)); //end of route

            // *********** Add pal1 start of machining
            pal1Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                "pal1", LogType.MachineCycle, "MC", 4,
                "prog1",
                true, //start of event
                pal1CycleTime.AddMinutes(20),
                "result",
                false)); //end of route

            AddLog(pal1Cycle);

            CheckLog(pal1Cycle, _jobLog.CurrentPalletLog("pal1"), DateTime.UtcNow.AddHours(-10));
            CheckLog(pal2Cycle, _jobLog.CurrentPalletLog("pal2"), DateTime.UtcNow.AddHours(-10));

            //********  Complete the pal1 machining
            pal1Cycle.Add(new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                "pal1", LogType.MachineCycle, "MC", 4,
                "prog1",
                false, //start of event
                pal1CycleTime.AddMinutes(30),
                "result",
                true)); //end of route

            _jobLog.AddLogEntry(pal1Cycle[pal1Cycle.Count - 1]);

            CheckLog(pal1Cycle, _jobLog.CurrentPalletLog("pal1"), DateTime.UtcNow.AddHours(-10));
            CheckLog(pal2Cycle, _jobLog.CurrentPalletLog("pal2"), DateTime.UtcNow.AddHours(-10));

            _jobLog.CompletePalletCycle("pal1", pal1CycleTime.AddMinutes(40), "");
            _jobLog.CompletePalletCycle("pal1", pal1CycleTime.AddMinutes(40), ""); //filter duplicates

            var elapsed = pal1CycleTime.AddMinutes(40).Subtract(pal1InitialTime.AddMinutes(25));
            pal1Cycle.Add(new LogEntry(0, new LogMaterial[] {}, "pal1", LogType.PalletCycle, "Pallet Cycle", 1,
                "", false, pal1CycleTime.AddMinutes(40), "PalletCycle", false, elapsed, TimeSpan.Zero));

            Assert.Equal(pal1CycleTime.AddMinutes(40), _jobLog.LastPalletCycleTime("pal1"));
            Assert.Equal(0, _jobLog.CurrentPalletLog("pal1").Count);
            CheckLog(pal1Cycle, _jobLog.GetLogEntries(pal1CycleTime.AddMinutes(-5), DateTime.UtcNow), DateTime.UtcNow.AddHours(-50));

            CheckLog(pal2Cycle, _jobLog.CurrentPalletLog("pal2"), DateTime.UtcNow.AddHours(-10));
        }

        [Fact]
        public void ForeignID()
        {
            var mat1 = new LogMaterial(1, "unique", 1, "part1", 2, "face1");
            var mat2 = new LogMaterial(17, "unique2", 2, "part2", 2, "face2");

            var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal1", LogType.GeneralMessage, "ABC", 1,
                                  "prog1", false, DateTime.UtcNow, "result1", false, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(11));
            var log2 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal2", LogType.MachineCycle, "MC", 1,
                                  "prog2", false, DateTime.UtcNow, "result2", false, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(16));
            var log3 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal3", LogType.LoadUnloadCycle, "Load", 1,
                                  "prog3", false, DateTime.UtcNow, "result3", false, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(21));

            Assert.Equal("", _jobLog.MaxForeignID());
            _jobLog.AddStationCycle(log1, "for1");
            Assert.Equal("for1", _jobLog.MaxForeignID());
            _jobLog.AddStationCycle(log2, "for2");
            Assert.Equal("for2", _jobLog.MaxForeignID());
            _jobLog.AddLogEntry(log3);
            Assert.Equal("for2", _jobLog.MaxForeignID());
            _jobLog.AddPendingLoad("p", "k", 1, TimeSpan.Zero, TimeSpan.Zero, "for4");
            Assert.Equal("for4", _jobLog.MaxForeignID());
            var mat = new Dictionary<string, IEnumerable<LogMaterial>>();
            mat["k"] = new LogMaterial[] { };
            _jobLog.SetSerialSettings(new SerialSettings(SerialType.NoSerials, 0));
            _jobLog.CompletePalletCycle("p", DateTime.UtcNow, "for3", mat);
            Assert.Equal("for4", _jobLog.MaxForeignID()); // for4 should be copied

            var load1 = _jobLog.StationLogByForeignID("for1");
            Assert.Equal(1, load1.Count);
            CheckEqual(log1, load1[0]);

            var load2 = _jobLog.StationLogByForeignID("for2");
            Assert.Equal(1, load2.Count);
            CheckEqual(log2, load2[0]);

            var load3 = _jobLog.StationLogByForeignID("for3");
            Assert.Equal(1, load3.Count);
            Assert.Equal(LogType.PalletCycle, load3[0].LogType);

            var load4 = _jobLog.StationLogByForeignID("for4");
            Assert.Equal(1, load4.Count);
            Assert.Equal(LogType.LoadUnloadCycle, load4[0].LogType);

            Assert.Equal(0, _jobLog.StationLogByForeignID("abwgtweg").Count);

            Assert.Equal("for1", _jobLog.ForeignIDForCounter(load1[0].Counter));
            Assert.Equal("for2", _jobLog.ForeignIDForCounter(load2[0].Counter));
            Assert.Equal("", _jobLog.ForeignIDForCounter(load2[0].Counter + 30));
        }

        [Fact]
        public void OriginalMessage()
        {
            var mat1 = new LogMaterial(14, "uniqqqq", 1, "pppart66", 5, "facce");
            var mat2 = new LogMaterial(17, "uuuuuniq", 2, "part5", 2, "face2");

            var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "palllet16", LogType.GeneralMessage, "Hello", 5,
                                  "program125", false, DateTime.UtcNow, "result66", false, TimeSpan.FromMinutes(166), TimeSpan.FromMinutes(74));
            var log2 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pall53", LogType.MachineCycle, "MC", 9,
                                  "prog55", false, DateTime.UtcNow, "result", false, TimeSpan.FromMinutes(63), TimeSpan.FromMinutes(53));

            Assert.Equal("", _jobLog.MaxForeignID());
            _jobLog.AddStationCycles(new LogEntry[] { log1, log2 }, "foreign1", "the original message");
            Assert.Equal("foreign1", _jobLog.MaxForeignID());

            var load1 = _jobLog.StationLogByForeignID("foreign1");
            Assert.Equal(2, load1.Count);
            CheckEqual(log1, load1[0]);
            CheckEqual(log2, load1[1]);

            Assert.Equal("the original message", _jobLog.OriginalMessageByForeignID("foreign1"));
            Assert.Equal("", _jobLog.OriginalMessageByForeignID("abc"));
        }

        [Fact]
        public void PendingLoadOneSerialPerMat()
        {
            Assert.Equal(0, _jobLog.PendingLoads("pal1").Count);

            var mat1 = new LogMaterial(1, "unique", 1, "part1", 1, "face1");
            var mat2 = new LogMaterial(17, "unique2", 2, "part2", 2, "face2");
            var mat3 = new LogMaterial(25, "unique3", 3, "part3", 3, "face3");

            var serial1 = "0000000001";
            var serial2 = JobLogDB.ConvertToBase62(17).PadLeft(10, '0');
            var serial3 = JobLogDB.ConvertToBase62(25).PadLeft(10, '0');

            var t = DateTime.UtcNow.AddHours(-1);

            var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal1", LogType.GeneralMessage, "ABC", 1,
                                  "prog1", false, t, "result1", false, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(12));
            var log2 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal2", LogType.MachineCycle, "MC", 1,
                                  "prog2", false, t.AddMinutes(20), "result2", false, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(17));

            _jobLog.AddLogEntry(log1);
            _jobLog.AddLogEntry(log2);

            _jobLog.AddPendingLoad("pal1", "key1", 5, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38), "for1");
            _jobLog.AddPendingLoad("pal1", "key2", 7, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49), "for2");

            var pLoads = _jobLog.PendingLoads("pal1");
            Assert.Equal(2, pLoads.Count);
            Assert.Equal("pal1", pLoads[0].Pallet);
            Assert.Equal("key1", pLoads[0].Key);
            Assert.Equal(5, pLoads[0].LoadStation);
            Assert.Equal(TimeSpan.FromMinutes(32), pLoads[0].Elapsed);
            Assert.Equal("for1", pLoads[0].ForeignID);

            Assert.Equal("pal1", pLoads[1].Pallet);
            Assert.Equal("key2", pLoads[1].Key);
            Assert.Equal(7, pLoads[1].LoadStation);
            Assert.Equal(TimeSpan.FromMinutes(44), pLoads[1].Elapsed);
            Assert.Equal("for2", pLoads[1].ForeignID);

            var mat = new Dictionary<string, IEnumerable<LogMaterial>>();

            var palCycle = new LogEntry(0, new LogMaterial[] { }, "pal1",
              LogType.PalletCycle, "Pallet Cycle", 1, "", false,
              t.AddMinutes(45), "PalletCycle", false, TimeSpan.Zero, TimeSpan.Zero);

            mat["key1"] = new LogMaterial[] { mat1, mat2 };

            var nLoad1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                    "pal1", LogType.LoadUnloadCycle, "Load", 5, "", false,
                                    t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38));

            var ser1 = new LogEntry(0, new LogMaterial[] { mat1 },
                                  "pal1", LogType.PartMark, "Mark", 1, "MARK", false,
              t.AddMinutes(45).AddSeconds(2), serial1, false);

            var ser2 = new LogEntry(0, new LogMaterial[] { mat2 },
                                  "pal1", LogType.PartMark, "Mark", 1, "MARK", false,
              t.AddMinutes(45).AddSeconds(2), serial2, false);

            mat["key2"] = new LogMaterial[] { mat3 };

            var nLoad2 = new LogEntry(0, new LogMaterial[] { mat3 },
                                    "pal1", LogType.LoadUnloadCycle, "Load", 7, "", false,
                                    t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49));

            var ser3 = new LogEntry(0, new LogMaterial[] { mat3 },
                                  "pal1", LogType.PartMark, "Mark", 1, "MARK", false,
              t.AddMinutes(45).AddSeconds(2), serial3, false);

            _jobLog.SetSerialSettings(new SerialSettings(SerialType.OneSerialPerMaterial, 10));
            Assert.Equal(SerialType.OneSerialPerMaterial, _jobLog.GetSerialSettings().SerialType);
            Assert.Equal(10, _jobLog.GetSerialSettings().SerialLength);

            _jobLog.CompletePalletCycle("pal1", t.AddMinutes(45), "for3", mat);

            CheckLog(new LogEntry[] { log1, log2, palCycle, nLoad1, nLoad2, ser1, ser2, ser3 },
                     _jobLog.GetLogEntries(t.AddMinutes(-10), t.AddHours(1)), t.AddMinutes(-10));

            CheckEqual(nLoad1, _jobLog.StationLogByForeignID("for1")[0]);
            CheckEqual(nLoad2, _jobLog.StationLogByForeignID("for2")[0]);
            CheckEqual(palCycle, _jobLog.StationLogByForeignID("for3")[0]);

            Assert.Equal(0, _jobLog.PendingLoads("pal1").Count);
        }

        [Fact]
        public void PendingLoadOneSerialPerCycle()
        {
            var mat1 = new LogMaterial(6, "unique", 1, "part1", 1, "face1");
            var mat2 = new LogMaterial(17, "unique2", 2, "part2", 2, "face2");
            var mat3 = new LogMaterial(25, "unique3", 3, "part3", 3, "face3");

            var serial1 = "0000000006";
            var serial3 = JobLogDB.ConvertToBase62(25).PadLeft(10, '0');

            var t = DateTime.UtcNow.AddHours(-1);

            var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal1", LogType.GeneralMessage, "ABC", 1,
                                  "prog1", false, t, "result1", false, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(11));
            var log2 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal2", LogType.MachineCycle, "MC", 1,
                                  "prog2", false, t.AddMinutes(20), "result2", false, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(22));

            _jobLog.AddLogEntry(log1);
            _jobLog.AddLogEntry(log2);

            _jobLog.AddPendingLoad("pal1", "key1", 5, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38), "for1");
            _jobLog.AddPendingLoad("pal1", "key2", 7, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49), "for2");

            var mat = new Dictionary<string, IEnumerable<LogMaterial>>();

            var palCycle = new LogEntry(0, new LogMaterial[] { }, "pal1",
            LogType.PalletCycle, "Pallet Cycle", 1, "", false,
              t.AddMinutes(45), "PalletCycle", false, TimeSpan.Zero, TimeSpan.Zero);

            mat["key1"] = new LogMaterial[] { mat1, mat2 };

            var nLoad1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                    "pal1", LogType.LoadUnloadCycle, "Load", 5, "", false,
                                    t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38));

            var ser1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal1", LogType.PartMark, "Mark", 1, "MARK", false,
              t.AddMinutes(45).AddSeconds(2), serial1, false);

            mat["key2"] = new LogMaterial[] { mat3 };

            var nLoad2 = new LogEntry(0, new LogMaterial[] { mat3 },
                                    "pal1", LogType.LoadUnloadCycle, "Load", 7, "", false,
                                    t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49));

            var ser3 = new LogEntry(0, new LogMaterial[] { mat3 },
                                  "pal1", LogType.PartMark, "Mark", 1, "MARK", false,
              t.AddMinutes(45).AddSeconds(2), serial3, false);

            _jobLog.SetSerialSettings(new SerialSettings(SerialType.OneSerialPerCycle, 10));
            _jobLog.CompletePalletCycle("pal1", t.AddMinutes(45), "for3", mat);

            CheckLog(new LogEntry[] { log1, log2, palCycle, nLoad1, nLoad2, ser1, ser3 },
                     _jobLog.GetLogEntries(t.AddMinutes(-10), t.AddHours(1)), t.AddMinutes(-10));

            CheckEqual(nLoad1, _jobLog.StationLogByForeignID("for1")[0]);
            CheckEqual(nLoad2, _jobLog.StationLogByForeignID("for2")[0]);
            CheckEqual(palCycle, _jobLog.StationLogByForeignID("for3")[0]);

            Assert.Equal(0, _jobLog.PendingLoads("pal1").Count);
        }

		[Fact]
		public void WorkorderSummary()
		{
			var t = DateTime.UtcNow.AddHours(-1);

			//one material across two processes
			var mat1 = _jobLog.AllocateMaterialID("uniq1");
			var mat1_proc1 = new LogMaterial(mat1, "uniq1", 1, "part1", 2);
			var mat1_proc2 = new LogMaterial(mat1, "uniq1", 2, "part1", 2);
            var mat2_proc1 = new LogMaterial(_jobLog.AllocateMaterialID("uniq1"), "uniq1", 1, "part1", 2);

			//not adding all events, but at least one non-endofroute and one endofroute
			_jobLog.AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat1_proc1 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog1", false,
                             t.AddMinutes(5), "", false,
							 TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20))
			);
			_jobLog.AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat1_proc2 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog2", false,
                             t.AddMinutes(6), "", false,
							 TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(40))
			);
			_jobLog.AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat1_proc2, mat2_proc1 }, //mat2_proc1 should be ignored since it isn't final process
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "UNLOAD", false,
                             t.AddMinutes(7), "UNLOAD", true,
							 TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(60))
			);

			//four materials on the same pallet but different workorders
			var mat3 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2"), "uniq2", 1, "part1", 1);
			var mat4 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2"), "uniq2", 1, "part2", 1);
			var mat5 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2"), "uniq2", 1, "part3", 1);
			var mat6 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2"), "uniq2", 1, "part3", 1);

			_jobLog.AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat3, mat4, mat5, mat6 },
                             "pal1", LogType.MachineCycle, "MC", 5, "progdouble", false,
                             t.AddMinutes(15), "", false,
							 TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4))
			);
			_jobLog.AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat3, mat4, mat5, mat6 },
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "UNLOAD", false,
                             t.AddMinutes(17), "UNLOAD", true,
							 TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(6))
			);


			//now record serial and workorder
			_jobLog.RecordSerialForMaterialID(mat1_proc2, "serial1");
            _jobLog.RecordSerialForMaterialID(mat2_proc1, "serial2");
			_jobLog.RecordSerialForMaterialID(mat3, "serial3");
			_jobLog.RecordSerialForMaterialID(mat4, "serial4");
			_jobLog.RecordSerialForMaterialID(mat5, "serial5");
			_jobLog.RecordSerialForMaterialID(mat6, "serial6");

			_jobLog.RecordWorkorderForMaterialID(mat1_proc2, "work1");
			_jobLog.RecordWorkorderForMaterialID(mat3, "work1");
			_jobLog.RecordWorkorderForMaterialID(mat4, "work1");
			_jobLog.RecordWorkorderForMaterialID(mat5, "work2");
			_jobLog.RecordWorkorderForMaterialID(mat6, "work2");

			var summary = _jobLog.GetWorkorderSummaries(new [] {"work1", "work2"});
			Assert.Equal(2, summary.Count);

            //work1 contains part1 from mat1 and mat3, and part2 from mat4
			Assert.Equal("work1", summary[0].WorkorderId);
            Assert.False(summary[0].FinalizedTimeUTC.HasValue);
			Assert.Equal(new[] {"serial1", "serial3", "serial4"}, summary[0].Serials);
            var work1Parts = summary[0].Parts;
            Assert.Equal(2, work1Parts.Count);
            var work1Part1 = work1Parts[0];
            var work1Part2 = work1Parts[1];

            Assert.Equal("part1", work1Part1.Part);
            Assert.Equal(2, work1Part1.PartsCompleted); //mat1 and mat3
            Assert.Equal(2, work1Part1.ElapsedStationTime.Count);
            Assert.Equal(2, work1Part1.ActiveStationTime.Count);

            double c2Cnt = 4; //number of material on cycle 2

            //10 + 30 from mat1, 3*1/4 for mat3
            Assert.Equal(TimeSpan.FromMinutes(10 + 30 + 3*1/c2Cnt), work1Part1.ElapsedStationTime["MC"]);

            //50/2 from mat1_proc2, and 5*1/4 for mat3
            Assert.Equal(TimeSpan.FromMinutes(50 / 2 + 5*1/c2Cnt), work1Part1.ElapsedStationTime["Load"]);

            //20 + 40 from mat1, 4*1/4 for mat3
            Assert.Equal(TimeSpan.FromMinutes(20 + 40 + 4*1/c2Cnt), work1Part1.ActiveStationTime["MC"]);

            //60/2 from mat1_proc2, and 6*1/4 for mat3
            Assert.Equal(TimeSpan.FromMinutes(60/2 + 6*1/c2Cnt), work1Part1.ActiveStationTime["Load"]);

            Assert.Equal("part2", work1Part2.Part);
            Assert.Equal(1, work1Part2.PartsCompleted); //part2 is just mat4
            Assert.Equal(2, work1Part2.ElapsedStationTime.Count);
            Assert.Equal(2, work1Part2.ActiveStationTime.Count);
            Assert.Equal(TimeSpan.FromMinutes(3*1/c2Cnt), work1Part2.ElapsedStationTime["MC"]);
            Assert.Equal(TimeSpan.FromMinutes(5*1/c2Cnt), work1Part2.ElapsedStationTime["Load"]);
            Assert.Equal(TimeSpan.FromMinutes(4*1/c2Cnt), work1Part2.ActiveStationTime["MC"]);
            Assert.Equal(TimeSpan.FromMinutes(6*1/c2Cnt), work1Part2.ActiveStationTime["Load"]);

            //------- work2

			Assert.Equal("work2", summary[1].WorkorderId);
            Assert.False(summary[1].FinalizedTimeUTC.HasValue);
			Assert.Equal(new[] {"serial5", "serial6"}, summary[1].Serials);
            Assert.Equal(1, summary[1].Parts.Count);
            var work2Part2 = summary[1].Parts[0];
            Assert.Equal("part3", work2Part2.Part); //part3 is mat5 and 6
            Assert.Equal(2, work2Part2.PartsCompleted); //part3 is mat5 and 6
            Assert.Equal(2, work2Part2.ElapsedStationTime.Count);
            Assert.Equal(2, work2Part2.ActiveStationTime.Count);
            Assert.Equal(TimeSpan.FromMinutes(3*2/c2Cnt), work2Part2.ElapsedStationTime["MC"]);
            Assert.Equal(TimeSpan.FromMinutes(5*2/c2Cnt), work2Part2.ElapsedStationTime["Load"]);
            Assert.Equal(TimeSpan.FromMinutes(4*2/c2Cnt), work2Part2.ActiveStationTime["MC"]);
            Assert.Equal(TimeSpan.FromMinutes(6*2/c2Cnt), work2Part2.ActiveStationTime["Load"]);

            //---- test finalize
            var finalizedEntry = _jobLog.RecordFinalizedWorkorder("work1");
            Assert.Equal("", finalizedEntry.Pallet);
            Assert.Equal("work1", finalizedEntry.Result);
            Assert.Equal(LogType.FinalizeWorkorder, finalizedEntry.LogType);

            summary = _jobLog.GetWorkorderSummaries(new [] {"work1"});
            Assert.Equal("work1", summary[0].WorkorderId);
            Assert.True(summary[0].FinalizedTimeUTC.HasValue);
            Assert.InRange(summary[0].FinalizedTimeUTC.Value.Subtract(DateTime.UtcNow),
                TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1));
		}

        [Fact]
        public void LoadCompletedParts()
        {
			var old = DateTime.UtcNow.AddHours(-50);
            var recent = DateTime.UtcNow.AddHours(-1);

			//material
			var mat1 = _jobLog.AllocateMaterialID("uniq1");
			var mat1_proc1 = new LogMaterial(mat1, "uniq1", 1, "part1", 2);
			var mat1_proc2 = new LogMaterial(mat1, "uniq1", 2, "part1", 2);
            var mat2 = _jobLog.AllocateMaterialID("uniq1");
            var mat2_proc1 = new LogMaterial(mat2, "uniq1", 1, "part1", 2);
            var mat2_proc2 = new LogMaterial(mat2, "uniq1", 2, "part1", 2);

            var mat3 = new LogMaterial(_jobLog.AllocateMaterialID("uniq1"), "uniq1", 1, "part1", 1);
            var mat4 = new LogMaterial(_jobLog.AllocateMaterialID("uniq1"), "uniq1", 1, "part1", 1);

			//mat1 has proc1 in old, proc2 in recent so everything should be loaded
			var mat1_proc1old = AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat1_proc1 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog1", false,
                             old.AddMinutes(5), "", false,
							 TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20))
			);
            var mat1_proc1complete = AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat1_proc1 },
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "prog1", false,
                             old.AddMinutes(6), "", true,
							 TimeSpan.FromMinutes(11), TimeSpan.FromMinutes(21))
			);
			var mat1_proc2old = AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat1_proc2 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog2", false,
                             old.AddMinutes(7), "", false,
							 TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(22))
			);
			var mat1_proc2complete = AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat1_proc2 },
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "UNLOAD", false,
                             recent.AddMinutes(4), "UNLOAD", true,
							 TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(40))
			);

            //mat2 has everything in recent, (including proc1 complete) but no complete on proc2
            AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat2_proc1 },
                             "pal1", LogType.MachineCycle, "MC", 5, "mach2", false,
                             recent.AddMinutes(5), "", false,
							 TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(60))
			);
            AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat2_proc1 },
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "load2", false,
                             recent.AddMinutes(6), "", true,
							 TimeSpan.FromMinutes(51), TimeSpan.FromMinutes(61))
			);
			AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat2_proc2 },
                             "pal1", LogType.MachineCycle, "MC", 5, "mach2", false,
                             old.AddMinutes(7), "", false,
							 TimeSpan.FromMinutes(52), TimeSpan.FromMinutes(62))
			);

            //mat3 has everything in old
            AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat3 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog3", false,
                             old.AddMinutes(20), "", false,
							 TimeSpan.FromMinutes(70), TimeSpan.FromMinutes(80))
			);
            AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat3 },
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "load3", false,
                             old.AddMinutes(25), "", true,
							 TimeSpan.FromMinutes(71), TimeSpan.FromMinutes(81))
			);

            //mat4 has everything in new
            var mat4recent = AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat4 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog44", false,
                             recent.AddMinutes(40), "", false,
							 TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(100))
			);
            var mat4complete = AddLogEntry(
				new LogEntry(0, new LogMaterial[] { mat4 },
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "load4", false,
                             recent.AddMinutes(45), "", true,
							 TimeSpan.FromMinutes(91), TimeSpan.FromMinutes(101))
			);

            CheckLog(
              new [] {mat1_proc1old, mat1_proc1complete, mat1_proc2old, mat1_proc2complete,
                      mat4recent, mat4complete},
             _jobLog.GetCompletedPartLogs(recent.AddHours(-4), recent.AddHours(4)),
              DateTime.MinValue);
        }

        #region Helpers
        private LogEntry AddLogEntry(LogEntry l)
        {
            _jobLog.AddLogEntry(l);
            return l;
        }

        private System.DateTime AddLog(IList<LogEntry> logs)
        {
            System.DateTime last = default(System.DateTime);

            foreach (var l in logs)
            {
                _jobLog.AddLogEntry(l);

                if (l.EndTimeUTC > last)
                {
                    last = l.EndTimeUTC;
                }
            }

            return last;
        }

        private long CheckLog(IList<LogEntry> logs, IList<LogEntry> otherLogs, System.DateTime start)
        {
            long ctr = -1;

            //we need to check the material list
            foreach (LogEntry l in logs)
            {
                if (l.EndTimeUTC >= start)
                {
                    foreach (LogEntry l2 in otherLogs)
                    {
                        if (l2.Counter > ctr)
                        {
                            ctr = l2.Counter;
                        }
                        if (CheckEqual(l, l2))
                        {
                            otherLogs.Remove(l2);
                            goto found;
                        }
                    }
                    //not the same, could not find m
                    Assert.True(false, "Unable to find " + l.EndTimeUTC.ToString() + " " + l.Program);
                found:;
                }
            }

            Assert.Equal(0, otherLogs.Count);

            return ctr;
        }

        private bool CheckEqual(LogEntry x, LogEntry y)
        {
            //check all other for equality.  Used for testing purposes

            if (y.EndTimeUTC == x.EndTimeUTC &&
                y.Pallet == x.Pallet &&
                y.LogType.CompareTo(x.LogType) == 0 &&
                y.LocationName.CompareTo(x.LocationName) == 0 &&
                y.LocationNum.CompareTo(x.LocationNum) == 0 &&
                y.Program == x.Program && y.StartOfCycle == x.StartOfCycle &&
                y.Result == x.Result && y.EndOfRoute == x.EndOfRoute)
            {

                Assert.Equal(x.ActiveOperationTime, y.ActiveOperationTime);
                Assert.Equal(x.ElapsedTime, y.ElapsedTime);
                //we need to check the material list
                var matLst = new List<LogMaterial>(x.Material);
                foreach (var m in y.Material)
                {
                    foreach (var m2 in matLst)
                    {
                        if (CheckEqual(m, m2))
                        {
                            matLst.Remove(m2);
                            goto found;
                        }
                    }
                    //not the same, could not find m
                    return false;
                found:;
                }

                if (matLst.Count > 0)
                {
                    return false;
                }

                if (x.ProgramDetails.Count != y.ProgramDetails.Count) return false;
                foreach (var key in x.ProgramDetails.Keys)
                {
                    if (x.ProgramDetails[key] != y.ProgramDetails[key]) return false;
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        private bool CheckEqual(LogMaterial x, LogMaterial y)
        {
            if (x.MaterialID == y.MaterialID && x.JobUniqueStr == y.JobUniqueStr &&
                x.Process == y.Process && x.PartName == y.PartName &&
                x.NumProcesses == y.NumProcesses && x.Face == y.Face)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        #endregion
    }
}
