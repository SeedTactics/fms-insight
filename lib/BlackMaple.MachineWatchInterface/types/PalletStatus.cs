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
