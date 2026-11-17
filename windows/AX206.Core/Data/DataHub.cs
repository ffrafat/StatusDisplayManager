namespace AX206.Core.Data;

/// <summary>Owns every data provider; each polls on its own schedule.</summary>
public sealed class DataHub : IDisposable
{
    public GeoLocator Geo { get; } = new();
    public MediaProvider Media { get; } = new();
    public ClaudeUsageProvider Claude { get; } = new();
    public AntigravityProvider Antigravity { get; } = new();
    public BanglaGovProvider Bangla { get; } = new();
    public WeatherProvider Weather { get; }
    public RadarProvider Radar { get; }

    public event Action<string, LogLevel>? Log;

    public DataHub()
    {
        Weather = new WeatherProvider(Geo);
        Radar = new RadarProvider(Geo);
        Media.Log += Forward;
        Claude.Log += Forward;
        Antigravity.Log += Forward;
        Bangla.Log += Forward;
        Weather.Log += Forward;
        Radar.Log += Forward;
    }

    private void Forward(string message, LogLevel level) => Log?.Invoke(message, level);

    public void Start()
    {
        Media.Start();
        Claude.Start();
        Antigravity.Start();
        Bangla.Start();
        Weather.Start();
        Radar.Start();
    }

    public void Dispose()
    {
        Media.Dispose();
        Claude.Dispose();
        Antigravity.Dispose();
        Bangla.Dispose();
        Weather.Dispose();
        Radar.Dispose();
    }
}
