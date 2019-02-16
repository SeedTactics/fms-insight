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
using BlackMaple.MachineWatchInterface;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
#pragma warning disable RECS0060 //warning to use culture-aware functions
#pragma warning disable RECS0063 //another warning to use culture-aware fuctions

namespace Cincron
{
  public class CincronMessage
  {
    //offset of the start of the message from the start of the file
    public DateTime TimeOfFirstEntryInLogFileUTC { get; set; }
    public long LogFileOffset { get; set; }
    public string LogMessage { get; set; }

    public DateTime TimeUTC { get; set; }

    public class PalletStartMove : CincronMessage
    {
      //trafic--I10402:Location Data for Work Unit 11 Updated   [MOVE_TO_LOCATION = 'STG3']
      private static Regex regex = new Regex(@"Location Data for Work Unit (?<pal>\d+) Updated   \[MOVE_TO_LOCATION = '(?<loc>\w+)'\]");

      public static PalletStartMove TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new PalletStartMove();
        ret.Pallet = m.Groups["pal"].Value;
        ret.Destination = ParseLocation(m.Groups["loc"].Value);
        return ret;
      }

      public string Pallet { get; set; }
      public PalletLocation Destination { get; set; }

      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Pallet Start Move: Pallet = " + Pallet + " to " + PrintLoc(Destination);
      }
    }

    public class PalletEndMove : CincronMessage
    {
      //trafic--I10402:Location Data for Work Unit 11 Updated   [MOVE_TO_LOCATION = NULL, QUEUE_POSITION = 10000, CURRENT_LMHS = NULL, CURRENT_LOCATION = 'MTC4']
      private static Regex regex = new Regex(@"Location Data for Work Unit (?<pal>\d+) Updated   \[MOVE_TO_LOCATION = NULL, QUEUE_POSITION = (?<queue>\d+), CURRENT_LMHS = NULL, CURRENT_LOCATION = '(?<loc>\w+)'\]");

      public static PalletEndMove TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new PalletEndMove();
        ret.Pallet = m.Groups["pal"].Value;
        ret.Destination = ParseLocation(m.Groups["loc"].Value);
        ret.QueuePos = m.Groups["queue"].Value;
        return ret;
      }

      public string Pallet { get; set; }
      public PalletLocation Destination { get; set; }
      public string QueuePos { get; set; }

      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Pallet End Move: Pallet = " + Pallet + " to " + PrintLoc(Destination) + " queue " + QueuePos;
      }
    }

    public class PalletOnRgv : CincronMessage
    {
      //trafic--I10402:Location Data for Work Unit 11 Updated   [QUEUE_POSITION = 1, CURRENT_LMHS = NULL, CURRENT_LOCATION = 'RGV1']
      private static Regex regex = new Regex(@"Location Data for Work Unit (?<pal>\d+) Updated   \[QUEUE_POSITION = 1, CURRENT_LMHS = NULL, CURRENT_LOCATION = 'RGV1'\]");

      public static PalletOnRgv TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new PalletOnRgv();
        ret.Pallet = m.Groups["pal"].Value;
        return ret;
      }

      public string Pallet { get; set; }

      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Pallet Loaded Onto RGV: Pallet = " + Pallet;
      }
    }

    public class QueuePositionChange : CincronMessage
    {
      //stn002--I10402:Location Data for Work Unit 11 Updated   [QUEUE_POSITION = 10010, CURRENT_LMHS = NULL, CURRENT_LOCATION = 'STG3']
      private static Regex regex = new Regex(@"Location Data for Work Unit (?<pal>\d+) Updated   \[QUEUE_POSITION = (?<queue>\d+), CURRENT_LMHS = NULL, CURRENT_LOCATION = '(?<loc>\w+)'\]");

      public static QueuePositionChange TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new QueuePositionChange();
        ret.Pallet = m.Groups["pal"].Value;
        ret.CurrentLocation = ParseLocation(m.Groups["loc"].Value);
        ret.NewQueuePosition = m.Groups["queue"].Value;
        return ret;
      }

      public string Pallet { get; set; }
      public PalletLocation CurrentLocation { get; set; }
      public string NewQueuePosition { get; set; }

      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Pallet Queue Position Change: Pallet = " + Pallet + " Location = " + PrintLoc(CurrentLocation) + " new queue pos = " + NewQueuePosition;
      }
    }

    public class PalletDesiredMove : CincronMessage
    {
      //stn002--I10402:Location Data for Work Unit 11 Updated   [NEXT_DESIRED_LOC = 'ZONE-TWO-2456', REASON_FOR_MOVE = 2]
      private static Regex regex = new Regex(@"Location Data for Work Unit (?<pal>\d+) Updated   \[NEXT_DESIRED_LOC = '(?<loc>[^']+)', REASON_FOR_MOVE = (?<reason>\d+)\]");

      public static PalletDesiredMove TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new PalletDesiredMove();
        ret.Pallet = m.Groups["pal"].Value;
        ret.Location = ParseLocation(m.Groups["loc"].Value);
        ret.ReasonForMove = int.Parse(m.Groups["reason"].Value);
        return ret;
      }

      public string Pallet { get; set; }
      public PalletLocation Location { get; set; }
      public int ReasonForMove { get; set; }

      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Pallet Wants to Move: Pallet = " + Pallet + " Target = " + PrintLoc(Location) + " reason for move = " + ReasonForMove.ToString();
      }
    }

    public class PalletDesiredQueueChange : CincronMessage
    {
      //stn001--I12672:[LMHS Component 'LOCAL-MHS1' Station 'MTC4']: Move Queued: Pallet [11] to Position [10010]
      private static Regex regex = new Regex(@"Station '(?<loc>\w+)'\]: Move Queued: Pallet \[(?<pal>\d+)\] to Position \[(?<queue>\d+)\]");

      public static PalletDesiredQueueChange TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new PalletDesiredQueueChange();
        ret.Pallet = m.Groups["pal"].Value;
        ret.Location = ParseLocation(m.Groups["loc"].Value);
        ret.NewQueuePosition = m.Groups["queue"].Value;
        return ret;
      }

      public string Pallet { get; set; }
      public PalletLocation Location { get; set; }
      public string NewQueuePosition { get; set; }

      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Pallet Wants to change queue position: Pallet = " + Pallet + " Current Loc = " + PrintLoc(Location) + " target queue = " + NewQueuePosition;
      }
    }

    public class PartCompleted : CincronMessage
    {
      //there can be multiple of these for a single pallet
      //stn002--I10503:WC PART COMPLETED     WL ID = 3481255903     PART NAME = 3481255903     SETUP = 1
      private static Regex regex = new Regex(@"WC PART COMPLETED +WL ID = (?<id>[^ ]+) +PART NAME = (?<part>[^ ]+) +SETUP = (?<setup>\d+)");

      public static PartCompleted TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new PartCompleted();
        ret.WorkId = m.Groups["id"].Value;
        ret.PartName = m.Groups["part"].Value;
        ret.Setup = int.Parse(m.Groups["setup"].Value);
        return ret;
      }

      public string WorkId { get; set; }
      public string PartName { get; set; }
      public int Setup { get; set; } // what is this???

      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Part Completed: WorkId = " + WorkId + " Part = " + PartName + " setup = " + Setup.ToString();
      }
    }

    public class PartUnloadStart : CincronMessage
    {
      //stn002--I10402:Control Data for Work Unit 11 Updated   [WL_ID = NULL, CONTEXT_ID = NULL, ROUTE_ID = NULL, STEP_NO = 0]
      private static Regex regex = new Regex(@"Control Data for Work Unit (?<pal>\d+) Updated   \[WL_ID = NULL, CONTEXT_ID = NULL, ROUTE_ID = NULL, STEP_NO = 0\]");

      public static PartUnloadStart TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new PartUnloadStart();
        ret.Pallet = m.Groups["pal"].Value;
        return ret;
      }
      public string Pallet { get; set; }

      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Part Unload start: Pallet = " + Pallet;
      }
    }

    public class PartLoadStart : CincronMessage
    {
      //stn002--I10402:Control Data for Work Unit 11 Updated   [WL_ID = '3481255903', CONTEXT_ID = 'SSJEc1_o4_lW1', ROUTE_ID = '3481255903', STEP_NO = 1]
      private static Regex regex =
          new Regex(@"Control Data for Work Unit (?<pal>\d+) Updated   " +
                    @"\[WL_ID = '(?<id>[^']+)', " +
                    @"CONTEXT_ID = '(?<context>[^']+)', " +
                    @"ROUTE_ID = '(?<route>[^']+)', " +
                    @"STEP_NO = (?<step>\d+)\]");

      public static PartLoadStart TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new PartLoadStart();
        ret.Pallet = m.Groups["pal"].Value;
        ret.WorkId = m.Groups["id"].Value;
        ret.Context = m.Groups["context"].Value;
        ret.RouteId = m.Groups["route"].Value;
        ret.StepNo = int.Parse(m.Groups["step"].Value);
        return ret;
      }

      public string Pallet { get; set; }
      public string WorkId { get; set; }
      public string Context { get; set; }
      public string RouteId { get; set; }
      public int StepNo { get; set; }

      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Part Load start: Pallet = " + Pallet + " WorkId = " + WorkId + " Context = " + Context + " RouteId = " + RouteId + " Step " + StepNo.ToString();
      }
    }

    public class PartNewStep : CincronMessage
    {
      //stn002--I10402:Control Data for Work Unit 11 Updated   [STEP_NO = 1]
      private static Regex regex = new Regex(@"Control Data for Work Unit (?<pal>\d+) Updated   \[STEP_NO = (?<step>\d+)\]");

      public static PartNewStep TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new PartNewStep();
        ret.Pallet = m.Groups["pal"].Value;
        ret.StepNo = int.Parse(m.Groups["step"].Value);
        return ret;
      }

      public string Pallet { get; set; }

      //StepNo = 1 is loaded
      //StepNo = 5 is machine complete
      public int StepNo { get; set; }

      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Part step change: Pallet = " + Pallet + " Step = " + StepNo.ToString();
      }
    }

    public class ProgramFinished : CincronMessage
    {
      //this doesn't seem to appear in all situations... StepNo=5 seems more reliable as end of cycle.
      //psleng--W22113:EXECSYS for Route for Pallet #11; Line 273;  returns Status = 126   (Context [SSJEc1_o4_lW1])
      private static Regex regex = new Regex(@"EXECSYS for Route for Pallet #(?<pal>\d+); Line \d+; +returns Status = (?<status>\d+) +\(Context \[(?<context>\w+)\]\)");

      public static ProgramFinished TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new ProgramFinished();
        ret.Pallet = m.Groups["pal"].Value;
        ret.Context = m.Groups["context"].Value;
        ret.Status = int.Parse(m.Groups["status"].Value);
        return ret;
      }

      public string Pallet { get; set; }
      public string Context { get; set; }
      public int Status { get; set; }


      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Program Finished: Pallet = " + Pallet + " Context = " + Context + " Status = " + Status.ToString();
      }
    }

    public class PreviousMessageRepeated : CincronMessage
    {
      private static Regex regex = new Regex(@"last message repeated (?<cnt>\d+) times");

      public static PreviousMessageRepeated TryParse(string line)
      {
        var m = regex.Match(line);
        if (!m.Success) return null;
        var ret = new PreviousMessageRepeated();
        ret.NumRepeated = int.Parse(m.Groups["cnt"].Value);
        return ret;
      }

      public int NumRepeated { get; set; }

      public override string ToString()
      {
        return "Previous Message Repeated " + this.NumRepeated.ToString() + " times";
      }
    }

    public class UnknownLogMessage : CincronMessage
    {
      public override string ToString()
      {
        return TimeUTC.ToString("o") + "   Unknown Message " + LogMessage;
      }
    }

    protected static PalletLocation ParseLocation(string loc)
    {
      if (loc.StartsWith("STG"))
      {
        int n;
        if (int.TryParse(loc.Substring(3), out n))
        {
          return new PalletLocation(PalletLocationEnum.LoadUnload, "L/U", n);
        }
        else
        {
          return new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 0);
        }
      }
      else if (loc.StartsWith("MTC"))
      {
        int n = int.Parse(loc.Substring(3));
        return new PalletLocation(PalletLocationEnum.Machine, "MC", n);
      }
      else if (loc.StartsWith("STORAGE"))
      {
        return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 1);
      }
      else
      {
        return new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 0);
      }
    }

    protected string PrintLoc(PalletLocation loc)
    {
      return loc.Location.ToString() + " #" + loc.Num.ToString();
    }
  }

  public class MessageParser
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<MessageParser>();

    public static IList<CincronMessage>
        ExtractMessages(string file, int prevMessageOffset, string prevMessage)
    {
      if (!File.Exists(file))
        return new CincronMessage[] { };

      var prevMessageBuff = System.Text.Encoding.UTF8.GetBytes(prevMessage);

      using (var f = new FileStream(file, FileMode.Open, FileAccess.Read))
      {

        //find the time of the first message in the file, which is used to
        //identify when the file changes and used in the foreign id.
        DateTime firstTimeInFile;
        {
          var s = new StreamReader(f);
          var firstLine = s.ReadLine();
          firstTimeInFile = ParseTimeUTC(s.ReadLine());
        }

        //check if the previous message matches and if so seek to it
        bool prevMessageMatches = false;

        if (prevMessageOffset > 0 && prevMessageOffset < f.Length)
        {
          byte[] fileBuff = new byte[prevMessageBuff.Length];
          f.Seek(prevMessageOffset, SeekOrigin.Begin);
          f.Read(fileBuff, 0, prevMessageBuff.Length);
          if (prevMessageBuff.SequenceEqual(fileBuff))
          {
            f.Seek(prevMessageOffset, SeekOrigin.Begin);
            prevMessageMatches = true;
            Log.Debug("Previous message matches");
          }
          else
          {
            //file rotated, start from the beginning
            Log.Debug("Previous message did not match, starting read from beginning");
            f.Seek(0, SeekOrigin.Begin);
            prevMessageMatches = false;
          }
        }
        else
        {
          f.Seek(0, SeekOrigin.Begin);
        }

        var ret = new List<CincronMessage>();
        using (var s = new StreamReader(f))
        {
          if (prevMessageMatches)
          {
            var skipped = s.ReadLine(); // discard previous message
          }
          long pos = GetStreamPosition(s);
          while (true)
          {
            var line = s.ReadLine();
            if (line == null) break;
            if (line.IndexOf("CINCRON") > 0 || line.IndexOf("last message repeated") > 0)
            {
              var msg = ParseMessage(line);
              msg.TimeOfFirstEntryInLogFileUTC = firstTimeInFile;
              msg.LogFileOffset = pos;
              msg.LogMessage = line;
              msg.TimeUTC = ParseTimeUTC(line);
              ret.Add(msg);
            }
            pos = GetStreamPosition(s);
          }
        }
        return ret;
      }
    }

    private static long GetStreamPosition(StreamReader r)
    {
      try
      {
        //use reflection to access internal charPos variable, since the stream reader has a buffer
        var charPos = (int)typeof(StreamReader).InvokeMember("charPos",
            System.Reflection.BindingFlags.DeclaredOnly
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.GetField,
            null, r, null);
        var charLen = (int)typeof(StreamReader).InvokeMember("charLen",
            System.Reflection.BindingFlags.DeclaredOnly
        | System.Reflection.BindingFlags.Public
        | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance
        | System.Reflection.BindingFlags.GetField,
            null, r, null);
        return r.BaseStream.Position - charLen + charPos;
      }
      catch (System.MissingFieldException)
      {
        // .NET core uses _charPos and _charLen
        //use reflection to access internal charPos variable, since the stream reader has a buffer
        var charPos = (int)typeof(StreamReader).InvokeMember("_charPos",
            System.Reflection.BindingFlags.DeclaredOnly
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.GetField,
            null, r, null);
        var charLen = (int)typeof(StreamReader).InvokeMember("_charLen",
            System.Reflection.BindingFlags.DeclaredOnly
        | System.Reflection.BindingFlags.Public
        | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance
        | System.Reflection.BindingFlags.GetField,
            null, r, null);
        return r.BaseStream.Position - charLen + charPos;
      }
    }

    private static DateTime ParseTimeUTC(string line)
    {
      var d = DateTime.ParseExact(line.Substring(0, 15), "MMM d HH:mm:ss", null, System.Globalization.DateTimeStyles.AllowInnerWhite);
      //the timestamp does not have a year and so the current year is used when processing.
      //to correctly handle dates during the year change, if the month of the date is december
      //and it is currently january, subtract one from the year.
      if (d.Month == 12 && DateTime.Now.Month == 1)
      {
        d = d.AddYears(-1);
      }
      return d.ToUniversalTime();
    }

    private static CincronMessage ParseMessage(string line)
    {
      CincronMessage ret;
      ret = CincronMessage.PalletStartMove.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.PalletEndMove.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.PalletOnRgv.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.QueuePositionChange.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.PalletDesiredMove.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.PalletDesiredQueueChange.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.PartCompleted.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.PartUnloadStart.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.PartLoadStart.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.PartNewStep.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.ProgramFinished.TryParse(line); if (ret != null) return ret;
      ret = CincronMessage.PreviousMessageRepeated.TryParse(line); if (ret != null) return ret;
      return new CincronMessage.UnknownLogMessage();
    }
  }
}
