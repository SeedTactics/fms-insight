using System.Threading.Tasks;
using BlackMaple.MachineFramework;

namespace BlackMaple.FMSInsight.Tests;

public sealed class MaterialOperationPolicySpec
{
  [Test]
  [Arguments(InProcessMaterialAction.ActionType.Loading)]
  [Arguments(InProcessMaterialAction.ActionType.LoadingToBasket)]
  [Arguments(InProcessMaterialAction.ActionType.UnloadToInProcess)]
  [Arguments(InProcessMaterialAction.ActionType.UnloadToCompletedMaterial)]
  public async Task AutomatedTransferTokensNeverAuthorizeDirectEdits(
    InProcessMaterialAction.ActionType action
  )
  {
    var material = Material(action, true, "operator-token");
    await Assert.That(MaterialOperationState.CanSignalQuarantine(material)).IsTrue();
    await Assert.That(MaterialOperationState.CanCancelLoad(material)).IsFalse();
    await Assert.That(MaterialOperationState.CanDirectlyQuarantine(material)).IsFalse();
    await Assert.That(MaterialOperationState.CanAddOrMoveToQueue(material)).IsFalse();
    await Assert.That(MaterialOperationState.CanInvalidate(material)).IsFalse();
    await Assert
      .That(MaterialOperationState.CanSignalQuarantine(material with { MaterialID = -1 }))
      .IsFalse();
  }

  [Test]
  public async Task MixedUnloadingCanSignalWhileMalformedTokensStillProtectMaterial()
  {
    var unloading = Material(InProcessMaterialAction.ActionType.UnloadToInProcess, false, "mixed");
    await Assert.That(MaterialOperationState.CanSignalQuarantine(unloading)).IsTrue();
    await Assert.That(MaterialOperationState.CanInvalidate(unloading)).IsFalse();
    var malformed = Material(InProcessMaterialAction.ActionType.Waiting, false, " ");
    await Assert.That(MaterialOperationState.CanCancelLoad(malformed)).IsFalse();
    await Assert.That(MaterialOperationState.CanSignalQuarantine(malformed)).IsFalse();
    await Assert.That(MaterialOperationState.CanAddOrMoveToQueue(malformed)).IsFalse();
    await Assert.That(MaterialOperationState.CanDirectlyQuarantine(malformed)).IsFalse();
  }

  private static InProcessMaterial Material(
    InProcessMaterialAction.ActionType action,
    bool automated,
    string token
  ) =>
    new()
    {
      MaterialID = 1,
      JobUnique = "job",
      PartName = "part",
      Process = 1,
      Path = 1,
      SignaledInspections = [],
      Location = new()
      {
        Type = InProcessMaterialLocation.LocType.InQueue,
        CurrentQueue = "source",
      },
      Action = new()
      {
        Type = action,
        AutomatedTransfer = automated,
        LoadCancellationId = token,
      },
    };
}
