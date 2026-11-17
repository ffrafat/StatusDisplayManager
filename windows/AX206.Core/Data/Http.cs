using System.Net;
using System.Net.Http.Headers;

namespace AX206.Core.Data;

/// <summary>One shared HttpClient for all providers (adsb.fi rejects default user agents).</summary>
internal static class Http
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan // per request, via CancellationToken
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AX206-Display-Manager", "1.0"));
        return client;
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("timed out");
        }
    }

    public static Task<HttpResponseMessage> GetAsync(string url, TimeSpan timeout, CancellationToken ct) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, url), timeout, ct);

    /// <summary>Header value as a string, whether it came back on the response or its content.</summary>
    public static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault()
        : response.Content.Headers.TryGetValues(name, out var c) ? c.FirstOrDefault()
        : null;

    public static string Reason(Exception e) => e switch
    {
        TimeoutException => "timed out",
        HttpRequestException h when h.InnerException is { } inner => inner.Message,
        _ => e.Message
    };
}
