using System.Collections.Immutable;

namespace BlackMaple.MachineFramework;

public enum AlarmImpact
{
  /// <summary>Display the message without suspending instructions or changes.</summary>
  Informational,

  /// <summary>
  /// Retain the known data and saved targets, but mark instructions unavailable and disable
  /// status-dependent changes within the declared scope. Protective actions remain available.
  /// </summary>
  SuspendChanges,
}

public enum AlarmActivity
{
  BasketMovement,
  BasketLoadStation,
}

/// <summary>
/// The backend's assessment of its latest known status, not a client freshness estimate. An absent
/// activity or basket filter includes all activities or baskets respectively; supplied filters
/// intersect. With neither filter the alarm applies to the whole status. Emit a blocking alarm
/// only when all instructions and status-dependent changes in that scope should be suspended,
/// including corrective work. Normal work readiness remains a separate backend decision.
/// </summary>
public record CurrentStatusAlarm
{
  public required string Code { get; init; }
  public required string Message { get; init; }
  public required AlarmImpact Impact { get; init; }
  public AlarmActivity? Activity { get; init; }
  public int? BasketId { get; init; }
}

public static class CurrentStatusAlarms
{
  /// <summary>Append one diagnostic to both the structured and compatible human-readable views.</summary>
  public static CurrentStatus AddAlarm(this CurrentStatus status, CurrentStatusAlarm alarm) =>
    status with
    {
      Alarms = status.Alarms.Add(alarm.Message),
      AlarmDetails = (status.AlarmDetails ?? ImmutableList<CurrentStatusAlarm>.Empty).Add(alarm),
    };
}
