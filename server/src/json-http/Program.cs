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
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MachineWatchApiServer
{
    public class Program
    {
        public static IConfiguration Configuration {get;} = new ConfigurationBuilder()
            #if USE_SERVICE
            .SetBasePath(Path.GetDirectoryName(
                System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName
            ))
            #else
            .SetBasePath(Directory.GetCurrentDirectory())
            #endif
            .AddIniFile("config.ini", optional: true)
            .AddEnvironmentVariables()
            .Build();

        private static void EnableSerilog()
        {
            var dataDir = Configuration["DataDirectory"];
            var enableDebug = Configuration["EnableDebug"];
            var logConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information);

            #if LOG_TO_EVENTLOG
            logConfig = logConfig.WriteTo.EventLog(
                "Machine Watch",
                manageEventSource: true,
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information);
            #endif

            if (   !string.IsNullOrEmpty(dataDir)
                && !string.IsNullOrEmpty(enableDebug)
                && bool.TryParse(enableDebug, out bool enable)
                && enable) {

                logConfig = logConfig.WriteTo.File(
                    new Serilog.Formatting.Compact.CompactJsonFormatter(),
                    System.IO.Path.Combine(dataDir, "machinewatch-debug.txt"),
                    rollingInterval: RollingInterval.Day,
                    restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug);
            }

            Log.Logger = logConfig.CreateLogger();
        }

        public static IWebHost BuildWebHost()
        {
            #if USE_SERVICE
            var contentRoot = Path.GetDirectoryName(
                System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName
            );
            #else
            var contentRoot = Directory.GetCurrentDirectory();
            #endif

            return new WebHostBuilder()
                .UseConfiguration(Configuration)
                .UseKestrel()
                .UseContentRoot(contentRoot)
                .UseSerilog()
                .UseStartup<Startup>()
                .Build();
        }

        public static void Main()
        {
            EnableSerilog();

            var host = BuildWebHost();

            #if USE_SERVICE
                Microsoft.AspNetCore.Hosting.WindowsServices.WebHostWindowsServiceExtensions
                    .RunAsService();
            #else
                host.Run();
            #endif
        }
    }
}
