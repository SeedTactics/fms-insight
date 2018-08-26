/* Copyright (c) 2018, John Lenz

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
using System.IO;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using BlackMaple.MachineWatchInterface;
using System.Runtime.Serialization;

namespace BlackMaple.MachineFramework.Controllers
{
  [DataContract]
  public class FMSInfo
  {
    [DataMember] public string Name { get; set; }
    [DataMember] public string Version { get; set; }
    [DataMember] public bool RequireScanAtWash { get; set; }
    [DataMember] public bool RequireWorkorderBeforeAllowWashComplete { get; set; }
    [DataMember] public IReadOnlyList<string> AdditionalLogServers { get; set; }
  }

  [Route("api/v1/[controller]")]
  public class serverController : ControllerBase
  {
    private IStoreSettings _settings;
    private IFMSImplementation _fmsImpl;

    public serverController(IFMSImplementation impl, IStoreSettings s)
    {
      _settings = s;
      _fmsImpl = impl;
    }

    [HttpGet("fms-information")]
    public FMSInfo FMSInformation()
    {
      var info = _fmsImpl.NameAndVersion;
      return new FMSInfo()
      {
        Name = info.Name,
        Version = info.Version,
        RequireScanAtWash = Program.FMSSettings.RequireScanAtWash,
        RequireWorkorderBeforeAllowWashComplete = Program.FMSSettings.RequireWorkorderBeforeAllowWashComplete,
        AdditionalLogServers = Program.FMSSettings.AdditionalLogServers
      };
    }

    [HttpGet("settings/{id}")]
    public string GetSettings(string id)
    {
      return _settings.GetSettings(id);
    }

    [HttpPut("settings/{id}")]
    public void SetSetting(string id, [FromBody] string setting)
    {
      _settings.SetSettings(id, setting);
    }

    public static string SearchFiles(string part, string type)
    {
      foreach (var f in Directory.GetFiles(Program.FMSSettings.InstructionFilePath))
      {
        if (!Path.GetFileName(f).Contains(part)) continue;
        if (!string.IsNullOrEmpty(type) && !Path.GetFileName(f).ToLower().Contains(type.ToLower())) continue;
        return Path.GetFileName(f);
      }
      return null;
    }

    [HttpGet("find-instructions/{part}")]
    [ProducesResponseType(302)]
    [ProducesResponseType(404)]
    public IActionResult FindInstructions(string part, [FromQuery] string type, [FromQuery] int? process = null, [FromQuery] long? materialID = null)
    {
      try
      {
        var path = _fmsImpl.CustomizeInstructionPath(part, process, type, materialID);
        if (string.IsNullOrEmpty(path))
        {
          return NotFound(
              "Error: could not find an instruction for " +
              (string.IsNullOrEmpty(type) ? part : part + " and " + type) +
              " in the directory " +
              Program.FMSSettings.InstructionFilePath
          );
        }
        return Redirect(path);
      }
      catch (NotImplementedException)
      {
        // do nothing, continue with default impl
      }

      if (string.IsNullOrEmpty(Program.FMSSettings.InstructionFilePath))
      {
        return NotFound("Error: instruction directory must be configured in FMS Insight config file.");
      }
      if (!Directory.Exists(Program.FMSSettings.InstructionFilePath))
      {
        return NotFound("Error: configured instruction directory does not exist");
      }

      // try part with process
      var instrFile = SearchFiles(part + "-" + process.ToString(), type);

      // try without process
      if (string.IsNullOrEmpty(instrFile))
      {
        instrFile = SearchFiles(part, type);
      }

      // try unload with process fallback to load
      if (string.IsNullOrEmpty(instrFile) && type == "unload")
      {
        instrFile = SearchFiles(part + "-" + process.ToString(), "load");
      }

      // try unload without process fallback to load
      if (string.IsNullOrEmpty(instrFile) && type == "unload")
      {
        instrFile = SearchFiles(part, "load");
      }

      if (string.IsNullOrEmpty(instrFile))
      {
        return NotFound(
            "Error: could not find a file with " +
            (string.IsNullOrEmpty(type) ? part : part + " and " + type) +
            " in the filename inside " +
            Program.FMSSettings.InstructionFilePath
        );
      }
      else
      {
        return Redirect("/instructions/" + System.Uri.EscapeDataString(instrFile));
      }
    }
  }
}