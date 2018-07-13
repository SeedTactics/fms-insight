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
using Newtonsoft.Json;

namespace DebugMachineWatchApiServer
{
  public static class DebugMockProgram
  {
    public static void Main()
    {
      System.Environment.SetEnvironmentVariable("FMS__InstructionFilePath",
      System.IO.Path.Combine(
          System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
          "../../../sample-instructions/"
      ));
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

    private Dictionary<string, CurrentStatus> Statuses {get;} = new Dictionary<string, CurrentStatus>();
    private CurrentStatus CurrentStatus {get;set;}

    private JsonSerializerSettings _jsonSettings;

    public event NewCurrentStatus OnNewCurrentStatus;

    public void Init(string dataDir, IConfig config, FMSSettings settings)
    {
      string path = null; // dataDir

      string dbFile(string f) => System.IO.Path.Combine(path, f + ".db");

      if (path != null)
      {
        if (System.IO.File.Exists(dbFile("log"))) System.IO.File.Delete(dbFile("log"));
        LogDB = new JobLogDB();
        LogDB.Open(dbFile("log"), dbFile("insp"));

        if (System.IO.File.Exists(dbFile("job"))) System.IO.File.Delete(dbFile("job"));
        JobDB = new JobDB();
        JobDB.Open(dbFile("job"));
      }
      else
      {
        var conn = SqliteExtensions.ConnectMemory();
        conn.Open();
        LogDB = new JobLogDB(conn);
        LogDB.CreateTables(firstMaterialId: null);

        conn = SqliteExtensions.ConnectMemory();
        conn.Open();
        JobDB = new JobDB(conn);
        JobDB.CreateTables();
      }

      _jsonSettings = new JsonSerializerSettings();
      _jsonSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
      _jsonSettings.Converters.Add(new BlackMaple.MachineFramework.TimespanConverter());
      _jsonSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
      _jsonSettings.ConstructorHandling = Newtonsoft.Json.ConstructorHandling.AllowNonPublicDefaultConstructor;

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
      LoadStatus(sampleDataPath, offset);
    }

    public void Halt()
    {
      JobDB.Close();
      LogDB.Close();
    }

    public IInspectionControl InspectionControl()
    {
      return LogDB;
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
      return CurrentStatus;
    }

    public List<string> CheckValidRoutes(IEnumerable<JobPlan> newJobs)
    {
      return new List<string>();
    }

    public void AddJobs(NewJobs jobs, string expectedPreviousScheduleId)
    {
      JobDB.AddJobs(jobs, expectedPreviousScheduleId);
    }

    public void AddUnallocatedCastingToQueue(string part, string queue, int position, string serial)
    {
    }

    public void AddUnprocessedMaterialToQueue(string jobUnique, int lastCompletedProcess, string queue, int position, string serial)
    {
    }
    public void SetMaterialInQueue(long materialId, string queue, int position)
    {
    }
    public void RemoveMaterialFromAllQueues(long materialId)
    {
    }

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
      var files = System.IO.Directory.GetFiles(sampleDataPath, "events-*.json");
      var evts = new List<BlackMaple.MachineWatchInterface.LogEntry>();
      foreach (var f in files)
      {
        using (var file = System.IO.File.OpenRead(f))
        {
          var reader = new System.IO.StreamReader(file);
          while (reader.Peek() >= 0)
          {
            var evtJson = reader.ReadLine();
            var e = (BlackMaple.MachineWatchInterface.LogEntry)JsonConvert.DeserializeObject(
              evtJson,
              typeof(BlackMaple.MachineWatchInterface.LogEntry),
              _jsonSettings
            );
            evts.Add(e);
          }
        }
      }

      foreach (var e in evts.OrderBy(e => e.EndTimeUTC))
      {
        foreach (var m in e.Material) {
          var matDetails = LogDB.GetMaterialDetails(m.MaterialID);
          if (matDetails == null && !string.IsNullOrEmpty(m.JobUniqueStr)) {
            LogDB.CreateMaterialID(m.MaterialID, m.JobUniqueStr, m.PartName, m.NumProcesses);
          }
        }
        if (e.LogType == LogType.PartMark) {
          foreach (var m in e.Material)
            LogDB.RecordSerialForMaterialID(m, e.Result, e.EndTimeUTC.Add(offset));
        } else if (e.LogType == LogType.OrderAssignment) {
          foreach (var m in e.Material)
            LogDB.RecordWorkorderForMaterialID(m, e.Result, e.EndTimeUTC.Add(offset));
        } else if (e.LogType == LogType.FinalizeWorkorder) {
          LogDB.RecordFinalizedWorkorder(e.Result, e.EndTimeUTC.Add(offset));
        } else {
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
          if (e.ProgramDetails != null) {
            foreach (var x in e.ProgramDetails)
              e2.ProgramDetails.Add(x.Key, x.Value);
          }
          LogDB.AddLogEntryFromUnitTest(e2);
        }
      }
    }

    private void LoadJobs(string sampleDataPath, TimeSpan offset)
    {
      var newJobsJson = System.IO.File.ReadAllText(
        System.IO.Path.Combine(sampleDataPath, "newjobs.json"));
      var allNewJobs = (List<BlackMaple.MachineWatchInterface.NewJobs>)JsonConvert.DeserializeObject(
        newJobsJson,
        typeof(List<BlackMaple.MachineWatchInterface.NewJobs>),
        _jsonSettings
      );

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

    private void LoadStatus(string sampleDataPath, TimeSpan offset)
    {
      var files = System.IO.Directory.GetFiles(sampleDataPath, "status-*.json");
      foreach (var f in files)
      {
        var name = System.IO.Path.GetFileNameWithoutExtension(f).Replace("status-", "");

        var statusJson = System.IO.File.ReadAllText(f);
        var curSt = (BlackMaple.MachineWatchInterface.CurrentStatus)JsonConvert.DeserializeObject(
          statusJson,
          typeof(BlackMaple.MachineWatchInterface.CurrentStatus),
          _jsonSettings
        );

        foreach (var uniq in curSt.Jobs.Keys) {
          MockServerBackend.OffsetJob(curSt.Jobs[uniq], offset);
        }
        Statuses.Add(name, curSt);
      }

      string statusFromEnv = System.Environment.GetEnvironmentVariable("BMS_CURRENT_STATUS");
      if (string.IsNullOrEmpty(statusFromEnv) || !Statuses.ContainsKey(statusFromEnv))
      {
        CurrentStatus = Statuses.OrderBy(st => st.Key).First().Value;
      } else {
        CurrentStatus = Statuses[statusFromEnv];
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
  }
}