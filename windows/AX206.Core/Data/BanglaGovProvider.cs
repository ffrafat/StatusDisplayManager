using System.Net;
using System.Text.Json;

namespace AX206.Core.Data;

/// <summary>Download counts for three bangla.gov.bd tools (port of fetch_bangla_gov_sync).</summary>
public sealed class BanglaGovProvider() : PollingProvider<BanglaGovData>(new BanglaGovData(false, "Loading...", []))
{
    // Product icons ship in Assets/; the Python backend downloaded them on first run.
    private static readonly (string Id, string Icon)[] Products =
    [
        ("02b6b237-2875-44a4-80ea-e720f8d7d488", "product_purno.png"),
        ("904bfa8b-5dd4-4f9b-b0dc-568d381717af", "product_sothik.png"),
        ("602a728d-b718-47ee-906f-d153f05d99fc", "product_banglaword.png")
    ];

    protected override TimeSpan Interval => TimeSpan.FromSeconds(60);

    protected override async Task PollAsync(CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync("https://bangla.gov.bd/api/bangla-gov-bd/bangla-gov-bd/", TimeSpan.FromSeconds(8), ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Current = new BanglaGovData(false, $"HTTP {(int)response.StatusCode}", []);
                return;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var items = doc.RootElement.EnumerateArray().ToList();
            var tools = new List<BanglaTool>();
            foreach (var (id, icon) in Products)
            {
                var match = items.FirstOrDefault(i => i.TryGetProperty("id", out var v) && v.GetString() == id);
                if (match.ValueKind != JsonValueKind.Object) continue;
                tools.Add(new BanglaTool(id, Str(match, "title_en"), Str(match, "version"), Str(match, "type_en"),
                    match.TryGetProperty("downloaded_file_count", out var d) && d.TryGetInt64(out long n) ? n : 0,
                    icon));
            }
            Current = new BanglaGovData(true, null, tools);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Current = new BanglaGovData(false, Http.Reason(e), []);
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
