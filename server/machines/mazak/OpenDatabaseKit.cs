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
    protected string _connectionStr;

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
      throw new Exception("Mazak database is locked and can not be accessed");
#else
      throw new Exception("Mazak Web and VerE are not supported on .NET Core");
#endif
    }

    protected IDbConnection CreateConnection()
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

  }

	public class OpenDatabaseKitTransactionDB
		: OpenDatabaseKitDB, IWriteData
	{
    private static Serilog.ILogger Log = Serilog.Log.ForContext<OpenDatabaseKitTransactionDB>();

    public OpenDatabaseKitTransactionDB(string dbConnStr, MazakDbType ty)
			: base(dbConnStr, ty)
    {
      if (MazakType == MazakDbType.MazakWeb || MazakType == MazakDbType.MazakVersionE)
      {
        _connectionStr = "Provider=Microsoft.Jet.OLEDB.4.0;Password=\"\";" +
            "User ID=Admin;" +
            "Data Source=" + System.IO.Path.Combine(dbConnStr, "FCNETUSER1.mdb") + ";" +
            "Mode=Share Deny None;";
      } else {
        _connectionStr = dbConnStr + ";Database=FCNETUSER01";
      }
    }

    private const int WaitCount = 5;

    public void Save(MazakWriteData data, string prefix, System.Collections.Generic.IList<string> log)
    {
      CheckReadyForConnect();

      int checkInterval = data.Schedules.Count() + data.Pallets.Count() + data.Parts.Count() + data.Fixtures.Count();

      Log.Debug("Writing {@data} to transaction db", data);

      try {
        using (var conn = CreateConnection())
        {
          var trans = conn.BeginTransaction(IsolationLevel.ReadCommitted);
          try {

            ClearTransactionDatabase(conn, trans);
            SaveData(data, conn, trans);
            trans.Commit();
          } catch {
            trans.Rollback();
            throw;
          }

          int i = 0;
          for (i = 0; i <= WaitCount; i++)
          {
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(checkInterval));
            if (CheckTransactionErrors(conn, prefix, log))
            {
              return;
            }
          }
          throw new Exception("Timeout during download: open database kit is not running or responding");
        }
#if USE_OLEDB
			} catch (OleDbException ex) {
				throw new Exception(ex.ToString());
#endif
      } finally {}
    }

    private void SaveData(MazakWriteData data, IDbConnection conn, IDbTransaction trans)
    {
      conn.Execute(
        @"INSERT INTO Fixture_t(
          FixtureName,
          Comment,
          Command,
          TransactionStatus
        ) VALUES (
          @FixtureName,
          @Comment,
          @Command,
          0
        )",
        data.Fixtures,
        transaction: trans
      );

      if (MazakType == MazakDbType.MazakVersionE) {
        // pallet version 1
        conn.Execute(
          @"INSERT INTO Pallet_t(
            PalletNumber,
            Fixture,
            RecordID,
            Angle,
            Command,
            TransactionStatus
          ) VALUES (
            @PalletNumber,
            @Fixture,
            @RecordID,
            @AngleV1,
            @Command,
            0
          )",
          data.Pallets,
          transaction: trans
        );
      } else {
        // pallet version 2
        conn.Execute(
          @"INSERT INTO Pallet_t(
            PalletNumber,
            Fixture,
            RecordID,
            FixtureGroup,
            Command,
            TransactionStatus
          ) VALUES (
            @PalletNumber,
            @Fixture,
            @RecordID,
            @FixtureGroupV2,
            @Command,
            0
          )",
          data.Pallets,
          transaction: trans
        );
      }

      if (MazakType == MazakDbType.MazakSmooth) {
        conn.Execute(
          @"INSERT INTO ScheduleProcess_t(
            ScheduleID,
            ProcessNumber,
            ProcessMaterialQuantity,
            ProcessExecuteQuantity,
            ProcessBadQuantity,
            ProcessMachine,
            FixedMachineFlag,
            FixedMachineNumber,
            ScheduleProcess_1,
            ScheduleProcess_2,
            ScheduleProcess_3,
            ScheduleProcess_4,
            ScheduleProcess_5
          ) VALUES (
            @MazakScheduleRowId,
            @ProcessNumber,
            @ProcessMaterialQuantity,
            @ProcessExecuteQuantity,
            @ProcessBadQuantity,
            @ProcessMachine,
            @FixedMachineFlag,
            @FixedMachineNumber,
            @ScheduleProcess_1,
            @ScheduleProcess_2,
            @ScheduleProcess_3,
            @ScheduleProcess_4,
            @ScheduleProcess_5
          )",
          data.Schedules.SelectMany(s => s.Processes),
          transaction: trans
        );
        conn.Execute(
          @"INSERT INTO Schedule_t(
            ScheduleID,
            Comment,
            PartName,
            PlanQuantity,
            CompleteQuantity,
            Priority,
            DueDate,
            FixForMachine,
            HoldMode,
            MissingFixture,
            MissingProgram,
            MissingTool,
            MixScheduleID,
            ProcessingPriority,
            Command,
            TransactionStatus,
            Schedule_1,
            Schedule_2,
            Schedule_3,
            Schedule_4,
            Schedule_5,
            Schedule_6,
            StartDate,
            SetNumber,
            SetQuantity,
            SetNumberSets
          ) VALUES (
            @Id,
            @Comment,
            @PartName,
            @PlanQuantity,
            @CompleteQuantity,
            @Priority,
            @DueDate,
            @FixForMachine,
            @HoldMode,
            @MissingFixture,
            @MissingProgram,
            @MissingTool,
            @MixScheduleID,
            @ProcessingPriority,
            @Command,
            0,
            @Schedule_1,
            @Schedule_2,
            @Schedule_3,
            @Schedule_4,
            @Schedule_5,
            @Schedule_6,
            @StartDate,
            @SetNumber,
            @SetQuantity,
            @SetNumberSets
          )",
          data.Schedules,
          transaction: trans
        );
        conn.Execute(
          @"INSERT INTO PartProcess_t(
            PartName,
            ProcessNumber,
            FixQuantity,
            ContinueCut,
            CutMc,
            FixLDS,
            FixPhoto,
            Fixture,
            MainProgram,
            RemoveLDS,
            RemovePhoto,
            WashType,
            PartProcess_1,
            PartProcess_2,
            PartProcess_3,
            PartProcess_4,
            FixTime,
            RemoveTime,
            CreateToolList_RA
          ) VALUES(
            @PartName,
            @ProcessNumber,
            @FixQuantity,
            @ContinueCut,
            @CutMc,
            @FixLDS,
            @FixPhoto,
            @Fixture,
            @MainProgram,
            @RemoveLDS,
            @REmovePhoto,
            @WashType,
            @PartProcess_1,
            @PartProcess_2,
            @PartProcess_3,
            @PartProcess_4,
            @FixTime,
            @RemoveTime,
            @CreateToolList_RA
          )",
          data.Parts.SelectMany(p => p.Processes),
          transaction: trans
        );
        conn.Execute(
          @"INSERT INTO Part_t(
            PartName,
            Comment,
            Price,
            TotalProcess,
            Command,
            TransactionStatus,
            MaterialName,
            Part_1,
            Part_2,
            Part_3,
            Part_4,
            Part_5
          ) VALUES (
            @PartName,
            @Comment,
            @Price,
            @TotalProcess,
            @Command,
            0,
            @MaterialName,
            @Part_1,
            @Part_2,
            @Part_3,
            @Part_4,
            @Part_5
          )",
          data.Parts,
          transaction: trans
        );

      } else {
        // mazak ver e and web
        conn.Execute(
          @"INSERT INTO ScheduleProcess_t(
            ScheduleID,
            ProcessNumber,
            ProcessMaterialQuantity,
            ProcessExecuteQuantity,
            ProcessBadQuantity,
            ProcessMachine
          ) VALUES (
            @MazakScheduleRowId,
            @ProcessNumber,
            @ProcessMaterialQuantity,
            @ProcessExecuteQuantity,
            @ProcessBadQuantity,
            @ProcessMachine)",
          data.Schedules.SelectMany(s => s.Processes),
          transaction: trans
        );
        conn.Execute(
          @"INSERT INTO Schedule_t(
            ScheduleID,
            Comment,
            PartName,
            PlanQuantity,
            CompleteQuantity,
            Priority,
            DueDate,
            FixForMachine,
            HoldMode,
            MissingFixture,
            MissingProgram,
            MissingTool,
            MixScheduleID,
            ProcessingPriority,
            Command,
            TransactionStatus
          ) VALUES (
            @Id,
            @Comment,
            @PartName,
            @PlanQuantity,
            @CompleteQuantity,
            @Priority,
            @DueDate,
            @FixForMachine,
            @HoldMode,
            @MissingFixture,
            @MissingProgram,
            @MissingTool,
            @MixScheduleID,
            @ProcessingPriority,
            @Command,
            0
          )",
          data.Schedules,
          transaction: trans
        );
        conn.Execute(
          @"INSERT INTO PartProcess_t(
            PartName,
            ProcessNumber,
            FixQuantity,
            ContinueCut,
            CutMc,
            FixLDS,
            FixPhoto,
            Fixture,
            MainProgram,
            RemoveLDS,
            RemovePhoto,
            WashType
          ) VALUES(
            @PartName,
            @ProcessNumber,
            @FixQuantity,
            @ContinueCut,
            @CutMc,
            @FixLDS,
            @FixPhoto,
            @Fixture,
            @MainProgram,
            @RemoveLDS,
            @REmovePhoto,
            @WashType
          )",
          data.Parts.SelectMany(p => p.Processes),
          transaction: trans
        );
        conn.Execute(
          @"INSERT INTO Part_t(
            PartName,
            Comment,
            Price,
            TotalProcess,
            Command,
            TransactionStatus
          ) VALUES (
            @PartName,
            @Comment,
            @Price,
            @TotalProcess,
            @Command,
            0
          )",
          data.Parts,
          transaction: trans
        );
      }
    }

    private class CommandStatus
    {
      public MazakWriteCommand Command {get;set;}
      public int TransactionStatus {get;set;}
    }

    private bool CheckTransactionErrors(IDbConnection conn, string prefix, System.Collections.Generic.IList<string> log)
    {
      string[] TransactionTables = {
        "Fixture_t",
        "Pallet_t",
        "Part_t",
        "Schedule_t",
      };
      var trans = conn.BeginTransaction(IsolationLevel.ReadCommitted);
      try
      {
        bool foundUnprocesssedRow = false;
        foreach (var table in TransactionTables) {
          var ret = conn.Query<CommandStatus>("SELECT Command, TransactionStatus FROM " + table);
          foreach (var row in ret) {
            if (row.Command == MazakWriteCommand.Error)
            {
              log.Add(prefix + " Mazak transaction returned error " + row.TransactionStatus.ToString());
            } else {
              foundUnprocesssedRow = true;
            }
          }
        }
        trans.Commit();
        return !foundUnprocesssedRow;
      }
      catch (Exception ex)
      {
        trans.Rollback();
        throw ex;
      }
    }

    private void ClearTransactionDatabase(IDbConnection conn, IDbTransaction trans)
    {
      string[] TransactionTables = {
        "Fixture_t",
        "Pallet_t",
        "Part_t",
        "PartProcess_t",
        "Schedule_t",
        "ScheduleProcess_t"
      };

      using (var cmd = conn.CreateCommand()) {
        cmd.Transaction = trans;
        foreach (string table in TransactionTables)
        {
          cmd.CommandText = "DELETE FROM " + table;
          cmd.ExecuteNonQuery();

        }
      }
    }

	}

	public class OpenDatabaseKitReadDB
	  : OpenDatabaseKitDB, IReadDataAccess
	{
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

      if (MazakType != MazakDbType.MazakSmooth) {
        _scheduleSelect = "SELECT Comment, CompleteQuantity, DueDate, FixForMachine, HoldMode, MissingFixture, MissingProgram, MissingTool, MixScheduleID, PartName, PlanQuantity, Priority, ProcessingPriority, Reserved, ScheduleID As Id, UpdatedFlag FROM Schedule";
        _scheduleProcSelect = "SELECT ProcessBadQuantity, ProcessExecuteQuantity, ProcessMachine, ProcessMaterialQuantity, ProcessNumber, ScheduleID As MazakScheduleRowId, UpdatedFlag " +
          " FROM ScheduleProcess";
      } else {
        _scheduleSelect = "SELECT Comment, CompleteQuantity, DueDate, FixForMachine, HoldMode, MissingFixture, MissingProgram, MissingTool, MixScheduleID, PartName, PlanQuantity, Priority, ProcessingPriority, Reserved, ScheduleID As Id, UpdatedFlag, Schedule_1, Schedule_2, Schedule_3, Schedule_4, Schedule_5, Schedule_6, StartDate, SetNumber, SetQuantity, SetNumberSets FROM Schedule";
        _scheduleProcSelect = "SELECT ProcessBadQuantity, ProcessExecuteQuantity, ProcessMachine, ProcessMaterialQuantity, ProcessNumber, ScheduleID As MazakScheduleRowId, UpdatedFlag, FixedMachineFlag, FixedMachineNumber, ScheduleProcess_1, ScheduleProcess_2, ScheduleProcess_3, ScheduleProcess_4, ScheduleProcess_5 " +
          " FROM ScheduleProcess";
      }

      // normally would use a join to determine FixQuantity as part of the schedule proc row,
      // but the mazak readdb has no indexes and no keys, so everything is a table scan.
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
