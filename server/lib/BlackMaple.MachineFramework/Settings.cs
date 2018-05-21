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
using Microsoft.Extensions.Configuration;

namespace BlackMaple.MachineFramework
{
    public enum SerialType
    {
        NoAutomaticSerials,
        AssignOneSerialPerMaterial,  // assign a different serial to each piece of material
        AssignOneSerialPerCycle,     // assign a single serial to all the material on each cycle
    }

    public class SerialSettings
    {
        public SerialType SerialType {get;set;}
        public int SerialLength {get;set;}
    }

    public enum WorkorderAssignmentType
    {
      AssignWorkorderAtUnload,
      AssignWorkorderAtWash,
      NoAutomaticWorkorderAssignment,
    }

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

      public static string ConfigDirectory {get;} =
        Directory.GetCurrentDirectory();

      public static string ContentRootDirectory {get;} =
        Directory.GetCurrentDirectory();

      #endif


      public string DataDirectory {get;set;} = null;
      public bool EnableDebugLog {get;set;} = false;
      public int Port {get;set;} = 5000;
      public string TLSCertFile {get;set;} = null;
      public bool EnableSailAPI {get;set;} = false;

      public static ServerSettings Load(IConfiguration config)
      {
        var s = config.GetSection("SERVER").Get<ServerSettings>();
        if (s == null)
            s = new ServerSettings();

        if (string.IsNullOrEmpty(s.DataDirectory)) {
          s.DataDirectory = DefaultDataDirectory();
        }
        return s;
      }

      private static string DefaultDataDirectory()
      {
        var commonData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData);

        //check old cms research data directory
        var dataDir = Path.Combine(commonData, "CMS Research", "MachineWatch");
        if (Directory.Exists(dataDir))
          return dataDir;

        //try new seedtactics directory
        dataDir = Path.Combine(commonData, "SeedTactics", "MachineWatch");
        if (Directory.Exists(dataDir))
          return dataDir;

        //now FMSInsight directory
        dataDir = Path.Combine(commonData, "SeedTactics", "FMSInsight");
        if (!Directory.Exists(dataDir)) {
          try {
            Directory.CreateDirectory(dataDir);
          } catch (UnauthorizedAccessException) {
            // don't have permissions in CommonApplicationData
            dataDir = Path.Combine(
              System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
              "SeedTactics",
              "FMSInsight"
            );
          }
        }

        if (!Directory.Exists(dataDir))
          Directory.CreateDirectory(dataDir);
        return dataDir;
      }
    }

    public class FMSSettings
    {
      public bool AutomaticSerials {get;set;} = false;
      public int SerialLength {get;set;} = 10;
      public BlackMaple.MachineFramework.WorkorderAssignmentType WorkorderAssignment {get;set;}
        = BlackMaple.MachineFramework.WorkorderAssignmentType.NoAutomaticWorkorderAssignment;

      static public FMSSettings Load(IConfiguration config)
      {
        var s = config.GetSection("FMS").Get<FMSSettings>();
        if (s == null)
            s = new FMSSettings();
        return s;
      }
    }

}