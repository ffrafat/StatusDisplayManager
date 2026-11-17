namespace AX206.Services;

/// <summary>
/// One running copy per user session. A second launch signals the first to show its window
/// and then exits, so Start-menu clicks while running in the tray bring the window back.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showSignal;
    private volatile bool _disposed;

    public bool IsFirst { get; }

    /// <summary>Raised on a background thread when another launch asks to be shown.</summary>
    public event Action? ShowRequested;

    public SingleInstance(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out bool createdNew);
        IsFirst = createdNew;
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Show");
        if (!IsFirst) return;

        new Thread(() =>
        {
            while (_showSignal.WaitOne() && !_disposed)
                ShowRequested?.Invoke();
        }) { IsBackground = true, Name = "SingleInstance" }.Start();
    }

    public void SignalFirstInstance() => _showSignal.Set();

    public void Dispose()
    {
        _disposed = true;
        _showSignal.Set();
        _showSignal.Dispose();
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
