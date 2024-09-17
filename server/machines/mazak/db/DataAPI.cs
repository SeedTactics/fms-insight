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
using System.Runtime.Serialization;

namespace MazakMachineInterface
{
  public enum MazakDbType
  {
    MazakVersionE,
    MazakWeb,
    MazakSmooth,
  }

  [DataContract]
  public enum MazakWriteCommand
  {
    [EnumMember]
    ForceUnlockEdit = 3,

    [EnumMember]
    Add = 4,

    [EnumMember]
    Delete = 5,

    [EnumMember]
    Edit = 6,

    [EnumMember]
    ScheduleSafeEdit = 7,

    [EnumMember]
    ScheduleMaterialEdit = 8,

    [EnumMember]
    Error = 9,
  }

  [DataContract]
#if NET35
  public class MazakPartRow
#else
  public record MazakPartRow
#endif
  {
    [DataMember]
    public string PartName { get; init; }

    [DataMember]
    public string Comment { get; init; }

    [DataMember]
    public double Price { get; init; }

    [DataMember]
    public int TotalProcess { get; init; }

    // these columns are new on smooth
    private string _matName;

    [DataMember]
    public string MaterialName
    {
      get => _matName ?? "";
      init => _matName = value;
    }

    [DataMember]
    public int Part_1 { get; init; }

    [DataMember]
    public int Part_2 { get; init; }

    private string _part3;

    [DataMember]
    public string Part_3
    {
      get => _part3 ?? "";
      init => _part3 = value;
    }

    [DataMember]
    public int Part_4 { get; init; }

    [DataMember]
    public int Part_5 { get; init; }

    [DataMember]
    public int CheckCount { get; init; }

    [DataMember]
    public int ProductCount { get; init; }

    [DataMember]
    public IList<MazakPartProcessRow> Processes { get; init; } = new List<MazakPartProcessRow>();

    [DataMember]
    public MazakWriteCommand Command { get; init; } // only for transaction DB
  }

  [DataContract]
#if NET35
  public class MazakPartProcessRow
#else
  public record MazakPartProcessRow
#endif
  {
    [DataMember]
    public string PartName { get; init; }

    [DataMember]
    public int ProcessNumber { get; init; }

    [DataMember]
    public int FixQuantity { get; init; }

    [DataMember]
    public int ContinueCut { get; init; }

    [DataMember]
    public string CutMc { get; init; }

    [DataMember]
    public string FixLDS { get; init; }

    [DataMember]
    public string FixPhoto { get; init; }

    [DataMember]
    public string Fixture { get; init; }

    [DataMember]
    public string MainProgram { get; init; }

    [DataMember]
    public string RemoveLDS { get; init; }

    [DataMember]
    public string RemovePhoto { get; init; }

    [DataMember]
    public int WashType { get; init; }

    // these are new on smooth
    [DataMember]
    public int PartProcess_1 { get; init; }

    [DataMember]
    public int PartProcess_2 { get; init; }

    [DataMember]
    public int PartProcess_3 { get; init; }

    [DataMember]
    public int PartProcess_4 { get; init; }

    [DataMember]
    public int FixTime { get; init; }

    [DataMember]
    public int RemoveTime { get; init; }

    [DataMember]
    public int CreateToolList_RA { get; init; }
  }

  [DataContract]
#if NET35
  public class MazakScheduleRow
#else
  public record MazakScheduleRow
#endif
  {
    [DataMember]
    public int Id { get; init; }

    [DataMember]
    public string Comment { get; init; }

    [DataMember]
    public string PartName { get; init; }

    [DataMember]
    public int PlanQuantity { get; init; }

    [DataMember]
    public int CompleteQuantity { get; init; }

    [DataMember]
    public int Priority { get; init; }

    [DataMember]
    public DateTime? DueDate { get; init; }

    [DataMember]
    public int FixForMachine { get; init; }

    [DataMember]
    public int HoldMode { get; init; }

    [DataMember]
    public int MissingFixture { get; init; }

    [DataMember]
    public int MissingProgram { get; init; }

    [DataMember]
    public int MissingTool { get; init; }

    [DataMember]
    public int MixScheduleID { get; init; }

    [DataMember]
    public int ProcessingPriority { get; init; }

    //these are only on Smooth
    [DataMember]
    public int Schedule_1 { get; init; }

    [DataMember]
    public int Schedule_2 { get; init; }

    [DataMember]
    public int Schedule_3 { get; init; }

    [DataMember]
    public int Schedule_4 { get; init; }

    [DataMember]
    public int Schedule_5 { get; init; }

    [DataMember]
    public int Schedule_6 { get; init; }

    [DataMember]
    public DateTime? StartDate { get; init; }

    [DataMember]
    public int SetNumber { get; init; }

    [DataMember]
    public int SetQuantity { get; init; }

    [DataMember]
    public int SetNumberSets { get; init; }

    [DataMember]
    public IList<MazakScheduleProcessRow> Processes { get; init; } = new List<MazakScheduleProcessRow>();

    [DataMember]
    public MazakWriteCommand Command { get; init; } // only for transaction DB
  }

  [DataContract]
#if NET35
  public class MazakScheduleProcessRow
#else
  public record MazakScheduleProcessRow
#endif
  {
    [DataMember]
    public int MazakScheduleRowId { get; init; }

    [DataMember]
    public int FixQuantity { get; init; }

    [DataMember]
    public int ProcessNumber { get; init; }

    [DataMember]
    public int ProcessMaterialQuantity { get; init; }

    [DataMember]
    public int ProcessExecuteQuantity { get; init; }

    [DataMember]
    public int ProcessBadQuantity { get; init; }

    [DataMember]
    public int ProcessMachine { get; init; }

    // these are only on Smooth
    [DataMember]
    public int FixedMachineFlag { get; init; }

    [DataMember]
    public int FixedMachineNumber { get; init; }

    [DataMember]
    public int ScheduleProcess_1 { get; init; }

    [DataMember]
    public int ScheduleProcess_2 { get; init; }

    [DataMember]
    public int ScheduleProcess_3 { get; init; }

    [DataMember]
    public int ScheduleProcess_4 { get; init; }

    [DataMember]
    public int ScheduleProcess_5 { get; init; }
  }

  [DataContract]
#if NET35
  public class MazakPalletRow
#else
  public record MazakPalletRow
#endif
  {
    [DataMember]
    public int PalletNumber { get; init; }

    [DataMember]
    public string Fixture { get; init; }

    [DataMember]
    public int RecordID { get; init; }

    [DataMember]
    public int AngleV1 { get; init; }

    [DataMember]
    public int FixtureGroupV2 { get; init; }

    [DataMember]
    public MazakWriteCommand Command { get; init; } // only for transaction DB

    public int FixtureGroup
    {
      get
      {
        if (AngleV1 > 1000)
        {
          return AngleV1 % 1000;
        }
        else
        {
          return FixtureGroupV2;
        }
      }
    }
  }

  [DataContract]
#if NET35
  public class MazakPalletSubStatusRow
#else
  public record MazakPalletSubStatusRow
#endif
  {
    [DataMember]
    public int PalletNumber { get; init; }

    [DataMember]
    public string FixtureName { get; init; }

    [DataMember]
    public int ScheduleID { get; init; }

    [DataMember]
    public string PartName { get; init; }

    [DataMember]
    public int PartProcessNumber { get; init; }

    [DataMember]
    public int FixQuantity { get; init; }
  }

  [DataContract]
#if NET35
  public class MazakPalletPositionRow
#else
  public record MazakPalletPositionRow
#endif
  {
    [DataMember]
    public int PalletNumber { get; init; }

    [DataMember]
    public string PalletPosition { get; init; }
  }

  [DataContract]
#if NET35
  public class MazakFixtureRow
#else
  public record MazakFixtureRow
#endif
  {
    [DataMember]
    public string FixtureName { get; init; }

    [DataMember]
    public string Comment { get; init; }

    [DataMember]
    public MazakWriteCommand Command { get; init; } // only for transaction DB
  }

  [DataContract]
#if NET35
  public class MazakAlarmRow
#else
  public record MazakAlarmRow
#endif
  {
    [DataMember]
    public int? AlarmNumber { get; init; }

    [DataMember]
    public string AlarmMessage { get; init; }
  }

  [DataContract]
#if NET35
  public class ToolPocketRow
#else
  public record ToolPocketRow
#endif
  {
    [DataMember]
    public int? MachineNumber { get; init; }

    [DataMember]
    public int? PocketNumber { get; init; }

    [DataMember]
    public int? PocketType { get; init; }

    [DataMember]
    public int? ToolID { get; init; }

    [DataMember]
    public int? ToolNameCode { get; init; }

    [DataMember]
    public int? ToolNumber { get; init; }

    [DataMember]
    public bool? IsLengthMeasured { get; init; }

    [DataMember]
    public bool? IsToolDataValid { get; init; }

    [DataMember]
    public bool? IsToolLifeOver { get; init; }

    [DataMember]
    public bool? IsToolLifeBroken { get; init; }

    [DataMember]
    public bool? IsDataAccessed { get; init; }

    [DataMember]
    public bool? IsBeingUsed { get; init; }

    [DataMember]
    public bool? UsableNow { get; init; }

    [DataMember]
    public bool? WillBrokenInform { get; init; }

    [DataMember]
    public int? InterferenceType { get; init; }

    [DataMember]
    public int? LifeSpan { get; init; }

    [DataMember]
    public int? LifeUsed { get; init; }

    [DataMember]
    public string NominalDiameter { get; init; }

    [DataMember]
    public int? SuffixCode { get; init; }

    [DataMember]
    public int? ToolDiameter { get; init; }

    [DataMember]
    public string GroupNo { get; init; }
  }

  [DataContract]
#if NET35
  public class LoadAction
#else
  public record LoadAction
#endif
  {
    [DataMember]
    public int LoadStation { get; init; }

    [DataMember]
    public bool LoadEvent { get; init; }

    [DataMember]
    public string Part { get; init; }

    [DataMember]
    public string Comment { get; init; }

    [DataMember]
    public int Process { get; init; }

    [DataMember]
    public int Qty { get; init; }
  }

  [DataContract]
#if NET35
  public class MazakCurrentStatus
#else
  public record MazakCurrentStatus
#endif
  {
    [DataMember]
    public IEnumerable<MazakScheduleRow> Schedules { get; init; }

    [DataMember]
    public IEnumerable<LoadAction> LoadActions { get; init; }

    [DataMember]
    public IEnumerable<MazakPalletSubStatusRow> PalletSubStatuses { get; init; }

    [DataMember]
    public IEnumerable<MazakPalletPositionRow> PalletPositions { get; init; }

    [DataMember]
    public IEnumerable<MazakAlarmRow> Alarms { get; init; }

    [DataMember]
    public IEnumerable<MazakPartRow> Parts { get; init; }
  }

  [DataContract]
#if NET35
  public class MazakProgramRow
#else
  public record MazakProgramRow
#endif
  {
    [DataMember]
    public string MainProgram { get; init; }

    [DataMember]
    public string Comment { get; init; }
  }

  [DataContract]
#if NET35
  public class MazakAllData : MazakCurrentStatus
#else
  public record MazakAllData : MazakCurrentStatus
#endif
  {
    [DataMember]
    public IEnumerable<MazakPalletRow> Pallets { get; init; }

    [DataMember]
    public IEnumerable<MazakProgramRow> MainPrograms { get; init; }

    [DataMember]
    public IEnumerable<MazakFixtureRow> Fixtures { get; init; }
  }

  [DataContract]
  public enum LogCode
  {
    // Codes are in section 9-8-8 "Log Message Screen" of the Mazak operations Manual
    [EnumMember]
    MachineCycleStart = 441,

    [EnumMember]
    MachineCycleEnd = 442,

    [EnumMember]
    LoadBegin = 501,

    [EnumMember]
    LoadEnd = 502,

    [EnumMember]
    UnloadBegin = 511,

    [EnumMember]
    UnloadEnd = 512,

    [EnumMember]
    PalletMoving = 301,

    [EnumMember]
    PalletMoveComplete = 302,

    // 431 and 432 are used when rotating something into machine, no matter if a
    // different pallet is rotating out or not.
    [EnumMember]
    StartRotatePalletIntoMachine = 431, // event has pallet moving into machine

    //EndRotatePalletIntoMachine = 432, // event has pallet moving out of machine if it exists, otherwise pallet = 0

    // 433 and 434 are only used if nothing is being sent in.
    [EnumMember]
    StartRotatePalletOutOfMachine = 433, // event has pallet moving out of machine
    //EndRotatePalletOutOfMachine = 434, // event also has pallet moving out of machine

    //StartOffsetProgram = 435,
    //EndOffsetProgram = 436
  }

  [DataContract]
#if NET35
  public class LogEntry
#else
  public record LogEntry
#endif
  {
    [DataMember]
    public DateTime TimeUTC { get; init; }

    [DataMember]
    public LogCode Code { get; init; }

    [DataMember]
    public string ForeignID { get; init; }

    //Only sometimes filled in depending on the log code
    [DataMember]
    public int Pallet { get; init; }

    [DataMember]
    public string FullPartName { get; init; } //Full part name in the mazak system

    [DataMember]
    public string JobPartName { get; init; } //Part name with : stripped off

    [DataMember]
    public int Process { get; init; }

    [DataMember]
    public int FixedQuantity { get; init; }

    [DataMember]
    public string Program { get; init; }

    [DataMember]
    public int StationNumber { get; init; }

    //Only filled in for pallet movement
    [DataMember]
    public string TargetPosition { get; init; }

    [DataMember]
    public string FromPosition { get; init; }
  }

  [DataContract]
#if NET35
  public class MazakAllDataAndLogs : MazakAllData
#else
  public record MazakAllDataAndLogs : MazakAllData
#endif
  {
    [DataMember]
    public IList<LogEntry> Logs { get; init; }
  }

  public interface ICurrentLoadActions
  {
    IEnumerable<LoadAction> CurrentLoadActions();
  }

  public interface IMazakDB
  {
    MazakAllData LoadAllData();
    MazakAllDataAndLogs LoadAllDataAndLogs(string maxLogID);
    IEnumerable<MazakProgramRow> LoadPrograms();
    IEnumerable<ToolPocketRow> LoadTools();
    void DeleteLogs(string lastSeenForeignId);

    void Save(MazakWriteData data);

    event Action OnNewEvent;
  }

  [DataContract]
#if NET35
  public class NewMazakProgram
#else
  public record NewMazakProgram
#endif
  {
    [DataMember]
    public string ProgramName { get; init; }

    [DataMember]
    public long ProgramRevision { get; init; }

    [DataMember]
    public string MainProgram { get; init; }

    [DataMember]
    public string Comment { get; init; }

    [DataMember]
    public MazakWriteCommand Command { get; init; }

    [DataMember]
    public string ProgramContent { get; init; }
  }

  [DataContract]
#if NET35
  public class MazakWriteData
#else
  public record MazakWriteData
#endif
  {
    [DataMember]
    public string Prefix { get; init; } = "";

    [DataMember]
    public IList<MazakScheduleRow> Schedules { get; init; } = new List<MazakScheduleRow>();

    [DataMember]
    public IList<MazakPartRow> Parts { get; init; } = new List<MazakPartRow>();

    [DataMember]
    public IList<MazakPalletRow> Pallets { get; init; } = new List<MazakPalletRow>();

    [DataMember]
    public IList<MazakFixtureRow> Fixtures { get; init; } = new List<MazakFixtureRow>();

    [DataMember]
    public IList<NewMazakProgram> Programs { get; init; } = new List<NewMazakProgram>();
  }
}
