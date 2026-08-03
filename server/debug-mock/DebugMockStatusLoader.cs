using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlackMaple.MachineFramework;

namespace DebugMachineWatchApiServer;

public static class DebugMockStatusLoader
{
  public static CurrentStatus LoadExternal(
    string filePath,
    JsonSerializerOptions jsonSettings,
    TimeSpan offset
  )
  {
    if (string.IsNullOrWhiteSpace(filePath))
    {
      throw new InvalidOperationException(
        "BMS_CURRENT_STATUS_FILE must contain a path to a CurrentStatus JSON file."
      );
    }

    if (!File.Exists(filePath))
    {
      throw new InvalidOperationException(
        $"BMS_CURRENT_STATUS_FILE does not identify a file: '{filePath}'."
      );
    }

    try
    {
      using var file = File.OpenRead(filePath);
      var status = JsonSerializer.Deserialize<CurrentStatus>(file, jsonSettings);
      if (status is null)
      {
        throw new InvalidOperationException(
          $"BMS_CURRENT_STATUS_FILE contains null instead of a CurrentStatus: '{filePath}'."
        );
      }

      return OffsetStatus(status, offset);
    }
    catch (JsonException ex)
    {
      throw new InvalidOperationException(
        $"BMS_CURRENT_STATUS_FILE is not valid CurrentStatus JSON: '{filePath}'.",
        ex
      );
    }
    catch (IOException ex)
    {
      throw new InvalidOperationException(
        $"BMS_CURRENT_STATUS_FILE could not be read: '{filePath}'.",
        ex
      );
    }
    catch (UnauthorizedAccessException ex)
    {
      throw new InvalidOperationException(
        $"BMS_CURRENT_STATUS_FILE could not be read: '{filePath}'.",
        ex
      );
    }
  }

  public static CurrentStatus OffsetStatus(CurrentStatus originalStatus, TimeSpan offset)
  {
    return originalStatus with
    {
      TimeOfCurrentStatusUTC = originalStatus.TimeOfCurrentStatusUTC.Add(offset),
      Jobs = originalStatus
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
                    .SimulatedProduction.Select(prod =>
                      prod with
                      {
                        TimeUTC = prod.TimeUTC.Add(offset),
                      }
                    )
                    .ToImmutableSortedSet(),
                }
              )
              .ToImmutableList(),
          }
        )
        .ToImmutableList(),
    };
    // not converted: hold patterns
  }
}
