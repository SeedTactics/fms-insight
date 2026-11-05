#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using MazakMachineInterface;
using NSubstitute;

namespace BlackMaple.FMSInsight.Mazak.Tests;

public sealed partial class MazakAcquisitionSpec
{
  [Test]
  public async Task TimerOnlyToolDiagnosticChangesReachCachedStatusAndConnectedClients()
  {
    using var cell = new Cell();
    var toolReadFails = 0;
    var readTools = 1;
    var calculated = 0;
    cell.BeforeTools = () =>
    {
      if (Volatile.Read(ref toolReadFails) != 0)
        throw new IOException("Tool observations unavailable");
    };
    // The staged caller explicitly provides supplementary tool observations, even when there
    // are no machining events. Production acquisition's event-driven query policy is unchanged.
    // Exercise the real default publisher and reconciliation without any schedule-writing policy;
    // no refresh request or source event forces publication.
    var synchronization = Substitute.For<ISynchronizeCellState<MazakState>>();
    synchronization
      .CalculateCellState(Arg.Any<IRepository>())
      .Returns(call =>
      {
        var repository = call.Arg<IRepository>();
        var observed = cell.Sync.AcquireCellState(cell.Sync.CaptureLogWatermark(repository));
        if (Volatile.Read(ref readTools) != 0)
          try
          {
            observed = observed with { Tools = cell.Native.LoadTools().ToImmutableList() };
          }
          catch (IOException)
          {
            observed = observed with { ToolReadFailed = true };
          }
        var reconciled = cell.Sync.ReconcileCellState(repository, observed);
        cell.Sync.AcknowledgeLogs(reconciled);
        Interlocked.Increment(ref calculated);
        return reconciled.State with { TimeUntilNextRefresh = TimeSpan.FromMilliseconds(25) };
      });
    using var host = new JobsAndQueuesFromDb<MazakState>(
      cell.Repositories,
      new ServerSettings(),
      new FMSSettings(),
      synchronization,
      startThread: false
    );
    var published = new ConcurrentQueue<CurrentStatus>();
    host.OnNewCurrentStatus += published.Enqueue;
    host.StartThread();
    static bool HasDiagnostic(CurrentStatus status) =>
      status.AlarmDetails?.Any(a => a.Code == "mazak-tools-unavailable") == true;
    async Task Until(Func<bool> condition)
    {
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      while (!condition())
        await Task.Delay(10, timeout.Token);
    }
    await Until(() => !published.IsEmpty);
    var initial = published.Last();
    Volatile.Write(ref toolReadFails, 1);
    await Until(() => published.Any(s => s.Revision > initial.Revision && HasDiagnostic(s)));
    await Assert.That(HasDiagnostic(host.GetCurrentStatus())).IsTrue();
    var unavailable = published.Last(HasDiagnostic);
    Volatile.Write(ref readTools, 0);
    var beforeIdle = Volatile.Read(ref calculated);
    await Until(() => Volatile.Read(ref calculated) >= beforeIdle + 2);
    var reads = cell
      .Native.ReceivedCalls()
      .Count(call => call.GetMethodInfo().Name == nameof(IMazakDB.LoadTools));
    var idle = Volatile.Read(ref calculated);
    await Until(() => Volatile.Read(ref calculated) >= idle + 2);
    await Assert.That(HasDiagnostic(host.GetCurrentStatus())).IsTrue();
    await Assert
      .That(
        cell.Native.ReceivedCalls()
          .Count(call => call.GetMethodInfo().Name == nameof(IMazakDB.LoadTools))
      )
      .IsEqualTo(reads);
    // Recover through a successful observed-empty result, without manufacturing any event.
    // Both the cached HTTP assessment and subscribers must receive that diagnostic transition.
    cell.Tools = [];
    Volatile.Write(ref toolReadFails, 0);
    Volatile.Write(ref readTools, 1);
    await Until(() => published.Any(s => s.Revision > unavailable.Revision && !HasDiagnostic(s)));
    await Assert.That(HasDiagnostic(host.GetCurrentStatus())).IsFalse();
    await Assert.That(cell.Repository.GetRecentLog(0)).IsEmpty();
  }
}
