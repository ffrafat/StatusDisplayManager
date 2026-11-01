using SkiaSharp;

namespace AX206.Core.Rendering;

/// <summary>LCD colours, matching PALETTE in the Electron renderer.js.</summary>
public static class Palette
{
    public static readonly SKColor Bg = SKColor.Parse("#0a0c10");
    public static readonly SKColor Panel = SKColor.Parse("#141820");
    public static readonly SKColor Ink = SKColor.Parse("#f0f4fc");
    public static readonly SKColor Muted = SKColor.Parse("#5e6d91");
    public static readonly SKColor Track = SKColor.Parse("#202738");
    public static readonly SKColor Cpu = SKColor.Parse("#60a5fa");
    public static readonly SKColor Ram = SKColor.Parse("#c084fc");
    public static readonly SKColor Disk = SKColor.Parse("#34d399");
    public static readonly SKColor Net = SKColor.Parse("#facc15");
    public static readonly SKColor Music = SKColor.Parse("#1DB954");
    public static readonly SKColor ActiveApp = SKColor.Parse("#00e5ff");
}
