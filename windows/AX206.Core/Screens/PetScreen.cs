using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>
/// "Byte", a ginger cat whose mood follows CPU/RAM load. Needs no data beyond the stats
/// sampled every second, so it is always available.
/// </summary>
public sealed class PetScreen : IScreen
{
    private static readonly SKColor Fur = SKColor.Parse("#f4a259");
    private static readonly SKColor FurDark = SKColor.Parse("#d9773a");
    private static readonly SKColor Cream = SKColor.Parse("#fde7cf");
    private static readonly SKColor EarPink = SKColor.Parse("#f6a5b5");
    private static readonly SKColor Nose = SKColor.Parse("#f07a9b");
    private static readonly SKColor Ink = SKColor.Parse("#2a1a12");
    private static readonly SKColor Whisker = SKColor.Parse("#fff4e6");

    private enum Mood { Happy, Focused, Stressed }

    public string Id => "pet";
    public string Title => "Pixel Pet";

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        double cpu = context.Stats?.CpuPercent ?? 0, ram = context.Stats?.RamPercent ?? 0;
        double load = Math.Max(cpu, ram);
        (Mood mood, SKColor color, string caption) = load > 80 ? (Mood.Stressed, SKColor.Parse("#f87171"), "Byte is overworked!")
            : load > 50 ? (Mood.Focused, SKColor.Parse("#60a5fa"), "Byte is focused")
            : (Mood.Happy, SKColor.Parse("#34d399"), "Byte is chillin’");

        double t = context.UnixSeconds;
        float jitter = mood == Mood.Stressed ? (float)(Math.Sin(t * 30) * 3) : 0;
        float breathe = (float)Math.Sin(t * 1.6);
        const float cx0 = 480;
        float cx = cx0 + jitter;

        // Floor shadow, then the cat from back to front: tail, body, head.
        using (var shadow = Rendering.Draw.Fill(Palette.Panel))
            canvas.DrawOval(cx0, 452, 150, 16, shadow);
        Tail(canvas, cx, t, mood);
        Body(canvas, cx, breathe);
        Head(canvas, cx, 228 + breathe * 3, mood, t);

        using (var cap = Fonts.Display(30, SKFontStyleWeight.SemiBold))
            Rendering.Draw.Text(canvas, caption, cx0, 510, cap, Palette.Ink, HAlign.Center);

        // Energy / Calm are the inverse of CPU / RAM - a full bar means low load.
        const float barW = 320, barH = 14, barGap = 46, barsY = 560, barsX = cx0 - barW / 2;
        Rendering.Draw.ProgressBar(canvas, barsX, barsY, barW, barH, 7, 100 - cpu, Palette.Track, color);
        Rendering.Draw.ProgressBar(canvas, barsX, barsY + barGap, barW, barH, 7, 100 - ram, Palette.Track, color);
        using var label = Fonts.Text(14, SKFontStyleWeight.SemiBold);
        Rendering.Draw.Text(canvas, "ENERGY", barsX, barsY - 16, label, Palette.Muted, HAlign.Left, VAlign.Middle);
        Rendering.Draw.Text(canvas, "CALM", barsX, barsY + barGap - 16, label, Palette.Muted, HAlign.Left, VAlign.Middle);
    }

    private static void Tail(SKCanvas canvas, float cx, double t, Mood mood)
    {
        // Lazy sway when content, a fast flick when stressed.
        float sway = (float)(mood == Mood.Stressed ? Math.Sin(t * 9) * 18 : Math.Sin(t * 1.1) * 26);
        using var path = new SKPath();
        path.MoveTo(cx + 70, 430);
        path.CubicTo(cx + 175, 440, cx + 170, 360, cx + 150 + sway * 0.4f, 320);
        path.QuadTo(cx + 140 + sway, 280, cx + 165 + sway, 262);
        using (var fur = Rendering.Draw.Stroke(Fur, 30, SKStrokeCap.Round)) canvas.DrawPath(path, fur);

        // Darker tip
        using var tip = new SKPath();
        tip.MoveTo(cx + 146 + sway * 0.9f, 284);
        tip.QuadTo(cx + 145 + sway, 270, cx + 165 + sway, 262);
        using (var dark = Rendering.Draw.Stroke(FurDark, 30, SKStrokeCap.Round)) canvas.DrawPath(tip, dark);
    }

    private static void Body(SKCanvas canvas, float cx, float breathe)
    {
        float ry = 92 + breathe * 2;
        using (var fur = Rendering.Draw.Fill(Fur)) canvas.DrawOval(cx, 445 - ry, 108, ry, fur);
        using (var belly = Rendering.Draw.Fill(Cream)) canvas.DrawOval(cx, 382, 54, 52, belly);

        // Front paws with toe lines
        using var paw = Rendering.Draw.Fill(Cream);
        using var toe = Rendering.Draw.Stroke(FurDark.WithAlpha(150), 3, SKStrokeCap.Round);
        foreach (float px in new[] { cx - 38, cx + 38 })
        {
            canvas.DrawOval(px, 436, 30, 17, paw);
            canvas.DrawLine(px - 8, 428, px - 8, 440, toe);
            canvas.DrawLine(px + 8, 428, px + 8, 440, toe);
        }
    }

    private static void Head(SKCanvas canvas, float cx, float cy, Mood mood, double t)
    {
        // Ears flatten a little when stressed.
        float earTilt = mood == Mood.Stressed ? 16 : 0;
        foreach (int side in new[] { -1, 1 })
        {
            Ear(canvas, cx, cy, side, earTilt, Fur, 1f);
            Ear(canvas, cx, cy, side, earTilt, EarPink, 0.55f);
        }

        using (var fur = Rendering.Draw.Fill(Fur)) canvas.DrawOval(cx, cy, 116, 94, fur);

        // Forehead stripes
        using (var stripe = Rendering.Draw.Stroke(FurDark, 9, SKStrokeCap.Round))
        {
            canvas.DrawLine(cx, cy - 90, cx, cy - 62, stripe);
            canvas.DrawLine(cx - 24, cy - 86, cx - 19, cy - 64, stripe);
            canvas.DrawLine(cx + 24, cy - 86, cx + 19, cy - 64, stripe);
        }

        // Muzzle
        using (var muzzle = Rendering.Draw.Fill(Cream))
        {
            canvas.DrawOval(cx - 20, cy + 40, 30, 24, muzzle);
            canvas.DrawOval(cx + 20, cy + 40, 30, 24, muzzle);
        }

        Eyes(canvas, cx, cy - 6, mood, t);

        if (mood == Mood.Happy)
        {
            using var blush = Rendering.Draw.Fill(EarPink.WithAlpha(120));
            canvas.DrawOval(cx - 72, cy + 26, 16, 10, blush);
            canvas.DrawOval(cx + 72, cy + 26, 16, 10, blush);
        }

        // Nose and mouth
        using (var nose = new SKPath())
        {
            nose.MoveTo(cx - 10, cy + 22);
            nose.LineTo(cx + 10, cy + 22);
            nose.LineTo(cx, cy + 33);
            nose.Close();
            using var fill = Rendering.Draw.Fill(Nose);
            using var round = Rendering.Draw.Stroke(Nose, 5);
            canvas.DrawPath(nose, fill);
            canvas.DrawPath(nose, round);
        }
        using (var mouth = Rendering.Draw.Stroke(Ink, 4, SKStrokeCap.Round))
        using (var path = new SKPath())
        {
            if (mood == Mood.Stressed)
            {
                path.MoveTo(cx - 14, cy + 54);
                path.QuadTo(cx, cy + 44, cx + 14, cy + 54);   // little frown
            }
            else
            {
                path.MoveTo(cx, cy + 34);
                path.QuadTo(cx - 2, cy + 48, cx - 14, cy + 46); // the "w"
                path.MoveTo(cx, cy + 34);
                path.QuadTo(cx + 2, cy + 48, cx + 14, cy + 46);
            }
            canvas.DrawPath(path, mouth);
        }

        using (var whisker = Rendering.Draw.Stroke(Whisker.WithAlpha(200), 3, SKStrokeCap.Round))
        {
            foreach (int side in new[] { -1, 1 })
            {
                canvas.DrawLine(cx + side * 52, cy + 34, cx + side * 128, cy + 22, whisker);
                canvas.DrawLine(cx + side * 52, cy + 42, cx + side * 130, cy + 44, whisker);
                canvas.DrawLine(cx + side * 50, cy + 50, cx + side * 122, cy + 64, whisker);
            }
        }

        if (mood == Mood.Stressed)
        {
            float dx = cx + 142, dy = cy - 34; // beside the head, clear of the ear
            using var drop = new SKPath();
            drop.MoveTo(dx, dy - 14);
            drop.QuadTo(dx + 10, dy + 4, dx, dy + 15);
            drop.QuadTo(dx - 10, dy + 4, dx, dy - 14);
            using var fill = Rendering.Draw.Fill(new SKColor(147, 197, 253, 230));
            canvas.DrawPath(drop, fill);
        }
    }

    private static void Ear(SKCanvas canvas, float cx, float cy, int side, float tilt, SKColor color, float scale)
    {
        // Base on the head, tip up and out; scaled about the ear's centre for the inner ear.
        var a = new SKPoint(cx + side * 30, cy - 78);
        var b = new SKPoint(cx + side * (88 + tilt), cy - 150 + tilt * 1.6f);
        var c = new SKPoint(cx + side * 108, cy - 40);
        var mid = new SKPoint((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3 + (scale < 1 ? 8 : 0));
        SKPoint S(SKPoint p) => new(mid.X + (p.X - mid.X) * scale, mid.Y + (p.Y - mid.Y) * scale);

        using var path = new SKPath();
        path.MoveTo(S(a));
        path.LineTo(S(b));
        path.LineTo(S(c));
        path.Close();
        using var fill = Rendering.Draw.Fill(color);
        using var round = Rendering.Draw.Stroke(color, 14 * scale);
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, round);
    }

    private static void Eyes(SKCanvas canvas, float cx, float cy, Mood mood, double t)
    {
        const float dx = 44;
        // At 1 FPS a sub-second blink would almost never land on a frame; close the eyes for
        // one whole frame every 7 seconds instead.
        bool blink = mood == Mood.Focused && (long)Math.Floor(t) % 7 == 0;

        if (mood == Mood.Happy || blink)
        {
            // Content, closed eyes: arcs curving up (happy) or flat lines (blink)
            using var p = Rendering.Draw.Stroke(Ink, 6, SKStrokeCap.Round);
            foreach (int side in new[] { -1, 1 })
            {
                float ex = cx + side * dx;
                using var path = new SKPath();
                path.MoveTo(ex - 15, cy + 4);
                path.QuadTo(ex, blink ? cy + 4 : cy - 12, ex + 15, cy + 4);
                canvas.DrawPath(path, p);
            }
            return;
        }

        using var eye = Rendering.Draw.Fill(mood == Mood.Stressed ? SKColors.White : Ink);
        using var pupil = Rendering.Draw.Fill(Ink);
        using var shine = Rendering.Draw.Fill(SKColors.White);
        foreach (int side in new[] { -1, 1 })
        {
            float ex = cx + side * dx;
            if (mood == Mood.Stressed)
            {
                canvas.DrawOval(ex, cy, 19, 21, eye);          // wide-eyed, tiny pupils
                canvas.DrawCircle(ex, cy + 2, 6, pupil);
            }
            else
            {
                canvas.DrawOval(ex, cy, 14, 19, eye);
                canvas.DrawCircle(ex + 5, cy - 7, 5, shine);
                canvas.DrawCircle(ex - 4, cy + 6, 2.5f, shine);
            }
        }
    }
}
