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
using System.Threading;
using BlackMaple.MachineWatchInterface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Converters;
using NSwag.AspNetCore;
using Serilog;

namespace MachineWatchApiServer
{
    public class Startup
    {
        private class ConfigWrapper : BlackMaple.MachineFramework.IConfig
        {
            public T GetValue<T>(string section, string key)
            {
                return Program.Configuration.GetSection(section).GetValue<T>(key);
            }
        }

        #if DEBUG
        public static BlackMaple.MachineFramework.IServerBackend DebugMockBackend {get;set;} =
            new MockBackend();
        public static PluginInfo DebugMockPluginInfo {get;set;} =
            new PluginInfo() {
                Name = "mock-machinewatch",
                Version = "1.2.3.4"
            };
        #endif

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            IPlugin plugin;
            if (!string.IsNullOrEmpty(Program.FMSSettings.PluginFile))
                plugin = new Plugin(Program.FMSSettings.PluginFile);
            else
            {
                #if DEBUG
                    plugin = new Plugin(DebugMockBackend, DebugMockPluginInfo);
                #else
                    throw new Exception("Must specify plugin");
                #endif
            }
            plugin.Backend.Init(
                Program.ServerSettings.DataDirectory,
                new ConfigWrapper(),
                new BlackMaple.MachineFramework.SerialSettings() {
                    SerialType = Program.FMSSettings.AutomaticSerials ?
                        BlackMaple.MachineFramework.SerialType.AssignOneSerialPerMaterial :
                        BlackMaple.MachineFramework.SerialType.NoAutomaticSerials,
                    SerialLength = Program.FMSSettings.SerialLength,
                });
            foreach (var w in plugin.Workers) w.Init(plugin.Backend);

            System.Diagnostics.Trace.AutoFlush = true;
            var traceListener = new SerilogTraceListener();
            foreach (var s in plugin.Backend.TraceSources())
            {
                s.Listeners.Add(traceListener);
            }
            foreach (var w in plugin.Workers)
            {
                w.TraceSource.Listeners.Add(traceListener);
            }

            var settings = new BlackMaple.MachineFramework.SettingStore(Program.ServerSettings.DataDirectory);

            #if SERVE_REMOTING
            var machServer =
                new BlackMaple.MachineWatch.RemotingServer(
                    p: new ServicePlugin(plugin),
                    dataDir,
                    settings
                );
            services.AddSingleton<BlackMaple.MachineWatch.RemotingServer>(machServer);
            #endif

            services
                .AddSingleton<IPlugin>(plugin)
                .AddSingleton<IStoreSettings>(settings)
                .AddSingleton<BlackMaple.MachineFramework.IServerBackend>(plugin.Backend)
                .AddSingleton<Controllers.WebsocketManager>(
                    new Controllers.WebsocketManager(
                        plugin.Backend.LogDatabase(),
                        plugin.Backend.JobDatabase(),
                        plugin.Backend.JobControl())
                );

            services
                .AddMvcCore(options => {
                    options.ModelBinderProviders.Insert(0, new DateTimeBinderProvider());
                })
                .AddApiExplorer()
                .AddFormatterMappings()
                .AddJsonFormatters()
                .AddJsonOptions(options => {
                    options.SerializerSettings.Converters.Add(new StringEnumConverter());
                    options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
                });

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(
            IApplicationBuilder app,
            IPlugin plugin,
            IApplicationLifetime lifetime,
            IHostingEnvironment env,
            IServiceProvider services,
            Controllers.WebsocketManager wsManager)
        {
            app.UseMvc();
            app.UseStaticFiles();

            app.UseWebSockets();
            app.Use(async (context, next) => {
                if (context.Request.Path == "/api/v1/events")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var ws = await context.WebSockets.AcceptWebSocketAsync();
                        await wsManager.HandleWebsocket(ws);
                    } else {
                        context.Response.StatusCode = 400;
                    }
                } else {
                    await next();
                }
            });

            app.UseSwaggerUi3(typeof(Startup).Assembly,
                new SwaggerUi3Settings() {
                    Title = "MachineWatch",
                    Version = "v1",
                    DefaultEnumHandling = NJsonSchema.EnumHandling.String,
                    DefaultPropertyNameHandling = NJsonSchema.PropertyNameHandling.Default,
                    PostProcess = document => {
                        document.Host = "";
                        document.BasePath = "/";
                    }
                });


            lifetime.ApplicationStopping.Register(async () => {
                if (plugin == null) return;
                await wsManager.CloseAll();
                plugin.Backend?.Halt();
                foreach (var w in plugin.Workers)
                    w.Halt();
            });

            #if SERVE_REMOTING
            lifetime.ApplicationStopping.Register(() => {
                var machServer = services.GetService<BlackMaple.MachineWatch.RemotingServer>();
                machServer.Dispose();
            });
            #endif
        }
    }
}
