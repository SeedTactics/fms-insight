/* Copyright (c) 2019, John Lenz

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

namespace BlackMaple.FMSInsight.Niigata
{
  public class NiigataStationNum
  {
    public int StatNum { get; }
    public NiigataStationNum(int n) => StatNum = n;
    public static NiigataStationNum LoadStation(int i) => new NiigataStationNum(900 + i);
    public static NiigataStationNum Machine(int i) => new NiigataStationNum(800 + i);
    public static NiigataStationNum MachineQueue(int i) => new NiigataStationNum(830 + i);
    public static NiigataStationNum Buffer(int i) => new NiigataStationNum(i);
    public static NiigataStationNum Cart() => new NiigataStationNum(990);
    public PalletLocation Location
    {
      get
      {
        if (StatNum >= 1 && StatNum <= 200)
        {
          return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", StatNum);
        }
        else if (StatNum >= 801 && StatNum <= 821)
        {
          return new PalletLocation(PalletLocationEnum.Machine, "MC", StatNum - 800);
        }
        else if (StatNum >= 831 && StatNum <= 851)
        {
          return new PalletLocation(PalletLocationEnum.MachineQueue, "MC", StatNum - 830);
        }
        else if (StatNum >= 861 && StatNum <= 891)
        {
          return new PalletLocation(PalletLocationEnum.MachineQueue, "MC", StatNum - 860);
        }
        else if (StatNum >= 901 && StatNum <= 910)
        {
          return new PalletLocation(PalletLocationEnum.LoadUnload, "L/U", StatNum - 900);
        }
        else if (StatNum == 990)
        {
          return new PalletLocation(PalletLocationEnum.Cart, "Cart", 1);
        }
        else
        {
          Serilog.Log.Error("Unknown station number {statNum}", StatNum);
          return new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 1);
        }
      }
    }
  }

  public abstract class RouteStep { }

  public class LoadStep : RouteStep
  {
    public List<int> LoadStations { get; set; } = new List<int>();
  }

  public class ReclampStep : RouteStep
  {
    public List<int> Reclamp { get; set; } = new List<int>();
  }

  public class UnloadStep : RouteStep
  {
    public List<int> UnloadStations { get; set; } = new List<int>();
    public int CompletedPartCount { get; set; } = 1;
  }

  public class MachiningStep : RouteStep
  {
    public List<int> Machines { get; set; } = new List<int>();
    ///<summary>Up to 8 programs can be set and will be run in the given order</summary>
    public List<int> ProgramNumsToRun { get; set; } = new List<int>();
  }

  public class WashStep : RouteStep
  {
    public List<int> WashStations { get; set; } = new List<int>();
    public int WashingPattern { get; set; } = 0;
  }

  /// <summary>Main Niigata information about each pallet</summary>
  public class PalletMaster
  {
    public int PalletNum { get; set; }

    public string Comment { get; set; } = null;

    /// <summary>0-999 where 999 indicates run forever</summary>
    public int RemainingPalletCycles { get; set; } = 0; // 999 indicates run forever

    /// <summary>9 is lowest, 1 is highest, 0 is no priority</summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// When true, no workpiece is loaded.  This gets set when the user presses the "Unload" button
    /// at the load station
    /// </summary>
    public bool NoWork { get; set; } = false;

    /// <summary>If true, pallet is on hold</summary>
    public bool Skip { get; set; } = false;

    /// <summary>Pallet designated to hold tools too long to fit in the tool changer</summary>
    public bool ForLongToolMaintenance { get; set; } = false;

    /// <summary>Programs attached to the route are downloaded to the machines</summary>
    public bool PerformProgramDownload { get; set; } = false;

    public List<RouteStep> Routes { get; set; } = new List<RouteStep>();
  }


  ///<summary>Describes what has been done on the pallet in its current cycle.</summary>
  public class TrackingInfo
  {
    public bool RouteInvalid { get; set; }

    ///<summary>1-indexed, so 1 is the first route in the list</summary>
    public int CurrentStepNum { get; set; } = 1;

    ///<summary>Pallet can be before or after each route step.
    ///<para>
    ///If tracking is MC before, then not allowed to be located at load station
    ///If tracking is LD before, then not allowed to be located at machine
    ///</para>
    ///</summary>
    public bool BeforeCurrentStep => CurrentControlNum % 2 == 1;

    ///<summary>CurStepNum * 2 - (Before ? 1 : 0)</summary>
    public int CurrentControlNum { get; set; } = 1;

    public bool DummyPallet { get; set; } = false;

    public bool Alarm { get; set; } = false;
    public int AlarmCode { get; set; }

    ///<summary>Actual station visisted at each route step in the current cycle.</summary>
    public List<int> ExecutedStationNumber { get; set; }
  }

  ///<summary>Everything about the current status of a pallet</summary>
  public class PalletStatus
  {
    public PalletMaster Master { get; set; }
    public TrackingInfo Tracking { get; set; }
    public NiigataStationNum CurStation { get; set; }
    public RouteStep CurrentStep =>
      Tracking.CurrentStepNum >= 1 && Tracking.CurrentStepNum <= Master.Routes.Count
         ? Master.Routes[Tracking.CurrentStepNum - 1]
         : null;
  }

  ///<summary>The ICC maintains the collection of programs</summary>
  public class ProgramEntry
  {
    public int ProgramNum { get; set; }
    public string Comment { get; set; }
    public TimeSpan CycleTime { get; set; }
    public List<int> Tools { get; set; }
  }

  ///<summary>The current status of each machine</summary>
  public class MachineStatus
  {
    public int MachineNumber { get; set; }
    public bool Power { get; set; }
    public bool FMSLinkMode { get; set; }
    public bool Machining { get; set; }
    public int CurrentlyExecutingProgram { get; set; }
    public bool Alarm { get; set; }
  }

  ///<summary>The current status of each load station</summary>
  public class LoadStatus
  {
    public int LoadNumber { get; set; }
    public bool PalletExists { get; set; }
  }

  public class NiigataStatus
  {
    public List<PalletStatus> Pallets { get; set; }
    public Dictionary<int, ProgramEntry> Programs { get; set; }
    public Dictionary<int, MachineStatus> Machines { get; set; }
    public Dictionary<int, LoadStatus> LoadStations { get; set; }
    public enum ModeE
    {
      Ready = 0,
      Manual = 1,
      Auto = 2,
      Cycle = 3
    }
    public ModeE Mode { get; set; }
    public bool Alarm { get; set; }
    public List<int> PalletsWithUnavailableTools { get; set; } = new List<int>();
    public DateTime TimeOfStatusUTC { get; set; }
  }

  public abstract class NiigataAction { }

  public class NewPalletRoute : NiigataAction
  {
    public long PendingID { get; set; }
    public PalletMaster NewMaster { get; set; }
  }

  public class UpdatePalletQuantities : NiigataAction
  {
    public int Pallet { get; set; }
    public int Priority { get; set; }
    public int Cycles { get; set; }
    public bool NoWork { get; set; }
    public bool Skip { get; set; }
    public bool ForLongTool { get; set; }
  }

  public class NewProgram : NiigataAction
  {
    public ProgramEntry Program { get; set; }
    public string ProgramFile { get; set; }
    public bool Overwrite { get; set; }
  }


  public interface INiigataCommunication
  {
    NiigataStatus LoadStatus();
    void PerformAction(NiigataAction a);
  }
}
