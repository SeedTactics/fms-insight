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
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BlackMaple.MachineFramework;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Namotion.Reflection;
using NJsonSchema.Generation;

namespace DebugMachineWatchApiServer
{
  public static class DebugMockProgram
  {
    public static readonly string InsightBackupDbFile = null;

    public class RequiredModifierSchemaProcessor : ISchemaProcessor
    {
      public void Process(SchemaProcessorContext ctx)
      {
        foreach (var prop in ctx.ContextualType.Properties)
        {
          if (prop.PropertyInfo.DeclaringType != ctx.ContextualType.Type)
          {
            continue;
          }
          if (prop.GetAttribute<System.Runtime.CompilerServices.RequiredMemberAttribute>(false) != null)
          {
            string name = prop.Name;
            var jsonNameAttr = prop.GetAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>(
              false
            );
            if (jsonNameAttr != null)
            {
              name = jsonNameAttr.Name;
            }
            if (ctx.Schema.AllOf.Count > 1)
            {
              ctx.Schema.AllOf.ElementAt(1).RequiredProperties.Add(name);
            }
            else
            {
              ctx.Schema.RequiredProperties.Add(name);
            }
          }
        }
      }
    }

    public static void Main()
    {
      var cfg = new ConfigurationBuilder().AddEnvironmentVariables().Build();

      var serverSettings = ServerSettings.Load(cfg);

      InsightLogging.EnableSerilog(serverSt: serverSettings, enableEventLog: false);

      var fmsSettings = FMSSettings.Load(cfg) with
      {
        InstructionFilePath = Path.Combine(
          Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
          "../../../sample-instructions/"
        ),
        QuarantineQueue = "Initial Quarantine",
        RequireScanAtCloseout = true,
        AllowChangeWorkorderAtLoadStation = true,
        UsingLabelPrinterForSerials = true,
        AddRawMaterial = AddRawMaterialType.RequireBarcodeScan,
        AddInProcessMaterial = AddInProcessMaterialType.RequireExistingMaterial,
        RebookingPrefix = "RE:",
        RebookingsDisplayName = "DRebook",
      };

      string tempDbFile = null;

      var hostB = new HostBuilder()
        .UseContentRoot(Path.GetDirectoryName(Environment.ProcessPath))
        .ConfigureServices(s =>
        {
          s.AddSingleton<MockServerBackend>();
          s.AddSingleton<IMachineControl>(sp => sp.GetRequiredService<MockServerBackend>());
          s.AddSingleton<ISynchronizeCellState<DebugCellState>>(sp =>
            sp.GetRequiredService<MockServerBackend>()
          );
          s.AddSingleton<IPrintLabelForMaterial>(sp => sp.GetRequiredService<MockServerBackend>());
          s.AddSingleton<IParseBarcode>(sp => sp.GetRequiredService<MockServerBackend>());

          s.AddOpenApiDocument(cfg =>
          {
            cfg.Title = "SeedTactic FMS Insight";
            cfg.Description = "API for access to FMS Insight for flexible manufacturing system control";
            cfg.Version = "1.14";
            cfg.SchemaSettings.SchemaProcessors.Add(new RequiredModifierSchemaProcessor());
            cfg.DefaultResponseReferenceTypeNullHandling = NJsonSchema
              .Generation
              .ReferenceTypeNullHandling
              .NotNull;
            cfg.RequireParametersWithoutDefault = true;
            cfg.SchemaSettings.IgnoreObsoleteProperties = true;
          });

          if (InsightBackupDbFile != null)
          {
            s.AddRepository(
              InsightBackupDbFile,
              new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() }
            );
          }
          else
          {
            tempDbFile = Path.Combine(
              Path.GetTempPath(),
              "debug-mock-" + Path.GetRandomFileName() + ".sqlite"
            );
            s.AddRepository(
              tempDbFile,
              new SerialSettings() { ConvertMaterialIDToSerial = (id) => id.ToString() }
            );
          }
        })
        .AddFMSInsightWebHost(cfg, serverSettings, fmsSettings);

      var host = hostB.Build();

      host.Services.GetService<IHostApplicationLifetime>()
        .ApplicationStopped.Register(() =>
        {
          if (tempDbFile != null)
          {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(tempDbFile);
          }
        });

      host.Run();
    }
  }

  public record DebugCellState : ICellState
  {
    public required CurrentStatus CurrentStatus { get; init; }

    public required bool StateUpdated { get; init; }

    public required TimeSpan TimeUntilNextRefresh { get; init; }
  }

  public sealed class MockServerBackend
    : IMachineControl,
      ISynchronizeCellState<DebugCellState>,
      IPrintLabelForMaterial,
      IParseBarcode
  {
    public RepositoryConfig RepoConfig { get; private set; }

    private Dictionary<string, CurrentStatus> Statuses { get; } = new Dictionary<string, CurrentStatus>();
    private CurrentStatus CurrentStatus { get; set; }
    private ImmutableList<ToolInMachine> Tools { get; set; }

    private class MockProgram
    {
      public string ProgramName { get; set; }
      public long? Revision { get; set; }
      public string Comment { get; set; }
      public string CellControllerProgramName { get; set; }
    }

    private List<MockProgram> Programs { get; set; }

    private JsonSerializerOptions _jsonSettings;

    public event EditMaterialInLogDelegate OnEditMaterialInLog;

    public bool AllowQuarantineToCancelLoad { get; } = true;
    public bool AddJobsAsCopiedToSystem { get; } = true;

    private readonly FMSSettings _fmsSettings;

    public MockServerBackend(FMSSettings settings, RepositoryConfig repo)
    {
      _fmsSettings = settings;
      _jsonSettings = new JsonSerializerOptions();
      FMSInsightWebHost.JsonSettings(_jsonSettings);

      RepoConfig = repo;

      if (DebugMockProgram.InsightBackupDbFile != null)
      {
        LoadStatusFromLog(System.IO.Path.GetDirectoryName(DebugMockProgram.InsightBackupDbFile));
      }
      else
      {
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

    private long _curStatusLoadCount = 0;

    public void PrintLabel(long matId, int process, Uri httpReferer)
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
    }

    public ScannedMaterial ParseBarcode(string barcode, Uri referer)
    {
      Serilog.Log.Information("Parsing barcode {barcode} {referer}", barcode, referer);
      System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));
      var commaIdx = barcode.IndexOf(',');
      if (commaIdx >= 0)
        barcode = barcode.Substring(0, commaIdx);
      using (var conn = RepoConfig.OpenConnection())
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
              //PossibleCastings = ImmutableList.Create("part1", "part2"),
              PossibleJobs = ImmutableList.Create("aaa-offline-2018-01-01", "bbb-offline-2018-01-01"),
            },
          };
        }
      }
    }

    public event Action NewCellState
    {
      add { }
      remove { }
    }

    public IEnumerable<string> CheckNewJobs(IRepository db, NewJobs jobs)
    {
      return Enumerable.Empty<string>();
    }

    public DebugCellState CalculateCellState(IRepository db)
    {
      bool changed = false;
      _curStatusLoadCount += 1;
      if (_curStatusLoadCount % 5 == 0)
      {
        changed = true;
        if (CurrentStatus.Alarms.Count > 0)
        {
          CurrentStatus = CurrentStatus with { Alarms = ImmutableList<string>.Empty };
        }
        else
        {
          CurrentStatus = CurrentStatus with
          {
            Alarms = ImmutableList.Create("Test alarm " + _curStatusLoadCount.ToString(), "Another alarm"),
          };
        }
      }

      return new DebugCellState()
      {
        CurrentStatus = CurrentStatus,
        TimeUntilNextRefresh = TimeSpan.FromMinutes(1),
        StateUpdated = changed,
      };
    }

    public bool ApplyActions(IRepository db, DebugCellState st)
    {
      return false;
    }

    public bool DecrementJobs(IRepository db, DebugCellState st)
    {
      return false;
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
        .Select(p => new ProgramInCellController()
        {
          ProgramName = p.ProgramName,
          Revision = p.Revision,
          Comment = p.Comment,
          CellControllerProgramName = p.CellControllerProgramName,
        })
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
        .Select(i => new ProgramRevision()
        {
          ProgramName = programName,
          Revision = start - i,
          Comment = $"programName comment {start - i}",
          CellControllerProgramName = "cell " + programName,
        })
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
          evts.AddRange(JsonSerializer.Deserialize<List<LogEntry>>(file, _jsonSettings));
        }
      }

      var tools = JsonSerializer.Deserialize<Dictionary<long, List<ToolUse>>>(
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
        else if (e.LogType == LogType.Inspection)
        {
          LogDB.ForceInspection(
            mat: EventLogMaterial.FromLogMat(e.Material[0]),
            inspType: e.ProgramDetails["InspectionType"],
            utcNow: e.EndTimeUTC.Add(offset),
            inspect: bool.Parse(e.Result)
          );
        }
        else if (e.LogType == LogType.InspectionResult)
        {
          if (e.Material.Any(m => m.MaterialID == 2965))
          {
            // ignore inspection complete
            continue;
          }
          LogDB.RecordInspectionCompleted(
            mat: EventLogMaterial.FromLogMat(e.Material[0]),
            inspectionLocNum: e.LocationNum,
            inspectionType: e.Program,
            success: bool.Parse(e.Result),
            inspectTimeUTC: e.EndTimeUTC.Add(offset),
            extraData: e.ProgramDetails ?? ImmutableDictionary<string, string>.Empty,
            elapsed: e.ElapsedTime,
            active: e.ActiveOperationTime
          );
        }
        else if (e.LogType == LogType.MachineCycle && e.StartOfCycle)
        {
          LogDB.RecordMachineStart(
            mats: e.Material.Select(EventLogMaterial.FromLogMat).ToImmutableList(),
            pallet: e.Pallet,
            statName: e.LocationName,
            statNum: e.LocationNum,
            program: e.Program,
            timeUTC: e.EndTimeUTC.Add(offset),
            extraData: e.ProgramDetails
          );
        }
        else if (e.LogType == LogType.MachineCycle && !e.StartOfCycle)
        {
          LogDB.RecordMachineEnd(
            mats: e.Material.Select(EventLogMaterial.FromLogMat).ToImmutableList(),
            pallet: e.Pallet,
            statName: e.LocationName,
            statNum: e.LocationNum,
            program: e.Program,
            timeUTC: e.EndTimeUTC.Add(offset),
            extraData: e.ProgramDetails,
            elapsed: e.ElapsedTime,
            active: e.ActiveOperationTime,
            result: e.Result,
            tools: tools.GetValueOrDefault(e.Counter)?.ToImmutableList()
          );
        }
        else if (e.LogType == LogType.LoadUnloadCycle && e.StartOfCycle && e.Program == "LOAD")
        {
          LogDB.RecordLoadStart(
            mats: e.Material.Select(EventLogMaterial.FromLogMat).ToImmutableList(),
            lulNum: e.LocationNum,
            timeUTC: e.EndTimeUTC.Add(offset),
            pallet: e.Pallet
          );
        }
        else if (e.LogType == LogType.LoadUnloadCycle && !e.StartOfCycle && e.Program == "LOAD")
        {
          LogDB.RecordPartialLoadUnload(
            toUnload: null,
            lulNum: e.LocationNum,
            toLoad:
            [
              new MaterialToLoadOntoFace()
              {
                MaterialIDs = e.Material.Select(m => m.MaterialID).ToImmutableList(),
                Process = e.Material[0].Process,
                Path = e.Material[0].Path,
                FaceNum = e.Material[0].Face,
                ActiveOperationTime = e.ActiveOperationTime,
              },
            ],
            pallet: e.Pallet,
            totalElapsed: e.ElapsedTime,
            externalQueues: null,
            timeUTC: e.EndTimeUTC.Add(offset)
          );
        }
        else if (e.LogType == LogType.LoadUnloadCycle && e.StartOfCycle && e.Program == "UNLOAD")
        {
          LogDB.RecordUnloadStart(
            mats: e.Material.Select(EventLogMaterial.FromLogMat).ToImmutableList(),
            pallet: e.Pallet,
            lulNum: e.LocationNum,
            timeUTC: e.EndTimeUTC.Add(offset)
          );
        }
        else if (e.LogType == LogType.LoadUnloadCycle && !e.StartOfCycle && e.Program == "UNLOAD")
        {
          LogDB.RecordPartialLoadUnload(
            toLoad: null,
            toUnload:
            [
              new MaterialToUnloadFromFace()
              {
                MaterialIDToQueue = e.Material.ToImmutableDictionary(m => m.MaterialID, m => (string?)null),
                FaceNum = e.Material[0].Face,
                Process = e.Material[0].Process,
                ActiveOperationTime = e.ActiveOperationTime,
              },
            ],
            pallet: e.Pallet,
            lulNum: e.LocationNum,
            timeUTC: e.EndTimeUTC.Add(offset),
            totalElapsed: e.ElapsedTime,
            externalQueues: null
          );
        }
        else if (e.LogType == LogType.PalletCycle)
        {
          LogDB.RecordEmptyPallet(pallet: e.Pallet, timeUTC: e.EndTimeUTC.Add(offset));
        }
        else if (e.LogType == LogType.PalletOnRotaryInbound && e.StartOfCycle)
        {
          LogDB.RecordPalletArriveRotaryInbound(
            mats: e.Material.Select(EventLogMaterial.FromLogMat).ToImmutableList(),
            pallet: e.Pallet,
            statName: e.LocationName,
            statNum: e.LocationNum,
            timeUTC: e.EndTimeUTC.Add(offset)
          );
        }
        else if (e.LogType == LogType.PalletOnRotaryInbound && !e.StartOfCycle)
        {
          LogDB.RecordPalletDepartRotaryInbound(
            mats: e.Material.Select(EventLogMaterial.FromLogMat).ToImmutableList(),
            pallet: e.Pallet,
            statName: e.LocationName,
            statNum: e.LocationNum,
            timeUTC: e.EndTimeUTC.Add(offset),
            elapsed: e.ElapsedTime,
            rotateIntoWorktable: e.Result == "RotateIntoWorktable"
          );
        }
        else if (e.LogType == LogType.PalletInStocker && e.StartOfCycle)
        {
          LogDB.RecordPalletArriveStocker(
            mats: e.Material.Select(EventLogMaterial.FromLogMat).ToImmutableList(),
            pallet: e.Pallet,
            stockerNum: e.LocationNum,
            timeUTC: e.EndTimeUTC.Add(offset),
            waitForMachine: e.Result == "WaitForMachine"
          );
        }
        else if (e.LogType == LogType.PalletInStocker && !e.StartOfCycle)
        {
          LogDB.RecordPalletDepartStocker(
            mats: e.Material.Select(EventLogMaterial.FromLogMat).ToImmutableList(),
            pallet: e.Pallet,
            stockerNum: e.LocationNum,
            timeUTC: e.EndTimeUTC.Add(offset),
            elapsed: e.ElapsedTime,
            waitForMachine: e.Result == "WaitForMachine"
          );
        }
        else if (e.LogType == LogType.CloseOut)
        {
          LogDB.RecordCloseoutCompleted(
            mat: EventLogMaterial.FromLogMat(e.Material[0]),
            locNum: e.LocationNum,
            closeoutType: e.Program,
            success: e.Result != "Failed",
            extraData: e.ProgramDetails ?? ImmutableDictionary<string, string>.Empty,
            elapsed: e.ElapsedTime,
            active: e.ActiveOperationTime,
            completeTimeUTC: e.EndTimeUTC.Add(offset)
          );
        }
        else
        {
          throw new Exception("Invalid log type " + e.LogType);
        }
      }
    }

    private void LoadJobs(string sampleDataPath, TimeSpan offset)
    {
      var newJobsJson = System.IO.File.ReadAllText(System.IO.Path.Combine(sampleDataPath, "newjobs.json"));
      var allNewJobs = JsonSerializer.Deserialize<List<NewJobs>>(newJobsJson, _jsonSettings);

      using var LogDB = RepoConfig.OpenConnection();
      foreach (var newJobs in allNewJobs)
      {
        var newJobsOffset = newJobs with
        {
          Jobs = newJobs.Jobs.Select(j => OffsetJob(j, offset)).ToImmutableList(),
          StationUse = newJobs
            .StationUse?.Select(su =>
              su with
              {
                StartUTC = su.StartUTC.Add(offset),
                EndUTC = su.EndUTC.Add(offset),
              }
            )
            .ToImmutableList(),
          CurrentUnfilledWorkorders = newJobs
            .CurrentUnfilledWorkorders?.Select(w => w with { DueDate = w.DueDate.Add(offset) })
            .ToImmutableList(),
          SimDayUsage = newJobs
            .SimDayUsage?.Select(su => su with { Day = su.Day.AddDays((int)Math.Round(offset.TotalDays)) })
            .ToImmutableList(),
        };

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
        var curSt = JsonSerializer.Deserialize<CurrentStatus>(statusJson, _jsonSettings);
        curSt = curSt with
        {
          TimeOfCurrentStatusUTC = curSt.TimeOfCurrentStatusUTC.Add(offset),
          Jobs = curSt
            .Jobs.Values.Select(j =>
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
            .ToImmutableDictionary(j => j.UniqueStr, j => j),
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
        Processes = originalJob
          .Processes.Select(p =>
            p with
            {
              Paths = p
                .Paths.Select(path =>
                  path with
                  {
                    SimulatedStartingUTC = path.SimulatedStartingUTC.Add(offset),
                    SimulatedProduction = path
                      .SimulatedProduction.Select(prod => prod with { TimeUTC = prod.TimeUTC.Add(offset) })
                      .ToImmutableList(),
                  }
                )
                .ToImmutableList(),
            }
          )
          .ToImmutableList(),
      };
      // not converted: hold patterns
    }

    public void LoadTools(string sampleDataPath)
    {
      var json = System.IO.File.ReadAllText(Path.Combine(sampleDataPath, "tools.json"));
      Tools = JsonSerializer.Deserialize<ImmutableList<ToolInMachine>>(json, _jsonSettings);
    }

    public void LoadPrograms(string sampleDataPath)
    {
      var programJson = System.IO.File.ReadAllText(Path.Combine(sampleDataPath, "programs.json"));
      Programs = JsonSerializer.Deserialize<List<MockProgram>>(programJson, _jsonSettings);
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
        Programs = System
          .Text.Json.Nodes.JsonNode.Parse(lastAddSch)["mazakData"]["MainPrograms"]
          .AsArray()
          .Select(prog => new MockProgram()
          {
            ProgramName = (string)prog["MainProgram"],
            CellControllerProgramName = (string)prog["MainProgram"],
            Comment = (string)prog["Comment"],
          })
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
        Queues = ImmutableDictionary<string, QueueInfo>.Empty,
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
