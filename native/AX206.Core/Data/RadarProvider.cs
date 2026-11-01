using System.Globalization;
using System.Net;
using System.Text.Json;

namespace AX206.Core.Data;

/// <summary>
/// Nearby aircraft from volunteer ADS-B networks, no API keys (port of fetch_radar_sync).
/// adsb.lol has no published rate limit and is polled every 10 s; anonymous OpenSky gets
/// 400 credits a day, so its interval is paced from X-Rate-Limit-Remaining.
/// </summary>
public sealed class RadarProvider : PollingProvider<RadarData>
{
    private const int AdsbLolInterval = 10;
    private const int OpenSkyMinInterval = 60;
    private const double MaxAgeSec = 300;     // drop positions older than this
    private const int MaxAircraft = 25;
    private const double FetchFactor = 1.5;   // fetch beyond the ring so traffic just outside shows as rim dots

    private static readonly Dictionary<string, string> SourceNames = new()
    {
        ["adsblol"] = "adsb.lol",
        ["opensky"] = "OpenSky"
    };

    private sealed class Feed
    {
        public List<Aircraft> Aircraft = [];
        public double FetchedAt, NextPoll;
        public string? Error;
        public bool Backoff;
    }

    private sealed class BackoffException(string message, double waitSeconds) : Exception(message)
    {
        public double WaitSeconds { get; } = waitSeconds;
    }

    private readonly GeoLocator _geo;
    private readonly object _lock = new();
    private readonly Dictionary<string, Feed> _feeds = new() { ["adsblol"] = new(), ["opensky"] = new() };
    private RadarConfig _config = new();
    private double _lastSeenTs;
    private string _airportIcao = "";
    private Airport? _airport;
    private double _airportRetryAt;

    public RadarProvider(GeoLocator geo) : base(new RadarData(false, "Loading...")) => _geo = geo;

    protected override TimeSpan Interval => TimeSpan.FromSeconds(AdsbLolInterval);

    public RadarConfig Config
    {
        get { lock (_lock) return _config; }
    }

    public void SetConfig(RadarConfig config)
    {
        config = config with { RangeKm = Math.Clamp(config.RangeKm, 5, 400) };
        lock (_lock)
        {
            bool moved = _config.Lat != config.Lat || _config.Lon != config.Lon || _config.RangeKm != config.RangeKm;
            _config = config;
            foreach (var feed in _feeds.Values)
            {
                if (moved) feed.Aircraft = [];
                if (!feed.Backoff) feed.NextPoll = 0; // refetch now, but never skip an OpenSky backoff
            }
        }
        PollNow();
    }

    private static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    protected override async Task PollAsync(CancellationToken ct)
    {
        RadarConfig cfg = Config;
        if (!cfg.Enabled) return; // only hit the feeds while the radar screen is on

        var home = await HomeAsync(cfg, ct);
        if (home is null)
        {
            Current = new RadarData(false, "Could not determine location");
            return;
        }

        string[] active = cfg.Source == "auto" ? ["adsblol", "opensky"]
            : SourceNames.ContainsKey(cfg.Source) ? [cfg.Source] : ["adsblol", "opensky"];
        double fetchKm = cfg.RangeKm * FetchFactor;

        foreach (string name in active)
        {
            Feed feed;
            lock (_lock) feed = _feeds[name];
            if (Now() < feed.NextPoll) continue;
            try
            {
                var (aircraft, interval) = name == "adsblol"
                    ? await FetchAdsbLolAsync(home, fetchKm, ct)
                    : await FetchOpenSkyAsync(home, fetchKm, ct);
                lock (_lock)
                {
                    feed.Aircraft = aircraft;
                    feed.FetchedAt = Now();
                    feed.Error = null;
                    feed.Backoff = false;
                    feed.NextPoll = Now() + interval;
                }
            }
            catch (BackoffException e)
            {
                lock (_lock) { feed.Error = e.Message; feed.Backoff = true; feed.NextPoll = Now() + e.WaitSeconds; }
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                lock (_lock)
                {
                    feed.Error = Http.Reason(e);
                    feed.Backoff = false;
                    feed.NextPoll = Now() + (name == "adsblol" ? 30 : 120);
                }
            }
        }

        var airport = await ResolveAirportAsync(cfg.Airport, ct);
        lock (_lock) Current = Merge(home, cfg, active, airport);
    }

    private RadarData Merge(RadarHome home, RadarConfig cfg, string[] active, Airport? airport)
    {
        double now = Now();
        var merged = new Dictionary<string, Aircraft>();
        foreach (string name in active)
        {
            foreach (var ac in _feeds[name].Aircraft)
            {
                if (ac.OnGround || now - ac.PosTs > MaxAgeSec) continue;
                string key = ac.Hex.Length > 0 ? ac.Hex : $"{name}:{ac.Callsign}:{ac.Lat}";
                if (!merged.TryGetValue(key, out var have) || ac.PosTs > have.PosTs)
                    merged[key] = ac with { Source = SourceNames[name] };
            }
        }

        double cosHome = Math.Cos(home.Lat * Math.PI / 180);
        (double dist, double bearing) Place(double lat, double lon)
        {
            double dLat = (lat - home.Lat) * 111.0;
            double dLon = (lon - home.Lon) * 111.0 * cosHome;
            double bearing = (Math.Atan2(dLon, dLat) * 180 / Math.PI + 360) % 360; // 0 = N, 90 = E
            return (Math.Round(Math.Sqrt(dLat * dLat + dLon * dLon), 2), Math.Round(bearing, 1));
        }

        var aircraft = merged.Values
            .Select(ac => { var (d, b) = Place(ac.Lat, ac.Lon); return ac with { DistKm = d, Bearing = b }; })
            .OrderBy(ac => ac.DistKm)
            .Take(MaxAircraft)
            .ToList();

        if (airport is not null)
        {
            var (d, b) = Place(airport.Lat, airport.Lon);
            airport = airport with { DistKm = d, Bearing = b };
        }

        int inRange = aircraft.Count(a => a.DistKm <= cfg.RangeKm);
        if (inRange > 0) _lastSeenTs = now;

        var fetched = active.Select(n => _feeds[n].FetchedAt).Where(t => t > 0).ToList();
        string errors = string.Join("; ", active.Where(n => _feeds[n].Error is not null)
            .Select(n => $"{SourceNames[n]}: {_feeds[n].Error}"));
        if (fetched.Count == 0)
            return new RadarData(false, errors.Length > 0 ? errors : "No data yet");

        return new RadarData(true, errors.Length > 0 ? errors : null, home, cfg.RangeKm, fetched.Max(),
            string.Join(" + ", active.Select(n => SourceNames[n])), inRange,
            _lastSeenTs > 0 ? _lastSeenTs : null, aircraft, airport);
    }

    private async Task<RadarHome?> HomeAsync(RadarConfig cfg, CancellationToken ct)
    {
        if (cfg.Lat is double lat && cfg.Lon is double lon)
            return new RadarHome(lat, lon, "Manual location");
        var geo = await _geo.GetAsync(ct);
        return geo is null ? null : new RadarHome(geo.Lat, geo.Lon, geo.City.Length > 0 ? geo.City : "IP location");
    }

    private static async Task<(List<Aircraft>, double)> FetchAdsbLolAsync(RadarHome home, double fetchKm, CancellationToken ct)
    {
        int nm = (int)Math.Round(fetchKm / 1.852) + 1; // +1 covers the ring edge
        string url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.adsb.lol/v2/lat/{home.Lat:F4}/lon/{home.Lon:F4}/dist/{nm}");
        using var response = await Http.GetAsync(url, TimeSpan.FromSeconds(5), ct);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"adsb.lol HTTP {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        double now = Now();
        var list = new List<Aircraft>();
        if (doc.RootElement.TryGetProperty("ac", out var acs) && acs.ValueKind == JsonValueKind.Array)
        {
            foreach (var ac in acs.EnumerateArray())
            {
                if (Num(ac, "lat") is not double lat || Num(ac, "lon") is not double lon) continue;
                bool onGround = ac.TryGetProperty("alt_baro", out var alt) && alt.ValueKind == JsonValueKind.String &&
                                alt.GetString() == "ground";
                double? altFt = onGround ? 0 : Num(ac, "alt_baro");
                // Local clock, so dead-reckoning in the renderer is immune to PC clock skew
                double seen = Num(ac, "seen_pos") ?? Num(ac, "seen") ?? 0;
                list.Add(new Aircraft(
                    (Str(ac, "hex") ?? "").ToLowerInvariant(),
                    (Str(ac, "flight") ?? "").Trim(),
                    lat, lon, altFt, onGround, Num(ac, "gs"), Num(ac, "track"), now - seen));
            }
        }
        return (list, AdsbLolInterval);
    }

    private static async Task<(List<Aircraft>, double)> FetchOpenSkyAsync(RadarHome home, double fetchKm, CancellationToken ct)
    {
        double dLat = fetchKm / 111.0;
        double dLon = fetchKm / (111.0 * Math.Max(0.1, Math.Cos(home.Lat * Math.PI / 180)));
        string url = string.Create(CultureInfo.InvariantCulture,
            $"https://opensky-network.org/api/states/all?lamin={home.Lat - dLat:F4}&lomin={home.Lon - dLon:F4}" +
            $"&lamax={home.Lat + dLat:F4}&lomax={home.Lon + dLon:F4}");
        using var response = await Http.GetAsync(url, TimeSpan.FromSeconds(8), ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            double wait = double.TryParse(Http.Header(response, "X-Rate-Limit-Retry-After-Seconds"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 3600;
            throw new BackoffException("OpenSky daily limit reached", wait);
        }
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"OpenSky HTTP {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        double now = Now();
        double serverTime = Num(doc.RootElement, "time") ?? now;
        var list = new List<Aircraft>();
        if (doc.RootElement.TryGetProperty("states", out var states) && states.ValueKind == JsonValueKind.Array)
        {
            // [0] icao24 [1] callsign [3] time_position [5] lon [6] lat
            // [7] baro_altitude (m) [8] on_ground [9] velocity (m/s) [10] true_track
            foreach (var s in states.EnumerateArray())
            {
                if (s.GetArrayLength() < 11 || NumAt(s, 5) is not double lon || NumAt(s, 6) is not double lat) continue;
                double? altM = NumAt(s, 7), vel = NumAt(s, 9);
                double timePos = NumAt(s, 3) ?? serverTime;
                list.Add(new Aircraft(
                    (s[0].GetString() ?? "").ToLowerInvariant(),
                    (s[1].ValueKind == JsonValueKind.String ? s[1].GetString() ?? "" : "").Trim(),
                    lat, lon,
                    altM is double m ? Math.Round(m * 3.28084) : null,
                    s[8].ValueKind == JsonValueKind.True,
                    vel is double v ? Math.Round(v * 1.943844, 1) : null,
                    NumAt(s, 10),
                    now - Math.Max(0, serverTime - timePos)));
            }
        }
        return (list, OpenSkyInterval(response));
    }

    // Spread what is left of today's (UTC) credits over the rest of the day.
    private static double OpenSkyInterval(HttpResponseMessage response)
    {
        if (!int.TryParse(Http.Header(response, "X-Rate-Limit-Remaining"), out int remaining)) return 240;
        double secsLeft = 86400 - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 86400;
        return Math.Min(3600, Math.Max(OpenSkyMinInterval, secsLeft / Math.Max(remaining, 1)));
    }

    // adsb.lol's routes database has a keyless airport lookup; resolved once per ICAO code.
    private async Task<Airport?> ResolveAirportAsync(string icao, CancellationToken ct)
    {
        icao = new string(icao.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Take(4).ToArray());
        if (icao != _airportIcao)
        {
            _airportIcao = icao;
            _airport = null;
            _airportRetryAt = 0;
        }
        if (icao.Length == 0 || _airport is not null || Now() < _airportRetryAt) return _airport;

        try
        {
            using var response = await Http.GetAsync($"https://api.adsb.lol/api/0/airport/{icao}", TimeSpan.FromSeconds(5), ct);
            if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var r = doc.RootElement;
            _airport = new Airport(icao, Str(r, "iata") ?? "", Str(r, "name") ?? icao,
                Num(r, "lat") ?? throw new FormatException("no lat"),
                Num(r, "lon") ?? throw new FormatException("no lon"));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            _airportRetryAt = Now() + 600;
        }
        return _airport;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Numbers may arrive as JSON numbers or numeric strings (the airport API uses strings).
    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? AsNumber(v) : null;

    private static double? NumAt(JsonElement array, int index) => AsNumber(array[index]);

    private static double? AsNumber(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetDouble(),
        JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null
    };
}
