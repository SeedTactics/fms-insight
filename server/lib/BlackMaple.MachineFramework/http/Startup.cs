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
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Converters;
using Serilog;

namespace BlackMaple.MachineFramework
{
  public class Startup
  {
    public static void AddServices(IServiceCollection services, FMSImplementation fmsImpl, FMSSettings fmsSt, ServerSettings serverSt)
    {
      services
          .AddSingleton<Controllers.WebsocketManager>(
              new Controllers.WebsocketManager(fmsImpl.Backend)
          );

      services
        .AddResponseCompression()
        .AddCors(options =>
        {
          options.AddDefaultPolicy(builder =>
          {
            builder
              .WithOrigins(
                  fmsSt.AdditionalLogServers
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
                .WithHeaders("content-type", "authorization");
          });
        });

      var mvcBuilder = services
          .AddControllers(options =>
          {
            options.ModelBinderProviders.Insert(0, new DateTimeBinderProvider());
          })
          .ConfigureApplicationPartManager(am =>
          {
            if (fmsImpl.ExtraApplicationParts != null)
            {
              foreach (var p in fmsImpl.ExtraApplicationParts) am.ApplicationParts.Add(p);
            }
          })
          .AddNewtonsoftJson(options =>
          {
            options.SerializerSettings.Converters.Add(new StringEnumConverter());
            options.SerializerSettings.Converters.Add(new TimespanConverter());
            options.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
            options.SerializerSettings.ConstructorHandling = Newtonsoft.Json.ConstructorHandling.AllowNonPublicDefaultConstructor;
          });

      services.AddOpenApiDocument(cfg =>
      {
        cfg.Title = "SeedTactic FMS Insight";
        cfg.Description = "API for access to FMS Insight for flexible manufacturing system control";
        cfg.Version = "1.14";
        var settings = new Newtonsoft.Json.JsonSerializerSettings();
        settings.Converters.Add(new StringEnumConverter());
        settings.Converters.Add(new TimespanConverter());
        settings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
        cfg.SerializerSettings = settings;
        //cfg.DefaultReferenceTypeNullHandling = NJsonSchema.ReferenceTypeNullHandling.NotNull;
        cfg.DefaultResponseReferenceTypeNullHandling = NJsonSchema.Generation.ReferenceTypeNullHandling.NotNull;
        cfg.RequireParametersWithoutDefault = true;
        cfg.IgnoreObsoleteProperties = true;
      });

      if (serverSt.UseAuthentication)
      {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
          options.Authority = serverSt.AuthAuthority;
          options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
          {
            ValidateIssuer = true,
            ValidAudiences = serverSt.AuthTokenAudiences.Split(';')
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

    public void Configure(
        IApplicationBuilder app,
        IHostApplicationLifetime lifetime,
        IWebHostEnvironment env,
        FMSImplementation fmsImpl,
        ServerSettings serverSt,
        FMSSettings fmsSt,
        Controllers.WebsocketManager wsManager)
    {
      app.UseResponseCompression();

      app.UseStaticFiles();
      if (!string.IsNullOrEmpty(fmsSt.InstructionFilePath))
      {
        if (System.IO.Directory.Exists(fmsSt.InstructionFilePath))
        {
          app.UseStaticFiles(new StaticFileOptions()
          {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(fmsSt.InstructionFilePath),
            RequestPath = "/instructions"
          });
        }
        else
        {
          Log.Error("Instruction directory {path} does not exist or is not a directory", fmsSt.InstructionFilePath);
        }
      }

      app.UseMiddleware(typeof(ErrorHandlingMiddleware));

      app.UseRouting();

      app.UseCors();

      if (serverSt.UseAuthentication)
      {
        app.UseAuthentication();
        app.UseAuthorization();
      }

      if (!string.IsNullOrEmpty(serverSt.TLSCertFile))
      {
        if (!env.IsDevelopment())
          app.UseHsts();
        app.UseHttpsRedirection();
      }

      app.UseWebSockets();

      app.UseOpenApi();
      app.UseSwaggerUi3();

      app.UseEndpoints(endpoints =>
      {
        endpoints.MapControllers();

        endpoints.Map("/api/v1/events", async context =>
        {
          if (context.WebSockets.IsWebSocketRequest)
          {
            if (serverSt.UseAuthentication)
            {
              var res = await context.AuthenticateAsync();
              if (!res.Succeeded)
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
        });

        endpoints.MapFallbackToFile("/index.html");
      });

      lifetime.ApplicationStopping.Register(async () =>
      {
        if (fmsImpl == null) return;
        await wsManager.CloseAll();
        foreach (var w in fmsImpl.Workers)
          w.Dispose();
        fmsImpl.Backend?.Dispose();
        Serilog.Log.CloseAndFlush();
      });
    }
  }
}
