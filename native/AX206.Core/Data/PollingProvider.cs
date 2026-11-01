namespace AX206.Core.Data;

/// <summary>
/// Runs <see cref="PollAsync"/> on its own loop so a slow feed never delays the others or
/// the 1 FPS render. Subclasses publish an immutable snapshot through <see cref="Current"/>.
/// </summary>
public abstract class PollingProvider<T> : IDisposable where T : class
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0);
    private Task? _loop;
    private volatile T _current;

    protected PollingProvider(T initial) => _current = initial;

    public T Current
    {
        get => _current;
        protected set => _current = value;
    }

    public event Action<string, LogLevel>? Log;

    /// <summary>Delay before the next poll (can depend on the last result, e.g. rate limits).</summary>
    protected abstract TimeSpan Interval { get; }

    protected abstract Task PollAsync(CancellationToken ct);

    protected void Report(string message, LogLevel level) => Log?.Invoke(message, level);

    public void Start() => _loop ??= Task.Run(RunAsync);

    /// <summary>Skip the rest of the current wait and poll now (e.g. after a settings change).</summary>
    public void PollNow() => _wake.Release();

    private async Task RunAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try { await PollAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e) { Report($"{GetType().Name}: {e.Message}", LogLevel.Warning); }

            try { await _wake.WaitAsync(Interval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _cts.Dispose();
        _wake.Dispose();
    }
}
