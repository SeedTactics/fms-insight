using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using DebugMachineWatchApiServer;

namespace BlackMaple.FMSInsight.Tests;

public sealed class DebugMockScenarioSpec
{
  [Test]
  public async Task AdvancesAndResetsGeneratedCurrentStatusSnapshots()
  {
    var directory = Directory.CreateTempSubdirectory("debug-mock-scenario-");
    try
    {
      await File.WriteAllTextAsync(
        Path.Combine(directory.FullName, "00-initial.json"),
        Status("initial")
      );
      await File.WriteAllTextAsync(
        Path.Combine(directory.FullName, "01-active.json"),
        Status("active")
      );
      var manifestPath = Path.Combine(directory.FullName, "scenario.json");
      await File.WriteAllTextAsync(
        manifestPath,
        """
        {
          "initial": "normal",
          "steps": {
            "normal": {
              "status": "00-initial.json",
              "on": [{
                "method": "POST",
                "path": "/api/example/start",
                "response": {
                  "status": 200,
                  "json": { "Receipt": "started" }
                },
                "next": "active"
              }]
            },
            "active": {
              "status": "01-active.json"
            }
          }
        }
        """
      );
      var options = new JsonSerializerOptions();
      FMSInsightWebHost.JsonSettings(options);
      var player = DebugMockScenarioPlayer.Load(manifestPath, options, TimeSpan.FromDays(1));

      await Assert.That(CustomMode(player.CurrentStatus)).IsEqualTo("initial");
      await Assert.That(player.Status.CurrentStep).IsEqualTo("normal");
      await Assert.That(player.TryTransition("POST", "/api/example/different", out _)).IsFalse();
      await Assert
        .That(player.TryTransition("post", "/api/example/start", out var response))
        .IsTrue();
      await Assert.That(response.Status).IsEqualTo(200);
      await Assert
        .That(response.Json!.Value.GetProperty("Receipt").GetString())
        .IsEqualTo("started");
      await Assert.That(CustomMode(player.CurrentStatus)).IsEqualTo("active");
      await Assert.That(player.Next()).IsFalse();

      player.Reset();

      await Assert.That(player.Status.CurrentStep).IsEqualTo("normal");
      await Assert.That(CustomMode(player.CurrentStatus)).IsEqualTo("initial");
      await Assert.That(player.Next()).IsTrue();
      await Assert.That(CustomMode(player.CurrentStatus)).IsEqualTo("active");
    }
    finally
    {
      directory.Delete(recursive: true);
    }
  }

  private static string Status(string mode) =>
    $$"""
      {
        "TimeOfCurrentStatusUTC": "2026-08-10T12:00:00Z",
        "Jobs": {},
        "Pallets": {},
        "Material": [],
        "Alarms": [],
        "Queues": {},
        "CustomState": { "Mode": "{{mode}}" }
      }
      """;

  private static string CustomMode(CurrentStatus status) =>
    ((JsonElement)status.CustomState!).GetProperty("Mode").GetString()!;
}
