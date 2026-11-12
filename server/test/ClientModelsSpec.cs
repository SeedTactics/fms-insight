using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Api = BlackMaple.FMSInsight.Client;

namespace BlackMaple.FMSInsight.Tests;

public sealed class ClientModelsSpec
{
  private static readonly JsonSerializerOptions ClientJson = Api.InsightJson.CreateOptions();
  private static readonly JsonSerializerOptions ServerJson = ServerOptions();
  private const string EventJson = """
    {
      "counter": "42", "material": [{
        "id": 9007199254740991, "uniq": "job-1", "part": "part-1", "proc": 1,
        "numproc": 2, "face": 3, "path": null, "serial": "ABC", "workorder": null
      }],
      "type": "MachineCycle", "startofcycle": false, "endUTC": "2026-10-08T12:00:00Z",
      "loc": "MC", "locnum": 2, "pal": 4, "program": "100", "result": "end",
      "elapsed": "PT3M0.5S", "active": "00:02:00", "details": {"ProgramRevision": "6"},
      "tooluse": null,
    }
    """;
  private const string HistoryJson = """
    {
      "Jobs": {"job-1": {
        "Unique": "job-1", "RouteStartUTC": "2026-10-08T00:00:00Z",
        "RouteEndUTC": "2026-10-09T00:00:00Z", "Archived": false,
        "PartName": "part-1", "Cycles": 10, "Bookings": ["order-1"],
        "ArtifactRunDate": "2026-10-08",
        "ProcsAndPaths": [{"paths": []}], "ScheduleId": "schedule-1", "CopiedToSystem": false,
        "Decrements": [{"DecrementId": 3, "TimeUTC": "2026-10-08T01:00:00Z", "Quantity": 2}]
      }},
      "StationUse": [{"ScheduleId": "schedule-1", "StationGroup": "MC", "StationNum": 2,
        "StartUTC": "2026-10-08T12:00:00Z", "EndUTC": "2026-10-08T13:00:00Z",
        "PlanDown": true, "Pallet": null,
        "Parts": [{"JobUnique":"job-1", "Process":1, "Path":2}]}]
    }
    """;

  private static JsonSerializerOptions ServerOptions()
  {
    var services = new ServiceCollection();
    services.AddLogging();
    services
      .AddControllers()
      .AddJsonOptions(o => FMSInsightWebHost.JsonSettings(o.JsonSerializerOptions));
    using var provider = services.BuildServiceProvider();
    return provider
      .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
      .Value.JsonSerializerOptions;
  }

  private static async Task<TClient> RoundTrip<TServer, TClient>(string input)
  {
    var server = JsonSerializer.Deserialize<TServer>(input, ServerJson);
    var wire = JsonSerializer.Serialize(server, ServerJson);
    var client = JsonSerializer.Deserialize<TClient>(wire, ClientJson)!;
    var returned = JsonSerializer.Serialize(client, ClientJson);
    await Assert
      .That(JsonNode.DeepEquals(JsonNode.Parse(wire), JsonNode.Parse(returned)))
      .IsTrue()
      .Because($"Server JSON: {wire}\nClient JSON: {returned}");
    return client;
  }

  [Test]
  public async Task ServerEventsPreserveAliasesEnumsIdentifiersAndDurations()
  {
    var log = await RoundTrip<LogEntry, Api.LogEntry>(EventJson);
    await Assert.That(log.Counter).IsEqualTo(42);
    await Assert.That(log.Type).IsEqualTo(Api.LogType.MachineCycle);
    await Assert.That(log.Material.Single().Id).IsEqualTo(MaterialId.MaxValue);
    await Assert.That(log.EndUTC.Kind).IsEqualTo(DateTimeKind.Utc);
    await Assert.That(log.Elapsed).IsEqualTo(TimeSpan.FromSeconds(180.5));
    await Assert.That(log.Active).IsEqualTo(TimeSpan.FromMinutes(2));
    await Assert.That(log.Material.Single().Path).IsNull();
    await Assert.That(log.Material.Single().Workorder).IsNull();
    await Assert.That(log.Details!["ProgramRevision"]).IsEqualTo("6");
  }

  [Test]
  public async Task JobInheritanceDatesSimulationAndDecrementsMatchServerJson()
  {
    var history = await RoundTrip<HistoricData, Api.HistoricData>(HistoryJson);
    var job = history.Jobs["job-1"];
    Api.Job plan = job;
    await Assert.That(plan.Unique).IsEqualTo("job-1");
    await Assert.That(plan.ArtifactRunDate).IsEqualTo(new DateOnly(2026, 10, 8));
    await Assert.That(plan.ProcsAndPaths.Single().Paths).IsEmpty();
    await Assert.That(plan.Bookings!.Single()).IsEqualTo("order-1");
    await Assert.That(job.CopiedToSystem).IsFalse();
    await Assert.That(job.Decrements!.Single().DecrementId).IsEqualTo(3);
    await Assert.That(history.StationUse.Single().Parts!.Single().Path).IsEqualTo(2);
    await Assert.That(history.StationUse.Single().Pallet).IsNull();
    await Assert.That(history.StationUse.Single().StartUTC.Kind).IsEqualTo(DateTimeKind.Utc);
    await RoundTrip<RecentHistoricData, Api.RecentHistoricData>(
      """
      {"Jobs":{},"StationUse":[],"MostRecentSimulationId":"simulation",
       "MostRecentSimDayUsage":[{"Day":"2026-10-08","MachineGroup":"MC","Usage":0.75}]}
      """
    );
  }

  [Test]
  public async Task NestedRoutesAndHoldDurationsRetainTheirTransportShape()
  {
    var history = JsonSerializer.Deserialize<HistoricData>(HistoryJson, ServerJson)!;
    var job = history.Jobs["job-1"];
    var path = JobLogTest.EmptyPath with
    {
      PalletNums = [2, 4],
      ExpectedLoadTime = TimeSpan.FromSeconds(6.5),
      ExpectedUnloadTime = TimeSpan.FromSeconds(8),
      Stops =
      [
        new MachiningStop
        {
          StationGroup = "MC",
          Stations = [1, 3],
          Program = "100",
          ProgramRevision = 7,
          ExpectedCycleTime = TimeSpan.FromMinutes(2),
        },
      ],
      HoldMachining = new HoldPattern
      {
        UserHold = false,
        ReasonForUserHold = "live hold",
        HoldUnholdPattern = [TimeSpan.FromMinutes(1)],
        HoldUnholdPatternStartUTC = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
        HoldUnholdPatternRepeats = true,
      },
    };
    history = history with
    {
      Jobs = history.Jobs.SetItem(
        "job-1",
        job with
        {
          Processes = [job.Processes[0] with { Paths = [path] }],
        }
      ),
    };
    var decoded = await RoundTrip<HistoricData, Api.HistoricData>(
      JsonSerializer.Serialize(history, ServerJson)
    );
    var route = decoded.Jobs["job-1"].ProcsAndPaths.Single().Paths.Single();
    await Assert.That(route.PalletNums).IsEquivalentTo([2, 4]);
    await Assert.That(route.ExpectedLoadTime).IsEqualTo(TimeSpan.FromSeconds(6.5));
    await Assert.That(route.Stops.Single().ExpectedCycleTime).IsEqualTo(TimeSpan.FromMinutes(2));
    await Assert
      .That(route.HoldMachining!.HoldUnholdPattern.Single())
      .IsEqualTo(TimeSpan.FromMinutes(1));
  }

  [Test]
  public async Task HistoricalActionPayloadsAreReadableWithoutServerTypes()
  {
    using var configuration = RepositoryConfig.InitializeMemoryDB(null, Guid.NewGuid());
    using var db = configuration.OpenConnection();
    var job = JsonSerializer.Deserialize<HistoricData>(HistoryJson, ServerJson)!.Jobs["job-1"];
    db.AddJobs(new NewJobs { ScheduleId = "schedule-1", Jobs = [job] }, null, false);
    var material = db.BulkAddNewCastingsInQueue("casting", 1, "stock", [], null, null)
      .MaterialIds.Single();
    db.AllocateCastingsInQueue("stock", "casting", job.UniqueStr, job.PartName, 2, 2, 1);
    var assigned = db.GetLogForMaterial(material).Single(e => e.LogType == LogType.CastingAssigned);
    var action = await RoundTrip<LogEntry, Api.LogEntry>(
      JsonSerializer.Serialize(assigned, ServerJson)
    );
    var assignment = JsonSerializer.Deserialize<Api.MaterialDetails>(
      action.Details!["Assignment"],
      ClientJson
    )!;
    await Assert.That(assignment.JobUnique).IsEqualTo("job-1");
    await Assert.That(assignment.Paths!["1"]).IsEqualTo(2);
    db.AddNewDecrement([
      new NewDecrementQuantity
      {
        JobUnique = "job-1",
        Part = "part-1",
        Quantity = 2,
      },
    ]);
    var decrement = db.GetRecentLog(assigned.Counter).Single();
    var change = await RoundTrip<LogEntry, Api.LogEntry>(
      JsonSerializer.Serialize(decrement, ServerJson)
    );
    var quantities = JsonSerializer.Deserialize<ImmutableList<Api.JobAndDecrementQuantity>>(
      change.Details!["Decrements"],
      ClientJson
    )!;
    var stored = db.LoadDecrementsForJob("job-1").Single();
    await Assert.That(quantities.Single().JobUnique).IsEqualTo("job-1");
    await Assert.That(quantities.Single().DecrementId).IsEqualTo(stored.DecrementId);
    await Assert.That(quantities.Single().Quantity).IsEqualTo(stored.Quantity);
  }

  [Test]
  public async Task UnknownResponseFieldsAreToleratedAndOptionalNullsAreReadable()
  {
    var response = JsonNode.Parse(
      JsonSerializer.Serialize(
        JsonSerializer.Deserialize<LogEntry>(EventJson, ServerJson),
        ServerJson
      )
    )!;
    response["futureField"] = JsonNode.Parse("""{"futureValue":[1,2,3]}""");
    response["material"]![0]!["path"] = null;
    response["tooluse"] = null;
    var decoded = JsonSerializer.Deserialize<Api.LogEntry>(response.ToJsonString(), ClientJson)!;
    await Assert.That(decoded.Counter).IsEqualTo(42);
    await Assert.That(decoded.Material.Single().Path).IsNull();
    await Assert.That(decoded.Tooluse).IsNull();
  }

  [Test]
  public async Task RequiredFieldsAndNonnullableCollectionsAreNotSilentlyDefaulted()
  {
    await Assert
      .That(() => JsonSerializer.Deserialize<Api.LogEntry>("{}", ClientJson))
      .Throws<JsonException>();
    var response = JsonNode.Parse(
      JsonSerializer.Serialize(
        JsonSerializer.Deserialize<LogEntry>(EventJson, ServerJson),
        ServerJson
      )
    )!;
    response["material"] = null;
    await Assert
      .That(() => JsonSerializer.Deserialize<Api.LogEntry>(response.ToJsonString(), ClientJson))
      .Throws<JsonException>();
  }

  [Test]
  [Arguments("PT2M", 120.0)]
  [Arguments("00:02:00", 120.0)]
  [Arguments("-PT30S", -30.0)]
  [Arguments("P1DT2H", 93600.0)]
  [Arguments("PT0.125S", 0.125)]
  public async Task IsoAndLegacyDurationsAreReadAndWrittenAsServerDurations(
    string duration,
    double seconds
  )
  {
    var value = JsonSerializer.Deserialize<TimeSpan>(
      JsonSerializer.Serialize(duration),
      ClientJson
    );
    await Assert.That(value).IsEqualTo(TimeSpan.FromSeconds(seconds));
    await Assert
      .That(JsonSerializer.Serialize(value, ClientJson))
      .IsEqualTo(JsonSerializer.Serialize(value, ServerJson));
  }

  [Test]
  [Arguments("\"\"")]
  [Arguments("\"P1\"")]
  [Arguments("\"PT1.5\"")]
  [Arguments("\"P999999999999D\"")]
  [Arguments("42")]
  [Arguments("null")]
  public async Task MalformedDurationsAreJsonErrors(string json)
  {
    await Assert
      .That(() => JsonSerializer.Deserialize<TimeSpan>(json, ClientJson))
      .Throws<JsonException>();
  }

  [Test]
  public async Task ClientModelsHaveNoServerOrGeneratorRuntimeDependency()
  {
    var assembly = typeof(Api.LogEntry).Assembly;
    await Assert.That(assembly == typeof(LogEntry).Assembly).IsFalse();
    await Assert
      .That(assembly.GetReferencedAssemblies())
      // Coverage instrumentation can reference the standard framework facade.
      .All(r => r.Name == "netstandard" || r.Name!.StartsWith("System.", StringComparison.Ordinal));
  }
}
