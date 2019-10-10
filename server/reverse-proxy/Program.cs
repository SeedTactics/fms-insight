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

namespace BlackMaple.FMSInsight.ReverseProxy
{
  public class Program
  {
    public const string InsightHost = "ip.address.of.insight";
    public const int InsightPort = 5000;
    public const int ListenPort = 5000;
    public const string TLSCertificateFile = null;

    public static void Main(string[] args)
    {
      new WebHostBuilder()
        .UseKestrel(options =>
        {
          options.Listen(System.Net.IPAddress.IPv6Any, ListenPort, listenOptions =>
            {
              if (!string.IsNullOrEmpty(TLSCertificateFile))
              {
                listenOptions.UseHttps(TLSCertificateFile);
              }
            }
          );
        })
        .UseStartup<Startup>()
        .ConfigureLogging(logging => logging.AddConsole())
        .Build()
        .Run();
    }
  }
  public class Startup
  {
    public void ConfigureServices(IServiceCollection services)
    {
      services
        .AddResponseCompression()
        .AddCors()
        .AddHttpClient()
        ;
    }

    private readonly HashSet<string> ValidResponseHeaders = new HashSet<string>(new[] {
      "content-type",
      "etag",
      "last-modified"
    });

    public void Configure(IApplicationBuilder app, IHttpClientFactory httpFactory, ILogger<Startup> logger)
    {
      app.UseResponseCompression();
      if (!string.IsNullOrEmpty(Program.TLSCertificateFile))
      {
        app.UseHttpsRedirection();
        app.UseHsts();
      }
      app.UseCors();
      app.UseWebSockets();

      app.Run(async context =>
      {
        if (context.Request.Path == "/api/v1/events")
        {
          if (context.WebSockets.IsWebSocketRequest)
          {
            var ws = await context.WebSockets.AcceptWebSocketAsync();

            var buffer = new byte[1024 * 1024];
            using (var client = new ClientWebSocket())
            {
              var uriB = new UriBuilder()
              {
                Scheme = "ws",
                Host = Program.InsightHost,
                Port = Program.InsightPort,
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
          }
          else
          {
            context.Response.StatusCode = 400;
          }
        }
        else
        {
          var uriB = new UriBuilder()
          {
            Scheme = "http",
            Host = Program.InsightHost,
            Port = Program.InsightPort,
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
        }
      });
    }
  }
}
