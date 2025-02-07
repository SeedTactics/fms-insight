/* Copyright (c) 2024, John Lenz

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
using System.Data.OleDb;
using System.Linq;
#if NET35
using System.Data.SqlClient;
#else
// On net3.5, we have a small reflection-based implementation
using Dapper;
using Microsoft.Data.SqlClient;
#endif

namespace MazakMachineInterface
{
  public class OpenDatabaseKitTransactionError : Exception
  {
    public OpenDatabaseKitTransactionError(string msg)
      : base(msg) { }
  }

  public sealed class OpenDatabaseKitDB : IMazakDB, IDisposable
  {
    private static Serilog.ILogger Log = Serilog.Log.ForContext<OpenDatabaseKitDB>();
    public static System.Threading.Mutex MazakTransactionLock = new System.Threading.Mutex();

    private readonly string ready4ConectPath;
    private readonly string _readConnStr;
    private readonly string _writeConnStr;
    private readonly ICurrentLoadActions _loadOper;
    private readonly MazakConfig _cfg;
    private System.IO.FileSystemWatcher logWatcher;
    private MazakDbType _dbType => _cfg.DBType;

    private void CheckReadyForConnect()
    {
      if (_dbType != MazakDbType.MazakVersionE)
        return;
      if (!System.IO.File.Exists(ready4ConectPath))
      {
        throw new Exception("Open database kit is not running");
      }
    }

    public OpenDatabaseKitDB(MazakConfig cfg, ICurrentLoadActions loadOper)
    {
      _cfg = cfg;
      _loadOper = loadOper;

      if (_dbType != MazakDbType.MazakSmooth)
      {
        ready4ConectPath = System.IO.Path.Combine(cfg.OleDbDatabasePath, "ready4Conect.mdb");
      }

      if (_dbType == MazakDbType.MazakWeb || _dbType == MazakDbType.MazakVersionE)
      {
        _readConnStr =
          cfg.SQLConnectionString
          + ";Data Source="
          + System.IO.Path.Combine(cfg.OleDbDatabasePath, "FCREADDAT01.mdb")
          + ";";
        _writeConnStr =
          cfg.SQLConnectionString
          + ";Data Source="
          + System.IO.Path.Combine(cfg.OleDbDatabasePath, "FCNETUSER1.mdb")
          + ";";
      }
      else
      {
        _readConnStr = cfg.SQLConnectionString + ";Database=FCREADDAT01";
        _writeConnStr = cfg.SQLConnectionString + ";Database=FCNETUSER01";
      }

      if (_dbType == MazakDbType.MazakVersionE)
      {
        if (System.IO.Directory.Exists(cfg.LoadCSVPath))
        {
          logWatcher = new System.IO.FileSystemWatcher(cfg.LoadCSVPath) { Filter = "*.csv" };
          logWatcher.Created += RaiseNewLog;
          logWatcher.Changed += RaiseNewLog;
        }
        else
        {
          Log.Error("Load CSV Directory does not exist.  Set the directory in the config.ini file.");
        }
      }
      else
      {
        if (System.IO.Directory.Exists(cfg.LogCSVPath))
        {
          logWatcher = new System.IO.FileSystemWatcher(cfg.LogCSVPath) { Filter = "*.csv" };
          logWatcher.Created += RaiseNewLog;
        }
        else
        {
          Log.Error("Log CSV Directory does not exist.  Set the directory in the config.ini file.");
        }
      }
      if (logWatcher != null)
        logWatcher.EnableRaisingEvents = true;

      InitReadSQLStatements();
    }

    public void Dispose()
    {
      if (logWatcher != null)
      {
        logWatcher.EnableRaisingEvents = false;
        logWatcher.Created -= RaiseNewLog;
        logWatcher.Changed -= RaiseNewLog;
        logWatcher.Dispose();
        logWatcher = null;
      }
    }

#pragma warning disable CA1416 // Validate platform compatibility
    private static IDbConnection OpenOleDb(string connStr)
    {
      int attempts = 0;

      var conn = new OleDbConnection(connStr);
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
              conn.Close();
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
              conn.Close();
              throw;
            }
          }
        }

        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));

        attempts += 1;
      }

      conn.Close();
      throw new Exception("Mazak database is locked and can not be accessed");
    }

    private IDbConnection CreateReadConnection()
    {
      if (_dbType == MazakDbType.MazakWeb || _dbType == MazakDbType.MazakVersionE)
      {
        return OpenOleDb(_readConnStr);
      }
      else
      {
        var conn = new SqlConnection(_readConnStr);
        conn.Open();
        return conn;
      }
    }

    private IDbConnection CreateWriteConnection()
    {
      if (_dbType == MazakDbType.MazakWeb || _dbType == MazakDbType.MazakVersionE)
      {
        return OpenOleDb(_writeConnStr);
      }
      else
      {
        var conn = new SqlConnection(_writeConnStr);
        conn.Open();
        return conn;
      }
    }

    #region Writing
    private const int EntriesPerTransaction = 20;

    public static IEnumerable<MazakWriteData> SplitWriteData(MazakWriteData original)
    {
      // Open Database Kit can lockup if too many commands are sent at once
      return original
        .Schedules.Cast<object>()
        .Concat(original.Parts.Cast<object>())
        .Concat(original.Pallets.Cast<object>())
        .Concat(original.Fixtures.Cast<object>())
        .Concat(original.Programs.Cast<object>())
        .Chunk(EntriesPerTransaction)
        .Select(chunk => new MazakWriteData()
        {
          Prefix = original.Prefix,
          Schedules = chunk.OfType<MazakScheduleRow>().ToArray(),
          Parts = chunk.OfType<MazakPartRow>().ToArray(),
          Pallets = chunk.OfType<MazakPalletRow>().ToArray(),
          Fixtures = chunk.OfType<MazakFixtureRow>().ToArray(),
          Programs = chunk.OfType<NewMazakProgram>().ToArray(),
        })
        .ToList();
    }

    public void Save(MazakWriteData data)
    {
      foreach (var chunk in SplitWriteData(data))
      {
        SaveChunck(chunk);
      }
    }

    private void SaveChunck(MazakWriteData data)
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
        using (var conn = CreateWriteConnection())
        {
          using (var trans = conn.BeginTransaction(IsolationLevel.ReadCommitted))
          {
            ClearTransactionDatabase(conn, trans);
            SaveData(data, conn, trans);
            trans.Commit();
          }

          // check for errors
          foreach (int waitSecs in new[] { 5, 5, 10, 10, 15, 15, 30 })
          {
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(waitSecs));
            if (CheckTransactionErrors(conn, data))
            {
              goto noErrors;
            }
          }
          throw new Exception("Timeout during download: open database kit is not running or responding");

          noErrors:
          // wait for any new parts and schedules
          var newParts = data
            .Parts.Where(p => p.Command == MazakWriteCommand.Add)
            .Select(p => p.PartName)
            .ToList();
          var newSchIds = data
            .Schedules.Where(s => s.Command == MazakWriteCommand.Add)
            .Select(s => s.Id)
            .ToList();

          if (newParts.Count > 0 || newSchIds.Count > 0)
          {
            foreach (var waitSecs in new[] { 0.5, 1, 1, 3, 3, 5 })
            {
              System.Threading.Thread.Sleep(TimeSpan.FromSeconds(waitSecs));
              if (CheckPartsExist(newParts) && CheckSchedulesExist(newSchIds))
              {
                goto partsAndSchsExist;
              }
            }

            Log.Error("Timeout waiting for new parts and schedules to appear in database");
            throw new Exception("Timeout waiting for new parts and schedules to appear in database");
          }

          partsAndSchsExist:
          ;
        }
      }
      catch (OleDbException ex)
      {
        throw new Exception(ex.ToString());
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

      if (_dbType == MazakDbType.MazakVersionE)
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

      if (_dbType == MazakDbType.MazakSmooth)
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
            @RemovePhoto,
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

      if (_dbType == MazakDbType.MazakSmooth)
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
      else if (data.Programs.Any())
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

    private bool CheckTransactionErrors(IDbConnection conn, MazakWriteData writeData)
    {
      // Once Mazak processes a row, the row in the table is blanked.  If an error occurs, instead
      // the Command is changed to 9 and an error code is put in the TransactionStatus column.

      // There is a suspected bug in the mazak software when deleting parts.  Sometimes, open database kit
      // successfully deletes a part but still outputs an error 7.  This only happens occasionally, but
      // if we get an error 7 when deleting the part table, check if the part was successfully deleted and
      // if so ignore the error.
      bool foundUnprocesssedRow = false;
      var log = new List<string>();

      var partsToDelete = new HashSet<string>(
        writeData.Parts.Where(p => p.Command == MazakWriteCommand.Delete).Select(p => p.PartName)
      );
      var partDelErrors = new HashSet<string>();

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
                writeData.Prefix
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
            && partsToDelete.Contains(row.PartName)
          )
          {
            Log.Debug(
              writeData.Prefix
                + " mazak transaction on table Part_t at index {idx} returned error 7 for part {part}",
              partIdx,
              row.PartName
            );
            partDelErrors.Add(row.PartName);
          }
          else if (row.Command.Value == MazakWriteCommand.Error)
          {
            log.Add(
              writeData.Prefix
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
        var errs = string.Join(Environment.NewLine, log.ToArray());
        Log.Error("Error communicating with Open Database kit " + errs);
        throw new OpenDatabaseKitTransactionError(string.Join(Environment.NewLine, log.ToArray()));
      }

      if (foundUnprocesssedRow)
      {
        return false;
      }
      else if (partDelErrors.Count > 0)
      {
        foreach (var part in partDelErrors)
        {
          if (CheckPartsExist([part]))
          {
            throw new Exception("Mazak returned an error when attempting to delete part " + part);
          }
        }
        // if they all no longer exists, ignore the error
        return true;
      }
      else
      {
        return true;
      }
    }

    private static void ClearTransactionDatabase(IDbConnection conn, IDbTransaction trans)
    {
      string[] TransactionTables =
      {
        "Fixture_t",
        "Pallet_t",
        "Part_t",
        "PartProcess_t",
        "Schedule_t",
        "ScheduleProcess_t",
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
    #endregion

    #region Reading
    private string _fixtureSelect;
    private string _palletSelect;
    private string _partSelect;
    private string _partProcSelect;
    private string _scheduleSelect;
    private string _scheduleProcSelect;
    private string _palStatusSelect;
    private string _palSubStatusSelect;
    private string _palPositionSelect;
    private string _mainProgSelect;
    private string _alarmSelect;
    private string _toolSelect;

    private void InitReadSQLStatements()
    {
      _fixtureSelect = "SELECT FixtureName, Comment FROM Fixture";

      if (_dbType != MazakDbType.MazakVersionE)
      {
        _palletSelect = "SELECT PalletNumber, FixtureGroup AS FixtureGroupV2, Fixture, RecordID FROM Pallet";
      }
      else
      {
        _palletSelect = "SELECT PalletNumber, Angle AS AngleV1, Fixture, RecordID FROM Pallet";
      }

      if (_dbType != MazakDbType.MazakSmooth)
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

      _palStatusSelect = "SELECT PalletNumber, IsOnHold FROM PalletStatus";
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
      using (var conn = CreateReadConnection())
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
            FixQuantity = proc.FixQuantity,
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

          schRow.Processes.Add(
            new MazakScheduleProcessRow()
            {
              FixQuantity = fixQty,

              //all the rest the same
              MazakScheduleRowId = proc.MazakScheduleRowId,
              ProcessNumber = proc.ProcessNumber,
              ProcessMaterialQuantity = proc.ProcessMaterialQuantity,
              ProcessExecuteQuantity = proc.ProcessExecuteQuantity,
              ProcessBadQuantity = proc.ProcessBadQuantity,
              ProcessMachine = proc.ProcessMachine,
              FixedMachineFlag = proc.FixedMachineFlag,
              FixedMachineNumber = proc.FixedMachineNumber,
              ScheduleProcess_1 = proc.ScheduleProcess_1,
              ScheduleProcess_2 = proc.ScheduleProcess_2,
              ScheduleProcess_3 = proc.ScheduleProcess_3,
              ScheduleProcess_4 = proc.ScheduleProcess_4,
              ScheduleProcess_5 = proc.ScheduleProcess_5,
            }
          );
        }
      }
      return schs;
    }

    private MazakAllData LoadAllData(IDbConnection conn, IDbTransaction trans)
    {
      var parts = LoadParts(conn, trans, out var fixQty);

      return new MazakAllData()
      {
        Schedules = LoadSchedules(conn, trans, fixQty),
        LoadActions = _loadOper.CurrentLoadActions(),
        PalletStatuses = conn.Query<MazakPalletStatusRow>(_palStatusSelect, transaction: trans),
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
      return WithReadDBConnection(conn =>
      {
        using var trans = conn.BeginTransaction();
        return conn.Query<MazakProgramRow>(_mainProgSelect, transaction: trans).ToList();
      });
    }

    public IEnumerable<ToolPocketRow> LoadTools()
    {
      if (_dbType == MazakDbType.MazakSmooth)
      {
        return WithReadDBConnection(conn =>
        {
          using var trans = conn.BeginTransaction();
          return conn.Query<ToolPocketRow>(_toolSelect, transaction: trans).ToList();
        });
      }
      else
      {
        return Enumerable.Empty<ToolPocketRow>();
      }
    }

    private bool CheckPartsExist(IList<string> parts)
    {
      if (parts.Count == 0)
        return true;

      return WithReadDBConnection(conn =>
      {
        using var trans = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;
        cmd.CommandText = "SELECT COUNT(*) FROM Part WHERE PartName = @p";
        var param = cmd.CreateParameter();
        param.ParameterName = "@p";
        param.DbType = DbType.String;
        cmd.Parameters.Add(param);

        foreach (var p in parts)
        {
          param.Value = p;
          var ret = cmd.ExecuteScalar();
          if (ret == null && ret == DBNull.Value || Convert.ToInt64(ret) == 0)
          {
            return false;
          }
        }

        return true;
      });
    }

    private bool CheckSchedulesExist(IList<int> scheduleIds)
    {
      if (scheduleIds.Count == 0)
        return true;

      return WithReadDBConnection(conn =>
      {
        using var trans = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;

        cmd.CommandText = "SELECT COUNT(*) FROM Schedule WHERE ScheduleID = @s";
        var param = cmd.CreateParameter();
        param.ParameterName = "@s";
        param.DbType = DbType.Int32;
        cmd.Parameters.Add(param);

        foreach (var s in scheduleIds)
        {
          param.Value = s;
          var ret = cmd.ExecuteScalar();
          if (ret == null && ret == DBNull.Value || Convert.ToInt64(ret) == 0)
          {
            return false;
          }
        }

        return true;
      });
    }

    public MazakAllData LoadAllData()
    {
      var data = WithReadDBConnection(conn =>
      {
        using var trans = conn.BeginTransaction();
        var ret = LoadAllData(conn, trans);
        return ret;
      });

      return data;
    }

    public MazakAllDataAndLogs LoadAllDataAndLogs(string maxLogID)
    {
      return WithReadDBConnection(conn =>
      {
        using var trans = conn.BeginTransaction();
        var data = LoadAllData(conn, trans);

        IList<LogEntry> logs;

        if (_dbType == MazakDbType.MazakVersionE)
        {
          logs = LogDataVerE.LoadLog(maxLogID, conn, trans);
        }
        else
        {
          trans.Rollback();
          logs = LogCSVParsing.LoadLog(maxLogID, _cfg.LogCSVPath);
        }

        return new MazakAllDataAndLogs()
        {
          Schedules = data.Schedules,
          LoadActions = data.LoadActions,
          PalletStatuses = data.PalletStatuses,
          PalletSubStatuses = data.PalletSubStatuses,
          PalletPositions = data.PalletPositions,
          Alarms = data.Alarms,
          Parts = data.Parts,
          Pallets = data.Pallets,
          MainPrograms = data.MainPrograms,
          Fixtures = data.Fixtures,
          Logs = logs,
        };
      });
    }

    public void DeleteLogs(string lastSeenForeignId)
    {
      if (_dbType == MazakDbType.MazakVersionE)
      {
        // verE logs are deleted by Mazak
      }
      else
      {
        LogCSVParsing.DeleteLog(lastSeenForeignId, _cfg.LogCSVPath);
      }
    }

    public event Action OnNewEvent;

    private void RaiseNewLog(object sender, System.IO.FileSystemEventArgs e)
    {
      OnNewEvent?.Invoke();
    }
    #endregion
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
