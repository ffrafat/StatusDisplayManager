using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>Port of renderBanglaGov(): download counts for three bangla.gov.bd tools.</summary>
public sealed class BanglaGovScreen : IScreen
{
    private static readonly float[] RowY = [130, 290, 450];

    public string Id => "bangla";
    public string Title => "Bangla.gov.bd";
    public bool IsAvailable(ScreenContext context) => context.Bangla?.Ok == true;

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        Rendering.Draw.Header(canvas, "bangla.gov.bd", 50, "bangla.png", SKRect.Create(32, 15, 70, 70), 100, Palette.Ink, Palette.Track);

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

        const float cardX = 32, cardW = 896, cardH = 140, iconSize = 80;
        using var title = Fonts.Display(28, SKFontStyleWeight.Bold);
        using var sub = Fonts.Text(20, SKFontStyleWeight.Light);
        using var count = Fonts.Display(36, SKFontStyleWeight.Bold);
        using var caption = Fonts.Text(14, SKFontStyleWeight.Bold);

        for (int i = 0; i < Math.Min(3, data.Tools.Count); i++)
        {
            var tool = data.Tools[i];
            float y = RowY[i];
            Rendering.Draw.Card(canvas, cardX, y, cardW, cardH, 20, Palette.Panel);

            var icon = SKRect.Create(cardX + 30, y + 30, iconSize, iconSize);
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(icon, 14), antialias: true);
            if (Assets.Image(tool.IconFile) is { } image)
            {
                canvas.DrawImage(image, icon, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }
            else
            {
                using var fill = Rendering.Draw.Fill(Palette.Track);
                canvas.DrawRect(icon, fill);
                Icons.Music(canvas, icon.MidX, icon.MidY, 36, Palette.Music);
            }
            canvas.Restore();

            string downloads = tool.Downloads.ToString("N0");
            float right = cardX + cardW - 30;
            Rendering.Draw.Text(canvas, downloads, right, y + 45, count, Palette.ActiveApp, HAlign.Right, VAlign.Middle);
            Rendering.Draw.Text(canvas, "DOWNLOADS", right, y + 95, caption, Palette.Muted, HAlign.Right, VAlign.Middle);

            // Keep the title clear of the download count (renderer.js left this unbounded).
            float textX = cardX + 135;
            float titleMax = right - count.MeasureText(downloads) - 24 - textX;
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(title, tool.TitleEn, titleMax), textX, y + 45, title, Palette.Ink, HAlign.Left, VAlign.Middle);
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(sub, $"v{tool.Version}  ·  {tool.TypeEn}", titleMax), textX, y + 95, sub, Palette.Muted, HAlign.Left, VAlign.Middle);
        }
    }
}
