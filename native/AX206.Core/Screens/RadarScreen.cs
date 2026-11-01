using AX206.Core.Data;
using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>
/// Port of renderRadar(): a range scope with altitude-coloured aircraft on the left, the five
/// nearest in a list on the right.
/// </summary>
public sealed class RadarScreen : IScreen
{
    private const double MaxDeadReckonSec = 240; // after this a plane freezes in place
    private const float Cx = 320, Cy = 320, R = 284, Px = 652, Pw = 280;
    private const float Scale = R / 110; // smalltv-mod's sizes are for a ~110 px radius

    private static readonly SKPath PlanePath = SKPath.ParseSvgPathData(
        "M17.8 19.2 16 11l3.5-3.5C21 6 21.5 4 21 3c-1-.5-3 0-4.5 1.5L13 8 4.8 6.2c-.5-.1-.9.1-1.1.5l-.3.5c-.2.5-.1 1 .3 1.3L9 12l-2 3H4l-1 1 3 2 2 3 1-1v-3l3-2 3.5 5.3c.3.4.8.5 1.3.3l.5-.2c.4-.3.6-.7.5-1.2z");

    private static readonly SKColor Offline = new(255, 82, 82);

    public string Id => "radar";
    public string Title => "Radar";

    // Stays in rotation while enabled even when the sky is empty (coverage is thin), so the
    // empty and loading states are visible.
    public bool IsAvailable(ScreenContext context) => context.RadarConfig.Enabled;

    private sealed record Plot(Aircraft Ac, double XKm, double YKm, double Dist, double Bearing);

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        var r = context.Radar;
        bool ok = r is { Ok: true };
        bool loading = r is null || r.Error == "Loading...";
        double now = context.UnixSeconds;
        double rangeKm = ok ? r!.RangeKm : context.RadarConfig.RangeKm;

        Scope(canvas, rangeKm);

        var list = ok ? Project(r!, now) : [];
        var inside = list.Where(p => p.Dist <= rangeKm).ToList();
        var airportBoxes = ok ? AirportMarker(canvas, r!.Airport, rangeKm) : [];
        if (ok) AircraftMarkers(canvas, list, rangeKm, airportBoxes);

        // Home marker last, on top of everything
        using (var halo = Rendering.Draw.Fill(new SKColor(52, 211, 153, 64))) canvas.DrawCircle(Cx, Cy, 14, halo);
        using (var dot = Rendering.Draw.Fill(Palette.Disk)) canvas.DrawCircle(Cx, Cy, 7, dot);

        // Corner captions: range (top-left), location (bottom-left)
        using (var cap = Fonts.Text(14, SKFontStyleWeight.Bold)) Rendering.Draw.Text(canvas, "RANGE", 28, 40, cap, Palette.Muted);
        using (var rng = Fonts.Display(28, SKFontStyleWeight.Bold)) Rendering.Draw.Text(canvas, $"{Format.Whole(rangeKm)} km", 28, 72, rng, Palette.Ink);
        if (ok && r!.Home is { } home)
        {
            using var f = Fonts.Text(16, SKFontStyleWeight.Medium);
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(f, home.Label, 150), 28, 616, f, Palette.Muted);
        }

        SidePanel(canvas, r, ok, loading, now, inside, list.Count - inside.Count);
    }

    // Moves each aircraft along its track for the time since its position was reported, so
    // planes glide at 1 FPS instead of jumping every poll.
    private static List<Plot> Project(RadarData r, double nowSec) =>
        (r.Aircraft ?? []).Select(ac =>
        {
            double b = ac.Bearing * Math.PI / 180;
            double x = ac.DistKm * Math.Sin(b), y = ac.DistKm * Math.Cos(b);
            if (ac.GsKt is > 0 && ac.Track is double track)
            {
                double age = Math.Min(MaxDeadReckonSec, Math.Max(0, nowSec - ac.PosTs));
                double ahead = ac.GsKt.Value * 1.852 / 3600 * age;
                double t = track * Math.PI / 180;
                x += ahead * Math.Sin(t);
                y += ahead * Math.Cos(t);
            }
            double bearing = (Math.Atan2(x, y) * 180 / Math.PI + 360) % 360;
            return new Plot(ac, x, y, Math.Sqrt(x * x + y * y), bearing);
        }).OrderBy(p => p.Dist).ToList();

    private static SKColor AltColor(Aircraft ac) => ac.AltFt switch
    {
        null => Palette.ActiveApp,
        < 10000 => Palette.Net,
        < 25000 => Palette.Cpu,
        _ => Palette.Ram
    };

    private static string Altitude(Aircraft ac) => ac.AltFt switch
    {
        null => "",
        >= 10000 => $"FL{Math.Round(ac.AltFt.Value / 100)}",
        _ => $"{Math.Round(ac.AltFt.Value / 100) * 100:N0} ft"
    };

    private static string Compass(double deg) =>
        new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(int)Math.Round(deg / 45) % 8];

    private static SKPoint Polar(double distPx, double bearingDeg)
    {
        double b = bearingDeg * Math.PI / 180;
        return new SKPoint(Cx + (float)(distPx * Math.Sin(b)), Cy - (float)(distPx * Math.Cos(b)));
    }

    private static void Scope(SKCanvas canvas, double rangeKm)
    {
        using (var disc = Rendering.Draw.Fill(Palette.Panel)) canvas.DrawCircle(Cx, Cy, R, disc);
        using (var outer = Rendering.Draw.Stroke(Palette.Track, 3)) canvas.DrawCircle(Cx, Cy, R, outer);
        using (var inner = Rendering.Draw.Stroke(Palette.Track, 2))
        {
            canvas.DrawCircle(Cx, Cy, R / 2, inner);
            canvas.DrawLine(Cx - R, Cy, Cx + R, Cy, inner);
            canvas.DrawLine(Cx, Cy - R, Cx, Cy + R, inner);
        }

        using (var n = Fonts.Display(22, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, "N", Cx, Cy - R + 24, n, Palette.Muted, HAlign.Center, VAlign.Middle);
        using var ring = Fonts.Text(15, SKFontStyleWeight.Medium);
        Rendering.Draw.Text(canvas, Format.Whole(rangeKm / 2), Cx + R / 2 - 6, Cy - 6, ring, Palette.Muted, HAlign.Right, VAlign.Bottom);
        Rendering.Draw.Text(canvas, $"{Format.Whole(rangeKm)} km", Cx + R - 8, Cy - 6, ring, Palette.Muted, HAlign.Right, VAlign.Bottom);
    }

    // Airport reference marker; returns the boxes it occupies so labels steer clear of it.
    private static List<SKRect> AirportMarker(SKCanvas canvas, Airport? ap, double rangeKm)
    {
        if (ap is null || ap.DistKm > rangeKm) return [];
        var p = Polar(ap.DistKm / rangeKm * R, ap.Bearing);
        var ink = new SKColor(240, 244, 252, 140);

        using (var ringPaint = Rendering.Draw.Stroke(ink, 3)) canvas.DrawCircle(p, 12, ringPaint);
        using (var runway = Rendering.Draw.Stroke(ink, 5, SKStrokeCap.Round)) canvas.DrawLine(p.X - 13, p.Y + 13, p.X + 13, p.Y - 13, runway);

        using var f = Fonts.Text(15, SKFontStyleWeight.Bold);
        Rendering.Draw.Text(canvas, ap.Icao, p.X, p.Y + 18, f, ink, HAlign.Center, VAlign.Top);
        float w = f.MeasureText(ap.Icao);
        return [SKRect.Create(p.X - 16, p.Y - 16, 32, 32), SKRect.Create(p.X - w / 2 - 2, p.Y + 16, w + 4, 20)];
    }

    private static void AircraftMarkers(SKCanvas canvas, List<Plot> list, double rangeKm, List<SKRect> reserved)
    {
        // Boxes labels must not cover: the N marker, ring captions, home dot and airport
        var taken = new List<SKRect>
        {
            SKRect.Create(Cx - 16, Cy - R + 8, 32, 32),
            SKRect.Create(Cx + R / 2 - 30, Cy - 26, 26, 22),
            SKRect.Create(Cx + R - 70, Cy - 26, 64, 22),
            SKRect.Create(Cx - 14, Cy - 14, 28, 28)
        };
        taken.AddRange(reserved);

        // Traffic just outside the ring: a dot on the rim at its bearing
        using (var rim = Rendering.Draw.Fill(Palette.Muted))
            foreach (var p in list.Where(p => p.Dist > rangeKm))
                canvas.DrawCircle(Polar(R, p.Bearing), 3 * Scale, rim);

        var pts = list.Where(p => p.Dist <= rangeKm).Select(p => (
            Plot: p,
            X: Cx + (float)(p.XKm / rangeKm * R),
            Y: Cy - (float)(p.YKm / rangeKm * R),
            Color: AltColor(p.Ac))).ToList();
        float m = 6 * Scale;
        foreach (var p in pts) taken.Add(SKRect.Create(p.X - m, p.Y - m, m * 2, m * 2));

        // Markers back to front so the nearest is on top, clipped to the scope so speed
        // vectors near the rim stay inside the ring...
        canvas.Save();
        using (var clip = new SKPath())
        {
            clip.AddCircle(Cx, Cy, R);
            canvas.ClipPath(clip, antialias: true);
        }
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            var (plot, x, y, color) = pts[i];
            using var fill = Rendering.Draw.Fill(color);
            if (plot.Ac.Track is double track)
            {
                double t = track * Math.PI / 180;
                float s = 5 * Scale;
                float len = (float)Math.Clamp((plot.Ac.GsKt ?? 0) * 0.08, 6, 24) * Scale;
                float sin = (float)Math.Sin(t), cos = (float)Math.Cos(t);
                using (var vector = Rendering.Draw.Stroke(color, 2))
                    canvas.DrawLine(x + sin * s, y - cos * s, x + sin * (s + len), y - cos * (s + len), vector);

                canvas.Save();
                canvas.Translate(x, y);
                canvas.RotateDegrees((float)track);
                using var tri = new SKPath();
                tri.MoveTo(0, -s * 1.3f);
                tri.LineTo(s, s);
                tri.LineTo(0, s * 0.45f);
                tri.LineTo(-s, s);
                tri.Close();
                canvas.DrawPath(tri, fill);
                canvas.Restore();
            }
            else
            {
                canvas.DrawCircle(x, y, 3 * Scale, fill);
            }
        }
        canvas.Restore();

        // ...but labels are placed nearest-first, so the nearest always gets one.
        using var nameFont = Fonts.Display(19, SKFontStyleWeight.Bold);
        using var altFont = Fonts.Text(15, SKFontStyleWeight.Medium);
        foreach (var (plot, x, y, color) in pts)
        {
            string name = plot.Ac.Callsign.Length > 0 ? plot.Ac.Callsign : plot.Ac.Hex.ToUpperInvariant();
            string alt = Altitude(plot.Ac);
            float w = Math.Max(nameFont.MeasureText(name), altFont.MeasureText(alt));
            float h = alt.Length > 0 ? 40 : 22;
            float gap = 7 * Scale;
            // Right of the marker first (left near the edge), then the other side
            float[] sides = x + gap + w > Cx + R + 12 ? [x - gap - w, x + gap] : [x + gap, x - gap - w];

            float? lx = null;
            foreach (float sx in sides)
            {
                var box = SKRect.Create(sx - 2, y - h / 2 - 2, w + 4, h + 4);
                if (taken.Any(b => b.IntersectsWith(box))) continue;
                lx = sx;
                taken.Add(box);
                break;
            }
            if (lx is not float labelX) continue; // the marker still shows, just unlabelled

            Rendering.Draw.Text(canvas, name, labelX, y - h / 2, nameFont, Palette.Ink, HAlign.Left, VAlign.Top);
            if (alt.Length > 0)
                Rendering.Draw.Text(canvas, alt, labelX, y - h / 2 + 22, altFont, color, HAlign.Left, VAlign.Top);
        }
    }

    private static void SidePanel(SKCanvas canvas, RadarData? r, bool ok, bool loading, double now,
        List<Plot> inside, int outsideCount)
    {
        using (var title = Fonts.Display(32, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, "Nearby", Px, 50, title, Palette.Ink, HAlign.Left, VAlign.Middle);

        string countText = ok ? inside.Count.ToString() : "–";
        using (var countFont = Fonts.Display(26, SKFontStyleWeight.Bold))
        {
            float pillW = Math.Max(52, countFont.MeasureText(countText) + 30), pillX = Px + Pw - pillW;
            Rendering.Draw.Card(canvas, pillX, 30, pillW, 40, 20, inside.Count > 0 ? Palette.Cpu : Palette.Track);
            Rendering.Draw.Text(canvas, countText, pillX + pillW / 2, 51, countFont,
                inside.Count > 0 ? Palette.Bg : Palette.Ink, HAlign.Center, VAlign.Middle);

            // Red dot when a feed's last fetch failed, or the data has gone stale
            bool stale = ok && now - r!.FetchedAt > 600;
            if (!loading && (!ok || r!.Error is not null || stale))
                using (var dot = Rendering.Draw.Fill(Offline)) canvas.DrawCircle(pillX - 18, 50, 7, dot);
        }

        using (var rule = Rendering.Draw.Stroke(Palette.Track, 4)) canvas.DrawLine(Px, 92, Px + Pw, 92, rule);

        if (inside.Count == 0)
            EmptyState(canvas, r, ok, loading, now, outsideCount);
        else
            NearestList(canvas, inside);

        if (ok)
        {
            using var f = Fonts.Text(15);
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(f, $"{r!.Source} · updated {Format.Ago(now - r.FetchedAt)}", Pw),
                Px, 616, f, Palette.Muted);
        }
    }

    private static void EmptyState(SKCanvas canvas, RadarData? r, bool ok, bool loading, double now, int outsideCount)
    {
        const float midY = 300, centre = Px + Pw / 2;
        canvas.Save();
        canvas.Translate(centre - 42, midY - 140);
        canvas.Scale(3.5f);
        using (var plane = Rendering.Draw.Stroke(Palette.Muted, 1.4f, SKStrokeCap.Round)) canvas.DrawPath(PlanePath, plane);
        canvas.Restore();

        string title, sub, note = "";
        if (loading) (title, sub) = ("Scanning the sky", "Fetching nearby aircraft...");
        else if (!ok) (title, sub) = ("Radar offline", r?.Error ?? "");
        else
        {
            title = "No aircraft in range";
            sub = r!.LastSeenTs is double seen ? $"Last one seen {Format.Ago(now - seen)}" : "None seen since the app started";
            if (outsideCount > 0) note = $"{outsideCount} just outside the ring";
        }

        using (var f = Fonts.Display(28, SKFontStyleWeight.SemiBold))
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(f, title, Pw), centre, midY + 20, f, Palette.Ink, HAlign.Center);
        using var small = Fonts.Text(19);
        var lines = Rendering.Draw.Wrap(small, sub, Pw, 3);
        for (int i = 0; i < lines.Count; i++)
            Rendering.Draw.Text(canvas, lines[i], centre, midY + 58 + i * 26, small, Palette.Muted, HAlign.Center);
        if (note.Length > 0)
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(small, note, Pw), centre, midY + 58 + lines.Count * 26 + 8, small, Palette.Cpu, HAlign.Center);
    }

    private static void NearestList(SKCanvas canvas, List<Plot> inside)
    {
        const float rowH = 88, rowGap = 10, top = 108;
        using var dist = Fonts.Display(24, SKFontStyleWeight.Bold);
        using var call = Fonts.Display(26, SKFontStyleWeight.Bold);
        using var detail = Fonts.Text(18);
        for (int i = 0; i < Math.Min(5, inside.Count); i++)
        {
            var p = inside[i];
            float y = top + i * (rowH + rowGap);
            Rendering.Draw.Card(canvas, Px, y, Pw, rowH, 16, Palette.Panel);
            Rendering.Draw.Card(canvas, Px + 14, y + 18, 6, rowH - 36, 3, AltColor(p.Ac));

            string distText = $"{(p.Dist < 10 ? p.Dist.ToString("0.0") : Format.Whole(p.Dist))} km";
            Rendering.Draw.Text(canvas, distText, Px + Pw - 16, y + 38, dist, Palette.Ink, HAlign.Right);
            float distW = dist.MeasureText(distText);

            string name = p.Ac.Callsign.Length > 0 ? p.Ac.Callsign : p.Ac.Hex.ToUpperInvariant();
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(call, name, Pw - 60 - distW), Px + 34, y + 38, call, Palette.Ink);

            var parts = new List<string>();
            if (Altitude(p.Ac) is { Length: > 0 } alt) parts.Add(alt);
            if (p.Ac.GsKt is double gs) parts.Add($"{Format.Whole(gs)} kt");
            parts.Add(Compass(p.Bearing));
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(detail, string.Join(" · ", parts), Pw - 50),
                Px + 34, y + 70, detail, Palette.Muted);
        }
    }
}
