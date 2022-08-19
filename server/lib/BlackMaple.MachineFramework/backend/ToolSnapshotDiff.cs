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
using System.Linq;

namespace BlackMaple.MachineFramework
{
  public static class ToolSnapshotDiff
  {
    public static IDictionary<string, ToolUse> Diff(IEnumerable<ToolPocketSnapshot> start, IEnumerable<ToolPocketSnapshot> end)
    {
      if (start == null) start = Enumerable.Empty<ToolPocketSnapshot>();
      if (end == null) end = Enumerable.Empty<ToolPocketSnapshot>();
      var endPockets = new Dictionary<(int, string), ToolPocketSnapshot>();
      foreach (var t in end)
      {
        endPockets[(t.PocketNumber, t.Tool)] = t;
      }

      var tools = new Dictionary<string, ToolUse>();
      void addUse(string tool, ToolUse use)
      {
        if (tools.TryGetValue(tool, out var existingUse))
        {
          tools[tool] %= draft =>
          {
            draft.ToolUseDuringCycle += use.ToolUseDuringCycle;
            draft.ConfiguredToolLife += use.ConfiguredToolLife;
            draft.TotalToolUseAtEndOfCycle += use.TotalToolUseAtEndOfCycle;
            draft.ToolChangeOccurred = existingUse.ToolChangeOccurred.GetValueOrDefault() || use.ToolChangeOccurred.GetValueOrDefault();
          };
        }
        else
        {
          tools[tool] = use;
        }
      }

      foreach (var startPocket in start)
      {
        if (endPockets.TryGetValue((startPocket.PocketNumber, startPocket.Tool), out var endPocket))
        {
          endPockets.Remove((startPocket.PocketNumber, startPocket.Tool));

          if (startPocket.CurrentUse < endPocket.CurrentUse)
          {
            // no tool change
            addUse(startPocket.Tool, new ToolUse()
            {
              ToolUseDuringCycle = endPocket.CurrentUse - startPocket.CurrentUse,
              TotalToolUseAtEndOfCycle = endPocket.CurrentUse,
              ConfiguredToolLife = endPocket.ToolLife,
              ToolChangeOccurred = false
            });
          }
          else if (endPocket.CurrentUse < startPocket.CurrentUse)
          {
            // there was a tool change
            addUse(startPocket.Tool, new ToolUse()
            {
              ToolUseDuringCycle = TimeSpan.FromTicks(Math.Max(0, startPocket.ToolLife.Ticks - startPocket.CurrentUse.Ticks)) + endPocket.CurrentUse,
              TotalToolUseAtEndOfCycle = endPocket.CurrentUse,
              ConfiguredToolLife = startPocket.ToolLife,
              ToolChangeOccurred = true
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
          addUse(startPocket.Tool, new ToolUse()
          {
            ToolUseDuringCycle = TimeSpan.FromTicks(Math.Max(0, startPocket.ToolLife.Ticks - startPocket.CurrentUse.Ticks)),
            TotalToolUseAtEndOfCycle = TimeSpan.Zero,
            ConfiguredToolLife = startPocket.ToolLife,
            ToolChangeOccurred = true
          });
        }
      }

      // now any new tools which appeared
      foreach (var endPocket in endPockets.Values)
      {
        if (endPocket.CurrentUse.Ticks > 0)
        {
          addUse(endPocket.Tool, new ToolUse()
          {
            ToolUseDuringCycle = endPocket.CurrentUse,
            TotalToolUseAtEndOfCycle = endPocket.CurrentUse,
            ConfiguredToolLife = endPocket.ToolLife,
            ToolChangeOccurred = false
          });
        }
      }

      return tools;
    }
  }
}