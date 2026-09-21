using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MazakMachineInterface;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public class LoadStationContextSpec
{
  [Test]
  [Arguments("valid")]
  [Arguments("empty")]
  [Arguments("duplicate-position")]
  [Arguments("duplicate-station-row")]
  [Arguments("duplicate-status")]
  [Arguments("missing-pallet")]
  [Arguments("missing-status")]
  [Arguments("different-pallets")]
  public async Task ContextRequiresUniquePositiveStationPallet(string mode)
  {
    var row = new LoadOperationsFromDB.StationRow
    {
      OperationID = 7,
      Station = 1,
      Pallet = 2,
      Held = 0,
      PositionCount = 1,
      PalletCount = 1,
      AssignmentID = 10,
      Part = "part:4:1",
      Comment = "job-2-1-InsightS",
      Process = 1,
      Quantity = 3,
    };
    var rows = new List<LoadOperationsFromDB.StationRow> { row };
    switch (mode)
    {
      case "empty":
        row.AssignmentID = null;
        break;
      case "duplicate-position":
        row.PositionCount = 2;
        break;
      case "duplicate-status":
        row.PalletCount = 2;
        break;
      case "missing-pallet":
        row.Pallet = 0;
        break;
      case "missing-status":
        row.Held = null;
        break;
      case "duplicate-station-row":
        rows.Add(row);
        break;
      case "different-pallets":
        rows.Add(
          new LoadOperationsFromDB.StationRow
          {
            OperationID = 7,
            Station = 1,
            Pallet = 5,
            Held = 0,
            PositionCount = 1,
            PalletCount = 1,
            AssignmentID = 11,
          }
        );
        break;
    }
    var context = LoadOperationsFromDB.BuildStationContexts(rows)[7];
    await Assert.That(context.Station).IsEqualTo(1);
    if (mode is not ("valid" or "empty"))
      await Assert.That(context.Pallet).IsNull();
    else
    {
      await Assert.That(context.Pallet.PalletNumber).IsEqualTo(2);
      await Assert.That(context.Pallet.Material.Count()).IsEqualTo(mode == "empty" ? 0 : 1);
      var action = new LoadAction { LoadStation = context.Station, StationPallet = context.Pallet };
      var restored = JsonSerializer.Deserialize<LoadAction>(JsonSerializer.Serialize(action));
      await Assert.That(restored.StationPallet.PalletNumber).IsEqualTo(2);
      await Assert.That(restored.StationPallet.Material).IsEquivalentTo(context.Pallet.Material);
    }
  }
}
