using AX206.Core.Rendering;
using AX206.Core.Screens;
using AX206.Core.Telemetry;
using AX206.Core.Usb;

namespace AX206.Core;

public enum LogLevel { Info, Success, Warning, Error }

public sealed record RotationState(string Current, string Next, int SecondsLeft, bool Paused);

/// <summary>
/// The 1 FPS loop: samples stats, rotates screens, renders, raises a preview frame and pushes
/// it to the AX206. Events fire on the loop's background thread.
/// </summary>
public sealed class DisplayService : IDisposable
{
    private readonly IReadOnlyList<IScreen> _screens;
    private readonly FrameRenderer _renderer = new();
    private readonly SystemStatsSampler _sampler = new();
    private readonly object _deviceLock = new();
    private readonly object _rotationLock = new();

    private Ax206Device? _device;
    private int _index;
    private DateTime _screenStart = DateTime.Now;
    private bool _paused;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public DisplayService(IReadOnlyList<IScreen> screens)
    {
        if (screens.Count == 0) throw new ArgumentException("At least one screen is required.", nameof(screens));
        _screens = screens;
    }

    /// <summary>480x320 BGRA copy of each rendered frame.</summary>
    public event Action<byte[]>? FrameRendered;
    public event Action<bool>? ConnectionChanged;
    public event Action<string, LogLevel>? Log;
    public event Action<RotationState>? RotationChanged;

    public TimeSpan ScreenDuration { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsConnected
    {
        get { lock (_deviceLock) return _device is not null; }
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                try { Tick(); }
                catch (Exception e) { Log?.Invoke($"Render error: {e.Message}", LogLevel.Error); }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
    }

    private void Tick()
    {
        var stats = _sampler.Sample();
        IScreen screen;
        RotationState state;
        lock (_rotationLock)
        {
            var now = DateTime.Now;
            if (!_paused && now - _screenStart >= ScreenDuration)
            {
                _index = (_index + 1) % _screens.Count;
                _screenStart = now;
            }
            screen = _screens[_index];
            int left = (int)Math.Ceiling((ScreenDuration - (now - _screenStart)).TotalSeconds);
            state = new RotationState(screen.Title, _screens[(_index + 1) % _screens.Count].Title, Math.Max(0, left), _paused);
        }

        _renderer.Render(screen, new ScreenContext { Now = DateTime.Now, Stats = stats });
        FrameRendered?.Invoke(_renderer.Bgra.ToArray());
        RotationChanged?.Invoke(state);
        Push(_renderer.Rgb565);
    }

    private void Push(byte[] frame)
    {
        lock (_deviceLock)
        {
            if (_device is null) return;
            try
            {
                _device.Blit(frame);
            }
            catch (Exception e)
            {
                Log?.Invoke($"USB blit error: {e.Message}. Attempting recovery...", LogLevel.Warning);
                try
                {
                    _device.Recover();
                    _device.Blit(frame);
                    Log?.Invoke("Recovered.", LogLevel.Success);
                }
                catch (Exception e2)
                {
                    Log?.Invoke($"Recovery failed: {e2.Message}", LogLevel.Error);
                    CloseDeviceLocked();
                }
            }
        }
    }

    public bool Connect()
    {
        lock (_deviceLock)
        {
            if (_device is not null) return true;
            try
            {
                _device = Ax206Device.Open();
                Log?.Invoke($"AX206 display opened ({_device.DevicePath}).", LogLevel.Success);
                ConnectionChanged?.Invoke(true);
                return true;
            }
            catch (Exception e)
            {
                Log?.Invoke($"Failed to open display: {e.Message}", LogLevel.Error);
                ConnectionChanged?.Invoke(false);
                return false;
            }
        }
    }

    public void Disconnect()
    {
        lock (_deviceLock)
        {
            if (_device is null) return;
            CloseDeviceLocked();
            Log?.Invoke("AX206 display closed.", LogLevel.Info);
        }
    }

    private void CloseDeviceLocked()
    {
        _device?.Dispose();
        _device = null;
        ConnectionChanged?.Invoke(false);
    }

    public void Next() => Step(+1);
    public void Previous() => Step(-1);

    private void Step(int delta)
    {
        lock (_rotationLock)
        {
            _index = (_index + delta + _screens.Count) % _screens.Count;
            _screenStart = DateTime.Now;
        }
    }

    public bool TogglePause()
    {
        lock (_rotationLock)
        {
            _paused = !_paused;
            _screenStart = DateTime.Now;
            return _paused;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
        Disconnect();
        _renderer.Dispose();
        _cts?.Dispose();
    }
}
