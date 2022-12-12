/* Copyright (c) 2020, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace BlackMaple.MachineFramework
{
  public static class ToolSnapshotDiff
  {
    public static ImmutableList<ToolUse> Diff(IEnumerable<ToolSnapshot> start, IEnumerable<ToolSnapshot> end)
    {
      if (start == null) start = Enumerable.Empty<ToolSnapshot>();
      if (end == null) end = Enumerable.Empty<ToolSnapshot>();
      var endPockets = new Dictionary<(int, string), ToolSnapshot>();
      foreach (var t in end)
      {
        endPockets[(t.Pocket, t.ToolName)] = t;
      }

      var tools = ImmutableList.CreateBuilder<ToolUse>();

      foreach (var startPocket in start)
      {
        if (endPockets.TryGetValue((startPocket.Pocket, startPocket.ToolName), out var endPocket))
        {
          endPockets.Remove((startPocket.Pocket, startPocket.ToolName));

          if (startPocket.CurrentUse < endPocket.CurrentUse)
          {
            // no tool change
            tools.Add(new ToolUse()
            {
              Tool = startPocket.ToolName,
              Pocket = startPocket.Pocket,
              ToolUseDuringCycle =
                endPocket.CurrentUse.HasValue && startPocket.CurrentUse.HasValue
                  ? endPocket.CurrentUse.Value - startPocket.CurrentUse.Value
                  : null,
              TotalToolUseAtEndOfCycle = endPocket.CurrentUse,
              ConfiguredToolLife = endPocket.TotalLifeTime,
              ToolChangeOccurred = false,
              ToolSerialAtStartOfCycle = null,
              ToolSerialAtEndOfCycle = null,
              ToolUseCountDuringCycle = null,
              TotalToolUseCountAtEndOfCycle = null,
              ConfiguredToolLifeCount = null,
            });
          }
          else if (endPocket.CurrentUse < startPocket.CurrentUse)
          {
            // there was a tool change
            tools.Add(new ToolUse()
            {
              Tool = startPocket.ToolName,
              Pocket = startPocket.Pocket,
              ToolUseDuringCycle =
                startPocket.TotalLifeTime.HasValue && startPocket.CurrentUse.HasValue && endPocket.CurrentUse.HasValue
                 ? TimeSpan.FromTicks(Math.Max(0, startPocket.TotalLifeTime.Value.Ticks - startPocket.CurrentUse.Value.Ticks)) + endPocket.CurrentUse.Value
                 : null,
              TotalToolUseAtEndOfCycle = endPocket.CurrentUse,
              ConfiguredToolLife = startPocket.TotalLifeTime,
              ToolChangeOccurred = true,
              ToolSerialAtStartOfCycle = null,
              ToolSerialAtEndOfCycle = null,
              ToolUseCountDuringCycle = null,
              TotalToolUseCountAtEndOfCycle = null,
              ConfiguredToolLifeCount = null,
            });
          }
          else
          {
            // tool was not used, use same at beginning and end
          }
        }
        else
        {
          // no matching tool at end
          // assume start tool was used until life
          tools.Add(new ToolUse()
          {
            Tool = startPocket.ToolName,
            Pocket = startPocket.Pocket,
            ToolUseDuringCycle =
              startPocket.TotalLifeTime.HasValue && startPocket.CurrentUse.HasValue
                ? TimeSpan.FromTicks(Math.Max(0, startPocket.TotalLifeTime.Value.Ticks - startPocket.CurrentUse.Value.Ticks))
                : null,
            TotalToolUseAtEndOfCycle = TimeSpan.Zero,
            ConfiguredToolLife = startPocket.TotalLifeTime,
            ToolChangeOccurred = true,
            ToolSerialAtStartOfCycle = null,
            ToolSerialAtEndOfCycle = null,
            ToolUseCountDuringCycle = null,
            TotalToolUseCountAtEndOfCycle = null,
            ConfiguredToolLifeCount = null,
          });
        }
      }

      // now any new tools which appeared
      foreach (var endPocket in endPockets.Values)
      {
        if (endPocket.CurrentUse.HasValue && endPocket.CurrentUse.Value.Ticks > 0)
        {
          tools.Add(new ToolUse()
          {
            Tool = endPocket.ToolName,
            Pocket = endPocket.Pocket,
            ToolUseDuringCycle = endPocket.CurrentUse,
            TotalToolUseAtEndOfCycle = endPocket.CurrentUse,
            ConfiguredToolLife = endPocket.TotalLifeTime,
            ToolChangeOccurred = false,
            ToolSerialAtStartOfCycle = null,
            ToolSerialAtEndOfCycle = null,
            ToolUseCountDuringCycle = null,
            TotalToolUseCountAtEndOfCycle = null,
            ConfiguredToolLifeCount = null,
          });
        }
      }

      return tools.ToImmutable();
    }
  }
}