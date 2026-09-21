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
  [Arguments("ambiguous-operation")]
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
      StationOperationCount = 1,
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
      case "ambiguous-operation":
        row.StationOperationCount = 2;
        break;
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
            StationOperationCount = 1,
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

  [Test]
  [Arguments(false)]
  [Arguments(true)]
  public async Task CombinedActionsMapOperationToStationWithoutDuplicatingAssignments(
    bool ambiguous
  )
  {
    var rows = new List<LoadOperationsFromDB.ActionRow>();
    foreach (var load in new[] { true, false })
    foreach (var assignment in new[] { 10, 11 })
      rows.Add(
        new LoadOperationsFromDB.ActionRow
        {
          ActionID = 1, // IDs in A8 and A9 need not be globally unique.
          LoadEvent = load,
          OperationID = 7,
          Station = 1,
          StationOperationCount = 1,
          ActionPart = "part:4:1",
          ActionComment = "job-Insight",
          ActionProcess = load ? 2 : 1,
          ActionQuantity = 3,
          Pallet = 2,
          Held = 0,
          PositionCount = 1,
          PalletCount = 1,
          AssignmentID = assignment,
          Part = "part:4:1",
          Comment = "job-Insight",
          Process = 1,
          Quantity = 3,
        }
      );
    if (ambiguous)
      rows.Add(rows[0]);
    var actions = LoadOperationsFromDB.BuildActions(rows);
    await Assert.That(actions.Count).IsEqualTo(2);
    await Assert.That(actions.Select(a => a.LoadStation)).IsEquivalentTo(new[] { 1, 1 });
    await Assert.That(actions.Select(a => a.Part)).IsEquivalentTo(new[] { "part", "part" });
    await Assert.That(actions.Select(a => a.LoadEvent)).IsEquivalentTo(new[] { true, false });
    await Assert.That(actions.Select(a => a.Process)).IsEquivalentTo(new[] { 2, 1 });
    if (ambiguous)
      await Assert.That(actions[0].StationPallet).IsNull();
    else
      await Assert.That(actions[0].StationPallet.Material.Count()).IsEqualTo(2);
    await Assert.That(actions[1].StationPallet.Material.Count()).IsEqualTo(2);
  }
}
