using System.Globalization;
using System.Net;
using System.Text.Json;

namespace AX206.Core.Data;

/// <summary>City-level location from the public IP, resolved once and shared by weather and radar.</summary>
public sealed class GeoLocator
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private GeoLocation? _cached;
    private DateTime _retryAt;

    public async Task<GeoLocation?> GetAsync(CancellationToken ct)
    {
        if (_cached is not null) return _cached;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null || DateTime.UtcNow < _retryAt) return _cached;
            using var response = await Http.GetAsync("http://ip-api.com/json/", TimeSpan.FromSeconds(6), ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var r = doc.RootElement;
            if (r.TryGetProperty("status", out var s) && s.GetString() == "success")
                _cached = new GeoLocation(r.GetProperty("lat").GetDouble(), r.GetProperty("lon").GetDouble(),
                    r.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "");
            else
                _retryAt = DateTime.UtcNow.AddMinutes(1);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            _retryAt = DateTime.UtcNow.AddMinutes(1);
        }
        finally
        {
            _lock.Release();
        }
        return _cached;
    }
}

/// <summary>Current conditions from Open-Meteo (port of fetch_weather_sync).</summary>
public sealed class WeatherProvider(GeoLocator geo) : PollingProvider<WeatherData>(new WeatherData(false, "Loading..."))
{
    // WMO weather codes -> label + icon key
    private static readonly Dictionary<int, (string Label, string Icon)> Codes = new()
    {
        [0] = ("Clear sky", "sun"), [1] = ("Mostly clear", "sun"), [2] = ("Partly cloudy", "cloud-sun"), [3] = ("Overcast", "cloud"),
        [45] = ("Fog", "fog"), [48] = ("Fog", "fog"),
        [51] = ("Light drizzle", "rain"), [53] = ("Drizzle", "rain"), [55] = ("Dense drizzle", "rain"),
        [56] = ("Freezing drizzle", "rain"), [57] = ("Freezing drizzle", "rain"),
        [61] = ("Light rain", "rain"), [63] = ("Rain", "rain"), [65] = ("Heavy rain", "rain"),
        [66] = ("Freezing rain", "rain"), [67] = ("Freezing rain", "rain"),
        [71] = ("Light snow", "snow"), [73] = ("Snow", "snow"), [75] = ("Heavy snow", "snow"), [77] = ("Snow grains", "snow"),
        [80] = ("Rain showers", "rain"), [81] = ("Rain showers", "rain"), [82] = ("Violent showers", "rain"),
        [85] = ("Snow showers", "snow"), [86] = ("Snow showers", "snow"),
        [95] = ("Thunderstorm", "storm"), [96] = ("Thunderstorm", "storm"), [99] = ("Thunderstorm", "storm")
    };

    // Retry quickly while failing, then settle to the 15-minute cadence.
    protected override TimeSpan Interval => Current.Ok ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(1);

    protected override async Task PollAsync(CancellationToken ct)
    {
        var loc = await geo.GetAsync(ct);
        if (loc is null)
        {
            Current = new WeatherData(false, "Could not determine location");
            return;
        }

        try
        {
            string url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={loc.Lat}&longitude={loc.Lon}" +
                $"&current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code,wind_speed_10m" +
                $"&daily=temperature_2m_max,temperature_2m_min&timezone=auto");
            using var response = await Http.GetAsync(url, TimeSpan.FromSeconds(8), ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Current = new WeatherData(false, $"HTTP {(int)response.StatusCode}");
                return;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var cur = doc.RootElement.GetProperty("current");
            var daily = doc.RootElement.TryGetProperty("daily", out var d) ? d : default;
            int code = cur.TryGetProperty("weather_code", out var wc) && wc.TryGetInt32(out int c) ? c : -1;
            var (label, icon) = Codes.GetValueOrDefault(code, ("Unknown", "cloud"));

            Current = new WeatherData(true, null,
                loc.City,
                Num(cur, "temperature_2m"),
                Num(cur, "apparent_temperature"),
                Num(cur, "relative_humidity_2m"),
                Num(cur, "wind_speed_10m"),
                !cur.TryGetProperty("is_day", out var day) || day.GetInt32() != 0,
                label,
                icon,
                FirstOf(daily, "temperature_2m_max"),
                FirstOf(daily, "temperature_2m_min"));
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Current = new WeatherData(false, Http.Reason(e));
        }
    }

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static double? FirstOf(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var arr) &&
        arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0 &&
        arr[0].ValueKind == JsonValueKind.Number
            ? arr[0].GetDouble()
            : null;
}
