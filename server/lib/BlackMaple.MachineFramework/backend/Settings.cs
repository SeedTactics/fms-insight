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
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace BlackMaple.MachineFramework
{
  public class ServerSettings
  {
#if SERVICE_AVAIL

      public static string ConfigDirectory {get;} =
        Path.Combine(
          System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
          "SeedTactics",
          "FMSInsight"
        );

      public static string ContentRootDirectory {get;} =
        Path.GetDirectoryName(
            System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName
        );

#else

    public static string ConfigDirectory { get; } =
      Directory.GetCurrentDirectory();

    public static string ContentRootDirectory { get; } =
      Directory.GetCurrentDirectory();

#endif

    public bool EnableDebugLog { get; set; } = false;
    public int Port { get; set; } = 5000;
    public string TLSCertFile { get; set; } = null;
    public bool EnableSailAPI { get; set; } = false;
    public string OpenIDConnectAuthority { get; set; } = null;
    public string OpenIDConnectClientId { get; set; } = null;
    public string AuthAuthority { get; set; } = null;
    public string AuthTokenAudiences { get; set; } = null;

    public bool UseAuthentication =>
         !string.IsNullOrEmpty(OpenIDConnectClientId)
      && !string.IsNullOrEmpty(OpenIDConnectAuthority)
      && !string.IsNullOrEmpty(AuthAuthority)
      && !string.IsNullOrEmpty(AuthTokenAudiences);

    public static ServerSettings Load(IConfiguration config)
    {
      var s = config.GetSection("SERVER").Get<ServerSettings>();
      if (s == null)
        s = new ServerSettings();

      return s;
    }
  }

  public enum SerialType
  {
    NoAutomaticSerials,
    AssignOneSerialPerMaterial,  // assign a different serial to each piece of material
    AssignOneSerialPerCycle,     // assign a single serial to all the material on each cycle
  }

  public class FMSSettings
  {
    public string DataDirectory { get; set; } = null;
    public SerialType SerialType { get; set; } = SerialType.NoAutomaticSerials;
    public int SerialLength { get; set; } = 9;
    public string StartingSerial { get; set; } = null;
    public Func<string, long> ConvertSerialToMaterialID { get; set; } = ConvertFromBase62;
    public Func<long, string> ConvertMaterialIDToSerial { get; set; } = ConvertToBase62;
    public string InstructionFilePath { get; set; }
    public bool RequireScanAtWash { get; set; }
    public bool RequireWorkorderBeforeAllowWashComplete { get; set; }
    public string QuarantineQueue { get; set; }
    public bool RequireOperatorNamePromptWhenAddingMaterial { get; set; }
    public bool AllowAddRawMaterialForNonRunningJobs { get; set; }
    public bool RequireSerialWhenAddingMaterialToQueue { get; set; }

    public Dictionary<string, MachineWatchInterface.QueueSize> Queues { get; }
      = new Dictionary<string, MachineWatchInterface.QueueSize>();

    // key is queue name, value is IP address or DNS name of fms insight server with the queue
    public Dictionary<string, string> ExternalQueues { get; }
      = new Dictionary<string, string>();

    public IReadOnlyList<string> AdditionalLogServers { get; set; }

    public FMSSettings() { }
    public FMSSettings(IConfiguration config)
    {
      var fmsSection = config.GetSection("FMS");

      DataDirectory = fmsSection.GetValue<string>("DataDirectory", null);
      if (string.IsNullOrEmpty(DataDirectory))
      {
        DataDirectory = DefaultDataDirectory();
      }

      if (fmsSection.GetValue<bool>("AutomaticSerials", false))
      {
        SerialType = SerialType.AssignOneSerialPerMaterial;
      }
      SerialLength = fmsSection.GetValue<int>("SerialLength", 10);
      StartingSerial = fmsSection.GetValue<string>("StartingSerial", null);
      RequireScanAtWash = fmsSection.GetValue<bool>("RequireScanAtWash", false);
      RequireWorkorderBeforeAllowWashComplete = fmsSection.GetValue<bool>("RequireWorkorderBeforeAllowWashComplete", false);
      RequireOperatorNamePromptWhenAddingMaterial = fmsSection.GetValue<bool>("RequireOperatorNamePromptWhenAddingMaterial", false);
      RequireSerialWhenAddingMaterialToQueue = fmsSection.GetValue<bool>("RequireSerialWhenAddingMaterialToQueue", false);
      AllowAddRawMaterialForNonRunningJobs = fmsSection.GetValue<bool>("AllowAddRawMaterialForNonRunningJobs", true);
      QuarantineQueue = fmsSection.GetValue<string>("QuarantineQueue", null);
      AdditionalLogServers =
        fmsSection.GetValue<string>("AdditionalServersForLogs", "")
        .Split(',')
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x =>
        {
          var uri = new UriBuilder(x);
          if (uri.Scheme == "") uri.Scheme = "http";
          if (uri.Port == 80 && x.IndexOf(':') < 0) uri.Port = 5000;
          var uriS = uri.Uri.ToString();
          // remove trailing slash
          return uriS.Substring(0, uriS.Length - 1);
        })
        .ToList();

      InstructionFilePath = fmsSection.GetValue<string>("InstructionFilePath");

      foreach (var q in config.GetSection("QUEUE").AsEnumerable())
      {
        var key = q.Key.Substring(q.Key.IndexOf(':') + 1);
        if (q.Key.IndexOf(':') >= 0 && !string.IsNullOrEmpty(key) && int.TryParse(q.Value, out int count))
        {
          Queues[key] = new MachineWatchInterface.QueueSize()
          {
            MaxSizeBeforeStopUnloading = count > 0 ? (int?)count : null
          };
        }
      }

      foreach (var q in config.GetSection("EXTERNAL_QUEUE").AsEnumerable())
      {
        var key = q.Key.Substring(q.Key.IndexOf(':') + 1);
        if (q.Key.IndexOf(':') >= 0 && !string.IsNullOrEmpty(key))
        {
          ExternalQueues[key] = q.Value;
        }
      }

      if (!string.IsNullOrEmpty(QuarantineQueue) && !Queues.ContainsKey(QuarantineQueue) && !ExternalQueues.ContainsKey(QuarantineQueue))
      {
        Serilog.Log.Error("QuarantineQueue {queue} is not configured as a queue or external queue", QuarantineQueue);
      }
    }

    private static string DefaultDataDirectory()
    {
      // FMSInsight directory
      var dataDir = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
        "SeedTactics", "FMSInsight");
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
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
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
    private static string Base62Chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string ConvertToBase62(long num)
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

      return res;
    }

    public static long ConvertFromBase62(string msg)
    {
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
}