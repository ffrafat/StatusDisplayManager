using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>Port of renderAgUsage(): one usage bar per model family.</summary>
public sealed class AntigravityScreen : IScreen
{
    private static readonly SKColor Low = new(255, 82, 82);

    public string Id => "ag";
    public string Title => "Antigravity";
    public bool IsAvailable(ScreenContext context) => context.Antigravity?.Available == true;

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        Rendering.Draw.Header(canvas, "Antigravity", 46, "antigravity.png", SKRect.Create(32, 10, 72, 72), 92, Palette.Ink, Palette.Track);

        var usage = context.Antigravity;
        if (usage is not { Available: true })
        {
            Rendering.Draw.Message(canvas, usage?.Error ?? "Loading IDE quota...", 320, Palette.Muted);
            return;
        }
        if (usage.Groups.Count == 0)
        {
            Rendering.Draw.Message(canvas, "No model quota details available.", 320, Palette.Muted, 30);
            return;
        }

        const float top = 140, rowH = 150, margin = 40, barW = 960 - margin * 2, barH = 38;
        using var label = Fonts.Display(26, SKFontStyleWeight.Bold);
        using var small = Fonts.Text(20, SKFontStyleWeight.Light);
        for (int i = 0; i < usage.Groups.Count; i++)
        {
            var g = usage.Groups[i];
            float ry = top + i * rowH;
            double used = 100 - g.RemainingPercent;
            var bar = g.RemainingPercent < 20 ? Low : g.Color;

            Rendering.Draw.Text(canvas, g.Label, margin, ry + 10, label, g.Color, HAlign.Left, VAlign.Top);
            Rendering.Draw.Text(canvas, $"{Format.Whole(used)}% used", 960 - margin, ry + 10, label, bar, HAlign.Right, VAlign.Top);

            float barY = ry + 52;
            Rendering.Draw.ProgressBar(canvas, margin, barY, barW, barH, barH / 2, used, Palette.Track, bar);
            Rendering.Draw.Text(canvas, Format.Reset(g.Reset), margin, barY + 52, small, Palette.Muted, HAlign.Left, VAlign.Top);
        }
    }
}
