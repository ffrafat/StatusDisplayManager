using AX206.Core;

namespace AX206.Services;

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string Tag => Level switch
    {
        LogLevel.Success => "OK",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO"
    };

    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>Recent activity, kept while the window is closed so the Activity page shows history.</summary>
public sealed class LogBook
{
    private const int Capacity = 300;
    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = [];

    /// <summary>Raised on the thread that logged; UI subscribers must marshal.</summary>
    public event Action<LogEntry>? Added;

    public void Add(string message, LogLevel level)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > Capacity) _entries.RemoveAt(0);
        }
        Added?.Invoke(entry);
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock) return _entries.ToList();
    }
}
