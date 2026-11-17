using AX206.Core.Rendering;
using SkiaSharp;

namespace AX206.Core.Screens;

/// <summary>Port of renderMusic(): album art left, title/artist/album and a status pill right.</summary>
public sealed class MusicScreen : IScreen
{
    private static readonly SKColor Background = SKColor.Parse("#05140b");

    public string Id => "music";
    public string Title => "Music";
    public bool PreemptsWhenAvailable => true;
    public bool IsAvailable(ScreenContext context) => context.Media?.IsActive == true;

    public void Draw(SKCanvas canvas, ScreenContext context)
    {
        canvas.Clear(Background);
        var media = context.Media;

        var art = new SKRect(60, 70, 560, 570);
        canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(art, 16), antialias: true);
        if (media?.Artwork is { } artwork)
        {
            canvas.DrawImage(artwork, art, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        else
        {
            using var placeholder = new SKPaint { IsAntialias = true };
            placeholder.Shader = SKShader.CreateLinearGradient(new SKPoint(0, art.Top), new SKPoint(0, art.Bottom),
                [SKColor.Parse("#102e1b"), Background], SKShaderTileMode.Clamp);
            canvas.DrawRect(art, placeholder);
            Icons.Music(canvas, art.MidX, art.MidY, 110, Palette.Music);
        }
        canvas.Restore();

        const float x = 600, maxWidth = 960 - x - 40;
        using (var f = Fonts.Display(44, SKFontStyleWeight.Bold))
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(f, NonEmpty(media?.Title, "Unknown Song"), maxWidth), x, 200, f, Palette.Ink, HAlign.Left, VAlign.Top);
        using (var f = Fonts.Display(32, SKFontStyleWeight.Medium))
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(f, NonEmpty(media?.Artist, "Unknown Artist"), maxWidth), x, 275, f, Palette.Music, HAlign.Left, VAlign.Top);
        if (!string.IsNullOrEmpty(media?.Album))
        {
            using var f = Fonts.Text(24, SKFontStyleWeight.Light);
            Rendering.Draw.Text(canvas, Rendering.Draw.FitText(f, media.Album, maxWidth), x, 345, f, Palette.Muted, HAlign.Left, VAlign.Top);
        }

        if (media?.Status is "Playing" or "Paused")
        {
            bool paused = media.Status == "Paused";
            var stroke = paused ? SKColor.Parse("#d07010") : SKColor.Parse("#1db954");
            var text = paused ? SKColor.Parse("#ffb300") : SKColor.Parse("#1db954");
            var pill = new SKRoundRect(new SKRect(x, 410, x + 130, 450), 20);

            using (var fill = Rendering.Draw.Fill(stroke.WithAlpha(38))) canvas.DrawRoundRect(pill, fill);
            using (var line = Rendering.Draw.Stroke(stroke, 1)) canvas.DrawRoundRect(pill, line);
            using var f = Fonts.Display(18, SKFontStyleWeight.Bold);
            Rendering.Draw.Text(canvas, paused ? "PAUSED" : "PLAYING", x + 65, 430, f, text, HAlign.Center, VAlign.Middle);
        }
    }

    private static string NonEmpty(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
