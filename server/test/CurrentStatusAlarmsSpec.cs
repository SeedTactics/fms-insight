using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using TUnit.Assertions.Enums;

namespace BlackMaple.FMSInsight.Tests;

public sealed class CurrentStatusAlarmsSpec
{
  private static JsonSerializerOptions JsonOptions()
  {
    var options = new JsonSerializerOptions();
    FMSInsightWebHost.JsonSettings(options);
    return options;
  }

  private static CurrentStatus Status() =>
    new()
    {
      Revision = 10,
      TimeOfCurrentStatusUTC = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc),
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = ["Equipment message"],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty,
    };

  [Test]
  public async Task LegacyStatusNeedsNoAlarmDetails()
  {
    var options = JsonOptions();
    var json = JsonSerializer.Serialize(Status(), options);
    using var document = JsonDocument.Parse(json);
    var loaded = JsonSerializer.Deserialize<CurrentStatus>(json, options)!;

    await Assert.That(document.RootElement.TryGetProperty("AlarmDetails", out _)).IsFalse();
    await Assert.That(loaded.AlarmDetails).IsNull();
    await Assert.That(loaded.Alarms).IsEquivalentTo(["Equipment message"]);
  }

  [Test]
  public async Task ScopedDiagnosticRoundTripsAlongsideLegacyMessagesAndRetainedData()
  {
    var original = Status();
    var alarm = new CurrentStatusAlarm
    {
      Code = "station-observation-unavailable",
      Message = "Basket station observation is unavailable.",
      Impact = AlarmImpact.SuspendChanges,
      Activity = AlarmActivity.BasketLoadStation,
      BasketId = 7,
    };
    var status = original.AddAlarm(alarm);
    var options = JsonOptions();
    var json = JsonSerializer.Serialize(status, options);
    var loaded = JsonSerializer.Deserialize<CurrentStatus>(json, options)!;

    await Assert.That(status.Material).IsSameReferenceAs(original.Material);
    await Assert.That(status.Jobs).IsSameReferenceAs(original.Jobs);
    await Assert.That(status.TimeOfCurrentStatusUTC).IsEqualTo(original.TimeOfCurrentStatusUTC);
    await Assert.That(original.AlarmDetails).IsNull();
    await Assert.That(original.Alarms).IsEquivalentTo(["Equipment message"]);
    await Assert.That(loaded.AlarmDetails).IsEquivalentTo([alarm]);
    await Assert
      .That(loaded.Alarms)
      .IsEquivalentTo(["Equipment message", alarm.Message], CollectionOrdering.Matching);
    using var document = JsonDocument.Parse(json);
    await Assert
      .That(document.RootElement.GetProperty("AlarmDetails")[0].GetProperty("Impact").GetString())
      .IsEqualTo("SuspendChanges");
  }

  [Test]
  public async Task StatusWideDiagnosticOmitsScopeFilters()
  {
    var alarm = new CurrentStatusAlarm
    {
      Code = "status-refresh-failed",
      Message = "Displaying the last known status; its refresh failed.",
      Impact = AlarmImpact.SuspendChanges,
    };
    var json = JsonSerializer.Serialize(Status().AddAlarm(alarm), JsonOptions());
    using var document = JsonDocument.Parse(json);
    var detail = document.RootElement.GetProperty("AlarmDetails")[0];

    await Assert.That(detail.TryGetProperty("Activity", out _)).IsFalse();
    await Assert.That(detail.TryGetProperty("BasketId", out _)).IsFalse();
  }
}
