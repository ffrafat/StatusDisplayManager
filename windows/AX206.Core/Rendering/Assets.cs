using System.Collections.Concurrent;
using SkiaSharp;

namespace AX206.Core.Rendering;

/// <summary>Logos and product icons shipped in the app's Assets folder, decoded once.</summary>
public static class Assets
{
    private static readonly ConcurrentDictionary<string, SKImage?> Cache = new();

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Assets");

    public static SKImage? Image(string file) =>
        Cache.GetOrAdd(file, f =>
        {
            string path = Path.Combine(Folder, f);
            return File.Exists(path) ? SKImage.FromEncodedData(path) : null;
        });
}
