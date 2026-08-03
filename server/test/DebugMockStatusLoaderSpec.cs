using System;
using System.IO;
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
      var status = DebugMockStatusLoader.LoadExternal(path, options, TimeSpan.FromDays(1));
      var customState = (JsonElement)status.CustomState;

      await Assert
        .That(status.TimeOfCurrentStatusUTC)
        .IsEqualTo(new DateTime(2018, 1, 30, 0, 0, 0, DateTimeKind.Utc));
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
  public async Task RejectsMissingExternalStatusFile()
  {
    var exception = Capture(() =>
      DebugMockStatusLoader.LoadExternal(
        Path.Combine(Path.GetTempPath(), "debug-mock-status-that-does-not-exist.json"),
        new JsonSerializerOptions(),
        TimeSpan.Zero
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
        DebugMockStatusLoader.LoadExternal(path, new JsonSerializerOptions(), TimeSpan.Zero)
      );

      await Assert.That(exception).IsTypeOf<InvalidOperationException>();
      await Assert.That(exception.Message).Contains("not valid CurrentStatus JSON");
    }
    finally
    {
      File.Delete(path);
    }
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
