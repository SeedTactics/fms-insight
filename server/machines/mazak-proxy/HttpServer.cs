namespace BlackMaple.FMSInsight.Mazak.FrameworkProxy
{
  using System;
  using System.Collections.Generic;
  using System.Net;
  using System.Threading;
  using System.Threading.Tasks;

  public interface IHttpServer
  {
    void AddLoadingHandler<T>(string path, Func<Task<T>> handler);
    void AddPostHandler<T, R>(string path, Func<T, Task<R>> handler);
    void Start();
  }

  public sealed class HttpServer : IDisposable, IHttpServer
  {
    private readonly HttpListener _listener;
    private Task _lisiningTask = null;
    private readonly SemaphoreSlim _cancel;

    private class PostHandler
    {
      public Type BodyType { get; set; }
      public Func<object, Task<object>> Handler { get; set; }
    }

    private readonly Dictionary<string, Func<Task<object>>> _loadingHandlers;
    private readonly Dictionary<string, PostHandler> _postHandlers;

    public HttpServer(string url)
    {
      _listener = new HttpListener();
      _listener.Prefixes.Add(url);
      _cancel = new SemaphoreSlim(0);
      _loadingHandlers = new Dictionary<string, Func<Task<object>>>();
      _postHandlers = new Dictionary<string, PostHandler>();
    }

    public void AddLoadingHandler<T>(string path, Func<Task<T>> handler)
    {
      _loadingHandlers.Add(path, async () => await handler().ConfigureAwait(false));
    }

    public void AddPostHandler<T, R>(string path, Func<T, Task<R>> handler)
    {
      _postHandlers.Add(
        path,
        new PostHandler
        {
          BodyType = typeof(T),
          Handler = async body => await handler((T)body).ConfigureAwait(false)
        }
      );
    }

    public void Start()
    {
      if (_lisiningTask == null)
      {
        _listener.Start();
        _lisiningTask = HandleConnections();
      }
    }

    public void Dispose()
    {
      if (_lisiningTask != null)
      {
        _cancel.Release();
        _lisiningTask.GetAwaiter().GetResult();
        _listener.Close();
      }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
      var req = ctx.Request;
      var resp = ctx.Response;

      try
      {
        if (req.HttpMethod == "GET")
        {
          if (_loadingHandlers.TryGetValue(req.Url.AbsolutePath, out var handler))
          {
            var result = await handler().ConfigureAwait(false);

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

            var result = await handler.Handler(body).ConfigureAwait(false);

            resp.StatusCode = 200;
            resp.ContentType = "application/json";
            resp.ContentEncoding = System.Text.Encoding.UTF8;
            serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(result.GetType());
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
        resp.StatusCode = 500;
        Console.Error.WriteLine(ex);
      }

      resp.Close();
    }

    private async Task HandleConnections()
    {
      while (true)
      {
        var ctxTask = _listener.GetContextAsync();
        if (ctxTask != await Task.WhenAny(ctxTask, _cancel.WaitAsync()).ConfigureAwait(false))
        {
          break;
        }

        var ctx = await ctxTask.ConfigureAwait(false);
        await HandleRequest(ctx).ConfigureAwait(false);
      }
    }
  }
}
