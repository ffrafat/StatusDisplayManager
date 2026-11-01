using AX206.Core;
using AX206.Core.Data;
using AX206.Core.Screens;
using AX206.Core.Settings;

namespace AX206.Services;

/// <summary>
/// Everything that keeps running while the window is closed to the tray: settings, data
/// feeds, the display loop and the activity log.
/// </summary>
public sealed class AppServices : IDisposable
{
    public SettingsStore Settings { get; } = SettingsStore.Load();
    public DataHub Data { get; } = new();
    public DisplayService Display { get; }
    public LogBook Log { get; } = new();

    public AppServices()
    {
        Display = new DisplayService(ScreenCatalog.All(), Data);
        Display.Log += Log.Add;
        Data.Log += Log.Add;
        ApplyScreenSettings();
        Data.Radar.SetConfig(RadarConfig());
        Data.Claude.SetManualToken(SecretStore.LoadClaudeToken());
    }

    public void Start()
    {
        Log.Add("AX206 Display Manager started.", LogLevel.Info);
        Data.Start();
        Display.Start();
        if (Settings.Current.AutoConnect) _ = Task.Run(Display.Connect);
    }

    public void ApplyScreenSettings()
    {
        foreach (var screen in Display.Screens)
        {
            var s = Settings.Current.Screen(screen.Id);
            Display.SetScreenEnabled(screen.Id, s.Enabled);
            Display.SetScreenDuration(screen.Id, TimeSpan.FromSeconds(Math.Clamp(s.DurationSeconds, 2, 600)));
        }
    }

    /// <summary>The radar only polls its feeds while its screen is enabled.</summary>
    public RadarConfig RadarConfig()
    {
        var r = Settings.Current.Radar;
        return new RadarConfig(Settings.Current.Screen("radar").Enabled, r.Lat, r.Lon, r.RangeKm, r.Source, r.Airport);
    }

    public void SetScreenEnabled(string id, bool enabled)
    {
        Settings.Current.Screen(id).Enabled = enabled;
        Display.SetScreenEnabled(id, enabled);
        if (id == "radar") Data.Radar.SetConfig(RadarConfig());
        Settings.Save();
    }

    public void SetScreenDuration(string id, int seconds)
    {
        seconds = Math.Clamp(seconds, 2, 600);
        Settings.Current.Screen(id).DurationSeconds = seconds;
        Display.SetScreenDuration(id, TimeSpan.FromSeconds(seconds));
        Settings.Save();
    }

    public void UpdateRadar(Action<RadarSettings> change)
    {
        change(Settings.Current.Radar);
        Settings.Save();
        Data.Radar.SetConfig(RadarConfig());
    }

    public void SetClaudeToken(string? token)
    {
        SecretStore.SaveClaudeToken(token);
        Data.Claude.SetManualToken(token);
        Log.Add(string.IsNullOrWhiteSpace(token) ? "Claude token removed." : "Claude token saved.", LogLevel.Info);
    }

    public bool HasSavedClaudeToken => SecretStore.LoadClaudeToken() is not null;

    public void Dispose()
    {
        Display.Dispose(); // releases the USB interface
        Data.Dispose();
    }
}
