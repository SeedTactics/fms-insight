using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using DebugMachineWatchApiServer;

namespace BlackMaple.FMSInsight.Tests;

public sealed class DebugMockStatusLoaderSpec
{
  [Test]
  public async Task LoadsExternalStatusWithOffsetAndOpaqueCustomState()
  {
    var path = Path.GetTempFileName();
    try
    {
      await File.WriteAllTextAsync(
        path,
        """
        {
          "TimeOfCurrentStatusUTC": "2018-01-29T00:00:00Z",
          "Jobs": {},
          "Pallets": {},
          "Material": [],
          "Alarms": [],
          "Queues": {},
          "CustomState": {
            "PrivateMode": "working",
            "Nested": { "Preserve": true },
            "MaterialIds": ["9007199254740993"]
          }
        }
        """
      );

      var options = new JsonSerializerOptions();
      FMSInsightWebHost.JsonSettings(options);
      var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
      var status = DebugMockStatusLoader.LoadExternal(path, options, now);
      var customState = (JsonElement)status.CustomState;

      await Assert.That(status.TimeOfCurrentStatusUTC).IsEqualTo(now);
      await Assert.That(customState.GetProperty("PrivateMode").GetString()).IsEqualTo("working");
      await Assert
        .That(customState.GetProperty("Nested").GetProperty("Preserve").GetBoolean())
        .IsTrue();
      await Assert
        .That(customState.GetProperty("MaterialIds")[0].GetString())
        .IsEqualTo("9007199254740993");
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Test]
  public async Task LoadsExternalStatusWithMissingEmptyAndPopulatedSimulatedProduction()
  {
    var options = new JsonSerializerOptions();
    FMSInsightWebHost.JsonSettings(options);
    var original = DebugMockStatusLoader.Load(
      Path.Combine("..", "..", "..", "..", "debug-mock", "sample-data"),
      options,
      TimeSpan.Zero
    );
    var job = original.Jobs.Values.First();
    var route = job.Processes[0].Paths[0];
    var production = new SimulatedProduction
    {
      TimeUTC = original.TimeOfCurrentStatusUTC.AddHours(1),
      Quantity = 3,
    };
    original = original with
    {
      Jobs = original.Jobs.SetItem(
        job.UniqueStr,
        job with
        {
          Processes =
          [
            new ProcessInfo
            {
              Paths =
              [
                route with
                {
                  SimulatedProduction = null,
                },
                route with
                {
                  SimulatedProduction = [],
                },
                route with
                {
                  SimulatedProduction = [production],
                },
              ],
            },
          ],
        }
      ),
    };

    var path = Path.GetTempFileName();
    try
    {
      await File.WriteAllTextAsync(path, JsonSerializer.Serialize(original, options));
      var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
      var loaded = DebugMockStatusLoader.LoadExternal(path, options, now);
      var routes = loaded.Jobs[job.UniqueStr].Processes[0].Paths;
      var offset = now - original.TimeOfCurrentStatusUTC;

      await Assert.That(loaded.TimeOfCurrentStatusUTC).IsEqualTo(now);
      await Assert.That(routes[0].SimulatedProduction).IsNull();
      await Assert.That(routes[1].SimulatedProduction).IsEmpty();
      await Assert
        .That(routes[2].SimulatedProduction)
        .IsEquivalentTo([production with { TimeUTC = production.TimeUTC.Add(offset) }]);
      await Assert
        .That(routes[0].SimulatedStartingUTC)
        .IsEqualTo(route.SimulatedStartingUTC.Add(offset));
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Test]
  public async Task RejectsMissingExternalStatusFile()
  {
    var exception = Capture(() =>
      DebugMockStatusLoader.LoadExternal(
        Path.Combine(Path.GetTempPath(), "debug-mock-status-that-does-not-exist.json"),
        new JsonSerializerOptions(),
        DateTime.UtcNow
      )
    );

    await Assert.That(exception).IsTypeOf<InvalidOperationException>();
    await Assert.That(exception.Message).Contains("BMS_CURRENT_STATUS_FILE");
  }

  [Test]
  public async Task RejectsInvalidExternalStatusJson()
  {
    var path = Path.GetTempFileName();
    try
    {
      await File.WriteAllTextAsync(path, "{ not valid json");

      var exception = Capture(() =>
        DebugMockStatusLoader.LoadExternal(path, new JsonSerializerOptions(), DateTime.UtcNow)
      );

      await Assert.That(exception).IsTypeOf<InvalidOperationException>();
      await Assert.That(exception.Message).Contains("not valid CurrentStatus JSON");
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Test]
  public async Task NamesLoadStationsFromJobRoutes()
  {
    var status = DebugMockStatusLoader.Load(
      Path.Combine("..", "..", "..", "..", "debug-mock", "sample-data"),
      MockServerBackend.JsonSettings(),
      TimeSpan.Zero
    );
    await Assert
      .That(DebugMockStatusLoader.LoadStationNames(status))
      .IsEquivalentTo(ImmutableDictionary<int, string>.Empty.Add(1, "L/U #1").Add(2, "L/U #2"));
  }

  [Test]
  public async Task NamesLoadStationsHoldingPalletsAndBaskets()
  {
    static PalletStatus Pallet(int num, PalletLocationEnum loc, int locNum) =>
      new()
      {
        PalletNum = num,
        FixtureOnPallet = "",
        OnHold = false,
        CurrentPalletLocation = new PalletLocation(loc, loc.ToString(), locNum),
        NumFaces = 1,
      };
    static BasketStatus Basket(int id, BasketLocationEnum loc, int locNum) =>
      new()
      {
        BasketId = id,
        Position = new BasketPosition { Location = loc, LocationNum = locNum },
      };
    var status = new CurrentStatus
    {
      TimeOfCurrentStatusUTC = DateTime.UtcNow,
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>
        .Empty.Add(1, Pallet(1, PalletLocationEnum.LoadUnload, 3))
        .Add(2, Pallet(2, PalletLocationEnum.Machine, 7)),
      Material = [],
      Alarms = [],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty,
      Baskets = ImmutableDictionary<int, BasketStatus>
        .Empty.Add(10, Basket(10, BasketLocationEnum.LoadUnload, 4))
        .Add(11, Basket(11, BasketLocationEnum.LoadStationStaging, 5))
        .Add(12, Basket(12, BasketLocationEnum.Storage, 9))
        .Add(13, new BasketStatus { BasketId = 13 }),
    };
    await Assert
      .That(DebugMockStatusLoader.LoadStationNames(status).Keys)
      .IsEquivalentTo([3, 4, 5]);
  }

  private static Exception Capture(Action action)
  {
    try
    {
      action();
      return null;
    }
    catch (Exception exception)
    {
      return exception;
    }
  }
}
