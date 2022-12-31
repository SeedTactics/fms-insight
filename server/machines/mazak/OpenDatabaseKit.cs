/* Copyright (c) 2020, John Lenz

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
  public class OpenDatabaseKitTransactionError : Exception
  {
    public OpenDatabaseKitTransactionError(string msg) : base(msg) { }
  }

  public class ErrorModifyingParts : Exception
  {
    // There is a suspected bug in the mazak software when deleting parts.  Sometimes, open database kit
    // successfully deletes a part but still outputs an error 7.  This only happens occasionally, but
    // if we get an error 7 when deleting the part table, check if the part was successfully deleted and
    // if so ignore the error.
    public HashSet<string> PartNames { get; }

    public ErrorModifyingParts(HashSet<string> names)
      : base("Mazak returned error when modifying parts: " + string.Join(",", names))
    {
      PartNames = names;
    }
  }

  public class OpenDatabaseKitDB
  {
    //Global Settings
    public static System.Threading.Mutex MazakTransactionLock = new System.Threading.Mutex();

    protected string ready4ConectPath;
    protected string _connectionStr;

    public MazakDbType MazakType { get; }

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
        }
        catch (OleDbException ex)
        {
          if (!(ex.Message.ToLower().IndexOf("could not use") >= 0))
          {
            if (!(ex.Message.ToLower().IndexOf("try again") >= 0))
            {
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

  public class OpenDatabaseKitTransactionDB : OpenDatabaseKitDB, IWriteData
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<OpenDatabaseKitTransactionDB>();

    public OpenDatabaseKitTransactionDB(string dbConnStr, MazakDbType ty) : base(dbConnStr, ty)
    {
      if (MazakType == MazakDbType.MazakWeb || MazakType == MazakDbType.MazakVersionE)
      {
        _connectionStr =
          "Provider=Microsoft.Jet.OLEDB.4.0;Password=\"\";"
          + "User ID=Admin;"
          + "Data Source="
          + System.IO.Path.Combine(dbConnStr, "FCNETUSER1.mdb")
          + ";"
          + "Mode=Share Deny None;";
      }
      else
      {
        _connectionStr = dbConnStr + ";Database=FCNETUSER01";
      }
    }

    private const int EntriesPerTransaction = 20;

    internal static IEnumerable<MazakWriteData> SplitWriteData(MazakWriteData original)
    {
      // Open Database Kit can lockup if too many commands are sent at once
      return original.Schedules
        .Cast<object>()
        .Concat(original.Parts)
        .Concat(original.Pallets)
        .Concat(original.Fixtures)
        .Concat(original.Programs)
        .Chunk(EntriesPerTransaction)
        .Select(
          chunk =>
            new MazakWriteData()
            {
              Schedules = chunk.OfType<MazakScheduleRow>().ToArray(),
              Parts = chunk.OfType<MazakPartRow>().ToArray(),
              Pallets = chunk.OfType<MazakPalletRow>().ToArray(),
              Fixtures = chunk.OfType<MazakFixtureRow>().ToArray(),
              Programs = chunk.OfType<NewMazakProgram>().ToArray(),
            }
        )
        .ToList();
    }

    public void Save(MazakWriteData data, string prefix)
    {
      foreach (var chunk in SplitWriteData(data))
      {
        SaveChunck(chunk, prefix);
      }
    }

    private void SaveChunck(MazakWriteData data, string prefix)
    {
      CheckReadyForConnect();

      Log.Debug("Writing {@data} to transaction db", data);

      foreach (var prog in data.Programs)
      {
        if (prog.Command == MazakWriteCommand.Add)
        {
          var dir = System.IO.Path.GetDirectoryName(prog.MainProgram);
          System.IO.Directory.CreateDirectory(dir);
          System.IO.File.WriteAllText(prog.MainProgram, prog.ProgramContent);
        }
      }

      try
      {
        using (var conn = CreateConnection())
        {
          using (var trans = conn.BeginTransaction(IsolationLevel.ReadCommitted))
          {
            ClearTransactionDatabase(conn, trans);
            SaveData(data, conn, trans);
            trans.Commit();
          }

          foreach (int waitSecs in new[] { 5, 5, 10, 10, 15, 15, 30, 30 })
          {
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(waitSecs));
            if (CheckTransactionErrors(conn, prefix))
            {
              return;
            }
          }
          throw new Exception("Timeout during download: open database kit is not running or responding");
        }
#if USE_OLEDB
      }
      catch (OleDbException ex)
      {
        throw new Exception(ex.ToString());
#endif
      }
      finally { }
    }

    private void SaveData(MazakWriteData data, IDbConnection conn, IDbTransaction trans)
    {
      conn.Execute(
        @"INSERT INTO Fixture_t(
          Command,
          Comment,
          FixtureName,
          TransactionStatus
        ) VALUES (
          @Command,
          @Comment,
          @FixtureName,
          0
        )",
        data.Fixtures,
        transaction: trans
      );

      if (MazakType == MazakDbType.MazakVersionE)
      {
        // pallet version 1
        conn.Execute(
          @"INSERT INTO Pallet_t(
            Angle,
            Command,
            Fixture,
            PalletNumber,
            RecordID,
            TransactionStatus
          ) VALUES (
            @AngleV1,
            @Command,
            @Fixture,
            @PalletNumber,
            @RecordID,
            0
          )",
          data.Pallets,
          transaction: trans
        );
      }
      else
      {
        // pallet version 2
        conn.Execute(
          @"INSERT INTO Pallet_t(
            Command,
            Fixture,
            FixtureGroup,
            PalletNumber,
            RecordID,
            TransactionStatus
          ) VALUES (
            @Command,
            @Fixture,
            @FixtureGroupV2,
            @PalletNumber,
            @RecordID,
            0
          )",
          data.Pallets,
          transaction: trans
        );
      }

      if (MazakType == MazakDbType.MazakSmooth)
      {
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
            Part_5,
            CheckCount,
            ProductCount
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
            @Part_5,
            @CheckCount,
            @ProductCount
          )",
          data.Parts,
          transaction: trans
        );
      }
      else
      {
        // mazak ver e and web
        conn.Execute(
          @"INSERT INTO ScheduleProcess_t(
            ScheduleID,
            ProcessBadQuantity,
            ProcessExecuteQuantity,
            ProcessMachine,
            ProcessMaterialQuantity,
            ProcessNumber
          ) VALUES (
            @MazakScheduleRowId,
            @ProcessBadQuantity,
            @ProcessExecuteQuantity,
            @ProcessMachine,
            @ProcessMaterialQuantity,
            @ProcessNumber
          )",
          data.Schedules.SelectMany(s => s.Processes),
          transaction: trans
        );
        conn.Execute(
          @"INSERT INTO Schedule_t(
            Command,
            Comment,
            CompleteQuantity,
            DueDate,
            FixForMachine,
            HoldMode,
            ScheduleID,
            MissingFixture,
            MissingProgram,
            MissingTool,
            MixScheduleID,
            PartName,
            PlanQuantity,
            Priority,
            ProcessingPriority,
            TransactionStatus
          ) VALUES (
            @Command,
            @Comment,
            @CompleteQuantity,
            @DueDate,
            @FixForMachine,
            @HoldMode,
            @Id,
            @MissingFixture,
            @MissingProgram,
            @MissingTool,
            @MixScheduleID,
            @PartName,
            @PlanQuantity,
            @Priority,
            @ProcessingPriority,
            0
          )",
          data.Schedules,
          transaction: trans
        );
        conn.Execute(
          @"INSERT INTO PartProcess_t(
            ContinueCut,
            CutMc,
            FixLDS,
            FixPhoto,
            FixQuantity,
            Fixture,
            MainProgram,
            PartName,
            ProcessNumber,
            RemoveLDS,
            RemovePhoto,
            WashType
          ) VALUES(
            @ContinueCut,
            @CutMc,
            @FixLDS,
            @FixPhoto,
            @FixQuantity,
            @Fixture,
            @MainProgram,
            @PartName,
            @ProcessNumber,
            @RemoveLDS,
            @RemovePhoto,
            @WashType
          )",
          data.Parts.SelectMany(p => p.Processes),
          transaction: trans
        );
        conn.Execute(
          @"INSERT INTO Part_t(
            Command,
            Comment,
            PartName,
            Price,
            TotalProcess,
            TransactionStatus
          ) VALUES (
            @Command,
            @Comment,
            @PartName,
            @Price,
            @TotalProcess,
            0
          )",
          data.Parts,
          transaction: trans
        );
      }

      if (MazakType == MazakDbType.MazakSmooth)
      {
        conn.Execute(
          @"INSERT INTO MainProgram_t(
            Command,
            Comment,
            Count,
            CreateToolList_PMC,
            CutTime,
            CutTimeFlag,
            FileStatus,
            MainProgram,
            MainProgram_1,
            MainProgram_2,
            MainProgram_3,
            MainProgram_4,
            MainProgram_5,
            SingleBlockStop,
            TransactionStatus
          ) VALUES (
            @Command,
            @Comment,
            0,
            0,
            0,
            1,
            0,
            @MainProgram,
            0,
            0,
            0,
            0,
            0,
            0,
            0
          )",
          data.Programs,
          transaction: trans
        );
      }
      else
      {
        throw new Exception("Downloading programs only supported on Smooth-PMC");
      }
    }

    private class CommandStatus
    {
      public MazakWriteCommand? Command { get; set; }
      public int TransactionStatus { get; set; }
    }

    private class PartCommandStatus : CommandStatus
    {
      public string PartName { get; set; }
    }

    private bool CheckTransactionErrors(IDbConnection conn, string prefix)
    {
      // Once Mazak processes a row, the row in the table is blanked.  If an error occurs, instead
      // the Command is changed to 9 and an error code is put in the TransactionStatus column.
      bool foundUnprocesssedRow = false;
      var log = new List<string>();
      var partEditErrors = new HashSet<string>();
      using (var trans = conn.BeginTransaction(IsolationLevel.ReadCommitted))
      {
        foreach (var table in new[] { "Fixture_t", "Pallet_t", "Schedule_t" })
        {
          var ret = conn.Query<CommandStatus>(
            "SELECT Command, TransactionStatus FROM " + table,
            transaction: trans
          );
          int idx = -1;
          foreach (var row in ret)
          {
            idx += 1;
            if (!row.Command.HasValue)
              continue;
            if (row.Command.Value == MazakWriteCommand.Error)
            {
              log.Add(
                prefix
                  + " Mazak transaction on table "
                  + table
                  + " at index "
                  + idx.ToString()
                  + " returned error "
                  + row.TransactionStatus.ToString()
              );
            }
            else
            {
              foundUnprocesssedRow = true;
              Log.Debug("Unprocessed row for table {table} at index {idx} with {@row}", table, idx, row);
            }
          }
        }

        // now the part, with custom checking for error 7
        var parts = conn.Query<PartCommandStatus>(
          "SELECT Command, TransactionStatus, PartName FROM Part_t",
          transaction: trans
        );
        var partIdx = -1;
        foreach (var row in parts)
        {
          partIdx += 1;
          if (!row.Command.HasValue)
            continue;
          if (
            row.Command.Value == MazakWriteCommand.Error
            && row.TransactionStatus == 7
            && !string.IsNullOrEmpty(row.PartName)
          )
          {
            Log.Debug(
              prefix + " mazak transaction on table Part_t at index {idx} returned error 7 for part {part}",
              partIdx,
              row.PartName
            );
            partEditErrors.Add(row.PartName);
          }
          else if (row.Command.Value == MazakWriteCommand.Error)
          {
            log.Add(
              prefix
                + " Mazak transaction on table Part_t at index "
                + partIdx.ToString()
                + " returned error "
                + row.TransactionStatus.ToString()
            );
          }
          else
          {
            foundUnprocesssedRow = true;
            Log.Debug("Unprocessed Part_t row for part {part} with row {@row}", row.PartName, row);
          }
        }
        trans.Commit();
      }

      if (log.Any())
      {
        Log.Error("Error communicating with Open Database kit {@errs}", log);
        throw new OpenDatabaseKitTransactionError(string.Join(Environment.NewLine, log));
      }

      if (foundUnprocesssedRow)
      {
        return false;
      }
      else if (partEditErrors.Count > 0)
      {
        throw new ErrorModifyingParts(partEditErrors);
      }
      else
      {
        return true;
      }
    }

    private void ClearTransactionDatabase(IDbConnection conn, IDbTransaction trans)
    {
      string[] TransactionTables =
      {
        "Fixture_t",
        "Pallet_t",
        "Part_t",
        "PartProcess_t",
        "Schedule_t",
        "ScheduleProcess_t"
      };

      using (var cmd = conn.CreateCommand())
      {
        cmd.Transaction = trans;
        foreach (string table in TransactionTables)
        {
          cmd.CommandText = "DELETE FROM " + table;
          cmd.ExecuteNonQuery();
        }
      }
    }
  }

  public class OpenDatabaseKitReadDB : OpenDatabaseKitDB, IReadDataAccess
  {
    private string _fixtureSelect;
    private string _palletSelect;
    private string _partSelect;
    private string _partProcSelect;
    private string _scheduleSelect;
    private string _scheduleProcSelect;
    private string _palSubStatusSelect;
    private string _palPositionSelect;
    private string _mainProgSelect;
    private string _alarmSelect;
    private string _toolSelect;

    private ICurrentLoadActions _loadOper;

    public OpenDatabaseKitReadDB(string dbConnStr, MazakDbType ty, ICurrentLoadActions loadOper)
      : base(dbConnStr, ty)
    {
      _loadOper = loadOper;
      if (MazakType == MazakDbType.MazakWeb || MazakType == MazakDbType.MazakVersionE)
      {
        _connectionStr =
          "Provider=Microsoft.Jet.OLEDB.4.0;Password=\"\";User ID=Admin;"
          + "Data Source="
          + System.IO.Path.Combine(dbConnStr, "FCREADDAT01.mdb")
          + ";"
          + "Mode=Share Deny Write;";
      }
      else
      {
        _connectionStr = dbConnStr + ";Database=FCREADDAT01";
      }

      _fixtureSelect = "SELECT FixtureName, Comment FROM Fixture";

      if (MazakType != MazakDbType.MazakVersionE)
      {
        _palletSelect = "SELECT PalletNumber, FixtureGroup AS FixtureGroupV2, Fixture, RecordID FROM Pallet";
      }
      else
      {
        _palletSelect = "SELECT PalletNumber, Angle AS AngleV1, Fixture, RecordID FROM Pallet";
      }

      if (MazakType != MazakDbType.MazakSmooth)
      {
        _partSelect = "SELECT Id, PartName, Comment, Price, TotalProcess FROM Part";
        _partSelect =
          @"SELECT
            PartName,
            Comment,
            Price,
            TotalProcess
          FROM Part";
        _partProcSelect =
          "SELECT ContinueCut, CutMc, FixLDS, FixPhoto, FixQuantity, Fixture, MainProgram, PartName, ProcessNumber, RemoveLDS, RemovePhoto, WashType FROM PartProcess";
        _partProcSelect =
          @"SELECT
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
          FROM PartProcess";

        _scheduleSelect =
          @"SELECT
            ScheduleID As Id,
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
            ProcessingPriority
          FROM Schedule";
        _scheduleProcSelect =
          @"SELECT
            ScheduleID As MazakScheduleRowId,
            ProcessNumber,
            ProcessMaterialQuantity,
            ProcessExecuteQuantity,
            ProcessBadQuantity,
            ProcessMachine
          FROM ScheduleProcess";
      }
      else
      {
        _partSelect =
          @"SELECT
            PartName,
            Comment,
            Price,
            TotalProcess,
            MaterialName,
            Part_1,
            Part_2,
            Part_3,
            Part_4,
            Part_5,
            CheckCount,
            ProductCount
          FROM Part";
        _partProcSelect =
          @"SELECT
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
          FROM PartProcess";

        _scheduleSelect =
          @"SELECT
            ScheduleID As Id,
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
          FROM Schedule";
        _scheduleProcSelect =
          @"SELECT
            ScheduleID As MazakScheduleRowId,
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
          FROM ScheduleProcess";
      }

      _palSubStatusSelect =
        "SELECT FixQuantity, FixtureName, PalletNumber, PartName, PartProcessNumber, ScheduleID FROM PalletSubStatus";
      _palPositionSelect = "SELECT PalletNumber, PalletPosition FROM PalletPosition WHERE PalletNumber > 0";
      _mainProgSelect = "SELECT MainProgram, Comment FROM MainProgram";
      _alarmSelect = "SELECT AlarmNumber, AlarmMessage FROM Alarm";

      _toolSelect =
        @"SELECT
          MachineNumber,
          PocketNumber,
          PocketType,
          ToolID,
          ToolNameCode,
          ToolNumber,
          IsLengthMeasured,
          IsToolDataValid,
          IsToolLifeOver,
          IsToolLifeBroken,
          IsDataAccessed,
          IsBeingUsed,
          UsableNow,
          WillBrokenInform,
          InterferenceType,
          LifeSpan,
          LifeUsed,
          NominalDiameter,
          SuffixCode,
          ToolDiameter,
          GroupNo
        FROM ToolPocket
      ";
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
      public string PartName { get; set; }
      public int ProcessNumber { get; set; }
      public int FixQuantity { get; set; }
    }

    private IEnumerable<MazakPartRow> LoadParts(
      IDbConnection conn,
      IDbTransaction trans,
      out IEnumerable<PartProcessFixQty> fixQtys
    )
    {
      var parts = conn.Query<MazakPartRow>(_partSelect, transaction: trans);
      var partsByName = parts.ToDictionary(p => p.PartName, p => p);
      var procs = conn.Query<MazakPartProcessRow>(_partProcSelect, transaction: trans);
      var fixQty = new List<PartProcessFixQty>();
      fixQtys = fixQty;
      foreach (var proc in procs)
      {
        if (partsByName.ContainsKey(proc.PartName))
        {
          var part = partsByName[proc.PartName];
          part.Processes.Add(proc);
        }
        fixQty.Add(
          new PartProcessFixQty()
          {
            PartName = proc.PartName,
            ProcessNumber = proc.ProcessNumber,
            FixQuantity = proc.FixQuantity
          }
        );
      }

      return parts;
    }

    private IEnumerable<MazakScheduleRow> LoadSchedules(
      IDbConnection conn,
      IDbTransaction trans,
      IEnumerable<PartProcessFixQty> fixQtys
    )
    {
      var schs = conn.Query<MazakScheduleRow>(_scheduleSelect, transaction: trans);
      var schDict = schs.ToDictionary(s => s.Id, s => s);

      var procs = conn.Query<MazakScheduleProcessRow>(_scheduleProcSelect, transaction: trans);
      foreach (var proc in procs)
      {
        if (schDict.ContainsKey(proc.MazakScheduleRowId))
        {
          var schRow = schDict[proc.MazakScheduleRowId];

          var fixQty = 1;
          foreach (var p in fixQtys)
          {
            if (p.PartName == schRow.PartName && p.ProcessNumber == proc.ProcessNumber)
            {
              fixQty = p.FixQuantity;
            }
          }

          schRow.Processes.Add(proc with { FixQuantity = fixQty });
        }
      }
      return schs;
    }

    public MazakCurrentStatusAndTools LoadStatusAndTools()
    {
      return WithReadDBConnection(conn =>
      {
        using (var trans = conn.BeginTransaction())
        {
          var parts = LoadParts(conn, trans, out var fixQty);
          var ret = new MazakCurrentStatusAndTools()
          {
            Schedules = LoadSchedules(conn, trans, fixQty),
            LoadActions = _loadOper.CurrentLoadActions(),
            PalletSubStatuses = conn.Query<MazakPalletSubStatusRow>(_palSubStatusSelect, transaction: trans),
            PalletPositions = conn.Query<MazakPalletPositionRow>(_palPositionSelect, transaction: trans),
            Alarms = conn.Query<MazakAlarmRow>(_alarmSelect, transaction: trans),
            Parts = parts,
            Tools =
              MazakType == MazakDbType.MazakSmooth
                ? conn.Query<ToolPocketRow>(_toolSelect, transaction: trans)
                : Enumerable.Empty<ToolPocketRow>()
          };
          trans.Commit();
          return ret;
        }
      });
    }

    private MazakAllData LoadAllData(IDbConnection conn, IDbTransaction trans)
    {
      var parts = LoadParts(conn, trans, out var fixQty);

      return new MazakAllData()
      {
        Schedules = LoadSchedules(conn, trans, fixQty),
        LoadActions = _loadOper.CurrentLoadActions(),
        PalletSubStatuses = conn.Query<MazakPalletSubStatusRow>(_palSubStatusSelect, transaction: trans),
        PalletPositions = conn.Query<MazakPalletPositionRow>(_palPositionSelect, transaction: trans),
        Alarms = conn.Query<MazakAlarmRow>(_alarmSelect, transaction: trans),
        Parts = parts,
        Pallets = conn.Query<MazakPalletRow>(_palletSelect, transaction: trans),
        MainPrograms = conn.Query<MazakProgramRow>(_mainProgSelect, transaction: trans),
        Fixtures = conn.Query<MazakFixtureRow>(_fixtureSelect, transaction: trans),
      };
    }

    public IEnumerable<MazakProgramRow> LoadPrograms()
    {
      return WithReadDBConnection(conn => conn.Query<MazakProgramRow>(_mainProgSelect).ToList());
    }

    public bool CheckProgramExists(string mainProgram)
    {
      return WithReadDBConnection(conn =>
      {
        var cnt = conn.ExecuteScalar<int>(
          "SELECT COUNT(*) FROM MainProgram WHERE MainProgram = @m",
          new { m = mainProgram }
        );
        return cnt > 0;
      });
    }

    public IEnumerable<ToolPocketRow> LoadTools()
    {
      if (MazakType == MazakDbType.MazakSmooth)
      {
        return WithReadDBConnection(conn =>
        {
          return conn.Query<ToolPocketRow>(_toolSelect).ToList();
        });
      }
      else
      {
        return Enumerable.Empty<ToolPocketRow>();
      }
    }

    public bool CheckPartExists(string partName)
    {
      return WithReadDBConnection(conn =>
      {
        var cnt = conn.ExecuteScalar<int>(
          "SELECT COUNT(*) FROM Part WHERE PartName = @p",
          new { p = partName }
        );
        return cnt > 0;
      });
    }

    public MazakAllData LoadAllData()
    {
      var data = WithReadDBConnection(conn =>
      {
        using (var trans = conn.BeginTransaction())
        {
          var ret = LoadAllData(conn, trans);
          trans.Commit();
          return ret;
        }
      });

      return data;
    }
  }

  public static class ChunkList
  {
    public static IEnumerable<List<T>> Chunk<T>(this IEnumerable<T> enumerable, int chunkSize)
    {
      using (var enumerator = enumerable.GetEnumerator())
      {
        List<T> list = null;
        while (enumerator.MoveNext())
        {
          if (list == null)
          {
            list = new List<T> { enumerator.Current };
          }
          else if (list.Count < chunkSize)
          {
            list.Add(enumerator.Current);
          }
          else
          {
            yield return list;
            list = new List<T> { enumerator.Current };
          }
        }

        if (list?.Count > 0)
        {
          yield return list;
        }
      }
    }
  }
}
