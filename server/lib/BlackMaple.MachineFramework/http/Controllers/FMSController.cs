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
using System.IO;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

#nullable enable

namespace BlackMaple.MachineFramework.Controllers
{
  [KnownType(typeof(ServerEvent))]
  public record FMSInfo
  {
    public required string Name { get; init; }

    public required string Version { get; init; }

    public DateTime? LicenseExpires { get; init; }

    public IReadOnlyList<string>? AdditionalLogServers { get; init; }

    public string? OpenIDConnectAuthority { get; init; }

    public string? LocalhostOpenIDConnectAuthority { get; init; }

    public string? OpenIDConnectClientId { get; init; }

    public bool? UsingLabelPrinterForSerials { get; init; }

    public bool? UseClientPrinterForLabels { get; init; }

    public bool? AllowQuarantineToCancelLoad { get; init; }

    public string? QuarantineQueue { get; init; }

    public string? CustomStationMonitorDialogUrl { get; init; }

    public string? SupportsRebookings { get; init; }

    // LoadStation Options
    public bool? AllowChangeWorkorderAtLoadStation { get; init; }

    public bool? AllowSwapSerialAtLoadStation { get; init; }

    public bool? AllowInvalidateMaterialAtLoadStation { get; init; }

    // Closeout Page Options
    public bool? RequireScanAtCloseout { get; init; }

    public bool? RequireWorkorderBeforeAllowCloseoutComplete { get; init; }

    // Queues Page Options
    public AddRawMaterialType? AddRawMaterial { get; init; }

    public AddInProcessMaterialType? AddInProcessMaterial { get; init; }

    public bool? RequireOperatorNamePromptWhenAddingMaterial { get; init; }

    public string? AllowEditJobPlanQuantityFromQueuesPage { get; init; }

    public bool? AllowInvalidateMaterialOnQueuesPage { get; init; } = false;
  }

  [ApiController]
  [Route("api/v1/fms")]
  public class FmsController : ControllerBase
  {
    private readonly FMSSettings _cfg;
    private readonly ServerSettings _serverSt;
    private readonly RepositoryConfig _repo;
    private readonly IPrintLabelForMaterial? _printLabel;
    private readonly IJobAndQueueControl _jobsAndQueues;
    private readonly ICalculateInstructionPath? _instrPath;
    private readonly IParseBarcode? _parseBarcode;
    private readonly ICheckLicense? _checkLicense;

    private readonly string _name;
    private readonly string _version;

    public FmsController(
      FMSSettings fmsSt,
      ServerSettings serverSt,
      IJobAndQueueControl jobsAndQueues,
      RepositoryConfig repo,
      IPrintLabelForMaterial? printLabel = null,
      ICalculateInstructionPath? instrPath = null,
      IParseBarcode? parseBarcode = null,
      ICheckLicense? license = null
    )
    {
      _cfg = fmsSt;
      _serverSt = serverSt;
      _printLabel = printLabel;
      _jobsAndQueues = jobsAndQueues;
      _repo = repo;
      _instrPath = instrPath;
      _parseBarcode = parseBarcode;
      _checkLicense = license;

      var startA = System.Reflection.Assembly.GetEntryAssembly();
      if (startA == null)
      {
        _name = "FMS Insight";
        _version = "";
      }
      else
      {
        _name =
          (
            (System.Reflection.AssemblyTitleAttribute?)
              Attribute.GetCustomAttribute(startA, typeof(System.Reflection.AssemblyTitleAttribute), false)
          )?.Title
          ?? startA.GetName().Name
          ?? "FMS Insight";
        _version = startA.GetName().Version?.ToString() ?? "";
      }
    }

    [HttpGet("fms-information")]
    [AllowAnonymous]
    public FMSInfo FMSInformation()
    {
      return new FMSInfo()
      {
        Name = _name,
        Version = _version,
        RequireScanAtCloseout = _cfg.RequireScanAtCloseout,
        RequireWorkorderBeforeAllowCloseoutComplete = _cfg.RequireWorkorderBeforeAllowCloseoutComplete,
        AdditionalLogServers = _cfg.AdditionalLogServers,
        OpenIDConnectAuthority = _serverSt.OpenIDConnectAuthority,
        OpenIDConnectClientId = _serverSt.OpenIDConnectClientId,
        LocalhostOpenIDConnectAuthority = _serverSt.AuthAuthority,
        UsingLabelPrinterForSerials = _cfg.UsingLabelPrinterForSerials,
        UseClientPrinterForLabels = _printLabel == null,
        QuarantineQueue = _cfg.QuarantineQueue,
        RequireOperatorNamePromptWhenAddingMaterial = _cfg.RequireOperatorNamePromptWhenAddingMaterial,
        AddRawMaterial = _cfg.AddRawMaterial,
        AddInProcessMaterial = _cfg.AddInProcessMaterial,
        AllowEditJobPlanQuantityFromQueuesPage = _cfg.AllowEditJobPlanQuantityFromQueuesPage,
        AllowQuarantineToCancelLoad = _jobsAndQueues.AllowQuarantineToCancelLoad,
        SupportsRebookings =
          _cfg.RebookingPrefix != null ? (_cfg.RebookingsDisplayName ?? "Rebookings") : null,
        AllowChangeWorkorderAtLoadStation = _cfg.AllowChangeWorkorderAtLoadStation,
        AllowInvalidateMaterialAtLoadStation = _cfg.AllowInvalidateMaterialAtLoadStation,
        AllowInvalidateMaterialOnQueuesPage = _cfg.AllowInvalidateMaterialOnQueuesPage,
        AllowSwapSerialAtLoadStation = _cfg.AllowSwapSerialAtLoadStation,
        LicenseExpires = _checkLicense?.LicenseExpires(),
        CustomStationMonitorDialogUrl = _cfg.CustomStationMonitorDialogUrl,
      };
    }

    private string? SearchFiles(string part, string type)
    {
      if (string.IsNullOrEmpty(_cfg.InstructionFilePath))
        return null;
      foreach (var f in Directory.GetFiles(_cfg.InstructionFilePath))
      {
        if (!Path.GetFileName(f).Contains(part))
          continue;
        if (
          !string.IsNullOrEmpty(type)
          && !Path.GetFileName(f).Contains(type, StringComparison.OrdinalIgnoreCase)
        )
          continue;
        return Path.GetFileName(f);
      }
      return null;
    }

    [HttpGet("find-instructions/{part}")]
    [ProducesResponseType(302)]
    [ProducesResponseType(404)]
    public IActionResult FindInstructions(
      string part,
      [BindRequired, FromQuery] string type,
      [FromQuery] int? process = null,
      [FromQuery] long? materialID = null,
      [FromQuery] string? operatorName = null,
      [FromQuery] int pallet = 0
    )
    {
      try
      {
        if (_instrPath != null)
        {
          var path = _instrPath.InstructionPath(part, process, type, materialID, operatorName, pallet);
          if (string.IsNullOrEmpty(path))
          {
            return NotFound(
              "Error: could not find an instruction for "
                + (string.IsNullOrEmpty(type) ? part : part + " and " + type)
                + " in the directory "
                + _cfg.InstructionFilePath
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

      string? instrFile = null;

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
          "Error: could not find a file with "
            + (string.IsNullOrEmpty(type) ? part : part + " and " + type)
            + " in the filename inside "
            + _cfg.InstructionFilePath
        );
      }
      else
      {
        return Redirect("/instructions/" + System.Uri.EscapeDataString(instrFile));
      }
    }

    [HttpPost("print-label/{materialId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public IActionResult PrintLabel(long materialId, [FromQuery] int process = 1)
    {
      if (_printLabel != null)
      {
        _printLabel.PrintLabel(materialId, process, Request.GetTypedHeaders().Referer ?? new Uri("/"));
        return Ok();
      }
      else
      {
        return BadRequest("FMS configuration does not support printing labels");
      }
    }

    [HttpPost("parse-barcode")]
    public ScannedMaterial? ParseBarcode([BindRequired, FromQuery] string barcode)
    {
      if (_parseBarcode != null)
      {
        return _parseBarcode.ParseBarcode(
          barcode: barcode,
          httpReferer: Request.GetTypedHeaders().Referer ?? new Uri("/")
        );
      }
      else
      {
        var idxComma = barcode.IndexOf(',');
        var serial = idxComma > 0 ? barcode[..idxComma] : barcode;
        using var conn = _repo.OpenConnection();
        var mats = conn.GetMaterialDetailsForSerial(serial);
        if (mats.Count > 0)
        {
          return new ScannedMaterial() { ExistingMaterial = mats[mats.Count - 1] };
        }
        else
        {
          return new ScannedMaterial() { Casting = new ScannedCasting() { Serial = serial } };
        }
      }
    }

    [HttpPost("enable-verbose-logging-for-five-minutes")]
    public void EnableVerboseLoggingForFiveMinutes()
    {
      InsightLogging.EnableVerboseLoggingFor(TimeSpan.FromMinutes(5));
    }
  }
}
