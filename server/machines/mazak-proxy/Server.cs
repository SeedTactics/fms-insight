namespace BlackMaple.FMSInsight.Mazak.Proxy
{
  using System;
  using System.Collections.Generic;
  using System.Net;
  using System.Threading;

  public interface IHttpServer : IDisposable
  {
    void AddLoadingHandler<T>(string path, Func<T> handler);
    void AddPostHandler<T, R>(string path, Func<T, R> handler);
    void Start();
  }

  public sealed class HttpServer : IDisposable, IHttpServer
  {
    private readonly HttpListener _listener;
    private Thread _listenThread = null;
    private readonly ManualResetEvent _cancel;

    private class PostHandler
    {
      public Type BodyType { get; set; }
      public Func<object, object> Handler { get; set; }
    }

    private readonly Dictionary<string, Func<object>> _loadingHandlers;
    private readonly Dictionary<string, PostHandler> _postHandlers;

    public HttpServer(string url)
    {
      _listener = new HttpListener();
      _listener.Prefixes.Add(url);
      _cancel = new ManualResetEvent(false);
      _loadingHandlers = new Dictionary<string, Func<object>>();
      _postHandlers = new Dictionary<string, PostHandler>();
    }

    public void AddLoadingHandler<T>(string path, Func<T> handler)
    {
      _loadingHandlers.Add(path, () => handler());
    }

    public void AddPostHandler<T, R>(string path, Func<T, R> handler)
    {
      _postHandlers.Add(path, new PostHandler { BodyType = typeof(T), Handler = t => handler((T)t) });
    }

    public void Start()
    {
      if (_listenThread == null)
      {
        _listener.Start();
        _listenThread = new Thread(HandleConnections);
        _listenThread.IsBackground = true;
        _listenThread.Start();
      }
    }

    public void Dispose()
    {
      if (_listenThread != null)
      {
        _cancel.Set();
        _listenThread.Join(TimeSpan.FromSeconds(5));
        _listenThread.Abort();
        _listener.Close();
      }
    }

    private void HandleRequest(IAsyncResult asyncResult)
    {
      HttpListener listener = (HttpListener)asyncResult.AsyncState;
      var ctx = listener.EndGetContext(asyncResult);
      var req = ctx.Request;
      var resp = ctx.Response;

      try
      {
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
        var buffer = System.Text.Encoding.UTF8.GetBytes(ex.ToString());
        resp.StatusCode = 500;
        resp.ContentType = "text/plain";
        resp.ContentEncoding = System.Text.Encoding.UTF8;
        resp.ContentLength64 = buffer.Length;
        resp.OutputStream.Write(buffer, 0, buffer.Length);
        Serilog.Log.Error(ex, "Error handling request");
      }
      finally
      {
        resp.Close();
      }
    }

    private void HandleConnections()
    {
      while (true)
      {
        if (_cancel.WaitOne(0))
        {
          break;
        }
        _listener.BeginGetContext(HandleRequest, _listener);
      }
    }
  }
}
