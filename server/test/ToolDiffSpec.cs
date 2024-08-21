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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BlackMaple.MachineFramework;
using FluentAssertions;
using Xunit;

namespace MachineWatchTest;

public class ToolDiffSpec
{
  [Fact]
  public void ToolSnapshotDifference()
  {
    var start = new List<ToolSnapshot>();
    var end = new List<ToolSnapshot>();
    var expected = new List<ToolUse>();

    // first a normal use
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool1",
        CurrentUse = TimeSpan.FromSeconds(10),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool1",
        CurrentUse = TimeSpan.FromSeconds(50),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool1",
        Pocket = 0,
        ToolUseDuringCycle = TimeSpan.FromSeconds(50 - 10),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(50),
        ConfiguredToolLife = TimeSpan.FromSeconds(100),
        ToolChangeOccurred = null,
      }
    );

    // now an unused tool
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 1,
        ToolName = "tool2",
        CurrentUse = TimeSpan.FromSeconds(10),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 1,
        ToolName = "tool2",
        CurrentUse = TimeSpan.FromSeconds(10),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );

    // now a tool which is replaced and used
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 2,
        ToolName = "tool3",
        CurrentUse = TimeSpan.FromSeconds(70),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 2,
        ToolName = "tool3",
        CurrentUse = TimeSpan.FromSeconds(20),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool3",
        Pocket = 2,
        ToolUseDuringCycle = TimeSpan.FromSeconds(100 - 70 + 20),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(20),
        ConfiguredToolLife = TimeSpan.FromSeconds(100),
        ToolChangeOccurred = true,
      }
    );

    // now a pocket with two tools
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 3,
        ToolName = "tool4",
        CurrentUse = TimeSpan.FromSeconds(60),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 3,
        ToolName = "tool5",
        CurrentUse = TimeSpan.FromSeconds(80),
        TotalLifeTime = TimeSpan.FromSeconds(200),
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 3,
        ToolName = "tool4",
        CurrentUse = TimeSpan.FromSeconds(0),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 3,
        ToolName = "tool5",
        CurrentUse = TimeSpan.FromSeconds(110),
        TotalLifeTime = TimeSpan.FromSeconds(200),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool4",
        Pocket = 3,
        ToolUseDuringCycle = TimeSpan.FromSeconds(100 - 60),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(0),
        ConfiguredToolLife = TimeSpan.FromSeconds(100),
        ToolChangeOccurred = true,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool5",
        Pocket = 3,
        ToolUseDuringCycle = TimeSpan.FromSeconds(110 - 80),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(110),
        ConfiguredToolLife = TimeSpan.FromSeconds(200),
        ToolChangeOccurred = null,
      }
    );

    // now a tool which is removed and a new tool added
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 4,
        ToolName = "tool6",
        CurrentUse = TimeSpan.FromSeconds(65),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 4,
        ToolName = "tool7",
        CurrentUse = TimeSpan.FromSeconds(30),
        TotalLifeTime = TimeSpan.FromSeconds(120),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool6",
        Pocket = 4,
        ToolUseDuringCycle = TimeSpan.FromSeconds(100 - 65),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(0),
        ConfiguredToolLife = TimeSpan.FromSeconds(100),
        ToolChangeOccurred = true,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool7",
        Pocket = 4,
        ToolUseDuringCycle = TimeSpan.FromSeconds(30),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(30),
        ConfiguredToolLife = TimeSpan.FromSeconds(120),
        ToolChangeOccurred = null,
      }
    );

    // now a tool which is removed and nothing added
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 5,
        ToolName = "tool8",
        CurrentUse = TimeSpan.FromSeconds(80),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool8",
        Pocket = 5,
        ToolUseDuringCycle = TimeSpan.FromSeconds(100 - 80),
        TotalToolUseAtEndOfCycle = TimeSpan.Zero,
        ConfiguredToolLife = TimeSpan.FromSeconds(100),
        ToolChangeOccurred = true,
      }
    );

    // now a new tool which is appears
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 6,
        ToolName = "tool9",
        CurrentUse = TimeSpan.FromSeconds(15),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool9",
        Pocket = 6,
        ToolUseDuringCycle = TimeSpan.FromSeconds(15),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(15),
        ConfiguredToolLife = TimeSpan.FromSeconds(100),
        ToolChangeOccurred = null,
      }
    );

    // a new unused tool
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 7,
        ToolName = "tool10",
        CurrentUse = TimeSpan.FromSeconds(0),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );

    // same tools in separate pockets
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 8,
        ToolName = "tool11",
        CurrentUse = TimeSpan.FromSeconds(50),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 8,
        ToolName = "tool11",
        CurrentUse = TimeSpan.FromSeconds(77),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 9,
        ToolName = "tool11",
        CurrentUse = TimeSpan.FromSeconds(80),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 9,
        ToolName = "tool11",
        CurrentUse = TimeSpan.FromSeconds(13),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool11",
        Pocket = 8,
        ToolUseDuringCycle = TimeSpan.FromSeconds(77 - 50),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(77),
        ConfiguredToolLife = TimeSpan.FromSeconds(100),
        ToolChangeOccurred = null,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool11",
        Pocket = 9,
        ToolUseDuringCycle = TimeSpan.FromSeconds(100 - 80 + 13),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(13),
        ConfiguredToolLife = TimeSpan.FromSeconds(100),
        ToolChangeOccurred = true,
      }
    );

    // a tool which changed between cycles to the starting was zero
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 10,
        ToolName = "tool12",
        CurrentUse = TimeSpan.Zero,
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 10,
        ToolName = "tool12",
        CurrentUse = TimeSpan.FromSeconds(34),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool12",
        Pocket = 10,
        ToolUseDuringCycle = TimeSpan.FromSeconds(34),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(34),
        ConfiguredToolLife = TimeSpan.FromSeconds(100),
        ToolChangeOccurred = null,
      }
    );

    ToolSnapshotDiff.Diff(start, end).Should().BeEquivalentTo(expected);
  }

  [Fact]
  public void Counts()
  {
    var start = new List<ToolSnapshot>();
    var end = new List<ToolSnapshot>();
    var expected = new List<ToolUse>();

    // first a normal use
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool1",
        CurrentUseCount = 1,
        TotalLifeCount = 20,
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool1",
        CurrentUseCount = 8,
        TotalLifeCount = 30,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool1",
        Pocket = 0,
        ToolUseCountDuringCycle = 8 - 1,
        TotalToolUseCountAtEndOfCycle = 8,
        ConfiguredToolLifeCount = 30,
      }
    );

    // a normal use with both use and count
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool2",
        CurrentUse = TimeSpan.FromMinutes(20),
        TotalLifeTime = TimeSpan.FromMinutes(100),
        CurrentUseCount = 1,
        TotalLifeCount = 20,
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool2",
        CurrentUse = TimeSpan.FromMinutes(32),
        TotalLifeTime = TimeSpan.FromMinutes(110),
        CurrentUseCount = 8,
        TotalLifeCount = 30,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool2",
        Pocket = 0,
        ToolUseDuringCycle = TimeSpan.FromMinutes(32 - 20),
        TotalToolUseAtEndOfCycle = TimeSpan.FromMinutes(32),
        ConfiguredToolLife = TimeSpan.FromMinutes(110),
        ToolUseCountDuringCycle = 8 - 1,
        TotalToolUseCountAtEndOfCycle = 8,
        ConfiguredToolLifeCount = 30,
      }
    );

    // a tool that is not used
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool3",
        CurrentUseCount = 5,
        TotalLifeCount = 20,
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool3",
        CurrentUseCount = 5,
        TotalLifeCount = 30,
      }
    );

    // a not-used tool with both life and count
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool4",
        CurrentUse = TimeSpan.FromMinutes(44),
        TotalLifeTime = TimeSpan.FromMinutes(100),
        CurrentUseCount = 5,
        TotalLifeCount = 20,
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool4",
        CurrentUse = TimeSpan.FromMinutes(44),
        TotalLifeTime = TimeSpan.FromMinutes(110),
        CurrentUseCount = 5,
        TotalLifeCount = 30,
      }
    );

    // a tool with only the count changing but not life (probably not realistic, but ...)
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool5",
        CurrentUse = TimeSpan.FromMinutes(24),
        TotalLifeTime = TimeSpan.FromMinutes(100),
        CurrentUseCount = 4,
        TotalLifeCount = 30,
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool5",
        CurrentUse = TimeSpan.FromMinutes(24),
        TotalLifeTime = TimeSpan.FromMinutes(100),
        CurrentUseCount = 7,
        TotalLifeCount = 30,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool5",
        Pocket = 0,
        TotalToolUseAtEndOfCycle = TimeSpan.FromMinutes(24),
        ConfiguredToolLife = TimeSpan.FromMinutes(100),
        ToolUseCountDuringCycle = 7 - 4,
        TotalToolUseCountAtEndOfCycle = 7,
        ConfiguredToolLifeCount = 30,
      }
    );

    // a tool with only the life changing but not count (probably not realistic, but ...)
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool6",
        CurrentUse = TimeSpan.FromMinutes(24),
        TotalLifeTime = TimeSpan.FromMinutes(100),
        CurrentUseCount = 6,
        TotalLifeCount = 30,
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool6",
        CurrentUse = TimeSpan.FromMinutes(35),
        TotalLifeTime = TimeSpan.FromMinutes(100),
        CurrentUseCount = 6,
        TotalLifeCount = 30,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool6",
        Pocket = 0,
        ToolUseDuringCycle = TimeSpan.FromMinutes(35 - 24),
        TotalToolUseAtEndOfCycle = TimeSpan.FromMinutes(35),
        ConfiguredToolLife = TimeSpan.FromMinutes(100),
        TotalToolUseCountAtEndOfCycle = 6,
        ConfiguredToolLifeCount = 30,
      }
    );

    // a tool that was replaced and used
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool7",
        CurrentUseCount = 6,
        TotalLifeCount = 20,
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 0,
        ToolName = "tool7",
        CurrentUseCount = 2,
        TotalLifeCount = 30,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool7",
        Pocket = 0,
        ToolChangeOccurred = true,
        ToolUseCountDuringCycle = 20 - 6 + 2,
        TotalToolUseCountAtEndOfCycle = 2,
        ConfiguredToolLifeCount = 20,
      }
    );

    // now a new tool which is appears
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 6,
        ToolName = "tool8",
        CurrentUseCount = 4,
        TotalLifeCount = 22,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool8",
        Pocket = 6,
        ToolUseCountDuringCycle = 4,
        TotalToolUseCountAtEndOfCycle = 4,
        ConfiguredToolLifeCount = 22,
      }
    );

    // a new unused tool
    end.Add(
      new ToolSnapshot()
      {
        Pocket = 7,
        ToolName = "tool9",
        CurrentUseCount = 0,
        TotalLifeCount = 40,
      }
    );

    // a tool that was removed with nothing added
    start.Add(
      new ToolSnapshot()
      {
        Pocket = 6,
        ToolName = "tool10",
        CurrentUseCount = 4,
        TotalLifeCount = 22,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool10",
        Pocket = 6,
        ToolChangeOccurred = true,
        ToolUseCountDuringCycle = 22 - 4,
        TotalToolUseCountAtEndOfCycle = 0,
        ConfiguredToolLifeCount = 22,
      }
    );

    ToolSnapshotDiff.Diff(start, end).Should().BeEquivalentTo(expected);
  }

  [Fact]
  public void Serials()
  {
    var start = new List<ToolSnapshot>();
    var end = new List<ToolSnapshot>();
    var expected = new List<ToolUse>();

    // a normal use
    start.Add(
      new ToolSnapshot()
      {
        Serial = "aa",
        Pocket = 0,
        ToolName = "tool1",
        CurrentUseCount = 3,
        TotalLifeCount = 20,
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Serial = "aa",
        Pocket = 0,
        ToolName = "tool1",
        CurrentUseCount = 6,
        TotalLifeCount = 30,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool1",
        Pocket = 0,
        ToolSerialAtStartOfCycle = "aa",
        ToolSerialAtEndOfCycle = "aa",
        ToolUseCountDuringCycle = 6 - 3,
        TotalToolUseCountAtEndOfCycle = 6,
        ConfiguredToolLifeCount = 30,
      }
    );

    // not used
    start.Add(
      new ToolSnapshot()
      {
        Serial = "bb",
        Pocket = 1,
        ToolName = "tool1",
        CurrentUseCount = 4,
        TotalLifeCount = 20,
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Serial = "bb",
        Pocket = 1,
        ToolName = "tool1",
        CurrentUseCount = 4,
        TotalLifeCount = 20,
      }
    );

    // a tool that was replaced in the same pocket with a different serial
    start.Add(
      new ToolSnapshot()
      {
        Serial = "cc",
        Pocket = 2,
        ToolName = "tool1",
        CurrentUse = TimeSpan.FromSeconds(50),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    end.Add(
      new ToolSnapshot()
      {
        Serial = "dd",
        Pocket = 2,
        ToolName = "tool1",
        CurrentUse = TimeSpan.FromSeconds(60),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool1",
        Pocket = 2,
        ToolChangeOccurred = true,
        ToolSerialAtStartOfCycle = "cc",
        ToolSerialAtEndOfCycle = "dd",
        ToolUseDuringCycle = TimeSpan.FromSeconds(100 - 50 + 60),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(60),
        ConfiguredToolLife = TimeSpan.FromSeconds(100),
      }
    );

    // a tool moved to an existing pocket and a tool removed
    start.Add(
      new ToolSnapshot()
      {
        Serial = "ee",
        Pocket = 3,
        ToolName = "tool1",
        CurrentUse = TimeSpan.FromSeconds(30),
        TotalLifeTime = TimeSpan.FromSeconds(100),
      }
    );
    start.Add(
      new ToolSnapshot()
      {
        Serial = "ff",
        Pocket = 4,
        ToolName = "tool1",
        CurrentUse = TimeSpan.FromSeconds(4),
        TotalLifeTime = TimeSpan.FromSeconds(101),
      }
    );
    // ee moved into pocket 4
    end.Add(
      new ToolSnapshot()
      {
        Serial = "ee",
        Pocket = 4,
        ToolName = "tool1",
        CurrentUse = TimeSpan.FromSeconds(35),
        TotalLifeTime = TimeSpan.FromSeconds(102),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool1",
        Pocket = 4,
        ToolChangeOccurred = null,
        ToolSerialAtStartOfCycle = "ee",
        ToolSerialAtEndOfCycle = "ee",
        ToolUseDuringCycle = TimeSpan.FromSeconds(35 - 30),
        TotalToolUseAtEndOfCycle = TimeSpan.FromSeconds(35),
        ConfiguredToolLife = TimeSpan.FromSeconds(102),
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool1",
        Pocket = -4,
        ToolChangeOccurred = true,
        ToolSerialAtStartOfCycle = "ff",
        ToolSerialAtEndOfCycle = null,
        ToolUseDuringCycle = TimeSpan.FromSeconds(101 - 4),
        TotalToolUseAtEndOfCycle = TimeSpan.Zero,
        ConfiguredToolLife = TimeSpan.FromSeconds(101),
      }
    );

    // a new tool added
    end.Add(
      new ToolSnapshot()
      {
        Serial = "gg",
        Pocket = 5,
        ToolName = "tool1",
        CurrentUseCount = 4,
        TotalLifeCount = 22,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool1",
        Pocket = 5,
        ToolSerialAtStartOfCycle = null,
        ToolSerialAtEndOfCycle = "gg",
        ToolUseCountDuringCycle = 4,
        TotalToolUseCountAtEndOfCycle = 4,
        ConfiguredToolLifeCount = 22,
      }
    );

    // a tool removed
    start.Add(
      new ToolSnapshot()
      {
        Serial = "hh",
        Pocket = 6,
        ToolName = "tool1",
        CurrentUseCount = 6,
        TotalLifeCount = 18,
      }
    );
    expected.Add(
      new ToolUse()
      {
        Tool = "tool1",
        Pocket = 6,
        ToolChangeOccurred = true,
        ToolSerialAtStartOfCycle = "hh",
        ToolSerialAtEndOfCycle = null,
        ToolUseCountDuringCycle = 18 - 6,
        TotalToolUseCountAtEndOfCycle = 0,
        ConfiguredToolLifeCount = 18,
      }
    );

    ToolSnapshotDiff.Diff(start, end).Should().BeEquivalentTo(expected);
  }
}
