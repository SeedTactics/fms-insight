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
using System.Text.Json;
using System.Text.Json.Serialization;
using BlackMaple.MachineFramework;

#nullable enable

namespace BlackMaple.FMSInsight.Niigata
{
  public record ProgramsForProcess
  {
    [JsonPropertyName("StopIndex")]
    public int MachineStopIndex { get; init; }

    public string? ProgramName { get; init; }

    public long? Revision { get; init; }
  }

  /// Recorded as a general message in the log to keep track of what we decided to set on each niigata pallet route
  public record AssignedJobAndPathForFace
  {
    public int Face { get; init; }

    public string? Unique { get; init; }

    public int Proc { get; init; }

    public int Path { get; init; }

    public ImmutableList<ProgramsForProcess>? ProgOverride { get; init; }
  }

  public static class RecordFacesForPallet
  {
    public static IEnumerable<AssignedJobAndPathForFace>? Load(string palComment, IRepository logDB)
    {
      if (palComment == null || !palComment.StartsWith("Insight:"))
      {
        return Enumerable.Empty<AssignedJobAndPathForFace>();
      }
      var msg = logDB.OriginalMessageByForeignID("faces:" + palComment.Substring(8)); // substring 8 removes Insight: prefix
      if (string.IsNullOrEmpty(msg))
      {
        Serilog.Log.Error("Unable to find faces for pallet comment {comment}", palComment);
        return Enumerable.Empty<AssignedJobAndPathForFace>();
      }

      return JsonSerializer.Deserialize<List<AssignedJobAndPathForFace>>(msg);
    }

    public static string Save(
      int pal,
      DateTime nowUtc,
      IEnumerable<AssignedJobAndPathForFace> newPaths,
      IRepository logDB
    )
    {
      // comments can be 32 characters. A base64 guid is 22 characters to which we add "Insight:" 8 characters
      var guid64 = Convert
        .ToBase64String(System.Guid.NewGuid().ToByteArray())
        .Replace("/", "_")
        .Replace("+", "-")
        .Substring(0, 22);
      string json = JsonSerializer.Serialize(newPaths.ToList());

      logDB.RecordGeneralMessage(
        mat: null,
        program: "Assign",
        result: "New Niigata Route",
        pallet: pal,
        foreignId: "faces:" + guid64,
        originalMessage: json,
        timeUTC: nowUtc
      );

      return "Insight:" + guid64;
    }
  }
}
