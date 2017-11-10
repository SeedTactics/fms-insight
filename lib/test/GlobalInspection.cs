using System;
using System.Collections.Generic;
using Xunit;
using Microsoft.Data.Sqlite;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using FluentAssertions;

namespace MachineWatchTest
{
    public class GlobalInspectionTest : IDisposable
    {
        private JobLogDB _jobLog;
        private InspectionDB _insp;

        public GlobalInspectionTest()
        {
            var logConn = new SqliteConnection("Data Source=:memory:");
            logConn.Open();
            _jobLog = new JobLogDB(logConn);
            _jobLog.CreateTables();

            var inspConn = new SqliteConnection("Data Source=:memory:");
            inspConn.Open();
            _insp = new InspectionDB(_jobLog, inspConn);
            _insp.CreateTables();
        }

        public void Dispose()
        {
            _jobLog.Close();
            _insp.Close();
        }

        [Fact]
        public void Empty()
        {
            Assert.Empty(_insp.LoadAllGlobalInspections());
            Assert.Empty(_insp.LoadGlobalInspectionTypes());
            Assert.Null(_insp.LoadGlobalInspectionType("abc"));
            _insp.DeleteGlobalInspectionType("a");
        }

        [Fact]
        public void LoadAndSave()
        {
            var rand = new Random();

            var ty1 = RandInspType("ty1");

            _insp.SetGlobalInspectionType(ty1);

            _insp.LoadGlobalInspectionTypes()
              .ShouldAllBeEquivalentTo(new string[] {"ty1"});
            _insp.LoadAllGlobalInspections()
              .ShouldAllBeEquivalentTo(new InspectionType[] {ty1});
            _insp.LoadGlobalInspectionType("ty1")
              .ShouldBeEquivalentTo(ty1);

            //add second
            var ty2 = RandInspType("ty2");
            _insp.SetGlobalInspectionType(ty2);

            _insp.LoadGlobalInspectionTypes()
              .ShouldAllBeEquivalentTo(new string[] {"ty1", "ty2"});
            _insp.LoadAllGlobalInspections()
              .ShouldAllBeEquivalentTo(new InspectionType[] {ty1, ty2});
            _insp.LoadGlobalInspectionType("ty1")
              .ShouldBeEquivalentTo(ty1);
            _insp.LoadGlobalInspectionType("ty2")
              .ShouldBeEquivalentTo(ty2);

            //overwrite t1
            var newTy1 = RandInspType("ty1");
            _insp.SetGlobalInspectionType(newTy1);

            _insp.LoadGlobalInspectionTypes()
              .ShouldAllBeEquivalentTo(new string[] {"ty1", "ty2"});
            _insp.LoadAllGlobalInspections()
              .ShouldAllBeEquivalentTo(new InspectionType[] {newTy1, ty2});
            _insp.LoadGlobalInspectionType("ty1")
              .ShouldBeEquivalentTo(newTy1);
            _insp.LoadGlobalInspectionType("ty2")
              .ShouldBeEquivalentTo(ty2);

            //delete t1
            _insp.DeleteGlobalInspectionType("ty1");

            _insp.LoadGlobalInspectionTypes()
              .ShouldAllBeEquivalentTo(new string[] {"ty2"});
            _insp.LoadAllGlobalInspections()
              .ShouldAllBeEquivalentTo(new InspectionType[] {ty2});
            _insp.LoadGlobalInspectionType("ty1")
              .Should().BeNull();
            _insp.LoadGlobalInspectionType("ty2")
              .ShouldBeEquivalentTo(ty2);

        }

        [Fact]
        public void ConvertToJob()
        {
            var ty1 = RandInspType("ty1");

            var job = ty1.ConvertToJobInspection("partnooverride", 1);
            Assert.Equal(ty1.Name, job.InspectionType);
            Assert.Equal(ty1.InspectSingleProcess, job.InspectSingleProcess);
            Assert.Equal(ty1.DefaultDeadline, job.TimeInterval);
            if (ty1.DefaultCountToTriggerInspection == 0)
            {
                Assert.Equal(0, job.MaxVal);
                Assert.Equal(ty1.DefaultRandomFreq, job.RandomFreq);
            }
            else
            {
                Assert.Equal(ty1.DefaultCountToTriggerInspection, job.MaxVal);
                Assert.Equal(-1, job.RandomFreq);
            }
        }

        [Fact]
        public void ParseInspectionCounterTrackEverything()
        {
            var ty = new InspectionType();
            ty.Name = "insp1";
            ty.TrackPartName = true;
            ty.TrackPalletName = true;
            ty.TrackStationName = true;
            ty.Overrides = new List<InspectionFrequencyOverride>();
            var cntr = ty.ConvertToJobInspection("partname", 2).Counter;

            Assert.Equal("partname,insp1,P%pal1%,S%stat1,1%,P%pal2%,S%stat2,1%", cntr);

            var cntrReplaced = cntr
              .Replace("%pal1%", "ppp")
              .Replace("%pal2%", "xxx")
              .Replace("%stat1,1%", "mmm")
              .Replace("%stat2,1%", "sss");

            var parsed = InspectionType.ParseInspectionCounter(cntrReplaced);
            Assert.Equal("insp1", parsed.InspectionName);
            Assert.Equal("partname", parsed.PartName);
            Assert.Equal(new [] {"ppp", "xxx"}, parsed.Pallets);
            Assert.Equal(new [] {"mmm", "sss"}, parsed.Stations);
        }

        [Fact]
        public void ParseInspectionCounterNoPallets()
        {
            var ty = new InspectionType();
            ty.Name = "iiins";
            ty.TrackPartName = true;
            ty.TrackPalletName = false;
            ty.TrackStationName = true;
            ty.Overrides = new List<InspectionFrequencyOverride>();
            var cntr = ty.ConvertToJobInspection("pparttt", 2).Counter;

            Assert.Equal("pparttt,iiins,S%stat1,1%,S%stat2,1%", cntr);

            var cntrReplaced = cntr
              .Replace("%stat1,1%", "mmm")
              .Replace("%stat2,1%", "sss");

            var parsed = InspectionType.ParseInspectionCounter(cntrReplaced);
            Assert.Equal("iiins", parsed.InspectionName);
            Assert.Equal("pparttt", parsed.PartName);
            Assert.Equal(new string[] {}, parsed.Pallets);
            Assert.Equal(new [] {"mmm", "sss"}, parsed.Stations);
        }

        [Fact]
        public void ParseInspectionCounterNoStationsOrPart()
        {
            var ty = new InspectionType();
            ty.Name = "iiins";
            ty.TrackPartName = false;
            ty.TrackPalletName = true;
            ty.TrackStationName = false;
            ty.Overrides = new List<InspectionFrequencyOverride>();
            var cntr = ty.ConvertToJobInspection("thepart", 2).Counter;

            Assert.Equal(",iiins,P%pal1%,P%pal2%", cntr);

            var cntrReplaced = cntr
              .Replace("%pal1%", "ppp")
              .Replace("%pal2%", "xxx");

            var parsed = InspectionType.ParseInspectionCounter(cntrReplaced);
            Assert.Equal("iiins", parsed.InspectionName);
            Assert.Equal("", parsed.PartName);
            Assert.Equal(new string[] {}, parsed.Stations);
            Assert.Equal(new [] {"ppp", "xxx"}, parsed.Pallets);
        }

        private InspectionType RandInspType(string tyName)
        {
            var rand = new Random();
            var ty = new InspectionType();
            ty.Name = tyName;
            ty.TrackPartName = rand.Next(2) < 1;
            ty.TrackPalletName = rand.Next(2) < 1;
            ty.TrackStationName = rand.Next(2) < 1;
            ty.InspectSingleProcess = rand.Next();
            ty.DefaultCountToTriggerInspection = rand.Next();
            ty.DefaultRandomFreq = rand.NextDouble();
            ty.DefaultDeadline = TimeSpan.FromSeconds(rand.Next(10000));
            ty.Overrides = new List<InspectionFrequencyOverride>();
            ty.Overrides.Add(new InspectionFrequencyOverride {
                Part = "part1",
                CountBeforeInspection = rand.Next(),
                Deadline = TimeSpan.FromSeconds(rand.Next(10000)),
                RandomFreq = rand.NextDouble()
            });
            ty.Overrides.Add(new InspectionFrequencyOverride {
                Part = "part2",
                CountBeforeInspection = rand.Next(),
                Deadline = TimeSpan.FromSeconds(rand.Next(10000)),
                RandomFreq = rand.NextDouble()
            });
            return ty;
        }
    }
}