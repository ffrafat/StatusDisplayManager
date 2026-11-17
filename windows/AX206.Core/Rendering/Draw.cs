using SkiaSharp;

namespace AX206.Core.Rendering;

public enum HAlign { Left, Center, Right }

/// <summary>Vertical anchor, as canvas textBaseline: Top, Middle, Bottom or the alphabetic baseline.</summary>
public enum VAlign { Top, Middle, Bottom, Baseline }

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
            VAlign.Bottom => -m.Descent,
            _ => 0
        };

        using var paint = Fill(color);
        canvas.DrawText(text, x + dx, y + dy, font, paint);
    }

    /// <summary>Greedy word wrap; the last line is ellipsised (wrapLines in renderer.js).</summary>
    public static List<string> Wrap(SKFont font, string? text, float maxWidth, int maxLines)
    {
        string[] words = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        string line = "";
        for (int i = 0; i < words.Length; i++)
        {
            string test = line.Length > 0 ? $"{line} {words[i]}" : words[i];
            if (line.Length == 0 || font.MeasureText(test) <= maxWidth)
            {
                line = test;
                continue;
            }
            lines.Add(line);
            line = words[i];
            if (lines.Count == maxLines - 1)
            {
                line = string.Join(' ', words[i..]);
                break;
            }
        }
        if (line.Length > 0) lines.Add(line);
        return lines.Select(l => FitText(font, l, maxWidth)).ToList();
    }

    /// <summary>
    /// A screen-wide status message (loading / error), centred on y and wrapped onto up to
    /// three lines so instructions like "paste a token in Settings" aren't cut off.
    /// </summary>
    public static void Message(SKCanvas canvas, string? text, float y, SKColor color, float size = 32)
    {
        using var font = Fonts.Text(size, SKFontStyleWeight.Light);
        var lines = Wrap(font, text, 820, 3);
        float lineHeight = size * 1.35f;
        float top = y - (lines.Count - 1) * lineHeight / 2;
        for (int i = 0; i < lines.Count; i++)
            Text(canvas, lines[i], 480, top + i * lineHeight, font, color, HAlign.Center, VAlign.Middle);
    }

    /// <summary>The header row most data screens share: logo at left, centred title, a rule.</summary>
    public static void Header(SKCanvas canvas, string title, float titleY, string? logo, SKRect logoRect,
        float ruleY, SKColor ink, SKColor rule)
    {
        using (var font = Fonts.Display(36, SKFontStyleWeight.Bold))
            Text(canvas, title, 480, titleY, font, ink, HAlign.Center, VAlign.Middle);
        if (logo is not null && Assets.Image(logo) is { } image)
            canvas.DrawImage(image, logoRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var p = Stroke(rule, 4);
        canvas.DrawLine(32, ruleY, 928, ruleY, p);
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
