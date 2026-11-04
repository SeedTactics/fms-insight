using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BlackMaple.FMSInsight.Cli;

internal static class InsightHttp
{
  public static HttpClient Create(string server)
  {
    if (
      !Uri.TryCreate(server, UriKind.Absolute, out var source)
      || source.Scheme is not ("http" or "https")
      || source.UserInfo.Length != 0
      || source.Query.Length != 0
      || source.Fragment.Length != 0
    )
      throw new ArgumentException(
        "--server must be an HTTP(S) base URL without credentials, query or fragment."
      );
    var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    {
      BaseAddress = new Uri(source.AbsoluteUri.TrimEnd('/') + "/"),
      Timeout = TimeSpan.FromMinutes(5),
    };
    var token = Environment.GetEnvironmentVariable("INSIGHT_TOKEN");
    if (!string.IsNullOrEmpty(token))
    {
      if (token.Any(char.IsWhiteSpace))
      {
        client.Dispose();
        throw new ArgumentException("INSIGHT_TOKEN must be a bearer token without whitespace.");
      }
      client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
    client.DefaultRequestHeaders.Accept.Add(
      new MediaTypeWithQualityHeaderValue("application/json")
    );
    return client;
  }

  public static async Task<JsonDocument> Get(
    HttpClient client,
    string path,
    CancellationToken cancellationToken
  ) =>
    (await Send(client, new HttpRequestMessage(HttpMethod.Get, path), cancellationToken)).Document!;

  public static async Task<JsonDocument> History(
    HttpClient client,
    string path,
    ImmutableHashSet<string> knownSchedules,
    CancellationToken cancellationToken
  ) =>
    (
      await Send(
        client,
        new HttpRequestMessage(HttpMethod.Post, path)
        {
          Content = JsonContent.Create(
            knownSchedules.Order(StringComparer.Ordinal).ToImmutableList()
          ),
        },
        cancellationToken
      )
    ).Document!;

  public static Task<(HttpStatusCode Status, JsonDocument? Document)> Job(
    HttpClient client,
    string unique,
    CancellationToken cancellationToken
  ) =>
    Send(
      client,
      new HttpRequestMessage(
        HttpMethod.Get,
        $"api/v1/jobs/job/{Uri.EscapeDataString(unique)}/plan"
      ),
      cancellationToken,
      allowMissing: true
    );

  private static async Task<(HttpStatusCode Status, JsonDocument? Document)> Send(
    HttpClient client,
    HttpRequestMessage message,
    CancellationToken cancellationToken,
    bool allowMissing = false
  )
  {
    using var request = message;
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(client.Timeout);
    using var response = await client.SendAsync(
      request,
      HttpCompletionOption.ResponseHeadersRead,
      timeout.Token
    );
    if (allowMissing && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
      return (response.StatusCode, null);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
    return (
      response.StatusCode,
      await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
    );
  }

  internal static string Time(DateTime time) =>
    Utc(time).ToString("O", System.Globalization.CultureInfo.InvariantCulture);

  internal static string QueryTime(DateTime time) =>
    Utc(time)
      .ToString(
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        System.Globalization.CultureInfo.InvariantCulture
      );

  private static DateTime Utc(DateTime time) =>
    DateTime.SpecifyKind(
      time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : time,
      DateTimeKind.Utc
    );
}
