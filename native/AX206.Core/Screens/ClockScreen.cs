using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>Port of renderClock() from renderer.js.</summary>
public sealed class ClockScreen : IScreen
{
    private static readonly string[] Days = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];
    private static readonly string[] Months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    public string Id => "clock";
    public string Title => "Clock";

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        var t = context.Now;

        // Hours right of centre, minutes left of it, offset by half a colon so the digits
        // don't shift as the colon blinks.
        using var big = Fonts.Display(220, SKFontStyleWeight.Bold);
        float offset = big.MeasureText(":") / 2 + 10;
        Rendering.Draw.Text(canvas, t.Hour.ToString("00"), 480 - offset, 270, big, Palette.Ink, HAlign.Right, VAlign.Middle);
        Rendering.Draw.Text(canvas, t.Minute.ToString("00"), 480 + offset, 270, big, Palette.Ink, HAlign.Left, VAlign.Middle);

        // The panel refreshes once a second, so blink on second parity. (renderer.js used
        // a 500 ms phase, which a 1 s timer samples at the same parity every tick.)
        if (t.Second % 2 == 0)
            Rendering.Draw.Text(canvas, ":", 480, 270, big, Palette.Ink, HAlign.Center, VAlign.Middle);

        using var small = Fonts.Text(36);
        string date = $"{Days[(int)t.DayOfWeek]}  ·  {t.Day} {Months[t.Month - 1]}";
        Rendering.Draw.Text(canvas, date, 480, 480, small, Palette.Muted, HAlign.Center, VAlign.Middle);

        const float barY = 620, barH = 10, margin = 32, barW = 960 - margin * 2;
        Rendering.Draw.Card(canvas, margin, barY, barW, barH, 5, Palette.Track);
        float fill = barW * (t.Second / 59f);
        if (fill > 0)
            Rendering.Draw.Card(canvas, margin, barY, fill, barH, 5, Palette.Cpu);
    }
}
