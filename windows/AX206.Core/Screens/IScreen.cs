using AX206.Core.Data;
using AX206.Core.Telemetry;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>One page of the LCD rotation. Drawn onto a 960x640 canvas.</summary>
public interface IScreen
{
    string Id { get; }
    string Title { get; }
    void Draw(SKCanvas canvas, ScreenContext context);

    /// <summary>Whether the screen has something to show (it stays in rotation only while true).</summary>
    bool IsAvailable(ScreenContext context) => true;

    /// <summary>Jump straight to this screen the moment it becomes available (music starting).</summary>
    bool PreemptsWhenAvailable => false;
}

/// <summary>Everything a screen may draw from, captured once per frame.</summary>
public sealed class ScreenContext
{
    public required DateTime Now { get; init; }
    public SystemStats? Stats { get; init; }
    public MediaInfo? Media { get; init; }
    public ClaudeUsage? Claude { get; init; }
    public AntigravityUsage? Antigravity { get; init; }
    public BanglaGovData? Bangla { get; init; }
    public WeatherData? Weather { get; init; }
    public RadarData? Radar { get; init; }
    public RadarConfig RadarConfig { get; init; } = new();

    /// <summary>Wall-clock seconds, for animation phases and data timestamps.</summary>
    public double UnixSeconds => new DateTimeOffset(Now).ToUnixTimeMilliseconds() / 1000.0;
}
