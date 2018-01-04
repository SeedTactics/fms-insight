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
using System.Runtime.Serialization;

namespace BlackMaple.MachineWatchInterface
{
    [Serializable, DataContract]
    public class LogMaterial
    {
        [DataMember(Name = "id")]
        public long MaterialID { get; private set; }

        [DataMember(Name = "uniq")]
        public string JobUniqueStr { get; private set; }

        [DataMember(Name = "part")]
        public string PartName { get; private set; }

        [DataMember(Name = "proc")]
        public int Process { get; private set; }

        [DataMember(Name = "numproc")]
        public int NumProcesses { get; private set; }

        [DataMember(Name = "face")]
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
    }

    [Serializable, DataContract]
    public enum LogType
    {
        [EnumMember] LoadUnloadCycle = 1, //numbers are for backwards compatibility with old type enumeration
        [EnumMember] MachineCycle = 2,
        [EnumMember] PartMark = 6,
        [EnumMember] Inspection = 7,
        [EnumMember] OrderAssignment = 10,
        [EnumMember] GeneralMessage = 100,
        [EnumMember] PalletCycle = 101,
        [EnumMember] FinalizeWorkorder = 102
    }

    [Serializable, DataContract]
    public class LogEntry
    {
        [DataMember(Name = "counter")]
        public long Counter { get; private set; }

        [DataMember(Name = "material")]
        public IEnumerable<LogMaterial> Material { get; private set; }

        [DataMember(Name = "type")]
        public LogType LogType { get; private set; }

        [DataMember(Name = "startofcycle")]
        public bool StartOfCycle { get; private set; }

        [DataMember(Name = "endUTC")]
        public DateTime EndTimeUTC { get; private set; }

        [DataMember(Name = "loc")]
        public string LocationName {get; private set;}

        [DataMember(Name = "locnum")]
        public int LocationNum {get; private set;}

        [DataMember(Name = "pal")]
        public string Pallet { get; private set; }

        [DataMember(Name = "program")]
        public string Program { get; private set; }

        [DataMember(Name = "result")]
        public string Result { get; private set; }

        [DataMember(Name = "endofroute")]
        public bool EndOfRoute { get; private set; }

        [DataMember(Name = "elapsed")]
        public TimeSpan ElapsedTime { get; private set; } //time from cycle-start to cycle-stop

        [DataMember(Name = "active")]
        public TimeSpan ActiveOperationTime { get; private set; } //time that the machining or operation is actually active

        [DataMember(Name = "details")]
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

    [Serializable, DataContract]
    public class WorkorderPartSummary
    {
        [DataMember(Name="name")]
        public string Part {get;set;}

        [DataMember(Name="completed-qty")]
        public int PartsCompleted {get;set;}

        [DataMember(Name="elapsed-station-time")]
        private Dictionary<string, TimeSpan> _elapsedStatTime;

        public Dictionary<string, TimeSpan> ElapsedStationTime
        {
            get
            {
                if (_elapsedStatTime == null) _elapsedStatTime = new Dictionary<string, TimeSpan>();
                return _elapsedStatTime;
            }
        }

        [DataMember(Name="active-stat-time")]
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

    [Serializable, DataContract]
    public class WorkorderSummary
    {
        [DataMember(Name="id")]
        public string WorkorderId {get;set;}

        [DataMember(Name="parts")]
        private List<WorkorderPartSummary> _parts;

        public List<WorkorderPartSummary> Parts
        {
            get
            {
                if (_parts == null) _parts = new List<WorkorderPartSummary>();
                return _parts;
            }
        }

        [DataMember(Name="serials")]
        private List<string> _serials;

        public List<string> Serials
        {
            get
            {
                if (_serials == null) _serials = new List<string>();
                return _serials;
            }
        }

        [DataMember(Name="finalized")]
        public DateTime? FinalizedTimeUTC {get;set;}
    }
}
