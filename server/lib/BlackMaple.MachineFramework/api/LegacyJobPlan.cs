/* Copyright (c) 2021, John Lenz

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
using System.Collections.Immutable;
using Germinate;

namespace BlackMaple.MachineWatchInterface
{

  //stores information about a single "stop" of the pallet in a route
  [DataContract]
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

    public string ProgramName { get => _program; set => _program = value; }

    public long? ProgramRevision { get => _programRevision; set => _programRevision = value; }

    public IList<int> Stations
    {
      get
      {
        if (_stations == null) _stations = new List<int>();
        return _stations;
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
      _stations = new List<int>();
      _tools = new Dictionary<string, TimeSpan>();
      _expectedCycleTime = TimeSpan.Zero;
    }

    public JobMachiningStop(JobMachiningStop stop)
    {
      _statGroup = stop._statGroup;
      _stations = new List<int>(stop._stations);
      _program = stop._program;
      _programRevision = stop._programRevision;
      _expectedCycleTime = stop._expectedCycleTime;
      _tools = new Dictionary<string, TimeSpan>(stop._tools);
    }

    [DataMember(Name = "StationNums")]
    private List<int> _stations;

    [DataMember(Name = "Program")]
    private string _program;

    // During Download:
    //   * A null or zero revision value means use the latest program, either the one already in the cell controller
    //     or the most recent revision in the database.
    //   * A positive revision number will use this specified revision which must exist in the database or
    //     be included in the Programs field of the NewJobs structure accompaning the downloaded job.
    //   * A negative revision number must match a ProgramEntry in the NewJobs structure accompaning the download.
    //     The ProgramEntry will be assigned a (positive) revision during the download and then this stop will
    //     be updated to use that (positive) revision.
    // When loading Jobs,
    //   * A null revision means the program already exists in the cell controller and the DB is not managing programs.
    //   * A positive revision means the program exists in the job DB with this specific revision.
    //   * Negative revisions are never returned (they get translated as part of the download)
    [DataMember(Name = "ProgramRevision")]
    private long? _programRevision;

    [DataMember(Name = "Tools", IsRequired = true)]
    private Dictionary<string, TimeSpan> _tools; //key is tool, value is expected cutting time

    [DataMember(Name = "StationGroup", IsRequired = true)]
    private string _statGroup;

    [DataMember(Name = "ExpectedCycleTime", IsRequired = true)]
    private TimeSpan _expectedCycleTime;

    private JobMachiningStop() { } //for json deserialization

    // include old stations format for backwards compatibility
    [DataMember(Name = "Stations", IsRequired = false), Obsolete]
    private Dictionary<int, string> OldPrograms
    {
      get
      {
        if (_stations == null) return null;
        var d = new Dictionary<int, string>();
        foreach (var s in _stations)
        {
          d[s] = _program;
        }
        return d;
      }
      set
      {
        _stations = value.Keys.ToList();
        if (value.Count > 0)
        {
          _program = value.Values.First();
          _programRevision = 0;
        }
      }
    }
  }

  // JobInspectionData is the old format before we added ability to control per-path
  // It is kept for backwards compatability, but new stuff should use PathInspection instead.
  [DataContract]
  public class JobInspectionData
  {
    [DataMember(IsRequired = true)]
    public readonly string InspectionType;

    //There are two possible ways of triggering an exception: counts and frequencies.
    // * For counts, the MaxVal will contain a number larger than zero and RandomFreq will contain -1
    // * For frequencies, the value of MaxVal is -1 and RandomFreq contains
    //   the frequency as a number between 0 and 1.

    //Every time a material completes, the counter string is expanded (see below).
    [DataMember(IsRequired = true)]
    public readonly string Counter;

    //For each completed material, the counter is incremented.  If the counter is equal to MaxVal,
    //we signal an inspection and reset the counter to 0.
    [DataMember(IsRequired = true)]
    public readonly int MaxVal;

    //The random frequency of inspection
    [DataMember(IsRequired = true)]
    public readonly double RandomFreq;

    //If the last inspection signaled for this counter was longer than TimeInterval,
    //signal an inspection.  This can be disabled by using TimeSpan.Zero
    [DataMember(IsRequired = true)]
    public readonly TimeSpan TimeInterval;

    //If set to -1, the entire job should be inspected once the job completes.
    //If set to a positive number, only that process should have the inspection triggered.
    [DataMember(IsRequired = true)]
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

    private JobInspectionData() { } //for json deserialization
  }

  [DataContract]
  public class JobHoldPattern
  {
    // All of the following hold types are an OR, meaning if any one of them says a hold is in effect,
    // the job is on hold.

    [DataMember(IsRequired = true)]
    public bool UserHold;

    [DataMember(IsRequired = true)]
    public string ReasonForUserHold;

    //A list of timespans the job should be on hold/not on hold.
    //During the first timespan, the job is on hold.
    [DataMember(IsRequired = true)]
    public readonly IList<TimeSpan> HoldUnholdPattern;

    [DataMember(IsRequired = true)]
    public DateTime HoldUnholdPatternStartUTC;

    [DataMember(IsRequired = true)]
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

      if (UserHold)
      {
        isOnHold = true;
        nextTransitionUTC = DateTime.MaxValue;
        return;
      }

      if (HoldUnholdPattern.Count == 0)
      {
        isOnHold = false;
        nextTransitionUTC = DateTime.MaxValue;
        return;
      }

      //check that the hold pattern has a non-zero timespan.
      //Without this check we will get in an infinite loop below.
      bool foundSpan = false;
      foreach (var span in HoldUnholdPattern)
      {
        if (span.Ticks > 0)
        {
          foundSpan = true;
          break;
        }
      }
      if (!foundSpan)
      {
        //If not, then we are not on hold.
        isOnHold = false;
        nextTransitionUTC = DateTime.MaxValue;
        return;
      }

      if (nowUTC < HoldUnholdPatternStartUTC)
      {
        isOnHold = false;
        nextTransitionUTC = HoldUnholdPatternStartUTC;
      }

      //Start with a span from the current time to the start time.
      //We will remove time from this span until it goes negative, indicating
      //that we have found the point in the pattern containing the current time.
      var remainingSpan = nowUTC.Subtract(HoldUnholdPatternStartUTC);

      int curIndex = 0;

      do
      {

        // Decrement the time.
        remainingSpan = remainingSpan.Subtract(HoldUnholdPattern[curIndex]);

        // If we pass 0, we have found the span with the current time.
        if (remainingSpan.Ticks < 0)
        {
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
      HoldUnholdPatternStartUTC = new DateTime(2000, 1, 1);
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

  [DataContract]
  public partial class JobPlan
  {
    public string UniqueStr
    {
      get { return _uniqueStr; }
    }

    //general info about the route

    //The overall starting time of the period when we expect the job to run.
    //Note that the job might not immediately start at this point, the expected
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
    public ICollection<string> ScheduledBookingIds
    {
      get { return _scheduledIds; }
    }
    public int GetNumPaths(int process)
    {
      if (process >= 1 && process <= NumProcesses)
      {
        return _procPath[process - 1].NumPaths;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process number");
      }
    }
    public int GetPathGroup(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].PathGroup;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetPathGroup(int process, int path, int pgroup)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].PathGroup = pgroup;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }


    // Hold Status
    public JobHoldPattern HoldEntireJob
    {
      get { return _holdJob; }
      set { _holdJob = value; }
    }
    public JobHoldPattern HoldMachining(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].HoldMachining;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetHoldMachining(int process, int path, JobHoldPattern hold)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].HoldMachining = hold;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public JobHoldPattern HoldLoadUnload(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].HoldLoadUnload;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetHoldLoadUnload(int process, int path, JobHoldPattern hold)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].HoldLoadUnload = hold;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }

    // Planned cycles
    public int GetPlannedCyclesOnFirstProcess(int path)
    {
      if (path >= 1 && path <= _pCycles.Length)
      {
        return _pCycles[path - 1];
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid path number");
      }
    }
    public void SetPlannedCyclesOnFirstProcess(int path, int numCycles)
    {
      if (path >= 1 && path <= _pCycles.Length)
      {
        _pCycles[path - 1] = numCycles;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid path number");
      }
    }

    //Simulated Starting Time
    public DateTime GetSimulatedStartingTimeUTC(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].SimulatedStartingUTC;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetSimulatedStartingTimeUTC(int process, int path, DateTime startUTC)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].SimulatedStartingUTC = startUTC;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public IEnumerable<SimulatedProduction> GetSimulatedProduction(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].SimulatedProduction;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetSimulatedProduction(int process, int path, IEnumerable<SimulatedProduction> prod)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].SimulatedProduction = new List<SimulatedProduction>(prod);
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public TimeSpan GetSimulatedAverageFlowTime(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].SimulatedAverageFlowTime;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetSimulatedAverageFlowTime(int process, int path, TimeSpan t)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].SimulatedAverageFlowTime = t;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }

    //pallet and fixture information
    public void AddProcessOnPallet(int process, int path, string pallet)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1][path - 1].Pallets.Add(pallet);
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetFixtureFace(int process, int path, string fixture, int face)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].Fixture = fixture;
        _procPath[process - 1].Paths[path - 1].Face = face;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }

    }
    public IEnumerable<string> PlannedPallets(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].Pallets;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
#if NET35
    // tuples don't work in net3.5
    public void PlannedFixture(int process, int path, out string fixture, out int face)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        fixture = _procPath[process - 1][path - 1].Fixture;
        face = _procPath[process - 1][path - 1].Face ?? 1;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
#else
    public (string fixture, int face) PlannedFixture(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return (fixture: _procPath[process - 1][path - 1].Fixture, face: _procPath[process - 1][path - 1].Face ?? 1);
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
#endif
    public IEnumerable<string> AllPlannedPallets()
    {
      var ret = new List<string>();
      for (int process = 0; process < NumProcesses; process++)
      {
        for (int path = 0; path < GetNumPaths(process + 1); path++)
        {
          foreach (string pal in _procPath[process][path].Pallets)
          {
            if (!ret.Contains(pal))
              ret.Add(pal);
          }
        }
      }
      return ret;
    }
    public bool HasPallet(int process, int path, string pallet)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].Pallets.Contains(pallet);
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public int PartsPerPallet(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].PartsPerPallet;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetPartsPerPallet(int process, int path, int partsPerPallet)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].PartsPerPallet = partsPerPallet;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }

    public IEnumerable<int> LoadStations(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].Load;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void AddLoadStation(int process, int path, int statNum)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1][path - 1].Load.Add(statNum);
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    //Expected Load time is per material, need to multiply by PartsPerPallet to get total time
    public TimeSpan GetExpectedLoadTime(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1].Paths[path - 1].ExpectedLoadTime;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetExpectedLoadTime(int process, int path, TimeSpan t)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].ExpectedLoadTime = t;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public IEnumerable<int> UnloadStations(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].Unload;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void AddUnloadStation(int process, int path, int statNum)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1][path - 1].Unload.Add(statNum);
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    //Expected Unload time is per material, need to multiply by PartsPerPallet to get total time
    public TimeSpan GetExpectedUnloadTime(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1].Paths[path - 1].ExpectedUnloadTime;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetExpectedUnloadTime(int process, int path, TimeSpan t)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].ExpectedUnloadTime = t;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public IEnumerable<JobMachiningStop> GetMachiningStop(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].Stops;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void AddMachiningStop(int process, int path, JobMachiningStop r)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1][path - 1].Stops.Add(r);
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void AddMachiningStops(int process, int path, IEnumerable<JobMachiningStop> stops)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        foreach (var s in stops)
          _procPath[process - 1][path - 1].Stops.Add(s);
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public string GetInputQueue(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].InputQueue;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public string GetCasting(int proc1path)
    {
      if (proc1path >= 1 && proc1path <= GetNumPaths(1))
      {
        return _procPath[0][proc1path - 1].Casting;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid path number");
      }
    }
    public string GetOutputQueue(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        return _procPath[process - 1][path - 1].OutputQueue;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetInputQueue(int process, int path, string queue)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].InputQueue = queue;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public void SetCasting(int proc1path, string casting)
    {
      if (proc1path >= 1 && proc1path <= GetNumPaths(1))
      {
        _procPath[0].Paths[proc1path - 1].Casting = casting;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid path number");
      }
    }
    public void SetOutputQueue(int process, int path, string queue)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        _procPath[process - 1].Paths[path - 1].OutputQueue = queue;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }
    public ICollection<MachineFramework.PathInspection> PathInspections(int process, int path)
    {
      if (process >= 1 && process <= NumProcesses && path >= 1 && path <= GetNumPaths(process))
      {
        if (_procPath[process - 1].Paths[path - 1].Inspections == null)
        {
          _procPath[process - 1].Paths[path - 1].Inspections = new List<MachineFramework.PathInspection>();
        }
        return _procPath[process - 1].Paths[path - 1].Inspections;
      }
      else
      {
        throw new IndexOutOfRangeException("Invalid process or path number");
      }
    }

    //Inspection information
    public IEnumerable<JobInspectionData> GetOldObsoleteInspections()
    {
#pragma warning disable CS0612
      return _inspections;
#pragma warning restore CS0612
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
      _comment = "";
      _manuallyCreated = false;
      _holdJob = new JobHoldPattern();
      _scheduledIds = new List<string>();

      _procPath = new ProcessInfo[numProcess];
      for (int i = 0; i < numProcess; i++)
      {
        if (numPaths == null || i >= numPaths.Length)
        {
          _procPath[i].Paths = new ProcPathInfo[1];
        }
        else
        {
          _procPath[i].Paths = new ProcPathInfo[Math.Max(1, numPaths[i])];
        }
        for (int j = 0; j < _procPath[i].NumPaths; j++)
        {
          _procPath[i].Paths[j] = new ProcPathInfo(default(ProcPathInfo));
        }
      }
      _pCycles = new int[_procPath[0].NumPaths];
      for (int path = 0; path < _procPath[0].NumPaths; path++)
      {
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
      _comment = job._comment;
      _manuallyCreated = job._manuallyCreated;
      _holdJob = new JobHoldPattern(job._holdJob);
      _scheduledIds = new List<string>(job._scheduledIds);

#pragma warning disable CS0612
      if (job._inspections != null)
      {
        _inspections = new List<JobInspectionData>(job._inspections.Count);
        foreach (var insp in job._inspections)
          _inspections.Add(new JobInspectionData(insp));
      }
#pragma warning restore CS0612

      //copy the path info
      _procPath = new ProcessInfo[job._procPath.Length];
      for (int i = 0; i < _procPath.Length; i++)
      {
        _procPath[i].Paths = new ProcPathInfo[job._procPath[i].NumPaths];
        for (int j = 0; j < _procPath[i].NumPaths; j++)
        {
          _procPath[i].Paths[j] = new ProcPathInfo(job._procPath[i][j]);
        }
      }

      //do not copy the planned cycles, since we are creating a new job
      _pCycles = new int[_procPath[0].NumPaths];
      for (int path = 0; path < _procPath[0].NumPaths; path++)
      {
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
      _comment = job._comment;
      _manuallyCreated = job._manuallyCreated;
      _holdJob = new JobHoldPattern(job._holdJob);
      _scheduledIds = new List<string>(job._scheduledIds);

#pragma warning disable CS0612
      if (job._inspections != null)
      {
        _inspections = new List<JobInspectionData>(job._inspections.Count);
        foreach (var insp in job._inspections)
          _inspections.Add(new JobInspectionData(insp));
      }
#pragma warning restore CS0612

      _procPath = new ProcessInfo[job._procPath.Length];
      for (int i = 0; i < _procPath.Length; i++)
      {
        _procPath[i].Paths = new ProcPathInfo[job._procPath[i].NumPaths];
        for (int j = 0; j < _procPath[i].NumPaths; j++)
        {
          _procPath[i].Paths[j] = new ProcPathInfo(job._procPath[i][j]);
        }
      }
      _pCycles = new int[_procPath[0].NumPaths];
      for (int path = 0; path < _procPath[0].NumPaths; path++)
      {
        _pCycles[path] = job._pCycles[path];
      }
    }

    [DataMember(Name = "RouteStartUTC", IsRequired = true)]
    private DateTime _routeStartUTC;

    [DataMember(Name = "RouteEndUTC", IsRequired = true)]
    private DateTime _routeEndUTC;

    [DataMember(Name = "Archived", IsRequired = true)]
    private bool _archived;

    [DataMember(Name = "CopiedToSystem", IsRequired = true)]
    private bool _copiedToSystem;

    [DataMember(Name = "PartName", IsRequired = true)]
    private string _partName;

    [DataMember(Name = "Comment", IsRequired = false, EmitDefaultValue = false)]
    private string _comment;

    [DataMember(Name = "Unique", IsRequired = true)]
    private string _uniqueStr;

#pragma warning disable CS0169
    // priority and CreateMarkingData field is no longer used but this is kept for backwards network compatibility
    [DataMember(Name = "Priority", IsRequired = false, EmitDefaultValue = false), Obsolete]
    private int _priority;

    [DataMember(Name = "CreateMarkingData", IsRequired = false, EmitDefaultValue = true), Obsolete]
    private bool _createMarker;
#pragma warning restore CS0169

    [DataMember(Name = "ScheduleId", IsRequired = false, EmitDefaultValue = false)]
    private string _scheduleId;

    [DataMember(Name = "Bookings", IsRequired = false, EmitDefaultValue = false)]
    private List<string> _scheduledIds;

    [DataMember(Name = "ManuallyCreated", IsRequired = true)]
    private bool _manuallyCreated;

    [DataMember(Name = "Inspections", IsRequired = false, EmitDefaultValue = false), Obsolete]
    private IList<JobInspectionData> _inspections;

    [DataMember(Name = "HoldEntireJob", IsRequired = false, EmitDefaultValue = false)]
    private JobHoldPattern _holdJob;

    [DataMember(Name = "CyclesOnFirstProcess", IsRequired = true)]
    private int[] _pCycles;

    [DataContract]
    private struct FixtureFace : IComparable<FixtureFace>
    {
#pragma warning disable CS0649
      [DataMember(IsRequired = true)] public string Fixture;
      [DataMember(IsRequired = true)] public string Face;
#pragma warning restore CS0649

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

    [DataContract]
    public struct SimulatedProduction
    {
      [DataMember(IsRequired = true)] public DateTime TimeUTC;
      [DataMember(IsRequired = true)] public int Quantity; //total quantity simulated to be completed at TimeUTC
    }

    [DataContract]
    private struct ProcessInfo
    {
      [DataMember(Name = "paths", IsRequired = true)]
      public ProcPathInfo[] Paths;
      public ProcPathInfo this[int i] => Paths[i];
      public int NumPaths => Paths.Length;
    }

    [DataContract]
    private struct ProcPathInfo
    {
      [DataMember(IsRequired = true)]
      public int PathGroup;

      [DataMember(IsRequired = true)]
      public IList<string> Pallets;

      [DataMember(IsRequired = false, EmitDefaultValue = false), Obsolete]
      private IList<FixtureFace> Fixtures
      {
        set
        {
          if (value.Count > 0)
          {
            var f = value[0];
            Fixture = f.Fixture;
            if (int.TryParse(f.Face, out var fNum))
            {
              Face = fNum;
            }
          }

        }
      }

      [DataMember(IsRequired = false)]
      public string Fixture;

      [DataMember(IsRequired = false)]
      public int? Face;

      [DataMember(IsRequired = true)]
      public IList<int> Load;

      [DataMember(IsRequired = false)]
      public TimeSpan ExpectedLoadTime;

      [DataMember(IsRequired = true)]
      public IList<int> Unload;

      [DataMember(IsRequired = false)]
      public TimeSpan ExpectedUnloadTime;

      [DataMember(IsRequired = true)]
      public IList<JobMachiningStop> Stops;

      [DataMember(IsRequired = false, EmitDefaultValue = false)]
      public IList<SimulatedProduction> SimulatedProduction;

      [DataMember(IsRequired = true)]
      public DateTime SimulatedStartingUTC;

      [DataMember(IsRequired = true)]
      public TimeSpan SimulatedAverageFlowTime; // average time a part takes to complete the entire sequence

      [DataMember(IsRequired = false, EmitDefaultValue = false)]
      public JobHoldPattern HoldMachining;

      [DataMember(IsRequired = false, EmitDefaultValue = false)]
      public JobHoldPattern HoldLoadUnload;

      [DataMember(IsRequired = true)]
      public int PartsPerPallet;

      [DataMember(IsRequired = false, EmitDefaultValue = false), OptionalField]
      public string InputQueue;

      [DataMember(IsRequired = false, EmitDefaultValue = false), OptionalField]
      public string OutputQueue;

      [DataMember(IsRequired = false, EmitDefaultValue = false), OptionalField]
      public List<MachineFramework.PathInspection> Inspections;

      [DataMember(IsRequired = false, EmitDefaultValue = false), OptionalField]
      public string Casting;

      public ProcPathInfo(ProcPathInfo other)
      {
        if (other.Pallets == null)
        {
          PathGroup = 0;
          Pallets = new List<string>();
          Fixture = null;
          Face = 0;
          Load = new List<int>();
          ExpectedLoadTime = TimeSpan.Zero;
          Unload = new List<int>();
          ExpectedUnloadTime = TimeSpan.Zero;
          Stops = new List<JobMachiningStop>();
          SimulatedProduction = new List<SimulatedProduction>();
          SimulatedStartingUTC = DateTime.MinValue;
          SimulatedAverageFlowTime = TimeSpan.Zero;
          HoldMachining = new JobHoldPattern();
          HoldLoadUnload = new JobHoldPattern();
          PartsPerPallet = 1;
          InputQueue = null;
          OutputQueue = null;
          Inspections = null;
          Casting = null;
        }
        else
        {
          PathGroup = other.PathGroup;
          Pallets = new List<string>(other.Pallets);
          Fixture = other.Fixture;
          Face = other.Face;
          Load = new List<int>(other.Load);
          ExpectedLoadTime = other.ExpectedLoadTime;
          Unload = new List<int>(other.Unload);
          ExpectedUnloadTime = other.ExpectedUnloadTime;
          Stops = new List<JobMachiningStop>();
          SimulatedProduction = new List<SimulatedProduction>(other.SimulatedProduction);
          SimulatedStartingUTC = other.SimulatedStartingUTC;
          SimulatedAverageFlowTime = other.SimulatedAverageFlowTime;
          foreach (var s in other.Stops)
          {
            Stops.Add(new JobMachiningStop(s));
          }
          HoldMachining = new JobHoldPattern(other.HoldMachining);
          HoldLoadUnload = new JobHoldPattern(other.HoldLoadUnload);
          PartsPerPallet = other.PartsPerPallet;
          InputQueue = other.InputQueue;
          OutputQueue = other.OutputQueue;
          Inspections = other.Inspections?.ToList();
          Casting = other.Casting;
        }
      }
    }

    [DataMember(Name = "ProcsAndPaths", IsRequired = true)]
    private ProcessInfo[] _procPath;

    protected JobPlan() { } //for json deserialization
  }


  public static class LegacyJobConversions
  {
    public static JobPlan ToLegacyJob(this MachineFramework.Job job, bool copiedToSystem, string scheduleId)
    {
      var newJob = new JobPlan(job.UniqueStr, job.Processes.Count, job.Processes.Select(p => p.Paths.Count).ToArray());
      ToLegacyJob(job, newJob, copiedToSystem, scheduleId);
      return newJob;
    }

    public static JobPlan ToLegacyJob(this MachineFramework.HistoricJob job)
    {
      return ToLegacyJob(job, job.CopiedToSystem, job.ScheduleId);
    }

    private static JobHoldPattern ToLegacyHold(MachineFramework.HoldPattern h)
    {
      var ret = new JobHoldPattern()
      {
        UserHold = h.UserHold,
        ReasonForUserHold = h.ReasonForUserHold,
        HoldUnholdPatternStartUTC = h.HoldUnholdPatternStartUTC,
        HoldUnholdPatternRepeats = h.HoldUnholdPatternRepeats
      };
      foreach (var p in h.HoldUnholdPattern) ret.HoldUnholdPattern.Add(p);
      return ret;
    }

    private static void ToLegacyJob(MachineFramework.Job source, JobPlan dest, bool copiedToSystem, string scheduleId)
    {
      dest.Comment = source.Comment;
      dest.ManuallyCreatedJob = source.ManuallyCreated;
      if (source.BookingIds != null) foreach (var b in source.BookingIds) dest.ScheduledBookingIds.Add(b);
      dest.ScheduleId = scheduleId;
      dest.PartName = source.PartName;
      dest.JobCopiedToSystem = copiedToSystem;
      dest.Archived = source.Archived;
      dest.RouteStartingTimeUTC = source.RouteStartUTC;
      dest.RouteEndingTimeUTC = source.RouteEndUTC;
      if (source.HoldJob != null) dest.HoldEntireJob = ToLegacyHold(source.HoldJob);

      var firstProcCycles = source.CyclesOnFirstProcess.ToList();
      for (int path = 1; path <= firstProcCycles.Count; path++)
      {
        dest.SetPlannedCyclesOnFirstProcess(path: path, numCycles: firstProcCycles[path - 1]);
      }

      // Ignore obsolete job-level inspections

      // ProcsAndPaths
      var procs = source.Processes.ToList();
      for (int proc = 1; proc <= procs.Count; proc++)
      {
        var paths = procs[proc - 1].Paths.ToList();
        for (int path = 1; path <= paths.Count; path++)
        {
          var p = paths[path - 1];

          dest.SetExpectedUnloadTime(proc, path, p.ExpectedUnloadTime);
          dest.SetOutputQueue(proc, path, p.OutputQueue);
          dest.SetInputQueue(proc, path, p.InputQueue);
          dest.SetPartsPerPallet(proc, path, p.PartsPerPallet);
          if (p.HoldLoadUnload != null) dest.SetHoldLoadUnload(proc, path, ToLegacyHold(p.HoldLoadUnload));
          if (p.HoldMachining != null) dest.SetHoldMachining(proc, path, ToLegacyHold(p.HoldMachining));
          dest.SetSimulatedAverageFlowTime(proc, path, p.SimulatedAverageFlowTime);
          dest.SetSimulatedStartingTimeUTC(proc, path, p.SimulatedStartingUTC);
          if (p.SimulatedProduction != null)
          {
            dest.SetSimulatedProduction(proc, path, p.SimulatedProduction.Select(s => new JobPlan.SimulatedProduction()
            {
              TimeUTC = s.TimeUTC,
              Quantity = s.Quantity
            }).ToList());
          }
          if (p.Stops != null)
          {
            foreach (var s in p.Stops)
            {
              var newStop = new JobMachiningStop(s.StationGroup)
              {
                StationGroup = s.StationGroup,
                ExpectedCycleTime = s.ExpectedCycleTime,
                ProgramName = s.Program,
                ProgramRevision = s.ProgramRevision,
              };
              if (s.Stations != null) foreach (var n in s.Stations) newStop.Stations.Add(n);
              if (s.Tools != null) foreach (var t in s.Tools) newStop.Tools.Add(t.Key, t.Value);
              dest.AddMachiningStop(proc, path, newStop);
            }
          }
          if (proc == 1) dest.SetCasting(path, p.Casting);
          if (p.Unload != null) foreach (var s in p.Unload) dest.AddUnloadStation(proc, path, s);
          dest.SetExpectedLoadTime(proc, path, p.ExpectedLoadTime);
          if (p.Load != null) foreach (var s in p.Load) dest.AddLoadStation(proc, path, s);
          if (!string.IsNullOrEmpty(p.Fixture)) dest.SetFixtureFace(proc, path, p.Fixture, p.Face ?? 1);
          if (p.Pallets != null) foreach (var pal in p.Pallets) dest.AddProcessOnPallet(proc, path, pal);
          dest.SetPathGroup(proc, path, p.PathGroup);
          dest.PathInspections(proc, path); // always create the inspections to be non-null
          if (p.Inspections != null)
          {
            foreach (var i in p.Inspections)
            {
              dest.PathInspections(proc, path).Add(new MachineFramework.PathInspection()
              {
                InspectionType = i.InspectionType,
                Counter = i.Counter,
                MaxVal = i.MaxVal,
                RandomFreq = i.RandomFreq,
                TimeInterval = i.TimeInterval,
                ExpectedInspectionTime = i.ExpectedInspectionTime
              });
            }
          }
        }
      }
    }
  }
}