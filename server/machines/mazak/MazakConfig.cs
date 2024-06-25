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
    public required string SQLConnectionString { get; init; }
    public required string LogCSVPath { get; init; }
    public required string ProgramDirectory { get; init; }
    public string? LoadCSVPath { get; init; }
    public bool UseStartingOffsetForDueDate { get; init; } = true;
    public bool WaitForAllCastings { get; init; } = false;

    public Func<IRepository, CurrentStatus, CurrentStatus>? AdjustCurrentStatus { get; init; }
    public Func<ToolPocketRow, string>? ExtractToolName { get; init; }

    public static MazakConfig Load(IConfiguration configuration)
    {
      var cfg = configuration.GetSection("Mazak");
      var localDbPath = cfg.GetValue<string>("Database Path");
      var sqlConnectString = cfg.GetValue<string>("SQL ConnectionString");
      var dbtype = DetectMazakType(cfg, localDbPath);

      // database settings
      string dbConnStr;
      if (dbtype == MazakDbType.MazakSmooth)
      {
        if (!string.IsNullOrEmpty(sqlConnectString))
        {
          dbConnStr = sqlConnectString;
        }
        else if (!string.IsNullOrEmpty(localDbPath))
        {
          // old installers put sql server computer name in localDbPath
          dbConnStr = "Server=" + localDbPath + "\\pmcsqlserver;" + "User ID=mazakpmc;Password=Fms-978";
        }
        else
        {
          var b = new System.Data.SqlClient.SqlConnectionStringBuilder
          {
            UserID = "mazakpmc",
            Password = "Fms-978",
            DataSource = "(local)"
          };
          dbConnStr = b.ConnectionString;
        }
      }
      else
      {
        dbConnStr = localDbPath ?? "c:\\Mazak\\NFMS\\DB";
      }

      // log csv
      var logPath = cfg.GetValue<string>("Log CSV Path");
      if (logPath == null || logPath == "")
        logPath = "c:\\Mazak\\FMS\\Log";

      if (dbtype != MazakDbType.MazakVersionE && !System.IO.Directory.Exists(logPath))
      {
        Serilog.Log.Error(
          "Log CSV Directory {path} does not exist.  Set the directory in the config.ini file.",
          logPath
        );
      }

      string? loadPath = null;
      if (dbtype == MazakDbType.MazakVersionE || dbtype == MazakDbType.MazakWeb)
      {
        loadPath = cfg.GetValue<string>("Load CSV Path");
        if (string.IsNullOrEmpty(loadPath))
        {
          loadPath = "c:\\mazak\\FMS\\LDS\\";
        }

        if (!System.IO.Directory.Exists(loadPath))
        {
          loadPath = "c:\\mazak\\LDS\\";

          if (!System.IO.Directory.Exists(loadPath))
          {
            Serilog.Log.Error(
              "Unable to determine the path to the mazak load CSV files.  Please add/update a setting"
                + " called 'Load CSV Path' in config file"
            );
          }
        }
      }

      return new MazakConfig
      {
        DBType = dbtype,
        SQLConnectionString = dbConnStr,
        LogCSVPath = logPath,
        LoadCSVPath = loadPath,
        ProgramDirectory = cfg.GetValue<string?>("Program Directory") ?? "C:\\NCProgs",
        UseStartingOffsetForDueDate = Convert.ToBoolean(
          cfg.GetValue("Use Starting Offset For Due Date", "true")
        ),
        WaitForAllCastings = cfg.GetValue<bool>("Wait For All Castings", false)
      };
    }

    private static MazakDbType DetectMazakType(IConfigurationSection cfg, string? localDbPath)
    {
      var verE = cfg.GetValue<bool>("VersionE");
      var webver = cfg.GetValue<bool>("Web Version");
      var smoothVer = cfg.GetValue<bool>("Smooth Version");

      if (verE)
        return MazakDbType.MazakVersionE;
      else if (webver)
        return MazakDbType.MazakWeb;
      else if (smoothVer)
        return MazakDbType.MazakSmooth;

      string testPath;
      if (string.IsNullOrEmpty(localDbPath))
      {
        testPath = "C:\\Mazak\\NFMS\\DB\\FCREADDAT01.mdb";
      }
      else
      {
        testPath = System.IO.Path.Combine(localDbPath, "FCREADDAT01.mdb");
      }

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
