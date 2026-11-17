using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>Download counts for three bangla.gov.bd tools: a total, then one card per tool with its share.</summary>
public sealed class BanglaGovScreen : IScreen
{
    // Each product's own colour, from its icon
    private static readonly Dictionary<string, SKColor> Accent = new()
    {
        ["product_purno.png"] = SKColor.Parse("#5b8def"),
        ["product_sothik.png"] = SKColor.Parse("#38bdf8"),
        ["product_banglaword.png"] = SKColor.Parse("#f05252")
    };
    private static readonly SKColor Secondary = SKColor.Parse("#9aa4b8");

    private const float Left = 32, Right = 928;

    public string Id => "bangla";
    public string Title => "Bangla.gov.bd";
    public bool IsAvailable(ScreenContext context) => context.Bangla?.Ok == true;

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        if (Assets.Image("bangla.png") is { } logo)
            canvas.DrawImage(logo, SKRect.Create(Left, 20, 64, 64), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using (var f = Fonts.Display(34, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, "bangla.gov.bd", Left + 84, 52, f, Palette.Ink, HAlign.Left, VAlign.Middle);

        var data = context.Bangla;
        if (data is not { Ok: true })
        {
            Rendering.Draw.Message(canvas, data?.Error ?? "Loading tools statistics...", 340, Palette.Muted);
            return;
        }
        if (data.Tools.Count == 0)
        {
            Rendering.Draw.Message(canvas, "No tool details available.", 340, Palette.Muted);
            return;
        }

        var tools = data.Tools.Take(3).ToList();
        long total = tools.Sum(x => x.Downloads);
        long max = Math.Max(1, tools.Max(x => x.Downloads));

        using (var f = Fonts.Text(14, SKFontStyleWeight.SemiBold))
            Rendering.Draw.Text(canvas, "TOTAL DOWNLOADS", Right, 30, f, Palette.Muted, HAlign.Right, VAlign.Middle);
        using (var f = Fonts.Display(40, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, total.ToString("N0"), Right, 66, f, Palette.Ink, HAlign.Right, VAlign.Middle);

        const float top = 112, cardH = 160, gap = 14, iconSize = 84;
        using var name = Fonts.Display(28, SKFontStyleWeight.Bold);
        using var sub = Fonts.Text(19);
        using var count = Fonts.Display(34, SKFontStyleWeight.Bold);
        using var share = Fonts.Text(17, SKFontStyleWeight.SemiBold);

        for (int i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            float y = top + i * (cardH + gap);
            var accent = Accent.GetValueOrDefault(tool.IconFile, Palette.ActiveApp);
            Rendering.Draw.Card(canvas, Left, y, Right - Left, cardH, 22, Palette.Panel);

            var icon = SKRect.Create(Left + 28, y + (cardH - iconSize) / 2, iconSize, iconSize);
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(icon, 18), antialias: true);
            if (Assets.Image(tool.IconFile) is { } image)
                canvas.DrawImage(image, icon, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            else
                using (var fill = Rendering.Draw.Fill(Palette.Track)) canvas.DrawRect(icon, fill);
            canvas.Restore();

            float textX = icon.Right + 28, right = Right - 28;
            string downloads = tool.Downloads.ToString("N0");
            Rendering.Draw.Text(canvas, downloads, right, y + 48, count, Palette.Ink, HAlign.Right, VAlign.Middle);
            string pct = total > 0 ? $"{Math.Round(tool.Downloads * 100.0 / total):0}% of total" : "";
            Rendering.Draw.Text(canvas, pct, right, y + 88, share, accent, HAlign.Right, VAlign.Middle);

            float textMax = right - Math.Max(count.MeasureText(downloads), share.MeasureText(pct)) - 24 - textX;
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(name, tool.TitleEn, textMax), textX, y + 48, name, Palette.Ink, HAlign.Left, VAlign.Middle);
            string meta = string.Join("  ·  ", new[] { tool.Version.Length > 0 ? $"v{tool.Version}" : "", tool.TypeEn }.Where(s => s.Length > 0));
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(sub, meta, textMax), textX, y + 88, sub, Secondary, HAlign.Left, VAlign.Middle);

            // Bar relative to the most-downloaded tool, so the leader is always full width
            Rendering.Draw.ProgressBar(canvas, textX, y + 120, right - textX, 10, 5, tool.Downloads * 100.0 / max, Palette.Track, accent);
        }
    }
}
