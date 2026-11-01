using AX206.Core.Telemetry;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>One page of the LCD rotation. Drawn onto a 960x640 canvas.</summary>
public interface IScreen
{
    string Id { get; }
    string Title { get; }
    void Draw(SKCanvas canvas, ScreenContext context);
}

/// <summary>Everything a screen may draw from, captured once per frame.</summary>
public sealed class ScreenContext
{
    public required DateTime Now { get; init; }
    public SystemStats? Stats { get; init; }
}
