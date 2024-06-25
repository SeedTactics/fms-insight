/* Copyright (c) 2024, John Lenz

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
using Microsoft.Extensions.Configuration;

#nullable enable

namespace BlackMaple.FMSInsight.Niigata;

public record NiigataSettings
{
  private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<NiigataSettings>();

  public required string ProgramDirectory { get; init; }
  public required NiigataStationNames StationNames { get; init; }
  public required ImmutableList<string> MachineIPs { get; init; }
  public required string SQLConnectionString { get; init; }
  public required bool RequireProgramsInJobs { get; init; }
  public Func<ActiveJob, bool>? DecrementJobFilter { get; init; } = null;

  public static NiigataSettings Load(IConfiguration config)
  {
    var ProgramDirectory = config.GetValue<string>("Program Directory") ?? "";
    if (!System.IO.Directory.Exists(ProgramDirectory))
    {
      Log.Error("Program directory {dir} does not exist", ProgramDirectory);
    }

    var reclampNames = config.GetValue<string>("Reclamp Group Names");
    var machineNames = config.GetValue<string>("Machine Names");
    var StationNames = new NiigataStationNames()
    {
      ReclampGroupNames = new HashSet<string>(
        string.IsNullOrEmpty(reclampNames)
          ? Enumerable.Empty<string>()
          : reclampNames.Split(',').Select(s => s.Trim())
      ),
      IccMachineToJobMachNames = string.IsNullOrEmpty(machineNames)
        ? []
        : machineNames
          .Split(',')
          .Select(m => m.Trim())
          .Select(
            (machineName, idx) =>
            {
              if (!char.IsDigit(machineName.Last()))
              {
                return null;
              }
              var group = new string(machineName.Reverse().SkipWhile(char.IsDigit).Reverse().ToArray());
              if (int.TryParse(machineName.AsSpan(group.Length), out var num))
              {
                return new
                {
                  iccMc = idx + 1,
                  group,
                  num
                };
              }
              else
              {
                return null;
              }
            }
          )
          .Where(x => x != null)
          .ToDictionary(x => x!.iccMc, x => (x!.group, x.num))
    };

    return new NiigataSettings()
    {
      ProgramDirectory = ProgramDirectory,
      StationNames = StationNames,
      MachineIPs =
        config.GetValue<string>("Machine IP Addresses")?.Split(',').Select(s => s.Trim()).ToImmutableList()
        ?? ImmutableList<string>.Empty,

      SQLConnectionString = config.GetValue<string>("Connection String") ?? "",

      RequireProgramsInJobs = config.GetValue<bool>("Require Programs In Jobs", true),
    };
  }
}
