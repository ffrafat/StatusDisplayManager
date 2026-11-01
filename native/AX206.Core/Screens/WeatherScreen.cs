using AX206.Core.Data;
using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>Port of renderWeather(): an animated sky scene with temperature and stat chips.</summary>
public sealed class WeatherScreen : IScreen
{
    private static readonly SKColor White = SKColors.White;

    public string Id => "weather";
    public string Title => "Weather";
    public bool IsAvailable(ScreenContext context) => context.Weather?.Ok == true;

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        var w = context.Weather;
        double t = context.UnixSeconds;
        string icon = w?.Icon ?? "sun";
        bool isDay = w?.IsDay ?? true;

        Sky(canvas, isDay, icon, t);

        if (w is not { Ok: true })
        {
            Rendering.Draw.Message(canvas, w?.Error ?? "Finding your location...", 320, White.WithAlpha(217), 30);
            return;
        }

        WeatherIcon(canvas, icon, 250, 300, 260, isDay, t);

        const float tx = 560;
        using (var f = Fonts.Display(26, SKFontStyleWeight.SemiBold))
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(f, w.City.Length > 0 ? w.City : "Your Location", 340), tx, 80, f, White.WithAlpha(191));
        using (var f = Fonts.Display(150, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, $"{Format.Whole(w.Temp)}°", tx, 235, f, White);
        using (var f = Fonts.Text(30))
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(f, w.Label, 340), tx, 280, f, White.WithAlpha(230));
        if (w.TempMax is double hi && w.TempMin is double lo)
        {
            using var f = Fonts.Display(24, SKFontStyleWeight.Medium);
            Rendering.Draw.Text(canvas, $"H:{Format.Whole(hi)}°   L:{Format.Whole(lo)}°", tx, 320, f, White.WithAlpha(166));
        }

        // Glass chips: feels-like / humidity / wind
        (Action<SKCanvas, float, float, float, SKColor> Icon, string Value, string Label)[] chips =
        [
            (Thermometer, $"{Format.Whole(w.FeelsLike)}°", "Feels like"),
            (Droplet, $"{Format.Whole(w.Humidity)}%", "Humidity"),
            (Wind, $"{Format.Whole(w.WindKmh)} km/h", "Wind")
        ];
        const float chipW = 270, chipH = 76, gap = 20, chipY = 540;
        float startX = 480 - (chipW * 3 + gap * 2) / 2;
        using var valueFont = Fonts.Display(24, SKFontStyleWeight.Bold);
        using var labelFont = Fonts.Text(15, SKFontStyleWeight.Light);
        for (int i = 0; i < chips.Length; i++)
        {
            float x = startX + i * (chipW + gap), cy = chipY + chipH / 2;
            Rendering.Draw.Card(canvas, x, chipY, chipW, chipH, 18, White.WithAlpha(36));
            chips[i].Icon(canvas, x + 40, cy, 26, White);
            Rendering.Draw.Text(canvas, chips[i].Value, x + 72, cy - 10, valueFont, White, HAlign.Left, VAlign.Middle);
            Rendering.Draw.Text(canvas, chips[i].Label, x + 72, cy + 14, labelFont, White.WithAlpha(166), HAlign.Left, VAlign.Middle);
        }
    }

    // ---- sky -----------------------------------------------------------------------------

    private static void Sky(SKCanvas canvas, bool isDay, string icon, double t)
    {
        bool stormy = icon is "rain" or "storm";
        bool overcast = icon is "cloud" or "fog";
        (string top, string bottom) = isDay
            ? stormy ? ("#3d4f68", "#63748c")
            : overcast ? ("#6c8199", "#a9bccb")
            : icon == "snow" ? ("#7fa4c2", "#d7e8f3")
            : ("#2e8fe0", "#8fd3ff")
            : stormy || overcast ? ("#0c1018", "#1e2434") : ("#080c1e", "#1b2a4d");

        using var sky = new SKPaint();
        sky.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 640),
            [SKColor.Parse(top), SKColor.Parse(bottom)], SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, 960, 640, sky);

        if (isDay) return;
        using var star = Rendering.Draw.Fill(White);
        for (int i = 0; i < 40; i++)
        {
            float sx = (float)(i * 137.5 % 960), sy = (float)(i * 71.3 % 420);
            star.Color = White.WithAlpha(Alpha(0.15 + 0.35 * Math.Abs(Math.Sin(t * 0.6 + i))));
            canvas.DrawCircle(sx, sy, i % 3 == 0 ? 1.6f : 1f, star);
        }
    }

    private static void WeatherIcon(SKCanvas canvas, string icon, float cx, float cy, float size, bool isDay, double t)
    {
        var cloud = isDay ? White.WithAlpha(235) : new SKColor(200, 210, 230, 217);
        switch (icon)
        {
            case "sun":
                if (isDay) Sun(canvas, cx, cy, size, t); else Moon(canvas, cx, cy, size, t);
                break;
            case "cloud-sun":
                if (isDay) Sun(canvas, cx - size * 0.22f, cy - size * 0.18f, size * 0.75f, t);
                else Moon(canvas, cx - size * 0.22f, cy - size * 0.18f, size * 0.75f, t);
                Cloud(canvas, cx + size * 0.08f, cy + size * 0.12f, size / 120, cloud);
                break;
            case "fog":
                Cloud(canvas, cx, cy - size * 0.12f, size / 130, cloud);
                using (var band = Rendering.Draw.Stroke(White.WithAlpha(115), 6, SKStrokeCap.Round))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float y = cy + size * 0.2f + i * 16, off = (float)(Math.Sin(t * 0.8 + i) * 10);
                        canvas.DrawLine(cx - size * 0.32f + off, y, cx + size * 0.32f + off, y, band);
                    }
                }
                break;
            case "rain":
                Cloud(canvas, cx, cy - size * 0.15f, size / 110, cloud);
                Rain(canvas, cx, cy + size * 0.1f, size * 0.8f, t, 7);
                break;
            case "snow":
                Cloud(canvas, cx, cy - size * 0.15f, size / 110, cloud);
                Snow(canvas, cx, cy + size * 0.1f, size * 0.8f, t);
                break;
            case "storm":
                Cloud(canvas, cx, cy - size * 0.2f, size / 110, new SKColor(150, 160, 180, 235));
                Rain(canvas, cx, cy + size * 0.05f, size * 0.7f, t, 5);
                Lightning(canvas, cx - 10, cy - size * 0.05f, t);
                break;
            default:
                Cloud(canvas, cx, cy, size / 110, cloud);
                break;
        }
    }

    private static void Sun(SKCanvas canvas, float cx, float cy, float size, double t)
    {
        float r = size * 0.32f;
        using (var glow = new SKPaint { IsAntialias = true })
        {
            glow.Shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r * 2.2f,
                [new SKColor(255, 214, 102, 140), new SKColor(255, 214, 102, 0)], [0.2f / 2.2f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawCircle(cx, cy, r * 2.2f, glow);
        }

        using (var ray = Rendering.Draw.Stroke(new SKColor(255, 224, 130, 230), size * 0.035f, SKStrokeCap.Round))
        {
            for (int i = 0; i < 10; i++)
            {
                double a = i / 10.0 * Math.PI * 2 + t * 0.15;
                float r0 = r * 1.15f, r1 = r * 1.15f + r * 0.55f;
                canvas.DrawLine(cx + (float)Math.Cos(a) * r0, cy + (float)Math.Sin(a) * r0,
                    cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1, ray);
            }
        }

        using var core = new SKPaint { IsAntialias = true };
        core.Shader = SKShader.CreateTwoPointConicalGradient(new SKPoint(cx - r * 0.25f, cy - r * 0.25f), r * 0.1f,
            new SKPoint(cx, cy), r, [SKColor.Parse("#fff6d8"), SKColor.Parse("#ffb238")], SKShaderTileMode.Clamp);
        canvas.DrawCircle(cx, cy, r, core);
    }

    private static void Moon(SKCanvas canvas, float cx, float cy, float size, double t)
    {
        float r = size * 0.3f;
        using (var glow = new SKPaint { IsAntialias = true })
        {
            glow.Shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r * 2,
                [new SKColor(220, 230, 255, 89), new SKColor(220, 230, 255, 0)], [0.15f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawCircle(cx, cy, r * 2, glow);
        }

        // The crescent is a real shape (disc minus offset disc). renderer.js erased the bite
        // with destination-out, which also erased the sky behind it, leaving a black hole.
        using var disc = new SKPath();
        disc.AddCircle(cx, cy, r);
        using var bite = new SKPath();
        bite.AddCircle(cx + r * 0.45f, cy - r * 0.25f, r * 0.85f);
        using var crescent = disc.Op(bite, SKPathOp.Difference);
        using (var fill = Rendering.Draw.Fill(SKColor.Parse("#eef2ff")))
            if (crescent is not null) canvas.DrawPath(crescent, fill);

        using var star = Rendering.Draw.Fill(White);
        for (int i = 0; i < 6; i++)
        {
            double a = i * 137.5 * Math.PI / 180, dist = r * (1.8 + i % 3 * 0.5);
            star.Color = White.WithAlpha(Alpha(0.4 + 0.5 * Math.Abs(Math.Sin(t * 1.3 + i))));
            canvas.DrawCircle(cx + (float)(Math.Cos(a) * dist), cy + (float)(Math.Sin(a) * dist * 0.6) - r * 0.5f, 2.2f, star);
        }
    }

    // Fluffy cloud: overlapping ovals in one path, filled as their union.
    private static void Cloud(SKCanvas canvas, float cx, float cy, float s, SKColor color)
    {
        using var path = new SKPath();
        void Oval(float x, float y, float rx, float ry) => path.AddOval(SKRect.Create(x - rx, y - ry, rx * 2, ry * 2));
        Oval(cx - 38 * s, cy + 6 * s, 30 * s, 22 * s);
        Oval(cx + 10 * s, cy - 14 * s, 40 * s, 32 * s);
        Oval(cx + 46 * s, cy + 8 * s, 28 * s, 20 * s);
        Oval(cx, cy + 16 * s, 50 * s, 18 * s);
        using var fill = Rendering.Draw.Fill(color);
        canvas.DrawPath(path, fill);
    }

    private static void Rain(SKCanvas canvas, float cx, float cy, float size, double t, int count)
    {
        using var p = Rendering.Draw.Stroke(new SKColor(150, 200, 255, 217), 3, SKStrokeCap.Round);
        for (int i = 0; i < count; i++)
        {
            float x = cx - size * 0.5f + (i * 97 % 53) / 53f * size;
            double speed = 90 + i % 3 * 20;
            float y = cy - size * 0.1f + (float)((t * speed + i * 40) % (size * 0.7));
            canvas.DrawLine(x, y, x - 4, y + 14, p);
        }
    }

    private static void Snow(SKCanvas canvas, float cx, float cy, float size, double t)
    {
        using var p = Rendering.Draw.Fill(White.WithAlpha(242));
        for (int i = 0; i < 8; i++)
        {
            float x = cx - size * 0.5f + (i * 61 % 47) / 47f * size;
            double speed = 26 + i % 3 * 10;
            float y = cy - size * 0.1f + (float)((t * speed + i * 30) % (size * 0.7));
            canvas.DrawCircle(x + (float)(Math.Sin(t * 1.5 + i) * 8), y, 3, p);
        }
    }

    private static void Lightning(SKCanvas canvas, float cx, float cy, double t)
    {
        bool flash = (long)Math.Floor(t * 2) % 5 == 0;
        using var path = new SKPath();
        path.MoveTo(cx + 6, cy - 6);
        path.LineTo(cx - 8, cy + 18);
        path.LineTo(cx + 2, cy + 18);
        path.LineTo(cx - 6, cy + 42);
        path.LineTo(cx + 14, cy + 14);
        path.LineTo(cx + 4, cy + 14);
        path.Close();
        using var fill = Rendering.Draw.Fill(flash ? SKColor.Parse("#fff27a") : new SKColor(255, 224, 110, 140));
        canvas.DrawPath(path, fill);
    }

    // ---- chip icons (24-unit box, as the other Icons) ------------------------------------

    private static void Thermometer(SKCanvas canvas, float cx, float cy, float sz, SKColor color)
    {
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.Scale(sz / 24f);
        using (var line = Rendering.Draw.Stroke(color, 2, SKStrokeCap.Round))
            canvas.DrawRoundRect(SKRect.Create(-2, -9, 4, 12), 2, 2, line);
        using (var fill = Rendering.Draw.Fill(color))
            canvas.DrawCircle(0, 5, 3, fill);
        canvas.Restore();
    }

    private static void Droplet(SKCanvas canvas, float cx, float cy, float sz, SKColor color)
    {
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.Scale(sz / 24f);
        using var path = new SKPath();
        path.MoveTo(0, -10);
        path.QuadTo(8, 4, 0, 10);
        path.QuadTo(-8, 4, 0, -10);
        path.Close();
        using var fill = Rendering.Draw.Fill(color);
        canvas.DrawPath(path, fill);
        canvas.Restore();
    }

    private static void Wind(SKCanvas canvas, float cx, float cy, float sz, SKColor color)
    {
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.Scale(sz / 24f);
        using var line = Rendering.Draw.Stroke(color, 2.2f, SKStrokeCap.Round);
        using var path = new SKPath();
        path.MoveTo(-10, -5); path.QuadTo(8, -9, 9, -3); path.QuadTo(9, 1, 4, 0);
        path.MoveTo(-10, 1); path.QuadTo(10, -3, 11, 3); path.QuadTo(11, 7, 5, 6);
        path.MoveTo(-10, 7); path.QuadTo(4, 4, 5, 9);
        canvas.DrawPath(path, line);
        canvas.Restore();
    }

    private static byte Alpha(double opacity) => (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
}
