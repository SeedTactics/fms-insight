using System;
using System.Collections.Generic;
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
      .Select(e => e with { Counter = 0 })
      .ToList();

    var sortedExpected = expected
      .OrderBy(e => e.EndTimeUTC)
      .ThenBy(e => e.LogType)
      .ThenBy(e => e.StartOfCycle)
      .Select(e => e with { Counter = 0 })
      .ToList();

    sortedActual.ShouldBeEquivalentTo(sortedExpected);
  }
}
