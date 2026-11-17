using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AX206.Core.Data;

/// <summary>
/// Claude plan usage, read from the unified rate-limit headers of a 1-token request
/// (port of fetch_claude_usage_sync in backend.py, including its HTTP-status check).
/// </summary>
public sealed class ClaudeUsageProvider() : PollingProvider<ClaudeUsage>(new ClaudeUsage(false, "Loading..."))
{
    private const string Probe =
        """{"model":"claude-haiku-4-5-20251001","max_tokens":1,"messages":[{"role":"user","content":"hi"}]}""";

    private volatile string? _manualToken;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(60);

    /// <summary>Token pasted in the GUI; takes priority over ~/.claude/.credentials.json.</summary>
    public void SetManualToken(string? token)
    {
        _manualToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        PollNow();
    }

    protected override async Task PollAsync(CancellationToken ct)
    {
        string? token = _manualToken ?? ReadCredentialsFile();
        if (token is null)
        {
            Current = new ClaudeUsage(false, "No Claude token. Run 'claude setup-token' and paste it in Data sources.");
            return;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(Probe, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        try
        {
            using var response = await Http.SendAsync(request, TimeSpan.FromSeconds(10), ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                // Error responses carry no rate-limit headers; reporting them as 0% would lie.
                Current = new ClaudeUsage(false, await ErrorMessageAsync(response, ct));
                return;
            }

            Current = new ClaudeUsage(
                true, null,
                Fraction(response, "anthropic-ratelimit-unified-5h-utilization") * 100,
                Fraction(response, "anthropic-ratelimit-unified-7d-utilization") * 100,
                ParseReset(Http.Header(response, "anthropic-ratelimit-unified-5h-reset")),
                ParseReset(Http.Header(response, "anthropic-ratelimit-unified-7d-reset")));
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Current = new ClaudeUsage(false, Http.Reason(e));
        }
    }

    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string message = $"HTTP {(int)response.StatusCode}";
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg) && msg.GetString() is { Length: > 0 } detail)
                message += $": {detail}";
        }
        catch (JsonException) { }
        return message;
    }

    private static double Fraction(HttpResponseMessage response, string header) =>
        double.TryParse(Http.Header(response, header), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>Unix seconds or ISO-8601, as parse_reset_ts in backend.py.</summary>
    internal static DateTimeOffset? ParseReset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
            return secs > 1_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(secs * 1000)) : null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto
            : null;
    }

    private static string? ReadCredentialsFile()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (string path in new[]
                 {
                     Path.Combine(home, ".claude", ".credentials.json"),
                     Path.Combine(localAppData, "Claude", ".credentials.json")
                 })
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                string? token = root.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(token) &&
                    root.TryGetProperty("claudeAiOauth", out var oauth) &&
                    oauth.TryGetProperty("accessToken", out var ot))
                    token = ot.GetString();
                if (!string.IsNullOrWhiteSpace(token)) return token.Trim();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return null;
    }
}
