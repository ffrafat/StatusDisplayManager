namespace AX206.Core.Rendering;

/// <summary>Short display strings shared by several screens.</summary>
public static class Format
{
    /// <summary>"Resets in 2h 14m" - fmtReset in renderer.js.</summary>
    public static string Reset(DateTimeOffset? reset)
    {
        if (reset is null) return "Resets soon";
        long secs = (long)Math.Floor((reset.Value - DateTimeOffset.UtcNow).TotalSeconds);
        if (secs <= 0) return "Resets soon";
        if (secs < 3600) return $"Resets in {secs / 60}m";
        if (secs < 86400) return $"Resets in {secs / 3600}h {secs % 3600 / 60}m";
        return $"Resets in {secs / 86400}d {secs % 86400 / 3600}h";
    }

    /// <summary>"12s ago" / "3 min ago" / "2 h ago".</summary>
    public static string Ago(double seconds) => seconds switch
    {
        < 60 => $"{Math.Max(0, (int)Math.Round(seconds))}s ago",
        < 3600 => $"{(int)Math.Round(seconds / 60)} min ago",
        _ => $"{(int)Math.Round(seconds / 3600)} h ago"
    };

    /// <summary>Rounded to a whole number without the "-0" that Math.Round(-0.3) would print.</summary>
    public static string Whole(double value) => ((int)Math.Round(value)).ToString();
}
