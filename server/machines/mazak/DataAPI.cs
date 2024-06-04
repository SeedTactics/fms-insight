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
using System.Data;

namespace MazakMachineInterface
{
  public enum MazakDbType
  {
    MazakVersionE,
    MazakWeb,
    MazakSmooth
  }

  public enum MazakWriteCommand
  {
    ForceUnlockEdit = 3,
    Add = 4,
    Delete = 5,
    Edit = 6,
    ScheduleSafeEdit = 7,
    ScheduleMaterialEdit = 8,
    Error = 9,
  }

  public record MazakPartRow
  {
    public string PartName { get; init; }
    public string Comment { get; init; }
    public double Price { get; init; }
    public int TotalProcess { get; init; }

    // these columns are new on smooth
    private string _matName;
    public string MaterialName
    {
      get => _matName ?? "";
      init => _matName = value;
    }
    public int Part_1 { get; init; }
    public int Part_2 { get; init; }

    private string _part3;
    public string Part_3
    {
      get => _part3 ?? "";
      init => _part3 = value;
    }
    public int Part_4 { get; init; }
    public int Part_5 { get; init; }
    public int CheckCount { get; init; }
    public int ProductCount { get; init; }

    public IList<MazakPartProcessRow> Processes { get; init; } = new List<MazakPartProcessRow>();

    public MazakWriteCommand Command { get; init; } // only for transaction DB
  }

  public record MazakPartProcessRow
  {
    public string PartName { get; init; }
    public int ProcessNumber { get; init; }

    public int FixQuantity { get; init; }
    public int ContinueCut { get; init; }
    public string CutMc { get; init; }
    public string FixLDS { get; init; }
    public string FixPhoto { get; init; }
    public string Fixture { get; init; }
    public string MainProgram { get; init; }
    public string RemoveLDS { get; init; }
    public string RemovePhoto { get; init; }
    public int WashType { get; init; }

    // these are new on smooth
    public int PartProcess_1 { get; init; }
    public int PartProcess_2 { get; init; }
    public int PartProcess_3 { get; init; }
    public int PartProcess_4 { get; init; }
    public int FixTime { get; init; }
    public int RemoveTime { get; init; }
    public int CreateToolList_RA { get; init; }
  }

  public record MazakScheduleRow
  {
    public int Id { get; init; }
    public string Comment { get; init; }
    public string PartName { get; init; }
    public int PlanQuantity { get; init; }
    public int CompleteQuantity { get; init; }
    public int Priority { get; init; }

    public DateTime? DueDate { get; init; }
    public int FixForMachine { get; init; }
    public int HoldMode { get; init; }
    public int MissingFixture { get; init; }
    public int MissingProgram { get; init; }
    public int MissingTool { get; init; }
    public int MixScheduleID { get; init; }
    public int ProcessingPriority { get; init; }

    //these are only on Smooth
    public int Schedule_1 { get; init; }
    public int Schedule_2 { get; init; }
    public int Schedule_3 { get; init; }
    public int Schedule_4 { get; init; }
    public int Schedule_5 { get; init; }
    public int Schedule_6 { get; init; }
    public DateTime? StartDate { get; init; }
    public int SetNumber { get; init; }
    public int SetQuantity { get; init; }
    public int SetNumberSets { get; init; }

    public IList<MazakScheduleProcessRow> Processes { get; init; } = new List<MazakScheduleProcessRow>();

    public MazakWriteCommand Command { get; init; } // only for transaction DB
  }

  public record MazakScheduleProcessRow
  {
    public int MazakScheduleRowId { get; init; }
    public int FixQuantity { get; init; }

    public int ProcessNumber { get; init; }
    public int ProcessMaterialQuantity { get; init; }
    public int ProcessExecuteQuantity { get; init; }
    public int ProcessBadQuantity { get; init; }
    public int ProcessMachine { get; init; }

    // these are only on Smooth
    public int FixedMachineFlag { get; init; }
    public int FixedMachineNumber { get; init; }
    public int ScheduleProcess_1 { get; init; }
    public int ScheduleProcess_2 { get; init; }
    public int ScheduleProcess_3 { get; init; }
    public int ScheduleProcess_4 { get; init; }
    public int ScheduleProcess_5 { get; init; }
  }

  public record MazakPalletRow
  {
    public int PalletNumber { get; init; }
    public string Fixture { get; init; }
    public int RecordID { get; init; }
    public int AngleV1 { get; init; }
    public int FixtureGroupV2 { get; init; }
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

  public record MazakPalletSubStatusRow
  {
    public int PalletNumber { get; init; }
    public string FixtureName { get; init; }
    public int ScheduleID { get; init; }
    public string PartName { get; init; }
    public int PartProcessNumber { get; init; }
    public int FixQuantity { get; init; }
  }

  public record MazakPalletPositionRow
  {
    public int PalletNumber { get; init; }
    public string PalletPosition { get; init; }
  }

  public record MazakFixtureRow
  {
    public string FixtureName { get; init; }
    public string Comment { get; init; }
    public MazakWriteCommand Command { get; init; } // only for transaction DB
  }

  public record MazakAlarmRow
  {
    public int? AlarmNumber { get; init; }
    public string AlarmMessage { get; init; }
  }

  public record ToolPocketRow
  {
    public int? MachineNumber { get; init; }
    public int? PocketNumber { get; init; }
    public int? PocketType { get; init; }
    public int? ToolID { get; init; }
    public int? ToolNameCode { get; init; }
    public int? ToolNumber { get; init; }
    public bool? IsLengthMeasured { get; init; }
    public bool? IsToolDataValid { get; init; }
    public bool? IsToolLifeOver { get; init; }
    public bool? IsToolLifeBroken { get; init; }
    public bool? IsDataAccessed { get; init; }
    public bool? IsBeingUsed { get; init; }
    public bool? UsableNow { get; init; }
    public bool? WillBrokenInform { get; init; }
    public int? InterferenceType { get; init; }
    public int? LifeSpan { get; init; }
    public int? LifeUsed { get; init; }
    public string NominalDiameter { get; init; }
    public int? SuffixCode { get; init; }
    public int? ToolDiameter { get; init; }
    public string GroupNo { get; init; }
  }

  public record LoadAction
  {
    public int LoadStation { get; init; }
    public bool LoadEvent { get; init; }
    public string Unique { get; init; }
    public string Part { get; init; }
    public int Process { get; init; }
    public int Path { get; init; }
    public int Qty { get; init; }

    public LoadAction(bool l, int stat, string p, string comment, int proc, int q)
    {
      LoadStation = stat;
      LoadEvent = l;
      Part = p;
      Process = proc;
      Qty = q;
      if (string.IsNullOrEmpty(comment))
      {
        Unique = "";
        Path = 1;
      }
      else
      {
        MazakPart.ParseComment(comment, out string uniq, out var procToPath, out bool manual);
        Unique = uniq;
        Path = procToPath.PathForProc(proc);
      }
    }

    [System.Text.Json.Serialization.JsonConstructor]
    public LoadAction() { }
  }

  public record MazakCurrentStatus
  {
    public IEnumerable<MazakScheduleRow> Schedules { get; init; }
    public IEnumerable<LoadAction> LoadActions { get; init; }
    public IEnumerable<MazakPalletSubStatusRow> PalletSubStatuses { get; init; }
    public IEnumerable<MazakPalletPositionRow> PalletPositions { get; init; }
    public IEnumerable<MazakAlarmRow> Alarms { get; init; }
    public IEnumerable<MazakPartRow> Parts { get; init; }

    public void FindSchedule(string mazakPartName, int proc, out string unique, out int path, out int numProc)
    {
      unique = "";
      numProc = proc;
      path = 1;
      foreach (var schRow in Schedules)
      {
        if (schRow.PartName == mazakPartName && !string.IsNullOrEmpty(schRow.Comment))
        {
          bool manual;
          MazakPart.ParseComment(schRow.Comment, out unique, out var procToPath, out manual);
          numProc = schRow.Processes.Count;
          if (numProc < proc)
            numProc = proc;
          path = procToPath.PathForProc(proc);
          return;
        }
      }
    }
  }

  public record MazakProgramRow
  {
    public string MainProgram { get; init; }
    public string Comment { get; init; }
  }

  public record MazakAllData : MazakCurrentStatus
  {
    public IEnumerable<MazakPalletRow> Pallets { get; init; }
    public IEnumerable<MazakProgramRow> MainPrograms { get; init; }
    public IEnumerable<MazakFixtureRow> Fixtures { get; init; }
  }

  public enum LogCode
  {
    // Codes are in section 9-8-8 "Log Message Screen" of the Mazak operations Manual
    MachineCycleStart = 441,
    MachineCycleEnd = 442,

    LoadBegin = 501,
    LoadEnd = 502,

    UnloadBegin = 511,
    UnloadEnd = 512,

    PalletMoving = 301,
    PalletMoveComplete = 302,

    // 431 and 432 are used when rotating something into machine, no matter if a
    // different pallet is rotating out or not.
    StartRotatePalletIntoMachine = 431, // event has pallet moving into machine

    //EndRotatePalletIntoMachine = 432, // event has pallet moving out of machine if it exists, otherwise pallet = 0

    // 433 and 434 are only used if nothing is being sent in.
    StartRotatePalletOutOfMachine = 433, // event has pallet moving out of machine
    //EndRotatePalletOutOfMachine = 434, // event also has pallet moving out of machine

    //StartOffsetProgram = 435,
    //EndOffsetProgram = 436
  }

  public record LogEntry
  {
    public DateTime TimeUTC { get; init; }
    public LogCode Code { get; init; }
    public string ForeignID { get; init; }

    //Only sometimes filled in depending on the log code
    public int Pallet { get; init; }
    public string FullPartName { get; init; } //Full part name in the mazak system
    public string JobPartName { get; init; } //Part name with : stripped off
    public int Process { get; init; }
    public int FixedQuantity { get; init; }
    public string Program { get; init; }
    public int StationNumber { get; init; }

    //Only filled in for pallet movement
    public string TargetPosition { get; init; }
    public string FromPosition { get; init; }
  }

  public interface ICurrentLoadActions : IDisposable
  {
    IEnumerable<LoadAction> CurrentLoadActions();
  }

  public interface IReadDataAccess
  {
    MazakDbType MazakType { get; }
    MazakCurrentStatus LoadStatus();
    MazakAllData LoadAllData();
    IEnumerable<MazakProgramRow> LoadPrograms();
    IEnumerable<ToolPocketRow> LoadTools();

    bool CheckPartExists(string part);
    bool CheckProgramExists(string mainProgram);

    TResult WithReadDBConnection<TResult>(Func<IDbConnection, TResult> action);
  }

  public record NewMazakProgram
  {
    public string ProgramName { get; init; }
    public long ProgramRevision { get; init; }
    public string MainProgram { get; init; }
    public string Comment { get; init; }
    public MazakWriteCommand Command { get; init; }
    public string ProgramContent { get; init; }
  }

  public record MazakWriteData
  {
    public IReadOnlyList<MazakScheduleRow> Schedules { get; init; } = new List<MazakScheduleRow>();
    public IReadOnlyList<MazakPartRow> Parts { get; init; } = new List<MazakPartRow>();
    public IReadOnlyList<MazakPalletRow> Pallets { get; init; } = new List<MazakPalletRow>();
    public IReadOnlyList<MazakFixtureRow> Fixtures { get; init; } = new List<MazakFixtureRow>();
    public IReadOnlyList<NewMazakProgram> Programs { get; init; } = new List<NewMazakProgram>();
  }

  public interface IWriteData
  {
    MazakDbType MazakType { get; }
    void Save(MazakWriteData data, string prefix);
  }
}
