using AX206.Core.Screens;
using AX206.Core.Usb;
using SkiaSharp;

namespace AX206.Core.Rendering;

/// <summary>
/// Draws a screen at 2x (960x640) for smooth edges, downsamples to the panel's 480x320, and
/// produces both the BGRA pixels for the on-screen preview and the RGB565 bytes for the device.
/// </summary>
public sealed class FrameRenderer : IDisposable
{
    public const int CanvasWidth = 960;
    public const int CanvasHeight = 640;

    private readonly SKSurface _hiRes = SKSurface.Create(
        new SKImageInfo(CanvasWidth, CanvasHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
    private readonly SKSurface _panel = SKSurface.Create(
        new SKImageInfo(Ax206Device.Width, Ax206Device.Height, SKColorType.Bgra8888, SKAlphaType.Premul));

    /// <summary>Last frame as 480x320 BGRA (premultiplied, opaque).</summary>
    public byte[] Bgra { get; } = new byte[Ax206Device.Width * Ax206Device.Height * 4];

    /// <summary>Last frame as 480x320 big-endian RGB565, ready for <see cref="Ax206Device.Blit(ReadOnlySpan{byte})"/>.</summary>
    public byte[] Rgb565 { get; } = new byte[Ax206Device.FrameBytes];

    public void Render(IScreen screen, ScreenContext context)
    {
        var canvas = _hiRes.Canvas;
        canvas.ResetMatrix();
        canvas.Clear(Palette.Bg);
        screen.Draw(canvas, context);
        canvas.Flush();

        using var image = _hiRes.Snapshot();
        var panel = _panel.Canvas;
        panel.Clear(SKColors.Black);
        panel.DrawImage(image, new SKRect(0, 0, Ax206Device.Width, Ax206Device.Height),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        panel.Flush();

        using var pixmap = _panel.PeekPixels();
        pixmap.GetPixelSpan().CopyTo(Bgra);
        ToRgb565(Bgra, Rgb565);
    }

    private static void ToRgb565(ReadOnlySpan<byte> bgra, Span<byte> rgb565)
    {
        for (int i = 0, o = 0; i < bgra.Length; i += 4, o += 2)
        {
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            int v = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3);
            rgb565[o] = (byte)(v >> 8);
            rgb565[o + 1] = (byte)v;
        }
    }

    public void SavePng(string path)
    {
        using var image = _panel.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    public void Dispose()
    {
        _hiRes.Dispose();
        _panel.Dispose();
    }
}
