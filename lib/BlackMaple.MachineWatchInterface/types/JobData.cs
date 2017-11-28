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
using System.Linq;
using System.Runtime.Serialization;

namespace BlackMaple.MachineWatchInterface
{

    //stores information about a single "stop" of the pallet in a route
    [SerializableAttribute, JsonObject(MemberSerialization.OptIn)]
    public class JobMachiningStop
    {
        public string StationGroup
        {
            get { return _statGroup; }
	    set { _statGroup = value; }
        }

        public TimeSpan ExpectedCycleTime
        {
            get { return _expectedCycleTime; }
            set { _expectedCycleTime = value; }
        }

        public void AddProgram(int station, string program)
        {
            _programs[station] = program;
        }

        public string Program(int statNum)
        {
            if (_programs.ContainsKey(statNum)) {
                return _programs[statNum];
            } else {
                return "";
            }
        }

        public IEnumerable<ProgramEntry> AllPrograms()
        {
            var ret = new List<ProgramEntry>();
            foreach (var k in _programs) {
                ret.Add(new ProgramEntry(k.Key, k.Value));
            }
            return ret;
        }

        public IEnumerable<int> Stations()
        {
            return _programs.Keys;
        }

        [SerializableAttribute]
        public struct ProgramEntry : IComparable<ProgramEntry>
        {
            public int StationNum;
            public string Program;

            internal ProgramEntry(int s, string prog)
            {
                StationNum = s;
                Program = prog;
            }

            public override string ToString()
            {
                return StationNum.ToString() + ":" + Program;
            }

            public int CompareTo(ProgramEntry o)
            {
                var i = StationNum.CompareTo(o.StationNum);
                if (i < 0) return -1;
                if (i > 0) return 1;
                return Program.CompareTo(o.Program);
            }
        }

        //Key is tool name, value is expected elapsed time
        public IDictionary<string, TimeSpan> Tools
        {
            get { return _tools; }
        }

        public JobMachiningStop(string sGroup)
        {
            _statGroup = sGroup;
            _programs = new Dictionary<int, string>();
            _tools = new Dictionary<string, TimeSpan>();
            _expectedCycleTime = TimeSpan.Zero;
        }

        public JobMachiningStop(JobMachiningStop stop)
        {
            _statGroup = stop._statGroup;
            _programs = new Dictionary<int, string>(stop._programs);
            _expectedCycleTime = stop._expectedCycleTime;
            _tools = new Dictionary<string, TimeSpan>(stop._tools);
        }

        [JsonProperty(PropertyName="Stations")]
        private Dictionary<int, string> _programs;

        [JsonProperty(PropertyName="Tools")]
        private Dictionary<string, TimeSpan> _tools; //key is tool, value is expected cutting time

        [JsonProperty(PropertyName="StationGroup")]
        private string _statGroup;

        [JsonProperty(PropertyName="ExpectedCycleTime")]
        private TimeSpan _expectedCycleTime;
    }

    [SerializableAttribute, JsonObject(MemberSerialization.OptIn)]
    public class JobInspectionData
    {
        [JsonProperty]
        public readonly string InspectionType;

        //There are two possible ways of triggering an exception: counts and frequencies.
        // * For counts, the MaxVal will contain a number larger than zero and RandomFreq will contain -1
        // * For frequencies, the value of MaxVal is -1 and RandomFreq contains
        //   the frequency as a number between 0 and 1.

        //Every time a material completes, the counter string is expanded (see below).
        [JsonProperty]
        public readonly string Counter;

        //For each completed material, the counter is inremented.  If the counter is equal to MaxVal,
        //we signal an inspection and reset the counter to 0.
        [JsonProperty]
        public readonly int MaxVal;

        //The random frequency of inspection
        [JsonProperty]
        public readonly double RandomFreq;

        //If the last inspection signaled for this counter was longer than TimeInterval,
        //signal an inspection.  This can be disabled by using TimeSpan.Zero
        [JsonProperty]
        public readonly TimeSpan TimeInterval;

        //If set to -1, the entire job should be inspected once the job completes.
        //If set to a positive number, only that process should have the inspection triggered.
        [JsonProperty]
        public readonly int InspectSingleProcess;

        public JobInspectionData(string iType, string ctr, int max, TimeSpan interval, int inspSingleProc = -1)
        {
            InspectionType = iType;
            Counter = ctr;
            MaxVal = max;
            TimeInterval = interval;
            RandomFreq = -1;
            InspectSingleProcess = inspSingleProc;
        }
        public JobInspectionData(string iType, string ctr, double frequency, TimeSpan interval, int inspSingleProc = -1)
        {
            InspectionType = iType;
            Counter = ctr;
            MaxVal = -1;
            TimeInterval = interval;
            RandomFreq = frequency;
            InspectSingleProcess = inspSingleProc;
        }
        public JobInspectionData(JobInspectionData insp)
        {
            InspectionType = insp.InspectionType;
            Counter = insp.Counter;
            MaxVal = insp.MaxVal;
            TimeInterval = insp.TimeInterval;
            RandomFreq = insp.RandomFreq;
            InspectSingleProcess = insp.InspectSingleProcess;
        }

        //The final counter string is determined by replacing following substrings in the counter
        public static string PalletFormatFlag(int proc)
        {
            return "%pal" + proc.ToString() + "%";
        }
        public static string LoadFormatFlag(int proc)
        {
            return "%load" + proc.ToString() + "%";
        }
        public static string UnloadFormatFlag(int proc)
        {
            return "%unload" + proc.ToString() + "%";
        }
        public static string StationFormatFlag(int proc, int routeNum)
        {
            return "%stat" + proc.ToString() + "," + routeNum.ToString() + "%";
        }
    }

    [SerializableAttribute, JsonObject(MemberSerialization.OptIn)]
    public class JobHoldPattern
    {
        // All of the following hold types are an OR, meaning if any one of them says a hold is in effect,
        // the job is on hold.

        [JsonProperty]
        public bool UserHold;

        [JsonProperty]
        public string ReasonForUserHold;

        //A list of timespans the job should be on hold/not on hold.
        //During the first timespan, the job is on hold.
        [JsonProperty]
        public readonly IList<TimeSpan> HoldUnholdPattern;

        [JsonProperty]
        public DateTime HoldUnholdPatternStartUTC;

        [JsonProperty]
        public bool HoldUnholdPatternRepeats;

        public bool IsJobOnHold
        {
            get
            {
                bool hold;
                DateTime next;

                HoldInformation(DateTime.UtcNow, out hold, out next);

                return hold;
            }
        }

        // Given a time, allows you to calculate if the hold is active
        // and the next transition time.
        public void HoldInformation(DateTime nowUTC,
                                    out bool isOnHold,
                                    out DateTime nextTransitionUTC)
        {

            if (UserHold) {
                isOnHold = true;
                nextTransitionUTC = DateTime.MaxValue;
                return;
            }

            if (HoldUnholdPattern.Count == 0) {
                isOnHold = false;
                nextTransitionUTC = DateTime.MaxValue;
                return;
            }

            //check that the hold pattern has a non-zero timespan.
            //Without this check we will get in an infinite loop below.
            bool foundSpan = false;
            foreach (var span in HoldUnholdPattern) {
                if (span.Ticks > 0) {
                    foundSpan = true;
                    break;
                }
            }
            if (!foundSpan) {
                //If not, then we are not on hold.
                isOnHold = false;
                nextTransitionUTC = DateTime.MaxValue;
                return;
            }

            if (nowUTC < HoldUnholdPatternStartUTC) {
                isOnHold = false;
                nextTransitionUTC = HoldUnholdPatternStartUTC;
            }

            //Start with a span from the current time to the start time.
            //We will remove time from this span until it goes negative, indicating
            //that we have found the point in the pattern containing the current time.
            var remainingSpan = nowUTC.Subtract(HoldUnholdPatternStartUTC);

            int curIndex = 0;

            do {

                // Decrement the time.
                remainingSpan = remainingSpan.Subtract(HoldUnholdPattern[curIndex]);

                // If we pass 0, we have found the span with the current time.
                if (remainingSpan.Ticks < 0) {
                    //since remainingSpan is negative, we subtract to add the time to the next transition.
                    isOnHold = (curIndex % 2 == 0);
                    nextTransitionUTC = nowUTC.Subtract(remainingSpan);
                    return;
                }

                curIndex += 1;

                // check for repeat patterns
                if (curIndex >= HoldUnholdPattern.Count && HoldUnholdPatternRepeats)
                    curIndex = 0;

            } while (curIndex < HoldUnholdPattern.Count && remainingSpan.Ticks > 0);

            //We are past the end of the pattern, so we are not on hold.
            isOnHold = false;
            nextTransitionUTC = DateTime.MaxValue;
        }

        public JobHoldPattern()
        {
            UserHold = false;
            ReasonForUserHold = "";

            HoldUnholdPattern = new List<TimeSpan>();
            HoldUnholdPatternStartUTC = DateTime.UtcNow;
            HoldUnholdPatternRepeats = false;
        }

        public JobHoldPattern(JobHoldPattern pattern)
        {
            UserHold = pattern.UserHold;
            ReasonForUserHold = pattern.ReasonForUserHold;
            HoldUnholdPattern = new List<TimeSpan>(pattern.HoldUnholdPattern);
            HoldUnholdPatternStartUTC = pattern.HoldUnholdPatternStartUTC;
            HoldUnholdPatternRepeats = pattern.HoldUnholdPatternRepeats;
        }
    }

    [SerializableAttribute, JsonObject(MemberSerialization.OptIn)]
    public class JobPlan
    {
        public string UniqueStr
        {
            get { return _uniqueStr; }
        }

        //general info about the route

        //The overall starting time of the period when we expect the job to run.
        //Note that the job might not immedietly start at this point, the expected
        //start time from the simulation is passed per path in the SimulatedStartingTime
        public DateTime RouteStartingTimeUTC
        {
            get { return _routeStartUTC; }
            set { _routeStartUTC = value; }
        }
        public DateTime RouteEndingTimeUTC
        {
            get { return _routeEndUTC; }
            set { _routeEndUTC = value; }
        }
        public bool Archived
        {
            get { return _archived; }
            set { _archived = value; }
        }
        public string ScheduleId
        {
            get { return _scheduleId; }
            set { _scheduleId = value; }
        }
        public bool JobCopiedToSystem
        {
            get { return _copiedToSystem; }
            set { _copiedToSystem = value; }
        }
        public int Priority
        { // larger number means higher priority
            get { return _priority; }
            set { _priority = value; }
        }
        public string PartName
        {
            get { return _partName; }
            set { _partName = value; }
        }
        public string Comment
        {
            get { return _comment; }
            set { _comment = value; }
        }
        public int NumProcesses
        {
            get { return _procPath.Length; }
        }
        public bool ManuallyCreatedJob
        {
            get { return _manuallyCreated; }
            set { _manuallyCreated = value; }
        }
        public bool CreateMarkerData
        {
            get { return _createMarker; }
            set { _createMarker = value; }
        }
        public ICollection<string> ScheduledBookingIds
        {
            get { return _scheduledIds; }
        }
        public int GetNumPaths(int process)
        {
            if (process >= 1 && process <= NumProcesses) {
                return _procPath[process - 1].Length;
            } else {
                throw new IndexOutOfRangeException("Invalid process number");
            }
        }
        public int GetPathGroup(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].PathGroup;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetPathGroup(int process, int path, int pgroup)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].PathGroup = pgroup;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }


        // Hold Status
        public JobHoldPattern HoldEntireJob
        {
            get { return _holdJob; }
        }
        public JobHoldPattern HoldMachining(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].HoldMachining;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public JobHoldPattern HoldLoadUnload(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].HoldLoadUnload;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }

        // Planned cycles
        public int GetPlannedCyclesOnFirstProcess(int path)
        {
            if (path >= 1 && path <= _pCycles.Length) {
                return _pCycles[path - 1];
            } else {
                throw new IndexOutOfRangeException("Invalid path number");
            }
        }
        public void SetPlannedCyclesOnFirstProcess(int path, int numCycles)
        {
            if (path >= 1 && path <= _pCycles.Length) {
                _pCycles[path - 1] = numCycles;
            } else {
                throw new IndexOutOfRangeException("Invalid path number");
            }
        }

        //Simulated Starting Time
        public DateTime GetSimulatedStartingTimeUTC(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].SimulatedStartingUTC;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetSimulatedStartingTimeUTC(int process, int path, DateTime startUTC)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].SimulatedStartingUTC = startUTC;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<SimulatedProduction> GetSimulatedProduction(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].SimulatedProduction;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetSimulatedProduction(int process, int path, IEnumerable<SimulatedProduction> prod)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].SimulatedProduction = new List<SimulatedProduction>(prod);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public TimeSpan GetSimulatedAverageFlowTime(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].SimulatedAverageFlowTime;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetSimulatedAverageFlowTime(int process, int path, TimeSpan t)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].SimulatedAverageFlowTime = t;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }

        //pallet and fixture information
        public void AddProcessOnPallet(int process, int path, string pallet)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].Pallets.Add(pallet);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void AddProcessOnFixture(int process, int path, string fixture, string face)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                var f = default(FixtureFace);
                f.Fixture = fixture;
                f.Face = face;
                _procPath[process - 1][path - 1].Fixtures.Add(f);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<string> PlannedPallets(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Pallets;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<FixtureFace> PlannedFixtures(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Fixtures;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<string> AllPlannedPallets()
        {
            var ret = new List<string>();
            for (int process = 0; process < NumProcesses; process++) {
                for (int path = 0; path < GetNumPaths(process + 1); path++) {
                    foreach (string pal in _procPath[process][path].Pallets) {
                        if (!ret.Contains(pal))
                            ret.Add(pal);
                    }
                }
            }
            return ret;
        }
        public bool HasPallet(int process, int path, string pallet)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Pallets.Contains(pallet);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public bool HasFixture(int process, int path, string fixture, string face)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                foreach (var f in _procPath[process - 1][path - 1].Fixtures) {
                    if (f.Fixture == fixture && f.Face == face)
                        return true;
                }
                return false;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        /* PartsPerPallet is here for situations when the fixture/face info in CellConfiguration is not used */
        public int PartsPerPallet(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].PartsPerPallet;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void SetPartsPerPallet(int process, int path, int partsPerPallet)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].PartsPerPallet = partsPerPallet;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }

        public IEnumerable<int> LoadStations(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Load;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void AddLoadStation(int process, int path, int statNum)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].Load.Add(statNum);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<int> UnloadStations(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Unload;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void AddUnloadStation(int process, int path, int statNum)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].Unload.Add(statNum);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public IEnumerable<JobMachiningStop> GetMachiningStop(int process, int path)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                return _procPath[process - 1][path - 1].Stops;
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void AddMachiningStop(int process, int path, JobMachiningStop r)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                _procPath[process - 1][path - 1].Stops.Add(r);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }
        public void AddMachiningStops(int process, int path, IEnumerable<JobMachiningStop> stops)
        {
            if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process)) {
                foreach (var s in stops)
                    _procPath[process - 1][path - 1].Stops.Add(s);
            } else {
                throw new IndexOutOfRangeException("Invalid process or path number");
            }
        }

        //Inspection information
        public IEnumerable<JobInspectionData> GetInspections()
        {
            return _inspections;
        }
        public void AddInspection(JobInspectionData insp)
        {
            foreach (var i in _inspections) {
                if (insp.InspectionType == i.InspectionType)
                    throw new ArgumentException("Duplicate inspection types");
            }
            _inspections.Add(insp);
        }
        public void ReplaceInspection(JobInspectionData insp)
        {
            foreach (var i in _inspections) {
                if (insp.InspectionType == i.InspectionType) {
                    _inspections.Remove(i);
                    break;
                }
            }
            _inspections.Add(insp);
        }
        public void AddInspections(IEnumerable<JobInspectionData> insps)
        {
            foreach (var insp in insps)
                AddInspection(insp);
        }

        public JobPlan(string unique, int numProcess) : this(unique, numProcess, null)
        {
        }
        public JobPlan(string unique, int numProcess, int[] numPaths)
        {
            _routeStartUTC = DateTime.MinValue;
            _routeEndUTC = DateTime.MinValue;
            _archived = false;
            _copiedToSystem = false;
            _partName = "";
            _scheduleId = "";
            _uniqueStr = unique;
            _priority = 0;
            _comment = "";
            _manuallyCreated = false;
            _inspections = new List<JobInspectionData>();
            _holdJob = new JobHoldPattern();
            _scheduledIds = new List<string>();

            _procPath = new ProcPathInfo[numProcess][];
            for (int i = 0; i < numProcess; i++) {
                if (numPaths == null || i >= numPaths.Length) {
                    _procPath[i] = new ProcPathInfo[1];
                } else {
                    _procPath[i] = new ProcPathInfo[Math.Max(1, numPaths[i])];
                }
                for (int j = 0; j < _procPath[i].Length; j++) {
                    _procPath[i][j] = new ProcPathInfo(default(ProcPathInfo));
                }
            }
            _pCycles = new int[_procPath[0].Length];
            for (int path = 0; path < _procPath[0].Length; path++) {
                _pCycles[path] = 0;
            }
        }
        public JobPlan(JobPlan job, string newUniqueStr)
        {
            _routeStartUTC = job._routeStartUTC;
            _routeEndUTC = job._routeEndUTC;
            _archived = job._archived;
            _copiedToSystem = job._copiedToSystem;
            _partName = job.PartName;
            _scheduleId = job._scheduleId;
            _uniqueStr = newUniqueStr;
            _priority = job._priority;
            _comment = job._comment;
            _manuallyCreated = job._manuallyCreated;
            _createMarker = job._createMarker;
            _holdJob = new JobHoldPattern(job._holdJob);
            _scheduledIds = new List<string>(job._scheduledIds);

            _inspections = new List<JobInspectionData>(job._inspections.Count);
            foreach (var insp in job._inspections)
                _inspections.Add(new JobInspectionData(insp));

            //copy the path info
            _procPath = new ProcPathInfo[job._procPath.Length][];
            for (int i = 0; i < _procPath.Length; i++) {
                _procPath[i] = new ProcPathInfo[job._procPath[i].Length];
                for (int j = 0; j < _procPath[i].Length; j++) {
                    _procPath[i][j] = new ProcPathInfo(job._procPath[i][j]);
                }
            }

            //do not copy the planned cycles, since we are creating a new job
            _pCycles = new int[_procPath[0].Length];
            for (int path = 0; path < _procPath[0].Length; path++) {
                _pCycles[path] = 0;
            }
        }
        public JobPlan(JobPlan job)
        {
            _routeStartUTC = job._routeStartUTC;
            _routeEndUTC = job._routeEndUTC;
            _archived = job._archived;
            _copiedToSystem = job._copiedToSystem;
            _partName = job.PartName;
            _scheduleId = job._scheduleId;
            _uniqueStr = job._uniqueStr;
            _priority = job._priority;
            _comment = job._comment;
            _manuallyCreated = job._manuallyCreated;
            _createMarker = job._createMarker;
            _holdJob = new JobHoldPattern(job._holdJob);
            _scheduledIds = new List<string>(job._scheduledIds);

            _inspections = new List<JobInspectionData>(job._inspections.Count);
            foreach (var insp in job._inspections)
                _inspections.Add(new JobInspectionData(insp));

            _procPath = new ProcPathInfo[job._procPath.Length][];
            for (int i = 0; i < _procPath.Length; i++) {
                _procPath[i] = new ProcPathInfo[job._procPath[i].Length];
                for (int j = 0; j < _procPath[i].Length; j++) {
                    _procPath[i][j] = new ProcPathInfo(job._procPath[i][j]);
                }
            }
            _pCycles = new int[_procPath[0].Length];
            for (int path = 0; path < _procPath[0].Length; path++) {
                _pCycles[path] = job._pCycles[path];
            }
        }

        [JsonProperty(PropertyName="RouteStartUTC")]
        private DateTime _routeStartUTC;

        [JsonProperty(PropertyName="RouteEndUTC")]
        private DateTime _routeEndUTC;

        [JsonProperty(PropertyName="Archived")]
        private bool _archived;

        [JsonProperty(PropertyName="CopiedToSystem")]
        private bool _copiedToSystem;

        [JsonProperty(PropertyName="PartName")]
        private string _partName;

        [JsonProperty(PropertyName="Comment")]
        private string _comment;

        [JsonProperty(PropertyName="Unique")]
        private string _uniqueStr;

        [JsonProperty(PropertyName="Priority")]
        private int _priority;

        [JsonProperty(PropertyName="ScheduleId", Required=Required.AllowNull)]
        private string _scheduleId;

        [JsonProperty(PropertyName="Bookings")]
        private List<string> _scheduledIds;

        [JsonProperty(PropertyName="ManuallyCreated")]
        private bool _manuallyCreated;

        [JsonProperty(PropertyName="CreateMarkingData")]
        private bool _createMarker;

        [JsonProperty(PropertyName="Inspections")]
        private IList<JobInspectionData> _inspections;

        [JsonProperty(PropertyName="HoldEntireJob")]
        private JobHoldPattern _holdJob;

        [JsonProperty(PropertyName="CyclesOnFirstProcess")]
        private int[] _pCycles;

        [SerializableAttribute, JsonObject(MemberSerialization.OptIn)]
        public struct FixtureFace : IComparable<FixtureFace>
        {
            [JsonProperty] public string Fixture;
            [JsonProperty] public string Face;

            public int CompareTo(FixtureFace o)
            {
                var i = Fixture.CompareTo(o.Fixture);
                if (i < 0) return -1;
                if (i > 0) return 1;
                return Face.CompareTo(o.Face);
            }

            public override string ToString()
            {
                return Fixture + ":" + Face;
            }
        }

        [SerializableAttribute, JsonObject(MemberSerialization.OptIn)]
        public struct SimulatedProduction
        {
            [JsonProperty] public DateTime TimeUTC;
            [JsonProperty] public int Quantity; //total quantity simulated to be completed at TimeUTC
        }

        [SerializableAttribute, JsonObject(MemberSerialization.OptIn)]
        private struct ProcPathInfo
        {
            [JsonProperty] public int PathGroup;
            [JsonProperty] public IList<string> Pallets;
            [JsonProperty] public IList<FixtureFace> Fixtures;
            [JsonProperty] public IList<int> Load;
            [JsonProperty] public IList<int> Unload;
            [JsonProperty] public IList<JobMachiningStop> Stops;
            [JsonProperty] public IList<SimulatedProduction> SimulatedProduction;
            [JsonProperty] public DateTime SimulatedStartingUTC;
            [JsonProperty] public TimeSpan SimulatedAverageFlowTime; // average time a part takes to complete the entire sequence
            [JsonProperty] public JobHoldPattern HoldMachining;
            [JsonProperty] public JobHoldPattern HoldLoadUnload;
            [JsonProperty] public int PartsPerPallet;

            public ProcPathInfo(ProcPathInfo other)
            {
                if (other.Pallets == null) {
                    PathGroup = 0;
                    Pallets = new List<string>();
                    Fixtures = new List<FixtureFace>();
                    Load = new List<int>();
                    Unload = new List<int>();
                    Stops = new List<JobMachiningStop>();
                    SimulatedProduction = new List<SimulatedProduction>();
                    SimulatedStartingUTC = DateTime.MinValue;
                    SimulatedAverageFlowTime = TimeSpan.Zero;
                    HoldMachining = new JobHoldPattern();
                    HoldLoadUnload = new JobHoldPattern();
                    PartsPerPallet = 1;
                } else {
                    PathGroup = other.PathGroup;
                    Pallets = new List<string>(other.Pallets);
                    Fixtures = new List<FixtureFace>(other.Fixtures);
                    Load = new List<int>(other.Load);
                    Unload = new List<int>(other.Unload);
                    Stops = new List<JobMachiningStop>();
                    SimulatedProduction = new List<SimulatedProduction>(other.SimulatedProduction);
                    SimulatedStartingUTC = other.SimulatedStartingUTC;
                    SimulatedAverageFlowTime = other.SimulatedAverageFlowTime;
                    foreach (var s in other.Stops) {
                        Stops.Add(new JobMachiningStop(s));
                    }
                    HoldMachining = new JobHoldPattern(other.HoldMachining);
                    HoldLoadUnload = new JobHoldPattern(other.HoldLoadUnload);
                    PartsPerPallet = other.PartsPerPallet;
                }
            }
        }

        [JsonProperty(PropertyName="ProcsAndPaths")]
        private ProcPathInfo[][] _procPath;
    }

    [SerializableAttribute]
    public class JobCurrentInformation : JobPlan
    {
        [SerializableAttribute]
        public struct Material
        {
            //can be -1 if the job manager does not track material
            public readonly long MaterialId;
            public readonly int Process;
            public readonly int Path;

            public readonly string Pallet;
            public readonly string Fixture;
            public readonly string FaceName;

            public Material(long matID, int proc, int path, string pal, string fix, string face)
            {
                MaterialId = matID;
                Process = proc;
                Path = path;
                Pallet = pal;
                Fixture = fix;
                FaceName = face;
            }
        }

        public IList<Material> MaterialInExecution
        {
            get { return _material; }
        }
        public void AddMaterial(Material mat)
        {
            _material.Add(mat);
        }

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
        public int GetCompletedOnFinalProcess()
        {
            return _totalComplete;
        }
        public void SetCompletedOnFinalProcess(int comp)
        {
            _totalComplete = comp;
        }
        public void IncrementCompletedOnFinalProcess(int comp)
        {
            _totalComplete += comp;
        }

        public JobCurrentInformation(string unique, int numProc) : this(unique, numProc, null)
        {

        }
        public JobCurrentInformation(string unique, int numProc, int[] numPaths) : base(unique, numProc, numPaths)
        {
            _material = new List<Material>();
            _completedProc1 = new int[GetNumPaths(1)];
            for (int i = 0; i < _completedProc1.Length; i++)
                _completedProc1[i] = 0;
            _totalComplete = 0;
        }
        public JobCurrentInformation(JobPlan job) : base(job)
        {
            _material = new List<Material>();
            _completedProc1 = new int[GetNumPaths(1)];
            for (int i = 0; i < _completedProc1.Length; i++)
                _completedProc1[i] = 0;
            _totalComplete = 0;
        }

        private List<Material> _material;
        private int[] _completedProc1;
        private int _totalComplete;
    }

    [SerializableAttribute]
    public class CurrentStatus
    {
        public IDictionary<string, JobCurrentInformation> Jobs
        {
            get { return _jobs; }
        }

        public IDictionary<string, PalletStatus> Pallets
        {
            get { return _pals; }
        }

        public IList<string> Alarms
        {
            get { return _alarms; }
        }

        public string LatestScheduleId {get;}

        public IDictionary<string, int> ExtraPartsForLatestSchedule
        {
            get { return _extraParts; }
        }

        public IList<JobPlan> JobsNotCopiedToSystem { get { return _notCopied; } }

        public CurrentStatus(IEnumerable<JobCurrentInformation> jobs,
                             IDictionary<string, PalletStatus> pals,
                             string latestSchId,
                             IDictionary<string, int> extraParts)
        {
            _jobs = new Dictionary<string, JobCurrentInformation>();
            _pals = new Dictionary<string, PalletStatus>(pals);
            _alarms = new List<string>();
            LatestScheduleId = latestSchId;
            _extraParts = new Dictionary<string, int>(extraParts);

            foreach (var j in jobs)
                _jobs.Add(j.UniqueStr, j);
        }

        public CurrentStatus(IDictionary<string, JobCurrentInformation> jobs,
                             IDictionary<string, PalletStatus> pals,
                             string latestSchId,
                             IDictionary<string, int> extraParts,
                             IEnumerable<JobPlan> notCopied)
        {
            _jobs = new Dictionary<string, JobCurrentInformation>(jobs);
            _pals = new Dictionary<string, PalletStatus>(pals);
            _alarms = new List<string>();
            LatestScheduleId = latestSchId;
            _extraParts = new Dictionary<string, int>(extraParts);
            _notCopied = new List<JobPlan>(notCopied);
        }

        private Dictionary<string, JobCurrentInformation> _jobs;
        private Dictionary<string, PalletStatus> _pals;
        private List<string> _alarms;
        private Dictionary<string, int> _extraParts;
        private List<JobPlan> _notCopied;
    }

    [SerializableAttribute]
    public class SimulatedStationUtilization
    {
        public string SimulationId; // a unique string shared between all utilization structs that are part of the same simulation
        public string StationGroup;
        public int StationNum;
        public DateTime StartUTC;
        public DateTime EndUTC;
        public TimeSpan UtilizationTime; //time between StartUTC and EndUTC the station is busy.
        public TimeSpan PlannedDownTime; //time between StartUTC and EndUTC the station is planned to be down.

        public SimulatedStationUtilization(string id, string group, int num, DateTime start, DateTime endT, TimeSpan u, TimeSpan d)
        {
            SimulationId = id;
            StationGroup = group;
            StationNum = num;
            StartUTC = start;
            EndUTC = endT;
            UtilizationTime = u;
            PlannedDownTime = d;
        }
    }

    [SerializableAttribute]
    public struct NewJobs
    {
        public string ScheduleId;
        public List<JobPlan> Jobs;
        public List<SimulatedStationUtilization> StationUse;
        public Dictionary<string, int> ExtraParts;
        public bool ArchiveCompletedJobs;

        public NewJobs(string scheduleId,
                       IEnumerable<JobPlan> newJobs,
                       IEnumerable<SimulatedStationUtilization> stationUse = null,
                       Dictionary<string, int> extraParts = null,
                       bool archiveCompletedJobs = false)
        {
            this.ScheduleId = scheduleId;
            this.Jobs = newJobs.ToList();
            this.StationUse =
                stationUse == null ? new List<SimulatedStationUtilization>() : stationUse.ToList();
            this.ExtraParts =
                extraParts == null ? new Dictionary<string, int>() : extraParts;
            this.ArchiveCompletedJobs = archiveCompletedJobs;
        }
    }

    [SerializableAttribute()]
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

    [SerializableAttribute]
    public struct HistoricData
    {
        public IDictionary<string, JobPlan> Jobs;
        public ICollection<SimulatedStationUtilization> StationUse;
    }

    [SerializableAttribute]
    public struct JobsAndExtraParts
    {
        public string LatestScheduleId;
        public List<JobPlan> Jobs;
        public Dictionary<string, int> ExtraParts;
    }

    [SerializableAttribute]
    public struct JobAndDecrementQuantity
    {
        public string DecrementId;
        public string JobUnique;
        public DateTime TimeUTC;
        public string Part;
        public int Quantity;
    }
}
