/* Copyright (c) 2022, John Lenz

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

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace BlackMaple.MachineFramework
{
  public static class ToolSnapshotDiff
  {
    /** Looks for a matching tool or tools for the specified endTool
     *
     * This method returns two possible tools: startSerial and startPocket.  startSerial is the exact matching tool to endTool.
     * startPocket is a tool that was in the same pocket as the endTool but is a different tool.
     *
     * When using serials, if the tool with matching serial is still in the same pocket, it is put in startSerial.
     * If the tool was removed and a new serial inserted, startPocket will have the old tool and startSerial will be null.
     * While unlikely, it is possible for the tool to not be removed but to change which pocket it is in.
     * In this case, startPocket will be null and startSerial will have the old tool.
     *
     * When not using serials, we attempt to guess when the tool was changed.  If the tool was changed, startPocket will have the new tool
     * and if the tool was not changed, startPocket will be null and startSerial will have the old tool.
     */
    private static void FindMatchingTool(
      ToolSnapshot endTool,
      Dictionary<(int, string), ToolSnapshot> startPockets,
      IReadOnlyDictionary<string, ToolSnapshot> startSerials,
      out ToolSnapshot? startPocket,
      out ToolSnapshot? startSerial
    )
    {
      startPocket = null;
      startSerial = null;

      if (!string.IsNullOrEmpty(endTool.Serial))
      {
        startSerials.TryGetValue(endTool.Serial, out startSerial);
        if (startSerial == null)
        {
          startPockets.TryGetValue((endTool.Pocket, endTool.ToolName), out startPocket);
        }
      }
      else
      {
        startPockets.TryGetValue((endTool.Pocket, endTool.ToolName), out startPocket);

        // since we don't have serials, we try and guess when the tool is the same.  If the tool is the same,
        // we put the tool into the startSerial var and set startPocket to null
        if (
          startPocket != null
          && (
            (
              startPocket.CurrentUse.HasValue
              && endTool.CurrentUse.HasValue
              && startPocket.CurrentUse <= endTool.CurrentUse
            )
            || (
              startPocket.CurrentUseCount.HasValue
              && endTool.CurrentUseCount.HasValue
              && startPocket.CurrentUseCount <= endTool.CurrentUseCount
            )
          )
        )
        {
          startSerial = startPocket;
          startPocket = null;
        }
      }

      // remove these tools so they are not considered again
      if (startSerial != null)
      {
        startPockets.Remove((startSerial.Pocket, startSerial.ToolName));
      }
      if (startPocket != null)
      {
        startPockets.Remove((startPocket.Pocket, startPocket.ToolName));
      }
    }

    public static ImmutableList<ToolUse> Diff(IEnumerable<ToolSnapshot> start, IEnumerable<ToolSnapshot> end)
    {
      if (start == null)
        start = Enumerable.Empty<ToolSnapshot>();
      if (end == null)
        end = Enumerable.Empty<ToolSnapshot>();

      var startPockets = start.ToDictionary(t => (t.Pocket, t.ToolName));
      var startSerials = start.Where(t => !string.IsNullOrEmpty(t.Serial)).ToDictionary(t => t.Serial!);
      var endPockets = end.ToDictionary(t => (t.Pocket, t.ToolName));

      var tools = ImmutableList.CreateBuilder<ToolUse>();

      foreach (var endTool in end)
      {
        FindMatchingTool(endTool, startPockets, startSerials, out var startPocket, out var startSerial);

        // calculate usage
        TimeSpan? usageTime = endTool.CurrentUse;
        int? usageCount = endTool.CurrentUseCount;

        if (startSerial != null)
        {
          if (startSerial.CurrentUse.HasValue)
          {
            usageTime = (usageTime ?? TimeSpan.Zero) - startSerial.CurrentUse;
          }
          if (startSerial.CurrentUseCount.HasValue)
          {
            usageCount = (usageCount ?? 0) - startSerial.CurrentUseCount;
          }
        }

        if (startPocket != null)
        {
          // assume startPocket was used until lifetime was reached
          if (startPocket.TotalLifeTime.HasValue && startPocket.CurrentUse.HasValue)
          {
            usageTime =
              (usageTime ?? TimeSpan.Zero)
              + TimeSpan.FromTicks(
                Math.Max(0, startPocket.TotalLifeTime.Value.Ticks - startPocket.CurrentUse.Value.Ticks)
              );
          }
          if (startPocket.TotalLifeCount.HasValue && startPocket.CurrentUseCount.HasValue)
          {
            usageCount =
              (usageCount ?? 0)
              + Math.Max(0, startPocket.TotalLifeCount.Value - startPocket.CurrentUseCount.Value);
          }
        }

        if (usageTime.HasValue && usageTime <= TimeSpan.Zero)
          usageTime = null;
        if (usageCount.HasValue && usageCount <= 0)
          usageCount = null;

        // add the tool if there is some usage
        if (usageTime.HasValue || usageCount.HasValue)
        {
          tools.Add(
            new ToolUse()
            {
              Tool = endTool.ToolName,
              Pocket = endTool.Pocket,
              ToolChangeOccurred = startPocket == null ? null : (bool?)true,
              ToolSerialAtStartOfCycle = startPocket?.Serial ?? startSerial?.Serial, // check startPocket first because that is when there was a different serial at the start
              ToolSerialAtEndOfCycle = endTool.Serial,
              ToolUseDuringCycle = usageTime,
              TotalToolUseAtEndOfCycle = endTool.CurrentUse,
              ConfiguredToolLife = startPocket?.TotalLifeTime ?? endTool.TotalLifeTime,
              ToolUseCountDuringCycle = usageCount,
              TotalToolUseCountAtEndOfCycle = endTool.CurrentUseCount,
              ConfiguredToolLifeCount = startPocket?.TotalLifeCount ?? endTool.TotalLifeCount
            }
          );
        }
      }

      foreach (var startTool in startPockets.Values)
      {
        var pocket = startTool.Pocket;
        // assume the tool was used until lifetime and then removed
        tools.Add(
          new ToolUse()
          {
            Tool = startTool.ToolName,
            // The (tool,pocket) must be unique, so use the negative of the pocket if it is already in the list.
            // This can only occur in the rare case when serials are used and tools are shuffled around
            // between multiple pockets.
            Pocket = endPockets.ContainsKey((startTool.Pocket, startTool.ToolName))
              ? -startTool.Pocket
              : startTool.Pocket,
            ToolChangeOccurred = true,
            ToolSerialAtStartOfCycle = startTool.Serial,
            ToolSerialAtEndOfCycle = null,
            ToolUseDuringCycle =
              startTool.TotalLifeTime.HasValue && startTool.CurrentUse.HasValue
                ? TimeSpan.FromTicks(
                  Math.Max(0, startTool.TotalLifeTime.Value.Ticks - startTool.CurrentUse.Value.Ticks)
                )
                : null,
            TotalToolUseAtEndOfCycle = startTool.CurrentUse.HasValue ? TimeSpan.Zero : null,
            ConfiguredToolLife = startTool.TotalLifeTime,
            ToolUseCountDuringCycle =
              startTool.TotalLifeCount.HasValue && startTool.CurrentUseCount.HasValue
                ? Math.Max(0, startTool.TotalLifeCount.Value - startTool.CurrentUseCount.Value)
                : null,
            TotalToolUseCountAtEndOfCycle = startTool.CurrentUseCount.HasValue ? 0 : null,
            ConfiguredToolLifeCount = startTool.TotalLifeCount,
          }
        );
      }

      return tools.ToImmutable();
    }
  }
}
