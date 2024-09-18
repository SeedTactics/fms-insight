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

#nullable enable

using System;
using BlackMaple.MachineFramework;
using Microsoft.Extensions.Configuration;

namespace MazakMachineInterface
{
  public record MazakConfig
  {
    public required MazakDbType DBType { get; init; }
    public string? SQLConnectionString { get; init; }
    public string? OleDbDatabasePath { get; init; }
    public string? LogCSVPath { get; init; }
    public string? ProgramDirectory { get; init; }
    public string? LoadCSVPath { get; init; }

    public Uri? ProxyDBUri { get; init; }
    public bool UseStartingOffsetForDueDate { get; init; } = true;
    public bool WaitForAllCastings { get; init; } = false;

    // When a robot is configured in the Mazak software, it can jump ahead when searching
    // for the next part to load, which can cause parts to run out of sequence (because
    // the fixture group is used first to determine what to do next, then schedule due date,
    // then priority).  Setting all groups to zero is dangerous because it could place everything
    // simulatiously on the pallet, but at least the current version of Mazak won't place things
    // simulatiously on the pallet if the schedules have different due dates and priorities.
    // This hack of setting everything to zero is the only workaround on the current mazak version,
    // although that could change on any future versions.  Thus this must be manually configured
    // in a plugin after testing with the specific Mazak cell controller version in use.
    public bool OverrideFixtureGroupToZero { get; init; } = false;

    public Func<IRepository, CurrentStatus, CurrentStatus>? AdjustCurrentStatus { get; init; }
    public Func<ToolPocketRow, string>? ExtractToolName { get; init; }
    public ConvertJobsToMazakParts.ProcessFromJobDelegate? ProcessFromJob { get; init; }

    public static MazakConfig Load(IConfiguration configuration)
    {
      var cfg = configuration.GetSection("Mazak");
      var localDbPath = cfg.GetValue<string>("Database Path") ?? "c:\\Mazak\\NFMS\\DB";
      var dbtype = DetectMazakType(cfg, localDbPath);
      var proxyDBUrl = cfg.GetValue<string?>("Proxy DB Url");

      // database settings
      string dbConnStr;
      var sqlConnectString = cfg.GetValue<string>("SQL ConnectionString");
      if (!string.IsNullOrEmpty(sqlConnectString))
      {
        dbConnStr = sqlConnectString;
      }
      else if (dbtype == MazakDbType.MazakSmooth)
      {
        var b = new System.Data.SqlClient.SqlConnectionStringBuilder
        {
          UserID = "mazakpmc",
          Password = "Fms-978",
          DataSource = "(local)",
        };
        dbConnStr = b.ConnectionString;
      }
      else
      {
        dbConnStr = "Provider=Microsoft.ACE.OLEDB.12.0;Password=\"\";User ID=Admin;Mode=Share Deny None;";
      }

      // log csv
      var logPath = cfg.GetValue<string>("Log CSV Path") ?? "c:\\Mazak\\FMS\\Log";
      if (
        string.IsNullOrEmpty(proxyDBUrl)
        && (dbtype == MazakDbType.MazakSmooth || dbtype == MazakDbType.MazakWeb)
        && !System.IO.Directory.Exists(logPath)
      )
      {
        Serilog.Log.Error(
          "Log CSV Directory {path} does not exist.  Set the directory in the config.ini file.",
          logPath
        );
      }

      string? loadPath = null;
      if (
        string.IsNullOrEmpty(proxyDBUrl)
        && (dbtype == MazakDbType.MazakVersionE || dbtype == MazakDbType.MazakWeb)
      )
      {
        loadPath = cfg.GetValue<string>("Load CSV Path") ?? "c:\\mazak\\FMS\\LDS";
        if (!System.IO.Directory.Exists(loadPath))
        {
          Serilog.Log.Error(
            "Load CSV Directory {path} does not exist.  Set the directory in the config.ini file.",
            loadPath
          );
        }
      }

      Uri? proxyUri = null;
      if (!string.IsNullOrEmpty(proxyDBUrl))
      {
        var b = new UriBuilder(proxyDBUrl);
        if (b.Scheme != "http" && b.Scheme != "https")
        {
          b.Scheme = "http";
        }
        if (b.Port == 0)
        {
          b.Port = 5200;
        }
        proxyUri = b.Uri;
      }

      return new MazakConfig
      {
        DBType = dbtype,
        SQLConnectionString = dbConnStr,
        OleDbDatabasePath = localDbPath,
        ProxyDBUri = proxyUri,
        LogCSVPath = logPath,
        LoadCSVPath = loadPath,
        ProgramDirectory = cfg.GetValue<string?>("Program Directory") ?? "C:\\NCProgs",
        UseStartingOffsetForDueDate = Convert.ToBoolean(
          cfg.GetValue("Use Starting Offset For Due Date", "true")
        ),
        WaitForAllCastings = cfg.GetValue<bool>("Wait For All Castings", false),
      };
    }

    private static MazakDbType DetectMazakType(IConfigurationSection cfg, string localDbPath)
    {
      var verE = cfg.GetValue<bool>("VersionE");
      var webver = cfg.GetValue<bool>("Web Version");
      var smoothVer = cfg.GetValue<bool>("Smooth Version");

      string testPath = System.IO.Path.Combine(localDbPath, "FCREADDAT01.mdb");

      if (verE || webver)
      {
        if (!System.IO.File.Exists(testPath))
        {
          Serilog.Log.Error(
            "Mazak Version E or Web Version selected, but database file {path} does not exist.",
            testPath
          );
        }
      }

      if (verE)
        return MazakDbType.MazakVersionE;
      else if (webver)
        return MazakDbType.MazakWeb;
      else if (smoothVer)
        return MazakDbType.MazakSmooth;

      if (System.IO.File.Exists(testPath))
      {
        //TODO: open database to check column existance for web vs E.
        Serilog.Log.Information(
          "Assuming Mazak WEB version.  If this is incorrect it can be changed in the settings."
        );
        return MazakDbType.MazakWeb;
      }
      else
      {
        Serilog.Log.Information(
          "Assuming Mazak Smooth version.  If this is incorrect it can be changed in the settings."
        );
        return MazakDbType.MazakSmooth;
      }
    }
  }
}
