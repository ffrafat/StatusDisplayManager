using SkiaSharp;

namespace AX206.Core.Rendering;

/// <summary>
/// Line icons ported from renderer.js (themselves from Lucide paths). Drawn in a 24-unit
/// box centred on (cx, cy) and scaled to sz.
/// </summary>
public static class Icons
{
    private static void InBox(SKCanvas canvas, float cx, float cy, float sz, Action draw)
    {
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.Scale(sz / 24f);
        canvas.Translate(-12, -12); // paths below use Lucide's 0..24 coordinates
        draw();
        canvas.Restore();
    }

    private static SKPaint Line(SKColor color) => Draw.Stroke(color, 2, SKStrokeCap.Round);

    public static void Cpu(SKCanvas canvas, float cx, float cy, float sz, SKColor color) => InBox(canvas, cx, cy, sz, () =>
    {
        using var p = Line(color);
        canvas.DrawRoundRect(new SKRect(4, 4, 20, 20), 2, 2, p);
        canvas.DrawRoundRect(new SKRect(9, 9, 15, 15), 1, 1, p);
        using var pins = new SKPath();
        foreach (float v in new[] { 9f, 15f })
        {
            pins.MoveTo(v, 1); pins.LineTo(v, 4);    // top
            pins.MoveTo(v, 20); pins.LineTo(v, 23);  // bottom
            pins.MoveTo(20, v); pins.LineTo(23, v);  // right
            pins.MoveTo(1, v); pins.LineTo(4, v);    // left
        }
        canvas.DrawPath(pins, p);
    });

    public static void Ram(SKCanvas canvas, float cx, float cy, float sz, SKColor color) => InBox(canvas, cx, cy, sz, () =>
    {
        using var p = Line(color);
        canvas.DrawRoundRect(new SKRect(2, 5, 22, 21), 2, 2, p);
        using var lines = new SKPath();
        foreach (float x in new[] { 6f, 10f, 14f, 18f }) { lines.MoveTo(x, 19); lines.LineTo(x, 16); } // contacts
        foreach (float x in new[] { 8f, 12f, 16f }) { lines.MoveTo(x, 11); lines.LineTo(x, 9); }       // chips
        canvas.DrawPath(lines, p);
    });

    public static void Disk(SKCanvas canvas, float cx, float cy, float sz, SKColor color) => InBox(canvas, cx, cy, sz, () =>
    {
        using var p = Line(color);
        canvas.DrawLine(2, 12, 22, 12, p);

        using var body = new SKPath();
        body.MoveTo(5.45f, 5.11f);
        body.LineTo(2, 16);
        body.LineTo(2, 19);
        body.ArcTo(2, 21, 4, 21, 2);
        body.LineTo(20, 21);
        body.ArcTo(22, 21, 22, 19, 2);
        body.LineTo(22, 16);
        body.LineTo(18.55f, 5.11f);
        body.ArcTo(18.55f, 4, 16.73f, 4, 2);
        body.LineTo(7.27f, 4);
        body.ArcTo(5.45f, 4, 5.45f, 5.11f, 2);
        body.Close();
        canvas.DrawPath(body, p);

        canvas.DrawLine(6, 16, 6.01f, 16, p);   // LEDs (round caps make dots)
        canvas.DrawLine(10, 16, 10.01f, 16, p);
    });

    public static void Network(SKCanvas canvas, float cx, float cy, float sz, SKColor color) => InBox(canvas, cx, cy, sz, () =>
    {
        using var p = Line(color);
        canvas.DrawRoundRect(new SKRect(16, 16, 22, 22), 1, 1, p);
        canvas.DrawRoundRect(new SKRect(2, 16, 8, 22), 1, 1, p);
        canvas.DrawRoundRect(new SKRect(9, 2, 15, 8), 1, 1, p);

        using var link = new SKPath();
        link.MoveTo(5, 16);
        link.LineTo(5, 13);
        link.ArcTo(5, 12, 6, 12, 1);
        link.LineTo(18, 12);
        link.ArcTo(19, 12, 19, 13, 1);
        link.LineTo(19, 16);
        canvas.DrawPath(link, p);

        canvas.DrawLine(12, 12, 12, 8, p);
    });

    public static void Music(SKCanvas canvas, float cx, float cy, float sz, SKColor color) => InBox(canvas, cx, cy, sz, () =>
    {
        using var p = Line(color);
        using var stem = new SKPath();
        stem.MoveTo(9, 18); stem.LineTo(9, 5); stem.LineTo(21, 3); stem.LineTo(21, 16);
        canvas.DrawPath(stem, p);

        using var f = Draw.Fill(color);
        canvas.DrawCircle(6, 18, 3, f);
        canvas.DrawCircle(18, 16, 3, f);
    });
}
