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
using System.Diagnostics;
using System.Data.OleDb;
using Dapper;

namespace MazakMachineInterface
{
  public class OpenDatabaseKitDB
  {
    //Global Settings
    public static System.Threading.Mutex MazakTransactionLock = new System.Threading.Mutex();

    protected string ready4ConectPath;

    public MazakDbType MazakType {get;}

    protected void CheckReadyForConnect()
    {
      if (MazakType != MazakDbType.MazakVersionE)
        return;
      if (!System.IO.File.Exists(ready4ConectPath))
      {
        throw new Exception("Open database kit is not running");
      }
    }

    protected OpenDatabaseKitDB(string dbConnStr, MazakDbType ty)
    {
      MazakType = ty;
      if (MazakType != MazakDbType.MazakSmooth)
      {
        ready4ConectPath = System.IO.Path.Combine(dbConnStr, "ready4Conect.mdb");
      }
    }
  }

	public class OpenDatabaseKitTransactionDB
		: OpenDatabaseKitDB, IWriteData
	{
    public OpenDatabaseKitTransactionDB(string dbConnStr, MazakDbType ty)
			: base(dbConnStr, ty)
    {
      databaseConnStr = dbConnStr;
      InitializeTransaction();
    }

    private string databaseConnStr;

    //List of commands for the transaction database.
    public const int ForceUnlockEditCommand = 3;
    public const int AddCommand = 4;
    public const int DeleteCommand = 5;
    public const int EditCommand = 6;
    public const int ScheduleSafeEditCommand = 7;
    public const int ScheduleMaterialEditCommand = 8;
    public const int ErrorCommand = 9;

    protected const int WaitCount = 5;


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
#else
        throw new Exception("Mazak Web and VerE are not supported on .NET Core");
#endif
      }
      else
      {
        var dbConn = new System.Data.SqlClient.SqlConnection(databaseConnStr + ";Database=FCNETUSER01");
        MazakTransactionConnection = dbConn;
        createAdapter = s => new System.Data.SqlClient.SqlDataAdapter(s, dbConn);
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
    }

    private void EnsureInsertCommands()
    {
      foreach (var adapter in TransactionAdapters)
      {
        if (adapter.InsertCommand == null) {
          System.Data.Common.DbCommandBuilder builder;
          if (MazakType == MazakDbType.MazakWeb || MazakType == MazakDbType.MazakVersionE)
          {
    #if USE_OLEDB
            builder = new OleDbCommandBuilder((OleDbDataAdapter)adapter);
    #else
            throw new Exception("Mazak Web and VerE are not supported on .NET Core");
    #endif
          }
          else
          {
            builder = new System.Data.SqlClient.SqlCommandBuilder((System.Data.SqlClient.SqlDataAdapter)adapter);
          }

          adapter.InsertCommand = builder.GetInsertCommand();
        }
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
          EnsureInsertCommands();
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

    public void SaveTransaction(TransactionDataSet dset, System.Collections.Generic.IList<string> log, string prefix, int checkInterval = -1)
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

          using (var cmd = MazakTransactionConnection.CreateCommand()) {
          cmd.Transaction = trans;

          foreach (string table in TransactionTables)
          {

            cmd.CommandText = "DELETE FROM " + table;
            cmd.ExecuteNonQuery();

          }

          trans.Commit();
          }
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

    public static void BuildPartProcessRow(TransactionDataSet.PartProcess_tRow newRow, MazakPartProcessRow curRow)
    {
      if (curRow.ContinueCut.HasValue)
        newRow.ContinueCut = curRow.ContinueCut.Value;
      newRow.CutMc = curRow.CutMc;
      newRow.FixLDS = curRow.FixLDS;
      newRow.FixPhoto = curRow.FixPhoto;
      newRow.FixQuantity = curRow.FixQuantity.ToString();
      newRow.Fixture = curRow.Fixture;
      newRow.MainProgram = curRow.MainProgram;
      newRow.PartName = curRow.PartName;
      newRow.ProcessNumber = curRow.ProcessNumber;
      newRow.RemoveLDS = curRow.RemoveLDS;
      newRow.RemovePhoto = curRow.RemovePhoto;
      if (curRow.WashType.HasValue)
        newRow.WashType = curRow.WashType.Value;
    }
    public static void BuildScheduleEditRow(TransactionDataSet.Schedule_tRow newRow, MazakScheduleRow curRow, bool updateMaterial)
    {
      if (updateMaterial)
      {
        newRow.Command = ScheduleMaterialEditCommand;
      }
      else
      {
        newRow.Command = ScheduleSafeEditCommand;
      }
      newRow.Comment = curRow.Comment;
      newRow.CompleteQuantity = curRow.CompleteQuantity;
      if (curRow.DueDate.HasValue)
        newRow.DueDate = curRow.DueDate.Value;
      if (curRow.FixForMachine.HasValue)
        newRow.FixForMachine = curRow.FixForMachine.Value;
      if (curRow.HoldMode.HasValue)
        newRow.HoldMode = curRow.HoldMode.Value;
      if (curRow.MissingFixture.HasValue)
        newRow.MissingFixture = curRow.MissingFixture.Value;
      if (curRow.MissingProgram.HasValue)
        newRow.MissingProgram = curRow.MissingProgram.Value;
      if (curRow.MissingTool.HasValue)
        newRow.MissingTool = curRow.MissingTool.Value;
      if (curRow.MixScheduleID.HasValue)
        newRow.MixScheduleID = curRow.MixScheduleID.Value;
      newRow.PartName = curRow.PartName;
      newRow.PlanQuantity = curRow.PlanQuantity;
      newRow.Priority = curRow.Priority;
      if (curRow.ProcessingPriority.HasValue)
        newRow.ProcessingPriority = curRow.ProcessingPriority.Value;
      newRow.ScheduleID = curRow.Id;
    }
    public static void BuildScheduleProcEditRow(TransactionDataSet.ScheduleProcess_tRow newRow, MazakScheduleProcessRow curRow)
    {
      newRow.ProcessBadQuantity = curRow.ProcessBadQuantity;
      newRow.ProcessExecuteQuantity = curRow.ProcessExecuteQuantity;
      newRow.ProcessMachine = curRow.ProcessMachine;
      newRow.ProcessMaterialQuantity = curRow.ProcessMaterialQuantity;
      newRow.ProcessNumber = curRow.ProcessNumber;
      newRow.ScheduleID = curRow.MazakScheduleRowId;
    }
	}

	public class OpenDatabaseKitReadDB
	  : OpenDatabaseKitDB, IReadDataAccess
	{
    private string _connectionStr;
    private string _fixtureSelect;
    private string _palletSelect;
    private string _partSelect;
    private string _partProcSelect;
    private string _scheduleSelect;
    private string _scheduleProcSelect;
    private string _partProcFixQty;
    private string _palSubStatusSelect;
    private string _palPositionSelect;
    private string _mainProgSelect;

    private LoadOperationsFromFile _loadOper;

    public OpenDatabaseKitReadDB(string dbConnStr, MazakDbType ty, LoadOperationsFromFile loadOper)
			: base(dbConnStr, ty)
		{
      _loadOper = loadOper;
      if (MazakType == MazakDbType.MazakWeb || MazakType == MazakDbType.MazakVersionE)
      {
        _connectionStr = "Provider=Microsoft.Jet.OLEDB.4.0;Password=\"\";User ID=Admin;" +
            "Data Source=" + System.IO.Path.Combine(dbConnStr, "FCREADDAT01.mdb") + ";" +
            "Mode=Share Deny Write;";
      }
      else
      {
        _connectionStr = dbConnStr + ";Database=FCREADDAT01";
      }

      _fixtureSelect = "SELECT Comment, FixtureName FROM Fixture";

      if (MazakType != MazakDbType.MazakVersionE)
      {
        _palletSelect = "SELECT FixtureGroup AS FixtureGroupV2, Fixture, PalletNumber, RecordID FROM Pallet";
      }
      else
      {
        _palletSelect = "SELECT Angle AS AngleV1, Fixture, PalletNumber, RecordID FROM Pallet";
      }

      _partSelect = "SELECT Comment, Id, PartName, Price FROM Part";
      _partProcSelect = "SELECT ContinueCut, CutMc, FixLDS, FixPhoto, FixQuantity, Fixture, MainProgram, PartName, ProcessNumber, RemoveLDS, RemovePhoto, WashType FROM PartProcess";
      _scheduleSelect = "SELECT Comment, CompleteQuantity, DueDate, FixForMachine, HoldMode, MissingFixture, MissingProgram, MissingTool, MixScheduleID, PartName, PlanQuantity, Priority, ProcessingPriority, Reserved, ScheduleID As Id, UpdatedFlag FROM Schedule";

      // normally would use a join to determine FixQuantity as part of the schedule proc row,
      // but the mazak readdb has no indexes and no keys, so everything is a table scan.
      _scheduleProcSelect = "SELECT ProcessBadQuantity, ProcessExecuteQuantity, ProcessMachine, ProcessMaterialQuantity, ProcessNumber, ScheduleID As MazakScheduleRowId, UpdatedFlag " +
        " FROM ScheduleProcess";
      _partProcFixQty = "SELECT PartName, ProcessNumber, FixQuantity FROM PartProcess";

      _palSubStatusSelect = "SELECT FixQuantity, FixtureName, PalletNumber, PartName, PartProcessNumber, ScheduleID FROM PalletSubStatus";
      _palPositionSelect = "SELECT PalletNumber, PalletPosition FROM PalletPosition WHERE PalletNumber > 0";
      _mainProgSelect = "SELECT MainProgram FROM MainProgram";
  	}

    public TResult WithReadDBConnection<TResult>(Func<IDbConnection, TResult> action)
    {
      CheckReadyForConnect();
      using (var conn = CreateConnection())
      {
        return action(conn);
      }
    }

    private IDbConnection OpenReadonlyOleDb()
    {
#if USE_OLEDB
      int attempts = 0;

      var conn = new OleDbConnection(_connectionStr);
      while (attempts < 20)
      {
        try
        {
          conn.Open();
          return conn;
				} catch (OleDbException ex) {
					if (!(ex.Message.ToLower().IndexOf("could not use") >= 0)) {
						if (!(ex.Message.ToLower().IndexOf("try again") >= 0)) {
							//if this is not a locking exception, throw it
              conn.Dispose();
							throw new DataException(ex.ToString());
						}
					}
        }
        catch (Exception ex)
        {
          if (!(ex.Message.ToLower().IndexOf("could not use") >= 0))
          {
            if (!(ex.Message.ToLower().IndexOf("try again") >= 0))
            {
              //if this is not a locking exception, throw it
              conn.Dispose();
              throw;
            }
          }
        }

        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));

        attempts += 1;
      }

      conn.Dispose();
      throw new Exception("Readonly database is locked and can not be accessed");
#else
      throw new Exception("Mazak Web and VerE are not supported on .NET Core");
#endif
    }

    private IDbConnection CreateConnection()
    {
      if (MazakType == MazakDbType.MazakWeb || MazakType == MazakDbType.MazakVersionE)
      {
        return OpenReadonlyOleDb();
      }
      else
      {
        var conn = new System.Data.SqlClient.SqlConnection(_connectionStr);
        conn.Open();
        return conn;
      }
    }

    private struct PartProcessFixQty
    {
      public string PartName {get;set;}
      public int ProcessNumber {get;set;}
      public int FixQuantity {get;set;}
    }

    private IEnumerable<MazakScheduleRow> LoadSchedules(IDbConnection conn, IDbTransaction trans, IEnumerable<PartProcessFixQty> fixQtys)
    {
      var schs = conn.Query<MazakScheduleRow>(_scheduleSelect, transaction: trans);
      var schDict = schs.ToDictionary(s => s.Id, s => s);

      var procs = conn.Query<MazakScheduleProcessRow>(_scheduleProcSelect, transaction: trans);
      foreach (var proc in procs) {
        proc.FixQuantity = 1;
        if (schDict.ContainsKey(proc.MazakScheduleRowId)) {
          var schRow = schDict[proc.MazakScheduleRowId];
          schRow.Processes.Add(proc);

          foreach (var p in fixQtys) {
            if (p.PartName == schRow.PartName && p.ProcessNumber == proc.ProcessNumber) {
              proc.FixQuantity = p.FixQuantity;
            }
          }
        }
      }
      return schs;
    }

    public MazakSchedules LoadSchedules()
    {
      return WithReadDBConnection(conn => {
        var trans = conn.BeginTransaction();
        try {

          var partFixQty = conn.Query<PartProcessFixQty>(_partProcFixQty, transaction: trans);
          var ret = new MazakSchedules() {
            Schedules = LoadSchedules(conn, trans, partFixQty)
          };

          trans.Commit();

          return ret;
        } catch {
          trans.Rollback();
          throw;
        }
      });
    }

    public MazakSchedulesAndLoadActions LoadSchedulesAndLoadActions()
    {
      var sch = LoadSchedules();
      return new MazakSchedulesAndLoadActions() {
        Schedules = sch.Schedules,
        LoadActions = _loadOper.CurrentLoadActions()
      };
    }

    private MazakSchedulesPartsPallets LoadSchedulesPartsPallets(IDbConnection conn, IDbTransaction trans)
    {
      var parts = conn.Query<MazakPartRow>(_partSelect, transaction: trans);
      var partsByName = parts.ToDictionary(p => p.PartName, p => p);
      var procs = conn.Query<MazakPartProcessRow>(_partProcSelect, transaction: trans);
      var fixQty = new List<PartProcessFixQty>();
      foreach (var proc in procs) {
        if (partsByName.ContainsKey(proc.PartName)) {
          var part = partsByName[proc.PartName];
          part.Processes.Add(proc);
          proc.MazakPartRowId = part.Id;
        }
        fixQty.Add(new PartProcessFixQty() {
          PartName = proc.PartName,
          ProcessNumber = proc.ProcessNumber,
          FixQuantity = proc.FixQuantity
        });
      }

      return new MazakSchedulesPartsPallets() {
        Schedules = LoadSchedules(conn, trans, fixQty),
        Parts = parts,
        Pallets = conn.Query<MazakPalletRow>(_palletSelect, transaction: trans),
        PalletSubStatuses = conn.Query<MazakPalletSubStatusRow>(_palSubStatusSelect, transaction: trans),
        PalletPositions = conn.Query<MazakPalletPositionRow>(_palPositionSelect, transaction: trans),
        MainPrograms = new HashSet<string>(conn.Query<string>(_mainProgSelect, transaction: trans)),
      };
    }
    public MazakSchedulesPartsPallets LoadSchedulesPartsPallets()
    {
      return LoadSchedulesPartsPallets(includeLoadActions: true);
    }
    public MazakSchedulesPartsPallets LoadSchedulesPartsPallets(bool includeLoadActions)
    {
      var data = WithReadDBConnection(conn => {
        var trans = conn.BeginTransaction();
        try {
          var ret = LoadSchedulesPartsPallets(conn, trans);
          trans.Commit();
          return ret;
        } catch {
          trans.Rollback();
          throw;
        }
      });

      if (includeLoadActions) {
        data.LoadActions = _loadOper.CurrentLoadActions();
      }
      return data;
    }

    public MazakAllData LoadAllData()
    {
      return LoadAllData(includeLoadActions: true);
    }

    public MazakAllData LoadAllData(bool includeLoadActions)
    {
      var data = WithReadDBConnection(conn => {
        var trans = conn.BeginTransaction();
        try {
          var schs = LoadSchedulesPartsPallets(conn, trans);
          var ret = new MazakAllData() {
            Schedules = schs.Schedules,
            Parts = schs.Parts,
            Pallets = schs.Pallets,
            PalletSubStatuses = schs.PalletSubStatuses,
            PalletPositions = schs.PalletPositions,
            MainPrograms = schs.MainPrograms,
            Fixtures = conn.Query<MazakFixtureRow>(_fixtureSelect, transaction: trans)
          };

          trans.Commit();
          return ret;
        } catch {
          trans.Rollback();
          throw;
        }
      });

      if (includeLoadActions) {
        data.LoadActions = _loadOper.CurrentLoadActions();
      }
      return data;
    }
  }
}
