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
using Microsoft.AspNetCore.Authorization;
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
    [DataMember] public string OpenIDConnectAuthority { get; set; }
    [DataMember] public string LocalhostOpenIDConnectAuthority { get; set; }
    [DataMember] public string OpenIDConnectClientId { get; set; }
    [DataMember] public bool UsingLabelPrinterForSerials { get; set; }
    [DataMember] public bool? UseClientPrinterForLabels { get; set; }
    [DataMember] public string QuarantineQueue { get; set; }
    [DataMember] public bool? RequireOperatorNamePromptWhenAddingMaterial { get; set; }
    [DataMember] public bool? AllowAddRawMaterialForNonRunningJobs { get; set; }
    [DataMember] public bool? RequireSerialWhenAddingMaterialToQueue { get; set; }
    [DataMember] public bool? AllowQuarantineAtLoadStation { get; set; }
    [DataMember] public bool? AllowChangeSerial { get; set; }
    [DataMember] public bool? AllowChangeWorkorderAtLoadStation { get; set; }

    [DataMember(IsRequired = false, EmitDefaultValue = false)]
    public string AllowEditJobPlanQuantityFromQueuesPage { get; set; }
  }

  [ApiController]
  [Route("api/v1/fms")]
  public class fmsController : ControllerBase
  {
    private FMSImplementation _impl;
    private FMSSettings _cfg;
    private ServerSettings _serverSt;

    public fmsController(FMSSettings fmsSt, ServerSettings serverSt, FMSImplementation impl)
    {
      _cfg = fmsSt;
      _serverSt = serverSt;
      _impl = impl;
    }

    [HttpGet("fms-information")]
    public FMSInfo FMSInformation()
    {
      return new FMSInfo()
      {
        Name = _impl.Name,
        Version = _impl.Version,
        RequireScanAtWash = _cfg.RequireScanAtWash,
        RequireWorkorderBeforeAllowWashComplete = _cfg.RequireWorkorderBeforeAllowWashComplete,
        AdditionalLogServers = _cfg.AdditionalLogServers,
        OpenIDConnectAuthority = _serverSt.OpenIDConnectAuthority,
        OpenIDConnectClientId = _serverSt.OpenIDConnectClientId,
        LocalhostOpenIDConnectAuthority = _serverSt.AuthAuthority,
        UsingLabelPrinterForSerials = _impl.UsingLabelPrinterForSerials,
        UseClientPrinterForLabels = _impl.PrintLabel == null,
        QuarantineQueue = _cfg.QuarantineQueue,
        RequireOperatorNamePromptWhenAddingMaterial = _cfg.RequireOperatorNamePromptWhenAddingMaterial,
        RequireSerialWhenAddingMaterialToQueue = _cfg.RequireSerialWhenAddingMaterialToQueue,
        AllowAddRawMaterialForNonRunningJobs = _cfg.AllowAddRawMaterialForNonRunningJobs,
        AllowEditJobPlanQuantityFromQueuesPage = _impl.AllowEditJobPlanQuantityFromQueuesPage,
        AllowQuarantineAtLoadStation = _impl.Backend.SupportsQuarantineAtLoadStation,
        AllowChangeSerial = _cfg.AllowChangeSerial,
        AllowChangeWorkorderAtLoadStation = _cfg.AllowChangeWorkorderAtLoadStation
      };
    }

    [HttpGet("settings/{id}"), Authorize]
    public string GetSettings(string id)
    {
      var f = System.IO.Path.Combine(
          _cfg.DataDirectory,
          System.IO.Path.GetFileNameWithoutExtension(id))
          + ".json";
      if (System.IO.File.Exists(f))
        return System.IO.File.ReadAllText(f);
      else
        return null;
    }

    [HttpPut("settings/{id}"), Authorize]
    [ProducesResponseType(typeof(void), 200)]
    public void SetSetting(string id, [FromBody] string setting)
    {
      var f = System.IO.Path.Combine(
          _cfg.DataDirectory,
          System.IO.Path.GetFileNameWithoutExtension(id))
          + ".json";
      System.IO.File.WriteAllText(f, setting);
    }

    private string SearchFiles(string part, string type)
    {
      foreach (var f in Directory.GetFiles(_cfg.InstructionFilePath))
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
    [Authorize]
    public IActionResult FindInstructions(string part,
                                         [FromQuery] string type,
                                         [FromQuery] int? process = null,
                                         [FromQuery] long? materialID = null,
                                         [FromQuery] string operatorName = null,
                                         [FromQuery] string pallet = null
                                         )
    {
      try
      {
        if (_impl != null && _impl.InstructionPath != null)
        {
          var path = _impl.InstructionPath(part, process, type, materialID, operatorName, pallet);
          if (string.IsNullOrEmpty(path))
          {
            return NotFound(
                "Error: could not find an instruction for " +
                (string.IsNullOrEmpty(type) ? part : part + " and " + type) +
                " in the directory " +
                _cfg.InstructionFilePath
            );
          }
          return Redirect(path);
        }
      }
      catch (NotImplementedException)
      {
        // do nothing, continue with default impl
      }

      if (string.IsNullOrEmpty(_cfg.InstructionFilePath))
      {
        return NotFound("Error: instruction directory must be configured in FMS Insight config file.");
      }
      if (!Directory.Exists(_cfg.InstructionFilePath))
      {
        return NotFound("Error: configured instruction directory does not exist");
      }

      string instrFile = null;

      // try part with process
      if (process.HasValue)
      {
        instrFile = SearchFiles(part + "-" + process.Value.ToString(), type);
      }

      // try without process
      if (string.IsNullOrEmpty(instrFile))
      {
        instrFile = SearchFiles(part, type);
      }

      // try unload with process fallback to load
      if (process.HasValue && string.IsNullOrEmpty(instrFile) && type == "unload")
      {
        instrFile = SearchFiles(part + "-" + process.Value.ToString(), "load");
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
            _cfg.InstructionFilePath
        );
      }
      else
      {
        return Redirect("/instructions/" + System.Uri.EscapeDataString(instrFile));
      }
    }

    [HttpPost("print-label/{materialId}"), Authorize]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public IActionResult PrintLabel(long materialId, [FromQuery] int process = 1, [FromQuery] int? loadStation = null, [FromQuery] string queue = null)
    {
      if (_impl != null && _impl.PrintLabel != null)
      {
        _impl.PrintLabel(materialId, process, loadStation, queue);
        return Ok();
      }
      else
      {
        return BadRequest("FMS configuration does not support printing labels");
      }
    }
  }


  // the old controller before I renamed server to fms
  [Route("api/v1/server"), NSwag.Annotations.SwaggerIgnore]
  public class serverController : fmsController
  {
    public serverController(FMSSettings fmsSt, ServerSettings serverSt, FMSImplementation impl)
      : base(fmsSt, serverSt, impl)
    {
    }

  }
}