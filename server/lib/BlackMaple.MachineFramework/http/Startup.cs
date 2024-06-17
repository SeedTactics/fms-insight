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
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BlackMaple.MachineFramework.DebugMock")]

namespace BlackMaple.MachineFramework;

public static class FMSInsightWebHost
{
  public static void JsonSettings(JsonSerializerOptions settings)
  {
    settings.Converters.Add(new JsonStringEnumConverter());
    settings.Converters.Add(new TimespanConverter());
    settings.AllowTrailingCommas = true;
    settings.PropertyNamingPolicy = null;
    settings.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
  }

  public static IHostBuilder AddFMSInsightWebHost(
    this IHostBuilder host,
    IConfiguration cfg,
    ServerSettings serverSt,
    FMSSettings fmsSt,
    System.Collections.Generic.IEnumerable<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPart> extraParts =
      null
  )
  {
    return host.UseSerilog()
      .ConfigureServices(s =>
      {
        s.AddSingleton<ServerSettings>(serverSt);
        s.AddSingleton<FMSSettings>(fmsSt);

        var jobStType =
          s.Select(service =>
              service.ServiceType.IsGenericType
              && service.ServiceType.GetGenericTypeDefinition() == typeof(ISynchronizeCellState<>)
                ? service.ServiceType.GenericTypeArguments[0]
                : null
            )
            .FirstOrDefault(x => x != null)
          ?? throw new Exception("Backend must implement ISynchronizeCellState");

        s.AddSingleton(typeof(IJobAndQueueControl), typeof(JobsAndQueuesFromDb<>).MakeGenericType(jobStType));

        s.AddSingleton<Controllers.WebsocketManager>();

        s.AddResponseCompression();

        s.AddCors(options =>
        {
          options.AddDefaultPolicy(builder =>
          {
            builder
              .WithOrigins([.. fmsSt.AdditionalLogServers])
              .WithMethods("GET")
              .WithHeaders("content-type", "authorization");
          });
        });

        s.AddControllers(options =>
          {
            options.ModelBinderProviders.Insert(0, new DateTimeBinderProvider());
          })
          .ConfigureApplicationPartManager(am =>
          {
            am.ApplicationParts.Add(
              new Microsoft.AspNetCore.Mvc.ApplicationParts.AssemblyPart(
                typeof(Controllers.jobsController).Assembly
              )
            );
            if (extraParts != null)
            {
              foreach (var p in extraParts)
                am.ApplicationParts.Add(p);
            }
          })
          .AddJsonOptions(options =>
          {
            JsonSettings(options.JsonSerializerOptions);
          });

        if (serverSt.UseAuthentication)
        {
          s.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
              options.Authority = serverSt.AuthAuthority;
              options.TokenValidationParameters =
                new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
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
      })
      .ConfigureWebHost(webBuilder =>
      {
        webBuilder
          .UseConfiguration(cfg)
          .SuppressStatusMessages(suppressStatusMessages: true)
          .AddKestral(cfg, serverSt)
          .UseStartup<Startup>();
      });
  }

  public class Startup
  {
    public void Configure(
      IApplicationBuilder app,
      FMSSettings fmsSt,
      ServerSettings serverSt,
      IWebHostEnvironment env,
      Controllers.WebsocketManager wsManager,
      IHostApplicationLifetime lifetime
    )
    {
      app.UseResponseCompression();

      app.UseStaticFiles();

      if (!string.IsNullOrEmpty(fmsSt.InstructionFilePath))
      {
        if (System.IO.Directory.Exists(fmsSt.InstructionFilePath))
        {
          app.UseStaticFiles(
            new StaticFileOptions()
            {
              FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                fmsSt.InstructionFilePath
              ),
              RequestPath = "/instructions"
            }
          );
        }
        else
        {
          Log.Error(
            "Instruction directory {path} does not exist or is not a directory",
            fmsSt.InstructionFilePath
          );
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

      app.Use(
        async (context, next) =>
        {
          context.Response.Headers.ContentSecurityPolicy =
            "default-src 'self'; style-src 'self' 'unsafe-inline'; connect-src *; base-uri 'self'; form-action 'self'; font-src 'self' data:; manifest-src 'self' data:; "
            +
            // https://github.com/vitejs/vite/tree/main/packages/plugin-legacy#content-security-policy
            "script-src 'self';";
          context.Response.Headers.Append("Cross-Origin-Embedder-Policy", "require-corp");
          context.Response.Headers.Append("Cross-Origin-Opener-Policy", "same-origin");
          await next();
        }
      );

      app.UseWebSockets();

      app.UseEndpoints(endpoints =>
      {
        var ctrlBuilder = endpoints.MapControllers();
        if (serverSt.UseAuthentication)
        {
          ctrlBuilder.RequireAuthorization();
        }

        endpoints.Map(
          "/api/v1/events",
          async context =>
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
          }
        );

        endpoints.MapFallbackToFile("/index.html");
      });

      lifetime.ApplicationStopped.Register(() =>
      {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Serilog.Log.CloseAndFlush();
      });
    }
  }

  private static IWebHostBuilder AddKestral(
    this IWebHostBuilder webHost,
    IConfiguration cfg,
    ServerSettings serverSt
  )
  {
    return webHost
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
        var minReqRate = cfg.GetSection("Kestrel").GetSection("Limits").GetSection("MinRequestBodyDataRate");
        if (minReqRate.Value == "")
        {
          options.Limits.MinRequestBodyDataRate = null;
        }
        if (minReqRate.GetSection("BytesPerSecond").Exists() && minReqRate.GetSection("GracePeriod").Exists())
        {
          options.Limits.MinRequestBodyDataRate = new MinDataRate(
            minReqRate.GetValue<double>("BytesPerSecond"),
            minReqRate.GetValue<TimeSpan>("GracePeriod")
          );
        }
        var minRespRate = cfg.GetSection("Kestrel").GetSection("Limits").GetSection("MinResponseDataRate");
        if (minRespRate.Value == "")
        {
          options.Limits.MinResponseDataRate = null;
        }
        if (
          minRespRate.GetSection("BytesPerSecond").Exists() && minRespRate.GetSection("GracePeriod").Exists()
        )
        {
          options.Limits.MinResponseDataRate = new MinDataRate(
            minRespRate.GetValue<double>("BytesPerSecond"),
            minRespRate.GetValue<TimeSpan>("GracePeriod")
          );
        }

        Log.Debug("Kestrel Limits {@kestrel}", options.Limits);
      });
  }
}
