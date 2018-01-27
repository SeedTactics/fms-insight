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
using BlackMaple.MachineWatchInterface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Converters;
using Swashbuckle.AspNetCore.Swagger;

namespace MachineWatchApiServer
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration config)
        {
            Configuration = config;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            var dataDir = Configuration["DataDir"];

            var pluginFile = Configuration["PluginFile"];
            var workerDir = Configuration["WorkerDirectory"];
            IPlugin plugin;
            if (!string.IsNullOrEmpty(pluginFile))
                plugin = new Plugin(dataDir, pluginFile, workerDir);
            else
            {
                #if DEBUG
                plugin = new Plugin(
                    settingsPath: dataDir,
                    backend: new MockBackend(),
                    info: new PluginInfo() {
                        Name = "mock-machinewatch",
                        Version = "1.2.3.4"
                    });
                #else
                    throw new Exception("Must specify plugin");
                #endif
            }

            plugin.Backend.Init(dataDir);
            foreach (var w in plugin.Workers) w.Init(plugin.Backend);

            #if USE_SERVICE
            var machServer =
                new BlackMaple.MachineWatch.Server(
                    p: new ServicePlugin(plugin),
                    forceTrace: false,
                    callInit: false
                );
            services.AddSingleton<BlackMaple.MachineWatch.Server>(machServer);
            lifetime.ApplicationStopping.Register(() => {
                machServer.Dispose();
            });
            #endif

            services.AddSingleton<IPlugin>(plugin);
            services.AddSingleton<BlackMaple.MachineWatchInterface.IServerBackend>(plugin.Backend);

            services.AddMvcCore()
                .AddApiExplorer()
                .AddFormatterMappings()
                .AddJsonFormatters()
                .AddJsonOptions(options => {
                    options.SerializerSettings.Converters.Add(new StringEnumConverter());
                });

            services.AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new Info { Title = "Machine Watch", Version = "v1" });
                    c.CustomSchemaIds(type => {
                        if (type == typeof(PalletStatus.Material))
                            return "PalletMaterial";
                        else
                            return type.Name;
                    });
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime lifetime, IPlugin plugin)
        {
            app.UseMvc();
            app.UseStaticFiles();
            app.UseSwagger();
            app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Machine Watch V1");
                });

            lifetime.ApplicationStopping.Register(() => {
                if (plugin == null) return;
                plugin.Backend?.Halt();
                foreach (var w in plugin.Workers)
                    w.Halt();
            });
        }
    }
}
