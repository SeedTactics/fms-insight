using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Authentication;

namespace BlackMaple.FMSInsight.ReverseProxy
{
  public class Program
  {
    public static void Main(string[] args)
    {
      var exeDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
      System.IO.Directory.SetCurrentDirectory(exeDir);
      Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
          services.Configure<KestrelServerOptions>(context.Configuration.GetSection("Kestrel"));
        })
        .UseWindowsService()
        .ConfigureWebHostDefaults(webBuilder => webBuilder
          .UseStartup<Startup>()
          .UseContentRoot(exeDir) // must appear after UseWindowsService or setting is overwritten
        )
        .Build()
        .Run();
    }
  }
  public class Startup
  {
    public class InsightProxyConfig
    {
      public string InsightHost { get; set; }
      public int InsightPort { get; set; }
      public string OpenIDConnectAuthority { get; set; }
      public List<string> AuthTokenAudiences { get; set; }
    }

    public IConfiguration Configuration { get; }
    public InsightProxyConfig ProxyConfig { get; }
    public Startup(IConfiguration cfg)
    {
      Configuration = cfg;
      ProxyConfig = new InsightProxyConfig();
      Configuration.GetSection("InsightProxy").Bind(ProxyConfig);
    }

    public void ConfigureServices(IServiceCollection services)
    {
      services
        .AddResponseCompression()
        .AddCors()
        .AddHttpClient("proxy")
        .ConfigurePrimaryHttpMessageHandler(() =>
          new HttpClientHandler()
          {
            AllowAutoRedirect = false
          }
        )
        ;

      if (!string.IsNullOrEmpty(ProxyConfig.OpenIDConnectAuthority))
      {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
          options.Authority = ProxyConfig.OpenIDConnectAuthority;
          options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
          {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidAudiences = ProxyConfig.AuthTokenAudiences
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

    private readonly HashSet<string> IgnoreRequestHeaders = new HashSet<string>(new[] {
      "host",
      "connection",
      "keep-alive",
      "transfer-encoding",
      "upgrade",
    });

    private readonly HashSet<string> IgnoreResponseHeaders = new HashSet<string>(new[] {
      "date",
      "server",
      "transfer-encoding",
    });

    public void Configure(IApplicationBuilder app, IHttpClientFactory httpFactory, ILogger<Startup> logger)
    {
      app.UseResponseCompression();
      app.UseHttpsRedirection();
      app.UseHsts();
      app.UseCors();
      app.UseWebSockets();

      // authentication
      if (!string.IsNullOrEmpty(ProxyConfig.OpenIDConnectAuthority))
      {
        app.Use(async (context, next) =>
        {
          // the fms-information path is unauthorized, everything else requires auth
          if (context.Request.Path == "/api/v1/fms/fms-information")
          {
            await next.Invoke();
          }
          else if (context.Request.Path.StartsWithSegments("/api"))
          {
            var authResponse = await context.AuthenticateAsync();
            if (!authResponse.Succeeded)
            {
              context.Response.StatusCode = 401;
              return;
            }
            else
            {
              await next.Invoke();
            }
          }
          else
          {
            // loading static pages
            await next.Invoke();
          }
        });
      }

      // websocket proxy
      app.Use(async (context, next) =>
      {
        if (context.Request.Path != "/api/v1/events")
        {
          await next.Invoke();
          return;
        }
        if (!context.WebSockets.IsWebSocketRequest)
        {
          context.Response.StatusCode = 400;
          return;
        }

        var ws = await context.WebSockets.AcceptWebSocketAsync();

        var buffer = new byte[1024 * 1024];
        using (var client = new ClientWebSocket())
        {
          var uriB = new UriBuilder()
          {
            Scheme = "ws",
            Host = ProxyConfig.InsightHost,
            Port = ProxyConfig.InsightPort,
            Path = "/api/v1/events"
          };
          try
          {
            await client.ConnectAsync(uriB.Uri, context.RequestAborted);
            WebSocketReceiveResult res;
            do
            {
              res = await client.ReceiveAsync(buffer, context.RequestAborted);
              await ws.SendAsync(new ArraySegment<byte>(buffer, 0, res.Count), res.MessageType, res.EndOfMessage, context.RequestAborted);
            } while (res.MessageType != WebSocketMessageType.Close);
          }
          catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
          {
            //do nothing, client closed connection
          }
          catch (System.Threading.Tasks.TaskCanceledException)
          {
            // also do nothing, client closed connection
          }
          catch (System.OperationCanceledException)
          {
            // a third possibility when the client closed connection
          }
          catch (Exception ex)
          {
            logger.LogInformation(ex, "Error during websocket");
          }
          finally
          {
            try
            {
              await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server is closing", context.RequestAborted);
            }
            catch (System.OperationCanceledException)
            {
              // ignore if request is being canceled
            }
          }
        }
      });

      //HTTP proxy
      app.Run(async context =>
      {
        var uriB = new UriBuilder()
        {
          Scheme = "http",
          Host = ProxyConfig.InsightHost,
          Port = ProxyConfig.InsightPort,
          Path = context.Request.Path,
          Query = context.Request.QueryString.ToUriComponent()
        };
        var msg = new HttpRequestMessage();
        msg.RequestUri = uriB.Uri;
        msg.Method = new HttpMethod(context.Request.Method);
        if (HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPost(context.Request.Method))
        {
          msg.Content = new StreamContent(context.Request.Body);
        }
        HashSet<string> connHeader = new HashSet<string>();
        if (context.Request.Headers.TryGetValue("Connection", out var vals))
        {
          connHeader = new HashSet<string>(vals.SelectMany(s => s.Split(",")).Select(s => s.Trim().ToLower()));
        }
        foreach (var h in context.Request.Headers)
        {
          if (!connHeader.Contains(h.Key.ToLower()) && !IgnoreRequestHeaders.Contains(h.Key.ToLower()))
          {
            var ok = msg.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
            if (!ok)
            {
              ok = msg.Content?.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray()) ?? false;
            }
          }
        }
        msg.Headers.Host = msg.RequestUri.Host;
        var client = httpFactory.CreateClient("proxy");
        using (var resp = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted))
        {
          context.Response.StatusCode = (int)resp.StatusCode;
          foreach (var h in resp.Headers.Concat(resp.Content.Headers))
          {
            if (!IgnoreResponseHeaders.Contains(h.Key.ToLower()))
            {
              context.Response.Headers[h.Key] = h.Value.ToArray();
            }
          }
          await resp.Content.CopyToAsync(context.Response.Body);
        }
      });
    }
  }
}
