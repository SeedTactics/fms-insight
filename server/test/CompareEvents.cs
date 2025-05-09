using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;
using Shouldly;

namespace BlackMaple.FMSInsight.Tests;

public static class CompareEvents
{
  public static void EventsShouldBe(this IEnumerable<LogEntry> actual, IEnumerable<LogEntry> expected)
  {
    // We don't care about the exact ordering of events, which depends on the exact internals of
    // event processing.  The Counter is also dependent upon the order of events, so we set it to 0
    // before comparing the two lists.

    var sortedActual = actual
      .OrderBy(e => e.EndTimeUTC)
      .ThenBy(e => e.LogType)
      .ThenBy(e => e.StartOfCycle)
      .ThenBy(e => e.Material?.FirstOrDefault()?.MaterialID)
      .Select(e =>
        e with
        {
          Counter = 0,
          Tools = e.Tools?.OrderBy(t => t.Tool).ToImmutableList(),
          Material = e.Material.OrderBy(m => m.MaterialID).ToImmutableList(),
        }
      )
      .ToList();

    var sortedExpected = expected
      .OrderBy(e => e.EndTimeUTC)
      .ThenBy(e => e.LogType)
      .ThenBy(e => e.StartOfCycle)
      .ThenBy(e => e.Material?.FirstOrDefault()?.MaterialID)
      .Select(e =>
        e with
        {
          Counter = 0,
          Tools = e.Tools?.OrderBy(t => t.Tool).ToImmutableList(),
          Material = e.Material.OrderBy(m => m.MaterialID).ToImmutableList(),
        }
      )
      .ToList();

    sortedActual.ShouldBeEquivalentTo(sortedExpected);
  }
}
