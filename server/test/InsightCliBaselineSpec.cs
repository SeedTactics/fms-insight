using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class InsightCliBaselineSpec
{
  private static readonly DateTime Start = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);

  [Test]
  public async Task SharedCyclesCorrectionsPlanningAndDelayedQualityHaveDistinctArithmetic()
  {
    await using var host = await InsightCliTestHost.Start();
    using var db = host.Repository.OpenConnection();
    var end = Start.AddDays(1);
    db.AddJobs(
      new NewJobs
      {
        ScheduleId = "plan",
        Jobs = [InsightCliSyncSpec.Job("job"), InsightCliSyncSpec.Job("other")],
        StationUse =
        [
          new SimulatedStationUtilization
          {
            ScheduleId = "plan",
            StationGroup = "MC",
            StationNum = 1,
            StartUTC = Start,
            EndUTC = Start.AddMinutes(10),
            Parts =
            [
              new SimulatedStationPart
              {
                JobUnique = "job",
                Process = 1,
                Path = 1,
              },
              new SimulatedStationPart
              {
                JobUnique = "other",
                Process = 1,
                Path = 1,
              },
            ],
          },
          new SimulatedStationUtilization
          {
            ScheduleId = "plan",
            StationGroup = "MC",
            StationNum = 1,
            StartUTC = Start.AddMinutes(10),
            EndUTC = Start.AddMinutes(15),
            PlanDown = true,
          },
        ],
      },
      null,
      false
    );
    db.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = "job",
          Part = "part",
          Quantity = 2,
        },
      ],
      Start.AddHours(-1)
    );
    var first = db.AllocateMaterialID("job", "part", 1);
    var second = db.AllocateMaterialID("job", "part", 1);
    var invalid = db.AllocateMaterialID("job", "part", 1);
    db.RecordSerialForMaterialID(first, 1, "ambiguous", Start.AddDays(-1));
    db.RecordSerialForMaterialID(second, 1, "ambiguous", Start.AddDays(-1));
    db.RecordMachineEnd(
      [InsightCliSyncSpec.Material(first)],
      1,
      "MC",
      1,
      "100",
      "pre-window",
      Start.AddDays(-1),
      TimeSpan.FromSeconds(90),
      TimeSpan.FromSeconds(60)
    );
    var shared = ImmutableList.Create(
      InsightCliSyncSpec.Material(first),
      InsightCliSyncSpec.Material(second)
    );
    db.RecordMachineStart(shared, 1, "MC", 1, "100", Start.AddHours(1));
    db.RecordMachineEnd(
      shared,
      1,
      "MC",
      1,
      "100",
      "shared",
      Start.AddHours(1).AddMinutes(3),
      TimeSpan.FromSeconds(180),
      TimeSpan.FromSeconds(120)
    );
    db.RecordMachineEnd(
      [InsightCliSyncSpec.Material(invalid)],
      2,
      "MC",
      2,
      "100",
      "invalid",
      Start.AddHours(2),
      TimeSpan.FromSeconds(240),
      TimeSpan.FromSeconds(120)
    );
    var initial = await host.Sync("baseline.sqlite", Start, end);
    await Assert.That(initial.Exit).IsEqualTo(0).Because(initial.Errors);
    db.InvalidatePalletCycle(invalid, 1, "operator", end.AddDays(1)).ToImmutableList();
    db.AddNewDecrement(
      [
        new NewDecrementQuantity
        {
          JobUnique = "job",
          Part = "part",
          Quantity = 3,
        },
      ],
      end.AddDays(1)
    );
    db.RecordInspectionCompleted(
      InsightCliSyncSpec.Material(first),
      1,
      "dimensional",
      false,
      ImmutableDictionary<string, string>.Empty,
      TimeSpan.FromMinutes(2),
      TimeSpan.FromMinutes(1),
      end.AddDays(2)
    );
    var sync = await InsightCliSyncSpec.Resume(host, "baseline.sqlite");
    await Assert.That(sync.Exit).IsEqualTo(0).Because(sync.Errors);
    using (
      var facts = await host.Query(
        "baseline.sqlite",
        """
        SELECT (SELECT SUM(elapsed_seconds) FROM events WHERE log_type='MachineCycle' AND start_of_cycle=0 AND invalidated=0) valid_elapsed,
          (SELECT SUM(active_seconds) FROM events WHERE log_type='MachineCycle' AND start_of_cycle=0) target,
          (SELECT SUM(elapsed_seconds) FROM events WHERE log_type='MachineCycle' AND start_of_cycle=0) all_elapsed,
          (SELECT SUM(e.elapsed_seconds) FROM events e JOIN event_material m USING(counter)
            WHERE e.log_type='MachineCycle' AND e.start_of_cycle=0 AND e.invalidated=0) duplicated_elapsed,
          (SELECT SUM(quantity) FROM job_decrements WHERE job_unique='job') decrements,
          (SELECT COUNT(*) FROM events WHERE log_type='InspectionResult') outcomes,
        (SELECT COUNT(*) FROM sim_station_use) intervals,
        (SELECT ROUND(SUM((julianday(end_utc)-julianday(start_utc))*86400)) FROM sim_station_use) simulated_seconds,
          (SELECT COUNT(*) FROM sim_station_parts p JOIN sim_station_use s ON p.interval_id=s.id) participants
        """
      )
    )
    {
      var row = facts.RootElement[0];
      await Assert.That(row.GetProperty("valid_elapsed").GetDouble()).IsEqualTo(180);
      await Assert.That(row.GetProperty("target").GetDouble()).IsEqualTo(120);
      await Assert.That(row.GetProperty("all_elapsed").GetDouble()).IsEqualTo(420);
      await Assert.That(row.GetProperty("duplicated_elapsed").GetDouble()).IsEqualTo(360);
      await Assert.That(row.GetProperty("decrements").GetInt32()).IsEqualTo(5);
      await Assert.That(row.GetProperty("outcomes").GetInt32()).IsEqualTo(0);
      await Assert.That(row.GetProperty("intervals").GetInt32()).IsEqualTo(2);
      await Assert.That(row.GetProperty("simulated_seconds").GetDouble()).IsEqualTo(900);
      await Assert.That(row.GetProperty("participants").GetInt32()).IsEqualTo(2);
    }
    var targeted = await InsightCliTestHost.Run([
      "material",
      "--server",
      host.Url,
      "--database",
      Path.Combine(host.Directory, "baseline.sqlite"),
      "--serial",
      "ambiguous",
    ]);
    await Assert.That(targeted.Exit).IsEqualTo(0).Because(targeted.Errors);
    using var extra = await host.Query(
      "baseline.sqlite",
      """
      SELECT (SELECT COUNT(*) FROM material WHERE serial='ambiguous') matches,
        (SELECT COUNT(*) FROM events WHERE result='pre-window') before_window,
        (SELECT COUNT(*) FROM events WHERE log_type='InspectionResult') outcomes,
        (SELECT COUNT(DISTINCT m.material_id) FROM event_material m JOIN events e USING(counter) WHERE e.log_type='InspectionResult') inspected_materials
      """
    );
    await Assert.That(extra.RootElement[0].GetProperty("matches").GetInt32()).IsEqualTo(2);
    await Assert.That(extra.RootElement[0].GetProperty("before_window").GetInt32()).IsEqualTo(1);
    await Assert.That(extra.RootElement[0].GetProperty("outcomes").GetInt32()).IsEqualTo(1);
    await Assert
      .That(extra.RootElement[0].GetProperty("inspected_materials").GetInt32())
      .IsEqualTo(1);
  }
}
