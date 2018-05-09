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
using System.Runtime.Serialization;
using BlackMaple.MachineWatchInterface;
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using Microsoft.Extensions.DependencyModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace DebugMachineWatchApiServer
{
  public static class DebugMockProgram
  {
    public static void Main()
    {
      BlackMaple.MachineFramework.Program.Run(false, new MockFMSImplementation());
    }
  }
  public class MockFMSImplementation : IFMSImplementation
  {
    public FMSInfo Info { get; }
        = new FMSInfo()
        {
          Name = "mock",
          Version = "1.2.3.4"
        };

    public IFMSBackend Backend { get; }
        = new MockServerBackend();

    public IList<IBackgroundWorker> Workers { get; }
        = new List<IBackgroundWorker>();
  }

  public class MockServerBackend : IFMSBackend, IJobControl, IOldJobDecrement
  {
    public JobLogDB LogDB { get; private set; }
    public JobDB JobDB { get; private set; }
    public InspectionDB InspectionDB { get; private set; }
    public MockCurrentStatus MockStatus {get; private set;}

    public event NewCurrentStatus OnNewCurrentStatus;

    public void Init(string dataDir, IConfig config, SerialSettings serialSettings)
    {
      BlackMaple.MachineFramework.Program.FMSSettings.WorkorderAssignment = WorkorderAssignmentType.AssignWorkorderAtWash;
      string path = null; // dataDir

      string dbFile(string f) => System.IO.Path.Combine(path, f + ".db");

      if (path != null)
      {
        if (System.IO.File.Exists(dbFile("log"))) System.IO.File.Delete(dbFile("log"));
        LogDB = new JobLogDB();
        LogDB.Open(dbFile("log"));

        if (System.IO.File.Exists(dbFile("insp"))) System.IO.File.Delete(dbFile("insp"));
        InspectionDB = new InspectionDB(LogDB);
        InspectionDB.Open(dbFile("insp"));

        if (System.IO.File.Exists(dbFile("job"))) System.IO.File.Delete(dbFile("job"));
        JobDB = new JobDB();
        JobDB.Open(dbFile("job"));
      }
      else
      {
        var conn = SqliteExtensions.ConnectMemory();
        conn.Open();
        LogDB = new JobLogDB(conn);
        LogDB.CreateTables();

        conn = SqliteExtensions.ConnectMemory();
        conn.Open();
        InspectionDB = new InspectionDB(LogDB, conn);
        InspectionDB.CreateTables();

        conn = SqliteExtensions.ConnectMemory();
        conn.Open();
        JobDB = new JobDB(conn);
        JobDB.CreateTables();
      }

      /*
      var sample = new LogEntryGenerator(LogDB);
      DateTime today = DateTime.Today;
      DateTime month = new DateTime(today.Year, today.Month, 1);

      month = month.ToUniversalTime();
      sample.AddMonthOfCycles(month, "uniq1", "part1", "pal1", 1, 40, 70);
      sample.AddMonthOfCycles(month, "uniq2", "part2", "pal2", 2, 80, 110);
      sample.AddMonthOfCycles(month, "uniq1", "part1", "pal2", 3, 72, 72);

      month = month.AddMonths(-1);
      sample.AddMonthOfCycles(month, "uniq3", "part1", "pal1", 1, 40, 70);
      sample.AddMonthOfCycles(month, "uniq4", "part2", "pal2", 2, 80, 110);
      sample.AddMonthOfCycles(month, "uniq3", "part1", "pal2", 3, 72, 72);
      sample.AddEntriesToDatabase();
      */

      var sampleDataPath = System.IO.Path.Combine(
          System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
          "../../../sample-data/"
      );

      // sample data starts at Jan 1, 2018.  Need to offset to current month
      var today = DateTime.Today;
      var curMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Local);
      curMonth = curMonth.ToUniversalTime();
      var jan1_18 = new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc);
      var offset = curMonth.Subtract(jan1_18);

      LoadEvents(sampleDataPath, offset);
      LoadJobs(sampleDataPath, offset);

      MockStatus = new MockCurrentStatus(JobDB, sampleDataPath, offset);
    }

    public IEnumerable<System.Diagnostics.TraceSource> TraceSources()
    {
      return new System.Diagnostics.TraceSource[] { };
    }

    public void Halt()
    {
      JobDB.Close();
      InspectionDB.Close();
      LogDB.Close();
    }

    public IInspectionControl InspectionControl()
    {
      return InspectionDB;
    }

    public IJobControl JobControl()
    {
      return this;
    }

    public ILogDatabase LogDatabase()
    {
      return LogDB;
    }

    public IJobDatabase JobDatabase()
    {
      return JobDB;
    }

    public CurrentStatus GetCurrentStatus()
    {
      return MockStatus.GetCurrentStatus();
    }

    public List<string> CheckValidRoutes(IEnumerable<JobPlan> newJobs)
    {
      return new List<string>();
    }

    public void AddJobs(NewJobs jobs, string expectedPreviousScheduleId)
    {
      JobDB.AddJobs(jobs, expectedPreviousScheduleId);
    }

    public void AddUnprocessedMaterialToQueue(string jobUnique, int lastCompletedProcess, string queue, int position, string serial)
        => MockStatus.AddUnprocessedMaterialToQueue(jobUnique, lastCompletedProcess, queue, position, serial);
    public void SetMaterialInQueue(long materialId, string queue, int position)
        => MockStatus.SetMaterialInQueue(materialId, queue, position);
    public void RemoveMaterialFromAllQueues(long materialId)
        => MockStatus.RemoveMaterialFromAllQueues(materialId);

    public List<JobAndDecrementQuantity> DecrementJobQuantites(string loadDecrementsStrictlyAfterDecrementId)
    {
      throw new NotImplementedException();
    }

    public List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
    {
      throw new NotImplementedException();
    }

    public IOldJobDecrement OldJobDecrement()
    {
      return this;
    }

    protected void OnNewStatus(CurrentStatus s)
    {
      OnNewCurrentStatus?.Invoke(s);
    }

    public Dictionary<JobAndPath, int> OldDecrementJobQuantites()
    {
      throw new NotImplementedException();
    }

    public void OldFinalizeDecrement()
    {
      throw new NotImplementedException();
    }

    private void LoadEvents(string sampleDataPath, TimeSpan offset)
    {
      using (var file = System.IO.File.OpenRead(System.IO.Path.Combine(sampleDataPath, "events.json")))
      {
        var reader = new System.IO.StreamReader(file);
        var settings = new DataContractJsonSerializerSettings();
        settings.DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ssZ");
        settings.UseSimpleDictionaryFormat = true;
        var s = new DataContractJsonSerializer(typeof(BlackMaple.MachineWatchInterface.LogEntry), settings);
        while (reader.Peek() >= 0)
        {
          var evtJson = reader.ReadLine();
          using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(evtJson)))
          {
            var e = (BlackMaple.MachineWatchInterface.LogEntry)s.ReadObject(ms);
            var e2 = new BlackMaple.MachineWatchInterface.LogEntry(
                cntr: e.Counter,
                mat: e.Material,
                pal: e.Pallet,
                ty: e.LogType,
                locName: e.LocationName,
                locNum: e.LocationNum,
                prog: e.Program,
                start: e.StartOfCycle,
                endTime: e.EndTimeUTC.Add(offset),
                result: e.Result,
                endOfRoute: e.EndOfRoute,
                elapsed: e.ElapsedTime,
                active: e.ActiveOperationTime
            );
            LogDB.AddLogEntry(e2);
          }
        }
      }
    }

    private void LoadJobs(string sampleDataPath, TimeSpan offset)
    {
      using (var file = System.IO.File.OpenRead(System.IO.Path.Combine(sampleDataPath, "newjobs.json")))
      {
        var settings = new DataContractJsonSerializerSettings();
        settings.DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ssZ");
        settings.UseSimpleDictionaryFormat = true;
        var s = new DataContractJsonSerializer(typeof(List<BlackMaple.MachineWatchInterface.NewJobs>), settings);
        var allNewJobs = (List<BlackMaple.MachineWatchInterface.NewJobs>)s.ReadObject(file);

        foreach (var newJobs in allNewJobs)
        {
          foreach (var j in newJobs.Jobs)
          {
            OffsetJob(j, offset);
          }
          foreach (var su in newJobs.StationUse)
          {
            su.StartUTC = su.StartUTC.Add(offset);
            su.EndUTC = su.EndUTC.Add(offset);
          }
          foreach (var w in newJobs.CurrentUnfilledWorkorders)
          {
            w.DueDate = w.DueDate.Add(offset);
          }

          JobDB.AddJobs(newJobs, null);
        }
      }
    }

    public static void OffsetJob(JobPlan j, TimeSpan offset)
    {
      j.RouteStartingTimeUTC = j.RouteStartingTimeUTC.Add(offset);
      j.RouteEndingTimeUTC = j.RouteEndingTimeUTC.Add(offset);
      for (int proc = 1; proc <= j.NumProcesses; proc++)
      {
        for (int path = 1; path <= j.GetNumPaths(proc); path++)
        {
          j.SetSimulatedStartingTimeUTC(proc, path,
              j.GetSimulatedStartingTimeUTC(proc, path).Add(offset)
          );
          var prod = new List<JobPlan.SimulatedProduction>();
          foreach (var p in j.GetSimulatedProduction(proc, path))
          {
            prod.Add(new JobPlan.SimulatedProduction()
            {
              TimeUTC = p.TimeUTC.Add(offset),
              Quantity = p.Quantity,
            });
          }
        }
      }
      // not converted: hold patterns
    }

    private void LoadStatuses(string sampleDataPath, TimeSpan offset)
    {
    }
  }

  public class LogEntryGenerator
  {
    private Random rand = new Random();
    private JobLogDB db;
    private List<LogEntry> NewEntries = new List<LogEntry>();

    public LogEntryGenerator(JobLogDB d) => db = d;

    ///Take all the created log entries and add them to the database in sorted order
    public void AddEntriesToDatabase()
    {
      foreach (var e in NewEntries.OrderBy(x => x.EndTimeUTC))
      {
        if (e.LogType == LogType.PartMark)
          db.RecordSerialForMaterialID(e.Material.FirstOrDefault(), e.Result, e.EndTimeUTC);
        else if (e.LogType == LogType.OrderAssignment)
          db.RecordWorkorderForMaterialID(e.Material.FirstOrDefault(), e.Result, e.EndTimeUTC);
        else if (e.LogType == LogType.FinalizeWorkorder)
          db.RecordFinalizedWorkorder(e.Result, e.EndTimeUTC);
        else
          db.AddLogEntry(e);
      }
    }

    public void AddMonthOfCycles(DateTime month, string uniq, string part, string pal, int machine, double active, double time)
    {
      var workPrefix = "work" + part;
      var workCounter = 1;
      var workRemaining = rand.Next(3, 20);

      DateTime cur = month.AddMinutes(RandomCycleTime(time));
      while (cur < month.AddMonths(1))
      {
        LogMaterial mat;
        (mat, cur) = AddSinglePartCycle(cur, uniq, part, pal, machine, active, time);

        AddWorkorder(workPrefix + "-" + workCounter.ToString(), mat, cur);
        cur = cur.AddSeconds(5);
        workRemaining -= 1;
        if (workRemaining == 0)
        {
          FinalizeWorkorder(workPrefix + "-" + workCounter.ToString(), cur);
          cur = cur.AddSeconds(5);
          workRemaining = rand.Next(3, 30);
          workCounter += 1;
        }

        AddInspection(part, pal, machine, mat, cur);
        cur = cur.AddMinutes(1);
      }
    }

    private (LogMaterial, DateTime) AddSinglePartCycle(DateTime cur, string uniq, string part, string pal, int machine, double active, double time)
    {
      DateTime start = cur;
      var mat = new LogMaterial(matID: GetMatId(uniq), uniq: uniq, proc: 1, part: part, numProc: 1);
      AddLoad(mat, 5, pal, 1, ref cur);
      //5 minutes for transfer
      cur = cur.AddMinutes(RandomCycleTime(5));
      AddMachine(mat, time, pal, machine, active, ref cur);
      cur = cur.AddMinutes(RandomCycleTime(5));
      AddUnload(mat, 5, pal, 1, ref cur);
      AddPallet(pal, cur.Subtract(start), cur);
      cur = cur.AddSeconds(5);
      AddSerial(part, mat, cur);
      cur = cur.AddSeconds(5);
      return (mat, cur);
    }

    private double RandomCycleTime(double mean)
    {
      double u1 = 1.0 - rand.NextDouble(); //uniform(0,1] random doubles
      double u2 = 1.0 - rand.NextDouble();
      double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) *
                   Math.Sin(2.0 * Math.PI * u2); //random normal(0,1)
      double randNormal =
                   mean + 5 * randStdNormal; //random normal(mean,stdDev^2)
      return randNormal > 1 ? randNormal : 1;
    }

    private string RandomString(int length)
    {
      const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
      return new string(Enumerable.Repeat(chars, length)
          .Select(s => s[rand.Next(s.Length)]).ToArray());
    }


    private int GetMatId(string uniq)
    {
      return (int)db.AllocateMaterialID(uniq);
    }

    private void AddLoad(LogMaterial mat, double cycle, string pal, int stat, ref DateTime cur)
    {
      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { mat },
          pal: pal,
          ty: LogType.LoadUnloadCycle,
          locName: "Load",
          locNum: stat,
          prog: "prog1",
          start: true,
          endTime: cur,
          result: "LOAD",
          endOfRoute: false));

      var elap = RandomCycleTime(cycle);
      cur = cur.AddMinutes(elap);

      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { mat },
          pal: pal,
          ty: LogType.LoadUnloadCycle,
          locName: "Load",
          locNum: stat,
          prog: "prog1",
          start: false,
          endTime: cur,
          result: "LOAD",
          endOfRoute: false,
          elapsed: TimeSpan.FromMinutes(elap),
          active: TimeSpan.FromMinutes(3.5)
          ));
    }

    private void AddUnload(LogMaterial mat, double cycle, string pal, int stat, ref DateTime cur)
    {
      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { mat },
          pal: pal,
          ty: LogType.LoadUnloadCycle,
          locName: "Load",
          locNum: stat,
          prog: "prog1",
          start: true,
          endTime: cur,
          result: "UNLOAD",
          endOfRoute: false));

      var elap = RandomCycleTime(cycle);
      cur = cur.AddMinutes(elap);

      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { mat },
          pal: pal,
          ty: LogType.LoadUnloadCycle,
          locName: "Load",
          locNum: stat,
          prog: "prog1",
          start: false,
          endTime: cur,
          result: "UNLOAD",
          endOfRoute: true,
          elapsed: TimeSpan.FromMinutes(elap),
          active: TimeSpan.Zero
          ));
    }

    private void AddMachine(LogMaterial mat, double cycle, string pal, int stat, double active, ref DateTime cur)
    {
      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { mat },
          pal: pal,
          ty: LogType.MachineCycle,
          locName: "MC",
          locNum: stat,
          prog: "prog1",
          start: true,
          endTime: cur,
          result: "",
          endOfRoute: false));

      var elap = RandomCycleTime(cycle);
      cur = cur.AddMinutes(elap);

      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { mat },
          pal: pal,
          ty: LogType.MachineCycle,
          locName: "MC",
          locNum: stat,
          prog: "prog1",
          start: false,
          endTime: cur,
          result: "",
          endOfRoute: false,
          elapsed: TimeSpan.FromMinutes(elap),
          active: TimeSpan.FromMinutes(active)
          ));
    }

    private void AddPallet(string pal, TimeSpan elapsed, DateTime cur)
    {
      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { },
          pal: pal,
          ty: LogType.PalletCycle,
          locName: "Pallet Cycle",
          locNum: 1,
          prog: "PalletCycle",
          start: false,
          endTime: cur,
          result: "",
          endOfRoute: false,
          elapsed: elapsed,
          active: TimeSpan.Zero
          ));
    }

    private void AddInspection(string part, string pallet, int machine, LogMaterial mat, DateTime cur)
    {
      bool result = rand.NextDouble() < 0.2;

      var ty = new InspectionType
      {
        Name = "MyInspection",
        TrackPartName = true,
        TrackPalletName = true,
        TrackStationName = true,
        DefaultCountToTriggerInspection = 10,
        DefaultDeadline = TimeSpan.Zero,
        DefaultRandomFreq = -1,
        Overrides = new List<InspectionFrequencyOverride>()
      };
      var insp = ty.ConvertToJobInspection(part, 1);

      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { mat },
          pal: "",
          ty: LogType.Inspection,
          locName: "Inspect",
          locNum: 1,
          prog: insp.Counter
              .Replace(JobInspectionData.StationFormatFlag(1, 1), machine.ToString())
              .Replace(JobInspectionData.PalletFormatFlag(1), pallet),
          start: false,
          endTime: cur,
          result: result.ToString(),
          endOfRoute: false
          ));
    }

    private void AddSerial(string pal, LogMaterial mat, DateTime cur)
    {
      var result = JobLogDB.ConvertToBase62(mat.MaterialID);
      result = result.PadLeft(5, '0');
      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { mat },
          pal: pal,
          ty: LogType.PartMark,
          locName: "Mark",
          locNum: 1,
          prog: "MARK",
          start: false,
          endTime: cur,
          result: result,
          endOfRoute: false
          ));
    }

    private void AddWorkorder(string work, LogMaterial mat, DateTime cur)
    {
      if (work == null) return;
      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { mat },
          pal: "",
          ty: LogType.OrderAssignment,
          locName: "Order",
          locNum: 1,
          prog: "Order",
          start: false,
          endTime: cur,
          result: work,
          endOfRoute: false
          ));
    }

    private void FinalizeWorkorder(string work, DateTime cur)
    {
      NewEntries.Add(new LogEntry(
          cntr: 0,
          mat: new LogMaterial[] { },
          pal: "",
          ty: LogType.FinalizeWorkorder,
          locName: "FinalizeWorkorder",
          locNum: 1,
          prog: "FinalizeWorkorder",
          start: false,
          endTime: cur,
          result: work,
          endOfRoute: false
          ));
    }
  }

  public class MockCurrentStatus
  {
    private JobDB _jobDb;
    private Dictionary<string, CurrentStatus> Statuses {get;} = new Dictionary<string, CurrentStatus>();
    private CurrentStatus CurrentStatus {get;set;}

    public MockCurrentStatus(JobDB jobDB, string sampleDataPath, TimeSpan offset)
    {
      _jobDb = jobDB;
      var settings = new DataContractJsonSerializerSettings();
      settings.DateTimeFormat = new DateTimeFormat("yyyy-MM-ddTHH:mm:ssZ");
      settings.UseSimpleDictionaryFormat = true;
      var s = new DataContractJsonSerializer(typeof(BlackMaple.MachineWatchInterface.CurrentStatus), settings);

      var files = System.IO.Directory.GetFiles(sampleDataPath, "status-*.json");
      foreach (var f in files)
      {
        var name = System.IO.Path.GetFileNameWithoutExtension(f).Replace("status-", "");

        using (var file = System.IO.File.OpenRead(f)) {
          var curSt = (BlackMaple.MachineWatchInterface.CurrentStatus)s.ReadObject(file);
          foreach (var uniq in curSt.Jobs.Keys) {
            MockServerBackend.OffsetJob(curSt.Jobs[uniq], offset);
          }
          Statuses.Add(name, curSt);
        }
      }

      string statusFromEnv = System.Environment.GetEnvironmentVariable("BMS_CURRENT_STATUS");
      if (string.IsNullOrEmpty(statusFromEnv) || !Statuses.ContainsKey(statusFromEnv))
      {
        CurrentStatus = Statuses.OrderBy(st => st.Key).First().Value;
      } else {
        CurrentStatus = Statuses[statusFromEnv];
      }
    }

    public CurrentStatus GetCurrentStatus()
    {
      return CurrentStatus;
    }

    public CurrentStatus BuildCurrentStatus()
    {
      var status = _jobDb.LoadMostRecentSchedule();
      var jobsByPart = status.Jobs.ToDictionary(j => j.PartName, j => j);

      //aaa
      var aaa = new InProcessJob(jobsByPart["aaa"]);
      aaa.SetCompleted(1, 1, 5);
      aaa.SetCompleted(2, 1, 3);

      //bbb
      var bbb = new InProcessJob(jobsByPart["bbb"]);
      bbb.SetCompleted(1, 1, 11);
      bbb.SetCompleted(2, 1, 6);

      //ccc
      var ccc = new InProcessJob(jobsByPart["ccc"]);
      ccc.SetCompleted(1, 1, 8);
      ccc.SetCompleted(2, 1, 0);

      //xxx
      var xxx = new InProcessJob(jobsByPart["xxx"]);
      xxx.SetCompleted(1, 1, 3);

      //yyy
      var yyy = new InProcessJob(jobsByPart["yyy"]);
      yyy.SetCompleted(1, 1, 7);

      //xxx
      var zzz = new InProcessJob(jobsByPart["zzz"]);
      zzz.SetCompleted(1, 1, 0);

      //pallets

      //1, 2, 3, 4 can go to load1,2 and machine 1, 2, 3, 4
      //5, 6, 7, 8 can also go everywhere

      //pallet 1 at load 1
      //pallet 2 on cart
      //pallet 3 in buffer
      //pallet 4 at machine 1
      //pallet 5 at load 2
      //pallet 6 at machine 2
      //pallet 7 at machine 2 queue
      //pallet 8 at machine 3

      var pal1 = new PalletStatus()
      {
        Pallet = "1",
        FixtureOnPallet = "fix1",
        NumFaces = 2,
        CurrentPalletLocation = new PalletLocation(PalletLocationEnum.LoadUnload, "Load", 1),
        NewFixture = "newfix1"
      };
      var pal2 = new PalletStatus()
      {
        Pallet = "2",
        NumFaces = 2,
        CurrentPalletLocation = new PalletLocation(PalletLocationEnum.Cart, "Cart", 1),
        TargetLocation = new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 2),
        PercentMoveCompleted = (decimal)0.45
      };
      var pal3 = new PalletStatus()
      {
        Pallet = "3",
        NumFaces = 2,
        CurrentPalletLocation = new PalletLocation(PalletLocationEnum.Buffer, "Buffer", 3),
      };
      var pal4 = new PalletStatus()
      {
        Pallet = "4",
        NumFaces = 2,
        CurrentPalletLocation = new PalletLocation(PalletLocationEnum.Machine, "MC", 1),
      };
      var pal5 = new PalletStatus()
      {
        Pallet = "5",
        NumFaces = 1,
        CurrentPalletLocation = new PalletLocation(PalletLocationEnum.LoadUnload, "Load", 2),
      };
      var pal6 = new PalletStatus()
      {
        Pallet = "6",
        NumFaces = 1,
        CurrentPalletLocation = new PalletLocation(PalletLocationEnum.Machine, "MC", 2),
      };
      var pal7 = new PalletStatus()
      {
        Pallet = "7",
        NumFaces = 1,
        CurrentPalletLocation = new PalletLocation(PalletLocationEnum.MachineQueue, "MC", 2),
      };
      var pal8 = new PalletStatus()
      {
        Pallet = "8",
        NumFaces = 1,
        CurrentPalletLocation = new PalletLocation(PalletLocationEnum.Machine, "MC", 3),
      };

      //pallet 1 unloading a completed aaa-2, moving aaa-1 to aaa-2, and loading a new aaa-1
      //pallet 2 on cart has a completed bbb-1 and bbb-2
      //pallet 3 is empty
      //pallet 4 is machining a ccc-2 and has a ccc-1 also on the pallet
      //pallet 5 at load 2, unloading a completed xxx and loading a zzz on pallet 5
      //pallet 6 is machining a zzz
      //pallet 7 has an unmachined yyy (yyy has 2 per pallet)
      //pallet 8 is machining a xxx

      var mats = new List<InProcessMaterial> {

                //pallet 1 at load station

                //unload completed aaa-2
                new InProcessMaterial() {
                    MaterialID = 10,
                    JobUnique = "aaa-schId1234",
                    PartName = "aaa",
                    Process = 2,
                    Path = 1,
                    Serial = "ABC123",
                    SignaledInspections = new List<string> {"insp1", "insp2"},
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "1",
                        Face = 2
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
                    }
                },
                //transfer from aaa-1 to aaa-2
                new InProcessMaterial() {
                    MaterialID = 11,
                    JobUnique = "aaa-schId1234",
                    PartName = "aaa",
                    Process = 1,
                    Path = 1,
                    Serial = "ABC987",
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "1",
                        Face = 1
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Loading,
                        LoadOntoPallet = "1",
                        LoadOntoFace = 2,
                        ProcessAfterLoad = 2,
                        PathAfterLoad = 1
                    }
                },
                //load new aaa-1
                new InProcessMaterial() {
                    MaterialID = -1,
                    JobUnique = "aaa-schId1234",
                    PartName = "aaa",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.Free,
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Loading,
                        LoadOntoPallet = "1",
                        LoadOntoFace = 1,
                        ProcessAfterLoad = 1,
                        PathAfterLoad = 1
                    }
                },

                //pallet 2 on cart has bbb1 and bbb2
                new InProcessMaterial() {
                    MaterialID = 12,
                    JobUnique = "bbb-schId1234",
                    PartName = "bbb",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "2",
                        Face = 1
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Waiting,
                    }
                },
                new InProcessMaterial() {
                    MaterialID = 13,
                    JobUnique = "bbb-schId1234",
                    PartName = "bbb",
                    Process = 2,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "2",
                        Face = 2
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Waiting,
                    }
                },

                //pallet 3 is empty

                //pallet 4 is machining a ccc-2 and has a ccc-1
                new InProcessMaterial() {
                    MaterialID = 14,
                    JobUnique = "ccc-schId1234",
                    PartName = "ccc",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "4",
                        Face = 1
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Waiting
                    }
                },
                new InProcessMaterial() {
                    MaterialID = 15,
                    JobUnique = "ccc-schId1234",
                    PartName = "ccc",
                    Process = 2,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "4",
                        Face = 2
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Machining,
                        Program = "cccprog2",
                        ElapsedMachiningTime = TimeSpan.FromMinutes(10),
                        ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(20)
                    }
                },

                //pallet 5 at load station

                //unload xxx
                new InProcessMaterial() {
                    MaterialID = 16,
                    JobUnique = "xxx-schId1234",
                    PartName = "xxx",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "5",
                        Face = 1
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.UnloadToCompletedMaterial,
                    }
                },
                //load new zzz
                new InProcessMaterial() {
                    MaterialID = -1,
                    JobUnique = "zzz-schId1234",
                    PartName = "zzz",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.Free,
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Loading,
                        LoadOntoPallet = "5",
                        LoadOntoFace = 1,
                        ProcessAfterLoad = 1,
                        PathAfterLoad = 1
                    }
                },

                //pallet 6 machining zzz
                new InProcessMaterial() {
                    MaterialID = 17,
                    JobUnique = "zzz-schId1234",
                    PartName = "zzz",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "6",
                        Face = 1
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Machining,
                        Program = "zzzprog",
                        ElapsedMachiningTime = TimeSpan.FromMinutes(30),
                        ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(2)
                    }
                },

                //pallet 7 is at machine queue with yyy (two parts)
                new InProcessMaterial() {
                    MaterialID = 18,
                    JobUnique = "yyy-schId1234",
                    PartName = "yyy",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "7",
                        Face = 1
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Waiting,
                    }
                },
                new InProcessMaterial() {
                    MaterialID = 19,
                    JobUnique = "yyy-schId1234",
                    PartName = "yyy",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "7",
                        Face = 1
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Waiting,
                    }
                },

                //pallet 8 machining xxx
                new InProcessMaterial() {
                    MaterialID = 20,
                    JobUnique = "xxx-schId1234",
                    PartName = "xxx",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.OnPallet,
                        Pallet = "8",
                        Face = 1
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Machining,
                        Program = "xxxprog",
                        ElapsedMachiningTime = TimeSpan.FromMinutes(1),
                        ExpectedRemainingMachiningTime = TimeSpan.FromMinutes(45)
                    }
                },

                // some material in Queue1
                new InProcessMaterial() {
                    MaterialID = 100,
                    JobUnique = "xxx-schId1234",
                    PartName = "xxx",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.InQueue,
                        CurrentQueue = "Queue1",
                        QueuePosition = 1,
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Waiting,
                    }
                },
                new InProcessMaterial() {
                    MaterialID = 101,
                    JobUnique = "aaa-schId1234",
                    PartName = "aaa",
                    Process = 2,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.InQueue,
                        CurrentQueue = "Queue1",
                        QueuePosition = 2,
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Waiting,
                    }
                },

                // some material in Queue2
                new InProcessMaterial() {
                    MaterialID = 152,
                    JobUnique = "aaa-schId1234",
                    PartName = "aaa",
                    Process = 1,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.InQueue,
                        CurrentQueue = "Queue2",
                        QueuePosition = 1,
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Waiting,
                    }
                },
                new InProcessMaterial() {
                    MaterialID = 200,
                    JobUnique = "ccc-schId1234",
                    PartName = "ccc",
                    Process = 2,
                    Path = 1,
                    Location = new InProcessMaterialLocation() {
                        Type = InProcessMaterialLocation.LocType.InQueue,
                        CurrentQueue = "Queue2",
                        QueuePosition = 2,
                    },
                    Action = new InProcessMaterialAction() {
                        Type = InProcessMaterialAction.ActionType.Waiting,
                    }
                }
            };


      var st = new CurrentStatus()
      {
        Jobs = {
                    {aaa.UniqueStr, aaa},
                    {bbb.UniqueStr, bbb},
                    {ccc.UniqueStr, ccc},
                    {xxx.UniqueStr, xxx},
                    {yyy.UniqueStr, yyy},
                    {zzz.UniqueStr, zzz}
                },
        Pallets = {
                    {"1", pal1},
                    {"2", pal2},
                    {"3", pal3},
                    {"4", pal4},
                    {"5", pal5},
                    {"6", pal6},
                    {"7", pal7},
                    {"8", pal8},
                },
        LatestScheduleId = aaa.ScheduleId,
      };
      st.QueueSizes["Queue1"] = new QueueSize();
      st.QueueSizes["Queue2"] = new QueueSize();
      foreach (var m in mats) st.Material.Add(m);
      return st;
    }

    public List<string> GetQueueNames()
    {
      return new List<string> {
                "queueaaaa",
                "queuebbbb"
            };
    }

    private List<long> aaaQueue = new List<long> {
            10,
            11,
            12
        };
    private List<long> bbbQueue = new List<long> {
            20,
            21,
        };

    public void AddUnprocessedMaterialToQueue(string jobUnique, int lastCompletedProcess, string queue, int position, string serial)
    {

    }

    public void SetMaterialInQueue(long materialId, string queue, int position)
    {
      aaaQueue.Remove(materialId);
      bbbQueue.Remove(materialId);
      if (queue == "queueaaa")
      {
        aaaQueue.Add(materialId);
      }
      else if (queue == "queuebbb")
      {
        bbbQueue.Add(materialId);
      }
    }

    public void RemoveMaterialFromAllQueues(long materialId)
    {
      aaaQueue.Remove(materialId);
      bbbQueue.Remove(materialId);
    }
  }
}