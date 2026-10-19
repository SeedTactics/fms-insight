using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlackMaple.MachineFramework;

namespace DebugMachineWatchApiServer;

public static class DebugMockStatusLoader
{
  /// Loads BMS_CURRENT_STATUS_FILE when set, otherwise the hand-written sample status.
  public static CurrentStatus Load(
    string sampleDataPath,
    JsonSerializerOptions jsonSettings,
    TimeSpan sampleDataOffset
  )
  {
    var statusFile = Environment.GetEnvironmentVariable("BMS_CURRENT_STATUS_FILE");
    if (!string.IsNullOrEmpty(statusFile))
    {
      return LoadExternal(statusFile, jsonSettings, DateTime.UtcNow);
    }

    var statusJson = File.ReadAllText(Path.Combine(sampleDataPath, "status-mock.json"));
    return OffsetStatus(
      JsonSerializer.Deserialize<CurrentStatus>(statusJson, jsonSettings),
      sampleDataOffset
    );
  }

  /// Names every load station used by the status with the client's default label. Sites name their
  /// stations in code; the client offers named stations in its load station dropdown and limits the
  /// system overview to them, so any loaded status shows just its own stations.
  public static ImmutableDictionary<int, string> LoadStationNames(CurrentStatus status) =>
    status
      .Jobs.Values.SelectMany(j => j.Processes)
      .SelectMany(proc =>
        proc.Paths.SelectMany(path => path.Load.Concat(path.Unload))
          .Concat(proc.BasketLoadStations ?? [])
          .Concat(proc.BasketUnloadStations ?? [])
      )
      .Concat(
        status
          .Pallets.Values.Where(p =>
            p.CurrentPalletLocation.Location == PalletLocationEnum.LoadUnload
          )
          .Select(p => p.CurrentPalletLocation.Num)
      )
      .Concat(
        (status.Baskets?.Values ?? [])
          .Select(b => b.Position)
          .Where(pos =>
            pos?.Location is BasketLocationEnum.LoadUnload or BasketLocationEnum.LoadStationStaging
          )
          .Select(pos => pos.LocationNum)
      )
      .Distinct()
      .ToImmutableDictionary(num => num, num => $"L/U #{num}");

  /// Loads a status captured at any time, such as a scenario snapshot, and shifts it to be current
  /// as of nowUTC.
  public static CurrentStatus LoadExternal(
    string filePath,
    JsonSerializerOptions jsonSettings,
    DateTime nowUTC
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

      return OffsetStatus(status, nowUTC - status.TimeOfCurrentStatusUTC);
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
                    .SimulatedProduction?.Select(prod =>
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
