/* Copyright (c) 2019, John Lenz

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
using System.Threading.Tasks;
using System.IO;
using BlackMaple.MachineWatchInterface;

namespace Cincron
{

  public class CellCommands
  {
    public async Task ImportWorkLoad(JobPlan job)
    {
      var wlId = "INSGT-" + job.PartName.ToUpper() + "-" + DateTime.Now.ToString("yyMMddHHmm");
      var file = AllocateFilename("work", "wkl");

      var st = job.GetSimulatedStartingTimeUTC(1, 1).ToLocalTime();
      var end = job.RouteEndingTimeUTC.ToLocalTime();


      await WriteFile(file, string.Join("\n", new[] {
        wlId,
        st.ToString(DateFormat),
        end.ToString(DateFormat),
        job.Priority.ToString(),
        "R-" + job.PartName,
        job.UniqueStr,
        "FMS-INSIGHT",
        "/",
        job.PartName,
        "0",
        job.GetPlannedCyclesOnFirstProcess(1).ToString(),
        "/"
      }));
      await _plantHost.Send("imwrkl -w " + wlId + " -s -c -W " + file.CellPath);
    }

    public async Task<byte[]> StationReport(DateTime startUTC, DateTime endUTC)
    {
      var file = AllocateFilename("stat", "rpt");
      await _plantHost.Send(
        "rpstat -R " + file.CellPath + " -s UTILIZATION " +
        "-a \"" + startUTC.ToLocalTime().ToString(DateFormat) + "\" " +
        "-e \"" + endUTC.ToLocalTime().ToString(DateFormat) + "\""
      );
      return await File.ReadAllBytesAsync(file.NetworkPath);
    }

    public async Task<byte[]> RouteReport()
    {
      var file = AllocateFilename("routes", "rpt");
      await _plantHost.Send("rpsrte -R " + file.CellPath + " -s DETAIL");
      return await File.ReadAllBytesAsync(file.NetworkPath);

    }

    public async Task<byte[]> WorkLoadReport()
    {
      var file = AllocateFilename("work", "rpt");
      await _plantHost.Send("rpwrkl -R " + file.CellPath);
      return await File.ReadAllBytesAsync(file.NetworkPath);
    }


    private IPlantHostCommunication _plantHost;
    private string _networkSharePath;
    private string _localCellPath;

    public CellCommands(IPlantHostCommunication p, string networkSharePath, string localCellControllerPath)
    {
      _plantHost = p;
      _networkSharePath = networkSharePath;
      _localCellPath = localCellControllerPath;
      if (_localCellPath[_localCellPath.Length - 1] != '/')
      {
        _localCellPath += "/";
      }
    }

    private struct CellFile
    {
      public string CellPath;
      public string NetworkPath;
    }

    private CellFile AllocateFilename(string prefix, string extension)
    {
      var file = prefix + System.Guid.NewGuid().ToString() + "." + extension;
      return new CellFile()
      {
        CellPath = _localCellPath + file,
        NetworkPath = Path.Combine(_networkSharePath, file)
      };
    }

    private async Task WriteFile(CellFile file, string ct)
    {
      using (var f = File.OpenWrite(file.NetworkPath))
      using (var w = new StreamWriter(f))
      {
        w.Write(ct);
        await f.FlushAsync();
      }
    }

    private const string DateFormat = "ddd MMM dd yyyy HH:mm:ss";
  }
}