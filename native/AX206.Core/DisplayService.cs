using System.Collections.Concurrent;
using AX206.Core.Data;
using AX206.Core.Rendering;
using AX206.Core.Screens;
using AX206.Core.Telemetry;
using AX206.Core.Usb;

namespace AX206.Core;

public enum LogLevel { Info, Success, Warning, Error }

public sealed record RotationState(string Current, string Next, int SecondsLeft, bool Paused);

/// <summary>
/// The 1 FPS loop: samples stats, picks the screen (rotation over the screens that are enabled
/// and currently have something to show), renders, raises a preview frame and pushes it to
/// the AX206. Events fire on the loop's background thread.
/// </summary>
public sealed class DisplayService : IDisposable
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<IScreen> _screens;
    private readonly DataHub _data;
    private readonly FrameRenderer _renderer = new();
    private readonly SystemStatsSampler _sampler = new();
    private readonly object _deviceLock = new();
    private readonly object _rotationLock = new();
    private readonly ConcurrentDictionary<string, bool> _enabled = new();
    private readonly ConcurrentDictionary<string, TimeSpan> _durations = new();

    private Ax206Device? _device;
    private List<IScreen> _available = [];
    private HashSet<string> _wasAvailable = [];
    private string _currentId;
    private DateTime _screenStart = DateTime.Now;
    private bool _paused;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public DisplayService(IReadOnlyList<IScreen> screens, DataHub data)
    {
        if (screens.Count == 0) throw new ArgumentException("At least one screen is required.", nameof(screens));
        _screens = screens;
        _data = data;
        _currentId = screens[0].Id;
    }

    /// <summary>480x320 BGRA copy of each rendered frame.</summary>
    public event Action<byte[]>? FrameRendered;
    public event Action<bool>? ConnectionChanged;
    public event Action<string, LogLevel>? Log;
    public event Action<RotationState>? RotationChanged;

    public IReadOnlyList<IScreen> Screens => _screens;

    /// <summary>Why the last <see cref="Connect"/> failed, or null after a successful one.</summary>
    public string? LastConnectError { get; private set; }

    public bool IsConnected
    {
        get { lock (_deviceLock) return _device is not null; }
    }

    public void SetScreenEnabled(string id, bool enabled) => _enabled[id] = enabled;
    public bool IsScreenEnabled(string id) => _enabled.GetValueOrDefault(id, true);
    public void SetScreenDuration(string id, TimeSpan duration) => _durations[id] = duration;
    public TimeSpan GetScreenDuration(string id) => _durations.GetValueOrDefault(id, DefaultDuration);

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

    private ScreenContext Snapshot() => new()
    {
        Now = DateTime.Now,
        Stats = _sampler.Sample(),
        Media = _data.Media.Current.Info,
        Claude = _data.Claude.Current,
        Antigravity = _data.Antigravity.Current,
        Bangla = _data.Bangla.Current,
        Weather = _data.Weather.Current,
        Radar = _data.Radar.Current,
        RadarConfig = _data.Radar.Config
    };

    private void Tick()
    {
        var context = Snapshot();
        IScreen screen;
        RotationState state;
        lock (_rotationLock)
        {
            screen = Rotate(context);
            int index = _available.FindIndex(s => s.Id == screen.Id);
            var next = _available[(index + 1) % _available.Count];
            int left = (int)Math.Ceiling((GetScreenDuration(screen.Id) - (context.Now - _screenStart)).TotalSeconds);
            state = new RotationState(screen.Title, next.Title, Math.Max(0, left), _paused);
        }

        _renderer.Render(screen, context);
        FrameRendered?.Invoke(_renderer.Bgra.ToArray());
        RotationChanged?.Invoke(state);
        Push(_renderer.Rgb565);
    }

    // updateScreenList() + the rotation step from renderer.js. Caller holds _rotationLock.
    private IScreen Rotate(ScreenContext context)
    {
        var available = _screens.Where(s => IsScreenEnabled(s.Id) && s.IsAvailable(context)).ToList();
        if (available.Count == 0) available = [_screens[0]];

        var now = context.Now;
        var preempt = available.FirstOrDefault(s => s.PreemptsWhenAvailable && !_wasAvailable.Contains(s.Id));
        _wasAvailable = available.Select(s => s.Id).ToHashSet();
        _available = available;

        if (preempt is not null)
        {
            _currentId = preempt.Id;          // music just started: show it now
            _screenStart = now;
        }
        else if (available.All(s => s.Id != _currentId))
        {
            _currentId = available[0].Id;     // the current screen has nothing to show any more
            _screenStart = now;
        }
        else if (!_paused && now - _screenStart >= GetScreenDuration(_currentId))
        {
            int i = available.FindIndex(s => s.Id == _currentId);
            _currentId = available[(i + 1) % available.Count].Id;
            _screenStart = now;
        }
        return available.First(s => s.Id == _currentId);
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
                LastConnectError = null;
                Log?.Invoke($"AX206 display opened ({_device.DevicePath}).", LogLevel.Success);
                ConnectionChanged?.Invoke(true);
                return true;
            }
            catch (Exception e)
            {
                LastConnectError = e.Message;
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
            if (_available.Count == 0) return;
            int i = Math.Max(0, _available.FindIndex(s => s.Id == _currentId));
            _currentId = _available[(i + delta + _available.Count) % _available.Count].Id;
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
