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

namespace BlackMaple.MachineWatchInterface
{
    [SerializableAttribute()]
    public struct InspectCount
    {
        public string Counter;
        public int Value;
        public DateTime LastUTC;
    }

    ///<summary>An inspection type with the rules for how inspections should be signaled</summary>
    ///<remarks>
    ///  <para>
    ///  When checking if a piece of material should be inspected, machine watch
    ///  keeps track of how many parts have completed since the previous inspection and how
    ///  long it has been since the previous inspection.  The three properties <c>TrackPartName</c>,
    ///  <c>TrackPalletName</c>, and <c>TrackStationName</c> determine how many separate
    ///  buckets parts are tracked in.
    ///  </para>
    ///  <para>
    ///  For example, consider that TrackPartName is True and TrackPalletName, TrackStationName are
    ///  False.  In this case, Machine Watch will keep track for each part name the quantity of parts
    ///  produced and how long it has been since the last inspection.  When deciding if a piece of
    ///  material should be inspected, machine watch uses the per-partname count and timing to decide.
    ///  </para>
    ///  <para>
    ///  If instead both TrackPartName and TrackPalletName are True (and TrackStationName is still
    ///  False), then Machine Watch will keep separate quantities for each possible combination of
    ///  part name and pallet name.  For example, Machine Watch will keep track that there have
    ///  been 15 parts with name ABC on pallet 12 since a part ABC which ran on pallet 12 was inspected.  
    ///  Inspections and completed ABC parts on different pallets will go into a separate tracking,
    ///  so for example if an ABC part from pallet 5 was inspected, it won't have an impact on the
    ///  tracking of the (ABC, pallet 12) combination.  Thus, an ABC from pallet 12 will eventually
    ///  be guaranteed to be inspected.
    ///  </para>
    ///  <para>
    ///  In general, Machine Watch will track separate quantities and time since previous inspection
    ///  for a range of possible buckets.  The buckets to track parts are determined by using all
    ///  combinations of the booleans from among TrackPartName, TrackPalletName, and TrackStationName
    ///  are true.  This allows fine-graned control and allows the user to make sure that each possible
    ///  path through the system is periodically inspected.
    ///  </para>
    ///</remarks>
    [SerializableAttribute] public class InspectionType
    {
        ///<summary>Inspection name: unique identifier for this inspection type</summary>
        public string Name { get; set; }

        ///<summary>Whether to track the part name when tracking quantities and time</summary>
        public bool TrackPartName { get; set; }

        ///<summary>Whether to track the pallet name when tracking quantities and time</summary>
        public bool TrackPalletName { get; set; }

        ///<summary>Whether to track the pallet name when tracking quantities and time</summary>
        public bool TrackStationName { get; set; }

        ///<summary>Whether this inspection type is for a single process or the part as a whole</summary>
        ///<remarks>
        ///  <para>A value of -1 means the inspection happens at the end once the entire part is completed.
        ///  A positive value means the inspection should be triggered on the specific process number.
        ///  </para>
        ///</remarks>
        public int InspectSingleProcess { get; set; }

        ///<summary>The default count of completed parts to trigger an inspection</summary>
        ///<remarks>
        ///  <para>
        ///  If this value is 0, the count of parts is not used.  Otherwise, once the number
        ///  of completed parts (in the bucket determined by the tracking properties) reaches
        ///  this count an inspection will be triggered and the count reset to 0.
        ///  </para>
        ///  <para>
        ///  This value is used if no <c>InspectionFrequencyOverride</c> is found.
        ///  </para>
        ///</remarks>
        public int DefaultCountToTriggerInspection { get; set; }
        
        ///<summary>The default time before an inspection is triggered</summary>
        ///<remarks>
        ///  <para>
        ///  If this value is 0, the time is not used.  Otherwise, once the time since a
        ///  previous inspection (in the bucket determined by the tracking properties) exceeds
        ///  this value an inspection will be triggered.
        ///  </para>
        ///  <para>
        ///  This value is used if no <c>InspectionFrequencyOverride</c> is found.
        ///  </para>
        ///</remarks>
        public TimeSpan DefaultDeadline { get; set; }

        ///<summary>The default random frequency (between 0 and 1) for inspections</summary>
        ///<remarks>
        ///  <para>
        ///  Random Frequency inspection is only used if counting is not used.  Therefore, if
        ///  CountToTriggerInspection is non-zero, the random frequency is ignored.
        ///  In addition, if this value is 0, no random inspections are made.
        ///  Otherwise, the value is treated as a probability (a number between 0 and 1)
        //   and each part will have this probability of being triggered for an inspection.
        ///  </para>
        ///  <para>
        ///  This value is used if no <c>InspectionFrequencyOverride</c> is found.
        ///  </para>
        ///</remarks>
        public double DefaultRandomFreq {get; set;}

        ///<summary>This allows specific overrides of the inspection triggers</summary>
        ///<remarks>
        ///  <para>
        ///  For a specific part name, the default count before inspection, the default deadline,
        ///  and the default random frequency can be overriden.
        ///  </para>
        ///</remarks>
        public List<InspectionFrequencyOverride> Overrides;

        public JobInspectionData ConvertToJobInspection(string part, int numProc)
        {
            int maxCnt = DefaultCountToTriggerInspection;
            TimeSpan deadline = DefaultDeadline;
            double randFreq = DefaultRandomFreq;
            foreach (var o in Overrides) {
                if (o.Part == part) {
                    maxCnt = o.CountBeforeInspection;
                    deadline = o.Deadline;
                    randFreq = o.RandomFreq;
                }
            }

            string cntr;
            if (TrackPartName)
                cntr = part + "," + Name;
            else
                cntr = "," + Name;
            for (int i = 1; i <= numProc; i++) {
                if (TrackPalletName) {
                    cntr += ",P" + JobInspectionData.PalletFormatFlag(i);
                }
                if (TrackStationName) {
                    cntr += ",S" + JobInspectionData.StationFormatFlag(i, 1);
                }
            }
            

            if (maxCnt == 0)
                return new JobInspectionData(Name, cntr, randFreq, deadline, InspectSingleProcess);
            else
                return new JobInspectionData(Name, cntr, maxCnt, deadline, InspectSingleProcess);
        }

        public struct ParsedInspectionCounter
        {
            public string InspectionName;
            public string PartName;
            public List<string> Pallets; //one entry per process
            public List<string> Stations; //one entry per process
        }
        public static ParsedInspectionCounter ParseInspectionCounter(string cntr)
        {
            var s = cntr.Split(',');
            var ret = default(ParsedInspectionCounter);
            ret.PartName = s.Length >= 1 ? s[0] : "";
            ret.InspectionName = s.Length >= 2 ? s[1] : "";
            ret.Pallets = new List<string>();
            ret.Stations = new List<string>();
            for (int i = 2; i < s.Length; i++)
            {
                if (s[i].StartsWith("P"))
                  ret.Pallets.Add(s[i].Substring(1));
                else if (s[i].StartsWith("S"))
                  ret.Stations.Add(s[i].Substring(1));
            }
            return ret;
        }
    }

    ///<summary>Overrides for inspection triggers for a specific part name</summary>
    [SerializableAttribute] public class InspectionFrequencyOverride
    {
        public string Part { get; set; }
        public int CountBeforeInspection { get; set; }
        public TimeSpan Deadline { get; set; }
        public double RandomFreq { get; set; }
    }
}