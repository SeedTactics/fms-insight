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
      Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
          services.Configure<KestrelServerOptions>(context.Configuration.GetSection("Kestrel"));
        })
        .ConfigureWebHostDefaults(webBuilder => webBuilder
          .UseStartup<Startup>()
        )
        .ConfigureLogging(logging => logging.AddConsole())
        .Build()
        .Run();
    }
  }
  public class Startup
  {
    public class InsightProxyConfig
    {
      public string NiigataInsightHost { get; set; }
      public int NiigataInsightPort { get; set; }
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
        .AddHttpClient()
        ;

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

    private readonly HashSet<string> ValidResponseHeaders = new HashSet<string>(new[] {
      "content-type",
      "etag",
      "last-modified"
    });

    public void Configure(IApplicationBuilder app, IHttpClientFactory httpFactory, ILogger<Startup> logger)
    {
      app.UseResponseCompression();
      app.UseHttpsRedirection();
      app.UseHsts();
      app.UseCors();
      app.UseWebSockets();

      // authentication
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
            Host = ProxyConfig.NiigataInsightHost,
            Port = ProxyConfig.NiigataInsightPort,
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
            //do nothing
          }
          catch (Exception ex)
          {
            logger.LogInformation(ex, "Error during websocket");
          }
          finally
          {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server is closing", context.RequestAborted);
          }
        }
      });

      //HTTP proxy
      app.Run(async context =>
      {
        var uriB = new UriBuilder()
        {
          Scheme = "http",
          Host = ProxyConfig.NiigataInsightHost,
          Port = ProxyConfig.NiigataInsightPort,
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
        foreach (var h in context.Request.Headers)
        {
          msg.Content?.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
        }
        msg.Headers.Host = msg.RequestUri.Host;
        var client = httpFactory.CreateClient();
        using (var resp = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted))
        {
          context.Response.StatusCode = (int)resp.StatusCode;
          foreach (var h in resp.Headers.Concat(resp.Content.Headers))
          {
            if (ValidResponseHeaders.Contains(h.Key.ToLower()))
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
