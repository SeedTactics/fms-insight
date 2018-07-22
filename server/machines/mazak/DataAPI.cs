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
using System.Linq;
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

  public class MazakPartRow
  {
    public int Id {get;set;}
    public string Comment {get;set;}
    public string PartName {get;set;}
    public int Price {get;set;}
    public int TotalProcess => Processes.Count();

    // these columns are new on smooth
    public string MaterialName {get;set;}
    public int Part_1 {get;set;}
    public int Part_2 {get;set;}
    public int Part_3 {get;set;}
    public int Part_4 {get;set;}
    public int Part_5 {get;set;}

    public IList<MazakPartProcessRow> Processes {get;set;} = new List<MazakPartProcessRow>();

    public MazakWriteCommand Command {get;set;} // only for transaction DB

    public MazakPartRow Clone()
    {
      var p = (MazakPartRow)MemberwiseClone();
      p.Processes =
        Processes
        .Select(proc => proc.Clone())
        .ToList();
      return p;
    }
  }

  public class MazakPartProcessRow
  {
    public int MazakPartRowId {get;set;}

    public string PartName {get;set;}
    public int ProcessNumber {get;set;}

    public int FixQuantity {get;set;}
    public int ContinueCut {get;set;}
    public string CutMc {get;set;}
    public string FixLDS {get;set;}
    public string FixPhoto {get;set;}
    public string Fixture {get;set;}
    public string MainProgram {get;set;}
    public string RemoveLDS {get;set;}
    public string RemovePhoto {get;set;}
    public int WashType {get;set;}

    // these are new on smooth
    public int PartProcess_1 {get;set;}
    public int PartProcess_2 {get;set;}
    public int PartProcess_3 {get;set;}
    public int PartProcess_4 {get;set;}
    public int FixTime {get;set;}
    public int RemoveTime {get;set;}
    public int CreateToolList_RA {get;set;}

    public MazakPartProcessRow Clone() => (MazakPartProcessRow)MemberwiseClone();
  }

  public class MazakScheduleRow
  {
    public int Id {get;set;}
    public string Comment {get;set;}
    public string PartName {get;set;}
    public int PlanQuantity {get;set;}
    public int CompleteQuantity {get;set;}
    public int Priority {get;set;}

    public DateTime? DueDate {get;set;}
    public int FixForMachine {get;set;}
    public int HoldMode {get;set;}
    public int MissingFixture {get;set;}
    public int MissingProgram {get;set;}
    public int MissingTool {get;set;}
    public int MixScheduleID {get;set;}
    public int ProcessingPriority {get;set;}

    //these are only on Smooth
    public int Schedule_1 {get;set;}
    public int Schedule_2 {get;set;}
    public int Schedule_3 {get;set;}
    public int Schedule_4 {get;set;}
    public int Schedule_5 {get;set;}
    public int Schedule_6 {get;set;}
    public DateTime? StartDate {get;set;}
    public int SetNumber {get;set;}
    public int SetQuantity {get;set;}
    public int SetNumberSets {get;set;}

    public IList<MazakScheduleProcessRow> Processes {get;set;} = new List<MazakScheduleProcessRow>();

    public MazakWriteCommand Command {get;set;} // only for transaction DB

    public MazakScheduleRow Clone()
    {
      var s = (MazakScheduleRow)MemberwiseClone();
      s.Processes =
        Processes
        .Select(proc => proc.Clone())
        .ToList();
      return s;
    }
  }

  public class MazakScheduleProcessRow
  {
    public int MazakScheduleRowId {get;set;}
    public int FixQuantity {get;set;}

    public int ProcessNumber {get;set;}
    public int ProcessMaterialQuantity {get;set;}
    public int ProcessExecuteQuantity {get;set;}
    public int ProcessBadQuantity {get;set;}
    public int ProcessMachine {get;set;}

    // these are only on Smooth
    public int FixedMachineFlag {get;set;}
    public int FixedMachineNumber {get;set;}
    public int ScheduleProcess_1 {get;set;}
    public int ScheduleProcess_2 {get;set;}
    public int ScheduleProcess_3 {get;set;}
    public int ScheduleProcess_4 {get;set;}
    public int ScheduleProcess_5 {get;set;}

    public MazakScheduleProcessRow Clone() => (MazakScheduleProcessRow)MemberwiseClone();
  }

  public class MazakPalletRow
  {
    public int PalletNumber {get;set;}
    public string Fixture {get;set;}
    public int RecordID {get;set;}
    public int AngleV1 {get;set;}
    public int FixtureGroupV2 {get;set;}
    public MazakWriteCommand Command {get;set;} // only for transaction DB

    public MazakPalletRow Clone() => (MazakPalletRow)MemberwiseClone();
  }

  public class MazakPalletSubStatusRow
  {
    public int PalletNumber {get;set;}
    public string FixtureName {get;set;}
    public int ScheduleID {get;set;}
    public string PartName {get;set;}
    public int PartProcessNumber {get;set;}
    public int FixQuantity {get;set;}
  }

  public class MazakPalletPositionRow
  {
    public int PalletNumber {get;set;}
    public string PalletPosition {get;set;}
  }

  public class MazakFixtureRow
  {
    public string FixtureName {get;set;}
    public string Comment {get;set;}
    public MazakWriteCommand Command {get;set;} // only for transaction DB

    public MazakFixtureRow Clone() => (MazakFixtureRow)MemberwiseClone();
  }

  public class LoadAction
  {
    public int LoadStation {get;set;}
    public bool LoadEvent {get;set;}
    public string Unique {get;set;}
    public string Part {get;set;}
    public int Process {get;set;}
    public int Path {get;set;}
    public int Qty {get;set;}

    public LoadAction(bool l, int stat, string p, string comment, int proc, int q)
    {
      LoadStation = stat;
      LoadEvent = l;
      Part = p;
      Process = proc;
      Qty = q;
      if (string.IsNullOrEmpty(comment)) {
        Unique = "";
        Path = 1;
      } else {
        MazakPart.ParseComment(comment, out string uniq, out var procToPath, out bool manual);
        Unique = uniq;
        Path = procToPath.PathForProc(proc);
      }
    }

    [Newtonsoft.Json.JsonConstructor] public LoadAction() {}

    public override string ToString()
    {
      return (LoadEvent ? "Load " : "Unload ") +
        Part + "-" + Process.ToString() + " qty: " + Qty.ToString();
    }
  }


  public class MazakSchedules
  {
    public IEnumerable<MazakScheduleRow> Schedules {get;set;}

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
          if (numProc < proc) numProc = proc;
          path = procToPath.PathForProc(proc);
          return;
        }
      }
    }
  }

  public class MazakSchedulesAndLoadActions : MazakSchedules
  {
    public IEnumerable<LoadAction> LoadActions {get;set;}
  }

  public class MazakSchedulesPartsPallets : MazakSchedules
  {
    public IEnumerable<MazakPartRow> Parts {get;set;}
    public IEnumerable<MazakPalletRow> Pallets {get;set;}
    public IEnumerable<MazakPalletSubStatusRow> PalletSubStatuses {get;set;}
    public IEnumerable<MazakPalletPositionRow> PalletPositions {get;set;}
    public IEnumerable<LoadAction> LoadActions {get;set;}
    public ISet<string> MainPrograms {get;set;}
  }

  public class MazakAllData : MazakSchedulesPartsPallets
  {
    public IEnumerable<MazakFixtureRow> Fixtures {get;set;}
  }

  public interface IReadDataAccess
  {
    MazakDbType MazakType {get;}
    MazakSchedules LoadSchedules();
    MazakSchedulesAndLoadActions LoadSchedulesAndLoadActions();
    MazakSchedulesPartsPallets LoadSchedulesPartsPallets();
    MazakAllData LoadAllData();

    TResult WithReadDBConnection<TResult>(Func<IDbConnection, TResult> action);
  }

  public class MazakWriteData
  {
    public IList<MazakScheduleRow> Schedules {get;set;} = new List<MazakScheduleRow>();
    public IList<MazakPartRow> Parts {get;set;} = new List<MazakPartRow>();
    public IList<MazakPalletRow> Pallets {get;set;} = new List<MazakPalletRow>();
    public IList<MazakFixtureRow> Fixtures {get;set;} = new List<MazakFixtureRow>();
  }

  public interface IWriteData
  {
    MazakDbType MazakType {get;}
    void Save(MazakWriteData data, string prefix, System.Collections.Generic.IList<string> log);
  }

}