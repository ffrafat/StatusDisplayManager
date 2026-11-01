using AX206.Core.Rendering;
using AX206.Core.Telemetry;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>Port of renderStats() from renderer.js: hostname/IP header and a 2x2 card grid.</summary>
public sealed class StatsScreen : IScreen
{
    private const float CardW = 432, CardH = 240, Pad = 32, Gap = 24, Top = 104;

    public string Id => "stats";
    public string Title => "Stats";

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        var s = context.Stats;

        using (var host = Fonts.Display(30, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, s?.Hostname ?? "LOCALHOST", 32, 50, host, Palette.Ink, HAlign.Left, VAlign.Middle);
        using (var ip = Fonts.Text(28, SKFontStyleWeight.Light))
            Rendering.Draw.Text(canvas, s?.Ip ?? "127.0.0.1", 928, 50, ip, Palette.Muted, HAlign.Right, VAlign.Middle);
        using (var rule = Rendering.Draw.Stroke(Palette.Track, 4))
            canvas.DrawLine(32, 72, 928, 72, rule);

        if (s is null)
        {
            using var f = Fonts.Text(32, SKFontStyleWeight.Light);
            Rendering.Draw.Text(canvas, "Gathering metrics...", 480, 320, f, Palette.Muted, HAlign.Center, VAlign.Middle);
            return;
        }

        string cpuSub = $"{s.CpuGhz:0.0} GHz";
        Card(canvas, Pad, Top, Icons.Cpu, Palette.Cpu, $"{Math.Round(s.CpuPercent)}%", cpuSub, s.CpuPercent);

        Card(canvas, Pad + CardW + Gap, Top, Icons.Ram, Palette.Ram,
            $"{Math.Round(s.RamPercent)}%", $"{s.RamUsed / 1e9:0.0} / {s.RamTotal / 1e9:0.0} GB", s.RamPercent);

        Card(canvas, Pad, Top + CardH + Gap, Icons.Disk, Palette.Disk,
            $"{Math.Round(s.DiskPercent)}%", $"{s.DiskUsed / 1e9:0} / {s.DiskTotal / 1e9:0} GB", s.DiskPercent);

        // Network bar is load against a 100 Mbps (12.5 MB/s) link, as before.
        double netPct = Math.Min(100, (s.RxBytesPerSec + s.TxBytesPerSec) / 12.5e6 * 100);
        Card(canvas, Pad + CardW + Gap, Top + CardH + Gap, Icons.Network, Palette.Net,
            $"↓ {FormatRate(s.RxBytesPerSec)}   ↑ {FormatRate(s.TxBytesPerSec)}",
            $"Total: ↓{s.RxTotal / 1e9:0.0}G  ↑{s.TxTotal / 1e9:0.0}G", netPct);
    }

    private static void Card(SKCanvas canvas, float x, float y,
        Action<SKCanvas, float, float, float, SKColor> icon, SKColor accent,
        string value, string sub, double percent)
    {
        float cx = x + CardW / 2;
        Rendering.Draw.Card(canvas, x, y, CardW, CardH, 20, Palette.Panel);
        icon(canvas, cx, y + 40, 48, accent);

        using (var big = Fonts.Display(42, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, value, cx, y + 106, big, Palette.Ink, HAlign.Center, VAlign.Middle);
        using (var small = Fonts.Text(24, SKFontStyleWeight.Light))
            Rendering.Draw.Text(canvas, sub, cx, y + 154, small, Palette.Muted, HAlign.Center, VAlign.Middle);

        Rendering.Draw.ProgressBar(canvas, x + 40, y + 195, CardW - 80, 14, 7, percent, Palette.Track, accent);
    }

    private static string FormatRate(double bytes) => bytes switch
    {
        >= 1e6 => $"{bytes / 1e6:0.0}M",
        >= 1e3 => $"{bytes / 1e3:0}K",
        _ => $"{Math.Round(bytes)}B"
    };
}
