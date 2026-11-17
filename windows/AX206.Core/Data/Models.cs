using SkiaSharp;

namespace AX206.Core.Data;

/// <summary>Now-playing info from Windows' media session (GSMTC).</summary>
public sealed record MediaInfo(string Title, string Artist, string Album, string Status, SKImage? Artwork)
{
    public bool IsPlaying => Status == "Playing";
    public bool IsActive => Status is "Playing" or "Paused";
}

public sealed record ClaudeUsage(
    bool Ok,
    string? Error,
    double SessionPercent = 0,
    double WeeklyPercent = 0,
    DateTimeOffset? SessionReset = null,
    DateTimeOffset? WeeklyReset = null);

public sealed record AntigravityGroup(string Label, SKColor Color, double RemainingPercent, DateTimeOffset? Reset);

public sealed record AntigravityUsage(bool Available, string? Error, IReadOnlyList<AntigravityGroup> Groups);

public sealed record BanglaTool(string Id, string TitleEn, string Version, string TypeEn, long Downloads, string IconFile);

public sealed record BanglaGovData(bool Ok, string? Error, IReadOnlyList<BanglaTool> Tools);

public sealed record WeatherData(
    bool Ok,
    string? Error,
    string City = "",
    double Temp = 0,
    double FeelsLike = 0,
    double Humidity = 0,
    double WindKmh = 0,
    bool IsDay = true,
    string Label = "",
    string Icon = "sun",
    double? TempMax = null,
    double? TempMin = null);

public sealed record GeoLocation(double Lat, double Lon, string City);

/// <summary>One aircraft from an ADS-B feed. PosTs is local-clock unix seconds of the position.</summary>
public sealed record Aircraft(
    string Hex,
    string Callsign,
    double Lat,
    double Lon,
    double? AltFt,
    bool OnGround,
    double? GsKt,
    double? Track,
    double PosTs,
    string Source = "",
    double DistKm = 0,
    double Bearing = 0);

public sealed record Airport(string Icao, string Iata, string Name, double Lat, double Lon, double DistKm = 0, double Bearing = 0);

public sealed record RadarHome(double Lat, double Lon, string Label);

public sealed record RadarData(
    bool Ok,
    string? Error,
    RadarHome? Home = null,
    double RangeKm = RadarConfig.MaxRangeKm,
    double FetchedAt = 0,
    string Source = "",
    int InRange = 0,
    double? LastSeenTs = null,
    IReadOnlyList<Aircraft>? Aircraft = null,
    Airport? Airport = null);

public sealed record RadarConfig(
    bool Enabled = true,
    double? Lat = null,
    double? Lon = null,
    double RangeKm = RadarConfig.MaxRangeKm,
    string Source = "auto",
    string Airport = "")
{
    /// <summary>Scope radius limits, in km. The scope is for traffic overhead, not regional.</summary>
    public const double MinRangeKm = 2, MaxRangeKm = 20;
}
