using System.Text.Json;
using System.Text.Json.Nodes;
using BlackMaple.FMSInsight.Client;

namespace BlackMaple.FMSInsight.Cli;

internal static class HistoricalPlan
{
  // Select maintained job/process/path fields explicitly; retain original responses separately.
  internal static string Project(HistoricJob job)
  {
    var wire = JsonSerializer.SerializeToElement(job, InsightJson.CreateOptions());
    var plan = Select(
      wire,
      "Unique",
      "PartName",
      "RouteStartUTC",
      "RouteEndUTC",
      "ScheduleId",
      "Cycles",
      "AllocationAlgorithm",
      "ProvisionalWorkorderId",
      "ArtifactRunDate",
      "ManuallyCreated"
    );
    plan["ProcsAndPaths"] = new JsonArray(
      wire.GetProperty("ProcsAndPaths")
        .EnumerateArray()
        .Select(process =>
        {
          var projected = Select(
            process,
            "ExtraFields",
            "BasketLoadStations",
            "ExpectedBasketLoadTime",
            "BasketUnloadStations",
            "ExpectedBasketUnloadTime"
          );
          projected["paths"] = new JsonArray(
            process
              .GetProperty("paths")
              .EnumerateArray()
              .Select(path =>
                (JsonNode)Select(
                  path,
                  "PalletNums",
                  "Fixture",
                  "Face",
                  "Load",
                  "ExpectedLoadTime",
                  "Unload",
                  "ExpectedUnloadTime",
                  "Stops",
                  "SimulatedProduction",
                  "SimulatedStartTimePerPallet",
                  "SimulatedStartingUTC",
                  "SimulatedAverageFlowTime",
                  "PartsPerPallet",
                  "InputQueue",
                  "OutputQueue",
                  "Inspections",
                  "Casting"
                )
              )
              .ToArray()
          );
          return (JsonNode)projected;
        })
        .ToArray()
    );
    return plan.ToJsonString();
  }

  private static JsonObject Select(JsonElement value, params string[] names)
  {
    var result = new JsonObject();
    foreach (var name in names)
      if (value.TryGetProperty(name, out var property))
        result.Add(name, JsonNode.Parse(property.GetRawText()));
    return result;
  }
}
