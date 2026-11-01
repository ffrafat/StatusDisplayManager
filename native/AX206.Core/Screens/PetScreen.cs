using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>
/// Port of renderPixelPet(): "Byte", a slime whose mood follows CPU/RAM load. Needs no data
/// beyond the stats sampled every second, so it is always available.
/// </summary>
public sealed class PetScreen : IScreen
{
    private static readonly SKColor Face = SKColor.Parse("#0d1420");

    public string Id => "pet";
    public string Title => "Pixel Pet";

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        canvas.Clear(SKColor.Parse("#101425"));

        double cpu = context.Stats?.CpuPercent ?? 0, ram = context.Stats?.RamPercent ?? 0;
        double load = Math.Max(cpu, ram);
        (string mood, SKColor color, string caption) = load > 80 ? ("stressed", SKColor.Parse("#f87171"), "Byte is overworked!")
            : load > 50 ? ("neutral", SKColor.Parse("#60a5fa"), "Byte is focused")
            : ("happy", SKColor.Parse("#34d399"), "Byte is chillin’");

        double t = context.UnixSeconds;
        bool stressed = mood == "stressed";
        float bounce = (float)(stressed ? Math.Sin(t * 14) * 4 : Math.Sin(t * 1.6) * 10);
        float jitter = stressed ? (float)(Math.Sin(t * 30) * 3) : 0;
        const float cx0 = 480, r = 130;
        float cx = cx0 + jitter, cy = 300 + bounce;

        using (var glow = new SKPaint { IsAntialias = true })
        {
            glow.Shader = SKShader.CreateRadialGradient(new SKPoint(cx0, cy), r * 2.2f,
                [color.WithAlpha(0x33), color.WithAlpha(0)], [0.3f / 2.2f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawCircle(cx0, cy, r * 2.2f, glow);
        }

        if (mood == "happy")
        {
            using var sparkle = Rendering.Draw.Fill(SKColors.White);
            for (int i = 0; i < 6; i++)
            {
                double a = i / 6.0 * Math.PI * 2 + t * 0.5, dist = r * 1.5 + Math.Sin(t * 2 + i) * 14;
                sparkle.Color = SKColors.White.WithAlpha((byte)Math.Round((0.3 + 0.4 * Math.Abs(Math.Sin(t * 2 + i * 1.3))) * 255));
                canvas.DrawCircle(cx + (float)(Math.Cos(a) * dist), cy + (float)(Math.Sin(a) * dist * 0.6), 2.4f, sparkle);
            }
        }

        using (var body = new SKPaint { IsAntialias = true })
        using (var path = Body(cx, cy, r))
        {
            body.Shader = SKShader.CreateLinearGradient(new SKPoint(cx, cy - r), new SKPoint(cx, cy + r),
                [color, Shade(color, -18)], SKShaderTileMode.Clamp);
            canvas.DrawPath(path, body);
        }

        DrawFace(canvas, mood, cx, cy, r, t);

        using (var name = Fonts.Display(14, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, "B Y T E", cx0, cy - r - 34, name, SKColors.White.WithAlpha(102), HAlign.Center);
        using (var cap = Fonts.Display(30, SKFontStyleWeight.SemiBold))
            Rendering.Draw.Text(canvas, caption, cx0, cy + r + 60, cap, Palette.Ink, HAlign.Center);

        // Energy / Calm are the inverse of CPU / RAM - a full bar means low load.
        const float barW = 320, barH = 14, barGap = 46, barsY = 560, barsX = cx0 - barW / 2;
        Rendering.Draw.ProgressBar(canvas, barsX, barsY, barW, barH, 7, 100 - cpu, Palette.Track, color);
        Rendering.Draw.ProgressBar(canvas, barsX, barsY + barGap, barW, barH, 7, 100 - ram, Palette.Track, color);
        using var label = Fonts.Text(14, SKFontStyleWeight.SemiBold);
        Rendering.Draw.Text(canvas, "ENERGY", barsX, barsY - 16, label, Palette.Muted, HAlign.Left, VAlign.Middle);
        Rendering.Draw.Text(canvas, "CALM", barsX, barsY + barGap - 16, label, Palette.Muted, HAlign.Left, VAlign.Middle);
    }

    private static void DrawFace(SKCanvas canvas, string mood, float cx, float cy, float r, double t)
    {
        float eyeY = cy - r * 0.12f, eyeDx = r * 0.35f;
        // At 1 FPS a sub-second blink would almost never land on a frame; close the eyes for
        // one whole frame every 7 seconds instead.
        bool blink = (long)Math.Floor(t) % 7 == 0;

        if (mood == "stressed")
        {
            using var p = Rendering.Draw.Stroke(Face, 6, SKStrokeCap.Round);
            canvas.DrawLine(cx - eyeDx - 10, eyeY - 8, cx - eyeDx + 10, eyeY + 6, p);
            canvas.DrawLine(cx + eyeDx + 10, eyeY - 8, cx + eyeDx - 10, eyeY + 6, p);
        }
        else if (blink)
        {
            using var p = Rendering.Draw.Stroke(Face, 5, SKStrokeCap.Round);
            canvas.DrawLine(cx - eyeDx - 9, eyeY, cx - eyeDx + 9, eyeY, p);
            canvas.DrawLine(cx + eyeDx - 9, eyeY, cx + eyeDx + 9, eyeY, p);
        }
        else
        {
            using var p = Rendering.Draw.Fill(Face);
            canvas.DrawCircle(cx - eyeDx, eyeY, 9, p);
            canvas.DrawCircle(cx + eyeDx, eyeY, 9, p);
        }

        float mouthY = cy + r * 0.22f;
        using (var mouth = Rendering.Draw.Stroke(Face, 5, SKStrokeCap.Round))
        using (var path = new SKPath())
        {
            if (mood == "happy")
                path.AddArc(SKRect.Create(cx - 22, mouthY - 10 - 22, 44, 44), 27, 126);   // smile
            else if (mood == "stressed")
                path.AddArc(SKRect.Create(cx - 16, mouthY + 14 - 16, 32, 32), 207, 126);  // frown
            else
            {
                path.MoveTo(cx - 16, mouthY);
                path.LineTo(cx + 16, mouthY);
            }
            canvas.DrawPath(path, mouth);
        }

        if (mood == "stressed")
        {
            float dx = cx + r * 0.62f, dy = cy - r * 0.55f;
            using var drop = new SKPath();
            drop.MoveTo(dx, dy - 12);
            drop.QuadTo(dx + 9, dy + 4, dx, dy + 14);
            drop.QuadTo(dx - 9, dy + 4, dx, dy - 12);
            using var fill = Rendering.Draw.Fill(new SKColor(147, 197, 253, 230));
            canvas.DrawPath(drop, fill);
        }
    }

    // Slime silhouette: rounded top, scalloped bottom.
    private static SKPath Body(float cx, float cy, float r)
    {
        var path = new SKPath();
        path.MoveTo(cx - r, cy);
        path.ArcTo(SKRect.Create(cx - r, cy - r, r * 2, r * 2), 180, 180, false);
        path.LineTo(cx + r, cy + r * 0.65f);
        const int bumps = 4;
        float bumpW = 2 * r / bumps;
        for (int i = 0; i < bumps; i++)
        {
            float xStart = cx + r - i * bumpW;
            float dip = i % 2 == 0 ? r * 0.18f : -r * 0.02f;
            path.QuadTo(xStart - bumpW / 2, cy + r * 0.65f + dip, xStart - bumpW, cy + r * 0.65f);
        }
        path.LineTo(cx - r, cy);
        path.Close();
        return path;
    }

    private static SKColor Shade(SKColor c, int percent)
    {
        int d = (int)Math.Round(2.55 * percent);
        return new SKColor(
            (byte)Math.Clamp(c.Red + d, 0, 255),
            (byte)Math.Clamp(c.Green + d, 0, 255),
            (byte)Math.Clamp(c.Blue + d, 0, 255));
    }
}
