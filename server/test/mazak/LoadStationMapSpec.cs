using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using MazakMachineInterface;
using Microsoft.Extensions.Configuration;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public class LoadStationMapSpec
{
  [Test]
  public async Task ConfigurationUsesListPositionAndRejectsRemovedOffset()
  {
    var values = new Dictionary<string, string?>
    {
      ["Mazak:Smooth Version"] = "true",
      ["Mazak:Proxy DB Url"] = "http://localhost:5200",
      ["Mazak:Load Station Numbers"] = "10, 30",
    };
    var config = MazakConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    await Assert.That(config.TranslateLoadStationNumber(2)).IsEqualTo(30);
    await Assert.That(config.InverseLoadStationNumber(30)).IsEqualTo(2);
    await Assert
      .That(() => config.TranslateLoadStationNumber(3))
      .Throws<ArgumentOutOfRangeException>();
    await Assert
      .That(() => config.InverseLoadStationNumber(20))
      .Throws<ArgumentOutOfRangeException>();
    values["Mazak:Starting Load Station Number"] = "10";
    await Assert
      .That(() =>
        MazakConfig.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
      )
      .Throws<InvalidOperationException>();
  }

  [Test]
  public async Task ExplicitMapRejectsAmbiguousOrInvalidIdentity()
  {
    foreach (
      var numbers in new ImmutableList<int>[]
      {
        [],
        [10, 10],
        [0],
        [-1],
        [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
      }
    )
      await Assert
        .That(() =>
          new MazakConfig { DBType = MazakDbType.MazakSmooth, LoadStationNumbers = numbers }
        )
        .Throws<ArgumentException>();
  }
}
