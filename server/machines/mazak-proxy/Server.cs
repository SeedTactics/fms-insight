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

namespace BlackMaple.FMSInsight.Mazak.Proxy
{
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Net;
  using System.Threading;

  public interface IHttpServer : IDisposable
  {
    void AddLoadingHandler<T>(string path, Func<T> handler);
    void AddPostHandler<T, R>(string path, Func<T, R> handler);
    void Start();
  }

  public sealed class HttpServer : IHttpServer, IDisposable
  {
    private readonly HttpListener _listener;

    private class PostHandler
    {
      public Type BodyType { get; set; }
      public Func<object, object> Handler { get; set; }
    }

    private readonly Dictionary<string, Func<object>> _loadingHandlers;
    private readonly Dictionary<string, PostHandler> _postHandlers;
    private int _disposed;
    private long _requestSequence;
    private int _activeRequests;

    public HttpServer(string url)
    {
      _listener = new HttpListener();
      _listener.Prefixes.Add(url);
      _loadingHandlers = new Dictionary<string, Func<object>>();
      _postHandlers = new Dictionary<string, PostHandler>();
    }

    public void Dispose()
    {
      if (Interlocked.Exchange(ref _disposed, 1) != 0)
        return;
      try
      {
        _listener.Close();
      }
      catch (Exception ex)
      {
        Serilog.Log.Error(ex, "Error closing Mazak proxy HTTP listener");
      }
    }

    public void Start()
    {
      _listener.Start();
      BeginListening();
    }

    private bool IsDisposed => Interlocked.CompareExchange(ref _disposed, 0, 0) != 0;

    private void BeginListening()
    {
      if (IsDisposed)
        return;
      try
      {
        _listener.BeginGetContext(HandleRequest, null);
      }
      catch (ObjectDisposedException) when (IsDisposed) { }
      catch (HttpListenerException) when (IsDisposed) { }
      catch (Exception ex)
      {
        Serilog.Log.Error(ex, "Error accepting Mazak proxy HTTP request; retrying in one second");
        ThreadPool.QueueUserWorkItem(_ =>
        {
          Thread.Sleep(TimeSpan.FromSeconds(1));
          BeginListening();
        });
      }
    }

    public void AddLoadingHandler<T>(string path, Func<T> handler)
    {
      _loadingHandlers.Add(path, () => handler());
    }

    public void AddPostHandler<T, R>(string path, Func<T, R> handler)
    {
      _postHandlers.Add(
        path,
        new PostHandler { BodyType = typeof(T), Handler = t => handler((T)t) }
      );
    }

    private void HandleRequest(IAsyncResult asyncResult)
    {
      var requestId = Interlocked.Increment(ref _requestSequence);
      var timer = Stopwatch.StartNew();
      var activeRequests = Interlocked.Increment(ref _activeRequests);
      HttpListenerContext ctx = null;
      string method = "unknown";
      string path = "unknown";
      string remote = "unknown";

      try
      {
        try
        {
          ctx = _listener.EndGetContext(asyncResult);
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
          return;
        }
        catch (HttpListenerException) when (IsDisposed)
        {
          return;
        }

        // Always arm the next accept before processing this request. BeginListening catches and
        // retries its own failures so an accept error cannot escape this ThreadPool callback.
        BeginListening();

        var req = ctx.Request;
        var resp = ctx.Response;
        method = req.HttpMethod ?? "unknown";
        path = req.Url?.AbsolutePath ?? "unknown";
        remote = req.RemoteEndPoint?.ToString() ?? "unknown";
        resp.Headers["X-FMS-Insight-Request-ID"] = requestId.ToString();
        Serilog.Log.Debug(
          "Started Mazak proxy request {requestId}: {method} {path} from {remote}; {activeRequests} active requests",
          requestId,
          method,
          path,
          remote,
          activeRequests
        );

        if (req.HttpMethod == "GET")
        {
          if (_loadingHandlers.TryGetValue(req.Url.AbsolutePath, out var handler))
          {
            var result = handler();

            resp.StatusCode = 200;
            resp.ContentType = "application/json";
            resp.ContentEncoding = System.Text.Encoding.UTF8;
            var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(
              result.GetType()
            );
            serializer.WriteObject(resp.OutputStream, result);
          }
          else
          {
            resp.StatusCode = 404;
          }
        }
        else if (req.HttpMethod == "POST")
        {
          if (_postHandlers.TryGetValue(req.Url.AbsolutePath, out var handler))
          {
            var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(
              handler.BodyType
            );
            var body = serializer.ReadObject(req.InputStream);
            req.InputStream.Close();

            var result = handler.Handler(body);

            resp.StatusCode = 200;
            resp.ContentType = "application/json";
            resp.ContentEncoding = System.Text.Encoding.UTF8;
            serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(
              result.GetType()
            );
            serializer.WriteObject(resp.OutputStream, result);
          }
          else
          {
            resp.StatusCode = 404;
          }
        }
        else
        {
          resp.StatusCode = 405;
        }
      }
      catch (Exception ex)
      {
        if (ctx == null && !IsDisposed)
          BeginListening();
        // Log before touching the response. A disconnected client can make even the error response
        // fail, and that secondary failure must not hide the original exception or terminate the
        // service.
        Serilog.Log.Error(
          ex,
          $"Error handling Mazak proxy request {requestId}: {method} {path} from {remote}"
        );
        TryWriteErrorResponse(ctx, requestId);
      }
      finally
      {
        var statusCode = TryGetStatusCode(ctx);
        TryCloseResponse(ctx, requestId);
        timer.Stop();
        var remainingRequests = Interlocked.Decrement(ref _activeRequests);
        if (ctx != null)
          Serilog.Log.Debug(
            "Finished Mazak proxy request {requestId}: {method} {path} with status {statusCode} in {elapsedMilliseconds}ms; {activeRequests} active requests remain",
            requestId,
            method,
            path,
            statusCode,
            timer.ElapsedMilliseconds,
            remainingRequests
          );
      }
    }

    private static int TryGetStatusCode(HttpListenerContext ctx)
    {
      if (ctx == null)
        return 0;
      try
      {
        return ctx.Response.StatusCode;
      }
      catch
      {
        return 0;
      }
    }

    private static void TryWriteErrorResponse(HttpListenerContext ctx, long requestId)
    {
      if (ctx == null)
        return;
      try
      {
        var buffer = System.Text.Encoding.UTF8.GetBytes(
          $"Mazak proxy request {requestId} failed. See the proxy diagnostics for details."
        );
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "text/plain";
        ctx.Response.ContentEncoding = System.Text.Encoding.UTF8;
        ctx.Response.ContentLength64 = buffer.Length;
        ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
      }
      catch (Exception responseException)
      {
        Serilog.Log.Debug(
          responseException,
          "Unable to write error response for Mazak proxy request {requestId}",
          requestId
        );
      }
    }

    private static void TryCloseResponse(HttpListenerContext ctx, long requestId)
    {
      if (ctx == null)
        return;
      try
      {
        ctx.Response.Close();
      }
      catch (Exception closeException)
      {
        Serilog.Log.Debug(
          closeException,
          "Unable to close response for Mazak proxy request {requestId}",
          requestId
        );
      }
    }
  }
}
