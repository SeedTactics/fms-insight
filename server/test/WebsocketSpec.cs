/* Copyright (c) 2026, SeedTactics

All rights reserved.
*/

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using BlackMaple.MachineFramework.Controllers;
using NSubstitute;

namespace BlackMaple.FMSInsight.Tests;

public sealed class WebsocketSpec
{
  [Test]
  public async Task CurrentStatusPublishedAfterShutdownIsIgnored()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var jobAndQueue = Substitute.For<IJobAndQueueControl>();
    var manager = new WebsocketManager(repository, jobAndQueue);
    await manager.DisposeAsync();
    var status = new CurrentStatus
    {
      TimeOfCurrentStatusUTC = DateTime.UtcNow,
      Jobs = ImmutableDictionary<string, ActiveJob>.Empty,
      Pallets = ImmutableDictionary<int, PalletStatus>.Empty,
      Material = [],
      Alarms = [],
      Queues = ImmutableDictionary<string, QueueInfo>.Empty,
    };

    jobAndQueue.OnNewCurrentStatus += Raise.Event<NewCurrentStatus>(status);
  }
}
