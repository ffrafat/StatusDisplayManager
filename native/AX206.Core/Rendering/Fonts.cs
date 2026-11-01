using System.Collections.Concurrent;
using SkiaSharp;

namespace AX206.Core.Rendering;

/// <summary>
/// The Electron version asked for Outfit / Inter but never shipped them, so it fell back to
/// Arial. Natively we use Windows 11's own Segoe UI Variable: Display for big numbers and
/// headings, Text for small copy.
/// </summary>
public static class Fonts
{
    private const string DisplayFamily = "Segoe UI Variable Display";
    private const string TextFamily = "Segoe UI Variable Text";

    private static readonly ConcurrentDictionary<(string, SKFontStyleWeight), SKTypeface> Faces = new();

    private static SKTypeface Face(string family, SKFontStyleWeight weight) =>
        Faces.GetOrAdd((family, weight), key =>
            SKTypeface.FromFamilyName(key.Item1, key.Item2, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            ?? SKTypeface.Default);

    public static SKFont Display(float size, SKFontStyleWeight weight = SKFontStyleWeight.Bold) =>
        Create(DisplayFamily, size, weight);

    public static SKFont Text(float size, SKFontStyleWeight weight = SKFontStyleWeight.Normal) =>
        Create(TextFamily, size, weight);

    // Greyscale antialiasing: subpixel (ClearType) edges would colour-fringe on the LCD.
    private static SKFont Create(string family, float size, SKFontStyleWeight weight) =>
        new(Face(family, weight), size) { Edging = SKFontEdging.Antialias, Subpixel = true };
}
