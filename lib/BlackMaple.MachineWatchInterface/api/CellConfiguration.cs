using System;
using System.Collections.Generic;

namespace BlackMaple.MachineWatchInterface
{
    [SerializableAttribute]
    public class PalletOffsets
    {
        public string Pallet {get;}
        public string Fixture {get;}
        public decimal Angle {get;set;}
        public decimal X {get;set;}
        public decimal Y {get;set;}
        public decimal Z {get;set;}

        public PalletOffsets(string pal, string fix)
        {
            Pallet = pal;
            Fixture = fix;
            Angle = X = Y = Z = 0;
        }
    }

    [SerializableAttribute]
    public class CellFixtureFace
    {
        public int FaceNum {get;}
        public Dictionary<string, int> Parts {get;} = new Dictionary<string, int>();
        public CellFixtureFace(int i)
        {
            FaceNum = i;
        }
    }

    [SerializableAttribute]
    public class CellFixture
    {
        public string FixtureName {get;}
        public List<string> Pallets {get;} = new List<string>();
        public int FixtureQuantity {get;set;}
        public List<CellFixtureFace> Faces {get;} = new List<CellFixtureFace>();
        public CellFixture(string name)
        {
            FixtureName = name;
        }
    }

    [SerializableAttribute]
    public class Program
    {
        public string ProgramName {get;}
        public string Comment {get;}
        public TimeSpan EstimatedCuttingTime {get;}
        public string ProgramText {get;set;}

        public Program(string name, string comment, TimeSpan estimated)
        {
            ProgramName = name;
            Comment = comment;
            EstimatedCuttingTime = estimated;
            ProgramText = "";
        }
    }

    //In general, the philosophy of Machine Watch is that all data to build a part is included
    //as part of the JobPlan.  This includes fixture and face data, programs, and pallet offsets.
    //
    //Current cell controllers do not work like that though, and pallet offsets, fixtures, and programs
    //are stored in the cell controller itself.   The 'ICellConfiguration' interface provides read
    //access to this data.  Then in the JobPlan that is downloaded we cheat and download only a
    //program name or fixture name instead of the entire program text or fixture information.
    //
    //At some point if things are ever re-designed for a new cell controller, JobPlan should be updated
    //to allow downloading everything.
    public interface ICellConfiguration
    {
        IEnumerable<CellFixture> ServerFixtures();
        IEnumerable<string> ProgramNames();
        Program LoadProgram(string name);
        PalletOffsets LoadPalletOffsets(string name);
    }

    public interface IMachineWatchVersion
    {
        string Version();
	    string PluginName();
    }

    //Allow programs to store and load JSON settings.  Useful to allow programs to
    //share settings across differnet computers.  Settings should be versioned by flexible
    //JSON parsing.
    public interface IStoreSettings
    {
        string GetSettings(string ID);
        void SetSettings(string ID, string settingsJson);
    }
}

