using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>Current weather, minimal: black background, big temperature, one line-art icon.</summary>
public sealed class WeatherScreen : IScreen
{
    private static readonly SKColor Secondary = SKColor.Parse("#9aa4b8");
    private static readonly SKColor CloudInk = SKColor.Parse("#e5e9f2");
    private static readonly SKColor SunInk = SKColor.Parse("#fbbf24");
    private static readonly SKColor MoonInk = SKColor.Parse("#c7d2fe");
    private static readonly SKColor RainInk = SKColor.Parse("#60a5fa");
    private static readonly SKColor SnowInk = SKColor.Parse("#bae6fd");

    private const float Left = 56, Right = 904;

    public string Id => "weather";
    public string Title => "Weather";
    public bool IsAvailable(ScreenContext context) => context.Weather?.Ok == true;

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        canvas.Clear(SKColors.Black);

        var w = context.Weather;
        if (w is not { Ok: true })
        {
            Rendering.Draw.Message(canvas, w?.Error ?? "Finding your location...", 320, Palette.Muted);
            return;
        }

        using (var f = Fonts.Display(30, SKFontStyleWeight.SemiBold))
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(f, w.City.Length > 0 ? w.City : "Your location", 560), Left, 78, f, Palette.Ink, HAlign.Left, VAlign.Middle);
        if (w.TempMax is double hi && w.TempMin is double lo)
        {
            using var f = Fonts.Display(26, SKFontStyleWeight.Medium);
            Rendering.Draw.Text(canvas, $"H {Format.Whole(hi)}°   L {Format.Whole(lo)}°", Right, 78, f, Secondary, HAlign.Right, VAlign.Middle);
        }

        using (var f = Fonts.Display(220, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, $"{Format.Whole(w.Temp)}°", Left - 8, 335, f, Palette.Ink);
        using (var f = Fonts.Text(34))
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(f, w.Label, 460), Left, 400, f, Secondary, HAlign.Left, VAlign.Middle);

        Icon(canvas, w.Icon, w.IsDay, 712, 250, 300);

        using (var rule = Rendering.Draw.Stroke(Palette.Track, 2))
            canvas.DrawLine(Left, 470, Right, 470, rule);

        (string Label, string Value)[] stats =
        [
            ("FEELS LIKE", $"{Format.Whole(w.FeelsLike)}°"),
            ("HUMIDITY", $"{Format.Whole(w.Humidity)}%"),
            ("WIND", $"{Format.Whole(w.WindKmh)} km/h")
        ];
        float col = (Right - Left) / stats.Length;
        using var labelFont = Fonts.Text(16, SKFontStyleWeight.SemiBold);
        using var valueFont = Fonts.Display(44, SKFontStyleWeight.SemiBold);
        for (int i = 0; i < stats.Length; i++)
        {
            float x = Left + i * col;
            Rendering.Draw.Text(canvas, stats[i].Label, x, 518, labelFont, Palette.Muted, HAlign.Left, VAlign.Middle);
            Rendering.Draw.Text(canvas, stats[i].Value, x, 574, valueFont, Palette.Ink, HAlign.Left, VAlign.Middle);
        }
    }

    // ---- line icons, drawn in a 100-unit box centred on (0, 0) ------------------------------

    private const float StrokeUnits = 4.5f;

    private static void Icon(SKCanvas canvas, string icon, bool isDay, float cx, float cy, float size)
    {
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.Scale(size / 100f);
        switch (icon)
        {
            case "sun":
                if (isDay) Sun(canvas); else Moon(canvas);
                break;
            case "cloud-sun":
                canvas.Save();
                canvas.Translate(-12, -14);
                canvas.Scale(0.75f);
                if (isDay) Sun(canvas); else Moon(canvas);
                canvas.Restore();
                Cloud(canvas, 8, 8, 0.85f);
                break;
            case "rain":
                Cloud(canvas, 0, -10);
                using (var p = Rendering.Draw.Stroke(RainInk, StrokeUnits, SKStrokeCap.Round))
                    foreach (float x in new[] { -16f, 0, 16 })
                        canvas.DrawLine(x + 3, 21, x - 3, 33, p);
                break;
            case "snow":
                Cloud(canvas, 0, -10);
                using (var p = Rendering.Draw.Fill(SnowInk))
                    foreach (var (x, y) in new[] { (-16f, 22f), (0f, 26f), (16f, 22f), (-8f, 35f), (8f, 35f) })
                        canvas.DrawCircle(x, y, 3, p);
                break;
            case "storm":
                Cloud(canvas, 0, -10);
                using (var bolt = new SKPath())
                {
                    bolt.MoveTo(4, 15);
                    bolt.LineTo(-7, 30);
                    bolt.LineTo(1, 30);
                    bolt.LineTo(-4, 43);
                    bolt.LineTo(11, 25);
                    bolt.LineTo(3, 25);
                    bolt.LineTo(9, 15);
                    bolt.Close();
                    using var fill = Rendering.Draw.Fill(SunInk);
                    canvas.DrawPath(bolt, fill);
                }
                break;
            case "fog":
                Cloud(canvas, 0, -8);
                using (var p = Rendering.Draw.Stroke(CloudInk.WithAlpha(150), StrokeUnits, SKStrokeCap.Round))
                {
                    canvas.DrawLine(-30, 26, 22, 26, p);
                    canvas.DrawLine(-20, 36, 30, 36, p);
                }
                break;
            default:
                Cloud(canvas, 0, 0);
                break;
        }
        canvas.Restore();
    }

    private static void Sun(SKCanvas canvas)
    {
        using var p = Rendering.Draw.Stroke(SunInk, StrokeUnits, SKStrokeCap.Round);
        canvas.DrawCircle(0, 0, 17, p);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            canvas.DrawLine(c * 27, s * 27, c * 36, s * 36, p);
        }
    }

    private static void Moon(SKCanvas canvas)
    {
        using var disc = new SKPath();
        disc.AddCircle(0, 0, 22);
        using var bite = new SKPath();
        bite.AddCircle(11, -9, 19);
        using var crescent = disc.Op(bite, SKPathOp.Difference);
        using (var p = Rendering.Draw.Stroke(MoonInk, StrokeUnits, SKStrokeCap.Round))
            if (crescent is not null) canvas.DrawPath(crescent, p);
        using var star = Rendering.Draw.Fill(MoonInk);
        canvas.DrawCircle(27, -20, 2.5f, star);
        canvas.DrawCircle(33, 6, 1.8f, star);
    }

    /// <summary>Outline of a cloud (a base bar plus two bumps). Filled black first so it hides what's behind.</summary>
    private static void Cloud(SKCanvas canvas, float dx, float dy, float scale = 1)
    {
        using var shape = new SKPath();
        shape.AddRoundRect(new SKRect(-38, -2, 38, 22), 12, 12);
        foreach (var (x, y, r) in new[] { (-14f, -2f, 18f), (12f, -10f, 22f) })
        {
            using var bump = new SKPath();
            bump.AddCircle(x, y, r);
            using var merged = shape.Op(bump, SKPathOp.Union);
            if (merged is not null) { shape.Reset(); shape.AddPath(merged); }
        }

        canvas.Save();
        canvas.Translate(dx, dy);
        canvas.Scale(scale);
        using (var hide = Rendering.Draw.Fill(SKColors.Black)) canvas.DrawPath(shape, hide);
        using (var p = Rendering.Draw.Stroke(CloudInk, StrokeUnits / scale, SKStrokeCap.Round)) canvas.DrawPath(shape, p);
        canvas.Restore();
    }
}
