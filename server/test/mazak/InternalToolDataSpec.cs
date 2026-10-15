/* Copyright (c) 2026, SeedTactics

All rights reserved.
*/

using System;
using System.Threading.Tasks;
using MazakMachineInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed class InternalToolDataSpec
{
  [Test]
  public async Task PreservesNativeQuantitiesAndLargeToolIdsWithoutDecodingStatusBits()
  {
    var tool = InternalToolData.ToToolPocket(
      new InternalToolData.Row()
      {
        DeviceNumber = 1,
        PocketNumber = 20,
        PocketStatus = 1,
        PocketOccupancy = 1,
        PocketAttribute = 5,
        ToolID = 4_000_000_000,
        ToolStatus = int.MaxValue,
        ToolInformation = int.MaxValue,
        GroupNumber = 7,
        NominalDiameter = 12000,
        LifeUsed = 123,
        LifeSpan = 300,
        LifeUsedCount = 12,
        LifeCount = 50,
      }
    );

    await Assert.That(tool.IsToolDataValid).IsTrue();
    await Assert.That(tool.PocketType).IsEqualTo(0);
    await Assert.That(tool.ToolID).IsEqualTo(4_000_000_000L);
    await Assert.That(tool.LifeUsed).IsEqualTo(123);
    await Assert.That(tool.LifeSpan).IsEqualTo(300);
    await Assert.That(tool.LifeUsedCount).IsEqualTo(12);
    await Assert.That(tool.LifeCount).IsEqualTo(50);
    await Assert.That(tool.IsToolLifeOver).IsNull();
    await Assert.That(tool.IsToolLifeBroken).IsNull();
    await Assert.That(tool.NominalDiameter).IsNull();
  }

  [Test]
  [Arguments(null, null, null)]
  [Arguments(0, 1, 0)]
  [Arguments(1, 0, 0)]
  [Arguments(1, 1, 4)]
  [Arguments(1, 1, 255)]
  public async Task MissingInvalidEmptyOrReservedPocketsAreExcluded(
    int? status,
    int? occupied,
    int? attribute
  )
  {
    var tool = InternalToolData.ToToolPocket(
      new InternalToolData.Row()
      {
        DeviceNumber = 1,
        PocketNumber = 20,
        PocketStatus = status,
        PocketOccupancy = occupied,
        PocketAttribute = attribute,
      }
    );
    await Assert.That(tool.IsToolDataValid).IsFalse();
  }

  [Test]
  [Arguments(0, 10)]
  [Arguments(17, 10)]
  [Arguments(1, 0)]
  [Arguments(1, 1000)]
  public async Task NonMachineOrOutOfRangePocketsAreExcluded(int device, int pocket)
  {
    var tool = InternalToolData.ToToolPocket(
      new InternalToolData.Row()
      {
        DeviceNumber = device,
        PocketNumber = pocket,
        PocketStatus = 1,
        PocketOccupancy = 1,
        PocketAttribute = 0,
      }
    );
    await Assert.That(tool.IsToolDataValid).IsFalse();
  }

  [Test]
  public async Task ExperimentCannotBeEnabledForWeb()
  {
    await Assert
      .That(() =>
        new OpenDatabaseKitDB(
          new MazakConfig() { DBType = MazakDbType.MazakWeb, ExperimentalInternalToolData = true },
          Substitute.For<ICurrentLoadActions>()
        )
      )
      .Throws<ArgumentException>();
  }
}
