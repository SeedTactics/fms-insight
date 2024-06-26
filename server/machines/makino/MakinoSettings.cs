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
using System.IO;
using Microsoft.Extensions.Configuration;

namespace BlackMaple.FMSInsight.Makino;

public record MakinoSettings
{
  public required string ADEPath { get; init; }
  public required bool DownloadOnlyOrders { get; init; }
  public required Func<IMakinoDB> OpenMakinoConnection { get; init; }

  private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MakinoSettings>();

  public static MakinoSettings Load(IConfiguration config)
  {
    var cfg = config.GetSection("Makino");

    var adePath = cfg.GetValue<string>("ADE Path") ?? "";
    if (string.IsNullOrEmpty(adePath))
    {
      adePath = @"c:\Makino\ADE";
    }
    try
    {
      foreach (var f in Directory.GetFiles(adePath, "insight*.xml"))
      {
        File.Delete(f);
      }
    }
    catch (DirectoryNotFoundException)
    {
      Log.Error("ADE Path {path} does not exist", adePath);
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Error when deleting old insight xml files");
    }

    var connStr = cfg.GetValue<string>("SQL Server Connection String");
    if (string.IsNullOrEmpty(connStr))
    {
      connStr = DetectSqlConnectionStr();
    }

    return new MakinoSettings()
    {
      ADEPath = adePath,
      DownloadOnlyOrders = cfg.GetValue<bool>("Download Only Orders"),
      OpenMakinoConnection = () => new MakinoDB(connStr)
    };
  }

  private static string DetectSqlConnectionStr()
  {
    var b = new System.Data.SqlClient.SqlConnectionStringBuilder
    {
      UserID = "sa",
      Password = "M@k1n0Admin",
      InitialCatalog = "Makino",
      DataSource = "(local)"
    };
    return b.ConnectionString;
  }
}
