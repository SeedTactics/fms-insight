/* Copyright (c) 2021, John Lenz

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
using System.Reflection;
using System.IO;
using System.Linq;
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Newtonsoft.Json;
using Germinate;
using System.Collections.Immutable;

namespace DebugMachineWatchApiServer
{
  public static class DebugMockProgram
  {
    public static string InsightBackupDbFile = null;

    private static void AddOpenApiDoc(IServiceCollection services)
    {
      services.AddOpenApiDocument(cfg =>
      {
        cfg.Title = "SeedTactic FMS Insight";
        cfg.Description = "API for access to FMS Insight for flexible manufacturing system control";
        cfg.Version = "1.14";
        var settings = new Newtonsoft.Json.JsonSerializerSettings();
        BlackMaple.MachineFramework.Startup.NewtonsoftJsonSettings(settings);
        cfg.SerializerSettings = settings;
        //cfg.DefaultReferenceTypeNullHandling = NJsonSchema.ReferenceTypeNullHandling.NotNull;
        cfg.DefaultResponseReferenceTypeNullHandling = NJsonSchema
          .Generation
          .ReferenceTypeNullHandling
          .NotNull;
        cfg.RequireParametersWithoutDefault = true;
        cfg.IgnoreObsoleteProperties = true;
      });
    }

    public static void Main()
    {
      var cfg = new ConfigurationBuilder().AddEnvironmentVariables().Build();

      var serverSettings = ServerSettings.Load(cfg);

      var fmsSettings = new FMSSettings(cfg);
      fmsSettings.InstructionFilePath = System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        "../../../sample-instructions/"
      );
      fmsSettings.QuarantineQueue = "Initial Quarantine";
      fmsSettings.RequireScanAtCloseout = true;
      fmsSettings.AllowChangeWorkorderAtLoadStation = true;

      BlackMaple.MachineFramework.InsightLogging.EnableSerilog(
        serverSt: serverSettings,
        enableEventLog: false
      );

      var backend = new MockServerBackend(fmsSettings);
      var fmsImpl = new FMSImplementation()
      {
        Backend = backend,
        Name = "mock",
        Version = "1.2.3.4",
        UsingLabelPrinterForSerials = true,
        PrintLabel = (matId, process, httpReferer) =>
        {
          int loadStation = -10;
          if (
            httpReferer.Segments.Length == 4
            && httpReferer.Segments[0] == "/"
            && httpReferer.Segments[1] == "station/"
            && httpReferer.Segments[2] == "loadunload/"
            && int.TryParse(httpReferer.Segments[3], out var lul)
          )
          {
            loadStation = lul;
          }
          Serilog.Log.Information(
            "Print label for {matId} {process} {referer} and {load}",
            matId,
            process,
            httpReferer,
            loadStation
          );
        },
        ParseBarcode = (barcode, referer) =>
        {
          Serilog.Log.Information("Parsing barcode {barcode} {referer}", barcode, referer);
          System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));
          var commaIdx = barcode.IndexOf(',');
          if (commaIdx >= 0)
            barcode = barcode.Substring(0, commaIdx);
          using (var conn = backend.RepoConfig.OpenConnection())
          {
            var mats = conn.GetMaterialDetailsForSerial(barcode);
            if (mats.Count > 0)
            {
              return new ScannedMaterial() { ExistingMaterial = mats[mats.Count - 1] };
            }
            else
            {
              return new ScannedMaterial()
              {
                Casting = new ScannedCasting()
                {
                  Serial = barcode,
                  Workorder = "work1",
                  PossibleCastings = ImmutableList.Create("part1", "part2")
                },
              };
            }
          }
        },
        AddRawMaterial = AddRawMaterialType.RequireBarcodeScan,
        AddInProcessMaterial = AddInProcessMaterialType.RequireExistingMaterial
      };

      var hostB = BlackMaple.MachineFramework.Program.CreateHostBuilder(
        cfg,
        serverSettings,
        fmsSettings,
        fmsImpl,
        useService: false
      );

      var webroot = Environment.GetEnvironmentVariable("BMS_WEBROOT");
      if (!string.IsNullOrEmpty(webroot))
      {
        hostB.ConfigureWebHostDefaults(webBuilder =>
        {
          webBuilder.UseWebRoot(webroot);
        });
      }

      hostB.ConfigureServices((_, services) => AddOpenApiDoc(services));

      hostB.Build().Run();
    }
  }

  public sealed class MockServerBackend
    : IFMSBackend,
      IMachineControl,
      IJobControl,
      IQueueControl,
      IDisposable
  {
    public RepositoryConfig RepoConfig { get; private set; }

    private Dictionary<string, CurrentStatus> Statuses { get; } = new Dictionary<string, CurrentStatus>();
    private CurrentStatus CurrentStatus { get; set; }
    private ImmutableList<ToolInMachine> Tools { get; set; }
    private string _tempDbFile;

    private class MockProgram
    {
      public string ProgramName { get; set; }
      public long? Revision { get; set; }
      public string Comment { get; set; }
      public string CellControllerProgramName { get; set; }
    }

    private List<MockProgram> Programs { get; set; }

    private JsonSerializerSettings _jsonSettings;

    public event NewCurrentStatus OnNewCurrentStatus;
    public event NewJobsDelegate OnNewJobs;
    public event EditMaterialInLogDelegate OnEditMaterialInLog;

    public bool AllowQuarantineToCancelLoad { get; } = true;

    private readonly FMSSettings _fmsSettings;

    public MockServerBackend(FMSSettings settings)
    {
      _fmsSettings = settings;
      _jsonSettings = new JsonSerializerSettings();
      _jsonSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
      _jsonSettings.Converters.Add(new BlackMaple.MachineFramework.TimespanConverter());
      _jsonSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
      _jsonSettings.ConstructorHandling = Newtonsoft
        .Json
        .ConstructorHandling
        .AllowNonPublicDefaultConstructor;

      if (DebugMockProgram.InsightBackupDbFile != null)
      {
        RepoConfig = RepositoryConfig.InitializeEventDatabase(
          new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() },
          DebugMockProgram.InsightBackupDbFile
        );
        LoadStatusFromLog(System.IO.Path.GetDirectoryName(DebugMockProgram.InsightBackupDbFile));
      }
      else
      {
        _tempDbFile = System.IO.Path.GetTempFileName();
        System.IO.File.Delete(_tempDbFile);
        RepoConfig = RepositoryConfig.InitializeEventDatabase(
          new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() },
          _tempDbFile
        );

        // sample data starts at Jan 1, 2018.  Need to offset to current month
        var jan1_18 = new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var offset = DateTime.UtcNow.AddDays(-28).Subtract(jan1_18);

        var sampleDataPath = System.IO.Path.Combine(
          System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
          "../../../sample-data/"
        );

        LoadEvents(sampleDataPath, offset);
        LoadJobs(sampleDataPath, offset);
        LoadStatus(sampleDataPath, offset);
        LoadTools(sampleDataPath);
        LoadPrograms(sampleDataPath);
      }
    }

    public void Dispose()
    {
      Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
      if (_tempDbFile != null)
      {
        System.IO.File.Delete(_tempDbFile);
      }
    }

    public IJobControl JobControl
    {
      get => this;
    }
    public IQueueControl QueueControl => this;

    public IMachineControl MachineControl => this;

    private long _curStatusLoadCount = 0;

    public CurrentStatus GetCurrentStatus()
    {
      _curStatusLoadCount += 1;
      if (_curStatusLoadCount % 5 == 0)
      {
        if (CurrentStatus.Alarms.Count > 0)
        {
          CurrentStatus = CurrentStatus with { Alarms = ImmutableList<string>.Empty };
        }
        else
        {
          CurrentStatus = CurrentStatus with
          {
            Alarms = ImmutableList.Create("Test alarm " + _curStatusLoadCount.ToString(), "Another alarm")
          };
        }
      }

      return CurrentStatus;
    }

    public void RecalculateCellState() { }

    public List<string> CheckValidRoutes(IEnumerable<Job> newJobs)
    {
      return new List<string>();
    }

    public void AddJobs(NewJobs jobs, string expectedPreviousScheduleId)
    {
      using var LogDB = RepoConfig.OpenConnection();
      LogDB.AddJobs(jobs, expectedPreviousScheduleId, addAsCopiedToSystem: true);
      OnNewJobs?.Invoke(jobs);
    }

    public void SetJobComment(string jobUnique, string comment)
    {
      using var LogDB = RepoConfig.OpenConnection();
      Serilog.Log.Information("Setting comment for {job} to {comment}", jobUnique, comment);
      LogDB.SetJobComment(jobUnique, comment);
      CurrentStatus = CurrentStatus.Produce(draft =>
      {
        if (draft.Jobs.ContainsKey(jobUnique))
        {
          draft.Jobs[jobUnique] = draft.Jobs[jobUnique] with { Comment = comment };
        }
      });
      OnNewCurrentStatus?.Invoke(CurrentStatus);
    }

    public List<InProcessMaterial> AddUnallocatedCastingToQueue(
      string casting,
      int qty,
      string queue,
      IList<string> serials,
      string workorder,
      string operatorName
    )
    {
      Serilog.Log.Information(
        "AddUnallocatedCastingToQueue: {casting} x{qty} {queue} {@serials} {workorder} {oper}",
        casting,
        qty,
        queue,
        serials,
        workorder,
        operatorName
      );
      var ret = new List<InProcessMaterial>();
      for (int i = 0; i < qty; i++)
      {
        var m = new InProcessMaterial()
        {
          MaterialID = -500,
          JobUnique = null,
          PartName = casting,
          Process = 0,
          Path = 1,
          Serial = i < serials.Count ? serials[i] : null,
          WorkorderId = workorder,
          Location = new InProcessMaterialLocation()
          {
            Type = InProcessMaterialLocation.LocType.InQueue,
            CurrentQueue = queue,
            QueuePosition = 10 + i
          },
          Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
          SignaledInspections = ImmutableList<string>.Empty
        };
        CurrentStatus %= st => st.Material.Add(m);
        ret.Add(m);
      }
      OnNewCurrentStatus?.Invoke(CurrentStatus);
      return ret;
    }

    public InProcessMaterial AddUnprocessedMaterialToQueue(
      string jobUnique,
      int lastCompletedProcess,
      string queue,
      int position,
      string serial,
      string operatorName = null
    )
    {
      Serilog.Log.Information(
        "AddUnprocessedMaterialToQueue: {unique} {lastCompProcess} {queue} {position} {serial} {oper}",
        jobUnique,
        lastCompletedProcess,
        queue,
        position,
        serial,
        operatorName
      );

      var part = CurrentStatus.Jobs.TryGetValue(jobUnique, out var job) ? job.PartName : "";
      var m = new InProcessMaterial()
      {
        MaterialID = -500,
        JobUnique = jobUnique,
        PartName = part,
        Process = lastCompletedProcess,
        Path = 1,
        Serial = serial,
        Location = new InProcessMaterialLocation()
        {
          Type = InProcessMaterialLocation.LocType.InQueue,
          CurrentQueue = queue,
          QueuePosition = position
        },
        Action = new InProcessMaterialAction() { Type = InProcessMaterialAction.ActionType.Waiting },
        SignaledInspections = ImmutableList<string>.Empty
      };
      CurrentStatus %= st => st.Material.Add(m);
      OnNewCurrentStatus?.Invoke(CurrentStatus);
      return m;
    }

    public void SetMaterialInQueue(long materialId, string queue, int position, string operatorName = null)
    {
      Serilog.Log.Information(
        "SetMaterialInQueue {matId} {queue} {position} {oper}",
        materialId,
        queue,
        position,
        operatorName
      );

      var toMove = CurrentStatus.Material.FirstOrDefault(
        m => m.MaterialID == materialId && m.Location.Type == InProcessMaterialLocation.LocType.InQueue
      );
      if (toMove == null)
        return;

      // shift old downward
      CurrentStatus = CurrentStatus with
      {
        Material = CurrentStatus.Material
          .Select(m =>
          {
            int pos = m.Location.QueuePosition ?? 0;

            if (
              m.Location.Type == InProcessMaterialLocation.LocType.InQueue
              && m.Location.CurrentQueue == toMove.Location.CurrentQueue
              && pos > toMove.Location.QueuePosition
            )
            {
              pos -= 1;
            }
            if (
              m.Location.Type == InProcessMaterialLocation.LocType.InQueue
              && m.Location.CurrentQueue == queue
              && pos >= position
            )
            {
              pos += 1;
            }

            if (m.MaterialID == toMove.MaterialID)
            {
              return m with
              {
                Location = new()
                {
                  Type = InProcessMaterialLocation.LocType.InQueue,
                  CurrentQueue = queue,
                  QueuePosition = position
                }
              };
            }
            else
            {
              return m with { Location = m.Location with { QueuePosition = pos } };
            }
          })
          .ToImmutableList()
      };

      using (var LogDB = RepoConfig.OpenConnection())
      {
        LogDB.RecordAddMaterialToQueue(
          mat: new EventLogMaterial()
          {
            MaterialID = materialId,
            Process = 0,
            Face = ""
          },
          queue: queue,
          position: position,
          operatorName: operatorName,
          reason: "SetByOperator"
        );
      }

      OnNewStatus(CurrentStatus);
    }

    public void SignalMaterialForQuarantine(long materialId, string operatorName = null, string reason = null)
    {
      Serilog.Log.Information("SignalMatForQuarantine {matId} {oper}", materialId, operatorName);
      var mat = CurrentStatus.Material.FirstOrDefault(m => m.MaterialID == materialId);
      if (mat == null)
        throw new BadRequestException("Material does not exist");

      using (var LogDB = RepoConfig.OpenConnection())
      {
        LogDB.SignalMaterialForQuarantine(
          new EventLogMaterial()
          {
            MaterialID = materialId,
            Process = mat.Process,
            Face = ""
          },
          pallet: mat.Location.PalletNum ?? 0,
          queue: _fmsSettings.QuarantineQueue,
          operatorName: operatorName,
          reason: reason
        );
      }
    }

    public void RemoveMaterialFromAllQueues(IList<long> materialIds, string operatorName = null)
    {
      Serilog.Log.Information("RemoveMaterialFromAllQueues {@matId} {oper}", materialIds, operatorName);

      foreach (var materialId in materialIds)
      {
        var toRemove = CurrentStatus.Material.FirstOrDefault(
          m => m.MaterialID == materialId && m.Location.Type == InProcessMaterialLocation.LocType.InQueue
        );
        if (toRemove == null)
          return;

        // shift downward
        CurrentStatus %= st =>
        {
          for (int i = 0; i < st.Material.Count; i++)
          {
            var m = st.Material[i];
            if (
              m.Location.Type == InProcessMaterialLocation.LocType.InQueue
              && m.Location.CurrentQueue == toRemove.Location.CurrentQueue
              && m.Location.QueuePosition < toRemove.Location.QueuePosition
            )
            {
              st.Material[i] = m %= mat => mat.Location.QueuePosition -= 1;
            }
          }

          st.Material.Remove(toRemove);
        };
      }

      OnNewStatus(CurrentStatus);
    }

    public List<JobAndDecrementQuantity> DecrementJobQuantites(long loadDecrementsStrictlyAfterDecrementId)
    {
      throw new NotImplementedException();
    }

    public List<JobAndDecrementQuantity> DecrementJobQuantites(DateTime loadDecrementsAfterTimeUTC)
    {
      throw new NotImplementedException();
    }

    protected void OnNewStatus(CurrentStatus s)
    {
      OnNewCurrentStatus?.Invoke(s);
    }

    public ImmutableList<ToolInMachine> CurrentToolsInMachines()
    {
      System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
      return Tools;
    }

    public ImmutableList<ToolInMachine> CurrentToolsInMachine(string machineGroup, int machineNum)
    {
      System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
      return Tools
        .Where(t => t.MachineGroupName == machineGroup && t.MachineNum == machineNum)
        .ToImmutableList();
    }

    public ImmutableList<ProgramInCellController> CurrentProgramsInCellController()
    {
      System.Threading.Thread.Sleep(TimeSpan.FromSeconds(2));
      return Programs
        .Where(p => !string.IsNullOrEmpty(p.CellControllerProgramName))
        .Select(
          p =>
            new ProgramInCellController()
            {
              ProgramName = p.ProgramName,
              Revision = p.Revision,
              Comment = p.Comment,
              CellControllerProgramName = p.CellControllerProgramName,
            }
        )
        .ToImmutableList();
    }

    public ImmutableList<ProgramRevision> ProgramRevisionsInDecendingOrderOfRevision(
      string programName,
      int count,
      long? revisionToStart
    )
    {
      var start = revisionToStart.GetValueOrDefault(50);
      if (start - count < 0)
      {
        count = Math.Max(1, (int)(start - 3));
      }
      return Enumerable
        .Range(0, count)
        .Select(
          i =>
            new ProgramRevision()
            {
              ProgramName = programName,
              Revision = start - i,
              Comment = $"programName comment {start - i}",
              CellControllerProgramName = "cell " + programName
            }
        )
        .ToImmutableList();
    }

    public string GetProgramContent(string programName, long? revision)
    {
      return "Program Content for " + programName + " and revision " + revision.ToString();
    }

    private void LoadEvents(string sampleDataPath, TimeSpan offset)
    {
      var files = System.IO.Directory.GetFiles(sampleDataPath, "events-*.json");
      var evts = new List<LogEntry>();
      foreach (var f in files)
      {
        using (var file = System.IO.File.OpenRead(f))
        {
          var reader = new System.IO.StreamReader(file);
          while (reader.Peek() >= 0)
          {
            // replace empty pal "" with zero.  The pallet type changed to int and I didn't
            // want to regenerate all the sample data
            var evtJson = reader
              .ReadLine()
              .Replace("\"pal\":\"\"", "\"pal\":0")
              .Replace("\"pal\": \"\"", "\"pal\":0");
            var e = (LogEntry)JsonConvert.DeserializeObject(evtJson, typeof(LogEntry), _jsonSettings);
            evts.Add(e);
          }
        }
      }

      var tools = JsonConvert.DeserializeObject<Dictionary<long, List<ToolUse>>>(
        System.IO.File.ReadAllText(Path.Combine(sampleDataPath, "tool-use.json")),
        _jsonSettings
      );

      using var LogDB = RepoConfig.OpenConnection();
      foreach (var e in evts.OrderBy(e => e.EndTimeUTC))
      {
        foreach (var m in e.Material)
        {
          var matDetails = LogDB.GetMaterialDetails(m.MaterialID);
          if (matDetails == null && !string.IsNullOrEmpty(m.JobUniqueStr))
          {
            LogDB.CreateMaterialID(m.MaterialID, m.JobUniqueStr, m.PartName, m.NumProcesses);
          }
        }
        if (e.LogType == LogType.PartMark)
        {
          foreach (var m in e.Material)
            LogDB.RecordSerialForMaterialID(
              EventLogMaterial.FromLogMat(m),
              e.Result,
              e.EndTimeUTC.Add(offset)
            );
        }
        else if (e.LogType == LogType.OrderAssignment)
        {
          foreach (var m in e.Material)
            LogDB.RecordWorkorderForMaterialID(
              EventLogMaterial.FromLogMat(m),
              e.Result,
              e.EndTimeUTC.Add(offset)
            );
        }
        else if (e.LogType == LogType.WorkorderComment)
        {
          LogDB.RecordWorkorderComment(
            e.Result,
            e.ProgramDetails["Comment"],
            e.ProgramDetails["Operator"],
            e.EndTimeUTC.Add(offset)
          );
        }
        else
        {
          if (e.LogType == LogType.InspectionResult && e.Material.Any(m => m.MaterialID == 2965))
          {
            // ignore inspection complete
            continue;
          }
          var e2 = e.Produce(draft =>
          {
            draft.EndTimeUTC = draft.EndTimeUTC.Add(offset);
            if (tools.TryGetValue(e.Counter, out var usage))
            {
              foreach (var u in usage)
              {
                draft.Tools.Add(u);
              }
            }
          });
          ((Repository)LogDB).AddLogEntryFromUnitTest(e2);
        }
      }
    }

    private void LoadJobs(string sampleDataPath, TimeSpan offset)
    {
      var newJobsJson = System.IO.File.ReadAllText(System.IO.Path.Combine(sampleDataPath, "newjobs.json"));
      var allNewJobs =
        (List<NewJobs>)JsonConvert.DeserializeObject(newJobsJson, typeof(List<NewJobs>), _jsonSettings);

      using var LogDB = RepoConfig.OpenConnection();
      foreach (var newJobs in allNewJobs)
      {
        var newJobsOffset = newJobs.Produce(newJobsDraft =>
        {
          for (int i = 0; i < newJobs.Jobs.Count; i++)
          {
            newJobsDraft.Jobs[i] = OffsetJob(newJobsDraft.Jobs[i], offset);
          }

          for (int i = 0; i < newJobs.StationUse.Count; i++)
          {
            newJobsDraft.StationUse[i] %= draft =>
            {
              draft.StartUTC = draft.StartUTC.Add(offset);
              draft.EndUTC = draft.EndUTC.Add(offset);
            };
          }
          for (int i = 0; i < newJobs.CurrentUnfilledWorkorders.Count; i++)
          {
            newJobsDraft.CurrentUnfilledWorkorders[i] %= w => w.DueDate = w.DueDate.Add(offset);
          }
          if (newJobs.SimDayUsage != null)
          {
            for (int i = 0; i < newJobs.SimDayUsage.Count; i++)
            {
              var old = newJobsDraft.SimDayUsage[i];
              newJobsDraft.SimDayUsage[i] = old with
              {
                Day = old.Day.AddDays((int)Math.Round(offset.TotalDays))
              };
            }
          }
        });

        LogDB.AddJobs(newJobsOffset, null, addAsCopiedToSystem: true);
      }
    }

    private void LoadStatus(string sampleDataPath, TimeSpan offset)
    {
      var files = System.IO.Directory.GetFiles(sampleDataPath, "status-*.json");
      foreach (var f in files)
      {
        var name = System.IO.Path.GetFileNameWithoutExtension(f).Replace("status-", "");

        var statusJson = System.IO.File.ReadAllText(f);
        var curSt = (CurrentStatus)
          JsonConvert.DeserializeObject(statusJson, typeof(CurrentStatus), _jsonSettings);
        curSt = curSt with
        {
          TimeOfCurrentStatusUTC = curSt.TimeOfCurrentStatusUTC.Add(offset),
          Jobs = curSt.Jobs.Values
            .Select(
              j =>
                OffsetJob(j, offset).CloneToDerived<ActiveJob, Job>() with
                {
                  ScheduleId = j.ScheduleId,
                  CopiedToSystem = j.CopiedToSystem,
                  Decrements = j.Decrements,
                  Completed = j.Completed,
                  RemainingToStart = j.RemainingToStart,
                  Precedence = j.Precedence,
                  AssignedWorkorders = j.AssignedWorkorders,
                }
            )
            .ToImmutableDictionary(j => j.UniqueStr, j => j)
        };
        Statuses.Add(name, curSt);
      }

      string statusFromEnv = System.Environment.GetEnvironmentVariable("BMS_CURRENT_STATUS");
      if (string.IsNullOrEmpty(statusFromEnv) || !Statuses.ContainsKey(statusFromEnv))
      {
        CurrentStatus = Statuses.OrderBy(st => st.Key).First().Value;
      }
      else
      {
        CurrentStatus = Statuses[statusFromEnv];
      }
    }

    public static Job OffsetJob(Job originalJob, TimeSpan offset)
    {
      return originalJob with
      {
        RouteStartUTC = originalJob.RouteStartUTC.Add(offset),
        RouteEndUTC = originalJob.RouteEndUTC.Add(offset),
        Processes = originalJob.Processes
          .Select(
            p =>
              p with
              {
                Paths = p.Paths
                  .Select(
                    path =>
                      path with
                      {
                        SimulatedStartingUTC = path.SimulatedStartingUTC.Add(offset),
                        SimulatedProduction = path.SimulatedProduction
                          .Select(prod => prod with { TimeUTC = prod.TimeUTC.Add(offset) })
                          .ToImmutableList()
                      }
                  )
                  .ToImmutableList()
              }
          )
          .ToImmutableList()
      };
      // not converted: hold patterns
    }

    public void LoadTools(string sampleDataPath)
    {
      var json = System.IO.File.ReadAllText(Path.Combine(sampleDataPath, "tools.json"));
      Tools = JsonConvert.DeserializeObject<ImmutableList<ToolInMachine>>(json, _jsonSettings);
    }

    public void LoadPrograms(string sampleDataPath)
    {
      var programJson = System.IO.File.ReadAllText(Path.Combine(sampleDataPath, "programs.json"));
      Programs = JsonConvert.DeserializeObject<List<MockProgram>>(programJson, _jsonSettings);
    }

    public void LoadStatusFromLog(string logPath)
    {
      var file = System.IO.Directory.GetFiles(logPath, "*.txt").OrderByDescending(x => x).First();
      string lastAddSch = null;
      foreach (var line in System.IO.File.ReadLines(file))
      {
        if (line.Contains("@mt\":\"Adding new schedules for {@jobs}, mazak data is {@mazakData}\""))
        {
          lastAddSch = line;
        }
      }

      if (lastAddSch != null)
      {
        Programs = Newtonsoft.Json.Linq.JObject.Parse(lastAddSch)["mazakData"]["MainPrograms"]
          .Select(
            prog =>
              new MockProgram()
              {
                ProgramName = (string)prog["MainProgram"],
                CellControllerProgramName = (string)prog["MainProgram"],
                Comment = (string)prog["Comment"]
              }
          )
          .ToList();
      }
      else
      {
        Programs = new List<MockProgram>();
      }

      Tools = ImmutableList<ToolInMachine>.Empty;

      CurrentStatus = new CurrentStatus()
      {
        TimeOfCurrentStatusUTC = DateTime.UtcNow,
        Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
        Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
        Material = ImmutableList<InProcessMaterial>.Empty,
        Alarms = ImmutableList<string>.Empty,
        Queues = ImmutableDictionary<string, QueueInfo>.Empty
      };
    }

    public void SwapMaterialOnPallet(int pallet, long oldMatId, long newMatId, string operatorName = null)
    {
      using var LogDB = RepoConfig.OpenConnection();
      Serilog.Log.Information(
        "Swapping {oldMatId} to {newMatId} on pallet {pallet}",
        oldMatId,
        newMatId,
        pallet
      );
      var o = LogDB.SwapMaterialInCurrentPalletCycle(
        pallet: pallet,
        oldMatId: oldMatId,
        newMatId: newMatId,
        operatorName: operatorName,
        quarantineQueue: null
      );
      OnEditMaterialInLog?.Invoke(
        new EditMaterialInLogEvents()
        {
          OldMaterialID = oldMatId,
          NewMaterialID = newMatId,
          EditedEvents = o.ChangedLogEntries,
        }
      );
    }

    public void InvalidatePalletCycle(
      long matId,
      int process,
      string oldMatPutInQueue = null,
      string operatorName = null
    )
    {
      using var LogDB = RepoConfig.OpenConnection();
      Serilog.Log.Information("Invalidating {matId} process {process}", matId, process);
      var o = LogDB.InvalidatePalletCycle(
        matId: matId,
        process: process,
        oldMatPutInQueue: oldMatPutInQueue,
        operatorName: operatorName
      );
    }
  }
}
