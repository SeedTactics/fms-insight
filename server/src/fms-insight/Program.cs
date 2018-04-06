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
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace MachineWatchApiServer
{
    public class Program
    {
        public static string BaseDirectory {get;} =
            #if USE_SERVICE
            Path.GetDirectoryName(
                System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName
            );
            #else
            Directory.GetCurrentDirectory();
            #endif

        public static IConfiguration Configuration {get; private set;}
        public static ServerSettings ServerSettings {get; private set;}
        public static FMSSettings FMSSettings {get; private set;}

        private static void LoadConfig()
        {
            Configuration =
                new ConfigurationBuilder()
                .SetBasePath(Program.BaseDirectory)
                .AddIniFile("config.ini", optional: true)
                .AddEnvironmentVariables()
                .Build();

            ServerSettings = ServerSettings.Load(Configuration);
            FMSSettings = FMSSettings.Load(Configuration);
        }

        private static void EnableSerilog()
        {
            var logConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .WriteTo.Console(restrictedToMinimumLevel:
                    ServerSettings.EnableDebugLog ?
                        Serilog.Events.LogEventLevel.Debug
                      : Serilog.Events.LogEventLevel.Information );

            #if USE_SERVICE
            logConfig = logConfig.WriteTo.EventLog(
                "Machine Watch",
                manageEventSource: true,
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information);
            #endif

            if (!string.IsNullOrEmpty(ServerSettings.DataDirectory)
                && ServerSettings.EnableDebugLog) {

                logConfig = logConfig.WriteTo.File(
                    new Serilog.Formatting.Compact.CompactJsonFormatter(),
                    System.IO.Path.Combine(ServerSettings.DataDirectory, "machinewatch-debug.txt"),
                    rollingInterval: RollingInterval.Day,
                    restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug);
            }

            Log.Logger = logConfig.CreateLogger();
        }

        public static IWebHost BuildWebHost()
        {
            return new WebHostBuilder()
                .UseConfiguration(Configuration)
                .UseKestrel(options => {
                    IPAddress address;
                    #if DEBUG
                    address = IPAddress.Loopback;
                    #else
                    if (ServerSettings.IPv6) {
                        address = IPAddress.IPv6Any;
                    } else {
                        address = IPAddress.Any;
                    }
                    #endif

                    if (!string.IsNullOrEmpty(ServerSettings.TLSCertFile)) {
                        options.Listen(address, ServerSettings.Port, listenOptions => {
                            listenOptions.UseHttps(ServerSettings.TLSCertFile);
                        });
                    } else {
                        options.Listen(address, ServerSettings.Port);
                    }
                })
                .UseContentRoot(Program.BaseDirectory)
                .UseSerilog()
                .UseStartup<Startup>()
                .Build();
        }

        public static void Main()
        {
            LoadConfig();
            EnableSerilog();

            Log.Information("Starting machine watch with settings {@ServerSettings} and {@FMSSettings}", ServerSettings, FMSSettings);

            var host = BuildWebHost();

            #if USE_SERVICE
                Microsoft.AspNetCore.Hosting.WindowsServices.WebHostWindowsServiceExtensions
                    .RunAsService(host);
            #else
                host.Run();
            #endif
        }
    }
}
