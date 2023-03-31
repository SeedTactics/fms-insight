/* Copyright (c) 2020, John Lenz

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
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.DebugMock")]

namespace BlackMaple.MachineFramework
{
  public class Program
  {
    private static (IConfiguration, ServerSettings) LoadConfig()
    {
      var configFile = Path.Combine(ServerSettings.ConfigDirectory, "config.ini");
      if (!File.Exists(configFile))
      {
        var defaultConfigFile = Path.Combine(ServerSettings.ContentRootDirectory, "default-config.ini");
        if (File.Exists(defaultConfigFile))
        {
          if (!Directory.Exists(ServerSettings.ConfigDirectory))
            Directory.CreateDirectory(ServerSettings.ConfigDirectory);
          System.IO.File.Copy(defaultConfigFile, configFile, overwrite: false);
        }
      }

      var cfg = new ConfigurationBuilder()
        .AddIniFile(configFile, optional: true)
        .AddEnvironmentVariables()
        .Build();

      var s = ServerSettings.Load(cfg);

      return (cfg, s);
    }

    public class CompressSerilogDebugLog : Serilog.Sinks.File.FileLifecycleHooks
    {
      public override void OnFileDeleting(string origTxtFile)
      {
        try
        {
          var newGzFile = Path.Combine(
            Path.GetDirectoryName(origTxtFile),
            Path.GetFileName(origTxtFile) + ".gz"
          );

          using (
            var sourceStream = new FileStream(origTxtFile, FileMode.Open, FileAccess.Read, FileShare.Read)
          )
          using (
            var targetStream = new FileStream(
              newGzFile,
              FileMode.OpenOrCreate,
              FileAccess.Write,
              FileShare.None
            )
          )
          using (
            var compressStream = new System.IO.Compression.GZipStream(
              targetStream,
              System.IO.Compression.CompressionLevel.Optimal
            )
          )
          {
            sourceStream.CopyTo(compressStream);
          }

          // delete after 30 days
          var rx = new System.Text.RegularExpressions.Regex(@"fmsinsight-debug(\d+)\.txt\.gz");
          foreach (var existingGzFile in Directory.GetFiles(Path.GetDirectoryName(origTxtFile), "*.gz"))
          {
            var m = rx.Match(Path.GetFileName(existingGzFile));
            if (m != null && m.Success && m.Groups.Count >= 2)
            {
              if (
                DateTime.TryParseExact(
                  m.Groups[1].Value,
                  "yyyyMMdd",
                  System.Globalization.CultureInfo.InvariantCulture,
                  System.Globalization.DateTimeStyles.None,
                  out var d
                )
              )
              {
                if (DateTime.Today.Subtract(d) > TimeSpan.FromDays(30))
                {
                  System.IO.File.Delete(existingGzFile);
                }
              }
            }
          }
        }
        catch (Exception ex)
        {
          Serilog.Debugging.SelfLog.WriteLine(
            "Error while archiving debug log " + origTxtFile + ": " + ex.ToString()
          );
        }
      }
    }

    public static void EnableSerilog(ServerSettings serverSt, bool enableEventLog)
    {
      var logConfig = new LoggerConfiguration().MinimumLevel
        .ControlledBy(serverSt.LogLevel)
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information);

#if SERVICE_AVAIL
      if (enableEventLog)
      {
        logConfig = logConfig.WriteTo.EventLog(
          "FMS Insight",
          manageEventSource: true,
          restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information
        );
      }
#endif

      if (serverSt.EnableDebugLog)
      {
        logConfig = logConfig.WriteTo.File(
          new Serilog.Formatting.Compact.CompactJsonFormatter(),
          System.IO.Path.Combine(ServerSettings.ConfigDirectory, "fmsinsight-debug.txt"),
          rollingInterval: RollingInterval.Day,
          hooks: new CompressSerilogDebugLog(),
          restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Verbose,
          retainedFileCountLimit: 3
        );
      }

      Log.Logger = logConfig.CreateLogger();
    }

    public static IHostBuilder CreateHostBuilder(
      IConfiguration cfg,
      ServerSettings serverSt,
      FMSSettings fmsSt,
      FMSImplementation fmsImpl,
      bool useService
    )
    {
      return new HostBuilder()
        .UseContentRoot(ServerSettings.ContentRootDirectory)
        .UseSerilog()
        .ConfigureWebHost(webBuilder =>
        {
          webBuilder
            .ConfigureServices(s =>
            {
              s.AddSingleton<FMSImplementation>(fmsImpl);
              s.AddSingleton<IFMSBackend>(fmsImpl.Backend);
              s.AddSingleton<FMSSettings>(fmsSt);
              s.AddSingleton<ServerSettings>(serverSt);
              Startup.AddServices(s, fmsImpl, fmsSt, serverSt);
            })
            .UseConfiguration(cfg)
            .SuppressStatusMessages(suppressStatusMessages: true)
            .ConfigureServices(s =>
            {
              s.Configure<KestrelServerOptions>(cfg.GetSection("Kestrel"));
            })
            .UseKestrel(options =>
            {
              var address = IPAddress.IPv6Any;
              if (!string.IsNullOrEmpty(serverSt.TLSCertFile))
              {
                options.Listen(
                  address,
                  serverSt.Port,
                  listenOptions =>
                  {
                    listenOptions.UseHttps(serverSt.TLSCertFile);
                  }
                );
              }
              else
              {
                options.Listen(address, serverSt.Port);
              }

              // support for MinDataRate
              // https://github.com/dotnet/aspnetcore/issues/4765
              var minReqRate = cfg.GetSection("Kestrel")
                .GetSection("Limits")
                .GetSection("MinRequestBodyDataRate");
              if (minReqRate.Value == "")
              {
                options.Limits.MinRequestBodyDataRate = null;
              }
              if (
                minReqRate.GetSection("BytesPerSecond").Exists()
                && minReqRate.GetSection("GracePeriod").Exists()
              )
              {
                options.Limits.MinRequestBodyDataRate = new MinDataRate(
                  minReqRate.GetValue<double>("BytesPerSecond"),
                  minReqRate.GetValue<TimeSpan>("GracePeriod")
                );
              }
              var minRespRate = cfg.GetSection("Kestrel")
                .GetSection("Limits")
                .GetSection("MinResponseDataRate");
              if (minRespRate.Value == "")
              {
                options.Limits.MinResponseDataRate = null;
              }
              if (
                minRespRate.GetSection("BytesPerSecond").Exists()
                && minRespRate.GetSection("GracePeriod").Exists()
              )
              {
                options.Limits.MinResponseDataRate = new MinDataRate(
                  minRespRate.GetValue<double>("BytesPerSecond"),
                  minRespRate.GetValue<TimeSpan>("GracePeriod")
                );
              }

              Serilog.Log.Debug("Kestrel Limits {@kestrel}", options.Limits);
            })
            .UseStartup<Startup>();
        })
#if SERVICE_AVAIL
        .ConfigureServices(services =>
        {
          if (useService)
          {
            services.AddSingleton<
              IHostLifetime,
              Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceLifetime
            >();
          }
        })
#endif
      ;
    }

    public static void Run(
      bool useService,
      Func<IConfiguration, FMSSettings, FMSImplementation> initalize,
      bool outputConfigToLog = true
    )
    {
      var (cfg, serverSt) = LoadConfig();
      EnableSerilog(serverSt: serverSt, enableEventLog: useService);

      FMSImplementation fmsImpl;
      FMSSettings fmsSt;
      try
      {
        fmsSt = new FMSSettings(cfg);
        if (outputConfigToLog)
        {
          Log.Information(
            "Starting FMS Insight with settings {@ServerSettings} and {@FMSSettings}. "
              + " Using ContentRoot {ContentRoot} and Config {ConfigDir}.",
            serverSt,
            fmsSt,
            ServerSettings.ContentRootDirectory,
            ServerSettings.ConfigDirectory
          );
        }
        fmsImpl = initalize(cfg, fmsSt);
      }
      catch (Exception ex)
      {
        Serilog.Log.Error(ex, "Error initializing FMS Insight");
        return;
      }
      var host = CreateHostBuilder(cfg, serverSt, fmsSt, fmsImpl, useService).Build();
      host.Run();
    }
  }
}
