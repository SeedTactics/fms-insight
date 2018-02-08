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
using System.Runtime.Serialization;

namespace BlackMaple.MachineWatchInterface
{
    ///Stores what is currently happening to a piece of material.
    [Serializable, DataContract]
    public class InProcessMaterialAction
    {
        // This should be a sum type, and while C# sum types can work with some helper code it doesn't work
        // well for serialization.  Swagger supports discriminated types but NSWag does not yet.

        [Serializable, DataContract]
        public enum ActionType
        {
            Waiting = 0,
            Loading,
            Unloading,
            Machining
        }
        [DataMember(IsRequired=true)] public ActionType Type {get;set;}

        // If Type = Loading
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public string LoadOntoPallet {get;set;}
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public int LoadOntoFace {get;set;}

        //If Type = Unloading
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public string UnloadIntoQueue {get;set;}

        // If Type = Machining
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public string Program {get;set;}
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public TimeSpan? ElapsedMachiningTime {get;set;}
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public TimeSpan? ExpectedRemainingMachiningTime {get;set;}
    }

    ///Stores the current location of a piece of material.  If a transfer operation is currently in process
    ///(such as unloading), the location will store the previous location and the action will store the new location.
    [Serializable, DataContract]
    public class InProcessMaterialLocation
    {
        //Again, this should be a sum type.
        [Serializable, DataContract]
        public enum LocType
        {
            Free = 0,
            OnPallet,
            InQueue,
        }
        [DataMember(IsRequired=true)] public LocType Type {get;set;}

        //If Type == OnPallet
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public string Pallet {get;set;}
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public string Fixture {get;set;}
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public int Face {get;set;}

        //If Type == InQueue
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public string CurrentQueue {get;set;}
    }

    //Stores information about a piece of material, where it is, and what is happening to it.
    [Serializable, DataContract]
    public class InProcessMaterial
    {
        // Information about the material
        [DataMember(IsRequired=true)] public long MaterialID {get;set;}
        [DataMember(IsRequired=true)] public string JobUnique { get; set; }
        [DataMember(IsRequired=true)] public string PartName {get; set;}
        [DataMember(IsRequired=true)] public int Process {get;set;}  // When in a queue, the process is the last completed process
        [DataMember(IsRequired=true)] public int Path {get;set;}

        // Where is the material?
        [DataMember(IsRequired=true)] public InProcessMaterialLocation Location {get;set;}

        // What is currently happening to the material?
        [DataMember(IsRequired=true)] public InProcessMaterialAction Action {get;set;}
    }

    [Serializable, DataContract]
    public enum PalletLocationEnum
    {
        [EnumMember] LoadUnload,
        [EnumMember] Machine,
        [EnumMember] MachineQueue,
        [EnumMember] Buffer,
        [EnumMember] Cart
    }

    [Serializable, DataContract]
    public struct PalletLocation : IComparable
    {

        [DataMember(Name = "loc", IsRequired=true)]
        public PalletLocationEnum Location {get;set;}

        [DataMember(Name = "num", IsRequired=true)]
        public int Num {get;set;}

        public PalletLocation(PalletLocationEnum l)
        {
            Location = l;
            Num = 1;
        }

        public PalletLocation(PalletLocationEnum l, int n)
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

    [Serializable, DataContract]
    public class PalletStatus
    {
        [DataMember(IsRequired=true)] public string Pallet {get;set;}
        [DataMember(IsRequired=true)] public string FixtureOnPallet {get;set;}
        [DataMember(IsRequired=true)] public bool OnHold {get;set;}
        [DataMember(IsRequired=true)] public PalletLocation CurrentPalletLocation {get;set;}

        // If the pallet is at a load station and a new fixture should be loaded, this is filled in.
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public string NewFixture {get;set;}

        //If CurrentPalletLocation is Cart, the following two fields will be filled in.
        //If the percentage is unknown, -1 is returned.
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public PalletLocation? TargetLocation {get;set;}
        [DataMember(IsRequired=false, EmitDefaultValue=false)] public decimal? PercentMoveCompleted {get;set;}
    }

    [SerializableAttribute, DataContract]
    public class InProcessJob : JobPlan
    {
        public int GetCompletedOnFirstProcess(int path)
        {
            if (path >= 1 && path <= GetNumPaths(1)) {
                return _completedProc1[path - 1];
            } else {
                throw new IndexOutOfRangeException("Invalid path number");
            }
        }
        public void SetCompletedOnFirstProcess(int path, int comp)
        {
            if (path >= 1 && path <= GetNumPaths(1)) {
                _completedProc1[path - 1] = comp;
            } else {
                throw new IndexOutOfRangeException("Invalid path number");
            }
        }

        [DataMember(IsRequired=true)]
        public int TotalCompleteOnFinalProcess {get;set;}

        public InProcessJob(string unique, int numProc, int[] numPaths = null) : base(unique, numProc, numPaths)
        {
            _completedProc1 = new int[GetNumPaths(1)];
            for (int i = 0; i < _completedProc1.Length; i++)
                _completedProc1[i] = 0;
        }
        public InProcessJob(JobPlan job) : base(job)
        {
            _completedProc1 = new int[GetNumPaths(1)];
            for (int i = 0; i < _completedProc1.Length; i++)
                _completedProc1[i] = 0;
        }

        [DataMember(Name="CompletedProc1", IsRequired=true)] private int[] _completedProc1;
    }

    [SerializableAttribute, DataContract]
    public class CurrentStatus
    {
        public IDictionary<string, InProcessJob> Jobs => _jobs;
        public IDictionary<string, PalletStatus> Pallets => _pals;
        public IList<InProcessMaterial> Material => _material;
        public IList<string> Alarms => _alarms;

        [DataMember(IsRequired=false, EmitDefaultValue=false)] public string LatestScheduleId {get;set;}

        public CurrentStatus()
        {
            _jobs = new Dictionary<string, InProcessJob>();
            _pals = new Dictionary<string, PalletStatus>();
            _material = new List<InProcessMaterial>();
            _alarms = new List<string>();
            LatestScheduleId = null;
        }

        [DataMember(Name="Jobs", IsRequired=true)]
        private Dictionary<string, InProcessJob> _jobs;

        [DataMember(Name="Pallets", IsRequired=true)]
        private Dictionary<string, PalletStatus> _pals;

        [DataMember(Name="Material", IsRequired=true)]
        private List<InProcessMaterial> _material;

        [DataMember(Name="Alarms", IsRequired=false, EmitDefaultValue=false)]
        private List<string> _alarms;
    }

    [Serializable, DataContract]
    public struct JobAndDecrementQuantity
    {
        [DataMember(IsRequired=true)] public string DecrementId {get;set;}
        [DataMember(IsRequired=true)] public string JobUnique {get;set;}
        [DataMember(IsRequired=true)] public DateTime TimeUTC {get;set;}
        [DataMember(IsRequired=true)] public string Part {get;set;}
        [DataMember(IsRequired=true)] public int Quantity {get;set;}
    }

    // The following is only used for old decrement for backwards compatibility,
    // and shouldn't be used for anything else.
    [Serializable]
    public class JobAndPath : IEquatable<JobAndPath>
    {
        public readonly string UniqueStr;
        public readonly int Path;

        public JobAndPath(string unique, int path)
        {
            UniqueStr = unique;
            Path = path;
        }

        public bool Equals(JobAndPath other)
        {
            return (UniqueStr == other.UniqueStr && Path == other.Path);
        }
    }
}