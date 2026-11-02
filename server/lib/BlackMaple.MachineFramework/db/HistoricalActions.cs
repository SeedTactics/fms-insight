using System;
using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using System.Text.Json;

namespace BlackMaple.MachineFramework;

internal sealed partial class Repository
{
  // Assignment is captured in details, independently of the current material joins on log reads.
  private LogEntry RecordCastingAssignment(
    IDbTransaction transaction,
    MaterialDetails assignment,
    string casting,
    bool assigned,
    DateTime timeUTC
  )
  {
    var entry = new NewEventLogEntry
    {
      Material =
      [
        new EventLogMaterial
        {
          MaterialID = assignment.MaterialID,
          Process = 0,
          Face = 0,
        },
      ],
      LogType = assigned ? LogType.CastingAssigned : LogType.CastingUnassigned,
      StartOfCycle = false,
      EndTimeUTC = timeUTC,
      LocationName = "Casting",
      LocationNum = 0,
      Pallet = 0,
      Program = "",
      Result = assignment.JobUnique,
      ElapsedTime = TimeSpan.Zero,
      ActiveOperationTime = TimeSpan.Zero,
    };
    entry.ProgramDetails["Assignment"] = JsonSerializer.Serialize(assignment);
    entry.ProgramDetails["Casting"] = casting;
    return AddLogEntry(transaction, entry, null, null);
  }

  private LogEntry RecordJobDecrement(
    IDbTransaction transaction,
    long decrementId,
    DateTime timeUTC,
    ImmutableList<JobAndDecrementQuantity> decrements
  )
  {
    var entry = new NewEventLogEntry
    {
      Material = [],
      LogType = LogType.JobDecrement,
      StartOfCycle = false,
      EndTimeUTC = timeUTC,
      LocationName = "JobDecrement",
      LocationNum = 0,
      Pallet = 0,
      Program = "",
      Result = decrementId.ToString(CultureInfo.InvariantCulture),
      ElapsedTime = TimeSpan.Zero,
      ActiveOperationTime = TimeSpan.Zero,
    };
    entry.ProgramDetails["Decrements"] = JsonSerializer.Serialize(decrements);
    return AddLogEntry(transaction, entry, null, null);
  }
}
