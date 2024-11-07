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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;

#nullable enable

namespace BlackMaple.MachineFramework;

public static class Configuration
{
  public static IConfiguration LoadFromIni(string? configFile = null)
  {
    configFile ??= Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
      "SeedTactics",
      "FMSInsight",
      "config.ini"
    );

    if (!File.Exists(configFile))
    {
      var defaultConfigFile = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath)!,
        "default-config.ini"
      );
      if (File.Exists(defaultConfigFile))
      {
        if (!Directory.Exists(Path.GetDirectoryName(configFile)))
          Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);
        File.Copy(defaultConfigFile, configFile, overwrite: false);
      }
    }

    return new ConfigurationBuilder()
      .AddIniFile(configFile, optional: true)
      .AddEnvironmentVariables()
      .Build();
  }
}

public record ServerSettings
{
  public bool EnableDebugLog { get; init; } = false;
  public int Port { get; init; } = 5000;
  public string? TLSCertFile { get; init; } = null;
  public string? OpenIDConnectAuthority { get; init; } = null;
  public string? OpenIDConnectClientId { get; init; } = null;
  public string? AuthAuthority { get; init; } = null;
  public string? AuthTokenAudiences { get; init; } = null;
  public TimeZoneInfo? ExpectedTimeZone { get; init; } = null;

  public bool UseAuthentication =>
    !string.IsNullOrEmpty(OpenIDConnectClientId)
    && !string.IsNullOrEmpty(OpenIDConnectAuthority)
    && !string.IsNullOrEmpty(AuthAuthority)
    && !string.IsNullOrEmpty(AuthTokenAudiences);

  public static ServerSettings Load(IConfiguration config)
  {
    return config.GetSection("SERVER").Get<ServerSettings>() ?? new ServerSettings();
  }
}

public record SerialSettings
{
  public long StartingMaterialID { get; init; } = 0; // if the current material id in the database is below this value, it will be set to this value
  public required Func<long, string> ConvertMaterialIDToSerial { get; init; }

  public static SerialSettings SerialsUsingBase62(IConfiguration? config = null, int? length = null)
  {
    var fmsSection = config?.GetSection("FMS");
    var len = fmsSection?.GetValue<int?>("SerialLength", null) ?? length ?? 10;
    var startingSerial = fmsSection?.GetValue<string?>("StartingSerial", null);
    var startingMatId = string.IsNullOrEmpty(startingSerial) ? 0 : ConvertFromBase62(startingSerial);

    return new SerialSettings()
    {
      StartingMaterialID = startingMatId,
      ConvertMaterialIDToSerial = (long matId) => ConvertToBase62(matId, len),
    };
  }

  private static readonly string Base62Chars =
    "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

  public static string ConvertToBase62(long num, int? len = null)
  {
    string res = "";
    long cur = num;

    while (cur > 0)
    {
      long quotient = cur / 62;
      int remainder = (int)(cur % 62);

      res = Base62Chars[remainder] + res;
      cur = quotient;
    }

    if (len.HasValue)
    {
      res = res.PadLeft(len.Value, '0');
    }

    return res;
  }

  public static long ConvertFromBase62(string msg)
  {
    if (string.IsNullOrEmpty(msg))
      return -1;
    long res = 0;
    int len = msg.Length;
    long multiplier = 1;

    for (int i = 0; i < len; i++)
    {
      char c = msg[len - i - 1];
      int idx = Base62Chars.IndexOf(c);
      if (idx < 0)
        throw new Exception("Serial " + msg + " has an invalid character " + c);
      res += idx * multiplier;
      multiplier *= 62;
    }
    return res;
  }
}

public enum AddRawMaterialType
{
  AddAsUnassigned,
  RequireExistingMaterial,
  RequireBarcodeScan,
  AddAndSpecifyJob,
}

public enum AddInProcessMaterialType
{
  RequireExistingMaterial,
  AddAndSpecifyJob,
}

public record FMSSettings
{
  public string DataDirectory { get; init; } = DefaultDataDirectory();
  public string? InstructionFilePath { get; init; }

  public bool RequireScanAtCloseout { get; init; }
  public bool RequireWorkorderBeforeAllowCloseoutComplete { get; init; }
  public bool RequireOperatorNamePromptWhenAddingMaterial { get; init; }
  public bool AllowChangeWorkorderAtLoadStation { get; init; }
  public string? AllowEditJobPlanQuantityFromQueuesPage { get; init; } = null;
  public bool AllowSwapSerialAtLoadStation { get; init; }
  public bool AllowInvalidateMaterialAtLoadStation { get; init; }
  public bool AllowInvalidateMaterialOnQueuesPage { get; init; }
  public bool UsingLabelPrinterForSerials { get; init; }

  public string? CustomStationMonitorDialogUrl { get; init; } = null;
  public AddRawMaterialType AddRawMaterial { get; init; } = AddRawMaterialType.RequireExistingMaterial;
  public AddInProcessMaterialType AddInProcessMaterial { get; init; } =
    AddInProcessMaterialType.RequireExistingMaterial;

  public string? QuarantineQueue { get; init; } = null;

  public Dictionary<string, QueueInfo> Queues { get; } = [];

  // key is queue name, value is IP address or DNS name of fms insight server with the queue
  public Dictionary<string, string> ExternalQueues { get; } = [];

  public IReadOnlyList<string> AdditionalLogServers { get; init; } = [];

  // If null, rebookings are not allowed.  Otherwise, this prefix followed by a UUID is
  // the booking ID created for rebookings.
  public string? RebookingPrefix { get; init; } = null;

  // If provided, the display name will be used in the client webpages for rebookings
  public string? RebookingsDisplayName { get; init; } = null;

  public FMSSettings() { }

  public static FMSSettings Load(IConfiguration config)
  {
    var fmsSection = config.GetSection("FMS");

    var dd = fmsSection.GetValue<string?>("DataDirectory", null);
    if (string.IsNullOrEmpty(dd))
    {
      dd = DefaultDataDirectory();
    }

    var st = new FMSSettings()
    {
      DataDirectory = dd,
      InstructionFilePath = fmsSection.GetValue<string>("InstructionFilePath"),

      RequireScanAtCloseout = fmsSection.GetValue<bool>("RequireScanAtCloseout", false),
      RequireWorkorderBeforeAllowCloseoutComplete = fmsSection.GetValue<bool>(
        "RequireWorkorderBeforeAllowCloseoutComplete",
        false
      ),
      RequireOperatorNamePromptWhenAddingMaterial = fmsSection.GetValue<bool>(
        "RequireOperatorNamePromptWhenAddingMaterial",
        false
      ),
      AllowChangeWorkorderAtLoadStation = fmsSection.GetValue<bool>(
        "AllowChangeWorkorderAtLoadStation",
        false
      ),
      AllowEditJobPlanQuantityFromQueuesPage = fmsSection.GetValue<string?>(
        "AllowEditJobPlanQuantityFromQueuesPage",
        null
      ),
      AllowSwapSerialAtLoadStation = fmsSection.GetValue<bool>("AllowSwapSerialAtLoadStation", false),
      AllowInvalidateMaterialAtLoadStation = fmsSection.GetValue<bool>(
        "AllowInvalidateMaterialAtLoadStation",
        false
      ),
      AllowInvalidateMaterialOnQueuesPage = fmsSection.GetValue<bool>(
        "AllowInvalidateMaterialOnQueuesPage",
        false
      ),

      QuarantineQueue = fmsSection.GetValue<string?>("QuarantineQueue", null),

      AdditionalLogServers = (fmsSection.GetValue("AdditionalServersForLogs", "") ?? "")
        .Split(',')
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x =>
        {
          var uri = new UriBuilder(x);
          if (uri.Scheme == "")
            uri.Scheme = "http";
          if (uri.Port == 80 && x.IndexOf(':') < 0)
            uri.Port = 5000;
          var uriS = uri.Uri.ToString();
          // remove trailing slash
          return uriS[..^1];
        })
        .ToList(),
    };

    foreach (var q in config.GetSection("QUEUE").AsEnumerable())
    {
      var key = q.Key[(q.Key.IndexOf(':') + 1)..];
      if (q.Key.Contains(':') && !string.IsNullOrEmpty(key) && int.TryParse(q.Value, out int count))
      {
        st.Queues[key] = new QueueInfo() { MaxSizeBeforeStopUnloading = count > 0 ? (int?)count : null };
      }
    }

    foreach (var q in config.GetSection("EXTERNAL_QUEUE").AsEnumerable())
    {
      var key = q.Key[(q.Key.IndexOf(':') + 1)..];
      if (q.Key.Contains(':') && !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(q.Value))
      {
        st.ExternalQueues[key] = q.Value;
      }
    }

    if (
      !string.IsNullOrEmpty(st.QuarantineQueue)
      && !st.Queues.ContainsKey(st.QuarantineQueue)
      && !st.ExternalQueues.ContainsKey(st.QuarantineQueue)
    )
    {
      Serilog.Log.Error(
        "QuarantineQueue {queue} is not configured as a queue or external queue",
        st.QuarantineQueue
      );
    }

    return st;
  }

  private static string DefaultDataDirectory()
  {
    // FMSInsight directory
    var dataDir = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
      "SeedTactics",
      "FMSInsight"
    );
    if (!Directory.Exists(dataDir))
    {
      try
      {
        Directory.CreateDirectory(dataDir);
      }
      catch (UnauthorizedAccessException)
      {
        // don't have permissions in CommonApplicationData, fall back to LocalApplicationData
        dataDir = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "SeedTactics",
          "FMSInsight"
        );
        if (!Directory.Exists(dataDir))
        {
          Directory.CreateDirectory(dataDir);
        }
      }
    }

    return dataDir;
  }
}
