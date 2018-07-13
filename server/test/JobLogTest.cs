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
using System.Linq;
using Xunit;
using Microsoft.Data.Sqlite;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using FluentAssertions;
using AutoFixture;

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
            _jobLog.CreateTables(firstSerialOnEmpty: null);
        }

        public void Dispose()
        {
            _jobLog.Close();
        }

        [Fact]
        public void MaterialIDs()
        {
            long m1 = _jobLog.AllocateMaterialID("U1", "P1", 52);
            long m2 = _jobLog.AllocateMaterialID("U2", "P2", 66);
            long m3 = _jobLog.AllocateMaterialID("U3", "P3", 566);
            m1.Should().Be(1);
            m2.Should().Be(2);
            m3.Should().Be(3);

            _jobLog.GetMaterialDetails(m1).Should().BeEquivalentTo(new MaterialDetails() {
                MaterialID = m1,
                JobUnique = "U1",
                PartName = "P1",
                NumProcesses = 52,
            });

            _jobLog.GetMaterialDetails(m2).Should().BeEquivalentTo(new MaterialDetails() {
                MaterialID = m2,
                JobUnique = "U2",
                PartName = "P2",
                NumProcesses = 66,
            });

            _jobLog.GetMaterialDetails(m3).Should().BeEquivalentTo(new MaterialDetails() {
                MaterialID = m3,
                JobUnique = "U3",
                PartName = "P3",
                NumProcesses = 566,
            });

            long m4 = _jobLog.AllocateMaterialIDForCasting("P4", 44);
            _jobLog.GetMaterialDetails(m4).Should().BeEquivalentTo(new MaterialDetails() {
                MaterialID = m4,
                PartName = "P4",
                NumProcesses = 44,
            });

            _jobLog.SetDetailsForMaterialID(m4, "U4", "P4444", 77);
            _jobLog.GetMaterialDetails(m4).Should().BeEquivalentTo(new MaterialDetails() {
                MaterialID = m4,
                JobUnique = "U4",
                PartName = "P4444",
                NumProcesses = 77,
            });
        }

        [Fact]
        public void AddLog()
        {
            Assert.Equal(DateTime.MinValue, _jobLog.MaxLogDate());

            System.DateTime start = DateTime.UtcNow.AddHours(-10);

            List<LogEntry> logs = new List<LogEntry>();
            var logsForMat1 = new List<LogEntry>();

            LogMaterial mat1 = new LogMaterial(
                _jobLog.AllocateMaterialID("grgaegr", "pp2", 23), "grgaegr", 7, "pp2", 23, "face22");
            LogMaterial mat19 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique", "pp1", 53), "unique", 2, "pp1", 53, "face55");

            var loadStartActualCycle = _jobLog.RecordLoadStart(
                mats: new[] {mat1, mat19},
                pallet: "pal",
                lulNum: 2,
                timeUTC: start.AddHours(1)
            );
            loadStartActualCycle.Should().BeEquivalentTo(
                new LogEntry(
                    loadStartActualCycle.Counter, new LogMaterial[] { mat1, mat19 }, "pal",
                    LogType.LoadUnloadCycle, "L/U", 2,
                    "LOAD", true, start.AddHours(1), "LOAD", false)
            );
            logs.Add(loadStartActualCycle);
            logsForMat1.Add(loadStartActualCycle);

            var mat2 = new LogMaterial(
                _jobLog.AllocateMaterialID("ahre", "gewoiweg", 13), "ahre", 1, "gewoiweg", 13);
            var mat15 = new LogMaterial(
                _jobLog.AllocateMaterialID("qghr4e", "ppp532", 14), "qghr4e", 3, "ppp532", 14);

            var loadEndActualCycle = _jobLog.RecordLoadEnd(
                mats: new[] {mat2, mat15},
                pallet: "aaaa",
                lulNum: 16,
                timeUTC: start.AddHours(3),
                elapsed: TimeSpan.FromMinutes(52),
                active: TimeSpan.FromMinutes(25)
            );
            loadEndActualCycle.Should().BeEquivalentTo( new [] {
                new LogEntry(
                    loadEndActualCycle.First().Counter, new LogMaterial[] { mat2, mat15 }, "aaaa",
                    LogType.LoadUnloadCycle, "L/U", 16,
                    "LOAD", false, start.AddHours(3), "LOAD", false,
                    TimeSpan.FromMinutes(52), TimeSpan.FromMinutes(25))
            });
            logs.Add(loadEndActualCycle.First());

            var machineStartActualCycle = _jobLog.RecordMachineStart(
                mats: new[] {mat15},
                pallet: "rrrr",
                statName: "ssssss",
                statNum: 152,
                program: "progggg",
                timeUTC: start.AddHours(5).AddMinutes(10)
            );
            machineStartActualCycle.Should().BeEquivalentTo(
                new LogEntry(
                    machineStartActualCycle.Counter, new LogMaterial[] { mat15 }, "rrrr",
                    LogType.MachineCycle, "ssssss", 152,
                    "progggg", true, start.AddHours(5).AddMinutes(10), "", false)
            );
            logs.Add(machineStartActualCycle);

            var machineEndActualCycle = _jobLog.RecordMachineEnd(
                mats: new[] {mat2},
                pallet: "www",
                statName: "xxx",
                statNum: 177,
                program: "progggg",
                result: "4444",
                timeUTC: start.AddHours(5).AddMinutes(19),
                elapsed: TimeSpan.FromMinutes(12),
                active: TimeSpan.FromMinutes(99),
                extraData: new Dictionary<string, string>() {
                    {"aa", "AA"}, {"bb", "BB"}
                }
            );
            var machineEndExpectedCycle =
                new LogEntry(
                    machineEndActualCycle.Counter, new LogMaterial[] { mat2 }, "www",
                    LogType.MachineCycle, "xxx", 177,
                    "progggg", false, start.AddHours(5).AddMinutes(19), "4444", false,
                    TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(99));
            machineEndExpectedCycle.ProgramDetails["aa"] = "AA";
            machineEndExpectedCycle.ProgramDetails["bb"] = "BB";
            machineEndActualCycle.Should().BeEquivalentTo(machineEndExpectedCycle);
            logs.Add(machineEndActualCycle);

            var unloadStartActualCycle = _jobLog.RecordUnloadStart(
                mats: new[] {mat15, mat19},
                pallet: "rrr",
                lulNum: 87,
                timeUTC: start.AddHours(6).AddMinutes(10)
            );
            unloadStartActualCycle.Should().BeEquivalentTo(
                new LogEntry(
                    unloadStartActualCycle.Counter, new LogMaterial[] { mat15, mat19 }, "rrr",
                    LogType.LoadUnloadCycle, "L/U", 87,
                    "UNLOAD", true, start.AddHours(6).AddMinutes(10), "UNLOAD", false)
            );
            logs.Add(unloadStartActualCycle);

            var unloadEndActualCycle = _jobLog.RecordUnloadEnd(
                mats: new[] {mat2, mat19},
                pallet: "bb",
                lulNum: 14,
                timeUTC: start.AddHours(7),
                elapsed: TimeSpan.FromMinutes(152),
                active: TimeSpan.FromMinutes(55)
            );
            unloadEndActualCycle.Should().BeEquivalentTo( new [] {
                new LogEntry(
                    unloadEndActualCycle.First().Counter, new LogMaterial[] { mat2, mat19 }, "bb",
                    LogType.LoadUnloadCycle, "L/U", 14,
                    "UNLOAD", false, start.AddHours(7), "UNLOAD", true,
                    TimeSpan.FromMinutes(152), TimeSpan.FromMinutes(55))
            });
            logs.Add(unloadEndActualCycle.First());


            // ----- check loading of logs -----

            Assert.Equal(start.AddHours(7), _jobLog.MaxLogDate());

            IList<LogEntry> otherLogs = null;

            otherLogs = _jobLog.GetLogEntries(start, DateTime.UtcNow);
            CheckLog(logs, otherLogs, start);

            otherLogs = _jobLog.GetLogEntries(start.AddHours(5), DateTime.UtcNow);
            CheckLog(logs, otherLogs, start.AddHours(5));

            otherLogs = _jobLog.GetLog(loadEndActualCycle.First().Counter);
            CheckLog(logs, otherLogs, start.AddHours(5));

            otherLogs = _jobLog.GetLog(unloadStartActualCycle.Counter);
            CheckLog(logs, otherLogs, start.AddHours(6.5));

            otherLogs = _jobLog.GetLog(unloadEndActualCycle.First().Counter);
            Assert.Equal(0, otherLogs.Count);

            foreach (var c in logs)
                Assert.True(_jobLog.CycleExists(c.EndTimeUTC, c.Pallet, c.LogType, c.LocationName, c.LocationNum), "Checking " + c.EndTimeUTC.ToString());

            Assert.False(_jobLog.CycleExists(
                DateTime.Parse("4/6/2011"), "Pallll", LogType.MachineCycle, "MC", 3));

            CheckLog(logsForMat1, _jobLog.GetLogForMaterial(1), start);
            Assert.Equal(_jobLog.GetLogForMaterial(18).Count, 0);

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

            CheckLog(logsForMat1, _jobLog.GetLogForJobUnique(mat1.JobUniqueStr), start);
            Assert.Equal(_jobLog.GetLogForJobUnique("sofusadouf").Count, 0);

            //inspection and wash
            var inspCompLog = _jobLog.RecordInspectionCompleted(
                mat1, 5, "insptype1", true, new Dictionary<string, string> {{"a", "aaa"}, {"b", "bbb"}},
                TimeSpan.FromMinutes(100), TimeSpan.FromMinutes(5));
            var expectedInspLog = new LogEntry(-1, new LogMaterial[] { mat1 }, "",
                LogType.InspectionResult, "Inspection", 5, "insptype1", false, inspCompLog.EndTimeUTC, "True", false,
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

            CheckLog(logsForMat1, _jobLog.GetLogForJobUnique(mat1.JobUniqueStr), start);
        }

        [Fact]
        public void LookupByPallet()
        {
            Assert.Equal(0, _jobLog.CurrentPalletLog("pal1").Count);
            Assert.Equal(0, _jobLog.CurrentPalletLog("pal2").Count);
            Assert.Equal(DateTime.MinValue, _jobLog.LastPalletCycleTime("pal1"));

            var pal1Initial = new List<LogEntry>();
            var pal1Cycle = new List<LogEntry>();
            var pal2Cycle = new List<LogEntry>();

            var mat1 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique", "part1", 2), "unique", 1, "part1", 2, "face1");
            var mat2 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique2", "part2", 2), "unique2", 2, "part2", 2, "face2");

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

            _jobLog.AddLogEntryFromUnitTest(pal1Cycle[pal1Cycle.Count - 1]);

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
            var mat1 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique", "part1", 2), "unique", 1, "part1", 2, "face1");
            var mat2 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique2", "part2", 2), "unique2", 2, "part2", 2, "face2");

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
            _jobLog.AddLogEntryFromUnitTest(log1, "for1");
            Assert.Equal("for1", _jobLog.MaxForeignID());
            _jobLog.AddLogEntryFromUnitTest(log2, "for2");
            Assert.Equal("for2", _jobLog.MaxForeignID());
            _jobLog.AddLogEntryFromUnitTest(log3);
            Assert.Equal("for2", _jobLog.MaxForeignID());
            _jobLog.AddPendingLoad("p", "k", 1, TimeSpan.Zero, TimeSpan.Zero, "for4");
            Assert.Equal("for4", _jobLog.MaxForeignID());
            var mat = new Dictionary<string, IEnumerable<LogMaterial>>();
            mat["k"] = new LogMaterial[] { };
            _jobLog.CompletePalletCycle("p", DateTime.UtcNow, "for3", mat, SerialType.NoAutomaticSerials, 10);
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
            var mat1 = new LogMaterial(
                _jobLog.AllocateMaterialID("uniqqqq", "pppart66", 5), "uniqqqq", 1, "pppart66", 5, "facce");
            var mat2 = new LogMaterial(
                _jobLog.AllocateMaterialID("uuuuuniq", "part5", 2), "uuuuuniq", 2, "part5", 2, "face2");

            var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "palllet16", LogType.GeneralMessage, "Hello", 5,
                                  "program125", false, DateTime.UtcNow, "result66", false, TimeSpan.FromMinutes(166), TimeSpan.FromMinutes(74));

            Assert.Equal("", _jobLog.MaxForeignID());
            _jobLog.AddLogEntryFromUnitTest(log1, "foreign1", "the original message");
            Assert.Equal("foreign1", _jobLog.MaxForeignID());

            var load1 = _jobLog.StationLogByForeignID("foreign1");
            Assert.Equal(1, load1.Count);
            CheckEqual(log1, load1[0]);

            Assert.Equal("the original message", _jobLog.OriginalMessageByForeignID("foreign1"));
            Assert.Equal("", _jobLog.OriginalMessageByForeignID("abc"));
        }

        [Fact]
        public void PendingLoadOneSerialPerMat()
        {
            Assert.Equal(0, _jobLog.PendingLoads("pal1").Count);

            var mat1 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique", "part1", 1), "unique", 1, "part1", 1, "face1");
            var mat2 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique2", "part2", 2), "unique2", 2, "part2", 2, "face2");
            var mat3 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique3", "part3", 3), "unique3", 3, "part3", 3, "face3");

            var serial1 = JobLogDB.ConvertToBase62(mat1.MaterialID).PadLeft(10, '0');
            var serial2 = JobLogDB.ConvertToBase62(mat2.MaterialID).PadLeft(10, '0');
            var serial3 = JobLogDB.ConvertToBase62(mat3.MaterialID).PadLeft(10, '0');

            var t = DateTime.UtcNow.AddHours(-1);

            var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal1", LogType.GeneralMessage, "ABC", 1,
                                  "prog1", false, t, "result1", false, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(12));
            var log2 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal2", LogType.MachineCycle, "MC", 1,
                                  "prog2", false, t.AddMinutes(20), "result2", false, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(17));

            _jobLog.AddLogEntryFromUnitTest(log1);
            _jobLog.AddLogEntryFromUnitTest(log2);

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
                                    "pal1", LogType.LoadUnloadCycle, "L/U", 5, "LOAD", false,
                                    t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38));

            var ser1 = new LogEntry(0, new LogMaterial[] { mat1 },
                                  "", LogType.PartMark, "Mark", 1, "MARK", false,
              t.AddMinutes(45).AddSeconds(2), serial1, false);

            var ser2 = new LogEntry(0, new LogMaterial[] { mat2 },
                                  "", LogType.PartMark, "Mark", 1, "MARK", false,
              t.AddMinutes(45).AddSeconds(2), serial2, false);

            mat["key2"] = new LogMaterial[] { mat3 };

            var nLoad2 = new LogEntry(0, new LogMaterial[] { mat3 },
                                    "pal1", LogType.LoadUnloadCycle, "L/U", 7, "LOAD", false,
                                    t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49));

            var ser3 = new LogEntry(0, new LogMaterial[] { mat3 },
                                  "", LogType.PartMark, "Mark", 1, "MARK", false,
              t.AddMinutes(45).AddSeconds(2), serial3, false);

            _jobLog.CompletePalletCycle("pal1", t.AddMinutes(45), "for3", mat, SerialType.AssignOneSerialPerMaterial, 10);

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
            var mat1 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique", "part1", 1), "unique", 1, "part1", 1, "face1");
            var mat2 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique2", "part2", 2), "unique2", 2, "part2", 2, "face2");
            var mat3 = new LogMaterial(
                _jobLog.AllocateMaterialID("unique3", "part3", 3), "unique3", 3, "part3", 3, "face3");

            var serial1 = JobLogDB.ConvertToBase62(mat1.MaterialID).PadLeft(10, '0');
            var serial3 = JobLogDB.ConvertToBase62(mat3.MaterialID).PadLeft(10, '0');

            var t = DateTime.UtcNow.AddHours(-1);

            var log1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal1", LogType.GeneralMessage, "ABC", 1,
                                  "prog1", false, t, "result1", false, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(11));
            var log2 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "pal2", LogType.MachineCycle, "MC", 1,
                                  "prog2", false, t.AddMinutes(20), "result2", false, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(22));

            _jobLog.AddLogEntryFromUnitTest(log1);
            _jobLog.AddLogEntryFromUnitTest(log2);

            _jobLog.AddPendingLoad("pal1", "key1", 5, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38), "for1");
            _jobLog.AddPendingLoad("pal1", "key2", 7, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49), "for2");

            var mat = new Dictionary<string, IEnumerable<LogMaterial>>();

            var palCycle = new LogEntry(0, new LogMaterial[] { }, "pal1",
            LogType.PalletCycle, "Pallet Cycle", 1, "", false,
              t.AddMinutes(45), "PalletCycle", false, TimeSpan.Zero, TimeSpan.Zero);

            mat["key1"] = new LogMaterial[] { mat1, mat2 };

            var nLoad1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                    "pal1", LogType.LoadUnloadCycle, "L/U", 5, "LOAD", false,
                                    t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(32), TimeSpan.FromMinutes(38));

            var ser1 = new LogEntry(0, new LogMaterial[] { mat1, mat2 },
                                  "", LogType.PartMark, "Mark", 1, "MARK", false,
              t.AddMinutes(45).AddSeconds(2), serial1, false);

            mat["key2"] = new LogMaterial[] { mat3 };

            var nLoad2 = new LogEntry(0, new LogMaterial[] { mat3 },
                                    "pal1", LogType.LoadUnloadCycle, "L/U", 7, "LOAD", false,
                                    t.AddMinutes(45).AddSeconds(1), "LOAD", false, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(49));

            var ser3 = new LogEntry(0, new LogMaterial[] { mat3 },
                                  "", LogType.PartMark, "Mark", 1, "MARK", false,
              t.AddMinutes(45).AddSeconds(2), serial3, false);

            _jobLog.CompletePalletCycle("pal1", t.AddMinutes(45), "for3", mat, SerialType.AssignOneSerialPerCycle, 10);

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
			var mat1 = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
			var mat1_proc1 = new LogMaterial(mat1, "uniq1", 1, "part1", 2);
			var mat1_proc2 = new LogMaterial(mat1, "uniq1", 2, "part1", 2);
            var mat2_proc1 = new LogMaterial(_jobLog.AllocateMaterialID("uniq1", "part1", 2), "uniq1", 1, "part1", 2);

			//not adding all events, but at least one non-endofroute and one endofroute
			_jobLog.AddLogEntryFromUnitTest(
				new LogEntry(0, new LogMaterial[] { mat1_proc1 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog1", false,
                             t.AddMinutes(5), "", false,
							 TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20))
			);
			_jobLog.AddLogEntryFromUnitTest(
				new LogEntry(0, new LogMaterial[] { mat1_proc2 },
                             "pal1", LogType.MachineCycle, "MC", 5, "prog2", false,
                             t.AddMinutes(6), "", false,
							 TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(40))
			);
			_jobLog.AddLogEntryFromUnitTest(
				new LogEntry(0, new LogMaterial[] { mat1_proc2, mat2_proc1 }, //mat2_proc1 should be ignored since it isn't final process
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "UNLOAD", false,
                             t.AddMinutes(7), "UNLOAD", false,
							 TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(60))
			);

			//four materials on the same pallet but different workorders
			var mat3 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2", "part1", 1), "uniq2", 1, "part1", 1);
			var mat4 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2", "part2", 1), "uniq2", 1, "part2", 1);
			var mat5 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2", "part3", 1), "uniq2", 1, "part3", 1);
			var mat6 = new LogMaterial(_jobLog.AllocateMaterialID("uniq2", "part3", 1), "uniq2", 1, "part3", 1);

			_jobLog.AddLogEntryFromUnitTest(
				new LogEntry(0, new LogMaterial[] { mat3, mat4, mat5, mat6 },
                             "pal1", LogType.MachineCycle, "MC", 5, "progdouble", false,
                             t.AddMinutes(15), "", false,
							 TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4))
			);
			_jobLog.AddLogEntryFromUnitTest(
				new LogEntry(0, new LogMaterial[] { mat3, mat4, mat5, mat6 },
                             "pal1", LogType.LoadUnloadCycle, "Load", 5, "UNLOAD", false,
                             t.AddMinutes(17), "UNLOAD", false,
							 TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(6))
			);


			//now record serial and workorder
			_jobLog.RecordSerialForMaterialID(mat1_proc2, "serial1");
            _jobLog.RecordSerialForMaterialID(mat2_proc1, "serial2");
			_jobLog.RecordSerialForMaterialID(mat3, "serial3");
			_jobLog.RecordSerialForMaterialID(mat4, "serial4");
			_jobLog.RecordSerialForMaterialID(mat5, "serial5");
			_jobLog.RecordSerialForMaterialID(mat6, "serial6");
            Assert.Equal("serial1", _jobLog.GetMaterialDetails(mat1_proc2.MaterialID).Serial);

			_jobLog.RecordWorkorderForMaterialID(mat1_proc2, "work1");
			_jobLog.RecordWorkorderForMaterialID(mat3, "work1");
			_jobLog.RecordWorkorderForMaterialID(mat4, "work1");
			_jobLog.RecordWorkorderForMaterialID(mat5, "work2");
			_jobLog.RecordWorkorderForMaterialID(mat6, "work2");
            Assert.Equal("work2", _jobLog.GetMaterialDetails(mat5.MaterialID).Workorder);

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
			var mat1 = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
			var mat1_proc1 = new LogMaterial(mat1, "uniq1", 1, "part1", 2);
			var mat1_proc2 = new LogMaterial(mat1, "uniq1", 2, "part1", 2);
            var mat2 = _jobLog.AllocateMaterialID("uniq1", "part1", 2);
            var mat2_proc1 = new LogMaterial(mat2, "uniq1", 1, "part1", 2);
            var mat2_proc2 = new LogMaterial(mat2, "uniq1", 2, "part1", 2);

            var mat3 = new LogMaterial(_jobLog.AllocateMaterialID("uniq1", "part1", 1), "uniq1", 1, "part1", 1);
            var mat4 = new LogMaterial(_jobLog.AllocateMaterialID("uniq1", "part1", 1), "uniq1", 1, "part1", 1);

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
                             old.AddMinutes(6), "", false,
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
                             recent.AddMinutes(4), "UNLOAD", false,
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
                             recent.AddMinutes(6), "UNLOAD", false,
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
                             old.AddMinutes(25), "UNLOAD", false,
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
                             recent.AddMinutes(45), "UNLOAD", false,
							 TimeSpan.FromMinutes(91), TimeSpan.FromMinutes(101))
			);

            CheckLog(
              new [] {mat1_proc1old, mat1_proc1complete, mat1_proc2old, mat1_proc2complete,
                      mat4recent, mat4complete},
             _jobLog.GetCompletedPartLogs(recent.AddHours(-4), recent.AddHours(4)),
              DateTime.MinValue);
        }

        [Fact]
        public void Queues()
        {
            var start = DateTime.UtcNow.AddHours(-10);

            var otherQueueMat = new LogMaterial(100, "uniq100", 100, "part100", 100);
            _jobLog.CreateMaterialID(100, "uniq100", "part100", 100);
            _jobLog.RecordAddMaterialToQueue(otherQueueMat, "BBBB", 0, start.AddHours(-1))
                .Should().BeEquivalentTo(new [] {AddToQueueExpectedEntry(otherQueueMat, 1, "BBBB", 0, start.AddHours(-1))});


            var expectedLogs = new List<LogEntry>();

            var mat1 = new LogMaterial(1, "uniq1", 15, "part111", 19);
            _jobLog.CreateMaterialID(1, "uniq1", "part111", 19);
            var mat2 = new LogMaterial(2, "uniq2", 1, "part2", 22);
            _jobLog.CreateMaterialID(2, "uniq2", "part2", 22);
            var mat3 = new LogMaterial(3, "uniq3", 3, "part3", 36);
            _jobLog.CreateMaterialID(3, "uniq3", "part3", 36);

            // add via LogMaterial with position -1
            _jobLog.RecordAddMaterialToQueue(mat1, "AAAA", -1, start)
                .Should().BeEquivalentTo(new [] {AddToQueueExpectedEntry(mat1, 2, "AAAA", -1, start)});
            expectedLogs.Add(AddToQueueExpectedEntry(mat1, 2, "AAAA", -1, start));

            _jobLog.GetMaterialInQueue("AAAA")
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartName = "part111", NumProcesses = 19}
                });

            //adding with LogMaterial with position -1 and existing queue
            _jobLog.RecordAddMaterialToQueue(mat2, "AAAA", -1, start.AddMinutes(10))
                .Should().BeEquivalentTo(new [] {AddToQueueExpectedEntry(mat2, 3, "AAAA", -1, start.AddMinutes(10))});
            expectedLogs.Add(AddToQueueExpectedEntry(mat2, 3, "AAAA", -1, start.AddMinutes(10)));

            _jobLog.GetMaterialInQueue("AAAA")
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartName = "part111", NumProcesses = 19},
                    new JobLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 1, Unique = "uniq2", PartName = "part2", NumProcesses = 22}
                });


            //inserting into queue with LogMaterial
            _jobLog.RecordAddMaterialToQueue(mat3, "AAAA", 1, start.AddMinutes(20))
                .Should().BeEquivalentTo(new [] {AddToQueueExpectedEntry(mat3, 4, "AAAA", 1, start.AddMinutes(20))});
            expectedLogs.Add(AddToQueueExpectedEntry(mat3, 4, "AAAA", 1, start.AddMinutes(20)));

            _jobLog.GetMaterialInQueue("AAAA")
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartName = "part111", NumProcesses = 19},
                    new JobLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 1, Unique = "uniq3", PartName = "part3", NumProcesses = 36},
                    new JobLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 2, Unique = "uniq2", PartName = "part2", NumProcesses = 22}
                });
            _jobLog.GetMaterialInAllQueues()
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartName = "part111", NumProcesses = 19},
                    new JobLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 1, Unique = "uniq3", PartName = "part3", NumProcesses = 36},
                    new JobLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 2, Unique = "uniq2", PartName = "part2", NumProcesses = 22},
                    new JobLogDB.QueuedMaterial() { MaterialID = 100, Queue = "BBBB", Position = 0, Unique = "uniq100", PartName = "part100", NumProcesses = 100}
                });

            //removing from queue with LogMaterial
            _jobLog.RecordRemoveMaterialFromAllQueues(mat3, start.AddMinutes(30))
                .Should().BeEquivalentTo(new [] {RemoveFromQueueExpectedEntry(mat3, 5, "AAAA", 1, start.AddMinutes(30))});
            expectedLogs.Add(RemoveFromQueueExpectedEntry(mat3, 5, "AAAA", 1, start.AddMinutes(30)));

            _jobLog.GetMaterialInQueue("AAAA")
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartName = "part111", NumProcesses = 19},
                    new JobLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 1, Unique = "uniq2", PartName = "part2", NumProcesses = 22}
                });


            //add back in with matid only
            _jobLog.RecordAddMaterialToQueue(mat3.MaterialID, mat3.Process, "AAAA", 2, start.AddMinutes(40))
                .Should().BeEquivalentTo(new [] {AddToQueueExpectedEntry(mat3, 6, "AAAA", 2, start.AddMinutes(40))});
            expectedLogs.Add(AddToQueueExpectedEntry(mat3, 6, "AAAA", 2, start.AddMinutes(40)));

            _jobLog.GetMaterialInQueue("AAAA")
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartName = "part111", NumProcesses = 19},
                    new JobLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 1, Unique = "uniq2", PartName = "part2", NumProcesses = 22},
                    new JobLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 2, Unique = "uniq3", PartName = "part3", NumProcesses = 36}
                });

            //add also removes from queue (rearrange material 1)
            _jobLog.RecordAddMaterialToQueue(mat1, "AAAA", 2, start.AddMinutes(50))
                .Should().BeEquivalentTo(new [] {
                    RemoveFromQueueExpectedEntry(mat1, 7, "AAAA", 0, start.AddMinutes(50)),
                    AddToQueueExpectedEntry(mat1, 8, "AAAA", 2, start.AddMinutes(50))
                });
            expectedLogs.Add(RemoveFromQueueExpectedEntry(mat1, 7, "AAAA", 0, start.AddMinutes(50)));
            expectedLogs.Add(AddToQueueExpectedEntry(mat1, 8, "AAAA", 2, start.AddMinutes(50)));

            _jobLog.GetMaterialInQueue("AAAA")
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 0, Unique = "uniq2", PartName = "part2", NumProcesses = 22},
                    new JobLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 1, Unique = "uniq3", PartName = "part3", NumProcesses = 36},
                    new JobLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 2, Unique = "uniq1", PartName = "part111", NumProcesses = 19}
                });

            //removing from queue with matid
            _jobLog.RecordRemoveMaterialFromAllQueues(mat2.MaterialID, start.AddMinutes(60))
                .Should().BeEquivalentTo(new [] {RemoveFromQueueExpectedEntry(mat2, 9, "AAAA", 0, start.AddMinutes(60))});
            expectedLogs.Add(RemoveFromQueueExpectedEntry(mat2, 9, "AAAA", 0, start.AddMinutes(60)));

            _jobLog.GetMaterialInQueue("AAAA")
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 0, Unique = "uniq3", PartName = "part3", NumProcesses = 36},
                    new JobLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 1, Unique = "uniq1", PartName = "part111", NumProcesses = 19}
                });


            _jobLog.GetLogEntries(start, DateTime.UtcNow)
                .Should().BeEquivalentTo(expectedLogs);
        }

        [Fact]
        public void LoadUnloadIntoQueues()
        {
            var start = DateTime.UtcNow.AddHours(-10);
            var expectedLogs = new List<LogEntry>();

            var mat1 = new LogMaterial(1, "uniq1", 15, "part111", 19);
            _jobLog.CreateMaterialID(1, "uniq1", "part111", 19);
            var mat2 = new LogMaterial(2, "uniq2", 1, "part2", 22);
            _jobLog.CreateMaterialID(2, "uniq2", "part2", 22);
            var mat3 = new LogMaterial(3, "uniq3", 3, "part3", 36);
            _jobLog.CreateMaterialID(3, "uniq3", "part3", 36);
            var mat4 = new LogMaterial(4, "uniq4", 4, "part4", 47);
            _jobLog.CreateMaterialID(4, "uniq4", "part4", 47);

            // add two material into queue 1
            _jobLog.RecordAddMaterialToQueue(mat1, "AAAA", -1, start);
            expectedLogs.Add(AddToQueueExpectedEntry(mat1, 1, "AAAA", -1, start));
            _jobLog.RecordAddMaterialToQueue(mat2, "AAAA", -1, start);
            expectedLogs.Add(AddToQueueExpectedEntry(mat2, 2, "AAAA", -1, start));

            _jobLog.GetMaterialInQueue("AAAA")
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 0, Unique = "uniq1", PartName = "part111", NumProcesses = 19},
                    new JobLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 1, Unique = "uniq2", PartName = "part2", NumProcesses = 22}
                });


            // loading should remove from queue
            var loadEndActual = _jobLog.RecordLoadEnd(new[] {mat1}, "pal1", 16, start.AddMinutes(10), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20));
            loadEndActual.Should().BeEquivalentTo(new [] {
                new LogEntry(3, new [] {mat1}, "pal1",
                    LogType.LoadUnloadCycle, "L/U", 16,
                    "LOAD", false, start.AddMinutes(10), "LOAD", false,
                    TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20)),

                RemoveFromQueueExpectedEntry(mat1, 4, "AAAA", 0, start.AddMinutes(10))
            });
            expectedLogs.AddRange(loadEndActual);

            _jobLog.GetMaterialInQueue("AAAA")
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 0, Unique = "uniq2", PartName = "part2", NumProcesses = 22}
                });

            //unloading should add to queue
            var unloadEndActual = _jobLog.RecordUnloadEnd(
                new[] {mat1, mat3, mat4}, "pal5", 77, start.AddMinutes(30), TimeSpan.FromMinutes(52), TimeSpan.FromMinutes(23),
                new Dictionary<long, string>() { {1, "AAAA"}, {3, "AAAA"}});
            unloadEndActual.Should().BeEquivalentTo(new [] {
                new LogEntry(7, new [] {mat1, mat3, mat4}, "pal5",
                    LogType.LoadUnloadCycle, "L/U", 77,
                    "UNLOAD", false, start.AddMinutes(30), "UNLOAD", true,
                    TimeSpan.FromMinutes(52), TimeSpan.FromMinutes(23)),

                AddToQueueExpectedEntry(mat1, 5, "AAAA", -1, start.AddMinutes(30)),
                AddToQueueExpectedEntry(mat3, 6, "AAAA", -1, start.AddMinutes(30)),
            });
            expectedLogs.AddRange(unloadEndActual);

            _jobLog.GetMaterialInQueue("AAAA")
                .Should().BeEquivalentTo(new [] {
                    new JobLogDB.QueuedMaterial() { MaterialID = 2, Queue = "AAAA", Position = 0, Unique = "uniq2", PartName = "part2", NumProcesses = 22},
                    new JobLogDB.QueuedMaterial() { MaterialID = 1, Queue = "AAAA", Position = 1, Unique = "uniq1", PartName = "part111", NumProcesses = 19},
                    new JobLogDB.QueuedMaterial() { MaterialID = 3, Queue = "AAAA", Position = 2, Unique = "uniq3", PartName = "part3", NumProcesses = 36}
                });


            _jobLog.GetLogEntries(start, DateTime.UtcNow)
                .Should().BeEquivalentTo(expectedLogs);
        }

        [Fact]
        public void AllocateCastingsFromQueues()
        {
            var mat1 = new LogMaterial(
                _jobLog.AllocateMaterialIDForCasting("part1", 5), "", 2, "part1", 5);
            var mat2 = new LogMaterial(
                _jobLog.AllocateMaterialIDForCasting("part1", 5), "", 1, "part1", 5);
            var mat3 = new LogMaterial(
                _jobLog.AllocateMaterialIDForCasting("part3", 3), "", 3, "part3", 3);

            _jobLog.RecordAddMaterialToQueue(mat1, "queue1", 0);
            _jobLog.RecordAddMaterialToQueue(mat2, "queue1", 1);
            _jobLog.RecordAddMaterialToQueue(mat3, "queue1", 2);

            _jobLog.AllocateCastingsInQueue("queue1", "part5", "uniqAAA", numProcesses: 15, maxCount: 2)
                .Should().BeEquivalentTo(new long[] {});

            _jobLog.AllocateCastingsInQueue("queue1", "part1", "uniqAAA", numProcesses: 6312, maxCount: 2)
                .Should().BeEquivalentTo(new[] {mat1.MaterialID, mat2.MaterialID});

            _jobLog.GetMaterialDetails(mat1.MaterialID).Should().BeEquivalentTo(new MaterialDetails() {
                MaterialID = mat1.MaterialID,
                JobUnique = "uniqAAA",
                PartName = "part1",
                NumProcesses = 6312,
            });

            _jobLog.GetMaterialDetails(mat2.MaterialID).Should().BeEquivalentTo(new MaterialDetails() {
                MaterialID = mat2.MaterialID,
                JobUnique = "uniqAAA",
                PartName = "part1",
                NumProcesses = 6312,
            });
        }

        #region Helpers
        private LogEntry AddLogEntry(LogEntry l)
        {
            _jobLog.AddLogEntryFromUnitTest(l);
            return l;
        }

        private System.DateTime AddLog(IList<LogEntry> logs)
        {
            System.DateTime last = default(System.DateTime);

            foreach (var l in logs)
            {
                _jobLog.AddLogEntryFromUnitTest(l);

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

        private LogEntry AddToQueueExpectedEntry(LogMaterial mat, long cntr, string queue, int position, DateTime timeUTC)
        {
            return new LogEntry(
                cntr: cntr,
                mat: new [] {mat},
                pal: "",
                ty: LogType.AddToQueue,
                locName: queue,
                locNum: position,
                prog: "",
                start: false,
                endTime: timeUTC,
                result: "",
                endOfRoute: false);
        }

        private LogEntry RemoveFromQueueExpectedEntry(LogMaterial mat, long cntr, string queue, int position, DateTime timeUTC)
        {
            return new LogEntry(
                cntr: cntr,
                mat: new [] {mat},
                pal: "",
                ty: LogType.RemoveFromQueue,
                locName: queue,
                locNum: position,
                prog: "",
                start: false,
                endTime: timeUTC,
                result: "",
                endOfRoute: false);
        }
        #endregion
    }

    public class LogStartingMaterialIDSpec : IDisposable
    {
        private JobLogDB _jobLog;

        public LogStartingMaterialIDSpec()
        {
            var connection = BlackMaple.MachineFramework.SqliteExtensions.ConnectMemory();
            connection.Open();
            _jobLog = new JobLogDB(connection);
            _jobLog.CreateTables(firstSerialOnEmpty: "AbCd12");
        }

        public void Dispose()
        {
            _jobLog.Close();
        }

        [Fact]
        public void ConvertSerials()
        {
            var fixture = new Fixture();
            var matId = fixture.Create<long>();
            JobLogDB.ConvertFromBase62(JobLogDB.ConvertToBase62(matId)).Should().Be(matId);
        }


        [Fact]
        public void MaterialIDs()
        {
            long m1 = _jobLog.AllocateMaterialID("U1", "P1", 52);
            long m2 = _jobLog.AllocateMaterialID("U2", "P2", 66);
            long m3 = _jobLog.AllocateMaterialID("U3", "P3", 566);
            m1.Should().Be(33152428148);
            m2.Should().Be(33152428149);
            m3.Should().Be(33152428150);

            _jobLog.GetMaterialDetails(m1).Should().BeEquivalentTo(new MaterialDetails() {
                MaterialID = m1,
                JobUnique = "U1",
                PartName = "P1",
                NumProcesses = 52,
            });
        }
    }
}
