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
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Makino
{
  #region Description of DB
  /* Makino static data.
   * ----------------------------------------------------------------------------------
   * A part consists of:
   *   - A globally unique integer called PartID
   *   - A name, revision, comment, priority
   *   - dbo.Parts stores the above data with one row per part
   *   - list of processes (found in dbo.Processes)
   *
   * A process consists of:
   *   - A globally unique integer called ProcessID (unique over all parts)
   *   - The PartID
   *   - A process number (a counter starting at 1 for each part)
   *   - A name, comment, offset file, priority
   *   - dbo.Process stores the above data with one row per process
   *
   *   - a list of operations
   *   - a list of jobs
   *
   * An operation consists of
   *   - A globally unique integer called OperationID
   *   - ProcessID  (which since it is unique identifies also the part)
   *   - An OperationSequence starting at 1 for each process
   *   - Three fields called ClampQuantity, UnclampMultiplier, OperationName
   *   - dbo.Operations stores the above data
   *
   * A job consists of
   *   - A globally unique JobID
   *   - ProcessID (which identifies a process of a part)
   *   - A JobNumber starting at 1 for each process
   *   - Comment
   *   - A JobType, which is 1 for load/unload and 2 for machining
   *   - dbo.Jobs stores the above data
   *
   * If a job is a load/unload job, the following additional data is stored
   *   - JobID
   *   - OperationType: 0 = load, 1 = unload
   *   - MotionType, InformationFile, WorkSetTime
   *   - dbo.WorkSetJobs stores this.
   *
   * If a job is a machining job, the following additional data is stored
   *   - JobID
   *   - OperatorCall (0 = none, 1 = permanent, 2 = oneshot)
   *   - OptionalBlockSkip (comma delimited list of values 2 through 9)
   *   - NCProgramID (lookup in dbo.NCProgramFiles)
   *   - MachiningMode, MachiningTime, TimeStudy, WHPMachiningType
   *   - dbo.MachiningJobs stores this
   *
   * A job also consists of a list of feasible devices in dbo.JobFeasibleDevices
   *   - JobID, FeasibleDeviceID (lookup in dbo.Devices)
   *
   * -------------------------------------------------------------------------------
   *
   * A fixture consists of
   *   - Globally unique FixtureID
   *   - name, comment, offsetfile
   *   - above stored in dbo.Fixtures.
   *   - list of processes
   *
   * The list of processes on a fixture is
   *   - FixtureID
   *   - ProcessID (uniquely identifies a process and part)
   *   - FixturePriority
   *   - OffsetFile
   *   - above stored in dbo.FixtureProcesses
   *
   * -------------------------------------------------------------------------------
   *
   * An order consists of
   *   - OrderID
   *   - PartID
   *   - name, comment, quantity, priority, start date, due date
   *   - above stored in dbo.Orders
   *
   * -------------------------------------------------------------------------------
   *
   * A pallet consists of
   *   - PalletID (globally unique integer)
   *   - PalletNumber, name, comment, pallet type (seems to be 0 always), priority, home info, vehicle id
   *   - above stored in dbo.Pallets
   *   - A list of fixtures currently mounted on the pallet
   *   - a list of feasible devices
   *
   * The list of fixutres assigned to the pallet
   *   - PalletFixtureID (globally unique integer)
   *   - PalletID
   *   - FixtureNumber (counter starting at 1 for each pallet id)
   *   - FixtureID currently in this (PalletID, FixtureNumber)
   *   - offset file, comment, pallet index (always seems to be zero)
   *   - some status columns
   *   - Above stored in dbo.PalletFixtures
   *
   * The list of pallet feasible devices is in dbo.PalletFeasibleDevice
   */

  /* Current Status
   *  -------------------------------------------------------------------------------
   * 	 - dbo.Quantities (one row per Order, Process pair)
   *        OrderID, ProcessID, quantities
   *
   *   - dbo.FixtureRun  (one row per PalletFixtureID)
   *       PalletFixtureID (a location on a pallet)
   *       current process, order, job
   *       status flag (seems to be 1 or 2 for before/after?)
   *       MachineDeviceID
   *
   *   - dbo.FixtureQuantity (one row per PalletFixtureID)
   *       PalletFixtureID (a location on a pallet)
   *       OperationID ??
   *       some quantities
   *
   *   - dbo.PalletLocation
   *       PalletID, DeviceID
     *
   *   - dbo.ManualTransport
   *       Current manual transports
   *
   *   - dbo.WorkSetStatus (one row per load station)
   *       DeviceID, status info
   *
   * 	 - dbo.MachineStatus (one row per machine)
   *       DeviceID
   *
   *       TablePalletID (matches with PalletLocation)
   *       TableStatus (???)
   *       FrontPalletID (incoming pallet)
   *       FrontStatus (???)
   *       BackPalletID (outgoing pallet)
   *       BackStatus (???)
   *
   *       MachineAction bool for if machine is running?
   *       Status (seems to be 1 for empty, 4 when running)
   *       CNCRun (seems to be 0 when empty, 3 when running)
   *       RunJob (bool if a job is running)
   *       JobID (seems to be 0 when not running, or a JobID for which specific thing on the pallet is running)
   *       MainONumber (always filled in, seems to be most recent number which ran or is running)
   *       StartDateTime (when the machine started running, or null)
   *       FinishDateTime (???, seems to be expected finish time)
   *
   *
   *   - dbo.WorkSetCommand (load/unload instructions)
   *       DeviceID
   *       PalletFixtureID (a location on a pallet)
   *       UnclampJobID, UnclampOrderID
   *       ClampJobID, ClampOrderID
   *       A bunch of other columns, not sure what they do
   */

  /* Tooling
     --------------------------------------
     - dbo.FunctionalToolData stores data about a tool type
        * FTNID is a unique number
        * FTN is a user visible name and is what is used inside the machining programs
        * TotalCutter shows how many cutters are in the tool

     - dbo.FunctionalToolCutterData has data about a cutter inside a single tool (most tools have just one cutter)
        * FTNCutterID is the unique id
        * FTNID is the key for the tool
        * CutterNo (1-indexed number of the cutter inside the functional tool)
        * ToolLife is the time in seconds the tool is expected to last
        * ToolLifeType is always 3, suspect that 3 means time while another value would mean usage count
        * there is more data about the tool

     - dbo.IndividualToolData stores data about a specific tool
       * FTNID refers back to the specific tool type
       * ITNID is a unique id for this tool
       * ITN is a user-visible number for the tool (we use as the serial)

     - dbo.IndividualToolCutterData stores data about a cutter in a specific tool
       * ITNCutterID for this specific cutter
       * ITNID to refer to the individual tool data
       * CutterNo (used to match with the CutterNo in the functional tool)
       * ActualToolLife

     - dbo.ToolLocation
       * ITNID to refer to the individual tool
       * CurrentDeviceID where the tool is located
       * CurrentPot for the tool pot
  */

  /* Unknown Tables
   * - dbo.ONumbers ????
   * - lots of tooling tables
   */
  #endregion

  //A common value from the CommonValue table holds values that were produced by
  //the execution of the part program.  This is commonly hold results.
  public record CommonValue
  {
    public required DateTime ExecDateTimeUTC { get; init; }

    public required int Number { get; init; }
    public required string? Value { get; init; }

    /* Fields Not Loaded
     *
     * - PalletNumber
     * - MainONNumber
     * - ExecOnNumber
     * - SequenceNumber
     */
  }

  public record MachineResults
  {
    public required DateTime StartDateTimeLocal { get; init; }
    public required DateTime EndDateTimeLocal { get; init; }
    public required DateTime StartDateTimeUTC { get; init; }
    public required DateTime EndDateTimeUTC { get; init; }

    public int DeviceID { get; set; }

    /* PalletID and FixtureNumber uniquely identifiy the location on the pallet */
    public int PalletID { get; set; }
    public int FixtureNumber { get; set; }

    /* data about the fixture */
    public string? FixtureName { get; set; }
    public string? FixtureComment { get; set; }

    public string? OrderName { get; set; }

    /* Part, revision, process, and job uniquely identify what the machine is currently doing */
    public string? PartName { get; set; }
    public string? Revision { get; set; }
    public int ProcessNum { get; set; }
    public int JobNum { get; set; }

    /* Process name is just some data about the process entered by the user,
       * not used as a key or anything like that */
    public string? ProcessName { get; set; }

    /* program for this (part,revision,process,job) combo */
    public string? Program { get; set; }

    /* Some status about the operation */
    public int SpindleTimeSeconds { get; set; }
    public List<int>? OperQuantities { get; set; }
    public List<CommonValue>? CommonValues { get; set; }

    /* Fields not loaded
     *
     * PalletIndex:
     *   always zero in the test data
     *
     * OffestFileName:
     *   not currently used/needed by machine watch
     *
     * AlarmNumber
     *   can/should we load and use this?
     *   it is always null in the test data
     *
     * FinishStatus
     *   what is this?
     *   it is either 'Normal' or 'ToolLife' in the test data
     */
  }

  public record WorkSetResults
  {
    public DateTime StartDateTimeUTC { get; set; }
    public DateTime EndDateTimeUTC { get; set; }

    public int DeviceID { get; set; }

    /* PalletID and FixtureNumber uniquely identifiy the location on the pallet */
    public int PalletID { get; set; }
    public int FixtureNumber { get; set; }

    /* data about the fixture entered by the user */
    public string? FixtureName { get; set; }
    public string? FixtureComment { get; set; }

    public string? UnloadOrderName { get; set; }
    public string? LoadOrderName { get; set; }

    /* Part, revision, process, and job uniquely identify which step we are on */
    public string? UnloadPartName { get; set; }
    public string? UnloadRevision { get; set; }
    public int UnloadProcessNum { get; set; }
    public int UnloadJobNum { get; set; }
    public string? LoadPartName { get; set; }
    public string? LoadRevision { get; set; }
    public int LoadProcessNum { get; set; }
    public int LoadJobNum { get; set; }

    /* Process name is just some data about the process entered by the user,
       * not used as a key or anything like that */
    public string? UnloadProcessName { get; set; }
    public string? LoadProcessName { get; set; }

    public List<int>? LoadQuantities { get; set; }
    public List<int>? UnloadNormalQuantities { get; set; }
    public List<int>? UnloadScrapQuantities { get; set; }
    public List<int>? UnloadOutProcQuantities { get; set; } /* TODO: What is this? */

    /* At the load station, the operator can push a button saying nothing was unloaded.
     * This still adds an entry to the log but it is marked as a remachine, and no quantities are updated
     */
    public bool Remachine { get; set; }

    /* Fields not loaded
      * OperationType:
      *    seems to be 2 for load and unload, 6 for no load or unload, 4 for unload only,
      *    and 1 for load only, plus 3 shows up sometimes.
      *
      * OperationStatus:
      *    not sure what this is
      *
      * ErrorCode:
      *    always null in my test data
      *
      * ResultVersion
      *    always 1 in the test data
      */
  }

  public record MakinoResults
  {
    public required IReadOnlyDictionary<int, PalletLocation> Devices { get; init; }
    public required List<MachineResults> MachineResults { get; init; }
    public required List<WorkSetResults> WorkSetResults { get; init; }
  }

  public record RemainingToRun
  {
    public required string JobUnique { get; init; }
    public required int RemainingQuantity { get; init; }
  }

  public interface IMakinoDB
  {
    IReadOnlyDictionary<int, PalletLocation> LoadMakinoDevices();
    CurrentStatus LoadCurrentInfo(IRepository logDb, DateTime nowUTC);
    MakinoResults LoadResults(DateTime startUTC, DateTime endUTC);
    ImmutableList<RemainingToRun> RemainingToRun();
    ImmutableList<ProgramInCellController> CurrentProgramsInCellController();
    ImmutableList<ToolInMachine> AllTools(string? machineGroup = null, int? machineNum = null);
  }

  public sealed class MakinoDB : IMakinoDB
  {
    #region Init
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MakinoDB>();

    private readonly MakinoSettings makinoCfg;
    private readonly string dbo;
    private readonly bool machineStatusHasJobColumn;

    public MakinoDB(MakinoSettings cfg)
    {
      makinoCfg = cfg;
      dbo = makinoCfg.DbConnectionString?.StartsWith("sqlite:") == true ? "" : "dbo.";
      machineStatusHasJobColumn = CheckForMachineStatusJobColumn();
    }

    public IDbConnection OpenConnection()
    {
      if (string.IsNullOrEmpty(makinoCfg.DbConnectionString))
      {
        var db = new Microsoft.Data.SqlClient.SqlConnection(
          "Data Source = (localdb)\\MSSQLLocalDB; Initial Catalog = Makino;"
        );
        db.Open();
        return db;
      }
      else if (makinoCfg.DbConnectionString.StartsWith("sqlite:"))
      {
        var db = new Microsoft.Data.Sqlite.SqliteConnection(makinoCfg.DbConnectionString[7..]);
        db.Open();
        return db;
      }
      else
      {
        var db = new Microsoft.Data.SqlClient.SqlConnection(makinoCfg.DbConnectionString);
        db.Open();
        return db;
      }
    }

    private bool CheckForMachineStatusJobColumn()
    {
      try
      {
        using var db = OpenConnection();
        using var cmd = db.CreateCommand();
        if (makinoCfg.DbConnectionString?.StartsWith("sqlite:") == true)
        {
          cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('MachineStatus') WHERE name = 'JobID'";
          return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }
        else
        {
          cmd.CommandText =
            "SELECT COUNT(*) FROM sys.columns WHERE Object_ID = Object_ID(N'dbo.MachineStatus') AND Name = N'JobID'";
          return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }
      }
      catch (Exception ex)
      {
        Log.Debug(ex, "Error checking for JobID column in MachineStatus");
        return false;
      }
    }

    #endregion

    #region Log Data
    /*
   * Log Data
   *   - dbo.CNCAlarams
   *   - dbo.MachineResults
   *   - dbo.PMCAlarms
   *   - dbo.VehicleResults
   *   - dbo.WorkSetResults
   * 	 - dbo.OrderResults (row seems to be recorded when the order is completed)
   * 	 - dbo.CommonValues
   */

    public MakinoResults LoadResults(DateTime startUTC, DateTime endUTC)
    {
      using var _db = OpenConnection();
      using var trans = _db.BeginTransaction();
      var devices = Devices(_db, trans);
      return new MakinoResults
      {
        Devices = devices,
        MachineResults = QueryMachineResults(_db, startUTC, endUTC, trans),
        WorkSetResults = QueryLoadUnloadResults(_db, startUTC, endUTC, trans),
      };
    }

    private List<MachineResults> QueryMachineResults(
      IDbConnection _db,
      DateTime startUTC,
      DateTime endUTC,
      IDbTransaction trans
    )
    {
      using var cmd = _db.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT StartDateTime,FinishDateTime,DeviceID,PalletID,"
        + "FixtureNumber,FixtureName,FixtureComment,OrderName,PartName,Revision,ProcessNumber,"
        + "JobNumber,ProcessName,NCProgramFileName,SpindleTime,"
        + "MaxOperation,QuantityOpe1,QuantityOpe2,QuantityOpe3,QuantityOpe4"
        + " FROM "
        + dbo
        + "MachineResults WHERE FinishDateTime >= @start AND FinishDateTime <= @end";
      var param = cmd.CreateParameter();
      param.ParameterName = "@start";
      param.DbType = DbType.DateTime;
      param.Value = TimeZoneInfo.ConvertTimeFromUtc(startUTC, makinoCfg.LocalTimeZone);
      cmd.Parameters.Add(param);
      param = cmd.CreateParameter();
      param.ParameterName = "@end";
      param.DbType = DbType.DateTime;
      param.Value = TimeZoneInfo.ConvertTimeFromUtc(endUTC, makinoCfg.LocalTimeZone);
      cmd.Parameters.Add(param);

      var ret = new List<MachineResults>();

      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          var localStart = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Unspecified);
          var localEnd = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Unspecified);
          var m = new MachineResults
          {
            StartDateTimeLocal = localStart,
            EndDateTimeLocal = localEnd,
            StartDateTimeUTC = TimeZoneInfo.ConvertTimeToUtc(localStart, makinoCfg.LocalTimeZone),
            EndDateTimeUTC = TimeZoneInfo.ConvertTimeToUtc(localEnd, makinoCfg.LocalTimeZone),
          };

          m.DeviceID = reader.GetInt32(2);
          m.PalletID = reader.GetInt32(3);
          m.FixtureNumber = reader.GetInt32(4);
          if (!reader.IsDBNull(5))
            m.FixtureName = reader.GetString(5);
          m.FixtureComment = reader.GetString(6);
          m.OrderName = reader.GetString(7);
          m.PartName = reader.GetString(8);
          if (!reader.IsDBNull(9))
            m.Revision = reader.GetString(9);
          m.ProcessNum = reader.GetInt32(10);
          m.JobNum = reader.GetInt32(11);
          if (!reader.IsDBNull(12))
            m.ProcessName = reader.GetString(12);
          m.Program = reader.GetString(13);
          m.SpindleTimeSeconds = reader.GetInt32(14);
          m.OperQuantities = [];
          int numOper = reader.GetInt32(15);
          for (int i = 0; i < Math.Min(4, numOper); i++)
          {
            m.OperQuantities.Add(reader.GetInt32(16 + i));
          }

          ret.Add(m);
        }
      }

      foreach (var m in ret)
      {
        m.CommonValues = QueryCommonValues(_db, m.StartDateTimeLocal, m.EndDateTimeLocal, m.DeviceID, trans);
      }

      return ret;
    }

    private List<WorkSetResults> QueryLoadUnloadResults(
      IDbConnection db,
      DateTime startUTC,
      DateTime endUTC,
      IDbTransaction trans
    )
    {
      using var cmd = db.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT StartDateTime,FinishDateTime,DeviceID,PalletID,"
        + "FixtureNumber,FixtureName,FixtureComment,"
        + "UnclampOrderName,UnclampPartName,UnclampRevision,UnclampProcessNumber,UnclampJobNumber,UnclampProcessName,"
        + "ClampOrderName,ClampPartName,ClampRevision,ClampProcessNumber,ClampJobNumber,ClampProcessName,"
        + "Remachine,"
        + "ClampQuantityOpe1,ClampQuantityOpe2,ClampQuantityOpe3,ClampQuantityOpe4,"
        + "UnclampNormalQtyOpe1, UnclampNormalQtyOpe2, UnclampNormalQtyOpe3, UnclampNormalQtyOpe4,"
        + "UnclampScrapQtyOpe1, UnclampScrapQtyOpe2, UnclampScrapQtyOpe3, UnclampScrapQtyOpe4,"
        + "UnclampOutProcQtyOpe1, UnclampOutProcQtyOpe2, UnclampOutProcQtyOpe3, UnclampOutProcQtyOpe4"
        + " FROM "
        + dbo
        + "WorkSetResults WHERE FinishDateTime >= @start AND FinishDateTime <= @end";
      var param = cmd.CreateParameter();
      param.ParameterName = "@start";
      param.DbType = DbType.DateTime;
      param.Value = TimeZoneInfo.ConvertTimeFromUtc(startUTC, makinoCfg.LocalTimeZone);
      cmd.Parameters.Add(param);
      param = cmd.CreateParameter();
      param.ParameterName = "@end";
      param.DbType = DbType.DateTime;
      param.Value = TimeZoneInfo.ConvertTimeFromUtc(endUTC, makinoCfg.LocalTimeZone);
      cmd.Parameters.Add(param);

      var ret = new List<WorkSetResults>();

      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          var m = new WorkSetResults
          {
            StartDateTimeUTC = TimeZoneInfo.ConvertTimeToUtc(
              DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Unspecified),
              makinoCfg.LocalTimeZone
            ),
            EndDateTimeUTC = TimeZoneInfo.ConvertTimeToUtc(
              DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Unspecified),
              makinoCfg.LocalTimeZone
            ),
          };

          m.DeviceID = reader.GetInt32(2);
          m.PalletID = reader.GetInt32(3);
          m.FixtureNumber = reader.GetInt32(4);
          if (!reader.IsDBNull(5))
            m.FixtureName = reader.GetString(5);
          if (!reader.IsDBNull(6))
            m.FixtureComment = reader.GetString(6);
          if (!reader.IsDBNull(7))
          {
            m.UnloadOrderName = reader.GetString(7);
            m.UnloadPartName = reader.GetString(8);
            if (!reader.IsDBNull(9))
              m.UnloadRevision = reader.GetString(9);
            m.UnloadProcessNum = reader.GetInt32(10);
            m.UnloadJobNum = reader.GetInt32(11);
            if (!reader.IsDBNull(12))
              m.UnloadProcessName = reader.GetString(12);
          }
          if (!reader.IsDBNull(13))
          {
            m.LoadOrderName = reader.GetString(13);
            m.LoadPartName = reader.GetString(14);
            if (!reader.IsDBNull(15))
              m.LoadRevision = reader.GetString(15);
            m.LoadProcessNum = reader.GetInt32(16);
            m.LoadJobNum = reader.GetInt32(17);
            if (!reader.IsDBNull(18))
              m.LoadProcessName = reader.GetString(18);
          }
          m.Remachine = Convert.ToBoolean(reader.GetValue(19));

          m.LoadQuantities = [];
          m.UnloadNormalQuantities = [];
          m.UnloadScrapQuantities = [];
          m.UnloadOutProcQuantities = [];
          for (int i = 0; i < 4; i++)
          {
            if (reader.GetInt32(20 + i) != 0)
              m.LoadQuantities.Add(reader.GetInt32(20 + i));
            if (reader.GetInt32(24 + i) != 0)
              m.UnloadNormalQuantities.Add(reader.GetInt32(24 + i));
            if (reader.GetInt32(28 + i) != 0)
              m.UnloadScrapQuantities.Add(reader.GetInt32(28 + i));
            if (reader.GetInt32(32 + i) != 0)
              m.UnloadOutProcQuantities.Add(reader.GetInt32(32 + i));
          }

          ret.Add(m);
        }
      }

      return ret;
    }

    private List<CommonValue> QueryCommonValues(
      IDbConnection db,
      DateTime startLocal,
      DateTime endLocal,
      int deviceId,
      IDbTransaction trans
    )
    {
      using var cmd = db.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT ExecDateTime,Number,Value"
        + " FROM "
        + dbo
        + "CommonValues "
        + " WHERE ExecDateTime >= @start AND ExecDateTime <= @end AND DeviceID = @dev";
      var param = cmd.CreateParameter();
      param.ParameterName = "@start";
      param.DbType = DbType.DateTime;
      param.Value = startLocal;
      cmd.Parameters.Add(param);
      param = cmd.CreateParameter();
      param.ParameterName = "@end";
      param.DbType = DbType.DateTime;
      param.Value = endLocal;
      cmd.Parameters.Add(param);
      param = cmd.CreateParameter();
      param.ParameterName = "@dev";
      param.DbType = DbType.Int64;
      param.Value = deviceId;
      cmd.Parameters.Add(param);

      var ret = new List<CommonValue>();

      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          var v = new CommonValue()
          {
            ExecDateTimeUTC = TimeZoneInfo.ConvertTimeToUtc(
              DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Unspecified),
              makinoCfg.LocalTimeZone
            ),
            Number = reader.GetInt32(1),
            Value = reader.GetValue(2).ToString(),
          };
          ret.Add(v);
        }
      }

      return ret;
    }

    #endregion

    #region Current Status
    public IReadOnlyDictionary<int, PalletLocation> LoadMakinoDevices()
    {
      using var db = OpenConnection();
      using var trans = db.BeginTransaction();
      return Devices(db, trans);
    }

    private Dictionary<int, PalletLocation> Devices(IDbConnection db, IDbTransaction trans)
    {
      using var cmd = db.CreateCommand();
      cmd.Transaction = trans;
      //Makino: Devices
      var devices = new Dictionary<int, PalletLocation>();

      cmd.CommandText = "SELECT DeviceID, DeviceType, DeviceNumber, DeviceName FROM " + dbo + "Devices";

      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          var devID = reader.GetInt32(0);
          //some versions of makino use short
          var dType = Convert.ToInt32(reader.GetValue(1));
          var dNum = Convert.ToInt32(reader.GetValue(2));
          var name = reader.GetString(3);

          devices.Add(devID, ParseDevice(dType, dNum, name));
        }
      }

      return devices;
    }

    public CurrentStatus LoadCurrentInfo(IRepository logDb, DateTime nowUTC)
    {
      var map = new MakinoToJobMap(logDb);

      using var _db = OpenConnection();
      using var trans = _db.BeginTransaction();

      var devices = Devices(_db, trans);

      Load(
        "SELECT PartID, ProcessNumber, ProcessID FROM " + dbo + "Processes",
        reader =>
        {
          map.AddProcess(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        },
        trans
      );

      Load(
        "SELECT PartID, PartName, Comment FROM " + dbo + "Parts",
        reader =>
        {
          var partID = reader.GetInt32(0);

          var partName = reader.GetString(1);

          map.CreateJob(
            partName,
            partID,
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2)
          );
        },
        trans
      );

      Load(
        "SELECT ProcessID, JobNumber, JobID FROM " + dbo + "Jobs",
        reader =>
        {
          map.AddJobToProcess(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        },
        trans
      );

      Load(
        "SELECT a.JobID, b.Name FROM "
          + dbo
          + "MachineJobs a "
          + "INNER JOIN "
          + dbo
          + "NCProgramFiles b "
          + "ON a.NCProgramFileID = b.NCProgramFileID",
        reader =>
        {
          map.AddProgramToJob(reader.GetInt32(0), reader.GetString(1));
        },
        trans
      );

      Load(
        "SELECT JobID, FeasibleDeviceID FROM " + dbo + "JobFeasibleDevices",
        reader =>
        {
          map.AddAllowedStationToJob(reader.GetInt32(0), devices[reader.GetInt32(1)]);
        },
        trans
      );

      map.CompleteStations();

      Load(
        "SELECT a.PalletID, a.PalletFixtureID, a.FixtureNumber, a.FixtureID, b.PalletNumber, c.CurDeviceType, c.CurDeviceNumber, d.DeviceName "
          + $" FROM {dbo}PalletFixtures a, {dbo}Pallets b, {dbo}PalletLocation c "
          + $" LEFT OUTER JOIN {dbo}Devices d ON c.CurDeviceType = d.DeviceType AND c.CurDeviceNumber = d.DeviceNumber"
          + " WHERE a.PalletID = b.PalletID AND a.PalletID = c.PalletID "
          + " AND a.FixtureID IS NOT NULL",
        reader =>
        {
          var loc = new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 0);
          if (!reader.IsDBNull(5) && !reader.IsDBNull(6))
            loc = ParseDevice(
              reader.GetInt16(5),
              reader.GetInt16(6),
              reader.IsDBNull(7) ? null : reader.GetString(7)
            );

          map.AddPalletInfo(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            loc
          );
        },
        trans
      );

      Load(
        "SELECT ProcessID, FixtureID FROM " + dbo + "FixtureProcesses",
        reader =>
        {
          map.AddFixtureToProcess(reader.GetInt32(0), reader.GetInt32(1));
        },
        trans
      );

      Load(
        "SELECT ProcessID, ClampQuantity FROM " + dbo + "Operations",
        reader =>
        {
          map.AddOperationToProcess(reader.GetInt32(0), reader.GetInt32(1));
        },
        trans
      );

      Load(
        "SELECT OrderID, OrderName, PartID, Comment, Quantity, Priority FROM " + dbo + "Orders",
        reader =>
        {
          var comment = reader.IsDBNull(3) ? "" : reader.GetString(3);
          map.DuplicateForOrder(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(2),
            newJob =>
              newJob with
              {
                Comment = string.IsNullOrEmpty(comment) ? newJob.Comment : comment,
                Cycles = reader.GetInt32(4),
                Precedence =
                [
                  [reader.GetInt16(5)],
                ],
              }
          );
        },
        trans
      );

      Load(
        "SELECT OrderID, ProcessID, RemainingQuantity, NormalQuantity, ScrapQuantity "
          + "FROM "
          + dbo
          + "Quantities",
        reader =>
        {
          map.AddQuantityToProcess(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
          );
        },
        trans
      );

      string loadFixRunCmd;
      if (machineStatusHasJobColumn)
      {
        loadFixRunCmd =
          "SELECT f.PalletFixtureID, f.CurProcessID, f.CurOrderID, f.CurJobID, f.LastMachiningJobID, m.TablePalletID, m.MachineAction, m.RunJob "
          + $"FROM {dbo}FixtureRun f LEFT OUTER JOIN {dbo}MachineStatus m ON f.MachineDeviceID = m.DeviceID AND f.CurJobID = m.JobID"
          + " WHERE f.CurProcessID IS NOT NULL";
      }
      else
      {
        loadFixRunCmd =
          "SELECT f.PalletFixtureID, f.CurProcessID, f.CurOrderID, f.CurJobID, f.LastMachiningJobID, m.TablePalletID, m.MachineAction, 1 "
          + $"FROM {dbo}FixtureRun f LEFT OUTER JOIN {dbo}MachineStatus m ON f.MachineDeviceID = m.DeviceID "
          + " WHERE f.CurProcessID IS NOT NULL";
      }

      Load(
        loadFixRunCmd,
        reader =>
        {
          int palfixID = reader.GetInt32(0);
          int orderID = reader.GetInt32(2);
          var job = map.JobForOrder(orderID);

          string orderName = "";
          if (job != null)
            orderName = job.UniqueStr;

          // Lookup (pallet,location) for this palfixID
          map.PalletLocInfo(palfixID, out int palletNum, out int fixtureNum);

          //look for material id
          IEnumerable<(long matId, string? serial, string? workorder)> mats;
          if (orderName == "")
          {
            mats = [];
          }
          else
          {
            var mostRecentLog = LogBuilder.FindLogByForeign(
              palletNum,
              fixtureNum,
              orderName,
              nowUTC.AddSeconds(10),
              logDb
            );
            if (mostRecentLog == null)
            {
              mats = [];
            }
            else
            {
              mats = mostRecentLog.Material.Select(m => (m.MaterialID, m.Serial, m.Workorder));
            }
          }

          if (!mats.Any())
          {
            mats = [(-1, null, null)];
          }

          foreach (var (matId, serial, workorder) in mats)
          {
            map.AddMaterial(
              fixturePalletID: palfixID,
              orderID: orderID,
              processID: reader.GetInt32(1),
              palletNum: palletNum,
              fixtureNum: fixtureNum,
              curJobID: reader.IsDBNull(3) ? null : reader.GetInt32(3),
              lastMachiningJobID: reader.IsDBNull(4) ? null : reader.GetInt32(4),
              tablePalletID: reader.IsDBNull(5) ? null : reader.GetInt32(5),
              machineAction: reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6)),
              runJob: reader.IsDBNull(7) ? null : Convert.ToInt32(reader.GetValue(7)),
              matID: matId,
              serial: serial,
              workorder: workorder
            );
          }
        },
        trans
      );

      //There is a MovePalletFixtureID column which presumebly means rotate through process?
      //Other columns include remachining, cancel, recleaning, offdutyprocess, operation status
      Load(
        "SELECT PalletFixtureID, UnclampJobID, UnclampOrderID, ClampJobID, ClampOrderID, ClampQuantity "
          + "FROM "
          + dbo
          + "WorkSetCommand WHERE PalletFixtureID IS NOT NULL",
        reader =>
        {
          var palfixID = reader.GetInt32(0);

          if (!reader.IsDBNull(1) && !reader.IsDBNull(2))
          {
            var unclampJobID = reader.GetInt32(1);
            var unclampOrder = reader.GetInt32(2);

            var procNum = map.ProcessForJobID(unclampJobID);
            var job = map.JobForOrder(unclampOrder);
            if (job != null)
              map.SetMaterialAsUnload(palfixID, job.Processes.Count == procNum);
          }

          if (!reader.IsDBNull(3) && !reader.IsDBNull(4))
          {
            var clampJobID = reader.GetInt32(3);
            var clampOrder = reader.GetInt32(4);

            var procNum = map.ProcessForJobID(clampJobID);
            var job = map.JobForOrder(clampOrder);
            int qty = 1;
            if (!reader.IsDBNull(5))
              qty = reader.GetInt32(5);

            if (job != null)
              map.AddMaterialToLoad(palfixID, job.UniqueStr, job.PartName, procNum, qty);
          }
        },
        trans
      );

      Load(
        "SELECT DeviceID, FrontPalletID, BackPalletID "
          + "FROM "
          + dbo
          + "MachineStatus"
          + " WHERE FrontPalletID IS NOT NULL OR BackPalletID IS NOT NULL",
        reader =>
        {
          var machine = devices[reader.GetInt32(0)].Num;

          if (!reader.IsDBNull(1))
          {
            map.SetPalletOnRotary(machine, reader.GetInt32(1));
          }
          else if (!reader.IsDBNull(2))
          {
            map.SetPalletOnRotary(machine, reader.GetInt32(2));
          }
        },
        trans
      );

      return new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = nowUTC,
        Jobs = map.Jobs.ToImmutableDictionary(j => j.UniqueStr),
        Pallets = map.Pallets.ToImmutableDictionary(),
        Material = map.Material.ToImmutableList(),
        Alarms = [],
        Queues = ImmutableDictionary<string, QueueInfo>.Empty,
      };
    }

    public ImmutableList<RemainingToRun> RemainingToRun()
    {
      var ret = ImmutableList.CreateBuilder<RemainingToRun>();

      using var db = OpenConnection();
      using var trans = db.BeginTransaction();

      Load(
        "SELECT o.OrderName, q.RemainingQuantity"
          + $" FROM {dbo}Quantities q"
          + $" INNER JOIN {dbo}Orders o ON q.OrderID = o.OrderID"
          + $" INNER JOIN {dbo}Processes p ON q.ProcessID = p.ProcessID"
          + $" INNER JOIN {dbo}Parts pa ON o.PartID = pa.PartID"
          + " WHERE (pa.Revision = 'Insight' OR pa.Revision = 'SAIL') AND p.ProcessNumber = 1 "
          + " AND o.OrderName IS NOT NULL AND q.RemainingQuantity > 0",
        reader =>
        {
          ret.Add(
            new RemainingToRun { JobUnique = reader.GetString(0), RemainingQuantity = reader.GetInt32(1) }
          );
        },
        trans
      );

      return ret.ToImmutable();
    }

    private static void Load(string command, Action<IDataReader> onEachRow, IDbTransaction trans)
    {
      Log.Verbose(string.Concat("Loading ", command.AsSpan(7)));

      using var cmd = trans.Connection!.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText = command;
      using var reader = cmd.ExecuteReader();
      while (reader.Read())
      {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Verbose))
        {
          var row = "   ";
          for (int i = 0; i < reader.FieldCount; i++)
          {
            row += reader.GetName(i) + " ";
            row += reader.GetDataTypeName(i) + ": ";
            if (reader.IsDBNull(i))
            {
              row += "(null)";
            }
            else
            {
              row += reader.GetValue(i).ToString();
            }
            row += "  ";
          }
          Log.Verbose(row);
        }

        onEachRow(reader);
      }
    }

    private static PalletLocation ParseDevice(int deviceType, int deviceNumber, string? name)
    {
      var ret = new PalletLocation(PalletLocationEnum.Buffer, "Unknown", 0);

      if (deviceType == 1)
      {
        if (string.IsNullOrEmpty(name))
        {
          Log.Error("Device name is null for {type} and {num}", deviceType, deviceNumber);
          name = "MC";
        }
        // name is something like MCW001, we want to parse the numbers at the end into deviceNumber and then get the name of the machine without the numbers
        var lastDigitIdx = name.Length - 1;
        while (lastDigitIdx >= 0 && char.IsDigit(name[lastDigitIdx]))
        {
          lastDigitIdx--;
        }
        if (lastDigitIdx != name.Length - 1 && lastDigitIdx != 0)
        {
          _ = int.TryParse(name[(lastDigitIdx + 1)..], out deviceNumber);
          name = name[..(lastDigitIdx + 1)];
        }
        ret = new PalletLocation(PalletLocationEnum.Machine, name, deviceNumber);
      }
      else if (deviceType == 2)
        ret = new PalletLocation(PalletLocationEnum.Cart, "Cart", deviceNumber);
      else if (deviceType == 3)
        ret = new PalletLocation(PalletLocationEnum.LoadUnload, "L/U", deviceNumber);
      else if (deviceType == 4)
        ret = new PalletLocation(PalletLocationEnum.Buffer, "Buffer", deviceNumber);
      //dType = 5 is Tool Crib
      //dType = 7 is Presetter
      return ret;
    }
    #endregion

    #region Monitoring

    #endregion

    #region Programs and Tools
    public ImmutableList<ProgramInCellController> CurrentProgramsInCellController()
    {
      var progs = ImmutableList.CreateBuilder<ProgramInCellController>();

      using var _db = OpenConnection();
      using var trans = _db.BeginTransaction();
      using var cmd = _db.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText = "SELECT Name, Comment FROM " + dbo + "NCProgramFiles";

      using var reader = cmd.ExecuteReader();

      while (reader.Read())
      {
        var name = reader.GetString(0);
        progs.Add(
          new ProgramInCellController()
          {
            CellControllerProgramName = name,
            ProgramName = name,
            Comment = reader.IsDBNull(1) ? "" : reader.GetString(1),
          }
        );
      }

      return progs.ToImmutable();
    }

    private static string ToolName(string comment, long ftn, int cutterNo, int totalCutter)
    {
      var name = comment;
      if (totalCutter != 1)
      {
        name += " Cutter " + cutterNo;
      }
      name += " (" + ftn.ToString() + ")";
      return name;
    }

    public ImmutableList<ToolSnapshot> SnapshotForProgram(int NCProgramFileID, int deviceId)
    {
      // I suspect ftd.ToolLifeType determines either time or count, but in the test data it is always
      // 3 which seems to be time
      using var _db = OpenConnection();
      using var trans = _db.BeginTransaction();
      using var cmd = _db.CreateCommand();
      cmd.Transaction = trans;

      cmd.CommandText =
        $"SELECT ftd.FTNComment, ftd.FTN, ftd.TotalCutter, ftcd.CutterNo, ftcd.ToolLife, itd.ITN, itcd.ActualToolLife, tl.CurrentPot "
        + $" FROM {dbo}MainProgramTools mpt "
        + $" INNER JOIN {dbo}FunctionalToolData ftd ON mpt.FTN = ftd.FTN"
        + $" INNER JOIN {dbo}FunctionalToolCutterData ftcd ON ftd.FTNID = ftcd.FTNID"
        + $" INNER JOIN {dbo}IndividualToolData itd ON ftd.FTNID = itd.FTNID"
        + $" INNER JOIN {dbo}IndividualToolCutterData itcd ON itd.ITNID = itcd.ITNID AND ftcd.CutterNo = itcd.CutterNo"
        + $" INNER JOIN {dbo}ToolLocation tl ON itd.ITNID = tl.ITNID"
        + " WHERE mpt.NCProgramFileID = @id AND tl.CurrentDeviceID = @dev";

      var param = cmd.CreateParameter();
      param.ParameterName = "@id";
      param.DbType = DbType.Int32;
      param.Value = NCProgramFileID;
      cmd.Parameters.Add(param);

      param = cmd.CreateParameter();
      param.ParameterName = "@dev";
      param.DbType = DbType.Int32;
      param.Value = deviceId;
      cmd.Parameters.Add(param);

      var tools = ImmutableList.CreateBuilder<ToolSnapshot>();

      using var reader = cmd.ExecuteReader();

      while (reader.Read())
      {
        if (Enumerable.Range(0, 8).Any(reader.IsDBNull))
        {
          continue;
        }

        tools.Add(
          new ToolSnapshot()
          {
            ToolName = ToolName(
              comment: reader.GetString(0),
              ftn: reader.GetInt32(1),
              totalCutter: reader.GetInt32(2),
              cutterNo: reader.GetInt32(3)
            ),
            Pocket = reader.GetInt32(7),
            Serial = reader.GetInt32(5).ToString(),
            CurrentUse = TimeSpan.FromSeconds(reader.GetInt32(6)),
            TotalLifeTime = TimeSpan.FromSeconds(reader.GetInt32(4)),
          }
        );
      }

      return tools.ToImmutable();
    }

    public ImmutableList<ToolInMachine> AllTools(string? machineName = null, int? machineNum = null)
    {
      using var db = OpenConnection();
      using var trans = db.BeginTransaction();

      int? deviceId = null;

      if (!string.IsNullOrEmpty(machineName) && machineNum.HasValue && machineNum.Value > 0)
      {
        deviceId = Devices(db, trans)
          .FirstOrDefault(d =>
            d.Value.Location == PalletLocationEnum.Machine
            && d.Value.StationGroup == machineName
            && d.Value.Num == machineNum.Value
          )
          .Key;

        if (deviceId == 0)
        {
          // device not found
          return [];
        }
      }

      var sql =
        $"SELECT ftd.FTNComment, ftd.FTN, ftd.TotalCutter, ftcd.CutterNo, ftcd.ToolLife, itd.ITN, itcd.ActualToolLife, tl.CurrentPot, d.DeviceType, d.DeviceNumber, d.DeviceName "
        + $" FROM {dbo}FunctionalToolData ftd"
        + $" INNER JOIN {dbo}FunctionalToolCutterData ftcd ON ftd.FTNID = ftcd.FTNID"
        + $" INNER JOIN {dbo}IndividualToolData itd ON ftd.FTNID = itd.FTNID"
        + $" INNER JOIN {dbo}IndividualToolCutterData itcd ON itd.ITNID = itcd.ITNID AND ftcd.CutterNo = itcd.CutterNo"
        + $" INNER JOIN {dbo}ToolLocation tl ON itd.ITNID = tl.ITNID "
        + $" INNER JOIN {dbo}Devices d ON tl.CurrentDeviceID = d.DeviceID";

      if (deviceId.HasValue)
      {
        sql += " WHERE d.DeviceId = @dev";
      }

      using var cmd = db.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText = sql;

      if (deviceId.HasValue)
      {
        var param = cmd.CreateParameter();
        param.ParameterName = "@dev";
        param.DbType = DbType.Int32;
        param.Value = deviceId.Value;
        cmd.Parameters.Add(param);
      }

      var tools = ImmutableList.CreateBuilder<ToolInMachine>();

      using var reader = cmd.ExecuteReader();

      while (reader.Read())
      {
        if (Enumerable.Range(0, 11).Any(reader.IsDBNull))
        {
          continue;
        }
        var dev = ParseDevice(reader.GetInt16(8), reader.GetInt16(9), reader.GetString(10));

        tools.Add(
          new ToolInMachine()
          {
            ToolName = ToolName(
              comment: reader.GetString(0),
              ftn: reader.GetInt32(1),
              totalCutter: reader.GetInt32(2),
              cutterNo: reader.GetInt32(3)
            ),
            Pocket = reader.GetInt32(7),
            Serial = reader.GetInt32(5).ToString(),
            CurrentUse = TimeSpan.FromSeconds(reader.GetInt32(6)),
            TotalLifeTime = TimeSpan.FromSeconds(reader.GetInt32(4)),
            MachineGroupName = dev.StationGroup,
            MachineNum = dev.Num,
          }
        );
      }

      return tools.ToImmutable();
    }

    #endregion
  }
}
