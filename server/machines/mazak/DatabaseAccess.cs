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
using System.Diagnostics;
using System.Data.OleDb;

namespace MazakMachineInterface
{
  //the following is only defined starting in .net 3.5
  public delegate TResult Func<T, TResult>(T arg);

  public interface IReadDataAccess
  {
    TResult WithReadDBConnection<TResult>(Func<IDbConnection, TResult> action);
    ReadOnlyDataSet LoadReadOnly();
  }

  public class DatabaseAccess
  {
    //Global Settings
    public System.Threading.Mutex MazakTransactionLock = new System.Threading.Mutex();

    //There are two errors we can generate...
    //    a timeout for the open database kit
    //    some errors returned by open database kit
    public const int OpenDatabaseKitTimeoutError = 201;
    public const int TransactionError = 202;
    public const int DatabaseLockedError = 203;
    public const int InvalidPalletPartMapping = 204;
    public const int InvalidArgument = 205;

    //List of commands for the transaction database.
    public const int ForceUnlockEditCommand = 3;
    public const int AddCommand = 4;
    public const int DeleteCommand = 5;
    public const int EditCommand = 6;
    public const int ScheduleSafeEditCommand = 7;
    public const int ScheduleMaterialEditCommand = 8;
    public const int ErrorCommand = 9;

    protected const int WaitCount = 5;

    protected string databaseConnStr;

    public enum MazakDbType
    {
      MazakVersionE,
      MazakWeb,
      MazakSmooth
    }

    public readonly MazakDbType MazakType;

    protected void CheckReadyForConnect()
    {
      if (MazakType != MazakDbType.MazakVersionE)
        return;
      if (!System.IO.File.Exists(System.IO.Path.Combine(databaseConnStr, "ready4Conect.mdb")))
      {
        throw new Exception("Open database kit is not running");
      }
    }

    public DatabaseAccess(string dbConnStr, MazakDbType ty)
    {
      MazakType = ty;
      databaseConnStr = dbConnStr;
    }

    public static string BuildStationStr(int num, string str)
    {
      int i = 0;
      string ret = "";
      string[] arr = str.Split(' ');
      string s = null;
      for (i = 1; i <= num; i++)
      {
        foreach (string s_loopVariable in arr)
        {
          s = s_loopVariable;
          int v;
          if (int.TryParse(s, out v) && v == i)
          {
            ret += i.ToString();
            goto found;
          }
        }
        ret += "0";
      found:;
      }
      return ret;
    }

    public static string ParsePallet(int p)
    {
      return p.ToString().PadLeft(2, '0');
    }

    public static string Join(System.Collections.IEnumerable lst)
    {
      return Join(lst, ",");
    }
    public static string Join(System.Collections.IEnumerable lst, string Seperator)
    {
      string ret = "";

      if (lst == null)
        return "";

      object o = null;
      foreach (object o_loopVariable in lst)
      {
        o = o_loopVariable;
        ret += Seperator + o.ToString();
      }

      if (ret.Length > 0)
      {
        return ret.Substring(Seperator.Length);
      }
      else
      {
        return ret;
      }
    }
  }

	public class TransactionDatabaseAccess
		: DatabaseAccess
	{
    public TransactionDatabaseAccess(string dbConnStr, MazakDbType ty)
			: base(dbConnStr, ty) { }

    //For the transaction database
    private IDbConnection MazakTransactionConnection;
    private System.Data.Common.DbDataAdapter Fixture_t_Adapter;
    private System.Data.Common.DbDataAdapter Pallet_tV2_Adapter;
    private System.Data.Common.DbDataAdapter Pallet_tV1_Adapter;
    private System.Data.Common.DbDataAdapter PartProcess_t_Adapter;
    private System.Data.Common.DbDataAdapter Part_t_Adapter;
    private System.Data.Common.DbDataAdapter ScheduleProcess_t_Adapter;
    private System.Data.Common.DbDataAdapter Schedule_t_Adapter;

    private IList<System.Data.Common.DbDataAdapter> TransactionAdapters = new List<System.Data.Common.DbDataAdapter>();
    private readonly string[] TransactionTables = {
      "fixture_t",
      "pallet_t",
      "Part_t",
      "PartProcess_t",
      "Schedule_t",
      "ScheduleProcess_t"
    };

    private void InitializeTransaction()
    {

      Func<String, System.Data.Common.DbDataAdapter> createAdapter;
      Func<System.Data.Common.DbDataAdapter, System.Data.Common.DbCommandBuilder> createCmdBuilder;
      if (MazakType == MazakDbType.MazakWeb || MazakType == MazakDbType.MazakVersionE)
      {
#if USE_OLEDB
                var transConn = new OleDbConnection();
                transConn.ConnectionString = "Provider=Microsoft.Jet.OLEDB.4.0;Password=\"\";" +
                    "User ID=Admin;" +
                    "Data Source=" + System.IO.Path.Combine(databaseConnStr, "FCNETUSER1.mdb") + ";" +
                    "Mode=Share Deny None;";

                MazakTransactionConnection = transConn;
                createAdapter = s => new OleDbDataAdapter(s, transConn);
                createCmdBuilder = a => new OleDbCommandBuilder((OleDbDataAdapter)a);
#else
        throw new Exception("Mazak Web and VerE are not supported on .NET Core");
#endif
      }
      else
      {
        var dbConn = new System.Data.SqlClient.SqlConnection(databaseConnStr + ";Database=FCNETUSER01");
        MazakTransactionConnection = dbConn;
        createAdapter = s => new System.Data.SqlClient.SqlDataAdapter(s, dbConn);
        createCmdBuilder = a => new System.Data.SqlClient.SqlCommandBuilder((System.Data.SqlClient.SqlDataAdapter)a);
      }


      Fixture_t_Adapter = createAdapter("SELECT Command, Comment, FixtureName, Reserved, TransactionStatus FROM Fixture_t");
      TransactionAdapters.Add(Fixture_t_Adapter);

      if (MazakType == MazakDbType.MazakVersionE)
      {
        Pallet_tV1_Adapter = createAdapter("SELECT Angle, Command, Fixture, PalletNumber, RecordID, TransactionStatus FROM Pallet_t");
        TransactionAdapters.Add(Pallet_tV1_Adapter);
      }
      else
      {
        Pallet_tV2_Adapter = createAdapter("SELECT Command, Fixture, FixtureGroup, PalletNumber, RecordID, TransactionStatus FROM Pallet_t");
        TransactionAdapters.Add(Pallet_tV2_Adapter);
      }

      if (MazakType == MazakDbType.MazakSmooth)
      {
        Part_t_Adapter = createAdapter("SELECT Command, Comment, PartName, Price, Reserved, TotalProcess," +
            "MaterialName, Part_1, Part_2, Part_3, Part_4, Part_5, TransactionStatus FROM Part_t");
        TransactionAdapters.Add(Part_t_Adapter);

        PartProcess_t_Adapter = createAdapter("SELECT ContinueCut, CutMc, FixLDS, FixPhoto, FixQuantity," +
            " Fixture, MainProgram, PartName, ProcessNumber, RemoveLDS, RemovePhoto, Reserved, WashType, " +
            "PartProcess_1, PartProcess_2, PartProcess_3, PartProcess_4, FixTime, RemoveTime, CreateToolList_RA" +
            " FROM PartProcess_t");
        TransactionAdapters.Add(PartProcess_t_Adapter);
      }
      else
      {
        Part_t_Adapter = createAdapter("SELECT Command, Comment, PartName, Price, Reserved, TotalProcess, TransactionStatus FROM Part_t");
        TransactionAdapters.Add(Part_t_Adapter);

        PartProcess_t_Adapter = createAdapter("SELECT ContinueCut, CutMc, FixLDS, FixPhoto, FixQuantity, Fixture, MainProgram, PartName, ProcessNumber, RemoveLDS, RemovePhoto, Reserved, WashType FROM PartProcess_t");
        TransactionAdapters.Add(PartProcess_t_Adapter);
      }

      Schedule_t_Adapter = createAdapter("SELECT Command, Comment, CompleteQuantity, DueDate, FixForMachine, HoldMode, MissingFixture, MissingProgram, MissingTool, MixScheduleID, PartName, PlanQuantity, Priority, ProcessingPriority, Reserved, ScheduleID, TransactionStatus FROM Schedule_t");
      TransactionAdapters.Add(Schedule_t_Adapter);

      ScheduleProcess_t_Adapter = createAdapter("SELECT ProcessBadQuantity, ProcessExecuteQuantity, ProcessMachine, ProcessMaterialQuantity, ProcessNumber, Reserved, ScheduleID FROM ScheduleProcess_t");
      TransactionAdapters.Add(ScheduleProcess_t_Adapter);

      foreach (var adapter in TransactionAdapters)
      {
        var builder = createCmdBuilder(adapter);
        adapter.InsertCommand = builder.GetInsertCommand();
      }
    }

    private void OpenTransaction()
    {
      int attempts = 0;

      while (attempts < 20)
      {
        try
        {
          MazakTransactionConnection.Open();
          return;
#if USE_OLEDB
				} catch (OleDbException ex) {
					if (!(ex.Message.ToLower().IndexOf("could not use") >= 0)) {
						if (!(ex.Message.ToLower().IndexOf("try again") >= 0)) {
							//if this is not a locking exception, throw it
							throw new Exception(ex.ToString());
						}
					}
#endif
        }
        catch (Exception ex)
        {
          if (!(ex.Message.ToLower().IndexOf("could not use") >= 0))
          {
            if (!(ex.Message.ToLower().IndexOf("try again") >= 0))
            {
              //if this is not a locking exception, throw it
              throw;
            }
          }
        }

        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));

        attempts += 1;
      }

      throw new Exception("Transaction database is locked and can not be accessed");
    }

    public void SaveTransaction(TransactionDataSet dset, System.Collections.Generic.IList<string> log, string prefix)
    {
      SaveTransaction(dset, log, prefix, -1);
    }
    public void SaveTransaction(TransactionDataSet dset, System.Collections.Generic.IList<string> log, string prefix, int checkInterval)
    {
      CheckReadyForConnect();

      if (checkInterval <= 0)
      {
        int totalRows = 0;
        totalRows += dset.Fixture_t.Rows.Count;
        totalRows += dset.Pallet_tV1.Rows.Count;
        totalRows += dset.Pallet_tV2.Rows.Count;
        totalRows += dset.Part_t.Rows.Count;
        totalRows += dset.PartProcess_t.Rows.Count;
        totalRows += dset.Schedule_t.Rows.Count;
        totalRows += dset.ScheduleProcess_t.Rows.Count;
        checkInterval = 10 + totalRows;
        //We wait 10 seconds plus one second for every row in the table
      }

      if (MazakType == MazakDbType.MazakSmooth)
      {
        dset.Part_t.Columns.Add("MaterialName", typeof(string));
        dset.Part_t.Columns.Add("Part_1", typeof(int));
        dset.Part_t.Columns.Add("Part_2", typeof(int));
        dset.Part_t.Columns.Add("Part_3", typeof(string));
        dset.Part_t.Columns.Add("Part_4", typeof(int));
        dset.Part_t.Columns.Add("Part_5", typeof(int));
        foreach (var row in dset.Part_t)
        {
          row["MaterialName"] = ' ';
          row["Part_1"] = 0;
          row["Part_2"] = 0;
          row["Part_3"] = ' ';
          row["Part_4"] = 0;
          row["Part_5"] = 0;
        }
        dset.PartProcess_t.Columns.Add("PartProcess_1", typeof(int));
        dset.PartProcess_t.Columns.Add("PartProcess_2", typeof(int));
        dset.PartProcess_t.Columns.Add("PartProcess_3", typeof(int));
        dset.PartProcess_t.Columns.Add("PartProcess_4", typeof(int));
        dset.PartProcess_t.Columns.Add("FixTime", typeof(int));
        dset.PartProcess_t.Columns.Add("RemoveTime", typeof(int));
        dset.PartProcess_t.Columns.Add("CreateToolList_RA", typeof(int));
        foreach (var row in dset.PartProcess_t)
        {
          row["PartProcess_1"] = 0;
          row["PartProcess_2"] = 0;
          row["PartProcess_3"] = 0;
          row["PartProcess_4"] = 0;
          row["FixTime"] = 0;
          row["RemoveTime"] = 0;
          row["CreateToolList_RA"] = 0;
        }
      }

      OpenTransaction();

      try
      {
        var trans = MazakTransactionConnection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
          SetTransactionTransaction(trans);

          Fixture_t_Adapter.Update(dset.Fixture_t);
          if (MazakType != MazakDbType.MazakVersionE)
          {
            Pallet_tV2_Adapter.Update(dset.Pallet_tV2);
          }
          else
          {
            Pallet_tV1_Adapter.Update(dset.Pallet_tV1);
          }
          PartProcess_t_Adapter.Update(dset.PartProcess_t);
          Part_t_Adapter.Update(dset.Part_t);
          ScheduleProcess_t_Adapter.Update(dset.ScheduleProcess_t);
          Schedule_t_Adapter.Update(dset.Schedule_t);

          trans.Commit();
        }
        catch (Exception ex)
        {
          trans.Rollback();
          throw ex;
        }
#if USE_OLEDB
			} catch (OleDbException ex) {
				throw new Exception(ex.ToString());
#endif
      }
      finally
      {
        MazakTransactionConnection.Close();
      }

      int i = 0;
      for (i = 0; i <= WaitCount; i++)
      {
        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(checkInterval));
        if (CheckTransactionErrors(prefix, log))
        {
          goto success;
        }
      }
      throw new Exception("Timeout during download: open database kit is not running");
    success:


      ClearTransactionDatabase();
    }

    private bool CheckTransactionErrors(string prefix, System.Collections.Generic.IList<string> log)
    {
      TransactionDataSet transSet = new TransactionDataSet();

      CheckReadyForConnect();

      OpenTransaction();
      try
      {
        var trans = MazakTransactionConnection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
          SetTransactionTransaction(trans);

          Fixture_t_Adapter.Fill(transSet.Fixture_t);
          if (MazakType != MazakDbType.MazakVersionE)
          {
            Pallet_tV2_Adapter.Fill(transSet.Pallet_tV2);
          }
          else
          {
            Pallet_tV1_Adapter.Fill(transSet.Pallet_tV1);
          }
          Part_t_Adapter.Fill(transSet.Part_t);
          Schedule_t_Adapter.Fill(transSet.Schedule_t);

          trans.Commit();
        }
        catch (Exception ex)
        {
          trans.Rollback();
          throw ex;
        }

#if USE_OLEDB
			} catch (OleDbException ex) {
				throw new Exception(ex.ToString());
#endif
      }
      finally
      {
        MazakTransactionConnection.Close();
      }

      DataTable[] lst = {
        transSet.Fixture_t,
        transSet.Pallet_tV1,
        transSet.Pallet_tV2,
        transSet.Part_t,
        transSet.Schedule_t
      };
      foreach (DataTable dataTable in lst)
      {
        try
        {
          foreach (DataRow dataRow in dataTable.Rows)
          {
            if (!dataRow.IsNull("Command"))
            {
              if (Convert.ToInt32(dataRow["Command"]) == ErrorCommand)
              {
                log.Add(prefix + " Mazak transaction returned error " + dataRow["TransactionStatus"].ToString() + " on row " + ConvertRowToString(dataRow));
              }
              else
              {
                return false;
              }
            }
          }
        }
        catch
        {
        }
      }

      return true;
    }
    private string ConvertRowToString(DataRow row)
    {
      DataColumn col = null;
      string res = "";
      foreach (DataColumn col_loopVariable in row.Table.Columns)
      {
        col = col_loopVariable;
        if (row.IsNull(col))
        {
          res += col.ColumnName + ":(null);";
        }
        else
        {
          res += col.ColumnName + ":" + row[col].ToString() + ";";
        }
      }
      return res;
    }
    public void ClearTransactionDatabase()
    {
      CheckReadyForConnect();

      OpenTransaction();
      try
      {
        var trans = MazakTransactionConnection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {

          var cmd = MazakTransactionConnection.CreateCommand();
          cmd.Transaction = trans;

          foreach (string table in TransactionTables)
          {

            cmd.CommandText = "DELETE FROM " + table;
            cmd.ExecuteNonQuery();

          }

          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }

#if USE_OLEDB
			} catch (OleDbException ex) {
				throw new Exception(ex.ToString());
#endif
      }
      finally
      {
        MazakTransactionConnection.Close();
      }
    }

    private void SetTransactionTransaction(IDbTransaction trans)
    {
      foreach (var adapter in TransactionAdapters)
      {
        ((IDbCommand)adapter.SelectCommand).Transaction = trans;
        ((IDbCommand)adapter.InsertCommand).Transaction = trans;
      }
    }

    public static void BuildPartRow(TransactionDataSet.Part_tRow newRow, ReadOnlyDataSet.PartRow curRow)
    {
      newRow.Command = EditCommand;
      if (!curRow.IsCommentNull())
        newRow.Comment = curRow.Comment;
      if (!curRow.IsPriceNull())
        newRow.Price = curRow.Price;
      newRow.TotalProcess = curRow.GetPartProcessRows().Length;
    }
    public static void BuildPartProcessRow(TransactionDataSet.PartProcess_tRow newRow, ReadOnlyDataSet.PartProcessRow curRow)
    {
      if (!curRow.IsContinueCutNull())
        newRow.ContinueCut = curRow.ContinueCut;
      if (!curRow.IsCutMcNull())
        newRow.CutMc = curRow.CutMc;
      if (!curRow.IsFixLDSNull())
        newRow.FixLDS = curRow.FixLDS;
      if (!curRow.IsFixPhotoNull())
        newRow.FixPhoto = curRow.FixPhoto;
      if (!curRow.IsFixQuantityNull())
        newRow.FixQuantity = curRow.FixQuantity.ToString();
      if (!curRow.IsFixtureNull())
        newRow.Fixture = curRow.Fixture;
      if (!curRow.IsMainProgramNull())
        newRow.MainProgram = curRow.MainProgram;
      if (!curRow.IsPartNameNull())
        newRow.PartName = curRow.PartName;
      if (!curRow.IsProcessNumberNull())
        newRow.ProcessNumber = curRow.ProcessNumber;
      if (!curRow.IsRemoveLDSNull())
        newRow.RemoveLDS = curRow.RemoveLDS;
      if (!curRow.IsRemovePhotoNull())
        newRow.RemovePhoto = curRow.RemovePhoto;
      if (!curRow.IsWashTypeNull())
        newRow.WashType = curRow.WashType;
    }
    public static void BuildScheduleEditRow(TransactionDataSet.Schedule_tRow newRow, ReadOnlyDataSet.ScheduleRow curRow, bool updateMaterial)
    {
      if (updateMaterial)
      {
        newRow.Command = ScheduleMaterialEditCommand;
      }
      else
      {
        newRow.Command = ScheduleSafeEditCommand;
      }
      if (!curRow.IsCommentNull())
        newRow.Comment = curRow.Comment;
      if (!curRow.IsCompleteQuantityNull())
        newRow.CompleteQuantity = curRow.CompleteQuantity;
      if (!curRow.IsDueDateNull())
        newRow.DueDate = curRow.DueDate;
      if (!curRow.IsFixForMachineNull())
        newRow.FixForMachine = curRow.FixForMachine;
      if (!curRow.IsHoldModeNull())
        newRow.HoldMode = curRow.HoldMode;
      if (!curRow.IsMissingFixtureNull())
        newRow.MissingFixture = curRow.MissingFixture;
      if (!curRow.IsMissingProgramNull())
        newRow.MissingProgram = curRow.MissingProgram;
      if (!curRow.IsMissingToolNull())
        newRow.MissingTool = curRow.MissingTool;
      if (!curRow.IsMixScheduleIDNull())
        newRow.MixScheduleID = curRow.MixScheduleID;
      if (!curRow.IsPartNameNull())
        newRow.PartName = curRow.PartName;
      if (!curRow.IsPlanQuantityNull())
        newRow.PlanQuantity = curRow.PlanQuantity;
      if (!curRow.IsPriorityNull())
        newRow.Priority = curRow.Priority;
      if (!curRow.IsProcessingPriorityNull())
        newRow.ProcessingPriority = curRow.ProcessingPriority;
      newRow.ScheduleID = curRow.ScheduleID;
    }
    public static void BuildScheduleProcEditRow(TransactionDataSet.ScheduleProcess_tRow newRow, ReadOnlyDataSet.ScheduleProcessRow curRow)
    {
      if (!curRow.IsProcessBadQuantityNull())
        newRow.ProcessBadQuantity = curRow.ProcessBadQuantity;
      if (!curRow.IsProcessExecuteQuantityNull())
        newRow.ProcessExecuteQuantity = curRow.ProcessExecuteQuantity;
      if (!curRow.IsProcessMachineNull())
        newRow.ProcessMachine = curRow.ProcessMachine;
      if (!curRow.IsProcessMaterialQuantityNull())
        newRow.ProcessMaterialQuantity = curRow.ProcessMaterialQuantity;
      if (!curRow.IsProcessNumberNull())
        newRow.ProcessNumber = curRow.ProcessNumber;
      if (!curRow.IsScheduleIDNull())
        newRow.ScheduleID = curRow.ScheduleID;
    }
	}

	public class ReadonlyDatabaseAccess
	  : DatabaseAccess, IReadDataAccess
	{
		private System.Threading.Mutex MazakReadonlyLock = new System.Threading.Mutex();

    public ReadonlyDatabaseAccess(string dbConnStr, MazakDbType ty)
			: base(dbConnStr, ty)
		{
			InitializeReadOnly();
  	}

    //For the read-only database
    private IDbConnection ReadOnlyDBConnection;
    private System.Data.Common.DbDataAdapter FixtureAdapter;
    private System.Data.Common.DbDataAdapter PalletAdapter;
    private System.Data.Common.DbDataAdapter PartAdapter;
    private System.Data.Common.DbDataAdapter PartProcessAdapter;
    private System.Data.Common.DbDataAdapter ScheduleAdapter;
    private System.Data.Common.DbDataAdapter ScheduleProcessAdapter;
    private System.Data.Common.DbDataAdapter PalletSubStatusAdapter;
    private System.Data.Common.DbDataAdapter PalletPositionAdapter;
    private System.Data.Common.DbDataAdapter MainProgramAdapter;

    public TResult WithReadDBConnection<TResult>(Func<IDbConnection, TResult> action)
    {
      if (!MazakReadonlyLock.WaitOne(TimeSpan.FromMinutes(1)))
      {
        throw new ApplicationException("Timed out waiting for readonly database");
      }
      try
      {
        CheckReadyForConnect();

        OpenReadonly();
        try
        {
          return action(ReadOnlyDBConnection);

#if USE_OLEDB
				} catch (OleDbException ex) {
					throw new Exception(ex.ToString());
#endif
        }
        finally
        {
          ReadOnlyDBConnection.Close();
        }

      }
      finally
      {
        MazakReadonlyLock.ReleaseMutex();
      }
    }

    private IList<System.Data.Common.DbDataAdapter> ReadOnlyAdapters = new List<System.Data.Common.DbDataAdapter>();

    private void InitializeReadOnly()
    {
      Func<String, System.Data.Common.DbDataAdapter> createAdapter;
      if (MazakType == MazakDbType.MazakWeb || MazakType == MazakDbType.MazakVersionE)
      {
#if USE_OLEDB
                var dbConn = new OleDbConnection();


                dbConn.ConnectionString = "Provider=Microsoft.Jet.OLEDB.4.0;Password=\"\";User ID=Admin;" +
                    "Data Source=" + System.IO.Path.Combine(databaseConnStr, "FCREADDAT01.mdb") + ";" +
                    "Mode=Share Deny Write;";

                ReadOnlyDBConnection = dbConn;
                createAdapter = s => new OleDbDataAdapter(s, dbConn);
#else
        throw new Exception("Mazak Web and VerE are not supported on .NET Core");
#endif
      }
      else
      {
        var dbConn = new System.Data.SqlClient.SqlConnection(databaseConnStr + ";Database=FCREADDAT01");
        ReadOnlyDBConnection = dbConn;
        createAdapter = s => new System.Data.SqlClient.SqlDataAdapter(s, dbConn);
      }

      FixtureAdapter = createAdapter("SELECT Comment, FixtureName, ID, Reserved, UpdatedFlag FROM Fixture");
      ReadOnlyAdapters.Add(FixtureAdapter);

      if (MazakType != MazakDbType.MazakVersionE)
      {
        PalletAdapter = createAdapter("SELECT FixtureGroup AS FixtureGroupV2, Fixture, PalletNumber, RecordID, Reserved, UpdatedFlag FROM Pallet");
      }
      else
      {
        PalletAdapter = createAdapter("SELECT Angle AS AngleV1, Fixture, PalletNumber, RecordID, Reserved, UpdatedFlag FROM Pallet");
      }
      ReadOnlyAdapters.Add(PalletAdapter);

      PartAdapter = createAdapter("SELECT Comment, id, PartName, Price, Reserved, UpdatedFlag FROM Part");
      ReadOnlyAdapters.Add(PartAdapter);

      PartProcessAdapter = createAdapter("SELECT ContinueCut, CutMc, CuttingID, FixLDS, FixPhoto, FixQuantity, Fixture, MainProgram, PartName, ProcessNumber, RemoveLDS, RemovePhoto, Reserved, UpdatedFlag, WashType FROM PartProcess");
      ReadOnlyAdapters.Add(PartProcessAdapter);

      ScheduleAdapter = createAdapter("SELECT Comment, CompleteQuantity, DueDate, FixForMachine, HoldMode, MissingFixture, MissingProgram, MissingTool, MixScheduleID, PartName, PlanQuantity, Priority, ProcessingPriority, Reserved, ScheduleID, UpdatedFlag FROM Schedule");
      ReadOnlyAdapters.Add(ScheduleAdapter);

      ScheduleProcessAdapter = createAdapter("SELECT ID, ProcessBadQuantity, ProcessExecuteQuantity, ProcessMachine, ProcessMaterialQuantity, ProcessNumber, ScheduleID, UpdatedFlag FROM ScheduleProcess");
      ReadOnlyAdapters.Add(ScheduleProcessAdapter);

      PalletSubStatusAdapter = createAdapter("SELECT ErrorStatus, FixQuantity, FixtureName, id, MeasureCode, PalletID, PalletNumber, PalletStatus, PartName, PartProcessNumber, ProgramNumber, Reserved, ScheduleID, UpdatedFlag FROM PalletSubStatus");
      ReadOnlyAdapters.Add(PalletSubStatusAdapter);

      PalletPositionAdapter = createAdapter("SELECT id, PalletNumber, PalletPosition FROM PalletPosition");
      ReadOnlyAdapters.Add(PalletPositionAdapter);

      MainProgramAdapter = createAdapter("SELECT id, MainProgram, Comment FROM MainProgram");
      ReadOnlyAdapters.Add(MainProgramAdapter);
    }

    private void OpenReadonly()
    {
      int attempts = 0;

      while (attempts < 20)
      {
        try
        {
          ReadOnlyDBConnection.Open();
          return;
#if USE_OLEDB
				} catch (OleDbException ex) {
					if (!(ex.Message.ToLower().IndexOf("could not use") >= 0)) {
						if (!(ex.Message.ToLower().IndexOf("try again") >= 0)) {
							//if this is not a locking exception, throw it
							throw new DataException(ex.ToString());
						}
					}
#endif
        }
        catch (Exception ex)
        {
          if (!(ex.Message.ToLower().IndexOf("could not use") >= 0))
          {
            if (!(ex.Message.ToLower().IndexOf("try again") >= 0))
            {
              //if this is not a locking exception, throw it
              throw;
            }
          }
        }

        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));

        attempts += 1;
      }

      throw new Exception("Readonly database is locked and can not be accessed");
    }

    public ReadOnlyDataSet LoadReadOnly()
    {
      return WithReadDBConnection(conn =>
      {
        ReadOnlyDataSet dset = new ReadOnlyDataSet();
        dset.EnforceConstraints = false;

        var trans = ReadOnlyDBConnection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
          SetReadOnlyTransaction(trans);

          FixtureAdapter.Fill(dset.Fixture);
          PalletAdapter.Fill(dset.Pallet);
          PalletPositionAdapter.Fill(dset.PalletPosition);
          PartAdapter.Fill(dset.Part);
          PartProcessAdapter.Fill(dset.PartProcess);
          ScheduleAdapter.Fill(dset.Schedule);
          ScheduleProcessAdapter.Fill(dset.ScheduleProcess);
          PalletSubStatusAdapter.Fill(dset.PalletSubStatus);
          MainProgramAdapter.Fill(dset.MainProgram);

          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }

        return dset;

      });
    }

    private void SetReadOnlyTransaction(IDbTransaction trans)
    {
      foreach (var adapter in ReadOnlyAdapters)
        ((IDbCommand)adapter.SelectCommand).Transaction = trans;
    }

	}
}
