using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>Port of renderClaudeUsage(): 5-hour and 7-day usage panels in Claude's warm palette.</summary>
public sealed class ClaudeScreen : IScreen
{
    private static readonly SKColor Panel = SKColor.Parse("#1f1f1e");
    private static readonly SKColor Ink = SKColor.Parse("#faf9f5");
    private static readonly SKColor Dim = SKColor.Parse("#b0aea5");
    private static readonly SKColor Accent = SKColor.Parse("#d97757");
    private static readonly SKColor Green = SKColor.Parse("#788c5d");
    private static readonly SKColor Red = SKColor.Parse("#c0392b");
    private static readonly SKColor Track = SKColor.Parse("#2a2a28");

    public string Id => "claude";
    public string Title => "Claude";

    // Always in rotation when enabled, so a missing/expired token shows as an error instead of vanishing.
    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        Rendering.Draw.Header(canvas, "Claude", 50, "claude-logo.png", SKRect.Create(32, 14, 72, 72), 100, Ink, Track);

        var usage = context.Claude;
        if (usage is not { Ok: true })
        {
            Rendering.Draw.Message(canvas, usage?.Error ?? "Loading credentials...", 340, Dim);
            return;
        }

        Panel_(canvas, 120, usage.SessionPercent, "Current · 5h", Format.Reset(usage.SessionReset));
        Panel_(canvas, 365, usage.WeeklyPercent, "Weekly · 7d", Format.Reset(usage.WeeklyReset));
    }

    private static void Panel_(SKCanvas canvas, float y0, double pct, string label, string reset)
    {
        const float pX = 32, pW = 896, pH = 220;
        Rendering.Draw.Card(canvas, pX, y0, pW, pH, 20, Panel);

        using (var big = Fonts.Display(64, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, $"{Format.Whole(pct)}%", pX + 40, y0 + 70, big, Ink, HAlign.Left, VAlign.Middle);

        using (var pillFont = Fonts.Display(24, SKFontStyleWeight.Bold))
        {
            float pillW = pillFont.MeasureText(label) + 40, pillH = 46;
            float pillX = pX + pW - 40 - pillW, pillY = y0 + 47;
            Rendering.Draw.Card(canvas, pillX, pillY, pillW, pillH, pillH / 2, Track);
            Rendering.Draw.Text(canvas, label, pillX + pillW / 2, pillY + pillH / 2, pillFont, Ink, HAlign.Center, VAlign.Middle);
        }

        var bar = pct >= 80 ? Red : pct >= 50 ? Accent : Green;
        Rendering.Draw.ProgressBar(canvas, pX + 40, y0 + 130, pW - 80, 20, 10, pct, Track, bar);

        using var small = Fonts.Text(20, SKFontStyleWeight.Light);
        Rendering.Draw.Text(canvas, reset, pX + 40, y0 + 185, small, Dim, HAlign.Left, VAlign.Middle);
    }
}
