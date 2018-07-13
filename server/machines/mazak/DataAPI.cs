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
using System.Data;

namespace MazakMachineInterface
{
  public enum MazakDbType
  {
    MazakVersionE,
    MazakWeb,
    MazakSmooth
  }

  public interface IWriteData
  {
    MazakDbType MazakType {get;}
    void ClearTransactionDatabase();
    void SaveTransaction(TransactionDataSet dset, System.Collections.Generic.IList<string> log, string prefix, int checkInterval = -1);
  }

  public class MazakPartRow
  {
    public int Id {get;set;}
    public string Comment {get;set;}
    public string PartName {get;set;}
    public int? Price {get;set;}

    public IList<MazakPartProcessRow> Processes {get;} = new List<MazakPartProcessRow>();

    public MazakPartRow() {}
    public MazakPartRow(ReadOnlyDataSet.PartRow p)
    {
      Id = p.id;
      Comment = p.IsCommentNull() ? null : p.Comment;
      PartName = p.PartName;
      Price = p.IsPriceNull() ? null : (int?) p.Price;
    }
  }

  public class MazakPartProcessRow
  {
    public int MazakPartRowId {get;set;}
    public MazakPartRow MazakPartRow {get;set;}

    public string PartName {get;set;}
    public int ProcessNumber {get;set;}

    public int FixQuantity {get;set;}
    public int? ContinueCut {get;set;}
    public string CutMc {get;set;}
    public string FixLDS {get;set;}
    public string FixPhoto {get;set;}
    public string Fixture {get;set;}
    public string MainProgram {get;set;}
    public string RemoveLDS {get;set;}
    public string RemovePhoto {get;set;}
    public int? WashType {get;set;}

    public MazakPartProcessRow(MazakPartRow p)
    {
      MazakPartRowId = p.Id;
      MazakPartRow = p;
    }
    public MazakPartProcessRow(MazakPartRow p, ReadOnlyDataSet.PartProcessRow r)
    {
      MazakPartRowId = p.Id;
      MazakPartRow = p;
      PartName = r.PartName;
      ProcessNumber = r.ProcessNumber;
      FixQuantity = r.IsFixQuantityNull() ? 1 : r.FixQuantity;
      ContinueCut = r.IsContinueCutNull() ? null : (int?) r.ContinueCut;
      CutMc = r.IsCutMcNull() ? null : r.CutMc;
      FixLDS = r.IsFixLDSNull() ? null : r.FixLDS;
      FixPhoto = r.IsFixPhotoNull() ? null : r.FixPhoto;
      Fixture = r.IsFixtureNull() ? null : r.Fixture;
      MainProgram = r.IsMainProgramNull() ? null : r.MainProgram;
      RemoveLDS = r.IsRemoveLDSNull() ? null : r.RemoveLDS;
      RemovePhoto = r.IsRemovePhotoNull() ? null : r.RemovePhoto;
      WashType = r.IsWashTypeNull() ? null : (int?) r.WashType;
    }
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
    public int? FixForMachine {get;set;}
    public int? HoldMode {get;set;}
    public int? MissingFixture {get;set;}
    public int? MissingProgram {get;set;}
    public int? MissingTool {get;set;}
    public int? MixScheduleID {get;set;}
    public int? ProcessingPriority {get;set;}
    public int? Reserved {get;set;}
    public int? UpdatedFlag {get;set;}

    public IList<MazakScheduleProcessRow> Processes {get;} = new List<MazakScheduleProcessRow>();

    public MazakScheduleRow() {}
    public MazakScheduleRow(ReadOnlyDataSet.ScheduleRow s)
    {
      Id = s.ScheduleID;
      Comment = s.IsCommentNull() ? null : s.Comment;
      PartName = s.PartName;
      PlanQuantity = s.PlanQuantity;
      CompleteQuantity = s.CompleteQuantity;
      Priority = s.Priority;
      DueDate = s.IsDueDateNull() ? null : (DateTime?)s.DueDate;
      FixForMachine = s.IsFixForMachineNull() ? null : (int?)s.FixForMachine;
      HoldMode = s.IsHoldModeNull() ? null : (int?)s.HoldMode;
      MissingFixture = s.IsMissingFixtureNull() ? null : (int?)s.MissingFixture;
      MissingProgram = s.IsMissingProgramNull() ? null : (int?)s.MissingProgram;
      MissingTool = s.IsMissingToolNull() ? null : (int?)s.MissingTool;
      MixScheduleID = s.IsMixScheduleIDNull() ? null : (int?)s.MixScheduleID;
      ProcessingPriority = s.IsProcessingPriorityNull() ? null : (int?)s.ProcessingPriority;
      Reserved = s.IsReservedNull() ? null : (int?)s.Reserved;
      UpdatedFlag = s.IsUpdatedFlagNull() ? null : (int?)s.UpdatedFlag;

    }
  }

  public class MazakScheduleProcessRow
  {
    public int MazakScheduleRowId {get;set;}
    public MazakScheduleRow MazakScheduleRow {get;set;}
    public int FixQuantity {get;set;}

    public int ProcessNumber {get;set;}
    public int ProcessMaterialQuantity {get;set;}
    public int ProcessExecuteQuantity {get;set;}
    public int ProcessBadQuantity {get;set;}
    public int ProcessMachine {get;set;}
    public int UpdatedFlag {get;set;}

    public MazakScheduleProcessRow(MazakScheduleRow sch)
    {
      MazakScheduleRowId = sch.Id;
      MazakScheduleRow = sch;
    }
    public MazakScheduleProcessRow(MazakScheduleRow sch, ReadOnlyDataSet.ScheduleProcessRow p)
    {
      MazakScheduleRowId = sch.Id;
      MazakScheduleRow = sch;
      ProcessNumber = p.ProcessNumber;
      ProcessMaterialQuantity = p.ProcessMaterialQuantity;
      ProcessExecuteQuantity = p.ProcessExecuteQuantity;
      ProcessBadQuantity = p.ProcessBadQuantity;
      ProcessMachine = p.ProcessMachine;
      UpdatedFlag = p.UpdatedFlag;

      FixQuantity = 1;
      foreach (var partRow in ((ReadOnlyDataSet)p.Table.DataSet).PartProcess) {
        if (partRow.PartName == sch.PartName && partRow.ProcessNumber == ProcessNumber) {
          FixQuantity = partRow.FixQuantity;
          break;
        }
      }
    }
  }

  public class MazakSchedulesAndLoadActions
  {
    public IEnumerable<MazakScheduleRow> Schedules {get;}
    public IEnumerable<LoadAction> LoadActions {get;}

    public MazakSchedulesAndLoadActions(IEnumerable<MazakScheduleRow> s, IEnumerable<LoadAction> a)
    {
      Schedules = s;
      LoadActions = a;
    }

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

  public class MazakData : MazakSchedulesAndLoadActions
  {
    public IEnumerable<MazakPartRow> Parts {get;}
    public MazakData(IEnumerable<MazakScheduleRow> s, IEnumerable<LoadAction> a, IEnumerable<MazakPartRow> p)
      : base(s, a)
    {
      Parts = p;
    }
  }

  public interface IReadDataAccess
  {
    MazakSchedulesAndLoadActions LoadSchedules();
    MazakData LoadAllData();

    //these are only here during the transition until the entire read uses just IMazakData
    ReadOnlyDataSet LoadReadSet();
    (MazakData, ReadOnlyDataSet) LoadDataAndReadSet();
    TResult WithReadDBConnection<TResult>(Func<IDbConnection, TResult> action);
  }

}