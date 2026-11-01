using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace AX206.Core.Data;

/// <summary>
/// Model quotas from a running Antigravity IDE: finds its language_server process, takes the
/// CSRF token from its command line, and asks its local HTTP API (port of fetch_ag_usage_sync).
/// </summary>
public sealed partial class AntigravityProvider()
    : PollingProvider<AntigravityUsage>(new AntigravityUsage(false, "Loading...", []))
{
    private const string StatusPath = "/exa.language_server_pb.LanguageServerService/GetUserStatus";
    private const string StatusBody =
        """{"metadata":{"ideName":"antigravity","extensionName":"antigravity","locale":"en"}}""";

    private static readonly (string Label, SKColor Color, string[] Keywords)[] Groups =
    [
        ("Gemini Flash", new SKColor(64, 196, 255), ["gemini", "flash"]),
        ("Gemini Pro", new SKColor(105, 240, 174), ["gemini", "pro"]),
        ("Claude", new SKColor(255, 171, 64), ["claude"])
    ];

    private sealed record Server(int Port, string Csrf);
    private Server? _server;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    [GeneratedRegex(@"--csrf_token\s+(\S+)")]
    private static partial Regex CsrfToken();

    protected override async Task PollAsync(CancellationToken ct)
    {
        _server ??= await FindServerAsync(ct);
        if (_server is null)
        {
            Current = new AntigravityUsage(false, "Antigravity not running", []);
            return;
        }

        try
        {
            using var response = await QueryAsync(_server, TimeSpan.FromSeconds(5), ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                _server = null;
                Current = new AntigravityUsage(false, $"HTTP {(int)response.StatusCode}", []);
                return;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            Current = new AntigravityUsage(true, null, ParseGroups(doc.RootElement));
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _server = null; // the IDE may have restarted on a new port
            Current = new AntigravityUsage(false, Http.Reason(e), []);
        }
    }

    private static List<AntigravityGroup> ParseGroups(JsonElement root)
    {
        var models = new List<(string Label, double Remaining, string? Reset)>();
        if (root.TryGetProperty("userStatus", out var us) &&
            us.TryGetProperty("cascadeModelConfigData", out var cfg) &&
            cfg.TryGetProperty("clientModelConfigs", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in list.EnumerateArray())
            {
                string label = m.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                if (!m.TryGetProperty("quotaInfo", out var q) ||
                    !q.TryGetProperty("remainingFraction", out var rf) || rf.ValueKind != JsonValueKind.Number)
                    continue;
                string? reset = q.TryGetProperty("resetTime", out var rt) ? rt.GetString() : null;
                models.Add((label.ToLowerInvariant(), rf.GetDouble(), reset));
            }
        }

        var groups = new List<AntigravityGroup>();
        foreach (var (label, color, keywords) in Groups)
        {
            var matches = models.Where(m => keywords.All(k => m.Label.Contains(k))).ToList();
            if (matches.Count == 0) continue;
            var worst = matches.MinBy(m => m.Remaining);
            groups.Add(new AntigravityGroup(label, color, worst.Remaining * 100, ClaudeUsageProvider.ParseReset(worst.Reset)));
        }
        return groups;
    }

    private static async Task<Server?> FindServerAsync(CancellationToken ct)
    {
        foreach (var (pid, csrf) in LanguageServers())
        {
            foreach (int port in ListeningPorts(pid))
            {
                var candidate = new Server(port, csrf);
                try
                {
                    using var response = await QueryAsync(candidate, TimeSpan.FromSeconds(2), ct);
                    if (response.StatusCode == HttpStatusCode.OK) return candidate;
                }
                catch (Exception) when (!ct.IsCancellationRequested) { }
            }
        }
        return null;
    }

    private static Task<HttpResponseMessage> QueryAsync(Server server, TimeSpan timeout, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{server.Port}{StatusPath}")
        {
            Content = new StringContent(StatusBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Codeium-Csrf-Token", server.Csrf);
        request.Headers.Add("Connect-Protocol-Version", "1");
        return Http.SendAsync(request, timeout, ct);
    }

    // .NET can't read another process's command line; WMI can.
    private static List<(int Pid, string Csrf)> LanguageServers()
    {
        var found = new List<(int, string)>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE '%language_server%' OR CommandLine LIKE '%language_server%'");
        foreach (var o in searcher.Get())
        {
            using (o)
            {
                var match = CsrfToken().Match(o["CommandLine"] as string ?? "");
                if (match.Success) found.Add((Convert.ToInt32(o["ProcessId"]), match.Groups[1].Value));
            }
        }
        return found;
    }

    private static List<int> ListeningPorts(int pid)
    {
        const int afInet = 2, tcpTableOwnerPidListener = 3, rowSize = 24;
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, afInet, tcpTableOwnerPidListener, 0);
        IntPtr table = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(table, ref size, false, afInet, tcpTableOwnerPidListener, 0) != 0) return [];
            var ports = new List<int>();
            int rows = Marshal.ReadInt32(table);
            for (int i = 0; i < rows; i++)
            {
                IntPtr row = table + 4 + i * rowSize; // MIB_TCPROW_OWNER_PID
                if (Marshal.ReadInt32(row, 20) != pid) continue;
                int raw = Marshal.ReadInt32(row, 8);  // port, network byte order in the low word
                ports.Add(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));
            }
            return ports;
        }
        finally
        {
            Marshal.FreeHGlobal(table);
        }
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int ipVersion, int tableClass, uint reserved);
}
