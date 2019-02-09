/* Copyright (c) 2017, John Lenz

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

namespace BlackMaple.MachineWatchInterface
{
  public delegate void NewLogEntryDelegate(LogEntry e, string foreignId);

  public interface ILogDatabase
  {
    List<LogEntry> GetLogEntries(DateTime startUTC, DateTime endUTC);
    List<LogEntry> GetLog(long lastSeenCounter);
    List<LogEntry> GetLogForMaterial(long materialID);
    List<LogEntry> GetLogForSerial(string serial);
    List<LogEntry> GetLogForWorkorder(string workorder);
    List<LogEntry> GetCompletedPartLogs(DateTime startUTC, DateTime endUTC);
    List<WorkorderSummary> GetWorkorderSummaries(IEnumerable<string> workorderIds);
    MaterialDetails GetMaterialDetails(long materialID);

    LogEntry RecordSerialForMaterialID(long materialID, int process, string serial);
    LogEntry RecordWorkorderForMaterialID(long materialID, int process, string workorder);
    LogEntry RecordFinalizedWorkorder(string workorder);
    LogEntry ForceInspection(long materialID, int process, string inspType, bool inspect);

    LogEntry RecordInspectionCompleted(
      long materialID,
      int process,
      int inspectionLocNum,
      string inspectionType,
      bool success,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active);
    LogEntry RecordWashCompleted(
      long materialID,
      int process,
      int washLocNum,
      IDictionary<string, string> extraData,
      TimeSpan elapsed,
      TimeSpan active);

    event NewLogEntryDelegate NewLogEntry;
  }
}

