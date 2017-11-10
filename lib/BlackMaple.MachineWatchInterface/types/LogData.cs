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
using System.ComponentModel;
using Newtonsoft.Json;

namespace BlackMaple.MachineWatchInterface
{
    [SerializableAttribute, JsonObject(MemberSerialization.OptIn)]
    public class LogMaterial
    {
        [JsonProperty(PropertyName = "id", Required = Required.Always)]
        public long MaterialID { get; private set; }

        [JsonProperty(PropertyName = "uniq", Required = Required.Always)]
        public string JobUniqueStr { get; private set; }

        [JsonProperty(PropertyName = "part", Required = Required.Always)]
        public string PartName { get; private set; }

        [JsonProperty(PropertyName = "proc", Required = Required.Always)]
        public int Process { get; private set; }

        [JsonProperty(PropertyName = "numproc", Required = Required.Always)]
        public int NumProcesses { get; private set; }

        [JsonProperty(PropertyName = "face", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate), DefaultValue("")]
        public string Face { get; private set; }

        public LogMaterial(long matID, string uniq, int proc, string part, int numProc, string face = "")
        {
            MaterialID = matID;
            JobUniqueStr = uniq;
            PartName = part;
            Process = proc;
            NumProcesses = numProc;
            Face = face;
        }

        [JsonConstructor]
        private LogMaterial() { }
    }

    public enum LogType
    {
        LoadUnloadCycle = 1, //numbers are for backwards compatibility with old type enumeration
        MachineCycle = 2,
        PartMark = 6,
        Inspection = 7,
        OrderAssignment = 10,
        GeneralMessage = 100,
        PalletCycle = 101,
        FinalizeWorkorder = 102
    }

    [SerializableAttribute, JsonObject(MemberSerialization.OptIn)]
    public class LogEntry
    {
        [JsonProperty(PropertyName = "counter", Required = Required.Always)]
        public long Counter { get; private set; }

        [JsonProperty(PropertyName = "material", Required = Required.Always)]
        public IEnumerable<LogMaterial> Material { get; private set; }

        [JsonProperty(PropertyName = "type", Required = Required.Always)]
        public LogType LogType { get; private set; }

        [JsonProperty(PropertyName = "startofcycle", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate), DefaultValue(false)]
        public bool StartOfCycle { get; private set; }

        [JsonProperty(PropertyName = "endUTC", Required = Required.Always)]
        public DateTime EndTimeUTC { get; private set; }

        [JsonProperty(PropertyName = "loc", Required = Required.Always)]
        public string LocationName {get; private set;}

        [JsonProperty(PropertyName = "locnum", Required = Required.Always)]
        public int LocationNum {get; private set;}

        [JsonProperty(PropertyName = "pal", Required = Required.Always)]
        public string Pallet { get; private set; }

        [JsonProperty(PropertyName = "program", Required = Required.Always)]
        public string Program { get; private set; }

        [JsonProperty(PropertyName = "result", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate), DefaultValue("")]
        public string Result { get; private set; }

        [JsonProperty(PropertyName = "endofroute", DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate), DefaultValue(false)]
        public bool EndOfRoute { get; private set; }

        [JsonProperty(PropertyName = "elapsed")]
        public TimeSpan ElapsedTime { get; private set; } //time from cycle-start to cycle-stop

        [JsonProperty(PropertyName = "active")]
        public TimeSpan ActiveOperationTime { get; private set; } //time that the machining or operation is actually active

        [JsonProperty(PropertyName = "details")]
        private Dictionary<string, string> _details;
        public IDictionary<string, string> ProgramDetails { get { return _details; } }

        public LogEntry(
            long cntr,
            IEnumerable<LogMaterial> mat,
            string pal,
            LogType ty,
            string locName,
            int locNum,                          
            string prog,
            bool start,
            DateTime endTime,
            string result,
            bool endOfRoute)
            : this(cntr, mat, pal, ty, locName, locNum, prog, start, endTime, result, endOfRoute,
                  TimeSpan.FromMinutes(-1), TimeSpan.Zero)
        { }

        public LogEntry(
            long cntr,
            IEnumerable<LogMaterial> mat,
            string pal,
            LogType ty,
            string locName,
            int locNum,                            
            string prog,
            bool start,
            DateTime endTime,
            string result,
            bool endOfRoute,
            TimeSpan elapsed,
            TimeSpan active)
        {
            Counter = cntr;
            Material = mat; // ok since material is immutable
            Pallet = pal;
            LogType = ty;
            LocationName = locName;
            LocationNum = locNum;
            Program = prog;
            StartOfCycle = start;
            EndTimeUTC = endTime;
            Result = result;
            EndOfRoute = endOfRoute;
            ElapsedTime = elapsed;
            ActiveOperationTime = active;
            _details = new Dictionary<string, string>();
        }

        public LogEntry(LogEntry copy, long newCounter)
        {
            Counter = newCounter;
            Material = copy.Material; // ok since material is immutable
            Pallet = copy.Pallet;
            LogType = copy.LogType;
            LocationName = copy.LocationName;
            LocationNum = copy.LocationNum;
            Program = copy.Program;
            StartOfCycle = copy.StartOfCycle;
            EndTimeUTC = copy.EndTimeUTC;
            Result = copy.Result;
            EndOfRoute = copy.EndOfRoute;
            ElapsedTime = copy.ElapsedTime;
            ActiveOperationTime = copy.ActiveOperationTime;
            _details = new Dictionary<string, string>(copy._details);
        }

        public LogEntry(LogEntry copy) : this(copy, copy.Counter) { }

        [JsonConstructor]
        private LogEntry() { }

        public bool ShouldSerializeElapsedTime()
        {
            return ElapsedTime != TimeSpan.FromMinutes(-1);
        }

        public bool ShouldSerializeActiveOperationTime()
        {
            return ActiveOperationTime != TimeSpan.Zero;
        }

        public bool ShouldSerializeProgramDetails()
        {
            return _details.Count > 0;
        }
    }

    [Serializable, JsonObject(MemberSerialization.OptIn)]
    public class WorkorderPartSummary
    {
        [JsonProperty(PropertyName="name", Required=Required.Always)]
        public string Part {get;set;}

        [JsonProperty(PropertyName="completed-qty", Required=Required.Always)]
        public int PartsCompleted {get;set;}

        [JsonProperty(PropertyName="elapsed-station-time", Required=Required.AllowNull)]
        private Dictionary<string, TimeSpan> _elapsedStatTime;

        public Dictionary<string, TimeSpan> ElapsedStationTime
        {
            get
            {
                if (_elapsedStatTime == null) _elapsedStatTime = new Dictionary<string, TimeSpan>();
                return _elapsedStatTime;
            }
        }

        [JsonProperty(PropertyName="active-stat-time", Required=Required.AllowNull)]
        private Dictionary<string, TimeSpan> _activeStatTime;

        public Dictionary<string, TimeSpan> ActiveStationTime
        {
            get
            {
                if (_activeStatTime == null) _activeStatTime = new Dictionary<string, TimeSpan>();
                return _activeStatTime;
            }
        }
    }

    [Serializable, JsonObject(MemberSerialization.OptIn)]
    public class WorkorderSummary
    {
        [JsonProperty(PropertyName="id", Required=Required.Always)]
        public string WorkorderId {get;set;}

        [JsonProperty(PropertyName="parts", Required=Required.Always)]
        private List<WorkorderPartSummary> _parts;

        public List<WorkorderPartSummary> Parts
        {
            get
            {
                if (_parts == null) _parts = new List<WorkorderPartSummary>();
                return _parts;
            }
        }

        [JsonProperty(PropertyName="serials", Required=Required.AllowNull)]
        private List<string> _serials;

        public List<string> Serials
        {
            get
            {
                if (_serials == null) _serials = new List<string>();
                return _serials;
            }
        }

        [JsonProperty(PropertyName="finalized", Required=Required.AllowNull)]
        public DateTime? FinalizedTimeUTC {get;set;}
    }
}
