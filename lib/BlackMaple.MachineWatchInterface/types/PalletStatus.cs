using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BlackMaple.MachineWatchInterface
{
    [SerializableAttribute()]
    public enum PalletLocationTypeEnum
    {
        Unkown /* sic */ = 0,
        LoadUnload,
        Machine,
        MachineInbound,
        Buffer,
        Cart,
        PartMarker,
        Inspection,
        Washer,
        Deburr,
        OrderAssignment
    }

    [SerializableAttribute(), JsonObject(MemberSerialization.OptIn)]
    public struct PalletLocation : IComparable
    {

        [JsonProperty(PropertyName = "loc", Required = Required.Always)]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public PalletLocationTypeEnum Location;

        [JsonProperty(PropertyName = "num", Required = Required.Always)]
        public int Num;

        public PalletLocation(PalletLocationTypeEnum l, int n)
        {
            Location = l;
            Num = n;
        }

        public int CompareTo(object obj)
        {
            PalletLocation other = (PalletLocation)obj;
            int cmp = Location.CompareTo(other.Location);
            if (cmp < 0)
                return -1;
            if (cmp > 0)
                return 1;
            return Num.CompareTo(other.Num);
        }
    }

    [SerializableAttribute()]
    public enum PalletStatusEnum
    {
        WaitingForInstructions = 0,
        AtLoadUnload,
        Machining
    }

    [SerializableAttribute()]
    public class PalletStatus
    {
        public readonly string Pallet;
        public readonly string FixtureOnPallet;
        public readonly bool OnHold;
        public readonly PalletLocation CurrentPalletLocation;
        public readonly List<Material> MaterialOnPallet;
        public readonly PalletStatusEnum Status;

        // If the pallet status is AtLoadUnload, the following list will be filled in with
        // the material to unload.  This material will still exist in the MaterialOnPallet list
        public readonly List<Material> MaterialToUnload;

        // If the pallet status is AtLoadUnload, the following list will be filled in with
        // new material to load.  None of this material will exist in the MaterialOnPallet list.
        public readonly List<Material> MaterialToLoad;

        // The new fixture to load if the status is AtLoadUnload.  An empty string means no change of fixture.
        public readonly string NewFixture;

        // If the pallet status is Machining, we record the material currently in execution.  This will
        // be a sublist of MaterialOnPallet.  Only material currently executing appears in this list.
        public readonly List<MaterialInExe> MaterialInExecution;

        //If CurrentPalletLocation is Cart, the following two fields will be filled in.  
        //If the percentage is unknown, -1 is returned.
        public PalletLocation TargetLocation;
        public decimal PercentMoveCompleted;

        private PalletStatus(string pal, string fix, bool hold, PalletLocation curLoc, PalletStatusEnum status, string newFix)
        {
            Pallet = pal;
            FixtureOnPallet = fix;
            OnHold = hold;
            CurrentPalletLocation = curLoc;
            MaterialOnPallet = new List<Material>();
            Status = status;

            MaterialToUnload = new List<Material>();
            MaterialToLoad = new List<Material>();
            NewFixture = newFix;

            MaterialInExecution = new List<MaterialInExe>();

            TargetLocation = new PalletLocation(PalletLocationTypeEnum.Unkown, 0);
            PercentMoveCompleted = -1;
        }

        public static PalletStatus CreateWaitingForInstructions(string pal, string fixture, bool hold, PalletLocation loc)
        {
            return new PalletStatus(pal, fixture, hold, loc, PalletStatusEnum.WaitingForInstructions, "");
        }

        public static PalletStatus CreateAtLoadUnload(string pal, string fixture, bool hold, PalletLocation loc, string newFixture)
        {
            return new PalletStatus(pal, fixture, hold, loc, PalletStatusEnum.AtLoadUnload, newFixture);
        }

        public static PalletStatus CreateMachining(string pal, string fixture, bool hold, PalletLocation loc)
        {
            return new PalletStatus(pal, fixture, hold, loc, PalletStatusEnum.Machining, "");
        }

        [SerializableAttribute()]
        public class Material
        {
            public string JobUnique;
            public string PartName;
            public long MaterialID;
            public int Process;
            public int Path;
            public string FaceName;

            public Material(string job, string part, long matID, int proc, int path, string face)
            {
                JobUnique = job;
                PartName = part;
                MaterialID = matID;
                Process = proc;
                Path = path;
                FaceName = face;
            }

        }

        [SerializableAttribute()]
        public class MaterialInExe : Material
        {
            public string Program;

            //Time times can be 0 if machine watch does not know the elapsed or expected remaining time.
            public TimeSpan ElapsedMachiningTime;
            public TimeSpan ExpectedRemainingMachiningTime;

            public MaterialInExe(string job, string part, long matID, int proc, int path, string face)
                : base(job, part, matID, proc, path, face)
            {
            }
        }
    }
}
