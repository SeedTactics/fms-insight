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
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using BlackMaple.MachineWatchInterface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
    private FMSImplementation _fmsImpl;
    private ServerSettings _serverSt;
    private FMSSettings _fmsSt;

    public Startup(FMSImplementation fmsImpl, ServerSettings serverSt, FMSSettings fmsSt)
    {
      _fmsImpl = fmsImpl;
      _serverSt = serverSt;
      _fmsSt = fmsSt;
    }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {

#if SERVE_REMOTING
      if (_serverSt.EnableSailAPI) {
        var machServer =
          new BlackMaple.MachineWatch.RemotingServer(
            _fmsImpl,
            _fmsSt.DataDirectory
          );
        services.AddSingleton<BlackMaple.MachineWatch.RemotingServer>(machServer);
      } else {
        // add dummy service which does not serve anything
        services.AddSingleton<BlackMaple.MachineWatch.RemotingServer>(new BlackMaple.MachineWatch.RemotingServer());
      }
#endif

      services
          .AddSingleton<BlackMaple.MachineFramework.FMSImplementation>(_fmsImpl)
          .AddSingleton<BlackMaple.MachineFramework.IFMSBackend>(_fmsImpl.Backend)
          .AddSingleton<Controllers.WebsocketManager>(
              new Controllers.WebsocketManager(
                  _fmsImpl.Backend.LogDatabase(),
                  _fmsImpl.Backend.JobDatabase(),
                  _fmsImpl.Backend.JobControl())
          );

      var mvcBuilder = services
          .AddResponseCompression()
          .AddCors()
          .AddMvcCore(options =>
          {
            options.ModelBinderProviders.Insert(0, new DateTimeBinderProvider());
          })
          .AddApiExplorer()
          .AddFormatterMappings()
          .AddJsonFormatters()
          .AddJsonOptions(options =>
          {
            options.SerializerSettings.Converters.Add(new StringEnumConverter());
            options.SerializerSettings.Converters.Add(new TimespanConverter());
            options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
            options.SerializerSettings.ConstructorHandling = Newtonsoft.Json.ConstructorHandling.AllowNonPublicDefaultConstructor;
          });

      services.AddSwaggerDocument(cfg =>
      {
        cfg.Title = "SeedTactic FMS Insight";
        cfg.Description = "API for access to FMS Insight for flexible manufacturing system control";
        cfg.Version = "1.11";
        var settings = new Newtonsoft.Json.JsonSerializerSettings();
        settings.Converters.Add(new StringEnumConverter());
        settings.Converters.Add(new TimespanConverter());
        settings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
        cfg.SerializerSettings = settings;
        //cfg.DefaultReferenceTypeNullHandling = NJsonSchema.ReferenceTypeNullHandling.NotNull;
        cfg.DefaultResponseReferenceTypeNullHandling = NJsonSchema.ReferenceTypeNullHandling.NotNull;
        cfg.RequireParametersWithoutDefault = true;
        cfg.IgnoreObsoleteProperties = true;
      });

      if (_serverSt.UseAuthentication)
      {
        mvcBuilder.AddAuthorization();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
          options.Authority = _serverSt.OpenIDConnectAuthority;
          options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
          {
            ValidateIssuer = true,
            ValidAudiences = _serverSt.AuthTokenAudiences.Split(';')
          };
#if DEBUG
          options.RequireHttpsMetadata = false;
#endif
          options.Events = new JwtBearerEvents
          {
            OnMessageReceived = context =>
            {
              var token = context.Request.Query["token"];
              if (context.Request.Path == "/api/v1/events" && !string.IsNullOrEmpty(token))
              {
                context.Token = token;
              }
              return System.Threading.Tasks.Task.CompletedTask;
            }
          };
        });
      }

    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(
        IApplicationBuilder app,
        IApplicationLifetime lifetime,
        IHostingEnvironment env,
#if SERVE_REMOTING
        BlackMaple.MachineWatch.RemotingServer machServer,
#endif
        Controllers.WebsocketManager wsManager)
    {
      app.UseResponseCompression();
      app.UseCors(builder => builder
        .WithOrigins(
            _fmsSt.AdditionalLogServers
#if DEBUG
                  .Concat(new[] { "http://localhost:1234" }) // parcel bundler url
#endif
                  .ToArray()
        )
#if DEBUG
        .WithMethods(new[] { "GET", "PUT", "POST", "DELETE" })
#else
        .WithMethods("GET")
#endif
        .WithHeaders("content-type", "authorization")
      );
      app.UseMiddleware(typeof(ErrorHandlingMiddleware));
      if (_serverSt.UseAuthentication)
      {
        app.UseAuthentication();
      }
      app.UseMvc();

      // https://github.com/aspnet/Home/issues/2442
      var fileExt = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
      fileExt.Mappings[".webmanifest"] = "application/manifest+json";
      app.UseStaticFiles(new StaticFileOptions
      {
        ContentTypeProvider = fileExt
      });

      if (!string.IsNullOrEmpty(_fmsSt.InstructionFilePath))
      {
        if (System.IO.Directory.Exists(_fmsSt.InstructionFilePath))
        {
          app.UseStaticFiles(new StaticFileOptions()
          {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(_fmsSt.InstructionFilePath),
            RequestPath = "/instructions"
          });
        }
        else
        {
          Log.Error("Instruction directory {path} does not exist or is not a directory", _fmsSt.InstructionFilePath);
        }
      }

      if (!string.IsNullOrEmpty(_serverSt.TLSCertFile))
      {
        if (!env.IsDevelopment())
          app.UseHsts();
        app.UseHttpsRedirection();
      }

      app.UseWebSockets();
      app.Use(async (context, next) =>
      {
        if (context.Request.Path == "/api/v1/events")
        {
          if (context.WebSockets.IsWebSocketRequest)
          {
            if (_serverSt.UseAuthentication)
            {
              var res = await context.AuthenticateAsync();
              if (res.Failure != null)
              {
                context.Response.StatusCode = 401;
                return;
              }
            }
            var ws = await context.WebSockets.AcceptWebSocketAsync();
            await wsManager.HandleWebsocket(ws);
          }
          else
          {
            context.Response.StatusCode = 400;
          }
        }
        else
        {
          await next();
        }
      });

      app
        .UseSwagger(settings =>
        {
          settings.PostProcess = (doc, req) =>
          {
            doc.Host = null;
            doc.BasePath = null;
            doc.Schemes = null;
          };
        })
        .UseSwaggerUi3();

      app.Run(async context =>
      {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(System.IO.Path.Combine(env.WebRootPath, "index.html"));
      });

      lifetime.ApplicationStopping.Register(async () =>
      {
        if (_fmsImpl == null) return;
        await wsManager.CloseAll();
        foreach (var w in _fmsImpl.Workers)
          w.Dispose();
        _fmsImpl.Backend?.Dispose();
      });

#if SERVE_REMOTING
      if (_serverSt.EnableSailAPI) {
        lifetime.ApplicationStopping.Register(() => {
          machServer.Dispose();
        });
      }
#endif
    }
  }
}
