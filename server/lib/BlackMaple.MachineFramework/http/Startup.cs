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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Converters;
using NSwag.AspNetCore;
using NSwag.SwaggerGeneration.WebApi;
using Serilog;

namespace BlackMaple.MachineFramework
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

        private IFMSImplementation _fmsImpl;

        public Startup(IFMSImplementation fmsImpl)
        {
            _fmsImpl = fmsImpl;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var cfgWrapper = new ConfigWrapper();
            _fmsImpl.Backend.Init(
                Program.ServerSettings.DataDirectory,
                cfgWrapper,
                Program.FMSSettings);
            foreach (var w in _fmsImpl.Workers)
                w.Init(
                    _fmsImpl.Backend,
                    Program.ServerSettings.DataDirectory,
                    cfgWrapper,
                    Program.FMSSettings
                );

            var settings = new BlackMaple.MachineFramework.SettingStore(Program.ServerSettings.DataDirectory);

            #if SERVE_REMOTING
            if (Program.ServerSettings.EnableSailAPI) {
                var machServer =
                    new BlackMaple.MachineWatch.RemotingServer(
                        _fmsImpl,
                        Program.ServerSettings.DataDirectory,
                        settings
                    );
                services.AddSingleton<BlackMaple.MachineWatch.RemotingServer>(machServer);
            }
            #endif

            services
                .AddSingleton<FMSInfo>(_fmsImpl.Info)
                .AddSingleton<IStoreSettings>(settings)
                .AddSingleton<BlackMaple.MachineFramework.IFMSBackend>(_fmsImpl.Backend)
                .AddSingleton<Controllers.WebsocketManager>(
                    new Controllers.WebsocketManager(
                        _fmsImpl.Backend.LogDatabase(),
                        _fmsImpl.Backend.JobDatabase(),
                        _fmsImpl.Backend.JobControl())
                );

            services
                .AddMvcCore(options => {
                    options.ModelBinderProviders.Insert(0, new DateTimeBinderProvider());
                })
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_1)
                .AddApiExplorer()
                .AddFormatterMappings()
                .AddJsonFormatters()
                .AddJsonOptions(options => {
                    options.SerializerSettings.Converters.Add(new StringEnumConverter());
                    options.SerializerSettings.Converters.Add(new TimespanConverter());
                    options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
                    options.SerializerSettings.ConstructorHandling = Newtonsoft.Json.ConstructorHandling.AllowNonPublicDefaultConstructor;
                });

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(
            IApplicationBuilder app,
            IApplicationLifetime lifetime,
            IHostingEnvironment env,
            IServiceProvider services,
            Controllers.WebsocketManager wsManager)
        {
            app.UseMiddleware(typeof(ErrorHandlingMiddleware));
            app.UseMvc();
            app.UseStaticFiles();

            if (!string.IsNullOrEmpty(Program.FMSSettings.InstructionFilePath))
            {
                if (System.IO.Directory.Exists(Program.FMSSettings.InstructionFilePath)) {
                    app.UseStaticFiles(new StaticFileOptions() {
                        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Program.FMSSettings.InstructionFilePath),
                        RequestPath = "/instructions"
                    });
                } else {
                    Log.Error("Instruction directory {path} does not exist or is not a directory", Program.FMSSettings.InstructionFilePath);
                }
            }

            if (!string.IsNullOrEmpty(Program.ServerSettings.TLSCertFile)) {
                if (!env.IsDevelopment())
                    app.UseHsts();
                app.UseHttpsRedirection();
            }

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

            app.UseSwaggerUi3(typeof(Startup).Assembly, settings => {
                settings.GeneratorSettings.Title = "SeedTactic FMS Insight";
                settings.GeneratorSettings.Description = "API for access to FMS Insight for flexible manufacturing system control";
                settings.GeneratorSettings.Version = "1.3";
                settings.GeneratorSettings.DefaultEnumHandling = NJsonSchema.EnumHandling.String;
                settings.GeneratorSettings.DefaultPropertyNameHandling = NJsonSchema.PropertyNameHandling.Default;
                settings.PostProcess = document => {
                    document.Host = null;
                    document.BasePath = null;
                    document.Schemes = null;
                };
            });

            app.Run(async context => {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(System.IO.Path.Combine(env.WebRootPath,"index.html"));
            });

            lifetime.ApplicationStopping.Register(async () => {
                if (_fmsImpl == null) return;
                await wsManager.CloseAll();
                _fmsImpl.Backend?.Halt();
                foreach (var w in _fmsImpl.Workers)
                    w.Halt();
            });

            #if SERVE_REMOTING
            if (Program.ServerSettings.EnableSailAPI) {
                lifetime.ApplicationStopping.Register(() => {
                    var machServer = services.GetService<BlackMaple.MachineWatch.RemotingServer>();
                    machServer.Dispose();
                });
            }
            #endif
        }
    }
}
