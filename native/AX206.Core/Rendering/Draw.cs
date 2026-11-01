using SkiaSharp;

namespace AX206.Core.Rendering;

public enum HAlign { Left, Center, Right }

/// <summary>Vertical anchor, as canvas textBaseline: Top, Middle or the alphabetic baseline.</summary>
public enum VAlign { Top, Middle, Baseline }

/// <summary>Canvas-2D-style drawing helpers shared by all screens.</summary>
public static class Draw
{
    public static SKPaint Fill(SKColor color) =>
        new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };

    public static SKPaint Stroke(SKColor color, float width, SKStrokeCap cap = SKStrokeCap.Butt) =>
        new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = width,
            StrokeCap = cap,
            StrokeJoin = SKStrokeJoin.Round
        };

    public static void Text(SKCanvas canvas, string? text, float x, float y, SKFont font, SKColor color,
        HAlign h = HAlign.Left, VAlign v = VAlign.Baseline)
    {
        if (string.IsNullOrEmpty(text)) return;

        float width = font.MeasureText(text);
        float dx = h switch { HAlign.Center => -width / 2, HAlign.Right => -width, _ => 0 };

        font.GetFontMetrics(out var m); // Ascent is negative, Descent positive
        float dy = v switch
        {
            VAlign.Top => -m.Ascent,
            VAlign.Middle => -(m.Ascent + m.Descent) / 2,
            _ => 0
        };

        using var paint = Fill(color);
        canvas.DrawText(text, x + dx, y + dy, font, paint);
    }

    /// <summary>Truncates to fit maxWidth with an ellipsis, by measured width (fitText in renderer.js).</summary>
    public static string FitText(SKFont font, string? text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (font.MeasureText(text) <= maxWidth) return text;
        const string ellipsis = "…";
        if (font.MeasureText(ellipsis) > maxWidth) return "";

        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (font.MeasureText(text[..mid] + ellipsis) <= maxWidth) lo = mid;
            else hi = mid - 1;
        }
        return text[..lo] + ellipsis;
    }

    public static void Card(SKCanvas canvas, float x, float y, float w, float h, float r, SKColor color)
    {
        using var paint = Fill(color);
        canvas.DrawRoundRect(new SKRect(x, y, x + w, y + h), r, r, paint);
    }

    public static void ProgressBar(SKCanvas canvas, float x, float y, float w, float h, float r,
        double percent, SKColor track, SKColor bar)
    {
        Card(canvas, x, y, w, h, r, track);
        if (percent > 0)
            Card(canvas, x, y, w * (float)(Math.Min(100, percent) / 100), h, r, bar);
    }
}
