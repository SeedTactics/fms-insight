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

namespace MazakMachineInterface;

public class MazakConfig
{
  public MazakDbType DBType { get; init; }
  public string SQLConnectionString { get; init; }
  public string OleDbDatabasePath { get; init; }
  public string LogCSVPath { get; init; }
  public string LoadCSVPath { get; init; }

  public const string DefaultConnectionStr =
    "Provider=Microsoft.Jet.OLEDB.4.0;Password=\"\";User ID=Admin;Mode=Share Deny None;";

  public static MazakConfig LoadFromRegistry()
  {
    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
      @"Software\SeedTactics\FMS Insight Mazak Proxy"
    );
    if (key != null)
    {
      return new MazakConfig()
      {
        DBType = (MazakDbType)Enum.Parse(typeof(MazakDbType), key.GetValue("DBType", "MazakWeb").ToString()),
        SQLConnectionString = key.GetValue("SQLConnectionString", DefaultConnectionStr).ToString(),
        OleDbDatabasePath = key.GetValue("OleDbDatabasePath", "c:\\Mazak\\NFMS\\DB").ToString(),
        LogCSVPath = key.GetValue("LogCSVPath", "c:\\Mazak\\FMS\\Log").ToString(),
        LoadCSVPath = key.GetValue("LoadCSVPath", "c:\\Mazak\\FMS\\LDS").ToString(),
      };
    }
    else
    {
      // the installer is 32bit and we could be 64 bit, so we need to use the Wow6432 registry
      // .NET 4 added an easy way to access the Wow6432 registry, but can't use that since we are
      // .NET 3.5
      var dbTy = RegistryWOW6432.GetRegKey32(
        RegHive.HKEY_LOCAL_MACHINE,
        @"Software\SeedTactics\FMS Insight Mazak Proxy",
        "DBType"
      );

      return new MazakConfig()
      {
        DBType = string.IsNullOrEmpty(dbTy)
          ? MazakDbType.MazakWeb
          : (MazakDbType)Enum.Parse(typeof(MazakDbType), dbTy),
        SQLConnectionString = RegistryWOW6432.GetRegKey32(
          RegHive.HKEY_LOCAL_MACHINE,
          @"Software\SeedTactics\FMS Insight Mazak Proxy",
          "SQLConnectionString"
        ),
        OleDbDatabasePath = RegistryWOW6432.GetRegKey32(
          RegHive.HKEY_LOCAL_MACHINE,
          @"Software\SeedTactics\FMS Insight Mazak Proxy",
          "OleDbDatabasePath"
        ),
        LogCSVPath = RegistryWOW6432.GetRegKey32(
          RegHive.HKEY_LOCAL_MACHINE,
          @"Software\SeedTactics\FMS Insight Mazak Proxy",
          "LogCSVPath"
        ),
        LoadCSVPath = RegistryWOW6432.GetRegKey32(
          RegHive.HKEY_LOCAL_MACHINE,
          @"Software\SeedTactics\FMS Insight Mazak Proxy",
          "LoadCSVPath"
        ),
      };
    }
  }
}
